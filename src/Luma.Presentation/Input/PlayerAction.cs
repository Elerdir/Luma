namespace Luma.Presentation.Input;

/// <summary>
/// What a key press asks Luma to do, named without reference to any particular key.
///
/// The separation earns its keep on macOS, where the same physical action is asked for
/// with a different modifier — and where the shortcuts had never been checked at all.
/// </summary>
public enum PlayerAction
{
    PlayPause,
    Stop,
    SeekBackward,
    SeekBackwardLarge,
    SeekForward,
    SeekForwardLarge,
    VolumeUp,
    VolumeDown,
    ToggleMute,
    CycleRepeat,
    TogglePlaylist,
    Open,
    Next,
    Previous,
    ToggleFullscreen,

    /// <summary>
    /// Escape, which leaves fullscreen and otherwise means nothing. Kept apart from
    /// <see cref="ToggleFullscreen"/> so Escape can never put the window <em>into</em>
    /// fullscreen — and so a window that is not fullscreen leaves the key unhandled for
    /// whatever else might want it.
    /// </summary>
    LeaveFullscreen
}
