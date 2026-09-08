using Printendar.Core.Scene;
using Printendar.Core.Text;

namespace Printendar.Core.Printing;

/// <summary>What happened when the user asked to print.</summary>
/// <param name="Printed">False when the user cancelled, which is not an error.</param>
/// <param name="Message">Plain-language outcome for the status bar, or null to say nothing.</param>
public readonly record struct PrintOutcome(bool Printed, string? Message)
{
    public static PrintOutcome Cancelled => new(false, null);

    public static PrintOutcome Success(string? message = null) => new(true, message);

    public static PrintOutcome Failed(string message) => new(false, message);
}

/// <summary>
/// Sends a laid-out page to a printer.
/// </summary>
/// <remarks>
/// An interface because only Windows can drive a printer directly today. Everywhere else falls
/// back to handing the PDF to the operating system, which is worse but works.
///
/// It lives in Core, which never references a UI framework, so the contract stays free of any
/// platform type and the Windows implementation can sit in its own Windows-only project.
/// </remarks>
public interface IPlatformPrinter
{
    /// <summary>
    /// Shows the user a print dialog and prints the page at its true physical size.
    /// </summary>
    /// <param name="layoutMarginInches">
    /// The margin the page was laid out with, so the printer's own unprintable margin can be
    /// compared against it and the user warned before the paper is used rather than after.
    /// </param>
    PrintOutcome Print(ScenePage page, string title, ITextMeasurer measurer, float layoutMarginInches);
}
