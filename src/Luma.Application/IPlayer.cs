using Luma.Domain.Media;
using Luma.Domain.Playback;
using Luma.Domain.Playlists;

namespace Luma.Application;

/// <summary>
/// The application-facing player facade. Coordinates the domain session, the
/// playlist, and the media engine, and publishes an immutable snapshot on change.
/// </summary>
public interface IPlayer
{
    /// <summary>The current state as an immutable read-model.</summary>
    PlayerSnapshot Snapshot { get; }

    /// <summary>Raised after any state change, carrying the fresh snapshot.</summary>
    event EventHandler<PlayerSnapshot>? Changed;

    /// <summary>Replace the playlist with a single source and start playing it.</summary>
    Task OpenAsync(MediaSource source, CancellationToken cancellationToken = default);

    /// <summary>Replace the playlist with several sources and start the first.</summary>
    Task OpenAsync(IReadOnlyList<MediaSource> sources, CancellationToken cancellationToken = default);

    void Play();
    void Pause();
    void TogglePlayPause();
    void Stop();
    void SeekTo(TimeSpan position);
    void SetVolume(Volume volume);
    void SetRate(PlaybackRate rate);
    void SetRepeat(RepeatMode mode);

    Task NextAsync(CancellationToken cancellationToken = default);
    Task PreviousAsync(CancellationToken cancellationToken = default);
}
