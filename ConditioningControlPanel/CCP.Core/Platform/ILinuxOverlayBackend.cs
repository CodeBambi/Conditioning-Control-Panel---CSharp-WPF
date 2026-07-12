using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Backend abstraction for Linux overlay surfaces.
/// Per docs/linux-overlay-contract.md §7.1-RESOLVED (web-verified 2026-07-12): this project
/// pins Avalonia 12.0.x, which is X11-only on Linux (Wayland sessions run via XWayland, so
/// the overlay window is ALWAYS an X11 window). The only backends are therefore
/// X11 (XFixes input shape + EWMH topmost) and the no-X fallback. Native Wayland backends
/// are dead code for this project and must not be added while Avalonia stays X11-only.
/// Backends own native resources (an Xlib display connection) and are IDisposable
/// (contract §6.0 item 5); <c>LinuxOverlaySurface.Close()</c> disposes the backend.
/// </summary>
public interface ILinuxOverlayBackend : IDisposable
{
    /// <summary>Backend name for diagnostics and logging.</summary>
    string Name { get; }

    /// <summary>Whether this backend is available on the current system.</summary>
    bool IsAvailable { get; }

    /// <summary>Capability: supports per-region click-through (vs full-window only).</summary>
    bool SupportsPerRegionInputShape { get; }

    /// <summary>
    /// Capability: supports guaranteed topmost (vs best-effort).
    /// Capability flags are a contract (linux-overlay-contract.md §1.1): a backend
    /// MUST NOT report a capability it does not deliver.
    /// </summary>
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
