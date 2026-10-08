// PORTED from WPF 7.1.5 ConditioningControlPanel/Controls/NavRail/NavGlow.cs. Timings, opacity
// track and ring geometry are Core (ConditioningControlPanel.Nav.NavGlowRules); this file draws.

using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using Serilog;
using CoreNav = global::ConditioningControlPanel.Nav;

namespace ConditioningControlPanel.Avalonia.Controls.NavRail
{
    /// <summary>
    /// The one "look here" glow of the nav rework: a rounded ring plus a soft halo in the section
    /// accent, drawn on the window's adorner layer over the element, so it shows on any target (a
    /// transparent pill, a rail row) and never fights the element's own look. Full = sheen in,
    /// 2 s hold, fade; Reduced = static hold; Off = nothing. A one-shot cue, not an ambient loop,
    /// so the performance tier does not gate it. It leaves with its target: the adorner layer is
    /// the window's, so a hidden target would otherwise keep a ring painted over the next page.
    /// </summary>
    public static class NavGlow
    {
        /// <summary>The last glow started (tests).</summary>
        internal static GlowAdorner? Last { get; private set; }

        /// <summary>Glow <paramref name="target"/> once. False (with a Debug line) when it cannot.</summary>
        public static bool Once(Control? target, uint accent, MotionLevel? levelOverride = null, string? why = null)
        {
            var level = levelOverride ?? AmbientFxCanvas.Env.Level;
            if (target == null) { Log.Debug("NavGlow({Why}): no target", why); return false; }
            if (level == MotionLevel.Off) { Log.Debug("NavGlow({Why}): motion off", why); return false; }
            if (!target.IsEffectivelyVisible) { Log.Debug("NavGlow({Why}): {T} is hidden", why, target.Name); return false; }
            var layer = AdornerLayer.GetAdornerLayer(target);
            if (layer == null) { Log.Debug("NavGlow({Why}): no adorner layer on {T}", why, target.Name); return false; }

            var ring = new GlowAdorner(target, accent, level);
            AdornerLayer.SetAdornedElement(ring, target);
            // The halo reaches 15 px past the target; the layer must not clip it to the target.
            AdornerLayer.SetIsClipEnabled(ring, false);
            layer.Children.Add(ring);
            ring.Begin();
            Last = ring;
            Log.Debug("NavGlow({Why}): on {T} {W}x{H}", why, target.Name, target.Bounds.Width, target.Bounds.Height);
            return true;
        }

        /// <summary>The ring itself. Opacity follows <see cref="CoreNav.NavGlowRules.OpacityTrack"/>.</summary>
        internal sealed class GlowAdorner : Control
        {
            private readonly Control _target;
            private readonly MotionLevel _level;
            private readonly IPen _ring;
            private readonly IPen[] _halo = new IPen[CoreNav.NavGlowRules.HaloCount];
            private readonly IBrush _fill;
            private readonly Stopwatch _watch = new();
            private DispatcherTimer? _timer;
            private IDisposable? _watchShown;
            private double _ms;

            internal GlowAdorner(Control target, uint accent, MotionLevel level)
            {
                _target = target;
                _level = level;
                IsHitTestVisible = false;
                Opacity = 0;
                var c = NavPaint.C(accent);
                _ring = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(c.R, c.G, c.B)), CoreNav.NavGlowRules.RingThickness);
                for (int i = 0; i < _halo.Length; i++)
                    _halo[i] = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(CoreNav.NavGlowRules.HaloAlpha(i), c.R, c.G, c.B)),
                                                CoreNav.NavGlowRules.HaloThickness);
                _fill = new ImmutableSolidColorBrush(Color.FromArgb(CoreNav.NavGlowRules.FillAlpha, c.R, c.G, c.B));
            }

            /// <summary>True until the glow has played out or its target hid (tests).</summary>
            internal bool IsLive { get; private set; }

            internal void Begin()
            {
                IsLive = true;
                // An ancestor hiding (the strip collapsing, a tab switch) drops the glow at once.
                _watchShown = EffectiveVisibility.Watch(_target, () =>
                {
                    if (!_target.IsEffectivelyVisible) Drop();
                });
                Advance(0);
                _watch.Start();
                _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Advance(_watch.Elapsed.TotalMilliseconds - _ms));
                _timer.Start();
            }

            /// <summary>Moves the glow on by <paramref name="ms"/> (the timer; tests).</summary>
            internal void Advance(double ms)
            {
                if (!IsLive) return;
                _ms += Math.Max(0, ms);
                if (_ms >= CoreNav.NavGlowRules.TotalMs(_level) && _ms > 0) { Drop(); return; }
                Opacity = CoreNav.NavGlowRules.OpacityAt(_level, _ms);
            }

            internal void Drop()
            {
                if (!IsLive) return;
                IsLive = false;
                _timer?.Stop();
                _timer = null;
                _watchShown?.Dispose();
                _watchShown = null;
                Opacity = 0;
                try { if (Parent is Panel p) p.Children.Remove(this); } catch { }
            }

            public override void Render(DrawingContext dc)
            {
                var size = _target.Bounds.Size;
                if (size.Width <= 0 || size.Height <= 0) return;
                double o = CoreNav.NavGlowRules.RingOutsetPx;
                var r = new Rect(-o, -o, size.Width + 2 * o, size.Height + 2 * o);
                double radius = CoreNav.NavGlowRules.RingRadius(size.Height);
                for (int i = _halo.Length - 1; i >= 0; i--)
                {
                    double d = CoreNav.NavGlowRules.HaloInflate(i);
                    var g = r.Inflate(d);
                    dc.DrawRectangle(null, _halo[i], g, radius + d, radius + d);
                }
                dc.DrawRectangle(_fill, _ring, r, radius, radius);
            }
        }
    }
}
