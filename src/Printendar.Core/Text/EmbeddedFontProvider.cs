using System.Reflection;
using SkiaSharp;

namespace Printendar.Core.Text;

/// <summary>
/// Serves Noto Sans from resources embedded in this assembly.
/// </summary>
/// <remarks>
/// This is the default provider and the one every test uses, because identical glyph advances
/// on Windows, macOS and Linux are what make a month lay out identically everywhere and let
/// snapshot baselines be compared across CI legs.
///
/// There is no fallback to a system font. A missing or unreadable resource throws, because a
/// silent fallback would substitute a different font with different advances and produce a
/// page that is subtly wrong rather than obviously broken.
/// </remarks>
public sealed class EmbeddedFontProvider : IFontProvider
{
    private const string RegularResourceName = "Printendar.Core.Fonts.NotoSans-Regular.ttf";
    private const string SemiBoldResourceName = "Printendar.Core.Fonts.NotoSans-SemiBold.ttf";

    private readonly Dictionary<FontWeightKind, SKTypeface> _typefaces = [];
    private bool _disposed;

    public SKTypeface GetTypeface(FontWeightKind weight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_typefaces.TryGetValue(weight, out var cached))
        {
            return cached;
        }

        var loaded = Load(ResourceNameFor(weight));
        _typefaces[weight] = loaded;
        return loaded;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var typeface in _typefaces.Values)
        {
            typeface.Dispose();
        }

        _typefaces.Clear();
        _disposed = true;
    }

    private static string ResourceNameFor(FontWeightKind weight) => weight switch
    {
        FontWeightKind.Regular => RegularResourceName,
        FontWeightKind.SemiBold => SemiBoldResourceName,
        _ => throw new ArgumentOutOfRangeException(nameof(weight), weight, "No embedded font for this weight."),
    };

    private static SKTypeface Load(string resourceName)
    {
        var assembly = typeof(EmbeddedFontProvider).GetTypeInfo().Assembly;

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded font resource '{resourceName}' is missing from {assembly.GetName().Name}. " +
                $"Available resources: {string.Join(", ", assembly.GetManifestResourceNames())}");

        // SKTypeface.FromStream needs to seek, and a manifest resource stream does not always
        // support it, so copy to memory first rather than getting an intermittent null back.
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;

        return SKTypeface.FromStream(buffer)
            ?? throw new InvalidOperationException(
                $"Skia could not parse the embedded font resource '{resourceName}'. " +
                "Printendar does not fall back to a system font, because that would change " +
                "text measurement and silently alter every page layout.");
    }
}
