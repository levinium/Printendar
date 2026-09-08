namespace Printendar.Core.Paper;

/// <summary>
/// Page margins in PDF points (1/72 inch).
/// </summary>
/// <param name="Left">Inset from the left edge.</param>
/// <param name="Top">Inset from the top edge.</param>
/// <param name="Right">Inset from the right edge.</param>
/// <param name="Bottom">Inset from the bottom edge.</param>
public readonly record struct Margins(float Left, float Top, float Right, float Bottom)
{
    /// <summary>Points per inch, which is the definition of the unit rather than a setting.</summary>
    public const float PointsPerInch = 72f;

    public static Margins Zero => new(0f, 0f, 0f, 0f);

    /// <summary>The same inset on all four edges, given in inches.</summary>
    public static Margins FromInches(float inches) =>
        new(inches * PointsPerInch, inches * PointsPerInch, inches * PointsPerInch, inches * PointsPerInch);

    public float Horizontal => Left + Right;

    public float Vertical => Top + Bottom;
}
