namespace Printendar.Core.Paper;

/// <summary>
/// A named sheet of paper, described by its edges in PDF points (1/72 inch).
/// </summary>
/// <remarks>
/// Stored as short and long edge rather than width and height, so orientation is applied in
/// one place (<see cref="PageSpec"/>) instead of every caller remembering to swap.
/// </remarks>
/// <param name="Id">Stable identifier used in settings and on the command line.</param>
/// <param name="DisplayName">What the user sees in the paper picker.</param>
/// <param name="ShortEdgePt">The shorter edge, in points.</param>
/// <param name="LongEdgePt">The longer edge, in points.</param>
public sealed record PaperSize(string Id, string DisplayName, float ShortEdgePt, float LongEdgePt);

/// <summary>The sizes Printendar offers.</summary>
public static class PaperSizes
{
    public static readonly PaperSize Letter = new("letter", "Letter (8.5 x 11 in)", 612f, 792f);
    public static readonly PaperSize Legal = new("legal", "Legal (8.5 x 14 in)", 612f, 1008f);
    public static readonly PaperSize Tabloid = new("tabloid", "Tabloid (11 x 17 in)", 792f, 1224f);
    public static readonly PaperSize A4 = new("a4", "A4 (210 x 297 mm)", 595.28f, 841.89f);
    public static readonly PaperSize A3 = new("a3", "A3 (297 x 420 mm)", 841.89f, 1190.55f);

    /// <summary>Every size, in the order the picker should show them.</summary>
    public static IReadOnlyList<PaperSize> All { get; } = [Letter, Legal, Tabloid, A4, A3];

    /// <summary>Looks a size up by <see cref="PaperSize.Id"/>, case-insensitively.</summary>
    public static bool TryGetById(string id, out PaperSize paper)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                paper = candidate;
                return true;
            }
        }

        paper = Letter;
        return false;
    }
}
