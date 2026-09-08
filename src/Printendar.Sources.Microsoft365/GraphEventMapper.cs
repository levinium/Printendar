using System.Globalization;
using System.Text.Json;
using Printendar.Core.Model;
using Printendar.Core.Time;

namespace Printendar.Sources.Microsoft365;

/// <summary>
/// Turns a Graph calendarView entry into a Printendar occurrence.
/// </summary>
/// <remarks>
/// Separated from the network code so that every provider quirk can be tested offline against
/// recorded shapes. This is where the bugs live; talking to Graph is comparatively dull.
///
/// The important quirk: Graph writes timestamps with no offset, as
/// "2026-03-17T14:00:00.0000000", and names the zone in a sibling field. Parsing one straight
/// into a <see cref="DateTimeOffset"/> attaches the machine's own offset instead, which puts
/// every event at the wrong clock time for anyone outside that zone, and on the wrong day for
/// some of them.
/// </remarks>
public static class GraphEventMapper
{
    /// <summary>Shown when Graph gives no subject, which happens for private events in a shared calendar.</summary>
    private const string UntitledSubject = "(no subject)";

    /// <summary>
    /// Maps one entry, or returns null if it cannot be placed on a calendar at all.
    /// </summary>
    /// <remarks>
    /// Returns null rather than throwing. One malformed entry should cost the user that entry,
    /// not the whole month it appears in.
    /// </remarks>
    public static CalendarEvent? Map(JsonElement element, string calendarId, TimeZoneInfo displayZone)
    {
        ArgumentNullException.ThrowIfNull(calendarId);
        ArgumentNullException.ThrowIfNull(displayZone);

        var id = String(element, "id");

        if (id is null)
        {
            return null;
        }

        var isAllDay = Bool(element, "isAllDay") ?? false;

        if (!TryReadGraphDateTime(element, "start", out var start) ||
            !TryReadGraphDateTime(element, "end", out var end))
        {
            return null;
        }

        var common = new
        {
            Id = id,
            CalendarId = calendarId,
            Subject = String(element, "subject") ?? UntitledSubject,
            Location = String(element.TryGetProperty("location", out var loc) ? loc : default, "displayName"),
            Organizer = ReadOrganizer(element),
            Status = ReadStatus(element),
            Sensitivity = ReadSensitivity(element),
            Categories = ReadCategories(element),
            SeriesMasterId = String(element, "seriesMasterId"),
        };

        if (isAllDay)
        {
            // Dates only, never converted. Graph's end is exclusive, and NormalizeAllDay is
            // the single place that is turned into an inclusive span.
            var days = EventNormalizer.NormalizeAllDay(
                DateOnly.FromDateTime(start.Naive),
                DateOnly.FromDateTime(end.Naive));

            return new CalendarEvent
            {
                Id = common.Id,
                CalendarId = common.CalendarId,
                Subject = common.Subject,
                Location = common.Location,
                Organizer = common.Organizer,
                IsAllDay = true,
                Days = days,
                Status = common.Status,
                Sensitivity = common.Sensitivity,
                Categories = common.Categories,
                SeriesMasterId = common.SeriesMasterId,
            };
        }

        var normalized = EventNormalizer.NormalizeTimed(
            ToOffset(start),
            ToOffset(end),
            displayZone);

        return new CalendarEvent
        {
            Id = common.Id,
            CalendarId = common.CalendarId,
            Subject = common.Subject,
            Location = common.Location,
            Organizer = common.Organizer,
            IsAllDay = false,
            Days = normalized.Days,
            Start = normalized.Start,
            End = normalized.End,
            Status = common.Status,
            Sensitivity = common.Sensitivity,
            Categories = common.Categories,
            SeriesMasterId = common.SeriesMasterId,
        };
    }

    /// <param name="Naive">The wall-clock time Graph wrote, with no offset attached.</param>
    /// <param name="Zone">The zone Graph named alongside it.</param>
    private readonly record struct GraphDateTime(DateTime Naive, TimeZoneInfo Zone);

    private static bool TryReadGraphDateTime(JsonElement element, string property, out GraphDateTime value)
    {
        value = default;

        if (!element.TryGetProperty(property, out var node) || node.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        var text = String(node, "dateTime");

        if (text is null ||
            !DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None | DateTimeStyles.AllowWhiteSpaces,
                out var parsed))
        {
            return false;
        }

        // Unspecified, emphatically. The string carries no offset, so letting .NET treat it as
        // local would attach this machine's offset to a time that is not in this machine's
        // zone.
        parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);

        EventNormalizer.TryResolveTimeZone(String(node, "timeZone"), out var zone);

        value = new GraphDateTime(parsed, zone);
        return true;
    }

    private static DateTimeOffset ToOffset(GraphDateTime value) =>
        new(value.Naive, value.Zone.GetUtcOffset(value.Naive));

    private static EventStatus ReadStatus(JsonElement element)
    {
        // Graph spreads this across three fields, so the precedence has to be decided here.
        // A cancelled meeting is cancelled whatever else it says; a declined invitation is
        // still on the calendar but is not something the user is attending.
        if (Bool(element, "isCancelled") ?? false)
        {
            return EventStatus.Cancelled;
        }

        var response = String(element.TryGetProperty("responseStatus", out var rs) ? rs : default, "response");

        if (string.Equals(response, "declined", StringComparison.OrdinalIgnoreCase))
        {
            return EventStatus.Declined;
        }

        if (string.Equals(response, "tentativelyAccepted", StringComparison.OrdinalIgnoreCase))
        {
            return EventStatus.Tentative;
        }

        return string.Equals(String(element, "showAs"), "tentative", StringComparison.OrdinalIgnoreCase)
            ? EventStatus.Tentative
            : EventStatus.Confirmed;
    }

    private static EventSensitivity ReadSensitivity(JsonElement element) =>
        String(element, "sensitivity")?.ToLowerInvariant() switch
        {
            "personal" => EventSensitivity.Personal,
            "private" => EventSensitivity.Private,
            "confidential" => EventSensitivity.Confidential,
            _ => EventSensitivity.Normal,
        };

    private static IReadOnlyList<string> ReadCategories(JsonElement element)
    {
        if (!element.TryGetProperty("categories", out var node) || node.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        var categories = new List<string>(node.GetArrayLength());

        foreach (var item in node.EnumerateArray())
        {
            if (item.ValueKind is JsonValueKind.String && item.GetString() is { Length: > 0 } value)
            {
                categories.Add(value);
            }
        }

        return categories;
    }

    private static string? ReadOrganizer(JsonElement element) =>
        element.TryGetProperty("organizer", out var organizer) &&
        organizer.TryGetProperty("emailAddress", out var email)
            ? String(email, "name") ?? String(email, "address")
            : null;

    private static string? String(JsonElement element, string property) =>
        element.ValueKind is JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? Bool(JsonElement element, string property) =>
        element.ValueKind is JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
