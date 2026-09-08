using Printendar.Core.Text;

namespace Printendar.Core.Tests.Text;

/// <summary>
/// A measurer whose arithmetic is obvious, so wrapping tests can assert exact line breaks.
/// </summary>
/// <remarks>
/// Every character is half the font size wide and every line is 1.2 times the font size tall.
/// That makes a 10 point font exactly 5 points per character, so a test can say "40 points
/// holds 8 characters" and mean it.
///
/// Wrapping logic is what this double exists to test. Anything asserting on real glyph
/// advances, and therefore on whether a real page fits, must use the Skia measurer instead;
/// a fake cannot tell you that the printed page is correct.
/// </remarks>
internal sealed class FixedAdvanceTextMeasurer : ITextMeasurer
{
    public const float AdvanceRatio = 0.5f;
    public const float LineHeightRatio = 1.2f;

    public FontMetrics GetMetrics(in FontSpec font) => new(
        Ascent: -font.SizePt * 0.8f,
        Descent: font.SizePt * 0.2f,
        Leading: font.SizePt * (LineHeightRatio - 1.0f));

    public float MeasureWidth(in FontSpec font, ReadOnlySpan<char> text) =>
        text.Length * font.SizePt * AdvanceRatio;

    public int CountCharsThatFit(in FontSpec font, ReadOnlySpan<char> text, float maxWidth)
    {
        var perChar = font.SizePt * AdvanceRatio;

        if (perChar <= 0f)
        {
            return 0;
        }

        var fits = (int)Math.Floor(maxWidth / perChar);
        return Math.Clamp(fits, 0, text.Length);
    }
}
