using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>What one beat change in the header banner is allowed to do.</summary>
    internal readonly record struct BannerBeatFx(bool Pop, bool Flash, bool Sparkles);

    /// <summary>One star of a sparkle run, in the sparkle layer's coordinates (its centre).</summary>
    internal readonly record struct SparkleFlight(double FromX, double ToX, double Y, double DelaySeconds,
                                                  double Size, double Spin);

    /// <summary>
    /// Nav polish wave 6 (owner, 2026-10-06): "make the changing banner 20% bigger and the text
    /// more visible, add some FX on it and some animation, do something fancy like sparkles that
    /// run on the text every so often on the consider supporting the project beat".
    ///
    /// <para>The pure half: sizes, timings and the motion gate, so the tests can pin them without
    /// a MainWindow. Full = pop + flash on every beat change, a sparkle run when the support beat
    /// arrives and again every <see cref="SparkleRepeatSeconds"/> while it stays, and the glow
    /// breath. Reduced = the pop, the flash and the sparkle run once per beat change, no breath,
    /// no repeats. Off = nothing moves, the text just swaps.</para>
    /// </summary>
    internal static class BannerFxRules
    {
        public const double HostHeight = 31;
        public const double HostMaxWidth = 744;
        public const double BeatFontSize = 14;

        public const double PopFrom = 0.96;
        public const int PopMs = 260;
        public const int FlashMs = 400;

        public const double BreathMin = 0.25;
        public const double BreathMax = 0.6;
        public const double BreathSeconds = 3.2;
        /// <summary>The glow's resting opacity when the breath is not allowed to run.</summary>
        public const double GlowRest = 0.35;

        public const int SparkleCount = 7;
        public const double SparkleRunSeconds = 1.25;
        public const double SparkleStaggerSeconds = 0.09;
        public const double SparkleRepeatSeconds = 12;

        /// <summary>A beat change. Nothing at Off or while the window is not in front.</summary>
        public static BannerBeatFx OnBeatChange(MotionLevel level, bool windowActive, bool isSupportBeat)
        {
            if (level == MotionLevel.Off || !windowActive) return default;
            return new BannerBeatFx(Pop: true, Flash: true, Sparkles: isSupportBeat);
        }

        /// <summary>The 12 s re-run while the support beat stays up: an ambient loop, Full only.</summary>
        public static bool RepeatSparkles(bool ambientAllowed, bool supportOnScreen) => ambientAllowed && supportOnScreen;

        /// <summary>The glow breath on the pill border: an ambient loop, Full only.</summary>
        public static bool Breathe(bool ambientAllowed) => ambientAllowed;

        /// <summary>
        /// Plans one run over a line of text that starts at <paramref name="textLeft"/> and is
        /// <paramref name="textWidth"/> wide, on a layer <paramref name="layerHeight"/> tall. Every
        /// star crosses the text left to right; they leave in a stagger so the run reads as a
        /// trail rather than one blob, and each takes its own lane a few pixels above or below
        /// the text's middle. Seeded, so a test can pin it.
        /// </summary>
        public static IReadOnlyList<SparkleFlight> PlanRun(double textLeft, double textWidth, double layerHeight,
                                                           int count, int seed)
        {
            var list = new List<SparkleFlight>();
            if (textWidth <= 4 || count <= 0) return list;
            var rng = new Random(seed);
            double mid = layerHeight / 2;
            for (int i = 0; i < count; i++)
            {
                double size = 11 + rng.NextDouble() * 7;              // 11-18 px sprites
                double lane = (rng.NextDouble() * 2 - 1) * Math.Min(7, layerHeight * 0.3);
                double from = textLeft - 6 + rng.NextDouble() * textWidth * 0.12;
                double to = textLeft + textWidth * (0.82 + rng.NextDouble() * 0.2);
                double spin = (rng.NextDouble() < 0.5 ? -1 : 1) * (90 + rng.NextDouble() * 120);
                list.Add(new SparkleFlight(from, to, mid + lane, i * SparkleStaggerSeconds, size, spin));
            }
            return list;
        }

        /// <summary>
        /// One star sprite: a soft radial glow in the theme pink under a white four-point star.
        /// Built in code (not a template) so the render test can draw the same sprite.
        /// </summary>
        public static FrameworkElement CreateSparkleSprite(double size, Color glow)
        {
            var root = new Grid
            {
                Width = size,
                Height = size,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            var halo = new RadialGradientBrush();
            halo.GradientStops.Add(new GradientStop(Color.FromArgb(200, glow.R, glow.G, glow.B), 0));
            halo.GradientStops.Add(new GradientStop(Color.FromArgb(90, glow.R, glow.G, glow.B), 0.35));
            halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, glow.R, glow.G, glow.B), 1));
            halo.Freeze();
            root.Children.Add(new Ellipse { Fill = halo });
            root.Children.Add(new Path
            {
                Data = StarGeometry,
                Fill = Brushes.White,
                Stretch = Stretch.Fill,
                Width = size * 0.72,
                Height = size * 0.72,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            return root;
        }

        private static readonly Geometry StarGeometry = MakeStar();

        private static Geometry MakeStar()
        {
            var g = Geometry.Parse("M 0.5,0 C 0.55,0.4 0.6,0.45 1,0.5 C 0.6,0.55 0.55,0.6 0.5,1 "
                                   + "C 0.45,0.6 0.4,0.55 0,0.5 C 0.4,0.45 0.45,0.4 0.5,0 Z");
            g.Freeze();
            return g;
        }
    }

    public partial class MainWindow
    {
        private DispatcherTimer? _bannerSparkleTimer;
        private bool _bannerBreathing;
        private int _bannerSparkleSeed = Environment.TickCount;

        /// <summary>
        /// The banner's ambient half, called from ApplyChromeFxLoops so focus, minimise, the
        /// motion picker and a mod switch all re-evaluate it in one place: the glow breath and
        /// the 12 s sparkle repeat run only while ChromeAmbientAllowed, and park otherwise.
        /// </summary>
        private void ApplyBannerFxLoops()
        {
            try
            {
                if (BannerGlow == null) return;
                bool ambient = ChromeAmbientAllowed;

                if (BannerFxRules.Breathe(ambient))
                {
                    if (!_bannerBreathing)
                    {
                        var breath = new DoubleAnimation(BannerFxRules.BreathMin, BannerFxRules.BreathMax,
                                                         TimeSpan.FromSeconds(BannerFxRules.BreathSeconds))
                        {
                            AutoReverse = true,
                            RepeatBehavior = RepeatBehavior.Forever,
                            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                        };
                        Timeline.SetDesiredFrameRate(breath, AmbientFrameRate);
                        BannerGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, breath);
                        _bannerBreathing = true;
                    }
                }
                else if (_bannerBreathing || BannerGlow.Opacity != BannerFxRules.GlowRest)
                {
                    BannerGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, null);
                    BannerGlow.Opacity = BannerFxRules.GlowRest;
                    _bannerBreathing = false;
                }

                if (ambient)
                {
                    if (_bannerSparkleTimer == null)
                    {
                        _bannerSparkleTimer = new DispatcherTimer
                        {
                            Interval = TimeSpan.FromSeconds(BannerFxRules.SparkleRepeatSeconds),
                        };
                        _bannerSparkleTimer.Tick += BannerSparkleTimer_Tick;
                    }
                    if (!_bannerSparkleTimer.IsEnabled) _bannerSparkleTimer.Start();
                }
                else
                {
                    _bannerSparkleTimer?.Stop();
                    ClearBannerSparkles();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyBannerFxLoops: {E}", ex.Message); }
        }

        private bool SupportBeatOnScreen =>
            _bannerCurrentIndex >= 0 && _bannerCurrentIndex < _bannerBeats.Length
            && ReferenceEquals(_bannerBeats[_bannerCurrentIndex], TxtBannerPrimary);

        private void BannerSparkleTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (!BannerFxRules.RepeatSparkles(ChromeAmbientAllowed, SupportBeatOnScreen)) return;
                RunBannerSparkles();
            }
            catch (Exception ex) { App.Logger?.Debug("BannerSparkleTimer_Tick: {E}", ex.Message); }
        }

        /// <summary>
        /// The rotation's hook: the incoming beat pops in, the pill flashes, and when the beat is
        /// the support line a sparkle run crosses it (plus the sheen, which earns its pass here
        /// rather than waiting out its 20 s throttle).
        /// </summary>
        private void OnBannerBeatChanged(TextBlock incoming)
        {
            try
            {
                bool support = ReferenceEquals(incoming, TxtBannerPrimary);
                var fx = BannerFxRules.OnBeatChange(MotionFx.Level, _chromeFxInitialized && _chromeFxWindowActive, support);
                if (fx.Pop) PopBannerBeat(incoming);
                if (fx.Flash) FlashBannerRing();
                if (fx.Sparkles)
                {
                    RunBannerSparkles();
                    SweepBannerSheen(force: true);
                    // A fresh arrival restarts the repeat clock so a run never lands on top of this one.
                    if (_bannerSparkleTimer?.IsEnabled == true)
                    {
                        _bannerSparkleTimer.Stop();
                        _bannerSparkleTimer.Start();
                    }
                }
            }
            catch (Exception ex) { App.Logger?.Debug("OnBannerBeatChanged: {E}", ex.Message); }
        }

        private static void PopBannerBeat(TextBlock beat)
        {
            if (beat.RenderTransform is not ScaleTransform scale || scale.IsFrozen)
            {
                scale = new ScaleTransform(1, 1);
                beat.RenderTransform = scale;
                beat.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            var pop = new DoubleAnimation(BannerFxRules.PopFrom, 1, TimeSpan.FromMilliseconds(BannerFxRules.PopMs))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 },
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

        private void FlashBannerRing()
        {
            if (BannerFlashRing == null) return;
            var total = TimeSpan.FromMilliseconds(BannerFxRules.FlashMs);
            var ring = new DoubleAnimationUsingKeyFrames { Duration = total };
            ring.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            ring.KeyFrames.Add(new EasingDoubleKeyFrame(0.95, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
            ring.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(total),
                new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            BannerFlashRing.BeginAnimation(OpacityProperty, ring);

            if (BannerGlow != null)
            {
                // The halo widens with the ring. BlurRadius, not Opacity, so the breath keeps its clock.
                var flare = new DoubleAnimationUsingKeyFrames { Duration = total };
                flare.KeyFrames.Add(new LinearDoubleKeyFrame(12, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                flare.KeyFrames.Add(new EasingDoubleKeyFrame(22, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
                flare.KeyFrames.Add(new EasingDoubleKeyFrame(12, KeyTime.FromTimeSpan(total),
                    new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                BannerGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, flare);
            }
        }

        /// <summary>One sparkle run left to right over the support line. One-shot: removes its sprites.</summary>
        private void RunBannerSparkles()
        {
            if (BannerSparkleLayer == null || TxtBannerPrimary == null) return;
            if (!MotionFx.AllowTransitions) return;
            double layerH = BannerSparkleLayer.ActualHeight;
            double textW = TxtBannerPrimary.ActualWidth;
            if (layerH <= 0 || textW <= 0 || !BannerSparkleLayer.IsVisible) return;

            double textLeft = TxtBannerPrimary.TranslatePoint(new Point(0, 0), BannerSparkleLayer).X;
            ClearBannerSparkles();

            var glow = TryFindResource("PinkColor") is Color c ? c : Color.FromRgb(0xFF, 0x69, 0xB4);
            var run = TimeSpan.FromSeconds(BannerFxRules.SparkleRunSeconds);
            var flights = BannerFxRules.PlanRun(textLeft, textW, layerH, BannerFxRules.SparkleCount, _bannerSparkleSeed++);
            foreach (var f in flights)
            {
                var sprite = BannerFxRules.CreateSparkleSprite(f.Size, glow);
                sprite.Opacity = 0;
                Canvas.SetLeft(sprite, 0);
                Canvas.SetTop(sprite, f.Y - f.Size / 2);
                var scale = new ScaleTransform(0, 0);
                var rotate = new RotateTransform(0);
                var slide = new TranslateTransform(f.FromX - f.Size / 2, 0);
                var group = new TransformGroup();
                group.Children.Add(scale);
                group.Children.Add(rotate);
                group.Children.Add(slide);
                sprite.RenderTransform = group;
                BannerSparkleLayer.Children.Add(sprite);

                var begin = TimeSpan.FromSeconds(f.DelaySeconds);

                var travel = new DoubleAnimation(f.FromX - f.Size / 2, f.ToX - f.Size / 2, run)
                {
                    BeginTime = begin,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                // Twinkle: in, dip, flare, out.
                var twinkle = new DoubleAnimationUsingKeyFrames { Duration = run, BeginTime = begin };
                twinkle.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                twinkle.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.15)));
                twinkle.KeyFrames.Add(new EasingDoubleKeyFrame(0.5, KeyTime.FromPercent(0.4)));
                twinkle.KeyFrames.Add(new EasingDoubleKeyFrame(1.1, KeyTime.FromPercent(0.65)));
                twinkle.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                var alpha = new DoubleAnimationUsingKeyFrames { Duration = run, BeginTime = begin };
                alpha.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                alpha.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.12)));
                alpha.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.8)));
                alpha.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                var spin = new DoubleAnimation(0, f.Spin, run) { BeginTime = begin };

                foreach (var tl in new Timeline[] { travel, twinkle, alpha, spin })
                    Timeline.SetDesiredFrameRate(tl, 48);

                // Each star removes only itself: a newer run may already share the layer.
                alpha.Completed += (_, __) => BannerSparkleLayer.Children.Remove(sprite);

                slide.BeginAnimation(TranslateTransform.XProperty, travel);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, twinkle);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, twinkle);
                rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
                sprite.BeginAnimation(OpacityProperty, alpha);
            }
        }

        private void ClearBannerSparkles()
        {
            try { BannerSparkleLayer?.Children.Clear(); }
            catch { }
        }
    }
}
