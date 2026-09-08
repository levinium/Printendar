using Printendar.Core.Settings;
using Printendar.Core.Sources;
using Printendar.Sources.Ics;
using Printendar.Sources.Microsoft365;

namespace Printendar.App.Sources;

/// <summary>
/// Turns a saved source into one that can actually be read.
/// </summary>
/// <remarks>
/// The single place that knows which provider each kind means. Everything else, including the
/// window and the aggregator, deals only in <see cref="ICalendarSource"/>, so adding a provider
/// is a new case here plus a new adapter rather than a change to anything that draws.
/// </remarks>
public static class CalendarSourceFactory
{
    /// <summary>Whether this build can actually use a kind, and why not when it cannot.</summary>
    /// <remarks>
    /// Asked before a source is offered, so an unusable one is explained in the list rather
    /// than failing when the user next prints.
    /// </remarks>
    public static string? UnavailableReason(CalendarSourceKind kind, AppSettings settings) => kind switch
    {
        CalendarSourceKind.Microsoft365 when !Microsoft365Options.Resolve(settings).IsConfigured =>
            "This build has no Microsoft app registration. Your IT administrator can add one.",

        CalendarSourceKind.Google =>
            "Google Calendar is not in this build yet.",

        _ => null,
    };

    public static ICalendarSource Create(ConfiguredSource configured, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(configured);

        return configured.Kind switch
        {
            CalendarSourceKind.IcsFile => new IcsFileCalendarSource(
                configured.Id, configured.DisplayName, configured.Location!),

            CalendarSourceKind.IcsUrl => new IcsUrlCalendarSource(
                configured.Id, configured.DisplayName, configured.Location!),

            // The source id is passed through so two Microsoft accounts stay apart. Without it
            // both would claim "microsoft365", their calendars would collide and the
            // aggregator could not say which of them failed.
            CalendarSourceKind.Microsoft365 => new GraphCalendarSource(
                Microsoft365Options.Resolve(settings), configured.Id),

            // Reached only if a kind is added to the enum and not to this switch, which is a
            // build-time mistake worth failing loudly on rather than silently skipping.
            _ => throw new NotSupportedException(
                $"{configured.Kind} calendars cannot be read by this build."),
        };
    }
}
