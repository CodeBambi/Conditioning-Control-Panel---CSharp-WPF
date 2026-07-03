using System;
using Avalonia;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Compositor;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor;

/// <summary>
/// Avalonia-specific extension of <see cref="ILayer"/> with strongly-typed SkiaSharp
/// render and update methods.
/// </summary>
public interface IAvaloniaLayer : ILayer
{
    /// <summary>Called once per frame before <see cref="Render"/>. Use for animation state updates.</summary>
    void Update(TimeSpan deltaTime);

    /// <summary>Render the layer's content onto the shared Skia canvas.</summary>
    void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime);

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
