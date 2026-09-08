namespace Printendar.Core.Sources;

/// <summary>Whether a source is actually contributing events to the page.</summary>
public enum SourceAvailability
{
    /// <summary>Open, readable, and included.</summary>
    Ready,

    /// <summary>Its account is signed out, so nothing from it is on the page.</summary>
    NotSignedIn,

    /// <summary>It could not be read, so nothing from it is on the page.</summary>
    Failed,
}

/// <param name="Message">Why, in the provider's words, or null when there is nothing to say.</param>
public sealed record SourceStatus(
    string SourceId,
    string DisplayName,
    SourceAvailability Availability,
    string? Message)
{
    /// <summary>Whether the printed page will be missing this calendar's events.</summary>
    public bool IsMissingFromPage => Availability is not SourceAvailability.Ready;
}

/// <summary>
/// Says, in one sentence, that the page is incomplete and what to do about it.
/// </summary>
/// <remarks>
/// This exists because of a specific and quiet failure: an account that signed out weeks ago
/// contributes nothing, the month draws perfectly, and the sheet goes on a wall missing half
/// the meetings it should show. Nothing about the page reveals that. A calendar that is wrong
/// but looks right is worse than one that is obviously broken, so it has to be said on the way
/// to the printer rather than left in a status bar to be scrolled past.
///
/// The wording states the consequence rather than the status. "Work could not be read" is a
/// fact about a program; "this page is missing events from Work" is a fact about the sheet
/// somebody is about to print, and only the second one stops them.
/// </remarks>
public static class MissingCalendarWarning
{
    public static string? Describe(IReadOnlyList<SourceStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);

        var missing = statuses.Where(s => s.IsMissingFromPage).ToList();

        if (missing.Count == 0)
        {
            return null;
        }

        var names = string.Join(", ", missing.Select(m => m.DisplayName));

        var opening = missing.Count == 1
            ? $"This page is missing events from {names}."
            : $"This page is missing events from {missing.Count} calendars: {names}.";

        // With one thing wrong there is room to say why, and knowing why is the difference
        // between signing in and only knowing that something is broken. With several, the
        // reasons differ and stacking them makes a paragraph nobody reads.
        var reason = missing.Count == 1 && !string.IsNullOrWhiteSpace(missing[0].Message)
            ? $" {missing[0].Message}"
            : string.Empty;

        return $"{opening}{reason} Sign in again or remove it, or print without it.";
    }
}
