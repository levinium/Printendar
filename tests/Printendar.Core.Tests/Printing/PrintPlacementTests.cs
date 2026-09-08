using Printendar.Core.Printing;

namespace Printendar.Core.Tests.Printing;

/// <summary>
/// Where the laid-out sheet lands on the physical paper.
/// </summary>
/// <remarks>
/// This is the arithmetic that decides whether a printed calendar is the right size and in the
/// right place, and it is invisible until somebody holds a sheet of paper. Getting it wrong by
/// a quarter inch looks plausible on screen and wrong in the hand, so it is pinned here.
///
/// Two coordinate systems meet. The scene is in PDF points (1/72 inch) measured from the
/// corner of the physical sheet. Windows printing hands out a Graphics measured in hundredths
/// of an inch whose origin sits at the corner of the PRINTABLE area, which is inset from the
/// sheet by the printer's own unprintable margin. The offset between those two origins is the
/// whole problem.
/// </remarks>
public class PrintPlacementTests
{
    // Letter landscape: 11in x 8.5in.
    private const float LetterLandscapeWidthPt = 792f;
    private const float LetterLandscapeHeightPt = 612f;

    [Fact]
    public void The_sheet_is_placed_at_its_true_physical_size()
    {
        // Never scaled to fill the printable area. The layout already accounts for the
        // margin, so stretching it would silently change every measurement on the page.
        var placement = PrintPlacement.Compute(
            LetterLandscapeWidthPt,
            LetterLandscapeHeightPt,
            new PrintableArea(25f, 25f, 1050f, 800f));

        Assert.Equal(1100f, placement.Width, 3);
        Assert.Equal(850f, placement.Height, 3);
    }

    [Fact]
    public void The_sheet_origin_is_pulled_back_by_the_printers_unprintable_margin()
    {
        // The Graphics origin is the printable corner, but the scene's origin is the paper
        // corner. Without this negative offset every printed page drifts down and right by
        // the hard margin, which looks like a centring problem and is not one.
        var placement = PrintPlacement.Compute(
            LetterLandscapeWidthPt,
            LetterLandscapeHeightPt,
            new PrintableArea(25f, 25f, 1050f, 800f));

        Assert.Equal(-25f, placement.X, 3);
        Assert.Equal(-25f, placement.Y, 3);
    }

    [Fact]
    public void A_printer_with_no_unprintable_margin_places_the_sheet_at_the_origin()
    {
        var placement = PrintPlacement.Compute(
            LetterLandscapeWidthPt,
            LetterLandscapeHeightPt,
            new PrintableArea(0f, 0f, 1100f, 850f));

        Assert.Equal(0f, placement.X, 3);
        Assert.Equal(0f, placement.Y, 3);
    }

    [Theory]
    [InlineData(25f, 0.25f)]
    [InlineData(0f, 0f)]
    [InlineData(50f, 0.5f)]
    [InlineData(17f, 0.17f)]
    public void The_hard_margin_is_reported_in_inches(float offsetHundredths, float expectedInches)
    {
        // Reported so the app can say "your printer cannot print closer than 0.5 inch to the
        // edge, and your margin is 0.4 inch" BEFORE the paper is used, rather than after.
        // Letter landscape is 1100 x 850 hundredths, so an even margin on all four edges
        // leaves this much printable. Sizing it any other way leaves a deeper margin on the
        // right or bottom, and the reported figure would be that edge instead of this one.
        var placement = PrintPlacement.Compute(
            LetterLandscapeWidthPt,
            LetterLandscapeHeightPt,
            new PrintableArea(
                offsetHundredths,
                offsetHundredths,
                1100f - (2f * offsetHundredths),
                850f - (2f * offsetHundredths)));

        Assert.Equal(expectedInches, placement.HardMarginInches, 4);
    }

    [Fact]
    public void The_hard_margin_is_the_largest_of_the_four_edges()
    {
        // A printer whose bottom margin is deeper than its top still clips, so the warning
        // has to be driven by the worst edge rather than an average or the first one read.
        // Letter landscape is 1100 x 850 hundredths.
        var placement = PrintPlacement.Compute(
            LetterLandscapeWidthPt,
            LetterLandscapeHeightPt,
            // left 20, top 16, right 1100-(20+1060)=20, bottom 850-(16+770)=64
            new PrintableArea(20f, 16f, 1060f, 770f));

        Assert.Equal(0.64f, placement.HardMarginInches, 4);
    }

    [Fact]
    public void A_layout_margin_clear_of_the_hard_margin_does_not_warn()
    {
        var placement = PrintPlacement.Compute(
            LetterLandscapeWidthPt,
            LetterLandscapeHeightPt,
            new PrintableArea(25f, 25f, 1050f, 800f));

        Assert.False(placement.WouldClip(layoutMarginInches: 0.4f));
    }

    [Fact]
    public void A_layout_margin_inside_the_hard_margin_warns()
    {
        // The specific failure this exists to catch: a user drops the margin to 0.15in to fit
        // more on, and the printer silently eats the grid's outer border.
        var placement = PrintPlacement.Compute(
            LetterLandscapeWidthPt,
            LetterLandscapeHeightPt,
            new PrintableArea(25f, 25f, 1050f, 800f));

        Assert.True(placement.WouldClip(layoutMarginInches: 0.15f));
    }

    [Fact]
    public void A_margin_exactly_on_the_hard_margin_does_not_warn()
    {
        // Equal is fine: the content starts exactly where the printer starts printing.
        // Warning here would nag on the very common 0.25in printer.
        var placement = PrintPlacement.Compute(
            LetterLandscapeWidthPt,
            LetterLandscapeHeightPt,
            new PrintableArea(25f, 25f, 1050f, 800f));

        Assert.False(placement.WouldClip(layoutMarginInches: 0.25f));
    }

    [Fact]
    public void Portrait_pages_are_placed_the_same_way()
    {
        var placement = PrintPlacement.Compute(612f, 792f, new PrintableArea(25f, 25f, 800f, 1050f));

        Assert.Equal(850f, placement.Width, 3);
        Assert.Equal(1100f, placement.Height, 3);
    }

    [Fact]
    public void The_raster_size_matches_the_sheet_at_the_requested_resolution()
    {
        // Letter landscape at 300 DPI is 3300 x 2550 pixels.
        var (widthPx, heightPx) = PrintPlacement.RasterSize(
            LetterLandscapeWidthPt, LetterLandscapeHeightPt, dpi: 300);

        Assert.Equal(3300, widthPx);
        Assert.Equal(2550, heightPx);
    }

    [Fact]
    public void A_fractional_raster_size_rounds_up_rather_than_losing_an_edge()
    {
        // Rounding down would leave the last row or column of pixels unpainted, which shows
        // as a hairline of white along two edges of an otherwise correct page.
        var (widthPx, heightPx) = PrintPlacement.RasterSize(100.4f, 100.4f, dpi: 300);

        Assert.Equal(419, widthPx);
        Assert.Equal(419, heightPx);
    }

    [Fact]
    public void A_printable_area_reported_in_portrait_is_transposed_for_a_landscape_page()
    {
        // Windows reports PageSettings.PrintableArea in portrait whatever the orientation is
        // set to. Bounds swaps; PrintableArea does not.
        var reported = new PrintableArea(16.667f, 16.667f, 816.667f, 1066.667f);

        var normalized = PrintPlacement.NormalizeReportedArea(reported, landscape: true);

        Assert.Equal(1066.667f, normalized.Width, 2);
        Assert.Equal(816.667f, normalized.Height, 2);
    }

    [Fact]
    public void A_portrait_page_leaves_the_reported_area_alone()
    {
        var reported = new PrintableArea(16.667f, 16.667f, 816.667f, 1066.667f);

        var normalized = PrintPlacement.NormalizeReportedArea(reported, landscape: false);

        Assert.Equal(reported, normalized);
    }

    [Fact]
    public void A_real_laser_printer_on_letter_landscape_reports_its_true_hard_margin()
    {
        // The regression this exists for. These are the numbers an HP LaserJet Pro M402
        // actually reports for Letter, and they are identical in both orientations.
        //
        // Comparing that portrait rectangle against a landscape sheet gives
        // 1100 - (16.67 + 816.67) = 266.67 hundredths, and the app told the user their printer
        // could not print within 2.67 inches of the edge. No printer has such a margin, and
        // acting on it would have shrunk the calendar to a block in the middle of the sheet.
        var reported = new PrintableArea(16.667f, 16.667f, 816.667f, 1066.667f);

        var placement = PrintPlacement.Compute(
            LetterLandscapeWidthPt,
            LetterLandscapeHeightPt,
            PrintPlacement.NormalizeReportedArea(reported, landscape: true));

        Assert.Equal(0.1667f, placement.HardMarginInches, 3);
        Assert.False(placement.WouldClip(layoutMarginInches: 0.4f));
    }

    [Fact]
    public void The_default_margin_is_not_clipped_by_a_typical_laser_printer()
    {
        // Guards the default itself. 0.4in has to clear the hard margin of ordinary office
        // hardware, or every user meets a warning on their first print.
        var reported = new PrintableArea(16.667f, 16.667f, 816.667f, 1066.667f);

        var placement = PrintPlacement.Compute(
            LetterLandscapeWidthPt,
            LetterLandscapeHeightPt,
            PrintPlacement.NormalizeReportedArea(reported, landscape: true));

        Assert.False(placement.WouldClip(0.4f));
    }

    [Fact]
    public void An_unusable_printable_area_is_rejected_rather_than_dividing_by_zero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PrintPlacement.Compute(0f, 612f, new PrintableArea(25f, 25f, 1050f, 800f)));
    }
}
