using Luma.Domain.Media;

namespace Luma.Domain.Tests.Media;

public sealed class NaturalNameComparerTests
{
    private static IReadOnlyList<string> Sorted(params string[] names) =>
        [.. names.Order(NaturalNameComparer.Instance)];

    [Fact]
    public void Numbers_are_compared_as_numbers_not_as_text()
    {
        Sorted("ep10.mkv", "ep2.mkv", "ep1.mkv")
            .ShouldBe(["ep1.mkv", "ep2.mkv", "ep10.mkv"]);
    }

    [Fact]
    public void Episodes_of_a_series_land_in_broadcast_order()
    {
        Sorted(
            "Show.S01E10.mkv", "Show.S01E02.mkv", "Show.S02E01.mkv", "Show.S01E09.mkv")
            .ShouldBe([
                "Show.S01E02.mkv", "Show.S01E09.mkv", "Show.S01E10.mkv", "Show.S02E01.mkv"
            ]);
    }

    [Fact]
    public void Leading_zeros_do_not_change_the_number()
    {
        NaturalNameComparer.Instance.Compare("ep007.mkv", "ep7.mkv").ShouldNotBe(0);
        Sorted("ep007.mkv", "ep8.mkv", "ep06.mkv")
            .ShouldBe(["ep06.mkv", "ep007.mkv", "ep8.mkv"]);
    }

    [Fact]
    public void Case_does_not_decide_the_order()
    {
        Sorted("Beta.mkv", "alpha.mkv").ShouldBe(["alpha.mkv", "Beta.mkv"]);
    }

    [Fact]
    public void A_prefix_sorts_before_the_longer_name()
    {
        Sorted("clipextended.mkv", "clip.mkv").ShouldBe(["clip.mkv", "clipextended.mkv"]);
    }

    [Fact]
    public void Names_with_several_numbers_compare_left_to_right()
    {
        Sorted("2x10.mkv", "10x2.mkv", "2x9.mkv")
            .ShouldBe(["2x9.mkv", "2x10.mkv", "10x2.mkv"]);
    }

    [Fact]
    public void Equal_names_compare_equal()
    {
        NaturalNameComparer.Instance.Compare("ep1.mkv", "ep1.mkv").ShouldBe(0);
    }

    /// <summary>
    /// Sorting requires a total order: two names that only ever tie would let the same
    /// folder come back in a different order from run to run.
    /// </summary>
    [Fact]
    public void Only_identical_names_tie()
    {
        NaturalNameComparer.Instance.Compare("Ep1.mkv", "ep1.mkv").ShouldNotBe(0);
    }

    [Fact]
    public void Nulls_sort_first_and_do_not_throw()
    {
        NaturalNameComparer.Instance.Compare(null, "a").ShouldBeLessThan(0);
        NaturalNameComparer.Instance.Compare("a", null).ShouldBeGreaterThan(0);
        NaturalNameComparer.Instance.Compare(null, null).ShouldBe(0);
    }
}
