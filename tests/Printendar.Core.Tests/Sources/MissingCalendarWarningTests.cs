using Printendar.Core.Sources;

namespace Printendar.Core.Tests.Sources;

/// <summary>
/// Telling somebody their page is missing a calendar, before they print it.
/// </summary>
/// <remarks>
/// The risk this exists for: a printed month that looks complete and is not. An account that
/// signed out weeks ago contributes nothing, the grid draws perfectly, and the sheet goes on a
/// wall missing half the meetings on it. Nothing about the page itself would ever reveal that,
/// so it has to be said out loud on the way to the printer.
/// </remarks>
public class MissingCalendarWarningTests
{
    private static SourceStatus Ready(string name) =>
        new(name, name, SourceAvailability.Ready, null);

    private static SourceStatus SignedOut(string name) =>
        new(name, name, SourceAvailability.NotSignedIn, "Signed out.");

    private static SourceStatus Failed(string name, string why) =>
        new(name, name, SourceAvailability.Failed, why);

    [Fact]
    public void Nothing_wrong_means_nothing_said()
    {
        Assert.Null(MissingCalendarWarning.Describe([Ready("Work"), Ready("Home")]));
    }

    [Fact]
    public void No_calendars_at_all_is_not_a_warning()
    {
        // The sample month is on screen and the user has added nothing. There is no missing
        // data, only an empty programme.
        Assert.Null(MissingCalendarWarning.Describe([]));
    }

    [Fact]
    public void A_signed_out_account_is_named()
    {
        var warning = MissingCalendarWarning.Describe([Ready("Home"), SignedOut("Work")]);

        Assert.NotNull(warning);
        Assert.Contains("Work", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void The_warning_says_the_page_is_incomplete_rather_than_only_that_something_failed()
    {
        // "Work could not be read" is a status. "This page is missing events from Work" is the
        // consequence, and the consequence is what stops somebody printing it anyway.
        var warning = MissingCalendarWarning.Describe([SignedOut("Work")]);

        Assert.Contains("missing", warning!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_broken_calendar_is_named_not_just_the_first()
    {
        // Fixing the one you were told about and printing again, still missing the other, is
        // exactly the trap this is meant to close.
        var warning = MissingCalendarWarning.Describe(
        [
            SignedOut("Work"),
            Failed("Team feed", "offline"),
            Ready("Home"),
        ]);

        Assert.Contains("Work", warning!, StringComparison.Ordinal);
        Assert.Contains("Team feed", warning!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reason_is_carried_through_for_a_single_calendar()
    {
        // With one thing wrong there is room to say why, which is the difference between the
        // user knowing to sign in and knowing only that something is broken.
        var warning = MissingCalendarWarning.Describe([Failed("Team feed", "the feed is offline")]);

        Assert.Contains("the feed is offline", warning!, StringComparison.Ordinal);
    }

    [Fact]
    public void It_says_what_to_do_about_it()
    {
        var warning = MissingCalendarWarning.Describe([SignedOut("Work")]);

        Assert.Contains("sign in", warning!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remove", warning!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(SourceAvailability.NotSignedIn)]
    [InlineData(SourceAvailability.Failed)]
    public void Anything_not_ready_counts_as_missing(SourceAvailability availability)
    {
        var status = new SourceStatus("s", "Work", availability, null);

        Assert.True(status.IsMissingFromPage);
    }

    [Fact]
    public void A_ready_calendar_is_not_missing()
    {
        Assert.False(Ready("Work").IsMissingFromPage);
    }
}
