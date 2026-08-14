using Avalonia.Controls;
using Luma.Presentation.Input;
using Luma.Presentation.Localization;
using Luma.Presentation.Services;
using Luma.Presentation.Tests.Fakes;
using Luma.Presentation.ViewModels;

namespace Luma.Presentation.Tests;

/// <summary>
/// The macOS menu bar. Nothing here can be seen from Windows, so what is checked is
/// what can be: that every item names a command that exists, carries text that is
/// translated, and follows the language when it changes.
///
/// Whether macOS renders it is the Mac's to answer.
/// </summary>
[Collection(nameof(LocalizerTests))]
public sealed class ApplicationMenuTests : IDisposable
{
    private readonly string _originalLanguage = Localizer.Instance.CurrentLanguage;

    public void Dispose() => Localizer.Instance.SetLanguage(_originalLanguage);

    private static MainViewModel ViewModel() =>
        new(new FakePlayer(), new FakeFilePicker(), new FakeUpdateService(),
            new FakeInstallerLauncher(),
            new InterfaceOptionsService(new FakeSettingsStore<InterfaceOptions>()));

    /// <summary>
    /// Every item in the menu, headings included, separators not.
    ///
    /// NativeMenuItemSeparator derives from NativeMenuItem in Avalonia and appears with
    /// "-" as its header, so filtering by type is not enough to leave it out.
    /// </summary>
    private static IEnumerable<NativeMenuItem> AllItems(NativeMenu menu)
    {
        foreach (var entry in menu.Items.OfType<NativeMenuItem>())
        {
            if (entry is NativeMenuItemSeparator)
                continue;

            yield return entry;

            if (entry.Menu is { } submenu)
                foreach (var nested in AllItems(submenu))
                    yield return nested;
        }
    }

    [Fact]
    public void The_menu_has_the_headings_a_player_needs()
    {
        Localizer.Instance.SetLanguage("en");

        var headings = ApplicationMenu.Build(ViewModel())
            .Items.OfType<NativeMenuItem>()
            .Select(item => item.Header)
            .ToArray();

        headings.ShouldBe(["File", "Playback", "View"]);
    }

    /// <summary>
    /// A heading opens a submenu and does nothing itself; everything else has to do
    /// something. An item with neither is a dead entry in the menu bar.
    /// </summary>
    [Fact]
    public void Every_item_either_opens_a_submenu_or_runs_a_command()
    {
        foreach (var item in AllItems(ApplicationMenu.Build(ViewModel())))
        {
            var opensSubmenu = item.Menu is not null;
            var runsSomething = item.Command is not null;

            (opensSubmenu ^ runsSomething).ShouldBeTrue(
                $"'{item.Header}' should do exactly one of the two.");
        }
    }

    /// <summary>
    /// Headers are bound rather than assigned, so text already on screen changes with
    /// the language. Assigning once would leave the menu bar in whatever language the
    /// application started in — and the menu bar is the one part of a Mac application
    /// that is always on screen.
    /// </summary>
    [Fact]
    public void Headings_follow_the_language()
    {
        Localizer.Instance.SetLanguage("en");
        var menu = ApplicationMenu.Build(ViewModel());
        var file = menu.Items.OfType<NativeMenuItem>().First();

        file.Header.ShouldBe("File");

        Localizer.Instance.SetLanguage("cs");

        file.Header.ShouldBe("Soubor");
    }

    [Fact]
    public void No_item_is_left_without_text()
    {
        Localizer.Instance.SetLanguage("cs");

        foreach (var item in AllItems(ApplicationMenu.Build(ViewModel())))
        {
            item.Header.ShouldNotBeNullOrWhiteSpace();

            // The localizer returns the key itself for a key it does not know, which is
            // loud enough to spot in a test and easy to miss on screen.
            item.Header.ShouldNotStartWith("Menu.");
        }
    }

    [Fact]
    public void Open_carries_the_platform_shortcut()
    {
        var open = AllItems(ApplicationMenu.Build(ViewModel()))
            .First(item => item.Command is not null);

        open.Gesture.ShouldBe(PlayerShortcuts.OpenGesture);
    }
}
