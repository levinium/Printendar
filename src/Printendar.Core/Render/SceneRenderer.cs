using Printendar.Core.Scene;
using Printendar.Core.Text;
using SkiaSharp;

namespace Printendar.Core.Render;

/// <param name="Background">Painted across the whole sheet before anything else.</param>
/// <param name="DrawPageBorder">Draws the sheet edge. For the preview only; never for print.</param>
public sealed record RenderOptions(SKColor Background, bool DrawPageBorder = false)
{
    public static RenderOptions ForPrint { get; } = new(SKColors.White);

    public static RenderOptions ForPreview { get; } = new(SKColors.White, DrawPageBorder: true);
}

/// <summary>
/// Draws a laid-out page onto a canvas. The only code in Printendar that touches a pixel.
/// </summary>
/// <remarks>
/// A switch over node types with no measurement, no wrapping and no font fallback. Every
/// decision was made during layout, which is what allows the preview, the PDF exporter, the
/// raster renderer and the printer to be driven from this one method and produce the same
/// page.
///
/// The canvas must already be in page points. Callers apply their own scale first: the
/// preview divides by its zoom, the raster renderer multiplies by DPI over 72, and the PDF
/// exporter uses points directly.
/// </remarks>
public static class SceneRenderer
{
    public static void Draw(SKCanvas canvas, ScenePage page, RenderOptions options, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(measurer);

        canvas.Clear(options.Background);

        using var fonts = new FontPool(measurer);
        DrawNode(canvas, page.Root, fonts, measurer);

        if (options.DrawPageBorder)
        {
            using var border = new SKPaint { Color = SKColors.LightGray, Style = SKPaintStyle.Stroke, StrokeWidth = 0.5f };
            canvas.DrawRect(page.Page.PageRect, border);
        }
    }

    private static void DrawNode(SKCanvas canvas, SceneNode node, FontPool fonts, ITextMeasurer measurer)
    {
        switch (node)
        {
            case SceneGroup group:
                DrawGroup(canvas, group, fonts, measurer);
                break;

            case SceneRect rect:
                DrawRect(canvas, rect);
                break;

            case SceneLine line:
                DrawLine(canvas, line);
                break;

            case SceneTextRun run:
                DrawText(canvas, run, fonts, measurer);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(node), node, "Unknown scene node type.");
        }
    }

    private static void DrawGroup(SKCanvas canvas, SceneGroup group, FontPool fonts, ITextMeasurer measurer)
    {
        var saved = false;

        if (group.Clip is { } clip)
        {
            canvas.Save();
            canvas.ClipRect(clip);
            saved = true;
        }

        foreach (var child in group.Children)
        {
            DrawNode(canvas, child, fonts, measurer);
        }

        if (saved)
        {
            canvas.Restore();
        }
    }

    private static void DrawRect(SKCanvas canvas, SceneRect rect)
    {
        if (rect.Fill is { } fill)
        {
            using var paint = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRect(rect.Bounds, paint);
        }

        if (rect.Stroke is { } stroke)
        {
            using var paint = new SKPaint
            {
                Color = stroke,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = rect.StrokeWidth,
                IsAntialias = true,
            };
            canvas.DrawRect(rect.Bounds, paint);
        }
    }

    private static void DrawLine(SKCanvas canvas, SceneLine line)
    {
        using var paint = new SKPaint
        {
            Color = line.Color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = line.StrokeWidth,
            IsAntialias = true,
        };

        canvas.DrawLine(line.A, line.B, paint);
    }

    private static void DrawText(SKCanvas canvas, SceneTextRun run, FontPool fonts, ITextMeasurer measurer)
    {
        if (run.Text.Length == 0)
        {
            return;
        }

        var font = fonts.Get(run.Font);
        var width = measurer.MeasureWidth(run.Font, run.Text);

        var left = run.Anchor switch
        {
            TextAnchor.Left => run.Baseline.X,
            TextAnchor.Center => run.Baseline.X - (width / 2f),
            TextAnchor.Right => run.Baseline.X - width,
            _ => run.Baseline.X,
        };

        using var paint = new SKPaint { Color = run.Color, IsAntialias = true };

        // Clipping to the width layout declared is belt and braces. Layout should never emit a
        // run that does not fit and the validator would catch it, but a title written across
        // the neighbouring day is the most visible way this program can be wrong, so the
        // renderer refuses to let it happen even when handed a bad scene.
        var metrics = measurer.GetMetrics(run.Font);
        var clip = new SKRect(
            left,
            run.Baseline.Y + metrics.Ascent - 1f,
            left + run.MaxWidth,
            run.Baseline.Y + metrics.Descent + 1f);

        canvas.Save();
        canvas.ClipRect(clip);
        canvas.DrawText(run.Text, left, run.Baseline.Y, SKTextAlign.Left, font, paint);
        canvas.Restore();
    }

    /// <summary>
    /// Reuses one <see cref="SKFont"/> per <see cref="FontSpec"/> for the length of a draw.
    /// </summary>
    /// <remarks>
    /// A dense month emits several hundred runs across a handful of distinct sizes, so
    /// allocating a font per run would dominate the render.
    /// </remarks>
    private sealed class FontPool(ITextMeasurer measurer) : IDisposable
    {
        private readonly Dictionary<FontSpec, SKFont> _fonts = [];

        public SKFont Get(in FontSpec spec)
        {
            if (_fonts.TryGetValue(spec, out var cached))
            {
                return cached;
            }

            var font = measurer is SkiaTextMeasurer skia
                ? skia.CreateFont(spec)
                : throw new InvalidOperationException(
                    $"Rendering needs a {nameof(SkiaTextMeasurer)} so that drawing and measurement use " +
                    "identical fonts and settings. A fake measurer can drive layout, but not the renderer.");

            _fonts[spec] = font;
            return font;
        }

        public void Dispose()
        {
            foreach (var font in _fonts.Values)
            {
                font.Dispose();
            }

            _fonts.Clear();
        }
    }
}
