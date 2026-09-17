using Luma.Domain.Media;
using Luma.Domain.Playlists;

namespace Luma.Domain.Tests.Playlists;

/// <summary>
/// <see cref="Playlist.Move"/> — reordering entries without disturbing which one is
/// actually playing, tracked by position rather than by comparing entries so a
/// playlist with the same file listed twice still keeps the right one current.
/// </summary>
public sealed class PlaylistMoveTests
{
    private static MediaSource File(string name) =>
        MediaSource.FromFile(Path.Combine(Path.GetTempPath(), "luma", $"{name}.mp4"));

    private static Playlist WithFour()
    {
        var p = new Playlist();
        p.AddRange([File("a"), File("b"), File("c"), File("d")]);
        return p;
    }

    [Fact]
    public void Move_forward_reorders_the_entries()
    {
        var p = WithFour();
        p.Move(0, 2); // a,b,c,d -> b,c,a,d

        p.Items.Select(i => i.DisplayName).ShouldBe(["b.mp4", "c.mp4", "a.mp4", "d.mp4"]);
    }

    [Fact]
    public void Move_backward_reorders_the_entries()
    {
        var p = WithFour();
        p.Move(3, 1); // a,b,c,d -> a,d,b,c

        p.Items.Select(i => i.DisplayName).ShouldBe(["a.mp4", "d.mp4", "b.mp4", "c.mp4"]);
    }

    [Fact]
    public void Moving_an_item_to_its_own_position_does_nothing()
    {
        var p = WithFour();
        p.Move(1, 1);

        p.Items.Select(i => i.DisplayName).ShouldBe(["a.mp4", "b.mp4", "c.mp4", "d.mp4"]);
    }

    [Fact]
    public void Moving_the_current_item_keeps_it_current()
    {
        var p = WithFour();
        p.JumpTo(0); // "a" is current
        p.Move(0, 3); // a,b,c,d -> b,c,d,a

        p.Current.ShouldBe(File("a"));
        p.CurrentIndex.ShouldBe(3);
    }

    [Fact]
    public void Moving_an_item_forward_past_the_current_one_shifts_it_left()
    {
        var p = WithFour();
        p.JumpTo(1); // "b" is current

        p.Move(0, 2); // a,b,c,d -> b,c,a,d — "b" slides from index 1 to 0

        p.Current.ShouldBe(File("b"));
        p.CurrentIndex.ShouldBe(0);
    }

    [Fact]
    public void Moving_an_item_backward_past_the_current_one_shifts_it_right()
    {
        var p = WithFour();
        p.JumpTo(1); // "b" is current

        p.Move(3, 0); // a,b,c,d -> d,a,b,c — "b" slides from index 1 to 2

        p.Current.ShouldBe(File("b"));
        p.CurrentIndex.ShouldBe(2);
    }

    [Fact]
    public void Moving_entries_that_do_not_involve_the_current_one_leaves_it_untouched()
    {
        var p = WithFour();
        p.JumpTo(0); // "a" is current

        p.Move(2, 3); // a,b,c,d -> a,b,d,c — none of that touches index 0

        p.Current.ShouldBe(File("a"));
        p.CurrentIndex.ShouldBe(0);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(4, 0)]
    [InlineData(0, 4)]
    public void Move_rejects_indexes_outside_the_list(int from, int to)
    {
        var p = WithFour();
        Should.Throw<ArgumentOutOfRangeException>(() => p.Move(from, to));
    }
}
