using System.Text;

namespace Printendar.Core.Text;

/// <summary>
/// The result of wrapping a string into a fixed width.
/// </summary>
/// <param name="Lines">The wrapped lines. Each one is guaranteed to fit the width asked for.</param>
/// <param name="MaxLineWidth">The widest line produced, in points.</param>
/// <param name="Height">Total height of the block, in points.</param>
/// <param name="Truncated">Whether text was dropped because the line cap was reached.</param>
public sealed record WrappedText(
    IReadOnlyList<string> Lines,
    float MaxLineWidth,
    float Height,
    bool Truncated)
{
    public static WrappedText Empty { get; } = new([], 0f, 0f, false);
}

/// <summary>
/// Greedy word wrap with a hard line cap and an ellipsis.
/// </summary>
/// <remarks>
/// Sits at the bottom of the fit search: a chip's height is decided by how many lines its
/// title wraps to, a cell's fit is decided by its chips' heights, and the page's scale is
/// decided by the cells. Everything above this is arithmetic on what this returns.
///
/// The postcondition that matters is that every returned line measures no wider than
/// <c>maxWidth</c>, including the one carrying the ellipsis. The scene validator re-checks it
/// with the real measurer, and the renderer clips to it as well, because a title that
/// overflows its cell is the most visible way this program can be wrong.
/// </remarks>
public static class TextLayout
{
    public const string Ellipsis = "…";

    public static WrappedText Wrap(
        ITextMeasurer measurer,
        in FontSpec font,
        string? text,
        float maxWidth,
        int maxLines,
        float lineSpacing)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLines, 1);

        var words = Tokenize(text);

        if (words.Count == 0 || maxWidth <= 0f)
        {
            return WrappedText.Empty;
        }

        var lines = new List<string>(maxLines);
        var current = new StringBuilder();
        var truncated = false;

        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            var candidate = current.Length == 0 ? word : $"{current} {word}";

            if (measurer.MeasureWidth(font, candidate) <= maxWidth)
            {
                current.Clear().Append(candidate);
                continue;
            }

            // The word does not fit alongside what is already on this line. Close the line
            // and reconsider the word on a fresh one.
            if (current.Length > 0)
            {
                if (!TryAddLine(lines, current.ToString(), maxLines))
                {
                    truncated = true;
                    break;
                }

                current.Clear();

                if (measurer.MeasureWidth(font, word) <= maxWidth)
                {
                    current.Append(word);
                    continue;
                }
            }

            // A single word wider than the whole line. No amount of breaking at spaces helps,
            // so break inside the word.
            var remainder = word;

            while (remainder.Length > 0)
            {
                var fits = measurer.CountCharsThatFit(font, remainder, maxWidth);

                if (fits <= 0)
                {
                    // Not even one character fits. Reachable at the smallest scale in a
                    // narrow compressed-weekend column, and it must terminate rather than
                    // spin.
                    truncated = true;
                    remainder = string.Empty;
                    break;
                }

                if (fits >= remainder.Length)
                {
                    current.Append(remainder);
                    remainder = string.Empty;
                    break;
                }

                if (!TryAddLine(lines, remainder[..fits], maxLines))
                {
                    truncated = true;
                    remainder = string.Empty;
                    break;
                }

                remainder = remainder[fits..];
            }

            if (truncated)
            {
                break;
            }
        }

        if (!truncated && current.Length > 0 && !TryAddLine(lines, current.ToString(), maxLines))
        {
            truncated = true;
        }

        if (truncated && lines.Count > 0)
        {
            lines[^1] = Ellipsize(measurer, font, lines[^1], maxWidth);
        }

        return Measure(measurer, font, lines, lineSpacing, truncated);
    }

    /// <summary>
    /// Splits on whitespace, which also collapses runs of it.
    /// </summary>
    /// <remarks>
    /// Subjects arrive from Graph, Google and ICS with stray tabs, newlines and double spaces.
    /// Whitespace that consumed real width would push a title onto an extra line and shrink
    /// the whole page for nothing.
    /// </remarks>
    private static List<string> Tokenize(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : [.. text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)];

    private static bool TryAddLine(List<string> lines, string line, int maxLines)
    {
        if (lines.Count >= maxLines)
        {
            return false;
        }

        lines.Add(line);
        return true;
    }

    /// <summary>
    /// Trims a line until it plus an ellipsis fits.
    /// </summary>
    private static string Ellipsize(ITextMeasurer measurer, in FontSpec font, string line, float maxWidth)
    {
        if (measurer.MeasureWidth(font, line + Ellipsis) <= maxWidth)
        {
            return line + Ellipsis;
        }

        for (var length = line.Length - 1; length > 0; length--)
        {
            var candidate = line[..length].TrimEnd() + Ellipsis;

            if (measurer.MeasureWidth(font, candidate) <= maxWidth)
            {
                return candidate;
            }
        }

        // Not even a lone ellipsis fits. Returning it anyway would overflow the cell, so the
        // line is dropped to nothing and the caller's overflow marker carries the meaning.
        return measurer.MeasureWidth(font, Ellipsis) <= maxWidth ? Ellipsis : string.Empty;
    }

    private static WrappedText Measure(
        ITextMeasurer measurer,
        in FontSpec font,
        List<string> lines,
        float lineSpacing,
        bool truncated)
    {
        lines.RemoveAll(string.IsNullOrEmpty);

        if (lines.Count == 0)
        {
            return WrappedText.Empty with { Truncated = truncated };
        }

        var lineHeight = measurer.GetMetrics(font).LineHeight;
        var widest = 0f;

        foreach (var line in lines)
        {
            widest = Math.Max(widest, measurer.MeasureWidth(font, line));
        }

        // Spacing goes between lines, never after the last one, or a single-line chip would
        // reserve height it does not use and the cell would fit fewer events than it can.
        var height = (lines.Count * lineHeight) + (Math.Max(0, lines.Count - 1) * lineSpacing);

        return new WrappedText(lines, widest, height, truncated);
    }
}
