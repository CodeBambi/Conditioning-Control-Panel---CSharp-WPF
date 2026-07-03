using System;
using Avalonia;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Compositor;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor;

/// <summary>
/// Avalonia-specific extension of <see cref="ILayer"/> with strongly-typed SkiaSharp
/// render and update methods.
///
/// COORDINATE CONTRACT: layer geometry — item positions/sizes and the <c>bounds</c>
/// passed to <see cref="Render(SKCanvas, ConditioningControlPanel.Core.Platform.PixelRect, ScreenInfo?, TimeSpan)"/> —
/// is in PHYSICAL virtual-desktop pixels, the same space as <c>ScreenInfo.Bounds</c> and
/// the WH_MOUSE_LL hook's <c>HookPoint</c>. The engine pre-transforms each per-monitor
/// window's canvas (scale 1/screen.Scaling, translate -screen origin) so physical
/// coordinates map onto that window's DIP surface. Consequences:
/// - positioned items (flash, bubbles, bouncing text) render only on the monitor whose
///   physical rect contains them — no cross-monitor mirroring;
/// - hit-testing compares raw hook coordinates against item geometry directly, with no
///   per-DPI conversion, and stays correct on mixed-DPI multi-monitor setups;
/// - full-bounds layers just fill/center within <c>bounds</c> (the monitor's physical
///   rect) and need no awareness of the transform.
/// Feeders that work in logical/DIP units (e.g. Core BubbleEngine) must convert at the
/// service seam (multiply by the item's screen scaling) before touching a layer.
/// </summary>
public interface IAvaloniaLayer : ILayer
{
    /// <summary>Called once per frame before <see cref="Render"/>. Use for animation state updates.</summary>
    void Update(TimeSpan deltaTime);

    /// <summary>Render the layer's content onto the shared Skia canvas.</summary>
    void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime);

    /// <summary>
    /// Dirty check, consumed once per engine tick after <see cref="Update"/>. Return true
    /// when the layer's visible content changed since the last presented frame; when every
    /// active layer returns false the engine skips InvalidateVisual for that tick, so a
    /// fully static frame (e.g. a constant tint, bubbles between 30Hz physics ticks) costs
    /// no GPU re-render. The default returns true (always repaint), which is correct for
    /// continuously animating layers; static/mutator-driven layers should track a dirty
    /// flag set in their mutators and cleared by this call.
    /// </summary>
    bool ConsumeDirty() => true;

    /// <summary>
    /// Capture affinity: when true the engine routes this layer to a dedicated per-monitor
    /// compositor window that is excluded from screen capture via
    /// SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) (WPF parity: brain-drain windows are
    /// excluded "so we don't capture ourselves" and never appear in streams/recordings).
    /// GUARDRAIL: the default MUST stay false. Subliminals (and flash/spiral/pink tint) are
    /// deliberately visible in capture (WPF SubliminalService sets WDA_NONE BY DESIGN);
    /// never move them to the excluded surface.
    /// </summary>
    bool ExcludeFromCapture => false;

    /// <summary>
    /// Screen-aware render overload. The engine calls this with the <see cref="ScreenInfo"/>
    /// of the monitor whose compositor window is being rendered (null when unknown, e.g. the
    /// fallback screen). Layers that need per-monitor content (screen-capture blur) override
    /// this; everything else inherits the default forward to the plain overload.
    /// </summary>
    void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, ScreenInfo? screen, TimeSpan deltaTime)
        => Render(canvas, bounds, deltaTime);
}
