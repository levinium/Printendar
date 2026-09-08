using Printendar.Core.Paper;

namespace Printendar.Core.Tests.Paper;

/// <summary>
/// The page spec turns a paper choice into the fixed box everything else is measured against.
/// </summary>
/// <remarks>
/// Every dimension in Core is in PDF points (1/72 inch). DPI exists only at the two edges,
/// the preview scale and the printer raster, so that layout arithmetic never has to know
/// which device it is destined for.
/// </remarks>
public class PageSpecTests
{
    private const float Tolerance = 0.01f;

    [Fact]
    public void Letter_portrait_is_612_by_792_points()
    {
        var page = new PageSpec(PaperSizes.Letter, Orientation.Portrait, Margins.Zero);

        Assert.Equal(612f, page.WidthPt, Tolerance);
        Assert.Equal(792f, page.HeightPt, Tolerance);
    }

    [Fact]
    public void Landscape_swaps_the_edges()
    {
        var page = new PageSpec(PaperSizes.Letter, Orientation.Landscape, Margins.Zero);

        Assert.Equal(792f, page.WidthPt, Tolerance);
        Assert.Equal(612f, page.HeightPt, Tolerance);
    }

    [Fact]
    public void A4_landscape_is_wider_than_it_is_tall()
    {
        var page = new PageSpec(PaperSizes.A4, Orientation.Landscape, Margins.Zero);

        Assert.Equal(841.89f, page.WidthPt, Tolerance);
        Assert.Equal(595.28f, page.HeightPt, Tolerance);
    }

    [Fact]
    public void Content_box_is_the_page_inset_by_the_margins()
    {
        // The headline number for this project: a Letter landscape sheet at the default
        // margin leaves 734.4 by 554.4 points to fit a month into.
        var page = new PageSpec(PaperSizes.Letter, Orientation.Landscape, Margins.FromInches(0.4f));

        var box = page.ContentBox;

        Assert.Equal(28.8f, box.Left, Tolerance);
        Assert.Equal(28.8f, box.Top, Tolerance);
        Assert.Equal(763.2f, box.Right, Tolerance);
        Assert.Equal(583.2f, box.Bottom, Tolerance);
        Assert.Equal(734.4f, box.Width, Tolerance);
        Assert.Equal(554.4f, box.Height, Tolerance);
    }

    [Fact]
    public void Asymmetric_margins_are_applied_to_the_correct_edges()
    {
        // Guards against the classic transposition, where left and top are correct and the
        // right and bottom insets are silently swapped.
        var page = new PageSpec(PaperSizes.Letter, Orientation.Portrait, new Margins(10f, 20f, 30f, 40f));

        var box = page.ContentBox;

        Assert.Equal(10f, box.Left, Tolerance);
        Assert.Equal(20f, box.Top, Tolerance);
        Assert.Equal(612f - 30f, box.Right, Tolerance);
        Assert.Equal(792f - 40f, box.Bottom, Tolerance);
    }

    [Fact]
    public void Rejects_margins_that_leave_no_usable_page()
    {
        // A month needs room for a header band, a weekday row and at least four week rows.
        // Failing here is far better than emitting a page whose cells are a point tall.
        var page = new PageSpec(PaperSizes.Letter, Orientation.Landscape, Margins.FromInches(4.5f));

        var error = Assert.Throws<ArgumentException>(page.Validate);

        Assert.Contains("margin", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Accepts_a_page_with_room_to_print()
    {
        var page = new PageSpec(PaperSizes.Letter, Orientation.Landscape, Margins.FromInches(0.4f));

        page.Validate();
    }
}
