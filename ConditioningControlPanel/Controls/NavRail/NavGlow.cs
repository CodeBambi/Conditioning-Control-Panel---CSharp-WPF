using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls.NavRail
{
    /// <summary>
    /// The one "look here" glow of the nav rework (UX-PLAYBOOK moved-here shimmer): a rounded
    /// ring plus a soft halo in the section accent, drawn on an adorner over the element, so it
    /// shows on any target (a transparent pill, a bare text header) and never fights the
    /// element's own Effect. 2 s hold. Full = sheen in, hold, fade; Reduced = static hold;
    /// Off = nothing. It is a one-shot cue, not an ambient loop, so the performance tier does
    /// not gate it (that gate is why the zone glows never showed on the desk run).
    /// </summary>
    public static class NavGlow
    {
        public const int SheenMs = 600;
        public const int HoldMs = 2000;
        public const int FadeMs = 400;

        /// <summary>Total length at a motion level (0 = no glow).</summary>
        public static int TotalMs(MotionLevel level) => level switch
        {
            MotionLevel.Off => 0,
            MotionLevel.Reduced => HoldMs,
            _ => SheenMs + HoldMs + FadeMs,
        };

        /// <summary>Glow <paramref name="target"/> once. False (with a Debug line) when it cannot.</summary>
        public static bool Once(FrameworkElement? target, Color accent, MotionLevel? levelOverride = null, string? why = null)
        {
            var level = levelOverride ?? MotionFx.Level;
            if (target == null) { App.Logger?.Debug("NavGlow({Why}): no target", why); return false; }
            if (level == MotionLevel.Off) { App.Logger?.Debug("NavGlow({Why}): motion off", why); return false; }
            var layer = AdornerLayer.GetAdornerLayer(target);
            if (layer == null) { App.Logger?.Debug("NavGlow({Why}): no adorner layer on {T}", why, target.Name); return false; }

            var ring = new GlowAdorner(target, accent) { IsHitTestVisible = false, Opacity = 0 };
            layer.Add(ring);

            // An adorner layer keeps drawing an adorner after its element is collapsed, at the
            // element's last laid-out spot. That is what left a lilac ring over the dashboard
            // tiles after choosing Dashboard on Home > Premium (polish 12 round 2, owner desk
            // pass): the strip collapsed, its pill's glow did not. The glow leaves with its target.
            DependencyPropertyChangedEventHandler? gone = null;
            void Drop()
            {
                if (gone != null) target.IsVisibleChanged -= gone;
                ring.BeginAnimation(UIElement.OpacityProperty, null);
                try { layer.Remove(ring); } catch { }
            }
            gone = (_, e) => { if (e.NewValue is false) Drop(); };
            target.IsVisibleChanged += gone;
            if (!target.IsVisible && target.IsLoaded) { Drop(); return false; }

            var anim = new DoubleAnimationUsingKeyFrames();
            if (level == MotionLevel.Full)
            {
                var sheen = TimeSpan.FromMilliseconds(SheenMs);
                var hold = TimeSpan.FromMilliseconds(HoldMs);
                anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(sheen / 2), new SineEase()));
                anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.8, KeyTime.FromTimeSpan(sheen), new SineEase()));
                anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.8, KeyTime.FromTimeSpan(sheen + hold)));
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(sheen + hold + TimeSpan.FromMilliseconds(FadeMs))));
            }
            else
            {
                anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.85, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.85, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(HoldMs))));
                anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(HoldMs + 1))));
            }
            anim.Completed += (_, _) => Drop();
            ring.BeginAnimation(UIElement.OpacityProperty, anim);
            App.Logger?.Debug("NavGlow({Why}): on {T} {W}x{H}", why, target.Name, target.ActualWidth, target.ActualHeight);
            return true;
        }

        private sealed class GlowAdorner : Adorner
        {
            private readonly Pen _ring;
            private readonly Pen[] _halo;
            private readonly Brush _fill;

            public GlowAdorner(UIElement adorned, Color accent) : base(adorned)
            {
                _ring = Frozen(new Pen(new SolidColorBrush(accent), 2.5));
                _halo = new Pen[4];
                for (int i = 0; i < _halo.Length; i++)
                    _halo[i] = Frozen(new Pen(new SolidColorBrush(Color.FromArgb((byte)(90 - i * 20), accent.R, accent.G, accent.B)), 3));
                _fill = Frozen(new SolidColorBrush(Color.FromArgb(0x26, accent.R, accent.G, accent.B)));
            }

            protected override void OnRender(DrawingContext dc)
            {
                var size = AdornedElement.RenderSize;
                if (size.Width <= 0 || size.Height <= 0) return;
                var r = new Rect(new Point(-3, -3), new Size(size.Width + 6, size.Height + 6));
                double radius = Math.Min(14, r.Height / 2);
                for (int i = _halo.Length - 1; i >= 0; i--)
                {
                    var g = r; g.Inflate(3 + i * 3, 3 + i * 3);
                    dc.DrawRoundedRectangle(null, _halo[i], g, radius + 3 + i * 3, radius + 3 + i * 3);
                }
                dc.DrawRoundedRectangle(_fill, _ring, r, radius, radius);
            }

            private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
        }
    }
}
