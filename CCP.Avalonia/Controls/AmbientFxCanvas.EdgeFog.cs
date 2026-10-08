using System;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Fx;
using Serilog;
using SkiaSharp;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

namespace ConditioningControlPanel.Avalonia.Controls
{
    // PORTED from ConditioningControlPanel/Controls/AmbientFxCanvas.EdgeFog.cs (nav polish wave
    // 11, 2026-10-07): the sim and paint of the section edge's fog. The numbers are Core
    // EdgeFogMath (ConditioningControlPanel.Fx), the dust colour Core NavRailRules.Vivid.

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
            var vivid = global::ConditioningControlPanel.Nav.NavRailRules.Vivid(Argb.FromRgb(_fogNow.Red, _fogNow.Green, _fogNow.Blue));
            var (vr, vg, vb) = (Argb.R(vivid), Argb.G(vivid), Argb.B(vivid));
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
