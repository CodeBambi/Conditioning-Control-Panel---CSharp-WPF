using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Core.Services.Chaos;

using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosFlashOverlay: full-screen, click-through overlay for the
/// Chaos "braindrain" payload. One window is created on first use and kept alive.
/// Animated GIFs are rendered via the cross-platform SkiaSharp helper <see cref="AvaloniaAnimatedGif"/>.
/// </summary>
public partial class ChaosFlashOverlay : Window
{
    private const int DEFAULT_DURATION_MS = 10000;
    private const double DEFAULT_OPACITY = 0.10;

    private static readonly string[] Extensions =
        { ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp" };

    private static ChaosFlashOverlay? _active;
    private static readonly Random _rng = new();

    private readonly ILogger<ChaosFlashOverlay> _logger;
    private readonly Image _img;
    private readonly DispatcherTimer _life = new();
    private OpacityFade? _fade;
    private AvaloniaAnimatedGif? _anim;
    private Bitmap? _still;
    private (string path, int durationMs, double opacity)? _pending;

    /// <summary>Guards the async still-decode against a newer wash / a clear. Bumped by
    /// <see cref="ClearImage"/> (and by every <see cref="DisplayCore"/>), so an in-flight decode whose
    /// generation no longer matches is discarded instead of overwriting the current image. WPF parity
    /// (ChaosFlashOverlay.cs _displayGen).</summary>
    private int _displayGen;

    public ChaosFlashOverlay()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<ChaosFlashOverlay>>();
        _img = new Image { Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
        Content = _img;

        var (sl, st, sw, sh) = AvaloniaChaosWindowZ.StageBounds(forcePrimary: true);
        Position = new PixelPoint((int)sl, (int)st);
        Width = sw;
        Height = sh;
        Opacity = 0;

        Opened += (_, _) => ApplyExStyles();
        Loaded += (_, _) =>
        {
            if (_pending is { } p) { _pending = null; DisplayCore(p.path, p.durationMs, p.opacity); }
        };

        _life.Interval = TimeSpan.FromMilliseconds(DEFAULT_DURATION_MS);
        _life.Tick += (_, _) =>
        {
            _life.Stop();
            _fade?.Dispose();
            _fade = new OpacityFade(this, Opacity, 0, 700, () =>
            {
                ClearImage();
                try { Hide(); } catch { }
            });
        };
    }

    public static void Show(int durationMs = DEFAULT_DURATION_MS, double opacity = DEFAULT_OPACITY)
    {
        var logger = App.Services.GetRequiredService<ILogger<ChaosFlashOverlay>>();
        try
        {
            var pick = PickImage();
            if (pick == null) return;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_active == null) { _active = new ChaosFlashOverlay(); ((global::Avalonia.Controls.Window)_active).Show(); }
                    else if (!_active.IsVisible) { try { ((global::Avalonia.Controls.Window)_active).Show(); } catch { } }
                    AvaloniaChaosWindowZ.RaiseAboveVideo(_active);
                    _active.Display(pick, durationMs, opacity);
                }
                catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosFlashOverlay>>().LogInformation("ChaosFlashOverlay.Show: {E}", ex.Message); }
            });
        }
        catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosFlashOverlay>>().LogInformation("ChaosFlashOverlay.Show: {E}", ex.Message); }
    }

    public static void RaiseActive() => AvaloniaChaosWindowZ.RaiseTopmost(_active);
    public static void CloseActive() { try { _active?.CloseNow(); } catch { } }

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
            _anim = AvaloniaAnimatedGif.TryCreate(path);
            if (_anim != null)
            {
                _img.Source = _anim.Source;
                _anim.FrameRendered += (_, _) => _img.InvalidateVisual();
                _anim.Start();
            }
            else
            {
                _img.Source = AvaloniaChaosArt.TryLoad(path);
            }
        }
        else
        {
            // Decode OFF the UI thread and at display size. The old synchronous full-native-res decode
            // stalled the UI thread for the whole parse on every wash (a phone photo is 4000+ px wide —
            // ~100MB of BGRA nobody sees at a 10% full-screen wash). UniformToFill covers the stage by
            // WIDTH, so decoding at stage width is lossless on screen; the 500ms fade-in hides the gap.
            // WPF parity: ChaosFlashOverlay.cs DisplayCore.
            int decodeWidth = (int)Math.Min(2560, Math.Max(640, Width));
            string file = path;
            Task.Run(() =>
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    var bmp = Bitmap.DecodeToWidth(stream, decodeWidth);
                    Dispatcher.UIThread.Post(() =>
                    {
                        // A newer wash, a clear/teardown, or a closed window superseded this decode.
                        if (gen != _displayGen || !IsVisible)
                        {
                            try { bmp.Dispose(); } catch { }
                            return;
                        }
                        try { _still = bmp; _img.Source = bmp; }
                        catch { try { bmp.Dispose(); } catch { } }
                    });
                }
                catch (Exception ex) { _logger?.LogInformation("ChaosFlashOverlay decode: {E}", ex.Message); }
            });
        }

        double peak = Math.Clamp(opacity, 0.02, 1.0);
        _fade?.Dispose();
        _fade = new OpacityFade(this, 0, peak, 500);
        _life.Interval = TimeSpan.FromMilliseconds(Math.Max(600, durationMs));
        _life.Start();
    }

    private void ClearImage()
    {
        _displayGen++;   // orphan any still-in-flight async decode
        try { _img.Source = null; } catch { }
        _anim?.Dispose();
        _anim = null;
        _still?.Dispose();
        _still = null;
    }

    private void CloseNow()
    {
        try { _life.Stop(); } catch { }
        _fade?.Dispose();
        ClearImage();
        if (ReferenceEquals(_active, this)) _active = null;
        try { Close(); } catch { }
    }

    private static string? PickImage()
    {
        try
        {
            var files = ChaosImagePool.GetFiles(AvaloniaChaosEnv.EffectiveAssetsPath ?? "");
            if (files.Count == 0) return null;
            return files[_rng.Next(files.Count)];
        }
        catch { return null; }
    }

    private void ApplyExStyles() => ChaosWin32Helper.ApplyOverlayExStyles(this, true);
}
