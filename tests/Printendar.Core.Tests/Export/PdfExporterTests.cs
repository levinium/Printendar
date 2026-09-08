using System.Text;
using Printendar.Core.Export;
using Printendar.Core.Paper;
using Printendar.Core.Scene;
using Printendar.Core.Text;
using SkiaSharp;

namespace Printendar.Core.Tests.Export;

/// <summary>
/// The export path, and the assertion this whole project turns on: one page.
/// </summary>
/// <remarks>
/// These read the produced bytes rather than counting calls into a fake document. Counting
/// BeginPage would prove the exporter did what the exporter was told; scanning the PDF proves
/// the artifact a user would actually print is a single sheet.
/// </remarks>
public class PdfExporterTests
{
    private static readonly PageSpec LetterLandscape =
        new(PaperSizes.Letter, Orientation.Landscape, Margins.FromInches(0.4f));

    private static byte[] Export(ScenePage page)
    {
        using var stream = new MemoryStream();
        using var measurer = SkiaTextMeasurer.CreateWithEmbeddedFont();

        PdfExporter.Export(page, stream, PdfMetadata.Default, measurer);

        return stream.ToArray();
    }

    private static ScenePage SamplePage(PageSpec? spec = null) => new(
        spec ?? LetterLandscape,
        new SceneGroup("root",
        [
            new SceneRect(new SKRect(50f, 50f, 400f, 300f), null, SKColors.Black, 0.75f),
            new SceneTextRun(
                "March 2026",
                new SKPoint(60f, 80f),
                new FontSpec(FontWeightKind.SemiBold, 14f),
                SKColors.Black,
                TextAnchor.Left,
                MaxWidth: 300f),
        ], Clip: null),
        LayoutDiagnostics.Empty);

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    [Fact]
    public void Produces_a_pdf()
    {
        var bytes = Export(SamplePage());

        Assert.StartsWith("%PDF", AsLatin1(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void Produces_exactly_one_page()
    {
        // The headline requirement. Classic Outlook could do this and new Outlook cannot.
        var content = AsLatin1(Export(SamplePage()));

        Assert.Contains("/Count 1", content, StringComparison.Ordinal);
        Assert.DoesNotContain("/Count 2", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Sizes_the_page_to_the_paper_in_landscape()
    {
        // Letter landscape is 792 by 612 points. A PDF that says otherwise will be scaled or
        // rotated by the print dialog, which is exactly the failure being fixed.
        var content = AsLatin1(Export(SamplePage()));

        Assert.Contains("/MediaBox [0 0 792 612]", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Sizes_the_page_to_the_paper_in_portrait()
    {
        var portrait = new PageSpec(PaperSizes.Letter, Orientation.Portrait, Margins.FromInches(0.4f));

        var content = AsLatin1(Export(SamplePage(portrait)));

        Assert.Contains("/MediaBox [0 0 612 792]", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Embeds_a_font_so_the_text_is_real_text_rather_than_a_picture()
    {
        // Vector text keeps the PDF small, sharp at any zoom, and searchable. Rasterizing it
        // would still print, but would defeat the point of exporting a PDF at all.
        var content = AsLatin1(Export(SamplePage()));

        Assert.Contains("/Font", content, StringComparison.Ordinal);
        Assert.Contains("/FontFile2", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Records_the_title_in_the_document_metadata()
    {
        using var stream = new MemoryStream();
        using var measurer = SkiaTextMeasurer.CreateWithEmbeddedFont();

        PdfExporter.Export(SamplePage(), stream, PdfMetadata.Default with { Title = "March 2026" }, measurer);

        Assert.Contains("March 2026", AsLatin1(stream.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_to_export_a_scene_that_does_not_fit_the_page()
    {
        // Exporting a malformed page would produce a sheet with content silently missing off
        // the edge. Better to fail with the reason than to print something wrong.
        var overflowing = new ScenePage(
            LetterLandscape,
            new SceneGroup("root",
            [
                new SceneRect(new SKRect(700f, 10f, 1200f, 100f), SKColors.Black, null, 0f),
            ], Clip: null),
            LayoutDiagnostics.Empty);

        var error = Assert.Throws<InvalidOperationException>(() => Export(overflowing));

        Assert.Contains("OutsidePage", error.Message, StringComparison.Ordinal);
    }
}
