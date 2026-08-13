using Luma.Application.Preferences;

namespace Luma.Application.Tests;

public class ResumePointRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    private static ResumePoint Point(string name, TimeSpan age) =>
        new($"file:///x/{name}.mkv", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(60))
        {
            SavedAt = Now - age
        };

    [Fact]
    public void Recent_positions_are_kept()
    {
        var kept = ResumePointRetention.Apply(
            [Point("a", TimeSpan.FromDays(1)), Point("b", TimeSpan.FromDays(30))], Now);

        kept.Count.ShouldBe(2);
    }

    [Fact]
    public void Positions_older_than_the_limit_are_dropped()
    {
        var kept = ResumePointRetention.Apply(
            [Point("fresh", TimeSpan.FromDays(1)), Point("stale", TimeSpan.FromDays(91))], Now);

        kept.ShouldHaveSingleItem().Location.ShouldEndWith("fresh.mkv");
    }

    [Fact]
    public void No_more_than_the_maximum_survives()
    {
        var many = Enumerable
            .Range(0, ResumePointRetention.MaxEntries + 25)
            .Select(i => Point($"ep{i}", TimeSpan.FromMinutes(i)));

        ResumePointRetention.Apply(many, Now).Count.ShouldBe(ResumePointRetention.MaxEntries);
    }

    [Fact]
    public void The_newest_are_the_ones_that_survive()
    {
        var many = Enumerable
            .Range(0, ResumePointRetention.MaxEntries + 5)
            .Select(i => Point($"ep{i}", TimeSpan.FromMinutes(i)));

        var kept = ResumePointRetention.Apply(many, Now);

        // ep0 is the most recent; the five oldest fall off the end.
        kept[0].Location.ShouldEndWith("ep0.mkv");
        kept.ShouldNotContain(p => p.Location.EndsWith("ep54.mkv"));
    }

    [Fact]
    public void An_undated_position_is_stamped_rather_than_expired()
    {
        var legacy = new ResumePoint("file:///x/old.mkv", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(90));

        var stamped = ResumePointRetention.StampIfUndated(legacy, Now);

        stamped.SavedAt.ShouldBe(Now);
        ResumePointRetention.Apply([stamped], Now).ShouldHaveSingleItem();
    }

    [Fact]
    public void A_dated_position_keeps_its_own_timestamp()
    {
        var dated = Point("a", TimeSpan.FromDays(2));

        ResumePointRetention.StampIfUndated(dated, Now).SavedAt.ShouldBe(Now - TimeSpan.FromDays(2));
    }

    [Fact]
    public void An_empty_set_stays_empty()
    {
        ResumePointRetention.Apply([], Now).ShouldBeEmpty();
    }
}
