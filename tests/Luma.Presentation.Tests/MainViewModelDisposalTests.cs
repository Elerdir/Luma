using Luma.Application;
using Luma.Application.Abstractions;
using Luma.Application.Updates;
using Luma.Domain.Media;
using Luma.Domain.Playback;
using Luma.Domain.Playlists;
using Luma.Presentation.Localization;
using Luma.Presentation.Services;
using Luma.Presentation.ViewModels;

namespace Luma.Presentation.Tests;

/// <summary>
/// Shares the localizer singleton with <see cref="LocalizerTests"/>, so it joins the
/// same non-parallel collection and puts the language back afterwards.
/// </summary>
[Collection(nameof(LocalizerTests))]
public sealed class MainViewModelDisposalTests : IDisposable
{
    private readonly string _originalLanguage = Localizer.Instance.CurrentLanguage;

    public void Dispose() => Localizer.Instance.SetLanguage(_originalLanguage);

    // ---- Minimal stand-ins. The view-model only needs these to exist here. ----

    private sealed class StubPlayer : IPlayer
    {
        public PlayerSnapshot Snapshot { get; private set; } = Empty();
        public event EventHandler<PlayerSnapshot>? Changed;
        public bool LoadWholeFolder { get; set; } = true;

        /// <summary>How many handlers are attached — the thing under test.</summary>
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

    private sealed class StubPicker : IFilePicker
    {
        public Task<IReadOnlyList<string>> PickVideosAsync() =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickSubtitleAsync() => Task.FromResult<string?>(null);
    }

    private sealed class StubUpdates : IUpdateService
    {
        public Task<AvailableUpdate?> CheckAsync(CancellationToken ct = default) =>
            Task.FromResult<AvailableUpdate?>(null);

        public Task<string> DownloadAsync(
            AvailableUpdate update, IProgress<double>? progress = null, CancellationToken ct = default) =>
            Task.FromResult("");
    }

    private sealed class StubLauncher : IInstallerLauncher
    {
        public void LaunchAndExit(string installerPath) { }
    }

    private sealed class StubStore : ISettingsStore<InterfaceOptions>
    {
        public Task<InterfaceOptions> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult(new InterfaceOptions());

        public Task SaveAsync(InterfaceOptions settings, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private static MainViewModel Create(StubPlayer player) =>
        new(player, new StubPicker(), new StubUpdates(), new StubLauncher(),
            new InterfaceOptionsService(new StubStore()));

    [Fact]
    public void Disposing_lets_go_of_the_player()
    {
        var player = new StubPlayer();
        var viewModel = Create(player);
        player.Subscribers.ShouldBe(1);

        viewModel.Dispose();

        player.Subscribers.ShouldBe(0);
    }

    [Fact]
    public void A_disposed_view_model_stops_reacting_to_the_language()
    {
        var player = new StubPlayer();
        var viewModel = Create(player);

        Localizer.Instance.SetLanguage("en");
        viewModel.Dispose();
        Localizer.Instance.SetLanguage("cs");

        // Still the English text it last built: the singleton no longer reaches it.
        viewModel.StatusText.ShouldBe("Ready");
    }

    [Fact]
    public void A_disposed_view_model_stops_reacting_to_the_player()
    {
        var player = new StubPlayer();
        var viewModel = Create(player);
        viewModel.Dispose();

        Should.NotThrow(player.Publish);
    }

    [Fact]
    public void Disposing_twice_is_harmless()
    {
        var player = new StubPlayer();
        var viewModel = Create(player);

        viewModel.Dispose();
        Should.NotThrow(viewModel.Dispose);
        player.Subscribers.ShouldBe(0);
    }
}
