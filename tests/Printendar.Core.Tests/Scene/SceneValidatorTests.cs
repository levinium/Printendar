using Printendar.Core.Paper;
using Printendar.Core.Scene;
using Printendar.Core.Text;
using Printendar.Core.Tests.Text;
using SkiaSharp;

namespace Printendar.Core.Tests.Scene;

/// <summary>
/// The validator turns "it fits on one page" from a hope into a checked postcondition.
/// </summary>
/// <remarks>
/// Every layout engine runs this before returning, and every layout test runs it again. It is
/// the one place that knows what a well-formed page looks like, so the rules live here rather
/// than being re-stated by each style.
/// </remarks>
public class SceneValidatorTests
{
    private static readonly FixedAdvanceTextMeasurer Measurer = new();
    private static readonly PageSpec Page =
        new(PaperSizes.Letter, Orientation.Landscape, Margins.FromInches(0.4f));

    private static ScenePage PageWith(params SceneNode[] children) =>
        new(Page, new SceneGroup("root", children, Clip: null), LayoutDiagnostics.Empty);

    private static IReadOnlyList<SceneViolation> Validate(params SceneNode[] children) =>
        SceneValidator.Validate(PageWith(children), Measurer);

    [Fact]
    public void An_empty_page_is_valid()
    {
        Assert.Empty(Validate());
    }

    [Fact]
    public void A_rectangle_inside_the_page_is_valid()
    {
        Assert.Empty(Validate(new SceneRect(new SKRect(10f, 10f, 100f, 100f), SKColors.White, null, 0f)));
    }

    [Fact]
    public void A_rectangle_hanging_off_the_page_is_a_violation()
    {
        // The failure this project exists to prevent, in its simplest form: content that the
        // printer will silently drop or push onto a second sheet.
        var violations = Validate(new SceneRect(new SKRect(700f, 10f, 900f, 100f), SKColors.White, null, 0f));

        var violation = Assert.Single(violations);
        Assert.Equal(SceneViolationKind.OutsidePage, violation.Kind);
    }

    [Fact]
    public void A_text_run_wider_than_it_promised_is_a_violation()
    {
        // Layout declares the width it wrapped to. If the emitted string does not actually fit
        // that width, the title will spill across the day cell's right-hand rule.
        var run = new SceneTextRun(
            Text: "Quarterly budget review",
            Baseline: new SKPoint(50f, 50f),
            Font: new FontSpec(FontWeightKind.Regular, 10f),
            Color: SKColors.Black,
            Anchor: TextAnchor.Left,
            MaxWidth: 20f);

        var violations = Validate(run);

        Assert.Contains(violations, v => v.Kind == SceneViolationKind.TextExceedsMaxWidth);
    }

    [Fact]
    public void A_text_run_within_its_declared_width_is_valid()
    {
        var run = new SceneTextRun(
            Text: "Board",
            Baseline: new SKPoint(50f, 50f),
            Font: new FontSpec(FontWeightKind.Regular, 10f),
            Color: SKColors.Black,
            Anchor: TextAnchor.Left,
            MaxWidth: 40f);

        Assert.Empty(Validate(run));
    }

    [Fact]
    public void A_hairline_stroke_is_a_violation()
    {
        // A zero width stroke means one device pixel, which is 1/96 inch on screen and 1/300
        // inch on the printer. The same page would then have visibly different rules in the
        // preview and in the output.
        var violations = Validate(new SceneLine(new SKPoint(10f, 10f), new SKPoint(100f, 10f), SKColors.Black, 0f));

        Assert.Contains(violations, v => v.Kind == SceneViolationKind.HairlineStroke);
    }

    [Fact]
    public void A_coordinate_that_is_not_a_number_is_a_violation()
    {
        // A division by a zero row count or column weight produces NaN, which Skia draws as
        // nothing at all. Silence is the worst possible failure mode here.
        var violations = Validate(new SceneRect(new SKRect(10f, float.NaN, 100f, 100f), SKColors.White, null, 0f));

        Assert.Contains(violations, v => v.Kind == SceneViolationKind.NotANumber);
    }

    [Fact]
    public void Nodes_nested_in_groups_are_checked_too()
    {
        var page = new ScenePage(
            Page,
            new SceneGroup("root",
            [
                new SceneGroup("week-row-0",
                [
                    new SceneRect(new SKRect(-50f, 10f, 100f, 100f), SKColors.White, null, 0f),
                ], Clip: null),
            ], Clip: null),
            LayoutDiagnostics.Empty);

        Assert.Contains(SceneValidator.Validate(page, Measurer), v => v.Kind == SceneViolationKind.OutsidePage);
    }

    [Fact]
    public void A_violation_names_the_group_it_was_found_in()
    {
        // Without the path, a failing invariant test on a 500-node page says only that
        // something escaped, which is close to useless when it fires in CI.
        var page = new ScenePage(
            Page,
            new SceneGroup("root",
            [
                new SceneGroup("day-2026-03-17", [
                    new SceneRect(new SKRect(-50f, 10f, 100f, 100f), SKColors.White, null, 0f),
                ], Clip: null),
            ], Clip: null),
            LayoutDiagnostics.Empty);

        var violation = Assert.Single(SceneValidator.Validate(page, Measurer));

        Assert.Contains("day-2026-03-17", violation.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Throwing_variant_reports_every_violation_at_once()
    {
        var page = PageWith(
            new SceneRect(new SKRect(700f, 10f, 900f, 100f), SKColors.White, null, 0f),
            new SceneLine(new SKPoint(10f, 10f), new SKPoint(100f, 10f), SKColors.Black, 0f));

        var error = Assert.Throws<InvalidOperationException>(() => SceneValidator.ThrowIfInvalid(page, Measurer));

        Assert.Contains("OutsidePage", error.Message, StringComparison.Ordinal);
        Assert.Contains("HairlineStroke", error.Message, StringComparison.Ordinal);
    }
}
