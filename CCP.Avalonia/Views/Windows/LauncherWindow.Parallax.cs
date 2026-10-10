// PORTED from WPF 7.1.5 Windows/Launcher/LauncherWindow.Backdrop.cs:26-32 and 211-280 (cursor parallax).
// Every backdrop layer is nudged by the cursor for depth: the glow, the pool and the ember canvas ride with
// the hand, the two spirals lean away. WPF retargets a 260 ms quadratic ease-out per layer at 30 fps; here
// the launcher's one frame clock steps the same eases (LauncherWindow.Fx.cs), so nothing ticks at rest.
// IN = the ease toward the cursor, OUT = the same ease back to 0 when the pointer leaves or the launcher
// parks. Off on the Performance tier and when the motion level forbids transitions (the layers snap to 0).
using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Serilog;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class LauncherWindow
    {
        // Backdrop.cs:26-32
        internal const int ParallaxMinGapMs = 33;
        internal const double ParallaxEaseSeconds = 0.26;
        internal const double ParallaxGlowPx = 18, ParallaxPoolPx = 14, ParallaxSpiralPx = 12, ParallaxAmbientPx = 8;
        internal const double ParallaxSmallSpiralFactor = 0.7;

        /// <summary>One sliding layer: its shift and the two eases that drive it.</summary>
        private sealed class Slide
        {
            public readonly TranslateTransform Shift = new();
            public readonly double Px;
            public Tween X, Y;
            public Slide(double px) => Px = px;
            public bool Busy => X.On || Y.On;
        }

        private readonly Slide _glowSlide = new(ParallaxGlowPx), _poolSlide = new(ParallaxPoolPx),
            _ambientSlide = new(ParallaxAmbientPx), _spiralSlide = new(-ParallaxSpiralPx),
            _spiralSmallSlide = new(-ParallaxSpiralPx * ParallaxSmallSpiralFactor);
        private Slide[] _slides = Array.Empty<Slide>();
        private DateTime _lastParallax = DateTime.MinValue;

        internal TranslateTransform GlowShift => _glowSlide.Shift;
        internal TranslateTransform PoolShift => _poolSlide.Shift;
        internal TranslateTransform AmbientShift => _ambientSlide.Shift;
        internal TranslateTransform SpiralShift => _spiralSlide.Shift;
        internal TranslateTransform SpiralSmallShift => _spiralSmallSlide.Shift;

        private static bool ParallaxAllowed => Env.AllowTransitions && !PerfLow;   // Backdrop.cs:217

        /// <summary>Called once from HookFx: each backdrop layer gets its own shift (WPF LauncherWindow.xaml:256-334).</summary>
        private void HookParallax()
        {
            _slides = new[] { _glowSlide, _poolSlide, _ambientSlide, _spiralSlide, _spiralSmallSlide };
            SpiralLayer.RenderTransform = new TransformGroup { Children = { SpiralTurn, SpiralShift } };
            SpiralSmall.RenderTransform = new TransformGroup { Children = { SpiralSmallTurn, SpiralSmallShift } };
            GlowLayer.RenderTransform = GlowShift;
            PoolLayer.RenderTransform = PoolShift;
            Ambient.RenderTransform = AmbientShift;
            PointerMoved += OnBackdropPointerMoved;
            PointerExited += (_, _) => SettleParallax();
        }

        private void OnBackdropPointerMoved(object? sender, PointerEventArgs e)
        {
            try
            {
                if (!FxLive || !ParallaxAllowed) return;
                var now = DateTime.UtcNow;
                if ((now - _lastParallax).TotalMilliseconds < ParallaxMinGapMs) return;
                _lastParallax = now;
                ParallaxToward(e.GetPosition(this));
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] parallax failed"); }
        }

        /// <summary>WPF OnBackdropMouseMove's body, without the 33 ms gate (tests call it).</summary>
        internal void ParallaxToward(Point at)
        {
            if (!ParallaxAllowed || Bounds.Width <= 0 || Bounds.Height <= 0) return;
            double nx = Math.Clamp(at.X / Bounds.Width * 2 - 1, -1, 1);
            double ny = Math.Clamp(at.Y / Bounds.Height * 2 - 1, -1, 1);
            ApplyParallax(nx, ny);
        }

        /// <summary>Near layers follow the cursor, far layers move against it; the difference is the depth.</summary>
        private void ApplyParallax(double nx, double ny)
        {
            foreach (var s in _slides)
            {
                s.X.Go(s.Shift.X, nx * s.Px, ParallaxEaseSeconds);
                s.Y.Go(s.Shift.Y, ny * s.Px, ParallaxEaseSeconds);
            }
            EnsureFxClock();
        }

        /// <summary>WPF SettleParallax: eased back to 0 while motion is allowed and the launcher shows, else snapped.</summary>
        internal void SettleParallax()
        {
            try
            {
                if (ParallaxAllowed && FxLive) { ApplyParallax(0, 0); return; }
                foreach (var s in _slides)
                {
                    s.X.On = s.Y.On = false;
                    s.Shift.X = s.Shift.Y = 0;
                }
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] parallax settle failed"); }
        }

        /// <summary>One frame of the eases; true while any layer is still moving.</summary>
        private bool StepParallax(double dt)
        {
            bool busy = false;
            foreach (var s in _slides)
            {
                if (!s.Busy) continue;
                if (!ParallaxAllowed) { s.X.On = s.Y.On = false; s.Shift.X = s.Shift.Y = 0; continue; }
                if (s.X.On) s.Shift.X = s.X.Step(dt);
                if (s.Y.On) s.Shift.Y = s.Y.Step(dt);
                busy |= s.Busy;
            }
            return busy;
        }
    }
}
