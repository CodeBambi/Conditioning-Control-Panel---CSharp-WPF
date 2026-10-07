using System;
using SkiaSharp;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// The section edge's fog (nav polish wave 11, 2026-10-07): soft round puffs in the section
    /// hue that drift along a window-edge strip and breathe gently inward and outward, each fading
    /// in, living a few seconds and fading out. Two layers per strip give the depth: a slow layer
    /// of big faint puffs and a quicker layer of small brighter ones. Pure numbers and steps, no
    /// WPF and no Skia, so tests pin the budget, the band and the envelope without a canvas.
    ///
    /// <para>Units: every distance is in the strip's own DIPs (the strips sit outside the Viewbox,
    /// so a DIP is a native window pixel at 100%). <c>along</c> runs clockwise along the side like
    /// <see cref="EdgeDriftMath"/>; <c>depth</c> is the puff centre's distance in from the window's
    /// outer edge (it may sit a few px outside, so the puff reads as light leaking in).</para>
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
        /// dots): big ones 4 to 7 times longer than deep, so five of them nearly close a long
        /// side and the band breathes as they overlap and part; small ones 1.4 to 2.4 times.</summary>
        public const double BigStretchMin = 4.0, BigStretchMax = 7.0;
        public const double SmallStretchMin = 1.4, SmallStretchMax = 2.4;

        /// <summary>Inward / outward breathing: amplitude (px) and rate (radians per second).</summary>
        public const double BreathePxMin = 3, BreathePxMax = 8;
        public const double BreatheRateMin = 0.5, BreatheRateMax = 1.1;

        /// <summary>How far outside the window a puff centre may sit (negative depth).</summary>
        public const double DepthOutPx = 6;
        /// <summary>The shallowest a puff centre rests, as a share of its size: most of every puff
        /// stays in the window, and the breathing carries it toward the frame and back.</summary>
        public const double MinDepthShare = 0.22;
        /// <summary>A puff's inner edge keeps this much clear of the strip's inner edge.</summary>
        public const double InnerClearPx = 2;

        /// <summary>Share of each life spent fading in, and the same share fading out.</summary>
        public const double FadeShare = 0.35;
        /// <summary>Share of puffs that drift anticlockwise, so the fog shifts instead of marching.</summary>
        public const double CounterShare = 0.3;
        /// <summary>Share of spawns placed in the corner zones (the first and last tenth of a side).</summary>
        public const double CornerShare = 0.35, CornerZone = 0.10;

        /// <summary>Full counts per strip: a long side (top, bottom) and a short side (left, right).
        /// Four strips hold 2 x (5 + 5) + 2 x (4 + 3) = 34 puffs, beside the 24 embers (58).</summary>
        public const int BigLong = 5, SmallLong = 5, BigShort = 4, SmallShort = 3;
        /// <summary>Reduced motion: half the puffs at half the speed.</summary>
        public const double ReducedCount = 0.5, ReducedSpeed = 0.5;
        /// <summary>The share of the full counts a canvas keeps when its live budget is under the
        /// Quality tier's 60 (Balanced, or the governor halving it after hitches).</summary>
        public const double LeanShare = 0.6;
        /// <summary>The live budget at and above which the full counts hold.</summary>
        public const int FullBudget = 60;
        /// <summary>Seconds between spawns while a layer is under its target.</summary>
        public const double SpawnEverySeconds = 0.35;

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

        /// <summary>A puff's alpha: its peak x the envelope x the strip's gain, capped at the
        /// small layer's ceiling so no hue ever pushes a puff past 0.22.</summary>
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
        /// either end of its side (<paramref name="spanPx"/> = its length along the edge).</summary>
        public static bool IsSpent(double age, double life, double along, double length, double spanPx) =>
            age >= life || along < -spanPx || along > length + spanPx;

        /// <summary>Element px of a puff on a strip of the given side, from its along and depth
        /// (the strip is <paramref name="w"/> by <paramref name="h"/> DIPs).</summary>
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
    }

    public partial class AmbientFxCanvas
    {
        private struct FogPuff
        {
            public float Along, BaseDepth, Speed, Size, Stretch, Peak, Age, Life, Phase, PhaseSpd, Breathe;
            public bool Big;
        }

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
        public void SetEdgeFog(System.Windows.Media.Color hue, double gain, int fadeMs)
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
                    _sk.InvalidateVisual();
                    return;
                }
                _fogFrom = _fogNow;
                _fogGainFrom = _fogGainNow;
                _fogTo = to;
                _fogGainTo = g;
                _fogFadeT = 0;
                _fogFadeDur = fadeMs / 1000f;
            }
            catch (Exception ex) { App.Logger?.Debug("AmbientFxCanvas.SetEdgeFog: {E}", ex.Message); }
        }

        private void RebuildFogTint()
        {
            _fogTint?.Dispose();
            _fogTint = SKColorFilter.CreateBlendMode(_fogNow, SKBlendMode.Modulate);
        }

        /// <summary>Seed the fog when the canvas (re)composes: the tint from the config, and the
        /// layers pre-filled at random ages so the fog is there at once, already mid-breath.</summary>
        private void ReseedFog()
        {
            _fogN = 0;
            _fogBigT = _fogSmallT = 0f;
            if (!FogLayer || _particleBudget <= 0) { _fog = Array.Empty<FogPuff>(); return; }

            var tint = _config.Tint ?? FxTheme.ParticleColor;
            _fogFrom = _fogTo = _fogNow = new SKColor(tint.R, tint.G, tint.B);
            _fogGainFrom = _fogGainTo = _fogGainNow = (float)Math.Clamp(_config.EdgeFogGain, 0.0, 1.5);
            _fogFadeDur = 0;
            RebuildFogTint();

            bool longSide = _config.EdgeSide is EdgeSide.Top or EdgeSide.Bottom;
            int cap = EdgeFogMath.FullCount(true, longSide) + EdgeFogMath.FullCount(false, longSide);
            _fog = new FogPuff[cap];
            _fogSeeded = false;
            PrefillFog();
        }

        /// <summary>Fill both layers to target at random ages, once the strip has a size.</summary>
        private void PrefillFog()
        {
            if (_fog.Length == 0 || ActualWidth <= 1 || ActualHeight <= 1) return;
            _fogSeeded = true;
            bool longSide = _config.EdgeSide is EdgeSide.Top or EdgeSide.Bottom;
            int big = EdgeFogMath.Target(EdgeFogMath.FullCount(true, longSide), _liveBudget, _config.EdgeFogReduced);
            int small = EdgeFogMath.Target(EdgeFogMath.FullCount(false, longSide), _liveBudget, _config.EdgeFogReduced);
            for (int i = 0; i < big; i++) SpawnFog(true, prefill: true);
            for (int i = 0; i < small; i++) SpawnFog(false, prefill: true);
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
            double w = ActualWidth, h = ActualHeight;
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
            double length = EdgeFogMath.Length(_config.EdgeSide, ActualWidth, ActualHeight);
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
            if (_fogN == 0 || _fogTint == null) return;
            double aw = ActualWidth, ah = ActualHeight;
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
                    DrawSprite(canvas, big ? GlowSprite : Dot, (float)x * sx, (float)y * sy,
                        (horizontal ? span : p.Size) * sx, (horizontal ? p.Size : span) * sy);
                }
            }
            _paint.ColorFilter = null;
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
                    EdgeFogMath.Depth(p.BaseDepth, p.Breathe, p.Phase), ActualWidth, ActualHeight);
                r[i] = (x, y, p.Size, p.Big);
            }
            return r;
        }

        /// <summary>The Reduced allowance: a fog layer marked <see cref="AmbientFxConfig.EdgeFogReduced"/>
        /// may run at Reduced motion on a tier that allows ambient motion and particles. Every
        /// other layer keeps the Full-only gate.</summary>
        private bool ReducedFogMayRun() =>
            FogLayer && _config.EdgeFogReduced
            && MotionFx.Level == Models.MotionLevel.Reduced
            && PerformanceProfile.AllowAmbientMotion(_tier) && _particleBudget > 0;
    }
}
