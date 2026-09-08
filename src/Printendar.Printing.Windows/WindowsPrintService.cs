using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Windows.Forms;
using Printendar.Core.Printing;
using Printendar.Core.Render;
using Printendar.Core.Scene;
using Printendar.Core.Text;
using SkiaSharp;
using PageRenderOptions = Printendar.Core.Render.RenderOptions;

namespace Printendar.Printing.Windows;

/// <summary>
/// Prints through the Windows print dialog, straight from the scene.
/// </summary>
/// <remarks>
/// The alternative, and what this replaces, was writing a PDF and asking the shell to open it.
/// That is not printing: it hands the user to whichever viewer is installed, and that viewer's
/// own dialog decides the scale. A viewer set to "fit to page" quietly shrinks a layout that
/// was measured against the paper, which defeats the one guarantee this program makes.
///
/// Raster rather than vector. GDI+ has no route to draw Skia's output as vectors, so the page
/// is rasterised at <see cref="DefaultDpi"/> and blitted. That is visually indistinguishable
/// for a calendar, and the PDF remains the vector artifact for anyone who wants one. The scene
/// seam means a vector GDI path could be added later without touching layout.
/// </remarks>
public sealed class WindowsPrintService : IPlatformPrinter
{
    /// <summary>
    /// Rasterisation resolution.
    /// </summary>
    /// <remarks>
    /// 300 rather than 600. A Letter landscape sheet at 300 DPI is 3300 x 2550 pixels, about
    /// 34 MB; at 600 it is four times that, and the difference is not visible in printed text
    /// this size. Memory matters here because the bitmap is held while the driver consumes it.
    /// </remarks>
    public const int DefaultDpi = 300;

    private readonly int _dpi;

    public WindowsPrintService(int dpi = DefaultDpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        _dpi = dpi;
    }

    public PrintOutcome Print(ScenePage page, string title, ITextMeasurer measurer, float layoutMarginInches)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(measurer);

        var widthPt = page.Page.WidthPt;
        var heightPt = page.Page.HeightPt;

        using var document = new PrintDocument();
        document.DocumentName = string.IsNullOrWhiteSpace(title) ? "Calendar" : title;

        // Landscape is the entire point of this program, so it is set from the page rather
        // than left for the user to notice. A page wider than it is tall is landscape.
        document.DefaultPageSettings.Landscape = widthPt > heightPt;

        SelectPaperSize(document, widthPt, heightPt);

        // False means the Graphics origin sits at the corner of the printable area. The scene
        // is measured from the corner of the paper, and PrintPlacement carries that difference.
        document.OriginAtMargins = false;

        string? warning = null;

        void OnPrintPage(object _, PrintPageEventArgs e)
        {
            var printable = e.PageSettings.PrintableArea;

            var placement = PrintPlacement.Compute(
                widthPt,
                heightPt,
                new PrintableArea(printable.X, printable.Y, printable.Width, printable.Height));

            if (placement.WouldClip(layoutMarginInches))
            {
                warning =
                    $"This printer cannot print closer than {placement.HardMarginInches:0.00} inch " +
                    $"to the edge, and the page was laid out with a {layoutMarginInches:0.00} inch " +
                    "margin, so the outer border may be cut off. Raise the margin and print again.";
            }

            using var bitmap = RenderToGdiBitmap(page, measurer, widthPt, heightPt);

            e.Graphics!.PageUnit = GraphicsUnit.Display; // hundredths of an inch, as PrintPlacement expects
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            e.Graphics.DrawImage(
                bitmap,
                new RectangleF(placement.X, placement.Y, placement.Width, placement.Height));

            // One page, always. Saying so explicitly is what stops a driver quirk turning into
            // an endless print job.
            e.HasMorePages = false;
        }

        document.PrintPage += OnPrintPage;

        try
        {
            using var dialog = new PrintDialog
            {
                Document = document,
                UseEXDialog = true, // the modern dialog; the legacy one can fail to show on some systems
                AllowPrintToFile = true,
            };

            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return PrintOutcome.Cancelled;
            }

            document.Print();

            return PrintOutcome.Success(
                warning ?? $"Sent to {document.PrinterSettings.PrinterName}.");
        }
        catch (Exception ex) when (ex is InvalidPrinterException or System.ComponentModel.Win32Exception)
        {
            // The two everyday failures: no printer installed, and a driver refusing the job.
            // Neither should take the window down.
            return PrintOutcome.Failed($"Could not print: {ex.Message}");
        }
        finally
        {
            document.PrintPage -= OnPrintPage;
        }
    }

    /// <summary>
    /// Renders the scene directly into a GDI bitmap's own pixel buffer.
    /// </summary>
    /// <remarks>
    /// Skia draws straight into the memory GDI+ already allocated, rather than rendering to an
    /// SKBitmap and copying. At this size the copy would double peak memory for no benefit.
    ///
    /// Bgra8888 is not arbitrary: it is the byte order Format32bppPArgb uses in memory on a
    /// little-endian machine, so no channel swizzle is needed. Asking Skia for Rgba8888 here
    /// would print with red and blue exchanged.
    /// </remarks>
    private Bitmap RenderToGdiBitmap(ScenePage page, ITextMeasurer measurer, float widthPt, float heightPt)
    {
        var (widthPx, heightPx) = PrintPlacement.RasterSize(widthPt, heightPt, _dpi);

        var bitmap = new Bitmap(widthPx, heightPx, PixelFormat.Format32bppPArgb);

        var data = bitmap.LockBits(
            new Rectangle(0, 0, widthPx, heightPx),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppPArgb);

        try
        {
            var info = new SKImageInfo(widthPx, heightPx, SKColorType.Bgra8888, SKAlphaType.Premul);

            using var surface = SKSurface.Create(info, data.Scan0, data.Stride);
            var canvas = surface.Canvas;

            // Paper is white. Without this the unpainted areas are transparent, which some
            // drivers render as black.
            canvas.Clear(SKColors.White);

            // Scale once, then draw in points. The renderer never learns about DPI, which is
            // what makes the printed page place text identically to the on-screen preview.
            canvas.Scale(_dpi / 72f);

            // ForPrint, not ForPreview: the preview's page border is a screen affordance
            // showing where the sheet ends, and printing it would draw a rectangle on the paper.
            SceneRenderer.Draw(canvas, page, PageRenderOptions.ForPrint, measurer);

            canvas.Flush();
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        bitmap.SetResolution(_dpi, _dpi);

        return bitmap;
    }

    /// <summary>
    /// Picks the printer's own entry for the paper the page was laid out on.
    /// </summary>
    /// <remarks>
    /// Matching on dimensions rather than name, because the same sheet is "Letter", "US Letter"
    /// and "Letter (8.5x11in)" on different drivers. When nothing matches within a tolerance,
    /// the printer's default is left alone: a custom size that the driver rejects fails the
    /// whole job, which is worse than printing on the default tray.
    ///
    /// The comparison is always portrait-oriented because PaperSize entries are, regardless of
    /// how the page is being printed.
    /// </remarks>
    private static void SelectPaperSize(PrintDocument document, float widthPt, float heightPt)
    {
        var shortEdge = PrintPlacement.PointsToHundredths(MathF.Min(widthPt, heightPt));
        var longEdge = PrintPlacement.PointsToHundredths(MathF.Max(widthPt, heightPt));

        const float toleranceHundredths = 5f; // 0.05in, comfortably inside the gap between standard sizes

        foreach (PaperSize candidate in document.PrinterSettings.PaperSizes)
        {
            var candidateShort = MathF.Min(candidate.Width, candidate.Height);
            var candidateLong = MathF.Max(candidate.Width, candidate.Height);

            if (MathF.Abs(candidateShort - shortEdge) <= toleranceHundredths &&
                MathF.Abs(candidateLong - longEdge) <= toleranceHundredths)
            {
                document.DefaultPageSettings.PaperSize = candidate;
                return;
            }
        }
    }
}
