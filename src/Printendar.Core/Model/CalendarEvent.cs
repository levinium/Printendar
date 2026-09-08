namespace Printendar.Core.Model;

/// <summary>Whether the event is really happening.</summary>
public enum EventStatus
{
    Confirmed,
    Tentative,
    Cancelled,

    /// <summary>The signed-in user declined the invitation.</summary>
    Declined,

    Unknown,
}

/// <summary>How private the organizer marked the event.</summary>
public enum EventSensitivity
{
    Normal,
    Personal,
    Private,
    Confidential,
}

/// <summary>
/// One occurrence of an event, normalized away from whichever provider it came from.
/// </summary>
/// <remarks>
/// An occurrence, not a series. Recurrence is expanded at the adapter boundary, by the
/// provider where possible, so nothing downstream has to understand a recurrence rule.
///
/// Two invariants that the rest of the program relies on:
///
/// First, <see cref="IsAllDay"/> implies <see cref="Start"/> and <see cref="End"/> are null.
/// An all-day event has no clock time, and inventing one is how it ends up shifted onto the
/// previous day by a time zone conversion.
///
/// Second, <see cref="Days"/> is inclusive at both ends and is the only authority on which
/// cells this event appears in. Every provider expresses an all-day end date exclusively: an
/// event on the 5th arrives as 5th to 6th. That is converted exactly once, in
/// <see cref="Time.EventNormalizer"/>, and never reasoned about again.
/// </remarks>
public sealed record CalendarEvent
{
    public required string Id { get; init; }

    public required string CalendarId { get; init; }

    public required string Subject { get; init; }

    public string? Location { get; init; }

    public string? Organizer { get; init; }

    public required bool IsAllDay { get; init; }

    /// <summary>The days this event occupies, inclusive, in the display time zone.</summary>
    public required DateSpan Days { get; init; }

    /// <summary>Start instant in the display time zone. Null for an all-day event.</summary>
    public DateTimeOffset? Start { get; init; }

    /// <summary>End instant in the display time zone. Null for an all-day event.</summary>
    public DateTimeOffset? End { get; init; }

    public EventStatus Status { get; init; } = EventStatus.Confirmed;

    public EventSensitivity Sensitivity { get; init; } = EventSensitivity.Normal;

    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>Whether this occurrence belongs to a repeating series.</summary>
    public string? SeriesMasterId { get; init; }

    /// <summary>True when the event covers more than one day.</summary>
    public bool IsMultiDay => Days.DayCount > 1;

    /// <summary>
    /// Throws if the invariants above are broken.
    /// </summary>
    /// <remarks>
    /// Called by adapters after normalizing, so a provider quirk surfaces at the boundary that
    /// introduced it rather than as a mysteriously misplaced chip six layers later.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The event contradicts itself.</exception>
    public void Validate()
    {
        if (IsAllDay && (Start is not null || End is not null))
        {
            throw new InvalidOperationException(
                $"All-day event '{Subject}' ({Id}) carries a clock time. All-day events are dates only, " +
                "so that a time zone conversion cannot move them across midnight.");
        }

        if (!IsAllDay && Start is null)
        {
            throw new InvalidOperationException($"Timed event '{Subject}' ({Id}) has no start time.");
        }

        if (!IsAllDay && Start is { } start && End is { } end && end < start)
        {
            throw new InvalidOperationException($"Event '{Subject}' ({Id}) ends at {end:o}, before it starts at {start:o}.");
        }
    }
}

/// <summary>
/// The one order events are sorted in, everywhere.
/// </summary>
/// <remarks>
/// A single canonical order is what makes "+3 more" mean the same thing twice in a row. Without
/// it, two layouts of the same month could hide different events, and snapshot baselines would
/// be meaningless.
/// </remarks>
public sealed class EventOrder : IComparer<CalendarEvent>
{
    public static EventOrder Comparer { get; } = new();

    private EventOrder()
    {
    }

    public int Compare(CalendarEvent? x, CalendarEvent? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        // All-day events read as banners for the whole day, so they belong at the top of the
        // cell rather than interleaved by a start time they do not have.
        var allDay = y.IsAllDay.CompareTo(x.IsAllDay);
        if (allDay != 0)
        {
            return allDay;
        }

        var start = Nullable.Compare(x.Start, y.Start);
        if (start != 0)
        {
            return start;
        }

        var end = Nullable.Compare(x.End, y.End);
        if (end != 0)
        {
            return end;
        }

        var subject = string.CompareOrdinal(x.Subject, y.Subject);
        return subject != 0 ? subject : string.CompareOrdinal(x.Id, y.Id);
    }
}
