using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Safety;
using MunzQuickTweaks.Core.Tools;

namespace MunzQuickTweaks.App.ViewModels;

public sealed record PingRow(string Name, string Host, string Result, bool IsGood, bool IsBad);

/// <summary>DNS wechseln, DNS-Cache leeren, Netzwerk zurücksetzen und Ping zu Spieleservern.</summary>
public sealed partial class NetworkViewModel : PageViewModel
{
    private readonly ChangeLedger _ledger;
    private readonly HistoryStore _history;
    private readonly Windows.WindowsCleanup _cleanup;
    private readonly LocalizationService _localization;
    private readonly ILogger<NetworkViewModel> _log;
    private IReadOnlyList<NetAdapterDns> _adapters = Array.Empty<NetAdapterDns>();

    public NetworkViewModel(ChangeLedger ledger, HistoryStore history, Windows.WindowsCleanup cleanup, LocalizationService localization, ILogger<NetworkViewModel> log)
        : base("Nav.Network")
    {
        _ledger = ledger;
        _history = history;
        _cleanup = cleanup;
        _localization = localization;
        _log = log;
    }

    public ObservableCollection<PingRow> Pings { get; } = new();

    [ObservableProperty]
    private string _adapterText = string.Empty;

    [ObservableProperty]
    private string _currentDns = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetDnsCommand), nameof(PingCommand), nameof(ResetNetworkCommand), nameof(FlushDnsCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _statusIsError;

    public override Task OnNavigatedToAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            _adapters = await Task.Run(Windows.WindowsNetwork.ListAdapters);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
        {
            _log.LogWarning(ex, "Netzwerkadapter nicht lesbar");
            _adapters = Array.Empty<NetAdapterDns>();
        }

        AdapterText = _adapters.Count == 0
            ? _localization.Get("Net.NoAdapter")
            : string.Join(", ", _adapters.Select(a => a.Name));
        var ids = _adapters.Select(a => NetworkPresets.Detect(a.Servers)).Distinct().ToList();
        CurrentDns = ids.Count == 1 ? DnsName(ids[0]) : _localization.Get("Net.Mixed");
    }

    private string DnsName(string? id) => id switch
    {
        "auto" => _localization.Get("Net.Auto"),
        null => _localization.Get("Net.Custom"),
        _ => NetworkPresets.Providers.First(p => p.Id == id).Name,
    };

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SetDnsAsync(string providerId)
    {
        if (_adapters.Count == 0)
        {
            Show(true, _localization.Get("Net.NoAdapter"));
            return;
        }

        var servers = providerId == "auto" ? Array.Empty<string>() : NetworkPresets.Providers.First(p => p.Id == providerId).Servers;
        IsBusy = true;
        try
        {
            var errors = new List<string>();
            foreach (var adapter in _adapters)
            {
                _ledger.RecordDns(adapter.Guid, adapter.Servers);
                var result = await Task.Run(() => Windows.WindowsNetwork.SetDns(adapter, servers));
                _history.Add(HistoryAction.AppAction, $"dns.{providerId}", result.Success, result.Success ? adapter.Name : $"{adapter.Name}: {result.Error}");
                if (!result.Success)
                {
                    errors.Add($"{adapter.Name}: {result.Error}");
                }
            }

            _cleanup.FlushDns();
            _log.LogInformation("DNS {Provider}, Fehler {Count}", providerId, errors.Count);
            Show(errors.Count > 0, errors.Count == 0
                ? string.Format(CultureInfo.CurrentCulture, _localization.Get("Net.DnsSet"), DnsName(providerId))
                : $"{_localization.Get("Tweak.Failed")} {string.Join(" · ", errors)}");
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void FlushDns()
    {
        var ok = _cleanup.FlushDns();
        Show(!ok, _localization.Get(ok ? "Net.Flushed" : "Tweak.Failed"));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ResetNetworkAsync()
    {
        if (MessageBox.Show(_localization.Get("Net.ConfirmReset"), _localization.Get("App.Name"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await Task.Run(Windows.WindowsNetwork.ResetStack);
            _history.Add(HistoryAction.AppAction, "network.reset", result.Success, result.Error);
            Show(!result.Success, result.Success ? _localization.Get("Net.ResetDone") : $"{_localization.Get("Tweak.Failed")} {result.Error}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task PingAsync()
    {
        IsBusy = true;
        Pings.Clear();
        Show(false, _localization.Get("Net.Pinging"));
        try
        {
            var results = await Task.WhenAll(NetworkPresets.PingTargets.Select(t => Windows.WindowsNetwork.PingAsync(t)));
            foreach (var r in results)
            {
                var text = r.AverageMs is { } ms
                    ? $"{ms.ToString("0", CultureInfo.CurrentCulture)} ms" + (r.Lost > 0 ? $" · {r.Lost}/{r.Sent} {_localization.Get("Net.Lost")}" : string.Empty)
                    : _localization.Get("Net.NoReply");
                Pings.Add(new PingRow(r.Name, r.Host, text, r.AverageMs is < 40 && r.Lost == 0, r.AverageMs is null or > 100 || r.Lost > 0));
            }

            Status = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Show(bool isError, string text)
    {
        StatusIsError = isError;
        Status = text;
    }
}
