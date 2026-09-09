using SkiaSharp;

namespace Printendar.Core.Sources;

/// <summary>A colour already spoken for, and what is using it.</summary>
public sealed record NamedColor(string Name, SKColor Color);

/// <summary>
/// What is wrong with a colour somebody picked, in a sentence, or nothing when it is fine.
/// </summary>
/// <remarks>
/// The eight offered colours are chosen to stay apart for the common forms of colour blindness
/// and to fall into different greys in a black and white print. Anything typed or dragged for
/// by hand gives that up, and the two ways it goes wrong are both invisible until the paper
/// comes out: a colour too pale to read on white, and one close enough to another calendar's
/// that the legend no longer tells them apart.
///
/// Warns rather than refuses, and says which other calendar rather than only that there is one,
/// because the picker does not show the rest of the list and being told to go and look is not
/// help.
/// </remarks>
public static class ColorAdvice
{
    /// <summary>How pale is too pale to read as a filled chip on white paper.</summary>
    private const double PaleAbove = 0.82;

    /// <summary>
    /// How near two colours may be before they stop being told apart.
    /// </summary>
    /// <remarks>
    /// Summed channel distance rather than lightness alone. Lightness would call a mid green
    /// and a mid red the same thing, and a warning that fires on colours anybody can plainly
    /// distinguish is one people learn to click past.
    /// </remarks>
    private const int TooNear = 60;

    public static string? Describe(SKColor candidate, IReadOnlyList<NamedColor> others)
    {
        ArgumentNullException.ThrowIfNull(others);

        // Said first when it is both. One of these means the events cannot be seen at all.
        if (Luma(candidate) > PaleAbove)
        {
            return "That is very pale and will be hard to see on white paper.";
        }

        var clash = others.FirstOrDefault(other => Distance(other.Color, candidate) < TooNear);

        return clash is null
            ? null
            : $"That is very close to the colour {clash.Name} prints in.";
    }

    /// <summary>Rec. 601 luma, which tracks how light a colour looks rather than its channels.</summary>
    private static double Luma(SKColor color) =>
        ((0.299 * color.Red) + (0.587 * color.Green) + (0.114 * color.Blue)) / 255.0;

    private static int Distance(SKColor a, SKColor b) =>
        Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue);
}
