using Printendar.Core.Sources;
using SkiaSharp;

namespace Printendar.Core.Tests.Sources;

/// <summary>
/// Deciding what colour each calendar prints in.
/// </summary>
/// <remarks>
/// The behaviour this protects is that a calendar's colour belongs to the calendar and stays
/// with it. Colours used to be handed out by position across every source, so removing an
/// account, or somebody adding a calendar in Outlook, silently recoloured everything after it:
/// you would print a month, come back a week later, and the wall chart no longer matched.
/// </remarks>
public class CalendarColorAssignmentTests
{
    private static IReadOnlyDictionary<string, string> Remembered(params (string Id, SKColor Color)[] entries) =>
        entries.ToDictionary(e => e.Id, e => CalendarPalette.ToHex(e.Color), StringComparer.Ordinal);

    [Fact]
    public void A_remembered_colour_survives_the_calendar_moving_position()
    {
        // The whole point. "work" was second and is now first, because the source above it was
        // removed. Its colour must not move with it.
        var chosen = CalendarPalette.At(4);

        var assigned = CalendarColorAssignment.Assign(
            ["work"],
            Remembered(("work", chosen)),
            inUseElsewhere: []);

        Assert.Equal(chosen, assigned["work"]);
    }

    [Fact]
    public void A_new_calendar_avoids_a_colour_another_source_is_already_using()
    {
        // Two calendars in the same colour, with a legend claiming they are different, is worse
        // than any particular choice of colour.
        var taken = CalendarPalette.At(0);

        var assigned = CalendarColorAssignment.Assign(
            ["fresh"],
            remembered: new Dictionary<string, string>(),
            inUseElsewhere: [taken]);

        Assert.NotEqual(taken, assigned["fresh"]);
    }

    [Fact]
    public void Calendars_added_together_do_not_collide_with_each_other()
    {
        var assigned = CalendarColorAssignment.Assign(
            ["a", "b", "c"],
            remembered: new Dictionary<string, string>(),
            inUseElsewhere: []);

        Assert.Equal(3, assigned.Values.Distinct().Count());
    }

    [Fact]
    public void A_new_calendar_avoids_a_colour_remembered_by_one_beside_it()
    {
        // The remembered one is not in "inUseElsewhere" because it is in this same source, so
        // the assignment has to notice it as it goes.
        var chosen = CalendarPalette.At(2);

        var assigned = CalendarColorAssignment.Assign(
            ["old", "new"],
            Remembered(("old", chosen)),
            inUseElsewhere: []);

        Assert.Equal(chosen, assigned["old"]);
        Assert.NotEqual(chosen, assigned["new"]);
    }

    [Fact]
    public void Every_calendar_gets_a_colour_even_past_the_end_of_the_palette()
    {
        // Running out is not a reason to leave one undrawn. Repeats are acceptable here in a
        // way that they are not while a free colour remains.
        var ids = Enumerable.Range(0, CalendarPalette.Colors.Count + 3)
            .Select(i => $"cal-{i}")
            .ToList();

        var assigned = CalendarColorAssignment.Assign(
            ids,
            remembered: new Dictionary<string, string>(),
            inUseElsewhere: []);

        Assert.Equal(ids.Count, assigned.Count);
        Assert.All(ids, id => Assert.True(assigned.ContainsKey(id)));
    }

    [Fact]
    public void An_unreadable_remembered_colour_is_replaced_rather_than_thrown_over()
    {
        // Hand-edited settings. A bad colour costs that one calendar its choice, not the run.
        var assigned = CalendarColorAssignment.Assign(
            ["work"],
            new Dictionary<string, string> { ["work"] = "not a colour" },
            inUseElsewhere: []);

        Assert.Contains(assigned["work"], CalendarPalette.Colors);
    }

    [Fact]
    public void A_colour_outside_the_palette_is_kept_because_the_user_chose_it()
    {
        // Custom colours are allowed, so "not one of ours" must not be treated as corruption.
        var custom = new SKColor(0x7A, 0x3B, 0x9F);

        var assigned = CalendarColorAssignment.Assign(
            ["work"],
            Remembered(("work", custom)),
            inUseElsewhere: []);

        Assert.Equal(custom, assigned["work"]);
    }

    [Theory]
    [InlineData(0x1F, 0x77, 0xB4)]
    [InlineData(0x00, 0x00, 0x00)]
    [InlineData(0xFF, 0xFF, 0xFF)]
    public void A_colour_survives_being_written_down_and_read_back(byte r, byte g, byte b)
    {
        var color = new SKColor(r, g, b);

        Assert.Equal(color, CalendarPalette.FromHex(CalendarPalette.ToHex(color)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#12345")]
    [InlineData("zzzzzz")]
    public void Nonsense_reads_back_as_no_colour_at_all(string? text)
    {
        Assert.Null(CalendarPalette.FromHex(text));
    }
}
