using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Identity.Client;
using Printendar.Core.Model;
using Printendar.Core.Sources;

namespace Printendar.Sources.Microsoft365;

/// <summary>
/// Reads calendars from Microsoft 365 and Outlook.com through Microsoft Graph.
/// </summary>
/// <remarks>
/// Uses calendarView rather than the events collection, and that is not interchangeable: only
/// calendarView expands a recurring series into the individual occurrences that fall in a
/// window. Asking /events for March returns the series masters, so a weekly standup would
/// appear once instead of on every weekday.
/// </remarks>
public sealed class GraphCalendarSource : ICalendarSource
{
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";

    /// <summary>
    /// Graph's cap is 1000, but a smaller page returns sooner and keeps the UI responsive.
    /// </summary>
    private const int PageSize = 200;

    /// <summary>
    /// Stops a pathological mailbox from paging forever.
    /// </summary>
    /// <remarks>
    /// A month cannot legitimately hold this many occurrences, so hitting it means something
    /// is wrong and the user is better served by a page that draws than by one that never
    /// arrives.
    /// </remarks>
    private const int MaxPages = 50;

    private readonly Microsoft365Options _options;

    private readonly Microsoft365Authenticator _authenticator;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    private readonly string _sourceId;

    /// <param name="sourceId">
    /// Distinguishes this account from other Microsoft accounts. Events and calendars are
    /// attributed by it, so two accounts sharing one id would merge into each other and the
    /// aggregator could not say which of them failed.
    /// </param>
    public GraphCalendarSource(
        Microsoft365Options options,
        string? sourceId = null,
        string? homeAccountId = null,
        HttpClient? http = null)
    {
        _options = options;
        _sourceId = string.IsNullOrWhiteSpace(sourceId) ? "microsoft365" : sourceId;
        _authenticator = new Microsoft365Authenticator(options, homeAccountId);
        _http = http ?? new HttpClient();
        _ownsHttp = http is null;
    }

    public string SourceId => _sourceId;

    /// <summary>MSAL's identifier for the account that signed in, once one has.</summary>
    public string? HomeAccountId { get; private set; }

    public string ProviderName => "Microsoft 365";

    public ValueTask<AuthState> GetAuthStateAsync(CancellationToken cancellationToken) =>
        _authenticator.GetStateAsync(cancellationToken);

    public async Task<AccountInfo> ConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _authenticator.AcquireTokenAsync(allowInteraction: true, cancellationToken)
                .ConfigureAwait(false);

            var account = result.Account;

            // Kept so the caller can store which account this is. Two Microsoft accounts are
            // told apart by this and nothing else.
            HomeAccountId = account.HomeAccountId?.Identifier;

            return new AccountInfo(
                account.Username ?? "Signed in",
                account.Username);
        }
        catch (Exception ex) when (ex is MsalException or OperationCanceledException)
        {
            // Translated at the boundary, so nothing above here has to know what an AADSTS
            // code is, and the user is told what to do rather than what went wrong.
            throw new Microsoft365SignInException(Microsoft365Diagnostics.Interpret(ex, _options), ex);
        }
    }

    public Task SignOutAsync(CancellationToken cancellationToken) =>
        _authenticator.SignOutAsync(cancellationToken);

    public async Task<IReadOnlyList<CalendarRef>> ListCalendarsAsync(CancellationToken cancellationToken)
    {
        var calendars = new List<CalendarRef>();

        await foreach (var element in EnumerateAsync($"{GraphBaseUrl}/me/calendars?$top={PageSize}", cancellationToken)
            .ConfigureAwait(false))
        {
            if (!element.TryGetProperty("id", out var id) || id.GetString() is not { } calendarId)
            {
                continue;
            }

            var name = element.TryGetProperty("name", out var n) ? n.GetString() : null;
            var isDefault = element.TryGetProperty("isDefaultCalendar", out var d) &&
                            d.ValueKind is JsonValueKind.True;

            calendars.Add(new CalendarRef(SourceId, calendarId, name ?? "Calendar", isDefault));
        }

        // Default first, then alphabetical, so the picker opens on the one most people want.
        return [.. calendars.OrderByDescending(c => c.IsDefault).ThenBy(c => c.DisplayName, StringComparer.CurrentCulture)];
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        IReadOnlyList<CalendarRef> calendars,
        DateSpan window,
        TimeZoneInfo displayZone,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(calendars);
        ArgumentNullException.ThrowIfNull(displayZone);

        var events = new List<CalendarEvent>();

        foreach (var calendar in calendars)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = BuildCalendarViewUrl(calendar.CalendarId, window, displayZone);

            await foreach (var element in EnumerateAsync(url, cancellationToken).ConfigureAwait(false))
            {
                var mapped = GraphEventMapper.Map(element, calendar.CalendarId, displayZone);

                if (mapped is not null)
                {
                    events.Add(mapped);
                }
            }
        }

        return events;
    }

    public async ValueTask DisposeAsync()
    {
        await _authenticator.DisposeAsync().ConfigureAwait(false);

        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    /// <summary>
    /// Builds the calendarView request for a date range.
    /// </summary>
    /// <remarks>
    /// The window is widened by a day at each end and expressed in UTC. A cell on the grid is
    /// a local day, so an event at 23:00 on the last day of the month is already the following
    /// day in UTC and would be missed by an exact range. Fetching a little more and letting the
    /// layout discard what it does not need is much cheaper than a missing event.
    /// </remarks>
    internal static string BuildCalendarViewUrl(string calendarId, DateSpan window, TimeZoneInfo displayZone)
    {
        var start = TimeZoneInfo.ConvertTimeToUtc(
            window.First.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), displayZone);

        var end = TimeZoneInfo.ConvertTimeToUtc(
            window.Last.AddDays(2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), displayZone);

        var query =
            $"startDateTime={Uri.EscapeDataString(start.ToString("o", CultureInfo.InvariantCulture))}" +
            $"&endDateTime={Uri.EscapeDataString(end.ToString("o", CultureInfo.InvariantCulture))}" +
            $"&$top={PageSize}" +
            "&$orderby=start/dateTime" +
            "&$select=id,subject,isAllDay,isCancelled,showAs,sensitivity,categories,seriesMasterId," +
            "start,end,location,organizer,responseStatus";

        return $"{GraphBaseUrl}/me/calendars/{Uri.EscapeDataString(calendarId)}/calendarView?{query}";
    }

    /// <summary>Walks a Graph collection, following nextLink until the pages run out.</summary>
    private async IAsyncEnumerable<JsonElement> EnumerateAsync(
        string url,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var next = url;

        for (var page = 0; next is not null && page < MaxPages; page++)
        {
            var document = await GetAsync(next, cancellationToken).ConfigureAwait(false);

            using (document)
            {
                if (document.RootElement.TryGetProperty("value", out var value) &&
                    value.ValueKind is JsonValueKind.Array)
                {
                    foreach (var item in value.EnumerateArray())
                    {
                        // Cloned because the document is disposed at the end of this block and
                        // the caller keeps the element beyond it.
                        yield return item.Clone();
                    }
                }

                next = document.RootElement.TryGetProperty("@odata.nextLink", out var link)
                    ? link.GetString()
                    : null;
            }
        }
    }

    private async Task<JsonDocument> GetAsync(string url, CancellationToken cancellationToken)
    {
        var token = await _authenticator.AcquireTokenAsync(allowInteraction: false, cancellationToken)
            .ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        // Asking for UTC keeps every provider on the same footing: the adapter hands UTC to
        // EventNormalizer, which is the single place a display zone is applied.
        request.Headers.TryAddWithoutValidation("Prefer", "outlook.timezone=\"UTC\"");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            throw new InvalidOperationException(
                $"Microsoft Graph returned {(int)response.StatusCode} {response.ReasonPhrase} for {url}. {Summarize(body)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Pulls the useful sentence out of a Graph error body.</summary>
    private static string Summarize(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            return document.RootElement.TryGetProperty("error", out var error) &&
                   error.TryGetProperty("message", out var message)
                ? message.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            return body.Length > 200 ? body[..200] : body;
        }
    }
}
