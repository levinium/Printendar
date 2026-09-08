namespace Printendar.Core.Render;

/// <param name="Scale">Points to device-independent pixels.</param>
/// <param name="OffsetX">Left edge of the drawn sheet within the viewport.</param>
/// <param name="OffsetY">Top edge of the drawn sheet within the viewport.</param>
/// <param name="Width">Drawn sheet width.</param>
/// <param name="Height">Drawn sheet height.</param>
public readonly record struct PreviewPlacement(
    float Scale,
    float OffsetX,
    float OffsetY,
    float Width,
    float Height);

/// <summary>
/// Works out where a page sits inside a viewport, scaled to fit and centred.
/// </summary>
/// <remarks>
/// In Core, and separate from the preview control, purely so it can be tested. This arithmetic
/// living inside a UI control is how a preview ends up drawn larger than its own window, with
/// the mistake only visible to someone looking at a screenshot.
/// </remarks>
public static class PreviewFit
{
    public static PreviewPlacement Place(
        float pageWidth,
        float pageHeight,
        float viewportWidth,
        float viewportHeight,
        float zoom = 1f)
    {
        if (pageWidth <= 0f || pageHeight <= 0f ||
            viewportWidth <= 0f || viewportHeight <= 0f ||
            !float.IsFinite(viewportWidth) || !float.IsFinite(viewportHeight))
        {
            return default;
        }

        var fit = Math.Min(viewportWidth / pageWidth, viewportHeight / pageHeight);
        var scale = fit * Math.Max(zoom, 0.01f);

        var width = pageWidth * scale;
        var height = pageHeight * scale;

        // Centred, and allowed to go negative when zoomed past the viewport, so that a
        // scrolling host can still show the middle of the page rather than only its top left.
        return new PreviewPlacement(
            scale,
            (viewportWidth - width) / 2f,
            (viewportHeight - height) / 2f,
            width,
            height);
    }
}
