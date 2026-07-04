using System;
using System.Collections.Generic;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Field FX for the chaos run-boon pool (WS2/WP3 Phase F #8): Size Queen's expanding
/// pop-ring, the Ripple's snap cast (linear cyan kill-front + eased pink echo + 8 radial
/// shards), Aftermath's crackling residue zone, the Tail-Plug/GG-sweeper rabbit sparkle
/// trail, and The Bound's elastic tether threads — all pure render primitives on one layer.
/// The popping itself lives in the bubble engine (WPF BubbleService.TickFieldHazards);
/// this layer is purely the visible half.
///
/// Behavior contract (WPF Chaos/ChaosFieldFxOverlay.cs — geometry DIP→px per monitor):
/// - Ripple(center, radius, lifeMs): ring stroke (190, #7AE0FF) thickness 6 DIP, scale
///   0.05→1.0 cubic ease-OUT over lifeMs (the WPF RenderTransform scales the STROKE too),
///   opacity 0.95→0 linear;
/// - SnapRipple: cyan front (200, #7AE0FF, 6 DIP, from 0.95) grows LINEARLY so the drawn
///   ring matches the linear kill front exactly; pink echo (200, #FF69B4, 3.5 DIP, from
///   0.65) at 0.82·r rides cubic-eased behind; EIGHT shards at 2π·i/8 + 0.39 (offset so
///   none sits on an axis): a 0.16·r segment translated from 0.2·r to 0.85·r cubic-out
///   over 0.7·lifeMs, stroke (220, cyan) 3 DIP round caps, opacity 0.9→0 — the legacy
///   Avalonia window drew TEN random-angle random-length endpoint-growing shards (a drift;
///   WPF restored);
/// - Residue(center, radius, lifeMs): radial gradient (70,#BFECFF)@0 → (90,#9C5CFF)@0.55 →
///   (0)@1.0; crackling flicker 0.55–1.0 repicked every 90ms for max(2, lifeMs/90) steps,
///   then a linear fade to 0 at lifeMs (the legacy window also forgot the px→DIP radius
///   conversion here — moot in px space, noted for the record);
/// - TrailDot(center, lifeSec, warm): 90-slot recycled ring buffer, dot 16 DIP, gold-core
///   radial spark (200,#FFE9A0)@0 → (120, edge)@0.55 → 0@1.0 with edge = #FF4DC4 pink /
///   #FF8A14 amber, opacity 0.65→0 and scale 1.1→0.3 both LINEAR over max(0.3, lifeSec)s
///   (the legacy window used 1.25→0 ease-out and no floor — a drift; WPF restored);
/// - SetTether/ClearTether(key, a, b): dashed 4,3 line (150, #FF69B4), opacity 0.55,
///   thickness clamp(5 − dist/250, 1.5..5) DIP — the further it stretches, the thinner.
///
/// HONEST wiring note: NO production caller exists in the Avalonia head — the WPF call
/// sites live in BubbleService paths (shockwaves, field hazards, rabbit trails, The Bound)
/// not yet ported; the old window's only "callers" were the EnsureCreated pre-warm and
/// RaiseActive z-churn, both deleted (registration is app-lifetime; z by constant, UCE
/// rule 9). The service seams are live and proven by --verify-layers; the bubble-engine
/// FX port wires production callers to them.
///
/// Zero per-frame alloc: paints + the unit-radius spark/residue shaders are built once
/// (or per spawn for residues); dash effects cached per monitor scale. Geometry is
/// PHYSICAL px; DIP thickness/size constants convert with the screen-aware Render
/// overload's Scaling (an item straddling a mixed-DPI seam draws each half at that
/// monitor's scale — the accepted seam-only difference, cursor-glow precedent).
///
/// Capture-VISIBLE (main surface).
/// </summary>
public sealed class ChaosFieldFxLayer : BaseLayer
{
    private const int TrailDotPool = 90;        // WPF TRAIL_DOT_POOL
    private const double TrailDotSizeDip = 16;  // WPF TRAIL_DOT_SIZE

    private static readonly SKColor RingColor = new(0x7A, 0xE0, 0xFF);      // Size Queen — snap-cyan
    private static readonly SKColor ResidueColor = new(0x9C, 0x5C, 0xFF);   // Aftermath — E-Stim violet
    private static readonly SKColor CastFrontColor = new(0x7A, 0xE0, 0xFF); // the water — cyan
    private static readonly SKColor CastEchoColor = new(0xFF, 0x69, 0xB4);  // the hand — pink
    private static readonly SKColor TetherColor = new SKColor(0xFF, 0x69, 0xB4, 150);

    private enum Grow { Linear, CubicOut }

    private sealed class Ring
    {
        public double Cx, Cy, R;          // px
        public double LifeMs, ClockMs;
        public double ThicknessDip;
        public double FromOpacity;
        public SKColor Stroke;
        public Grow Growth;
    }

    private sealed class Shard
    {
        public double Cx, Cy, Ux, Uy, R;  // px, unit direction
        public double FlyMs, ClockMs;     // 0.7 * lifeMs
    }

    private sealed class ResidueZone
    {
        public double Cx, Cy, R;          // px
        public double LifeMs, ClockMs;
        public double FadeStartMs;
        public double LastFlickMs = -1000;
        public double Flick;
        public SKShader Shader = null!;   // unit-radius, built at spawn, disposed on expiry under _sync
    }

    private struct TrailDotSlot
    {
        public bool Live;
        public double X, Y;               // px
        public double LifeMs, ClockMs;
        public bool Warm;
    }

    private readonly object _sync = new();
    private readonly List<Ring> _rings = new();
    private readonly List<Shard> _shards = new();
    private readonly List<ResidueZone> _residues = new();
    private readonly TrailDotSlot[] _dots = new TrailDotSlot[TrailDotPool];
    private int _dotIndex;
    private readonly Dictionary<int, (double Ax, double Ay, double Bx, double By)> _tethers = new();
    private readonly Random _rng = new();

    // Paints/shaders built once (UCE rule: no per-frame allocations). Never disposed —
    // the layer lives app-long (ChaosCursorGlowLayer precedent).
    private readonly SKPaint _strokePaint;    // rings + shards + tethers (color/width set per draw)
    private readonly SKPaint _fillPaint;      // residues + trail dots (shader + alpha set per draw)
    private readonly SKShader _sparkCool;     // unit-radius Tail-Plug pink spark
    private readonly SKShader _sparkWarm;     // unit-radius GG-sweeper amber spark
    // Dash 4,3 DIP → px per monitor scale; cached per distinct scaling (bounded by monitors).
    private readonly Dictionary<double, SKPathEffect> _dashByScale = new();

    public ChaosFieldFxLayer()
    {
        _strokePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
        _fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        _sparkCool = BuildSparkShader(new SKColor(0xFF, 0x4D, 0xC4));   // WPF TrailColor
        _sparkWarm = BuildSparkShader(new SKColor(0xFF, 0x8A, 0x14));   // WPF WarmTrailColor
    }

    /// <summary>WPF BuildTrailBrush: gold core → edge falloff, unit radius.</summary>
    private static SKShader BuildSparkShader(SKColor edge) =>
        SKShader.CreateRadialGradient(
            new SKPoint(0, 0), 1f,
            new[]
            {
                new SKColor(0xFF, 0xE9, 0xA0, 200),
                new SKColor(edge.Red, edge.Green, edge.Blue, 120),
                new SKColor(edge.Red, edge.Green, edge.Blue, 0),
            },
            new[] { 0.0f, 0.55f, 1.0f },
            SKShaderTileMode.Clamp);

    public override int ZIndex => CompositorLayers.ChaosFieldFx;

    public override bool IsActive
    {
        get
        {
            lock (_sync)
            {
                if (_rings.Count > 0 || _shards.Count > 0 || _residues.Count > 0 || _tethers.Count > 0) return true;
                foreach (var d in _dots) if (d.Live) return true;
                return false;
            }
        }
    }

    // ConsumeDirty stays the base always-dirty: rings/shards/residues/dots animate every
    // frame; a tether-only frame repaints too (WPF updates tethers every anim tick anyway),
    // and with nothing drawn IsActive is false so the engine never ticks this layer.

    /// <summary>Size Queen: one expanding ring at PHYSICAL px (WPF Ripple).</summary>
    public void Ripple(double cxPx, double cyPx, double radiusPx, double lifeMs)
    {
        lock (_sync)
        {
            _rings.Add(new Ring
            {
                Cx = cxPx, Cy = cyPx, R = radiusPx, LifeMs = Math.Max(1, lifeMs),
                ThicknessDip = 6, FromOpacity = 0.95,
                Stroke = new SKColor(RingColor.Red, RingColor.Green, RingColor.Blue, 190),
                Growth = Grow.CubicOut,
            });
        }
    }

    /// <summary>The Ripple cast: linear cyan kill-front + eased pink echo + 8 shards (WPF SnapRipple).</summary>
    public void SnapRipple(double cxPx, double cyPx, double radiusPx, double lifeMs)
    {
        lock (_sync)
        {
            lifeMs = Math.Max(1, lifeMs);
            _rings.Add(new Ring
            {
                Cx = cxPx, Cy = cyPx, R = radiusPx, LifeMs = lifeMs,
                ThicknessDip = 6, FromOpacity = 0.95,
                Stroke = new SKColor(CastFrontColor.Red, CastFrontColor.Green, CastFrontColor.Blue, 200),
                Growth = Grow.Linear,   // matches the linear kill front exactly (WPF comment)
            });
            _rings.Add(new Ring
            {
                Cx = cxPx, Cy = cyPx, R = radiusPx * 0.82, LifeMs = lifeMs,
                ThicknessDip = 3.5, FromOpacity = 0.65,
                Stroke = new SKColor(CastEchoColor.Red, CastEchoColor.Green, CastEchoColor.Blue, 200),
                Growth = Grow.CubicOut,
            });
            for (int i = 0; i < 8; i++)
            {
                var ang = Math.PI * 2 * i / 8 + 0.39;   // offset so no shard sits on an axis
                _shards.Add(new Shard
                {
                    Cx = cxPx, Cy = cyPx, Ux = Math.Cos(ang), Uy = Math.Sin(ang),
                    R = radiusPx, FlyMs = Math.Max(1, lifeMs * 0.7),
                });
            }
        }
    }

    /// <summary>Aftermath: one crackling residue zone (WPF Residue).</summary>
    public void Residue(double cxPx, double cyPx, double radiusPx, double lifeMs)
    {
        lock (_sync)
        {
            lifeMs = Math.Max(1, lifeMs);
            var steps = Math.Max(2, (int)(lifeMs / 90));
            _residues.Add(new ResidueZone
            {
                Cx = cxPx, Cy = cyPx, R = Math.Max(1, radiusPx), LifeMs = lifeMs,
                FadeStartMs = Math.Min(Math.Max(1, lifeMs - 1), (steps - 1) * 90.0),
                Flick = 0.55 + _rng.NextDouble() * 0.45,
                LastFlickMs = 0,
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(0, 0), 1f,
                    new[]
                    {
                        new SKColor(0xBF, 0xEC, 0xFF, 70),
                        new SKColor(ResidueColor.Red, ResidueColor.Green, ResidueColor.Blue, 90),
                        new SKColor(ResidueColor.Red, ResidueColor.Green, ResidueColor.Blue, 0),
                    },
                    new[] { 0.0f, 0.55f, 1.0f },
                    SKShaderTileMode.Clamp),
            });
        }
    }

    /// <summary>Drop one fading sparkle at a rabbit's trail point (WPF TrailDot; recycled
    /// 90-slot ring buffer — an old live dot gets stolen, same as WPF).</summary>
    public void TrailDot(double cxPx, double cyPx, double lifeSec, bool warm = false)
    {
        lock (_sync)
        {
            _dots[_dotIndex] = new TrailDotSlot
            {
                Live = true, X = cxPx, Y = cyPx, Warm = warm,
                LifeMs = Math.Max(0.3, lifeSec) * 1000.0,   // WPF max(0.3, lifeSec)
            };
            _dotIndex = (_dotIndex + 1) % TrailDotPool;
        }
    }

    /// <summary>The Bound: create/update the elastic thread between a tethered pair (WPF SetTether).</summary>
    public void SetTether(int key, double axPx, double ayPx, double bxPx, double byPx)
    {
        lock (_sync) { _tethers[key] = (axPx, ayPx, bxPx, byPx); }
    }

    /// <summary>The Bound: the pair resolved — drop its thread (WPF ClearTether).</summary>
    public void ClearTether(int key)
    {
        lock (_sync) { _tethers.Remove(key); }
    }

    /// <summary>Instant teardown (run end — WPF CloseActive).</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _rings.Clear();
            _shards.Clear();
            foreach (var r in _residues) r.Shader.Dispose();   // under _sync: no draw in flight
            _residues.Clear();
            for (int i = 0; i < _dots.Length; i++) _dots[i].Live = false;
            _tethers.Clear();
        }
    }

    public override void Update(TimeSpan deltaTime)
    {
        var dtMs = deltaTime.TotalMilliseconds;
        lock (_sync)
        {
            for (int i = _rings.Count - 1; i >= 0; i--)
            {
                _rings[i].ClockMs += dtMs;
                if (_rings[i].ClockMs >= _rings[i].LifeMs) _rings.RemoveAt(i);
            }
            for (int i = _shards.Count - 1; i >= 0; i--)
            {
                _shards[i].ClockMs += dtMs;
                if (_shards[i].ClockMs >= _shards[i].FlyMs) _shards.RemoveAt(i);
            }
            for (int i = _residues.Count - 1; i >= 0; i--)
            {
                var res = _residues[i];
                res.ClockMs += dtMs;
                if (res.ClockMs >= res.LifeMs)
                {
                    res.Shader.Dispose();
                    _residues.RemoveAt(i);
                    continue;
                }
                // Crackle: repick 0.55–1.0 every ~90ms until the fade window (WPF keyframes).
                if (res.ClockMs < res.FadeStartMs && res.ClockMs - res.LastFlickMs >= 90)
                {
                    res.LastFlickMs = res.ClockMs;
                    res.Flick = 0.55 + _rng.NextDouble() * 0.45;
                }
            }
            for (int i = 0; i < _dots.Length; i++)
            {
                if (!_dots[i].Live) continue;
                _dots[i].ClockMs += dtMs;
                if (_dots[i].ClockMs >= _dots[i].LifeMs) _dots[i].Live = false;
            }
        }
    }

    /// <summary>Screen-aware render: DIP stroke/dot constants convert with the composited
    /// monitor's scaling (cursor-glow pattern).</summary>
    public void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, ScreenInfo? screen, TimeSpan deltaTime)
    {
        var scale = screen != null && screen.Scaling > 0 ? screen.Scaling : 1.0;
        lock (_sync)
        {
            // Rings (plain ripple + snap cast fronts/echoes). WPF's RenderTransform scales
            // the stroke with the ring, so thickness rides the growth factor too.
            foreach (var ring in _rings)
            {
                var t = Math.Clamp(ring.ClockMs / ring.LifeMs, 0, 1);
                var grow = ring.Growth == Grow.Linear ? t : 1 - Math.Pow(1 - t, 3);
                var s = 0.05 + (1.0 - 0.05) * grow;
                var opacity = ring.FromOpacity * (1 - t);
                if (opacity <= 0) continue;
                _strokePaint.Color = ring.Stroke.WithAlpha((byte)Math.Clamp(ring.Stroke.Alpha * opacity, 0, 255));
                _strokePaint.StrokeWidth = (float)(ring.ThicknessDip * scale * s);
                _strokePaint.StrokeCap = SKStrokeCap.Butt;
                _strokePaint.PathEffect = null;
                canvas.DrawCircle((float)ring.Cx, (float)ring.Cy, (float)(ring.R * s), _strokePaint);
            }

            // Snap shards: fixed 0.16·r segment translated 0.2·r → 0.85·r cubic-out.
            foreach (var shard in _shards)
            {
                var t = Math.Clamp(shard.ClockMs / shard.FlyMs, 0, 1);
                var eased = 1 - Math.Pow(1 - t, 3);
                var d = shard.R * (0.2 + (0.85 - 0.2) * eased);
                var opacity = 0.9 * (1 - t);
                if (opacity <= 0) continue;
                var x1 = shard.Cx + shard.Ux * d;
                var y1 = shard.Cy + shard.Uy * d;
                var x2 = shard.Cx + shard.Ux * (d + shard.R * 0.16);
                var y2 = shard.Cy + shard.Uy * (d + shard.R * 0.16);
                _strokePaint.Color = new SKColor(CastFrontColor.Red, CastFrontColor.Green, CastFrontColor.Blue,
                    (byte)Math.Clamp(220 * opacity, 0, 255));
                _strokePaint.StrokeWidth = (float)(3 * scale);
                _strokePaint.StrokeCap = SKStrokeCap.Round;
                _strokePaint.PathEffect = null;
                canvas.DrawLine((float)x1, (float)y1, (float)x2, (float)y2, _strokePaint);
            }

            // Residues: unit shader mapped by transform; flicker/fade via paint alpha.
            foreach (var res in _residues)
            {
                double opacity;
                if (res.ClockMs < res.FadeStartMs) opacity = res.Flick;
                else
                {
                    var f = (res.ClockMs - res.FadeStartMs) / Math.Max(1, res.LifeMs - res.FadeStartMs);
                    opacity = res.Flick * (1 - Math.Clamp(f, 0, 1));
                }
                if (opacity <= 0) continue;
                _fillPaint.Shader = res.Shader;
                _fillPaint.Color = new SKColor(255, 255, 255, (byte)Math.Clamp(opacity * 255, 0, 255));
                var save = canvas.Save();
                canvas.Translate((float)res.Cx, (float)res.Cy);
                canvas.Scale((float)res.R);
                canvas.DrawCircle(0, 0, 1f, _fillPaint);
                canvas.RestoreToCount(save);
            }

            // Trail dots: opacity 0.65→0, scale 1.1→0.3, both linear (WPF).
            for (int i = 0; i < _dots.Length; i++)
            {
                ref var dot = ref _dots[i];
                if (!dot.Live) continue;
                var t = Math.Clamp(dot.ClockMs / dot.LifeMs, 0, 1);
                var opacity = 0.65 * (1 - t);
                if (opacity <= 0) continue;
                var dotScale = 1.1 + (0.3 - 1.1) * t;
                var radiusPx = TrailDotSizeDip / 2 * scale * dotScale;
                if (radiusPx <= 0) continue;
                _fillPaint.Shader = dot.Warm ? _sparkWarm : _sparkCool;
                _fillPaint.Color = new SKColor(255, 255, 255, (byte)Math.Clamp(opacity * 255, 0, 255));
                var save = canvas.Save();
                canvas.Translate((float)dot.X, (float)dot.Y);
                canvas.Scale((float)radiusPx);
                canvas.DrawCircle(0, 0, 1f, _fillPaint);
                canvas.RestoreToCount(save);
            }
            _fillPaint.Shader = null;   // never leave a residue/spark shader dangling on the shared paint

            // Tethers: dashed elastic threads, thinner the further they stretch (WPF).
            if (_tethers.Count > 0)
            {
                if (!_dashByScale.TryGetValue(scale, out var dash))
                {
                    dash = SKPathEffect.CreateDash(new[] { (float)(4 * scale), (float)(3 * scale) }, 0);
                    _dashByScale[scale] = dash;
                }
                foreach (var t in _tethers.Values)
                {
                    var dx = t.Bx - t.Ax;
                    var dy = t.By - t.Ay;
                    var distDip = Math.Sqrt(dx * dx + dy * dy) / scale;
                    _strokePaint.Color = TetherColor.WithAlpha((byte)(150 * 0.55));   // brush alpha 150 × element opacity 0.55
                    _strokePaint.StrokeWidth = (float)(Math.Clamp(5.0 - distDip / 250.0, 1.5, 5.0) * scale);
                    _strokePaint.StrokeCap = SKStrokeCap.Round;   // WPF StrokeDashCap round
                    _strokePaint.PathEffect = dash;
                    canvas.DrawLine((float)t.Ax, (float)t.Ay, (float)t.Bx, (float)t.By, _strokePaint);
                }
                _strokePaint.PathEffect = null;
            }
        }
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
        => Render(canvas, bounds, null, deltaTime);
}
