using System.Text.Json;
using System.Text.Json.Serialization;

namespace Printendar.Core.Sources;

/// <summary>Where a calendar comes from.</summary>
[JsonConverter(typeof(CalendarSourceKindConverter))]
public enum CalendarSourceKind
{
    /// <summary>
    /// A kind this build does not recognise, from a settings file written by a newer version.
    /// </summary>
    /// <remarks>
    /// Explicit and zero so that an unreadable value lands here rather than silently becoming
    /// the first real member. Without it a future "Exchange" source would come back as a
    /// Microsoft 365 account and be acted on.
    /// </remarks>
    Unknown = 0,

    /// <summary>A work, school or personal Microsoft account.</summary>
    Microsoft365,

    /// <summary>A Google account.</summary>
    Google,

    /// <summary>An iCalendar file on this computer.</summary>
    IcsFile,

    /// <summary>A published iCalendar feed, re-read each time.</summary>
    IcsUrl,
}

/// <summary>
/// Reads the kind as a name, and treats an unrecognised one as <see cref="CalendarSourceKind.Unknown"/>.
/// </summary>
/// <remarks>
/// The tolerance is the point. System.Text.Json throws on an enum value it cannot parse, and
/// because the settings file is read as a whole that would discard every other setting too: a
/// single source added by a newer version would cost the user their printer choices, their
/// margins and their other calendars. Landing on Unknown lets that one entry be dropped and
/// everything else survive.
///
/// Written as a name rather than a number so the file stays readable, since a user who opens
/// it is usually trying to fix something.
/// </remarks>
public sealed class CalendarSourceKindConverter : JsonConverter<CalendarSourceKind>
{
    public override CalendarSourceKind Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions __) =>
        reader.TokenType switch
        {
            JsonTokenType.String when Enum.TryParse<CalendarSourceKind>(reader.GetString(), ignoreCase: true, out var parsed)
                => parsed,

            // Older files, and anything hand-written as a number.
            JsonTokenType.Number when Enum.IsDefined(typeof(CalendarSourceKind), reader.GetInt32())
                => (CalendarSourceKind)reader.GetInt32(),

            _ => CalendarSourceKind.Unknown,
        };

    public override void Write(Utf8JsonWriter writer, CalendarSourceKind value, JsonSerializerOptions _) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>
/// A calendar source the user has added, as remembered between runs.
/// </summary>
/// <param name="Id">Printendar's own identifier. Addresses this source everywhere else.</param>
/// <param name="DisplayName">What the user calls it, and can rename.</param>
/// <param name="Location">A file path or feed URL. Null for the account-based kinds.</param>
/// <param name="AccountId">
/// The provider's identifier for the signed-in account, so several accounts of the same kind
/// stay apart. For Microsoft this is the MSAL home account id.
/// </param>
/// <remarks>
/// Deliberately thin, and deliberately holds no credential. Microsoft tokens live in MSAL's
/// own platform-encrypted cache (DPAPI, Keychain, libsecret) and only the account identifier is
/// written here, so this file is never worth stealing. A test asserts the record has no field
/// whose name suggests otherwise, which is what makes that a property of the type rather than
/// a habit.
/// </remarks>
public sealed record ConfiguredSource(
    string Id,
    CalendarSourceKind Kind,
    string DisplayName,
    string? Location = null,
    string? AccountId = null)
{
    /// <summary>
    /// The calendars inside this source that are being printed.
    /// </summary>
    /// <remarks>
    /// Remembered because otherwise every restart falls back to the account's default calendar
    /// and silently drops the others the user had chosen, which reads as data loss.
    /// </remarks>
    public IReadOnlyList<string> SelectedCalendarIds { get; init; } = [];

    /// <summary>Whether this entry is usable, as opposed to something a file edit produced.</summary>
    /// <remarks>
    /// The id addresses the source everywhere else, so a blank one is not a source. Location is
    /// required for the kinds that have nowhere else to look.
    /// </remarks>
    public bool IsUsable =>
        !string.IsNullOrWhiteSpace(Id) &&
        !string.IsNullOrWhiteSpace(DisplayName) &&
        Kind is not CalendarSourceKind.Unknown &&
        (Kind is not (CalendarSourceKind.IcsFile or CalendarSourceKind.IcsUrl) ||
         !string.IsNullOrWhiteSpace(Location));
}
