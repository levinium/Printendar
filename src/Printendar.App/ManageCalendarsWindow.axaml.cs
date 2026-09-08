using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
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

            try
            {
                // Each file becomes its own entry, so the list matches what was picked.
                await _sources.AddAsync(CalendarSourcesViewModel.ForFile(path));
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
        var url = await AskForTextAsync(
            "Add a calendar feed",
            "Paste the published calendar address. Outlook, Google and Apple all provide one; " +
            "it usually ends in .ics and may begin webcal://",
            "https://");

        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            await _sources.AddAsync(CalendarSourcesViewModel.ForUrl(url, null));
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

    /// <summary>A one-line prompt, since Avalonia has no input dialog of its own.</summary>
    private async Task<string?> AskForTextAsync(string title, string explanation, string watermark)
    {
        var box = new TextBox { PlaceholderText = watermark, Margin = new Avalonia.Thickness(0, 10, 0, 0) };
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

        await dialog.ShowDialog(this);

        return result;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
