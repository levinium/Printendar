namespace Printendar.Core.Printing;

/// <summary>
/// The area of a sheet a printer can actually put ink on, in hundredths of an inch.
/// </summary>
/// <param name="X">Left edge of the printable area, measured from the paper's left edge.</param>
/// <param name="Y">Top edge of the printable area, measured from the paper's top edge.</param>
/// <remarks>
/// Hundredths of an inch because that is the unit Windows printing reports and draws in, and
/// converting once at the boundary beats converting at every call site.
/// </remarks>
public readonly record struct PrintableArea(float X, float Y, float Width, float Height);

/// <summary>
/// Where a laid-out page lands on physical paper, and whether the printer will clip it.
/// </summary>
/// <param name="X">Left edge of the sheet, relative to the printable area's origin.</param>
/// <param name="Y">Top edge of the sheet, relative to the printable area's origin.</param>
/// <param name="HardMarginInches">The deepest unprintable edge on this printer.</param>
public readonly record struct SheetPlacement(
    float X,
    float Y,
    float Width,
    float Height,
    float HardMarginInches)
{
    /// <summary>
    /// Whether the printer's unprintable margin eats into the laid-out margin.
    /// </summary>
    /// <remarks>
    /// Equal margins do not clip: the content begins exactly where the printer begins
    /// printing. Warning on equality would nag on the very common quarter-inch printer.
    /// </remarks>
    public bool WouldClip(float layoutMarginInches) => layoutMarginInches < HardMarginInches;
}

/// <summary>
/// Maps a page in PDF points onto a printer's coordinate space.
/// </summary>
/// <remarks>
/// Two origins meet here and they are not the same point. The scene is measured from the
/// corner of the physical sheet; a Windows printing Graphics is measured from the corner of the
/// PRINTABLE area, inset by the printer's own unprintable margin. Everything in this class
/// exists to carry that difference correctly.
///
/// The page is never scaled to fill the printable area. The layout already accounts for the
/// margin the user chose, so stretching it would silently change every measurement on the
/// page, which is precisely the failure that makes other tools' output untrustworthy.
/// </remarks>
public static class PrintPlacement
{
    private const float PointsPerInch = 72f;
    private const float HundredthsPerInch = 100f;

    /// <summary>Converts PDF points to the hundredths of an inch printing works in.</summary>
    public static float PointsToHundredths(float points) => points / PointsPerInch * HundredthsPerInch;

    public static SheetPlacement Compute(float pageWidthPt, float pageHeightPt, PrintableArea printable)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageWidthPt);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageHeightPt);

        var sheetWidth = PointsToHundredths(pageWidthPt);
        var sheetHeight = PointsToHundredths(pageHeightPt);

        // The right and bottom margins are what is left of the sheet once the printable area
        // is placed, so they need the sheet dimensions rather than the printable ones.
        var right = sheetWidth - (printable.X + printable.Width);
        var bottom = sheetHeight - (printable.Y + printable.Height);

        // The worst edge decides. A printer with a deep bottom margin still clips even when
        // its top margin is generous, so an average or a first-edge reading would miss it.
        var worst = MathF.Max(
            MathF.Max(printable.X, printable.Y),
            MathF.Max(MathF.Max(right, bottom), 0f));

        return new SheetPlacement(
            X: -printable.X,
            Y: -printable.Y,
            Width: sheetWidth,
            Height: sheetHeight,
            HardMarginInches: worst / HundredthsPerInch);
    }

    /// <summary>
    /// Pixel dimensions for rasterising the sheet at a given resolution.
    /// </summary>
    /// <remarks>
    /// Rounds up. Rounding down leaves the final row and column of pixels unpainted, which
    /// prints as a hairline of white along two edges of an otherwise correct page.
    /// </remarks>
    public static (int Width, int Height) RasterSize(float pageWidthPt, float pageHeightPt, int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageWidthPt);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageHeightPt);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);

        return (
            (int)MathF.Ceiling(pageWidthPt / PointsPerInch * dpi),
            (int)MathF.Ceiling(pageHeightPt / PointsPerInch * dpi));
    }
}
