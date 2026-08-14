using Luma.Application;
using Luma.Domain.Media;
using Luma.Domain.Playback;
using Luma.Domain.Playlists;

namespace Luma.Presentation.Tests.Fakes;

/// <summary>
/// The least a view-model needs an <see cref="IPlayer"/> to be. Commands are accepted
/// and forgotten; what it does report is how many handlers are attached to
/// <see cref="Changed"/>, which is how the disposal tests catch a view-model that never
/// let go.
/// </summary>
public sealed class FakePlayer : IPlayer
{
    public PlayerSnapshot Snapshot { get; private set; } = Empty();
    public event EventHandler<PlayerSnapshot>? Changed;
    public bool LoadWholeFolder { get; set; } = true;

    /// <summary>How many handlers are attached.</summary>
    public int Subscribers => Changed?.GetInvocationList().Length ?? 0;

    public void Publish()
    {
        Snapshot = Empty();
        Changed?.Invoke(this, Snapshot);
    }

    private static PlayerSnapshot Empty() => new(
        PlaybackStatus.NoMedia, null, null, TimeSpan.Zero, TimeSpan.Zero,
        Volume.Default, false, PlaybackRate.Normal, null, 0, -1, [],
        RepeatMode.None, [], [], null, null);

    public Task OpenAsync(MediaSource source, CancellationToken ct = default) => Task.CompletedTask;
    public Task OpenAsync(IReadOnlyList<MediaSource> sources, CancellationToken ct = default) => Task.CompletedTask;
    public void Play() { }
    public void Pause() { }
    public void TogglePlayPause() { }
    public void Stop() { }
    public void SeekTo(TimeSpan position) { }
    public void SetVolume(Volume volume) { }
    public void SetMuted(bool muted) { }
    public void ToggleMute() { }
    public void SetRate(PlaybackRate rate) { }
    public void SetRepeat(RepeatMode mode) { }
    public void SelectAudioTrack(MediaTrack track) { }
    public void SelectSubtitleTrack(MediaTrack? track) { }
    public void AddSubtitleFile(MediaSource file) { }
    public Task NextAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task PreviousAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task EnqueueAsync(IReadOnlyList<MediaSource> sources, CancellationToken ct = default) => Task.CompletedTask;
    public Task PlayAtAsync(int index, CancellationToken ct = default) => Task.CompletedTask;
    public Task RemoveAtAsync(int index, CancellationToken ct = default) => Task.CompletedTask;
    public void ClearPlaylist() { }
}
