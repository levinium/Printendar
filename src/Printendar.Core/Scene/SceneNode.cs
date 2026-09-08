using Printendar.Core.Paper;
using Printendar.Core.Text;
using SkiaSharp;

namespace Printendar.Core.Scene;

/// <summary>How a text run sits relative to its anchor point.</summary>
public enum TextAnchor
{
    Left,
    Center,
    Right,
}

/// <summary>
/// One drawable thing, positioned in page points and fully resolved.
/// </summary>
/// <remarks>
/// The seam between layout and rendering. Everything a scene node carries has already been
/// decided: strings are single lines that already fit, colors are concrete, font sizes are
/// final. The renderer therefore makes no decisions and takes no measurements, which is what
/// lets the preview, the PDF and the printer all be driven from one code path and produce the
/// same page.
/// </remarks>
public abstract record SceneNode;

/// <param name="Bounds">The rectangle, in page points.</param>
/// <param name="Fill">Fill color, or null for no fill.</param>
/// <param name="Stroke">Stroke color, or null for no stroke.</param>
/// <param name="StrokeWidth">Stroke width in points. Must be greater than zero when stroking.</param>
public sealed record SceneRect(SKRect Bounds, SKColor? Fill, SKColor? Stroke, float StrokeWidth) : SceneNode;

/// <param name="A">Start point, in page points.</param>
/// <param name="B">End point, in page points.</param>
/// <param name="Color">Stroke color.</param>
/// <param name="StrokeWidth">Stroke width in points. Must be greater than zero.</param>
public sealed record SceneLine(SKPoint A, SKPoint B, SKColor Color, float StrokeWidth) : SceneNode;

/// <summary>
/// A single line of text on a baseline.
/// </summary>
/// <remarks>
/// <paramref name="Text"/> contains no line breaks. Wrapping happened during layout, so a
/// paragraph arrives here as several runs. <paramref name="MaxWidth"/> is the width layout
/// promised the run would fit; the validator checks that promise and the renderer clips to it,
/// so a measurement mistake shows up as a clipped word rather than a title written across the
/// neighbouring day.
/// </remarks>
public sealed record SceneTextRun(
    string Text,
    SKPoint Baseline,
    FontSpec Font,
    SKColor Color,
    TextAnchor Anchor,
    float MaxWidth) : SceneNode;

/// <param name="DebugName">Names this group in validator messages and in canonical snapshots.</param>
/// <param name="Children">Contents, drawn in order.</param>
/// <param name="Clip">Optional clip applied to the children.</param>
public sealed record SceneGroup(
    string DebugName,
    IReadOnlyList<SceneNode> Children,
    SKRect? Clip) : SceneNode;

/// <summary>
/// What the layout had to do to make the content fit, for the UI to report.
/// </summary>
/// <remarks>
/// Carried on the page rather than logged, because the honest answer to "why is this text so
/// small" is something the user needs to see next to the preview, along with what they could
/// change about it.
/// </remarks>
/// <param name="EffectiveScale">The typography scale the fit search settled on. 1 means nothing was shrunk.</param>
/// <param name="SmallestFontPt">The smallest font size actually emitted.</param>
/// <param name="HiddenEventCount">Events that did not fit and are represented by an overflow marker.</param>
/// <param name="DroppedWeekendEventCount">Events dropped because the weekend has no column.</param>
/// <param name="WeekRowCount">Week rows in the grid.</param>
/// <param name="Warnings">Messages worth showing the user.</param>
public sealed record LayoutDiagnostics(
    float EffectiveScale,
    float SmallestFontPt,
    int HiddenEventCount,
    int DroppedWeekendEventCount,
    int WeekRowCount,
    IReadOnlyList<string> Warnings)
{
    public static LayoutDiagnostics Empty { get; } = new(1f, 0f, 0, 0, 0, []);
}

/// <summary>
/// A complete page: the paper it is for, everything on it, and what fitting it cost.
/// </summary>
/// <remarks>
/// One of these is produced per layout and is the single source for the preview, the PDF and
/// the printer. The view model holds the instance the preview drew and hands that same
/// instance to the exporter, so preview and output cannot disagree.
/// </remarks>
public sealed record ScenePage(PageSpec Page, SceneGroup Root, LayoutDiagnostics Diagnostics);
