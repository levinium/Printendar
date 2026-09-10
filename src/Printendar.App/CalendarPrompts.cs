using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Printendar.App.Sources;
using Printendar.Sources.Ics;

namespace Printendar.App;

/// <summary>
/// The small dialogs that adding and removing a calendar need.
/// </summary>
/// <remarks>
/// Built in code rather than as XAML windows because each is a caption, a field or two and two
/// buttons. A file, a class and a code-behind apiece would be more ceremony than the thing
/// being asked for, and Avalonia offers no message box of its own.
///
/// Separate from the window that calls them so the main window stays about printing. These
/// moved here when the manage-calendars window was folded into the sidebar: the window went,
/// the prompts it owned did not.
/// </remarks>
public static class CalendarPrompts
{
    /// <summary>The address and name of a feed, as typed. Null when cancelled.</summary>
    public sealed record FeedEntry(string Address, string? Name);

    /// <summary>
    /// Asks for what to call a feed and where it lives, together.
    /// </summary>
    /// <remarks>
    /// The name is asked for here rather than left to be corrected afterwards because the
    /// automatic answer is so often wrong: a published address gives us the host and little
    /// else, so a Google feed arrives called "calendar.google.com" whatever is actually in it.
    /// The suggestion is still offered as the address is typed, so the name can be left alone
    /// when it happens to be sensible.
    ///
    /// Name first, because that is the order somebody thinks in: this is the school calendar,
    /// and here is where it lives. It also puts the field with a sensible default first and the
    /// one that must be right last, next to the button that acts on it.
    ///
    /// The address is checked on every keystroke rather than when Add is pressed. Add used to
    /// close the dialog whatever was in the box, so an empty one silently added nothing at all
    /// and left somebody looking at an unchanged list wondering what they had done wrong.
    /// </remarks>
    public static async Task<FeedEntry?> AskForFeedAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var name = new TextBox
        {
            PlaceholderText = SuggestionPlaceholder(null),
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
        };

        var address = new TextBox
        {
            PlaceholderText = "https://",
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
        };

        var ok = new Button { Content = "Add", Width = 90, IsDefault = true, IsEnabled = false };
        var cancel = new Button { Content = "Cancel", Width = 90, Margin = new Avalonia.Thickness(0, 0, 8, 0) };

        // One block doing two jobs: what a good address looks like while the box is empty, and
        // what is wrong with this one once something has been typed. Two blocks would mean the
        // advice and the complaint arguing with each other on screen.
        var addressNote = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
        };

        void Revalidate()
        {
            var typed = address.Text;
            var problem = IcsUrlCalendarSource.DescribeAddressProblem(typed);

            ok.IsEnabled = problem is null;

            // Nothing typed yet is not a mistake, so it reads as guidance rather than as a
            // complaint about a box nobody has reached.
            var blank = string.IsNullOrWhiteSpace(typed);

            addressNote.Text = problem ?? "Read fresh every time you print.";
            addressNote.Foreground = problem is not null && !blank ? Brushes.Firebrick : null;
            addressNote.Opacity = problem is not null && !blank ? 1 : 0.65;

            // Kept in step with the address so the name that would be used is visible before
            // the feed is added, rather than discovered in the list afterwards.
            name.PlaceholderText = SuggestionPlaceholder(typed);
        }

        address.TextChanged += (_, _) => Revalidate();

        Revalidate();

        var dialog = Shell("Add a calendar link", new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Children =
            {
                new TextBlock
                {
                    Text = "Outlook, Google and Apple can each publish a calendar as a link. " +
                           "Paste that link here and Printendar will re-read it before every print.",
                    TextWrapping = TextWrapping.Wrap,
                },
                Label("Name", top: 12),
                name,
                new TextBlock
                {
                    Text = "Shown in the list and in the legend on the printed page. " +
                           "Leave it blank to use the suggestion.",
                    FontSize = 11,
                    Opacity = 0.65,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(0, 4, 0, 0),
                },
                Label("Address", top: 12),
                address,
                addressNote,
                Buttons(cancel, ok),
            },
        });

        FeedEntry? result = null;

        ok.Click += (_, _) =>
        {
            // Asked again rather than trusting the button's state. Enter reaches the default
            // button, and a check that lives only in one enabled flag is one refactor away from
            // being no check at all.
            if (IcsUrlCalendarSource.DescribeAddressProblem(address.Text) is not null)
            {
                Revalidate();
                return;
            }

            result = new FeedEntry(address.Text ?? string.Empty, name.Text);
            dialog.Close();
        };

        cancel.Click += (_, _) => dialog.Close();

        dialog.Opened += (_, _) => name.Focus();

        await dialog.ShowDialog(owner);

        return result;
    }

    /// <summary>Asks what a calendar should be called. Null when cancelled.</summary>
    public static async Task<string?> AskForNameAsync(
        Window owner,
        string explanation,
        string suggested)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var box = new TextBox
        {
            PlaceholderText = suggested,
            Text = suggested,
            Margin = new Avalonia.Thickness(0, 10, 0, 0),
        };

        var ok = new Button { Content = "Add", Width = 90, IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 90, Margin = new Avalonia.Thickness(0, 0, 8, 0) };

        var dialog = Shell("Name this calendar", new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Children =
            {
                new TextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap },
                box,
                Buttons(cancel, ok),
            },
        });

        string? result = null;

        ok.Click += (_, _) => { result = box.Text; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();

        // Selected, not just focused: the suggested name is usually right, so Enter accepts it,
        // and typing replaces it without having to clear the box first.
        dialog.Opened += (_, _) => { box.Focus(); box.SelectAll(); };

        await dialog.ShowDialog(owner);

        return result;
    }

    /// <summary>
    /// Asks before doing something that cannot be undone.
    /// </summary>
    /// <remarks>
    /// Cancel is the default button, so Enter and Escape both decline. A confirmation whose
    /// dangerous answer is one thoughtless keypress away is a formality rather than a question.
    ///
    /// The confirming button says what it does rather than "OK", because "OK" next to a
    /// question is ambiguous about which way it answers.
    /// </remarks>
    public static async Task<bool> ConfirmAsync(
        Window owner,
        string title,
        string question,
        string confirmLabel)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var confirm = new Button { Content = confirmLabel, MinWidth = 90 };
        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 90,
            IsDefault = true,
            IsCancel = true,
            Margin = new Avalonia.Thickness(0, 0, 8, 0),
        };

        var dialog = Shell(title, new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Children =
            {
                new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap },
                Buttons(cancel, confirm),
            },
        }, width: 460);

        var answer = false;

        confirm.Click += (_, _) => { answer = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Opened += (_, _) => cancel.Focus();

        await dialog.ShowDialog(owner);

        return answer;
    }

    /// <summary>The name a feed would be given if the box were left empty.</summary>
    public static string SuggestionPlaceholder(string? address) =>
        CalendarSourcesViewModel.SuggestNameForUrl(address ?? string.Empty) is { } suggested
            ? suggested
            : "For example: Holidays";

    private static Window Shell(string title, Control content, int width = 560) => new()
    {
        Title = title,
        Width = width,
        SizeToContent = SizeToContent.Height,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Content = content,
    };

    private static TextBlock Label(string text, int top) => new()
    {
        Text = text,
        FontSize = 11,
        Opacity = 0.7,
        Margin = new Avalonia.Thickness(0, top, 0, 0),
    };

    private static StackPanel Buttons(Control cancel, Control confirm) => new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
        Margin = new Avalonia.Thickness(0, 14, 0, 0),
        Children = { cancel, confirm },
    };
}
