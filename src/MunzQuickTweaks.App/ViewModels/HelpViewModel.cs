using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core;
using MunzQuickTweaks.Windows;

namespace MunzQuickTweaks.App.ViewModels;

/// <summary>Hilfe und Support von Munz IT Service: Fernwartung, Kontakt, Support-Paket.</summary>
public sealed partial class HelpViewModel : PageViewModel
{
    public const string SupportMail = "support@munz-its.de";
    public const string Website = "https://munz-its.de";
    public const string Discord = "https://discord.gg/zmg2eqyM7M";

    private readonly LocalizationService _localization;
    private readonly SystemInfoProvider _systemInfo;
    private readonly TweakService _tweaks;
    private readonly ILogger<HelpViewModel> _log;

    public HelpViewModel(LocalizationService localization, SystemInfoProvider systemInfo, TweakService tweaks, ILogger<HelpViewModel> log)
        : base("Nav.Help")
    {
        _localization = localization;
        _systemInfo = systemInfo;
        _tweaks = tweaks;
        _log = log;
    }

    public string MailAddress => SupportMail;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSupportPackageCommand), nameof(SendRequestCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>Windows-Remotehilfe (Quick Assist). Neuere Windows-Versionen nutzen die App, ältere die klassische .exe.</summary>
    [RelayCommand]
    private void OpenRemoteHelp()
    {
        if (!Open("ms-quick-assist:"))
        {
            Open(Path.Combine(Environment.SystemDirectory, "quickassist.exe"));
        }
    }

    [RelayCommand]
    private void MailSupport()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "?";
        var subject = Uri.EscapeDataString($"Munz Quick Tweaks {version}: Support-Anfrage");
        Open($"mailto:{SupportMail}?subject={subject}");
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendRequestCommand))]
    private string _problem = string.Empty;

    [ObservableProperty]
    private string _requestStatus = string.Empty;

    private bool CanSend() => !IsBusy && Problem.Trim().Length >= 10;

    /// <summary>
    /// Support-Anfrage: Support-Paket auf den Desktop, E-Mail mit Beschreibung und Systemdaten im Mailprogramm öffnen.
    /// Das Paket hängt der Nutzer selbst an, mailto kann keine Anhänge.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendRequestAsync()
    {
        IsBusy = true;
        RequestStatus = _localization.Get("Help.Saving");
        try
        {
            var (file, system) = await Task.Run(() => (BuildPackage(), DescribeSystem()));
            var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "?";
            var subject = $"Munz Quick Tweaks {version}: Support-Anfrage";

            // Systemdaten ohne Tweak-Liste, damit der Link nicht zu lang für das Mailprogramm wird
            var shortSystem = string.Join("\r\n", system.Split('\n').TakeWhile(l => !l.StartsWith("Aktive Tweaks", StringComparison.Ordinal)).Select(l => l.TrimEnd('\r')));
            var body = $"{Problem.Trim()}\r\n\r\n{_localization.Get("Help.Request.AttachHint")} {Path.GetFileName(file)}\r\n\r\n---\r\n{shortSystem}";
            if (body.Length > 1800)
            {
                body = body[..1800];
            }

            Open($"mailto:{SupportMail}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}");
            Open("explorer.exe", $"/select,\"{file}\"");
            RequestStatus = string.Format(System.Globalization.CultureInfo.CurrentCulture, _localization.Get("Help.Request.Opened"), Path.GetFileName(file));
            _log.LogInformation("Support-Anfrage vorbereitet: {File}", file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _log.LogError(ex, "Support-Anfrage nicht vorbereitet");
            RequestStatus = $"{_localization.Get("Tweak.Failed")} {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenWebsite() => Open(Website);

    [RelayCommand]
    private void OpenDiscord() => Open(Discord);

    private bool CanSave() => !IsBusy;

    /// <summary>
    /// Zip auf dem Desktop mit Protokollen, Journal, Änderungsprotokoll und Systemdaten.
    /// Keine persönlichen Dateien, nur was der Support zur Fehlersuche braucht.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveSupportPackageAsync()
    {
        IsBusy = true;
        Status = _localization.Get("Help.Saving");
        try
        {
            var file = await Task.Run(BuildPackage);
            Status = string.Format(System.Globalization.CultureInfo.CurrentCulture, _localization.Get("Help.Saved"), Path.GetFileName(file));
            Open("explorer.exe", $"/select,\"{file}\"");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _log.LogError(ex, "Support-Paket nicht erstellt");
            Status = $"{_localization.Get("Tweak.Failed")} {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string BuildPackage()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var target = Path.Combine(desktop, $"MunzQuickTweaks-Support-{DateTime.Now:yyyyMMdd-HHmm}.zip");
        if (File.Exists(target))
        {
            File.Delete(target);
        }

        using var zip = ZipFile.Open(target, ZipArchiveMode.Create);

        // Protokolle der letzten Tage
        if (Directory.Exists(AppPaths.LogDirectory))
        {
            foreach (var log in Directory.GetFiles(AppPaths.LogDirectory, "*.log").OrderByDescending(f => f).Take(7))
            {
                AddShared(zip, log, "logs/" + Path.GetFileName(log));
            }
        }

        foreach (var name in new[] { "journal.json", "history.jsonl" })
        {
            var path = Path.Combine(AppPaths.MachineData, "state", name);
            if (File.Exists(path))
            {
                AddShared(zip, path, "state/" + name);
            }
        }

        if (File.Exists(AppPaths.SettingsFile))
        {
            AddShared(zip, AppPaths.SettingsFile, "settings.json");
        }

        var entry = zip.CreateEntry("system.txt");
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(DescribeSystem());
        return target;
    }

    // Protokolldatei ist evtl. gerade geöffnet: mit FileShare lesen statt CreateEntryFromFile
    private static void AddShared(ZipArchive zip, string path, string entryName)
    {
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var target = zip.CreateEntry(entryName, CompressionLevel.Optimal).Open();
        source.CopyTo(target);
    }

    private string DescribeSystem()
    {
        var sb = new StringBuilder();
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "?";
        sb.AppendLine($"Munz Quick Tweaks {version}");
        sb.AppendLine($"Erstellt: {DateTime.Now:dd.MM.yyyy HH:mm}");
        try
        {
            var info = _systemInfo.Read();
            sb.AppendLine($"Windows: {info.OperatingSystem}");
            sb.AppendLine($"Prozessor: {info.Processor}");
            sb.AppendLine($"Grafik: {string.Join(", ", info.GraphicsCards)}");
            sb.AppendLine($"Arbeitsspeicher: {info.MemoryGb} GB");
            sb.AppendLine($"Administrator: {info.IsAdministrator}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Systemdaten nicht lesbar: {ex.Message}");
        }

        sb.AppendLine($"Fremdes Admin-Konto: {_tweaks.IsForeignAccount}");
        sb.AppendLine();
        sb.AppendLine("Aktive Tweaks laut Journal:");
        foreach (var entry in _tweaks.Engine.Journal.All.OrderBy(e => e.TweakId))
        {
            var status = _tweaks.Find(entry.TweakId) is { } t ? _tweaks.GetStatus(t).ToString() : "nicht im Katalog";
            sb.AppendLine($"  {entry.TweakId} ({status}, seit {entry.AppliedAt.ToLocalTime():dd.MM.yyyy HH:mm})");
        }

        return sb.ToString();
    }

    private bool Open(string target, string? arguments = null)
    {
        try
        {
            var info = new ProcessStartInfo(target) { UseShellExecute = true };
            if (arguments is not null)
            {
                info.Arguments = arguments;
            }

            Process.Start(info)?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _log.LogWarning(ex, "{Target} konnte nicht geöffnet werden", target);
            return false;
        }
    }
}
