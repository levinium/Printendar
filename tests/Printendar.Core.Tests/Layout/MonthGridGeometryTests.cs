using Printendar.Core.Layout.Month;
using SkiaSharp;

namespace Printendar.Core.Tests.Layout;

/// <summary>
/// Pure rectangle arithmetic: how many week rows, which columns, and where every day sits.
/// </summary>
/// <remarks>
/// Geometry is computed before a single event is looked at, which is what lets the fit problem
/// decompose per cell. It knows nothing about fonts, events or Skia beyond the rectangle type,
/// so it is the cheapest thing in the project to test exhaustively.
///
/// The month fixtures are chosen because, with a Sunday week start in 2026, February needs
/// four week rows, March five, and May six. All three shapes have to work.
/// </remarks>
public class MonthGridGeometryTests
{
    private const float Tolerance = 0.01f;
    private static readonly SKRect GridBox = new(0f, 0f, 700f, 500f);
    private const float HeaderRowHeight = 20f;

    private static MonthGridGeometry Compute(
        int year,
        int month,
        DayOfWeek firstDayOfWeek = DayOfWeek.Sunday,
        WeekendMode weekendMode = WeekendMode.FullSevenDay) =>
        MonthGridGeometry.Compute(
            GridBox,
            new MonthGridOptions(year, month, firstDayOfWeek, weekendMode),
            HeaderRowHeight);

    [Theory]
    [InlineData(2026, 2, 4)]  // 28 days starting on the week-start day
    [InlineData(2026, 3, 5)]  // the common case
    [InlineData(2026, 5, 6)]  // 31 days starting late in the week
    public void Uses_as_many_week_rows_as_the_month_spans(int year, int month, int expectedRows)
    {
        Assert.Equal(expectedRows, Compute(year, month).WeekRowCount);
    }

    [Fact]
    public void Week_row_count_is_always_between_four_and_six()
    {
        // Property over every month in a leap year and a common year, both week starts. A
        // seventh row would mean the grid had silently grown past the page.
        foreach (var year in (int[])[2026, 2028])
        {
            foreach (var month in Enumerable.Range(1, 12))
            {
                foreach (var start in (DayOfWeek[])[DayOfWeek.Sunday, DayOfWeek.Monday])
                {
                    var rows = Compute(year, month, start).WeekRowCount;

                    Assert.True(rows is >= 4 and <= 6, $"{year}-{month:00} starting {start} produced {rows} rows.");
                }
            }
        }
    }

    [Fact]
    public void Every_day_of_the_month_appears_exactly_once()
    {
        var geometry = Compute(2026, 3);

        var inMonth = geometry.Cells.Where(c => c.IsInMonth).Select(c => c.Date).ToArray();

        Assert.Equal(31, inMonth.Length);
        Assert.Equal(inMonth.Length, inMonth.Distinct().Count());
        Assert.Equal(new DateOnly(2026, 3, 1), inMonth.Min());
        Assert.Equal(new DateOnly(2026, 3, 31), inMonth.Max());
    }

    [Fact]
    public void Pads_with_adjacent_days_so_every_week_row_is_complete()
    {
        // March 2026 starts on a Sunday, so there are no leading days, but the grid still runs
        // to the following Saturday.
        var geometry = Compute(2026, 3);

        Assert.Equal(new DateOnly(2026, 3, 1), geometry.VisibleSpan.First);
        Assert.Equal(new DateOnly(2026, 4, 4), geometry.VisibleSpan.Last);
        Assert.Equal(35, geometry.Cells.Count);
        Assert.Equal(4, geometry.Cells.Count(c => !c.IsInMonth));
    }

    [Fact]
    public void A_monday_week_start_shifts_the_leading_days()
    {
        // March 2026 opens on a Sunday, so a Monday-start grid has to reach back six days.
        var geometry = Compute(2026, 3, DayOfWeek.Monday);

        Assert.Equal(new DateOnly(2026, 2, 23), geometry.VisibleSpan.First);
        Assert.Equal(DayOfWeek.Monday, geometry.Columns[0].Days[0]);
    }

    [Fact]
    public void Full_seven_day_mode_produces_seven_equal_columns()
    {
        var geometry = Compute(2026, 3);

        Assert.Equal(7, geometry.Columns.Count);
        Assert.All(geometry.Columns, c => Assert.Equal(GridBox.Width / 7f, c.Width, Tolerance));
    }

    [Fact]
    public void Columns_tile_the_grid_box_with_no_gap_or_overhang()
    {
        // Accumulating fractional widths leaves a sliver at the right edge that shows up as a
        // hairline in the printed rule. The last column's right edge is snapped to the box.
        foreach (var mode in Enum.GetValues<WeekendMode>())
        {
            var start = mode is WeekendMode.CompressedWeekendColumn ? DayOfWeek.Monday : DayOfWeek.Sunday;
            var geometry = Compute(2026, 3, start, mode);

            Assert.Equal(GridBox.Left, geometry.Columns[0].X, Tolerance);
            Assert.Equal(GridBox.Right, geometry.Columns[^1].X + geometry.Columns[^1].Width, Tolerance);

            for (var i = 1; i < geometry.Columns.Count; i++)
            {
                Assert.Equal(
                    geometry.Columns[i - 1].X + geometry.Columns[i - 1].Width,
                    geometry.Columns[i].X,
                    Tolerance);
            }
        }
    }

    [Fact]
    public void Weekdays_only_mode_drops_the_weekend_columns()
    {
        var geometry = Compute(2026, 3, DayOfWeek.Monday, WeekendMode.WeekdaysOnly);

        Assert.Equal(5, geometry.Columns.Count);
        Assert.DoesNotContain(
            geometry.Cells,
            c => c.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    [Fact]
    public void Compressed_weekend_mode_shares_one_narrow_column_between_two_half_height_cells()
    {
        var geometry = Compute(2026, 3, DayOfWeek.Monday, WeekendMode.CompressedWeekendColumn);

        Assert.Equal(6, geometry.Columns.Count);

        var weekend = geometry.Columns[^1];
        Assert.Equal([DayOfWeek.Saturday, DayOfWeek.Sunday], weekend.Days);
        Assert.True(weekend.Width < geometry.Columns[0].Width, "The shared weekend column should be narrower than a weekday column.");

        var saturdays = geometry.Cells.Where(c => c.Date.DayOfWeek is DayOfWeek.Saturday).ToArray();
        Assert.NotEmpty(saturdays);
        Assert.All(saturdays, c => Assert.Equal(2, c.SubSlotCount));
        Assert.All(saturdays, c => Assert.Equal(geometry.WeekRowHeight / 2f, c.Bounds.Height, Tolerance));
    }

    [Fact]
    public void Compressed_weekend_mode_is_rejected_with_a_sunday_week_start()
    {
        // With Sunday first the weekend sits at both ends of the grid, so there is no single
        // column to compress. Better to refuse than to render something incoherent.
        var options = new MonthGridOptions(2026, 3, DayOfWeek.Sunday, WeekendMode.CompressedWeekendColumn);

        var error = Assert.Throws<ArgumentException>(options.Validate);

        Assert.Contains("Monday", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Week_rows_tile_the_grid_below_the_header()
    {
        var geometry = Compute(2026, 3);

        var firstRow = geometry.Cells.Where(c => c.Row == 0).ToArray();
        var lastRow = geometry.Cells.Where(c => c.Row == geometry.WeekRowCount - 1).ToArray();

        Assert.All(firstRow, c => Assert.Equal(GridBox.Top + HeaderRowHeight, c.Bounds.Top, Tolerance));
        Assert.All(lastRow, c => Assert.Equal(GridBox.Bottom, c.Bounds.Bottom, Tolerance));
    }

    [Fact]
    public void No_cell_falls_outside_the_grid_box()
    {
        foreach (var month in Enumerable.Range(1, 12))
        {
            var geometry = Compute(2026, month);

            Assert.All(geometry.Cells, c => Assert.True(
                c.Bounds.Left >= GridBox.Left - Tolerance &&
                c.Bounds.Right <= GridBox.Right + Tolerance &&
                c.Bounds.Top >= GridBox.Top - Tolerance &&
                c.Bounds.Bottom <= GridBox.Bottom + Tolerance,
                $"{c.Date:yyyy-MM-dd} at {c.Bounds} escapes the grid box {GridBox}."));
        }
    }

    [Fact]
    public void Finds_the_cell_for_a_date_in_the_visible_span()
    {
        var geometry = Compute(2026, 3);

        Assert.True(geometry.TryGetCell(new DateOnly(2026, 3, 17), out var cell));
        Assert.Equal(new DateOnly(2026, 3, 17), cell.Date);
        Assert.True(cell.IsInMonth);
    }

    [Fact]
    public void Reports_no_cell_for_a_date_outside_the_visible_span()
    {
        var geometry = Compute(2026, 3);

        Assert.False(geometry.TryGetCell(new DateOnly(2026, 6, 1), out _));
    }
}
