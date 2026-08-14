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
    private readonly string? _argument;

    public LocalizedString(string key, string? argument = null)
    {
        _key = key;
        _argument = argument;

        // Lives as long as the control it feeds, which for a window's chrome is the
        // lifetime of the app, so there is nothing to unsubscribe from.
        Localizer.Instance.PropertyChanged += OnLanguageChanged;
    }

    /// <summary>
    /// The translated text, with the argument substituted where the translation asks
    /// for it. An argument exists for text that has to name something the translation
    /// cannot know — the Open shortcut, which is written differently on a Mac.
    /// </summary>
    public string Value => _argument is null
        ? Localizer.Instance[_key]
        : Localizer.Instance.Format(_key, _argument);

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
