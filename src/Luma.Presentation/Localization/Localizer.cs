using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Luma.Presentation.Localization;

/// <summary>
/// The single source of translated text, and the thing bindings listen to when the
/// language changes.
///
/// A singleton rather than an injected service because XAML needs to reach it from
/// markup, where there is no constructor to inject into. Switching language announces
/// <see cref="AllStringsChanged"/>; each <see cref="LocalizedString"/> listens for that
/// and re-reads, which is what updates text already on screen without a restart.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    /// <summary>Sentinel meaning "whatever the operating system is set to".</summary>
    public const string SystemLanguage = "";

    /// <summary>
    /// Property name announced when the language changes, meaning "every string is
    /// now different". "Item[]" is the conventional name for a blanket indexer change.
    /// </summary>
    public const string AllStringsChanged = "Item[]";

    private static readonly ResourceManager Resources = new(
        "Luma.Presentation.Localization.Strings", typeof(Localizer).Assembly);

    public static Localizer Instance { get; } = new();

    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    private Localizer() { }

    /// <summary>
    /// Languages offered in the UI. Adding one means adding Strings.&lt;culture&gt;.resx
    /// and one entry here.
    /// </summary>
    public static IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
    [
        new(SystemLanguage, "Language.System"),
        new("en", "English"),
        new("cs", "Čeština")
    ];

    /// <summary>The culture code currently in force, or empty when following the system.</summary>
    public string CurrentLanguage { get; private set; } = SystemLanguage;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Look up a key. Unknown keys return the key itself, which is loud enough to spot.</summary>
    public string this[string key] => Resources.GetString(key, _culture) ?? key;

    /// <summary>Look up a key and substitute arguments into it.</summary>
    public string Format(string key, params object?[] args) =>
        string.Format(_culture, this[key], args);

    /// <summary>
    /// Switch language. Pass <see cref="SystemLanguage"/> to follow the OS. Unknown
    /// culture names fall back to the system rather than throwing — a bad value in a
    /// settings file must not stop the app from starting.
    /// </summary>
    public void SetLanguage(string language)
    {
        var culture = ResolveCulture(language);
        if (Equals(culture, _culture) && language == CurrentLanguage)
            return;

        _culture = culture;
        CurrentLanguage = language;

        // Also sets what string.Format and the file pickers see.
        CultureInfo.CurrentUICulture = culture;

        // "Item[]" is the conventional name for "every indexer value changed".
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(AllStringsChanged));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
    }

    private static CultureInfo ResolveCulture(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return CultureInfo.InstalledUICulture;

        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InstalledUICulture;
        }
    }
}

/// <summary>
/// One entry in the language picker.
/// </summary>
/// <param name="Code">Culture name, or empty for "follow the system".</param>
/// <param name="DisplayNameOrKey">
/// A language's own name is deliberately not translated — a Czech speaker looking for
/// Czech looks for "Čeština" whatever the UI is currently in. Only the "System" entry
/// is a resource key, because that one is a description rather than a name.
/// </param>
public sealed record LanguageOption(string Code, string DisplayNameOrKey)
{
    public bool IsSystem => Code.Length == 0;

    public string DisplayName =>
        IsSystem ? Localizer.Instance[DisplayNameOrKey] : DisplayNameOrKey;
}
