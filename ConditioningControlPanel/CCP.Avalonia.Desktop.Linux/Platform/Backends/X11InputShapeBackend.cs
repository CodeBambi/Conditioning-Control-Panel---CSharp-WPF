using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.Interop;
using Microsoft.Extensions.Logging;
using static ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.Interop.X11Interop;
using CorePixelRect = ConditioningControlPanel.Core.Platform.PixelRect;
using ILinuxOverlayBackend = ConditioningControlPanel.Core.Platform.ILinuxOverlayBackend;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.Backends;

/// <summary>
/// X11 overlay backend using XFixes extension for per-region click-through (Tier 1).
/// Provides full topmost via _NET_WM_STATE_ABOVE and per-region input passthrough
/// via XFixes input shape regions.
/// </summary>
/// <remarks>
/// Click-through mechanism: XFixes input shape defines which regions ACCEPT input.
/// - Empty input shape = all clicks pass through (full click-through)
/// - Input shape with capture regions = clicks inside regions are captured, outside pass through
/// 
/// Per the 2026-07-09 per-region contract: ambient layers (pink tint, spiral) pass clicks,
/// capture layers (video, flash, subliminal, brain drain, bouncing text, bubbles) capture clicks.
/// </remarks>
public sealed class X11InputShapeBackend : ILinuxOverlayBackend
{
    private readonly ILogger? _logger;
    private Window? _window;
    private IntPtr _display;
    private IntPtr _xid;
    private bool _xfixesAvailable;
    private bool _clickThroughEnabled;
    private IReadOnlyList<CorePixelRect> _currentCaptureRegions = Array.Empty<CorePixelRect>();

    public X11InputShapeBackend(ILogger<X11InputShapeBackend>? logger = null)
    {
        _logger = logger;
        ProbeXFixes();
    }

    public string Name => "X11InputShapeBackend";
    public bool IsAvailable => _xfixesAvailable;
    public bool SupportsPerRegionInputShape => true;
    public bool SupportsTopmost => true;

    public bool IsVisible => _window?.IsVisible ?? false;

    private void ProbeXFixes()
    {
        try
        {
            _display = XOpenDisplay(null);
            if (_display == IntPtr.Zero)
            {
                _logger?.LogWarning("X11InputShapeBackend: Cannot open X11 display");
                _xfixesAvailable = false;
                return;
            }

            int eventBase, errorBase;
            int result = XFixesQueryExtension(_display, out eventBase, out errorBase);
            _xfixesAvailable = result != 0;

            if (_xfixesAvailable)
            {
                _logger?.LogInformation("X11InputShapeBackend: XFixes extension available");
            }
            else
            {
                _logger?.LogWarning("X11InputShapeBackend: XFixes extension not available");
                XCloseDisplay(_display);
                _display = IntPtr.Zero;
            }
        }
        catch (DllNotFoundException ex)
        {
            _logger?.LogWarning("X11InputShapeBackend: libX11 or libXfixes not found: {Message}", ex.Message);
            _xfixesAvailable = false;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11InputShapeBackend: Failed to probe XFixes");
            _xfixesAvailable = false;
        }
    }

    public void Show()
    {
        EnsureWindow();
        _window!.Show();

        // Get the X11 window ID after window is shown
        if (_xid == IntPtr.Zero)
        {
            AcquireXid();
        }

        if (_xid != IntPtr.Zero)
        {
            ApplyNetWmStateAbove();
            ApplyInputShape();
        }

        _logger?.LogDebug("X11InputShapeBackend: Show (XID: 0x{Xid:X})", _xid);
    }

    public void Hide()
    {
        _window?.Hide();
        _logger?.LogDebug("X11InputShapeBackend: Hide");
    }

    public void Close()
    {
        _window?.Close();
        _window = null;
        _xid = IntPtr.Zero;

        if (_display != IntPtr.Zero)
        {
            XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }

        _logger?.LogDebug("X11InputShapeBackend: Close");
    }

    public void SetClickThrough(bool enabled)
    {
        _clickThroughEnabled = enabled;
        if (_xid != IntPtr.Zero && _display != IntPtr.Zero)
        {
            ApplyInputShape();
        }
        _logger?.LogDebug("X11InputShapeBackend: SetClickThrough({Enabled})", enabled);
    }

    public void SetBounds(CorePixelRect rect)
    {
        EnsureWindow();
        _window!.Position = new PixelPoint((int)rect.X, (int)rect.Y);
        _window!.Width = rect.Width;
        _window!.Height = rect.Height;

        // Re-apply input shape after bounds change (region coordinates may shift)
        if (_xid != IntPtr.Zero && _display != IntPtr.Zero)
        {
            ApplyInputShape();
        }
    }

    public void SetInputCaptureRegions(IReadOnlyList<CorePixelRect> captureRegions)
    {
        _currentCaptureRegions = captureRegions;
        if (_xid != IntPtr.Zero && _display != IntPtr.Zero)
        {
            ApplyInputShape();
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

        _logger?.LogInformation("X11InputShapeBackend: Created overlay window");
    }

    private void AcquireXid()
    {
        if (_window == null) return;

        try
        {
            // Avalonia v12 API: TryGetPlatformHandle() with HandleDescriptor "XID"
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

    private void ApplyNetWmStateAbove()
    {
        if (_display == IntPtr.Zero || _xid == IntPtr.Zero) return;

        try
        {
            var root = XDefaultRootWindow(_display);
            var netWmState = XInternAtom(_display, NET_WM_STATE, false);
            var netWmStateAbove = XInternAtom(_display, NET_WM_STATE_ABOVE, false);
            var netWmStateSkipTaskbar = XInternAtom(_display, NET_WM_STATE_SKIP_TASKBAR, false);
            var netWmStateSkipPager = XInternAtom(_display, NET_WM_STATE_SKIP_PAGER, false);

            // Send client message to add _NET_WM_STATE_ABOVE (EWMH compliant)
            SendNetWmStateMessage(root, netWmState, NET_WM_STATE_ADD, netWmStateAbove);
            SendNetWmStateMessage(root, netWmState, NET_WM_STATE_ADD, netWmStateSkipTaskbar);
            SendNetWmStateMessage(root, netWmState, NET_WM_STATE_ADD, netWmStateSkipPager);

            XFlush(_display);
            _logger?.LogDebug("X11InputShapeBackend: Applied _NET_WM_STATE_ABOVE");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11InputShapeBackend: Failed to apply _NET_WM_STATE_ABOVE");
        }
    }

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

    private void ApplyInputShape()
    {
        if (_display == IntPtr.Zero || _xid == IntPtr.Zero) return;

        try
        {
            if (_clickThroughEnabled && _currentCaptureRegions.Count == 0)
            {
                // Full click-through: empty input region (no rectangles = all clicks pass through)
                ApplyEmptyInputShape();
            }
            else if (_currentCaptureRegions.Count > 0)
            {
                // Per-region: clicks inside capture regions are captured, outside pass through
                ApplyCaptureRegionsInputShape();
            }
            else
            {
                // No click-through and no regions: full capture (reset to window bounds)
                ResetInputShape();
            }

            XFlush(_display);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11InputShapeBackend: Failed to apply input shape");
        }
    }

    private void ApplyEmptyInputShape()
    {
        // Create empty region (no rectangles) = full click-through
        IntPtr region = XFixesCreateRegion(_display, IntPtr.Zero, 0);
        XFixesSetWindowShapeRegion(_display, _xid, ShapeKind.Input, 0, 0, region);
        XFixesDestroyRegion(_display, region);
        _logger?.LogTrace("X11InputShapeBackend: Applied empty input shape (full click-through)");
    }

    private void ApplyCaptureRegionsInputShape()
    {
        // Convert PixelRect collection to XRectangle array
        var xrects = new XRectangle[_currentCaptureRegions.Count];
        for (int i = 0; i < _currentCaptureRegions.Count; i++)
        {
            var r = _currentCaptureRegions[i];
            xrects[i] = new XRectangle(
                (int)r.X,
                (int)r.Y,
                (int)r.Width,
                (int)r.Height);
        }

        // Create region from capture rectangles - clicks INSIDE these rects are captured
        IntPtr region = XFixesCreateRegion(_display, xrects, xrects.Length);
        XFixesSetWindowShapeRegion(_display, _xid, ShapeKind.Input, 0, 0, region);
        XFixesDestroyRegion(_display, region);

        _logger?.LogTrace(
            "X11InputShapeBackend: Applied {Count} capture regions (per-region click-through)",
            _currentCaptureRegions.Count);
    }

    private void ResetInputShape()
    {
        // Reset to full window capture (None region = entire window accepts input)
        // Pass IntPtr.Zero for region to remove the shape mask
        XFixesSetWindowShapeRegion(_display, _xid, ShapeKind.Input, 0, 0, IntPtr.Zero);
        _logger?.LogTrace("X11InputShapeBackend: Reset input shape (full capture)");
    }
}
