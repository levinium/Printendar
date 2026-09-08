using SkiaSharp;

namespace Printendar.Core.Text;

/// <summary>
/// Supplies the typefaces layout measures and the renderer draws with.
/// </summary>
/// <remarks>
/// An interface so the app can offer system fonts as an opt-in, while the default and every
/// test use the embedded font. Measurement and drawing must always come from the same
/// provider instance, or the page will be laid out with one font and drawn with another.
///
/// Implementations own the returned typefaces. Callers borrow them and must not dispose them.
/// </remarks>
public interface IFontProvider : IDisposable
{
    SKTypeface GetTypeface(FontWeightKind weight);
}
