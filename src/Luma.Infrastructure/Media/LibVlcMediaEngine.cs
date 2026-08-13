using Luma.Application.Abstractions;
using Luma.Domain.Media;
using Luma.Domain.Playback;
using LibVLCSharp.Shared;

// Both Luma.Domain.Media and LibVLCSharp.Shared define a MediaTrack; these aliases
// keep the unqualified name bound to the domain type and name the VLC one explicitly.
using MediaTrack = Luma.Domain.Media.MediaTrack;
using VlcTrack = LibVLCSharp.Shared.MediaTrack;
using VlcMedia = LibVLCSharp.Shared.Media;

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

        // Luma discovers sidecar subtitles itself (see FileSystemSubtitleFinder), so
        // VLC's own scan is turned off: one mechanism, with rules that are unit-tested
        // and cannot double-add the same file.
        _libVlc = new LibVLC("--no-sub-autodetect-file");
        _player = new MediaPlayer(_libVlc)
        {
            // The host application owns input: without this VLC's video window
            // consumes mouse/keyboard events (and handles fullscreen itself),
            // so clicks never reach the Avalonia UI.
            EnableMouseInput = false,
            EnableKeyInput = false
        };

        _player.TimeChanged += OnTimeChanged;
        _player.EndReached += OnEndReached;
        _player.EncounteredError += OnEncounteredError;
        _player.ESAdded += OnElementaryStreamAdded;
    }

    public event EventHandler<MediaOpenedEventArgs>? Opened;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler? EndReached;
    public event EventHandler<TracksChangedEventArgs>? TracksChanged;
    public event EventHandler<MediaFailedEventArgs>? Failed;

    public async Task OpenAsync(MediaSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ThrowIfDisposed();

        // Owned until the player takes it. Parsing reaches into native code and can fail
        // in ways beyond the parse result — a cancelled open, a malformed URI — and every
        // one of those used to leak the native object until a finalizer got round to it.
        var media = new LibVLCSharp.Shared.Media(_libVlc, source.Location);
        var handedOver = false;
        try
        {
            var status = await media.Parse(
                MediaParseOptions.ParseLocal | MediaParseOptions.ParseNetwork,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (status is MediaParsedStatus.Failed or MediaParsedStatus.Timeout)
            {
                Failed?.Invoke(this, new MediaFailedEventArgs($"Failed to open media ({status})."));
                return;
            }

            var previous = _currentMedia;
            _currentMedia = media;
            _player.Media = media;
            handedOver = true;
            previous?.Dispose();

            var duration = media.Duration > 0
                ? TimeSpan.FromMilliseconds(media.Duration)
                : TimeSpan.Zero;
            Opened?.Invoke(this, new MediaOpenedEventArgs(duration, ReadTracks(media)));
        }
        finally
        {
            if (!handedOver)
                media.Dispose();
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

    public void SelectAudioTrack(MediaTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        ThrowIfDisposed();
        _player.SetAudioTrack(track.Id);
    }

    public void SelectSubtitleTrack(MediaTrack? track)
    {
        ThrowIfDisposed();
        // -1 is LibVLC's "disable subtitles" sentinel.
        _player.SetSpu(track?.Id ?? -1);
    }

    public void AddSubtitleFile(MediaSource file, bool select)
    {
        ArgumentNullException.ThrowIfNull(file);
        ThrowIfDisposed();

        _player.AddSlave(MediaSlaveType.Subtitle, file.Location.AbsoluteUri, select);
    }

    /// <summary>The underlying player, needed by the Avalonia <c>VideoView</c> to render frames.</summary>
    public MediaPlayer Player => _player;

    /// <summary>
    /// Map LibVLC's stream descriptions onto domain tracks. LibVLC reports a synthetic
    /// "Disable" entry (id -1) for subtitles, which the domain models as a null selection.
    /// </summary>
    private static IReadOnlyList<MediaTrack> ReadTracks(VlcMedia media)
    {
        var tracks = new List<MediaTrack>();

        foreach (var t in media.Tracks)
        {
            switch (t.TrackType)
            {
                case TrackType.Audio:
                    tracks.Add(MediaTrack.Audio(t.Id, DescribeTrack(t)));
                    break;
                case TrackType.Text when t.Id >= 0:
                    tracks.Add(MediaTrack.Subtitle(t.Id, DescribeTrack(t)));
                    break;
            }
        }

        return tracks;
    }

    /// <summary>Best available human label; empty falls back to "Track {id}" in the domain type.</summary>
    private static string DescribeTrack(VlcTrack track)
    {
        if (!string.IsNullOrWhiteSpace(track.Description))
            return track.Description;
        return string.IsNullOrWhiteSpace(track.Language) ? string.Empty : track.Language;
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e) =>
        PositionChanged?.Invoke(this, TimeSpan.FromMilliseconds(e.Time));

    /// <summary>
    /// VLC registers an attached subtitle asynchronously; this fires once the stream
    /// exists and can be enumerated. The descriptions come from the player rather than
    /// the media, because slaves are attached to the player.
    /// </summary>
    private void OnElementaryStreamAdded(object? sender, MediaPlayerESAddedEventArgs e) =>
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                TracksChanged?.Invoke(this, new TracksChangedEventArgs(ReadPlayerTracks()));
            }
            catch (ObjectDisposedException)
            {
                // The player went away while the callback was queued.
            }
        });

    /// <summary>Streams currently selectable on the player, including attached slaves.</summary>
    private IReadOnlyList<MediaTrack> ReadPlayerTracks()
    {
        var tracks = new List<MediaTrack>();

        foreach (var description in _player.AudioTrackDescription)
        {
            if (description.Id >= 0)
                tracks.Add(MediaTrack.Audio(description.Id, description.Name ?? string.Empty));
        }

        foreach (var description in _player.SpuDescription)
        {
            // Id -1 is LibVLC's synthetic "Disable" entry; the domain models that as null.
            if (description.Id >= 0)
                tracks.Add(MediaTrack.Subtitle(description.Id, description.Name ?? string.Empty));
        }

        return tracks;
    }

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
        _player.ESAdded -= OnElementaryStreamAdded;

        _player.Dispose();
        _currentMedia?.Dispose();
        _libVlc.Dispose();

        return ValueTask.CompletedTask;
    }
}
