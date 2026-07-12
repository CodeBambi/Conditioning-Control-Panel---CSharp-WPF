using System;
using Avalonia;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Abstract base for Avalonia compositor layers. Provides common helpers for bounds,
/// opacity, and lifetime management.
/// </summary>
public abstract class BaseLayer : IAvaloniaLayer
{
    private bool _activated;

    public abstract int ZIndex { get; }

    public virtual bool IsActive => _activated;

    /// <summary>
    /// Capture affinity (see <see cref="IAvaloniaLayer.ExcludeFromCapture"/>). Only
    /// <c>BrainDrainLayer</c> overrides this to true; the main compositor surface must
    /// stay capturable so subliminals appear in the user's recordings BY DESIGN.
    /// </summary>
    public virtual bool ExcludeFromCapture => false;

    public virtual void OnActivated()
    {
        _activated = true;
    }

    public virtual void OnDeactivated()
    {
        _activated = false;
    }

    public abstract void Update(TimeSpan deltaTime);

    /// <summary>
    /// Dirty gate (see <see cref="IAvaloniaLayer.ConsumeDirty"/>). Base implementation
    /// always reports dirty — correct for continuously animating layers. Static or
    /// mutator-driven layers override this with a consume-once flag so the engine can skip
    /// whole-frame re-renders while nothing changed. Declared here (not only as the
    /// interface default) so derived-class overrides participate in interface dispatch.
    /// </summary>
    public virtual bool ConsumeDirty() => true;

    public abstract void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime);

    /// <summary>
    /// AMBIENT CLICK-THROUGH AFFINITY bridge (see <see cref="IAvaloniaLayer.IsAmbientClickThrough"/>).
    /// Only <c>PinkTintLayer</c> and <c>SpiralLayer</c> override this to true; every other
    /// active layer captures pointer input over its painted region and exposes that region via
    /// <see cref="CollectCaptureRegions"/>. Declared here (not only as the interface default)
    /// so derived-class overrides participate in interface dispatch.
    /// </summary>
    public virtual bool IsAmbientClickThrough => false;

    /// <summary>
    /// CAPTURE-REGION bridge (see <see cref="IAvaloniaLayer.CollectCaptureRegions"/>).
    /// Default contributes nothing; a non-ambient layer overrides this to expose its painted
    /// region(s) so the global mouse hook swallows clicks over them. Declared here (not only as
    /// the interface default) so derived-class overrides participate in interface dispatch.
    /// </summary>
    public virtual void CollectCaptureRegions(
        ConditioningControlPanel.Core.Services.Compositor.CaptureMaskBuilder builder,
        System.Collections.Generic.IReadOnlyList<ConditioningControlPanel.Core.Platform.ScreenInfo> screens)
    {
        // Non-ambient layers override to add their painted region(s).
    }

    /// <summary>Lerp helper: map value 0..1 onto [min,max].</summary>
    protected static double Lerp(double min, double max, double t) => min + (max - min) * Math.Clamp(t, 0, 1);

    /// <summary>Convert Avalonia <see cref="PixelRect"/> to Skia <see cref="SKRect"/>.</summary>
    protected static SKRect ToSkRect(ConditioningControlPanel.Core.Platform.PixelRect rect) =>
        new((float)rect.X, (float)rect.Y, (float)(rect.X + rect.Width), (float)(rect.Y + rect.Height));
}
