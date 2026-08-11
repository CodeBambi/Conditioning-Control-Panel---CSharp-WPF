using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// A z-ordered visual layer rendered by the <see cref="CompositorEngine"/> into the shared
/// per-monitor overlay host, replacing a dedicated per-effect window.
/// Contract mirrors the Avalonia port's ILayer/IAvaloniaLayer seam (same member names and
/// semantics) so layer implementations stay portable between the two heads.
///
/// Threading: Update/Render are always called on the UI thread from the engine tick.
/// Services own state and hand it to their layer under their own locking; layers only render.
/// </summary>
public interface IWpfLayer
{
    /// <summary>Z position from <see cref="CompositorLayers"/>. Lower renders first (behind).</summary>
    int ZIndex { get; }

    /// <summary>
    /// True while the layer has something to show. The engine shows/hides host windows and
    /// runs the render tick based on this; an inactive layer costs nothing.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// True routes this layer to the capture-EXCLUDED surface (WDA_EXCLUDEFROMCAPTURE), used by
    /// brain drain to avoid self-capture feedback. Everything else stays on the main surface,
    /// which must NEVER be capture-excluded (subliminals/flash/spiral are visible in recordings
    /// BY DESIGN - WPF SubliminalService deliberately sets WDA_NONE).
    /// </summary>
    bool ExcludeFromCapture => false;

    /// <summary>
    /// True = this layer draws in VIRTUAL-DESKTOP device pixels (positional effects that span
    /// monitors, e.g. pop bursts at a bubble's screen point). The engine translates the canvas so
    /// world (0,0) is the desktop origin and passes <c>boundsPx</c> as THIS monitor's rectangle
    /// in that same world space (for culling). Default (false) = monitor-local rendering:
    /// (0,0) is the monitor's top-left and boundsPx starts at zero.
    /// </summary>
    bool WorldSpacePx => false;

    /// <summary>
    /// False = skip this layer on the given monitor (its virtual-desktop device-px bounds). The
    /// engine hosts EVERY screen, so a fullscreen FILL layer (pink tint, spiral) that must honor
    /// <c>DualMonitorEnabled</c> has to opt out of the non-primary monitors here - otherwise it
    /// leaks onto screens the user disabled. Positional layers don't need this: their owning
    /// service already places geometry only on the allowed monitor. Default: draw on every host.
    /// </summary>
    bool ShouldRenderOnScreen(System.Drawing.Rectangle screenBoundsPx) => true;

    /// <summary>Called on the UI thread when IsActive transitions false -&gt; true.</summary>
    void OnActivated();

    /// <summary>Called on the UI thread when IsActive transitions true -&gt; false.</summary>
    void OnDeactivated();

    /// <summary>Advance animation state. Called once per engine tick while active.</summary>
    void Update(TimeSpan delta);

    /// <summary>
    /// True if this layer's visible output changed since the last <see cref="ClearDirty"/>. The
    /// engine only re-rasters a shared surface when at least one of its active layers is dirty, so
    /// a slow layer (a ~10fps spiral GIF) or a static one (a steady pink tint) no longer forces the
    /// fullscreen software surface to re-raster at 60fps on the UI thread (#550).
    ///
    /// The fold is per SURFACE, not per layer: a clean layer is still DRAWN into every present it
    /// shares, it just doesn't cause one. So an inaccurate <c>true</c> here costs the whole
    /// surface, and EVERY layer implements this honestly (#853) - including the ones that animate
    /// continuously, which report dirty only at the cadence their owning service actually steps
    /// them (a ~30fps bubble field, a 30fps brain-drain capture) rather than at refresh rate.
    /// Default true is a safe fallback for a new layer, never a design choice.
    /// </summary>
    bool Dirty => true;

    /// <summary>Engine calls this once per tick after it has queued this frame's surface
    /// invalidations, so a layer that overrides <see cref="Dirty"/> can reset its flag.</summary>
    void ClearDirty() { }

    /// <summary>
    /// Draw onto the shared surface. <paramref name="boundsPx"/> is the full monitor surface in
    /// DEVICE PIXELS; <paramref name="dpiScale"/> converts DIP-tuned effect math to pixels.
    /// Called once per host window per tick while active. Draw persistent SKImages, never
    /// per-frame SKBitmaps (allocation trap measured at ~480 MB/s in the Avalonia port).
    /// </summary>
    void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed);
}
