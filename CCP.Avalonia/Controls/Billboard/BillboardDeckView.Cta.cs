using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Motion;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard
{
    /// <summary>
    /// The card button's pop (WPF 7.1.5 BillboardDeckView.Cta; owner, 2026-10-07: "make the buttons
    /// pop there too"): a glow in the card's hue that breathes under the plank, a shine that crosses
    /// the face now and then, a bigger hover (scale, glow up, a shine and a few glints) and a squash
    /// on press. FX PERF RULES: the glow is a sibling layer whose Opacity tweens, never an effect;
    /// the shine is one small rectangle sliding across the face. Depth law stays with Lift /
    /// PressDown / PressUp; this only adds to it. Ambient loops off = a still glow and no shine;
    /// Motion Off = nothing moves.
    /// </summary>
    public sealed partial class BillboardDeckView
    {
        internal const double CtaGlowIdle = 0.32, CtaGlowBreath = 0.62, CtaGlowHover = 0.95;
        internal const double CtaHoverScale = 1.05;
        internal const double CtaBreathSeconds = 1.6, CtaShineSeconds = 0.6, CtaShineEverySeconds = 4.6;

        private readonly Border _ctaGlow = new();
        private readonly Rectangle _ctaShine = new() { IsHitTestVisible = false };
        private readonly TranslateTransform _ctaShineMove = new();
        private double _ctaShineAt = -1.2;   // the band's place across the face, -1.2 (off left) .. 1.2 (off right)
        private readonly ScaleTransform _ctaSquash = new(1, 1);
        private readonly ScaleTransform _ctaHover = new(1, 1);
        private bool _ctaHovered;
        private int _ctaFxToken;

        /// <summary>True while the button's idle breath runs (tests).</summary>
        internal bool CtaLoopsRunning { get; private set; }

        /// <summary>The glow opacity now (tests).</summary>
        internal double CtaGlowOpacity => _ctaGlow.Opacity;

        /// <summary>The glow layer, the shine and the squash, wired into the button the CTA build made.</summary>
        private void BuildCtaFx(Panel root, Panel faceGrid)
        {
            _ctaGlow.CornerRadius = new CornerRadius(18);
            _ctaGlow.Margin = new Thickness(-10, -8, -10, -6);
            _ctaGlow.IsHitTestVisible = false;
            _ctaGlow.Opacity = CtaGlowIdle;
            root.Children.Insert(0, _ctaGlow);

            _ctaShine.Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 0xff, 0xff, 0xff), 0.36),
                    new GradientStop(Color.FromArgb(0x8c, 0xff, 0xf6, 0xfc), 0.5),
                    new GradientStop(Color.FromArgb(0, 0xff, 0xff, 0xff), 0.64),
                },
            };
            _ctaShine.RenderTransform = _ctaShineMove;
            faceGrid.Children.Insert(Math.Min(1, faceGrid.Children.Count), _ctaShine);
            faceGrid.SizeChanged += (_, _) => PlaceShine();

            _ctaFace.RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative);
            _ctaFace.RenderTransform = new TransformGroup { Children = { _ctaSquash, _ctaPress } };
            ((TransformGroup)root.RenderTransform!).Children.Add(_ctaHover);
            _cta.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) RefreshCtaLoops(); };
        }

        /// <summary>WPF slid the brush (RelativeTransform); here the shine rectangle slides by its own width.</summary>
        private void PlaceShine() => _ctaShineMove.X = _ctaShineAt * Math.Max(1, _ctaShine.Bounds.Width);

        private void SetShine(double v) { _ctaShineAt = v; PlaceShine(); }

        /// <summary>A card landed or settled: tint the glow and (re)start the breath.</summary>
        private void RefreshCtaFx(bool landed, double shineAtMs)
        {
            var hue = _card != null ? HueOf(_card) : Color.FromRgb(0xff, 0x4f, 0xa8);
            _ctaGlow.Background = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0xb0, hue.R, hue.G, hue.B), 0),
                    new GradientStop(Color.FromArgb(0x70, hue.R, hue.G, hue.B), 0.55),
                    new GradientStop(Color.FromArgb(0, hue.R, hue.G, hue.B), 1),
                },
            };
            if (landed && BoardMotion.AllowTransitions && _cta.IsVisible)
            {
                // The landing flare: the glow blooms with the thud and settles into the breath.
                int token = ++_ctaFxToken;
                _tw.To(_ctaGlow, "opacity", () => _ctaGlow.Opacity, v => _ctaGlow.Opacity = v, CtaGlowHover, CtaGlowIdle,
                    Ms(700), delayMs: shineAtMs, done: () => { if (token == _ctaFxToken) RefreshCtaLoops(); });
                ShineOnce(shineAtMs);
                CtaLoopsRunning = false;
                return;
            }
            RefreshCtaLoops();
        }

        /// <summary>The idle breath and the periodic shine, from the gates there are.</summary>
        private void RefreshCtaLoops()
        {
            int token = ++_ctaFxToken;
            bool loops = BoardMotion.AllowAmbientLoops && _cta.IsVisible;
            if (_ctaHovered && BoardMotion.AllowTransitions && _cta.IsVisible)
            {
                _tw.To(_ctaGlow, "opacity", () => _ctaGlow.Opacity, v => _ctaGlow.Opacity = v, null, CtaGlowHover, 160);
                CtaLoopsRunning = false;
                return;
            }
            if (!loops)
            {
                _tw.Stop(_ctaGlow, "opacity");
                _ctaGlow.Opacity = CtaGlowIdle;
                _tw.Stop(_ctaShineMove, "x");
                SetShine(-1.2);
                CtaLoopsRunning = false;
                return;
            }

            // Ease from wherever the glow is to the floor, then breathe floor <-> peak forever.
            void Breathe(bool up)
            {
                if (token != _ctaFxToken) return;
                _tw.To(_ctaGlow, "opacity", () => _ctaGlow.Opacity, v => _ctaGlow.Opacity = v, null,
                    up ? CtaGlowBreath : CtaGlowIdle, CtaBreathSeconds * 1000, Easings.SineInOut, done: () => Breathe(!up));
            }
            _tw.To(_ctaGlow, "opacity", () => _ctaGlow.Opacity, v => _ctaGlow.Opacity = v, null, CtaGlowIdle, 260, done: () => Breathe(true));

            // The sweep: off left, across in 0.6 s, then wait out the rest of its 4.6 s, again.
            void Sweep(double delay)
            {
                if (token != _ctaFxToken) return;
                _tw.To(_ctaShineMove, "x", () => _ctaShineAt, SetShine, -1.2, 1.2, CtaShineSeconds * 1000, Easings.QuadInOut, delay,
                    done: () => Sweep((CtaShineEverySeconds - CtaShineSeconds) * 1000));
            }
            Sweep(1200);
            CtaLoopsRunning = true;
        }

        /// <summary>One shine across the face (hover, landing).</summary>
        private void ShineOnce(double delayMs)
        {
            if (!BoardMotion.AllowTransitions) return;
            int ms = Math.Max(1, Ms((int)(CtaShineSeconds * 1000)));
            _tw.To(_ctaShineMove, "x", () => _ctaShineAt, SetShine, -1.2, 1.2, ms, Easings.QuadInOut, delayMs);
        }

        /// <summary>Hover: the plank swells, the glow comes up, a shine crosses it and a few glints
        /// jump off its corner. Leaving eases it all back into the breath.</summary>
        private void CtaHover(bool on)
        {
            _ctaHovered = on;
            if (!BoardMotion.AllowTransitions)
            {
                _tw.Stop(_ctaHover, "s");
                _ctaHover.ScaleX = _ctaHover.ScaleY = 1;
                RefreshCtaLoops();
                return;
            }
            int ms = Ms(on ? 260 : 160);
            double to = on ? CtaHoverScale : 1.0;
            _tw.To(_ctaHover, "s", () => _ctaHover.ScaleX, v => { _ctaHover.ScaleX = v; _ctaHover.ScaleY = v; }, null, to, ms,
                on ? Easings.Thud : Easings.QuadOut);
            if (on)
            {
                ++_ctaFxToken;
                CtaLoopsRunning = false;
                _tw.To(_ctaGlow, "opacity", () => _ctaGlow.Opacity, v => _ctaGlow.Opacity = v, null, CtaGlowHover, 160);
                ShineOnce(0);
                if (_card != null && _cta.Bounds.Width > 0)
                {
                    var corner = _cta.TranslatePoint(new Point(_cta.Bounds.Width - 8, 6), _stage) ?? default;
                    Burst(corner, Lighter(HueOf(_card)), 6, 0.32);
                }
            }
            else RefreshCtaLoops();
        }

        /// <summary>Press: the face squashes into its socket (wider, shorter) with the 2 px sink;
        /// release springs it back past round and settles.</summary>
        private void CtaSquash(bool down)
        {
            void Set(double x, double y) { _ctaSquash.ScaleX = x; _ctaSquash.ScaleY = y; }
            if (!BoardMotion.AllowTransitions)
            {
                _tw.Stop(_ctaSquash, "x"); _tw.Stop(_ctaSquash, "y");
                Set(1, 1);
                return;
            }
            if (down)
            {
                int d = Math.Max(1, Ms(DepthRules.PressMs));
                _tw.To(_ctaSquash, "x", () => _ctaSquash.ScaleX, v => _ctaSquash.ScaleX = v, null, 1.07, d);
                _tw.To(_ctaSquash, "y", () => _ctaSquash.ScaleY, v => _ctaSquash.ScaleY = v, null, 0.86, d);
                return;
            }
            int ms = Math.Max(1, Ms((int)(DepthRules.ReleaseMs * 1.6)));
            Spring(_ctaSquash, "x", () => _ctaSquash.ScaleX, v => _ctaSquash.ScaleX = v, 0.95, ms);
            Spring(_ctaSquash, "y", () => _ctaSquash.ScaleY, v => _ctaSquash.ScaleY = v, 1.07, ms);
        }

        /// <summary>Two keyframes: past rest at 45% (ease out), home at 100% (ease in-out).</summary>
        private void Spring(object target, string name, Func<double> get, Action<double> set, double past, int ms) =>
            _tw.To(target, name, get, set, null, past, ms * 0.45, Easings.QuadOut,
                done: () => _tw.To(target, name, get, set, null, 1, ms * 0.55, Easings.QuadInOut));

        private static Color Lighter(Color c) => Color.FromRgb(
            (byte)(c.R + (255 - c.R) * 0.5), (byte)(c.G + (255 - c.G) * 0.5), (byte)(c.B + (255 - c.B) * 0.5));
    }
}
