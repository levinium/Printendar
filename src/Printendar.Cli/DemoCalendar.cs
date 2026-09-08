using Printendar.Core.Layout;
using Printendar.Core.Model;
using SkiaSharp;

namespace Printendar.Cli;

/// <summary>
/// Invented events across several calendars, for exercising the print engine with no account.
/// </summary>
/// <remarks>
/// Deliberately shaped like a real working month rather than uniformly dense: recurring
/// standups, a scattering of one-offs, a couple of all-day events, one genuinely overloaded
/// day, and a quiet week. A uniform grid of identical events would make the layout look better
/// than it is, because every cell would need the same height and nothing would ever overflow.
///
/// Seeded, so the same month always produces the same events and two runs can be compared.
/// </remarks>
internal static class DemoCalendar
{
    public static IReadOnlyList<CalendarLegendEntry> Calendars { get; } =
    [
        // A colourblind-safe set, and distinguishable as grey levels for a black and white
        // print, which is how most of these end up on a wall.
        new("work", "Work", new SKColor(0x1F, 0x77, 0xB4)),
        new("team", "Team", new SKColor(0x2C, 0xA0, 0x2C)),
        new("personal", "Personal", new SKColor(0xD6, 0x27, 0x28)),
        new("travel", "Travel", new SKColor(0x94, 0x67, 0xBD)),
        new("holidays", "Holidays", new SKColor(0x8C, 0x56, 0x4B)),
    ];

    private static readonly string[] Recurring =
    [
        "Standup", "Team sync", "Office hours", "1:1 with Sam", "Sprint planning",
    ];

    private static readonly string[] OneOffs =
    [
        "Budget review", "Board meeting", "Vendor call", "Dentist", "Design review",
        "Quarterly planning with the finance committee", "Interview: platform engineer",
        "Lunch with Priya", "Retro", "Security audit kickoff", "Parent teacher evening",
        "Renewal call with the insurer", "All hands",
    ];

    public static IReadOnlyList<CalendarEvent> ForMonth(int year, int month, int seed = 20260308)
    {
        var random = new Random(seed);
        var events = new List<CalendarEvent>();
        var daysInMonth = DateTime.DaysInMonth(year, month);

        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(year, month, day);
            var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

            // One week is deliberately quiet, so the fit search is not the only thing on show.
            var quietWeek = day is >= 8 and <= 14;

            if (!isWeekend && !quietWeek)
            {
                events.Add(Timed(date, 9, 15, Recurring[day % Recurring.Length], "team"));
            }

            if (isWeekend)
            {
                if (random.Next(4) == 0)
                {
                    events.Add(Timed(date, 11, 0, OneOffs[random.Next(OneOffs.Length)], "personal"));
                }

                continue;
            }

            var extra = quietWeek ? random.Next(0, 2) : random.Next(1, 4);

            for (var i = 0; i < extra; i++)
            {
                var hour = 10 + random.Next(0, 8);
                var calendar = random.Next(5) == 0 ? "personal" : "work";
                events.Add(Timed(date, hour, random.Next(2) == 0 ? 0 : 30, OneOffs[random.Next(OneOffs.Length)], calendar));
            }
        }

        // One overloaded day, so the overflow marker and the shrink both get exercised.
        var busyDay = new DateOnly(year, month, Math.Min(17, daysInMonth));

        for (var hour = 8; hour <= 18; hour++)
        {
            events.Add(Timed(busyDay, hour, 0, OneOffs[hour % OneOffs.Length], "work"));
        }

        // A multi-day all-day event, which currently repeats as a chip on each day it covers.
        if (daysInMonth >= 24)
        {
            events.Add(new CalendarEvent
            {
                Id = "travel-conference",
                CalendarId = "travel",
                Subject = "Conference in Chicago",
                IsAllDay = true,
                Days = new DateSpan(new DateOnly(year, month, 22), new DateOnly(year, month, 24)),
            });
        }

        events.Add(new CalendarEvent
        {
            Id = "holiday",
            CalendarId = "holidays",
            Subject = "Public holiday",
            IsAllDay = true,
            Days = DateSpan.SingleDay(new DateOnly(year, month, Math.Min(5, daysInMonth))),
        });

        foreach (var calendarEvent in events)
        {
            calendarEvent.Validate();
        }

        return events;
    }

    private static CalendarEvent Timed(DateOnly date, int hour, int minute, string subject, string calendarId)
    {
        var start = new DateTimeOffset(date.Year, date.Month, date.Day, hour, minute, 0, TimeSpan.Zero);

        return new CalendarEvent
        {
            Id = $"{calendarId}-{date:yyyyMMdd}-{hour:00}{minute:00}-{subject}",
            CalendarId = calendarId,
            Subject = subject,
            IsAllDay = false,
            Days = DateSpan.SingleDay(date),
            Start = start,
            End = start.AddHours(1),
        };
    }
}
