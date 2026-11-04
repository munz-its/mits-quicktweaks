using System.Windows;
using MunzQuickTweaks.Core.Safety;

namespace MunzQuickTweaks.App.Services;

/// <summary>Zeigt die Rückfragen der Sicherung als Dialog. Läuft im Hintergrund-Thread, Dialoge im UI-Thread.</summary>
public sealed class DialogSafetyPrompt : ISafetyPrompt
{
    private readonly LocalizationService _localization;
    private readonly Action<string> _progress;

    public DialogSafetyPrompt(LocalizationService localization, Action<string> progress)
    {
        _localization = localization;
        _progress = progress;
    }

    public bool? AskEnableProtection()
    {
        var answer = OnUi(() => MessageBox.Show(
            _localization.Get("Safety.AskEnable"),
            _localization.Get("App.Name"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Yes));

        return answer switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null,
        };
    }

    public bool AskContinueWithoutRestorePoint(string reason)
    {
        var text = reason == "policy"
            ? _localization.Get("Safety.Policy")
            : $"{_localization.Get("Safety.CreateFailed")}\n\n{reason}\n\n{_localization.Get("Safety.ContinueAnyway")}";

        return OnUi(() => MessageBox.Show(text, _localization.Get("App.Name"), MessageBoxButton.YesNo, MessageBoxImage.Warning))
            == MessageBoxResult.Yes;
    }

    public void ReportProgress(string stage) =>
        OnUi(() =>
        {
            _progress(_localization.Get($"Safety.Stage.{stage}"));
            return true;
        });

    private static T OnUi<T>(Func<T> action)
    {
        var dispatcher = Application.Current.Dispatcher;
        return dispatcher.CheckAccess() ? action() : dispatcher.Invoke(action);
    }
}
