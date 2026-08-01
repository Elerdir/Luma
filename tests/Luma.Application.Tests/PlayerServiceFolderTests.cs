using Luma.Application.Tests.Fakes;
using Luma.Domain.Media;

namespace Luma.Application.Tests;

/// <summary>
/// Opening one file of a series should leave the rest of the folder one press of
/// Next away.
/// </summary>
public class PlayerServiceFolderTests
{
    // Absolute on every platform; a "C:\..." literal is a relative path on Linux.
    private static MediaSource FileNamed(string name) =>
        MediaSource.FromFile(Path.Combine(Path.GetTempPath(), "luma", name));

    private static readonly MediaSource Episode1 = FileNamed("ep1.mkv");
    private static readonly MediaSource Episode2 = FileNamed("ep2.mkv");
    private static readonly MediaSource Episode3 = FileNamed("ep3.mkv");

    private static PlayerService PlayerWithFolder(FakeMediaEngine engine) =>
        new(engine, subtitleFinder: null,
            folderScanner: new FakeMediaFolderScanner(Episode1, Episode2, Episode3));

    [Fact]
    public async Task Opening_one_file_loads_the_whole_folder()
    {
        var engine = new FakeMediaEngine();
        var player = PlayerWithFolder(engine);

        await player.OpenAsync(Episode2);

        player.Snapshot.PlaylistItems.ShouldBe([Episode1, Episode2, Episode3]);
    }

    [Fact]
    public async Task The_file_that_was_opened_is_the_one_that_plays()
    {
        var engine = new FakeMediaEngine();
        var player = PlayerWithFolder(engine);

        await player.OpenAsync(Episode2);

        player.Snapshot.PlaylistIndex.ShouldBe(1);
        player.Snapshot.Source.ShouldBe(Episode2);
        engine.Opens.ShouldBe([Episode2]);
    }

    [Fact]
    public async Task Next_and_previous_step_through_the_folder()
    {
        var engine = new FakeMediaEngine();
        var player = PlayerWithFolder(engine);
        await player.OpenAsync(Episode2);

        await player.NextAsync();
        player.Snapshot.Source.ShouldBe(Episode3);

        await player.PreviousAsync();
        await player.PreviousAsync();
        player.Snapshot.Source.ShouldBe(Episode1);
    }

    [Fact]
    public async Task Both_directions_are_offered_from_the_middle_of_a_folder()
    {
        var engine = new FakeMediaEngine();
        var player = PlayerWithFolder(engine);

        await player.OpenAsync(Episode2);

        player.Snapshot.CanGoNext.ShouldBeTrue();
        player.Snapshot.CanGoPrevious.ShouldBeTrue();
    }

    [Fact]
    public async Task Picking_several_files_is_taken_literally()
    {
        var engine = new FakeMediaEngine();
        var player = PlayerWithFolder(engine);

        await player.OpenAsync([Episode3, Episode1]);

        // An explicit selection is a choice about what to play, and in what order.
        player.Snapshot.PlaylistItems.ShouldBe([Episode3, Episode1]);
        player.Snapshot.Source.ShouldBe(Episode3);
    }

    [Fact]
    public async Task A_file_the_scan_does_not_cover_still_plays_on_its_own()
    {
        var engine = new FakeMediaEngine();
        // The folder came back without the file that was opened — unreadable, or an
        // extension the scanner does not list.
        var player = new PlayerService(engine, subtitleFinder: null,
            folderScanner: new FakeMediaFolderScanner(Episode1, Episode3));

        await player.OpenAsync(FileNamed("home-video.mkv"));

        player.Snapshot.PlaylistItems.ShouldHaveSingleItem()
            .ShouldBe(FileNamed("home-video.mkv"));
    }

    [Fact]
    public async Task Turning_the_folder_off_makes_a_single_file_open_alone()
    {
        var engine = new FakeMediaEngine();
        var scanner = new FakeMediaFolderScanner(Episode1, Episode2, Episode3);
        var player = new PlayerService(engine, subtitleFinder: null, folderScanner: scanner)
        {
            LoadWholeFolder = false
        };

        await player.OpenAsync(Episode2);

        player.Snapshot.PlaylistItems.ShouldHaveSingleItem().ShouldBe(Episode2);
        player.Snapshot.CanGoNext.ShouldBeFalse();
        scanner.Queries.ShouldBeEmpty(); // not even asked
    }

    [Fact]
    public async Task Turning_the_folder_back_on_applies_to_the_next_open()
    {
        var engine = new FakeMediaEngine();
        var player = PlayerWithFolder(engine);
        player.LoadWholeFolder = false;
        await player.OpenAsync(Episode2);

        player.LoadWholeFolder = true;
        await player.OpenAsync(Episode1);

        player.Snapshot.PlaylistItems.ShouldBe([Episode1, Episode2, Episode3]);
    }

    [Fact]
    public async Task The_folder_is_loaded_unless_someone_asks_otherwise()
    {
        var engine = new FakeMediaEngine();
        var player = PlayerWithFolder(engine);

        player.LoadWholeFolder.ShouldBeTrue();

        await player.OpenAsync(Episode2);
        player.Snapshot.PlaylistCount.ShouldBe(3);
    }

    [Fact]
    public async Task Without_a_scanner_a_single_file_stays_a_playlist_of_one()
    {
        var engine = new FakeMediaEngine();
        var player = new PlayerService(engine);

        await player.OpenAsync(Episode2);

        player.Snapshot.PlaylistItems.ShouldHaveSingleItem().ShouldBe(Episode2);
    }

    [Fact]
    public async Task A_stream_is_asked_about_but_never_expanded()
    {
        var engine = new FakeMediaEngine();
        // A real scanner returns nothing for a stream; make sure that is handled here
        // rather than relying on the adapter to be the only guard.
        var scanner = new FakeMediaFolderScanner();
        var player = new PlayerService(engine, subtitleFinder: null, folderScanner: scanner);
        var stream = MediaSource.FromUri(new Uri("http://example.test/live.m3u8"));

        await player.OpenAsync(stream);

        scanner.Queries.ShouldHaveSingleItem().ShouldBe(stream);
        player.Snapshot.PlaylistItems.ShouldHaveSingleItem().ShouldBe(stream);
    }
}
