using Luma.Presentation.Services;

namespace Luma.Presentation.Tests;

/// <summary>
/// Startup restores volume, repeat mode and the resume point before it opens anything.
/// A file asked for before that has finished has to wait, or the film starts at the
/// wrong volume and from the beginning — which is exactly what a double-clicked film
/// on macOS does, because Finder's Apple Event can arrive before the window exists.
/// </summary>
public class FileOpenQueueTests
{
    private readonly List<IReadOnlyList<string>> _opened = [];

    private FileOpenQueue Queue() => new(paths =>
    {
        _opened.Add(paths);
        return Task.CompletedTask;
    });

    [Fact]
    public async Task A_file_offered_before_the_player_is_ready_waits()
    {
        var queue = Queue();

        await queue.OfferAsync(["/films/dune.mkv"]);

        _opened.ShouldBeEmpty();

        await queue.ReleaseAsync();

        _opened.ShouldHaveSingleItem().ShouldBe(["/films/dune.mkv"]);
    }

    [Fact]
    public async Task A_file_offered_afterwards_opens_straight_away()
    {
        var queue = Queue();
        await queue.ReleaseAsync();

        await queue.OfferAsync(["/films/dune.mkv"]);

        _opened.ShouldHaveSingleItem().ShouldBe(["/films/dune.mkv"]);
    }

    [Fact]
    public async Task Releasing_with_nothing_waiting_opens_nothing()
    {
        await Queue().ReleaseAsync();

        _opened.ShouldBeEmpty();
    }

    [Fact]
    public async Task Nothing_is_offered_twice()
    {
        var queue = Queue();
        await queue.OfferAsync(["/films/dune.mkv"]);

        await queue.ReleaseAsync();
        await queue.ReleaseAsync();

        _opened.ShouldHaveSingleItem();
    }

    /// <summary>
    /// Two requests before the window is up means the second one is what the user is
    /// waiting to watch — opening both would start playback on whichever the loop
    /// reached last while the playlist showed the other.
    /// </summary>
    [Fact]
    public async Task The_last_request_before_release_is_the_one_that_opens()
    {
        var queue = Queue();

        await queue.OfferAsync(["/films/dune.mkv"]);
        await queue.OfferAsync(["/films/arrival.mkv"]);
        await queue.ReleaseAsync();

        _opened.ShouldHaveSingleItem().ShouldBe(["/films/arrival.mkv"]);
    }

    /// <summary>
    /// An empty offer is what an activation carrying nothing Luma can open looks like —
    /// an iCloud placeholder, a file inside an archive. It must not count as the request
    /// that replaces a real one.
    /// </summary>
    [Fact]
    public async Task An_empty_request_is_ignored_rather_than_remembered()
    {
        var queue = Queue();

        await queue.OfferAsync(["/films/dune.mkv"]);
        await queue.OfferAsync([]);
        await queue.ReleaseAsync();

        _opened.ShouldHaveSingleItem().ShouldBe(["/films/dune.mkv"]);
    }

    [Fact]
    public async Task Every_request_after_release_opens()
    {
        var queue = Queue();
        await queue.ReleaseAsync();

        await queue.OfferAsync(["/films/dune.mkv"]);
        await queue.OfferAsync(["/films/arrival.mkv"]);

        _opened.Count.ShouldBe(2);
        _opened[1].ShouldBe(["/films/arrival.mkv"]);
    }
}
