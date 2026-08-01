using System.ComponentModel;

namespace Luma.Presentation.Localization;

/// <summary>
/// One localized string, as a bindable object with a plain <see cref="Value"/> property.
///
/// Bindings go through this rather than straight at the localizer's indexer because
/// Avalonia does not re-read an indexer binding when the source announces the blanket
/// <c>Item[]</c> change that WPF uses — the text stayed in whatever language the window
/// was built in. A normal property with a normal change notification has no such
/// ambiguity.
/// </summary>
public sealed class LocalizedString : INotifyPropertyChanged
{
    private readonly string _key;

    public LocalizedString(string key)
    {
        _key = key;

        // Lives as long as the control it feeds, which for a window's chrome is the
        // lifetime of the app, so there is nothing to unsubscribe from.
        Localizer.Instance.PropertyChanged += OnLanguageChanged;
    }

    public string Value => Localizer.Instance[_key];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Only the blanket "every string changed" signal; the localizer also announces
        // CurrentLanguage, and reacting to both would update every label twice.
        if (e.PropertyName is not (Localizer.AllStringsChanged or null or ""))
            return;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }

    public override string ToString() => Value;
}
