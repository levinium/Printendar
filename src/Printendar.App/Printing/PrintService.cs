using System.Diagnostics;
using System.Runtime.InteropServices;
using Printendar.Core.Export;
using Printendar.Core.Scene;
using Printendar.Core.Text;

namespace Printendar.App.Printing;

/// <summary>
/// Gets a laid-out page to a printer.
/// </summary>
/// <remarks>
/// Avalonia has no printing of its own, so every platform goes through the PDF. That is not
/// purely a workaround: the PDF is the artifact anyway, it is vector with selectable text, and
/// handing it to the operating system means the user gets their own familiar print dialog with
/// their own printers, paper trays and driver settings, rather than a reimplementation.
///
/// The page size is baked into the PDF, so the print dialog opens already set to landscape.
/// That is the specific thing new Outlook cannot do and the reason this program exists, so it
/// must not be left to the user to set.
///
/// A native Windows print dialog driven directly from the scene is a later refinement. It
/// would remove the trip through a viewer, and the scene graph means it can be added without
/// touching layout.
/// </remarks>
public static class PrintService
{
    public static void Print(ScenePage scene, string title, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var path = Path.Combine(Path.GetTempPath(), $"{Sanitize(title)}.pdf");

        PdfExporter.ExportToFile(scene, path, PdfMetadata.Default with { Title = title }, measurer);

        OpenInDefaultViewer(path);
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
