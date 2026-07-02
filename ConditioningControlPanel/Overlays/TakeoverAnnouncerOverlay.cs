using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace ConditioningControlPanel;

/// <summary>
/// Full-screen, click-through overlay that flashes a two-line cue near the top of the screen
/// whenever the Takeover (Autonomy) feature fires an effect: a small faded eyebrow "TAKEOVER"
/// above the effect name (FLASH, SPIRAL, MANTRA…). The point is *clarity* — it lets the user tell
/// a takeover-driven effect apart from an ordinary engine/scheduler effect.
///
/// On top of the plain fade it now plays a short "takeover moment": an additive Skia bloom + a
/// sparkle burst behind the rounded, bolder text, plus a quick impact shake that settles. The Skia
/// layer is an <see cref="SKElement"/> child of this same keep-alive window (mirroring the patterns
/// in <see cref="ChaosSkiaFxOverlay"/>) — no new layered window is created, so the documented
/// render-thread churn hazards don't apply. All Skia is wrapped defensively; if it ever fails the
/// cue degrades to plain (rounded, longer) text.
///
/// Like the Chaos announcer, ONE window is created on first use and KEPT ALIVE between cues
/// (each cue just swaps the label) — creating/closing a layered window churns the shared WPF
/// render thread and can wedge it (Application Hang 1002). It idles hidden between cues.
/// </summary>
public sealed class TakeoverAnnouncerOverlay : Window
{
    // ---- timing / layout tunables ----
    private const int IN_MS  = 150;    // fade-in
    private const int HOLD_MS = 1700;  // dwell (≈1s longer than before)
    private const int OUT_MS = 250;    // fade-out
    private const double PEAK_OPACITY = 1.0;    // fully opaque — more visible
    private const double EYEBROW_FONT = 24;
    private const double EFFECT_FONT  = 60;
    private const double TOP_OFFSET_DIP = 92;   // mirror the Chaos announcer's top anchor

    // Impact shake: a sharp jitter on appear that decays to still.
    private const double SHAKE_SEC = 0.26;
    private const double SHAKE_AMP = 3.5;       // peak ± offset (DIP)

    // Bloom pulse envelope (seconds since appear).
    private const float BLOOM_IN = 0.15f;       // ramp up
    private const float BLOOM_OUT = 0.70f;      // ease down
    private const float BLOOM_TOTAL = BLOOM_IN + BLOOM_OUT;

    // Palette — one calm takeover identity (violet eyebrow, white effect), unlike Chaos's per-kind colors.
    private static readonly Brush EyebrowFill = Frozen(Color.FromRgb(0xC9, 0xA0, 0xFF)); // soft violet
    private static readonly Brush EffectFill  = Frozen(Color.FromRgb(0xFF, 0xFF, 0xFF)); // white
    private static readonly Brush StrokeBrush = Frozen(Color.FromRgb(0x0B, 0x08, 0x12)); // near-black outline
    private static readonly FontFamily TakeoverFont = new("Segoe UI Rounded, Segoe UI"); // Win11 rounded face, falls back to Segoe UI

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); if (b.CanFreeze) b.Freeze(); return b; }

    private static TakeoverAnnouncerOverlay? _active;
    private static readonly Queue<string> _queue = new();
    private static bool _showing;

    /// <summary>
    /// Queue a takeover cue showing "TAKEOVER" + <paramref name="effectLabel"/>. No-op on a
    /// null/empty label or once the app is shutting down. Safe to call from any thread.
    /// </summary>
    public static void Announce(string? effectLabel)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(effectLabel)) return;
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.Invoke(() =>
            {
                _queue.Enqueue(effectLabel!);
                if (!_showing) ShowNext();
            });
        }
        catch (Exception ex) { App.Logger?.Debug("TakeoverAnnouncer.Announce: {E}", ex.Message); }
    }

    /// <summary>Drop any queued/visible cue and tear the window down (e.g. app teardown).</summary>
    public static void CloseActive()
    {
        try { _queue.Clear(); _showing = false; _active?.CloseNow(); } catch { }
    }

    private static void ShowNext()
    {
        if (_queue.Count == 0) { _showing = false; return; }
        _showing = true;
        var label = _queue.Dequeue();
        try
        {
            if (_active == null) { _active = new TakeoverAnnouncerOverlay(); ((Window)_active).Show(); }
            else if (!_active.IsVisible) { try { ((Window)_active).Show(); } catch { } }
            _active.Display(label);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("TakeoverAnnouncer.ShowNext: {E}", ex.Message);
            _showing = false;
        }
    }

    private readonly Grid _host;
    private readonly Grid _textLayer;     // swappable text content (cleared between cues)
    private SKElement? _sk;               // persistent Skia FX layer (behind the text)
    private readonly DispatcherTimer _life;
    private string? _pending;   // first Display can land before Loaded

    // ---- Skia FX state ----
    private struct Spark { public float X, Y, VX, VY, Life, Max, Size; public bool White; }
    private const int MAX_SPARKS = 48;
    private readonly Spark[] _sparks = new Spark[MAX_SPARKS];
    private int _sparkN;
    private readonly Random _rng = new();
    private float _bloomT = float.MaxValue;   // seconds since appear; MaxValue ⇒ no active bloom
    private float _centerX, _centerY;         // bloom/burst origin (window-local DIPs)

    private TranslateTransform? _shake;
    private double _shakeElapsed = double.MaxValue;

    private bool _renderHooked;
    private TimeSpan _lastRender = TimeSpan.MinValue;
    private double _topmostAccum;   // throttles the periodic HWND_TOPMOST re-assert
    private double _dpiX = 1, _dpiY = 1;

    private static readonly SKColor VioletBody = new(0xC9, 0xA0, 0xFF);
    private static readonly SKColorFilter VioletCF = SKColorFilter.CreateBlendMode(VioletBody, SKBlendMode.Modulate);
    private static readonly SKColorFilter WhiteCF = SKColorFilter.CreateBlendMode(SKColors.White, SKBlendMode.Modulate);
    private readonly SKPaint _paint = new() { BlendMode = SKBlendMode.Plus, IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
    private static SKImage? _dot;

    private TakeoverAnnouncerOverlay()
    {
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
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Opacity = 0;

        _host = new Grid();
        Content = _host;

        // Persistent Skia FX layer BEHIND the text; survives between cues so we never destroy the
        // SKElement (only the text content gets swapped). Defensive: if Skia init throws, the cue
        // still plays as plain text + shake.
        try
        {
            _sk = new SKElement { IsHitTestVisible = false };
            _sk.PaintSurface += OnPaintSurface;
            _host.Children.Add(_sk);
        }
        catch (Exception ex) { App.Logger?.Debug("TakeoverAnnouncer: Skia init failed: {E}", ex.Message); _sk = null; }

        _textLayer = new Grid();
        _host.Children.Add(_textLayer);

        SourceInitialized += (_, _) => { ApplyExStyles(); CacheDpi(); };
        Loaded += (_, _) =>
        {
            if (_pending is { } p) { _pending = null; DisplayCore(p); }
        };

        _life = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(IN_MS + HOLD_MS) };
        _life.Tick += (_, _) =>
        {
            _life.Stop();
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(OUT_MS));
            fade.Completed += (_, _) =>
            {
                try { _textLayer.Children.Clear(); } catch { }   // window stays — only content goes
                StopRender();
                _sparkN = 0;
                _bloomT = float.MaxValue;
                if (_shake != null) { _shake.X = 0; _shake.Y = 0; }
                try { _sk?.InvalidateVisual(); } catch { }
                if (_queue.Count == 0) { try { Hide(); } catch { } }
                ShowNext();
            };
            BeginAnimation(OpacityProperty, fade);
        };
    }

    private void Display(string label)
    {
        if (!IsLoaded) { _pending = label; return; }
        DisplayCore(label);
    }

    private void DisplayCore(string label)
    {
        _life.Stop();
        _life.Interval = TimeSpan.FromMilliseconds(IN_MS + HOLD_MS);

        var eyebrow = new OutlinedText
        {
            Text = "TAKEOVER",
            FontSize = EYEBROW_FONT,
            Fill = EyebrowFill,
            Stroke = StrokeBrush,
            StrokeThickness = 2.0,
            Family = TakeoverFont,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.75,   // extra-faded eyebrow
        };
        eyebrow.Build();

        var effect = new OutlinedText
        {
            Text = label.ToUpperInvariant(),
            FontSize = EFFECT_FONT,
            Fill = EffectFill,
            Stroke = StrokeBrush,
            StrokeThickness = 3.5,
            Family = TakeoverFont,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        };
        effect.Build();

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(eyebrow);
        panel.Children.Add(effect);
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Top;
        _shake = new TranslateTransform();
        panel.RenderTransform = _shake;

        // Centered over the PRIMARY work area (this window spans the whole virtual screen, so
        // centering against the window itself drifts off-monitor with a second display).
        var wa = SystemParameters.WorkArea;
        double leftMargin = Math.Max(0, wa.Left - SystemParameters.VirtualScreenLeft);
        double topMargin = Math.Max(0, wa.Top - SystemParameters.VirtualScreenTop) + TOP_OFFSET_DIP;
        var anchor = new Grid
        {
            Width = wa.Width,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(leftMargin, topMargin, 0, 0),
        };
        anchor.Children.Add(panel);
        _textLayer.Children.Clear();
        _textLayer.Children.Add(anchor);

        // FX origin in window-local DIPs: centered over the big effect word.
        _centerX = (float)(leftMargin + wa.Width / 2.0);
        _centerY = (float)(topMargin + EYEBROW_FONT + EFFECT_FONT * 0.65);
        SeedBurst();
        _bloomT = 0f;
        _shakeElapsed = 0;
        StartRender();

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, PEAK_OPACITY, TimeSpan.FromMilliseconds(IN_MS)));
        _life.Start();
    }

    // ---- Skia FX ----

    private void SeedBurst()
    {
        _sparkN = 0;
        if (_sk == null) return;
        int count = 22 + _rng.Next(7);   // ~22–28
        for (int i = 0; i < count && _sparkN < MAX_SPARKS; i++)
        {
            double ang = _rng.NextDouble() * Math.PI * 2;
            float spd = 60f + (float)_rng.NextDouble() * 110f;
            _sparks[_sparkN++] = new Spark
            {
                X = _centerX,
                Y = _centerY,
                VX = (float)Math.Cos(ang) * spd,
                VY = (float)Math.Sin(ang) * spd,
                Life = 0.5f + (float)_rng.NextDouble() * 0.35f,
                Max = 0,   // set below
                Size = 5f + (float)_rng.NextDouble() * 4f,
                White = _rng.NextDouble() < 0.35,
            };
            _sparks[_sparkN - 1].Max = _sparks[_sparkN - 1].Life;
        }
    }

    private void StartRender()
    {
        if (_sk == null || _renderHooked) return;
        try
        {
            _renderHooked = true;
            _lastRender = TimeSpan.MinValue;
            CompositionTarget.Rendering += OnRendering;
        }
        catch { _renderHooked = false; }
    }

    private void StopRender()
    {
        if (!_renderHooked) return;
        _renderHooked = false;
        try { CompositionTarget.Rendering -= OnRendering; } catch { }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        try
        {
            float dt = 1f / 60f;
            if (e is RenderingEventArgs r)
            {
                if (_lastRender == TimeSpan.MinValue) { _lastRender = r.RenderingTime; }
                else
                {
                    dt = (float)(r.RenderingTime - _lastRender).TotalSeconds;
                    _lastRender = r.RenderingTime;
                }
                if (dt <= 0) return;
                if (dt > 0.1f) dt = 0.1f;   // clamp a spike (alt-tab etc.)
            }

            // Keep this cue above the effect overlays. Pink/spiral/brain-drain windows re-assert
            // HWND_TOPMOST on OverlayService's 500ms timer, which otherwise climbs above this banner
            // and hides the cue text while an overlay effect is active (the "PINK FILTER" text not
            // showing consistently). Re-assert our own topmost a few times a second for the dwell.
            _topmostAccum += dt;
            if (_topmostAccum >= 0.2) { _topmostAccum = 0; ForceTopmost(); }

            // Advance sparkles: integrate, drag, gentle gravity, cull dead (swap-remove).
            float drag = MathF.Exp(-2.4f * dt);
            for (int i = _sparkN - 1; i >= 0; i--)
            {
                ref var s = ref _sparks[i];
                s.Life -= dt;
                if (s.Life <= 0f) { _sparks[i] = _sparks[--_sparkN]; continue; }
                s.X += s.VX * dt; s.Y += s.VY * dt;
                s.VX *= drag; s.VY *= drag;
                s.VY += 34f * dt;   // settle
            }

            // Advance bloom pulse.
            if (_bloomT != float.MaxValue) _bloomT += dt;

            // Impact shake: decaying jitter that settles to still.
            if (_shake != null && _shakeElapsed != double.MaxValue)
            {
                _shakeElapsed += dt;
                double tt = _shakeElapsed / SHAKE_SEC;
                if (tt >= 1.0)
                {
                    _shake.X = 0; _shake.Y = 0;
                    _shakeElapsed = double.MaxValue;
                }
                else
                {
                    double amp = SHAKE_AMP * (1.0 - tt);
                    _shake.X = (_rng.NextDouble() * 2 - 1) * amp;
                    _shake.Y = (_rng.NextDouble() * 2 - 1) * amp;
                }
            }

            try { _sk?.InvalidateVisual(); } catch { }

            // Idle short-circuit: nothing left to animate → unhook (text stays via window opacity).
            bool bloomDone = _bloomT == float.MaxValue || _bloomT > BLOOM_TOTAL;
            bool shakeDone = _shakeElapsed == double.MaxValue;
            if (_sparkN == 0 && bloomDone && shakeDone)
            {
                StopRender();
                _bloomT = float.MaxValue;
                try { _sk?.InvalidateVisual(); } catch { }
            }
        }
        catch (Exception ex) { App.Logger?.Debug("TakeoverAnnouncer.OnRendering: {E}", ex.Message); }
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        try
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.Save();
            canvas.Scale((float)_dpiX, (float)_dpiY);   // draw in window-local DIPs

            var img = Dot();

            // Bloom pulse behind the text.
            float env = BloomEnv(_bloomT);
            if (env > 0.001f)
            {
                float baseR = (float)EFFECT_FONT * 4.0f;
                DrawDot(canvas, img, _centerX, _centerY, baseR * (0.8f + 0.3f * env), (byte)(70 * env), VioletCF);  // wide bloom
                DrawDot(canvas, img, _centerX, _centerY, baseR * 0.5f * (0.8f + 0.3f * env), (byte)(55 * env), WhiteCF); // inner lift
            }

            // Sparkle burst: a wide dim bloom halo + a bright body + a hot core, all additive.
            for (int i = 0; i < _sparkN; i++)
            {
                ref var s = ref _sparks[i];
                float t = s.Life / s.Max;            // 1 -> 0
                float ease = t * t;                  // fade fast at the tail
                float scale = 0.35f + 0.75f * t;     // shrink as it dies
                float rad = s.Size * scale;
                var cf = s.White ? WhiteCF : VioletCF;

                DrawDot(canvas, img, s.X, s.Y, rad * 2.1f, (byte)(40 * ease), cf);   // bloom
                DrawDot(canvas, img, s.X, s.Y, rad, (byte)(210 * ease), cf);         // body
                DrawDot(canvas, img, s.X, s.Y, rad * 0.5f, (byte)(230 * ease), WhiteCF); // hot core
            }

            canvas.Restore();
        }
        catch (Exception ex) { App.Logger?.Debug("TakeoverAnnouncer.OnPaintSurface: {E}", ex.Message); }
    }

    private static float BloomEnv(float t)
    {
        if (t == float.MaxValue || t < 0f || t > BLOOM_TOTAL) return 0f;
        if (t < BLOOM_IN) return t / BLOOM_IN;
        return 1f - (t - BLOOM_IN) / BLOOM_OUT;
    }

    /// <summary>A soft white radial dot (premultiplied), tinted + additively blended per draw.</summary>
    private static SKImage Dot()
    {
        if (_dot != null) return _dot;
        const int s = 128;
        var info = new SKImageInfo(s, s, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surf = SKSurface.Create(info);
        var c = surf.Canvas;
        c.Clear(SKColors.Transparent);
        float r = s / 2f;
        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(r, r), r,
            new[] { new SKColor(255, 255, 255, 255), new SKColor(255, 255, 255, 160), new SKColor(255, 255, 255, 0) },
            new[] { 0f, 0.32f, 1f }, SKShaderTileMode.Clamp);
        using var p = new SKPaint { Shader = shader, IsAntialias = true };
        c.DrawCircle(r, r, r, p);
        _dot = surf.Snapshot();
        return _dot;
    }

    private void DrawDot(SKCanvas canvas, SKImage img, float cx, float cy, float radius, byte alpha, SKColorFilter tint)
    {
        if (radius <= 0.2f || alpha == 0) return;
        _paint.ColorFilter = tint;
        _paint.Color = new SKColor(255, 255, 255, alpha);   // global opacity multiplier for the image draw
        var dest = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
        canvas.DrawImage(img, dest, _paint);
    }

    private void CacheDpi()
    {
        try
        {
            var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice;
            if (m is Matrix mm && mm.M11 > 0 && mm.M22 > 0) { _dpiX = mm.M11; _dpiY = mm.M22; }
        }
        catch { }
    }

    private void CloseNow()
    {
        try { _life.Stop(); } catch { }
        StopRender();
        try { _textLayer.Children.Clear(); } catch { }
        if (ReferenceEquals(_active, this)) _active = null;
        try { Close(); } catch { }
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

    /// <summary>Re-assert HWND_TOPMOST so the cue stays above effect overlays that also re-assert topmost.</summary>
    private void ForceTopmost()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
