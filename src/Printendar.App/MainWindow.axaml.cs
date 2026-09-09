using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Printendar.App.Controls;
using Printendar.App.Printing;
using Printendar.Core.Export;
using Printendar.Core.Printing;
using Printendar.Core.Sources;
using SkiaSharp;

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

    /// <summary>Opens the spectrum picker for a colour outside the eight offered.</summary>
    private async void OnCustomColor(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SelectableCalendar calendar })
        {
            return;
        }

        // Every other calendar's colour goes in, so the picker can say which one a near-miss
        // is near. Naming it is the difference between a warning and a riddle.
        var others = _model.Sources.Sources
            .SelectMany(source => source.Calendars)
            .Where(other => !ReferenceEquals(other, calendar))
            .Select(other => new NamedColor(other.DisplayName, other.Color))
            .ToList();

        var picker = new ColorPickerWindow(calendar.DisplayName, calendar.Color, others);

        await picker.ShowDialog(this);

        if (picker.Chosen is { } chosen)
        {
            calendar.Color = chosen;
        }
    }


    private async void OnManageCalendars(object? sender, RoutedEventArgs e)
    {
        var dialog = new ManageCalendarsWindow(_model.Sources, _model.Settings);

        await dialog.ShowDialog(this);

        // The list may have changed while it was open, and the month on screen was laid out
        // from the old one.
        await _model.RefreshEventsAsync();
    }

    /// <summary>
    /// Stops incomplete output leaving the program without the user knowing.
    /// </summary>
    /// <remarks>
    /// Both printing and exporting produce something that looks finished, and neither shows
    /// any sign that a signed-out account contributed nothing. The sidebar already says so,
    /// but a banner is easy to stop seeing; this is deliberately in the way, once, at the
    /// moment it matters.
    ///
    /// It warns rather than refuses. Printing the calendars that do work while an account is
    /// being sorted out is legitimate, and a program that flatly refused would be worked around
    /// rather than heeded.
    /// </remarks>
    private async Task<bool> ConfirmIncompleteAsync(string action)
    {
        if (_model.Sources.MissingDataWarning is not { } warning)
        {
            return true;
        }

        var proceed = new Button { Content = $"{action} anyway", Width = 150 };
        var fix = new Button
        {
            Content = "Fix calendars",
            Width = 130,
            Margin = new Avalonia.Thickness(0, 0, 8, 0),
            IsDefault = true,
        };

        var dialog = new Window
        {
            Title = "This page is incomplete",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var result = false;

        proceed.Click += (_, _) => { result = true; dialog.Close(); };
        fix.Click += (_, _) => { result = false; dialog.Close(); };

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(18),
            Children =
            {
                new TextBlock
                {
                    Text = warning,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Margin = new Avalonia.Thickness(0, 18, 0, 0),
                    Children = { fix, proceed },
                },
            },
        };

        await dialog.ShowDialog(this);

        if (!result)
        {
            // Taken straight to the place the problem is fixed, rather than told to go and
            // find it.
            OnManageCalendars(this, new RoutedEventArgs());
        }

        return result;
    }

    private async void OnSavePdf(object? sender, RoutedEventArgs e)
    {
        if (_model.Scene is not { } scene)
        {
            return;
        }

        if (!await ConfirmIncompleteAsync("Save"))
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

        // Before the printer dialog, not after: paper cannot be un-printed.
        if (!await ConfirmIncompleteAsync("Print"))
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
