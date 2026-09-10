using System.Text.Json;
using System.Text.Json.Serialization;

namespace Printendar.Core.Sources;

/// <summary>Where a calendar comes from.</summary>
[JsonConverter(typeof(CalendarSourceKindConverter))]
public enum CalendarSourceKind
{
    /// <summary>
    /// A kind this build does not recognise, from a settings file written by another version.
    /// </summary>
    /// <remarks>
    /// Explicit and zero so that an unreadable value lands here rather than silently becoming
    /// the first real member, and so that an entry landing here is dropped rather than acted
    /// on as something it is not.
    ///
    /// Earlier versions signed in to Microsoft 365 and Google directly and wrote those kinds
    /// here. Both are gone, so those entries now land on Unknown and are dropped, which is
    /// what the numbers below are for: they are pinned rather than implied, so removing a
    /// member cannot silently renumber the survivors and turn a saved feed into a file.
    /// </remarks>
    Unknown = 0,

    /// <summary>An iCalendar file on this computer.</summary>
    IcsFile = 3,

    /// <summary>A published iCalendar feed, re-read every time it is printed.</summary>
    IcsUrl = 4,
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
/// <param name="Location">The file path or the feed address. Every kind has one.</param>
/// <param name="AccountId">
/// The provider's identifier for an account, kept so that entries written by an older version
/// can still be told apart. Nothing sets it now.
/// </param>
/// <remarks>
/// Deliberately thin, and deliberately holds no credential. There is nothing to hold: a
/// published feed address is itself the only secret involved, and a test asserts the record
/// has no field whose name suggests otherwise, which makes that a property of the type rather
/// than a habit.
///
/// A published address is worth treating as a password even so. Anyone holding it can read
/// that calendar, which is why the window says so when one is added.
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

    /// <summary>
    /// The printed colour of each calendar in this source, by calendar id, as #RRGGBB.
    /// </summary>
    /// <remarks>
    /// Written down for every calendar, not only the ones somebody chose by hand. Colours used
    /// to be handed out by position at start-up, so removing an account renumbered the rest and
    /// the whole wall chart changed colour behind the user's back. Remembering the first answer
    /// makes a calendar's colour its own, and makes an explicit choice nothing more than that
    /// same value written by a different hand.
    /// </remarks>
    public IReadOnlyDictionary<string, string> CalendarColors { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Whether this entry is usable, as opposed to something a file edit produced.</summary>
    /// <remarks>
    /// The id addresses the source everywhere else, so a blank one is not a source. Every
    /// remaining kind reads from a location, so an entry without one has nowhere to look:
    /// that is what a source left behind by a version that signed in to an account looks
    /// like, and it is dropped rather than carried forward broken.
    ///
    /// Kept out of the file because it is derived from the fields above rather than chosen.
    /// Written out it would read as a switch, and someone editing the settings to disable a
    /// calendar would set it to false and see nothing happen.
    /// </remarks>
    [JsonIgnore]
    public bool IsUsable =>
        !string.IsNullOrWhiteSpace(Id) &&
        !string.IsNullOrWhiteSpace(DisplayName) &&
        Kind is not CalendarSourceKind.Unknown &&
        !string.IsNullOrWhiteSpace(Location);
}
