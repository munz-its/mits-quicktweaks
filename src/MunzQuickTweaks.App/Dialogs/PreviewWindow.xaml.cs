using System.Windows;

namespace MunzQuickTweaks.App.Dialogs;

/// <summary>Zusammenfassung vor einem Profil oder vor "Alles zurücksetzen": jeder Punkt und jeder Bereich ist abwählbar.</summary>
public partial class PreviewWindow : Window
{
    public PreviewWindow(PreviewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }

    /// <returns>True, wenn bestätigt und mindestens ein Punkt gewählt ist.</returns>
    public static bool Ask(PreviewModel model)
    {
        var window = new PreviewWindow(model) { Owner = Application.Current?.MainWindow };
        return window.ShowDialog() == true && model.Sections.Any(s => s.Items.Any(i => i.IsChecked) && s.IsEnabled);
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
