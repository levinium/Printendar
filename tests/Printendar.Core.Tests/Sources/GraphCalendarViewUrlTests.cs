using Printendar.Core.Model;
using Printendar.Sources.Microsoft365;

namespace Printendar.Core.Tests.Sources;

/// <summary>
/// The calendarView request Printendar sends for a month.
/// </summary>
/// <remarks>
/// A grid cell is a local day, but Graph takes an absolute range. An event at 23:00 on the
/// last day of the month in New York is already the next day in UTC, so a range built from the
/// exact month boundaries drops it, and the user sees a calendar that is quietly missing
/// events at the edges.
/// </remarks>
public class GraphCalendarViewUrlTests
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

    private static readonly DateSpan March2026 =
        new(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

    private static (DateTimeOffset Start, DateTimeOffset End) RangeOf(DateSpan window, TimeZoneInfo zone)
    {
        var url = GraphCalendarSource.BuildCalendarViewUrl("cal-1", window, zone);
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);

        return (
            DateTimeOffset.Parse(query["startDateTime"]!, System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(query["endDateTime"]!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Uses_calendarView_rather_than_the_events_collection()
    {
        // Not interchangeable. Only calendarView expands a recurring series into occurrences;
        // /events would return the series master, so a weekly standup would appear once in the
        // month instead of on every weekday.
        var url = GraphCalendarSource.BuildCalendarViewUrl("cal-1", March2026, NewYork);

        Assert.Contains("/calendarView?", url, StringComparison.Ordinal);
        Assert.DoesNotContain("/events?", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Asks_for_the_whole_month_in_the_users_own_zone()
    {
        var (start, end) = RangeOf(March2026, NewYork);

        // Local midnight on 1 March in New York is 05:00 UTC, and the range reaches back
        // before it.
        Assert.True(start <= new DateTimeOffset(2026, 3, 1, 5, 0, 0, TimeSpan.Zero));
        Assert.True(end >= new DateTimeOffset(2026, 4, 1, 4, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Covers_a_late_event_on_the_last_day_of_the_month()
    {
        // 23:30 on 31 March in New York is 03:30 UTC on 1 April. An exact range would miss it.
        var (_, end) = RangeOf(March2026, NewYork);

        var lateEvent = new DateTimeOffset(2026, 4, 1, 3, 30, 0, TimeSpan.Zero);

        Assert.True(end > lateEvent, $"The range ends at {end:o}, before a 23:30 local event at {lateEvent:o}.");
    }

    [Fact]
    public void Covers_an_early_event_on_the_first_day_of_the_month()
    {
        // 00:30 on 1 March in Tokyo is 15:30 UTC on 28 February.
        var (start, _) = RangeOf(March2026, Tokyo);

        var earlyEvent = new DateTimeOffset(2026, 2, 28, 15, 30, 0, TimeSpan.Zero);

        Assert.True(start < earlyEvent, $"The range starts at {start:o}, after a 00:30 local event at {earlyEvent:o}.");
    }

    [Fact]
    public void Covers_the_adjacent_days_the_grid_shows()
    {
        // March 2026 runs to Saturday 4 April on a Sunday-start grid, and those cells are
        // drawn, so their events have to be fetched too.
        var visible = new DateSpan(new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 4));

        var (start, end) = RangeOf(visible, NewYork);

        Assert.True(start <= new DateTimeOffset(2026, 3, 1, 5, 0, 0, TimeSpan.Zero));
        Assert.True(end >= new DateTimeOffset(2026, 4, 5, 4, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Requests_only_the_fields_the_layout_uses()
    {
        // A calendarView without $select returns the body of every event, which for a busy
        // month is megabytes of HTML nobody is going to print.
        var url = GraphCalendarSource.BuildCalendarViewUrl("cal-1", March2026, NewYork);

        Assert.Contains("$select=", url, StringComparison.Ordinal);
        Assert.DoesNotContain("body", url, StringComparison.Ordinal);

        foreach (var field in (string[])["id", "subject", "isAllDay", "start", "end", "seriesMasterId"])
        {
            Assert.Contains(field, url, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Escapes_a_calendar_id_that_contains_url_characters()
    {
        // Graph calendar ids are long base64-ish strings that routinely contain "/" and "+".
        var url = GraphCalendarSource.BuildCalendarViewUrl("AAMk/AGI+1==", March2026, NewYork);

        Assert.DoesNotContain("AAMk/AGI+1==", url, StringComparison.Ordinal);
        Assert.Contains("AAMk%2FAGI%2B1%3D%3D", url, StringComparison.Ordinal);
    }
}
