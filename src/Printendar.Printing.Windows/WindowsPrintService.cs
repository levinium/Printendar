using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using Printendar.Core.Printing;
using Printendar.Core.Render;
using Printendar.Core.Scene;
using Printendar.Core.Text;
using SkiaSharp;
using PageRenderOptions = Printendar.Core.Render.RenderOptions;

namespace Printendar.Printing.Windows;

/// <summary>
/// Prints straight from the scene to a named Windows printer.
/// </summary>
/// <remarks>
/// No system print dialog. Windows 11 substitutes its modern dialog for classic printing calls
/// and its preview pane cannot render one for them, so it reports "This app doesn't support
/// print preview" over a dialog that otherwise works. There is no way to satisfy that from
/// System.Drawing.Printing, and Printendar does not need to: its own preview is the same scene
/// object drawn through the same renderer, so it is the page rather than an approximation of it.
///
/// Raster rather than vector. GDI+ has no route to draw Skia output as vectors, so the page is
/// rasterised at <see cref="DefaultDpi"/> and blitted. That is visually indistinguishable for a
/// calendar, and the PDF remains the vector artifact. The scene seam leaves a vector GDI path
/// open later without touching layout.
/// </remarks>
public sealed class WindowsPrintService : IPlatformPrinter
{
    /// <summary>
    /// Rasterisation resolution.
    /// </summary>
    /// <remarks>
    /// 300 rather than 600. A Letter landscape sheet at 300 DPI is 3300 x 2550 pixels, about
    /// 34 MB; at 600 it is four times that, for a difference not visible in printed text this
    /// size. Memory matters because the bitmap is held while the driver consumes it.
    /// </remarks>
    public const int DefaultDpi = 300;

    private readonly int _dpi;

    public WindowsPrintService(int dpi = DefaultDpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        _dpi = dpi;
    }

    public IReadOnlyList<PrinterInfo> GetPrinters()
    {
        string? defaultName = null;

        try
        {
            defaultName = new PrinterSettings().PrinterName;
        }
        catch (Exception ex) when (ex is InvalidPrinterException or System.ComponentModel.Win32Exception)
        {
            // No default printer configured. Not a reason to show an empty list.
        }

        var names = PrinterSettings.InstalledPrinters.Cast<string>().ToList();

        // Default first, so the common case is the preselected one and needs no thought.
        return
        [
            .. names
                .Select(n => new PrinterInfo(n, string.Equals(n, defaultName, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(p => p.IsDefault)
                .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
        ];
    }

    public PrintableArea? GetPrintableArea(string printerName, float pageWidthPt, float pageHeightPt)
    {
        try
        {
            var settings = new PrinterSettings { PrinterName = printerName };

            if (!settings.IsValid)
            {
                return null;
            }

            var page = settings.DefaultPageSettings;

            // Orientation changes which edges the hard margins fall on, so it has to be set
            // before the printable area is read or the warning can be computed for the wrong
            // two edges.
            page.Landscape = pageWidthPt > pageHeightPt;

            ApplyPaperSize(settings, page, pageWidthPt, pageHeightPt);

            var printable = page.PrintableArea;

            return new PrintableArea(printable.X, printable.Y, printable.Width, printable.Height);
        }
        catch (Exception ex) when (ex is InvalidPrinterException or System.ComponentModel.Win32Exception)
        {
            // An offline or misconfigured printer should not stop the user printing to it.
            return null;
        }
    }

    public PrintOutcome Print(
        ScenePage page,
        string title,
        ITextMeasurer measurer,
        string printerName,
        int copies)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copies);

        var widthPt = page.Page.WidthPt;
        var heightPt = page.Page.HeightPt;

        using var document = new PrintDocument();

        document.DocumentName = string.IsNullOrWhiteSpace(title) ? "Calendar" : title;
        document.PrinterSettings.PrinterName = printerName;

        if (!document.PrinterSettings.IsValid)
        {
            return PrintOutcome.Failed($"{printerName} is not available.");
        }

        document.PrinterSettings.Copies = (short)Math.Min(copies, (int)short.MaxValue);

        // Landscape is the entire point of this program, so it is set from the page rather
        // than left for the user to notice. A page wider than it is tall is landscape.
        document.DefaultPageSettings.Landscape = widthPt > heightPt;

        ApplyPaperSize(document.PrinterSettings, document.DefaultPageSettings, widthPt, heightPt);

        // False means the Graphics origin sits at the corner of the printable area. The scene
        // is measured from the corner of the paper, and PrintPlacement carries that difference.
        document.OriginAtMargins = false;

        void OnPrintPage(object _, PrintPageEventArgs e)
        {
            var printable = e.PageSettings.PrintableArea;

            var placement = PrintPlacement.Compute(
                widthPt,
                heightPt,
                new PrintableArea(printable.X, printable.Y, printable.Width, printable.Height));

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
            document.Print();

            var sheets = copies == 1 ? "1 page" : $"{copies} copies";

            return PrintOutcome.Success($"Sent {sheets} to {printerName}.");
        }
        catch (Exception ex) when (ex is InvalidPrinterException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // The everyday failures: printer offline, driver refusing the job, no printer at
            // all. None of them should take the window down.
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
    /// the printer's default is left alone: a custom size the driver rejects fails the whole
    /// job, which is worse than printing on the default tray.
    ///
    /// The comparison is always portrait-oriented because PaperSize entries are, regardless of
    /// how the page is being printed.
    /// </remarks>
    private static void ApplyPaperSize(
        PrinterSettings settings,
        PageSettings page,
        float widthPt,
        float heightPt)
    {
        var shortEdge = PrintPlacement.PointsToHundredths(MathF.Min(widthPt, heightPt));
        var longEdge = PrintPlacement.PointsToHundredths(MathF.Max(widthPt, heightPt));

        const float toleranceHundredths = 5f; // 0.05in, comfortably inside the gap between standard sizes

        foreach (PaperSize candidate in settings.PaperSizes)
        {
            var candidateShort = MathF.Min(candidate.Width, candidate.Height);
            var candidateLong = MathF.Max(candidate.Width, candidate.Height);

            if (MathF.Abs(candidateShort - shortEdge) <= toleranceHundredths &&
                MathF.Abs(candidateLong - longEdge) <= toleranceHundredths)
            {
                page.PaperSize = candidate;
                return;
            }
        }
    }
}
