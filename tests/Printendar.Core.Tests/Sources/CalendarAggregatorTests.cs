using Printendar.Core.Model;
using Printendar.Core.Sources;

namespace Printendar.Core.Tests.Sources;

/// <summary>
/// Reading several calendar sources into one month.
/// </summary>
/// <remarks>
/// The behaviour that matters is what happens when one source is broken. A calendar file that
/// has been moved, or a feed that is offline, must cost the user that calendar and nothing
/// else: the page still prints with everything that could be read, and the failure is named.
/// Losing a whole month's print because one of four sources timed out would be the worst
/// possible response.
/// </remarks>
public class CalendarAggregatorTests
{
    private static readonly DateSpan Window = new(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

    private static CalendarEvent Event(string sourceId, string calendarId, string subject, int day) =>
        new()
        {
            Id = $"{sourceId}-{subject}",
            CalendarId = calendarId,
            Subject = subject,
            IsAllDay = true,
            Days = new DateSpan(new DateOnly(2026, 3, day), new DateOnly(2026, 3, day)),
        };

    /// <summary>A source that returns what it was told to, or throws.</summary>
    private sealed class FakeSource(
        string id,
        IReadOnlyList<CalendarEvent>? events = null,
        Exception? throws = null,
        TimeSpan delay = default) : ICalendarSource
    {
        public string SourceId => id;

        public string ProviderName => "Fake";

        /// <summary>What this source was actually asked for, so fan-out can be checked.</summary>
        public List<string> RequestedCalendarIds { get; } = [];

        public int CallCount { get; private set; }

        public ValueTask<AuthState> GetAuthStateAsync(CancellationToken _) =>
            ValueTask.FromResult(AuthState.Connected);

        public Task<AccountInfo> ConnectAsync(CancellationToken _) =>
            Task.FromResult(new AccountInfo(id, null));

        public Task SignOutAsync(CancellationToken _) => Task.CompletedTask;

        public Task<IReadOnlyList<CalendarRef>> ListCalendarsAsync(CancellationToken _) =>
            Task.FromResult<IReadOnlyList<CalendarRef>>([]);

        public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
            IReadOnlyList<CalendarRef> calendars,
            DateSpan window,
            TimeZoneInfo displayZone,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestedCalendarIds.AddRange(calendars.Select(c => c.CalendarId));

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            return throws is not null ? throw throws : events ?? [];
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static SourceSelection Select(ICalendarSource source, params string[] calendarIds) =>
        new(source, [.. calendarIds.Select(c => new CalendarRef(source.SourceId, c, c, false))]);

    [Fact]
    public async Task Events_from_every_source_end_up_on_one_page()
    {
        var work = new FakeSource("work", [Event("work", "c1", "Standup", 2)]);
        var home = new FakeSource("home", [Event("home", "c2", "Dentist", 3)]);

        var result = await CalendarAggregator.GetEventsAsync(
            [Select(work, "c1"), Select(home, "c2")], Window, TimeZoneInfo.Utc, default);

        // Sorted before comparing, because what this test is about is that both arrived, not
        // the order they arrived in. Ordering has its own test.
        Assert.Equal(["Dentist", "Standup"], result.Events.Select(e => e.Subject).Order());
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task Each_source_is_asked_only_for_its_own_calendars()
    {
        // Passing one source another's calendar ids is the kind of mistake that produces an
        // empty page rather than an error, because most providers just return nothing.
        var work = new FakeSource("work");
        var home = new FakeSource("home");

        await CalendarAggregator.GetEventsAsync(
            [Select(work, "w1", "w2"), Select(home, "h1")], Window, TimeZoneInfo.Utc, default);

        Assert.Equal(["w1", "w2"], work.RequestedCalendarIds);
        Assert.Equal(["h1"], home.RequestedCalendarIds);
    }

    [Fact]
    public async Task One_broken_source_does_not_cost_the_others()
    {
        var good = new FakeSource("good", [Event("good", "c1", "Kept", 4)]);
        var broken = new FakeSource("broken", throws: new IOException("the file has moved"));

        var result = await CalendarAggregator.GetEventsAsync(
            [Select(good, "c1"), Select(broken, "c2")], Window, TimeZoneInfo.Utc, default);

        Assert.Equal("Kept", result.Events.Single().Subject);
        Assert.Equal("broken", result.Failures.Single().SourceId);
    }

    [Fact]
    public async Task A_failure_says_what_went_wrong_in_the_provider_words()
    {
        // "Something went wrong" tells the user nothing. "The file has moved" tells them what
        // to fix.
        var broken = new FakeSource("broken", throws: new IOException("the file has moved"));

        var result = await CalendarAggregator.GetEventsAsync(
            [Select(broken, "c1")], Window, TimeZoneInfo.Utc, default);

        Assert.Contains("the file has moved", result.Failures.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_source_with_nothing_ticked_is_not_called_at_all()
    {
        // Unticking every calendar in a source should not still cost a network round trip.
        var idle = new FakeSource("idle");

        await CalendarAggregator.GetEventsAsync([Select(idle)], Window, TimeZoneInfo.Utc, default);

        Assert.Equal(0, idle.CallCount);
    }

    [Fact]
    public async Task The_order_does_not_depend_on_which_source_answered_first()
    {
        // Sources are read concurrently, so completion order varies run to run. If it reached
        // the page, "+2 more" would hide different events each print and the layout snapshots
        // would flap for no reason.
        var slow = new FakeSource("slow", [Event("slow", "c1", "Aardvark", 2)], delay: TimeSpan.FromMilliseconds(60));
        var fast = new FakeSource("fast", [Event("fast", "c2", "Zebra", 2)]);

        var first = await CalendarAggregator.GetEventsAsync(
            [Select(slow, "c1"), Select(fast, "c2")], Window, TimeZoneInfo.Utc, default);

        var second = await CalendarAggregator.GetEventsAsync(
            [Select(fast, "c2"), Select(slow, "c1")], Window, TimeZoneInfo.Utc, default);

        Assert.Equal(
            first.Events.Select(e => e.Subject),
            second.Events.Select(e => e.Subject));
    }

    [Fact]
    public async Task Cancelling_is_not_reported_as_a_broken_calendar()
    {
        // Changing month cancels the previous read. Turning that into "your calendar failed"
        // would light up the window with errors during ordinary use.
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var source = new FakeSource("s", [Event("s", "c1", "X", 2)]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CalendarAggregator.GetEventsAsync([Select(source, "c1")], Window, TimeZoneInfo.Utc, cancelled.Token));
    }

    [Fact]
    public async Task Nothing_configured_returns_nothing_rather_than_failing()
    {
        var result = await CalendarAggregator.GetEventsAsync([], Window, TimeZoneInfo.Utc, default);

        Assert.Empty(result.Events);
        Assert.Empty(result.Failures);
    }
}
