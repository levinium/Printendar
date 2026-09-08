namespace Printendar.Core.Layout.Month;

/// <summary>How the weekend is handled in the grid.</summary>
public enum WeekendMode
{
    /// <summary>Seven equal columns.</summary>
    FullSevenDay,

    /// <summary>
    /// Saturday and Sunday share one narrow column, stacked as two half-height cells.
    /// </summary>
    /// <remarks>
    /// Buys roughly a fifth more width for the five days that usually carry the events.
    /// Requires a Monday week start, or the weekend lands at both ends of the grid and there
    /// is no single column to compress.
    /// </remarks>
    CompressedWeekendColumn,

    /// <summary>Five columns, Monday to Friday. Weekend events are not shown at all.</summary>
    WeekdaysOnly,
}

/// <summary>What to do with the days either side of the month that fill out the first and last week rows.</summary>
public enum AdjacentDayMode
{
    /// <summary>Leave the cell empty apart from its rule.</summary>
    Hidden,

    /// <summary>Show the date and any events, greyed back.</summary>
    Muted,

    /// <summary>Show the date and any events exactly as for a day in the month.</summary>
    Full,
}

/// <summary>Defaults for <see cref="MonthGridOptions"/>.</summary>
/// <remarks>
/// Separate from the record because a constant declared in a record body is not in scope for
/// that record's own primary constructor defaults.
/// </remarks>
public static class MonthGridDefaults
{
    /// <summary>
    /// Width of the shared weekend column relative to a weekday column.
    /// </summary>
    /// <remarks>
    /// Below about 0.5 the two half-height cells stop holding a readable event title, which
    /// defeats the point of showing the weekend at all.
    /// </remarks>
    public const float WeekendColumnWeight = 0.62f;
}

/// <summary>
/// The shape of the month grid, independent of paper, fonts or events.
/// </summary>
public sealed record MonthGridOptions(
    int Year,
    int Month,
    DayOfWeek FirstDayOfWeek = DayOfWeek.Sunday,
    WeekendMode WeekendMode = WeekendMode.FullSevenDay,
    AdjacentDayMode AdjacentDays = AdjacentDayMode.Muted,
    float WeekendColumnWeight = MonthGridDefaults.WeekendColumnWeight)
{
    public DateOnly FirstOfMonth => new(Year, Month, 1);

    public DateOnly LastOfMonth => new(Year, Month, DateTime.DaysInMonth(Year, Month));

    /// <summary>
    /// Throws if the combination of options cannot produce a coherent grid.
    /// </summary>
    /// <exception cref="ArgumentException">The options contradict each other.</exception>
    public void Validate()
    {
        if (Month is < 1 or > 12)
        {
            throw new ArgumentException($"Month must be between 1 and 12, but was {Month}.", nameof(Month));
        }

        if (FirstDayOfWeek is not (DayOfWeek.Sunday or DayOfWeek.Monday))
        {
            throw new ArgumentException(
                $"The week must start on Sunday or Monday, but {FirstDayOfWeek} was given.",
                nameof(FirstDayOfWeek));
        }

        if (WeekendMode is WeekendMode.CompressedWeekendColumn && FirstDayOfWeek is not DayOfWeek.Monday)
        {
            throw new ArgumentException(
                "Compressing the weekend into one column requires a week that starts on Monday. " +
                "With a Sunday start the weekend sits at both ends of the grid, so there is no " +
                "single column to compress.",
                nameof(WeekendMode));
        }

        if (WeekendColumnWeight is <= 0f or > 1f)
        {
            throw new ArgumentException(
                $"The weekend column weight must be greater than 0 and at most 1, but was {WeekendColumnWeight}.",
                nameof(WeekendColumnWeight));
        }
    }
}
