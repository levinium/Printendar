namespace Printendar.Core.Text;

/// <summary>
/// A font at a size, in PDF points.
/// </summary>
/// <remarks>
/// A value type with structural equality so it can key the measurement cache. The fit search
/// re-wraps the whole month once per rung of the scale ladder, so the same
/// (font, string) pair is measured many times per layout.
/// </remarks>
public readonly record struct FontSpec(FontWeightKind Weight, float SizePt);

/// <summary>
/// Vertical metrics for a font at a size, in PDF points.
/// </summary>
/// <param name="Ascent">Distance above the baseline. Negative, following Skia's convention.</param>
/// <param name="Descent">Distance below the baseline. Positive.</param>
/// <param name="Leading">Extra space the font asks for between lines.</param>
public readonly record struct FontMetrics(float Ascent, float Descent, float Leading)
{
    /// <summary>Baseline to baseline distance for consecutive lines.</summary>
    public float LineHeight => -Ascent + Descent + Leading;
}
