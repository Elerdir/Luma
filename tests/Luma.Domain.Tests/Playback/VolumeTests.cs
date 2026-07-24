using Luma.Domain.Playback;

namespace Luma.Domain.Tests.Playback;

public class VolumeTests
{
    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(55, 55)]
    [InlineData(100, 100)]
    [InlineData(150, 100)]
    public void Of_clamps_into_valid_range(int input, int expected)
    {
        Volume.Of(input).Level.ShouldBe(expected);
    }

    [Fact]
    public void Muted_is_zero_and_reports_muted()
    {
        Volume.Muted.Level.ShouldBe(0);
        Volume.Muted.IsMuted.ShouldBeTrue();
    }

    [Fact]
    public void Increase_and_decrease_stay_clamped()
    {
        Volume.Of(95).Increase(20).Level.ShouldBe(100);
        Volume.Of(5).Decrease(20).Level.ShouldBe(0);
    }

    [Fact]
    public void Equal_levels_are_equal_values()
    {
        Volume.Of(50).ShouldBe(Volume.Of(50));
    }
}
