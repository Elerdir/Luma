using Avalonia.Input;
using Luma.Presentation.Input;

namespace Luma.Presentation.Tests;

/// <summary>
/// The shortcut table used to be a private method on the window, so none of this had
/// ever been checked — which is how Cmd+S came to stop playback on macOS.
///
/// Every test names the command modifier explicitly rather than asking the running
/// platform, so the macOS behaviour is exercised from Windows and Linux too.
/// </summary>
public class PlayerShortcutsTests
{
    private const KeyModifiers Mac = KeyModifiers.Meta;
    private const KeyModifiers Windows = KeyModifiers.Control;

    private static PlayerAction? OnMac(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        PlayerShortcuts.For(key, modifiers, Mac);

    private static PlayerAction? OnWindows(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        PlayerShortcuts.For(key, modifiers, Windows);

    // ---- The keys that carry no modifier behave the same everywhere ----

    [Theory]
    [InlineData(Key.Space, PlayerAction.PlayPause)]
    [InlineData(Key.K, PlayerAction.PlayPause)]
    [InlineData(Key.S, PlayerAction.Stop)]
    [InlineData(Key.M, PlayerAction.ToggleMute)]
    [InlineData(Key.R, PlayerAction.CycleRepeat)]
    [InlineData(Key.L, PlayerAction.TogglePlaylist)]
    [InlineData(Key.Left, PlayerAction.SeekBackward)]
    [InlineData(Key.Right, PlayerAction.SeekForward)]
    [InlineData(Key.Up, PlayerAction.VolumeUp)]
    [InlineData(Key.Down, PlayerAction.VolumeDown)]
    [InlineData(Key.N, PlayerAction.Next)]
    [InlineData(Key.PageDown, PlayerAction.Next)]
    [InlineData(Key.P, PlayerAction.Previous)]
    [InlineData(Key.PageUp, PlayerAction.Previous)]
    [InlineData(Key.F, PlayerAction.ToggleFullscreen)]
    [InlineData(Key.F11, PlayerAction.ToggleFullscreen)]
    [InlineData(Key.Escape, PlayerAction.LeaveFullscreen)]
    public void A_bare_key_means_the_same_on_both_platforms(Key key, PlayerAction expected)
    {
        OnWindows(key).ShouldBe(expected);
        OnMac(key).ShouldBe(expected);
    }

    [Theory]
    [InlineData(Key.Left, PlayerAction.SeekBackwardLarge)]
    [InlineData(Key.Right, PlayerAction.SeekForwardLarge)]
    public void Shift_makes_a_seek_a_long_one(Key key, PlayerAction expected)
    {
        OnWindows(key, KeyModifiers.Shift).ShouldBe(expected);
        OnMac(key, KeyModifiers.Shift).ShouldBe(expected);
    }

    // ---- Open, the one shortcut with a modifier of its own ----

    [Fact]
    public void Open_is_Ctrl_O_on_Windows_and_Cmd_O_on_a_Mac()
    {
        OnWindows(Key.O, KeyModifiers.Control).ShouldBe(PlayerAction.Open);
        OnMac(Key.O, KeyModifiers.Meta).ShouldBe(PlayerAction.Open);
    }

    [Fact]
    public void The_other_platform_modifier_does_not_open_anything()
    {
        // Ctrl+O on a Mac is not how anything is opened, and Win+O on Windows opens
        // the system's own things.
        OnMac(Key.O, KeyModifiers.Control).ShouldBeNull();
        OnWindows(Key.O, KeyModifiers.Meta).ShouldBeNull();
    }

    [Fact]
    public void A_bare_O_does_nothing()
    {
        OnWindows(Key.O).ShouldBeNull();
        OnMac(Key.O).ShouldBeNull();
    }

    // ---- What used to fall through ----
    //
    // A modifier nobody asks about is a modifier nobody rejects. These are the presses
    // that reached the bare-letter shortcuts and did something nobody asked for.

    [Theory]
    [InlineData(Key.S)]      // Save
    [InlineData(Key.P)]      // Print — went to the previous episode
    [InlineData(Key.M)]      // Minimise — muted on the way
    [InlineData(Key.N)]      // New — went to the next episode
    [InlineData(Key.F)]      // Find — went fullscreen
    [InlineData(Key.L)]
    [InlineData(Key.R)]
    public void A_command_key_press_that_is_not_Open_belongs_to_the_system(Key key)
    {
        OnMac(key, KeyModifiers.Meta).ShouldBeNull();
        OnWindows(key, KeyModifiers.Control).ShouldBeNull();
    }

    [Fact]
    public void The_Windows_key_is_not_ours_either()
    {
        // Win+S opens search. It also used to stop the film.
        OnWindows(Key.S, KeyModifiers.Meta).ShouldBeNull();
    }

    [Theory]
    [InlineData(KeyModifiers.Alt)]
    [InlineData(KeyModifiers.Alt | KeyModifiers.Shift)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData(KeyModifiers.Meta | KeyModifiers.Alt)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Meta)]
    public void Anything_else_held_down_is_somebody_elses(KeyModifiers modifiers)
    {
        OnWindows(Key.Space, modifiers).ShouldBeNull();
        OnMac(Key.Space, modifiers).ShouldBeNull();
    }

    [Fact]
    public void An_unmapped_key_is_left_alone()
    {
        OnWindows(Key.Q).ShouldBeNull();
        OnMac(Key.Tab).ShouldBeNull();
    }

    // ---- What the rest of the app reads off this type ----

    [Fact]
    public void The_command_modifier_matches_the_running_platform()
    {
        PlayerShortcuts.CommandModifier.ShouldBe(
            OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
    }

    [Fact]
    public void The_gesture_shown_to_the_user_matches_the_one_that_works()
    {
        var gesture = PlayerShortcuts.OpenGesture;

        gesture.Key.ShouldBe(Key.O);
        gesture.KeyModifiers.ShouldBe(PlayerShortcuts.CommandModifier);

        PlayerShortcuts.For(gesture.Key, gesture.KeyModifiers).ShouldBe(PlayerAction.Open);

        PlayerShortcuts.OpenGestureText.ShouldBe(
            OperatingSystem.IsMacOS() ? "⌘O" : "Ctrl+O");
    }

    /// <summary>
    /// Space is claimed on the way down because a focused button would otherwise press
    /// on it. Nothing else may be — taking a key before it reaches whatever has focus
    /// takes it away from a combo box, a slider or the playlist.
    /// </summary>
    [Fact]
    public void Only_Space_is_taken_before_the_focused_control_sees_it()
    {
        PlayerShortcuts.IsClaimedOnTheWayDown(Key.Space).ShouldBeTrue();

        foreach (var key in (Key[])Enum.GetValues(typeof(Key)))
        {
            if (key is not Key.Space)
                PlayerShortcuts.IsClaimedOnTheWayDown(key).ShouldBeFalse($"{key} is claimed too early.");
        }
    }
}
