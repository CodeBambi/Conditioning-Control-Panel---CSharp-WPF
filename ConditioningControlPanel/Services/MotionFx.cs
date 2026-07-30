using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The app's shared motion helpers and the single reduced-motion gate everything else asks.
    ///
    /// Two clocks, per the FX plan: interaction motion is 80-400ms and always allowed unless the
    /// user picked Off; ambient motion is 8-60s loops and only runs at MotionLevel.Full on a tier
    /// that allows ambient motion. Every helper here no-ops (snapping to the end state) under
    /// Reduced/Off, and every helper animates via <c>element.BeginAnimation</c> — never
    /// Storyboard.SetTargetName, which silently no-ops across the tab-UserControl namescopes.
    /// The XAML side of the library lives in Resources/Theme/Motion.xaml.
    /// </summary>
    public static class MotionFx
    {
        public const double HoverLiftScale = 1.02;
        public const double PressSquishScale = 0.97;
        private const int HoverMs = 150;
        private const int PressMs = 80;
        private const int StaggerMs = 40;
        private const int StaggerCap = 6;
        private const int AmbientFrameRate = 24;

        /// <summary>
        /// The pure part of the reduced-motion decision: the user's setting, capped to Reduced when
        /// Windows' animation-effects flag is off (so the OS preference can only ever remove motion,
        /// never add it back).
        /// </summary>
        public static MotionLevel ResolveLevel(MotionLevel setting, bool osAnimationsEnabled)
        {
            if (setting == MotionLevel.Off) return MotionLevel.Off;
            if (!osAnimationsEnabled) return MotionLevel.Reduced;
            return setting;
        }

        /// <summary>The effective motion level right now (user setting capped by the OS flag).</summary>
        public static MotionLevel Level
        {
            get
            {
                var setting = App.Settings?.Current?.MotionLevel ?? MotionLevel.Full;
                bool osAnim = true;
                try { osAnim = SystemParameters.ClientAreaAnimation; } catch { }
                return ResolveLevel(setting, osAnim);
            }
        }

        /// <summary>Interaction motion (hover, press, transitions, entrances) — off only at Off.</summary>
        public static bool AllowTransitions => Level != MotionLevel.Off;

        /// <summary>
        /// Ambient loops (fog, shimmer, animated gradients, marquee, AmbientFxCanvas). Requires
        /// MotionLevel.Full AND a tier that permits ambient motion. Check this BEFORE starting a
        /// clock; the fallback is static art, not a slower loop.
        /// </summary>
        public static bool AllowAmbientLoops =>
            Level == MotionLevel.Full
            && PerformanceProfile.AllowAmbientMotion(PerformanceProfile.CurrentTier);

        /// <summary>Particles (dust fields, bursts) — ambient loops plus a non-zero tier budget.</summary>
        public static bool AllowParticles =>
            AllowAmbientLoops && PerformanceProfile.MaxAmbientParticles(PerformanceProfile.CurrentTier) > 0;

        // ---- interaction ----

        /// <summary>Hover lift: scale to 1.02 over 150ms (and back). No-op at Off.</summary>
        public static void HoverLift(FrameworkElement? element, bool on)
        {
            if (element == null) return;
            var scale = EnsureScale(element);
            if (scale == null) return;
            if (!AllowTransitions)
            {
                scale.ScaleX = scale.ScaleY = on ? HoverLiftScale : 1.0;
                return;
            }
            Scale(scale, on ? HoverLiftScale : 1.0, HoverMs);
        }

        /// <summary>Press squish: scale to 0.97 over 80ms (and back). No-op at Off.</summary>
        public static void PressSquish(FrameworkElement? element, bool down)
        {
            if (element == null) return;
            var scale = EnsureScale(element);
            if (scale == null) return;
            if (!AllowTransitions)
            {
                scale.ScaleX = scale.ScaleY = down ? PressSquishScale : 1.0;
                return;
            }
            Scale(scale, down ? PressSquishScale : 1.0, PressMs);
        }

        /// <summary>
        /// Entrance stagger: fade in from a 10px rise, 40ms apart, capped at 6 staggered items —
        /// anything past the cap enters on the last slot's clock so a long list never crawls in.
        /// </summary>
        public static void StaggerIn(IEnumerable<FrameworkElement>? items)
        {
            if (items == null) return;
            bool animate = AllowTransitions;
            int i = 0;
            foreach (var item in items)
            {
                if (item == null) continue;
                var slide = EnsureTranslate(item);
                if (!animate)
                {
                    item.Opacity = 1;
                    if (slide != null) slide.Y = 0;
                    i++;
                    continue;
                }

                var delay = TimeSpan.FromMilliseconds(StaggerMs * Math.Min(i, StaggerCap));
                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
                {
                    BeginTime = delay,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                item.BeginAnimation(UIElement.OpacityProperty, fade);

                if (slide != null)
                {
                    var rise = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260))
                    {
                        BeginTime = delay,
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    };
                    slide.BeginAnimation(TranslateTransform.YProperty, rise);
                }
                i++;
            }
        }

        // ---- numbers ----

        /// <summary>
        /// Odometer tween on a TextBlock: counts from → to and formats each step. Runs on an
        /// attached DP so the animation targets the element directly (no namescope lookup).
        /// Snaps to the final value under Off.
        /// </summary>
        public static void Odometer(TextBlock? target, double from, double to,
                                    string format = "{0:N0}", double seconds = 0.7)
        {
            if (target == null) return;
            SetOdometerFormat(target, format);
            if (!AllowTransitions || Math.Abs(to - from) < 0.5 || seconds <= 0)
            {
                target.BeginAnimation(OdometerValueProperty, null);
                SetOdometerValue(target, to);
                return;
            }
            var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            target.BeginAnimation(OdometerValueProperty, anim);
        }

        public static readonly DependencyProperty OdometerValueProperty =
            DependencyProperty.RegisterAttached("OdometerValue", typeof(double), typeof(MotionFx),
                new PropertyMetadata(0.0, OnOdometerValueChanged));

        public static void SetOdometerValue(DependencyObject d, double value) => d.SetValue(OdometerValueProperty, value);
        public static double GetOdometerValue(DependencyObject d) => (double)d.GetValue(OdometerValueProperty);

        public static readonly DependencyProperty OdometerFormatProperty =
            DependencyProperty.RegisterAttached("OdometerFormat", typeof(string), typeof(MotionFx),
                new PropertyMetadata("{0:N0}"));

        public static void SetOdometerFormat(DependencyObject d, string value) => d.SetValue(OdometerFormatProperty, value);
        public static string GetOdometerFormat(DependencyObject d) => (string)d.GetValue(OdometerFormatProperty);

        private static void OnOdometerValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb) return;
            try
            {
                var format = GetOdometerFormat(d) ?? "{0:N0}";
                tb.Text = string.Format(CultureInfo.CurrentCulture, format, (double)e.NewValue);
            }
            catch { }
        }

        /// <summary>
        /// Bar fill: tween a bar's Width and, when it lands on (or past) its cap, bloom the
        /// supplied glow element once. Snaps + skips the bloom under Off.
        /// </summary>
        public static void BarFill(FrameworkElement? bar, double fromWidth, double toWidth,
                                   FrameworkElement? capBloom = null, double seconds = 0.6)
        {
            if (bar == null) return;
            if (!AllowTransitions || seconds <= 0)
            {
                bar.BeginAnimation(FrameworkElement.WidthProperty, null);
                bar.Width = Math.Max(0, toWidth);
                return;
            }
            var fill = new DoubleAnimation(Math.Max(0, fromWidth), Math.Max(0, toWidth),
                                           TimeSpan.FromSeconds(seconds))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            bar.BeginAnimation(FrameworkElement.WidthProperty, fill);

            if (capBloom == null) return;
            var bloom = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds + 0.45) };
            bloom.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            bloom.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds * 0.8))));
            bloom.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds))));
            bloom.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds + 0.45))));
            capBloom.BeginAnimation(UIElement.OpacityProperty, bloom);
        }

        // ---- ambient ----

        /// <summary>
        /// Slow opacity breath on an already-drawn glow element. Ambient — only starts at
        /// MotionLevel.Full on a tier that allows ambient motion, and parks at full opacity
        /// otherwise. Runs at a capped frame rate.
        /// </summary>
        public static void GlowBreath(UIElement? element, double min = 0.6, double max = 1.0,
                                      double seconds = 3.4)
        {
            if (element == null) return;
            if (!AllowAmbientLoops)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = max;
                return;
            }
            var anim = new DoubleAnimation(min, max, TimeSpan.FromSeconds(seconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(anim, AmbientFrameRate);
            element.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        /// <summary>Stops the helpers' clocks on an element and leaves it at its resting state.</summary>
        public static void Stop(UIElement? element)
        {
            if (element == null) return;
            try
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                if (element is FrameworkElement fe)
                {
                    fe.BeginAnimation(FrameworkElement.WidthProperty, null);
                    if (FindScale(fe) is { } s)
                    {
                        s.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                        s.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                        s.ScaleX = s.ScaleY = 1.0;
                    }
                    if (FindTranslate(fe) is { } t)
                    {
                        t.BeginAnimation(TranslateTransform.YProperty, null);
                        t.Y = 0;
                    }
                }
            }
            catch { }
        }

        // ---- transform plumbing ----

        private static void Scale(ScaleTransform scale, double to, int ms)
        {
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        /// <summary>
        /// Finds the element's ScaleTransform, installing one only when the element has no
        /// transform of its own — never clobbers an authored transform (that is how layout
        /// silently breaks), it just declines to animate.
        /// </summary>
        private static ScaleTransform? EnsureScale(FrameworkElement element)
        {
            if (FindScale(element) is { } existing) return existing;
            var current = element.RenderTransform;
            if (current != null && current != Transform.Identity && !current.Value.IsIdentity) return null;
            var scale = new ScaleTransform(1, 1);
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = scale;
            return scale;
        }

        private static TranslateTransform? EnsureTranslate(FrameworkElement element)
        {
            if (FindTranslate(element) is { } existing) return existing;
            var current = element.RenderTransform;
            if (current != null && current != Transform.Identity && !current.Value.IsIdentity) return null;
            var slide = new TranslateTransform();
            element.RenderTransform = slide;
            return slide;
        }

        private static ScaleTransform? FindScale(FrameworkElement element) => Find<ScaleTransform>(element.RenderTransform);

        private static TranslateTransform? FindTranslate(FrameworkElement element) => Find<TranslateTransform>(element.RenderTransform);

        private static T? Find<T>(Transform? transform) where T : Transform
        {
            if (transform is T match) return match;
            if (transform is TransformGroup group)
            {
                foreach (var child in group.Children)
                    if (child is T hit) return hit;
            }
            return null;
        }
    }
}
