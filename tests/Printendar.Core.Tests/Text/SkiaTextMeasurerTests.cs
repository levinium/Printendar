using Printendar.Core.Text;

namespace Printendar.Core.Tests.Text;

/// <summary>
/// The measurer that decides whether a real page actually fits.
/// </summary>
/// <remarks>
/// The fake measurer proves the wrapping algorithm; only this one can say anything about a
/// printed sheet. These assertions are deliberately about relationships (wider text measures
/// more, a bigger size measures more) rather than exact point values, because pinning an
/// advance to three decimals would break on a font update without indicating a real defect.
/// The exact values are pinned separately, in the layout snapshots.
/// </remarks>
public class SkiaTextMeasurerTests : IDisposable
{
    private readonly EmbeddedFontProvider _fonts = new();
    private readonly SkiaTextMeasurer _measurer;

    public SkiaTextMeasurerTests() => _measurer = new SkiaTextMeasurer(_fonts);

    public void Dispose()
    {
        _measurer.Dispose();
        _fonts.Dispose();
        GC.SuppressFinalize(this);
    }

    private static FontSpec At(float sizePt) => new(FontWeightKind.Regular, sizePt);

    [Fact]
    public void Measures_a_positive_width_for_real_text()
    {
        Assert.True(_measurer.MeasureWidth(At(10f), "Board meeting") > 0f);
    }

    [Fact]
    public void Measures_nothing_for_an_empty_string()
    {
        Assert.Equal(0f, _measurer.MeasureWidth(At(10f), string.Empty), 0.001f);
    }

    [Fact]
    public void Longer_text_measures_wider()
    {
        var font = At(10f);

        Assert.True(_measurer.MeasureWidth(font, "Board meeting") > _measurer.MeasureWidth(font, "Board"));
    }

    [Fact]
    public void Width_scales_with_font_size()
    {
        // The fit search depends on this being proportional. If advances did not scale with
        // size, shrinking the font would not reliably reduce the wrapped line count and the
        // binary search over the scale ladder would be searching a non-monotone space.
        const string Text = "Quarterly budget review";

        var small = _measurer.MeasureWidth(At(6f), Text);
        var large = _measurer.MeasureWidth(At(12f), Text);

        Assert.Equal(2f, large / small, 0.02f);
    }

    [Fact]
    public void Line_height_is_positive_and_scales_with_size()
    {
        var small = _measurer.GetMetrics(At(6f)).LineHeight;
        var large = _measurer.GetMetrics(At(12f)).LineHeight;

        Assert.True(small > 0f);
        Assert.True(large > small);
    }

    [Fact]
    public void Ascent_is_negative_following_the_Skia_convention()
    {
        // Baselines are computed by adding -Ascent to a cell's top edge. A positive ascent
        // here would place every line of text above its cell.
        Assert.True(_measurer.GetMetrics(At(10f)).Ascent < 0f);
    }

    [Fact]
    public void Counts_the_characters_that_fit_a_given_width()
    {
        var font = At(10f);
        const string Text = "Reconciliation";

        var width = _measurer.MeasureWidth(font, "Recon");
        var fits = _measurer.CountCharsThatFit(font, Text, width);

        Assert.Equal(5, fits);
    }

    [Fact]
    public void Counts_zero_when_not_even_one_character_fits()
    {
        Assert.Equal(0, _measurer.CountCharsThatFit(At(10f), "Board", maxWidth: 0.1f));
    }

    [Fact]
    public void Never_reports_more_characters_than_the_text_has()
    {
        Assert.Equal(5, _measurer.CountCharsThatFit(At(10f), "Board", maxWidth: 10_000f));
    }

    [Fact]
    public void Wrapping_real_text_never_exceeds_the_width_asked_for()
    {
        // The postcondition that keeps a title inside its day cell, asserted against real
        // glyph advances rather than the fake's tidy arithmetic.
        var font = At(7f);
        const float MaxWidth = 60f;

        var wrapped = TextLayout.Wrap(
            _measurer, font, "Quarterly budget review with the finance committee", MaxWidth, maxLines: 3, lineSpacing: 1f);

        Assert.NotEmpty(wrapped.Lines);
        Assert.All(
            wrapped.Lines,
            line => Assert.True(
                _measurer.MeasureWidth(font, line) <= MaxWidth + 0.01f,
                $"Line \"{line}\" measures {_measurer.MeasureWidth(font, line):0.##} points against {MaxWidth} available."));
    }
}
