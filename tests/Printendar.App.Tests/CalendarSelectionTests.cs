using Printendar.App.Sources;
using Printendar.Core.Sources;

namespace Printendar.App.Tests;

/// <summary>
/// Which calendars are ticked, and whether that choice comes back.
/// </summary>
/// <remarks>
/// No window here. The restore rule is arithmetic over what was written down, and driving it
/// directly says what it does far more plainly than clicking a checkbox would.
/// </remarks>
public class CalendarSelectionTests
{
    private static SourceEntry Reopen(ConfiguredSource configured)
    {
        var entry = new SourceEntry(configured);

        // What happens on every start: the source is asked what it holds, and the entry
        // restores the ticks from the settings it was built with.
        entry.SetCalendars([new CalendarRef("s", "s", "Team", IsDefault: true)], []);

        return entry;
    }

    [Fact]
    public void A_calendar_nobody_has_expressed_an_opinion_about_starts_ticked()
    {
        // First sight of a source. Showing a connected calendar whose month is blank reads as
        // a failure, so the default calendar is on.
        var entry = Reopen(new ConfiguredSource("s", CalendarSourceKind.IcsUrl, "Team", "https://example.com/t.ics"));

        Assert.True(entry.Calendar!.IsSelected);
    }

    [Fact]
    public void Unticking_the_only_calendar_survives_a_restart()
    {
        // Unticking the last calendar wrote an empty list, and an empty list was read back as
        // "never chosen", so the calendar came back ticked on the next start. Somebody who had
        // deliberately turned a calendar off found it printing again the following morning,
        // which is worse than the setting not existing.
        var entry = Reopen(new ConfiguredSource("s", CalendarSourceKind.IcsUrl, "Team", "https://example.com/t.ics"));

        entry.Calendar!.IsSelected = false;

        var reopened = Reopen(entry.Configured);

        Assert.False(reopened.Calendar!.IsSelected);
    }

    [Fact]
    public void Ticking_it_again_also_survives_a_restart()
    {
        var entry = Reopen(new ConfiguredSource("s", CalendarSourceKind.IcsUrl, "Team", "https://example.com/t.ics"));

        entry.Calendar!.IsSelected = false;
        entry.Calendar.IsSelected = true;

        Assert.True(Reopen(entry.Configured).Calendar!.IsSelected);
    }
}
