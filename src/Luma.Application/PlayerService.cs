using Luma.Application.Abstractions;
using Luma.Domain.Media;
using Luma.Domain.Playback;
using Luma.Domain.Playlists;

namespace Luma.Application;

/// <summary>
/// Default <see cref="IPlayer"/> implementation. Owns the domain session and
/// playlist, translates commands into engine calls, and translates engine
/// callbacks back into domain transitions. All state mutation is serialized
/// through a gate so engine-thread callbacks and UI-thread commands are safe;
/// the <see cref="Changed"/> event is raised outside the lock.
/// </summary>
public sealed class PlayerService : IPlayer, IAsyncDisposable
{
    private readonly IMediaEngine _engine;
    private readonly PlaybackSession _session = new();
    private readonly Playlist _playlist = new();
    private readonly Lock _gate = new();
    private bool _disposed;

    public PlayerService(IMediaEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _engine.Opened += OnEngineOpened;
        _engine.PositionChanged += OnEnginePositionChanged;
        _engine.EndReached += OnEngineEndReached;
        _engine.Failed += OnEngineFailed;
    }

    public event EventHandler<PlayerSnapshot>? Changed;

    public PlayerSnapshot Snapshot
    {
        get { lock (_gate) return BuildSnapshot(); }
    }

    public async Task OpenAsync(MediaSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        await OpenAsync([source], cancellationToken).ConfigureAwait(false);
    }

    public async Task OpenAsync(IReadOnlyList<MediaSource> sources, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
            throw new ArgumentException("At least one source is required.", nameof(sources));

        lock (_gate)
        {
            _playlist.Clear();
            _playlist.AddRange(sources);
        }

        await LoadCurrentAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Play()
    {
        PlayerSnapshot snapshot;
        bool restartFromEnd;
        lock (_gate)
        {
            restartFromEnd = _session.Status is PlaybackStatus.Ended;
            _session.Play();
            if (restartFromEnd)
                _engine.SeekTo(TimeSpan.Zero);
            _engine.Play();
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);
    }

    public void Pause()
    {
        PlayerSnapshot snapshot;
        lock (_gate)
        {
            _session.Pause();
            _engine.Pause();
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);
    }

    public void TogglePlayPause()
    {
        PlayerSnapshot snapshot;
        lock (_gate)
        {
            // Decided inside the lock: reading the status first and acting after would
            // let an engine callback flip the state in between and desync the engine.
            var restartFromEnd = _session.Status is PlaybackStatus.Ended;
            _session.TogglePlayPause();

            if (_session.IsPlaying)
            {
                if (restartFromEnd)
                    _engine.SeekTo(TimeSpan.Zero);
                _engine.Play();
            }
            else
            {
                _engine.Pause();
            }

            snapshot = BuildSnapshot();
        }
        Publish(snapshot);
    }

    public void Stop()
    {
        PlayerSnapshot snapshot;
        lock (_gate)
        {
            _session.Stop();
            _engine.Stop();
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);
    }

    public void SeekTo(TimeSpan position)
    {
        PlayerSnapshot snapshot;
        lock (_gate)
        {
            _session.Seek(position);
            _engine.SeekTo(_session.Position);
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);
    }

    public void SetVolume(Volume volume)
    {
        PlayerSnapshot snapshot;
        lock (_gate)
        {
            _session.ChangeVolume(volume);
            _engine.SetVolume(volume);
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);
    }

    public void SetRate(PlaybackRate rate)
    {
        PlayerSnapshot snapshot;
        lock (_gate)
        {
            _session.ChangeRate(rate);
            _engine.SetRate(rate);
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);
    }

    public void SetRepeat(RepeatMode mode)
    {
        lock (_gate) _playlist.Repeat = mode;
    }

    public void SelectAudioTrack(MediaTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        PlayerSnapshot snapshot;
        lock (_gate)
        {
            _session.SelectAudioTrack(track);
            _engine.SelectAudioTrack(track);
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);
    }

    public void SelectSubtitleTrack(MediaTrack? track)
    {
        PlayerSnapshot snapshot;
        lock (_gate)
        {
            _session.SelectSubtitleTrack(track);
            _engine.SelectSubtitleTrack(track);
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);
    }

    public async Task NextAsync(CancellationToken cancellationToken = default)
    {
        bool moved;
        lock (_gate) moved = _playlist.MoveNext();
        if (moved) await LoadCurrentAsync(cancellationToken).ConfigureAwait(false);
        else Stop();
    }

    public async Task PreviousAsync(CancellationToken cancellationToken = default)
    {
        bool moved;
        lock (_gate) moved = _playlist.MovePrevious();
        if (moved) await LoadCurrentAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadCurrentAsync(CancellationToken cancellationToken)
    {
        MediaSource source;
        PlayerSnapshot snapshot;
        lock (_gate)
        {
            if (_playlist.Current is null)
                throw new InvalidOperationException("Playlist has no current item to load.");
            source = _playlist.Current;
            _session.BeginLoad(source);
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);
        await _engine.OpenAsync(source, cancellationToken).ConfigureAwait(false);
    }

    // ---- Engine callbacks (background thread) ----

    private void OnEngineOpened(object? sender, MediaOpenedEventArgs e)
    {
        PlayerSnapshot snapshot;
        lock (_gate)
        {
            if (_session.Status is not PlaybackStatus.Loading)
                return; // stale event
            _session.CompleteLoad(e.Duration, autoPlay: true);
            _session.SetAvailableTracks(e.Tracks);
            _engine.SetVolume(_session.Volume);
            _engine.SetRate(_session.Rate);

            // Push the session's default selection (first audio, subtitles off) to the engine.
            if (_session.SelectedAudioTrack is { } audio)
                _engine.SelectAudioTrack(audio);
            _engine.SelectSubtitleTrack(_session.SelectedSubtitleTrack);

            _engine.Play();
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);
    }

    private void OnEnginePositionChanged(object? sender, TimeSpan position)
    {
        PlayerSnapshot snapshot;
        lock (_gate)
        {
            _session.ReportPosition(position);
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);
    }

    private void OnEngineEndReached(object? sender, EventArgs e)
    {
        PlayerSnapshot snapshot;
        bool advance;
        lock (_gate)
        {
            if (_session.Status is not PlaybackStatus.Playing)
                return;
            _session.ReportEnded();
            advance = _playlist.MoveNext();
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);

        if (advance)
            _ = LoadNextInBackgroundAsync();
    }

    /// <summary>
    /// Auto-advance runs detached from any caller, so a failure to open the next item
    /// has nowhere to propagate. Surface it as a fault instead of losing it on a
    /// finalizer thread.
    /// </summary>
    private async Task LoadNextInBackgroundAsync()
    {
        try
        {
            await LoadCurrentAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PlayerSnapshot snapshot;
            lock (_gate)
            {
                _session.Fault(ex.Message);
                snapshot = BuildSnapshot();
            }
            Publish(snapshot);
        }
    }

    private void OnEngineFailed(object? sender, MediaFailedEventArgs e)
    {
        PlayerSnapshot snapshot;
        lock (_gate)
        {
            _session.Fault(e.Message);
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);
    }

    private PlayerSnapshot BuildSnapshot() => new(
        _session.Status,
        _session.Source?.DisplayName,
        _session.Position,
        _session.Duration,
        _session.Volume,
        _session.Rate,
        _session.FaultMessage,
        _playlist.Count,
        _playlist.CurrentIndex,
        [.. _session.AudioTracks],
        [.. _session.SubtitleTracks],
        _session.SelectedAudioTrack,
        _session.SelectedSubtitleTrack);

    private void Publish(PlayerSnapshot snapshot) => Changed?.Invoke(this, snapshot);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _engine.Opened -= OnEngineOpened;
        _engine.PositionChanged -= OnEnginePositionChanged;
        _engine.EndReached -= OnEngineEndReached;
        _engine.Failed -= OnEngineFailed;

        await _engine.DisposeAsync().ConfigureAwait(false);
    }
}
