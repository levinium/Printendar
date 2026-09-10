using System.Net;
using System.Text;
using Printendar.Core.Model;
using Printendar.Core.Sources;
using Printendar.Sources.Ics;

namespace Printendar.Core.Tests.Sources;

/// <summary>
/// Reading a published calendar feed.
/// </summary>
/// <remarks>
/// Offline: a stub handler answers instead of the network, so these run in CI and describe
/// what the code does with each answer rather than what some server did that day.
/// </remarks>
public class IcsUrlCalendarSourceTests
{
    private const string MinimalCalendar = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Test//EN
        BEGIN:VEVENT
        UID:1@test
        DTSTART;VALUE=DATE:20260304
        DTEND;VALUE=DATE:20260305
        SUMMARY:From the feed
        END:VEVENT
        END:VCALENDAR
        """;

    private sealed class StubHandler(
        HttpStatusCode status = HttpStatusCode.OK,
        string body = "",
        long? declaredLength = null) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken _)
        {
            LastRequest = request;
            Requests++;

            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/calendar"),
            };

            if (declaredLength is { } length)
            {
                response.Content.Headers.ContentLength = length;
            }

            return Task.FromResult(response);
        }
    }

    private static async Task<IReadOnlyList<CalendarEvent>> ReadAsync(IcsUrlCalendarSource source)
    {
        var calendars = await source.ListCalendarsAsync(default);

        return await source.GetEventsAsync(
            calendars,
            new DateSpan(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)),
            TimeZoneInfo.Utc,
            default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_address_is_described_rather_than_accepted(string? address)
    {
        // The Add button used to close the dialog on an empty box and do nothing whatsoever:
        // no calendar, no message, no clue that anything had gone wrong. Saying why is what
        // turns that into something a person can act on.
        Assert.NotNull(IcsUrlCalendarSource.DescribeAddressProblem(address));
    }

    [Theory]
    [InlineData("calendar.google.com/basic.ics")] // pasted without a scheme
    [InlineData("ftp://example.com/feed.ics")]    // a scheme, but not one that can be fetched
    [InlineData("just some words")]
    [InlineData("https://")]                      // a scheme and nothing else
    public void An_address_that_cannot_be_fetched_is_described_rather_than_accepted(string address)
    {
        var problem = IcsUrlCalendarSource.DescribeAddressProblem(address);

        Assert.NotNull(problem);

        // Naming what a good one looks like, because "invalid address" tells somebody they are
        // wrong without telling them what right would be.
        Assert.Contains("https://", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.com/feed.ics")]
    [InlineData("http://example.com/feed.ics")]
    [InlineData("webcal://example.com/feed.ics")]
    [InlineData("  https://example.com/feed.ics  ")]
    public void An_address_that_can_be_fetched_has_nothing_to_say_about_it(string address)
    {
        Assert.Null(IcsUrlCalendarSource.DescribeAddressProblem(address));
    }

    [Fact]
    public void The_check_and_the_conversion_agree_about_every_address()
    {
        // Two separate opinions about what a usable address is would drift, and the drift shows
        // up as a dialog that lets you press Add and then throws, or one that refuses an
        // address that would have worked perfectly.
        string?[] addresses =
        [
            null, "", "   ", "nonsense", "ftp://example.com/f.ics", "https://",
            "https://example.com/feed.ics", "webcal://example.com/feed.ics",
        ];

        foreach (var address in addresses)
        {
            var described = IcsUrlCalendarSource.DescribeAddressProblem(address) is null;
            var converts = Record.Exception(() => IcsUrlCalendarSource.NormalizeUrl(address!)) is null;

            Assert.True(described == converts, $"disagreement about \"{address}\"");
        }
    }

    [Fact]
    public async Task A_feed_is_asked_for_the_live_copy_rather_than_whatever_was_cached()
    {
        // The one thing Printendar prints is a calendar as it stands right now, and a meeting
        // added this morning missing from this afternoon's printout is the whole product
        // failing. Nothing between here and the publisher may answer from a store: not a
        // corporate proxy, not a CDN, not HttpClient's own handler.
        //
        // This is the half of freshness we control. How current the publisher's own copy is
        // remains theirs to decide.
        var handler = new StubHandler(body: MinimalCalendar);

        await using var source = new IcsUrlCalendarSource(
            "s", "Feed", "https://example.com/feed.ics", handler);

        await ReadAsync(source);

        var cacheControl = handler.LastRequest!.Headers.CacheControl;

        Assert.NotNull(cacheControl);
        Assert.True(cacheControl.NoCache, "the request must not be answered from a cache");
    }

    [Fact]
    public async Task Reading_twice_asks_the_publisher_twice()
    {
        // Printing re-reads every source first. If the source quietly answered the second read
        // from something it kept, that re-read would be theatre and the page would be as old
        // as the last time the month changed.
        var handler = new StubHandler(body: MinimalCalendar);

        await using var source = new IcsUrlCalendarSource(
            "s", "Feed", "https://example.com/feed.ics", handler);

        await ReadAsync(source);
        await ReadAsync(source);

        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task A_feed_is_read_into_events()
    {
        await using var source = new IcsUrlCalendarSource(
            "f1", "Team", "https://example.com/cal.ics", new StubHandler(body: MinimalCalendar));

        var events = await ReadAsync(source);

        Assert.Equal("From the feed", events.Single().Subject);
    }

    [Theory]
    [InlineData("webcal://example.com/cal.ics", "https://example.com/cal.ics")]
    [InlineData("WEBCAL://example.com/cal.ics", "https://example.com/cal.ics")]
    [InlineData("https://example.com/cal.ics", "https://example.com/cal.ics")]
    [InlineData("  https://example.com/cal.ics  ", "https://example.com/cal.ics")]
    public void A_webcal_link_is_treated_as_https(string pasted, string expected)
    {
        // Outlook and Apple Calendar hand out webcal:// links, so that is what people paste.
        // HttpClient does not know the scheme; it is plain HTTPS under a different name.
        Assert.Equal(expected, IcsUrlCalendarSource.NormalizeUrl(pasted).ToString());
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/cal.ics")]
    [InlineData("file:///c:/cal.ics")]
    public void Something_that_is_not_a_web_address_is_refused_with_an_explanation(string bad)
    {
        var error = Assert.Throws<InvalidOperationException>(() => IcsUrlCalendarSource.NormalizeUrl(bad));

        Assert.Contains("https://", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sign_in_page_returned_as_success_is_not_mistaken_for_a_calendar()
    {
        // The common failure with private feeds: the secret address is wrong, and the server
        // answers 200 with an HTML sign-in page. Without this check that parses as an empty
        // calendar and the user is told nothing at all.
        await using var source = new IcsUrlCalendarSource(
            "f1", "Team", "https://example.com/cal.ics",
            new StubHandler(body: "<html><body>Sign in</body></html>"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAsync(source));

        Assert.Contains("did not return a calendar", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_feed_says_the_address_is_wrong()
    {
        await using var source = new IcsUrlCalendarSource(
            "f1", "Team", "https://example.com/cal.ics", new StubHandler(HttpStatusCode.NotFound));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAsync(source));

        Assert.Contains("does not exist", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_feed_mentions_the_secret_in_the_address()
    {
        // Published calendar URLs carry a long secret; a truncated paste is the usual cause.
        await using var source = new IcsUrlCalendarSource(
            "f1", "Team", "https://example.com/cal.ics", new StubHandler(HttpStatusCode.Forbidden));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAsync(source));

        Assert.Contains("secret", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_enormous_feed_is_refused_before_it_is_downloaded()
    {
        await using var source = new IcsUrlCalendarSource(
            "f1", "Team", "https://example.com/cal.ics",
            new StubHandler(body: MinimalCalendar, declaredLength: 50L * 1024 * 1024));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAsync(source));

        Assert.Contains("larger than a calendar", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_request_looks_like_a_browser()
    {
        // Microsoft's published-calendar endpoint refuses clients that do not, and the
        // resulting error page reads as "not a calendar" rather than "was refused", which
        // sends anyone debugging it in the wrong direction.
        var handler = new StubHandler(body: MinimalCalendar);

        await using var source = new IcsUrlCalendarSource("f1", "Team", "https://example.com/cal.ics", handler);
        await ReadAsync(source);

        Assert.Contains("Mozilla", handler.LastRequest!.Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_ticked_means_nothing_is_fetched()
    {
        var handler = new StubHandler(body: MinimalCalendar);

        await using var source = new IcsUrlCalendarSource("f1", "Team", "https://example.com/cal.ics", handler);

        await source.GetEventsAsync(
            [], new DateSpan(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)), TimeZoneInfo.Utc, default);

        Assert.Null(handler.LastRequest);
    }
}
