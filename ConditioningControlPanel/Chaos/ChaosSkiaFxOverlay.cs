using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace ConditioningControlPanel;

/// <summary>
/// PROTOTYPE — the Skia-rendered Chaos FX layer. A single full-stage, click-through, keep-alive
/// overlay that owns a small additive-blended particle field driven on the WPF render clock
/// (<see cref="CompositionTarget.Rendering"/>). This is the A/B replacement for the legacy
/// WPF ellipse-pool effects: in this first slice it draws the rabbit sparkle TRAIL and the
/// Rabbit-Caller cursor GLOW with real additive bloom (SKBlendMode.Plus + layered soft sprites)
/// instead of alpha-over ellipses.
///
/// Why CPU <see cref="SKElement"/> and not GPU SKGLElement: SKElement rasterizes into a
/// WriteableBitmap that WPF composites normally, so it works on an AllowsTransparency layered
/// window today. GPU/per-pixel-alpha interop is the production follow-up once the look is signed
/// off. Keep-alive contract matches every other chaos overlay (see <see cref="ChaosCursorGlowOverlay"/>):
/// created once at run start, shown only while something is drawn, closed only at teardown — no
/// layered-window churn mid-run (that wedges the shared render thread).
///
/// Gated by <see cref="Enabled"/> (AppSettings.ChaosSkiaFxEnabled). When off, the old overlays
/// run instead; the call sites branch on the flag.
/// </summary>
public sealed class ChaosSkiaFxOverlay : Window
{
    /// <summary>A/B master switch — reads the persisted setting (default on).</summary>
    public static bool Enabled => App.Settings?.Current?.ChaosSkiaFxEnabled ?? true;

    private const int MAX_PARTICLES = 2400;

    // Trail palette (mirrors ChaosFieldFxOverlay): rabbit pink, GG-sweeper amber, hot gold core.
    private static readonly SKColor PinkBody = new(0xFF, 0x4D, 0xC4);
    private static readonly SKColor AmberBody = new(0xFF, 0x8A, 0x14);
    private static readonly SKColor GoldCore = new(0xFF, 0xE9, 0xA0);
    private static readonly SKColor CursorPink = new(0xFF, 0x8F, 0xC8);

    private static ChaosSkiaFxOverlay? _active;

    // Compositor routing: when the unified overlay host is on, the whole particle field renders
    // as a ChaosFxLayer on the shared per-monitor hosts instead of this dedicated fullscreen
    // layered window (one fewer surface presenting during a pop surge — the "explosion lag when
    // overlays are stacked"). The layer is created + registered lazily on first use and lives
    // for the app's lifetime; this window is then never created at all.
    private static Services.Compositor.ChaosFxLayer? _layer;
    private static bool UseCompositor => App.CompositorEnabled;

    // ---- public keep-alive / draw API (all thread-safe; marshalled to the UI thread) ----

    public static void EnsureCreated()
    {
        if (!Enabled) return;
        OnFx(_ => { }, w => { if (w.IsVisible) w.Hide(); });   // realizes the window/layer, then idles
    }

    /// <summary>Emit a sparkle burst at a rabbit trail point (physical px). <paramref name="warm"/>
    /// picks the amber GG-sweeper spark over the default Tail-Plug pink. Same call shape as
    /// ChaosFieldFxOverlay.TrailDot so the BubbleService call sites just branch on <see cref="Enabled"/>.</summary>
    public static void TrailDot(Point centerPx, double lifeSec, bool warm = false) =>
        OnFx(l => l.EmitTrail(centerPx, lifeSec, warm), w => w.EmitTrail(centerPx, lifeSec, warm));

    /// <summary>Trail with a travel-direction hint (dx,dy, any scale) so the sparks stream BEHIND the rabbit.</summary>
    public static void TrailDot(Point centerPx, double lifeSec, bool warm, double dirX, double dirY) =>
        OnFx(l => l.EmitTrail(centerPx, lifeSec, warm, dirX, dirY), w => w.EmitTrail(centerPx, lifeSec, warm, dirX, dirY));

    /// <summary>E-Stim: draw additive lightning bolts along the given segment endpoints (physical px).</summary>
    public static void Strike(IReadOnlyList<(Point From, Point To)> boltsPx) =>
        OnFx(l => l.AddStrike(boltsPx), w => w.AddStrike(boltsPx));

    /// <summary>Pop burst at a bubble's centre (physical px) in its payload colour: a white core
    /// flash + an expanding ring + radiating shards. <paramref name="scale"/> >1 makes a bigger
    /// release (e.g. a live snap). Colour is any WPF colour; tint filters are cached per colour.</summary>
    public static void Burst(Point centerPx, System.Windows.Media.Color color, double scale = 1.0) =>
        OnFx(l => l.EmitBurst(centerPx, ChaosBoonColors.ToSk(color), (float)scale),
             w => w.EmitBurst(centerPx, ChaosBoonColors.ToSk(color), (float)scale));

    /// <summary>An expanding additive shockwave (the Ripple cast / Size Queen / E-Stim wave). The
    /// leading edge grows LINEARLY to <paramref name="radiusPx"/> over <paramref name="lifeMs"/> so
    /// it matches the gameplay kill-front exactly; <paramref name="strong"/> adds trailing rings, a
    /// chromatic glassy edge and more front sparks for the player's right-click cast.</summary>
    public static void Ripple(Point centerPx, double radiusPx, double lifeMs, bool strong = false) =>
        OnFx(l => l.EmitRipple(centerPx, radiusPx, lifeMs, strong), w => w.EmitRipple(centerPx, radiusPx, lifeMs, strong));

    public static void ArmCursorGlow() =>
        OnFx(l => l.ArmCursor(), w => { w._cursorArmed = true; w.StartActivity(); });

    /// <summary>Disarm on BOTH paths: a mid-run UnifiedOverlayHost flip must not strand an armed
    /// glow on whichever renderer armed it (the pink/spiral act-on-state lesson).</summary>
    public static void DisarmCursorGlow()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    _layer?.DisarmCursor();
                    if (_active != null) _active._cursorArmed = false;
                }
                catch { }
            });
        }
        catch { }
    }

    public static void MoveCursorGlowToPx(double px, double py) =>
        OnFx(l => l.MoveCursor(px, py),
             w => { if (w._cursorArmed && w.Local(new Point(px, py)) is Point p) { w._cursorX = (float)p.X; w._cursorY = (float)p.Y; } });

    /// <summary>Re-stack the live window above a mandatory video (see ChaosWindowZ). UI thread only.
    /// Legacy-window path only — the compositor hosts manage their own z-order.</summary>
    public static void RaiseActive() => ChaosWindowZ.RaiseTopmost(_active);

    public static void CloseActive()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) { _active = null; return; }
            disp.BeginInvoke(() =>
            {
                try
                {
                    _layer?.Clear();   // the layer stays registered; just drop its state
                    var w = _active;
                    _active = null;
                    w?.StopRendering();
                    w?.Close();
                }
                catch { }
            });
        }
        catch { }
    }

    /// <summary>Marshal one FX call to the UI thread and route it: compositor layer when the
    /// unified host is on, else the legacy keep-alive window (created on first use).</summary>
    private static void OnFx(Action<Services.Compositor.ChaosFxLayer> viaLayer, Action<ChaosSkiaFxOverlay> viaWindow)
    {
        if (!Enabled) return;
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    if (UseCompositor)
                    {
                        if (_layer == null)
                        {
                            _layer = new Services.Compositor.ChaosFxLayer(App.Compositor!);
                            App.Compositor!.RegisterLayer(_layer);
                        }
                        viaLayer(_layer);
                        return;
                    }
                    if (_active == null)
                    {
                        _active = new ChaosSkiaFxOverlay();
                        ((Window)_active).Show();
                        _active.Hide();   // realize the hwnd, then idle hidden until something draws
                    }
                    viaWindow(_active);
                }
                catch (Exception ex) { App.Logger?.Debug("ChaosSkiaFx: {E}", ex.Message); }
            });
        }
        catch { }
    }

    // ---- instance state ----

    private readonly SKElement _sk;
    private struct Particle { public float X, Y, VX, VY, Life, Max, Size; public byte Kind; public uint Col; }   // Kind 0=pink,1=amber,2=custom(Col 0xRRGGBB)
    private readonly Particle[] _p = new Particle[MAX_PARTICLES];
    private int _n;
    private readonly Random _rng = new();

    private bool _cursorArmed;
    private float _cursorX, _cursorY;
    private float _breath;            // cursor-halo breathing phase (radians)
    private float _cursorEmitAcc;     // accumulator for the halo's drifting sparks

    private bool _rendering;
    private int _idleFrames;
    private TimeSpan _lastRender = TimeSpan.MinValue;
    private double _dpiX = 1, _dpiY = 1;

    // Hiding immediately on idle meant EVERY effect-bubble pop re-Show()ed a fullscreen layered
    // window (DWM re-composition = a visible frame hitch, and only payload bubbles paid it).
    // The render loop still parks after ~3 idle frames; the WINDOW stays up through this grace
    // so back-to-back pops reuse the already-presented (cleared, click-through) surface.
    private static readonly TimeSpan HideGrace = TimeSpan.FromSeconds(8);
    private readonly System.Windows.Threading.DispatcherTimer _hideTimer;

    // Cached tint filters (modulate the white soft-dot sprite to a colour without per-draw alloc).
    private static readonly SKColorFilter PinkCF = SKColorFilter.CreateBlendMode(PinkBody, SKBlendMode.Modulate);
    private static readonly SKColorFilter AmberCF = SKColorFilter.CreateBlendMode(AmberBody, SKBlendMode.Modulate);
    private static readonly SKColorFilter GoldCF = SKColorFilter.CreateBlendMode(GoldCore, SKBlendMode.Modulate);
    private static readonly SKColorFilter CursorCF = SKColorFilter.CreateBlendMode(CursorPink, SKBlendMode.Modulate);
    private static readonly SKColorFilter WhiteCF = SKColorFilter.CreateBlendMode(SKColors.White, SKBlendMode.Modulate);  // identity → bright white core
    private readonly SKPaint _paint = new() { BlendMode = SKBlendMode.Plus, IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
    private static SKImage? _dot;

    // Per-colour tint filters for pop bursts (keyed 0xRRGGBB) so arbitrary payload colours don't alloc per frame.
    private static readonly Dictionary<uint, SKColorFilter> _tintCache = new();
    private static SKColorFilter TintFor(uint rgb)
    {
        if (_tintCache.TryGetValue(rgb, out var f)) return f;
        f = SKColorFilter.CreateBlendMode(new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255), SKBlendMode.Modulate);
        _tintCache[rgb] = f;
        return f;
    }

    // ---- E-Stim lightning bolts ----
    private static readonly SKColorFilter ElectricCF = SKColorFilter.CreateBlendMode(new SKColor(0x42, 0xDC, 0xE6), SKBlendMode.Modulate);
    private static readonly SKColor BoltCoreColor = new(0xBF, 0xEC, 0xFF);   // electric white-blue core
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

    // ---- Ripple shockwaves ----
    private static readonly SKColor RipplePinkInner = new(0xFF, 0x4D, 0xC4);
    private readonly SKPaint _ringGlow = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, BlendMode = SKBlendMode.Plus, StrokeCap = SKStrokeCap.Round, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f) };
    private readonly SKPaint _ringCore = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, BlendMode = SKBlendMode.Plus, StrokeCap = SKStrokeCap.Round };
    private sealed class RippleFx { public SKPoint C; public float MaxR, Age, Life; public bool Strong; public SKColor Color; }
    private readonly List<RippleFx> _ripples = new();

    private ChaosSkiaFxOverlay()
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
        var (sl, st, sw, sh) = ChaosWindowZ.StageBounds();
        Left = sl; Top = st; Width = sw; Height = sh;

        _sk = new SKElement { IsHitTestVisible = false };
        _sk.PaintSurface += OnPaintSurface;
        Content = _sk;

        SourceInitialized += (_, _) => { ApplyExStyles(); CacheDpi(); };

        _hideTimer = new System.Windows.Threading.DispatcherTimer { Interval = HideGrace };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (!_rendering) { try { Hide(); } catch { } }
        };
    }

    /// <summary>A soft white radial dot (premultiplied), tinted + additively blended per particle.</summary>
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

        // Travel direction → a unit "behind" vector. The burst originates a touch behind the
        // rabbit and the sparks carry a backward bias, so the trail reads as STREAMING BEHIND
        // it rather than puffing out around it.
        double dl = Math.Sqrt(dirX * dirX + dirY * dirY);
        float bx = 0, by = 0;
        double cx = c0.X, cy = c0.Y;
        if (dl > 0.0001) { bx = (float)(-dirX / dl); by = (float)(-dirY / dl); cx += bx * 14; cy += by * 14; }

        float life = (float)Math.Max(0.30, lifeSec) * 1.3f;   // slightly longer-lived
        byte kind = (byte)(warm ? 1 : 0);

        // A bright core flash that drifts back along the trail...
        Add((float)cx, (float)cy, bx * 28f, by * 28f, life * 0.5f, 20f, kind);
        // ...with a small spread of sparks (lower count per burst — the denser emit cadence
        // in BubbleService carries the density now).
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

    /// <summary>Pop burst: a white core flash, an expanding ring, and radiating shards, all in
    /// the bubble's payload colour. Read fast at the tail like every other particle.</summary>
    private void EmitBurst(Point centerPx, SKColor col, float scale)
    {
        if (Local(centerPx) is not Point c) return;
        if (scale <= 0.05f) scale = 1f;
        float cx = (float)c.X, cy = (float)c.Y;
        uint rgb = (uint)(col.Red << 16 | col.Green << 8 | col.Blue);

        // Bright central flash (slow, fat — the "release").
        Add(cx, cy, 0f, 0f, 0.24f, 24f * scale, 2, rgb);
        // A crisp expanding ring.
        int ring = 10 + (int)(2 * scale);
        for (int i = 0; i < ring; i++)
        {
            double a = i / (double)ring * Math.PI * 2;
            float spd = (120f + (float)_rng.NextDouble() * 40f) * scale;
            Add(cx, cy, (float)Math.Cos(a) * spd, (float)Math.Sin(a) * spd, 0.34f, 6.5f * scale, 2, rgb);
        }
        // Scattered shards with a little upward bias + gravity settle.
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

    /// <summary>Build a jagged path between two points (perpendicular midpoint displacement).</summary>
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

    /// <summary>Rebuild a bolt's jagged main path + 0-2 short forks so the current dances.</summary>
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
            float t = bolt.Life / bolt.Max;                 // 1 -> 0
            float a = t > 0.4f ? 1f : t / 0.4f;             // hold hot, then fade
            _boltGlow.Color = bolt.Color.WithAlpha((byte)(120 * a));
            _boltGlow.StrokeWidth = 6.5f;
            _boltCore.Color = BoltCoreColor.WithAlpha((byte)(235 * a));
            _boltCore.StrokeWidth = 1.8f;

            DrawPolyline(canvas, bolt.Main, _boltGlow);
            foreach (var br in bolt.Branches) DrawPolyline(canvas, br, _boltGlow);
            DrawPolyline(canvas, bolt.Main, _boltCore);
            _boltCore.Color = BoltCoreColor.WithAlpha((byte)(160 * a));   // forks read a touch dimmer
            foreach (var br in bolt.Branches) DrawPolyline(canvas, br, _boltCore);

            float fr = 16f * a + 6f;
            DrawDot(canvas, img, bolt.B.X, bolt.B.Y, fr, (byte)(200 * a), ElectricCF);        // strike flash
            DrawDot(canvas, img, bolt.A.X, bolt.A.Y, fr * 0.7f, (byte)(150 * a), ElectricCF); // source spark
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
        float rDip = (float)(radiusPx / Math.Max(0.0001, _dpiX));
        float life = (float)Math.Max(0.1, lifeMs / 1000.0);
        var col = ChaosBoonColors.ToSk(ChaosBoonColors.Electric);
        _ripples.Add(new RippleFx { C = new SKPoint((float)c.X, (float)c.Y), MaxR = rDip, Age = 0, Life = life, Strong = strong, Color = col });

        // A ring of sparks thrown outward with the front (they drag/settle, so they read as
        // foam scattering off the wave rather than a perfect tracking ring).
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
            float t = rp.Age / rp.Life;        // 0 → 1
            if (t >= 1f) continue;
            float fade = 1f - t;               // overall dim as it dies
            float lead = rp.MaxR * t;          // LINEAR front — matches the kill radius
            var col = rp.Color;

            // Cast flash at the origin (early only).
            if (t < 0.35f)
            {
                float ff = 1f - t / 0.35f;
                DrawDot(canvas, img, rp.C.X, rp.C.Y, (rp.Strong ? 34f : 22f) * (0.6f + 0.4f * ff), (byte)(200 * ff), ElectricCF);
            }

            // Leading wavefront: a blurred glow stroke + a hot white-blue core, thinning as it spreads.
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
                // Chromatic glassy edge: cyan just outside, pink just inside the core.
                _ringCore.Color = col.WithAlpha((byte)(90 * fade));
                _ringCore.StrokeWidth = Math.Max(1f, w * 0.4f);
                canvas.DrawCircle(rp.C, lead + 3f, _ringCore);
                _ringCore.Color = RipplePinkInner.WithAlpha((byte)(70 * fade));
                canvas.DrawCircle(rp.C, Math.Max(0f, lead - 3f), _ringCore);

                // Trailing concentric rings lag behind the front for depth (look only).
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
            _hideTimer.Stop();
            if (!IsVisible)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Show();
                sw.Stop();
                if (sw.ElapsedMilliseconds >= 20)
                    App.Logger?.Information("[POPLAG] ChaosSkiaFx window Show() took {Ms}ms", sw.ElapsedMilliseconds);
            }
            ChaosWindowZ.RaiseAboveVideo(this);
            _idleFrames = 0;
            if (!_rendering)
            {
                _rendering = true;
                _lastRender = TimeSpan.MinValue;
                CompositionTarget.Rendering += OnRendering;
            }
        }
        catch { }
    }

    private void StopRendering()
    {
        if (!_rendering) return;
        _rendering = false;
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

            // Advance particles: integrate, damp, gentle gravity, cull dead (swap-remove).
            float drag = MathF.Exp(-2.4f * dt);
            for (int i = _n - 1; i >= 0; i--)
            {
                ref var p = ref _p[i];
                p.Life -= dt;
                if (p.Life <= 0f) { _p[i] = _p[--_n]; continue; }
                p.X += p.VX * dt; p.Y += p.VY * dt;
                p.VX *= drag; p.VY *= drag;
                p.VY += 34f * dt;   // settle
            }

            // Cursor halo: breathe + shed an occasional drifting spark for life.
            if (_cursorArmed)
            {
                _breath += dt * (float)(Math.PI * 2 / 1.24);   // ~620ms half-cycle, matches the old halo
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

            // Advance lightning bolts: fade over life, re-jitter during the hot window.
            for (int i = _bolts.Count - 1; i >= 0; i--)
            {
                var bolt = _bolts[i];
                bolt.Life -= dt;
                if (bolt.Life <= 0f) { _bolts.RemoveAt(i); continue; }
                bolt.JitterAcc += dt;
                if (bolt.Life > bolt.Max * 0.4f && bolt.JitterAcc >= 0.04f) { bolt.JitterAcc = 0f; Rejitter(bolt); }
            }

            // Advance ripples (linear front, cull when spent).
            for (int i = _ripples.Count - 1; i >= 0; i--)
            {
                var rp = _ripples[i];
                rp.Age += dt;
                if (rp.Age >= rp.Life) _ripples.RemoveAt(i);
            }

            _sk.InvalidateVisual();   // request a paint of the current snapshot

            if (_n == 0 && _bolts.Count == 0 && _ripples.Count == 0 && !_cursorArmed)
            {
                // Park the render loop right away (last painted frame is already cleared), but
                // leave the window visible for HideGrace so the next pop doesn't pay a Show().
                if (++_idleFrames > 2) { StopRendering(); _hideTimer.Stop(); _hideTimer.Start(); }
            }
            else _idleFrames = 0;
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosSkiaFx.OnRendering: {E}", ex.Message); }
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale((float)_dpiX, (float)_dpiY);   // draw in window-local DIPs

        var img = Dot();
        // Particles: a wide dim bloom halo + the bright body, both additive.
        for (int i = 0; i < _n; i++)
        {
            ref var p = ref _p[i];
            float t = p.Life / p.Max;                 // 1 -> 0
            float ease = t * t;                       // fade fast at the tail
            float scale = (0.35f + 0.75f * t);        // shrink as it dies
            float rad = p.Size * scale;
            var cf = p.Kind switch { 1 => AmberCF, 2 => TintFor(p.Col), _ => PinkCF };
            var coreCf = p.Kind == 2 ? WhiteCF : GoldCF;   // colour-agnostic white flash for pop bursts

            DrawDot(canvas, img, p.X, p.Y, rad * 2.1f, (byte)(40 * ease), cf);    // bloom
            DrawDot(canvas, img, p.X, p.Y, rad, (byte)(210 * ease), cf);          // body
            DrawDot(canvas, img, p.X, p.Y, rad * 0.5f, (byte)(230 * ease), coreCf); // hot core
        }

        if (_cursorArmed)
        {
            float breath = 0.92f + 0.14f * MathF.Sin(_breath);
            const float baseR = 34f;
            DrawDot(canvas, img, _cursorX, _cursorY, baseR * breath * 1.8f, 55, CursorCF);  // outer bloom
            DrawDot(canvas, img, _cursorX, _cursorY, baseR * breath, 150, CursorCF);        // halo
            DrawDot(canvas, img, _cursorX, _cursorY, baseR * breath * 0.42f, 120, GoldCF);  // gold heart
        }

        if (_ripples.Count > 0) DrawRipples(canvas, img);
        if (_bolts.Count > 0) DrawBolts(canvas, img);

        canvas.Restore();
    }

    private void DrawDot(SKCanvas canvas, SKImage img, float cx, float cy, float radius, byte alpha, SKColorFilter tint)
    {
        if (radius <= 0.2f || alpha == 0) return;
        _paint.ColorFilter = tint;
        _paint.Color = new SKColor(255, 255, 255, alpha);   // global opacity multiplier for the image draw
        var dest = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
        canvas.DrawImage(img, dest, _paint);
    }

    /// <summary>Physical px → this window's local DIPs (it spans the stage).</summary>
    private Point? Local(Point px)
    {
        var t = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        if (t == null) return null;
        var p = t.Value.Transform(px);
        return new Point(p.X - Left, p.Y - Top);
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
