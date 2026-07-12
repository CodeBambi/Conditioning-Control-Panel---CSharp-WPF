using System;
using ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.FrameSourceBackends;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform;

/// <summary>
/// Selects the Linux IFrameSource backend at runtime
/// (linux-framesource-contract.md §2.1 / §2.2). Wayland-first selection: on a Wayland or
/// XWayland session the X11 root window holds only XWayland content (typically a black
/// root), so XGetImage would silently return the wrong image — those sessions fall to the
/// black-frame fallback until the native Wayland capture backends (slices D-F) land. Only a
/// NATIVE X11 session with a reachable X display selects the X11 backend.
/// </summary>
/// <remarks>
/// <para>The Wayland-before-X11 order and the X11-vs-fallback split live in the pure,
/// unit-tested <see cref="LinuxFrameSourcePlan"/> (mirrors the overlay selector's split into
/// <c>LinuxOverlayBackendPlan</c>). This class owns only the part that touches native
/// libraries (opening the X display to probe X11 availability) and NEVER throws — every
/// fault terminates in a <see cref="FallbackFrameSource"/> (contract §2.2 guarantee).</para>
/// <para>Selection is performed on demand, when <see cref="SelectBackend"/> is called. The
/// consuming feature drives that call on its own lifecycle — there is no idle/background
/// capture (privacy-invariant: the source only works when a feature pulls a frame).</para>
/// </remarks>
public sealed class LinuxFrameSourceBackendSelector
{
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger? _logger;

    public LinuxFrameSourceBackendSelector(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<LinuxFrameSourceBackendSelector>();
    }

    /// <summary>
    /// Selects and instantiates the backend. Guaranteed never to throw and never to return
    /// null (worst case: <see cref="FallbackFrameSource"/>). The caller owns the returned
    /// backend's lifetime (Dispose when the consuming feature stops).
    /// </summary>
    public ILinuxFrameSourceBackend SelectBackend()
    {
        try
        {
            var sessionType = LinuxSessionDetector.Detect();

            // X11 availability is probed ONLY for NATIVE X11 sessions — never for
            // Wayland/XWayland (contract §2.1 XWayland note: never fall from a Wayland probe
            // to the X11 backend). The probe opens a dedicated X display; a failure (no
            // libX11, display closed, DllNotFoundException) degrades to fallback.
            bool x11Available = false;
            X11BasicFrameSourceBackend? x11 = null;
            if (sessionType == LinuxSessionType.X11)
            {
                try
                {
                    x11 = new X11BasicFrameSourceBackend(
                        _loggerFactory?.CreateLogger<X11BasicFrameSourceBackend>());
                    x11Available = x11.IsAvailable;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "X11BasicFrameSourceBackend construction threw; falling back");
                    x11Available = false;
                }
            }

            var choice = LinuxFrameSourcePlan.Choose(sessionType, x11Available);
            if (choice == LinuxFrameSourceBackendKind.X11 && x11 is not null)
            {
                _logger?.LogInformation(
                    "Selected X11BasicFrameSourceBackend (native X11 session, display reachable) — " +
                    "session: {SessionType}", sessionType);
                return x11;
            }

            x11?.Dispose();
            return CreateFallback(
                $"no usable screen-capture backend (session: {sessionType}; x11Available: {x11Available}; " +
                "native Wayland capture backends not yet implemented)");
        }
        catch (Exception ex)
        {
            // Terminal never-hard-fails arm (contract §2.2): FallbackFrameSource makes no
            // P/Invokes and always constructs.
            _logger?.LogError(ex, "Frame-source backend selection faulted; using FallbackFrameSource");
            return new FallbackFrameSource(
                "selector fault: " + ex.Message,
                _loggerFactory?.CreateLogger<FallbackFrameSource>());
        }
    }

    private FallbackFrameSource CreateFallback(string reason)
    {
        _logger?.LogWarning("Using FallbackFrameSource: {Reason}", reason);
        return new FallbackFrameSource(reason, _loggerFactory?.CreateLogger<FallbackFrameSource>());
    }
}
