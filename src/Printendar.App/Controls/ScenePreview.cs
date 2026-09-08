using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Printendar.Core.Render;
using Printendar.Core.Scene;
using Printendar.Core.Text;
using SkiaSharp;

// Avalonia.Media has its own RenderOptions, which is about how Avalonia draws. This one is
// about how a Printendar page is drawn, so the alias keeps the two apart at the call site.
using PageRenderOptions = Printendar.Core.Render.RenderOptions;

namespace Printendar.App.Controls;

/// <summary>
/// Shows a laid-out page as a sheet of paper.
/// </summary>
/// <remarks>
/// Draws the very same <see cref="ScenePage"/> object the exporter will write, through the
/// very same <see cref="SceneRenderer"/>. That is what makes the preview trustworthy: there is
/// no second layout pass and no parallel drawing code that could drift, so "what you see is
/// what prints" is a structural property rather than something anyone has to maintain.
/// </remarks>
public sealed class ScenePreview : Control
{
    public static readonly StyledProperty<ScenePage?> SceneProperty =
        AvaloniaProperty.Register<ScenePreview, ScenePage?>(nameof(Scene));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<ScenePreview, double>(nameof(Zoom), 1.0);

    private ITextMeasurer? _measurer;

    static ScenePreview()
    {
        AffectsRender<ScenePreview>(SceneProperty, ZoomProperty);
        AffectsMeasure<ScenePreview>(SceneProperty, ZoomProperty);
    }

    public ScenePage? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    /// <summary>1.0 fits the page to the control; larger zooms in.</summary>
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>
    /// The measurer used to draw. Must be the one the scene was laid out with.
    /// </summary>
    /// <remarks>
    /// Handing the renderer a different measurer would draw the page with fonts other than the
    /// ones it was measured against, which is precisely the drift this design exists to avoid.
    /// </remarks>
    public ITextMeasurer? Measurer
    {
        get => _measurer;
        set
        {
            _measurer = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Takes whatever space it is offered, rather than negotiating a size.
    /// </summary>
    /// <remarks>
    /// Deliberately not returning a fitted desired size. Doing that made the control ask for
    /// the space it wanted, which is how the sheet ended up drawn wider than the window and
    /// pushed the options panel off the edge. Filling the slot and fitting the page inside it
    /// during render means there is exactly one place the scale is decided.
    /// </remarks>
    protected override Size MeasureOverride(Size availableSize) => default;

    public override void Render(DrawingContext context)
    {
        if (Scene is not { } scene || Measurer is not { } measurer)
        {
            return;
        }

        var placement = PreviewFit.Place(
            scene.Page.WidthPt,
            scene.Page.HeightPt,
            (float)Bounds.Width,
            (float)Bounds.Height,
            (float)Zoom);

        if (placement.Scale <= 0f)
        {
            return;
        }

        context.Custom(new ScenePageDrawOperation(
            new Rect(placement.OffsetX, placement.OffsetY, placement.Width, placement.Height),
            scene,
            placement.Scale,
            placement.OffsetX,
            placement.OffsetY,
            measurer));
    }
}

/// <summary>
/// Draws a page onto the canvas Avalonia is already rendering with.
/// </summary>
/// <remarks>
/// Leasing Avalonia's own <see cref="SKCanvas"/> rather than rendering to a bitmap and blitting
/// it means the page is drawn as vectors at the window's real resolution, so text stays sharp
/// when zoomed and on a HiDPI display, with no intermediate copy.
///
/// This only works while Printendar and Avalonia resolve the same SkiaSharp major version. If
/// they diverge, the leased canvas is a different CLR type and this stops compiling. That is
/// deliberate: it fails at build time rather than at run time.
/// </remarks>
internal sealed class ScenePageDrawOperation : ICustomDrawOperation
{
    // Explicit fields rather than primary constructor parameters: Equals has to read these off
    // the other instance, and primary constructor parameters are only in scope for this one.
    private readonly ScenePage _scene;
    private readonly float _scale;
    private readonly float _offsetX;
    private readonly float _offsetY;
    private readonly ITextMeasurer _measurer;

    public ScenePageDrawOperation(
        Rect bounds,
        ScenePage scene,
        float scale,
        float offsetX,
        float offsetY,
        ITextMeasurer measurer)
    {
        Bounds = bounds;
        _scene = scene;
        _scale = scale;
        _offsetX = offsetX;
        _offsetY = offsetY;
        _measurer = measurer;
    }

    public Rect Bounds { get; }

    public void Dispose()
    {
    }

    public bool HitTest(Point p) => Bounds.Contains(p);

    /// <summary>
    /// Lets Avalonia skip a repaint when nothing that affects the drawing has changed.
    /// </summary>
    /// <remarks>
    /// Compares the scene by reference on purpose. Layout produces a new immutable
    /// <see cref="ScenePage"/> whenever anything changes, so reference equality is exactly the
    /// right question, and structurally comparing a page of several hundred nodes on every
    /// frame would cost more than the redraw it saved.
    /// </remarks>
    public bool Equals(ICustomDrawOperation? other) =>
        other is ScenePageDrawOperation op &&
        ReferenceEquals(op._scene, _scene) &&
        op._scale.Equals(_scale) &&
        op._offsetX.Equals(_offsetX) &&
        op._offsetY.Equals(_offsetY) &&
        op.Bounds == Bounds;

    public void Render(ImmediateDrawingContext context)
    {
        var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();

        if (lease is null)
        {
            // Only reachable on a non-Skia rendering backend. Drawing nothing is honest;
            // showing a stale or approximate page would be worse than showing none.
            return;
        }

        using var skia = lease.Lease();
        var canvas = skia.SkCanvas;

        if (Environment.GetEnvironmentVariable("PRINTENDAR_TRACE_PREVIEW") is not null)
        {
            var m = canvas.TotalMatrix;
            Console.Error.WriteLine(
                $"[preview] bounds={Bounds.Width:0.#}x{Bounds.Height:0.#} " +
                $"scale={_scale:0.###} offset=({_offsetX:0.#},{_offsetY:0.#}) " +
                $"canvasMatrix=({m.ScaleX:0.###},{m.ScaleY:0.###}) " +
                $"deviceClip={canvas.DeviceClipBounds.Width}x{canvas.DeviceClipBounds.Height}");
        }

        canvas.Save();

        // Avalonia has already applied the window's DPI scaling to this canvas, so translating
        // and scaling in device-independent points is all that is needed. Multiplying by
        // RenderScaling here as well would double-apply it on a HiDPI display.
        canvas.Translate(_offsetX, _offsetY);
        canvas.Scale(_scale);

        // Clip to the sheet so a layout bug shows as clipped content rather than as ink
        // scribbled across the rest of the window.
        canvas.ClipRect(SKRect.Create(0, 0, _scene.Page.WidthPt, _scene.Page.HeightPt));

        SceneRenderer.Draw(canvas, _scene, PageRenderOptions.ForPreview, _measurer);

        canvas.Restore();
    }
}
