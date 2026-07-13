using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// One shared fullscreen overlay host per monitor. Borderless, transparent, topmost, and
/// permanently click-through at BOTH levels (WPF hit-testing off + WS_EX_TRANSPARENT), it
/// renders all active compositor layers for its monitor through a single SKElement.
/// Created once and hidden/re-shown, never destroyed mid-run (layered-window churn deadlocks
/// the WPF render thread - see the chaos overlay keep-alive rule).
/// </summary>
public class CompositorHostWindow : Window, ICompositorHost
{
    private readonly SKElement _surface;
    private readonly bool _excludeFromCapture;
    private System.Drawing.Rectangle _screenBoundsPx;
    private readonly double _dpiScale;
    private IntPtr _hwnd = IntPtr.Zero;

    /// <summary>Device name of the screen this host covers (System.Windows.Forms.Screen.DeviceName).</summary>
    public string ScreenDeviceName { get; }

    /// <summary>True when this host is the capture-excluded surface (brain drain et al.).</summary>
    public bool IsExcludedSurface => _excludeFromCapture;

    /// <summary>Monitor DPI scale at creation (dpi / 96).</summary>
    public double DpiScale => _dpiScale;

    /// <summary>This host's monitor rectangle in device pixels (world-space layers render
    /// relative to it — see IWpfLayer.WorldSpacePx).</summary>
    public System.Drawing.Rectangle ScreenBoundsPx => _screenBoundsPx;

    /// <summary>Raised by the SKElement when the surface repaints; the engine draws the layers.</summary>
    public event Action<CompositorHostWindow, SKPaintSurfaceEventArgs>? PaintSurface;

    public CompositorHostWindow(System.Windows.Forms.Screen screen, bool excludeFromCapture)
    {
        ScreenDeviceName = screen.DeviceName;
        _screenBoundsPx = screen.Bounds;
        _excludeFromCapture = excludeFromCapture;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // Approximate DIP placement so WPF opens the window on the right monitor; the exact
        // cover is enforced in device pixels via SetWindowPos once the handle exists (the
        // mixed-DPI lesson from the lock card #539 fix: DIP layout renders with a stale scale
        // on secondary monitors, device-pixel bounds do not).
        var dpi = GetDpiScaleForScreen(screen);
        _dpiScale = dpi;
        Left = screen.Bounds.X / dpi;
        Top = screen.Bounds.Y / dpi;
        Width = screen.Bounds.Width / dpi;
        Height = screen.Bounds.Height / dpi;

        _surface = new SKElement();
        _surface.PaintSurface += (s, e) => PaintSurface?.Invoke(this, e);
        Content = _surface;

        SourceInitialized += OnSourceInitialized;
        Activated += (s, e) => ApplyNativeState();
        StateChanged += (s, e) =>
        {
            if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
            ApplyNativeState();
        };
    }

    /// <summary>Repaint the Skia surface (engine tick calls this once per frame while active).</summary>
    public void InvalidateSurface() => _surface.InvalidateVisual();

    /// <summary>Re-target the host after display topology changes.</summary>
    public void UpdateScreenBounds(System.Windows.Forms.Screen screen)
    {
        _screenBoundsPx = screen.Bounds;
        ApplyNativeState();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        ApplyNativeState();

        if (_excludeFromCapture && _hwnd != IntPtr.Zero)
        {
            // Brain-drain surface only: keep the effect out of screenshots/streams and out of
            // the app's own capture loops (self-capture feedback). Never apply this to the
            // main surface - subliminal/flash/spiral visibility in capture is a product decision.
            SetWindowDisplayAffinity(_hwnd, WDA_EXCLUDEFROMCAPTURE);
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        // Re-assert the physical cover; WPF's own DIP re-layout on DPI change is what
        // produced stale-scale under-covering on mixed-DPI setups (#539).
        ApplyNativeState();
    }

    /// <summary>
    /// Applies click-through ex-styles and the exact device-pixel monitor cover. Re-run on
    /// every native lifecycle edge (source init, activation, state/DPI change): styles and
    /// bounds are both known to decay across those transitions.
    /// </summary>
    private void ApplyNativeState()
    {
        if (_hwnd == IntPtr.Zero) return;
        try
        {
            var ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE,
                ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);
            SetWindowPos(_hwnd, HWND_TOPMOST,
                _screenBoundsPx.X, _screenBoundsPx.Y, _screenBoundsPx.Width, _screenBoundsPx.Height,
                SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        }
        catch (Exception ex2)
        {
            App.Logger?.Warning(ex2, "CompositorHostWindow: failed to apply native state for {Screen}", ScreenDeviceName);
        }
    }

    internal static double GetDpiScaleForScreen(System.Windows.Forms.Screen screen)
    {
        try
        {
            var pt = new System.Drawing.Point(screen.Bounds.X + screen.Bounds.Width / 2,
                                              screen.Bounds.Y + screen.Bounds.Height / 2);
            var monitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero &&
                GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
            {
                return dpiX / 96.0;
            }
        }
        catch { /* fall through to primary-DPI assumption */ }
        return 1.0;
    }

    #region Win32

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x0011;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    #endregion
}
