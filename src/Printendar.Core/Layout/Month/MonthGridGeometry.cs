using Printendar.Core.Model;
using SkiaSharp;

namespace Printendar.Core.Layout.Month;

/// <summary>One column of the grid, covering one weekday or the shared weekend pair.</summary>
/// <param name="Index">Position from the left, starting at zero.</param>
/// <param name="Days">The weekdays this column carries, in top-to-bottom order.</param>
/// <param name="Weight">Width relative to a plain weekday column.</param>
/// <param name="X">Left edge, in points.</param>
/// <param name="Width">Width, in points.</param>
public sealed record ColumnSpec(int Index, IReadOnlyList<DayOfWeek> Days, float Weight, float X, float Width);

/// <summary>One day's box in the grid.</summary>
/// <param name="Date">The day.</param>
/// <param name="IsInMonth">False for the adjacent days that pad the first and last week rows.</param>
/// <param name="Row">Week row, starting at zero.</param>
/// <param name="Column">Column index.</param>
/// <param name="SubSlot">Position within a shared column. Zero unless the weekend is compressed.</param>
/// <param name="SubSlotCount">How many days share this column. One unless the weekend is compressed.</param>
/// <param name="Bounds">The cell rectangle, in points.</param>
public sealed record DayCell(
    DateOnly Date,
    bool IsInMonth,
    int Row,
    int Column,
    int SubSlot,
    int SubSlotCount,
    SKRect Bounds);

/// <summary>
/// Where the weekday header, the week rows and every day cell sit inside the grid box.
/// </summary>
/// <remarks>
/// Deliberately ignorant of fonts, events and Skia beyond the rectangle type. Geometry is
/// fixed before any event is measured, which is what lets the fit problem decompose into
/// independent per-cell questions and makes the whole thing testable as arithmetic.
/// </remarks>
public sealed record MonthGridGeometry(
    SKRect GridBox,
    float HeaderRowHeight,
    IReadOnlyList<ColumnSpec> Columns,
    int WeekRowCount,
    float WeekRowHeight,
    IReadOnlyList<DayCell> Cells,
    DateSpan VisibleSpan)
{
    private static readonly DayOfWeek[] WeekendDays = [DayOfWeek.Saturday, DayOfWeek.Sunday];

    public static MonthGridGeometry Compute(SKRect gridBox, MonthGridOptions options, float headerRowHeight)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var visible = VisibleSpanFor(options);
        var weekRowCount = visible.DayCount / 7;
        var columns = BuildColumns(gridBox, options);

        var rowsTop = gridBox.Top + headerRowHeight;
        var weekRowHeight = (gridBox.Bottom - rowsTop) / weekRowCount;

        var cells = BuildCells(options, visible, columns, weekRowCount, rowsTop, weekRowHeight, gridBox);

        return new MonthGridGeometry(
            gridBox,
            headerRowHeight,
            columns,
            weekRowCount,
            weekRowHeight,
            cells,
            visible);
    }

    /// <summary>Finds the cell for a date, if that date is on the grid at all.</summary>
    public bool TryGetCell(DateOnly date, out DayCell cell)
    {
        foreach (var candidate in Cells)
        {
            if (candidate.Date == date)
            {
                cell = candidate;
                return true;
            }
        }

        cell = null!;
        return false;
    }

    /// <summary>
    /// The first of the month rolled back to the week start, and the last rolled forward to
    /// the week end, so every row is a complete week.
    /// </summary>
    private static DateSpan VisibleSpanFor(MonthGridOptions options)
    {
        var first = options.FirstOfMonth;
        var last = options.LastOfMonth;

        var lead = ((int)first.DayOfWeek - (int)options.FirstDayOfWeek + 7) % 7;
        var trail = 6 - (((int)last.DayOfWeek - (int)options.FirstDayOfWeek + 7) % 7);

        return new DateSpan(first.AddDays(-lead), last.AddDays(trail));
    }

    /// <summary>The weekdays in column order, starting at the configured week start.</summary>
    private static DayOfWeek[] WeekdayOrder(DayOfWeek firstDayOfWeek) =>
        [.. Enumerable.Range(0, 7).Select(offset => (DayOfWeek)(((int)firstDayOfWeek + offset) % 7))];

    private static List<ColumnSpec> BuildColumns(SKRect gridBox, MonthGridOptions options)
    {
        var order = WeekdayOrder(options.FirstDayOfWeek);

        List<(DayOfWeek[] Days, float Weight)> spec = options.WeekendMode switch
        {
            WeekendMode.FullSevenDay =>
                [.. order.Select(d => (new[] { d }, 1f))],

            WeekendMode.WeekdaysOnly =>
                [.. order.Where(d => !WeekendDays.Contains(d)).Select(d => (new[] { d }, 1f))],

            // Validate has already guaranteed a Monday start here, so Saturday and Sunday are
            // adjacent and last, and collapse into a single trailing column.
            WeekendMode.CompressedWeekendColumn =>
                [
                    .. order.Where(d => !WeekendDays.Contains(d)).Select(d => (new[] { d }, 1f)),
                    (WeekendDays, options.WeekendColumnWeight),
                ],

            _ => throw new ArgumentOutOfRangeException(nameof(options), options.WeekendMode, "Unknown weekend mode."),
        };

        var totalWeight = spec.Sum(s => s.Weight);
        var columns = new List<ColumnSpec>(spec.Count);
        var x = gridBox.Left;

        for (var i = 0; i < spec.Count; i++)
        {
            // The last column's right edge is snapped to the box rather than accumulated.
            // Accumulating fractional widths leaves a sliver that prints as a hairline.
            var width = i == spec.Count - 1
                ? gridBox.Right - x
                : gridBox.Width * (spec[i].Weight / totalWeight);

            columns.Add(new ColumnSpec(i, spec[i].Days, spec[i].Weight, x, width));
            x += width;
        }

        return columns;
    }

    private static List<DayCell> BuildCells(
        MonthGridOptions options,
        DateSpan visible,
        List<ColumnSpec> columns,
        int weekRowCount,
        float rowsTop,
        float weekRowHeight,
        SKRect gridBox)
    {
        var columnFor = new Dictionary<DayOfWeek, (ColumnSpec Column, int SubSlot)>();

        foreach (var column in columns)
        {
            for (var slot = 0; slot < column.Days.Count; slot++)
            {
                columnFor[column.Days[slot]] = (column, slot);
            }
        }

        var cells = new List<DayCell>(weekRowCount * columns.Count);

        foreach (var date in visible.Days())
        {
            // In weekdays-only mode the weekend has no column, so those days are simply not
            // emitted. The caller reports how many events that dropped.
            if (!columnFor.TryGetValue(date.DayOfWeek, out var placement))
            {
                continue;
            }

            var row = (date.DayNumber - visible.First.DayNumber) / 7;
            var (column, subSlot) = placement;
            var subSlotCount = column.Days.Count;

            var slotHeight = weekRowHeight / subSlotCount;
            var top = rowsTop + (row * weekRowHeight) + (subSlot * slotHeight);

            // The bottom row is snapped to the grid box for the same reason the last column is.
            var isLastSlotOfLastRow = row == weekRowCount - 1 && subSlot == subSlotCount - 1;
            var bottom = isLastSlotOfLastRow ? gridBox.Bottom : top + slotHeight;

            cells.Add(new DayCell(
                Date: date,
                IsInMonth: date.Month == options.Month && date.Year == options.Year,
                Row: row,
                Column: column.Index,
                SubSlot: subSlot,
                SubSlotCount: subSlotCount,
                Bounds: new SKRect(column.X, top, column.X + column.Width, bottom)));
        }

        return cells;
    }
}
