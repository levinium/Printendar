using SkiaSharp;

namespace Printendar.Core.Tests;

/// <summary>
/// Proves the Skia native library actually loads and draws on this machine.
/// </summary>
/// <remarks>
/// Worth its own file because when native asset resolution fails (macOS arm64, a headless
/// Linux container with no libfontconfig, a self-contained publish missing a RID), every
/// layout test in the suite fails at once with a TypeInitializationException that says
/// nothing useful. This one fails first and says what is wrong.
/// </remarks>
public class SkiaSmokeTests
{
    [Fact]
    public void Skia_can_create_a_raster_surface_and_draw()
    {
        using var surface = SKSurface.Create(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul));

        Assert.NotNull(surface);

        surface.Canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
        surface.Canvas.DrawRect(SKRect.Create(0, 0, 2, 2), paint);

        using var image = surface.Snapshot();
        using var pixels = image.PeekPixels();

        Assert.Equal(SKColors.Black, pixels.GetPixelColor(0, 0));
        Assert.Equal(SKColors.White, pixels.GetPixelColor(3, 3));
    }

    [Fact]
    public void Skia_can_measure_text_with_a_typeface()
    {
        // Layout is arithmetic over measured text, so a measurer that silently returns zero
        // would produce a page that looks empty rather than one that fails loudly.
        using var typeface = SKTypeface.Default;
        using var font = new SKFont(typeface, 10f);

        var width = font.MeasureText("Board meeting");

        Assert.True(width > 0f, "Measuring text returned a non-positive width, so no font is usable here.");
    }

    [Fact]
    public void Skia_can_write_a_single_page_pdf()
    {
        // The whole export path rests on this. If SKDocument.CreatePdf is unavailable in the
        // packaged build, better to know in the smoke test than in the exporter.
        using var stream = new MemoryStream();

        using (var document = SKDocument.CreatePdf(stream))
        {
            Assert.NotNull(document);
            var canvas = document.BeginPage(612, 792);
            canvas.Clear(SKColors.White);
            document.EndPage();
            document.Close();
        }

        var bytes = stream.ToArray();

        Assert.True(bytes.Length > 0, "SKDocument produced no bytes.");
        Assert.Equal("%PDF"u8.ToArray(), bytes.Take(4).ToArray());
    }
}
