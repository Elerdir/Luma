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
    private readonly ISubtitleFinder? _subtitleFinder;
    private readonly IMediaFolderScanner? _folderScanner;
    private readonly PlaybackSession _session = new();
    private readonly Playlist _playlist = new();
    private readonly Lock _gate = new();
    private bool _disposed;

    /// <param name="engine">The media backend.</param>
    /// <param name="subtitleFinder">
    /// Optional. When supplied, sidecar subtitle files are attached automatically as
    /// each source opens. Omitting it simply means no automatic discovery.
    /// </param>
    /// <param name="folderScanner">
    /// Optional. When supplied, opening a single file also picks up the rest of its
    /// folder as the playlist. Omitting it means a single file stays a playlist of one.
    /// </param>
    public PlayerService(
        IMediaEngine engine,
        ISubtitleFinder? subtitleFinder = null,
        IMediaFolderScanner? folderScanner = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _subtitleFinder = subtitleFinder;
        _folderScanner = folderScanner;
        _engine.Opened += OnEngineOpened;
        _engine.PositionChanged += OnEnginePositionChanged;
        _engine.EndReached += OnEngineEndReached;
        _engine.TracksChanged += OnEngineTracksChanged;
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

        var (entries, startAt) = ExpandToFolder(sources);

        lock (_gate)
        {
            _playlist.Clear();
            _playlist.AddRange(entries);
            _playlist.JumpTo(startAt);
        }

        await LoadCurrentAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opening a single file loads its whole folder, with the cursor on the file that
    /// was actually asked for. Open episode 5 of a series and 6 is one press of Next
    /// away — which is the point, since nobody wants to reach for the file dialog
    /// between episodes.
    /// <para>
    /// Only for a single file: picking several files, or dragging a handful in, is an
    /// explicit choice of what to play and is taken literally.
    /// </para>
    /// </summary>
    private (IReadOnlyList<MediaSource> Entries, int StartAt) ExpandToFolder(
        IReadOnlyList<MediaSource> sources)
    {
        if (_folderScanner is null || sources.Count != 1)
            return (sources, 0);

        var opened = sources[0];
        var siblings = _folderScanner.FindSiblingsOf(opened);

        // If the file we opened is not in what came back — an unreadable folder, an
        // extension the scanner does not list — the folder is no use and the single
        // file stands on its own.
        var index = IndexOf(siblings, opened);
        return index < 0 ? (sources, 0) : (siblings, index);
    }

    private static int IndexOf(IReadOnlyList<MediaSource> sources, MediaSource wanted)
    {
        for (var i = 0; i < sources.Count; i++)
            if (Same(sources[i], wanted))
                return i;

        return -1;
    }

    /// <summary>
    /// Path equality, allowing for the file dialog and the directory listing disagreeing
    /// about case on the file systems where case does not distinguish two files.
    /// </summary>
    private static bool Same(MediaSource a, MediaSource b)
    {
        if (a == b)
            return true;

        if (!a.IsLocalFile || !b.IsLocalFile || OperatingSystem.IsLinux())
            return false;

        return string.Equals(
            a.Location.LocalPath, b.Location.LocalPath, StringComparison.OrdinalIgnoreCase);
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
            _engine.SetVolume(_session.EffectiveVolume);
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);
    }

    public void SetMuted(bool muted)
    {
        PlayerSnapshot snapshot;
        lock (_gate)
        {
            _session.SetMuted(muted);
            _engine.SetVolume(_session.EffectiveVolume);
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);
    }

    public void ToggleMute()
    {
        PlayerSnapshot snapshot;
        lock (_gate)
        {
            _session.ToggleMute();
            _engine.SetVolume(_session.EffectiveVolume);
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
        PlayerSnapshot snapshot;
        lock (_gate)
        {
            _playlist.Repeat = mode;
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);
    }

    public async Task EnqueueAsync(IReadOnlyList<MediaSource> sources, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
            return;

        PlayerSnapshot snapshot;
        bool startPlaying;
        lock (_gate)
        {
            // Appending to an idle player should behave like opening; appending while
            // something is playing must not interrupt it.
            startPlaying = _playlist.IsEmpty;
            _playlist.AddRange(sources);
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);

        if (startPlaying)
            await LoadCurrentAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task PlayAtAsync(int index, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (index < 0 || index >= _playlist.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            _playlist.JumpTo(index);
        }

        await LoadCurrentAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAtAsync(int index, CancellationToken cancellationToken = default)
    {
        PlayerSnapshot snapshot;
        bool wasCurrent;
        bool listEmptied;
        lock (_gate)
        {
            if (index < 0 || index >= _playlist.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            wasCurrent = index == _playlist.CurrentIndex;
            _playlist.RemoveAt(index);
            listEmptied = _playlist.IsEmpty;
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);

        if (!wasCurrent)
            return;

        // The entry that was playing is gone; RemoveAt has already moved the cursor
        // onto its successor (or the new last entry).
        if (listEmptied)
            Stop();
        else
            await LoadCurrentAsync(cancellationToken).ConfigureAwait(false);
    }

    public void ClearPlaylist()
    {
        PlayerSnapshot snapshot;
        lock (_gate)
        {
            _playlist.Clear();
            _session.Stop();
            _engine.Stop();
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);
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

    public void AddSubtitleFile(MediaSource file)
    {
        ArgumentNullException.ThrowIfNull(file);

        lock (_gate)
        {
            if (!_session.HasMedia)
                throw new InvalidOperationException("Load a media file before adding subtitles.");

            _engine.AddSubtitleFile(file, select: true);
        }
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
            _engine.SetVolume(_session.EffectiveVolume);
            _engine.SetRate(_session.Rate);

            // Push the session's default selection (first audio, subtitles off) to the engine.
            if (_session.SelectedAudioTrack is { } audio)
                _engine.SelectAudioTrack(audio);
            _engine.SelectSubtitleTrack(_session.SelectedSubtitleTrack);

            _engine.Play();
            snapshot = BuildSnapshot();
        }
        Publish(snapshot);

        AttachSidecarSubtitles(snapshot.Source);
    }

    /// <summary>
    /// Offer any subtitle files sitting next to the media. They arrive back as a
    /// <see cref="IMediaEngine.TracksChanged"/> event; none is selected automatically,
    /// so subtitles stay off until the user asks for them.
    /// </summary>
    private void AttachSidecarSubtitles(MediaSource? source)
    {
        if (_subtitleFinder is null || source is null)
            return;

        foreach (var subtitle in _subtitleFinder.FindFor(source))
            _engine.AddSubtitleFile(subtitle, select: false);
    }

    private void OnEngineTracksChanged(object? sender, TracksChangedEventArgs e)
    {
        PlayerSnapshot snapshot;
        lock (_gate)
        {
            if (!_session.HasMedia)
                return; // stale event from media that has since been unloaded

            _session.UpdateAvailableTracks(e.Tracks);
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
        _session.Source,
        _session.Source?.DisplayName,
        _session.Position,
        _session.Duration,
        _session.Volume,
        _session.IsMuted,
        _session.Rate,
        _session.FaultMessage,
        _playlist.Count,
        _playlist.CurrentIndex,
        [.. _playlist.Items],
        _playlist.Repeat,
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
        _engine.TracksChanged -= OnEngineTracksChanged;
        _engine.Failed -= OnEngineFailed;

        await _engine.DisposeAsync().ConfigureAwait(false);
    }
}
