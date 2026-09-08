using Printendar.Core.Settings;
using Printendar.Sources.Microsoft365;

namespace Printendar.Core.Tests.Sources;

/// <summary>
/// Which application registration Printendar signs in with.
/// </summary>
/// <remarks>
/// The order matters and is not arbitrary. What an administrator entered in the app wins,
/// because it is the setting a person can actually reach and change. The environment variable
/// is next, for scripted deployment. The built-in registration is the fallback.
/// </remarks>
public class Microsoft365OptionsTests : IDisposable
{
    private const string EnvVar = Microsoft365Options.ClientIdEnvironmentVariable;
    private readonly string? _original = Environment.GetEnvironmentVariable(EnvVar);

    public void Dispose() => Environment.SetEnvironmentVariable(EnvVar, _original);

    [Fact]
    public void With_nothing_configured_it_is_not_configured()
    {
        Environment.SetEnvironmentVariable(EnvVar, null);

        var options = Microsoft365Options.Resolve(new AppSettings());

        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void An_id_entered_in_the_app_is_used()
    {
        Environment.SetEnvironmentVariable(EnvVar, null);

        var options = Microsoft365Options.Resolve(
            new AppSettings { MicrosoftClientId = "11111111-2222-3333-4444-555555555555" });

        Assert.True(options.IsConfigured);
        Assert.Equal("11111111-2222-3333-4444-555555555555", options.ClientId);
    }

    [Fact]
    public void What_the_administrator_entered_beats_the_environment_variable()
    {
        // The setting somebody can see and change in the window has to win, or an old machine
        // wide variable silently overrides what they just typed and they cannot tell why.
        Environment.SetEnvironmentVariable(EnvVar, "environment-id");

        var options = Microsoft365Options.Resolve(new AppSettings { MicrosoftClientId = "typed-in-app" });

        Assert.Equal("typed-in-app", options.ClientId);
    }

    [Fact]
    public void The_environment_variable_is_used_when_nothing_was_entered()
    {
        Environment.SetEnvironmentVariable(EnvVar, "environment-id");

        var options = Microsoft365Options.Resolve(new AppSettings());

        Assert.Equal("environment-id", options.ClientId);
    }

    [Fact]
    public void Clearing_the_box_falls_back_rather_than_configuring_an_empty_id()
    {
        Environment.SetEnvironmentVariable(EnvVar, null);

        var options = Microsoft365Options.Resolve(new AppSettings { MicrosoftClientId = "   " });

        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void Surrounding_spaces_are_trimmed_from_a_pasted_id()
    {
        // Copying a GUID out of the Entra portal very often brings whitespace with it.
        Environment.SetEnvironmentVariable(EnvVar, null);

        var options = Microsoft365Options.Resolve(
            new AppSettings { MicrosoftClientId = "  11111111-2222-3333-4444-555555555555  " });

        Assert.Equal("11111111-2222-3333-4444-555555555555", options.ClientId);
    }

    [Fact]
    public void Without_a_tenant_it_accepts_work_school_and_personal_accounts()
    {
        Environment.SetEnvironmentVariable(EnvVar, null);

        var options = Microsoft365Options.Resolve(new AppSettings { MicrosoftClientId = "abc" });

        Assert.Equal("common", options.Tenant);
    }

    [Fact]
    public void An_organisation_can_pin_sign_in_to_its_own_tenant()
    {
        Environment.SetEnvironmentVariable(EnvVar, null);

        var options = Microsoft365Options.Resolve(new AppSettings
        {
            MicrosoftClientId = "abc",
            MicrosoftTenant = "contoso.onmicrosoft.com",
        });

        Assert.Equal("contoso.onmicrosoft.com", options.Tenant);
    }

    [Fact]
    public void The_permissions_asked_for_stay_read_only()
    {
        // A calendar printer must never quietly start asking for write access. This is the
        // test that fails if somebody adds a scope without thinking about it.
        var options = Microsoft365Options.Resolve(new AppSettings());

        Assert.Equal(["User.Read", "Calendars.Read", "Calendars.Read.Shared"], options.Scopes);
        Assert.DoesNotContain(options.Scopes, s => s.Contains("Write", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(options.Scopes, s => s.Contains("Application.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(options.Scopes, s => s.Contains("Directory.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_portal_link_points_at_creating_an_app_registration()
    {
        Assert.StartsWith("https://entra.microsoft.com/", Microsoft365Options.PortalNewRegistrationUrl, StringComparison.Ordinal);
        Assert.Contains("RegisteredApps", Microsoft365Options.PortalNewRegistrationUrl, StringComparison.Ordinal);
    }
}
