using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Printendar.App.Controls;
using Printendar.App.Printing;
using Printendar.Core.Export;

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
