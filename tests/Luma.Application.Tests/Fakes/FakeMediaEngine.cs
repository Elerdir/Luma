using Luma.Application.Abstractions;
using Luma.Domain.Media;
using Luma.Domain.Playback;

namespace Luma.Application.Tests.Fakes;

/// <summary>
/// Hand-written test double for <see cref="IMediaEngine"/>. Records calls and lets
/// tests drive backend callbacks explicitly (Opened / Position / End / Failed).
/// </summary>
public sealed class FakeMediaEngine : IMediaEngine
{
    public List<MediaSource> Opens { get; } = [];
    public int PlayCount { get; private set; }
    public int PauseCount { get; private set; }
    public int StopCount { get; private set; }
    public TimeSpan? LastSeek { get; private set; }
    public Volume? LastVolume { get; private set; }
    public PlaybackRate? LastRate { get; private set; }
    public bool Disposed { get; private set; }

    public Task OpenAsync(MediaSource source, CancellationToken cancellationToken = default)
    {
        Opens.Add(source);
        return Task.CompletedTask;
    }

    public void Play() => PlayCount++;
    public void Pause() => PauseCount++;
    public void Stop() => StopCount++;
    public void SeekTo(TimeSpan position) => LastSeek = position;
    public void SetVolume(Volume volume) => LastVolume = volume;
    public void SetRate(PlaybackRate rate) => LastRate = rate;

    public MediaTrack? LastAudioTrack { get; private set; }
    public MediaTrack? LastSubtitleTrack { get; private set; }
    public int SubtitleSelectionCount { get; private set; }

    public void SelectAudioTrack(MediaTrack track) => LastAudioTrack = track;

    public void SelectSubtitleTrack(MediaTrack? track)
    {
        LastSubtitleTrack = track;
        SubtitleSelectionCount++;
    }

    /// <summary>Subtitle files attached so far, with whether each was asked to be selected.</summary>
    public List<(MediaSource File, bool Select)> AddedSubtitles { get; } = [];

    public void AddSubtitleFile(MediaSource file, bool select) => AddedSubtitles.Add((file, select));

    public event EventHandler<MediaOpenedEventArgs>? Opened;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler? EndReached;
    public event EventHandler<TracksChangedEventArgs>? TracksChanged;
    public event EventHandler<MediaFailedEventArgs>? Failed;

    public void RaiseTracksChanged(IReadOnlyList<MediaTrack> tracks) =>
        TracksChanged?.Invoke(this, new TracksChangedEventArgs(tracks));

    public void RaiseOpened(TimeSpan duration, IReadOnlyList<MediaTrack>? tracks = null) =>
        Opened?.Invoke(this, new MediaOpenedEventArgs(duration, tracks ?? []));
    public void RaisePosition(TimeSpan position) => PositionChanged?.Invoke(this, position);
    public void RaiseEnd() => EndReached?.Invoke(this, EventArgs.Empty);
    public void RaiseFailed(string message) => Failed?.Invoke(this, new MediaFailedEventArgs(message));

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
