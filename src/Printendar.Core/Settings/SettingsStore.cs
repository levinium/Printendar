using System.Text.Json;
using Printendar.Core.Sources;
using System.Text.Json.Serialization;

namespace Printendar.Core.Settings;

/// <summary>What Printendar remembers between runs.</summary>
/// <remarks>
/// Only the calendars. There is nothing else to keep: Printendar signs in to nothing, so it
/// holds no application id, no tenant and no token, and this file is never worth stealing.
/// </remarks>
public sealed record AppSettings
{
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

    /// <summary>Drops sources this build cannot read, and keeps everything else.</summary>
    /// <remarks>
    /// A settings file can name a source kind this build has never heard of: one written by a
    /// newer version, or by an older one back when Printendar signed in to Microsoft 365 and
    /// Google accounts directly. Either deserializes to the enum's default, and letting that
    /// through would make it masquerade as a calendar that can be read.
    ///
    /// Dropping the entry rather than refusing the file is the whole point. Someone who used
    /// an account keeps the feeds, names and colours they also had.
    /// </remarks>
    private static AppSettings Normalize(AppSettings settings) => settings with
    {
        Sources = [.. (settings.Sources ?? []).Where(s => s is { IsUsable: true })],
    };

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
