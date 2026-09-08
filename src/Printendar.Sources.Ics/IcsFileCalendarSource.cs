namespace Printendar.Sources.Ics;

/// <summary>
/// A calendar read from an .ics file on this computer.
/// </summary>
/// <remarks>
/// The route that needs nothing: no account, no administrator approval, no network. Export a
/// calendar from Outlook, Google, Apple Calendar or anything else that speaks iCalendar, open
/// the file, print it. For a public tool this is the only path a stranger can use the moment
/// they download it, and the fallback for anyone whose organisation will not approve a third
/// party application.
/// </remarks>
public sealed class IcsFileCalendarSource(string sourceId, string displayName, string path)
    : IcsSourceBase(sourceId, displayName)
{
    public string Path { get; } = path;

    public override string ProviderName => "Calendar file";

    /// <summary>
    /// Checks a file is worth adding, and returns the name to show for it.
    /// </summary>
    /// <remarks>
    /// Separate from reading so the user is told at the moment they pick the file, rather than
    /// when they next print. An .ics that is missing or absurdly large is a mistake worth
    /// catching while they still remember which file they meant.
    /// </remarks>
    public static string Validate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var info = new FileInfo(path);

        if (!info.Exists)
        {
            throw new InvalidOperationException($"There is no file at {path}.");
        }

        if (info.Length > MaxBytes)
        {
            throw new InvalidOperationException(
                $"That file is {info.Length / (1024 * 1024)} MB, which is far larger than a calendar " +
                "should be. Check it is the .ics file you meant to open.");
        }

        return System.IO.Path.GetFileNameWithoutExtension(info.Name);
    }

    protected override async Task<string> ReadContentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(Path))
        {
            // The usual way this source breaks: the file was moved or the drive is not
            // connected. Saying which file beats an IOException the user cannot place.
            throw new InvalidOperationException(
                $"{System.IO.Path.GetFileName(Path)} is no longer at {System.IO.Path.GetDirectoryName(Path)}.");
        }

        return await File.ReadAllTextAsync(Path, cancellationToken).ConfigureAwait(false);
    }
}
