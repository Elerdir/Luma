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

    /// <summary>Silence or restore output without discarding the chosen volume.</summary>
    void SetMuted(bool muted);

    void ToggleMute();
    void SetRate(PlaybackRate rate);
    void SetRepeat(RepeatMode mode);

    /// <summary>Switch the active audio stream.</summary>
    void SelectAudioTrack(MediaTrack track);

    /// <summary>Switch the active subtitle stream, or pass <c>null</c> to turn subtitles off.</summary>
    void SelectSubtitleTrack(MediaTrack? track);

    /// <summary>
    /// Attach an external subtitle file to the loaded media and select it. The new
    /// stream appears in the next snapshot's <see cref="PlayerSnapshot.SubtitleTracks"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">No media is loaded.</exception>
    void AddSubtitleFile(MediaSource file);

    Task NextAsync(CancellationToken cancellationToken = default);
    Task PreviousAsync(CancellationToken cancellationToken = default);

    /// <summary>Append sources to the playlist. Starts playing if nothing was loaded.</summary>
    Task EnqueueAsync(IReadOnlyList<MediaSource> sources, CancellationToken cancellationToken = default);

    /// <summary>Jump to an explicit playlist entry and start playing it.</summary>
    Task PlayAtAsync(int index, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drop a playlist entry. Removing the entry that is currently playing advances
    /// to the one that takes its place, or stops when the list empties.
    /// </summary>
    Task RemoveAtAsync(int index, CancellationToken cancellationToken = default);

    /// <summary>Empty the playlist and stop playback.</summary>
    void ClearPlaylist();
}
