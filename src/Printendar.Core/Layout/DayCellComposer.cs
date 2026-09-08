using System.Globalization;
using Printendar.Core.Model;
using Printendar.Core.Text;
using SkiaSharp;

namespace Printendar.Core.Layout;

/// <param name="Chips">The events that fit, stacked top to bottom.</param>
/// <param name="HiddenCount">Events that did not fit.</param>
/// <param name="OverflowMarker">Text such as "+3 more", or null when nothing is hidden.</param>
/// <param name="MarkerBounds">Where the marker sits. Meaningless when there is no marker.</param>
/// <param name="UsedHeight">Height consumed by the chips, excluding the marker.</param>
/// <param name="AvailableHeight">Height the cell offered.</param>
public sealed record CellPlacement(
    IReadOnlyList<ChipLayout> Chips,
    int HiddenCount,
    string? OverflowMarker,
    SKRect MarkerBounds,
    float UsedHeight,
    float AvailableHeight);

/// <summary>
/// Stacks event chips into one day cell and decides what has to be hidden.
/// </summary>
/// <remarks>
/// Guarantees, for any input:
///
///   usedHeight + (hidden &gt; 0 ? markerHeight : 0) &lt;= availableHeight
///
/// and that every event is either placed or counted in <see cref="CellPlacement.HiddenCount"/>.
/// Nothing is ever dropped silently.
///
/// Two mechanisms hold that up. The marker's height is reserved before each chip is placed, so
/// the last chip cannot claim the space the marker needs. And when reservation was not enough,
/// because the cell held exactly the chips it was given and one more event turned up, the
/// composer pops a chip back off to make room. Without that backtrack, a cell that fits two
/// chips and has three events has nowhere to say so, and the third disappears.
/// </remarks>
public static class DayCellComposer
{
    public static CellPlacement Fill(
        SKRect cell,
        IReadOnlyList<CalendarEvent> events,
        in ChipStyle style,
        Func<CalendarEvent, SKColor> colorOf,
        ITextMeasurer measurer,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(colorOf);
        ArgumentNullException.ThrowIfNull(measurer);

        var available = cell.Height;

        if (events.Count == 0 || available <= 0f)
        {
            return new CellPlacement([], events.Count, null, SKRect.Empty, 0f, Math.Max(available, 0f));
        }

        var markerHeight = style.ShowOverflowMarker
            ? measurer.GetMetrics(style.MarkerFont).LineHeight + style.ChipGap
            : 0f;

        var placed = new List<ChipLayout>(events.Count);
        var used = 0f;
        var index = 0;

        for (; index < events.Count; index++)
        {
            var height = EventChipComposer.MeasureHeight(events[index], cell.Width, style, measurer, culture);
            var gap = placed.Count == 0 ? 0f : style.ChipGap;

            // Reserve room for the marker before placing, whenever anything would be left over.
            var reserve = index < events.Count - 1 ? markerHeight : 0f;

            if (used + gap + height + reserve > available)
            {
                break;
            }

            var top = cell.Top + used + gap;
            placed.Add(EventChipComposer.Compose(
                events[index],
                new SKRect(cell.Left, top, cell.Right, top + height),
                style,
                colorOf(events[index]),
                measurer,
                culture));

            used += gap + height;
        }

        var hidden = events.Count - placed.Count;

        // Backtrack. Reservation covers the common case, but not the one where every event fit
        // exactly and the reserve was never applied to the final placement.
        while (hidden > 0 && markerHeight > 0f && used + markerHeight > available && placed.Count > 0)
        {
            var last = placed[^1];
            placed.RemoveAt(placed.Count - 1);
            used = placed.Count == 0 ? 0f : last.Bounds.Top - cell.Top;
            hidden++;
        }

        if (hidden == 0)
        {
            return new CellPlacement(placed, 0, null, SKRect.Empty, used, available);
        }

        var marker = style.ShowOverflowMarker ? BuildMarker(hidden, culture) : null;
        var markerTop = cell.Top + used + (placed.Count == 0 ? 0f : style.ChipGap);
        var markerBounds = new SKRect(cell.Left, markerTop, cell.Right, markerTop + Math.Max(markerHeight - style.ChipGap, 0f));

        // A marker that will not fit is worse than none: it would overflow the cell it exists
        // to keep tidy. The hidden count is still reported, so the caller can surface it.
        if (marker is not null && markerBounds.Bottom > cell.Bottom + 0.01f)
        {
            marker = null;
        }

        return new CellPlacement(placed, hidden, marker, markerBounds, used, available);
    }

    private static string BuildMarker(int hidden, CultureInfo culture) =>
        string.Format(culture, "+{0} more", hidden);
}
