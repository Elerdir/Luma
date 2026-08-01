using Luma.Application.Tests.Fakes;
using Luma.Domain.Media;

namespace Luma.Application.Tests;

public class PlayerServiceSubtitleTests
{
    private static readonly TimeSpan Len = TimeSpan.FromMinutes(90);
    // Absolute on every platform; a "C:\..." literal is a relative path on Linux.
    private static MediaSource FileNamed(string name) =>
        MediaSource.FromFile(Path.Combine(Path.GetTempPath(), "luma", name));

    private static MediaSource Movie => FileNamed("movie.mkv");
    private static MediaSource Srt => FileNamed("movie.en.srt");

    [Fact]
    public async Task Sidecar_subtitles_are_attached_when_the_media_opens()
    {
        var engine = new FakeMediaEngine();
        var finder = new FakeSubtitleFinder(Srt);
        var player = new PlayerService(engine, finder);

        await player.OpenAsync(Movie);
        engine.RaiseOpened(Len);

        finder.Queries.ShouldHaveSingleItem().ShouldBe(Movie);
        var attached = engine.AddedSubtitles.ShouldHaveSingleItem();
        attached.File.ShouldBe(Srt);
        attached.Select.ShouldBeFalse(); // found, but not forced on the user
    }

    [Fact]
    public async Task Without_a_finder_nothing_is_attached_automatically()
    {
        var engine = new FakeMediaEngine();
        var player = new PlayerService(engine);

        await player.OpenAsync(Movie);
        engine.RaiseOpened(Len);

        engine.AddedSubtitles.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_manually_loaded_subtitle_is_selected()
    {
        var engine = new FakeMediaEngine();
        var player = new PlayerService(engine);
        await player.OpenAsync(Movie);
        engine.RaiseOpened(Len);

        player.AddSubtitleFile(Srt);

        var attached = engine.AddedSubtitles.ShouldHaveSingleItem();
        attached.File.ShouldBe(Srt);
        attached.Select.ShouldBeTrue();
    }

    [Fact]
    public void Adding_a_subtitle_without_media_is_rejected()
    {
        var engine = new FakeMediaEngine();
        var player = new PlayerService(engine);

        Should.Throw<InvalidOperationException>(() => player.AddSubtitleFile(Srt));
    }

    [Fact]
    public async Task A_late_arriving_stream_shows_up_in_the_snapshot()
    {
        var engine = new FakeMediaEngine();
        var player = new PlayerService(engine);
        var czech = MediaTrack.Audio(1, "Czech");
        await player.OpenAsync(Movie);
        engine.RaiseOpened(Len, [czech]);

        engine.RaiseTracksChanged([czech, MediaTrack.Subtitle(9, "movie.en.srt")]);

        player.Snapshot.SubtitleTracks.ShouldHaveSingleItem().Name.ShouldBe("movie.en.srt");
        player.Snapshot.SelectedAudioTrack.ShouldBe(czech);
    }

    [Fact]
    public void A_tracks_changed_event_with_no_media_loaded_is_ignored()
    {
        var engine = new FakeMediaEngine();
        var player = new PlayerService(engine);

        Should.NotThrow(() => engine.RaiseTracksChanged([MediaTrack.Subtitle(1, "stray")]));
        player.Snapshot.SubtitleTracks.ShouldBeEmpty();
    }
}
