namespace Luma.Domain.Playback;

/// <summary>The logical playback state, independent of any media backend.</summary>
public enum PlaybackStatus
{
    /// <summary>No media loaded.</summary>
    NoMedia,

    /// <summary>A source is being opened/buffered.</summary>
    Loading,

    /// <summary>Media is loaded and advancing.</summary>
    Playing,

    /// <summary>Media is loaded but held at the current position.</summary>
    Paused,

    /// <summary>Playback reached the end of the media.</summary>
    Ended,

    /// <summary>An unrecoverable error occurred for the current source.</summary>
    Faulted
}
