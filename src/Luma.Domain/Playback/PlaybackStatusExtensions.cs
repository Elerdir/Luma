namespace Luma.Domain.Playback;

/// <summary>
/// Which operations a <see cref="PlaybackStatus"/> admits. The aggregate throws on
/// illegal transitions, so callers (UI commands, keyboard shortcuts) need to ask
/// first — these predicates are the single source of truth for both sides.
/// </summary>
public static class PlaybackStatusExtensions
{
    /// <summary>Media is open and addressable: loading finished and nothing faulted.</summary>
    public static bool IsLoaded(this PlaybackStatus status) =>
        status is PlaybackStatus.Playing or PlaybackStatus.Paused or PlaybackStatus.Ended;

    /// <summary>Playing is legal (from Ended it restarts from the beginning).</summary>
    public static bool CanPlay(this PlaybackStatus status) => status.IsLoaded();

    /// <summary>Pausing is legal. Unlike <see cref="CanPlay"/> this excludes Ended.</summary>
    public static bool CanPause(this PlaybackStatus status) =>
        status is PlaybackStatus.Playing or PlaybackStatus.Paused;

    /// <summary>Play/pause toggling is legal — Ended toggles into a restart.</summary>
    public static bool CanTogglePlayPause(this PlaybackStatus status) => status.IsLoaded();

    /// <summary>Seeking is legal.</summary>
    public static bool CanSeek(this PlaybackStatus status) => status.IsLoaded();

    /// <summary>Stopping is legal (it is a no-op when nothing is loaded).</summary>
    public static bool CanStop(this PlaybackStatus status) => status is not PlaybackStatus.NoMedia;
}
