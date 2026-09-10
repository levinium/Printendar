using System.Diagnostics;
using System.Runtime.InteropServices;
using Printendar.Core.Export;
using Printendar.Core.Printing;
using Printendar.Core.Scene;
using Printendar.Core.Text;

namespace Printendar.App.Printing;

/// <summary>
/// Gets a laid-out page to a printer.
/// </summary>
/// <remarks>
/// On Windows this drives the printer directly, through Printendar's own print dialog rather
/// than the operating system's. Windows 11 substitutes its modern dialog for classic printing
/// calls and its preview pane reports "This app doesn't support print preview" over a dialog
/// that otherwise works. Showing our own is the better answer anyway: the preview there is the
/// same scene the printer receives, so it is the page rather than an approximation.
///
/// Everywhere else it still writes a PDF and asks the operating system to open it, because
/// Avalonia has no printing of its own. That fallback is worth being honest about: it is not
/// printing, it is handing the user to a viewer whose own dialog decides the scale. A viewer
/// left on "fit to page" will quietly shrink a layout measured against the paper, so the
/// message says so.
/// </remarks>
public static class PrintService
{
    /// <summary>
    /// Whether this build can drive a printer itself.
    /// </summary>
    /// <remarks>
    /// Used to label the button honestly. A control that says "Print" and opens a PDF reader
    /// is the kind of small lie that costs a non-technical user their trust in the whole app.
    /// </remarks>
    public static bool CanPrintDirectly => CreatePrinter() is not null;

    /// <summary>
    /// The printer for this platform, or null when there is none.
    /// </summary>
    /// <remarks>
    /// Windows is decided at compile time, because its shim needs a Windows-only framework and
    /// must not exist in the portable build. CUPS is decided at runtime, because that project
    /// is plain net10.0 and ships everywhere: what varies is whether lp is actually installed,
    /// which a Linux box without cups-client will not have.
    /// </remarks>
    public static IPlatformPrinter? CreatePrinter() =>
#if WINDOWS
        new Printendar.Printing.Windows.WindowsPrintService();
#else
        Printendar.Printing.Cups.CupsPrintService.IsAvailable
            ? new Printendar.Printing.Cups.CupsPrintService()
            : null;
#endif

    /// <summary>
    /// The fallback for platforms that cannot drive a printer: write the PDF and hand it over.
    /// </summary>
    public static PrintOutcome OpenPdfForPrinting(ScenePage scene, string title, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var path = Path.Combine(Path.GetTempPath(), $"{Sanitize(title)}.pdf");

        PdfExporter.ExportToFile(scene, path, PdfMetadata.Default with { Title = title }, measurer);

        OpenInDefaultViewer(path);

        return PrintOutcome.Success(
            "Opened in your PDF viewer. Print from there, and set it to Actual size rather " +
            "than Fit to page, or the layout will be shrunk.");
    }

    private static void OpenInDefaultViewer(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // UseShellExecute is what makes the OS resolve the default PDF handler. Without
            // it, Process.Start tries to execute the file itself.
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return;
        }

        var opener = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open";

        Process.Start(new ProcessStartInfo(opener, path) { UseShellExecute = false });
    }

    /// <summary>Strips anything a file name cannot contain, so a month title can become one.</summary>
    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. name.Select(c => invalid.Contains(c) ? '-' : c)]);

        return string.IsNullOrWhiteSpace(cleaned) ? "calendar" : cleaned;
    }
}
