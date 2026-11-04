using System.Text;

namespace MunzQuickTweaks.Core.Tweaks;

public enum SelfTestOutcome
{
    Ok,
    Failed,
    NotAvailable,
    Skipped,
}

public sealed record SelfTestResult(
    string TweakId,
    SelfTestOutcome Outcome,
    TweakStatus Before,
    TweakStatus AfterApply,
    TweakStatus AfterRevert,
    bool StateRestored,
    string Note);

/// <summary>
/// Testet jeden Tweak technisch: Anwenden, Zielzustand prüfen, Zurücksetzen,
/// Ausgangszustand Wert für Wert vergleichen. Läuft mit eigenem Journal,
/// Tweaks, die der Nutzer aktiv hat, werden übersprungen und bleiben unberührt.
/// </summary>
public sealed class SelfTestRunner
{
    private readonly TweakEngine _engine;
    private readonly JournalStore _userJournal;

    /// <param name="engine">Engine mit eigenem, leerem Test-Journal.</param>
    /// <param name="userJournal">Journal des Nutzers, nur zum Überspringen aktiver Tweaks.</param>
    public SelfTestRunner(TweakEngine engine, JournalStore userJournal)
    {
        _engine = engine;
        _userJournal = userJournal;
    }

    public IReadOnlyList<SelfTestResult> Run(IReadOnlyList<TweakDefinition> tweaks, IProgress<(int Done, int Total, string Id)>? progress = null)
    {
        var results = new List<SelfTestResult>();
        for (var i = 0; i < tweaks.Count; i++)
        {
            var tweak = tweaks[i];
            progress?.Report((i, tweaks.Count, tweak.Id));
            results.Add(RunOne(tweak));
        }

        progress?.Report((tweaks.Count, tweaks.Count, string.Empty));
        return results;
    }

    private SelfTestResult RunOne(TweakDefinition tweak)
    {
        if (_userJournal.Get(tweak.Id) is not null)
        {
            return new SelfTestResult(tweak.Id, SelfTestOutcome.Skipped, TweakStatus.Applied, TweakStatus.Applied, TweakStatus.Applied, true,
                "Vom Nutzer aktiv, nicht angefasst");
        }

        var before = _engine.GetStatus(tweak);
        if (before == TweakStatus.NotAvailable)
        {
            return new SelfTestResult(tweak.Id, SelfTestOutcome.NotAvailable, before, before, before, true, _engine.UnavailableReason(tweak));
        }

        var stateBefore = _engine.CaptureState(tweak);
        var notes = new List<string>();

        var apply = _engine.Apply(tweak);
        var afterApply = _engine.GetStatus(tweak);
        if (!apply.Success)
        {
            notes.Add($"Anwenden: {apply.Error}");
        }
        else if (afterApply != TweakStatus.Applied)
        {
            notes.Add($"Nach Anwenden Status {afterApply} statt Applied");
        }

        var revert = _engine.Revert(tweak);
        var afterRevert = _engine.GetStatus(tweak);
        if (!revert.Success)
        {
            notes.Add($"Zurücksetzen: {revert.Error}");
        }

        var stateAfter = _engine.CaptureState(tweak);
        var restored = stateBefore.SequenceEqual(stateAfter);
        if (!restored)
        {
            var diff = stateBefore.Zip(stateAfter).Where(p => p.First != p.Second).Select(p => $"{p.First} -> {p.Second.Split(" = ").Last()}");
            notes.Add("Nicht exakt wiederhergestellt: " + string.Join("; ", diff));
        }

        var ok = apply.Success && afterApply == TweakStatus.Applied && revert.Success && restored;
        return new SelfTestResult(tweak.Id, ok ? SelfTestOutcome.Ok : SelfTestOutcome.Failed, before, afterApply, afterRevert, restored,
            notes.Count == 0 ? string.Empty : string.Join(" | ", notes));
    }

    /// <summary>Lesbarer Bericht als Text, zum Kopieren in den Chat.</summary>
    public static string FormatReport(IReadOnlyList<SelfTestResult> results, string systemInfo, string appVersion, DateTime now)
    {
        var sb = new StringBuilder();
        sb.AppendLine("MUNZ QUICK TWEAKS · SELBSTTEST");
        sb.AppendLine($"Version {appVersion} · {now:dd.MM.yyyy HH:mm}");
        sb.AppendLine(systemInfo);
        sb.AppendLine();
        sb.AppendLine($"OK: {results.Count(r => r.Outcome == SelfTestOutcome.Ok)} · " +
                      $"Fehler: {results.Count(r => r.Outcome == SelfTestOutcome.Failed)} · " +
                      $"Nicht verfügbar: {results.Count(r => r.Outcome == SelfTestOutcome.NotAvailable)} · " +
                      $"Übersprungen: {results.Count(r => r.Outcome == SelfTestOutcome.Skipped)} · " +
                      $"Gesamt: {results.Count}");
        sb.AppendLine();

        foreach (var group in new[] { SelfTestOutcome.Failed, SelfTestOutcome.Ok, SelfTestOutcome.NotAvailable, SelfTestOutcome.Skipped })
        {
            var items = results.Where(r => r.Outcome == group).ToList();
            if (items.Count == 0)
            {
                continue;
            }

            sb.AppendLine(group switch
            {
                SelfTestOutcome.Failed => "== FEHLER ==",
                SelfTestOutcome.Ok => "== OK ==",
                SelfTestOutcome.NotAvailable => "== NICHT VERFÜGBAR ==",
                _ => "== ÜBERSPRUNGEN ==",
            });

            foreach (var r in items)
            {
                var line = $"{r.TweakId,-30} vorher {r.Before,-10} angewendet {r.AfterApply,-10} zurück {r.AfterRevert,-10}";
                sb.AppendLine(string.IsNullOrEmpty(r.Note) ? line.TrimEnd() : $"{line} {r.Note}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
