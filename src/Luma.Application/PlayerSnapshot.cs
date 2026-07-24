using Luma.Domain.Playback;

namespace Luma.Application;

/// <summary>
/// An immutable read-model of the player's state, handed to the UI on every change.
/// Keeps the presentation layer decoupled from the mutable domain aggregate.
/// </summary>
public sealed record PlayerSnapshot(
    PlaybackStatus Status,
    string? MediaName,
    TimeSpan Position,
    TimeSpan Duration,
    Volume Volume,
    PlaybackRate Rate,
    string? FaultMessage,
    int PlaylistCount,
    int PlaylistIndex)
{
    public bool HasMedia => Status is not PlaybackStatus.NoMedia;
    public bool IsPlaying => Status is PlaybackStatus.Playing;

    /// <summary>Progress in the range [0, 1]; 0 when the duration is unknown.</summary>
    public double Progress =>
        Duration > TimeSpan.Zero
            ? Math.Clamp(Position / Duration, 0d, 1d)
            : 0d;
}
