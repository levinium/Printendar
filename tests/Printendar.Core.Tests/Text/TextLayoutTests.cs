using Printendar.Core.Text;

namespace Printendar.Core.Tests.Text;

/// <summary>
/// Greedy word wrap with a hard line cap and an ellipsis.
/// </summary>
/// <remarks>
/// This is the bottom of the fit search. The search shrinks a scale until every day cell's
/// stacked chips fit, and each chip's height comes from how many lines its title wrapped to.
/// If wrapping is wrong, the fit is wrong, and the page is wrong.
///
/// The measurer here charges half the font size per character, so at 10 point a character is
/// 5 points wide and the expected breaks below are arithmetic rather than guesses.
/// </remarks>
public class TextLayoutTests
{
    private static readonly FixedAdvanceTextMeasurer Measurer = new();
    private static readonly FontSpec Font = new(FontWeightKind.Regular, 10f);
    private const float Tolerance = 0.01f;

    private static WrappedText Wrap(string text, float maxWidth, int maxLines = 4) =>
        TextLayout.Wrap(Measurer, Font, text, maxWidth, maxLines, lineSpacing: 0f);

    [Fact]
    public void Text_that_fits_stays_on_one_line()
    {
        var wrapped = Wrap("Board", maxWidth: 40f);

        Assert.Equal(["Board"], wrapped.Lines);
        Assert.False(wrapped.Truncated);
    }

    [Fact]
    public void Breaks_at_a_space_rather_than_mid_word()
    {
        // "Board meeting" is 13 characters, so 65 points. At 40 points "Board" (25) fits and
        // the whole string does not.
        var wrapped = Wrap("Board meeting", maxWidth: 40f);

        Assert.Equal(["Board", "meeting"], wrapped.Lines);
        Assert.False(wrapped.Truncated);
    }

    [Fact]
    public void Packs_as_many_words_per_line_as_will_fit()
    {
        var wrapped = Wrap("All hands on deck", maxWidth: 45f);

        Assert.Equal(["All hands", "on deck"], wrapped.Lines);
    }

    [Fact]
    public void Collapses_runs_of_whitespace()
    {
        // Calendar subjects arrive with stray tabs and double spaces from every source. A run
        // of whitespace that consumed real width would push a title onto an extra line and
        // shrink the whole page for nothing.
        var wrapped = Wrap("Board    meeting", maxWidth: 200f);

        Assert.Equal(["Board meeting"], wrapped.Lines);
    }

    [Fact]
    public void Hard_breaks_a_single_word_too_long_for_the_line()
    {
        // A 12 character word is 60 points and cannot be broken at a space. At 25 points, 5
        // characters fit per line.
        var wrapped = Wrap("Reconciliat", maxWidth: 25f, maxLines: 3);

        Assert.Equal(["Recon", "cilia", "t"], wrapped.Lines);
        Assert.False(wrapped.Truncated);
    }

    [Fact]
    public void Ellipsizes_the_last_line_when_the_line_cap_is_reached()
    {
        var wrapped = Wrap("All hands on deck today", maxWidth: 45f, maxLines: 2);

        Assert.Equal(2, wrapped.Lines.Count);
        Assert.True(wrapped.Truncated);
        Assert.EndsWith("…", wrapped.Lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Every_returned_line_fits_the_available_width()
    {
        // The postcondition the renderer and the scene validator both rely on. An ellipsis
        // that pushed the last line over the limit would overflow the day cell it sits in.
        const float MaxWidth = 45f;

        var wrapped = Wrap("Quarterly budget review with finance", MaxWidth, maxLines: 3);

        Assert.All(
            wrapped.Lines,
            line => Assert.True(
                Measurer.MeasureWidth(Font, line) <= MaxWidth + Tolerance,
                $"Line \"{line}\" measures {Measurer.MeasureWidth(Font, line)} points, over the {MaxWidth} available."));
    }

    [Fact]
    public void Reports_the_height_of_the_lines_it_produced()
    {
        var wrapped = Wrap("Board meeting", maxWidth: 40f);

        // Two lines at 1.2 times a 10 point font, with no extra line spacing asked for.
        Assert.Equal(24f, wrapped.Height, Tolerance);
    }

    [Fact]
    public void Line_spacing_is_added_between_lines_but_not_after_the_last()
    {
        var wrapped = TextLayout.Wrap(Measurer, Font, "Board meeting", maxWidth: 40f, maxLines: 4, lineSpacing: 3f);

        Assert.Equal(27f, wrapped.Height, Tolerance);
    }

    [Fact]
    public void Empty_text_produces_no_lines_and_no_height()
    {
        var wrapped = Wrap("   ", maxWidth: 40f);

        Assert.Empty(wrapped.Lines);
        Assert.Equal(0f, wrapped.Height, Tolerance);
        Assert.False(wrapped.Truncated);
    }

    [Fact]
    public void A_width_too_small_for_even_one_character_does_not_loop_forever()
    {
        // Reachable through the fit search at its smallest scale on a compressed weekend
        // column. It must terminate, and it must not claim to have rendered the text.
        var wrapped = Wrap("Board meeting", maxWidth: 1f, maxLines: 3);

        Assert.True(wrapped.Truncated);
        Assert.True(wrapped.Lines.Count <= 3);
    }
}
