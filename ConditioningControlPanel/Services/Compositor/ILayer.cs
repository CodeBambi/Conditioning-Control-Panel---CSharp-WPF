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

    /// <summary>Called on the UI thread when IsActive transitions false -&gt; true.</summary>
    void OnActivated();

    /// <summary>Called on the UI thread when IsActive transitions true -&gt; false.</summary>
    void OnDeactivated();

    /// <summary>Advance animation state. Called once per engine tick while active.</summary>
    void Update(TimeSpan delta);

    /// <summary>
    /// Draw onto the shared surface. <paramref name="boundsPx"/> is the full monitor surface in
    /// DEVICE PIXELS; <paramref name="dpiScale"/> converts DIP-tuned effect math to pixels.
    /// Called once per host window per tick while active. Draw persistent SKImages, never
    /// per-frame SKBitmaps (allocation trap measured at ~480 MB/s in the Avalonia port).
    /// </summary>
    void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed);
}
