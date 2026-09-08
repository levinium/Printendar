using System.Globalization;
using Printendar.Core.Model;
using Printendar.Core.Text;
using SkiaSharp;

namespace Printendar.Core.Layout;

/// <summary>How start times are written.</summary>
public enum TimeFormat
{
    /// <summary>Compact 12 hour: 9a, 9:30a, 2p.</summary>
    Clock12Hour,

    /// <summary>24 hour: 09:00, 14:30.</summary>
    Clock24Hour,
}

/// <summary>
/// Typography and spacing for one event chip, at the scale the fit search has settled on.
/// </summary>
/// <remarks>
/// Every measurement here is multiplied by the same scale, deliberately. The fit search binary
/// searches a descending scale ladder and that is only valid while shrinking the scale cannot
/// increase the wrapped line count. A padding or gap that stayed fixed while fonts shrank
/// would break that, and the search would silently return a scale that does not fit.
/// </remarks>
public readonly record struct ChipStyle(
    FontSpec TitleFont,
    FontSpec TimeFont,
    FontSpec MarkerFont,
    float SwatchWidth,
    float InnerPadding,
    float ChipGap,
    float LineSpacing,
    int MaxLines,
    bool ShowStartTime,
    bool ShowOverflowMarker,
    TimeFormat TimeFormat)
{
    /// <summary>Returns this style with every dimension multiplied by <paramref name="scale"/>.</summary>
    public ChipStyle Scaled(float scale) => this with
    {
        TitleFont = TitleFont with { SizePt = TitleFont.SizePt * scale },
        TimeFont = TimeFont with { SizePt = TimeFont.SizePt * scale },
        MarkerFont = MarkerFont with { SizePt = MarkerFont.SizePt * scale },
        SwatchWidth = SwatchWidth * scale,
        InnerPadding = InnerPadding * scale,
        ChipGap = ChipGap * scale,
        LineSpacing = LineSpacing * scale,
    };
}

/// <param name="Event">The event this chip represents.</param>
/// <param name="Bounds">Where the chip sits, in page points.</param>
/// <param name="Text">The wrapped title, including any time prefix.</param>
/// <param name="TextWidth">Width the text was wrapped to.</param>
/// <param name="Color">The colour of the calendar this event came from.</param>
public sealed record ChipLayout(
    CalendarEvent Event,
    SKRect Bounds,
    WrappedText Text,
    float TextWidth,
    SKColor Color);

/// <summary>
/// Turns one event into a measured chip.
/// </summary>
/// <remarks>
/// Shared by the month grid, and later by the week and agenda styles, so that an event looks
/// the same wherever it is printed and there is one place where a title's height is decided.
/// </remarks>
public static class EventChipComposer
{
    public static float MeasureHeight(
        CalendarEvent calendarEvent,
        float maxWidth,
        in ChipStyle style,
        ITextMeasurer measurer,
        CultureInfo culture) =>
        Wrap(calendarEvent, TextWidthFor(maxWidth, style), style, measurer, culture).Height +
        (2f * style.InnerPadding);

    public static ChipLayout Compose(
        CalendarEvent calendarEvent,
        SKRect bounds,
        in ChipStyle style,
        SKColor color,
        ITextMeasurer measurer,
        CultureInfo culture)
    {
        var textWidth = TextWidthFor(bounds.Width, style);
        var text = Wrap(calendarEvent, textWidth, style, measurer, culture);

        return new ChipLayout(calendarEvent, bounds, text, textWidth, color);
    }

    /// <summary>Width left for text once the colour swatch and padding are taken off.</summary>
    public static float TextWidthFor(float chipWidth, in ChipStyle style) =>
        Math.Max(chipWidth - style.SwatchWidth - (3f * style.InnerPadding), 0f);

    private static WrappedText Wrap(
        CalendarEvent calendarEvent,
        float textWidth,
        in ChipStyle style,
        ITextMeasurer measurer,
        CultureInfo culture)
    {
        // The time is prefixed into the string rather than laid out as its own run, so that
        // wrapping accounts for it. Positioning it separately is how you end up with a time
        // that fits beside a title that does not, and a chip measured a line short.
        var label = BuildLabel(calendarEvent, style, culture);

        return TextLayout.Wrap(measurer, style.TitleFont, label, textWidth, style.MaxLines, style.LineSpacing);
    }

    private static string BuildLabel(CalendarEvent calendarEvent, in ChipStyle style, CultureInfo culture)
    {
        // An all-day event has no clock time, so printing one would be inventing information.
        if (!style.ShowStartTime || calendarEvent.IsAllDay || calendarEvent.Start is not { } start)
        {
            return calendarEvent.Subject;
        }

        return $"{FormatTime(start, style.TimeFormat, culture)} {calendarEvent.Subject}";
    }

    /// <summary>
    /// Formats a start time as compactly as it can be read.
    /// </summary>
    /// <remarks>
    /// A day cell on a landscape Letter sheet is about 100 points wide, so "9a" instead of
    /// "9:00 AM" is the difference between a title fitting on one line and taking two, across
    /// every timed event on the page.
    /// </remarks>
    private static string FormatTime(DateTimeOffset start, TimeFormat format, CultureInfo culture)
    {
        if (format is TimeFormat.Clock24Hour)
        {
            return start.ToString("HH:mm", culture);
        }

        var hour = start.Hour % 12 == 0 ? 12 : start.Hour % 12;
        var suffix = start.Hour < 12 ? "a" : "p";

        return start.Minute == 0
            ? $"{hour}{suffix}"
            : $"{hour}:{start.Minute:00}{suffix}";
    }
}
