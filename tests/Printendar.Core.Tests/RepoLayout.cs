namespace Printendar.Core.Tests;

/// <summary>
/// Locates files in the repository from a test run.
/// </summary>
/// <remarks>
/// Tests that inspect project files need the source tree, not the output directory. Walking
/// up from the test assembly to the solution file is deliberate: it survives Debug/Release,
/// any target framework folder, and a different checkout path, none of which a relative
/// "..\..\..\.." would.
/// </remarks>
internal static class RepoLayout
{
    private const string SolutionFileName = "Printendar.slnx";

    /// <summary>The repository root, i.e. the directory containing the solution file.</summary>
    public static DirectoryInfo Root { get; } = FindRoot();

    public static string PathTo(params string[] segments) =>
        Path.GetFullPath(Path.Combine([Root.FullName, .. segments]));

    private static DirectoryInfo FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
            {
                return dir;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find {SolutionFileName} above {AppContext.BaseDirectory}. " +
            "Tests that read the source tree cannot run from a detached output directory.");
    }
}
