using System.Net;

namespace Printendar.Sources.Ics;

/// <summary>
/// A published iCalendar feed, read over the network.
/// </summary>
/// <remarks>
/// This is the capability that makes Printendar a desktop app rather than a web page. A
/// browser cannot fetch these at all: published calendar endpoints send no CORS headers, so
/// the request is blocked before it starts, and Microsoft's in particular rejects clients that
/// do not look like a browser. A local program has neither problem.
///
/// The feed is re-read on every print, so a calendar that changed this morning prints
/// correctly this afternoon without the user doing anything.
/// </remarks>
public sealed class IcsUrlCalendarSource : IcsSourceBase
{
    /// <summary>
    /// A feed should answer quickly or be reported as broken.
    /// </summary>
    /// <remarks>
    /// Without this the default HttpClient timeout is 100 seconds, and a dead feed would hang
    /// the print for over a minute with the window giving no reason.
    /// </remarks>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public IcsUrlCalendarSource(string sourceId, string displayName, string url, HttpMessageHandler? handler = null)
        : base(sourceId, displayName)
    {
        Url = NormalizeUrl(url);

        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _ownsClient = true;
        _http.Timeout = Timeout;

        // Published-calendar endpoints, Microsoft's among them, reject requests that do not
        // look like a browser. Without this the feed returns an error page rather than a
        // calendar, and the failure reads as "not a calendar" rather than "was refused".
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Printendar/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("text/calendar, text/plain;q=0.9, */*;q=0.8");
    }

    public Uri Url { get; }

    public override string ProviderName => "Calendar feed";

    /// <summary>
    /// Turns what people actually paste into something HttpClient can fetch.
    /// </summary>
    /// <remarks>
    /// Outlook and Apple Calendar hand out `webcal://` links, and that is what lands in the
    /// box. It is not a scheme HttpClient knows; it is plain HTTPS with a different name, so
    /// rewriting it is the difference between the common case working and failing with an
    /// unhelpful protocol error.
    /// </remarks>
    public static Uri NormalizeUrl(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var trimmed = url.Trim();

        if (trimmed.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = string.Concat("https://", trimmed.AsSpan("webcal://".Length));
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                $"\"{url}\" is not a calendar address. It should begin with https:// or webcal://.");
        }

        return uri;
    }

    protected override async Task<string> ReadContentAsync(CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await _http
                .GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Could not reach {Url.Host}: {ex.Message}", ex);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation, which would otherwise be
            // mistaken for the user changing month.
            throw new InvalidOperationException($"{Url.Host} did not answer within {Timeout.TotalSeconds:0} seconds.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(Explain(response.StatusCode));
            }

            // Checked before reading rather than after, so a hostile or mistaken feed cannot
            // make the app allocate its way into trouble first.
            if (response.Content.Headers.ContentLength is { } declared && declared > MaxBytes)
            {
                throw new InvalidOperationException(
                    $"That feed is {declared / (1024 * 1024)} MB, which is far larger than a calendar should be.");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            // A feed that lies about its length, or sends none, is still capped.
            if (content.Length > MaxBytes)
            {
                throw new InvalidOperationException("That feed is far larger than a calendar should be.");
            }

            if (!content.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
            {
                // Almost always a sign-in page returned with 200, which is what a private feed
                // does when the secret address is wrong.
                throw new InvalidOperationException(
                    $"{Url.Host} did not return a calendar. Check the address is the published " +
                    "iCalendar link rather than a page you have to sign in to.");
            }

            return content;
        }
    }

    private static string Explain(HttpStatusCode status) => status switch
    {
        HttpStatusCode.NotFound => "That address does not exist. Check it was copied in full.",

        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            "That feed refused the request. Published calendar links usually contain a long " +
            "secret; check the whole address was copied.",

        _ => $"That feed answered {(int)status} {status}.",
    };

    public override ValueTask DisposeAsync()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
