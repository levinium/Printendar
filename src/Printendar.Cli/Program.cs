using System.Globalization;
using Printendar.Cli;

return CommandLine.Run(args, Console.Out, Console.Error);

namespace Printendar.Cli
{
    /// <summary>
    /// A headless harness for the print engine.
    /// </summary>
    /// <remarks>
    /// Exists so the single-page guarantee can be exercised, eyeballed and printed on real
    /// paper before there is a window, an account, or any authentication code. If the month
    /// does not come out right here, no amount of UI will fix it.
    ///
    /// Argument parsing is hand-rolled rather than taking a dependency. The surface is a
    /// handful of flags on a developer tool, and the shipping product is the desktop app.
    /// </remarks>
    internal static class CommandLine
    {
        private const int Ok = 0;
        private const int UsageError = 2;
        private const int Failure = 1;

        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
            {
                WriteUsage(output);
                return args.Length == 0 ? UsageError : Ok;
            }

            if (args[0] is not "render")
            {
                error.WriteLine($"Unknown command '{args[0]}'.");
                WriteUsage(error);
                return UsageError;
            }

            try
            {
                var options = RenderRequestParser.Parse(args.AsSpan(1));
                return RenderCommand.Execute(options, output);
            }
            catch (ArgumentException ex)
            {
                error.WriteLine(ex.Message);
                WriteUsage(error);
                return UsageError;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                error.WriteLine(ex.Message);
                return Failure;
            }
        }

        private static void WriteUsage(TextWriter writer)
        {
            writer.WriteLine("""
                printendar render [options]

                Renders a calendar month to a single-page PDF.

                Options:
                  --month <yyyy-MM>     Month to render. Defaults to the current month.
                  --paper <id>          letter, legal, tabloid, a4, a3. Defaults to letter.
                  --landscape           Landscape orientation. This is the default.
                  --portrait            Portrait orientation.
                  --margin <inches>     Margin on all four edges. Defaults to 0.4.
                  --week-start <day>    sunday or monday. Defaults to sunday.
                  --weekend <mode>      full, compressed, or none. Defaults to full.
                                        compressed requires --week-start monday.
                  --adjacent <mode>     hidden, muted, or full. Defaults to muted.
                  --culture <name>      Culture for month and day names, e.g. en-US, de-DE.
                  --out <path>          Output file. Defaults to <month>.pdf.
                  --png <path>          Also write a PNG of the same page, at 96 DPI.
                  --demo                Fill the month with invented events across five calendars.

                Example:
                  printendar render --month 2026-03 --paper letter --landscape --out march.pdf
                """);
        }
    }

    /// <param name="Month">First day of the month being rendered.</param>
    /// <param name="Paper">Paper size id.</param>
    /// <param name="Landscape">Whether to lay the sheet on its side.</param>
    /// <param name="MarginInches">Margin on all four edges.</param>
    /// <param name="WeekStart">Sunday or Monday.</param>
    /// <param name="Weekend">How the weekend columns are handled.</param>
    /// <param name="AdjacentDays">How days either side of the month are shown.</param>
    /// <param name="Culture">Culture supplying month and day names.</param>
    /// <param name="OutputPath">Where to write the PDF.</param>
    /// <param name="PngPath">Optional PNG of the same page, for looking at without a viewer.</param>
    /// <param name="Demo">Fill the month with invented events instead of leaving it blank.</param>
    internal sealed record RenderRequest(
        DateOnly Month,
        string Paper,
        bool Landscape,
        float MarginInches,
        DayOfWeek WeekStart,
        string Weekend,
        string AdjacentDays,
        CultureInfo Culture,
        string OutputPath,
        string? PngPath,
        bool Demo);
}
