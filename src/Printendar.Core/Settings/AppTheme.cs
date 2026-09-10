using System.Text.Json;
using System.Text.Json.Serialization;

namespace Printendar.Core.Settings;

/// <summary>Which colour scheme the window uses.</summary>
[JsonConverter(typeof(AppThemeConverter))]
public enum AppTheme
{
    /// <summary>
    /// Whatever the computer is set to, and what happens without a choice.
    /// </summary>
    /// <remarks>
    /// Explicit and zero so an unreadable value lands here. A program that decides on its own
    /// to be the only bright window on a dark desktop has made a choice that was not its to
    /// make; the other two exist for the people the system answer is wrong for.
    /// </remarks>
    System = 0,

    Light,

    Dark,
}

/// <summary>
/// Reads the theme as a name, and treats an unrecognised one as <see cref="AppTheme.System"/>.
/// </summary>
/// <remarks>
/// The tolerance matters more than it sounds. System.Text.Json throws on an enum value it
/// cannot parse, and the settings file is read as a whole, so a colour scheme added by a newer
/// version, or a typo in a hand-edited file, would cost somebody every calendar they had added.
/// Written as a name so the file stays readable by whoever opens it to fix something.
/// </remarks>
public sealed class AppThemeConverter : JsonConverter<AppTheme>
{
    public override AppTheme Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions __) =>
        reader.TokenType switch
        {
            JsonTokenType.String when Enum.TryParse<AppTheme>(reader.GetString(), ignoreCase: true, out var parsed)
                => parsed,

            JsonTokenType.Number when Enum.IsDefined(typeof(AppTheme), reader.GetInt32())
                => (AppTheme)reader.GetInt32(),

            _ => AppTheme.System,
        };

    public override void Write(Utf8JsonWriter writer, AppTheme value, JsonSerializerOptions _) =>
        writer.WriteStringValue(value.ToString());
}
