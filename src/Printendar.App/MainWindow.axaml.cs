using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Printendar.App.Controls;
using Printendar.App.Printing;
using Printendar.App.Sources;
using Printendar.Core.Export;
using Printendar.Core.Printing;
using Printendar.Core.Scene;
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
        this.FindControl<Button>("AddLink")!.Click += OnAddLink;
        this.FindControl<Button>("AddFile")!.Click += OnAddFile;

        // Loading opens every saved calendar, which for a feed means the network. Not awaited,
        // so the window appears at once and the calendars fill in as they answer.
        _ = model.LoadSettingsAsync();
    }

    /// <summary>Opens the spectrum picker for a colour outside the eight offered.</summary>
    private async void OnCustomColor(object? sender, RoutedEventArgs e)
    {
        // Either shape, because the row and the flyout inside it no longer share a data
        // context: the sidebar draws one row per source and reaches its single calendar
        // through the entry, while the manage window still hands the calendar over directly.
        // Matching only one of them is how a button silently stops doing anything.
        var calendar = sender switch
        {
            Control { DataContext: SelectableCalendar direct } => direct,
            Control { DataContext: SourceEntry entry } => entry.Calendar,
            _ => null,
        };

        if (calendar is null)
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


    // ------------------------------------------------------------ the calendar list
    //
    // These lived in a separate manage-calendars window. Everything it offered applies to one
    // calendar in a list the sidebar already draws, so the window was a second copy of that
    // list you had to open in order to act on the first one. The actions moved onto the cards.

    /// <summary>Says something about the calendar list, under the Add button.</summary>
    /// <remarks>
    /// Its own line rather than the status bar at the foot of the window. That bar explains
    /// what fitting the page cost, which is about the sheet; this is about the list, and it is
    /// eighteen inches away from the thing it is talking about.
    /// </remarks>
    private void Say(string message)
    {
        var block = this.FindControl<TextBlock>("CalendarMessage")!;

        block.Text = message;
        block.IsVisible = !string.IsNullOrEmpty(message);
    }

    /// <summary>Opens the name for editing, or closes it and keeps what was typed.</summary>
    private void OnToggleRename(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not SourceEntry entry)
        {
            return;
        }

        if (entry.IsEditingName)
        {
            FinishRename(entry, button);
            return;
        }

        entry.IsEditingName = true;

        // The box has only just been made visible, so it cannot take focus until a layout pass
        // has run. Posting puts the focus after that, which is the difference between clicking
        // the pencil and being able to type, and clicking the pencil and then having to click
        // the field as well.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (FindInCard<TextBox>(button, "NameBox") is { } box)
                {
                    box.Focus();
                    box.SelectAll();
                }
            },
            DispatcherPriority.Input);
    }

    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not SourceEntry entry)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                // Put back what it was called. The binding writes on losing focus, so nothing
                // has been saved yet and restoring the text is enough to undo the whole edit.
                box.Text = entry.DisplayName;
                FinishRename(entry, box);
                e.Handled = true;
                break;

            case Key.Enter:
                FinishRename(entry, box);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Closes the name field, committing whatever is in it.
    /// </summary>
    /// <remarks>
    /// Moving the focus is what commits: the name is bound on lost focus rather than on every
    /// keystroke, so a name is saved once when it is finished rather than once per letter, and
    /// a half-typed name never reaches the settings file.
    /// </remarks>
    private void FinishRename(SourceEntry entry, Control from)
    {
        // A blank name is refused rather than saved, because a calendar with no name cannot be
        // told apart in the list or in the printed legend. It was already refused, but
        // silently: the box simply sprang back to the old name with no explanation, which
        // reads as the app having lost what was typed.
        if (FindInCard<TextBox>(from, "NameBox") is { } box && string.IsNullOrWhiteSpace(box.Text))
        {
            Say($"A calendar needs a name, so this one is still called \"{entry.DisplayName}\".");

            // Put the old name back in the box as well as in the model. The box keeps whatever
            // was typed while it is hidden, so leaving it empty means the next click on the
            // pencil opens an empty field for a calendar that has a perfectly good name.
            box.Text = entry.DisplayName;
        }

        (FindInCard<Button>(from, "RenameButton") ?? (Control)this).Focus();
        entry.IsEditingName = false;
    }

    /// <summary>Finds a named control within the same card as <paramref name="from"/>.</summary>
    private static T? FindInCard<T>(Control from, string name)
        where T : Control =>
        from.FindAncestorOfType<Border>()?
            .GetLogicalDescendants()
            .OfType<T>()
            .FirstOrDefault(c => c.Name == name);

    /// <summary>Reads one calendar again, without touching the others.</summary>
    private async void OnRefreshOne(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SourceEntry entry })
        {
            return;
        }

        Say($"Reading {entry.DisplayName}…");

        await _model.Sources.ReconnectAsync(entry);

        Say(entry.HasError
            ? $"{entry.DisplayName} could not be read."
            : $"{entry.DisplayName} is up to date.");

        await _model.RefreshEventsAsync();
    }

    /// <summary>Removes one calendar, after asking.</summary>
    /// <remarks>
    /// A confirmation because there is no undo and the button sits a few pixels from the one
    /// that renames. What it costs is named exactly: a link has to be found again, a file only
    /// has to be picked again, and those are not the same loss.
    /// </remarks>
    private async void OnRemoveOne(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SourceEntry entry })
        {
            return;
        }

        var cost = entry.Configured.Kind == CalendarSourceKind.IcsUrl
            ? "Its address is not kept anywhere else, so you would need the published link again to add it back."
            : "The file itself is not touched, so you can add it again whenever you like.";

        if (!await CalendarPrompts.ConfirmAsync(
                this,
                "Remove this calendar?",
                $"\"{entry.DisplayName}\" will stop appearing on the printed page. {cost}",
                confirmLabel: "Remove"))
        {
            return;
        }

        entry.RemoveCommand?.Execute(entry);

        Say($"Removed {entry.DisplayName}.");
    }

    private async void OnAddLink(object? sender, RoutedEventArgs e)
    {
        if (await CalendarPrompts.AskForFeedAsync(this) is not { } feed)
        {
            return;
        }

        try
        {
            await _model.Sources.AddAsync(CalendarSourcesViewModel.ForUrl(feed.Address, feed.Name));
            Say("Added. If the link is private, keep it secret: it is the credential.");
        }
        catch (Exception ex)
        {
            Say(ex.Message);
        }
    }

    private async void OnAddFile(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add a calendar file",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Calendar files") { Patterns = ["*.ics"] },
                FilePickerFileTypes.All,
            ],
        });

        var added = 0;

        foreach (var file in files)
        {
            if (file.TryGetLocalPath() is not { } path)
            {
                continue;
            }

            string suggested;

            try
            {
                // Checked before the name is asked for, so a file that cannot be used is
                // refused straight away rather than after the user has thought of a name.
                suggested = CalendarSourcesViewModel.SuggestNameForFile(path);
            }
            catch (Exception ex)
            {
                Say(ex.Message);
                return;
            }

            var name = await CalendarPrompts.AskForNameAsync(
                this,
                $"Adding {System.IO.Path.GetFileName(path)}. What should it appear as, in the " +
                "list and in the legend on the printed page?",
                suggested);

            if (name is null)
            {
                // Cancelled this one. Any others picked at the same time still get their turn.
                continue;
            }

            try
            {
                // Each file becomes its own entry, so the list matches what was picked.
                await _model.Sources.AddAsync(CalendarSourcesViewModel.ForFile(path, name));
                added++;
            }
            catch (Exception ex)
            {
                Say(ex.Message);
                return;
            }
        }

        if (added > 0)
        {
            Say(added == 1 ? "Added." : $"Added {added} calendars.");
        }
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

        // "Go back" rather than "Fix calendars", because there is nowhere to be taken any
        // more: the calendar list, its errors and the button that reads one again are all on
        // the window behind this dialog. Offering to open something would be offering to open
        // what is already there.
        var fix = new Button
        {
            Content = "Go back",
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
            // The sidebar already names which calendar failed and why, so the message here
            // points at it rather than repeating it.
            Say(warning);
        }

        return result;
    }

    /// <summary>
    /// Re-reads the calendars, then hands back the page that resulted.
    /// </summary>
    /// <remarks>
    /// Both outputs go through here rather than reading the scene directly. A PDF saved from
    /// this morning's copy is exactly as wrong as a sheet printed from it, and having only one
    /// of them refresh is the kind of difference nobody notices until the two disagree.
    ///
    /// The refresh happens before the warning, so the warning describes the calendars as they
    /// are now: a feed that has started working again should not still be reported as missing.
    /// </remarks>
    private async Task<ScenePage?> PrepareForOutputAsync(string action)
    {
        await _model.RefreshBeforePrintingAsync();

        if (!await ConfirmIncompleteAsync(action))
        {
            return null;
        }

        return _model.Scene;
    }

    private async void OnSavePdf(object? sender, RoutedEventArgs e)
    {
        if (await PrepareForOutputAsync("Save") is not { } scene)
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

        // The same scene object the preview is showing, so the file cannot differ from what
        // is on screen.
        PdfExporter.ExportToFile(scene, path, PdfMetadata.Default with { Title = _model.MonthTitle }, _model.Measurer);
    }

    private async void OnPrint(object? sender, RoutedEventArgs e)
    {
        // Both the refresh and the warning happen before the printer dialog, not after: paper
        // cannot be un-printed.
        if (await PrepareForOutputAsync("Print") is not { } scene)
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
