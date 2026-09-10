using Printendar.Core.Printing;

namespace Printendar.Printing.Cups;

/// <summary>
/// Reads the printer list out of <c>lpstat -p</c>.
/// </summary>
/// <remarks>
/// Parsed from text because CUPS offers nothing better to a program that is not linked against
/// libcups, and linking against it would make this project platform-specific for the sake of
/// one list.
///
/// Only the name is taken. lpstat also says whether each destination is idle, printing or
/// disabled, and none of that should decide what the user is offered: disabled usually means
/// out of paper or paused, the job queues until somebody deals with it, and hiding the printer
/// would leave them hunting for one that is right there.
/// </remarks>
public static class CupsPrinters
{
    private const string Prefix = "printer ";

    /// <summary>
    /// Turns lpstat's output into a list, with the default first.
    /// </summary>
    /// <param name="output">Whatever <c>lpstat -p</c> wrote.</param>
    /// <param name="defaultPrinter">
    /// The name from <c>lpstat -d</c>, or null when no default is set. A name that is not in
    /// the list is ignored rather than added: a stale default is not a printer.
    /// </param>
    public static IReadOnlyList<PrinterInfo> Parse(string? output, string? defaultPrinter)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var names = new List<string>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();

            if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            // "printer NAME is idle. enabled since ..." and "printer NAME disabled since ...".
            // The name is the one token after the prefix either way.
            var name = trimmed[Prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        var isDefault = (string n) =>
            !string.IsNullOrWhiteSpace(defaultPrinter) &&
            string.Equals(n, defaultPrinter, StringComparison.Ordinal);

        // Default first, because it is the one nearly everybody wants and a dropdown that
        // opens on it is a dropdown most people never have to open.
        return
        [
            .. names
                .OrderByDescending(isDefault)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(n => new PrinterInfo(n, isDefault(n))),
        ];
    }

    /// <summary>
    /// Pulls the printer name out of <c>lpstat -d</c>, which answers in a sentence.
    /// </summary>
    /// <remarks>
    /// "system default destination: Office_Laser", or "no system default destination".
    /// </remarks>
    public static string? ParseDefault(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var colon = output.IndexOf(':');

        if (colon < 0)
        {
            return null;
        }

        var name = output[(colon + 1)..].Trim();

        return string.IsNullOrWhiteSpace(name) ? null : name.Split('\n')[0].Trim();
    }
}
