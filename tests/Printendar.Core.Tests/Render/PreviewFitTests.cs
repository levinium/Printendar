using Printendar.Core.Render;

namespace Printendar.Core.Tests.Render;

/// <summary>
/// Fitting a page into the preview viewport.
/// </summary>
/// <remarks>
/// These exist because the first version of this arithmetic lived inside the Avalonia control
/// and applied the zoom twice, once while measuring and again while drawing. At the default
/// zoom of 1 that is invisible, so it survived a clean build and a passing suite, and only
/// showed up as a page drawn wider than its own window.
/// </remarks>
public class PreviewFitTests
{
    private const float Tolerance = 0.01f;

    // Letter landscape.
    private const float PageWidth = 792f;
    private const float PageHeight = 612f;

    [Fact]
    public void Scales_the_page_down_to_fit_a_smaller_viewport()
    {
        var placement = PreviewFit.Place(PageWidth, PageHeight, viewportWidth: 396f, viewportHeight: 306f);

        Assert.Equal(0.5f, placement.Scale, Tolerance);
        Assert.Equal(396f, placement.Width, Tolerance);
        Assert.Equal(306f, placement.Height, Tolerance);
    }

    [Fact]
    public void Never_draws_the_page_larger_than_the_viewport_at_default_zoom()
    {
        // The defect that prompted this file. Every viewport shape must contain the sheet.
        foreach (var width in (float[])[200f, 640f, 975f, 1255f, 4000f])
        {
            foreach (var height in (float[])[150f, 480f, 700f, 897f, 3000f])
            {
                var placement = PreviewFit.Place(PageWidth, PageHeight, width, height);

                Assert.True(
                    placement.Width <= width + Tolerance && placement.Height <= height + Tolerance,
                    $"A {width} by {height} viewport drew a {placement.Width:0.#} by {placement.Height:0.#} sheet.");
            }
        }
    }

    [Fact]
    public void Fits_to_whichever_edge_binds_first()
    {
        // A wide, short viewport is limited by its height, not its width.
        var wide = PreviewFit.Place(PageWidth, PageHeight, viewportWidth: 4000f, viewportHeight: 306f);

        Assert.Equal(0.5f, wide.Scale, Tolerance);

        var tall = PreviewFit.Place(PageWidth, PageHeight, viewportWidth: 396f, viewportHeight: 4000f);

        Assert.Equal(0.5f, tall.Scale, Tolerance);
    }

    [Fact]
    public void Centres_the_sheet_in_the_viewport()
    {
        var placement = PreviewFit.Place(PageWidth, PageHeight, viewportWidth: 1000f, viewportHeight: 306f);

        Assert.Equal((1000f - placement.Width) / 2f, placement.OffsetX, Tolerance);
        Assert.Equal((306f - placement.Height) / 2f, placement.OffsetY, Tolerance);
    }

    [Fact]
    public void Applies_the_zoom_exactly_once()
    {
        // Applying it in both the measure and the render passes squares it, which at zoom 1
        // is invisible and at zoom 2 quadruples the page.
        var unzoomed = PreviewFit.Place(PageWidth, PageHeight, 792f, 612f);
        var zoomed = PreviewFit.Place(PageWidth, PageHeight, 792f, 612f, zoom: 2f);

        Assert.Equal(unzoomed.Scale * 2f, zoomed.Scale, Tolerance);
    }

    [Fact]
    public void A_zoomed_page_may_extend_past_the_viewport()
    {
        // Zooming in is a request to see detail, so the sheet is allowed to overflow and the
        // host scrolls. The centring offsets go negative rather than clamping to zero, or the
        // viewer would only ever see the top left corner.
        var placement = PreviewFit.Place(PageWidth, PageHeight, 792f, 612f, zoom: 2f);

        Assert.True(placement.Width > 792f);
        Assert.True(placement.OffsetX < 0f);
    }

    [Fact]
    public void An_unmeasured_viewport_places_nothing()
    {
        // Avalonia measures with infinity before it knows the window size, and a NaN scale
        // makes Skia draw nothing at all, silently.
        Assert.Equal(default, PreviewFit.Place(PageWidth, PageHeight, float.PositiveInfinity, 612f));
        Assert.Equal(default, PreviewFit.Place(PageWidth, PageHeight, 0f, 612f));
        Assert.Equal(default, PreviewFit.Place(PageWidth, PageHeight, -10f, 612f));
    }
}
