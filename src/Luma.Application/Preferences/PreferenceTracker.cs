using Luma.Application.Abstractions;
using Luma.Domain.Media;
using Luma.Domain.Playback;

namespace Luma.Application.Preferences;

/// <summary>
/// Keeps <see cref="PlayerPreferences"/> in step with a running <see cref="IPlayer"/>:
/// applies them at startup, records what changes while playing, and restores each
/// file's position when it is reopened.
///
/// It observes the player from the outside rather than living inside
/// <see cref="PlayerService"/>, so playback stays free of any notion of storage.
/// </summary>
public sealed class PreferenceTracker : IAsyncDisposable
{
    private readonly IPlayer _player;
    private readonly ISettingsStore<PlayerPreferences> _store;
    private readonly Func<DateTimeOffset> _now;
    private readonly Lock _gate = new();

    private PlayerPreferences _preferences = new();
    private readonly Dictionary<string, ResumePoint> _resumePoints = [];

    /// <summary>The source the last snapshot was about, used to spot media changes.</summary>
    private MediaSource? _currentSource;
    private TimeSpan _currentPosition;
    private TimeSpan _currentDuration;

    /// <summary>Sources already offered their saved position, so it is applied once only.</summary>
    private readonly HashSet<string> _resumed = [];

    private bool _restored;
    private bool _disposed;

    /// <param name="now">
    /// Clock used to date and expire resume points. Injectable so retention can be
    /// tested without waiting ninety days.
    /// </param>
    public PreferenceTracker(
        IPlayer player,
        ISettingsStore<PlayerPreferences> store,
        Func<DateTimeOffset>? now = null)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }



    /// <summary>
    /// Load the stored preferences and push them onto the player, then start tracking.
    /// Safe to call once; later calls are ignored.
    /// </summary>
    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (_restored)
            return;
        _restored = true;

        var loaded = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);

        var now = _now();

        lock (_gate)
        {
            _preferences = loaded;
            _resumePoints.Clear();

            // Stamp first, then expire: a file written before positions were dated must
            // keep them rather than have them all read as ninety days old.
            var dated = loaded.ResumePoints
                .Select(point => ResumePointRetention.StampIfUndated(point, now));

            foreach (var point in ResumePointRetention.Apply(dated, now))
                _resumePoints[point.Location] = point;
        }

        _player.SetVolume(Volume.Of(loaded.Volume));
        _player.SetMuted(loaded.IsMuted);
        _player.SetRepeat(loaded.Repeat);

        // Subscribed only after the restore, so applying the stored values does not
        // immediately read back as "the user changed something".
        _player.Changed += OnPlayerChanged;
    }

    private void OnPlayerChanged(object? sender, PlayerSnapshot snapshot)
    {
        MediaSource? resumeFor = null;
        var resumeTo = TimeSpan.Zero;

        lock (_gate)
        {
            var sourceChanged = !Equals(_currentSource, snapshot.Source);

            if (sourceChanged)
            {
                // Bank the outgoing file's position before losing sight of it.
                RecordResumePoint(_currentSource, _currentPosition, _currentDuration);
                _currentSource = snapshot.Source;

            }

            _currentPosition = snapshot.Position;
            _currentDuration = snapshot.Duration;

            _preferences = _preferences with
            {
                Volume = snapshot.Volume.Level,
                IsMuted = snapshot.IsMuted,
                Repeat = snapshot.Repeat
            };

            // Resume once the backend has actually opened the file — seeking earlier
            // would be discarded by the engine.
            if (snapshot.Source is { } source && snapshot.Status.IsLoaded())
            {
                var key = Key(source);
                if (_resumed.Add(key) &&
                    _resumePoints.TryGetValue(key, out var point) &&
                    point.IsWorthResuming)
                {
                    resumeFor = source;
                    resumeTo = point.Position;
                }
            }
        }


        if (resumeFor is not null)
        {
            // Hop off the callback: seeking re-enters the player, and this handler may
            // be running on the media backend's own thread.
            var target = resumeTo;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    _player.SeekTo(target);
                }
                catch (InvalidPlaybackTransitionException)
                {
                    // The media went away between the snapshot and this hop; nothing to resume.
                }
            });
        }
    }

    /// <summary>Record where a file got to, or forget it once it has been watched through.</summary>
    private void RecordResumePoint(MediaSource? source, TimeSpan position, TimeSpan duration)
    {
        if (source is null)
            return;

        var key = Key(source);
        var point = new ResumePoint(key, position, duration) { SavedAt = _now() };

        if (point.IsWorthResuming)
            _resumePoints[key] = point;
        else
            _resumePoints.Remove(key);
    }


    /// <summary>Write the current state out. Called on shutdown.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        PlayerPreferences toSave;
        lock (_gate)
        {
            RecordResumePoint(_currentSource, _currentPosition, _currentDuration);
            toSave = _preferences with
            {
                // Trimmed on the way out, so the file never grows past the working set
                // however long the app has been in use.
                ResumePoints = ResumePointRetention.Apply(_resumePoints.Values, _now())
            };
        }

        await _store.SaveAsync(toSave, cancellationToken).ConfigureAwait(false);
    }

    private static string Key(MediaSource source) => source.Location.ToString();

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        _player.Changed -= OnPlayerChanged;

        try
        {
            await FlushAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Failing to persist preferences must not stop the app from closing.
        }
    }
}
