using Luma.Application;
using Luma.Application.Tests.Fakes;
using Luma.Domain.Media;
using Luma.Domain.Playback;
using Luma.Domain.Playlists;

namespace Luma.Application.Tests;

public class PlayerServiceTests
{
    // Built from the temp directory rather than a "C:\..." literal: on Linux that
    // literal is not an absolute path, so it gets resolved against the working
    // directory and DisplayName then returns the whole mangled string.
    private static MediaSource File(string name) =>
        MediaSource.FromFile(Path.Combine(Path.GetTempPath(), "luma", $"{name}.mp4"));
    private static readonly TimeSpan Len = TimeSpan.FromMinutes(3);

    private static (PlayerService player, FakeMediaEngine engine) Create()
    {
        var engine = new FakeMediaEngine();
        return (new PlayerService(engine), engine);
    }

    [Fact]
    public async Task Open_enters_loading_then_opened_starts_playing()
    {
        var (player, engine) = Create();

        await player.OpenAsync(File("a"));
        player.Snapshot.Status.ShouldBe(PlaybackStatus.Loading);
        engine.Opens.Count.ShouldBe(1);

        engine.RaiseOpened(Len);

        player.Snapshot.Status.ShouldBe(PlaybackStatus.Playing);
        player.Snapshot.Duration.ShouldBe(Len);
        engine.PlayCount.ShouldBe(1);
    }

    [Fact]
    public async Task Open_raises_changed_events()
    {
        var (player, engine) = Create();
        var states = new List<PlaybackStatus>();
        player.Changed += (_, s) => states.Add(s.Status);

        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len);

        states.ShouldContain(PlaybackStatus.Loading);
        states.ShouldContain(PlaybackStatus.Playing);
    }

    [Fact]
    public async Task Pause_and_play_forward_to_engine()
    {
        var (player, engine) = Create();
        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len);

        player.Pause();
        player.Snapshot.Status.ShouldBe(PlaybackStatus.Paused);
        engine.PauseCount.ShouldBe(1);

        player.Play();
        player.Snapshot.Status.ShouldBe(PlaybackStatus.Playing);
        engine.PlayCount.ShouldBe(2); // once on open, once here
    }

    [Fact]
    public async Task Position_reports_flow_into_snapshot()
    {
        var (player, engine) = Create();
        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len);

        engine.RaisePosition(TimeSpan.FromMinutes(1));

        player.Snapshot.Position.ShouldBe(TimeSpan.FromMinutes(1));
        player.Snapshot.Progress.ShouldBe(1d / 3d, 0.001);
    }

    [Fact]
    public async Task Seek_forwards_clamped_position_to_engine()
    {
        var (player, engine) = Create();
        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len);

        player.SeekTo(Len + TimeSpan.FromMinutes(10));

        engine.LastSeek.ShouldBe(Len); // clamped to duration
        player.Snapshot.Position.ShouldBe(Len);
    }

    [Fact]
    public async Task End_of_single_item_without_repeat_stops_at_ended()
    {
        var (player, engine) = Create();
        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len);

        engine.RaiseEnd();

        player.Snapshot.Status.ShouldBe(PlaybackStatus.Ended);
        engine.Opens.Count.ShouldBe(1); // no advance
    }

    [Fact]
    public async Task End_advances_to_next_playlist_item()
    {
        var (player, engine) = Create();
        await player.OpenAsync([File("a"), File("b")]);
        engine.RaiseOpened(Len);

        engine.RaiseEnd();

        engine.Opens.Count.ShouldBe(2);
        engine.Opens[1].DisplayName.ShouldBe("b.mp4");
        player.Snapshot.Status.ShouldBe(PlaybackStatus.Loading);
        player.Snapshot.PlaylistIndex.ShouldBe(1);
    }

    [Fact]
    public async Task Repeat_one_replays_same_item_on_end()
    {
        var (player, engine) = Create();
        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len);
        player.SetRepeat(RepeatMode.One);

        engine.RaiseEnd();

        engine.Opens.Count.ShouldBe(2);
        engine.Opens[1].DisplayName.ShouldBe("a.mp4");
    }

    [Fact]
    public async Task Failure_faults_with_message()
    {
        var (player, engine) = Create();
        await player.OpenAsync(File("a"));

        engine.RaiseFailed("no codec");

        player.Snapshot.Status.ShouldBe(PlaybackStatus.Faulted);
        player.Snapshot.FaultMessage.ShouldBe("no codec");
    }

    [Fact]
    public async Task Volume_and_rate_are_reapplied_when_media_opens()
    {
        var (player, engine) = Create();
        player.SetVolume(Volume.Of(40));
        player.SetRate(PlaybackRate.Of(1.5));

        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len);

        engine.LastVolume!.Value.Level.ShouldBe(40);
        engine.LastRate!.Value.Multiplier.ShouldBe(1.5);
    }

    [Fact]
    public async Task Stop_unloads_and_forwards_to_engine()
    {
        var (player, engine) = Create();
        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len);

        player.Stop();

        player.Snapshot.Status.ShouldBe(PlaybackStatus.NoMedia);
        engine.StopCount.ShouldBe(1);
    }

    [Fact]
    public async Task Opening_publishes_tracks_and_applies_default_selection()
    {
        var (player, engine) = Create();
        var cz = MediaTrack.Audio(0, "Czech");
        var en = MediaTrack.Audio(1, "English");
        var subs = MediaTrack.Subtitle(2, "Czech subs");

        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len, [cz, en, subs]);

        var snap = player.Snapshot;
        snap.AudioTracks.ShouldBe([cz, en]);
        snap.SubtitleTracks.ShouldBe([subs]);
        snap.SelectedAudioTrack.ShouldBe(cz);
        snap.SelectedSubtitleTrack.ShouldBeNull();

        engine.LastAudioTrack.ShouldBe(cz);       // default pushed to backend
        engine.LastSubtitleTrack.ShouldBeNull();  // subtitles explicitly off
    }

    [Fact]
    public async Task Selecting_audio_track_forwards_to_engine()
    {
        var (player, engine) = Create();
        var cz = MediaTrack.Audio(0, "Czech");
        var en = MediaTrack.Audio(1, "English");

        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len, [cz, en]);

        player.SelectAudioTrack(en);

        player.Snapshot.SelectedAudioTrack.ShouldBe(en);
        engine.LastAudioTrack.ShouldBe(en);
    }

    [Fact]
    public async Task Subtitles_can_be_enabled_then_turned_off()
    {
        var (player, engine) = Create();
        var subs = MediaTrack.Subtitle(2, "Czech subs");

        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len, [subs]);

        player.SelectSubtitleTrack(subs);
        player.Snapshot.SelectedSubtitleTrack.ShouldBe(subs);
        engine.LastSubtitleTrack.ShouldBe(subs);

        player.SelectSubtitleTrack(null);
        player.Snapshot.SelectedSubtitleTrack.ShouldBeNull();
        engine.LastSubtitleTrack.ShouldBeNull();
    }

    [Fact]
    public async Task Selecting_unavailable_track_is_rejected_before_reaching_engine()
    {
        var (player, engine) = Create();
        var cz = MediaTrack.Audio(0, "Czech");

        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len, [cz]);
        var pushedBefore = engine.LastAudioTrack;

        Should.Throw<ArgumentException>(() => player.SelectAudioTrack(MediaTrack.Audio(42, "Ghost")));

        engine.LastAudioTrack.ShouldBe(pushedBefore); // engine untouched
    }

    [Fact]
    public async Task Dispose_unsubscribes_and_disposes_engine()
    {
        var (player, engine) = Create();
        await player.DisposeAsync();
        engine.Disposed.ShouldBeTrue();
    }
}
