using System.Text.Json;
using Printendar.Core.Sources;
using System.Text.Json.Serialization;

namespace Printendar.Core.Settings;

/// <summary>What Printendar remembers between runs.</summary>
/// <param name="MicrosoftClientId">
/// An organisation's own Entra application id, when they would rather not consent to
/// Printendar's. Not a secret: a public client application's id is public by design.
/// </param>
/// <param name="MicrosoftTenant">
/// The organisation to sign in to, when it should not be "common". Rarely needed.
/// </param>
public sealed record AppSettings
{
    public string? MicrosoftClientId { get; init; }

    public string? MicrosoftTenant { get; init; }

    /// <summary>A Google OAuth client id, for the same bring-your-own reason as Microsoft's.</summary>
    public string? GoogleClientId { get; init; }

    /// <summary>The calendars the user has added.</summary>
    public IReadOnlyList<ConfiguredSource> Sources { get; init; } = [];
}

/// <summary>Where the settings file lives.</summary>
/// <remarks>
/// An interface so tests write to a temporary directory instead of the real per-user folder.
/// </remarks>
public interface ISettingsLocation
{
    string Directory { get; }
}

/// <summary>The per-user location for the platform being run on.</summary>
public sealed class DesktopSettingsLocation : ISettingsLocation
{
    /// <remarks>
    /// ApplicationData resolves per platform without any conditional code:
    /// %APPDATA% on Windows, ~/.config on Linux, ~/.config on macOS under .NET.
    /// </remarks>
    public string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Printendar");
}

/// <summary>
/// Reads and writes the settings file.
/// </summary>
/// <remarks>
/// Neither loading nor saving ever throws at the caller. This is on the path that opens the
/// window and on the path that closes a settings panel; a read-only profile, a locked file or
/// a half-written one must cost the user a preference, never the application.
/// </remarks>
public sealed class SettingsStore(ISettingsLocation location)
{
    public const string FileName = "settings.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Path => System.IO.Path.Combine(location.Directory, FileName);

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return new AppSettings();
            }

            return Normalize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), Json)
                ?? new AppSettings());
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Moved aside, not deleted. The user may have hand-edited it and made a typo, and
            // keeping the broken copy is the difference between recovering their work and
            // silently destroying it.
            QuarantineBrokenFile();
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            System.IO.Directory.CreateDirectory(location.Directory);
            File.WriteAllText(Path, JsonSerializer.Serialize(Normalize(settings), Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Nothing the user can act on, and nothing worth taking the window down for.
        }
    }

    /// <summary>Treats blank text as absent, so a cleared box reads back as "not configured".</summary>
    /// <remarks>
    /// Also drops sources that cannot be used. A settings file written by a newer version can
    /// name a source kind this build has never heard of, which deserializes to the enum's
    /// default; letting that through would make an unknown future calendar masquerade as a
    /// Microsoft account. Dropping it means an older build still starts.
    /// </remarks>
    private static AppSettings Normalize(AppSettings settings) => settings with
    {
        MicrosoftClientId = Trimmed(settings.MicrosoftClientId),
        MicrosoftTenant = Trimmed(settings.MicrosoftTenant),
        GoogleClientId = Trimmed(settings.GoogleClientId),
        Sources = [.. (settings.Sources ?? []).Where(s => s is { IsUsable: true })],
    };

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void QuarantineBrokenFile()
    {
        try
        {
            if (File.Exists(Path))
            {
                File.Move(Path, $"{Path}.broken-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
