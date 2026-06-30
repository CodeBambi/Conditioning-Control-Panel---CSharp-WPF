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

    private static readonly string[] Extensions =
        { ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp" };

    private static ChaosFlashOverlay? _active;
    private static readonly Random _rng = new();

    /// <summary>Show a random flash-pool image full-screen for <paramref name="durationMs"/>
    /// at <paramref name="opacity"/> (0..1). No-op if no images are available.</summary>
    public static void Show(int durationMs = DEFAULT_DURATION_MS, double opacity = DEFAULT_OPACITY)
    {
        try
        {
            var pick = PickImage();
            if (pick == null) return;
            if (_active == null) { _active = new ChaosFlashOverlay(); ((Window)_active).Show(); }
            else if (!_active.IsVisible) { try { ((Window)_active).Show(); } catch { } }   // idles hidden between washes
            ChaosWindowZ.RaiseAboveVideo(_active);   // un-hiding doesn't re-stack — kick over a playing video
            // Dashboard "glitch" trigger-bubble use (no chaos run): force the singleton topmost so a
            // stale Topmost=false from a prior Free-Desktop run can't bury the wash behind the app.
            if (App.Chaos?.IsRunning != true) ChaosWindowZ.ForceTopmost(_active);
            _active.Display(pick, durationMs, opacity);
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosFlashOverlay.Show: {E}", ex.Message); }
    }

    /// <summary>Re-stack the live window above a mandatory video (see ChaosWindowZ). UI thread only.</summary>
    public static void RaiseActive() => ChaosWindowZ.RaiseTopmost(_active);

    /// <summary>Close any active overlay immediately (run teardown).</summary>
    public static void CloseActive() { try { _active?.CloseNow(); } catch { } }

    private readonly Image _img;
    private readonly DispatcherTimer _life;
    private (string path, int durationMs, double opacity)? _pending;   // first Display can land before Loaded

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

        SourceInitialized += (_, _) => ApplyExStyles();
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

        if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
        {
            AnimationBehavior.SetRepeatBehavior(_img, RepeatBehavior.Forever);
            AnimationBehavior.SetAutoStart(_img, true);
            AnimationBehavior.SetSourceUri(_img, new Uri(path, UriKind.Absolute));
        }
        else
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            if (bmp.CanFreeze) bmp.Freeze();
            _img.Source = bmp;
        }

        double peak = Math.Clamp(opacity, 0.02, 1.0);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, peak, TimeSpan.FromMilliseconds(500)));
        _life.Interval = TimeSpan.FromMilliseconds(Math.Max(600, durationMs));
        _life.Start();
    }

    private void ClearImage()
    {
        try { AnimationBehavior.SetSourceUri(_img, null); } catch { }
        try { _img.Source = null; } catch { }
    }

    private void CloseNow()
    {
        try { _life.Stop(); } catch { }
        ClearImage();
        if (ReferenceEquals(_active, this)) _active = null;
        try { Close(); } catch { }
    }

    /// <summary>Pick a random image file from the flash images folder, or null if none.</summary>
    private static string? PickImage()
    {
        try
        {
            var dir = Path.Combine(App.EffectiveAssetsPath ?? "", "images");
            if (!Directory.Exists(dir)) return null;
            var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
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

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
