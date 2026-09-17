using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Luma.Application;
using Luma.Domain.Media;
using Luma.Domain.Playback;
using Luma.Domain.Playlists;
using Luma.Presentation.Localization;
using Luma.Presentation.Services;
using Luma.Presentation.Tests.Fakes;
using Luma.Presentation.ViewModels;

namespace Luma.Presentation.Tests;

/// <summary>
/// <see cref="MainViewModel.OnPlayerChanged"/> does not apply a snapshot directly — it
/// posts to <see cref="Dispatcher.UIThread"/>, because the engine callback that produced
/// it is not on the UI thread. A plain xUnit test has no dispatcher pumping that post, so
/// <c>Should.NotThrow(player.Publish)</c> elsewhere in this project proves only that
/// <c>Post</c> itself does not throw — the queued update might as well not exist. These
/// run under <c>[AvaloniaFact]</c>, a real (headless) Avalonia application, so the post
/// actually runs once <see cref="Dispatcher.UIThread.RunJobs()"/> flushes it, and what
/// reaches the bound properties can finally be checked.
/// </summary>
[Collection(nameof(LocalizerTests))]
public sealed class MainViewModelDispatchTests : IDisposable
{
    private readonly string _originalLanguage = Localizer.Instance.CurrentLanguage;

    public MainViewModelDispatchTests() => Localizer.Instance.SetLanguage("en");

    public void Dispose() => Localizer.Instance.SetLanguage(_originalLanguage);

    private static readonly MediaSource Movie =
        MediaSource.FromFile(Path.Combine(Path.GetTempPath(), "luma", "movie.mkv"));

    private static (MainViewModel ViewModel, FakePlayer Player) Create()
    {
        var player = new FakePlayer();
        var viewModel = new MainViewModel(
            player, new FakeFilePicker(), new FakeUpdateService(), new FakeInstallerLauncher(),
            new InterfaceOptionsService(new FakeSettingsStore<InterfaceOptions>()),
            new FakePlaylistFileStore());
        return (viewModel, player);
    }

    private static PlayerSnapshot Playing(
        TimeSpan position, TimeSpan duration,
        IReadOnlyList<MediaSource>? playlist = null, int playlistIndex = 0,
        IReadOnlyList<MediaTrack>? audioTracks = null, MediaTrack? selectedAudio = null,
        IReadOnlyList<MediaTrack>? subtitleTracks = null, MediaTrack? selectedSubtitle = null) => new(
        PlaybackStatus.Playing, Movie, Movie.DisplayName, position, duration,
        Volume.Of(65), false, PlaybackRate.Normal, null,
        playlist?.Count ?? 1, playlistIndex, playlist ?? [Movie],
        RepeatMode.None,
        audioTracks ?? [], subtitleTracks ?? [],
        selectedAudio, selectedSubtitle);

    [AvaloniaFact]
    public void Publishing_a_snapshot_updates_bound_state_through_the_dispatcher()
    {
        var (viewModel, player) = Create();

        player.Publish(Playing(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)));
        Dispatcher.UIThread.RunJobs();

        viewModel.HasMedia.ShouldBeTrue();
        viewModel.IsPlaying.ShouldBeTrue();
        viewModel.MediaName.ShouldBe("movie.mkv");
        viewModel.PositionSeconds.ShouldBe(30);
        viewModel.DurationSeconds.ShouldBe(120);
        viewModel.PositionText.ShouldBe("00:30");
        viewModel.DurationText.ShouldBe("02:00");
        viewModel.StatusText.ShouldBe("Playing");

        // The transport buttons are bound to these — the point of posting through the
        // dispatcher in the first place is that they light up without anyone polling.
        viewModel.CanPlayPause.ShouldBeTrue();
        viewModel.CanSeek.ShouldBeTrue();
        viewModel.CanStop.ShouldBeTrue();
        viewModel.PlayPauseCommand.CanExecute(null).ShouldBeTrue();
        viewModel.StopCommand.CanExecute(null).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Playlist_entries_and_the_current_item_marker_arrive_through_the_dispatcher()
    {
        var (viewModel, player) = Create();
        var second = MediaSource.FromFile(Path.Combine(Path.GetTempPath(), "luma", "ep2.mkv"));
        var playlist = new[] { Movie, second };

        player.Publish(Playing(TimeSpan.Zero, TimeSpan.FromMinutes(10), playlist, playlistIndex: 1));
        Dispatcher.UIThread.RunJobs();

        viewModel.Playlist.Count.ShouldBe(2);
        viewModel.Playlist[0].IsCurrent.ShouldBeFalse();
        viewModel.Playlist[1].IsCurrent.ShouldBeTrue();
        viewModel.CanGoPrevious.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Audio_and_subtitle_dropdowns_arrive_through_the_dispatcher()
    {
        var (viewModel, player) = Create();
        var english = MediaTrack.Audio(1, "English");
        var czech = MediaTrack.Subtitle(2, "Czech");

        player.Publish(Playing(
            TimeSpan.Zero, TimeSpan.FromMinutes(10),
            audioTracks: [english], selectedAudio: english,
            subtitleTracks: [czech], selectedSubtitle: czech));
        Dispatcher.UIThread.RunJobs();

        viewModel.AudioTracks.ShouldHaveSingleItem().ShouldBe(english);
        viewModel.SelectedAudioTrack.ShouldBe(english);

        // "Subtitles off" is a sentinel the view-model prepends itself, not something
        // the backend reports.
        viewModel.SubtitleOptions.Count.ShouldBe(2);
        viewModel.SubtitleOptions.ShouldContain(czech);
        viewModel.SelectedSubtitle.ShouldBe(czech);
    }

    /// <summary>
    /// Two snapshots posted back-to-back queue two dispatcher jobs. Flushing once must
    /// leave the view-model reflecting the second, not some mix of both — the guard
    /// against a seek/volume echo (<c>_applyingSnapshot</c>) must not itself get stuck
    /// set after the first job and skip applying the second.
    /// </summary>
    [AvaloniaFact]
    public void Back_to_back_snapshots_converge_on_the_latest_after_one_flush()
    {
        var (viewModel, player) = Create();

        player.Publish(Playing(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(2)));
        player.Publish(Playing(TimeSpan.FromSeconds(90), TimeSpan.FromMinutes(2)));
        Dispatcher.UIThread.RunJobs();

        viewModel.PositionSeconds.ShouldBe(90);
        viewModel.PositionText.ShouldBe("01:30");

        // The guard must have been released again, or a real seek right after would be
        // silently swallowed.
        viewModel.PositionSeconds = 5;
        player.SeekCalls.ShouldHaveSingleItem().ShouldBe(TimeSpan.FromSeconds(5));
    }
}
