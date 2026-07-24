using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Compositor twin of the pink-filter windows: a fullscreen solid tint whose opacity rides in
/// the color's alpha channel, exactly like the legacy Border/SolidColorBrush (OverlayService
/// owns all opacity math - ramps, pulses, holds - and just pushes the final value here).
/// </summary>
public class PinkTintLayer : BaseLayer
{
    private byte _r = 255, _g = 105, _b = 180;
    private double _opacity;
    private bool _dirty = true;   // #550: a steady tint is static - only repaint when it changes
    private readonly SKPaint _paint = new();

    public PinkTintLayer(CompositorEngine engine) : base(engine) { }

    public override int ZIndex => CompositorLayers.PinkTint;

    // Fullscreen fill: honor the pink filter's per-effect monitor target (suggestion #639), which
    // itself falls back to DualMonitorEnabled when set to -1. The compositor hosts every screen,
    // so a non-targeted monitor just records an empty (transparent) frame for this layer.
    public override bool ShouldRenderOnScreen(System.Drawing.Rectangle screenBoundsPx)
        => App.ShouldRenderTargetOnScreen(
               App.Settings?.Current?.PinkFilterTargetMonitor ?? App.MonitorTargetFollowGlobal,
               screenBoundsPx);

    public override bool Dirty => _dirty;
    public override void ClearDirty() => _dirty = false;

    /// <summary>Show the tint (or retarget a visible one). Opacity 0..1. UI thread.</summary>
    public void Show(byte r, byte g, byte b, double opacity)
    {
        Set(r, g, b, opacity);
        _dirty = true;            // guarantee a paint on (re)show even if the values were unchanged
        SetActive(true);
    }

    /// <summary>Update color + opacity without changing visibility. UI thread.</summary>
    public void Set(byte r, byte g, byte b, double opacity)
    {
        var o = Math.Clamp(opacity, 0.0, 1.0);
        if (r == _r && g == _g && b == _b && o == _opacity) return;  // no visible change
        _r = r; _g = g; _b = b; _opacity = o;
        _dirty = true;
    }

    public void Hide() => SetActive(false);

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        if (_opacity <= 0) return;
        _paint.Color = new SKColor(_r, _g, _b, (byte)Math.Clamp(_opacity * 255, 0, 255));
        canvas.DrawRect(boundsPx, _paint);
    }
}
