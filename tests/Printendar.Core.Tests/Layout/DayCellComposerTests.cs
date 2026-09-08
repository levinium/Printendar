using System.Globalization;
using Printendar.Core.Layout;
using Printendar.Core.Model;
using Printendar.Core.Text;
using Printendar.Core.Tests.Text;
using SkiaSharp;

namespace Printendar.Core.Tests.Layout;

/// <summary>
/// Stacking event chips into a day cell, and the "+N more" marker.
/// </summary>
/// <remarks>
/// The postcondition asserted throughout is:
///
///   usedHeight + (hidden &gt; 0 ? markerHeight : 0) &lt;= availableHeight
///
/// It holds unconditionally because the marker's height is reserved before each chip is
/// placed, and because the composer backtracks by popping a chip if it ran out of room for the
/// marker after all. Without the backtrack, a cell that fits exactly N chips and has an N+1th
/// event has nowhere to say so, and the extra event vanishes silently.
///
/// The fake measurer is used deliberately: each character is half the font size wide and a
/// line is 1.2 times the font size tall, so every expected count below is arithmetic.
/// </remarks>
public class DayCellComposerTests
{
    private static readonly FixedAdvanceTextMeasurer Measurer = new();

    private static ChipStyle Style(int maxLines = 1, bool showOverflowMarker = true) => new(
        TitleFont: new FontSpec(FontWeightKind.Regular, 10f),
        TimeFont: new FontSpec(FontWeightKind.Regular, 10f),
        MarkerFont: new FontSpec(FontWeightKind.Regular, 10f),
        SwatchWidth: 2f,
        InnerPadding: 1f,
        ChipGap: 2f,
        LineSpacing: 0f,
        MaxLines: maxLines,
        ShowStartTime: false,
        ShowOverflowMarker: showOverflowMarker,
        TimeFormat: TimeFormat.Clock12Hour);

    private static CalendarEvent AllDay(string subject, int day = 17) => new()
    {
        Id = subject,
        CalendarId = "cal",
        Subject = subject,
        IsAllDay = true,
        Days = DateSpan.SingleDay(new DateOnly(2026, 3, day)),
    };

    private static CalendarEvent Timed(string subject, int hour, int minute = 0, int day = 17)
    {
        var start = new DateTimeOffset(2026, 3, day, hour, minute, 0, TimeSpan.Zero);

        return new CalendarEvent
        {
            Id = $"{subject}@{hour}",
            CalendarId = "cal",
            Subject = subject,
            IsAllDay = false,
            Days = DateSpan.SingleDay(new DateOnly(2026, 3, day)),
            Start = start,
            End = start.AddHours(1),
        };
    }

    private static CellPlacement Fill(
        IEnumerable<CalendarEvent> events,
        float availableHeight,
        float availableWidth = 100f,
        ChipStyle? style = null) =>
        DayCellComposer.Fill(
            new SKRect(0f, 0f, availableWidth, availableHeight),
            [.. events],
            style ?? Style(),
            _ => SKColors.Blue,
            Measurer,
            CultureInfo.InvariantCulture);

    private static void AssertPostcondition(CellPlacement placement, ChipStyle style)
    {
        // Charged only when a marker is actually drawn. A cell too short to hold even the
        // marker emits none, and still reports the hidden count so the caller can surface it
        // some other way. Drawing a marker that overflows the cell it exists to tidy would be
        // worse than drawing nothing.
        var markerHeight = placement.OverflowMarker is null
            ? 0f
            : Measurer.GetMetrics(style.MarkerFont).LineHeight + style.ChipGap;

        Assert.True(
            placement.UsedHeight + markerHeight <= placement.AvailableHeight + 0.01f,
            $"Used {placement.UsedHeight:0.##} plus marker {markerHeight:0.##} exceeds the " +
            $"{placement.AvailableHeight:0.##} points available.");
    }

    [Fact]
    public void An_empty_day_places_nothing()
    {
        var placement = Fill([], availableHeight: 100f);

        Assert.Empty(placement.Chips);
        Assert.Equal(0, placement.HiddenCount);
        Assert.Equal(0f, placement.UsedHeight);
    }

    [Fact]
    public void Places_every_event_when_there_is_room()
    {
        var placement = Fill([AllDay("A"), AllDay("B"), AllDay("C")], availableHeight: 200f);

        Assert.Equal(3, placement.Chips.Count);
        Assert.Equal(0, placement.HiddenCount);
        AssertPostcondition(placement, Style());
    }

    [Fact]
    public void Emits_no_marker_when_nothing_is_hidden()
    {
        var placement = Fill([AllDay("A")], availableHeight: 200f);

        Assert.Null(placement.OverflowMarker);
    }

    [Fact]
    public void Hides_what_does_not_fit_and_says_how_many()
    {
        // A one-line chip at 10 point is 12 points tall plus a 2 point gap, so 14 points each.
        // Thirty points holds two chips, but the second must give way to the marker.
        var placement = Fill([AllDay("A"), AllDay("B"), AllDay("C"), AllDay("D")], availableHeight: 30f);

        Assert.True(placement.HiddenCount > 0);
        Assert.Equal(4, placement.Chips.Count + placement.HiddenCount);
        Assert.NotNull(placement.OverflowMarker);
        AssertPostcondition(placement, Style());
    }

    [Fact]
    public void Backtracks_so_the_marker_always_has_room()
    {
        // The case the reservation alone does not cover: the cell holds exactly two chips, and
        // there is a third event. Without popping a chip there is nowhere to put the marker,
        // and the third event disappears with no indication.
        var style = Style();
        var chipHeight = Measurer.GetMetrics(style.TitleFont).LineHeight + style.ChipGap;

        var placement = Fill([AllDay("A"), AllDay("B"), AllDay("C")], availableHeight: chipHeight * 2f);

        AssertPostcondition(placement, style);
        Assert.NotNull(placement.OverflowMarker);
        Assert.Equal(3, placement.Chips.Count + placement.HiddenCount);
    }

    [Fact]
    public void Never_loses_an_event()
    {
        // Conservation, across a wide range of cell heights. Every event is either drawn or
        // counted in the overflow; none may simply vanish.
        var events = Enumerable.Range(0, 12).Select(i => AllDay($"Event {i}")).ToArray();

        for (var height = 5f; height <= 220f; height += 3f)
        {
            var placement = Fill(events, height);

            Assert.Equal(events.Length, placement.Chips.Count + placement.HiddenCount);
            AssertPostcondition(placement, Style());
        }
    }

    [Fact]
    public void Places_nothing_in_a_cell_too_short_for_even_one_chip()
    {
        var placement = Fill([AllDay("A"), AllDay("B")], availableHeight: 3f);

        Assert.Empty(placement.Chips);
        Assert.Equal(2, placement.HiddenCount);
        AssertPostcondition(placement, Style());
    }

    [Fact]
    public void Omits_the_marker_entirely_when_asked_to()
    {
        var style = Style(showOverflowMarker: false);

        var placement = Fill([AllDay("A"), AllDay("B"), AllDay("C")], availableHeight: 30f, style: style);

        Assert.Null(placement.OverflowMarker);
        Assert.True(placement.HiddenCount > 0);
        AssertPostcondition(placement, style);
    }

    [Fact]
    public void Chips_stack_downwards_without_overlapping()
    {
        var placement = Fill([AllDay("A"), AllDay("B"), AllDay("C")], availableHeight: 200f);

        for (var i = 1; i < placement.Chips.Count; i++)
        {
            Assert.True(
                placement.Chips[i].Bounds.Top >= placement.Chips[i - 1].Bounds.Bottom - 0.01f,
                "Chips overlap, so one event is drawn on top of another.");
        }
    }

    [Fact]
    public void Prefixes_a_start_time_when_asked_to()
    {
        var style = Style() with { ShowStartTime = true };

        var placement = Fill([Timed("Standup", 9, 30)], availableHeight: 200f, availableWidth: 300f, style: style);

        var chip = Assert.Single(placement.Chips);
        Assert.Contains("9:30", string.Join(" ", chip.Text.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public void Does_not_prefix_a_time_onto_an_all_day_event()
    {
        // An all-day event has no clock time. Printing one would be inventing information.
        var style = Style() with { ShowStartTime = true };

        var placement = Fill([AllDay("Conference")], availableHeight: 200f, availableWidth: 300f, style: style);

        var chip = Assert.Single(placement.Chips);
        Assert.Equal("Conference", string.Join(" ", chip.Text.Lines));
    }

    [Fact]
    public void The_time_prefix_is_wrapped_with_the_title_not_positioned_separately()
    {
        // Laying the time out as its own run is how you get a time that fits beside a title
        // that does not, and a chip whose measured height is a line short.
        var style = Style(maxLines: 2) with { ShowStartTime = true };

        var placement = Fill(
            [Timed("Quarterly budget review", 14)],
            availableHeight: 200f,
            availableWidth: 60f,
            style: style);

        var chip = Assert.Single(placement.Chips);

        Assert.All(
            chip.Text.Lines,
            line => Assert.True(Measurer.MeasureWidth(style.TitleFont, line) <= chip.TextWidth + 0.01f));
    }
}
