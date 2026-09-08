using Printendar.Core.Model;

namespace Printendar.Core.Sources;

/// <summary>Whether a source can currently be read.</summary>
public enum AuthState
{
    /// <summary>Nobody has signed in yet.</summary>
    NotConnected,

    /// <summary>Ready to read, from a cached token or because no sign-in is needed.</summary>
    Connected,

    /// <summary>Signed in once, but the token has expired and the user must be asked again.</summary>
    NeedsInteraction,
}

/// <param name="DisplayName">The person's name, for the account chip in the window.</param>
/// <param name="Email">Their address, to tell two accounts apart.</param>
public sealed record AccountInfo(string DisplayName, string? Email);

/// <summary>One calendar the user could choose to print.</summary>
/// <param name="SourceId">Which source it came from.</param>
/// <param name="CalendarId">The provider's own identifier.</param>
/// <param name="DisplayName">What the user calls it.</param>
/// <param name="IsDefault">True for the account's primary calendar, which is preselected.</param>
public sealed record CalendarRef(string SourceId, string CalendarId, string DisplayName, bool IsDefault);

/// <summary>
/// Reads calendars from somewhere: Microsoft 365, Google, or an ICS file or feed.
/// </summary>
/// <remarks>
/// Small on purpose. Everything hard about a provider (recurrence expansion, paging, time zone
/// conversion, its own idea of an exclusive end date) is the adapter's problem and is dealt
/// with before anything crosses this boundary. What comes back is
/// <see cref="CalendarEvent"/> occurrences, already in the display zone, already expanded.
///
/// The layout engine therefore has no idea Microsoft 365 exists, and a new provider is a new
/// implementation of this interface rather than a change to anything that draws.
/// </remarks>
public interface ICalendarSource : IAsyncDisposable
{
    /// <summary>Stable id for this source, used in settings and to attribute events.</summary>
    string SourceId { get; }

    /// <summary>What the user should see, for example "Microsoft 365".</summary>
    string ProviderName { get; }

    ValueTask<AuthState> GetAuthStateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Signs in, showing whatever the provider requires.
    /// </summary>
    /// <remarks>
    /// Tries silently first wherever the provider allows it, so that someone who signed in
    /// last week is not asked again every time the program starts.
    /// </remarks>
    Task<AccountInfo> ConnectAsync(CancellationToken cancellationToken);

    Task SignOutAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendarRef>> ListCalendarsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads occurrences in a date range.
    /// </summary>
    /// <remarks>
    /// Returns occurrences, not series: recurrence is already expanded. Times are already in
    /// <paramref name="displayZone"/>, and all-day events carry dates only.
    /// </remarks>
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        IReadOnlyList<CalendarRef> calendars,
        DateSpan window,
        TimeZoneInfo displayZone,
        CancellationToken cancellationToken);
}
