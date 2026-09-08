using Printendar.Core.Model;

namespace Printendar.Core.Time;

/// <param name="Start">Start in the display time zone.</param>
/// <param name="End">End in the display time zone.</param>
/// <param name="Days">The days the event occupies, inclusive.</param>
public readonly record struct NormalizedTimes(DateTimeOffset Start, DateTimeOffset End, DateSpan Days);

/// <summary>
/// The one place a provider's dates and times become the days a calendar cell is filled from.
/// </summary>
/// <remarks>
/// Deliberately the only place. Every provider Printendar reads from expresses an all-day
/// event's end date exclusively, so an event on the 5th arrives as 5th to 6th. Doing that
/// conversion in each adapter is how one of them ends up off by a day, and an event appears
/// on a day it does not happen.
///
/// Adapters therefore ask their provider for UTC and hand the result here, rather than each
/// doing its own time zone arithmetic in its own way.
/// </remarks>
public static class EventNormalizer
{
    /// <summary>
    /// Converts a provider's all-day range into the inclusive days it occupies.
    /// </summary>
    /// <param name="start">First day of the event.</param>
    /// <param name="endExclusive">The day after the last day, as every provider expresses it.</param>
    /// <remarks>
    /// Takes <see cref="DateOnly"/> and no time zone, and that is the point. An all-day event
    /// has no clock time, so there is nothing to convert; giving this method a zone would make
    /// it possible to slide a whole-day event onto the previous evening, which is exactly the
    /// bug the signature rules out.
    /// </remarks>
    public static DateSpan NormalizeAllDay(DateOnly start, DateOnly endExclusive)
    {
        var last = endExclusive > start ? endExclusive.AddDays(-1) : start;

        return new DateSpan(start, last);
    }

    /// <summary>
    /// Converts a timed event into the display zone and works out which days it touches.
    /// </summary>
    public static NormalizedTimes NormalizeTimed(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo displayZone)
    {
        ArgumentNullException.ThrowIfNull(displayZone);

        var localStart = TimeZoneInfo.ConvertTime(start, displayZone);

        // A provider occasionally sends an end before the start, from a malformed feed or a
        // truncated recurrence. Clamping keeps the span forward-running rather than producing
        // one that iterates backwards.
        var localEnd = TimeZoneInfo.ConvertTime(end < start ? start : end, displayZone);

        return new NormalizedTimes(localStart, localEnd, DaysCovered(localStart, localEnd));
    }

    /// <summary>
    /// The inclusive days a local time range covers.
    /// </summary>
    /// <remarks>
    /// The subtle case is an event ending exactly at midnight. A meeting from 22:00 to 24:00
    /// belongs to that evening alone, so an end sitting precisely on midnight is attributed to
    /// the day before. Without that, every late-running event puts a stray chip on the
    /// following morning.
    /// </remarks>
    private static DateSpan DaysCovered(DateTimeOffset localStart, DateTimeOffset localEnd)
    {
        var first = DateOnly.FromDateTime(localStart.DateTime);
        var last = DateOnly.FromDateTime(localEnd.DateTime);

        if (localEnd.TimeOfDay == TimeSpan.Zero && last > first)
        {
            last = last.AddDays(-1);
        }

        return new DateSpan(first, last);
    }

    /// <summary>
    /// Resolves a time zone id from any provider, falling back to the local zone.
    /// </summary>
    /// <remarks>
    /// Graph hands back Windows ids such as "Eastern Standard Time", ICS feeds carry IANA ids
    /// such as "America/New_York", and .NET only resolves the platform's own kind natively.
    ///
    /// An unknown id returns false and the local zone rather than throwing. A calendar that
    /// refuses to print because one subscribed feed named a zone nobody recognises is worse
    /// than one printed in the machine's own zone with a note.
    /// </remarks>
    public static bool TryResolveTimeZone(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Local;

        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Not this platform's flavour of id. Try converting between the two conventions.
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId))
        {
            return TryResolveExact(ianaId, out zone);
        }

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId))
        {
            return TryResolveExact(windowsId, out zone);
        }

        return false;
    }

    private static bool TryResolveExact(string id, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Local;
            return false;
        }
    }
}
