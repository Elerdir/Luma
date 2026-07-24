using Luma.Domain.Media;
using Luma.Domain.Playlists;

namespace Luma.Domain.Tests.Playlists;

public class PlaylistTests
{
    private static MediaSource File(string name) => MediaSource.FromFile($@"C:\v\{name}.mp4");

    private static Playlist WithThree()
    {
        var p = new Playlist();
        p.AddRange([File("a"), File("b"), File("c")]);
        return p;
    }

    [Fact]
    public void New_playlist_is_empty_with_no_current()
    {
        var p = new Playlist();
        p.IsEmpty.ShouldBeTrue();
        p.CurrentIndex.ShouldBe(-1);
        p.Current.ShouldBeNull();
    }

    [Fact]
    public void First_added_item_becomes_current()
    {
        var p = new Playlist();
        p.Add(File("a"));
        p.CurrentIndex.ShouldBe(0);
        p.Current.ShouldBe(File("a"));
    }

    [Fact]
    public void MoveNext_advances_and_stops_at_end_when_no_repeat()
    {
        var p = WithThree();
        p.MoveNext().ShouldBeTrue();
        p.MoveNext().ShouldBeTrue();
        p.CurrentIndex.ShouldBe(2);
        p.MoveNext().ShouldBeFalse();
        p.CurrentIndex.ShouldBe(2);
    }

    [Fact]
    public void MoveNext_wraps_when_repeat_all()
    {
        var p = WithThree();
        p.Repeat = RepeatMode.All;
        p.JumpTo(2);
        p.MoveNext().ShouldBeTrue();
        p.CurrentIndex.ShouldBe(0);
    }

    [Fact]
    public void MoveNext_stays_put_when_repeat_one()
    {
        var p = WithThree();
        p.Repeat = RepeatMode.One;
        p.MoveNext().ShouldBeTrue();
        p.CurrentIndex.ShouldBe(0);
    }

    [Fact]
    public void MovePrevious_wraps_when_repeat_all()
    {
        var p = WithThree();
        p.Repeat = RepeatMode.All;
        p.MovePrevious().ShouldBeTrue();
        p.CurrentIndex.ShouldBe(2);
    }

    [Fact]
    public void Removing_item_before_current_shifts_current_left()
    {
        var p = WithThree();
        p.JumpTo(2);
        p.RemoveAt(0);
        p.CurrentIndex.ShouldBe(1);
        p.Current.ShouldBe(File("c"));
    }

    [Fact]
    public void Removing_current_last_item_clamps_current()
    {
        var p = WithThree();
        p.JumpTo(2);
        p.RemoveAt(2);
        p.CurrentIndex.ShouldBe(1);
    }

    [Fact]
    public void Removing_last_remaining_item_clears_current()
    {
        var p = new Playlist();
        p.Add(File("only"));
        p.RemoveAt(0);
        p.IsEmpty.ShouldBeTrue();
        p.CurrentIndex.ShouldBe(-1);
    }

    [Fact]
    public void JumpTo_out_of_range_throws()
    {
        var p = WithThree();
        Should.Throw<ArgumentOutOfRangeException>(() => p.JumpTo(5));
    }

    [Fact]
    public void MoveNext_on_empty_returns_false()
    {
        new Playlist().MoveNext().ShouldBeFalse();
    }
}
