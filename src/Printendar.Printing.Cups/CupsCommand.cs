using Printendar.Core.Paper;

namespace Printendar.Printing.Cups;

/// <summary>What to print, and how.</summary>
/// <param name="PrinterName">The CUPS destination, as lpstat names it.</param>
/// <param name="Copies">How many, at least one.</param>
/// <param name="Title">What the print queue shows.</param>
/// <param name="Paper">The sheet the page was laid out against.</param>
/// <param name="Orientation">Carried for clarity; deliberately not passed to CUPS.</param>
/// <param name="PdfPath">The file to send.</param>
public readonly record struct PrintJob(
    string PrinterName,
    int Copies,
    string Title,
    PaperSize Paper,
    Orientation Orientation,
    string PdfPath);

/// <summary>
/// Builds the <c>lp</c> invocation that prints a page.
/// </summary>
/// <remarks>
/// Separate from the code that runs it so it can be pinned by tests. Nobody working on
/// Printendar can watch a sheet come out of a Linux printer, so the arguments are the only
/// thing standing between a change and a wrong page, and they are worth asserting exactly.
///
/// Two options here exist to stop CUPS helping. The PDF is already the right way round and
/// already the right size, because the whole product is a layout measured against the paper;
/// anything that rotates or scales it destroys the one guarantee Printendar makes.
/// </remarks>
public static class CupsCommand
{
    /// <summary>The command that lists destinations.</summary>
    public const string ListCommand = "lpstat";

    /// <summary>The command that prints.</summary>
    public const string PrintCommand = "lp";

    /// <summary>
    /// The name CUPS knows a paper size by.
    /// </summary>
    /// <remarks>
    /// These are PWG media names, which CUPS accepts directly. Falling back to Letter would
    /// be worse than failing loudly, so an unknown size is a bug rather than a default.
    /// </remarks>
    public static string MediaName(PaperSize paper)
    {
        ArgumentNullException.ThrowIfNull(paper);

        return paper.Id.ToLowerInvariant() switch
        {
            "letter" => "Letter",
            "legal" => "Legal",
            "tabloid" => "Tabloid",
            "a4" => "A4",
            "a3" => "A3",
            _ => throw new NotSupportedException(
                $"{paper.DisplayName} has no CUPS media name. Add one when the size is added."),
        };
    }

    /// <summary>
    /// The arguments to pass to <c>lp</c>, as a list rather than a command line.
    /// </summary>
    /// <remarks>
    /// A list, so nothing here is ever quoted or escaped and a printer called "Front Desk HP"
    /// cannot split into two arguments. The file comes last because lp takes it positionally;
    /// anywhere earlier and it reads as the value of the option before it.
    /// </remarks>
    public static IReadOnlyList<string> Arguments(PrintJob job)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(job.PrinterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.PdfPath);

        return
        [
            "-d", job.PrinterName,
            "-n", Math.Max(1, job.Copies).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-t", string.IsNullOrWhiteSpace(job.Title) ? "Printendar" : job.Title,

            // The size the page was measured against. Without it CUPS uses the queue's
            // default, and a Letter layout on an A4 default loses a strip off one edge.
            "-o", $"media={MediaName(job.Paper)}",

            // Orientation is deliberately absent. The PDF's own page is already wider than it
            // is tall for a landscape month, and asking CUPS to rotate as well turns it twice.
            "-o", "fit-to-page=false",
            "-o", "scaling=100",

            job.PdfPath,
        ];
    }

    /// <summary>The arguments that list the printers.</summary>
    public static IReadOnlyList<string> ListArguments() => ["-p"];

    /// <summary>The arguments that ask which printer is the default.</summary>
    public static IReadOnlyList<string> DefaultArguments() => ["-d"];
}
