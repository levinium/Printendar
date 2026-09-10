using Printendar.Core;

namespace Printendar.Core.Tests;

/// <summary>
/// Turning the assembly's version into something worth showing somebody.
/// </summary>
/// <remarks>
/// The version matters most in a bug report: the first question anybody asks is which build,
/// and "the one I downloaded" is the usual answer. Putting it on screen is only useful if what
/// is on screen is what a person would actually type back.
/// </remarks>
public class AppVersionTests
{
    [Fact]
    public void The_build_identifier_is_dropped()
    {
        // The SDK appends "+<commit sha>" whenever source linking is on. Nobody reads a sha
        // off a title bar, and it turns a six-character answer into a fifty-character one.
        Assert.Equal("0.3.0", AppVersion.Describe("0.3.0+3f2a91c8bd4e7a105c6"));
    }

    [Fact]
    public void A_plain_version_is_left_alone()
    {
        Assert.Equal("0.3.0", AppVersion.Describe("0.3.0"));
    }

    [Fact]
    public void A_prerelease_label_is_kept()
    {
        // "0.4.0" and "0.4.0-beta.2" are different builds and the difference is the whole
        // point of the label, so only the metadata after "+" goes.
        Assert.Equal("0.4.0-beta.2", AppVersion.Describe("0.4.0-beta.2+abc123"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+onlymetadata")]
    public void Nothing_worth_showing_reads_back_as_nothing(string? raw)
    {
        // Null rather than "unknown" or "0.0.0". The caller hides the label, which is better
        // than a version that says nothing sitting where a version should be.
        Assert.Null(AppVersion.Describe(raw));
    }

    [Fact]
    public void The_running_build_has_a_version()
    {
        // Guards the wiring, not the parsing: the attribute has to actually be emitted. A
        // version that reads fine in a unit test and is absent in the shipped executable is
        // the failure this is here to catch.
        Assert.False(string.IsNullOrWhiteSpace(AppVersion.Current));
    }
}
