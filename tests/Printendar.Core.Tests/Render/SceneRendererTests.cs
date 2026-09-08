using Printendar.Core.Paper;
using Printendar.Core.Render;
using Printendar.Core.Scene;
using Printendar.Core.Text;
using SkiaSharp;

namespace Printendar.Core.Tests.Render;

/// <summary>
/// The one drawing path, shared by the preview, the PDF and the printer.
/// </summary>
/// <remarks>
/// The renderer makes no decisions: no measuring, no wrapping, no font substitution. These
/// tests exist mostly to pin that it draws where the scene said, and that it clips text to the
/// width layout promised even when layout is wrong. That clip is the last line of defence
/// against a title written across the neighbouring day.
/// </remarks>
public class SceneRendererTests
{
    private static readonly PageSpec Page =
        new(PaperSizes.Letter, Orientation.Landscape, Margins.FromInches(0.4f));

    private static ScenePage PageWith(params SceneNode[] children) =>
        new(Page, new SceneGroup("root", children, Clip: null), LayoutDiagnostics.Empty);

    /// <summary>Renders at one pixel per point so pixel coordinates equal page coordinates.</summary>
    private static SKBitmap RenderToBitmap(ScenePage page)
    {
        var bitmap = new SKBitmap(
            (int)Math.Ceiling(page.Page.WidthPt),
            (int)Math.Ceiling(page.Page.HeightPt),
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        using var canvas = new SKCanvas(bitmap);
        using var measurer = SkiaTextMeasurer.CreateWithEmbeddedFont();
        SceneRenderer.Draw(canvas, page, RenderOptions.ForPrint, measurer);

        return bitmap;
    }

    [Fact]
    public void Fills_a_rectangle_where_the_scene_placed_it()
    {
        var page = PageWith(new SceneRect(new SKRect(100f, 100f, 200f, 200f), SKColors.Black, null, 0f));

        using var bitmap = RenderToBitmap(page);

        Assert.Equal(SKColors.Black, bitmap.GetPixel(150, 150));
        Assert.Equal(SKColors.White, bitmap.GetPixel(50, 50));
    }

    [Fact]
    public void Paints_the_background_across_the_whole_sheet()
    {
        // A transparent background is invisible on screen against a white preview and then
        // prints as nothing, so the page is painted explicitly.
        using var bitmap = RenderToBitmap(PageWith());

        Assert.Equal(SKColors.White, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.White, bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1));
    }

    [Fact]
    public void Applies_a_group_clip_to_its_children()
    {
        var page = new ScenePage(
            Page,
            new SceneGroup("root",
            [
                new SceneGroup(
                    "clipped",
                    [new SceneRect(new SKRect(100f, 100f, 300f, 300f), SKColors.Black, null, 0f)],
                    Clip: new SKRect(100f, 100f, 200f, 200f)),
            ], Clip: null),
            LayoutDiagnostics.Empty);

        using var bitmap = RenderToBitmap(page);

        Assert.Equal(SKColors.Black, bitmap.GetPixel(150, 150));
        Assert.Equal(SKColors.White, bitmap.GetPixel(250, 250));
    }

    [Fact]
    public void Clips_a_text_run_to_the_width_layout_promised()
    {
        // Deliberately a lie: the run claims to fit 20 points and does not. Layout should
        // never emit this, and the validator would catch it, but the renderer must still not
        // let the text escape into the next cell.
        var font = new FontSpec(FontWeightKind.Regular, 24f);
        var page = PageWith(new SceneTextRun(
            "WIDE TEXT THAT OVERFLOWS",
            new SKPoint(100f, 200f),
            font,
            SKColors.Black,
            TextAnchor.Left,
            MaxWidth: 20f));

        using var bitmap = RenderToBitmap(page);

        var inkFarRight = false;

        for (var y = 150; y < 220; y++)
        {
            for (var x = 130; x < 400; x++)
            {
                if (bitmap.GetPixel(x, y) != SKColors.White)
                {
                    inkFarRight = true;
                }
            }
        }

        Assert.False(inkFarRight, "Text was drawn beyond the MaxWidth the scene declared.");
    }

    [Fact]
    public void Draws_text_that_honours_its_declared_width()
    {
        var page = PageWith(new SceneTextRun(
            "Board",
            new SKPoint(100f, 200f),
            new FontSpec(FontWeightKind.Regular, 24f),
            SKColors.Black,
            TextAnchor.Left,
            MaxWidth: 200f));

        using var bitmap = RenderToBitmap(page);

        var ink = 0;

        for (var y = 170; y < 210; y++)
        {
            for (var x = 100; x < 300; x++)
            {
                if (bitmap.GetPixel(x, y) != SKColors.White)
                {
                    ink++;
                }
            }
        }

        Assert.True(ink > 0, "Nothing was drawn for a text run that fits.");
    }
}
