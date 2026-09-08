using System.Globalization;
using Printendar.Core.Layout.Fit;
using Printendar.Core.Layout.Month;
using Printendar.Core.Model;
using Printendar.Core.Paper;
using Printendar.Core.Scene;
using Printendar.Core.Text;
using SkiaSharp;

namespace Printendar.Core.Layout;

/// <summary>
/// One calendar's entry in the printed legend.
/// </summary>
/// <remarks>
/// Colours are assigned by Printendar rather than taken from the provider. Graph exposes
/// category names but not the colour values Outlook actually shows, and Google's palette is
/// different again, so reading them would give an inconsistent set that still had to be
/// remapped. A fixed palette the user can override is both simpler and predictable.
/// </remarks>
public sealed record CalendarLegendEntry(string CalendarId, string DisplayName, SKColor Color);

/// <summary>Presentation choices that are not about fitting.</summary>
/// <param name="ShowStartTime">Prefix timed events with a compact start time.</param>
/// <param name="TimeFormat">12 or 24 hour clock.</param>
/// <param name="ShowLegend">Print a legend when more than one calendar is in use.</param>
public sealed record MonthStyleOptions(
    bool ShowStartTime = true,
    TimeFormat TimeFormat = TimeFormat.Clock12Hour,
    bool ShowLegend = true)
{
    public static MonthStyleOptions Default { get; } = new();
}

/// <summary>
/// Everything a print style needs to lay out a page.
/// </summary>
/// <remarks>
/// <see cref="Culture"/> is explicit rather than taken from
/// <see cref="CultureInfo.CurrentCulture"/>. Month names, day abbreviations and the 12 or 24
/// hour clock all come from it, so an ambient value would make layout depend on the machine
/// and make tests non-deterministic.
/// </remarks>
public sealed record LayoutRequest(
    PageSpec Page,
    MonthGridOptions Grid,
    CultureInfo Culture,
    ITextMeasurer Measurer)
{
    /// <summary>Occurrences to place. Recurrence is already expanded.</summary>
    public IReadOnlyList<CalendarEvent> Events { get; init; } = [];

    /// <summary>The calendars the events came from, with their colours.</summary>
    public IReadOnlyList<CalendarLegendEntry> Calendars { get; init; } = [];

    public FitOptions Fit { get; init; } = FitOptions.Default;

    public MonthStyleOptions Style { get; init; } = MonthStyleOptions.Default;
}

/// <summary>
/// One way of putting a calendar on a page.
/// </summary>
/// <remarks>
/// The month grid is the first. A blank grid, a week, an agenda and a tri-fold follow, and
/// each produces the same <see cref="ScenePage"/>, so validation, rendering, export, preview
/// and printing are written once rather than per style.
/// </remarks>
public interface IPrintStyle
{
    string Id { get; }

    ScenePage Layout(LayoutRequest request);
}
