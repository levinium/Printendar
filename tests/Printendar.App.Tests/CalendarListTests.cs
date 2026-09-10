using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Printendar.App.Sources;
using Printendar.Core.Settings;
using Printendar.Core.Sources;

namespace Printendar.App.Tests;

/// <summary>
/// The calendar list in the sidebar, opened for real without a screen.
/// </summary>
/// <remarks>
/// This is where the app's own bugs have actually been. Compiled bindings catch a misspelled
/// property at build time, but nothing catches a control whose DataContext turns out to be a
/// different type than the handler expects, or a template that quietly renders nothing. Both
/// have happened here, and both were found by reading rather than by failing.
///
/// Every test drives the window Printendar really ships. A view model exercised on its own
/// would prove the state is right and say nothing about whether anything draws it.
/// </remarks>
public sealed class CalendarListTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "printendar-app-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FixedSettingsLocation(string directory) : ISettingsLocation
    {
        public string Directory { get; } = directory;
    }

    /// <summary>
    /// Opens the window with calendars already saved, and lets it lay itself out.
    /// </summary>
    /// <remarks>
    /// Pointed at a temporary directory, never the real per-user folder: these tests tick
    /// boxes and rename things, and doing that to whatever the person running them had saved
    /// would be unforgivable.
    /// </remarks>
    private MainWindow OpenWith(params ConfiguredSource[] sources)
    {
        var store = new SettingsStore(new FixedSettingsLocation(_directory));

        store.Save(new AppSettings { Sources = [.. sources] });

        var model = new MainViewModel(store);
        var window = new MainWindow(model);

        window.Show();

        // The window loads its calendars without awaiting, so the list is empty until that
        // finishes. Running the queue is what a real frame would have done.
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    private static ConfiguredSource Feed(string id, string name) =>
        new(id, CalendarSourceKind.IcsUrl, name, $"https://example.com/{id}.ics");

    private static List<T> Descendants<T>(Visual root, string name)
        where T : Control =>
        [.. root.GetVisualDescendants().OfType<T>().Where(c => c.Name == name)];

    private static List<SourceEntry> Cards(Visual root) =>
        [.. root.GetVisualDescendants()
            .OfType<Border>()
            .Select(b => b.DataContext)
            .OfType<SourceEntry>()
            .Distinct()];

    [AvaloniaFact]
    public void Every_saved_calendar_gets_a_card()
    {
        var window = OpenWith(Feed("a", "Family"), Feed("b", "Work"), Feed("c", "Holidays"));

        Assert.Equal(["Family", "Work", "Holidays"], Cards(window).Select(c => c.DisplayName));
    }

    [AvaloniaFact]
    public void A_calendar_is_drawn_once_rather_than_as_its_own_child()
    {
        // The list used to nest each source's calendars underneath it. Every source holds
        // exactly one calendar carrying the source's own name, so every calendar appeared
        // twice: once as a heading and once as the only row beneath it.
        var window = OpenWith(Feed("a", "Family"));

        // Visible ones only. The card carries two blocks for the name and shows whichever
        // suits the state: one inside the tick for a calendar that loaded, one plain for a
        // calendar that has not. Counting both would pass whatever the template did.
        var shown = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Count(t => t.Text == "Family" && t.IsVisible);

        Assert.Equal(1, shown);
    }

    [AvaloniaFact]
    public void The_buttons_on_a_card_can_tell_which_calendar_they_belong_to()
    {
        // The regression this is here for: the colour flyout stopped working when the row's
        // DataContext changed type, because its handler matched only the old one. Nothing
        // failed, the button simply did nothing. Every action on a card reaches its calendar
        // through DataContext, so that is the thing worth pinning.
        var window = OpenWith(Feed("a", "Family"));

        var rename = Assert.Single(Descendants<Button>(window, "RenameButton"));

        Assert.IsType<SourceEntry>(rename.DataContext);
        Assert.Equal("Family", ((SourceEntry)rename.DataContext!).DisplayName);
    }

    [AvaloniaFact]
    public void A_card_is_complete_before_the_feed_has_been_fetched()
    {
        // An iCalendar source describes itself without going near the network: it holds one
        // calendar, named after the source. So the tick and the swatch are there immediately,
        // for an address that has never answered and may never answer.
        //
        // That is the right behaviour and worth pinning, because the alternative reading is
        // tempting and wrong. A card is not a promise that the feed works; whether it can be
        // read is settled at print time, which is what the warning before printing is for.
        var window = OpenWith(Feed("a", "Nowhere"));

        var entry = Assert.Single(Cards(window));

        Assert.True(entry.HasCalendar);
        Assert.Equal("Nowhere", entry.DisplayName);

        var tick = Assert.Single(
            window.GetVisualDescendants().OfType<CheckBox>(),
            c => c.DataContext is SourceEntry);

        Assert.True(tick.IsVisible);
        Assert.True(tick.IsChecked);
    }

    [AvaloniaFact]
    public void The_version_is_on_screen()
    {
        // Not a formality: the label reads an assembly attribute, and an attribute that is not
        // emitted reads back as nothing. A version that works in a unit test and is blank in
        // the shipped window is exactly the failure this catches.
        var window = OpenWith();

        var label = Assert.Single(Descendants<TextBlock>(window, "VersionLabel"));

        Assert.True(label.IsVisible);
        Assert.StartsWith("v", label.Text ?? string.Empty, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void With_nothing_added_the_window_still_opens_and_offers_a_way_in()
    {
        var window = OpenWith();

        Assert.Empty(Cards(window));
        Assert.Single(Descendants<Button>(window, "AddCalendar"));
    }
}
