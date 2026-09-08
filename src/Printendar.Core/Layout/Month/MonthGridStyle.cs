using System.Globalization;
using Printendar.Core.Layout.Fit;
using Printendar.Core.Model;
using Printendar.Core.Scene;
using Printendar.Core.Text;
using SkiaSharp;

namespace Printendar.Core.Layout.Month;

/// <summary>
/// A calendar month on one page.
/// </summary>
/// <remarks>
/// The style this project exists for. Classic Outlook's Monthly Style could put a month on one
/// landscape sheet; new Outlook and Outlook on the web cannot.
///
/// The order of work matters. Chrome takes what it needs first, geometry is computed from what
/// remains, and only then is any event measured. Fixing the grid before looking at content is
/// what turns "will it fit" from one global question into a set of independent per-cell ones,
/// which is what makes the scale search a simple monotone predicate.
/// </remarks>
public sealed class MonthGridStyle : IPrintStyle
{
    public string Id => "month-grid";

    // Base typography, at scale 1. Everything that scales is listed in ChipStyle.Scaled or
    // multiplied here; nothing may be left at a fixed size, or the fit search stops being
    // monotone and quietly returns a scale that does not fit.
    private const float TitleSizePt = 16f;
    private const float WeekdayHeaderSizePt = 8.5f;
    private const float DayNumberSizePt = 9f;
    private const float LegendSizePt = 7.5f;

    private const float TitleGapPt = 6f;
    private const float CellPaddingPt = 3f;
    private const float RuleWidthPt = 0.5f;
    private const float SwatchWidthPt = 2.5f;
    private const float ChipInnerPaddingPt = 1f;
    private const float ChipGapPt = 1.5f;
    private const float LegendGapPt = 5f;
    private const float LegendSwatchPt = 6f;

    private static readonly SKColor RuleColor = new(0xC8, 0xC8, 0xC8);
    private static readonly SKColor HeaderRuleColor = new(0x88, 0x88, 0x88);
    private static readonly SKColor InkColor = SKColors.Black;
    private static readonly SKColor MutedInkColor = new(0xA0, 0xA0, 0xA0);
    private static readonly SKColor HeaderFillColor = new(0xF2, 0xF2, 0xF2);
    private static readonly SKColor FallbackEventColor = new(0x55, 0x55, 0x55);

    public ScenePage Layout(LayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Page.Validate();
        request.Grid.Validate();

        var measurer = request.Measurer;
        var chrome = new List<SceneNode>();

        var afterTitle = ComposeTitle(request, request.Page.ContentBox, chrome);
        var gridBox = ComposeLegend(request, afterTitle, chrome);

        var headerFont = new FontSpec(FontWeightKind.SemiBold, WeekdayHeaderSizePt);
        var headerRowHeight = measurer.GetMetrics(headerFont).LineHeight * 1.6f;
        var geometry = MonthGridGeometry.Compute(gridBox, request.Grid, headerRowHeight);

        var buckets = BucketEvents(request, geometry, out var droppedWeekendEvents);
        var fit = ResolveFit(request, geometry, buckets);

        var children = new List<SceneNode>(chrome)
        {
            ComposeWeekdayHeader(request, geometry, headerFont),
            ComposeCells(request, geometry, buckets, fit),
            ComposeRules(geometry),
        };

        var page = new ScenePage(
            request.Page,
            new SceneGroup("root", children, Clip: null),
            BuildDiagnostics(request, geometry, fit, droppedWeekendEvents));

        // Cheap insurance against a layout mistake reaching a printer. The exporter checks
        // again and the tests check every combination, but failing here names the style.
        SceneValidator.ThrowIfInvalid(page, measurer);

        return page;
    }

    // ---------------------------------------------------------------- fitting

    /// <param name="Scale">The typography scale settled on.</param>
    /// <param name="Style">Chip style at that scale.</param>
    /// <param name="Placements">What each cell ended up holding.</param>
    /// <param name="CappedAwayCount">
    /// Events removed by <see cref="FitPolicy.CapPerDay"/> before the cells were composed.
    /// </param>
    private sealed record FitResult(
        float Scale,
        ChipStyle Style,
        IReadOnlyDictionary<DateOnly, CellPlacement> Placements,
        int CappedAwayCount)
    {
        /// <summary>
        /// Every event not drawn, whichever stage discarded it.
        /// </summary>
        /// <remarks>
        /// The capped-away events have to be counted here as well as the ones the cell could
        /// not hold. Capping happens before composition, so the composer never sees them and
        /// cannot report them, and without this they would disappear from the page and from
        /// the diagnostics both, which is the one thing this layout must never do.
        /// </remarks>
        public int HiddenCount => Placements.Values.Sum(p => p.HiddenCount) + CappedAwayCount;
    }

    /// <summary>
    /// Searches the scale ladder for the largest type that still fits, then applies the policy.
    /// </summary>
    private FitResult ResolveFit(
        LayoutRequest request,
        MonthGridGeometry geometry,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<CalendarEvent>> buckets)
    {
        var options = request.Fit;

        var capped = buckets;
        var cappedAway = 0;

        if (options.Policy is FitPolicy.CapPerDay)
        {
            capped = Cap(buckets, options.MaxEventsPerDay);
            cappedAway = buckets.Values.Sum(events => Math.Max(events.Count - options.MaxEventsPerDay, 0));
        }

        var ladder = options.Ladder;
        var found = FitSearch.FindLargestFitting(ladder, scale => Compose(scale).HiddenCount == 0);

        var scale = (options.Policy, found) switch
        {
            // Everything fits, and comfortably. Nothing to trade off.
            (_, >= 0) when ladder.Rungs[found] >= options.MinReadableScale => ladder.Rungs[found],

            // It would fit, but only by shrinking past readable. Readability wins; the
            // remainder becomes overflow markers.
            (FitPolicy.Hybrid, _) => options.MinReadableScale,

            // AutoShrink and CapPerDay are willing to go to the hard floor.
            (_, >= 0) => ladder.Rungs[found],
            _ => options.MinScale,
        };

        return Compose(scale);

        FitResult Compose(float scale)
        {
            var style = BaseChipStyle(request).Scaled(scale);
            var placements = new Dictionary<DateOnly, CellPlacement>(geometry.Cells.Count);

            foreach (var cell in geometry.Cells)
            {
                var events = capped.TryGetValue(cell.Date, out var forDay) ? forDay : [];
                placements[cell.Date] = DayCellComposer.Fill(
                    EventAreaOf(cell, request, scale),
                    events,
                    style,
                    e => ColorFor(e, request),
                    request.Measurer,
                    request.Culture);
            }

            return new FitResult(scale, style, placements, cappedAway);
        }
    }

    private static Dictionary<DateOnly, IReadOnlyList<CalendarEvent>> Cap(
        IReadOnlyDictionary<DateOnly, IReadOnlyList<CalendarEvent>> buckets,
        int maxPerDay) =>
        buckets.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<CalendarEvent>)[.. pair.Value.Take(maxPerDay)]);

    private ChipStyle BaseChipStyle(LayoutRequest request) => new(
        TitleFont: new FontSpec(FontWeightKind.Regular, FitOptions.BaseEventSizePt),
        TimeFont: new FontSpec(FontWeightKind.Regular, FitOptions.BaseEventSizePt),
        MarkerFont: new FontSpec(FontWeightKind.Regular, FitOptions.BaseEventSizePt),
        SwatchWidth: SwatchWidthPt,
        InnerPadding: ChipInnerPaddingPt,
        ChipGap: ChipGapPt,
        LineSpacing: 0f,
        MaxLines: request.Fit.MaxLinesPerEvent,
        ShowStartTime: request.Style.ShowStartTime,
        ShowOverflowMarker: request.Fit.ShowOverflowMarker,
        TimeFormat: request.Style.TimeFormat);

    /// <summary>
    /// The part of a day cell left for events, below the day number.
    /// </summary>
    /// <remarks>
    /// Scales with the search, like everything else. A day number band of fixed height while
    /// the event type shrank would break monotonicity.
    /// </remarks>
    private static SKRect EventAreaOf(DayCell cell, LayoutRequest request, float scale)
    {
        var dayNumberFont = new FontSpec(FontWeightKind.SemiBold, DayNumberSizePt * scale);
        var band = request.Measurer.GetMetrics(dayNumberFont).LineHeight;
        var padding = CellPaddingPt * scale;

        return new SKRect(
            cell.Bounds.Left + padding,
            cell.Bounds.Top + padding + band,
            cell.Bounds.Right - padding,
            cell.Bounds.Bottom - padding);
    }

    // ---------------------------------------------------------------- events

    private static Dictionary<DateOnly, IReadOnlyList<CalendarEvent>> BucketEvents(
        LayoutRequest request,
        MonthGridGeometry geometry,
        out int droppedWeekendEvents)
    {
        var visibleDays = geometry.Cells.Select(c => c.Date).ToHashSet();
        var buckets = new Dictionary<DateOnly, List<CalendarEvent>>();
        var dropped = 0;

        foreach (var calendarEvent in request.Events)
        {
            var landedAnywhere = false;

            // A multi-day event appears in every day it covers. Outlook draws a spanning bar
            // instead; that needs a band allocator above the cells and is a later change.
            foreach (var day in calendarEvent.Days.Days())
            {
                if (!visibleDays.Contains(day))
                {
                    continue;
                }

                if (!buckets.TryGetValue(day, out var list))
                {
                    buckets[day] = list = [];
                }

                list.Add(calendarEvent);
                landedAnywhere = true;
            }

            // In weekdays-only mode the weekend has no column at all, so an event only on a
            // Saturday has nowhere to go. Counting it lets the UI say so rather than leaving
            // the user to notice something missing.
            if (!landedAnywhere && geometry.VisibleSpan.Intersects(calendarEvent.Days))
            {
                dropped++;
            }
        }

        droppedWeekendEvents = dropped;

        return buckets.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                var ordered = pair.Value.ToList();
                ordered.Sort(EventOrder.Comparer);
                return (IReadOnlyList<CalendarEvent>)ordered;
            });
    }

    private static SKColor ColorFor(CalendarEvent calendarEvent, LayoutRequest request)
    {
        foreach (var calendar in request.Calendars)
        {
            if (string.Equals(calendar.CalendarId, calendarEvent.CalendarId, StringComparison.Ordinal))
            {
                return calendar.Color;
            }
        }

        return FallbackEventColor;
    }

    // ---------------------------------------------------------------- chrome

    private static SKRect ComposeTitle(LayoutRequest request, SKRect content, List<SceneNode> children)
    {
        var titleFont = new FontSpec(FontWeightKind.SemiBold, TitleSizePt);
        var title = request.Grid.FirstOfMonth
            .ToDateTime(TimeOnly.MinValue)
            .ToString("MMMM yyyy", request.Culture);

        var metrics = request.Measurer.GetMetrics(titleFont);

        children.Add(new SceneGroup("title",
        [
            new SceneTextRun(
                title,
                new SKPoint(content.Left, content.Top - metrics.Ascent),
                titleFont,
                InkColor,
                TextAnchor.Left,
                MaxWidth: content.Width),
        ], Clip: null));

        return new SKRect(content.Left, content.Top + metrics.LineHeight + TitleGapPt, content.Right, content.Bottom);
    }

    /// <summary>
    /// Draws the calendar legend, and returns the box left for the grid.
    /// </summary>
    /// <remarks>
    /// Fixed size, deliberately excluded from the scale search. A legend shrunk until it is
    /// unreadable has stopped doing the one thing it was for.
    /// </remarks>
    private static SKRect ComposeLegend(LayoutRequest request, SKRect available, List<SceneNode> children)
    {
        var used = request.Calendars
            .Where(c => request.Events.Any(e => string.Equals(e.CalendarId, c.CalendarId, StringComparison.Ordinal)))
            .ToList();

        if (!request.Style.ShowLegend || used.Count < 2)
        {
            return available;
        }

        var font = new FontSpec(FontWeightKind.Regular, LegendSizePt);
        var metrics = request.Measurer.GetMetrics(font);
        var nodes = new List<SceneNode>();

        var x = available.Left;
        var top = available.Top;
        var baseline = top - metrics.Ascent;

        foreach (var calendar in used)
        {
            var width = request.Measurer.MeasureWidth(font, calendar.DisplayName);
            var entryWidth = LegendSwatchPt + 3f + width + LegendGapPt;

            if (x + entryWidth > available.Right)
            {
                break;
            }

            var swatchTop = baseline + metrics.Ascent + ((metrics.LineHeight - LegendSwatchPt) / 2f);

            nodes.Add(new SceneRect(
                new SKRect(x, swatchTop, x + LegendSwatchPt, swatchTop + LegendSwatchPt),
                calendar.Color,
                null,
                0f));

            nodes.Add(new SceneTextRun(
                calendar.DisplayName,
                new SKPoint(x + LegendSwatchPt + 3f, baseline),
                font,
                InkColor,
                TextAnchor.Left,
                MaxWidth: width));

            x += entryWidth;
        }

        children.Add(new SceneGroup("legend", nodes, Clip: null));

        return new SKRect(available.Left, top + metrics.LineHeight + LegendGapPt, available.Right, available.Bottom);
    }

    private static SceneGroup ComposeWeekdayHeader(
        LayoutRequest request,
        MonthGridGeometry geometry,
        FontSpec headerFont)
    {
        var metrics = request.Measurer.GetMetrics(headerFont);
        var top = geometry.GridBox.Top;
        var bottom = top + geometry.HeaderRowHeight;
        var baseline = top + ((geometry.HeaderRowHeight - metrics.LineHeight) / 2f) - metrics.Ascent;

        var nodes = new List<SceneNode>
        {
            new SceneRect(new SKRect(geometry.GridBox.Left, top, geometry.GridBox.Right, bottom), HeaderFillColor, null, 0f),
        };

        foreach (var column in geometry.Columns)
        {
            var label = string.Join(" / ", column.Days.Select(d => AbbreviatedDayName(d, request.Culture)));
            var available = column.Width - (2f * CellPaddingPt);

            // A narrow shared weekend column cannot hold "Sat / Sun", so the label falls back
            // to initials rather than being drawn over the column rule.
            if (request.Measurer.MeasureWidth(headerFont, label) > available && column.Days.Count > 1)
            {
                label = string.Join("/", column.Days.Select(d => AbbreviatedDayName(d, request.Culture)[..1]));
            }

            nodes.Add(new SceneTextRun(
                label,
                new SKPoint(column.X + (column.Width / 2f), baseline),
                headerFont,
                InkColor,
                TextAnchor.Center,
                MaxWidth: Math.Max(available, 0f)));
        }

        return new SceneGroup("weekday-header", nodes, Clip: null);
    }

    private static string AbbreviatedDayName(DayOfWeek day, CultureInfo culture) =>
        culture.DateTimeFormat.AbbreviatedDayNames[(int)day];

    // ---------------------------------------------------------------- cells

    private static SceneGroup ComposeCells(
        LayoutRequest request,
        MonthGridGeometry geometry,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<CalendarEvent>> buckets,
        FitResult fit)
    {
        var dayNumberFont = new FontSpec(FontWeightKind.SemiBold, DayNumberSizePt * fit.Scale);
        var metrics = request.Measurer.GetMetrics(dayNumberFont);
        var padding = CellPaddingPt * fit.Scale;
        var groups = new List<SceneNode>(geometry.Cells.Count);

        foreach (var cell in geometry.Cells)
        {
            var hideEntirely = !cell.IsInMonth && request.Grid.AdjacentDays is AdjacentDayMode.Hidden;

            if (hideEntirely)
            {
                continue;
            }

            var ink = cell.IsInMonth || request.Grid.AdjacentDays is AdjacentDayMode.Full ? InkColor : MutedInkColor;
            var nodes = new List<SceneNode>
            {
                new SceneTextRun(
                    cell.Date.Day.ToString(request.Culture),
                    new SKPoint(cell.Bounds.Right - padding, cell.Bounds.Top + padding - metrics.Ascent),
                    dayNumberFont,
                    ink,
                    TextAnchor.Right,
                    MaxWidth: Math.Max(cell.Bounds.Width - (2f * padding), 0f)),
            };

            if (fit.Placements.TryGetValue(cell.Date, out var placement))
            {
                AppendChips(nodes, placement, fit.Style, request);
            }

            groups.Add(new SceneGroup($"day-{cell.Date:yyyy-MM-dd}", nodes, Clip: cell.Bounds));
        }

        return new SceneGroup("cells", groups, Clip: null);
    }

    private static void AppendChips(
        List<SceneNode> nodes,
        CellPlacement placement,
        in ChipStyle style,
        LayoutRequest request)
    {
        var metrics = request.Measurer.GetMetrics(style.TitleFont);

        foreach (var chip in placement.Chips)
        {
            // A colour bar down the left edge rather than a filled block. It survives a
            // grayscale print as a visible mark, and it leaves the title on white so small
            // type stays legible.
            nodes.Add(new SceneRect(
                new SKRect(
                    chip.Bounds.Left,
                    chip.Bounds.Top,
                    chip.Bounds.Left + style.SwatchWidth,
                    chip.Bounds.Bottom),
                chip.Color,
                null,
                0f));

            var textLeft = chip.Bounds.Left + style.SwatchWidth + (2f * style.InnerPadding);
            var y = chip.Bounds.Top + style.InnerPadding - metrics.Ascent;

            foreach (var line in chip.Text.Lines)
            {
                nodes.Add(new SceneTextRun(
                    line,
                    new SKPoint(textLeft, y),
                    style.TitleFont,
                    InkColor,
                    TextAnchor.Left,
                    MaxWidth: chip.TextWidth));

                y += metrics.LineHeight + style.LineSpacing;
            }
        }

        if (placement.OverflowMarker is { } marker)
        {
            var markerMetrics = request.Measurer.GetMetrics(style.MarkerFont);

            nodes.Add(new SceneTextRun(
                marker,
                new SKPoint(placement.MarkerBounds.Left, placement.MarkerBounds.Top - markerMetrics.Ascent),
                style.MarkerFont,
                MutedInkColor,
                TextAnchor.Left,
                MaxWidth: placement.MarkerBounds.Width));
        }
    }

    // ---------------------------------------------------------------- rules

    /// <summary>
    /// The grid rules, drawn as straight lines rather than stroked rectangles.
    /// </summary>
    /// <remarks>
    /// Stroking a rectangle per cell would draw every interior rule twice, so shared edges
    /// print twice as dark as the outer ones.
    /// </remarks>
    private static SceneGroup ComposeRules(MonthGridGeometry geometry)
    {
        var box = geometry.GridBox;
        var rowsTop = box.Top + geometry.HeaderRowHeight;

        var nodes = new List<SceneNode>
        {
            new SceneLine(new SKPoint(box.Left, box.Top), new SKPoint(box.Right, box.Top), HeaderRuleColor, RuleWidthPt),
            new SceneLine(new SKPoint(box.Left, box.Bottom), new SKPoint(box.Right, box.Bottom), HeaderRuleColor, RuleWidthPt),
            new SceneLine(new SKPoint(box.Left, box.Top), new SKPoint(box.Left, box.Bottom), HeaderRuleColor, RuleWidthPt),
            new SceneLine(new SKPoint(box.Right, box.Top), new SKPoint(box.Right, box.Bottom), HeaderRuleColor, RuleWidthPt),
            new SceneLine(new SKPoint(box.Left, rowsTop), new SKPoint(box.Right, rowsTop), HeaderRuleColor, RuleWidthPt),
        };

        for (var row = 1; row < geometry.WeekRowCount; row++)
        {
            var y = rowsTop + (row * geometry.WeekRowHeight);
            nodes.Add(new SceneLine(new SKPoint(box.Left, y), new SKPoint(box.Right, y), RuleColor, RuleWidthPt));
        }

        for (var i = 1; i < geometry.Columns.Count; i++)
        {
            var x = geometry.Columns[i].X;
            nodes.Add(new SceneLine(new SKPoint(x, box.Top), new SKPoint(x, box.Bottom), RuleColor, RuleWidthPt));
        }

        foreach (var cell in geometry.Cells.Where(c => c.SubSlotCount > 1 && c.SubSlot > 0))
        {
            nodes.Add(new SceneLine(
                new SKPoint(cell.Bounds.Left, cell.Bounds.Top),
                new SKPoint(cell.Bounds.Right, cell.Bounds.Top),
                RuleColor,
                RuleWidthPt));
        }

        return new SceneGroup("rules", nodes, Clip: null);
    }

    // ---------------------------------------------------------------- diagnostics

    private static LayoutDiagnostics BuildDiagnostics(
        LayoutRequest request,
        MonthGridGeometry geometry,
        FitResult fit,
        int droppedWeekendEvents)
    {
        var smallestPt = FitOptions.BaseEventSizePt * fit.Scale;
        var warnings = new List<string>();

        if (smallestPt < FitOptions.SmallTypeWarningPt)
        {
            warnings.Add(
                $"Event text is {smallestPt:0.#} point, which is hard to read on paper. " +
                "Try a larger paper size, fewer calendars, or capping events per day.");
        }

        if (fit.HiddenCount > 0)
        {
            warnings.Add(
                $"{fit.HiddenCount} event(s) did not fit and are shown as \"+N more\". " +
                "A larger paper size or Legal in landscape will hold more.");
        }

        if (droppedWeekendEvents > 0)
        {
            warnings.Add(
                $"{droppedWeekendEvents} event(s) fall on a weekend, which this layout does not show at all.");
        }

        return new LayoutDiagnostics(
            EffectiveScale: fit.Scale,
            SmallestFontPt: smallestPt,
            HiddenEventCount: fit.HiddenCount,
            DroppedWeekendEventCount: droppedWeekendEvents,
            WeekRowCount: geometry.WeekRowCount,
            Warnings: warnings);
    }
}
