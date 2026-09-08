using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Printendar.App.Controls;
using Printendar.App.Printing;
using Printendar.Core.Export;
using Printendar.Sources.Microsoft365;

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
        this.FindControl<Button>("ConnectMicrosoft")!.Click += OnConnectMicrosoft;
        this.FindControl<Button>("Disconnect")!.Click += async (_, _) => await _model.DisconnectAsync();
        this.FindControl<Button>("AdminConsent")!.Click += OnAdminConsent;
        this.FindControl<Button>("OpenIcsFile")!.Click += OnOpenIcsFile;
        this.FindControl<Button>("MicrosoftSetup")!.Click += OnMicrosoftSetup;
        this.FindControl<Button>("SaveMicrosoftRegistration")!.Click += OnSaveMicrosoftRegistration;
        this.FindControl<Button>("OpenPortal")!.Click += OnOpenPortal;
        this.FindControl<Button>("ApproveForOrganisation")!.Click += OnApproveForOrganisation;

        // Saved settings decide whether the Connect button is available, so load before the
        // first bindings evaluate.
        model.LoadSettings();
    }

    private async void OnOpenIcsFile(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a calendar file",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Calendar files") { Patterns = ["*.ics"] },
                FilePickerFileTypes.All,
            ],
        });

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

        if (paths.Count == 0)
        {
            return;
        }

        await _model.OpenCalendarFilesAsync(paths);
    }

    private void OnAdminConsent(object? sender, RoutedEventArgs e)
    {
        if (_model.AdminConsentUrl is not { } url)
        {
            return;
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        _model.ClearAdminConsentPrompt();
        _model.ReportProblem(
            "Approve Printendar on the page that just opened, then come back and click " +
            "Connect Microsoft 365 again.");
    }

    private async void OnConnectMicrosoft(object? sender, RoutedEventArgs e)
    {
        var options = _model.MicrosoftOptions;

        if (!options.IsConfigured)
        {
            // The button is disabled in this state, so this is belt and braces rather than the
            // path anyone takes.
            _model.ShowMicrosoftSetup = true;
            return;
        }

        await _model.ConnectAsync(new GraphCalendarSource(options));
    }

    private void OnMicrosoftSetup(object? sender, RoutedEventArgs e) =>
        _model.ShowMicrosoftSetup = !_model.ShowMicrosoftSetup;

    private void OnSaveMicrosoftRegistration(object? sender, RoutedEventArgs e) =>
        _model.SaveMicrosoftRegistration();

    private void OnOpenPortal(object? sender, RoutedEventArgs e) =>
        Open(MainViewModel.PortalNewRegistrationUrl);

    private void OnApproveForOrganisation(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_model.MicrosoftAdminConsentUrl))
        {
            return;
        }

        Open(_model.MicrosoftAdminConsentUrl);

        _model.ReportProblem(
            "Approve Printendar on the page that just opened, then come back and click " +
            "Connect Microsoft 365.");
    }

    private static void Open(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

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

    private void OnPrint(object? sender, RoutedEventArgs e)
    {
        if (_model.Scene is not { } scene)
        {
            return;
        }

        var outcome = PrintService.Print(
            scene,
            _model.MonthTitle,
            _model.Measurer,
            (float)_model.MarginInches);

        // Cancelling carries no message, and saying "cancelled" back to somebody who just
        // pressed Cancel is noise.
        if (outcome.Message is { } message)
        {
            _model.ReportProblem(message);
        }
    }
}
