using System.Globalization;
using Printendar.Core.Layout;
using Printendar.Core.Layout.Fit;
using Printendar.Core.Layout.Month;
using Printendar.Core.Model;
using Printendar.Core.Paper;
using Printendar.Core.Scene;
using Printendar.Core.Text;
using SkiaSharp;

namespace Printendar.Core.Tests.Layout;

/// <summary>
/// The month grid carrying real events: the thing this project exists to print.
/// </summary>
/// <remarks>
/// Measured with the real Skia measurer throughout. A fake can prove the algorithm; only real
/// glyph advances can support the claim that a printed sheet holds what it says it holds.
/// </remarks>
public class MonthGridWithEventsTests : IDisposable
{
    private readonly SkiaTextMeasurer _measurer = SkiaTextMeasurer.CreateWithEmbeddedFont();

    public void Dispose()
    {
        _measurer.Dispose();
        GC.SuppressFinalize(this);
    }

    private static readonly CalendarLegendEntry Work = new("work", "Work", new SKColor(0x1F, 0x77, 0xB4));
    private static readonly CalendarLegendEntry Personal = new("personal", "Personal", new SKColor(0xD6, 0x27, 0x28));

    private static CalendarEvent Timed(int day, int hour, string subject, string calendarId = "work")
    {
        var start = new DateTimeOffset(2026, 3, day, hour, 0, 0, TimeSpan.Zero);

        return new CalendarEvent
        {
            Id = $"{calendarId}-{day}-{hour}-{subject}",
            CalendarId = calendarId,
            Subject = subject,
            IsAllDay = false,
            Days = DateSpan.SingleDay(new DateOnly(2026, 3, day)),
            Start = start,
            End = start.AddHours(1),
        };
    }

    private ScenePage Layout(
        IReadOnlyList<CalendarEvent> events,
        FitOptions? fit = null,
        PaperSize? paper = null,
        float marginInches = 0.4f,
        IReadOnlyList<CalendarLegendEntry>? calendars = null)
    {
        var request = new LayoutRequest(
            Page: new PageSpec(paper ?? PaperSizes.Letter, Orientation.Landscape, Margins.FromInches(marginInches)),
            Grid: new MonthGridOptions(2026, 3),
            Culture: CultureInfo.GetCultureInfo("en-US"),
            Measurer: _measurer)
        {
            Events = events,
            Calendars = calendars ?? [Work, Personal],
            Fit = fit ?? FitOptions.Default,
        };

        return new MonthGridStyle().Layout(request);
    }

    private static IEnumerable<SceneNode> Flatten(SceneNode node)
    {
        yield return node;

        if (node is SceneGroup group)
        {
            foreach (var descendant in group.Children.SelectMany(Flatten))
            {
                yield return descendant;
            }
        }
    }

    private static string[] TextOf(ScenePage page) =>
        [.. Flatten(page.Root).OfType<SceneTextRun>().Select(r => r.Text)];

    [Fact]
    public void Draws_an_event_on_its_day()
    {
        var page = Layout([Timed(17, 9, "Board meeting")]);

        Assert.Contains(TextOf(page), t => t.Contains("Board meeting", StringComparison.Ordinal));
    }

    [Fact]
    public void A_typical_month_fits_on_one_page_without_shrinking()
    {
        // Two or three events on most weekdays, which is what an ordinary working calendar
        // looks like. If this needed shrinking, the base type sizes would be wrong.
        var events = new List<CalendarEvent>();

        foreach (var day in Enumerable.Range(1, 31))
        {
            var date = new DateOnly(2026, 3, day);

            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            events.Add(Timed(day, 9, "Standup"));
            events.Add(Timed(day, 14, "1:1", "personal"));
        }

        var page = Layout(events);

        Assert.Empty(SceneValidator.Validate(page, _measurer));
        Assert.Equal(0, page.Diagnostics.HiddenEventCount);
        Assert.Equal(1f, page.Diagnostics.EffectiveScale, 0.001f);
    }

    [Fact]
    public void A_busy_month_still_fits_on_one_page()
    {
        var events = new List<CalendarEvent>();

        foreach (var day in Enumerable.Range(1, 31))
        {
            for (var hour = 8; hour < 18; hour++)
            {
                events.Add(Timed(day, hour, $"Meeting about the thing at {hour}"));
            }
        }

        var page = Layout(events);

        Assert.Empty(SceneValidator.Validate(page, _measurer));
    }

    [Fact]
    public void A_pathological_month_still_fits_on_one_page()
    {
        // Nothing here is realistic. It is here because the guarantee has to be unconditional:
        // 500 events, unbreakable 400 character subjects, and margins that leave almost
        // nothing to print into.
        var subject = new string('W', 400);

        var events = Enumerable.Range(0, 500)
            .Select(i => Timed((i % 31) + 1, 8 + (i % 12), subject))
            .ToList();

        var page = Layout(events, marginInches: 0.15f);

        Assert.Empty(SceneValidator.Validate(page, _measurer));
        Assert.True(page.Diagnostics.HiddenEventCount > 0, "With 500 events on one month, some must be reported hidden.");
    }

    [Fact]
    public void Reports_hidden_events_rather_than_dropping_them_silently()
    {
        var events = Enumerable.Range(0, 40).Select(i => Timed(17, 8 + (i % 12), $"Event {i}")).ToList();

        var page = Layout(events);

        Assert.True(page.Diagnostics.HiddenEventCount > 0);
        Assert.Contains(TextOf(page), t => t.Contains("more", StringComparison.Ordinal));
    }

    [Fact]
    public void Shrinks_before_it_hides()
    {
        // A day with more than fits at full size should first get smaller type. Hiding events
        // is the fallback, not the first move.
        var events = Enumerable.Range(0, 8).Select(i => Timed(17, 8 + i, $"Event {i}")).ToList();

        var page = Layout(events);

        Assert.True(page.Diagnostics.EffectiveScale < 1f, "Expected the fit search to shrink the type first.");
    }

    [Fact]
    public void Stops_shrinking_at_the_readable_floor_and_hides_the_rest()
    {
        // Readability beats completeness. A calendar nobody can read is not a calendar.
        var events = Enumerable.Range(0, 60).Select(i => Timed(17, 8 + (i % 12), $"Event {i}")).ToList();

        var page = Layout(events, FitOptions.Default);

        Assert.True(
            page.Diagnostics.EffectiveScale >= FitOptions.Default.MinReadableScale - 0.001f,
            $"Scale {page.Diagnostics.EffectiveScale} fell below the readable floor " +
            $"{FitOptions.Default.MinReadableScale}.");
        Assert.True(page.Diagnostics.HiddenEventCount > 0);
    }

    [Fact]
    public void Cap_per_day_policy_never_draws_more_than_the_cap()
    {
        var events = Enumerable.Range(0, 20).Select(i => Timed(17, 8 + (i % 12), $"Event {i}")).ToList();
        var fit = FitOptions.Default with { Policy = FitPolicy.CapPerDay, MaxEventsPerDay = 3 };

        var page = Layout(events, fit);

        Assert.Empty(SceneValidator.Validate(page, _measurer));
        Assert.True(page.Diagnostics.HiddenEventCount >= 17);
    }

    [Fact]
    public void Draws_a_legend_for_the_calendars_in_use()
    {
        var page = Layout([Timed(17, 9, "Board meeting"), Timed(18, 9, "Dentist", "personal")]);

        var texts = TextOf(page);

        Assert.Contains("Work", texts, StringComparer.Ordinal);
        Assert.Contains("Personal", texts, StringComparer.Ordinal);
    }

    [Fact]
    public void Colours_each_event_by_its_calendar()
    {
        var page = Layout([Timed(17, 9, "Board meeting"), Timed(18, 9, "Dentist", "personal")]);

        var fills = Flatten(page.Root)
            .OfType<SceneRect>()
            .Select(r => r.Fill)
            .Where(c => c == Work.Color || c == Personal.Color)
            .Distinct()
            .ToArray();

        Assert.Equal(2, fills.Length);
    }

    [Fact]
    public void Fitting_is_monotone_in_scale()
    {
        // The property the binary search rests on. Walking every rung on a dense month catches
        // anyone who later expresses a padding in fixed points while the fonts scale, which
        // would silently corrupt the search.
        var events = Enumerable.Range(0, 10).Select(i => Timed(17, 8 + i, $"Meeting number {i}")).ToList();

        var fitted = new List<bool>();

        foreach (var rung in FontScaleLadder.Default.Rungs)
        {
            var fit = FitOptions.Default with
            {
                Policy = FitPolicy.AutoShrink,
                MinReadableScale = rung,
                MinScale = rung,
            };

            fitted.Add(Layout(events, fit).Diagnostics.HiddenEventCount == 0);
        }

        for (var i = 1; i < fitted.Count; i++)
        {
            if (fitted[i - 1])
            {
                Assert.True(
                    fitted[i],
                    $"Content fitted at scale {FontScaleLadder.Default.Rungs[i - 1]} but not at the smaller " +
                    $"{FontScaleLadder.Default.Rungs[i]}. Some dimension is not scaling with the font, which " +
                    "breaks the binary search's monotonicity assumption.");
            }
        }
    }

    [Fact]
    public void The_readable_floor_never_produces_type_the_app_itself_calls_unreadable()
    {
        // These two constants have to agree. If the Hybrid policy stops shrinking at a floor
        // that still lands under the small-type warning threshold, the layout hides events to
        // protect readability and then reports that the result is unreadable anyway, which is
        // the worst of both. Whichever is tuned later, this keeps them consistent.
        var events = Enumerable.Range(0, 60).Select(i => Timed(17, 8 + (i % 12), $"Event {i}")).ToList();

        var page = Layout(events);

        Assert.True(
            page.Diagnostics.SmallestFontPt >= FitOptions.SmallTypeWarningPt,
            $"The Hybrid floor settled on {page.Diagnostics.SmallestFontPt:0.#} point, below the " +
            $"{FitOptions.SmallTypeWarningPt} point the layout warns about. Raise MinReadableScale.");
    }

    [Fact]
    public void One_overloaded_day_does_not_shrink_the_whole_month_past_readable()
    {
        // A single day with a dozen events must not penalize the other thirty. The scale is
        // global by design, so the defence is that the floor holds and the outlier day
        // overflows into "+N more" instead.
        var events = new List<CalendarEvent>();

        foreach (var day in Enumerable.Range(1, 31))
        {
            events.Add(Timed(day, 9, "Standup"));
        }

        for (var hour = 8; hour <= 18; hour++)
        {
            events.Add(Timed(17, hour, "Meeting about the thing"));
        }

        var page = Layout(events);

        Assert.True(
            page.Diagnostics.SmallestFontPt >= FitOptions.SmallTypeWarningPt,
            $"One busy day dragged the whole page down to {page.Diagnostics.SmallestFontPt:0.#} point.");
    }

    [Fact]
    public void Warns_when_the_type_has_become_very_small()
    {
        var events = Enumerable.Range(0, 60).Select(i => Timed(17, 8 + (i % 12), $"Event {i}")).ToList();

        var page = Layout(events);

        Assert.NotEmpty(page.Diagnostics.Warnings);
    }
}
