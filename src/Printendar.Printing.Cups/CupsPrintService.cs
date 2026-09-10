using System.Diagnostics;
using Printendar.Core.Export;
using Printendar.Core.Paper;
using Printendar.Core.Printing;
using Printendar.Core.Scene;
using Printendar.Core.Text;

namespace Printendar.Printing.Cups;

/// <summary>
/// Prints through CUPS, which is how macOS and Linux print.
/// </summary>
/// <remarks>
/// Writes the same PDF the Save button produces and hands it to <c>lp</c>. That is genuinely
/// printing rather than the fallback it replaces: the fallback opened the file in whatever
/// viewer the desktop had and left that viewer's own scale setting to decide the result, which
/// for a layout measured against the paper is the one thing that must not happen.
///
/// Running lp rather than linking libcups keeps this project plain net10.0, so it compiles and
/// ships everywhere and simply is not selected on Windows. The cost is that everything is text
/// on a pipe, which is why the command and the parsing live in their own classes with tests
/// around them.
/// </remarks>
public sealed class CupsPrintService : IPlatformPrinter
{
    /// <summary>
    /// A printer that has not answered in this long is not going to.
    /// </summary>
    /// <remarks>
    /// lpstat blocks while it tries to reach an unreachable network queue, and without a limit
    /// the window freezes with no explanation the moment somebody opens the print dialog.
    /// </remarks>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Whether this machine can actually print this way.</summary>
    /// <remarks>
    /// Asked before the service is offered at all, because a Linux install without cups-client
    /// has no lp, and a Print button that fails when pressed is worse than one that honestly
    /// says it will open a PDF instead.
    /// </remarks>
    public static bool IsAvailable =>
        (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) &&
        Run(CupsCommand.PrintCommand, ["--help"]) is not null;

    public IReadOnlyList<PrinterInfo> GetPrinters()
    {
        var listed = Run(CupsCommand.ListCommand, CupsCommand.ListArguments());
        var chosen = Run(CupsCommand.ListCommand, CupsCommand.DefaultArguments());

        return CupsPrinters.Parse(listed, CupsPrinters.ParseDefault(chosen));
    }

    /// <summary>
    /// Always null: CUPS does not tell us this cheaply.
    /// </summary>
    /// <remarks>
    /// The margin warning is a courtesy and the contract allows it to be absent. Getting a real
    /// answer means reading the PPD or querying media-bottom-margin and friends over IPP, which
    /// is a large amount of parsing for a warning, and a wrong answer here would be worse than
    /// none: it would tell somebody their margin is fine when it is about to be clipped.
    /// </remarks>
    public PrintableArea? GetPrintableArea(string printerName, float pageWidthPt, float pageHeightPt) => null;

    public PrintOutcome Print(
        ScenePage page,
        string title,
        ITextMeasurer measurer,
        string printerName,
        int copies)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (string.IsNullOrWhiteSpace(printerName))
        {
            return PrintOutcome.Failed("No printer was chosen.");
        }

        var path = Path.Combine(Path.GetTempPath(), $"printendar-{Guid.NewGuid():N}.pdf");

        try
        {
            PdfExporter.ExportToFile(page, path, PdfMetadata.Default with { Title = title }, measurer);

            // Taken from the page rather than worked out from its dimensions. The scene
            // carries the spec it was laid out against, so the size sent to CUPS is the size
            // the layout was measured for rather than the nearest match to it.
            var job = new PrintJob(
                printerName,
                copies,
                title,
                page.Page.Paper,
                page.Page.Orientation,
                path);

            return Run(CupsCommand.PrintCommand, CupsCommand.Arguments(job)) is null
                ? PrintOutcome.Failed($"{printerName} did not accept the job.")
                : PrintOutcome.Success($"Sent to {printerName}.");
        }
        catch (Exception ex)
        {
            return PrintOutcome.Failed(ex.Message);
        }
        finally
        {
            // lp reads the file before returning, so removing it now is safe. Left behind it
            // would put a copy of somebody's calendar in the temp directory after every print.
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// Runs a command and returns what it wrote, or null if it failed.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception, because every caller here treats "could not ask" and
    /// "answered nothing useful" the same way, and a missing lp is an ordinary state on a
    /// machine without cups-client rather than something worth a stack trace.
    /// </remarks>
    private static string? Run(string command, IReadOnlyList<string> arguments)
    {
        try
        {
            var info = new ProcessStartInfo(command)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in arguments)
            {
                // Added one at a time rather than as a command line, so a printer named
                // "Front Desk HP" stays one argument and nothing here needs quoting.
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info);

            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(Timeout))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception)
        {
            // The command is not installed, or the platform refused to start it.
            return null;
        }
    }
}
