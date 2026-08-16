using System;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Services.Descent;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// THE FIRST LIGHT's canvas — the reveal that plays inside the map window the one time this
    /// account is shown the spiral (CONTRACT-FUSE-0816 §2.4, owner ruling 2026-08-16: "go crazy
    /// with those 3-4 seconds where we reveal the spiral for the first time").
    ///
    /// <para><b>Same construction as <see cref="DescentFuseStageVisual"/>, for the same reasons.</b>
    /// A raw <see cref="FrameworkElement"/> with one <see cref="OnRender"/> rather than a Canvas of
    /// Shapes: the figure is two hundred arm segments, four fog blobs and twenty-two embers, and as
    /// Shapes that is two hundred and thirty FrameworkElements going through measure, arrange and
    /// hit-testing every frame for a surface nobody can click. Every brush and pen comes from a
    /// frozen ramp indexed by alpha, every layout is authored ONCE in unit space in the constructor,
    /// and nothing allocates in the loop.</para>
    ///
    /// <para><b>OPACITY AND TRANSFORM ONLY.</b> No layout is animated, no BitmapEffect exists, and
    /// the window this lives in is not layered — it is the ordinary opaque 900x700 map window. The
    /// whole reveal is a <see cref="DrawingContext"/> repainting itself off a pure timeline
    /// (<see cref="SpiralFirstLightTimeline"/>), which is the cheapest thing WPF can be asked to do
    /// and the only shape this repo trusts near a window that also hosts a WebView2.</para>
    ///
    /// <para><b>THE AIRSPACE RULE THIS EXISTS UNDER.</b> A WebView2 is a native child HWND and
    /// paints OVER any WPF content in the same window regardless of z-order, so this canvas can
    /// never be drawn on top of a live embed. Its host therefore does not merely hide the embed for
    /// the length of the intro — it does not CONSTRUCT one, and builds it only at the hand-off. See
    /// Views/Tabs/SpiralTabView.xaml.cs (the window this used to live in retired 2026-08-16).</para>
    ///
    /// <para><b>The mod's accent, resolved once by the window and handed in.</b> A brush frozen in a
    /// constructor cannot follow a later mod switch, which is the 2026-08-13 sweep's whole bug
    /// class; it is safe here for the same reason it is safe in the show: this element lives for
    /// four seconds inside one window and is thrown away.</para>
    /// </summary>
    public sealed class SpiralFirstLightVisual : FrameworkElement
    {
        // ================================ the ramps ================================

        private const int RampSteps = 32;

        /// <summary>The hot centre of the pink. Ember cores and the arm's head burn this.</summary>
        private static readonly Color Hot = Color.FromRgb(0xFF, 0xD9, 0xEC);

        private static readonly SolidColorBrush[] HotRamp = BuildRamp(Hot);

        private readonly SolidColorBrush[] _accentRamp;
        private readonly Pen[] _armPens;

        /// <summary>The room the reveal stands in: the ceremony's own radial ground
        /// (#241041 → #1A1A2E → #0A0514), frozen once.</summary>
        private static readonly Brush Backdrop = BuildBackdrop();

        private readonly Brush _fogBrush;
        private readonly Brush _bloomBrush;

        private static SolidColorBrush[] BuildRamp(Color color)
        {
            var ramp = new SolidColorBrush[RampSteps + 1];
            for (int i = 0; i <= RampSteps; i++)
            {
                var a = (byte)Math.Clamp(Math.Round(255.0 * i / RampSteps), 0, 255);
                var b = new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B));
                b.Freeze();
                ramp[i] = b;
            }
            return ramp;
        }

        private static Pen[] BuildPens(SolidColorBrush[] ramp, double thickness)
        {
            var pens = new Pen[ramp.Length];
            for (int i = 0; i < ramp.Length; i++)
            {
                var p = new Pen(ramp[i], thickness)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                };
                p.Freeze();
                pens[i] = p;
            }
            return pens;
        }

        private static Brush BuildBackdrop()
        {
            var b = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.75,
                RadiusY = 0.75,
            };
            b.GradientStops.Add(new GradientStop(Color.FromRgb(0x24, 0x10, 0x41), 0.0));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(0x1A, 0x1A, 0x2E), 0.55));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(0x0A, 0x05, 0x14), 1.0));
            b.Freeze();
            return b;
        }

        /// <summary>Index into a ramp for an opacity in 0..1.</summary>
        private static int Step(double alpha)
        {
            if (double.IsNaN(alpha) || alpha <= 0) return 0;
            if (alpha >= 1) return RampSteps;
            return (int)(alpha * RampSteps);
        }

        // ================================ the figure ================================

        /// <summary>Samples along the arm. The same count the ignition draws at.</summary>
        private const int ArmSamples = 200;

        /// <summary>Turns of the arm. Wider than SpiralGlyph's 2.5 — this is drawn at 700px.</summary>
        private const double ArmTurns = 3.0;

        /// <summary>Outer radius as a fraction of the short half-side.</summary>
        private const double ArmRadius = 0.78;

        /// <summary>The arm in unit polar form, so the whole figure can be rotated per frame by
        /// adding one angle instead of rebuilding two hundred points.</summary>
        private readonly double[] _armAngle = new double[ArmSamples + 1];
        private readonly double[] _armRadius = new double[ArmSamples + 1];

        // ---- fog ----
        private readonly double[] _fogPhase = new double[SpiralFirstLightTimeline.FogBlobs];
        private readonly double[] _fogRate = new double[SpiralFirstLightTimeline.FogBlobs];
        private readonly double[] _fogOrbit = new double[SpiralFirstLightTimeline.FogBlobs];
        private readonly double[] _fogSize = new double[SpiralFirstLightTimeline.FogBlobs];
        private readonly double[] _fogAlpha = new double[SpiralFirstLightTimeline.FogBlobs];

        // ---- embers ----
        private readonly double[] _emberX = new double[SpiralFirstLightTimeline.EmberCount];
        private readonly double[] _emberSpeed = new double[SpiralFirstLightTimeline.EmberCount];
        private readonly double[] _emberDrift = new double[SpiralFirstLightTimeline.EmberCount];
        private readonly double[] _emberSize = new double[SpiralFirstLightTimeline.EmberCount];
        private readonly double[] _emberSeed = new double[SpiralFirstLightTimeline.EmberCount];

        // ---- the live frame ----

        private bool _reduced;
        private double _elapsed;
        private double _ambient;

        /// <summary>
        /// Lay the reveal out. <paramref name="accent"/> is the mod's pink, read by the window from
        /// the live resource dictionary — see the class note on why freezing it is safe here.
        /// </summary>
        public SpiralFirstLightVisual(Color accent)
        {
            IsHitTestVisible = false;
            UseLayoutRounding = true;

            _accentRamp = BuildRamp(accent);
            _armPens = BuildPens(_accentRamp, 3.2);

            var fog = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5,
            };
            fog.GradientStops.Add(new GradientStop(Color.FromArgb(0x5C, accent.R, accent.G, accent.B), 0.0));
            fog.GradientStops.Add(new GradientStop(Color.FromArgb(0x24, accent.R, accent.G, accent.B), 0.45));
            fog.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1.0));
            fog.Freeze();
            _fogBrush = fog;

            var bloom = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5,
            };
            bloom.GradientStops.Add(new GradientStop(Color.FromArgb(0xD2, accent.R, accent.G, accent.B), 0.0));
            bloom.GradientStops.Add(new GradientStop(Color.FromArgb(0x78, accent.R, accent.G, accent.B), 0.32));
            bloom.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1.0));
            bloom.Freeze();
            _bloomBrush = bloom;

            for (int i = 0; i <= ArmSamples; i++)
            {
                var t = i / (double)ArmSamples;
                _armAngle[i] = t * ArmTurns * Math.PI * 2;
                _armRadius[i] = ArmRadius * t;
            }

            // A FIXED SEED, like the show's. The reveal is different from the crack but identical
            // from run to run, which is the only way a report about "the reveal looked wrong" can
            // ever be reproduced.
            var rng = new Random(0x11F1A57);

            for (int i = 0; i < SpiralFirstLightTimeline.FogBlobs; i++)
            {
                _fogPhase[i] = rng.NextDouble() * Math.PI * 2;
                _fogRate[i] = 0.16 + rng.NextDouble() * 0.22;     // radians/second, all slow
                _fogOrbit[i] = 0.20 + rng.NextDouble() * 0.34;    // fraction of the short half-side
                _fogSize[i] = 0.70 + rng.NextDouble() * 0.65;
                _fogAlpha[i] = 0.55 + rng.NextDouble() * 0.45;
            }

            for (int i = 0; i < SpiralFirstLightTimeline.EmberCount; i++)
            {
                _emberX[i] = rng.NextDouble();                    // 0..1 across the window
                _emberSpeed[i] = 0.10 + rng.NextDouble() * 0.16;  // fraction of height per second
                _emberDrift[i] = (rng.NextDouble() - 0.5) * 0.05;
                _emberSize[i] = 1.0 + rng.NextDouble() * 1.7;
                _emberSeed[i] = rng.NextDouble();
            }
        }

        // ================================ the driver ================================

        /// <summary>Point the canvas at a motion level. Called once, before the clock starts.</summary>
        public void Begin(bool reducedMotion) => _reduced = reducedMotion;

        /// <summary>
        /// Hand the canvas its two clocks and repaint.
        /// </summary>
        /// <param name="elapsedSeconds">The INTRO clock, which the window holds at the bloom while
        /// the descent block is still in flight (see SpiralFirstLightTimeline's hold note).</param>
        /// <param name="ambientSeconds">The wall clock since the window opened. The fog drifts off
        /// this one, so a held bloom is still a moving picture rather than a frozen frame.</param>
        public void SetFrame(double elapsedSeconds, double ambientSeconds)
        {
            _elapsed = elapsedSeconds;
            _ambient = ambientSeconds;
            InvalidateVisual();
        }

        // ================================ the paint ================================

        protected override void OnRender(DrawingContext dc)
        {
            try
            {
                double w = ActualWidth, h = ActualHeight;
                if (w <= 1 || h <= 1) return;

                var layer = SpiralFirstLightTimeline.LayerOpacity(_elapsed, _reduced);
                if (layer <= 0.004) return;

                var centre = new Point(w / 2, h / 2);
                double unit = Math.Min(w, h) / 2.0;

                dc.PushOpacity(layer);

                // The ground goes down at full every frame — the layer's own opacity is what makes
                // the whole picture leave, and a backdrop that faded separately would show the
                // window's flat background through the fog on the way out.
                dc.DrawRectangle(Backdrop, null, new Rect(0, 0, w, h));

                // The fog is authored against the WINDOW and the figure against the SHORT SIDE, on
                // purpose: the fog has to fill the room whatever shape it is, while the spiral must
                // stay a circle on an ultrawide.
                DrawFog(dc, centre, w, h);
                DrawArm(dc, centre, unit);
                DrawEmbers(dc, w, h);
                DrawBloom(dc, centre, unit);

                dc.Pop();
            }
            catch (Exception ex)
            {
                // A reveal that throws must go dark, not take the window down: the embed is still
                // underneath and the hand-off still happens on its own clock.
                App.Logger?.Debug("[Spiral] first-light frame failed: {E}", ex.Message);
            }
        }

        // ---------------------------------------------------------------- the fog

        /// <summary>
        /// Four drifting radial blobs. They orbit rather than wander: an orbit is one cosine per
        /// axis with no state to accumulate, so the fog is exactly reproducible from a timestamp and
        /// cannot drift off screen however long the intro is held waiting for a block.
        /// </summary>
        private void DrawFog(DrawingContext dc, Point centre, double w, double h)
        {
            var envelope = SpiralFirstLightTimeline.FogOpacity(_elapsed, _reduced);
            if (envelope <= 0.004) return;

            // Reach past the corners so the fog is a room and not four visible discs.
            var spread = Math.Max(w, h) * 0.55;

            for (int i = 0; i < SpiralFirstLightTimeline.FogBlobs; i++)
            {
                var angle = _fogPhase[i] + _ambient * _fogRate[i];
                var orbit = _fogOrbit[i] * spread;

                var cx = centre.X + Math.Cos(angle) * orbit;
                var cy = centre.Y + Math.Sin(angle * 0.78) * orbit * 0.72;

                // A slow breath on the size, out of phase with the orbit so the blobs never all
                // swell together.
                var breathe = 1.0 + 0.14 * Math.Sin(_ambient * 0.55 + _fogPhase[i]);
                var radius = spread * 0.52 * _fogSize[i] * breathe;

                dc.PushOpacity(envelope * _fogAlpha[i]);
                dc.DrawEllipse(_fogBrush, null, new Point(cx, cy), radius, radius);
                dc.Pop();
            }
        }

        // ---------------------------------------------------------------- the arm

        private void DrawArm(DrawingContext dc, Point centre, double unit)
        {
            var drawn = SpiralFirstLightTimeline.DrawFraction(_elapsed, _reduced);
            if (drawn <= 0) return;

            var swirl = SpiralFirstLightTimeline.SwirlAngle(_elapsed, _reduced);

            int last = (int)Math.Round(drawn * ArmSamples);
            if (last < 1) last = 1;

            var prev = PointOn(0, centre, unit, swirl);
            for (int i = 1; i <= last && i <= ArmSamples; i++)
            {
                var next = PointOn(i, centre, unit, swirl);

                // Brightest just behind the head, settling to a steady arm further back — the light
                // is being drawn, not switched on.
                var behind = (last - i) / (double)ArmSamples;
                var a = 0.58 + 0.42 * Math.Max(0, 1.0 - behind * 9.0);
                dc.DrawLine(_armPens[Step(a)], prev, next);

                prev = next;
            }

            // The travelling head. Gone once the arm is finished, so the last frame is a spiral and
            // not a spiral with a dot stuck on the end of it.
            if (drawn < 1.0)
            {
                var head = PointOn(last, centre, unit, swirl);
                dc.DrawEllipse(HotRamp[Step(0.75)], null, head, unit * 0.014, unit * 0.014);
                dc.DrawEllipse(_accentRamp[Step(0.30)], null, head, unit * 0.038, unit * 0.038);
            }

            foreach (var at in SpiralFirstLightTimeline.DotAt)
            {
                var g = SpiralFirstLightTimeline.DotGlow(_elapsed, at, _reduced);
                if (g <= 0.004) continue;

                int idx = (int)Math.Round(at * ArmSamples);
                if (idx > ArmSamples) idx = ArmSamples;
                var pt = PointOn(idx, centre, unit, swirl);

                dc.DrawEllipse(_accentRamp[Step(0.28 * g)], null, pt, unit * (0.007 + 0.022 * g), unit * (0.007 + 0.022 * g));
                dc.DrawEllipse(HotRamp[Step(g)], null, pt, unit * 0.009, unit * 0.009);
            }
        }

        /// <summary>The arm sample <paramref name="i"/>, rotated by the swirl and scaled to the box.</summary>
        private Point PointOn(int i, Point centre, double unit, double swirl)
        {
            var a = _armAngle[i] + swirl;
            var r = _armRadius[i] * unit;
            return new Point(centre.X + Math.Cos(a) * r, centre.Y + Math.Sin(a) * r);
        }

        // ------------------------------------------------------------- the embers

        /// <summary>
        /// Sparks lifting through the figure. Positions are a pure function of the ambient clock —
        /// no per-frame state, so a held intro keeps them rising and a dropped frame does not
        /// teleport them.
        /// </summary>
        private void DrawEmbers(DrawingContext dc, double w, double h)
        {
            var envelope = SpiralFirstLightTimeline.EmberOpacity(_elapsed, _reduced);
            if (envelope <= 0.004) return;

            for (int i = 0; i < SpiralFirstLightTimeline.EmberCount; i++)
            {
                // Wrapped, so the field never empties however long the bloom is held.
                var climb = (_emberSeed[i] + _ambient * _emberSpeed[i]) % 1.0;

                var y = h * (1.05 - climb * 1.15);
                var x = w * _emberX[i] + Math.Sin(_ambient * 0.9 + _emberSeed[i] * 6.28) * w * _emberDrift[i];

                // Fade in off the floor and out near the top: an ember that pops into existence at
                // full brightness reads as a dead pixel.
                var edge = Math.Min(1.0, Math.Min(climb * 6.0, (1.0 - climb) * 3.0));
                var a = envelope * edge * (0.35 + 0.5 * _emberSeed[i]);
                if (a <= 0.004) continue;

                dc.DrawEllipse(HotRamp[Step(a)], null, new Point(x, y), _emberSize[i], _emberSize[i]);
            }
        }

        // -------------------------------------------------------------- the bloom

        private void DrawBloom(DrawingContext dc, Point centre, double unit)
        {
            var b = SpiralFirstLightTimeline.BloomOpacity(_elapsed, _reduced);
            if (b <= 0.004) return;

            // Out of the same centre the arm was drawn from. It grows past the corners, so the last
            // thing on screen before the embed arrives is light rather than a shrinking picture.
            var radius = unit * (0.9 + 1.8 * b);

            dc.PushOpacity(Math.Min(1.0, b * 1.15));
            dc.DrawEllipse(_bloomBrush, null, centre, radius, radius);
            dc.Pop();

            var core = Math.Max(0, 1.0 - b);
            if (core > 0.01)
                dc.DrawEllipse(HotRamp[Step(core * 0.6)], null, centre, unit * 0.04 * core, unit * 0.04 * core);
        }
    }
}
