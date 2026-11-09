using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.Core.Tools;

namespace MunzQuickTweaks.App.Services;

/// <summary>Fragt munz-its.de nach einer neuen Version. Lädt nichts herunter, der Nutzer entscheidet selbst.</summary>
public sealed class UpdateService
{
    private static readonly HttpClient Http = CreateClient();
    private readonly ILogger<UpdateService> _log;

    public UpdateService(ILogger<UpdateService> log)
    {
        _log = log;
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        CurrentVersion = version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public string CurrentVersion { get; }

    /// <summary>Gesetzt, sobald eine neuere Version gefunden wurde.</summary>
    public UpdateInfo? Available { get; private set; }

    public event EventHandler? AvailableChanged;

    /// <returns>Gefundene neuere Version oder null. Fehler stehen in error.</returns>
    public async Task<(UpdateInfo? Newer, string? Error)> CheckAsync()
    {
        try
        {
            var json = await Http.GetStringAsync(UpdateCheck.VersionUrl);
            var info = UpdateCheck.Parse(json);
            var newer = UpdateCheck.IsNewer(info, CurrentVersion) ? info : null;
            _log.LogInformation("Update-Prüfung: angeboten {Offered}, laufend {Current}", info.Version, CurrentVersion);
            Available = newer;
            AvailableChanged?.Invoke(this, EventArgs.Empty);
            return (newer, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException or System.Text.Json.JsonException)
        {
            _log.LogWarning("Update-Prüfung fehlgeschlagen: {Error}", ex.Message);
            return (null, ex is TaskCanceledException ? "Zeitüberschreitung" : ex.Message);
        }
    }

    public void Dismiss()
    {
        Available = null;
        AvailableChanged?.Invoke(this, EventArgs.Empty);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MunzQuickTweaks");
        return client;
    }
}
