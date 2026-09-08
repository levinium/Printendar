namespace Printendar.Core.Layout.Fit;

/// <summary>What to do when a day holds more than will fit.</summary>
public enum FitPolicy
{
    /// <summary>Shrink the type until everything fits, down to a hard floor.</summary>
    AutoShrink,

    /// <summary>Show at most N events per day at full size, then shrink if even that overflows.</summary>
    CapPerDay,

    /// <summary>Shrink to a readable floor, then let overflow markers absorb the rest.</summary>
    Hybrid,
}

/// <summary>
/// How hard to work at making everything fit, and when to stop.
/// </summary>
/// <remarks>
/// The defaults encode one judgment: readability beats completeness. A calendar shrunk until
/// every event fits but nobody can read it has failed at the only thing it was for. So the
/// search stops at <see cref="MinReadableScale"/> and the remaining events are reported by an
/// overflow marker and in the diagnostics, where the user can see them and choose bigger
/// paper, fewer calendars, or a cap.
///
/// Note that capping alone does not guarantee a fit: four full-size chips still will not go
/// into the cells of a six row month on A4. <see cref="FitPolicy.CapPerDay"/> therefore still
/// runs the shrink search over the capped set.
/// </remarks>
public sealed record FitOptions
{
    public FitPolicy Policy { get; init; } = FitPolicy.Hybrid;

    /// <summary>
    /// The scale below which the search stops shrinking and starts hiding.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="SmallTypeWarningPt"/> and <see cref="BaseEventSizePt"/> rather
    /// than written as a number, so the two cannot drift apart. They did: a hand-picked 0.72
    /// put event text at 5.4 point, so the layout would hide events to protect readability and
    /// then warn that the result was unreadable anyway, which is the worst of both.
    ///
    /// Below this the type stops being readable at arm's length on paper, so the remaining
    /// events become overflow markers instead. Readability beats completeness: a calendar
    /// nobody can read has failed at the only thing it was for.
    /// </remarks>
    public float MinReadableScale { get; init; } = SmallTypeWarningPt / BaseEventSizePt;

    /// <summary>
    /// The hard floor. Never goes below this, whatever the policy.
    /// </summary>
    public float MinScale { get; init; } = 0.58f;

    /// <summary>Events per day under <see cref="FitPolicy.CapPerDay"/>.</summary>
    public int MaxEventsPerDay { get; init; } = 4;

    /// <summary>Lines one event title may wrap to before it is ellipsized.</summary>
    public int MaxLinesPerEvent { get; init; } = 2;

    /// <summary>Whether to draw "+3 more" when events are hidden.</summary>
    public bool ShowOverflowMarker { get; init; } = true;

    public static FitOptions Default { get; } = new();

    /// <summary>The scale ladder this configuration searches.</summary>
    public FontScaleLadder Ladder => FontScaleLadder.Between(MinScale, rungs: 11);

    /// <summary>
    /// Event text size at full scale, in points.
    /// </summary>
    /// <remarks>
    /// Lives here rather than in the style because the fit search reasons in scales and needs
    /// to convert one into a point size. The month style reads it from here so there is one
    /// definition rather than two that can disagree.
    /// </remarks>
    public const float BaseEventSizePt = 7.5f;

    /// <summary>
    /// The point size below which the layout warns the user that the print will be hard to read.
    /// </summary>
    public const float SmallTypeWarningPt = 6f;
}
