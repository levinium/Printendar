using Printendar.Core.Model;
using Printendar.Sources.Ics;

namespace Printendar.Core.Tests.Sources;

/// <summary>
/// Reading an iCalendar file into occurrences.
/// </summary>
/// <remarks>
/// The route that needs no account and no administrator: export a calendar from anywhere,
/// open the file, print it. For a public tool this is the only path a stranger can use the
/// minute they download it.
///
/// It is also the fiddliest source. Microsoft 365 and Google expand recurrence server side;
/// here Printendar has to do it, and the files come from every calendar program ever written.
/// </remarks>
public class IcsCalendarReaderTests
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static readonly DateSpan March2026 =
        new(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

    private static string Wrap(string body) => $"""
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Printendar//Test//EN
        {body}
        END:VCALENDAR
        """;

    private static IReadOnlyList<CalendarEvent> Read(
        string body,
        DateSpan? window = null,
        TimeZoneInfo? zone = null) =>
        IcsCalendarReader.Read(Wrap(body), "ics-1", window ?? March2026, zone ?? NewYork);

    [Fact]
    public void Reads_a_timed_event()
    {
        var events = Read("""
            BEGIN:VEVENT
            UID:evt-1
            SUMMARY:Board meeting
            LOCATION:Room 3
            DTSTART:20260317T140000Z
            DTEND:20260317T150000Z
            END:VEVENT
            """);

        var single = Assert.Single(events);

        Assert.Equal("Board meeting", single.Subject);
        Assert.Equal("Room 3", single.Location);
        Assert.False(single.IsAllDay);
        Assert.Equal(new DateOnly(2026, 3, 17), single.Days.First);
    }

    [Fact]
    public void Converts_a_utc_timestamp_into_the_display_zone()
    {
        // 14:00 UTC on 17 March 2026 is 10:00 in New York, which is on daylight saving by then.
        var single = Assert.Single(Read("""
            BEGIN:VEVENT
            UID:evt-1
            SUMMARY:Board meeting
            DTSTART:20260317T140000Z
            DTEND:20260317T150000Z
            END:VEVENT
            """));

        Assert.Equal(10, single.Start!.Value.Hour);
    }

    [Fact]
    public void An_all_day_event_keeps_its_dates_and_has_no_clock_time()
    {
        // VALUE=DATE means all day, and DTEND is exclusive: this is one day, the 5th.
        var single = Assert.Single(Read("""
            BEGIN:VEVENT
            UID:holiday
            SUMMARY:Public holiday
            DTSTART;VALUE=DATE:20260305
            DTEND;VALUE=DATE:20260306
            END:VEVENT
            """));

        Assert.True(single.IsAllDay);
        Assert.Null(single.Start);
        Assert.Equal(new DateOnly(2026, 3, 5), single.Days.First);
        Assert.Equal(1, single.Days.DayCount);
    }

    [Fact]
    public void A_multi_day_all_day_event_spans_the_right_days()
    {
        var single = Assert.Single(Read("""
            BEGIN:VEVENT
            UID:conf
            SUMMARY:Conference
            DTSTART;VALUE=DATE:20260322
            DTEND;VALUE=DATE:20260325
            END:VEVENT
            """));

        Assert.Equal(new DateOnly(2026, 3, 22), single.Days.First);
        Assert.Equal(new DateOnly(2026, 3, 24), single.Days.Last);
    }

    [Fact]
    public void An_all_day_event_with_no_end_lasts_one_day()
    {
        // Plenty of exporters omit DTEND entirely.
        var single = Assert.Single(Read("""
            BEGIN:VEVENT
            UID:holiday
            SUMMARY:Public holiday
            DTSTART;VALUE=DATE:20260305
            END:VEVENT
            """));

        Assert.Equal(1, single.Days.DayCount);
        Assert.Equal(new DateOnly(2026, 3, 5), single.Days.First);
    }

    [Fact]
    public void Expands_a_weekly_recurrence_across_the_month()
    {
        // The reason this cannot just parse and stop. A weekly standup has to become one chip
        // on every matching day, or the printed calendar shows it once and is simply wrong.
        var events = Read("""
            BEGIN:VEVENT
            UID:standup
            SUMMARY:Standup
            DTSTART:20260302T140000Z
            DTEND:20260302T141500Z
            RRULE:FREQ=WEEKLY;BYDAY=MO
            END:VEVENT
            """);

        // Mondays in March 2026: 2, 9, 16, 23, 30.
        Assert.Equal(5, events.Count);
        Assert.All(events, e => Assert.Equal(DayOfWeek.Monday, e.Days.First.DayOfWeek));
    }

    [Fact]
    public void A_recurrence_anchored_to_a_zone_keeps_its_local_time_across_a_dst_change()
    {
        // The realistic case, and the one that matters. Real exporters anchor a recurring
        // event to a named zone precisely so a 09:00 standup stays at 09:00 all year.
        // Daylight saving begins on 8 March 2026, so occurrences either side of it must both
        // read 09:00 locally even though they are an hour apart in absolute time.
        var events = Read("""
            BEGIN:VEVENT
            UID:standup
            SUMMARY:Standup
            DTSTART;TZID=America/New_York:20260302T090000
            DTEND;TZID=America/New_York:20260302T091500
            RRULE:FREQ=WEEKLY;BYDAY=MO
            END:VEVENT
            """);

        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.Equal(9, e.Start!.Value.Hour));

        // Spans the transition, so this is genuinely testing it rather than one side of it.
        Assert.Contains(events, e => e.Days.First < new DateOnly(2026, 3, 8));
        Assert.Contains(events, e => e.Days.First > new DateOnly(2026, 3, 8));
    }

    [Fact]
    public void A_recurrence_anchored_in_utc_is_allowed_to_shift_across_a_dst_change()
    {
        // The counterpart, and deliberately the opposite expectation. A UTC-anchored series is
        // fixed in absolute time, so its local clock time does move when daylight saving
        // starts. Both readings are correct; which one applies is decided by the file.
        var events = Read("""
            BEGIN:VEVENT
            UID:standup
            SUMMARY:Standup
            DTSTART:20260302T140000Z
            DTEND:20260302T141500Z
            RRULE:FREQ=WEEKLY;BYDAY=MO
            END:VEVENT
            """);

        var beforeDst = events.Single(e => e.Days.First == new DateOnly(2026, 3, 2));
        var afterDst = events.Single(e => e.Days.First == new DateOnly(2026, 3, 9));

        Assert.Equal(9, beforeDst.Start!.Value.Hour);
        Assert.Equal(10, afterDst.Start!.Value.Hour);
    }

    [Fact]
    public void Honours_an_exception_date_in_a_recurrence()
    {
        var events = Read("""
            BEGIN:VEVENT
            UID:standup
            SUMMARY:Standup
            DTSTART:20260302T140000Z
            DTEND:20260302T141500Z
            RRULE:FREQ=WEEKLY;BYDAY=MO
            EXDATE:20260316T140000Z
            END:VEVENT
            """);

        Assert.Equal(4, events.Count);
        Assert.DoesNotContain(events, e => e.Days.First == new DateOnly(2026, 3, 16));
    }

    [Fact]
    public void Stops_a_never_ending_recurrence_at_the_window()
    {
        // A daily rule with no UNTIL runs forever. Expanding beyond the window would hang the
        // program on a file that is perfectly valid.
        var events = Read("""
            BEGIN:VEVENT
            UID:daily
            SUMMARY:Daily
            DTSTART:20200101T140000Z
            DTEND:20200101T141500Z
            RRULE:FREQ=DAILY
            END:VEVENT
            """);

        Assert.Equal(31, events.Count);
        Assert.All(events, e => Assert.Equal(3, e.Days.First.Month));
    }

    [Fact]
    public void Ignores_events_outside_the_window()
    {
        var events = Read("""
            BEGIN:VEVENT
            UID:old
            SUMMARY:Last year
            DTSTART:20250317T140000Z
            DTEND:20250317T150000Z
            END:VEVENT
            """);

        Assert.Empty(events);
    }

    [Fact]
    public void Reads_a_cancelled_event_as_cancelled()
    {
        var single = Assert.Single(Read("""
            BEGIN:VEVENT
            UID:evt-1
            SUMMARY:Cancelled thing
            STATUS:CANCELLED
            DTSTART:20260317T140000Z
            DTEND:20260317T150000Z
            END:VEVENT
            """));

        Assert.Equal(EventStatus.Cancelled, single.Status);
    }

    [Fact]
    public void Reads_a_private_event_as_private()
    {
        var single = Assert.Single(Read("""
            BEGIN:VEVENT
            UID:evt-1
            SUMMARY:Personal thing
            CLASS:PRIVATE
            DTSTART:20260317T140000Z
            DTEND:20260317T150000Z
            END:VEVENT
            """));

        Assert.Equal(EventSensitivity.Private, single.Sensitivity);
    }

    [Fact]
    public void An_event_with_no_summary_still_appears()
    {
        // Dropping it would leave a hole in the printed day with no hint anything was there.
        var single = Assert.Single(Read("""
            BEGIN:VEVENT
            UID:evt-1
            DTSTART:20260317T140000Z
            DTEND:20260317T150000Z
            END:VEVENT
            """));

        Assert.False(string.IsNullOrWhiteSpace(single.Subject));
    }

    [Fact]
    public void One_broken_event_does_not_lose_the_rest_of_the_file()
    {
        // Real files from real programs contain surprises. Losing the whole month over one bad
        // entry is far worse than losing that entry.
        var events = Read("""
            BEGIN:VEVENT
            UID:good
            SUMMARY:Good event
            DTSTART:20260317T140000Z
            DTEND:20260317T150000Z
            END:VEVENT
            BEGIN:VEVENT
            UID:no-start
            SUMMARY:Missing its start
            END:VEVENT
            """);

        Assert.Contains(events, e => e.Subject == "Good event");
    }

    [Fact]
    public void A_file_that_is_not_a_calendar_at_all_reports_something_useful()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => IcsCalendarReader.Read("this is not a calendar", "ics-1", March2026, NewYork));

        Assert.Contains("calendar", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_event_read_satisfies_its_own_invariants()
    {
        var events = Read("""
            BEGIN:VEVENT
            UID:a
            SUMMARY:Timed
            DTSTART:20260317T140000Z
            DTEND:20260317T150000Z
            END:VEVENT
            BEGIN:VEVENT
            UID:b
            SUMMARY:All day
            DTSTART;VALUE=DATE:20260318
            DTEND;VALUE=DATE:20260319
            END:VEVENT
            """);

        Assert.Equal(2, events.Count);
        Assert.All(events, e => e.Validate());
    }
}
