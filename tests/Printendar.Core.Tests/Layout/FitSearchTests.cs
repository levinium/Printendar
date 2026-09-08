using Printendar.Core.Layout.Fit;

namespace Printendar.Core.Tests.Layout;

/// <summary>
/// Finding the largest typography scale at which the month still fits.
/// </summary>
/// <remarks>
/// The search runs over rung indices of a quantized ladder rather than over a continuous
/// float, for two reasons. A continuous bisection converges to a value that jitters with
/// floating point ordering, so the same month could lay out microscopically differently
/// between runs and every snapshot baseline would flap. And an eleven rung ladder settles in
/// four probes, where each probe costs a full re-wrap of the whole month.
///
/// Binary search is only valid because the predicate is monotone: if the content fits at some
/// scale, it fits at every smaller one. That is a property of the layout, not of this class,
/// and is asserted against the real engine in the month grid tests.
/// </remarks>
public class FitSearchTests
{
    private static readonly FontScaleLadder Ladder = FontScaleLadder.Default;

    /// <summary>A monotone predicate that fits at or below the given scale.</summary>
    private static Func<float, bool> FitsAtOrBelow(float threshold) => scale => scale <= threshold + 0.0001f;

    [Fact]
    public void The_default_ladder_descends_from_one()
    {
        Assert.Equal(1f, Ladder.Rungs[0]);

        for (var i = 1; i < Ladder.Rungs.Count; i++)
        {
            Assert.True(Ladder.Rungs[i] < Ladder.Rungs[i - 1], "Rungs must descend, or the search is searching backwards.");
        }
    }

    [Fact]
    public void Returns_the_top_rung_when_everything_fits_at_full_size()
    {
        var found = FitSearch.FindLargestFitting(Ladder, _ => true);

        Assert.Equal(0, found);
    }

    [Fact]
    public void Reports_no_fit_when_nothing_fits_even_at_the_smallest_rung()
    {
        var found = FitSearch.FindLargestFitting(Ladder, _ => false);

        Assert.Equal(FitSearch.NoFit, found);
    }

    [Fact]
    public void Finds_the_largest_rung_that_fits()
    {
        // For every rung, a predicate that fits exactly at and below it must return that rung.
        for (var expected = 0; expected < Ladder.Rungs.Count; expected++)
        {
            var found = FitSearch.FindLargestFitting(Ladder, FitsAtOrBelow(Ladder.Rungs[expected]));

            Assert.Equal(expected, found);
        }
    }

    [Fact]
    public void Probes_logarithmically_rather_than_walking_the_ladder()
    {
        // Each probe re-wraps every event in the month, so a linear walk would cost eleven
        // full layouts where four will do.
        var probes = 0;

        FitSearch.FindLargestFitting(Ladder, scale =>
        {
            probes++;
            return FitsAtOrBelow(0.8f)(scale);
        });

        Assert.True(probes <= 5, $"Took {probes} probes over a ladder of {Ladder.Rungs.Count} rungs.");
    }

    [Fact]
    public void Handles_a_single_rung_ladder()
    {
        var single = new FontScaleLadder([1f]);

        Assert.Equal(0, FitSearch.FindLargestFitting(single, _ => true));
        Assert.Equal(FitSearch.NoFit, FitSearch.FindLargestFitting(single, _ => false));
    }

    [Fact]
    public void A_ladder_must_descend()
    {
        // An ascending ladder would make the binary search return nonsense rather than fail,
        // so it is rejected at construction.
        Assert.Throws<ArgumentException>(() => new FontScaleLadder([0.6f, 1f]));
    }

    [Fact]
    public void A_ladder_must_have_at_least_one_rung()
    {
        Assert.Throws<ArgumentException>(() => new FontScaleLadder([]));
    }

    [Fact]
    public void Builds_a_ladder_between_a_floor_and_full_size()
    {
        var ladder = FontScaleLadder.Between(minScale: 0.6f, rungs: 11);

        Assert.Equal(11, ladder.Rungs.Count);
        Assert.Equal(1f, ladder.Rungs[0], 0.001f);
        Assert.Equal(0.6f, ladder.Rungs[^1], 0.001f);
    }
}
