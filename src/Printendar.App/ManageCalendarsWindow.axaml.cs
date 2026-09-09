using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Printendar.App.Sources;
using Printendar.Core.Settings;
using Printendar.Core.Sources;
using Printendar.Sources.Microsoft365;

namespace Printendar.App;

/// <summary>
/// Where calendars are added, renamed, reconnected and removed.
/// </summary>
/// <remarks>
/// Separate from the main window because these are occasional actions. Leaving them in the
/// sidebar meant the panel a person reads on every print was mostly buttons they press once.
/// </remarks>
public partial class ManageCalendarsWindow : Window
{
    private readonly CalendarSourcesViewModel _sources = null!;
    private readonly AppSettings _settings = new();

    /// <summary>Parameterless constructor for the XAML previewer only.</summary>
    public ManageCalendarsWindow() => InitializeComponent();

    public ManageCalendarsWindow(CalendarSourcesViewModel sources, AppSettings settings)
    {
        _sources = sources;
        _settings = settings;

        InitializeComponent();
        DataContext = sources;

        this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();
        this.FindControl<Button>("AddFile")!.Click += OnAddFile;
        this.FindControl<Button>("AddUrl")!.Click += OnAddUrl;
        this.FindControl<Button>("AddMicrosoft")!.Click += OnAddMicrosoft;
        this.FindControl<Button>("AddGoogle")!.Click += OnAddGoogle;

        // Said once, up front, rather than after somebody has tried and failed.
        var unavailable = new List<string>();

        if (CalendarSourceFactory.UnavailableReason(CalendarSourceKind.Microsoft365, settings) is not null)
        {
            unavailable.Add("Microsoft 365 needs an app registration this build does not have.");
        }

        unavailable.Add("Google Calendar is not in this build yet.");

        this.FindControl<TextBlock>("AddHint")!.Text = string.Join(" ", unavailable);
    }

    private void Say(string message) => this.FindControl<TextBlock>("Message")!.Text = message;

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
                if (FindInRow<TextBox>(button, "NameBox") is { } box)
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
        (FindInRow<Button>(from, "RenameButton") ?? (Control)this).Focus();
        entry.IsEditingName = false;
    }

    /// <summary>Finds a named control within the same list row as <paramref name="from"/>.</summary>
    private static T? FindInRow<T>(Control from, string name)
        where T : Control =>
        from.FindAncestorOfType<Grid>()?
            .GetLogicalDescendants()
            .OfType<T>()
            .FirstOrDefault(c => c.Name == name);

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

            var name = await AskForTextAsync(
                "Name this calendar",
                $"Adding {System.IO.Path.GetFileName(path)}. What should it appear as, in the " +
                "list and in the legend on the printed page?",
                placeholder: suggested,
                initialText: suggested);

            if (name is null)
            {
                // Cancelled this one. Any others picked at the same time still get their turn.
                continue;
            }

            try
            {
                // Each file becomes its own entry, so the list matches what was picked.
                await _sources.AddAsync(CalendarSourcesViewModel.ForFile(path, name));
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

    private async void OnAddUrl(object? sender, RoutedEventArgs e)
    {
        if (await AskForFeedAsync() is not { } feed || string.IsNullOrWhiteSpace(feed.Address))
        {
            return;
        }

        try
        {
            await _sources.AddAsync(CalendarSourcesViewModel.ForUrl(feed.Address, feed.Name));
            Say("Added. If the feed is private, keep that address secret: it is the credential.");
        }
        catch (Exception ex)
        {
            Say(ex.Message);
        }
    }

    private async void OnAddMicrosoft(object? sender, RoutedEventArgs e)
    {
        if (CalendarSourceFactory.UnavailableReason(CalendarSourceKind.Microsoft365, _settings) is { } reason)
        {
            Say(reason);
            return;
        }

        var options = Microsoft365Options.Resolve(_settings);
        var id = Guid.NewGuid().ToString("N");

        // Signed in before the entry is added, so a cancelled sign-in leaves nothing behind
        // for the user to tidy up.
        var source = new GraphCalendarSource(options, id);

        try
        {
            var account = await source.ConnectAsync(default);

            if (_sources.Sources.Any(s =>
                    s.Configured.Kind == CalendarSourceKind.Microsoft365 &&
                    string.Equals(s.Configured.AccountId, source.HomeAccountId, StringComparison.Ordinal)))
            {
                Say($"{account.Email ?? account.DisplayName} is already connected.");
                return;
            }

            await _sources.AddAsync(new ConfiguredSource(
                Id: id,
                Kind: CalendarSourceKind.Microsoft365,
                DisplayName: account.Email ?? account.DisplayName,
                Location: null,
                AccountId: source.HomeAccountId));

            Say($"Connected {account.Email ?? account.DisplayName}.");
        }
        catch (Microsoft365SignInException ex)
        {
            Say(ex.Diagnosis.Message);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Say(ex.Message);
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    private void OnAddGoogle(object? sender, RoutedEventArgs e) =>
        Say("Google Calendar is not in this build yet. In the meantime, Google Calendar can " +
            "publish a secret address under Settings, and that can be added as a web address.");

    /// <summary>The address and name of a feed, as typed. Null when cancelled.</summary>
    private sealed record FeedEntry(string Address, string? Name);

    /// <summary>
    /// Asks for a feed address and what to call it, together.
    /// </summary>
    /// <remarks>
    /// The name is asked for here rather than left to be corrected afterwards because the
    /// automatic answer is so often wrong: a published address gives us the host and little
    /// else, so a Google feed arrives called "calendar.google.com" whatever is actually in it.
    /// The suggestion is still shown, and updates as the address is typed, so the name can be
    /// left alone when it happens to be sensible.
    /// </remarks>
    private async Task<FeedEntry?> AskForFeedAsync()
    {
        var address = new TextBox
        {
            PlaceholderText = "https://",
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
        };

        var name = new TextBox
        {
            PlaceholderText = SuggestionPlaceholder(null),
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
        };

        // Kept in step with the address so the name that would be used is visible before the
        // feed is added, rather than discovered in the list afterwards.
        address.TextChanged += (_, _) => name.PlaceholderText = SuggestionPlaceholder(address.Text);

        var ok = new Button { Content = "Add", Width = 90, IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 90, Margin = new Avalonia.Thickness(0, 0, 8, 0) };

        var dialog = new Window
        {
            Title = "Add a calendar feed",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Children =
                {
                    new TextBlock
                    {
                        Text = "Paste the published calendar address. Outlook, Google and Apple " +
                               "all provide one; it usually ends in .ics and may begin webcal://",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    },
                    new TextBlock { Text = "Address", FontSize = 11, Opacity = 0.7, Margin = new Avalonia.Thickness(0, 12, 0, 0) },
                    address,
                    new TextBlock { Text = "Name", FontSize = 11, Opacity = 0.7, Margin = new Avalonia.Thickness(0, 10, 0, 0) },
                    name,
                    new TextBlock
                    {
                        Text = "Shown in the list and in the legend on the printed page. " +
                               "Leave it blank to use the suggestion.",
                        FontSize = 11,
                        Opacity = 0.65,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Margin = new Avalonia.Thickness(0, 4, 0, 0),
                    },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Margin = new Avalonia.Thickness(0, 14, 0, 0),
                        Children = { cancel, ok },
                    },
                },
            },
        };

        FeedEntry? result = null;

        ok.Click += (_, _) => { result = new FeedEntry(address.Text ?? string.Empty, name.Text); dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Opened += (_, _) => address.Focus();

        await dialog.ShowDialog(this);

        return result;
    }

    private static string SuggestionPlaceholder(string? address) =>
        CalendarSourcesViewModel.SuggestNameForUrl(address ?? string.Empty) is { } suggested
            ? suggested
            : "For example: Holidays";

    /// <summary>A one-line prompt, since Avalonia has no input dialog of its own.</summary>
    private async Task<string?> AskForTextAsync(
        string title,
        string explanation,
        string placeholder,
        string? initialText = null)
    {
        var box = new TextBox
        {
            PlaceholderText = placeholder,
            Text = initialText,
            Margin = new Avalonia.Thickness(0, 10, 0, 0),
        };

        var ok = new Button { Content = "Add", Width = 90, IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 90, Margin = new Avalonia.Thickness(0, 0, 8, 0) };

        var dialog = new Window
        {
            Title = title,
            Width = 560,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Children =
                {
                    new TextBlock { Text = explanation, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    box,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Margin = new Avalonia.Thickness(0, 14, 0, 0),
                        Children = { cancel, ok },
                    },
                },
            },
        };

        string? result = null;

        ok.Click += (_, _) => { result = box.Text; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();

        // Selected, not just focused: the suggested name is usually right, so Enter accepts it,
        // and typing replaces it without having to clear the box first.
        dialog.Opened += (_, _) => { box.Focus(); box.SelectAll(); };

        await dialog.ShowDialog(this);

        return result;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
