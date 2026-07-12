using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using XamlAnimatedGif;

namespace ConditioningControlPanel;

/// <summary>
/// Full-screen, click-through overlay for the Chaos "braindrain" payload: picks one random
/// image from the same pool the flashes draw on (<c>EffectiveAssetsPath/images</c>) and holds
/// it over the whole desktop at a low opacity for a few seconds, fading in then back out.
/// Animated GIFs loop; static images are shown frozen. Silent no-op if the pool is empty.
/// ONE window is created on first use and KEPT ALIVE between washes (a new Show() swaps the
/// image) — creating/closing a layered window mid-run can wedge the shared WPF render thread
/// (Application Hang 1002 — see ChaosEffectBannerOverlay). Closed only at run teardown via
/// <see cref="CloseActive"/>.
/// </summary>
public sealed class ChaosFlashOverlay : Window
{
    private const int DEFAULT_DURATION_MS = 10000;   // ~10s on screen
    private const double DEFAULT_OPACITY = 0.10;     // faint 10% wash

    private static ChaosFlashOverlay? _active;
    private static readonly Random _rng = new();

    /// <summary>Show a random flash-pool image full-screen for <paramref name="durationMs"/>
    /// at <paramref name="opacity"/> (0..1). No-op if no images are available.</summary>
    public static void Show(int durationMs = DEFAULT_DURATION_MS, double opacity = DEFAULT_OPACITY)
    {
        // Fetch the image OFF the UI thread: PickImage -> GetChaosImagePaths can do a synchronous
        // content-pack AES decrypt-to-temp under the FlashService lock, and a "glitch"/braindrain
        // trigger-bubble pop fires this ON the UI thread — that decrypt froze the dashboard bubble
        // game for pack users. Mirrors ChaosGifCascadeOverlay.Show's off-thread fetch.
        try
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                string? pick;
                try { pick = PickImage(); } catch { pick = null; }
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    ShowWithPick(pick, durationMs, opacity)));
            });
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosFlashOverlay.Show: {E}", ex.Message); }
    }

    /// <summary>UI-thread continuation of <see cref="Show"/> once the image has been picked
    /// (off-thread, because a pack pick decrypts on demand).</summary>
    private static void ShowWithPick(string? pick, int durationMs, double opacity)
    {
        try
        {
            if (pick == null) { App.Logger?.Debug("ChaosFlashOverlay: no images in pool — nothing to wash (glitch)"); return; }
            if (_active == null) { _active = new ChaosFlashOverlay(); ((Window)_active).Show(); }
            else if (!_active.IsVisible) { try { ((Window)_active).Show(); } catch { } }   // idles hidden between washes
            ChaosWindowZ.RaiseAboveVideo(_active);   // un-hiding doesn't re-stack — kick over a playing video
            // Dashboard "glitch" trigger-bubble use (no chaos run): force the singleton topmost so a
            // stale Topmost=false from a prior Free-Desktop run can't bury the wash behind the app.
            if (App.Chaos?.IsRunning != true) ChaosWindowZ.ForceTopmost(_active);
            _active.Display(pick, durationMs, opacity);
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosFlashOverlay.ShowWithPick: {E}", ex.Message); }
    }

    /// <summary>Re-stack the live window above a mandatory video (see ChaosWindowZ). UI thread only.</summary>
    public static void RaiseActive() => ChaosWindowZ.RaiseTopmost(_active);

    /// <summary>Close any active overlay immediately (run teardown).</summary>
    public static void CloseActive() { try { _active?.CloseNow(); } catch { } }

    private readonly Image _img;
    private readonly DispatcherTimer _life;
    private (string path, int durationMs, double opacity)? _pending;   // first Display can land before Loaded
    private int _displayGen;   // guards the async still-decode against a newer wash / a clear

    private ChaosFlashOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = ChaosWindowZ.BornTopmost;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // Single-monitor by default: confine to the primary screen unless the user has multi-
        // monitor on (else this layered flash surface paints on every monitor — see StageBounds).
        var (sl, st, sw, sh) = ChaosWindowZ.StageBounds();
        Left = sl; Top = st; Width = sw; Height = sh;
        Opacity = 0;

        _img = new Image { Stretch = Stretch.UniformToFill };
        Content = _img;

        SourceInitialized += (_, _) => { ApplyExStyles(); PlacePhysical(); };
        Loaded += (_, _) =>
        {
            if (_pending is { } p) { _pending = null; DisplayCore(p.path, p.durationMs, p.opacity); }
        };

        _life = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DEFAULT_DURATION_MS) };
        _life.Tick += (_, _) =>
        {
            _life.Stop();
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(700));
            // Window stays alive but HIDES between washes. Mid-run Close() of a layered
            // window is the render-thread deadlock trigger; an idle-but-visible full-screen
            // layered surface costs DWM composition every frame and stutters the GIF flashes.
            fade.Completed += (_, _) => { ClearImage(); try { Hide(); } catch { } };
            BeginAnimation(OpacityProperty, fade);
        };
    }

    private void Display(string path, int durationMs, double opacity)
    {
        if (!IsLoaded) { _pending = (path, durationMs, opacity); return; }
        DisplayCore(path, durationMs, opacity);
    }

    private void DisplayCore(string path, int durationMs, double opacity)
    {
        _life.Stop();
        ClearImage();
        int gen = ++_displayGen;

        if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
        {
            // Was XamlAnimatedGif — which decodes at NATIVE resolution with no size cap, so a
            // large GIF looped full-screen for the whole wash held ~100MB+ of resident BGRA
            // and dominated the render thread (the glitch-pop frame drops / OOM crash, #476).
            // SKCodec is format-agnostic, so GIFs ride the same capped/cached pipeline as
            // animated webp (1280px / 40 frames, off-thread, capped-still fallback). A faint
            // wash doesn't need native res; ClearImage's Detach already tears this down.
            Services.AnimatedWebp.AttachAnimation(_img, path, maxDim: 1280, maxFrames: 40);
        }
        else if (path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) && Services.AnimatedWebp.IsAnimated(path))
        {
            // Animated webp — XamlAnimatedGif is GIF-only, so these washed on as stills. Decode
            // is capped well below the still path's 2560: unlike XamlAnimatedGif's one-frame-at-
            // a-time decode, every kept frame stays resident, and a faint 10% wash doesn't need
            // native res. Decodes off-thread; ClearImage's Detach orphans a superseded decode.
            Services.AnimatedWebp.AttachAnimation(_img, path, maxDim: 1280, maxFrames: 40);
        }
        else
        {
            // Decode OFF the UI thread and at display size. The old synchronous full-native-res
            // decode stalled the UI thread for the whole parse on every wash, mid-run (a phone
            // photo is 4000+ px wide — ~100MB of BGRA nobody sees at a 10% full-screen wash).
            // UniformToFill covers the stage by WIDTH, so DecodePixelWidth at stage width is
            // lossless on screen. The wash fades in over 500ms, which hides the decode gap.
            int decodeWidth = (int)Math.Min(2560, Math.Max(640, Width));
            string file = path;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = decodeWidth;
                    bmp.UriSource = new Uri(file, UriKind.Absolute);
                    bmp.EndInit();
                    if (bmp.CanFreeze) bmp.Freeze();
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        // A newer wash (or a clear/teardown) may have superseded this decode.
                        try { if (gen == _displayGen) _img.Source = bmp; } catch { }
                    });
                }
                catch (Exception ex) { App.Logger?.Debug("ChaosFlashOverlay decode: {E}", ex.Message); }
            });
        }

        double peak = Math.Clamp(opacity, 0.02, 1.0);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, peak, TimeSpan.FromMilliseconds(500)));
        _life.Interval = TimeSpan.FromMilliseconds(Math.Max(600, durationMs));
        _life.Start();
    }

    private void ClearImage()
    {
        _displayGen++;   // orphan any still-in-flight async decode
        try { AnimationBehavior.SetSourceUri(_img, null); } catch { }
        Services.AnimatedWebp.Detach(_img);   // stops the webp frame loop (Forever clock pins _img)
        try { _img.Source = null; } catch { }
    }

    private void CloseNow()
    {
        try { _life.Stop(); } catch { }
        ClearImage();
        if (ReferenceEquals(_active, this)) _active = null;
        try { Close(); } catch { }
    }

    /// <summary>Pick one random image from the SAME enabled pool the flashes draw on (disk +
    /// content packs, honoring the asset manager's disabled set) — see FlashService.GetChaosImagePaths.
    /// Falls back to the raw folder listing only if the flash service isn't up yet. Returns null if
    /// nothing is enabled. One image = at most one pack decrypt, so it's cheap on the UI thread.</summary>
    private static string? PickImage()
    {
        try
        {
            var picks = App.Flash?.GetChaosImagePaths(1);
            if (picks != null && picks.Count > 0) return picks[0];
            // Fallback (Flash service not initialized): raw folder pool.
            var files = ChaosImagePool.GetFiles();
            if (files.Count == 0) return null;
            return files[_rng.Next(files.Count)];
        }
        catch { return null; }
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

    /// <summary>
    /// Stamps the HWND to the stage's PHYSICAL pixel bounds. StageBounds() sizes the ctor in
    /// DIPs from SystemParameters.VirtualScreen*, which use the PRIMARY monitor's scale — on a
    /// mixed-DPI desktop a spanning window rendered at one scale then falls short of the taller
    /// screen's bottom (#456). Runs at SourceInitialized, before the layered surface is realized,
    /// so this is not the forbidden mid-run resize.
    /// </summary>
    private int _dpiRestamps;   // consecutive DPI-triggered re-stamps; see OnDpiChanged

    private void PlacePhysical()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            bool dual = App.Settings?.Current?.DualMonitorEnabled ?? true;
            var b = dual
                ? System.Windows.Forms.SystemInformation.VirtualScreen
                : System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? System.Drawing.Rectangle.Empty;
            if (b.Width <= 0 || b.Height <= 0) return;

            // Already at target: converged — reset the oscillation counter and don't touch
            // the window (a redundant SetWindowPos could itself provoke a WM_DPICHANGED).
            if (GetWindowRect(hwnd, out var r) &&
                r.Left == b.X && r.Top == b.Y &&
                r.Right - r.Left == b.Width && r.Bottom - r.Top == b.Height)
            {
                _dpiRestamps = 0;
                return;
            }

            SetWindowPos(hwnd, IntPtr.Zero, b.X, b.Y, b.Width, b.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosFlashOverlay.PlacePhysical: {E}", ex.Message); }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        // A spanning window whose majority monitor changed gets a WM_DPICHANGED, and WPF then
        // re-applies the ctor's primary-scale DIP size — undoing the physical stamp. Re-place
        // after WPF finishes its own resize (Background priority). Capped: on pathological
        // mixed-DPI geometry the stamp itself can flip the majority monitor back and forth
        // (stamp → DPI change → WPF shrink → stamp → ...); rather than loop SetWindowPos on a
        // full-screen layered window (the freeze-cluster signature), give up after a few tries —
        // the next Show()/PlacePhysical convergence resets the counter.
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
