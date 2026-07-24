using Luma.Domain.Media;
using Luma.Domain.Playback;

namespace Luma.Application.Abstractions;

/// <summary>
/// The port to a media backend (LibVLC, FFmpeg, ...). This is the seam that keeps
/// the domain and application layers independent of any concrete player.
/// Implementations are expected to raise events on a background thread; callers
/// are responsible for marshalling to a UI thread if needed.
/// </summary>
public interface IMediaEngine : IAsyncDisposable
{
    /// <summary>Begin opening a source. Completion is signalled by <see cref="Opened"/>.</summary>
    Task OpenAsync(MediaSource source, CancellationToken cancellationToken = default);

    void Play();
    void Pause();
    void Stop();
    void SeekTo(TimeSpan position);
    void SetVolume(Volume volume);
    void SetRate(PlaybackRate rate);

    /// <summary>The source finished opening and its duration is known.</summary>
    event EventHandler<MediaOpenedEventArgs>? Opened;

    /// <summary>The playback position advanced.</summary>
    event EventHandler<TimeSpan>? PositionChanged;

    /// <summary>The current media reached its end.</summary>
    event EventHandler? EndReached;

    /// <summary>The backend encountered an unrecoverable error.</summary>
    event EventHandler<MediaFailedEventArgs>? Failed;
}
