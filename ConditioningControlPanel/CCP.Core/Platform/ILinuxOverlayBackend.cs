using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Backend abstraction for Linux overlay surfaces. Each backend implements topmost
/// and click-through behavior using a specific display protocol (X11/Wayland) and
/// protocol extensions (XFixes, wlr-layer-shell, etc.).
/// </summary>
public interface ILinuxOverlayBackend
{
    /// <summary>Backend name for diagnostics and logging.</summary>
    string Name { get; }

    /// <summary>Whether this backend is available on the current system.</summary>
    bool IsAvailable { get; }

    /// <summary>Capability: supports per-region click-through (vs full-window only).</summary>
    bool SupportsPerRegionInputShape { get; }

    /// <summary>Capability: supports guaranteed topmost (vs best-effort).</summary>
    bool SupportsTopmost { get; }

    /// <summary>Shows the overlay window.</summary>
    void Show();

    /// <summary>Hides the overlay window.</summary>
    void Hide();

    /// <summary>Closes and disposes the overlay window.</summary>
    void Close();

    /// <summary>Whether the overlay is currently visible.</summary>
    bool IsVisible { get; }

    /// <summary>
    /// Sets full-window click-through (all clicks pass through).
    /// </summary>
    void SetClickThrough(bool enabled);

    /// <summary>
    /// Sets the overlay bounds.
    /// </summary>
    void SetBounds(PixelRect rect);

    /// <summary>
    /// Updates the input capture regions. Clicks inside these regions are captured;
    /// clicks outside pass through. Pass an empty collection for full click-through.
    /// </summary>
    /// <param name="captureRegions">Rectangles (in window coordinates) that should capture input.</param>
    void SetInputCaptureRegions(IReadOnlyList<PixelRect> captureRegions);
}
