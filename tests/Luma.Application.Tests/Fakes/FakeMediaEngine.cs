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

    public event EventHandler<MediaOpenedEventArgs>? Opened;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler? EndReached;
    public event EventHandler<MediaFailedEventArgs>? Failed;

    public void RaiseOpened(TimeSpan duration) => Opened?.Invoke(this, new MediaOpenedEventArgs(duration));
    public void RaisePosition(TimeSpan position) => PositionChanged?.Invoke(this, position);
    public void RaiseEnd() => EndReached?.Invoke(this, EventArgs.Empty);
    public void RaiseFailed(string message) => Failed?.Invoke(this, new MediaFailedEventArgs(message));

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
