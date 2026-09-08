using Printendar.Core.Text;
using SkiaSharp;

namespace Printendar.Core.Scene;

/// <summary>What kind of thing is wrong with a scene.</summary>
public enum SceneViolationKind
{
    /// <summary>A node extends beyond the edge of the sheet.</summary>
    OutsidePage,

    /// <summary>A text run measures wider than the width layout said it would fit.</summary>
    TextExceedsMaxWidth,

    /// <summary>A stroke width of zero, which means one device pixel rather than a physical size.</summary>
    HairlineStroke,

    /// <summary>A coordinate that is NaN or infinite, which Skia draws as nothing.</summary>
    NotANumber,

    /// <summary>A text run carrying a line break, which layout should already have split.</summary>
    MultiLineTextRun,
}

/// <param name="Kind">What is wrong.</param>
/// <param name="Path">Group path to the offending node, for example "root/week-row-2/day-2026-03-17".</param>
/// <param name="Detail">Specifics, including the numbers involved.</param>
public sealed record SceneViolation(SceneViolationKind Kind, string Path, string Detail)
{
    public override string ToString() => $"{Kind} at {Path}: {Detail}";
}

/// <summary>
/// Checks that a laid-out page is well formed before anyone tries to draw it.
/// </summary>
/// <remarks>
/// This is where "fits on one page" is actually decided. Every layout engine calls it before
/// returning and every layout test calls it again, so the rule is stated once instead of being
/// re-derived by each print style.
///
/// It needs a measurer because the interesting check, whether a text run really fits the width
/// layout claimed, cannot be answered from the node alone. Pass the same measurer the layout
/// used, or the check is meaningless.
/// </remarks>
public static class SceneValidator
{
    /// <summary>
    /// Slack allowed on geometric comparisons, in points.
    /// </summary>
    /// <remarks>
    /// A hundredth of a point is far below anything a printer can resolve, and absorbs the
    /// last-bit float noise that accumulates through column and row division.
    /// </remarks>
    public const float Tolerance = 0.01f;

    public static IReadOnlyList<SceneViolation> Validate(ScenePage page, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(measurer);

        var violations = new List<SceneViolation>();
        Visit(page.Root, page.Page.PageRect, page.Root.DebugName, measurer, violations);
        return violations;
    }

    /// <summary>Validates and throws with every violation listed.</summary>
    /// <exception cref="InvalidOperationException">The scene is not well formed.</exception>
    public static void ThrowIfInvalid(ScenePage page, ITextMeasurer measurer)
    {
        var violations = Validate(page, measurer);

        if (violations.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The laid-out page is not well formed ({violations.Count} problem(s)):{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations.Select(v => "  " + v)));
    }

    private static void Visit(
        SceneNode node,
        SKRect pageRect,
        string path,
        ITextMeasurer measurer,
        List<SceneViolation> violations)
    {
        switch (node)
        {
            case SceneGroup group:
                foreach (var child in group.Children)
                {
                    var childPath = child is SceneGroup nested ? $"{path}/{nested.DebugName}" : path;
                    Visit(child, pageRect, childPath, measurer, violations);
                }

                break;

            case SceneRect rect:
                CheckFinite(rect.Bounds, path, violations);
                CheckWithinPage(rect.Bounds, pageRect, path, violations);
                CheckStroke(rect.Stroke is null ? null : rect.StrokeWidth, path, violations);
                break;

            case SceneLine line:
                CheckFinite(new SKRect(line.A.X, line.A.Y, line.B.X, line.B.Y), path, violations);
                CheckWithinPage(BoundsOf(line), pageRect, path, violations);
                CheckStroke(line.StrokeWidth, path, violations);
                break;

            case SceneTextRun run:
                CheckTextRun(run, pageRect, path, measurer, violations);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(node), node, "Unknown scene node type.");
        }
    }

    private static SKRect BoundsOf(SceneLine line) => new(
        Math.Min(line.A.X, line.B.X),
        Math.Min(line.A.Y, line.B.Y),
        Math.Max(line.A.X, line.B.X),
        Math.Max(line.A.Y, line.B.Y));

    private static void CheckTextRun(
        SceneTextRun run,
        SKRect pageRect,
        string path,
        ITextMeasurer measurer,
        List<SceneViolation> violations)
    {
        if (run.Text.Contains('\n') || run.Text.Contains('\r'))
        {
            violations.Add(new SceneViolation(
                SceneViolationKind.MultiLineTextRun,
                path,
                $"\"{run.Text}\" carries a line break. Layout wraps text into separate runs; the renderer does not."));
        }

        if (!float.IsFinite(run.Baseline.X) || !float.IsFinite(run.Baseline.Y))
        {
            violations.Add(new SceneViolation(
                SceneViolationKind.NotANumber,
                path,
                $"Baseline {run.Baseline} for \"{run.Text}\" is not a finite point."));
            return;
        }

        var width = measurer.MeasureWidth(run.Font, run.Text);

        if (width > run.MaxWidth + Tolerance)
        {
            violations.Add(new SceneViolation(
                SceneViolationKind.TextExceedsMaxWidth,
                path,
                $"\"{run.Text}\" measures {width:0.##}pt at {run.Font.SizePt:0.##}pt, " +
                $"over the {run.MaxWidth:0.##}pt layout said it would fit."));
        }

        var metrics = measurer.GetMetrics(run.Font);

        var left = run.Anchor switch
        {
            TextAnchor.Left => run.Baseline.X,
            TextAnchor.Center => run.Baseline.X - (width / 2f),
            TextAnchor.Right => run.Baseline.X - width,
            _ => run.Baseline.X,
        };

        var box = new SKRect(left, run.Baseline.Y + metrics.Ascent, left + width, run.Baseline.Y + metrics.Descent);
        CheckWithinPage(box, pageRect, path, violations);
    }

    private static void CheckFinite(SKRect rect, string path, List<SceneViolation> violations)
    {
        if (float.IsFinite(rect.Left) && float.IsFinite(rect.Top) &&
            float.IsFinite(rect.Right) && float.IsFinite(rect.Bottom))
        {
            return;
        }

        violations.Add(new SceneViolation(
            SceneViolationKind.NotANumber,
            path,
            $"{rect} contains a coordinate that is not a finite number. Skia draws such a node as nothing at all."));
    }

    private static void CheckWithinPage(SKRect bounds, SKRect pageRect, string path, List<SceneViolation> violations)
    {
        if (!float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top) ||
            !float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Bottom))
        {
            // Already reported as NotANumber; comparing would only add noise.
            return;
        }

        if (bounds.Left >= pageRect.Left - Tolerance &&
            bounds.Top >= pageRect.Top - Tolerance &&
            bounds.Right <= pageRect.Right + Tolerance &&
            bounds.Bottom <= pageRect.Bottom + Tolerance)
        {
            return;
        }

        violations.Add(new SceneViolation(
            SceneViolationKind.OutsidePage,
            path,
            $"{bounds} escapes the {pageRect.Width:0.##} by {pageRect.Height:0.##} point sheet. " +
            "Content outside the page is dropped by the printer or pushed onto a second sheet."));
    }

    private static void CheckStroke(float? strokeWidth, string path, List<SceneViolation> violations)
    {
        if (strokeWidth is null or > 0f)
        {
            return;
        }

        violations.Add(new SceneViolation(
            SceneViolationKind.HairlineStroke,
            path,
            $"A stroke width of {strokeWidth} means one device pixel, not a physical size, so the same " +
            "rule would be 1/96 inch on screen and 1/300 inch on paper."));
    }
}
