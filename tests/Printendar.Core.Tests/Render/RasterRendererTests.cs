using Printendar.Core.Paper;
using Printendar.Core.Render;
using Printendar.Core.Scene;
using Printendar.Core.Text;
using SkiaSharp;

namespace Printendar.Core.Tests.Render;

/// <summary>
/// Rasterizes a page, for golden tests, thumbnails, and the Windows print path.
/// </summary>
/// <remarks>
/// Goes through the same <see cref="SceneRenderer"/> as the PDF exporter and the preview. That
/// shared call is the parity guarantee: a golden image is evidence about the printed page only
/// because nothing else draws it.
/// </remarks>
public class RasterRendererTests : IDisposable
{
    private readonly SkiaTextMeasurer _measurer = SkiaTextMeasurer.CreateWithEmbeddedFont();

    public void Dispose()
    {
        _measurer.Dispose();
        GC.SuppressFinalize(this);
    }

    private static readonly PageSpec LetterLandscape =
        new(PaperSizes.Letter, Orientation.Landscape, Margins.FromInches(0.4f));

    private static ScenePage PageWith(params SceneNode[] children) =>
        new(LetterLandscape, new SceneGroup("root", children, Clip: null), LayoutDiagnostics.Empty);

    [Fact]
    public void Sizes_the_image_from_the_paper_and_the_requested_resolution()
    {
        // 792 by 612 points at 96 DPI is 1056 by 816 pixels.
        using var bitmap = RasterRenderer.Render(PageWith(), dpi: 96f, RenderOptions.ForPrint, _measurer);

        Assert.Equal(1056, bitmap.Width);
        Assert.Equal(816, bitmap.Height);
    }

    [Fact]
    public void Scales_content_with_the_resolution_rather_than_cropping_it()
    {
        // A rectangle covering the left half of the sheet must still cover the left half at
        // any DPI. Getting this wrong crops the page instead of resampling it.
        var page = PageWith(new SceneRect(new SKRect(0f, 0f, 396f, 612f), SKColors.Black, null, 0f));

        using var low = RasterRenderer.Render(page, dpi: 72f, RenderOptions.ForPrint, _measurer);
        using var high = RasterRenderer.Render(page, dpi: 144f, RenderOptions.ForPrint, _measurer);

        Assert.Equal(SKColors.Black, low.GetPixel(low.Width / 4, low.Height / 2));
        Assert.Equal(SKColors.White, low.GetPixel(low.Width * 3 / 4, low.Height / 2));
        Assert.Equal(SKColors.Black, high.GetPixel(high.Width / 4, high.Height / 2));
        Assert.Equal(SKColors.White, high.GetPixel(high.Width * 3 / 4, high.Height / 2));
    }

    [Fact]
    public void Writes_a_png()
    {
        using var stream = new MemoryStream();

        RasterRenderer.WritePng(PageWith(), dpi: 96f, RenderOptions.ForPrint, _measurer, stream);

        var bytes = stream.ToArray();

        Assert.True(bytes.Length > 0);
        Assert.Equal<byte[]>([0x89, (byte)'P', (byte)'N', (byte)'G'], bytes.Take(4).ToArray());
    }

    [Fact]
    public void Rejects_a_resolution_that_would_produce_no_pixels()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RasterRenderer.Render(PageWith(), dpi: 0f, RenderOptions.ForPrint, _measurer));
    }
}
