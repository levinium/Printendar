using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Printendar.Core.Printing;
using Printendar.Core.Scene;

namespace Printendar.App;

/// <summary>
/// The state behind Printendar's own print dialog.
/// </summary>
/// <remarks>
/// Printendar shows its own rather than the operating system's. On Windows 11 the system
/// dialog reports "This app doesn't support print preview" over an otherwise working dialog,
/// because Windows substitutes its modern dialog for classic printing calls and cannot render
/// a preview for them.
///
/// Showing our own is not a workaround so much as the better answer: the preview here is the
/// same scene object the printer receives, through the same renderer, so it is the page rather
/// than a second rendering that might disagree with it.
/// </remarks>
public sealed class PrintDialogViewModel : INotifyPropertyChanged
{
    private readonly IPlatformPrinter _printer;
    private readonly float _pageWidthPt;
    private readonly float _pageHeightPt;
    private readonly float _layoutMarginInches;

    private PrinterInfo? _selectedPrinter;
    private int _copies = 1;
    private string? _warning;

    public PrintDialogViewModel(
        IPlatformPrinter printer,
        ScenePage scene,
        string pageSummary,
        float layoutMarginInches)
    {
        _printer = printer ?? throw new ArgumentNullException(nameof(printer));
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        PageSummary = pageSummary;

        _pageWidthPt = scene.Page.WidthPt;
        _pageHeightPt = scene.Page.HeightPt;
        _layoutMarginInches = layoutMarginInches;

        foreach (var found in printer.GetPrinters())
        {
            Printers.Add(found);
        }

        // GetPrinters puts the default first, so this needs no separate search.
        SelectedPrinter = Printers.FirstOrDefault();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The page itself. The preview draws this, and the printer receives this.</summary>
    public ScenePage Scene { get; }

    public string PageSummary { get; }

    public ObservableCollection<PrinterInfo> Printers { get; } = [];

    public bool HasNoPrinters => Printers.Count == 0;

    public bool CanPrint => SelectedPrinter is not null;

    public PrinterInfo? SelectedPrinter
    {
        get => _selectedPrinter;
        set
        {
            _selectedPrinter = value;
            Raise(nameof(SelectedPrinter));
            Raise(nameof(CanPrint));
            RecomputeWarning();
        }
    }

    public int Copies
    {
        get => _copies;
        set
        {
            _copies = Math.Clamp(value, 1, 99);
            Raise(nameof(Copies));
        }
    }

    /// <summary>Told before the paper is used, not after.</summary>
    public string? Warning
    {
        get => _warning;
        private set
        {
            _warning = value;
            Raise(nameof(Warning));
            Raise(nameof(HasWarning));
        }
    }

    public bool HasWarning => !string.IsNullOrEmpty(_warning);

    /// <summary>
    /// Compares the chosen printer's unprintable margin against the laid-out margin.
    /// </summary>
    /// <remarks>
    /// Recomputed per printer because they differ: a page that clears one printer's edge is
    /// clipped by another's. Doing it on selection means the user sees it while they can still
    /// change the margin.
    /// </remarks>
    private void RecomputeWarning()
    {
        if (SelectedPrinter is not { } printer)
        {
            Warning = null;
            return;
        }

        var printable = _printer.GetPrintableArea(printer.Name, _pageWidthPt, _pageHeightPt);

        if (printable is not { } area)
        {
            Warning = null;
            return;
        }

        var placement = PrintPlacement.Compute(_pageWidthPt, _pageHeightPt, area);

        Warning = placement.WouldClip(_layoutMarginInches)
            ? $"This printer cannot print closer than {placement.HardMarginInches:0.00}\" to the " +
              $"edge, and the page uses a {_layoutMarginInches:0.00}\" margin. The outer border " +
              "will be cut off. Cancel and raise the margin to fix it."
            : null;
    }

    public PrintOutcome Print(Core.Text.ITextMeasurer measurer, string title)
    {
        if (SelectedPrinter is not { } printer)
        {
            return PrintOutcome.Failed("No printer selected.");
        }

        return _printer.Print(Scene, title, measurer, printer.Name, Copies);
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));
}
