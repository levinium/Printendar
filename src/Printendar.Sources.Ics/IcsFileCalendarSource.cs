using Printendar.Core.Model;
using Printendar.Core.Sources;

namespace Printendar.Sources.Ics;

/// <summary>
/// A calendar read from one or more .ics files on disk.
/// </summary>
/// <remarks>
/// The route that needs nothing: no account, no administrator approval, no network. Export a
/// calendar from Outlook, Google, Apple Calendar or anything else that speaks iCalendar, open
/// the file, print it. For a public tool this is the only path a stranger can use the moment
/// they download it, and the fallback for anyone whose organisation will not approve a third
/// party application.
/// </remarks>
public sealed class IcsFileCalendarSource : ICalendarSource
{
    /// <summary>
    /// Refuses a file large enough to suggest something other than a calendar.
    /// </summary>
    /// <remarks>
    /// A year of a busy calendar is a few hundred kilobytes. Twenty megabytes means a mistake,
    /// and reading it would freeze the window with no explanation.
    /// </remarks>
    private const long MaxFileBytes = 20L * 1024 * 1024;

    private readonly List<IcsFile> _files = [];

    private sealed record IcsFile(string Id, string DisplayName, string Path);

    public string SourceId => "ics";

    public string ProviderName => "Calendar file";

    /// <summary>Adds a file, returning the calendar it becomes.</summary>
    /// <exception cref="InvalidOperationException">The file is missing, too large, or not a calendar.</exception>
    public CalendarRef AddFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var info = new FileInfo(path);

        if (!info.Exists)
        {
            throw new InvalidOperationException($"There is no file at {path}.");
        }

        if (info.Length > MaxFileBytes)
        {
            throw new InvalidOperationException(
                $"That file is {info.Length / (1024 * 1024)} MB, which is far larger than a calendar should be. " +
                "Check it is the .ics file you meant to open.");
        }

        var id = info.FullName;
        var name = Path.GetFileNameWithoutExtension(info.Name);

        _files.RemoveAll(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));
        _files.Add(new IcsFile(id, name, info.FullName));

        return new CalendarRef(SourceId, id, name, IsDefault: _files.Count == 1);
    }

    public ValueTask<AuthState> GetAuthStateAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(AuthState.Connected);

    public Task<AccountInfo> ConnectAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AccountInfo("Calendar files", null));

    public Task SignOutAsync(CancellationToken cancellationToken)
    {
        _files.Clear();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CalendarRef>> ListCalendarsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CalendarRef>>(
            [.. _files.Select((f, i) => new CalendarRef(SourceId, f.Id, f.DisplayName, i == 0))]);

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        IReadOnlyList<CalendarRef> calendars,
        DateSpan window,
        TimeZoneInfo displayZone,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(calendars);

        var events = new List<CalendarEvent>();

        foreach (var calendar in calendars)
        {
            var file = _files.FirstOrDefault(f =>
                string.Equals(f.Id, calendar.CalendarId, StringComparison.OrdinalIgnoreCase));

            if (file is null)
            {
                continue;
            }

            // Read fresh each time rather than caching. The file is on disk and may have been
            // re-exported since, and a calendar showing last week's export with no way to tell
            // is worse than reading a few hundred kilobytes again.
            var content = await File.ReadAllTextAsync(file.Path, cancellationToken).ConfigureAwait(false);

            events.AddRange(IcsCalendarReader.Read(content, file.Id, window, displayZone));
        }

        return events;
    }

    public ValueTask DisposeAsync()
    {
        _files.Clear();
        return ValueTask.CompletedTask;
    }
}
