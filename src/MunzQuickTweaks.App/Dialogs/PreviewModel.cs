using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MunzQuickTweaks.App.Dialogs;

/// <summary>Ein abwählbarer Punkt in der Vorschau, z. B. ein Tweak oder eine App.</summary>
public sealed partial class PreviewItem : ObservableObject
{
    public PreviewItem(string text, object payload, string? badge = null, bool isChecked = true)
    {
        Text = text;
        Payload = payload;
        Badge = badge;
        _isChecked = isChecked;
    }

    public string Text { get; }

    /// <summary>Zusatz wie "Risiko mittel".</summary>
    public string? Badge { get; }

    public bool HasBadge => Badge is not null;

    public object Payload { get; }

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>Bereich der Vorschau mit Hauptschalter, z. B. "Apps entfernen".</summary>
public sealed partial class PreviewSection : ObservableObject
{
    private bool _syncing;

    public PreviewSection(string key, string title, string? hint, IEnumerable<PreviewItem> items, bool enabled = true)
    {
        Key = key;
        Title = title;
        Hint = hint;
        Items = new ObservableCollection<PreviewItem>(items);
        _isEnabled = enabled;
        foreach (var item in Items)
        {
            item.IsChecked &= enabled;
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PreviewItem.IsChecked) && !_syncing)
                {
                    _syncing = true;
                    IsEnabled = Items.Any(i => i.IsChecked);
                    _syncing = false;
                }
            };
        }
    }

    public string Key { get; }

    public string Title { get; }

    public string? Hint { get; }

    public bool HasHint => !string.IsNullOrEmpty(Hint);

    public ObservableCollection<PreviewItem> Items { get; }

    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        foreach (var item in Items)
        {
            item.IsChecked = value;
        }

        _syncing = false;
    }

    public IEnumerable<T> Selected<T>() => IsEnabled ? Items.Where(i => i.IsChecked).Select(i => i.Payload).OfType<T>() : Enumerable.Empty<T>();
}

public sealed class PreviewModel
{
    public PreviewModel(string heading, string intro, string confirmText, string cancelText, IEnumerable<PreviewSection> sections)
    {
        Heading = heading;
        Intro = intro;
        ConfirmText = confirmText;
        CancelText = cancelText;
        Sections = sections.Where(s => s.Items.Count > 0).ToList();
    }

    public string Heading { get; }

    public string Intro { get; }

    public string ConfirmText { get; }

    public string CancelText { get; }

    public IReadOnlyList<PreviewSection> Sections { get; }

    public PreviewSection? Section(string key) => Sections.FirstOrDefault(s => s.Key == key);
}
