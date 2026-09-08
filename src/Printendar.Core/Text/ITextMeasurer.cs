namespace Printendar.Core.Text;

/// <summary>
/// Measures text so layout can be computed without drawing anything.
/// </summary>
/// <remarks>
/// This interface is the seam that makes the whole layout engine unit-testable. Layout is
/// arithmetic over measured text, so with a measurer whose advances are known the expected
/// line breaks and cell heights become exact numbers a test can assert, with no bitmap and no
/// golden image involved.
///
/// The counterpart obligation: anything claiming a real page fits must be measured with the
/// Skia implementation. A fake measurer can prove the wrapping algorithm is right, never that
/// the printed sheet is.
/// </remarks>
public interface ITextMeasurer
{
    FontMetrics GetMetrics(in FontSpec font);

    float MeasureWidth(in FontSpec font, ReadOnlySpan<char> text);

    /// <summary>
    /// How many leading characters of <paramref name="text"/> fit within
    /// <paramref name="maxWidth"/>.
    /// </summary>
    /// <remarks>
    /// Used to hard-break a word longer than the line it must go on, which no amount of
    /// breaking at spaces can solve.
    /// </remarks>
    int CountCharsThatFit(in FontSpec font, ReadOnlySpan<char> text, float maxWidth);
}
