using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Point = Avalonia.Point;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of the Skia-rendered Chaos FX layer.
/// A single full-stage, click-through, keep-alive overlay that owns a small additive-blended
/// particle field driven on a dispatcher timer.
///
/// This draws the rabbit sparkle TRAIL, the Rabbit-Caller cursor GLOW, pop BURSTS,
/// E-Stim LIGHTNING, and Ripple shockwaves with additive bloom (SKBlendMode.Plus).
///
/// Keep-alive contract: created once at run start, shown only while something is drawn,
/// closed only at teardown — no layered-window churn mid-run.
///
/// Gated by AppSettings.ChaosSkiaFxEnabled. When off, the legacy overlays run instead.
/// </summary>
public sealed class ChaosSkiaFxOverlay : Window
{
    /// <summary>A/B master switch — reads the persisted setting (default on).</summary>
    public static bool Enabled =>
        (App.Services?.GetService<ConditioningControlPanel.Core.Services.Settings.ISettingsService>()?.Current as ConditioningControlPanel.Models.AppSettings)?.ChaosSkiaFxEnabled ?? true;

    private const int MAX_PARTICLES = 2400;

    // Trail palette (mirrors ChaosFieldFxOverlay): rabbit pink, GG-sweeper amber, hot gold core.
    private static readonly SKColor PinkBody = new(0xFF, 0x4D, 0xC4);
    private static readonly SKColor AmberBody = new(0xFF, 0x8A, 0x14);
    private static readonly SKColor GoldCore = new(0xFF, 0xE9, 0xA0);
    private static readonly SKColor CursorPink = new(0xFF, 0x8F, 0xC8);

    private static ChaosSkiaFxOverlay? _active;

    // ---- public keep-alive / draw API (all thread-safe; marshalled to the UI thread) ----

    public static void EnsureCreated()
    {
        if (!Enabled) return;
        OnUi(w => { });
        OnUi(w => { if (w.IsVisible) w.Hide(); });
    }

    public static void TrailDot(Point centerPx, double lifeSec, bool warm = false) =>
        OnUi(w => w.EmitTrail(centerPx, lifeSec, warm));

    public static void TrailDot(Point centerPx, double lifeSec, bool warm, double dirX, double dirY) =>
        OnUi(w => w.EmitTrail(centerPx, lifeSec, warm, dirX, dirY));

    public static void Strike(IReadOnlyList<(Point From, Point To)> boltsPx) =>
        OnUi(w => w.AddStrike(boltsPx));

    public static void Burst(Point centerPx, Color color, double scale = 1.0) =>
        OnUi(w => w.EmitBurst(centerPx, ChaosBoonColors.ToSk(color), (float)scale));

    public static void Ripple(Point centerPx, double radiusPx, double lifeMs, bool strong = false) =>
        OnUi(w => w.EmitRipple(centerPx, radiusPx, lifeMs, strong));

    public static void ArmCursorGlow() => OnUi(w => { w._cursorArmed = true; w.StartActivity(); });
    public static void DisarmCursorGlow() => OnUi(w => w._cursorArmed = false);

    public static void MoveCursorGlowToPx(double px, double py) =>
        OnUi(w => { if (w._cursorArmed && w.Local(new Point(px, py)) is Point p) { w._cursorX = (float)p.X; w._cursorY = (float)p.Y; } });

    public static void RaiseActive() => AvaloniaChaosWindowZ.RaiseTopmost(_active);

    public static void CloseActive()
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
                TryClose();
            else
                Dispatcher.UIThread.Post(TryClose);
        }
        catch { }
    }

    private static void OnUi(Action<ChaosSkiaFxOverlay> act)
    {
        if (!Enabled) return;
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
                TryAct(act);
            else
                Dispatcher.UIThread.Post(() => TryAct(act));
        }
        catch { }
    }

    private static void TryAct(Action<ChaosSkiaFxOverlay> act)
    {
        try
        {
            if (_active == null)
            {
                _active = new ChaosSkiaFxOverlay();
                _active.Show();
                _active.Hide();
            }
            act(_active);
        }
        catch
        {
            // swallow; diagnostics must never break a run
        }
    }

    private static void TryClose()
    {
        try
        {
            var w = _active;
            _active = null;
            w?.StopRendering();
            w?.Close();
        }
        catch { }
    }

    // ---- instance state ----

    private readonly SkiaFxControl _sk;

    private struct Particle { public float X, Y, VX, VY, Life, Max, Size; public byte Kind; public uint Col; }
    private readonly Particle[] _p = new Particle[MAX_PARTICLES];
    private int _n;
    private readonly Random _rng = new();

    private bool _cursorArmed;
    private float _cursorX, _cursorY;
    private float _breath;
    private float _cursorEmitAcc;

    private bool _rendering;
    private int _idleFrames;
    private DateTime _lastRender = DateTime.MinValue;
    private DispatcherTimer? _timer;

    private static readonly SKColorFilter PinkCF = SKColorFilter.CreateBlendMode(PinkBody, SKBlendMode.Modulate);
    private static readonly SKColorFilter AmberCF = SKColorFilter.CreateBlendMode(AmberBody, SKBlendMode.Modulate);
    private static readonly SKColorFilter GoldCF = SKColorFilter.CreateBlendMode(GoldCore, SKBlendMode.Modulate);
    private static readonly SKColorFilter CursorCF = SKColorFilter.CreateBlendMode(CursorPink, SKBlendMode.Modulate);
    private static readonly SKColorFilter WhiteCF = SKColorFilter.CreateBlendMode(SKColors.White, SKBlendMode.Modulate);
    private readonly SKPaint _paint = new() { BlendMode = SKBlendMode.Plus, IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
    private static SKImage? _dot;

    private static readonly Dictionary<uint, SKColorFilter> _tintCache = new();
    private static SKColorFilter TintFor(uint rgb)
    {
        if (_tintCache.TryGetValue(rgb, out var f)) return f;
        f = SKColorFilter.CreateBlendMode(new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255), SKBlendMode.Modulate);
        _tintCache[rgb] = f;
        return f;
    }

    private static readonly SKColorFilter ElectricCF = SKColorFilter.CreateBlendMode(new SKColor(0x42, 0xDC, 0xE6), SKBlendMode.Modulate);
    private static readonly SKColor BoltCoreColor = new(0xBF, 0xEC, 0xFF);
    private readonly SKPaint _boltGlow = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, BlendMode = SKBlendMode.Plus, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5f) };
    private readonly SKPaint _boltCore = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, BlendMode = SKBlendMode.Plus, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private sealed class Bolt
    {
        public SKPoint A, B;
        public SKPoint[] Main = Array.Empty<SKPoint>();
        public readonly List<SKPoint[]> Branches = new();
        public float Life, Max, JitterAcc;
        public SKColor Color;
    }
    private readonly List<Bolt> _bolts = new();

    private static readonly SKColor RipplePinkInner = new(0xFF, 0x4D, 0xC4);
    private readonly SKPaint _ringGlow = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, BlendMode = SKBlendMode.Plus, StrokeCap = SKStrokeCap.Round, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f) };
    private readonly SKPaint _ringCore = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, BlendMode = SKBlendMode.Plus, StrokeCap = SKStrokeCap.Round };
    private sealed class RippleFx { public SKPoint C; public float MaxR, Age, Life; public bool Strong; public SKColor Color; }
    private readonly List<RippleFx> _ripples = new();

    private ChaosSkiaFxOverlay()
    {
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

        var (sl, st, sw, sh) = AvaloniaChaosWindowZ.StageBoundsDip();
        Position = new PixelPoint((int)sl, (int)st);
        Width = sw;   // DIP (StageBoundsDip already divided the physical span by scaling)
        Height = sh;

        _sk = new SkiaFxControl(this) { IsHitTestVisible = false };
        Content = _sk;

        Opened += (_, _) => ApplyExStyles();
    }

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

    private void EmitTrail(Point centerPx, double lifeSec, bool warm, double dirX = 0, double dirY = 0)
    {
        if (Local(centerPx) is not Point c0) return;
        double dl = Math.Sqrt(dirX * dirX + dirY * dirY);
        float bx = 0, by = 0;
        double cx = c0.X, cy = c0.Y;
        if (dl > 0.0001) { bx = (float)(-dirX / dl); by = (float)(-dirY / dl); cx += bx * 14; cy += by * 14; }

        float life = (float)Math.Max(0.30, lifeSec) * 1.3f;
        byte kind = (byte)(warm ? 1 : 0);

        Add((float)cx, (float)cy, bx * 28f, by * 28f, life * 0.5f, 20f, kind);
        int sparks = 3 + _rng.Next(2);
        for (int i = 0; i < sparks; i++)
        {
            double ang = _rng.NextDouble() * Math.PI * 2;
            float spd = 10f + (float)_rng.NextDouble() * 30f;
            float vx = (float)Math.Cos(ang) * spd + bx * 48f;
            float vy = (float)Math.Sin(ang) * spd + by * 48f - 12f;
            Add((float)cx, (float)cy, vx, vy, life * (0.65f + (float)_rng.NextDouble() * 0.35f),
                6f + (float)_rng.NextDouble() * 6f, kind);
        }
        StartActivity();
    }

    private void EmitBurst(Point centerPx, SKColor col, float scale)
    {
        if (Local(centerPx) is not Point c) return;
        if (scale <= 0.05f) scale = 1f;
        float cx = (float)c.X, cy = (float)c.Y;
        uint rgb = (uint)(col.Red << 16 | col.Green << 8 | col.Blue);

        Add(cx, cy, 0f, 0f, 0.24f, 24f * scale, 2, rgb);
        int ring = 10 + (int)(2 * scale);
        for (int i = 0; i < ring; i++)
        {
            double a = i / (double)ring * Math.PI * 2;
            float spd = (120f + (float)_rng.NextDouble() * 40f) * scale;
            Add(cx, cy, (float)Math.Cos(a) * spd, (float)Math.Sin(a) * spd, 0.34f, 6.5f * scale, 2, rgb);
        }
        int shards = 7 + _rng.Next(5);
        for (int i = 0; i < shards; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2;
            float spd = (40f + (float)_rng.NextDouble() * 150f) * scale;
            Add(cx, cy, (float)Math.Cos(a) * spd, (float)Math.Sin(a) * spd - 20f,
                0.30f + (float)_rng.NextDouble() * 0.35f, (5f + (float)_rng.NextDouble() * 7f) * scale, 2, rgb);
        }
        StartActivity();
    }

    private void Add(float x, float y, float vx, float vy, float life, float size, byte kind, uint col = 0)
    {
        if (_n >= MAX_PARTICLES) return;
        _p[_n++] = new Particle { X = x, Y = y, VX = vx, VY = vy, Life = life, Max = life, Size = size, Kind = kind, Col = col };
    }

    private void AddStrike(IReadOnlyList<(Point From, Point To)> boltsPx)
    {
        if (boltsPx == null || boltsPx.Count == 0) return;
        var color = ChaosBoonColors.ToSk(ChaosBoonColors.Electric);
        foreach (var (fromPx, toPx) in boltsPx)
        {
            if (Local(fromPx) is not Point a || Local(toPx) is not Point b) continue;
            var bolt = new Bolt
            {
                A = new SKPoint((float)a.X, (float)a.Y),
                B = new SKPoint((float)b.X, (float)b.Y),
                Life = 0.20f, Max = 0.20f, Color = color,
            };
            Rejitter(bolt);
            _bolts.Add(bolt);
        }
        StartActivity();
    }

    private SKPoint[] BuildBolt(SKPoint a, SKPoint b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        int mids = Math.Max(2, (int)(len / 55f));
        float px = len > 0.001f ? -dy / len : 0f, py = len > 0.001f ? dx / len : 0f;
        var pts = new SKPoint[mids + 2];
        pts[0] = a;
        for (int m = 1; m <= mids; m++)
        {
            float f = m / (float)(mids + 1);
            float off = (float)(_rng.NextDouble() * 2 - 1) * 16f;
            pts[m] = new SKPoint(a.X + dx * f + px * off, a.Y + dy * f + py * off);
        }
        pts[mids + 1] = b;
        return pts;
    }

    private void Rejitter(Bolt bolt)
    {
        bolt.Main = BuildBolt(bolt.A, bolt.B);
        bolt.Branches.Clear();
        int forks = _rng.Next(3);
        for (int k = 0; k < forks && bolt.Main.Length > 2; k++)
        {
            int idx = 1 + _rng.Next(bolt.Main.Length - 2);
            var origin = bolt.Main[idx];
            float ang = (float)(_rng.NextDouble() * Math.PI * 2);
            float flen = 20f + (float)_rng.NextDouble() * 55f;
            var end = new SKPoint(origin.X + MathF.Cos(ang) * flen, origin.Y + MathF.Sin(ang) * flen);
            bolt.Branches.Add(BuildBolt(origin, end));
        }
    }

    private void DrawBolts(SKCanvas canvas, SKImage img)
    {
        foreach (var bolt in _bolts)
        {
            float t = bolt.Life / bolt.Max;
            float a = t > 0.4f ? 1f : t / 0.4f;
            _boltGlow.Color = bolt.Color.WithAlpha((byte)(120 * a));
            _boltGlow.StrokeWidth = 6.5f;
            _boltCore.Color = BoltCoreColor.WithAlpha((byte)(235 * a));
            _boltCore.StrokeWidth = 1.8f;

            DrawPolyline(canvas, bolt.Main, _boltGlow);
            foreach (var br in bolt.Branches) DrawPolyline(canvas, br, _boltGlow);
            DrawPolyline(canvas, bolt.Main, _boltCore);
            _boltCore.Color = BoltCoreColor.WithAlpha((byte)(160 * a));
            foreach (var br in bolt.Branches) DrawPolyline(canvas, br, _boltCore);

            float fr = 16f * a + 6f;
            DrawDot(canvas, img, bolt.B.X, bolt.B.Y, fr, (byte)(200 * a), ElectricCF);
            DrawDot(canvas, img, bolt.A.X, bolt.A.Y, fr * 0.7f, (byte)(150 * a), ElectricCF);
        }
    }

    private static void DrawPolyline(SKCanvas canvas, SKPoint[] pts, SKPaint paint)
    {
        if (pts.Length < 2) return;
        using var path = new SKPath();
        path.MoveTo(pts[0]);
        for (int i = 1; i < pts.Length; i++) path.LineTo(pts[i]);
        canvas.DrawPath(path, paint);
    }

    private void EmitRipple(Point centerPx, double radiusPx, double lifeMs, bool strong)
    {
        if (Local(centerPx) is not Point c) return;
        float rDip = (float)(radiusPx / RenderScaling);
        float life = (float)Math.Max(0.1, lifeMs / 1000.0);
        var col = ChaosBoonColors.ToSk(ChaosBoonColors.Electric);
        _ripples.Add(new RippleFx { C = new SKPoint((float)c.X, (float)c.Y), MaxR = rDip, Age = 0, Life = life, Strong = strong, Color = col });

        uint rgb = (uint)(col.Red << 16 | col.Green << 8 | col.Blue);
        int motes = strong ? 14 : 7;
        float frontSpeed = rDip / life;
        for (int i = 0; i < motes; i++)
        {
            double ang = Math.PI * 2 * i / motes + 0.2;
            float ux = (float)Math.Cos(ang), uy = (float)Math.Sin(ang);
            Add((float)c.X + ux * rDip * 0.15f, (float)c.Y + uy * rDip * 0.15f,
                ux * frontSpeed * 0.85f, uy * frontSpeed * 0.85f,
                life * 0.85f, strong ? 6f : 4.5f, 2, rgb);
        }
        StartActivity();
    }

    private void DrawRipples(SKCanvas canvas, SKImage img)
    {
        foreach (var rp in _ripples)
        {
            float t = rp.Age / rp.Life;
            if (t >= 1f) continue;
            float fade = 1f - t;
            float lead = rp.MaxR * t;
            var col = rp.Color;

            if (t < 0.35f)
            {
                float ff = 1f - t / 0.35f;
                DrawDot(canvas, img, rp.C.X, rp.C.Y, (rp.Strong ? 34f : 22f) * (0.6f + 0.4f * ff), (byte)(200 * ff), ElectricCF);
            }

            float baseW = rp.Strong ? 7f : 4.5f;
            float w = baseW * (0.5f + 0.5f * fade);
            _ringGlow.Color = col.WithAlpha((byte)(110 * fade));
            _ringGlow.StrokeWidth = w * 2.2f;
            canvas.DrawCircle(rp.C, lead, _ringGlow);
            _ringCore.Color = BoltCoreColor.WithAlpha((byte)(230 * fade));
            _ringCore.StrokeWidth = Math.Max(1f, w * 0.5f);
            canvas.DrawCircle(rp.C, lead, _ringCore);

            if (rp.Strong)
            {
                _ringCore.Color = col.WithAlpha((byte)(90 * fade));
                _ringCore.StrokeWidth = Math.Max(1f, w * 0.4f);
                canvas.DrawCircle(rp.C, lead + 3f, _ringCore);
                _ringCore.Color = RipplePinkInner.WithAlpha((byte)(70 * fade));
                canvas.DrawCircle(rp.C, Math.Max(0f, lead - 3f), _ringCore);

                _ringGlow.Color = col.WithAlpha((byte)(60 * fade));
                _ringGlow.StrokeWidth = w * 1.4f;
                if (lead > 10f) canvas.DrawCircle(rp.C, lead * 0.7f, _ringGlow);
                if (lead > 20f) canvas.DrawCircle(rp.C, lead * 0.45f, _ringGlow);
            }
        }
    }

    private void StartActivity()
    {
        try
        {
            if (!IsVisible) Show();
            AvaloniaChaosWindowZ.RaiseAboveVideo(this);
            _idleFrames = 0;
            if (!_rendering)
            {
                _rendering = true;
                _lastRender = DateTime.MinValue;
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                _timer.Tick += OnTimerTick;
                _timer.Start();
            }
        }
        catch { }
    }

    private void StopRendering()
    {
        if (!_rendering) return;
        _rendering = false;
        try { _timer?.Stop(); _timer = null; } catch { }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            float dt = 1f / 60f;
            var now = DateTime.UtcNow;
            if (_lastRender == DateTime.MinValue) _lastRender = now;
            else
            {
                dt = (float)(now - _lastRender).TotalSeconds;
                _lastRender = now;
            }
            if (dt <= 0) return;
            if (dt > 0.1f) dt = 0.1f;

            float drag = MathF.Exp(-2.4f * dt);
            for (int i = _n - 1; i >= 0; i--)
            {
                ref var p = ref _p[i];
                p.Life -= dt;
                if (p.Life <= 0f) { _p[i] = _p[--_n]; continue; }
                p.X += p.VX * dt; p.Y += p.VY * dt;
                p.VX *= drag; p.VY *= drag;
                p.VY += 34f * dt;
            }

            if (_cursorArmed)
            {
                _breath += dt * (float)(Math.PI * 2 / 1.24);
                _cursorEmitAcc += dt;
                if (_cursorEmitAcc >= 0.09f)
                {
                    _cursorEmitAcc = 0f;
                    double ang = _rng.NextDouble() * Math.PI * 2;
                    float spd = 8f + (float)_rng.NextDouble() * 18f;
                    Add(_cursorX, _cursorY, (float)Math.Cos(ang) * spd, (float)Math.Sin(ang) * spd - 10f,
                        0.5f + (float)_rng.NextDouble() * 0.3f, 6f + (float)_rng.NextDouble() * 4f, 0);
                }
            }

            for (int i = _bolts.Count - 1; i >= 0; i--)
            {
                var bolt = _bolts[i];
                bolt.Life -= dt;
                if (bolt.Life <= 0f) { _bolts.RemoveAt(i); continue; }
                bolt.JitterAcc += dt;
                if (bolt.Life > bolt.Max * 0.4f && bolt.JitterAcc >= 0.04f) { bolt.JitterAcc = 0f; Rejitter(bolt); }
            }

            for (int i = _ripples.Count - 1; i >= 0; i--)
            {
                var rp = _ripples[i];
                rp.Age += dt;
                if (rp.Age >= rp.Life) _ripples.RemoveAt(i);
            }

            _sk.InvalidateVisual();

            if (_n == 0 && _bolts.Count == 0 && _ripples.Count == 0 && !_cursorArmed)
            {
                if (++_idleFrames > 2) { StopRendering(); try { Hide(); } catch { } }
            }
            else _idleFrames = 0;
        }
        catch { }
    }

    private void RenderFx(SKCanvas canvas, SKImage img)
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale((float)RenderScaling, (float)RenderScaling);

        for (int i = 0; i < _n; i++)
        {
            ref var p = ref _p[i];
            float t = p.Life / p.Max;
            float ease = t * t;
            float scale = (0.35f + 0.75f * t);
            float rad = p.Size * scale;
            var cf = p.Kind switch { 1 => AmberCF, 2 => TintFor(p.Col), _ => PinkCF };
            var coreCf = p.Kind == 2 ? WhiteCF : GoldCF;

            DrawDot(canvas, img, p.X, p.Y, rad * 2.1f, (byte)(40 * ease), cf);
            DrawDot(canvas, img, p.X, p.Y, rad, (byte)(210 * ease), cf);
            DrawDot(canvas, img, p.X, p.Y, rad * 0.5f, (byte)(230 * ease), coreCf);
        }

        if (_cursorArmed)
        {
            float breath = 0.92f + 0.14f * MathF.Sin(_breath);
            const float baseR = 34f;
            DrawDot(canvas, img, _cursorX, _cursorY, baseR * breath * 1.8f, 55, CursorCF);
            DrawDot(canvas, img, _cursorX, _cursorY, baseR * breath, 150, CursorCF);
            DrawDot(canvas, img, _cursorX, _cursorY, baseR * breath * 0.42f, 120, GoldCF);
        }

        if (_ripples.Count > 0) DrawRipples(canvas, img);
        if (_bolts.Count > 0) DrawBolts(canvas, img);

        canvas.Restore();
    }

    private void DrawDot(SKCanvas canvas, SKImage img, float cx, float cy, float radius, byte alpha, SKColorFilter tint)
    {
        if (radius <= 0.2f || alpha == 0) return;
        _paint.ColorFilter = tint;
        _paint.Color = new SKColor(255, 255, 255, alpha);
        var dest = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
        canvas.DrawImage(img, dest, _paint);
    }

    private Point? Local(Point px)
    {
        // px is physical screen px; the Skia field draws in the window's DIPs (canvas.Scale is
        // RenderScaling). Position is physical px too, so subtract it in the SAME (physical) space
        // BEFORE dividing to DIPs: (px - Position) / scale.
        var scale = RenderScaling;
        if (scale <= 0) return null;
        return new Point((px.X - Position.X) / scale, (px.Y - Position.Y) / scale);
    }

    private void ApplyExStyles() => ChaosWin32Helper.ApplyOverlayExStyles(this, true);

    /// <summary>Custom Avalonia control that renders the particle field via Skia.</summary>
    private sealed class SkiaFxControl : Control
    {
        private readonly ChaosSkiaFxOverlay _owner;

        public SkiaFxControl(ChaosSkiaFxOverlay owner)
        {
            _owner = owner;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.Custom(new FxDrawOp(_owner, new Rect(0, 0, Bounds.Width, Bounds.Height)));
        }
    }

    private sealed class FxDrawOp : ICustomDrawOperation
    {
        private readonly ChaosSkiaFxOverlay _owner;

        public FxDrawOp(ChaosSkiaFxOverlay owner, Rect bounds)
        {
            _owner = owner;
            Bounds = bounds;
        }

        public Rect Bounds { get; }

        public bool HitTest(Point p) => false;

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null) return;
            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            _owner.RenderFx(canvas, Dot());
        }

        public void Dispose() { }

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
        public override bool Equals(object? obj) => obj is ICustomDrawOperation other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_owner, Bounds);
    }
}
