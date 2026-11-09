using System.Globalization;
using System.Windows;
using MunzQuickTweaks.Core.Safety;
using MunzQuickTweaks.Core.Tweaks;

namespace MunzQuickTweaks.App.Services;

public sealed record BatchResult(int Applied, int Failed, bool Cancelled);

/// <summary>
/// Wendet mehrere Tweaks nacheinander an (Profile, Import, erneut anwenden).
/// Eine Rückfrage zeigt vorher die Liste, Tweaks mit Risiko mittel sind markiert.
/// </summary>
public sealed class BatchApplyService
{
    private readonly TweakService _tweaks;
    private readonly LocalizationService _localization;
    private readonly HistoryStore _history;

    public BatchApplyService(TweakService tweaks, LocalizationService localization, HistoryStore history)
    {
        _tweaks = tweaks;
        _localization = localization;
        _history = history;
    }

    /// <summary>Nur Tweaks, die verfügbar und noch nicht aktiv sind.</summary>
    public async Task<IReadOnlyList<TweakDefinition>> FilterTodoAsync(IEnumerable<string> ids) =>
        await Task.Run(() => ids
            .Distinct()
            .Select(_tweaks.Find)
            .OfType<TweakDefinition>()
            .Where(t => _tweaks.GetStatus(t) is TweakStatus.NotApplied or TweakStatus.Partial)
            .ToList());

    /// <param name="heading">Erste Zeile der Rückfrage, z. B. "Profil Gaming: diese 11 Tweaks anwenden?".</param>
    /// <param name="progress">Fortschritt als Text für die Oberfläche.</param>
    /// <param name="historyNote">Eintrag im Änderungsprotokoll, z. B. "Gaming".</param>
    public async Task<BatchResult> ConfirmAndApplyAsync(IReadOnlyList<TweakDefinition> todo, string heading, Action<string> progress, string historyNote)
    {
        if (todo.Count == 0)
        {
            return new BatchResult(0, 0, false);
        }

        var lang = _localization.CurrentLanguage;
        var medium = _localization.Get("Profile.MediumMark");
        var list = string.Join("\n", todo.Select(t => "• " + t.Title.Get(lang) + (t.Risk == TweakRisk.Medium ? $" ({medium})" : string.Empty)));
        var question = heading + "\n\n" + list;
        if (todo.Any(t => t.Risk == TweakRisk.Medium))
        {
            question += "\n\n" + _localization.Get("Profile.MediumHint");
        }

        if (MessageBox.Show(question, _localization.Get("App.Name"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return new BatchResult(0, 0, true);
        }

        int ok = 0, failed = 0;
        for (var i = 0; i < todo.Count; i++)
        {
            var tweak = todo[i];
            var step = $"{tweak.Title.Get(lang)} ({i + 1}/{todo.Count})";
            progress(step);
            var prompt = new DialogSafetyPrompt(_localization, stage => progress($"{step}: {stage}"));
            var result = await _tweaks.ApplyAsync(tweak, prompt);
            if (result.Success)
            {
                ok++;
                continue;
            }

            failed++;
            if (result.Error == "Abgebrochen.")
            {
                break;
            }
        }

        _history.Add(HistoryAction.ProfileApplied, null, failed == 0, string.Create(CultureInfo.InvariantCulture, $"{historyNote}: {ok} ok, {failed} Fehler"));
        return new BatchResult(ok, failed, false);
    }
}
