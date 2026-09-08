namespace Printendar.Core.Layout.Fit;

/// <summary>
/// A descending, quantized set of typography scales.
/// </summary>
/// <remarks>
/// Quantized rather than continuous so that layout is reproducible byte for byte. A continuous
/// bisection converges to a value that shifts with floating point ordering, which would make
/// the same month lay out microscopically differently between runs and every snapshot baseline
/// flap for no reason.
/// </remarks>
public sealed class FontScaleLadder
{
    public FontScaleLadder(IReadOnlyList<float> rungs)
    {
        ArgumentNullException.ThrowIfNull(rungs);

        if (rungs.Count == 0)
        {
            throw new ArgumentException("A scale ladder needs at least one rung.", nameof(rungs));
        }

        for (var i = 1; i < rungs.Count; i++)
        {
            if (rungs[i] >= rungs[i - 1])
            {
                throw new ArgumentException(
                    $"Scale ladder rungs must descend, but rung {i} ({rungs[i]}) is not smaller than " +
                    $"rung {i - 1} ({rungs[i - 1]}). The search assumes index 0 is the largest scale.",
                    nameof(rungs));
            }
        }

        Rungs = rungs;
    }

    /// <summary>Scales from largest to smallest. Index 0 is full size.</summary>
    public IReadOnlyList<float> Rungs { get; }

    /// <summary>
    /// Eleven rungs from full size down to 0.6.
    /// </summary>
    /// <remarks>
    /// Eleven settles in four probes. Below 0.6 of the base sizes the text stops being
    /// readable at arm's length, which is the point of printing the thing.
    /// </remarks>
    public static FontScaleLadder Default { get; } = Between(minScale: 0.6f, rungs: 11);

    public static FontScaleLadder Between(float minScale, int rungs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rungs, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minScale, 1f);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(minScale, 0f);

        // A floor of exactly 1 means "do not shrink at all", which is a legitimate request and
        // is how a caller pins the layout to full size. It collapses to a single rung, because
        // spreading eleven rungs between 1 and 1 would produce duplicates and a ladder must
        // strictly descend.
        if (rungs == 1 || minScale >= 1f)
        {
            return new FontScaleLadder([1f]);
        }

        var step = (1f - minScale) / (rungs - 1);

        return new FontScaleLadder([.. Enumerable.Range(0, rungs).Select(i => 1f - (i * step))]);
    }
}

/// <summary>
/// Finds the largest scale at which content still fits.
/// </summary>
/// <remarks>
/// Correct only while the predicate is monotone: if it fits at some scale, it must fit at
/// every smaller one. That holds because cell widths are independent of scale while text
/// advances are proportional to it, so shrinking can never increase the wrapped line count.
///
/// It stops holding the moment a padding, gap or minimum height is expressed in fixed points
/// while fonts scale. Everything in <see cref="ChipStyle.Scaled"/> therefore scales together,
/// and a test walks the whole ladder on dense input to catch anyone who later adds a fixed
/// dimension.
/// </remarks>
public static class FitSearch
{
    /// <summary>Returned when the content does not fit even at the smallest rung.</summary>
    public const int NoFit = -1;

    /// <summary>
    /// The index of the largest rung at which <paramref name="fits"/> is true, or
    /// <see cref="NoFit"/>.
    /// </summary>
    public static int FindLargestFitting(FontScaleLadder ladder, Func<float, bool> fits)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentNullException.ThrowIfNull(fits);

        var low = 0;
        var high = ladder.Rungs.Count - 1;
        var found = NoFit;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);

            if (fits(ladder.Rungs[mid]))
            {
                // This rung works; a larger one might too, so keep looking upwards.
                found = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        return found;
    }
}
