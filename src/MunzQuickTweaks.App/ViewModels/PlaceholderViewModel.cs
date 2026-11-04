namespace MunzQuickTweaks.App.ViewModels;

/// <summary>Platzhalter für Bereiche, die in späteren Phasen entstehen.</summary>
public sealed class PlaceholderViewModel : PageViewModel
{
    public PlaceholderViewModel(string titleKey, string descriptionKey, int phase)
        : base(titleKey)
    {
        DescriptionKey = descriptionKey;
        Phase = phase;
    }

    public string DescriptionKey { get; }

    public int Phase { get; }
}
