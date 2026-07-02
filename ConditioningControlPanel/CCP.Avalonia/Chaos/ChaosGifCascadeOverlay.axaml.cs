using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Core.Services.Chaos;

using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosGifCascadeOverlay: full-screen click-through falling image cascade.
/// Animated GIFs are decoded via the cross-platform SkiaSharp helper <see cref="AvaloniaAnimatedGif"/>.
/// </summary>
public partial class ChaosGifCascadeOverlay : Window
{
    private readonly ILogger<ChaosGifCascadeOverlay> _logger;


    private static readonly string[] Extensions =
        { ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp" };

    private const int MAX_CONCURRENT = 14;
    private const long ANIMATED_MAX_BYTES = 3_000_000;

    private static ChaosGifCascadeOverlay? _active;
    private static readonly Random _rng = new();

    private readonly Canvas _canvas;
    private List<string> _files = new();
    private double _gifSize = 200;
    private double _fallSpeed = 4;
    private double _opacity = 1.0;
    private double _startScale = 1.0;
    private readonly List<Faller> _fallers = new();
    private readonly DispatcherTimer _spawn = new();
    private readonly DispatcherTimer _life = new();
    private readonly DispatcherTimer _step = new();
    private bool _spawning;
    private DateTime _lastStep = DateTime.MinValue;

    private sealed class Faller
    {
        public Image Img = null!;
        public AvaloniaAnimatedGif? Anim;
        public double Y;
        public double CenterX;
        public double Speed;
        public TranslateTransform Move = null!;
        public ScaleTransform Grow = null!;
    }

    public ChaosGifCascadeOverlay()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<ChaosGifCascadeOverlay>>();
WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = AvaloniaChaosWindowZ.BornTopmost;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var (sl, st, sw, sh) = AvaloniaChaosWindowZ.StageBounds(forcePrimary: true);
        Position = new PixelPoint((int)sl, (int)st);
        Width = sw;
        Height = sh;

        _canvas = new Canvas { IsHitTestVisible = false };
        Content = _canvas;

        _spawn.Interval = TimeSpan.FromMilliseconds(500);
        _spawn.Tick += (_, _) => SpawnOne();

        _life.Interval = TimeSpan.FromSeconds(8);
        _life.Tick += (_, _) => { _life.Stop(); _spawning = false; _spawn.Stop(); };

        _step.Interval = TimeSpan.FromMilliseconds(16);
        _step.Tick += StepTick;

        Opened += (_, _) => ApplyExStyles();
    }

    public static void Show(double spawnRatePerSec, double durationSec, double gifSize, double fallSpeed, double opacity, double startScale = 1.0)
    {
        var logger = App.Services.GetRequiredService<ILogger<Faller>>();
        try
        {
            var files = PickFiles();
            if (files.Count == 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_active == null) { _active = new ChaosGifCascadeOverlay(); ((global::Avalonia.Controls.Window)_active).Show(); }
                    else if (!_active.IsVisible) { try { ((global::Avalonia.Controls.Window)_active).Show(); } catch { } }
                    AvaloniaChaosWindowZ.RaiseAboveVideo(_active);
                    _active.Restart(files, spawnRatePerSec, durationSec, gifSize, fallSpeed, opacity, startScale);
                }
                catch (Exception ex) { App.Services?.GetRequiredService<ILogger<Faller>>().LogInformation("ChaosGifCascadeOverlay.Show: {E}", ex.Message); }
            });
        }
        catch (Exception ex) { App.Services?.GetRequiredService<ILogger<Faller>>().LogInformation("ChaosGifCascadeOverlay.Show: {E}", ex.Message); }
    }

    public static void RaiseActive() => AvaloniaChaosWindowZ.RaiseTopmost(_active);

    public static void CloseActive() { try { _active?.CloseNow(); } catch { } }

    public static bool IsRaining
    {
        get { try { var a = _active; return a != null && (a._spawning || a._fallers.Count > 0); } catch { return false; } }
    }

    private void Restart(List<string> files, double spawnRatePerSec, double durationSec,
                         double gifSize, double fallSpeed, double opacity, double startScale)
    {
        StopAndClear();
        _files = files;
        _gifSize = Math.Clamp(gifSize, 40, 600);
        _fallSpeed = Math.Clamp(fallSpeed, 0.5, 30);
        _opacity = Math.Clamp(opacity, 0.05, 1.0);
        _startScale = Math.Clamp(startScale, 0.1, 1.0);
        _spawn.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(0.05, spawnRatePerSec));
        _life.Interval = TimeSpan.FromSeconds(Math.Max(1.0, durationSec));
        _spawning = true;
        SpawnOne();
        _spawn.Start();
        _life.Start();
        _lastStep = DateTime.UtcNow;
        _step.Start();
    }

    private void SpawnOne()
    {
        if (!_spawning) return;
        if (_fallers.Count >= MAX_CONCURRENT) return;
        try
        {
            string path = _files[_rng.Next(_files.Count)];
            var img = new Image { Stretch = Stretch.Uniform, Opacity = _opacity };

            double centerX = _gifSize / 2 + _rng.NextDouble() * Math.Max(1, Width - _gifSize);
            double y = -_gifSize;
            var move = new TranslateTransform(0, y);
            var grow = new ScaleTransform(ScaleAt(y), ScaleAt(y));
            var tg = new TransformGroup();
            tg.Children.Add(grow);
            tg.Children.Add(move);
            img.Width = _gifSize;
            img.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            img.RenderTransform = tg;
            Canvas.SetLeft(img, centerX - _gifSize / 2);
            Canvas.SetTop(img, 0);

            var faller = new Faller
            {
                Img = img,
                CenterX = centerX,
                Y = y,
                Speed = _fallSpeed * (0.7 + _rng.NextDouble() * 0.6),
                Move = move,
                Grow = grow,
            };

            _canvas.Children.Add(img);
            _fallers.Add(faller);

            string file = path;
            bool isGif = file.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
            Task.Run(() =>
            {
                try
                {
                    if (isGif)
                    {
                        var anim = AvaloniaAnimatedGif.TryCreate(file);
                        if (anim != null)
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                try
                                {
                                    faller.Anim = anim;
                                    img.Source = anim.Source;
                                    anim.FrameRendered += (_, _) => img.InvalidateVisual();
                                    anim.Start();
                                }
                                catch { anim.Dispose(); }
                            });
                            return;
                        }
                    }

                    using var stream = File.OpenRead(file);
var bmp = new Bitmap(stream);
                    Dispatcher.UIThread.Post(() => { try { img.Source = bmp; } catch { } });
                }
                catch (Exception ex) { _logger?.LogInformation("GifCascade decode: {E}", ex.Message); }
            });
        }
        catch (Exception ex) { _logger?.LogInformation("GifCascade spawn: {E}", ex.Message); }
    }

    private void StepTick(object? sender, EventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            double dt = _lastStep == DateTime.MinValue ? 0.016 : (now - _lastStep).TotalSeconds;
            _lastStep = now;
            if (dt <= 0) return;
            if (dt > 0.1) dt = 0.1;
            double frameScale = dt / 0.016;

            for (int i = _fallers.Count - 1; i >= 0; i--)
            {
                var f = _fallers[i];
                f.Y += f.Speed * frameScale;
                double s = ScaleAt(f.Y);
                f.Grow.ScaleX = s;
                f.Grow.ScaleY = s;
                f.Move.Y = f.Y;
                if (f.Y > Height + _gifSize)
                {
                    try { f.Img.Source = null; }
                    catch { }
                    f.Anim?.Dispose();
                    f.Anim = null;
                    _canvas.Children.Remove(f.Img);
                    _fallers.RemoveAt(i);
                }
            }
            if (!_spawning && _fallers.Count == 0)
            {
                GoIdle();
                try { Hide(); } catch { }
            }
        }
        catch (Exception ex) { _logger?.LogInformation("GifCascade step: {E}", ex.Message); }
    }

    private double ScaleAt(double y)
    {
        if (_startScale >= 1.0) return 1.0;
        double p = Math.Clamp(y / Math.Max(1.0, Height * 0.75), 0, 1);
        return _startScale + (1.0 - _startScale) * p;
    }

    private void GoIdle()
    {
        try { _spawn.Stop(); } catch { }
        try { _step.Stop(); } catch { }
        try { _life.Stop(); } catch { }
    }

    private void StopAndClear()
    {
        GoIdle();
        foreach (var f in _fallers)
        {
            try { f.Img.Source = null; } catch { }
            f.Anim?.Dispose();
            f.Anim = null;
        }
        _fallers.Clear();
        try { _canvas.Children.Clear(); } catch { }
    }

    private void CloseNow()
    {
        StopAndClear();
        if (ReferenceEquals(_active, this)) _active = null;
        try { Close(); } catch { }
    }

    private static List<string> PickFiles()
    {
        try
        {
            return ChaosImagePool.GetFiles(AvaloniaChaosEnv.EffectiveAssetsPath ?? "");
        }
        catch { return new List<string>(); }
    }

    private void ApplyExStyles() => ChaosWin32Helper.ApplyOverlayExStyles(this, true);
}
