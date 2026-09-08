using Printendar.Core.Model;
using Printendar.Core.Sources;

namespace Printendar.Sources.Ics;

/// <summary>
/// One iCalendar, wherever it is read from.
/// </summary>
/// <remarks>
/// A file and a feed differ only in how the text is fetched, so everything else lives here.
///
/// One calendar per source, deliberately. The alternative was a single "Calendar files" source
/// holding several files, but then the list the user manages does not match the things they
/// added: they open three files and see one entry. Making each its own source also gives each
/// a distinct id, which the aggregator needs in order to attribute events and report which
/// calendar failed.
/// </remarks>
public abstract class IcsSourceBase(string sourceId, string displayName) : ICalendarSource
{
    /// <summary>
    /// Refuses content large enough to suggest something other than a calendar.
    /// </summary>
    /// <remarks>
    /// A year of a busy calendar is a few hundred kilobytes. Twenty megabytes means a mistake
    /// or a hostile feed, and reading it would freeze the window with no explanation.
    /// </remarks>
    protected const long MaxBytes = 20L * 1024 * 1024;

    public string SourceId { get; } = sourceId;

    public string DisplayName { get; } = displayName;

    public abstract string ProviderName { get; }

    /// <summary>Fetches the raw iCalendar text.</summary>
    protected abstract Task<string> ReadContentAsync(CancellationToken cancellationToken);

    public ValueTask<AuthState> GetAuthStateAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(AuthState.Connected);

    public Task<AccountInfo> ConnectAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AccountInfo(DisplayName, null));

    public Task SignOutAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<CalendarRef>> ListCalendarsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CalendarRef>>(
            [new CalendarRef(SourceId, SourceId, DisplayName, IsDefault: true)]);

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        IReadOnlyList<CalendarRef> calendars,
        DateSpan window,
        TimeZoneInfo displayZone,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(calendars);

        if (calendars.Count == 0)
        {
            return [];
        }

        // Read fresh every time rather than caching. The file may have been re-exported and
        // the feed may have changed, and a calendar quietly showing last week's export with no
        // way to tell is worse than fetching a few hundred kilobytes again.
        var content = await ReadContentAsync(cancellationToken).ConfigureAwait(false);

        return IcsCalendarReader.Read(content, SourceId, window, displayZone);
    }

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
