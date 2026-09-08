using SkiaSharp;

namespace Printendar.Core.Text;

/// <summary>
/// Measures text with Skia, using the same fonts and the same settings the renderer draws with.
/// </summary>
/// <remarks>
/// The font rendering flags here are not cosmetic and must stay identical to the ones
/// <c>SceneRenderer</c> applies.
///
/// <see cref="SKFontHinting.None"/> in particular is load-bearing: hinting snaps glyph
/// advances to whole device pixels, which makes the same string measure differently at 96 DPI
/// on screen and 300 DPI on a printer. Layout is computed once, in points, and drawn at both,
/// so hinted advances would mean the preview and the printed page disagree about where a
/// title wraps.
/// </remarks>
public sealed class SkiaTextMeasurer : ITextMeasurer, IDisposable
{
    private readonly IFontProvider _fonts;
    private readonly bool _ownsFonts;
    private readonly Dictionary<FontSpec, SKFont> _fontCache = [];
    private bool _disposed;

    /// <param name="fonts">Supplies typefaces. Not disposed by this instance.</param>
    public SkiaTextMeasurer(IFontProvider fonts)
        : this(fonts, ownsFonts: false)
    {
    }

    private SkiaTextMeasurer(IFontProvider fonts, bool ownsFonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        _fonts = fonts;
        _ownsFonts = ownsFonts;
    }

    /// <summary>A measurer over the embedded font, owning the provider it creates.</summary>
    public static SkiaTextMeasurer CreateWithEmbeddedFont() =>
        new(new EmbeddedFontProvider(), ownsFonts: true);

    /// <summary>
    /// Applies the font settings that measurement and rendering must share.
    /// </summary>
    /// <remarks>
    /// Public so <c>SceneRenderer</c> can apply exactly the same ones rather than keeping a
    /// second copy that could drift.
    /// </remarks>
    public static void ApplyRenderingSettings(SKFont font)
    {
        ArgumentNullException.ThrowIfNull(font);

        font.Edging = SKFontEdging.Antialias;
        font.Subpixel = true;
        font.Hinting = SKFontHinting.None;
    }

    /// <summary>
    /// Creates a font for drawing, configured exactly as the one used for measurement.
    /// </summary>
    /// <remarks>
    /// The renderer calls this rather than constructing its own, so there is no second place
    /// the rendering settings could drift from the ones layout was measured with. The caller
    /// owns the returned font.
    /// </remarks>
    public SKFont CreateFont(in FontSpec spec)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var font = new SKFont(_fonts.GetTypeface(spec.Weight), spec.SizePt);
        ApplyRenderingSettings(font);
        return font;
    }

    public FontMetrics GetMetrics(in FontSpec font)
    {
        var metrics = GetFont(font).Metrics;
        return new FontMetrics(metrics.Ascent, metrics.Descent, metrics.Leading);
    }

    public float MeasureWidth(in FontSpec font, ReadOnlySpan<char> text) =>
        text.IsEmpty ? 0f : GetFont(font).MeasureText(text);

    public int CountCharsThatFit(in FontSpec font, ReadOnlySpan<char> text, float maxWidth)
    {
        if (text.IsEmpty || maxWidth <= 0f)
        {
            return 0;
        }

        // BreakText reports bytes for UTF-8 encodings, but characters for UTF-16, which is
        // what a ReadOnlySpan<char> is. Asking for anything else would silently misplace the
        // break inside a surrogate pair.
        return GetFont(font).BreakText(text, maxWidth);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var font in _fontCache.Values)
        {
            font.Dispose();
        }

        _fontCache.Clear();

        if (_ownsFonts)
        {
            _fonts.Dispose();
        }

        _disposed = true;
    }

    private SKFont GetFont(in FontSpec spec)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_fontCache.TryGetValue(spec, out var cached))
        {
            return cached;
        }

        var font = new SKFont(_fonts.GetTypeface(spec.Weight), spec.SizePt);
        ApplyRenderingSettings(font);

        _fontCache[spec] = font;
        return font;
    }
}
