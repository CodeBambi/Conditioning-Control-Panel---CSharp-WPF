using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// The chaos FX particle field — pop bursts, rabbit sparkle trails, E-Stim lightning, ripple
/// shockwaves and the Rabbit-Caller cursor glow — as a compositor layer. This is
/// <see cref="ChaosSkiaFxOverlay"/>'s render loop moved onto the shared host: a pop surge no
/// longer presents its own fullscreen layered window on top of the active overlay hosts, which
/// was the residual "explosion lag when overlays are stacked".
///
/// World-space layer (<see cref="WorldSpacePx"/>): all sim state lives in VIRTUAL-DESKTOP device
/// pixels, so the physical-px points call sites already pass (bubble centres, cursor, bolt
/// endpoints) are used verbatim. The legacy overlay's DIP-tuned constants are multiplied by the
/// stage scale <see cref="_s"/> (the primary monitor's DPI scale) at emit/draw — visually
/// identical to the legacy window, which rendered the whole stage at that one scale.
///
/// Threading: emit methods are UI-thread only (ChaosSkiaFxOverlay's statics marshal every call);
/// Update/Render run on the engine tick. The sim is a straight port — keep the two in lockstep
/// until the legacy window is deleted.
/// </summary>
public sealed class ChaosFxLayer : BaseLayer
{
    private const int MAX_PARTICLES = 2400;

    // Trail palette (mirrors ChaosSkiaFxOverlay): rabbit pink, GG-sweeper amber, hot gold core.
    private static readonly SKColor PinkBody = new(0xFF, 0x4D, 0xC4);
    private static readonly SKColor AmberBody = new(0xFF, 0x8A, 0x14);
    private static readonly SKColor GoldCore = new(0xFF, 0xE9, 0xA0);
    private static readonly SKColor CursorPink = new(0xFF, 0x8F, 0xC8);

    public ChaosFxLayer(CompositorEngine engine) : base(engine)
    {
        RefreshScale();
    }

    public override int ZIndex => CompositorLayers.Fx;

    public override bool WorldSpacePx => true;

    // ---- sim state (all coordinates/velocities/sizes in virtual-desktop device px) ----

    private struct Particle { public float X, Y, VX, VY, Life, Max, Size; public byte Kind; public uint Col; }   // Kind 0=pink,1=amber,2=custom(Col 0xRRGGBB)
    private readonly Particle[] _p = new Particle[MAX_PARTICLES];
    private int _n;
    private readonly Random _rng = new();

    private bool _cursorArmed;
    private float _cursorX, _cursorY;
    private float _breath;            // cursor-halo breathing phase (radians)
    private float _cursorEmitAcc;     // accumulator for the halo's drifting sparks

    /// <summary>Stage scale: the primary monitor's DPI/96. Multiplies every DIP-tuned legacy
    /// constant so the layer matches the legacy window (one scale for the whole stage).</summary>
    private float _s = 1f;

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
    private readonly SKPaint _boltGlow = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, BlendMode = SKBlendMode.Plus, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
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
    private readonly SKPaint _ringGlow = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, BlendMode = SKBlendMode.Plus, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _ringCore = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, BlendMode = SKBlendMode.Plus, StrokeCap = SKStrokeCap.Round };
    private sealed class RippleFx { public SKPoint C; public float MaxR, Age, Life; public bool Strong; public SKColor Color; }
    private readonly List<RippleFx> _ripples = new();

    /// <summary>Re-sample the primary monitor's scale and rebuild the scale-dependent blur
    /// filters. Cheap; called at construction and whenever the layer re-activates (covers
    /// display-topology/DPI changes without subscribing to them).</summary>
    private void RefreshScale()
    {
        float s = 1f;
        try
        {
            var scr = System.Windows.Forms.Screen.PrimaryScreen;
            if (scr != null) s = (float)CompositorHostWindow.GetDpiScaleForScreen(scr);
        }
        catch { }
        if (s <= 0) s = 1f;
        if (Math.Abs(s - _s) > 0.001f || _boltGlow.MaskFilter == null)
        {
            _s = s;
            try
            {
                _boltGlow.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5f * s);
                _ringGlow.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f * s);
            }
            catch { }
        }
    }

    public override void OnActivated() => RefreshScale();

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

    // ---- emit API (UI thread; ChaosSkiaFxOverlay's statics marshal) ----

    public void EmitTrail(Point centerPx, double lifeSec, bool warm, double dirX = 0, double dirY = 0)
    {
        float s = _s;
        // Travel direction → a unit "behind" vector. The burst originates a touch behind the
        // rabbit and the sparks carry a backward bias, so the trail reads as STREAMING BEHIND
        // it rather than puffing out around it.
        double dl = Math.Sqrt(dirX * dirX + dirY * dirY);
        float bx = 0, by = 0;
        double cx = centerPx.X, cy = centerPx.Y;
        if (dl > 0.0001) { bx = (float)(-dirX / dl); by = (float)(-dirY / dl); cx += bx * 14 * s; cy += by * 14 * s; }

        float life = (float)Math.Max(0.30, lifeSec) * 1.3f;   // slightly longer-lived
        byte kind = (byte)(warm ? 1 : 0);

        // A bright core flash that drifts back along the trail...
        Add((float)cx, (float)cy, bx * 28f * s, by * 28f * s, life * 0.5f, 20f * s, kind);
        // ...with a small spread of sparks.
        int sparks = 3 + _rng.Next(2);
        for (int i = 0; i < sparks; i++)
        {
            double ang = _rng.NextDouble() * Math.PI * 2;
            float spd = (10f + (float)_rng.NextDouble() * 30f) * s;
            float vx = (float)Math.Cos(ang) * spd + bx * 48f * s;
            float vy = (float)Math.Sin(ang) * spd + by * 48f * s - 12f * s;
            Add((float)cx, (float)cy, vx, vy, life * (0.65f + (float)_rng.NextDouble() * 0.35f),
                (6f + (float)_rng.NextDouble() * 6f) * s, kind);
        }
        SetActive(true);
    }

    /// <summary>Pop burst: a white core flash, an expanding ring, and radiating shards, all in
    /// the bubble's payload colour. Reads fast at the tail like every other particle.</summary>
    public void EmitBurst(Point centerPx, SKColor col, float scale)
    {
        if (scale <= 0.05f) scale = 1f;
        float s = _s;
        float cx = (float)centerPx.X, cy = (float)centerPx.Y;
        uint rgb = (uint)(col.Red << 16 | col.Green << 8 | col.Blue);

        // Bright central flash (slow, fat — the "release").
        Add(cx, cy, 0f, 0f, 0.24f, 24f * scale * s, 2, rgb);
        // A crisp expanding ring.
        int ring = 10 + (int)(2 * scale);
        for (int i = 0; i < ring; i++)
        {
            double a = i / (double)ring * Math.PI * 2;
            float spd = (120f + (float)_rng.NextDouble() * 40f) * scale * s;
            Add(cx, cy, (float)Math.Cos(a) * spd, (float)Math.Sin(a) * spd, 0.34f, 6.5f * scale * s, 2, rgb);
        }
        // Scattered shards with a little upward bias + gravity settle.
        int shards = 7 + _rng.Next(5);
        for (int i = 0; i < shards; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2;
            float spd = (40f + (float)_rng.NextDouble() * 150f) * scale * s;
            Add(cx, cy, (float)Math.Cos(a) * spd, (float)Math.Sin(a) * spd - 20f * s,
                0.30f + (float)_rng.NextDouble() * 0.35f, (5f + (float)_rng.NextDouble() * 7f) * scale * s, 2, rgb);
        }
        SetActive(true);
    }

    private void Add(float x, float y, float vx, float vy, float life, float size, byte kind, uint col = 0)
    {
        if (_n >= MAX_PARTICLES) return;
        _p[_n++] = new Particle { X = x, Y = y, VX = vx, VY = vy, Life = life, Max = life, Size = size, Kind = kind, Col = col };
    }

    public void AddStrike(IReadOnlyList<(Point From, Point To)> boltsPx)
    {
        if (boltsPx == null || boltsPx.Count == 0) return;
        var color = ChaosBoonColors.ToSk(ChaosBoonColors.Electric);
        foreach (var (fromPx, toPx) in boltsPx)
        {
            var bolt = new Bolt
            {
                A = new SKPoint((float)fromPx.X, (float)fromPx.Y),
                B = new SKPoint((float)toPx.X, (float)toPx.Y),
                Life = 0.20f, Max = 0.20f, Color = color,
            };
            Rejitter(bolt);
            _bolts.Add(bolt);
        }
        SetActive(true);
    }

    /// <summary>Build a jagged path between two points (perpendicular midpoint displacement).</summary>
    private SKPoint[] BuildBolt(SKPoint a, SKPoint b)
    {
        float s = _s;
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        int mids = Math.Max(2, (int)(len / (55f * s)));
        float px = len > 0.001f ? -dy / len : 0f, py = len > 0.001f ? dx / len : 0f;
        var pts = new SKPoint[mids + 2];
        pts[0] = a;
        for (int m = 1; m <= mids; m++)
        {
            float f = m / (float)(mids + 1);
            float off = (float)(_rng.NextDouble() * 2 - 1) * 16f * s;
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
            float flen = (20f + (float)_rng.NextDouble() * 55f) * _s;
            var end = new SKPoint(origin.X + MathF.Cos(ang) * flen, origin.Y + MathF.Sin(ang) * flen);
            bolt.Branches.Add(BuildBolt(origin, end));
        }
    }

    public void EmitRipple(Point centerPx, double radiusPx, double lifeMs, bool strong)
    {
        float r = (float)Math.Max(1.0, radiusPx);   // world px — no DIP conversion needed
        float life = (float)Math.Max(0.1, lifeMs / 1000.0);
        var col = ChaosBoonColors.ToSk(ChaosBoonColors.Electric);
        _ripples.Add(new RippleFx { C = new SKPoint((float)centerPx.X, (float)centerPx.Y), MaxR = r, Age = 0, Life = life, Strong = strong, Color = col });

        // A ring of sparks thrown outward with the front (they drag/settle, so they read as
        // foam scattering off the wave rather than a perfect tracking ring).
        uint rgb = (uint)(col.Red << 16 | col.Green << 8 | col.Blue);
        int motes = strong ? 14 : 7;
        float frontSpeed = r / life;
        for (int i = 0; i < motes; i++)
        {
            double ang = Math.PI * 2 * i / motes + 0.2;
            float ux = (float)Math.Cos(ang), uy = (float)Math.Sin(ang);
            Add((float)centerPx.X + ux * r * 0.15f, (float)centerPx.Y + uy * r * 0.15f,
                ux * frontSpeed * 0.85f, uy * frontSpeed * 0.85f,
                life * 0.85f, (strong ? 6f : 4.5f) * _s, 2, rgb);
        }
        SetActive(true);
    }

    public void ArmCursor()
    {
        _cursorArmed = true;   // position lands with the first MoveCursor (same as the legacy window)
        SetActive(true);
    }

    public void DisarmCursor() => _cursorArmed = false;

    public void MoveCursor(double px, double py)
    {
        if (!_cursorArmed) return;
        _cursorX = (float)px;
        _cursorY = (float)py;
    }

    /// <summary>Run teardown (ChaosSkiaFxOverlay.CloseActive): drop everything immediately.</summary>
    public void Clear()
    {
        _n = 0;
        _bolts.Clear();
        _ripples.Clear();
        _cursorArmed = false;
        SetActive(false);
    }

    // ---- engine tick ----

    public override void Update(TimeSpan delta)
    {
        float dt = (float)delta.TotalSeconds;
        if (dt <= 0) return;
        if (dt > 0.1f) dt = 0.1f;   // engine clamps too; belt and braces
        float s = _s;

        // Advance particles: integrate, damp, gentle gravity, cull dead (swap-remove).
        float drag = MathF.Exp(-2.4f * dt);
        for (int i = _n - 1; i >= 0; i--)
        {
            ref var p = ref _p[i];
            p.Life -= dt;
            if (p.Life <= 0f) { _p[i] = _p[--_n]; continue; }
            p.X += p.VX * dt; p.Y += p.VY * dt;
            p.VX *= drag; p.VY *= drag;
            p.VY += 34f * s * dt;   // settle
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
                float spd = (8f + (float)_rng.NextDouble() * 18f) * s;
                Add(_cursorX, _cursorY, (float)Math.Cos(ang) * spd, (float)Math.Sin(ang) * spd - 10f * s,
                    0.5f + (float)_rng.NextDouble() * 0.3f, (6f + (float)_rng.NextDouble() * 4f) * s, 0);
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

        if (_n == 0 && _bolts.Count == 0 && _ripples.Count == 0 && !_cursorArmed)
            SetActive(false);   // engine paints one cleared frame on the transition
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        var img = Dot();
        float s = _s;

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
            float baseR = 34f * s;
            DrawDot(canvas, img, _cursorX, _cursorY, baseR * breath * 1.8f, 55, CursorCF);  // outer bloom
            DrawDot(canvas, img, _cursorX, _cursorY, baseR * breath, 150, CursorCF);        // halo
            DrawDot(canvas, img, _cursorX, _cursorY, baseR * breath * 0.42f, 120, GoldCF);  // gold heart
        }

        if (_ripples.Count > 0) DrawRipples(canvas, img);
        if (_bolts.Count > 0) DrawBolts(canvas, img);
    }

    private void DrawBolts(SKCanvas canvas, SKImage img)
    {
        float s = _s;
        foreach (var bolt in _bolts)
        {
            float t = bolt.Life / bolt.Max;                 // 1 -> 0
            float a = t > 0.4f ? 1f : t / 0.4f;             // hold hot, then fade
            _boltGlow.Color = bolt.Color.WithAlpha((byte)(120 * a));
            _boltGlow.StrokeWidth = 6.5f * s;
            _boltCore.Color = BoltCoreColor.WithAlpha((byte)(235 * a));
            _boltCore.StrokeWidth = 1.8f * s;

            DrawPolyline(canvas, bolt.Main, _boltGlow);
            foreach (var br in bolt.Branches) DrawPolyline(canvas, br, _boltGlow);
            DrawPolyline(canvas, bolt.Main, _boltCore);
            _boltCore.Color = BoltCoreColor.WithAlpha((byte)(160 * a));   // forks read a touch dimmer
            foreach (var br in bolt.Branches) DrawPolyline(canvas, br, _boltCore);

            float fr = (16f * a + 6f) * s;
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

    private void DrawRipples(SKCanvas canvas, SKImage img)
    {
        float s = _s;
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
                DrawDot(canvas, img, rp.C.X, rp.C.Y, (rp.Strong ? 34f : 22f) * s * (0.6f + 0.4f * ff), (byte)(200 * ff), ElectricCF);
            }

            // Leading wavefront: a blurred glow stroke + a hot white-blue core, thinning as it spreads.
            float baseW = (rp.Strong ? 7f : 4.5f) * s;
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
                canvas.DrawCircle(rp.C, lead + 3f * s, _ringCore);
                _ringCore.Color = RipplePinkInner.WithAlpha((byte)(70 * fade));
                canvas.DrawCircle(rp.C, Math.Max(0f, lead - 3f * s), _ringCore);

                // Trailing concentric rings lag behind the front for depth (look only).
                _ringGlow.Color = col.WithAlpha((byte)(60 * fade));
                _ringGlow.StrokeWidth = w * 1.4f;
                if (lead > 10f * s) canvas.DrawCircle(rp.C, lead * 0.7f, _ringGlow);
                if (lead > 20f * s) canvas.DrawCircle(rp.C, lead * 0.45f, _ringGlow);
            }
        }
    }

    private void DrawDot(SKCanvas canvas, SKImage img, float cx, float cy, float radius, byte alpha, SKColorFilter tint)
    {
        if (radius <= 0.2f || alpha == 0) return;
        _paint.ColorFilter = tint;
        _paint.Color = new SKColor(255, 255, 255, alpha);   // global opacity multiplier for the image draw
        var dest = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
        canvas.DrawImage(img, dest, _paint);
    }
}
