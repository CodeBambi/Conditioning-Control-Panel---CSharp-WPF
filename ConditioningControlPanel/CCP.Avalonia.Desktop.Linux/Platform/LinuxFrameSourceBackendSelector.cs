using System;
using ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.FrameSourceBackends;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform;

/// <summary>
/// Selects the Linux IFrameSource backend at runtime
/// (linux-framesource-contract.md §2.1 / §2.2). Wayland-first selection: on a Wayland or
/// XWayland session the X11 root window holds only XWayland content (typically a black
/// root), so XGetImage/XShmGetImage would silently return the wrong image — those sessions
/// fall to the black-frame fallback until the native Wayland capture backends (slices D-F)
/// land. Only a NATIVE X11 session with a reachable X display selects an X11 backend.
/// </summary>
/// <remarks>
/// <para>The Wayland-before-X11 order and the X11-vs-fallback split live in the pure,
/// unit-tested <see cref="LinuxFrameSourcePlan"/> (mirrors the overlay selector's split into
/// <c>LinuxOverlayBackendPlan</c>). This class owns only the part that touches native
/// libraries (opening the X display to probe X11 + MIT-SHM availability) and NEVER throws —
/// every fault terminates in a <see cref="FallbackFrameSource"/> (contract §2.2 guarantee).</para>
/// <para><b>X11 priority (contract §2.2):</b> on a native X11 session the MIT-SHM fast path
/// is probed FIRST; when its attach round-trip succeeds it is selected. Otherwise the basic
/// <c>XGetImage</c> backend is probed and selected. Otherwise the black-frame fallback. SHM is
/// strictly preferred over basic; basic over fallback.</para>
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
        X11ShmFrameSourceBackend? shm = null;
        X11BasicFrameSourceBackend? basic = null;
        bool shmUsable = false;
        bool basicUsable = false;

        try
        {
            var sessionType = LinuxSessionDetector.Detect();

            // X11 is probed ONLY for NATIVE X11 sessions — never for Wayland/XWayland
            // (contract §2.1 XWayland note: never fall from a Wayland probe to the X11
            // backend). Each probe opens a dedicated X display; a failure (no libX11/libXext,
            // display closed, DllNotFoundException, SHM attach refused) degrades down the chain.
            if (sessionType == LinuxSessionType.X11)
            {
                // Priority 1: MIT-SHM fast path (contract §2.2). The constructor opens a
                // dedicated display, checks the extension + locality, and runs a 1x1 attach
                // round-trip — IsAvailable is authoritative.
                try
                {
                    shm = new X11ShmFrameSourceBackend(
                        _loggerFactory?.CreateLogger<X11ShmFrameSourceBackend>());
                    shmUsable = shm.IsAvailable;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "X11ShmFrameSourceBackend construction threw; trying basic");
                    shmUsable = false;
                }

                // Priority 2: basic XGetImage path (contract §2.2). Only probed when SHM is not
                // usable, so a working SHM session never pays the second display open.
                if (!shmUsable)
                {
                    shm?.Dispose();
                    shm = null;

                    try
                    {
                        basic = new X11BasicFrameSourceBackend(
                            _loggerFactory?.CreateLogger<X11BasicFrameSourceBackend>());
                        basicUsable = basic.IsAvailable;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "X11BasicFrameSourceBackend construction threw; falling back");
                        basicUsable = false;
                    }
                }
            }

            var choice = LinuxFrameSourcePlan.Choose(sessionType, basicUsable, shmUsable);

            if (choice == LinuxFrameSourceBackendKind.X11Shm && shm is not null)
            {
                _logger?.LogInformation(
                    "Selected X11ShmFrameSourceBackend (native X11, MIT-SHM attach ok) — " +
                    "session: {SessionType}", sessionType);
                return shm;
            }

            if (choice == LinuxFrameSourceBackendKind.X11Basic && basic is not null)
            {
                _logger?.LogInformation(
                    "Selected X11BasicFrameSourceBackend (native X11, MIT-SHM unavailable: {ShmUsable}) — " +
                    "session: {SessionType}", shmUsable, sessionType);
                return basic;
            }

            // No usable backend: release any partial probes and degrade to black.
            shm?.Dispose();
            basic?.Dispose();
            return CreateFallback(
                $"no usable screen-capture backend (session: {sessionType}; shmAvailable: {shmUsable}; " +
                $"basicAvailable: {basicUsable}; native Wayland capture backends not yet implemented)");
        }
        catch (Exception ex)
        {
            // Terminal never-hard-fails arm (contract §2.2): FallbackFrameSource makes no
            // P/Invokes and always constructs. Release any partial probes first.
            shm?.Dispose();
            basic?.Dispose();
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
