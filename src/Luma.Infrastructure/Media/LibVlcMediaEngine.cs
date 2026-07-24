using Luma.Application.Abstractions;
using Luma.Domain.Media;
using Luma.Domain.Playback;
using LibVLCSharp.Shared;

namespace Luma.Infrastructure.Media;

/// <summary>
/// <see cref="IMediaEngine"/> backed by LibVLC via LibVLCSharp. Owns a single
/// <see cref="MediaPlayer"/> and translates its callbacks into engine events.
///
/// LibVLC forbids re-entering its API from a native callback thread, so callbacks
/// that lead the application to issue further engine calls (EndReached, error) are
/// re-raised on the thread pool.
/// </summary>
public sealed class LibVlcMediaEngine : IMediaEngine
{
    private static int _coreInitialized;

    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _player;
    private LibVLCSharp.Shared.Media? _currentMedia;
    private bool _disposed;

    public LibVlcMediaEngine()
    {
        EnsureCoreInitialized();
        _libVlc = new LibVLC();
        _player = new MediaPlayer(_libVlc);

        _player.TimeChanged += OnTimeChanged;
        _player.EndReached += OnEndReached;
        _player.EncounteredError += OnEncounteredError;
    }

    public event EventHandler<MediaOpenedEventArgs>? Opened;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler? EndReached;
    public event EventHandler<MediaFailedEventArgs>? Failed;

    public async Task OpenAsync(MediaSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ThrowIfDisposed();

        var media = new LibVLCSharp.Shared.Media(_libVlc, source.Location);
        try
        {
            var status = await media.Parse(
                MediaParseOptions.ParseLocal | MediaParseOptions.ParseNetwork,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (status is MediaParsedStatus.Failed or MediaParsedStatus.Timeout)
            {
                media.Dispose();
                Failed?.Invoke(this, new MediaFailedEventArgs($"Failed to open media ({status})."));
                return;
            }

            var previous = _currentMedia;
            _currentMedia = media;
            _player.Media = media;
            previous?.Dispose();

            var duration = media.Duration > 0
                ? TimeSpan.FromMilliseconds(media.Duration)
                : TimeSpan.Zero;
            Opened?.Invoke(this, new MediaOpenedEventArgs(duration));
        }
        catch (OperationCanceledException)
        {
            media.Dispose();
            throw;
        }
    }

    public void Play()
    {
        ThrowIfDisposed();
        _player.Play();
    }

    public void Pause()
    {
        ThrowIfDisposed();
        _player.SetPause(true);
    }

    public void Stop()
    {
        ThrowIfDisposed();
        _player.Stop();
    }

    public void SeekTo(TimeSpan position)
    {
        ThrowIfDisposed();
        _player.Time = (long)position.TotalMilliseconds;
    }

    public void SetVolume(Volume volume)
    {
        ThrowIfDisposed();
        _player.Volume = volume.Level;
    }

    public void SetRate(PlaybackRate rate)
    {
        ThrowIfDisposed();
        _player.SetRate((float)rate.Multiplier);
    }

    /// <summary>The underlying player, needed by the Avalonia <c>VideoView</c> to render frames.</summary>
    public MediaPlayer Player => _player;

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e) =>
        PositionChanged?.Invoke(this, TimeSpan.FromMilliseconds(e.Time));

    private void OnEndReached(object? sender, EventArgs e) =>
        // Must not touch LibVLC from its callback thread; hop to the pool.
        ThreadPool.QueueUserWorkItem(_ => EndReached?.Invoke(this, EventArgs.Empty));

    private void OnEncounteredError(object? sender, EventArgs e) =>
        ThreadPool.QueueUserWorkItem(
            _ => Failed?.Invoke(this, new MediaFailedEventArgs("The media backend encountered an error.")));

    private static void EnsureCoreInitialized()
    {
        if (Interlocked.Exchange(ref _coreInitialized, 1) == 0)
            Core.Initialize();
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        _player.TimeChanged -= OnTimeChanged;
        _player.EndReached -= OnEndReached;
        _player.EncounteredError -= OnEncounteredError;

        _player.Dispose();
        _currentMedia?.Dispose();
        _libVlc.Dispose();

        return ValueTask.CompletedTask;
    }
}
