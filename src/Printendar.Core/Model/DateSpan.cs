namespace Printendar.Core.Model;

/// <summary>
/// A run of whole days, inclusive at both ends.
/// </summary>
/// <remarks>
/// Inclusive on purpose, and stated loudly, because the alternative is the single most common
/// bug in calendar software. Every provider Printendar reads from expresses an all-day event's
/// end date exclusively: an event on the 5th arrives as 5th to 6th. Converting that once, at
/// the adapter boundary, and carrying an inclusive span everywhere else is what stops the
/// event appearing on the 6th as well.
///
/// Days, not instants. A <see cref="DateOnly"/> never passes through a time zone conversion,
/// so an all-day event cannot be shifted across midnight by a DST rule.
/// </remarks>
public readonly record struct DateSpan
{
    public DateSpan(DateOnly first, DateOnly last)
    {
        // A malformed or zero-length range from a provider collapses to a single day rather
        // than producing a span that iterates backwards forever.
        First = first;
        Last = last < first ? first : last;
    }

    public DateOnly First { get; }

    public DateOnly Last { get; }

    public int DayCount => Last.DayNumber - First.DayNumber + 1;

    public static DateSpan SingleDay(DateOnly day) => new(day, day);

    public bool Contains(DateOnly day) => day >= First && day <= Last;

    public bool Intersects(DateSpan other) => First <= other.Last && other.First <= Last;

    public IEnumerable<DateOnly> Days()
    {
        for (var day = First; day <= Last; day = day.AddDays(1))
        {
            yield return day;
        }
    }

    public override string ToString() =>
        First == Last ? First.ToString("yyyy-MM-dd") : $"{First:yyyy-MM-dd}..{Last:yyyy-MM-dd}";
}
