using Printendar.Core.Text;
using SkiaSharp;

namespace Printendar.Core.Tests.Text;

/// <summary>
/// The layout font must come from inside the assembly, not from the machine.
/// </summary>
/// <remarks>
/// Glyph advances decide where every event title wraps and therefore how tall each day cell
/// needs to be. If the font resolved differently per machine, the same month would fit on one
/// page here and spill on someone else's, and every snapshot baseline would flap in CI.
///
/// The provider owns its typefaces and hands out borrowed references, so callers never
/// dispose what they are given.
/// </remarks>
public class EmbeddedFontProviderTests
{
    [Fact]
    public void Provides_the_embedded_regular_typeface()
    {
        using var provider = new EmbeddedFontProvider();

        var typeface = provider.GetTypeface(FontWeightKind.Regular);

        Assert.Equal("Noto Sans", typeface.FamilyName);
    }

    [Fact]
    public void Provides_a_heavier_face_for_semibold()
    {
        using var provider = new EmbeddedFontProvider();

        var regular = provider.GetTypeface(FontWeightKind.Regular);
        var semiBold = provider.GetTypeface(FontWeightKind.SemiBold);

        Assert.Equal("Noto Sans", semiBold.FamilyName);
        Assert.True(
            semiBold.FontWeight > regular.FontWeight,
            $"Expected semibold ({semiBold.FontWeight}) to be heavier than regular ({regular.FontWeight}). " +
            "Day numbers and weekday headers rely on the contrast to stay readable at small sizes.");
    }

    [Fact]
    public void Does_not_fall_back_to_a_system_typeface()
    {
        // SKTypeface.FromStream returns null on a failed parse, and the tempting fix is to
        // fall back to SKTypeface.Default. That would be silent per-machine layout drift,
        // which is exactly what embedding the font prevents, so a load failure must throw.
        using var provider = new EmbeddedFontProvider();

        var embedded = provider.GetTypeface(FontWeightKind.Regular);

        Assert.NotEqual(SKTypeface.Default.FamilyName, embedded.FamilyName);
    }

    [Fact]
    public void Returns_the_same_instance_for_repeated_requests()
    {
        // Measurement calls this on every wrap, and the fit search re-wraps the whole month
        // once per rung of the scale ladder. Parsing a 400 KB font each time would dominate
        // the layout budget.
        using var provider = new EmbeddedFontProvider();

        Assert.Same(
            provider.GetTypeface(FontWeightKind.Regular),
            provider.GetTypeface(FontWeightKind.Regular));
    }
}
