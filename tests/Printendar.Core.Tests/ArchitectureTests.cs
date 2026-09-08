using System.Xml.Linq;

namespace Printendar.Core.Tests;

/// <summary>
/// Guards the claim that Printendar.Core is portable: pure logic plus Skia, usable unchanged
/// from the Avalonia app, the CLI, and a headless Linux CI container.
/// </summary>
/// <remarks>
/// These read the project file as XML on purpose.
///
/// Asserting on <c>TargetFrameworkAttribute</c> would be vacuous. It is true by construction
/// and cannot fail for any reason a reader cares about, so it reports a green check carrying
/// no information.
///
/// Reflecting over <c>Assembly.GetReferencedAssemblies()</c> is also insufficient, though it
/// looks stronger. The CLR omits references the JIT never needed, so a reference can be
/// present in the build and absent from that list, and the assertion passes vacuously again.
///
/// The project file is the thing that actually decides, so that is what gets asserted.
/// </remarks>
public class ArchitectureTests
{
    /// <summary>
    /// SkiaSharp and its per-platform native asset packages. Skia is Core's one permitted
    /// dependency because layout is computed from glyph advances and drawn to an SKCanvas.
    /// </summary>
    private static readonly string[] AllowedPackageReferences =
    [
        "SkiaSharp",
        "SkiaSharp.NativeAssets.Win32",
        "SkiaSharp.NativeAssets.macOS",
        "SkiaSharp.NativeAssets.Linux",
    ];

    private static XDocument CoreProject() =>
        XDocument.Load(RepoLayout.PathTo("src", "Printendar.Core", "Printendar.Core.csproj"));

    [Fact]
    public void Core_references_no_package_other_than_Skia()
    {
        var referenced = CoreProject()
            .Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(name => name is not null)
            .Select(name => name!)
            .ToArray();

        var unexpected = referenced.Except(AllowedPackageReferences, StringComparer.OrdinalIgnoreCase).ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Printendar.Core must depend on SkiaSharp only, but also references: {string.Join(", ", unexpected)}. " +
            "Core is consumed by the CLI and by headless Linux CI; a UI or platform package here breaks both.");
    }

    [Fact]
    public void Core_references_no_other_project()
    {
        var referenced = CoreProject()
            .Descendants("ProjectReference")
            .Select(e => (string?)e.Attribute("Include") ?? "(unnamed)")
            .ToArray();

        Assert.True(
            referenced.Length == 0,
            "Printendar.Core must sit at the bottom of the dependency graph, but references: " +
            string.Join(", ", referenced));
    }

    [Fact]
    public void Core_targets_a_framework_with_no_platform_suffix()
    {
        var targets = CoreProject()
            .Descendants("TargetFramework")
            .Concat(CoreProject().Descendants("TargetFrameworks"))
            .SelectMany(e => e.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();

        Assert.NotEmpty(targets);

        // "net10.0-windows" and friends would compile here and fail on the Linux CI leg.
        var platformSpecific = targets.Where(t => t.Contains('-')).ToArray();

        Assert.True(
            platformSpecific.Length == 0,
            $"Printendar.Core must target a platform-neutral framework, but targets: {string.Join(", ", platformSpecific)}");
    }
}
