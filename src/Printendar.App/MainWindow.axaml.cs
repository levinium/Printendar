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
        var options = Microsoft365Options.FromEnvironment();

        if (!options.IsConfigured)
        {
            // Says what to do rather than surfacing whatever the identity platform would have
            // said about an all-zero application id.
            _model.ReportProblem(
                "This build has no Microsoft application id, so it cannot sign in. Set the " +
                $"{Microsoft365Options.ClientIdEnvironmentVariable} environment variable to the client id " +
                "of an Entra application registration, then start Printendar again.");
            return;
        }

        await _model.ConnectAsync(new GraphCalendarSource(options));
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

    private void OnPrint(object? sender, RoutedEventArgs e)
    {
        if (_model.Scene is not { } scene)
        {
            return;
        }

        PrintService.Print(scene, _model.MonthTitle, _model.Measurer);
    }
}
