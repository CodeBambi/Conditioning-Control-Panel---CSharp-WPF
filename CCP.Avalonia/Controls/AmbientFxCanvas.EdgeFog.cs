using System;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using Serilog;
using SkiaSharp;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Controls/AmbientFxCanvas.EdgeFog.cs (nav polish wave
    /// 11, 2026-10-07), numbers exact. The section edge's fog: soft round puffs in the section hue
    /// that drift along a window-edge strip and breathe gently inward and outward, each fading in,
    /// living a few seconds and fading out. Two layers per strip give the depth: a slow layer of
    /// big faint puffs and a quicker layer of small brighter ones. Pure numbers and steps.
    ///
    /// <para>Units: every distance is in the strip's own DIPs. <c>along</c> runs clockwise along
    /// the side like <see cref="EdgeDriftMath"/>; <c>depth</c> is the puff centre's distance in from
    /// the window's outer edge (it may sit a few px outside, so the puff reads as light leaking in).</para>
    /// </summary>
    public static class EdgeFogMath
    {
        /// <summary>Strip thickness the fog is authored for: the deepest puff edge stays inside it.</summary>
        public const double StripPx = 56;

        /// <summary>Big slow layer: size across the edge (px), along speed (px/s), peak alpha, life (s).</summary>
        public const double BigSizeMinPx = 34, BigSizeMaxPx = 60;
        public const double BigSpeedMinPx = 4, BigSpeedMaxPx = 9;
        public const double BigAlphaMin = 0.12, BigAlphaMax = 0.22;
        public const double BigLifeMin = 5.0, BigLifeMax = 9.0;

        /// <summary>Small quick layer: diameter, along speed (px/s), peak alpha, life (s).</summary>
        public const double SmallSizeMinPx = 18, SmallSizeMaxPx = 32;
        public const double SmallSpeedMinPx = 10, SmallSpeedMaxPx = 20;
        public const double SmallAlphaMin = 0.14, SmallAlphaMax = 0.22;
        public const double SmallLifeMin = 3.0, SmallLifeMax = 5.5;

        /// <summary>Puffs are soft ellipses drawn out ALONG the edge (a band of fog, not a row of
        /// dots): big ones 4 to 7 times longer than deep, small ones 1.4 to 2.4 times.</summary>
        public const double BigStretchMin = 4.0, BigStretchMax = 7.0;
        public const double SmallStretchMin = 1.4, SmallStretchMax = 2.4;

        /// <summary>Inward / outward breathing: amplitude (px) and rate (radians per second).</summary>
        public const double BreathePxMin = 3, BreathePxMax = 8;
        public const double BreatheRateMin = 0.5, BreatheRateMax = 1.1;

        /// <summary>How far outside the window a puff centre may sit (negative depth).</summary>
        public const double DepthOutPx = 6;
        /// <summary>The shallowest a puff centre rests, as a share of its size.</summary>
        public const double MinDepthShare = 0.22;
        /// <summary>A puff's inner edge keeps this much clear of the strip's inner edge.</summary>
        public const double InnerClearPx = 2;

        /// <summary>Share of each life spent fading in, and the same share fading out.</summary>
        public const double FadeShare = 0.35;
        /// <summary>Share of puffs that drift anticlockwise, so the fog shifts instead of marching.</summary>
        public const double CounterShare = 0.3;
        /// <summary>Share of spawns placed in the corner zones (the first and last tenth of a side).</summary>
        public const double CornerShare = 0.35, CornerZone = 0.10;

        /// <summary>Full counts per strip: a long side (top, bottom) and a short side (left, right).</summary>
        public const int BigLong = 5, SmallLong = 5, BigShort = 4, SmallShort = 3;
        /// <summary>Reduced motion: half the puffs at half the speed.</summary>
        public const double ReducedCount = 0.5, ReducedSpeed = 0.5;
        /// <summary>The share of the full counts a canvas keeps when its live budget is under 60.</summary>
        public const double LeanShare = 0.6;
        /// <summary>The live budget at and above which the full counts hold.</summary>
        public const int FullBudget = 60;
        /// <summary>Seconds between spawns while a layer is under its target.</summary>
        public const double SpawnEverySeconds = 0.35;

        /// <summary>Dust specks drifting through the fog. Diameter, along speed (px/s), peak alpha, life (s).</summary>
        public const double DustSizeMinPx = 1.2, DustSizeMaxPx = 3.0;
        public const double DustSpeedMinPx = 5, DustSpeedMaxPx = 16;
        public const double DustAlphaMin = 0.55, DustAlphaMax = 1.0;
        public const double DustLifeMin = 2.5, DustLifeMax = 6.5;
        /// <summary>Sideways wander across the strip: amplitude (px) and rate (radians per second).</summary>
        public const double DustWanderPxMin = 1.5, DustWanderPxMax = 5;
        public const double DustWanderRateMin = 0.6, DustWanderRateMax = 1.8;
        /// <summary>Twinkle: the alpha swings this share around its envelope, at this rate (rad/s).</summary>
        public const double DustTwinkleShare = 0.45, DustTwinkleRateMin = 2.0, DustTwinkleRateMax = 6.0;
        /// <summary>Depth bias toward the frame: depth = strip x u^power.</summary>
        public const double DustDepthPower = 2.6;
        /// <summary>The deepest band dust rests in (px from the frame).</summary>
        public const double DustDepthSpanPx = 22;
        /// <summary>Specks wear the vivid section colour lifted only this far toward white.</summary>
        public const double DustLift = 0.12;
        /// <summary>Full dust counts per strip (long side, short side).</summary>
        public const int DustLong = 90, DustShort = 56;
        /// <summary>Seconds between dust spawns while under target.</summary>
        public const double DustSpawnEverySeconds = 0.04;

        /// <summary>Dust alpha: peak x envelope x twinkle x gain, capped at 0.95.</summary>
        public static double DustAlpha(double peak, double age, double life, double twinkle, double gain)
        {
            double tw = 1.0 - DustTwinkleShare * 0.5 * (1.0 - Math.Sin(twinkle));
            return Math.Min(0.95, Math.Max(0, peak) * Envelope(age, life) * tw * Math.Clamp(gain, 0, 1.5));
        }

        /// <summary>A speck's resting depth: biased toward the frame, always inside the strip.</summary>
        public static double DustDepth(double u, double wanderPx) =>
            1 + wanderPx + Math.Pow(Math.Clamp(u, 0, 1), DustDepthPower) * Math.Min(DustDepthSpanPx, Math.Max(0, StripPx - 2 - 2 * wanderPx - 1));

        /// <summary>Puffs one layer may hold. Zero budget = zero puffs; under 60 the lean share;
        /// Reduced halves it (never below one while anything is allowed).</summary>
        public static int Target(int fullCount, int liveBudget, bool reduced)
        {
            if (fullCount <= 0 || liveBudget <= 0) return 0;
            double n = fullCount * (liveBudget >= FullBudget ? 1.0 : LeanShare);
            if (reduced) n *= ReducedCount;
            return Math.Max(1, (int)Math.Round(n, MidpointRounding.ToEven));
        }

        /// <summary>The full count for one layer on one side.</summary>
        public static int FullCount(bool big, bool longSide) =>
            big ? (longSide ? BigLong : BigShort) : (longSide ? SmallLong : SmallShort);

        /// <summary>Fade in over the first 35% of the life, hold, fade out over the last 35%
        /// (smoothstep on both ends, so a puff never pops). 0 outside the life.</summary>
        public static double Envelope(double age, double life)
        {
            if (life <= 0 || age <= 0 || age >= life) return 0;
            double t = age / life;
            double edge = Math.Min(t, 1.0 - t) / FadeShare;
            if (edge >= 1) return 1;
            return edge * edge * (3 - 2 * edge);
        }

        /// <summary>A puff's alpha: its peak x the envelope x the strip's gain, capped at 0.22.</summary>
        public static double Alpha(double peak, double age, double life, double gain) =>
            Math.Min(SmallAlphaMax, Math.Max(0, peak) * Envelope(age, life) * Math.Clamp(gain, 0, 1.5));

        /// <summary>The deepest a puff centre may sit before its inner edge leaves the strip.</summary>
        public static double MaxDepth(double sizePx, double breathePx) =>
            StripPx - sizePx / 2 - breathePx - InnerClearPx;

        /// <summary>The breathing centre depth at a phase.</summary>
        public static double Depth(double baseDepth, double breathePx, double phase) =>
            baseDepth + breathePx * Math.Sin(phase);

        /// <summary>Along distance after <paramref name="dt"/> at a signed speed.</summary>
        public static double Advance(double along, double speedPx, double dt) =>
            along + speedPx * Math.Max(0.0, dt);

        /// <summary>A puff is spent when its life is over or it has drifted a whole puff past
        /// either end of its side.</summary>
        public static bool IsSpent(double age, double life, double along, double length, double spanPx) =>
            age >= life || along < -spanPx || along > length + spanPx;

        /// <summary>Element px of a puff on a strip of the given side (strip w by h DIPs).</summary>
        public static (double X, double Y) Position(EdgeSide side, double along, double depth, double w, double h) => side switch
        {
            EdgeSide.Top => (along, depth),
            EdgeSide.Right => (w - depth, along),
            EdgeSide.Bottom => (w - along, h - depth),
            _ => (depth, h - along),
        };

        /// <summary>The side's length along the edge, from the strip's size.</summary>
        public static double Length(EdgeSide side, double w, double h) =>
            side is EdgeSide.Top or EdgeSide.Bottom ? w : h;

        /// <summary>Where a new puff starts: <paramref name="corner"/> &lt; CornerShare puts it in a
        /// corner zone (end chosen by <paramref name="u"/>), otherwise anywhere along the side.</summary>
        public static double SpawnAlong(double u, double corner, double length)
        {
            if (corner < CornerShare)
            {
                double zone = CornerZone * length;
                return u < 0.5 ? (u * 2) * zone : length - ((u - 0.5) * 2) * zone;
            }
            return u * length;
        }

        /// <summary>
        /// NavRailRules.Vivid (WPF, nav polish): the section hue at neon strength, same angle,
        /// lightness pulled down to 0.58, saturation raised to at least 0.80. Copied here so the
        /// fog dust matches WPF; drop it for the Core twin once the nav rules land in Core.
        /// </summary>
        public static (byte R, byte G, byte B) Vivid(byte r8, byte g8, byte b8)
        {
            const double vividL = 0.58, vividS = 0.80;
            double r = r8 / 255.0, g = g8 / 255.0, b = b8 / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2, d = max - min, h = 0, s = 0;
            if (d >= 1e-9)
            {
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                h = (max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4) * 60;
            }
            s = Math.Max(s, vividS);
            l = Math.Min(l, vividL);
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q, k = h / 360.0;
            static double Ch(double p, double q, double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1 / 6.0) return p + (q - p) * 6 * t;
                if (t < 0.5) return q;
                if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
                return p;
            }
            static byte B(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
            return (B(Ch(p, q, k + 1 / 3.0)), B(Ch(p, q, k)), B(Ch(p, q, k - 1 / 3.0)));
        }
    }

    public partial class AmbientFxCanvas
    {
        private struct FogPuff
        {
            public float Along, BaseDepth, Speed, Size, Stretch, Peak, Age, Life, Phase, PhaseSpd, Breathe;
            public bool Big;
        }

        private struct GrainMote
        {
            public float Along, BaseDepth, Speed, Size, Peak, Age, Life, Phase, PhaseSpd, Wander, Twinkle, TwinkleSpd;
        }

        private GrainMote[] _grain = Array.Empty<GrainMote>();
        private int _grainN;
        private float _grainT;
        private SKColorFilter? _grainTint;

        /// <summary>Dust specks alive right now (tests).</summary>
        internal int GrainCount => _grainN;

        private FogPuff[] _fog = Array.Empty<FogPuff>();
        private int _fogN;
        private float _fogBigT, _fogSmallT;
        private bool _fogSeeded;

        // The fog's own tint, crossfaded on a section change (SetEdgeFog). Kept apart from the
        // particle tint so the embers keep Retint's instant swap.
        private SKColor _fogFrom, _fogTo, _fogNow;
        private float _fogGainFrom = 1f, _fogGainTo = 1f, _fogGainNow = 1f;
        private float _fogFadeT, _fogFadeDur;
        private SKColorFilter? _fogTint;

        /// <summary>Puffs alive right now (tests).</summary>
        internal int FogPuffCount => _fogN;

        /// <summary>The fog's current tint and gain (tests).</summary>
        internal (SKColor Tint, float Gain) FogPaint => (_fogNow, _fogGainNow);

        private bool FogLayer => (_config.Layers & AmbientFxLayers.EdgeFog) != 0;

        /// <summary>
        /// Hand the fog a hue and a gain, crossfading from the current one over
        /// <paramref name="fadeMs"/> (0, or a canvas whose clock is not running, swaps at once).
        /// </summary>
        public void SetEdgeFog(Color hue, double gain, int fadeMs)
        {
            try
            {
                var to = new SKColor(hue.R, hue.G, hue.B);
                float g = (float)Math.Clamp(gain, 0.0, 1.5);
                _config.EdgeFogGain = g;
                if (fadeMs <= 0 || !IsRunning || _fogFadeDur <= 0 && _fogNow == SKColor.Empty)
                {
                    _fogFrom = _fogTo = _fogNow = to;
                    _fogGainFrom = _fogGainTo = _fogGainNow = g;
                    _fogFadeDur = 0;
                    RebuildFogTint();
                    // WPF only re-showed the old frame here; repaint so a parked strip shows the hue.
                    _sk.Redraw();
                    return;
                }
                _fogFrom = _fogNow;
                _fogGainFrom = _fogGainNow;
                _fogTo = to;
                _fogGainTo = g;
                _fogFadeT = 0;
                _fogFadeDur = fadeMs / 1000f;
            }
            catch (Exception ex) { Log.Debug("AmbientFxCanvas.SetEdgeFog: {E}", ex.Message); }
        }

        private void RebuildFogTint()
        {
            _fogTint?.Dispose();
            _fogTint = SKColorFilter.CreateBlendMode(_fogNow, SKBlendMode.Modulate);
            var (vr, vg, vb) = EdgeFogMath.Vivid(_fogNow.Red, _fogNow.Green, _fogNow.Blue);
            float k = (float)EdgeFogMath.DustLift;
            var lifted = new SKColor(
                (byte)(vr + (255 - vr) * k),
                (byte)(vg + (255 - vg) * k),
                (byte)(vb + (255 - vb) * k));
            _grainTint?.Dispose();
            _grainTint = SKColorFilter.CreateBlendMode(lifted, SKBlendMode.Modulate);
        }

        private int GrainTarget()
        {
            bool longSide = _config.EdgeSide is EdgeSide.Top or EdgeSide.Bottom;
            return EdgeFogMath.Target(longSide ? EdgeFogMath.DustLong : EdgeFogMath.DustShort,
                _liveBudget, _config.EdgeFogReduced);
        }

        private void SpawnGrain(bool prefill)
        {
            if (_grainN >= _grain.Length) return;
            double length = EdgeFogMath.Length(_config.EdgeSide, Bounds.Width, Bounds.Height);
            if (length <= 1) return;
            double r() => _rng.NextDouble();
            double speed = EdgeFogMath.DustSpeedMinPx + r() * (EdgeFogMath.DustSpeedMaxPx - EdgeFogMath.DustSpeedMinPx);
            double life = EdgeFogMath.DustLifeMin + r() * (EdgeFogMath.DustLifeMax - EdgeFogMath.DustLifeMin);
            double rate = EdgeFogMath.DustWanderRateMin + r() * (EdgeFogMath.DustWanderRateMax - EdgeFogMath.DustWanderRateMin);
            double tw = EdgeFogMath.DustTwinkleRateMin + r() * (EdgeFogMath.DustTwinkleRateMax - EdgeFogMath.DustTwinkleRateMin);
            if (_config.EdgeFogReduced)
            {
                speed *= EdgeFogMath.ReducedSpeed; life /= EdgeFogMath.ReducedSpeed;
                rate *= EdgeFogMath.ReducedSpeed; tw *= EdgeFogMath.ReducedSpeed;
            }
            if (r() < 0.5) speed = -speed;
            double wander = EdgeFogMath.DustWanderPxMin + r() * (EdgeFogMath.DustWanderPxMax - EdgeFogMath.DustWanderPxMin);
            _grain[_grainN++] = new GrainMote
            {
                Along = (float)EdgeFogMath.SpawnAlong(r(), r(), length),
                BaseDepth = (float)EdgeFogMath.DustDepth(r(), wander),
                Speed = (float)speed,
                Size = (float)(EdgeFogMath.DustSizeMinPx + r() * (EdgeFogMath.DustSizeMaxPx - EdgeFogMath.DustSizeMinPx)),
                Peak = (float)(EdgeFogMath.DustAlphaMin + r() * (EdgeFogMath.DustAlphaMax - EdgeFogMath.DustAlphaMin)),
                Life = (float)life,
                Age = prefill ? (float)(r() * life) : 0f,
                Phase = (float)(r() * Math.PI * 2),
                PhaseSpd = (float)rate,
                Wander = (float)wander,
                Twinkle = (float)(r() * Math.PI * 2),
                TwinkleSpd = (float)tw,
            };
        }

        /// <summary>Age, drift, wander and twinkle every speck; retire the spent; refill to target.</summary>
        private void StepGrain(float dt)
        {
            if (_grain.Length == 0) return;
            double length = EdgeFogMath.Length(_config.EdgeSide, Bounds.Width, Bounds.Height);
            for (int i = _grainN - 1; i >= 0; i--)
            {
                var d = _grain[i];
                d.Age += dt;
                d.Along = (float)EdgeFogMath.Advance(d.Along, d.Speed, dt);
                d.Phase += d.PhaseSpd * dt;
                d.Twinkle += d.TwinkleSpd * dt;
                if (EdgeFogMath.IsSpent(d.Age, d.Life, d.Along, length, 4)) _grain[i] = _grain[--_grainN];
                else _grain[i] = d;
            }
            if (!FogLayer || length <= 1) return;
            int target = GrainTarget();
            _grainT = _grainN >= target
                ? Math.Min(_grainT + dt, (float)EdgeFogMath.DustSpawnEverySeconds)
                : _grainT + dt;
            while (_grainN < target && _grainT > EdgeFogMath.DustSpawnEverySeconds && _grainN < _grain.Length)
            {
                _grainT -= (float)EdgeFogMath.DustSpawnEverySeconds;
                SpawnGrain(prefill: false);
            }
        }

        /// <summary>Paint the dust over the fog: tiny lifted-hue specks, additive like the puffs.</summary>
        private void DrawGrain(SKCanvas canvas, float w, float h)
        {
            if (_grainN == 0 || _grainTint == null) return;
            double aw = Bounds.Width, ah = Bounds.Height;
            if (aw <= 1 || ah <= 1) return;
            float sx = (float)(w / aw), sy = (float)(h / ah);
            var side = _config.EdgeSide;
            _paint.ColorFilter = _grainTint;
            for (int i = 0; i < _grainN; i++)
            {
                var d = _grain[i];
                float a = (float)EdgeFogMath.DustAlpha(d.Peak, d.Age, d.Life, d.Twinkle, _fogGainNow);
                if (a <= 0.01f) continue;
                double depth = d.BaseDepth + d.Wander * Math.Sin(d.Phase);
                var (x, y) = EdgeFogMath.Position(side, d.Along, depth, aw, ah);
                _paint.Color = SKColors.White.WithAlpha(Alpha(a));
                // The Dot sprite is a soft disc: drawn 2.2x the speck so its bright core is the speck.
                float s = d.Size * 2.2f;
                DrawSprite(canvas, FxSprites.Dot, (float)x * sx, (float)y * sy, s * sx, s * sy);
            }
            _paint.ColorFilter = null;
        }

        /// <summary>Seed the fog when the canvas (re)composes: the tint from the config, and the
        /// layers pre-filled at random ages so the fog is there at once, already mid-breath.</summary>
        private void ReseedFog()
        {
            _fogN = 0;
            _fogBigT = _fogSmallT = 0f;
            _grainN = 0;
            _grainT = 0f;
            if (!FogLayer || _particleBudget <= 0)
            {
                _fog = Array.Empty<FogPuff>();
                _grain = Array.Empty<GrainMote>();
                return;
            }

            var tint = _config.Tint ?? Env.ParticleColor;
            _fogFrom = _fogTo = _fogNow = new SKColor(tint.R, tint.G, tint.B);
            _fogGainFrom = _fogGainTo = _fogGainNow = (float)Math.Clamp(_config.EdgeFogGain, 0.0, 1.5);
            _fogFadeDur = 0;
            RebuildFogTint();

            bool longSide = _config.EdgeSide is EdgeSide.Top or EdgeSide.Bottom;
            int cap = EdgeFogMath.FullCount(true, longSide) + EdgeFogMath.FullCount(false, longSide);
            _fog = new FogPuff[cap];
            _grain = new GrainMote[longSide ? EdgeFogMath.DustLong : EdgeFogMath.DustShort];
            _fogSeeded = false;
            PrefillFog();
        }

        /// <summary>Fill both layers to target at random ages, once the strip has a size.</summary>
        private void PrefillFog()
        {
            if (_fog.Length == 0 || Bounds.Width <= 1 || Bounds.Height <= 1) return;
            _fogSeeded = true;
            bool longSide = _config.EdgeSide is EdgeSide.Top or EdgeSide.Bottom;
            int big = EdgeFogMath.Target(EdgeFogMath.FullCount(true, longSide), _liveBudget, _config.EdgeFogReduced);
            int small = EdgeFogMath.Target(EdgeFogMath.FullCount(false, longSide), _liveBudget, _config.EdgeFogReduced);
            for (int i = 0; i < big; i++) SpawnFog(true, prefill: true);
            for (int i = 0; i < small; i++) SpawnFog(false, prefill: true);
            int dust = GrainTarget();
            for (int i = 0; i < dust; i++) SpawnGrain(prefill: true);
        }

        private int FogCount(bool big)
        {
            int n = 0;
            for (int i = 0; i < _fogN; i++) if (_fog[i].Big == big) n++;
            return n;
        }

        private void SpawnFog(bool big, bool prefill)
        {
            if (_fogN >= _fog.Length) return;
            double w = Bounds.Width, h = Bounds.Height;
            double length = EdgeFogMath.Length(_config.EdgeSide, w, h);
            if (length <= 1) return;

            double r() => _rng.NextDouble();
            double size = big
                ? EdgeFogMath.BigSizeMinPx + r() * (EdgeFogMath.BigSizeMaxPx - EdgeFogMath.BigSizeMinPx)
                : EdgeFogMath.SmallSizeMinPx + r() * (EdgeFogMath.SmallSizeMaxPx - EdgeFogMath.SmallSizeMinPx);
            double speed = big
                ? EdgeFogMath.BigSpeedMinPx + r() * (EdgeFogMath.BigSpeedMaxPx - EdgeFogMath.BigSpeedMinPx)
                : EdgeFogMath.SmallSpeedMinPx + r() * (EdgeFogMath.SmallSpeedMaxPx - EdgeFogMath.SmallSpeedMinPx);
            if (_config.EdgeFogReduced) speed *= EdgeFogMath.ReducedSpeed;
            if (r() < EdgeFogMath.CounterShare) speed = -speed;
            double life = big
                ? EdgeFogMath.BigLifeMin + r() * (EdgeFogMath.BigLifeMax - EdgeFogMath.BigLifeMin)
                : EdgeFogMath.SmallLifeMin + r() * (EdgeFogMath.SmallLifeMax - EdgeFogMath.SmallLifeMin);
            if (_config.EdgeFogReduced) life /= EdgeFogMath.ReducedSpeed;
            double peak = big
                ? EdgeFogMath.BigAlphaMin + r() * (EdgeFogMath.BigAlphaMax - EdgeFogMath.BigAlphaMin)
                : EdgeFogMath.SmallAlphaMin + r() * (EdgeFogMath.SmallAlphaMax - EdgeFogMath.SmallAlphaMin);
            double stretch = big
                ? EdgeFogMath.BigStretchMin + r() * (EdgeFogMath.BigStretchMax - EdgeFogMath.BigStretchMin)
                : EdgeFogMath.SmallStretchMin + r() * (EdgeFogMath.SmallStretchMax - EdgeFogMath.SmallStretchMin);
            double breathe = EdgeFogMath.BreathePxMin + r() * (EdgeFogMath.BreathePxMax - EdgeFogMath.BreathePxMin);
            double maxDepth = EdgeFogMath.MaxDepth(size, breathe);
            double minDepth = Math.Max(-EdgeFogMath.DepthOutPx + breathe, size * EdgeFogMath.MinDepthShare);
            double depth = minDepth + r() * Math.Max(0, maxDepth - minDepth);
            double rate = EdgeFogMath.BreatheRateMin + r() * (EdgeFogMath.BreatheRateMax - EdgeFogMath.BreatheRateMin);
            if (_config.EdgeFogReduced) rate *= EdgeFogMath.ReducedSpeed;

            _fog[_fogN++] = new FogPuff
            {
                Along = (float)EdgeFogMath.SpawnAlong(r(), r(), length),
                BaseDepth = (float)depth,
                Speed = (float)speed,
                Size = (float)size,
                Stretch = (float)stretch,
                Peak = (float)peak,
                Life = (float)life,
                Age = prefill ? (float)(r() * life) : 0f,
                Phase = (float)(r() * Math.PI * 2),
                PhaseSpd = (float)rate,
                Breathe = (float)breathe,
                Big = big,
            };
        }

        /// <summary>
        /// One fog step: age, drift and breathe every puff, retire the spent, then refill each
        /// layer one puff per <see cref="EdgeFogMath.SpawnEverySeconds"/> up to its governed
        /// target. Also advances a running hue crossfade.
        /// </summary>
        internal void StepFog(float dt)
        {
            if (_fogFadeDur > 0)
            {
                _fogFadeT += dt;
                float t = Math.Clamp(_fogFadeT / _fogFadeDur, 0f, 1f);
                float e = 1f - (1f - t) * (1f - t);
                _fogNow = new SKColor(
                    (byte)(_fogFrom.Red + (_fogTo.Red - _fogFrom.Red) * e),
                    (byte)(_fogFrom.Green + (_fogTo.Green - _fogFrom.Green) * e),
                    (byte)(_fogFrom.Blue + (_fogTo.Blue - _fogFrom.Blue) * e));
                _fogGainNow = _fogGainFrom + (_fogGainTo - _fogGainFrom) * e;
                if (t >= 1f) _fogFadeDur = 0;
                RebuildFogTint();
            }

            if (_fog.Length == 0) return;
            if (!_fogSeeded) PrefillFog();
            StepGrain(dt);
            double length = EdgeFogMath.Length(_config.EdgeSide, Bounds.Width, Bounds.Height);
            for (int i = _fogN - 1; i >= 0; i--)
            {
                var p = _fog[i];
                p.Age += dt;
                p.Along = (float)EdgeFogMath.Advance(p.Along, p.Speed, dt);
                p.Phase += p.PhaseSpd * dt;
                if (EdgeFogMath.IsSpent(p.Age, p.Life, p.Along, length, p.Size * p.Stretch))
                    _fog[i] = _fog[--_fogN];
                else
                    _fog[i] = p;
            }

            if (!FogLayer || length <= 1) return;
            bool longSide = _config.EdgeSide is EdgeSide.Top or EdgeSide.Bottom;
            RefillFog(true, longSide, ref _fogBigT, dt);
            RefillFog(false, longSide, ref _fogSmallT, dt);
        }

        private void RefillFog(bool big, bool longSide, ref float clock, float dt)
        {
            int target = EdgeFogMath.Target(EdgeFogMath.FullCount(big, longSide), _liveBudget, _config.EdgeFogReduced);
            int have = FogCount(big);
            // A full layer banks one spawn's worth at most, so a retirement is replaced alone.
            clock = have >= target
                ? Math.Min(clock + dt, (float)EdgeFogMath.SpawnEverySeconds)
                : clock + dt;
            while (have < target && clock > EdgeFogMath.SpawnEverySeconds && _fogN < _fog.Length)
            {
                clock -= (float)EdgeFogMath.SpawnEverySeconds;
                SpawnFog(big, prefill: false);
                have++;
            }
        }

        /// <summary>Paint the fog: big puffs first, small over them, each the soft white dot
        /// tinted to the fog hue (hue at the centre, clear at the rim), additive like the rest.</summary>
        private void DrawEdgeFog(SKCanvas canvas, float w, float h)
        {
            if (_fogN == 0 || _fogTint == null) { DrawGrain(canvas, w, h); return; }
            double aw = Bounds.Width, ah = Bounds.Height;
            if (aw <= 1 || ah <= 1) return;
            float sx = (float)(w / aw), sy = (float)(h / ah);
            var side = _config.EdgeSide;
            bool horizontal = side is EdgeSide.Top or EdgeSide.Bottom;
            _paint.ColorFilter = _fogTint;
            for (int pass = 0; pass < 2; pass++)
            {
                bool big = pass == 0;
                for (int i = 0; i < _fogN; i++)
                {
                    var p = _fog[i];
                    if (p.Big != big) continue;
                    float a = (float)EdgeFogMath.Alpha(p.Peak, p.Age, p.Life, _fogGainNow);
                    if (a <= 0.003f) continue;
                    double depth = EdgeFogMath.Depth(p.BaseDepth, p.Breathe, p.Phase);
                    var (x, y) = EdgeFogMath.Position(side, p.Along, depth, aw, ah);
                    _paint.Color = SKColors.White.WithAlpha(Alpha(a));
                    float span = p.Size * p.Stretch;
                    // Big puffs take the fuller-bodied glow sprite (a soft plateau), small ones the
                    // plain dot: the band reads as fog with brighter knots in it.
                    DrawSprite(canvas, big ? FxSprites.GlowSprite : FxSprites.Dot, (float)x * sx, (float)y * sy,
                        (horizontal ? span : p.Size) * sx, (horizontal ? p.Size : span) * sy);
                }
            }
            _paint.ColorFilter = null;
            DrawGrain(canvas, w, h);
        }

        /// <summary>
        /// Test and bench seam: paint every edge layer this canvas runs onto
        /// <paramref name="canvas"/> as if it were the surface (w x h device px), no window needed.
        /// </summary>
        internal void PaintEdgeLayersForTests(SKCanvas canvas, float w, float h)
        {
            float intensity = (float)Math.Clamp(_config.Intensity, 0.0, 1.5);
            if (FogLayer) DrawEdgeFog(canvas, w, h);
            if ((_config.Layers & AmbientFxLayers.EdgeDrift) != 0) DrawEdge(canvas, w, h, intensity);
        }

        /// <summary>Test seam: (re)seed the fog after a test has measured the canvas.</summary>
        internal void ReseedFogForTests() => ReseedFog();

        /// <summary>Test seam: the (x, y, size) in DIPs of every live puff.</summary>
        internal (double X, double Y, double Size, bool Big)[] FogPuffsForTests()
        {
            if (!_fogSeeded) PrefillFog();
            var r = new (double, double, double, bool)[_fogN];
            for (int i = 0; i < _fogN; i++)
            {
                var p = _fog[i];
                var (x, y) = EdgeFogMath.Position(_config.EdgeSide, p.Along,
                    EdgeFogMath.Depth(p.BaseDepth, p.Breathe, p.Phase), Bounds.Width, Bounds.Height);
                r[i] = (x, y, p.Size, p.Big);
            }
            return r;
        }

        /// <summary>The Reduced allowance: a fog layer marked <see cref="AmbientFxConfig.EdgeFogReduced"/>
        /// may run at Reduced motion on a tier that allows ambient motion and particles. Every
        /// other layer keeps the Full-only gate.</summary>
        private bool ReducedFogMayRun() =>
            FogLayer && _config.EdgeFogReduced
            && Env.Level == MotionLevel.Reduced
            && Env.AllowAmbientMotion(_tier) && _particleBudget > 0;
    }
}
