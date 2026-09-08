using Microsoft.Identity.Client;
using Printendar.Sources.Microsoft365;

namespace Printendar.Core.Tests.Sources;

/// <summary>
/// Turning an identity platform failure into something a person can act on.
/// </summary>
/// <remarks>
/// The raw errors are unusable by the audience this program is for. "AADSTS65001: The user or
/// administrator has not consented to use the application with ID '...' named '...'. Send an
/// interactive authorization request for this user and resource." tells someone printing a
/// calendar nothing about what to do next.
///
/// Each case here maps to a different thing the user should actually do, which is the only
/// reason to tell them apart.
/// </remarks>
public class SignInDiagnosisTests
{
    private static readonly Microsoft365Options Options = new() { ClientId = "11111111-2222-3333-4444-555555555555" };

    private static SignInDiagnosis Interpret(string errorCode, string message) =>
        Microsoft365Diagnostics.Interpret(new MsalServiceException(errorCode, message), Options);

    [Fact]
    public void Nobody_has_consented_yet_is_something_the_user_can_fix_themselves()
    {
        var diagnosis = Interpret(
            "invalid_grant",
            "AADSTS65001: The user or administrator has not consented to use the application with ID 'x'.");

        Assert.Equal(SignInProblem.ConsentRequired, diagnosis.Problem);
    }

    [Fact]
    public void A_policy_blocking_user_consent_needs_an_administrator()
    {
        // The case that matters for an organisation: the user cannot approve this themselves
        // however many times they try, so telling them to try again is useless.
        var diagnosis = Interpret(
            "consent_required",
            "AADSTS90094: The grant requires admin permission.");

        Assert.Equal(SignInProblem.AdminConsentRequired, diagnosis.Problem);
        Assert.NotNull(diagnosis.AdminConsentUrl);
    }

    [Fact]
    public void An_admin_consent_url_names_the_application_being_approved()
    {
        var diagnosis = Interpret("consent_required", "AADSTS90094: The grant requires admin permission.");

        Assert.Contains(Options.ClientId, diagnosis.AdminConsentUrl!, StringComparison.Ordinal);
        Assert.StartsWith("https://login.microsoftonline.com/", diagnosis.AdminConsentUrl, StringComparison.Ordinal);
        Assert.Contains("/adminconsent", diagnosis.AdminConsentUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void The_admin_consent_url_targets_work_accounts_rather_than_any_account()
    {
        // Admin consent only means anything in an organisation's tenant. Sending an admin to
        // the "common" endpoint invites them to sign in with a personal account, where there
        // is nothing to consent on behalf of.
        var url = Microsoft365Diagnostics.BuildAdminConsentUrl(Options.ClientId);

        Assert.Contains("/organizations/", url, StringComparison.Ordinal);
    }

    [Fact]
    public void An_administrator_can_be_pointed_at_their_own_tenant_directly()
    {
        var url = Microsoft365Diagnostics.BuildAdminConsentUrl(Options.ClientId, "contoso.onmicrosoft.com");

        Assert.Contains("/contoso.onmicrosoft.com/", url, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blocked_application_is_not_reported_as_a_consent_problem()
    {
        // Sending someone to a consent page they will be refused at wastes their time and an
        // administrator's. This one genuinely needs the administrator to unblock it first.
        var diagnosis = Interpret(
            "unauthorized_client",
            "AADSTS7000112: Application is disabled.");

        Assert.Equal(SignInProblem.ApplicationBlocked, diagnosis.Problem);
        Assert.Null(diagnosis.AdminConsentUrl);
    }

    [Fact]
    public void An_account_the_application_does_not_accept_is_named_as_such()
    {
        var diagnosis = Interpret(
            "invalid_request",
            "AADSTS50020: User account from identity provider does not exist in tenant.");

        Assert.Equal(SignInProblem.AccountNotSupported, diagnosis.Problem);
    }

    [Fact]
    public void A_cancelled_sign_in_is_not_an_error_to_report()
    {
        // Closing the browser window is a decision, not a fault, and should not leave an
        // alarming message on screen.
        var diagnosis = Microsoft365Diagnostics.Interpret(
            new MsalClientException("authentication_canceled", "User canceled authentication."),
            Options);

        Assert.Equal(SignInProblem.Cancelled, diagnosis.Problem);
    }

    [Fact]
    public void An_unrecognised_failure_still_produces_a_readable_message()
    {
        var diagnosis = Interpret("something_new", "AADSTS99999: A thing nobody has seen before.");

        Assert.Equal(SignInProblem.Unknown, diagnosis.Problem);
        Assert.False(string.IsNullOrWhiteSpace(diagnosis.Message));
    }

    [Fact]
    public void Every_diagnosis_says_what_to_do_next_rather_than_only_what_went_wrong()
    {
        // The whole point of this type. A message that only restates the failure leaves the
        // user exactly where they were.
        foreach (var (code, message) in ((string, string)[])
        [
            ("invalid_grant", "AADSTS65001: not consented"),
            ("consent_required", "AADSTS90094: requires admin permission"),
            ("unauthorized_client", "AADSTS7000112: Application is disabled"),
            ("invalid_request", "AADSTS50020: account does not exist in tenant"),
            ("something_new", "AADSTS99999: unknown"),
        ])
        {
            var diagnosis = Interpret(code, message);

            Assert.False(string.IsNullOrWhiteSpace(diagnosis.Message));
            Assert.DoesNotContain("AADSTS", diagnosis.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_technical_detail_is_kept_for_a_bug_report_without_being_shown()
    {
        var diagnosis = Interpret("consent_required", "AADSTS90094: The grant requires admin permission.");

        Assert.Contains("AADSTS90094", diagnosis.Detail, StringComparison.Ordinal);
    }
}
