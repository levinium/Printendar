using Printendar.Core.Model;

namespace Printendar.Core.Sources;

/// <summary>One source and the calendars inside it the user wants printed.</summary>
public sealed record SourceSelection(ICalendarSource Source, IReadOnlyList<CalendarRef> Calendars);

/// <summary>A source that could not be read, in words the user can act on.</summary>
public sealed record SourceFailure(string SourceId, string ProviderName, string Message);

/// <summary>What came back from reading every source.</summary>
/// <remarks>
/// Events and failures together, never one or the other. A partial page plus a named problem
/// is far more useful than an exception, because the other three calendars are still worth
/// printing.
/// </remarks>
public sealed record AggregateResult(
    IReadOnlyList<CalendarEvent> Events,
    IReadOnlyList<SourceFailure> Failures);

/// <summary>
/// Reads several calendar sources into the one set of events a month is laid out from.
/// </summary>
/// <remarks>
/// Two decisions carry the weight.
///
/// Sources are read concurrently, because they are independent network calls and doing them in
/// turn would make four accounts four times slower for no reason. That makes completion order
/// vary run to run, so the merged result is sorted before it is returned: otherwise "+2 more"
/// would hide different events on each print of the same month, and layout snapshots would
/// flap without anything having changed.
///
/// A source that throws costs the user that source and nothing else. A calendar file that has
/// been moved, or a feed that is offline, should not stop the other three printing.
/// Cancellation is excluded from that: it is what happens when the user changes month, and
/// reporting it as a broken calendar would fill the window with errors during ordinary use.
/// </remarks>
public static class CalendarAggregator
{
    public static async Task<AggregateResult> GetEventsAsync(
        IReadOnlyList<SourceSelection> selections,
        DateSpan window,
        TimeZoneInfo displayZone,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selections);

        // Unticking every calendar in a source should not still cost a round trip.
        var active = selections.Where(s => s.Calendars.Count > 0).ToList();

        if (active.Count == 0)
        {
            return new AggregateResult([], []);
        }

        var reads = active.Select(s => ReadAsync(s, window, displayZone, cancellationToken));

        var outcomes = await Task.WhenAll(reads);

        cancellationToken.ThrowIfCancellationRequested();

        var events = outcomes
            .SelectMany(o => o.Events)
            .Order(EventOrder.Comparer)
            .ToList();

        var failures = outcomes
            .Select(o => o.Failure)
            .Where(f => f is not null)
            .Select(f => f!)
            .ToList();

        return new AggregateResult(events, failures);
    }

    private static async Task<(IReadOnlyList<CalendarEvent> Events, SourceFailure? Failure)> ReadAsync(
        SourceSelection selection,
        DateSpan window,
        TimeZoneInfo displayZone,
        CancellationToken cancellationToken)
    {
        try
        {
            var events = await selection.Source.GetEventsAsync(
                selection.Calendars, window, displayZone, cancellationToken);

            return (events, null);
        }
        catch (OperationCanceledException)
        {
            // The user moved on. Not a broken calendar, and not this method's to report.
            throw;
        }
        catch (Exception ex)
        {
            // Deliberately broad. Every provider fails in its own vocabulary (HTTP, MSAL, IO,
            // malformed iCalendar), and the point here is that none of them can take down a
            // page that three other calendars would have printed fine.
            return ([], new SourceFailure(
                selection.Source.SourceId,
                selection.Source.ProviderName,
                ex.Message));
        }
    }
}
