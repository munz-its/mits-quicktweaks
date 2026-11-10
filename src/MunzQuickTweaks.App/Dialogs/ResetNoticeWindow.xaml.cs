using System.Windows;

namespace MunzQuickTweaks.App.Dialogs;

/// <summary>Kleiner Hinweis unten rechts nach dem Anmelden: ein Windows-Update hat Tweaks zurückgesetzt.</summary>
public partial class ResetNoticeWindow : Window
{
    public ResetNoticeWindow(string heading, string body, string apply, string later)
    {
        InitializeComponent();
        Heading.Text = heading;
        Body.Text = body;
        ApplyButton.Content = apply;
        LaterButton.Content = later;
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 8;
            Top = area.Bottom - ActualHeight - 8;
        };
    }

    /// <summary>True, wenn der Nutzer die Tweaks wieder anwenden will.</summary>
    public bool ApplyChosen { get; private set; }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        ApplyChosen = true;
        Close();
    }

    private void OnLater(object sender, RoutedEventArgs e) => Close();
}
