using Avalonia.Input;

namespace Luma.Presentation.Input;

/// <summary>
/// Which key press means which action.
///
/// This was a private method on the window, which is why it was never tested and why
/// it had been wrong on macOS since the day the shortcuts were written. Two faults,
/// one cause — the modifiers were asked about by name:
///
///   * Ctrl+O is how a Windows application is asked to open a file. A Mac is asked
///     with Cmd+O, and nothing here knew Cmd existed, so Luma had no Open shortcut on
///     macOS at all.
///   * Worse, a modifier nobody asks about is a modifier nobody rejects. Cmd+S is Save
///     everywhere on a Mac; here it fell through to the bare S and stopped playback.
///     Cmd+P went to the previous episode, Cmd+M muted while minimising the window.
///     Windows had a milder version of the same thing: Win+S opens search and also
///     stopped the film.
///
/// So the rule is stated once, positively: Shift is a decoration on seeking, the
/// platform's command modifier belongs to Open, and every other combination is
/// somebody else's.
/// </summary>
public static class PlayerShortcuts
{
    /// <summary>
    /// The modifier this platform means by "I am commanding the application" — Cmd on
    /// macOS, Ctrl everywhere else.
    /// </summary>
    public static KeyModifiers CommandModifier =>
        OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    /// <summary>
    /// The Open shortcut as it should be written for the person reading it. Spelled out
    /// here rather than taken from <see cref="KeyGesture"/>'s platform formatting, which
    /// depends on a formatter being registered and would render differently depending on
    /// when it was asked.
    /// </summary>
    public static string OpenGestureText =>
        OperatingSystem.IsMacOS() ? "⌘O" : "Ctrl+O";

    /// <summary>The Open shortcut, for a menu item to display and honour.</summary>
    public static KeyGesture OpenGesture => new(Key.O, CommandModifier);

    /// <summary>
    /// Keys that a focused control would consume before the window ever saw them, and
    /// which therefore have to be claimed on the way down.
    ///
    /// Deliberately the shortest possible list: taking a key on the way down means
    /// taking it away from whatever is focused. Space is here because it is what presses
    /// a focused button — click Play once and Space stopped pausing the film.
    /// </summary>
    public static bool IsClaimedOnTheWayDown(Key key) => key is Key.Space;

    /// <summary>What this key press asks for, or null if it is not ours.</summary>
    public static PlayerAction? For(Key key, KeyModifiers modifiers) =>
        For(key, modifiers, CommandModifier);

    /// <summary>
    /// As above, with the command modifier given explicitly — which is what lets the
    /// macOS behaviour be tested from a machine that is not one.
    /// </summary>
    public static PlayerAction? For(Key key, KeyModifiers modifiers, KeyModifiers command)
    {
        var shift = modifiers.HasFlag(KeyModifiers.Shift);

        // Shift is the only modifier that decorates an existing shortcut rather than
        // making a different one, so it is set aside before asking what is left.
        var held = modifiers & ~KeyModifiers.Shift;

        if (held == command)
            return key is Key.O ? PlayerAction.Open : null;

        // Anything else held down means the user is asking the system, or another
        // application, for something. Not ours to take.
        if (held is not KeyModifiers.None)
            return null;

        return key switch
        {
            Key.Space or Key.K => PlayerAction.PlayPause,
            Key.Left => shift ? PlayerAction.SeekBackwardLarge : PlayerAction.SeekBackward,
            Key.Right => shift ? PlayerAction.SeekForwardLarge : PlayerAction.SeekForward,
            Key.Up => PlayerAction.VolumeUp,
            Key.Down => PlayerAction.VolumeDown,
            Key.M => PlayerAction.ToggleMute,
            Key.S => PlayerAction.Stop,
            Key.R => PlayerAction.CycleRepeat,
            Key.L => PlayerAction.TogglePlaylist,

            // Next and previous, on both the letters and the keys a hand already rests
            // near while watching: page down is the next episode in the folder.
            Key.N or Key.PageDown => PlayerAction.Next,
            Key.P or Key.PageUp => PlayerAction.Previous,

            Key.F or Key.F11 => PlayerAction.ToggleFullscreen,
            Key.Escape => PlayerAction.LeaveFullscreen,

            _ => null
        };
    }
}
