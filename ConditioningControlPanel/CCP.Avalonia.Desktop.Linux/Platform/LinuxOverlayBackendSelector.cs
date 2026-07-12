using System;
using ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.Backends;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform;

/// <summary>
/// Selects the best available Linux overlay backend based on the detected session type
/// and available protocol extensions. Implements a 5-tier fallback chain to ensure
/// something functional always runs.
/// </summary>
/// <remarks>
/// Fallback chain (per docs/linux-overlay-contract.md §2.2):
/// 1. X11InputShapeBackend - X11 + XFixes (full per-region click-through)
/// 2. WaylandLayerShellBackend - wlr-layer-shell (sway/Hyprland/wlroots)
/// 3. WaylandInputRegionBackend - generic Wayland input regions (partial)
/// 4. WaylandDegradeBackend - Wayland fallback (no click-through)
/// 5. FallbackBackend - always-on-top window, no click-through
/// </remarks>
public sealed class LinuxOverlayBackendSelector
{
    private readonly ILogger? _logger;

    public LinuxOverlayBackendSelector(ILogger<LinuxOverlayBackendSelector>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Selects and instantiates the best available backend for the current system.
    /// Guaranteed to return a functional backend (worst case: FallbackBackend).
    /// </summary>
    public ILinuxOverlayBackend SelectBackend()
    {
        var sessionType = LinuxSessionDetector.Detect();
        _logger?.LogInformation("Detected Linux session type: {SessionType}", sessionType);

        return sessionType switch
        {
            LinuxSessionType.X11 => SelectX11Backend(),
            LinuxSessionType.Wayland => SelectWaylandBackend(),
            LinuxSessionType.XWayland => SelectXWaylandBackend(),
            _ => CreateFallback("Unknown session type")
        };
    }

    private ILinuxOverlayBackend SelectX11Backend()
    {
        // Tier 1: X11 with XFixes input shape
        var x11 = new X11InputShapeBackend(_logger as ILogger<X11InputShapeBackend>);
        if (x11.IsAvailable)
        {
            _logger?.LogInformation("Selected X11InputShapeBackend (Tier 1)");
            return x11;
        }

        _logger?.LogWarning("X11InputShapeBackend not available, falling back");
        return CreateFallback("X11 session but XFixes unavailable");
    }

    private ILinuxOverlayBackend SelectWaylandBackend()
    {
        // Tier 2: Wayland with wlr-layer-shell (sway, Hyprland, wlroots compositors)
        var layerShell = new WaylandLayerShellBackend(_logger as ILogger<WaylandLayerShellBackend>);
        if (layerShell.IsAvailable)
        {
            _logger?.LogInformation("Selected WaylandLayerShellBackend (Tier 2)");
            return layerShell;
        }

        // Tier 3: Wayland with input regions (partial support)
        // TODO: Slice E - WaylandInputRegionBackend

        // Tier 4: Wayland degrade (GNOME/KDE without layer-shell)
        // TODO: Slice F - WaylandDegradeBackend

        _logger?.LogWarning("No full-featured Wayland backend available, falling back");
        return CreateFallback("Wayland session but layer-shell unavailable");
    }

    private ILinuxOverlayBackend SelectXWaylandBackend()
    {
        // Prefer native Wayland if available
        var wayland = SelectWaylandBackend();
        if (wayland is not FallbackBackend)
        {
            return wayland;
        }

        // Fall back to X11 under XWayland
        _logger?.LogInformation("XWayland session: trying X11 backend");
        return SelectX11Backend();
    }

    private FallbackBackend CreateFallback(string reason)
    {
        _logger?.LogWarning("Using FallbackBackend (Tier 5): {Reason}", reason);
        return new FallbackBackend(reason, _logger as ILogger<FallbackBackend>);
    }
}
