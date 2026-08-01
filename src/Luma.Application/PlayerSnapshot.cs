using Luma.Domain.Media;
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
    int PlaylistIndex,
    IReadOnlyList<MediaTrack> AudioTracks,
    IReadOnlyList<MediaTrack> SubtitleTracks,
    MediaTrack? SelectedAudioTrack,
    MediaTrack? SelectedSubtitleTrack)
{
    public bool HasMedia => Status is not PlaybackStatus.NoMedia;
    public bool IsPlaying => Status is PlaybackStatus.Playing;

    // Command availability, mirroring the domain's transition rules so the UI can
    // disable controls that would otherwise throw.
    public bool CanPlay => Status.CanPlay();
    public bool CanPause => Status.CanPause();
    public bool CanTogglePlayPause => Status.CanTogglePlayPause();
    public bool CanSeek => Status.CanSeek();
    public bool CanStop => Status.CanStop();

    /// <summary>Progress in the range [0, 1]; 0 when the duration is unknown.</summary>
    public double Progress =>
        Duration > TimeSpan.Zero
            ? Math.Clamp(Position / Duration, 0d, 1d)
            : 0d;
}
