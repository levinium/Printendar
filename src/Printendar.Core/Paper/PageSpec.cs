using SkiaSharp;

namespace Printendar.Core.Paper;

/// <summary>
/// A sheet of paper with an orientation and margins: the fixed box every layout is measured
/// against.
/// </summary>
/// <remarks>
/// This type is the reason "does it fit on one page" is answerable rather than hopeful. The
/// content box is known before a single event is looked at, so the layout engine is packing
/// into a known space rather than producing content and asking a paginating engine to cope.
/// </remarks>
public sealed record PageSpec(PaperSize Paper, Orientation Orientation, Margins Margins)
{
    /// <summary>
    /// The smallest content box worth trying to print a calendar into, in points.
    /// </summary>
    /// <remarks>
    /// One inch square. Well below anything usable, and chosen only to catch margins that
    /// have consumed the sheet, not to express a design opinion.
    /// </remarks>
    private const float MinimumUsableExtentPt = 72f;

    public float WidthPt => Orientation is Orientation.Landscape ? Paper.LongEdgePt : Paper.ShortEdgePt;

    public float HeightPt => Orientation is Orientation.Landscape ? Paper.ShortEdgePt : Paper.LongEdgePt;

    /// <summary>The whole sheet, origin at the top left corner.</summary>
    public SKRect PageRect => SKRect.Create(0f, 0f, WidthPt, HeightPt);

    /// <summary>The printable area once margins are taken off.</summary>
    public SKRect ContentBox => new(
        Margins.Left,
        Margins.Top,
        WidthPt - Margins.Right,
        HeightPt - Margins.Bottom);

    /// <summary>
    /// Throws if the margins have left nothing to print into.
    /// </summary>
    /// <exception cref="ArgumentException">The content box is degenerate or too small.</exception>
    public void Validate()
    {
        var box = ContentBox;

        if (box.Width < MinimumUsableExtentPt || box.Height < MinimumUsableExtentPt)
        {
            throw new ArgumentException(
                $"The margins leave a content box of {box.Width:0.#} by {box.Height:0.#} points on " +
                $"{Paper.DisplayName} in {Orientation.ToString().ToLowerInvariant()}, which is too small to " +
                $"print a calendar into. Reduce the margins or choose a larger paper size.",
                nameof(Margins));
        }
    }
}
