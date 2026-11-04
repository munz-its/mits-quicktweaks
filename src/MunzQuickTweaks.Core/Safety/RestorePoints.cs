namespace MunzQuickTweaks.Core.Safety;

public enum ProtectionState
{
    Enabled,
    Disabled,
    /// <summary>Per Gruppenrichtlinie gesperrt, kann die App nicht ändern.</summary>
    BlockedByPolicy,
    Unknown,
}

public sealed record RestorePointInfo(int Sequence, string Description, DateTime Created);

public sealed record OperationResult(bool Success, string? Error = null)
{
    public static OperationResult Ok { get; } = new(true);

    public static OperationResult Fail(string error) => new(false, error);
}

/// <summary>Windows-Wiederherstellungspunkte (Ebene 1 der Sicherung).</summary>
public interface IRestorePointService
{
    ProtectionState GetProtectionState();

    OperationResult EnableProtection();

    /// <summary>Kann 10 bis 60 Sekunden dauern.</summary>
    OperationResult Create(string description);

    IReadOnlyList<RestorePointInfo> List();
}

/// <summary>Rückfragen an den Nutzer während der Sicherung.</summary>
public interface ISafetyPrompt
{
    /// <summary>Systemschutz ist aus. Null = Abbrechen, true = aktivieren, false = ohne Punkt weiter.</summary>
    bool? AskEnableProtection();

    /// <summary>Punkt nicht möglich. true = trotzdem fortfahren.</summary>
    bool AskContinueWithoutRestorePoint(string reason);

    void ReportProgress(string stage);
}

public enum GateResult
{
    Proceed,
    Cancelled,
}

/// <summary>
/// Sorgt dafür, dass vor dem ersten Tweak einer Sitzung ein Wiederherstellungspunkt entsteht.
/// Jede Entscheidung des Nutzers gilt bis zum Schließen der App, damit er nicht bei jedem Tweak gefragt wird.
/// </summary>
public sealed class RestorePointGate
{
    public const string Description = "Munz Quick Tweaks: vor Änderungen";

    private readonly IRestorePointService _service;
    private readonly HistoryStore _history;
    private readonly object _lock = new();
    private bool _settledThisSession;

    public RestorePointGate(IRestorePointService service, HistoryStore history)
    {
        _service = service;
        _history = history;
    }

    public bool SettledThisSession
    {
        get { lock (_lock) return _settledThisSession; }
    }

    public GateResult Ensure(ISafetyPrompt prompt)
    {
        lock (_lock)
        {
            if (_settledThisSession)
            {
                return GateResult.Proceed;
            }

            GateResult result;
            try
            {
                result = Run(prompt);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Unerwarteter Fehler beim Wiederherstellungspunkt: Nutzer entscheidet, Journal sichert trotzdem
                _history.Add(HistoryAction.RestorePointCreated, null, false, $"{ex.GetType().Name}: {ex.Message}");
                result = prompt.AskContinueWithoutRestorePoint($"{ex.GetType().Name} (0x{ex.HResult:X8})")
                    ? GateResult.Proceed
                    : GateResult.Cancelled;
            }
            if (result == GateResult.Proceed)
            {
                _settledThisSession = true;
            }

            return result;
        }
    }

    private GateResult Run(ISafetyPrompt prompt)
    {
        switch (_service.GetProtectionState())
        {
            case ProtectionState.BlockedByPolicy:
                return prompt.AskContinueWithoutRestorePoint("policy") ? GateResult.Proceed : GateResult.Cancelled;

            case ProtectionState.Disabled:
                var choice = prompt.AskEnableProtection();
                if (choice is null)
                {
                    return GateResult.Cancelled;
                }

                if (choice == false)
                {
                    _history.Add(HistoryAction.RestorePointSkipped, null, true, "Systemschutz aus, Nutzer fährt ohne Punkt fort");
                    return GateResult.Proceed;
                }

                prompt.ReportProgress("enable");
                var enabled = _service.EnableProtection();
                _history.Add(HistoryAction.ProtectionEnabled, null, enabled.Success, enabled.Error);
                if (!enabled.Success)
                {
                    return prompt.AskContinueWithoutRestorePoint(enabled.Error ?? "?") ? GateResult.Proceed : GateResult.Cancelled;
                }

                break;
        }

        prompt.ReportProgress("create");
        var created = _service.Create(Description);
        _history.Add(HistoryAction.RestorePointCreated, null, created.Success, created.Error);
        if (created.Success)
        {
            return GateResult.Proceed;
        }

        return prompt.AskContinueWithoutRestorePoint(created.Error ?? "?") ? GateResult.Proceed : GateResult.Cancelled;
    }
}
