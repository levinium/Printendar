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
/// On Windows this is a real print dialog driving a real printer, straight from the scene.
///
/// Everywhere else it still writes a PDF and asks the operating system to open it, because
/// Avalonia has no printing of its own. That fallback is worth being honest about: it is not
/// printing, it is handing the user to a viewer whose own dialog decides the scale. A viewer
/// left on "fit to page" will quietly shrink a layout that was measured against the paper. The
/// button says so on those platforms rather than pretending otherwise.
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
    public static bool CanPrintDirectly => Printer is not null;

    private static IPlatformPrinter? Printer =>
#if WINDOWS
        new Printendar.Printing.Windows.WindowsPrintService();
#else
        null;
#endif

    public static PrintOutcome Print(
        ScenePage scene,
        string title,
        ITextMeasurer measurer,
        float layoutMarginInches)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (Printer is { } printer)
        {
            return printer.Print(scene, title, measurer, layoutMarginInches);
        }

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
