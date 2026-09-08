using Printendar.Core.Scene;
using Printendar.Core.Text;
using SkiaSharp;

namespace Printendar.Core.Render;

/// <summary>
/// Rasterizes a page at a given resolution.
/// </summary>
/// <remarks>
/// Used for golden test images, thumbnails, the CLI's visual check, and the Windows print
/// path, which has no vector route to a printer.
///
/// It draws through <see cref="SceneRenderer"/> like everything else. That shared call is what
/// makes a golden image evidence about the printed page rather than about a second, parallel
/// renderer that happens to look similar.
/// </remarks>
public static class RasterRenderer
{
    private const float PointsPerInch = 72f;

    /// <summary>
    /// Renders the page to a bitmap. The caller owns the result.
    /// </summary>
    public static SKBitmap Render(ScenePage page, float dpi, RenderOptions options, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dpi, 0f);

        var scale = dpi / PointsPerInch;
        var width = (int)Math.Ceiling(page.Page.WidthPt * scale);
        var height = (int)Math.Ceiling(page.Page.HeightPt * scale);

        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var canvas = new SKCanvas(bitmap);

        // Scale first, then draw in points. The renderer never learns about DPI, which is what
        // keeps a 96 DPI preview and a 600 DPI print placing content identically.
        canvas.Scale(scale);
        SceneRenderer.Draw(canvas, page, options, measurer);

        return bitmap;
    }

    public static void WritePng(
        ScenePage page,
        float dpi,
        RenderOptions options,
        ITextMeasurer measurer,
        Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);

        using var bitmap = Render(page, dpi, options, measurer);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        data.SaveTo(output);
    }

    public static void WritePngToFile(
        ScenePage page,
        float dpi,
        RenderOptions options,
        ITextMeasurer measurer,
        string path)
    {
        using var stream = File.Create(path);
        WritePng(page, dpi, options, measurer, stream);
    }
}
