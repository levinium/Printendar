using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Printendar.App.Controls;
using Printendar.App.Printing;
using Printendar.Core.Export;
using Printendar.Core.Printing;

namespace Printendar.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;

    public MainWindow()
        : this(new MainViewModel())
    {
    }

    public MainWindow(MainViewModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;

        // The preview must draw with the measurer the scene was laid out against, or it would
        // be drawing the page with different fonts than it was measured with.
        this.FindControl<ScenePreview>("Preview")!.Measurer = model.Measurer;

        this.FindControl<Button>("PreviousMonth")!.Click += (_, _) => model.StepMonth(-1);
        this.FindControl<Button>("NextMonth")!.Click += (_, _) => model.StepMonth(1);
        this.FindControl<Button>("SavePdf")!.Click += OnSavePdf;
        this.FindControl<Button>("Print")!.Click += OnPrint;
        this.FindControl<Button>("ManageCalendars")!.Click += OnManageCalendars;

        // Loading opens every saved calendar, which for a feed means the network. Not awaited,
        // so the window appears at once and the calendars fill in as they answer.
        _ = model.LoadSettingsAsync();
    }

    private async void OnManageCalendars(object? sender, RoutedEventArgs e)
    {
        var dialog = new ManageCalendarsWindow(_model.Sources, _model.Settings);

        await dialog.ShowDialog(this);

        // The list may have changed while it was open, and the month on screen was laid out
        // from the old one.
        await _model.RefreshEventsAsync();
    }

    private async void OnSavePdf(object? sender, RoutedEventArgs e)
    {
        if (_model.Scene is not { } scene)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save calendar as PDF",
            SuggestedFileName = $"{_model.MonthTitle}.pdf",
            DefaultExtension = "pdf",
            FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }],
        });

        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        // The same scene object the preview drew, so the file cannot differ from what was
        // on screen.
        PdfExporter.ExportToFile(scene, path, PdfMetadata.Default with { Title = _model.MonthTitle }, _model.Measurer);
    }

    private async void OnPrint(object? sender, RoutedEventArgs e)
    {
        if (_model.Scene is not { } scene)
        {
            return;
        }

        if (PrintService.CreatePrinter() is not { } printer)
        {
            // No direct printing on this platform. Hand the PDF over and say plainly that the
            // viewer's own scale setting now matters.
            var fallback = PrintService.OpenPdfForPrinting(scene, _model.MonthTitle, _model.Measurer);

            if (fallback.Message is { } fallbackMessage)
            {
                _model.ReportProblem(fallbackMessage);
            }

            return;
        }

        var dialogModel = new PrintDialogViewModel(
            printer,
            scene,
            _model.PageSummary,
            (float)_model.MarginInches);

        var dialog = new PrintDialogWindow(dialogModel, _model.Measurer, _model.MonthTitle);

        var outcome = await dialog.ShowDialog<PrintOutcome?>(this);

        // Cancelling returns null and carries no message. Saying "cancelled" back to somebody
        // who just pressed Cancel is noise.
        if (outcome?.Message is { } message)
        {
            _model.ReportProblem(message);
        }
    }
}
