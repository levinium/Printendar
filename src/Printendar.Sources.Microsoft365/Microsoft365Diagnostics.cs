using System.Text.RegularExpressions;
using Microsoft.Identity.Client;

namespace Printendar.Sources.Microsoft365;

/// <summary>What went wrong signing in, in terms of what the user should do about it.</summary>
/// <remarks>
/// The cases exist because each one needs a different action, not because the identity
/// platform distinguishes them. Anything that would lead to the same advice is folded together.
/// </remarks>
public enum SignInProblem
{
    None,

    /// <summary>The user closed the sign-in window. Not a fault.</summary>
    Cancelled,

    /// <summary>Nobody has approved Printendar yet, and the user can do it themselves.</summary>
    ConsentRequired,

    /// <summary>The organisation's policy means only an administrator can approve it.</summary>
    AdminConsentRequired,

    /// <summary>An administrator has blocked the application. Consenting will not help.</summary>
    ApplicationBlocked,

    /// <summary>This kind of account cannot be used here.</summary>
    AccountNotSupported,

    Unknown,
}

/// <param name="Problem">What kind of failure it was.</param>
/// <param name="Message">Plain language, addressed to whoever is sitting in front of the app.</param>
/// <param name="AdminConsentUrl">Where an administrator approves it, when that is the answer.</param>
/// <param name="Detail">The original text, kept for a bug report rather than for display.</param>
public sealed record SignInDiagnosis(
    SignInProblem Problem,
    string Message,
    string? AdminConsentUrl,
    string Detail);

/// <summary>
/// Turns identity platform failures into something a person can act on.
/// </summary>
/// <remarks>
/// The raw errors are written for developers. "AADSTS65001: The user or administrator has not
/// consented to use the application with ID '...'. Send an interactive authorization request
/// for this user and resource." is accurate and completely useless to someone who wanted to
/// print a calendar.
///
/// Kept separate from the network and MSAL plumbing so every case can be tested directly.
/// </remarks>
public static class Microsoft365Diagnostics
{
    private static readonly Regex AadstsCode = new(@"AADSTS\d+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Where an administrator approves Printendar for their whole organisation.
    /// </summary>
    /// <param name="clientId">The application being approved.</param>
    /// <param name="tenant">
    /// The organisation. Defaults to "organizations", which accepts any work or school
    /// account and refuses personal ones, since there is nothing for an administrator to
    /// consent to on a personal account.
    /// </param>
    /// <remarks>
    /// No redirect_uri is included on purpose. Supplying one requires it to be registered on
    /// the application and adds a listener the user has to be returned to; without it,
    /// Microsoft shows its own confirmation page, and the user comes back and signs in
    /// normally. Fewer moving parts for the same outcome.
    /// </remarks>
    public static string BuildAdminConsentUrl(string clientId, string tenant = "organizations")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var target = string.IsNullOrWhiteSpace(tenant) ? "organizations" : tenant.Trim();

        return $"https://login.microsoftonline.com/{Uri.EscapeDataString(target)}/adminconsent" +
               $"?client_id={Uri.EscapeDataString(clientId)}";
    }

    public static SignInDiagnosis Interpret(Exception exception, Microsoft365Options options)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(options);

        var detail = exception.Message;
        var code = AadstsCode.Match(detail).Value;

        // Closing the browser is a decision, not a failure, and should not leave something
        // alarming on screen.
        if (exception is MsalClientException { ErrorCode: "authentication_canceled" } or OperationCanceledException)
        {
            return new SignInDiagnosis(SignInProblem.Cancelled, "Sign-in was cancelled.", null, detail);
        }

        return code switch
        {
            // The organisation has switched off user consent for third party applications.
            // Retrying achieves nothing; an administrator has to approve it once.
            "AADSTS90094" or "AADSTS65004" => new SignInDiagnosis(
                SignInProblem.AdminConsentRequired,
                "Your organisation needs an administrator to approve Printendar once before anyone " +
                "here can use it. It only ever reads calendars.",
                BuildAdminConsentUrl(options.ClientId),
                detail),

            // Nobody has approved it yet. The user may well be able to do that themselves, so
            // offer the retry first and the administrator route as a fallback.
            "AADSTS65001" => new SignInDiagnosis(
                SignInProblem.ConsentRequired,
                "Printendar has not been approved for this account yet. Sign in again and accept the " +
                "permission request. If your organisation does not allow that, an administrator can " +
                "approve it for everyone.",
                BuildAdminConsentUrl(options.ClientId),
                detail),

            // Blocked or disabled. Sending anyone to a consent page here wastes their time and
            // an administrator's.
            "AADSTS7000112" or "AADSTS700016" or "AADSTS50105" => new SignInDiagnosis(
                SignInProblem.ApplicationBlocked,
                "An administrator in your organisation has blocked Printendar, or it has not been made " +
                "available to your account. They will need to allow it before you can sign in.",
                null,
                detail),

            "AADSTS50020" or "AADSTS500011" or "AADSTS900971" => new SignInDiagnosis(
                SignInProblem.AccountNotSupported,
                "That account cannot be used here. Try signing in with the work, school or personal " +
                "Microsoft account whose calendar you want to print.",
                null,
                detail),

            _ => new SignInDiagnosis(
                SignInProblem.Unknown,
                "Printendar could not sign in to Microsoft 365. Check the internet connection and try " +
                "again; if it keeps happening the details below are worth reporting.",
                null,
                detail),
        };
    }
}
