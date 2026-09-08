using System.Text.Json;
using Printendar.Core.Model;
using Printendar.Sources.Microsoft365;

namespace Printendar.Core.Tests.Sources;

/// <summary>
/// Turning a Microsoft Graph calendarView entry into a Printendar occurrence.
/// </summary>
/// <remarks>
/// Driven by recorded response shapes rather than a live account, so the awkward cases can be
/// exercised deliberately and the suite runs offline in CI.
///
/// The trap that shapes this whole file: Graph writes its timestamps without an offset, as
/// "2026-03-17T14:00:00.0000000", and states the zone in a sibling field. Parsing one straight
/// into a DateTimeOffset silently attaches the machine's own offset, which puts every event on
/// the wrong clock time for anyone outside that zone, and on the wrong day for some of them.
/// </remarks>
public class GraphEventMapperTests
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static CalendarEvent Map(string json, TimeZoneInfo? displayZone = null)
    {
        using var document = JsonDocument.Parse(json);

        return GraphEventMapper.Map(document.RootElement, "cal-1", displayZone ?? NewYork)!;
    }

    private const string TimedEvent = """
    {
      "id": "AAMkAGI1",
      "subject": "Board meeting",
      "isAllDay": false,
      "isCancelled": false,
      "sensitivity": "normal",
      "showAs": "busy",
      "categories": ["Governance"],
      "location": { "displayName": "Room 3" },
      "organizer": { "emailAddress": { "name": "Sam Patel", "address": "sam@example.com" } },
      "responseStatus": { "response": "accepted" },
      "start": { "dateTime": "2026-03-17T14:00:00.0000000", "timeZone": "UTC" },
      "end":   { "dateTime": "2026-03-17T15:00:00.0000000", "timeZone": "UTC" }
    }
    """;

    [Fact]
    public void Reads_the_basics_of_a_timed_event()
    {
        var mapped = Map(TimedEvent);

        Assert.Equal("AAMkAGI1", mapped.Id);
        Assert.Equal("Board meeting", mapped.Subject);
        Assert.Equal("Room 3", mapped.Location);
        Assert.Equal("Sam Patel", mapped.Organizer);
        Assert.Equal("cal-1", mapped.CalendarId);
        Assert.False(mapped.IsAllDay);
        Assert.Equal(["Governance"], mapped.Categories);
    }

    [Fact]
    public void Interprets_the_timestamp_in_the_zone_Graph_states_not_the_local_one()
    {
        // The headline trap. 14:00 UTC on 17 March 2026 is 10:00 in New York, which is EDT by
        // then. If the offsetless string were parsed as local, this would read 14:00.
        var mapped = Map(TimedEvent);

        Assert.Equal(10, mapped.Start!.Value.Hour);
        Assert.Equal(new DateOnly(2026, 3, 17), mapped.Days.First);
    }

    [Fact]
    public void An_all_day_event_carries_dates_and_no_clock_time()
    {
        // Graph sends an exclusive end: the 22nd to the 25th is a three day event.
        var mapped = Map("""
        {
          "id": "conf",
          "subject": "Conference in Chicago",
          "isAllDay": true,
          "isCancelled": false,
          "start": { "dateTime": "2026-03-22T00:00:00.0000000", "timeZone": "UTC" },
          "end":   { "dateTime": "2026-03-25T00:00:00.0000000", "timeZone": "UTC" }
        }
        """);

        Assert.True(mapped.IsAllDay);
        Assert.Null(mapped.Start);
        Assert.Null(mapped.End);
        Assert.Equal(new DateOnly(2026, 3, 22), mapped.Days.First);
        Assert.Equal(new DateOnly(2026, 3, 24), mapped.Days.Last);
        Assert.Equal(3, mapped.Days.DayCount);
    }

    [Fact]
    public void A_single_day_all_day_event_does_not_spill_onto_the_next_day()
    {
        var mapped = Map("""
        {
          "id": "holiday",
          "subject": "Public holiday",
          "isAllDay": true,
          "isCancelled": false,
          "start": { "dateTime": "2026-03-05T00:00:00.0000000", "timeZone": "UTC" },
          "end":   { "dateTime": "2026-03-06T00:00:00.0000000", "timeZone": "UTC" }
        }
        """);

        Assert.Equal(1, mapped.Days.DayCount);
        Assert.Equal(new DateOnly(2026, 3, 5), mapped.Days.First);
    }

    [Fact]
    public void An_all_day_event_is_not_shifted_by_the_display_zone()
    {
        // Tokyo is far enough east that a naive conversion of midnight would move the event a
        // day. All-day events must be immune to this.
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

        var mapped = Map("""
        {
          "id": "holiday",
          "subject": "Public holiday",
          "isAllDay": true,
          "isCancelled": false,
          "start": { "dateTime": "2026-03-05T00:00:00.0000000", "timeZone": "UTC" },
          "end":   { "dateTime": "2026-03-06T00:00:00.0000000", "timeZone": "UTC" }
        }
        """, tokyo);

        Assert.Equal(new DateOnly(2026, 3, 5), mapped.Days.First);
    }

    [Theory]
    [InlineData("accepted", "busy", false, EventStatus.Confirmed)]
    [InlineData("declined", "free", false, EventStatus.Declined)]
    [InlineData("tentativelyAccepted", "tentative", false, EventStatus.Tentative)]
    [InlineData("accepted", "tentative", false, EventStatus.Tentative)]
    [InlineData("accepted", "busy", true, EventStatus.Cancelled)]
    public void Maps_the_status_a_user_would_recognise(
        string response,
        string showAs,
        bool isCancelled,
        EventStatus expected)
    {
        // Graph splits this across three fields. Cancellation wins over everything, then a
        // declined invitation, then whether the organizer marked it tentative.
        var mapped = Map($$"""
        {
          "id": "x",
          "subject": "Meeting",
          "isAllDay": false,
          "isCancelled": {{isCancelled.ToString().ToLowerInvariant()}},
          "showAs": "{{showAs}}",
          "responseStatus": { "response": "{{response}}" },
          "start": { "dateTime": "2026-03-17T14:00:00.0000000", "timeZone": "UTC" },
          "end":   { "dateTime": "2026-03-17T15:00:00.0000000", "timeZone": "UTC" }
        }
        """);

        Assert.Equal(expected, mapped.Status);
    }

    [Theory]
    [InlineData("normal", EventSensitivity.Normal)]
    [InlineData("personal", EventSensitivity.Personal)]
    [InlineData("private", EventSensitivity.Private)]
    [InlineData("confidential", EventSensitivity.Confidential)]
    public void Maps_sensitivity(string sensitivity, EventSensitivity expected)
    {
        var mapped = Map($$"""
        {
          "id": "x",
          "subject": "Meeting",
          "isAllDay": false,
          "sensitivity": "{{sensitivity}}",
          "start": { "dateTime": "2026-03-17T14:00:00.0000000", "timeZone": "UTC" },
          "end":   { "dateTime": "2026-03-17T15:00:00.0000000", "timeZone": "UTC" }
        }
        """);

        Assert.Equal(expected, mapped.Sensitivity);
    }

    [Fact]
    public void Records_that_an_occurrence_belongs_to_a_repeating_series()
    {
        var mapped = Map("""
        {
          "id": "occurrence-3",
          "seriesMasterId": "master-1",
          "subject": "Standup",
          "isAllDay": false,
          "start": { "dateTime": "2026-03-17T13:15:00.0000000", "timeZone": "UTC" },
          "end":   { "dateTime": "2026-03-17T13:30:00.0000000", "timeZone": "UTC" }
        }
        """);

        Assert.Equal("master-1", mapped.SeriesMasterId);
    }

    [Fact]
    public void Survives_an_entry_with_almost_nothing_in_it()
    {
        // Graph omits fields the mailbox does not have, and a private event in a shared
        // calendar arrives with no subject at all. Throwing here would fail the whole month.
        var mapped = Map("""
        {
          "id": "sparse",
          "isAllDay": false,
          "start": { "dateTime": "2026-03-17T14:00:00.0000000", "timeZone": "UTC" },
          "end":   { "dateTime": "2026-03-17T15:00:00.0000000", "timeZone": "UTC" }
        }
        """);

        Assert.NotNull(mapped);
        Assert.False(string.IsNullOrEmpty(mapped.Subject));
        Assert.Null(mapped.Location);
    }

    [Fact]
    public void Skips_an_entry_with_no_usable_times()
    {
        // Better to drop one malformed entry than to fail the month it appears in.
        using var document = JsonDocument.Parse("""
        { "id": "broken", "subject": "No times", "isAllDay": false }
        """);

        Assert.Null(GraphEventMapper.Map(document.RootElement, "cal-1", NewYork));
    }

    [Fact]
    public void Honours_a_non_utc_zone_named_by_Graph()
    {
        // Graph returns whatever zone the Prefer header asked for, using Windows ids.
        var mapped = Map("""
        {
          "id": "x",
          "subject": "Meeting",
          "isAllDay": false,
          "start": { "dateTime": "2026-03-17T09:00:00.0000000", "timeZone": "Eastern Standard Time" },
          "end":   { "dateTime": "2026-03-17T10:00:00.0000000", "timeZone": "Eastern Standard Time" }
        }
        """);

        Assert.Equal(9, mapped.Start!.Value.Hour);
        Assert.Equal(new DateOnly(2026, 3, 17), mapped.Days.First);
    }

    [Fact]
    public void Every_mapped_event_satisfies_its_own_invariants()
    {
        // CalendarEvent.Validate is the contract the layout engine relies on. Running it here
        // means a provider quirk fails at the boundary that introduced it.
        Map(TimedEvent).Validate();
    }
}
