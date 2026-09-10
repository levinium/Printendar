using System.Reflection;

namespace Printendar.Core;

/// <summary>
/// Which build this is, in a form somebody would type back into a bug report.
/// </summary>
/// <remarks>
/// Read from the assembly rather than written down anywhere, so there is one number and it is
/// the one the build produced. A version constant in source is a version that is right until
/// somebody forgets to change it, and then it is wrong in the least detectable way.
/// </remarks>
public static class AppVersion
{
    /// <summary>The running build's version, or null if it was compiled without one.</summary>
    public static string? Current { get; } = Describe(
        typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion);

    /// <summary>
    /// Trims an informational version down to the part worth showing.
    /// </summary>
    /// <remarks>
    /// The SDK appends "+&lt;commit sha&gt;" whenever source linking is on, which turns a
    /// six-character answer into a fifty-character one that nobody reads off a window. A
    /// prerelease label is kept, because "0.4.0" and "0.4.0-beta.2" are different builds and
    /// telling them apart is the entire purpose of the label.
    /// </remarks>
    public static string? Describe(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return null;
        }

        var trimmed = informationalVersion.Trim();
        var plus = trimmed.IndexOf('+');

        if (plus >= 0)
        {
            trimmed = trimmed[..plus];
        }

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
