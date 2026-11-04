using CommunityToolkit.Mvvm.ComponentModel;

namespace MunzQuickTweaks.App.ViewModels;

/// <summary>Basis aller Seiten. OnNavigatedTo läuft bei jedem Öffnen der Seite.</summary>
public abstract partial class PageViewModel : ObservableObject
{
    protected PageViewModel(string titleKey)
    {
        TitleKey = titleKey;
    }

    public string TitleKey { get; }

    /// <summary>Schlüssel der großen Überschrift. Standard: wie der Titel.</summary>
    public virtual string HeadingKey => TitleKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _heading = string.Empty;

    /// <summary>Kleine Zeile über der Überschrift, in Großbuchstaben wie auf munz-its.de.</summary>
    public string Label => Title.ToUpper(System.Globalization.CultureInfo.CurrentUICulture);

    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;
}
