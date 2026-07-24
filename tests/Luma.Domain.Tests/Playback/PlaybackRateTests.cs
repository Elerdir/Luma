using Luma.Domain.Playback;

namespace Luma.Domain.Tests.Playback;

public class PlaybackRateTests
{
    [Theory]
    [InlineData(0.1, 0.25)]
    [InlineData(0.25, 0.25)]
    [InlineData(1.0, 1.0)]
    [InlineData(4.0, 4.0)]
    [InlineData(10.0, 4.0)]
    public void Of_clamps_into_valid_range(double input, double expected)
    {
        PlaybackRate.Of(input).Multiplier.ShouldBe(expected);
    }

    [Fact]
    public void Normal_is_one_and_reports_normal()
    {
        PlaybackRate.Normal.Multiplier.ShouldBe(1.0);
        PlaybackRate.Normal.IsNormal.ShouldBeTrue();
    }
}
