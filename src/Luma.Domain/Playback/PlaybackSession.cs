using Luma.Domain.Media;

namespace Luma.Domain.Playback;

/// <summary>
/// The aggregate root for a single playback session. It is the single source of
/// truth for which operations are legal in which state, independent of any media
/// backend. Illegal operations throw <see cref="InvalidPlaybackTransitionException"/>;
/// backend-driven reports (position/ended) are tolerant of async races.
/// </summary>
public sealed class PlaybackSession
{
    public PlaybackStatus Status { get; private set; } = PlaybackStatus.NoMedia;
    public MediaSource? Source { get; private set; }
    public TimeSpan Position { get; private set; } = TimeSpan.Zero;
    public TimeSpan Duration { get; private set; } = TimeSpan.Zero;
    public Volume Volume { get; private set; } = Volume.Default;
    public PlaybackRate Rate { get; private set; } = PlaybackRate.Normal;
    public string? FaultMessage { get; private set; }

    public bool HasMedia => Status is not PlaybackStatus.NoMedia;
    public bool IsPlaying => Status is PlaybackStatus.Playing;

    /// <summary>Begin opening a new source. Legal from any state.</summary>
    public void BeginLoad(MediaSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        Status = PlaybackStatus.Loading;
        Position = TimeSpan.Zero;
        Duration = TimeSpan.Zero;
        FaultMessage = null;
    }

    /// <summary>The backend finished opening the source and reported its duration.</summary>
    public void CompleteLoad(TimeSpan duration, bool autoPlay = true)
    {
        Require(PlaybackStatus.Loading, nameof(CompleteLoad));
        Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        Status = autoPlay ? PlaybackStatus.Playing : PlaybackStatus.Paused;
    }

    /// <summary>Resume/start playback. Restarts from the beginning when Ended.</summary>
    public void Play()
    {
        switch (Status)
        {
            case PlaybackStatus.Playing:
                return; // idempotent
            case PlaybackStatus.Paused:
                Status = PlaybackStatus.Playing;
                return;
            case PlaybackStatus.Ended:
                Position = TimeSpan.Zero;
                Status = PlaybackStatus.Playing;
                return;
            default:
                throw new InvalidPlaybackTransitionException(Status, nameof(Play));
        }
    }

    /// <summary>Hold playback at the current position.</summary>
    public void Pause()
    {
        switch (Status)
        {
            case PlaybackStatus.Paused:
                return; // idempotent
            case PlaybackStatus.Playing:
                Status = PlaybackStatus.Paused;
                return;
            default:
                throw new InvalidPlaybackTransitionException(Status, nameof(Pause));
        }
    }

    /// <summary>Toggle between playing and paused. Ended is treated as "restart".</summary>
    public void TogglePlayPause()
    {
        if (Status is PlaybackStatus.Playing) Pause();
        else Play();
    }

    /// <summary>Backend reported the media reached its end.</summary>
    public void ReportEnded()
    {
        Require(PlaybackStatus.Playing, nameof(ReportEnded));
        Position = Duration;
        Status = PlaybackStatus.Ended;
    }

    /// <summary>
    /// Backend reported a new position. Tolerant of races: applied only while
    /// Playing or Paused, and always clamped to [0, Duration].
    /// </summary>
    public void ReportPosition(TimeSpan position)
    {
        if (Status is not (PlaybackStatus.Playing or PlaybackStatus.Paused))
            return;
        Position = Clamp(position);
    }

    /// <summary>Seek to a position. Legal only when media is loaded and not loading.</summary>
    public void Seek(TimeSpan position)
    {
        if (Status is PlaybackStatus.NoMedia or PlaybackStatus.Loading or PlaybackStatus.Faulted)
            throw new InvalidPlaybackTransitionException(Status, nameof(Seek));

        Position = Clamp(position);
        if (Status is PlaybackStatus.Ended)
            Status = PlaybackStatus.Paused;
    }

    /// <summary>Stop playback and unload the current media.</summary>
    public void Stop()
    {
        if (Status is PlaybackStatus.NoMedia)
            return; // idempotent

        Status = PlaybackStatus.NoMedia;
        Source = null;
        Position = TimeSpan.Zero;
        Duration = TimeSpan.Zero;
        FaultMessage = null;
    }

    /// <summary>Record an unrecoverable error for the current source. Legal from any state.</summary>
    public void Fault(string message)
    {
        FaultMessage = string.IsNullOrWhiteSpace(message) ? "Unknown playback error." : message;
        Status = PlaybackStatus.Faulted;
    }

    /// <summary>Change volume. Legal from any state.</summary>
    public void ChangeVolume(Volume volume) => Volume = volume;

    /// <summary>Change playback speed. Legal from any state.</summary>
    public void ChangeRate(PlaybackRate rate) => Rate = rate;

    private TimeSpan Clamp(TimeSpan position)
    {
        if (position < TimeSpan.Zero) return TimeSpan.Zero;
        if (Duration > TimeSpan.Zero && position > Duration) return Duration;
        return position;
    }

    private void Require(PlaybackStatus expected, string operation)
    {
        if (Status != expected)
            throw new InvalidPlaybackTransitionException(Status, operation);
    }
}
