using Printendar.Core.Settings;
namespace Printendar.Sources.Microsoft365;

/// <summary>
/// What Printendar needs in order to sign in to Microsoft 365.
/// </summary>
/// <remarks>
/// The client id is not a secret. For a public client application the identity platform
/// treats it as public by design, which is why it can sit in an open source repository and in
/// a downloadable binary without any protection.
///
/// <see cref="ClientId"/> can still be overridden, and that matters for two real cases: an
/// organisation that will not consent to a third party application but is happy to register
/// its own, and anyone who wants to run this without depending on the project's registration
/// remaining in good standing.
/// </remarks>
public sealed record Microsoft365Options
{
    /// <summary>The environment variable that overrides the built-in client id.</summary>
    public const string ClientIdEnvironmentVariable = "PRINTENDAR_MS_CLIENT_ID";

    /// <summary>
    /// Placeholder standing in for the project's own registration.
    /// </summary>
    /// <remarks>
    /// Replaced once the Entra application is registered. Kept obviously fake so that an
    /// unconfigured build fails with an explanation rather than an opaque error from the
    /// identity platform.
    /// </remarks>
    public const string UnconfiguredClientId = "00000000-0000-0000-0000-000000000000";

    public string ClientId { get; init; } = UnconfiguredClientId;

    /// <summary>
    /// "common" accepts both work or school accounts and personal Microsoft accounts.
    /// </summary>
    /// <remarks>
    /// The gap this program fills is felt by anyone whose Outlook stopped printing a month on
    /// one page, which includes people on Outlook.com, so restricting to "organizations" would
    /// shut out part of the audience for no benefit.
    /// </remarks>
    public string Tenant { get; init; } = "common";

    /// <summary>
    /// Delegated permissions requested at sign-in.
    /// </summary>
    /// <remarks>
    /// Read only, and no more than is needed to draw a calendar. Calendars.Read.Shared covers
    /// calendars someone else has shared with the user, which is most of the reason people
    /// print a month in the first place.
    ///
    /// offline_access is not listed: MSAL requests it itself, and naming it here makes the
    /// consent screen list a permission the user did not need to think about.
    /// </remarks>
    public IReadOnlyList<string> Scopes { get; init; } =
    [
        "User.Read",
        "Calendars.Read",
        "Calendars.Read.Shared",
    ];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.Equals(ClientId, UnconfiguredClientId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves which application registration to sign in with.
    /// </summary>
    /// <remarks>
    /// In order: what an administrator entered in the app, then the environment variable, then
    /// the one built in.
    ///
    /// The in-app setting comes first because it is the one a person can actually reach. The
    /// environment variable stays for scripted deployments, where setting a machine-wide
    /// variable is easier than touching a per-user file.
    /// </remarks>
    public static Microsoft365Options Resolve(AppSettings? settings = null)
    {
        if (!string.IsNullOrWhiteSpace(settings?.MicrosoftClientId))
        {
            return new Microsoft365Options
            {
                ClientId = settings.MicrosoftClientId.Trim(),
                Tenant = string.IsNullOrWhiteSpace(settings.MicrosoftTenant)
                    ? "common"
                    : settings.MicrosoftTenant.Trim(),
            };
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(ClientIdEnvironmentVariable);

        return string.IsNullOrWhiteSpace(fromEnvironment)
            ? new Microsoft365Options()
            : new Microsoft365Options { ClientId = fromEnvironment.Trim() };
    }

    /// <summary>
    /// The Entra portal page for creating a new application registration.
    /// </summary>
    /// <remarks>
    /// Offered instead of the app creating the registration itself. Doing that would need
    /// Application.ReadWrite.All, a documented privilege escalation path: anything holding it
    /// can add credentials to a privileged application and reach Global Administrator. A
    /// calendar printer asking for directory write access deserves to be refused, so the app
    /// sends the administrator to the portal and asks them to paste the id back.
    /// </remarks>
    public const string PortalNewRegistrationUrl =
        "https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/CreateApplicationBlade";
}
