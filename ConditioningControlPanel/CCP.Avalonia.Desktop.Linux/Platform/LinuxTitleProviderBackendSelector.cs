using System;
using ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.TitleProviderBackends;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform;

/// <summary>
/// Selects the Linux foreground-window-title backend at runtime
/// (linux-foreground-title-contract.md §2.1-2.2). Mirrors the structure of
/// <see cref="LinuxOverlayBackendSelector"/> but inverts its routing: for foreground-title
/// the X11 backend is selected ONLY for a native X11 session. Wayland/XWayland sessions
/// resolve to <see cref="FallbackTitleBackend"/> in this wave (the native Wayland backends
/// are WAVE-3) — an X11 connection on a Wayland session would see only XWayland windows and
/// report wrong/missing foregrounds, so honest Unknown beats misleading-but-wrong activity.
/// </summary>
/// <remarks>
/// The terminal never-hard-fails guarantee (§2.3): every probe failure — missing native
/// libraries, unexpected exceptions — terminates in a <see cref="FallbackTitleBackend"/>,
/// which makes zero P/Invokes and always constructs.
/// </remarks>
internal sealed class LinuxTitleProviderBackendSelector
{
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger? _logger;

    internal LinuxTitleProviderBackendSelector(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<LinuxTitleProviderBackendSelector>();
    }

    /// <summary>
    /// Selects and instantiates the backend for the current system.
    /// Guaranteed never to throw and never to return null (worst case: FallbackTitleBackend).
    /// </summary>
    internal ILinuxTitleProviderBackend SelectBackend()
    {
        try
        {
            // §2.1: session type drives routing. Wayland is probed first (in the pure plan);
            // the X11 backend is only even CONSTRUCTED for a native X11 session, because
            // constructing it opens an X display and there is no point paying that cost on a
            // Wayland session that will never route to it.
            var sessionType = LinuxSessionDetector.Detect();
            _logger?.LogInformation("Detected Linux session type: {SessionType}", sessionType);

            X11TitleBackend? x11 = null;
            if (sessionType == LinuxSessionType.X11)
            {
                try
                {
                    x11 = new X11TitleBackend(_loggerFactory?.CreateLogger<X11TitleBackend>());
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "X11TitleBackend probe threw; falling back");
                }
            }

            var choice = LinuxTitleProviderBackendPlan.Choose(
                sessionType, xDisplayReachable: x11?.IsAvailable == true);

            if (choice == LinuxTitleBackendKind.X11Title && x11 is not null)
            {
                _logger?.LogInformation("Selected X11TitleBackend (X11 session, EWMH display reachable)");
                return x11;
            }

            // Either the plan picked Fallback (Wayland/unknown session) or X11 was requested
            // but the display did not open / the WM has no EWMH. Capture the probe outcome
            // BEFORE disposing so the fallback reason is descriptive, then release the X display.
            bool x11ProbeWasAvailable = x11?.IsAvailable == true;
            x11?.Dispose();
            return CreateFallback(sessionType, x11ProbeWasAvailable);
        }
        catch (Exception ex)
        {
            // Terminal never-hard-fails arm (§2.3): FallbackTitleBackend makes no P/Invokes
            // and always constructs.
            _logger?.LogError(ex, "Title backend selection faulted; using FallbackTitleBackend");
            return new FallbackTitleBackend(
                "selector fault: " + ex.Message,
                _loggerFactory?.CreateLogger<FallbackTitleBackend>());
        }
    }

    private FallbackTitleBackend CreateFallback(LinuxSessionType sessionType, bool x11ProbeWasAvailable)
    {
        // Reason strings name the backend/probe outcome ONLY — never title content (§1.3).
        // x11ProbeWasAvailable is only meaningful for the X11 branch; on the X11 fallback path
        // it is always false (a true value would have been returned as the X11 backend above).
        // It is accepted as a parameter so the caller can hand off the pre-dispose probe result
        // without re-reading the (now-disposed) backend.
        _ = x11ProbeWasAvailable;
        string reason = sessionType switch
        {
            LinuxSessionType.Wayland =>
                "Wayland session — native Wayland foreground-title backends " +
                "(wlr-foreign-toplevel-management) are not present in this build; an X11 " +
                "connection would see only XWayland windows, so reporting honest Unknown",
            LinuxSessionType.XWayland =>
                "XWayland session (Wayland + X compatibility) — native Wayland foreground-title " +
                "backends are not present in this build; an X11 connection would see only " +
                "XWayland windows, so reporting honest Unknown",
            LinuxSessionType.X11 =>
                "X11 session but no reachable EWMH X display " +
                "(_NET_ACTIVE_WINDOW unavailable — no display or non-EWMH window manager)",
            _ => "unrecognized session type — no foreground-title backend available",
        };

        _logger?.LogWarning("Using FallbackTitleBackend: {Reason}", reason);
        return new FallbackTitleBackend(reason, _loggerFactory?.CreateLogger<FallbackTitleBackend>());
    }
}
