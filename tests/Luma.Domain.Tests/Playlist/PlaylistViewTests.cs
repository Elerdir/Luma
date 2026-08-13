using Luma.Domain.Media;
using Luma.Domain.Playlists;

namespace Luma.Domain.Tests.Playlists;

/// <summary>
/// The read-model handed to the UI is rebuilt on every position tick, so
/// <see cref="Playlist.Items"/> is cached and reused. These pin
/// down what the rest of the code now relies on: the same instance while nothing
/// changes, a new one the moment something does.
/// </summary>
public sealed class PlaylistViewTests
{
    private static MediaSource File(string name) =>
        MediaSource.FromFile(Path.Combine(Path.GetTempPath(), "luma", name));

    private static Playlist WithItems(params string[] names)
    {
        var playlist = new Playlist();
        playlist.AddRange(names.Select(File));
        return playlist;
    }

    [Fact]
    public void Reading_twice_without_a_change_gives_the_same_instance()
    {
        var playlist = WithItems("a.mkv", "b.mkv");

        ReferenceEquals(playlist.Items, playlist.Items).ShouldBeTrue();
    }

    [Fact]
    public void Moving_through_the_list_does_not_invalidate_it()
    {
        var playlist = WithItems("a.mkv", "b.mkv");
        var before = playlist.Items;

        playlist.MoveNext();
        playlist.JumpTo(0);

        // Navigation changes which entry is current, not what the entries are.
        ReferenceEquals(before, playlist.Items).ShouldBeTrue();
    }

    [Fact]
    public void Adding_gives_a_fresh_instance()
    {
        var playlist = WithItems("a.mkv");
        var before = playlist.Items;

        playlist.Add(File("b.mkv"));

        ReferenceEquals(before, playlist.Items).ShouldBeFalse();
        playlist.Items.Count.ShouldBe(2);
    }

    [Fact]
    public void Removing_gives_a_fresh_instance()
    {
        var playlist = WithItems("a.mkv", "b.mkv");
        var before = playlist.Items;

        playlist.RemoveAt(0);

        ReferenceEquals(before, playlist.Items).ShouldBeFalse();
        playlist.Items.ShouldHaveSingleItem();
    }

    [Fact]
    public void Clearing_gives_a_fresh_instance()
    {
        var playlist = WithItems("a.mkv");
        var before = playlist.Items;

        playlist.Clear();

        ReferenceEquals(before, playlist.Items).ShouldBeFalse();
        playlist.Items.ShouldBeEmpty();
    }

    /// <summary>
    /// A snapshot handed out earlier must keep showing what the playlist held then —
    /// callers store it and compare against the next one.
    /// </summary>
    [Fact]
    public void An_earlier_snapshot_does_not_change_underneath_its_holder()
    {
        var playlist = WithItems("a.mkv");
        var before = playlist.Items;

        playlist.Add(File("b.mkv"));

        before.ShouldHaveSingleItem();
    }
}
