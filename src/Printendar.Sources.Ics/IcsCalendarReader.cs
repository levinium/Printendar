using Ical.Net;
using Ical.Net.DataTypes;
using Printendar.Core.Model;
using Printendar.Core.Time;

// Both libraries have a CalendarEvent, and they mean different things: one is a VEVENT as it
// appears in the file, the other is a single occurrence ready to be drawn. The alias keeps
// which is which obvious at every use.
using VEvent = Ical.Net.CalendarComponents.CalendarEvent;

// Ical.Net has an EventStatus too, and it is a different thing: the raw STATUS property
// rather than the status Printendar draws.
using PrintendarEventStatus = Printendar.Core.Model.EventStatus;

namespace Printendar.Sources.Ics;

/// <summary>
/// Reads an iCalendar document into occurrences for a date range.
/// </summary>
/// <remarks>
/// The source that needs no account, no administrator and no network: export a calendar from
/// anything, open the file, print it.
///
/// It is also the one that has to work hardest. Microsoft 365 and Google expand a recurring
/// series before it ever reaches Printendar; an .ics file states the rule and leaves the
/// expanding here, and the files arrive from every calendar program ever written, each with
/// its own idea of which fields are optional.
/// </remarks>
public static class IcsCalendarReader
{
    /// <summary>
    /// A ceiling on how many occurrences one file may contribute.
    /// </summary>
    /// <remarks>
    /// A single month cannot legitimately hold this many. The cap is there because a daily
    /// recurrence with no UNTIL is valid iCalendar and would otherwise expand until memory
    /// ran out.
    /// </remarks>
    private const int MaxOccurrences = 20_000;

    private const string UntitledSubject = "(no subject)";

    public static IReadOnlyList<CalendarEvent> Read(
        string content,
        string calendarId,
        DateSpan window,
        TimeZoneInfo displayZone)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(calendarId);
        ArgumentNullException.ThrowIfNull(displayZone);

        var calendars = Parse(content);
        var results = new List<CalendarEvent>();

        var from = window.First.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var to = window.Last.AddDays(2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        foreach (var calendar in calendars)
        {
            // GetOccurrences is lazy and takes only a start, so a recurrence with no UNTIL
            // enumerates forever. TakeWhile is what bounds it, and it has to be a TakeWhile
            // rather than a Where: filtering would keep pulling from an infinite sequence and
            // never return.
            var occurrences = calendar
                .GetOccurrences<VEvent>(new CalDateTime(from, hasTime: true))
                .TakeWhile(o => o.Period?.StartTime is null || o.Period.StartTime.Value < to);

            foreach (var occurrence in occurrences)
            {
                if (results.Count >= MaxOccurrences)
                {
                    return results;
                }

                var mapped = TryMap(occurrence, calendarId, window, displayZone);

                if (mapped is not null)
                {
                    results.Add(mapped);
                }
            }
        }

        return results;
    }

    private static CalendarCollection Parse(string content)
    {
        try
        {
            var calendars = CalendarCollection.Load(content);

            if (calendars.Count == 0)
            {
                throw new InvalidOperationException(
                    "That file does not contain a calendar. Look for a file ending in .ics, which is what " +
                    "calendar programs produce when you export or download a calendar.");
            }

            return calendars;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                "That file could not be read as a calendar. Look for a file ending in .ics, which is what " +
                "calendar programs produce when you export or download a calendar.",
                ex);
        }
    }

    /// <summary>Maps one expanded occurrence, or returns null if it cannot be placed.</summary>
    private static CalendarEvent? TryMap(
        Occurrence occurrence,
        string calendarId,
        DateSpan window,
        TimeZoneInfo displayZone)
    {
        if (occurrence.Source is not VEvent source)
        {
            return null;
        }

        var start = occurrence.Period?.StartTime;

        if (start is null)
        {
            // Real files from real programs contain surprises. Losing this occurrence is much
            // better than losing the month it appears in.
            return null;
        }

        var subject = string.IsNullOrWhiteSpace(source.Summary) ? UntitledSubject : source.Summary.Trim();
        var id = $"{source.Uid ?? subject}@{start.Value:yyyyMMddTHHmmss}";

        // A date-valued DTSTART means all day. Those dates are floating by definition and must
        // never go through a time zone, or a whole-day event slides onto the previous evening
        // for anyone west of whoever wrote the file.
        // EffectiveEndTime, not EndTime. For an all-day occurrence Ical.Net leaves EndTime null
        // and carries the length as a duration, so reading EndTime makes every multi-day event
        // one day long. It also covers files that state DURATION instead of DTEND, which is
        // equally valid iCalendar and common in the wild.
        var end = occurrence.Period?.EffectiveEndTime;

        if (!start.HasTime)
        {
            var first = DateOnly.FromDateTime(start.Value);
            var endExclusive = end is not null && !end.HasTime
                ? DateOnly.FromDateTime(end.Value)
                : first.AddDays(1);

            var days = EventNormalizer.NormalizeAllDay(first, endExclusive);

            return window.Intersects(days)
                ? Build(id, calendarId, subject, source, isAllDay: true, days, null, null)
                : null;
        }

        var startOffset = ToOffset(start, displayZone);
        var endOffset = end is not null ? ToOffset(end, displayZone) : startOffset;

        var normalized = EventNormalizer.NormalizeTimed(startOffset, endOffset, displayZone);

        return window.Intersects(normalized.Days)
            ? Build(id, calendarId, subject, source, isAllDay: false, normalized.Days, normalized.Start, normalized.End)
            : null;
    }

    /// <summary>
    /// Turns an iCalendar timestamp into an absolute instant.
    /// </summary>
    /// <remarks>
    /// Three cases. UTC and an explicit TZID are unambiguous. A "floating" time, with neither,
    /// is defined by the standard as local wherever it is read, so it is interpreted in the
    /// display zone. That is the correct reading and also the one a user expects: a floating
    /// 09:00 should print as 09:00.
    /// </remarks>
    private static DateTimeOffset ToOffset(CalDateTime value, TimeZoneInfo displayZone)
    {
        if (value.IsUtc)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
        }

        var zone = EventNormalizer.TryResolveTimeZone(value.TzId, out var resolved) ? resolved : displayZone;
        var naive = DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified);

        return new DateTimeOffset(naive, zone.GetUtcOffset(naive));
    }

    private static CalendarEvent Build(
        string id,
        string calendarId,
        string subject,
        VEvent source,
        bool isAllDay,
        DateSpan days,
        DateTimeOffset? start,
        DateTimeOffset? end) =>
        new()
        {
            Id = id,
            CalendarId = calendarId,
            Subject = subject,
            Location = string.IsNullOrWhiteSpace(source.Location) ? null : source.Location.Trim(),
            Organizer = source.Organizer?.CommonName,
            IsAllDay = isAllDay,
            Days = days,
            Start = start,
            End = end,
            Status = ReadStatus(source),
            Sensitivity = ReadSensitivity(source),
            Categories = source.Categories?.ToList() ?? [],
            SeriesMasterId = source.RecurrenceRule is not null ? source.Uid : null,
        };

    private static PrintendarEventStatus ReadStatus(VEvent source) => source.Status?.ToUpperInvariant() switch
    {
        "CANCELLED" => PrintendarEventStatus.Cancelled,
        "TENTATIVE" => PrintendarEventStatus.Tentative,
        "CONFIRMED" => PrintendarEventStatus.Confirmed,
        _ => PrintendarEventStatus.Confirmed,
    };

    private static EventSensitivity ReadSensitivity(VEvent source) => source.Class?.ToUpperInvariant() switch
    {
        "PRIVATE" => EventSensitivity.Private,
        "CONFIDENTIAL" => EventSensitivity.Confidential,
        _ => EventSensitivity.Normal,
    };
}
