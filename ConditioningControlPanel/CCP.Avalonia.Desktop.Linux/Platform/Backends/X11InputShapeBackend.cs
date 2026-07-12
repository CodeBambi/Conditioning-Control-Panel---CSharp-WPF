using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.Interop;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.Logging;
using static ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.Interop.X11Interop;
using CorePixelRect = ConditioningControlPanel.Core.Platform.PixelRect;
using ILinuxOverlayBackend = ConditioningControlPanel.Core.Platform.ILinuxOverlayBackend;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.Backends;

/// <summary>
/// X11 overlay backend using the XFixes extension for per-region click-through (Tier 1).
/// Per linux-overlay-contract.md §7.1-RESOLVED this is the UNIVERSAL Linux backend: Avalonia
/// 12.0.x is X11-only on Linux, so on Wayland sessions the overlay window is an XWayland
/// window and this same machinery applies.
/// </summary>
/// <remarks>
/// <para><b>Connection and threading model (contract §3.1):</b> this backend owns a
/// DEDICATED <c>XOpenDisplay</c> connection and NEVER touches Avalonia's display or event
/// loop. Operating on the Avalonia window's XID from our own connection is legal and
/// race-free at the protocol level (X11 windows are server-side resources addressable from
/// any client connection). Xlib is not thread-safe and <c>XInitThreads</c> cannot be
/// guaranteed to run first next to Avalonia's own X11 use, so ALL access to our display is
/// serialized behind <see cref="_xLock"/> (SetInputCaptureRegions arrives from the
/// compositor tick, SetClickThrough/Show from the UI thread).</para>
///
/// <para><b>Error trap (contract §3.1):</b> Xlib errors are not exceptions — the default
/// handler exits the process. Every Xlib sequence runs inside a
/// <see cref="XlibErrorTrap"/> scope (Reset → calls → XSync → check). Trapped errors skip
/// the operation and log; repeated errors demote the backend (never-trap rule below).</para>
///
/// <para><b>Per-region click-through (contract §1.3, overlay-clickthrough skill):</b> ONLY
/// the spiral and theme-color-filter layers are ambient pass-through; every other active
/// layer CAPTURES. The XFixes input shape is set to the UNION of the capturing layers'
/// rects (window-local physical pixels), with normative precedence:
/// SetClickThrough(false) → full-window capture regardless of regions;
/// SetClickThrough(true) + regions → shape = union of capture regions;
/// SetClickThrough(true) + no regions → empty shape (fully ambient).</para>
///
/// <para><b>Never-trap rule (contract §1.4):</b> if ambient mode
/// (<c>SetClickThrough(true)</c>) is requested but the input shape cannot be applied
/// (no XID, X errors, demotion), the surface is HIDDEN — an invisible full-capture window
/// that traps the desktop is the worst possible failure and is never allowed.</para>
/// </remarks>
public sealed class X11InputShapeBackend : ILinuxOverlayBackend
{
    /// <summary>Consecutive trapped-X-error threshold after which the backend demotes itself.</summary>
    private const int DemotionErrorThreshold = 3;

    private readonly ILogger? _logger;
    private readonly object _xLock = new();

    private Window? _window;
    private IntPtr _display;
    private IntPtr _xid;
    private bool _xfixesAvailable;
    private bool _clickThroughEnabled;
    private bool _showRequested;
    private bool _demoted;
    private bool _neverTrapLogged;
    private int _consecutiveXErrors;
    private bool _disposed;
    private IReadOnlyList<CorePixelRect> _currentCaptureRegions = Array.Empty<CorePixelRect>();

    public X11InputShapeBackend(ILogger<X11InputShapeBackend>? logger = null)
    {
        _logger = logger;
        ProbeXFixes();
    }

    public string Name => "X11InputShapeBackend";
    public bool IsAvailable => _xfixesAvailable && !_demoted;
    public bool SupportsPerRegionInputShape => true;
    public bool SupportsTopmost => true;

    public bool IsVisible => _window?.IsVisible ?? false;

    private void ProbeXFixes()
    {
        try
        {
            lock (_xLock)
            {
                _display = XOpenDisplay(null);
                if (_display == IntPtr.Zero)
                {
                    _logger?.LogWarning("X11InputShapeBackend: Cannot open X11 display");
                    _xfixesAvailable = false;
                    return;
                }

                // Install the process-kill guard BEFORE any request that could error.
                XlibErrorTrap.RegisterDisplay(_display);

                if (XFixesQueryExtension(_display, out _, out _) == 0)
                {
                    _logger?.LogWarning("X11InputShapeBackend: XFixes extension not available");
                    CloseDisplayLocked();
                    return;
                }

                // Contract §3.1 / §7.1 row 4: input-shape regions are XFixes protocol v2
                // additions — extension presence alone is NOT enough. Gate on major >= 2.
                if (XFixesQueryVersion(_display, out int major, out int minor) == 0 || major < 2)
                {
                    _logger?.LogWarning(
                        "X11InputShapeBackend: XFixes version {Major}.{Minor} < 2, input shapes unsupported",
                        major, minor);
                    CloseDisplayLocked();
                    return;
                }

                _xfixesAvailable = true;
                _logger?.LogInformation(
                    "X11InputShapeBackend: XFixes {Major}.{Minor} available on dedicated display connection",
                    major, minor);
            }
        }
        catch (DllNotFoundException ex)
        {
            _logger?.LogWarning("X11InputShapeBackend: libX11 or libXfixes not found: {Message}", ex.Message);
            FailProbe();
        }
        catch (EntryPointNotFoundException ex)
        {
            _logger?.LogWarning("X11InputShapeBackend: missing Xlib entry point: {Message}", ex.Message);
            FailProbe();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11InputShapeBackend: Failed to probe XFixes");
            FailProbe();
        }
    }

    private void FailProbe()
    {
        _xfixesAvailable = false;
        lock (_xLock)
        {
            CloseDisplayLocked();
        }
    }

    /// <summary>Must be called under <see cref="_xLock"/>.</summary>
    private void CloseDisplayLocked()
    {
        if (_display == IntPtr.Zero) return;
        XlibErrorTrap.UnregisterDisplay(_display);
        try
        {
            XCloseDisplay(_display);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11InputShapeBackend: XCloseDisplay failed");
        }
        _display = IntPtr.Zero;
        _xfixesAvailable = false;
    }

    public void Show()
    {
        _showRequested = true;

        if (_demoted && _clickThroughEnabled)
        {
            // §1.4: a demoted backend cannot honor ambient click-through — refuse to show.
            LogNeverTrapOnce("backend demoted after repeated X errors");
            return;
        }

        EnsureWindow();
        _window!.Show();

        if (_xid == IntPtr.Zero)
        {
            AcquireXidAndApply();
        }
        else
        {
            ApplyAll();
        }

        _logger?.LogDebug("X11InputShapeBackend: Show (XID: 0x{Xid:X})", _xid);
    }

    public void Hide()
    {
        _showRequested = false;
        _window?.Hide();
        _logger?.LogDebug("X11InputShapeBackend: Hide");
    }

    public void Close()
    {
        _showRequested = false;

        if (_window != null)
        {
            _window.Opened -= OnWindowOpened;
            _window.Close();
            _window = null;
        }
        _xid = IntPtr.Zero;

        lock (_xLock)
        {
            CloseDisplayLocked();
        }

        _logger?.LogDebug("X11InputShapeBackend: Close");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
    }

    public void SetClickThrough(bool enabled)
    {
        _clickThroughEnabled = enabled;

        if (_xid != IntPtr.Zero && _display != IntPtr.Zero)
        {
            bool applied = ApplyInputShape();
            EnforceNeverTrap(applied);
        }

        // Re-show a surface that was previously suppressed by the never-trap rule once
        // the caller returns to full-capture mode (capturing input is then intended).
        if (!enabled && _showRequested && _window is { IsVisible: false })
        {
            _neverTrapLogged = false;
            Show();
        }

        _logger?.LogDebug("X11InputShapeBackend: SetClickThrough({Enabled})", enabled);
    }

    public void SetBounds(CorePixelRect rect)
    {
        EnsureWindow();
        _window!.Position = new PixelPoint((int)rect.X, (int)rect.Y);
        _window!.Width = rect.Width;
        _window!.Height = rect.Height;

        // Contract §3.3: re-apply the input shape after every bounds change (window-local
        // region coordinates may shift; monitor hotplug drives bounds updates).
        if (_xid != IntPtr.Zero && _display != IntPtr.Zero)
        {
            bool applied = ApplyInputShape();
            EnforceNeverTrap(applied);
        }
    }

    public void SetInputCaptureRegions(IReadOnlyList<CorePixelRect> captureRegions)
    {
        _currentCaptureRegions = captureRegions;
        if (_xid != IntPtr.Zero && _display != IntPtr.Zero)
        {
            bool applied = ApplyInputShape();
            EnforceNeverTrap(applied);
        }
        _logger?.LogDebug("X11InputShapeBackend: SetInputCaptureRegions({Count} regions)", captureRegions.Count);
    }

    private void EnsureWindow()
    {
        if (_window != null) return;

        _window = new Window
        {
            WindowDecorations = WindowDecorations.None,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            CanResize = false,
            ShowActivated = false,
            Focusable = false,
            IsHitTestVisible = false,
            Title = "CCP Overlay (X11)"
        };

        // Contract §3.3: Avalonia can recreate the native window on some property changes.
        // Opened fires on every native-window creation — re-acquire the XID there instead
        // of caching a stale one (a stale XID makes every shape call a trapped BadWindow).
        _window.Opened += OnWindowOpened;

        _logger?.LogInformation("X11InputShapeBackend: Created overlay window");
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        _xid = IntPtr.Zero;
        AcquireXidAndApply();
    }

    private void AcquireXidAndApply()
    {
        AcquireXid();

        if (_xid != IntPtr.Zero)
        {
            ApplyAll();
        }
        else
        {
            // §1.4: no XID means no input shape; if ambient mode is requested we must not
            // leave a full-capture window on screen.
            EnforceNeverTrap(applied: false);
        }
    }

    private void ApplyAll()
    {
        ApplyNetWmStateAbove();
        bool applied = ApplyInputShape();
        EnforceNeverTrap(applied);
    }

    private void AcquireXid()
    {
        if (_window == null) return;

        try
        {
            // Avalonia v12: TryGetPlatformHandle() → .Handle (XID) with
            // HandleDescriptor == "XID" on Linux/X11 (contract §7.1-VERIFIED, web-verified
            // 2026-07-12). This holds under XWayland too — the window is an X11 window.
            var platformHandle = _window.TryGetPlatformHandle();
            if (platformHandle?.HandleDescriptor == "XID")
            {
                _xid = platformHandle.Handle;
                _logger?.LogDebug("X11InputShapeBackend: Acquired XID 0x{Xid:X}", _xid);
            }
            else
            {
                _logger?.LogWarning(
                    "X11InputShapeBackend: TryGetPlatformHandle returned descriptor '{Descriptor}', expected 'XID'",
                    platformHandle?.HandleDescriptor ?? "(null)");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11InputShapeBackend: Failed to acquire XID");
        }
    }

    /// <summary>
    /// §1.4 enforcement: when ambient mode is requested but the shape is not in effect,
    /// hide the surface instead of trapping input. In full-capture mode
    /// (SetClickThrough(false)) a shape failure is benign — capturing is the intent and
    /// the shape-less default is full capture.
    /// </summary>
    private void EnforceNeverTrap(bool applied)
    {
        if (!_clickThroughEnabled || applied) return;

        if (_window is { IsVisible: true })
        {
            _window.Hide();
        }
        LogNeverTrapOnce("input shape could not be applied while ambient click-through is requested");
    }

    private void LogNeverTrapOnce(string reason)
    {
        if (_neverTrapLogged) return;
        _neverTrapLogged = true;
        _logger?.LogWarning(
            "X11InputShapeBackend: hiding overlay surface — {Reason} (never-trap rule, linux-overlay-contract.md §1.4)",
            reason);
    }

    private void ApplyNetWmStateAbove()
    {
        if (_display == IntPtr.Zero || _xid == IntPtr.Zero) return;

        try
        {
            lock (_xLock)
            {
                if (_display == IntPtr.Zero) return;

                XlibErrorTrap.Reset(_display);

                var root = XDefaultRootWindow(_display);
                var netWmState = XInternAtom(_display, NET_WM_STATE, false);
                var netWmStateAbove = XInternAtom(_display, NET_WM_STATE_ABOVE, false);
                var netWmStateSkipTaskbar = XInternAtom(_display, NET_WM_STATE_SKIP_TASKBAR, false);
                var netWmStateSkipPager = XInternAtom(_display, NET_WM_STATE_SKIP_PAGER, false);

                // Contract §3.2 (EWMH): post-map _NET_WM_STATE changes MUST use the client
                // message to the root window; the property form only works pre-map and is
                // ignored by compliant WMs once mapped. Idempotent — safe to re-send on
                // every Show()/re-acquisition (this doubles as topmost reassertion).
                SendNetWmStateMessage(root, netWmState, NET_WM_STATE_ADD, netWmStateAbove);
                SendNetWmStateMessage(root, netWmState, NET_WM_STATE_ADD, netWmStateSkipTaskbar);
                SendNetWmStateMessage(root, netWmState, NET_WM_STATE_ADD, netWmStateSkipPager);

                XSync(_display, false);
                int err = XlibErrorTrap.GetLastErrorCode(_display);
                if (err != 0)
                {
                    RecordXError(err, "_NET_WM_STATE client messages");
                    return;
                }

                _consecutiveXErrors = 0;
            }

            _logger?.LogDebug("X11InputShapeBackend: Applied _NET_WM_STATE_ABOVE");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11InputShapeBackend: Failed to apply _NET_WM_STATE_ABOVE");
        }
    }

    /// <summary>Must be called under <see cref="_xLock"/> with the trap already reset.</summary>
    private void SendNetWmStateMessage(IntPtr root, IntPtr netWmState, long action, IntPtr stateAtom)
    {
        var ev = new XClientMessageEvent
        {
            Type = ClientMessage,
            Window = _xid,
            MessageType = netWmState,
            Format = 32,
            Data = new ClientMessageData
            {
                L0 = action,
                L1 = stateAtom.ToInt64(),
                L2 = 0,
                L3 = 1, // Source indication: normal application
                L4 = 0
            }
        };

        XSendEvent(_display, root, false,
            SubstructureRedirectMask | SubstructureNotifyMask, ref ev);
    }

    /// <summary>
    /// Applies the §1.3 input-shape precedence. Returns true when the shape state required
    /// by the current mode is in effect (always true for full-capture mode, which is the
    /// X default even without a shape).
    /// </summary>
    private bool ApplyInputShape()
    {
        if (_display == IntPtr.Zero || _xid == IntPtr.Zero) return false;

        try
        {
            lock (_xLock)
            {
                if (_display == IntPtr.Zero) return false;

                XlibErrorTrap.Reset(_display);

                if (!_clickThroughEnabled)
                {
                    // §1.3 precedence: SetClickThrough(false) → full-window capture
                    // REGARDLESS of regions (e.g. mandatory video).
                    ResetInputShapeLocked();
                }
                else if (_currentCaptureRegions.Count > 0)
                {
                    // Ambient + capture regions: shape = union of the CAPTURING layers'
                    // rects. Clicks inside are delivered to the overlay, outside pass.
                    ApplyCaptureRegionsInputShapeLocked();
                }
                else
                {
                    // Ambient with no capture layers: empty shape, everything passes.
                    ApplyEmptyInputShapeLocked();
                }

                // XSync (not just XFlush): waits for server processing so the error trap
                // observes any BadWindow/BadMatch from THIS sequence (contract §3.3).
                XSync(_display, false);

                int err = XlibErrorTrap.GetLastErrorCode(_display);
                if (err != 0)
                {
                    RecordXError(err, "input shape");
                    // In full-capture mode a failed shape op still leaves the intended
                    // behavior (default = full capture); ambient mode must report failure.
                    return !_clickThroughEnabled;
                }

                _consecutiveXErrors = 0;
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11InputShapeBackend: Failed to apply input shape");
            return !_clickThroughEnabled;
        }
    }

    /// <summary>Must be called under <see cref="_xLock"/>.</summary>
    private void RecordXError(int errorCode, string operation)
    {
        _consecutiveXErrors++;
        _logger?.LogWarning(
            "X11InputShapeBackend: trapped X error {ErrorCode} during {Operation} (consecutive: {Count})",
            errorCode, operation, _consecutiveXErrors);

        if (_consecutiveXErrors >= DemotionErrorThreshold && !_demoted)
        {
            // Contract §3.1: repeated trapped errors demote the backend to SafeDegrade.
            _demoted = true;
            _logger?.LogError(
                "X11InputShapeBackend: demoted after {Count} consecutive X errors — ambient overlays disabled (SafeDegrade)",
                _consecutiveXErrors);
        }
    }

    /// <summary>Must be called under <see cref="_xLock"/>.</summary>
    private void ApplyEmptyInputShapeLocked()
    {
        // Empty region (0 rectangles) = every click passes through.
        IntPtr region = XFixesCreateRegion(_display, IntPtr.Zero, 0);
        XFixesSetWindowShapeRegion(_display, _xid, ShapeKind.Input, 0, 0, region);
        XFixesDestroyRegion(_display, region);
        _logger?.LogTrace("X11InputShapeBackend: Applied empty input shape (full click-through)");
    }

    /// <summary>Must be called under <see cref="_xLock"/>.</summary>
    private void ApplyCaptureRegionsInputShapeLocked()
    {
        // Window-local physical-pixel rects, clamped to the XRectangle short/ushort domain
        // (contract §3.3). Zero-area results (degenerate/out-of-range input) are skipped.
        var xrects = new List<XRectangle>(_currentCaptureRegions.Count);
        foreach (var r in _currentCaptureRegions)
        {
            var (x, y, w, h) = X11InputShapeMath.ClampRect(r.X, r.Y, r.Width, r.Height);
            if (w <= 0 || h <= 0) continue;
            xrects.Add(new XRectangle(x, y, w, h));
        }

        if (xrects.Count == 0)
        {
            ApplyEmptyInputShapeLocked();
            return;
        }

        var rectArray = xrects.ToArray();
        // XFixesCreateRegion computes the UNION of the rectangle list server-side.
        IntPtr region = XFixesCreateRegion(_display, rectArray, rectArray.Length);
        XFixesSetWindowShapeRegion(_display, _xid, ShapeKind.Input, 0, 0, region);
        XFixesDestroyRegion(_display, region);

        _logger?.LogTrace(
            "X11InputShapeBackend: Applied {Count} capture regions (per-region click-through)",
            rectArray.Length);
    }

    /// <summary>Must be called under <see cref="_xLock"/>.</summary>
    private void ResetInputShapeLocked()
    {
        // region = None (IntPtr.Zero) removes the shape → the entire window accepts input.
        XFixesSetWindowShapeRegion(_display, _xid, ShapeKind.Input, 0, 0, IntPtr.Zero);
        _logger?.LogTrace("X11InputShapeBackend: Reset input shape (full capture)");
    }
}
