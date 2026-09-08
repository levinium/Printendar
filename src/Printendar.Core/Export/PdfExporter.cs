using Printendar.Core.Render;
using Printendar.Core.Scene;
using Printendar.Core.Text;
using SkiaSharp;

namespace Printendar.Core.Export;

/// <param name="Title">Shown in the viewer's title bar and used as the default file name.</param>
/// <param name="Author">Document author.</param>
public sealed record PdfMetadata(string Title, string Author)
{
    public static PdfMetadata Default { get; } = new("Calendar", "Printendar");
}

/// <summary>
/// Writes a laid-out page as a single-page PDF.
/// </summary>
/// <remarks>
/// The primary artifact on every platform. Vector, with embedded subset fonts, so the text is
/// selectable and searchable and the file stays small and sharp at any zoom. On macOS and
/// Linux this is also the print path, because Avalonia has no printing of its own and both
/// systems print a PDF natively.
///
/// Exactly one <see cref="SKDocument.BeginPage(float, float)"/> call, by construction. There
/// is no loop here and no pagination to get wrong.
/// </remarks>
public static class PdfExporter
{
    /// <summary>
    /// Resolution used for anything Skia cannot express as vectors.
    /// </summary>
    /// <remarks>
    /// Printendar draws only rectangles, lines and text, so nothing should be rasterized at
    /// all. The value is set anyway so that a future addition which does need rasterizing
    /// lands at print quality rather than at the default.
    /// </remarks>
    private const float RasterFallbackDpi = 300f;

    public static void Export(ScenePage page, Stream output, PdfMetadata metadata, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(measurer);

        // Exporting a malformed page would produce a sheet with content missing off the edge
        // and no indication anything went wrong, which is the exact failure this project is
        // meant to eliminate.
        SceneValidator.ThrowIfInvalid(page, measurer);

        var documentMetadata = new SKDocumentPdfMetadata
        {
            Title = metadata.Title,
            Author = metadata.Author,
            Producer = "Printendar",
            Creation = DateTime.Now,
            RasterDpi = RasterFallbackDpi,
        };

        using var document = SKDocument.CreatePdf(output, documentMetadata)
            ?? throw new InvalidOperationException("Skia could not start a PDF document.");

        var canvas = document.BeginPage(page.Page.WidthPt, page.Page.HeightPt);
        SceneRenderer.Draw(canvas, page, RenderOptions.ForPrint, measurer);
        document.EndPage();
        document.Close();
    }

    public static void ExportToFile(ScenePage page, string path, PdfMetadata metadata, ITextMeasurer measurer)
    {
        using var stream = File.Create(path);
        Export(page, stream, metadata, measurer);
    }
}
