using Printendar.Core.Sources;
using SkiaSharp;

namespace Printendar.Core.Tests.Sources;

/// <summary>
/// Telling somebody a colour they picked will not work, before they print with it.
/// </summary>
/// <remarks>
/// Both of these are only ever discovered after the paper comes out, which is why they are
/// worth saying in advance: a colour too pale to see on white, and one so close to another
/// calendar's that the legend stops distinguishing anything. Neither is refused. Somebody
/// matching a house style knows their own reasons, and a tool that argues gets worked around.
/// </remarks>
public class ColorAdviceTests
{
    private static readonly SKColor StrongBlue = new(0x1F, 0x77, 0xB4);
    private static readonly SKColor StrongRed = new(0xD6, 0x27, 0x28);

    [Fact]
    public void A_strong_colour_nobody_else_is_using_draws_no_comment()
    {
        Assert.Null(ColorAdvice.Describe(StrongBlue, [new NamedColor("Work", StrongRed)]));
    }

    [Fact]
    public void Nothing_to_compare_against_is_never_a_clash()
    {
        Assert.Null(ColorAdvice.Describe(StrongBlue, []));
    }

    [Fact]
    public void A_very_pale_colour_is_warned_about()
    {
        // The failure this catches: a calendar that prints, technically, and cannot be seen.
        var advice = ColorAdvice.Describe(new SKColor(0xEE, 0xEE, 0xEE), []);

        Assert.NotNull(advice);
        Assert.Contains("pale", advice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_colour_close_to_another_calendar_says_which_one()
    {
        // "That is close to something" would leave the user to work out what, across a list of
        // calendars they cannot see from the picker.
        var almostRed = new SKColor(0xD2, 0x2E, 0x2E);

        var advice = ColorAdvice.Describe(almostRed, [new NamedColor("Work", StrongRed)]);

        Assert.NotNull(advice);
        Assert.Contains("Work", advice, StringComparison.Ordinal);
    }

    [Fact]
    public void Exactly_another_calendars_colour_is_caught()
    {
        var advice = ColorAdvice.Describe(StrongRed, [new NamedColor("Work", StrongRed)]);

        Assert.NotNull(advice);
        Assert.Contains("Work", advice, StringComparison.Ordinal);
    }

    [Fact]
    public void Being_unseeable_is_said_before_being_similar()
    {
        // A pale colour close to another pale one is both. Which is mentioned matters: one of
        // them means the events will not be visible at all.
        var paleGrey = new SKColor(0xEE, 0xEE, 0xEE);

        var advice = ColorAdvice.Describe(paleGrey, [new NamedColor("Work", new SKColor(0xEC, 0xEC, 0xEC))]);

        Assert.Contains("pale", advice!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Green_and_red_of_the_same_lightness_still_count_as_different()
    {
        // Guards against comparing lightness alone, which would call every mid-tone a clash and
        // make the warning noise people learn to ignore.
        Assert.Null(ColorAdvice.Describe(
            new SKColor(0x2C, 0xA0, 0x2C),
            [new NamedColor("Work", new SKColor(0xA0, 0x2C, 0x2C))]));
    }
}
