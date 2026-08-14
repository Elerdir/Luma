using System.ComponentModel;
using System.Globalization;
using Luma.Presentation.Localization;

namespace Luma.Presentation.Tests;

/// <summary>
/// The localizer is a singleton because XAML has to reach it from markup, so these
/// tests share one instance and restore the language afterwards. They are marked
/// non-parallel for the same reason.
/// </summary>
[Collection(nameof(LocalizerTests))]
[CollectionDefinition(nameof(LocalizerTests), DisableParallelization = true)]
public sealed class LocalizerTests : IDisposable
{
    private readonly string _originalLanguage = Localizer.Instance.CurrentLanguage;

    public void Dispose() => Localizer.Instance.SetLanguage(_originalLanguage);

    [Fact]
    public void English_is_the_neutral_resource()
    {
        Localizer.Instance.SetLanguage("en");

        Localizer.Instance["Button.Open"].ShouldBe("Open");
        Localizer.Instance["Status.Playing"].ShouldBe("Playing");
    }

    [Fact]
    public void Czech_strings_are_returned_for_cs()
    {
        Localizer.Instance.SetLanguage("cs");

        Localizer.Instance["Button.Open"].ShouldBe("Otevřít");
        Localizer.Instance["Status.Playing"].ShouldBe("Přehrává se");
    }

    [Fact]
    public void Switching_language_changes_what_the_same_key_returns()
    {
        Localizer.Instance.SetLanguage("en");
        var english = Localizer.Instance["Playlist.Title"];

        Localizer.Instance.SetLanguage("cs");
        var czech = Localizer.Instance["Playlist.Title"];

        english.ShouldBe("Playlist");
        czech.ShouldBe("Seznam stop");
    }

    /// <summary>
    /// The signal every LocalizedString listens for; without it nothing on screen
    /// would know to re-read.
    /// </summary>
    [Fact]
    public void Switching_language_announces_that_every_string_changed()
    {
        Localizer.Instance.SetLanguage("en");

        var changed = new List<string?>();
        PropertyChangedEventHandler handler = (_, e) => changed.Add(e.PropertyName);
        Localizer.Instance.PropertyChanged += handler;

        try
        {
            Localizer.Instance.SetLanguage("cs");
        }
        finally
        {
            Localizer.Instance.PropertyChanged -= handler;
        }

        changed.ShouldContain(Localizer.AllStringsChanged);
    }

    /// <summary>
    /// LocalizedString is what every XAML binding actually observes, so this is the
    /// test that says "text already on screen updates when the language changes".
    /// Binding straight at the localizer's indexer did not do this — Avalonia ignored
    /// the blanket Item[] notification and the labels stayed in the startup language.
    /// </summary>
    [Fact]
    public void A_bound_string_updates_when_the_language_changes()
    {
        Localizer.Instance.SetLanguage("en");
        var bound = new LocalizedString("Button.Open");
        bound.Value.ShouldBe("Open");

        var notifications = 0;
        bound.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LocalizedString.Value))
                notifications++;
        };

        Localizer.Instance.SetLanguage("cs");

        bound.Value.ShouldBe("Otevřít");
        notifications.ShouldBe(1);
    }

    /// <summary>
    /// Some text has to name something no translation can know. The Open tooltip is the
    /// case that forced this: it used to spell out "Ctrl+O" in both resource files, which
    /// is simply the wrong shortcut on a Mac — and a translated string is the last place
    /// anyone would look for a platform assumption.
    /// </summary>
    [Fact]
    public void A_bound_string_can_carry_a_value_the_translation_does_not_know()
    {
        Localizer.Instance.SetLanguage("en");
        var bound = new LocalizedString("Tooltip.Open", "⌘O");

        bound.Value.ShouldBe("Open files (⌘O)");

        Localizer.Instance.SetLanguage("cs");

        bound.Value.ShouldBe("Otevřít soubory (⌘O)");
    }

    [Fact]
    public void An_unknown_key_returns_the_key_itself()
    {
        Localizer.Instance.SetLanguage("cs");

        Localizer.Instance["No.Such.Key"].ShouldBe("No.Such.Key");
    }

    [Fact]
    public void A_key_missing_from_a_translation_falls_back_to_English()
    {
        // Klingon has no resource file, so every lookup falls through to the neutral one.
        Localizer.Instance.SetLanguage("tlh");

        Localizer.Instance["Button.Open"].ShouldBe("Open");
    }

    [Fact]
    public void An_unknown_culture_does_not_throw()
    {
        Should.NotThrow(() => Localizer.Instance.SetLanguage("not-a-culture-name"));
    }

    [Fact]
    public void Format_substitutes_into_the_translated_string()
    {
        Localizer.Instance.SetLanguage("cs");

        Localizer.Instance.Format("Status.Error", "disk plný").ShouldBe("Chyba: disk plný");
    }

    [Fact]
    public void Following_the_system_reports_an_empty_language()
    {
        Localizer.Instance.SetLanguage(Localizer.SystemLanguage);

        Localizer.Instance.CurrentLanguage.ShouldBe("");
    }

    [Fact]
    public void Every_offered_language_resolves_to_a_real_culture()
    {
        foreach (var option in Localizer.AvailableLanguages.Where(l => !l.IsSystem))
            Should.NotThrow(() => CultureInfo.GetCultureInfo(option.Code));
    }

    /// <summary>
    /// A language's own name stays in that language whatever the UI is set to,
    /// otherwise someone looking for Czech in an English UI cannot find it.
    /// </summary>
    [Fact]
    public void Language_names_are_not_translated_but_the_System_entry_is()
    {
        var czech = Localizer.AvailableLanguages.Single(l => l.Code == "cs");
        var system = Localizer.AvailableLanguages.Single(l => l.IsSystem);

        Localizer.Instance.SetLanguage("en");
        czech.DisplayName.ShouldBe("Čeština");
        system.DisplayName.ShouldBe("System");

        Localizer.Instance.SetLanguage("cs");
        czech.DisplayName.ShouldBe("Čeština");
        system.DisplayName.ShouldBe("Podle systému");
    }

    /// <summary>
    /// Catches the usual translation bug: a key added to English and forgotten in Czech
    /// shows up as an English word in a Czech UI.
    /// </summary>
    [Fact]
    public void Czech_translates_every_key_English_defines()
    {
        var missing = new List<string>();

        foreach (var key in AllKeys())
        {
            Localizer.Instance.SetLanguage("en");
            var english = Localizer.Instance[key];

            Localizer.Instance.SetLanguage("cs");
            var czech = Localizer.Instance[key];

            // Identical text means the Czech resource has no entry and fell back.
            if (english == czech)
                missing.Add(key);
        }

        missing.ShouldBeEmpty();
    }

    private static IEnumerable<string> AllKeys()
    {
        var assembly = typeof(Localizer).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "Luma.Presentation.Localization.Strings.resources");

        stream.ShouldNotBeNull();

        using var reader = new System.Resources.ResourceReader(stream);
        foreach (System.Collections.DictionaryEntry entry in reader)
            yield return (string)entry.Key;
    }
}
