using Printendar.Core.Model;
using Printendar.Core.Time;

namespace Printendar.Core.Tests.Time;

/// <summary>
/// Turning what a provider sends into the days a calendar cell can be filled from.
/// </summary>
/// <remarks>
/// This file carries more weight than its size suggests. Every provider Printendar reads from
/// expresses an all-day event's end date exclusively, so an event on the 5th arrives as 5th to
/// 6th. Converting that in more than one place is the single most common way calendar software
/// puts an event on one day too many, so it is converted exactly once, here.
///
/// The other half is that an all-day event has no clock time. Passing one through a time zone
/// conversion is how a whole-day event slides onto the previous evening for anyone west of the
/// organiser.
/// </remarks>
public class EventNormalizerTests
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

    // ------------------------------------------------------------------ all-day

    [Fact]
    public void A_one_day_all_day_event_occupies_exactly_that_day()
    {
        // Providers send 5th to 6th for an event that happens on the 5th.
        var span = EventNormalizer.NormalizeAllDay(new DateOnly(2026, 3, 5), endExclusive: new DateOnly(2026, 3, 6));

        Assert.Equal(new DateOnly(2026, 3, 5), span.First);
        Assert.Equal(new DateOnly(2026, 3, 5), span.Last);
        Assert.Equal(1, span.DayCount);
    }

    [Fact]
    public void A_three_day_all_day_event_occupies_three_days()
    {
        var span = EventNormalizer.NormalizeAllDay(new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 25));

        Assert.Equal(new DateOnly(2026, 3, 22), span.First);
        Assert.Equal(new DateOnly(2026, 3, 24), span.Last);
        Assert.Equal(3, span.DayCount);
    }

    [Fact]
    public void An_all_day_event_with_a_malformed_end_collapses_to_one_day()
    {
        // Seen from ICS feeds where DTEND is absent or equal to DTSTART. Better one day than a
        // span that iterates backwards.
        var span = EventNormalizer.NormalizeAllDay(new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 5));

        Assert.Equal(1, span.DayCount);
        Assert.Equal(new DateOnly(2026, 3, 5), span.First);
    }

    [Fact]
    public void All_day_dates_are_never_shifted_by_a_time_zone()
    {
        // The signature is the guarantee: NormalizeAllDay takes DateOnly and no zone, so it is
        // not possible to convert an all-day event onto the previous evening. This test exists
        // to make that a stated requirement rather than an accident of the current signature.
        var span = EventNormalizer.NormalizeAllDay(new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 6));

        Assert.Equal(new DateOnly(2026, 3, 5), span.First);
    }

    // ------------------------------------------------------------------ timed

    [Fact]
    public void A_timed_event_is_converted_into_the_display_zone()
    {
        // Daylight saving began on 8 March 2026, so 17 March is EDT at UTC-4 and 14:00 UTC is
        // 10:00 in New York. It would be 09:00 only under standard time.
        var start = new DateTimeOffset(2026, 3, 17, 14, 0, 0, TimeSpan.Zero);

        var result = EventNormalizer.NormalizeTimed(start, start.AddHours(1), NewYork);

        Assert.Equal(10, result.Start.Hour);
        Assert.Equal(new DateOnly(2026, 3, 17), result.Days.First);
        Assert.Equal(new DateOnly(2026, 3, 17), result.Days.Last);
    }

    [Fact]
    public void An_event_that_crosses_midnight_in_the_display_zone_occupies_both_days()
    {
        // 03:00 UTC on the 18th is 23:00 on the 17th in New York, so a two hour meeting there
        // runs into the 18th.
        var start = new DateTimeOffset(2026, 3, 18, 3, 0, 0, TimeSpan.Zero);

        var result = EventNormalizer.NormalizeTimed(start, start.AddHours(2), NewYork);

        Assert.Equal(new DateOnly(2026, 3, 17), result.Days.First);
        Assert.Equal(new DateOnly(2026, 3, 18), result.Days.Last);
    }

    [Fact]
    public void The_display_zone_decides_which_day_an_event_lands_on()
    {
        // The same instant is the 17th in New York and the 18th in Tokyo. Whoever is printing
        // the calendar should see it on their own day.
        var start = new DateTimeOffset(2026, 3, 17, 23, 0, 0, TimeSpan.Zero);

        var inNewYork = EventNormalizer.NormalizeTimed(start, start.AddMinutes(30), NewYork);
        var inTokyo = EventNormalizer.NormalizeTimed(start, start.AddMinutes(30), Tokyo);

        Assert.Equal(new DateOnly(2026, 3, 17), inNewYork.Days.First);
        Assert.Equal(new DateOnly(2026, 3, 18), inTokyo.Days.First);
    }

    [Fact]
    public void An_event_ending_exactly_at_midnight_does_not_claim_the_next_day()
    {
        // A meeting from 22:00 to 24:00 belongs to that evening, not to two days. Getting this
        // wrong puts a chip on the following morning of every late event on the calendar.
        var start = new DateTimeOffset(2026, 3, 17, 22, 0, 0, TimeSpan.FromHours(-4));

        var result = EventNormalizer.NormalizeTimed(start, start.AddHours(2), NewYork);

        Assert.Equal(new DateOnly(2026, 3, 17), result.Days.First);
        Assert.Equal(new DateOnly(2026, 3, 17), result.Days.Last);
    }

    [Fact]
    public void A_zero_length_event_occupies_the_day_it_starts_on()
    {
        var start = new DateTimeOffset(2026, 3, 17, 14, 0, 0, TimeSpan.Zero);

        var result = EventNormalizer.NormalizeTimed(start, start, NewYork);

        Assert.Equal(new DateOnly(2026, 3, 17), result.Days.First);
        Assert.Equal(1, result.Days.DayCount);
    }

    [Fact]
    public void An_event_that_ends_before_it_starts_is_clamped_rather_than_inverted()
    {
        var start = new DateTimeOffset(2026, 3, 17, 14, 0, 0, TimeSpan.Zero);

        var result = EventNormalizer.NormalizeTimed(start, start.AddHours(-3), NewYork);

        Assert.True(result.Days.Last >= result.Days.First);
        Assert.True(result.End >= result.Start);
    }

    // ------------------------------------------------------------------ DST

    [Fact]
    public void Survives_the_spring_forward_transition()
    {
        // 8 March 2026, 02:00 does not exist in New York; the clock jumps to 03:00.
        var start = new DateTimeOffset(2026, 3, 8, 6, 30, 0, TimeSpan.Zero);

        var result = EventNormalizer.NormalizeTimed(start, start.AddHours(1), NewYork);

        Assert.Equal(new DateOnly(2026, 3, 8), result.Days.First);
    }

    [Fact]
    public void Survives_the_autumn_back_transition()
    {
        // 1 November 2026, 01:00 to 02:00 happens twice in New York.
        var start = new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero);

        var result = EventNormalizer.NormalizeTimed(start, start.AddHours(1), NewYork);

        Assert.Equal(new DateOnly(2026, 11, 1), result.Days.First);
    }

    // ------------------------------------------------------------------ zone ids

    [Fact]
    public void Resolves_an_iana_zone_id()
    {
        Assert.True(EventNormalizer.TryResolveTimeZone("America/New_York", out var zone));
        Assert.Equal(NewYork.Id, zone.Id);
    }

    [Fact]
    public void Resolves_a_windows_zone_id()
    {
        // Graph hands back Windows ids such as "Eastern Standard Time", which do not resolve
        // on macOS or Linux without converting first.
        Assert.True(EventNormalizer.TryResolveTimeZone("Eastern Standard Time", out var zone));
        Assert.Equal(NewYork.BaseUtcOffset, zone.BaseUtcOffset);
    }

    [Fact]
    public void Falls_back_to_the_local_zone_for_an_unknown_id()
    {
        // A calendar that will not print because one feed named a zone nobody recognises is
        // worse than a calendar printed in the machine's own zone.
        Assert.False(EventNormalizer.TryResolveTimeZone("Middle Earth Standard Time", out var zone));
        Assert.Equal(TimeZoneInfo.Local.Id, zone.Id);
    }
}
