using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace ConditioningControlPanel;

/// <summary>
/// EXPERIMENTAL shared-host for chaos bubbles (gated by AppSettings.ChaosBubbleSharedHost).
///
/// Instead of one top-level layered <see cref="Window"/> per bubble — each repositioned via
/// SetWindowPos every frame, which saturates the UI thread and starves click input under a dense
/// field — every bubble's visual (<c>_grid</c>) lives as a child of this ONE full-virtual-screen
/// Canvas, positioned with <see cref="Canvas"/>.SetLeft/Top (cheap, batched in one render pass).
///
/// The window is fully CLICK-THROUGH (WS_EX_TRANSPARENT): empty space passes clicks to the desktop,
/// and pops are detected by the global mouse hook (BubbleService) which swallows a hit — so a click
/// on a bubble doesn't also land on whatever is behind it. No WPF hit-testing happens here.
///
/// Keep-alive contract like every chaos overlay (see <see cref="ChaosFieldFxOverlay"/>): created
/// once at run start, closed only at teardown — layered-window churn deadlocks the render thread.
/// All Add/Remove/Place calls run on the UI thread (BubbleService spawn/animate/destroy already do).
/// </summary>
public sealed class ChaosBubbleHostOverlay : Window
{
    private static ChaosBubbleHostOverlay? _active;
    private static int _refCount;
    private readonly Canvas _canvas;
    // Mixed-DPI metrics: the host spans every monitor but WPF renders it at ONE scale (its
    // majority monitor's — in practice the primary). Placement must therefore run in PHYSICAL px
    // against the hwnd's true pixel origin, not in per-screen DIPs: a bubble on a monitor with a
    // different scale computed DIPs the host silently re-scaled, drawing it displaced/mis-sized
    // from the physical-px hit disc the mouse hook tests (second-screen "unclickable" bug).
    private double _scaleX = 1.0;
    private double _originPxX, _originPxY;
    private bool _metricsValid;

    public static bool IsReady => _active != null;

    /// <summary>The host's render scale (DPI/96 of the monitor WPF composed it for). Bubbles on a
    /// screen with a different scale compensate with a LayoutTransform of bubbleScale/RenderScale.</summary>
    public static double RenderScale => _active?._scaleX ?? 1.0;

    /// <summary>Take a reference on the host, creating + showing it if this is the first owner. Each
    /// call must be balanced by exactly one <see cref="CloseActive"/>; the window only dies at the last
    /// release. Creates synchronously when already on the UI thread (so a same-tick spawn finds the host
    /// up), else marshals the create.</summary>
    public static void EnsureCreated()
    {
        System.Threading.Interlocked.Increment(ref _refCount);
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            if (disp.CheckAccess()) { CreateNow(); return; }
            disp.BeginInvoke(() => CreateNow());
        }
        catch { }
    }

    private static void CreateNow()
    {
        try
        {
            if (_active == null)
            {
                _active = new ChaosBubbleHostOverlay();
                ((Window)_active).Show();
                ChaosWindowZ.RaiseAboveVideo(_active);
            }
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosBubbleHost.EnsureCreated: {E}", ex.Message); }
    }

    /// <summary>Add a bubble visual to the host (UI thread). No-op if the host isn't up.</summary>
    public static void Add(UIElement el)
    {
        try { if (_active != null && el != null && !_active._canvas.Children.Contains(el)) _active._canvas.Children.Add(el); }
        catch { }
    }

    /// <summary>Remove a bubble visual from the host (UI thread).</summary>
    public static void Remove(UIElement el)
    {
        try { if (_active != null && el != null) _active._canvas.Children.Remove(el); }
        catch { }
    }

    /// <summary>Position a bubble visual. Coordinates are PHYSICAL px (the cross-screen-safe
    /// currency — the same space the hook hit discs live in); the host converts into its own
    /// canvas-local DIPs via its true pixel origin + render scale. UI thread only.</summary>
    public static void Place(UIElement el, double xPx, double yPx)
    {
        var w = _active;
        if (w == null || el == null) return;
        if (!w._metricsValid) w.RefreshMetrics();
        Canvas.SetLeft(el, (xPx - w._originPxX) / w._scaleX);
        Canvas.SetTop(el, (yPx - w._originPxY) / w._scaleX);
    }

    /// <summary>Re-stack the live host above a mandatory video (see ChaosWindowZ). UI thread only.</summary>
    public static void RaiseActive() => ChaosWindowZ.RaiseTopmost(_active);

    /// <summary>Release one reference. The hwnd is torn down (run end / shutdown) only when the LAST
    /// owner releases — a chaos run ending must not close a host the ambient game still holds.</summary>
    public static void CloseActive()
    {
        int n = System.Threading.Interlocked.Decrement(ref _refCount);
        if (n > 0) return;                                                  // another owner still needs it
        if (n < 0) { System.Threading.Interlocked.Exchange(ref _refCount, 0); return; }   // unbalanced — clamp
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) { _active = null; return; }
            disp.BeginInvoke(() =>
            {
                try
                {
                    var w = _active;
                    _active = null;
                    if (w != null) { w._canvas.Children.Clear(); w.Close(); }
                }
                catch { }
            });
        }
        catch { }
    }

    private ChaosBubbleHostOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = ChaosWindowZ.BornTopmost;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;   // click-through; pops come from the global mouse hook
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // Spans the same stage as the other field overlays (primary screen, or the whole virtual
        // desktop when multi-monitor is on). Bubbles are placed in GLOBAL DIPs minus this origin.
        var (sl, st, sw, sh) = ChaosWindowZ.StageBounds();
        Left = sl; Top = st; Width = sw; Height = sh;

        _canvas = new Canvas { IsHitTestVisible = false };
        Content = _canvas;

        SourceInitialized += (_, _) => { ApplyExStyles(); RefreshMetrics(); };
        Loaded += (_, _) => RefreshMetrics();
    }

    /// <summary>Cache the hwnd's physical origin + the DPI scale WPF renders this window at.
    /// Cheap, re-run on DPI change; Place() runs per bubble per frame and must not P/Invoke.</summary>
    private void RefreshMetrics()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            if (GetWindowRect(hwnd, out var r)) { _originPxX = r.Left; _originPxY = r.Top; }
            var dpi = VisualTreeHelper.GetDpi(this);
            _scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            _metricsValid = true;
        }
        catch { }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        RefreshMetrics();
    }

    private void ApplyExStyles()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
