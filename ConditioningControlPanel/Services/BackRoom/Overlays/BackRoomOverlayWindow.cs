using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services.BackRoom.Overlays;

/// <summary>The monitors, physical pixels (WinForms reports them per-monitor-DPI-correct under PerMonitorV2).</summary>
internal static class BackRoomOverlayScreens
{
    public static IReadOnlyList<FxScreenInfo> All()
    {
        var list = new List<FxScreenInfo>();
        try
        {
            foreach (var s in System.Windows.Forms.Screen.AllScreens)
                list.Add(new FxScreenInfo(new PxRect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height), s.Primary));
        }
        catch (Exception ex) { Diag.Swallowed(ex, "screen list"); }
        return list;
    }
}

/// <summary>
/// The shape every Hypno v3 overlay shares (CONTRACT 10.13.B): one click-through, never-activated, topmost
/// layered window over the whole virtual screen, stamped to physical pixels, drawn on a Canvas in its own
/// DIPs. Same keep-alive discipline as ChaosFlashOverlay: made once on first use, idles invisible, hides
/// after a quiet spell, never closed or resized mid-run (a layered window torn down or resized mid-run can
/// wedge the shared render thread). A frame loop runs only while something is drawing, capped per overlay.
/// UI thread only; the static entry points marshal.
/// </summary>
internal abstract class BackRoomOverlayWindow : Window
{
    protected readonly Canvas Stage = new() { IsHitTestVisible = false };
    private readonly DispatcherTimer _hideGrace;
    private readonly int _frameMs;
    private readonly PxRect _virtualPx;
    private bool _framing;
    private long _lastFrame;
    private int _dpiRestamps;

    protected BackRoomOverlayWindow(int frameMs)
    {
        _frameMs = frameMs;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var v = System.Windows.Forms.SystemInformation.VirtualScreen;
        _virtualPx = new PxRect(v.X, v.Y, v.Width, v.Height);
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Content = Stage;

        SourceInitialized += (_, _) => { ApplyExStyles(); PlacePhysical(); };
        _hideGrace = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _hideGrace.Tick += (_, _) =>
        {
            _hideGrace.Stop();
            if (_framing) return;
            try { Hide(); } catch (Exception ex) { Diag.Swallowed(ex, "overlay hide"); }
            OnIdleHidden();
        };
    }

    /// <summary>The virtual screen's physical origin, which <see cref="Local"/> measures from.</summary>
    protected PxRect VirtualPx => _virtualPx;

    protected double PxPerDip
    {
        get
        {
            try { var d = VisualTreeHelper.GetDpi(this).DpiScaleX; return d > 0 ? d : 1; }
            catch { return 1; }
        }
    }

    /// <summary>Physical px -> this window's canvas DIPs.</summary>
    protected PxRect Local(PxRect px) => BackRoomOverlayMath.ToLocalDip(px, _virtualPx, PxPerDip);

    /// <summary>Make the window a new effect's home. False while a display change is settling and the
    /// window does not exist yet (a fresh layered surface then is the crash the coordinator prevents).</summary>
    protected static bool MayCreate(object? existing)
        => existing != null || !UI.DisplayChangeCoordinator.SpawnsSuppressed;

    protected static void OnUi(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null || d.HasShutdownStarted) return;
        void Run() { try { a(); } catch (Exception ex) { App.Logger?.Debug("[BackRoom] overlay: {E}", ex.Message); } }
        if (d.CheckAccess()) Run(); else d.BeginInvoke(new Action(Run));
    }

    /// <summary>Start drawing: visible, on top, frame loop on.</summary>
    protected void Wake()
    {
        _hideGrace.Stop();
        if (!IsVisible) { try { Show(); } catch (Exception ex) { Diag.Swallowed(ex, "overlay show"); } }
        ChaosWindowZ.ForceTopmost(this);
        if (_framing) return;
        _framing = true;
        _lastFrame = 0;
        CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>Nothing left to draw: frame loop off, hide after the grace.</summary>
    protected void Sleep()
    {
        if (_framing)
        {
            CompositionTarget.Rendering -= OnRendering;
            _framing = false;
        }
        _hideGrace.Stop();
        _hideGrace.Start();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        long now = Environment.TickCount64;
        if (_lastFrame != 0 && now - _lastFrame < _frameMs) return;
        long dt = _lastFrame == 0 ? _frameMs : now - _lastFrame;
        _lastFrame = now;
        bool more;
        try { more = Frame(now, dt); }
        catch (Exception ex) { App.Logger?.Debug("[BackRoom] overlay frame: {E}", ex.Message); more = false; }
        if (!more) Sleep();
    }

    /// <summary>Draw one frame. Return false when nothing is left on screen.</summary>
    protected abstract bool Frame(long nowMs, long dtMs);

    /// <summary>The window just hid after a quiet spell: drop anything heavy it kept.</summary>
    protected virtual void OnIdleHidden() { }

    /// <summary>
    /// Put a local picture on <paramref name="img"/>: animated through the shared capped, off-thread
    /// decoder, or (still) its first frame decoded off the UI thread at <paramref name="maxDim"/>.
    /// </summary>
    protected static void SetPicture(Image img, string path, bool still, int maxDim)
    {
        ClearPicture(img);
        if (!still)
        {
            AnimatedWebp.AttachAnimation(img, path, maxDim, maxFrames: 40);
            return;
        }
        var token = new object();
        img.Tag = token;
        Task.Run(() =>
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = maxDim;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                if (bmp.CanFreeze) bmp.Freeze();
                OnUi(() => { if (ReferenceEquals(img.Tag, token)) img.Source = bmp; });
            }
            catch (Exception ex) { App.Logger?.Debug("[BackRoom] overlay still decode: {E}", ex.Message); }
        });
    }

    protected static void ClearPicture(Image img)
    {
        img.Tag = null;
        AnimatedWebp.Detach(img);
        img.Source = null;
    }

    private void ApplyExStyles()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT);
        }
        catch (Exception ex) { Diag.Swallowed(ex, "overlay ex styles"); }
    }

    /// <summary>Stamp the HWND to the virtual screen's physical bounds (the DIP size from SystemParameters
    /// uses the primary monitor's scale and falls short on a mixed-DPI desktop). At SourceInitialized only.</summary>
    private void PlacePhysical()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            var b = _virtualPx;
            if (GetWindowRect(hwnd, out var r) && r.Left == (int)b.X && r.Top == (int)b.Y
                && r.Right - r.Left == (int)b.W && r.Bottom - r.Top == (int)b.H)
            {
                _dpiRestamps = 0;
                return;
            }
            SetWindowPos(hwnd, IntPtr.Zero, (int)b.X, (int)b.Y, (int)b.W, (int)b.H, SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch (Exception ex) { App.Logger?.Debug("[BackRoom] overlay placement: {E}", ex.Message); }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        // Same capped re-stamp as ChaosFlashOverlay: WPF re-applies the DIP size on a majority-monitor change.
        if (_dpiRestamps >= 4) return;
        _dpiRestamps++;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(PlacePhysical));
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
}
