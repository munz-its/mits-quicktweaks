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

    [ObservableProperty]
    private string _title = string.Empty;

    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;
}
