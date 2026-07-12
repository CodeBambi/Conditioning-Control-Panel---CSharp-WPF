using System;
using ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.Backends;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform;

/// <summary>
/// Selects the Linux overlay backend. Per linux-overlay-contract.md §7.1-RESOLVED
/// (web-verified 2026-07-12): this project pins Avalonia 12.0.x, which is X11-ONLY on
/// Linux — on Wayland sessions the app runs under XWayland and the overlay window is
/// still an X11 window (TryGetPlatformHandle always yields an XID, never a wl_surface).
/// A native Wayland backend can therefore never obtain a valid surface and is dead code.
/// </summary>
/// <remarks>
/// Selection chain:
/// 1. X display reachable (native X11 OR XWayland) with XFixes ≥ 2 → <see cref="X11InputShapeBackend"/>.
/// 2. Otherwise → <see cref="FallbackBackend"/> (never-trap: full-capture only, §1.4).
///
/// The session type from <see cref="LinuxSessionDetector"/> is logged for diagnostics
/// only and NEVER routes to a Wayland backend. The never-hard-fails guarantee (§2.3):
/// every probe failure — including missing native libraries and unexpected exceptions —
/// terminates in a <see cref="FallbackBackend"/>, which makes zero P/Invokes and always
/// constructs.
/// </remarks>
public sealed class LinuxOverlayBackendSelector
{
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger? _logger;

    public LinuxOverlayBackendSelector(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<LinuxOverlayBackendSelector>();
    }

    /// <summary>
    /// Selects and instantiates the backend for the current system.
    /// Guaranteed never to throw and never to return null (worst case: FallbackBackend).
    /// </summary>
    public ILinuxOverlayBackend SelectBackend()
    {
        try
        {
            // Diagnostic only — the routing decision is the X-display probe, not the env
            // (contract §2.1: env vars say which SESSION you are in, not which windowing
            // system Avalonia used; with Avalonia 12.0.x X11-only, X11 applies even on
            // Wayland sessions via XWayland).
            var sessionType = LinuxSessionDetector.Detect();
            _logger?.LogInformation(
                "Detected Linux session type: {SessionType} (diagnostic only; Avalonia 12.0.x is X11-only)",
                sessionType);

            X11InputShapeBackend? x11 = null;
            try
            {
                x11 = new X11InputShapeBackend(_loggerFactory?.CreateLogger<X11InputShapeBackend>());
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "X11InputShapeBackend probe threw; falling back");
            }

            var choice = LinuxOverlayBackendPlan.Choose(sessionType, x11?.IsAvailable == true);
            if (choice == LinuxOverlayBackendKind.X11InputShape && x11 is not null)
            {
                _logger?.LogInformation("Selected X11InputShapeBackend (X display reachable, XFixes >= 2)");
                return x11;
            }

            x11?.Dispose();
            return CreateFallback("no reachable X display with XFixes >= 2 (session: " + sessionType + ")");
        }
        catch (Exception ex)
        {
            // Terminal never-hard-fails arm (§2.3): FallbackBackend makes no P/Invokes
            // and always constructs.
            _logger?.LogError(ex, "Backend selection faulted; using FallbackBackend");
            return new FallbackBackend(
                "selector fault: " + ex.Message,
                _loggerFactory?.CreateLogger<FallbackBackend>());
        }
    }

    private FallbackBackend CreateFallback(string reason)
    {
        _logger?.LogWarning("Using FallbackBackend: {Reason}", reason);
        return new FallbackBackend(reason, _loggerFactory?.CreateLogger<FallbackBackend>());
    }
}
