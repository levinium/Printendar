using System.Globalization;
using Printendar.Core.Layout;
using Printendar.Core.Layout.Month;
using Printendar.Core.Paper;
using Printendar.Core.Scene;
using Printendar.Core.Text;

namespace Printendar.Core.Tests.Layout;

/// <summary>
/// The month grid, laid out against real paper with the real font.
/// </summary>
/// <remarks>
/// Measured with <see cref="SkiaTextMeasurer"/> rather than the fake, deliberately. The fake
/// proves the wrapping algorithm; only real glyph advances can support a claim about whether a
/// printed sheet fits, which is the only claim that matters here.
/// </remarks>
public class MonthGridStyleTests : IDisposable
{
    private readonly SkiaTextMeasurer _measurer = SkiaTextMeasurer.CreateWithEmbeddedFont();

    public void Dispose()
    {
        _measurer.Dispose();
        GC.SuppressFinalize(this);
    }

    private ScenePage Layout(
        int year = 2026,
        int month = 3,
        PaperSize? paper = null,
        Orientation orientation = Orientation.Landscape,
        DayOfWeek firstDayOfWeek = DayOfWeek.Sunday,
        WeekendMode weekendMode = WeekendMode.FullSevenDay,
        AdjacentDayMode adjacentDays = AdjacentDayMode.Muted)
    {
        var request = new LayoutRequest(
            Page: new PageSpec(paper ?? PaperSizes.Letter, orientation, Margins.FromInches(0.4f)),
            Grid: new MonthGridOptions(year, month, firstDayOfWeek, weekendMode, adjacentDays),
            Culture: CultureInfo.GetCultureInfo("en-US"),
            Measurer: _measurer);

        return new MonthGridStyle().Layout(request);
    }

    private static IEnumerable<SceneNode> Flatten(SceneNode node)
    {
        yield return node;

        if (node is SceneGroup group)
        {
            foreach (var child in group.Children)
            {
                foreach (var descendant in Flatten(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static string[] TextOf(ScenePage page) =>
        [.. Flatten(page.Root).OfType<SceneTextRun>().Select(r => r.Text)];

    [Fact]
    public void Produces_a_well_formed_page()
    {
        Assert.Empty(SceneValidator.Validate(Layout(), _measurer));
    }

    [Fact]
    public void Titles_the_page_with_the_month_and_year()
    {
        Assert.Contains("March 2026", TextOf(Layout()), StringComparer.Ordinal);
    }

    [Fact]
    public void Uses_the_culture_for_the_month_name()
    {
        var request = new LayoutRequest(
            Page: new PageSpec(PaperSizes.A4, Orientation.Landscape, Margins.FromInches(0.4f)),
            Grid: new MonthGridOptions(2026, 3, DayOfWeek.Monday),
            Culture: CultureInfo.GetCultureInfo("de-DE"),
            Measurer: _measurer);

        var page = new MonthGridStyle().Layout(request);

        Assert.Contains("März 2026", TextOf(page), StringComparer.Ordinal);
    }

    [Fact]
    public void Labels_every_weekday_column()
    {
        var texts = TextOf(Layout());

        foreach (var day in (string[])["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"])
        {
            Assert.Contains(day, texts, StringComparer.Ordinal);
        }
    }

    [Fact]
    public void Numbers_every_day_of_the_month()
    {
        var texts = TextOf(Layout());

        foreach (var day in Enumerable.Range(1, 31))
        {
            Assert.Contains(
                day.ToString(CultureInfo.InvariantCulture),
                texts,
                StringComparer.Ordinal);
        }
    }

    [Fact]
    public void Hides_adjacent_day_numbers_when_asked_to()
    {
        // March 2026 runs to Saturday 4 April, so a hidden-adjacent grid must not show 1 to 4
        // a second time. Counting occurrences catches the case where the April days are drawn
        // on top of the March ones.
        var muted = TextOf(Layout(adjacentDays: AdjacentDayMode.Muted));
        var hidden = TextOf(Layout(adjacentDays: AdjacentDayMode.Hidden));

        Assert.Equal(2, muted.Count(t => t == "1"));
        Assert.Equal(1, hidden.Count(t => t == "1"));
    }

    [Fact]
    public void Reports_the_week_row_count_in_the_diagnostics()
    {
        Assert.Equal(5, Layout(2026, 3).Diagnostics.WeekRowCount);
        Assert.Equal(4, Layout(2026, 2).Diagnostics.WeekRowCount);
        Assert.Equal(6, Layout(2026, 5).Diagnostics.WeekRowCount);
    }

    [Fact]
    public void Fits_on_one_page_for_every_month_paper_orientation_and_weekend_mode()
    {
        // The top-line invariant. If any combination of these produces a node off the sheet,
        // some user somewhere gets a two-page calendar, which is the whole problem.
        foreach (var month in Enumerable.Range(1, 12))
        {
            foreach (var paper in (PaperSize[])[PaperSizes.Letter, PaperSizes.A4, PaperSizes.Legal, PaperSizes.Tabloid])
            {
                foreach (var orientation in Enum.GetValues<Orientation>())
                {
                    foreach (var mode in Enum.GetValues<WeekendMode>())
                    {
                        var start = mode is WeekendMode.CompressedWeekendColumn
                            ? DayOfWeek.Monday
                            : DayOfWeek.Sunday;

                        var page = Layout(2026, month, paper, orientation, start, mode);

                        Assert.Empty(SceneValidator.Validate(page, _measurer));
                    }
                }
            }
        }
    }

    [Fact]
    public void Survives_margins_that_leave_very_little_room()
    {
        var request = new LayoutRequest(
            Page: new PageSpec(PaperSizes.Letter, Orientation.Landscape, Margins.FromInches(0.05f)),
            Grid: new MonthGridOptions(2026, 5),
            Culture: CultureInfo.InvariantCulture,
            Measurer: _measurer);

        var page = new MonthGridStyle().Layout(request);

        Assert.Empty(SceneValidator.Validate(page, _measurer));
    }

    [Fact]
    public void Lays_out_the_same_page_twice_for_the_same_request()
    {
        // Determinism is what makes snapshot baselines meaningful and what stops a preview
        // and an export of the same options disagreeing.
        var first = Layout();
        var second = Layout();

        Assert.Equal(TextOf(first), TextOf(second));
    }
}
