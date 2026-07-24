using Luma.Domain.Media;

namespace Luma.Application.Abstractions;

/// <summary>
/// Raised when the backend has opened a source: its duration is known and the
/// selectable audio/subtitle streams have been enumerated.
/// </summary>
public sealed class MediaOpenedEventArgs(TimeSpan duration, IReadOnlyList<MediaTrack> tracks) : EventArgs
{
    public TimeSpan Duration { get; } = duration;

    /// <summary>All selectable streams (audio and subtitle) found in the media.</summary>
    public IReadOnlyList<MediaTrack> Tracks { get; } = tracks;
}

/// <summary>Raised when the backend fails to open or play the current source.</summary>
public sealed class MediaFailedEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}
