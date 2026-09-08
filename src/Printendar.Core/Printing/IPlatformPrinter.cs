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

/// <summary>A printer the user can choose.</summary>
public readonly record struct PrinterInfo(string Name, bool IsDefault)
{
    /// <summary>
    /// What the user reads in the printer list.
    /// </summary>
    /// <remarks>
    /// A real property rather than something the UI formats, because a record's generated
    /// ToString prints its type and every field. A dropdown with no item template falls back
    /// to that, and the list read "PrinterInfo { Name = ..., IsDefault = True }".
    /// </remarks>
    public string DisplayName => IsDefault ? $"{Name} (Default)" : Name;
}

/// <summary>
/// Sends a laid-out page to a printer.
/// </summary>
/// <remarks>
/// Deliberately does not show the operating system's print dialog. On Windows 11 that dialog
/// reports "This app doesn't support print preview", because Windows substitutes its modern
/// dialog for classic printing calls and cannot render a preview for them. Printing works, but
/// the message is alarming and there is no way to satisfy it from this printing API.
///
/// Printendar does not need it. Its own preview is the page: the same scene object, through
/// the same renderer. So the app asks which printer and prints, and shows the real preview
/// itself rather than borrowing a worse one.
///
/// It lives in Core, which never references a UI framework, so the contract stays free of any
/// platform type and the Windows implementation can sit in its own Windows-only project.
/// </remarks>
public interface IPlatformPrinter
{
    /// <summary>The printers installed on this machine, default first.</summary>
    IReadOnlyList<PrinterInfo> GetPrinters();

    /// <summary>
    /// The area of the sheet the named printer can put ink on, for a page of this size.
    /// </summary>
    /// <remarks>
    /// Queried before printing so the user can be warned that their margin will be clipped
    /// while they can still change it, rather than after the paper has gone through.
    /// Null when the printer cannot be interrogated, which is not worth blocking printing over.
    /// </remarks>
    PrintableArea? GetPrintableArea(string printerName, float pageWidthPt, float pageHeightPt);

    /// <summary>Prints the page at its true physical size on the named printer.</summary>
    PrintOutcome Print(
        ScenePage page,
        string title,
        ITextMeasurer measurer,
        string printerName,
        int copies);
}
