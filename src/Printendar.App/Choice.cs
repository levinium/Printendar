using Printendar.Core.Layout.Fit;
using Printendar.Core.Layout.Month;
using Printendar.Core.Paper;

namespace Printendar.App;

/// <summary>
/// One option in a picker: the value, and what a person should see instead of it.
/// </summary>
/// <remarks>
/// Enum names must never reach the window. "FullSevenDay" and "Hybrid" are perfectly good
/// identifiers and tell a non-technical user nothing at all, which is the audience this
/// program is for.
/// </remarks>
/// <param name="Value">The underlying value.</param>
/// <param name="Label">What the picker shows.</param>
/// <param name="Description">A sentence explaining the consequence, where one helps.</param>
public sealed record Choice<T>(T Value, string Label, string? Description = null)
{
    public override string ToString() => Label;
}

/// <summary>The wording for every option the window offers.</summary>
/// <remarks>
/// Gathered in one place so the vocabulary stays consistent, and so a future translation has
/// a single file to work from.
/// </remarks>
public static class Choices
{
    public static IReadOnlyList<Choice<Orientation>> Orientations { get; } =
    [
        new(Orientation.Landscape, "Landscape (sideways)"),
        new(Orientation.Portrait, "Portrait (upright)"),
    ];

    public static IReadOnlyList<Choice<DayOfWeek>> WeekStarts { get; } =
    [
        new(DayOfWeek.Sunday, "Weeks start on Sunday"),
        new(DayOfWeek.Monday, "Weeks start on Monday"),
    ];

    public static IReadOnlyList<Choice<WeekendMode>> WeekendModes { get; } =
    [
        new(WeekendMode.FullSevenDay, "Show all seven days"),
        new(WeekendMode.CompressedWeekendColumn, "Squeeze Sat and Sun together",
            "Gives the weekdays more room. Needs weeks to start on Monday."),
        new(WeekendMode.WeekdaysOnly, "Weekdays only, Mon to Fri",
            "Most room per day, but weekend events are not shown at all."),
    ];

    public static IReadOnlyList<Choice<FitPolicy>> FitPolicies { get; } =
    [
        // Labels stay short enough to read inside the picker without truncating. The
        // consequence goes in the description, which the panel shows underneath.
        new(FitPolicy.Hybrid, "Shrink, then summarise",
            "Recommended. Makes the text smaller until it stops being readable, then shows “+3 more”."),
        new(FitPolicy.AutoShrink, "Shrink as far as it takes",
            "Fits the most events, but the print can end up very small."),
        new(FitPolicy.CapPerDay, "Show only the first few",
            "Keeps the text at full size and summarises everything past the limit."),
    ];

    public static IReadOnlyList<Choice<AdjacentDayMode>> AdjacentDays { get; } =
    [
        new(AdjacentDayMode.Muted, "Show them greyed out"),
        new(AdjacentDayMode.Hidden, "Leave them blank"),
        new(AdjacentDayMode.Full, "Show them like any other day"),
    ];

    /// <summary>Finds the choice wrapping a value, so the picker can show the current setting.</summary>
    public static Choice<T> For<T>(IReadOnlyList<Choice<T>> choices, T value) =>
        choices.FirstOrDefault(c => EqualityComparer<T>.Default.Equals(c.Value, value)) ?? choices[0];
}
