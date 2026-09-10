using Printendar.Core.Settings;
using Printendar.Core.Sources;
using Printendar.Sources.Ics;

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
    public static ICalendarSource Create(ConfiguredSource configured, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(configured);

        return configured.Kind switch
        {
            CalendarSourceKind.IcsFile => new IcsFileCalendarSource(
                configured.Id, configured.DisplayName, configured.Location!),

            CalendarSourceKind.IcsUrl => new IcsUrlCalendarSource(
                configured.Id, configured.DisplayName, configured.Location!),

            // Reached only if a kind is added to the enum and not to this switch, which is a
            // build-time mistake worth failing loudly on rather than silently skipping.
            _ => throw new NotSupportedException(
                $"{configured.Kind} calendars cannot be read by this build."),
        };
    }
}
