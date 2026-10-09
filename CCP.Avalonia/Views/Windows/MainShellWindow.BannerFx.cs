// PORTED from WPF 7.1.5 MainWindow/MainWindow.BannerFx.cs (nav polish wave 6 + polish wave 13 drum)
// and the banner half of MainWindow.ChromeFx.cs (SweepBannerSheen). Rules and numbers live in Core
// ConditioningControlPanel.Fx.BannerFxRules; this file only drives the visuals the XAML names.
//
//   * the halo breathes (ambient: Full motion + window active), flares on every beat change;
//   * the incoming beat pops, the pill's ring flashes;
//   * the drum ROLLS one face per beat at Full motion only (Reduced crossfades, Off swaps);
//   * the support line gets a sparkle run on arrival and every 12 s while it is on screen;
//   * one sheen pass over the pill when the message changes, never twice inside 20 s.
//
// WPF animated DropShadowEffect.Opacity / BlurRadius and timeline keyframes with per-key easing;
// Avalonia Animations take one easing each, so the curves are sampled into linear keyframes
// (SampledAnimation) from the same functions. Visually the same curve, frame for frame.

using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using Serilog;
using BannerRules = ConditioningControlPanel.Fx.BannerFxRules;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private DispatcherTimer? _bannerSparkleTimer;
        private CancellationTokenSource? _bannerFlash, _bannerSheenRun;
        private int _bannerSparkleSeed = Environment.TickCount;
        private DateTime _lastBannerSheenUtc = DateTime.MinValue;
        // Lazy: a static Geometry needs the render interface, and the shell type is touched before it exists in tests.
        private static Geometry? _bannerStar;
        private static Geometry BannerStar => _bannerStar ??= Geometry.Parse(BannerRules.SparkleStarPath);

        /// <summary>The halo layer behind the banner pill: a BoxShadow in PinkColor whose Opacity
        /// breathes on the shared beat and whose blur flares on a beat change (never an Effect).</summary>
        private Border? BannerGlow
        {
            get
            {
                if (Named<Border>("HeaderBannerGlowLayer") is not { } layer) return null;
                if (layer.BoxShadow.Count == 0)
                    layer.BoxShadow = Features.CardGlow.Shadow(this.FindResource("PinkColor"), BannerRules.GlowBlur);
                return layer;
            }
        }

        private Features.BreathClock? _bannerBreathClock;
        private global::ConditioningControlPanel.Avalonia.Controls.Fx.FrameClock? _bannerFlareClock;

        /// <summary>The halo breath rides the shared beat (tests).</summary>
        internal bool BannerBreathRunning => _bannerBreathClock?.IsRunning == true;

        /// <summary>Starts or parks the banner's two ambient loops (the halo's breath and the
        /// sparkle repeat). Called on load, on activation changes and on a motion-level change.</summary>
        private void ApplyBannerFxLoops()
        {
            try
            {
                bool ambient = HudAmbientAllowed;
                var host = Named<Border>("HeaderBannerGlowHost");
                if (host != null)
                {
                    _bannerBreathClock?.Stop();
                    if (BannerRules.Breathe(ambient) && BannerGlow is { } glow)
                    {
                        // Same curve as the Animation it replaces (Alternate + SineEaseInOut over
                        // BreathSeconds), stepped on the shared 30 fps beat with the Home loops.
                        _bannerBreathClock ??= new Features.BreathClock(glow, BannerRules.BreathSeconds);
                        _bannerBreathClock.Start((glow, BannerRules.BreathMin, BannerRules.BreathMax));
                    }
                    else if (BannerGlow is { } rest) rest.Opacity = BannerRules.GlowRest;
                }

                if (ambient)
                {
                    if (_bannerSparkleTimer == null)
                    {
                        _bannerSparkleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(BannerRules.SparkleRepeatSeconds) };
                        _bannerSparkleTimer.Tick += BannerSparkleTimer_Tick;
                        Closed += (_, _) => { _bannerSparkleTimer?.Stop(); _bannerBreathClock?.Stop(); _bannerFlareClock?.Stop(); };
                    }
                    if (!_bannerSparkleTimer.IsEnabled) _bannerSparkleTimer.Start();
                }
                else
                {
                    _bannerSparkleTimer?.Stop();
                    ClearBannerSparkles();
                }
            }
            catch (Exception ex) { Log.Debug("ApplyBannerFxLoops: {E}", ex.Message); }
        }

        private bool SupportBeatOnScreen =>
            _bannerCurrentIndex >= 0 && _bannerCurrentIndex < _bannerBeats.Length
            && ReferenceEquals(_bannerBeats[_bannerCurrentIndex], BannerPrimary);

        private void BannerSparkleTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (!BannerRules.RepeatSparkles(HudAmbientAllowed, SupportBeatOnScreen)) return;
                RunBannerSparkles();
            }
            catch (Exception ex) { Log.Debug("BannerSparkleTimer_Tick: {E}", ex.Message); }
        }

        /// <summary>The beat change: pop, flash, and (support beat) the sparkle run and a sheen.</summary>
        private void OnBannerBeatChanged(TextBlock incoming, bool rolled = false)
        {
            try
            {
                bool support = ReferenceEquals(incoming, BannerPrimary);
                var fx = BannerRules.OnBeatChange(AmbientFxCanvas.Env.Level, Pr4aAmbientAllowed, support);
                if (fx.Pop) PopBannerBeat(incoming, popY: !rolled);
                if (fx.Flash) FlashBannerRing();
                if (fx.Sparkles)
                {
                    RunBannerSparkles();
                    SweepBannerSheen(force: true);
                    if (_bannerSparkleTimer?.IsEnabled == true) { _bannerSparkleTimer.Stop(); _bannerSparkleTimer.Start(); }
                }
            }
            catch (Exception ex) { Log.Debug("OnBannerBeatChanged: {E}", ex.Message); }
        }

        private static (ScaleTransform Scale, TranslateTransform Slide) BeatTransforms(TextBlock beat)
        {
            if (beat.RenderTransform is TransformGroup g && g.Children.Count == 2
                && g.Children[0] is ScaleTransform s0 && g.Children[1] is TranslateTransform t0)
                return (s0, t0);
            var s = new ScaleTransform(1, 1);
            var t = new TranslateTransform();
            beat.RenderTransform = new TransformGroup { Children = { s, t } };
            beat.RenderTransformOrigin = RelativePoint.Center;
            return (s, t);
        }

        private static void PopBannerBeat(TextBlock beat, bool popY = true)
        {
            var (scale, _) = BeatTransforms(beat);
            Func<double, double> pop = u => BannerRules.PopFrom + (1 - BannerRules.PopFrom) * BackOut(u, 0.6);
            SampledTween(scale, ScaleTransform.ScaleXProperty, BannerRules.PopMs, pop);
            if (popY) SampledTween(scale, ScaleTransform.ScaleYProperty, BannerRules.PopMs, pop);
        }

        /// <summary>Polish wave 13: at Full motion the drum rolls one face - the old beat rolls up
        /// and squashes away, the new one rolls in from below and settles. Returns false (and
        /// leaves both faces flat) at any other level, so the plain crossfade carries the change.</summary>
        private static bool RollBannerDrum(TextBlock outgoing, TextBlock incoming)
        {
            var (outScale, outSlide) = BeatTransforms(outgoing);
            var (inScale, inSlide) = BeatTransforms(incoming);
            if (!BannerRules.Roll(AmbientFxCanvas.Env.Level))
            {
                outScale.ScaleY = inScale.ScaleY = 1;
                outSlide.Y = inSlide.Y = 0;
                return false;
            }
            var ms = BannerRules.RollMs;
            Func<double, double> awayIn = u => u * u;                      // QuadraticEase In
            Func<double, double> settle = u => BackOut(u, BannerRules.RollSettle);
            SampledTween(outSlide, TranslateTransform.YProperty, ms, u => -BannerRules.RollTravelPx * awayIn(u), rest: 0);
            SampledTween(outScale, ScaleTransform.ScaleYProperty, ms, u => 1 + (BannerRules.RollSquash - 1) * awayIn(u), rest: 1);
            SampledTween(inSlide, TranslateTransform.YProperty, ms, u => BannerRules.RollTravelPx * (1 - settle(u)), rest: 0);
            SampledTween(inScale, ScaleTransform.ScaleYProperty, ms, u => BannerRules.RollSquash + (1 - BannerRules.RollSquash) * settle(u), rest: 1);
            return true;
        }

        /// <summary>The ring flash (0 -> 0.95 in 70 ms -> 0 by 400 ms) and the halo's blur flare.</summary>
        private void FlashBannerRing()
        {
            if (Named<Border>("BannerFlashRing") is not { } ring) return;
            _bannerFlash?.Cancel();
            _bannerFlash = new CancellationTokenSource();
            double peakAt = (double)BannerRules.FlashPeakMs / BannerRules.FlashMs;
            _ = SampledAnimation(OpacityProperty, BannerRules.FlashMs, u =>
                u <= peakAt ? BannerRules.FlashPeak * u / peakAt
                            : BannerRules.FlashPeak * (1 - QuadOut((u - peakAt) / (1 - peakAt))))
                .RunAsync(ring, _bannerFlash.Token);
            if (BannerGlow is { } glow)
            {
                double flareAt = (double)BannerRules.GlowFlarePeakMs / BannerRules.FlashMs;
                double Blur(double u) =>
                    u <= flareAt ? BannerRules.GlowBlur + (BannerRules.GlowFlareBlur - BannerRules.GlowBlur) * u / flareAt
                                 : BannerRules.GlowFlareBlur - (BannerRules.GlowFlareBlur - BannerRules.GlowBlur) * QuadOut((u - flareAt) / (1 - flareAt));
                // The blur flare steps on the frame clock (a BoxShadow is not animatable), IN and OUT
                // within FlashMs, and lands exactly on GlowBlur.
                _bannerFlareClock?.Stop();
                var started = DateTime.UtcNow;
                var clock = _bannerFlareClock = new global::ConditioningControlPanel.Avalonia.Controls.Fx.FrameClock(glow) { Interval = TimeSpan.FromSeconds(1.0 / 30) };
                clock.Tick += (_, _) =>
                {
                    double u = Math.Min(1, (DateTime.UtcNow - started).TotalMilliseconds / BannerRules.FlashMs);
                    Features.CardGlow.SetBlur(glow, Blur(u));
                    if (u >= 1) clock.Stop();
                };
                clock.Start();
            }
        }

        /// <summary>Flies a handful of star sprites along the support line.</summary>
        private void RunBannerSparkles()
        {
            if (Named<Canvas>("BannerSparkleLayer") is not { } layer || BannerPrimary is not { } text) return;
            if (!AmbientFxCanvas.Env.AllowTransitions) return;
            double layerH = layer.Bounds.Height;
            double textW = text.Bounds.Width;
            if (layerH <= 0 || textW <= 0 || !layer.IsEffectivelyVisible) return;
            var origin = text.TranslatePoint(new Point(0, 0), layer);
            if (origin == null) return;
            ClearBannerSparkles();
            var glow = this.TryFindResource("PinkColor", out var found) && found is Color c ? c : Color.FromRgb(0xFF, 0x69, 0xB4);
            var runMs = (int)(BannerRules.SparkleRunSeconds * 1000);
            foreach (var f in BannerRules.PlanRun(origin.Value.X, textW, layerH, BannerRules.SparkleCount, _bannerSparkleSeed++))
            {
                var sprite = SparkleSprite(f.Size, glow);
                var scale = new ScaleTransform(0, 0);
                var rotate = new RotateTransform(0);
                var slide = new TranslateTransform(f.FromX - f.Size / 2, 0);
                sprite.RenderTransform = new TransformGroup { Children = { scale, rotate, slide } };
                sprite.RenderTransformOrigin = RelativePoint.Center;
                sprite.Opacity = 0;
                Canvas.SetLeft(sprite, 0);
                Canvas.SetTop(sprite, f.Y - f.Size / 2);
                layer.Children.Add(sprite);
                var delay = TimeSpan.FromSeconds(f.DelaySeconds);
                Animation Delayed(Animation a) { a.Delay = delay; return a; }
                double from = f.FromX - f.Size / 2, to = f.ToX - f.Size / 2;
                SampledTween(slide, TranslateTransform.XProperty, runMs, u => from + (to - from) * (0.5 - 0.5 * Math.Cos(Math.PI * u)), delayMs: delay.TotalMilliseconds);
                SampledTween(scale, ScaleTransform.ScaleXProperty, runMs, BannerRules.TwinkleAt, delayMs: delay.TotalMilliseconds);
                SampledTween(scale, ScaleTransform.ScaleYProperty, runMs, BannerRules.TwinkleAt, delayMs: delay.TotalMilliseconds);
                SampledTween(rotate, RotateTransform.AngleProperty, runMs, u => f.Spin * u, delayMs: delay.TotalMilliseconds);
                Delayed(SampledAnimation(OpacityProperty, runMs, BannerRules.AlphaAt)).RunAsync(sprite)
                    .ContinueWith(_ => Dispatcher.UIThread.Post(() => layer.Children.Remove(sprite)));
            }
        }

        private static Control SparkleSprite(double size, Color glow)
        {
            var root = new Grid { Width = size, Height = size, IsHitTestVisible = false };
            root.Children.Add(new Ellipse
            {
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(200, glow.R, glow.G, glow.B), 0),
                        new GradientStop(Color.FromArgb(90, glow.R, glow.G, glow.B), 0.35),
                        new GradientStop(Color.FromArgb(0, glow.R, glow.G, glow.B), 1),
                    },
                },
            });
            root.Children.Add(new Path
            {
                Data = BannerStar,
                Fill = Brushes.White,
                Stretch = Stretch.Fill,
                Width = size * 0.72,
                Height = size * 0.72,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            return root;
        }

        private void ClearBannerSparkles()
        {
            try { Named<Canvas>("BannerSparkleLayer")?.Children.Clear(); }
            catch { /* teardown */ }
        }

        /// <summary>One sheen pass over the pill when its message changes; throttled, because the
        /// rotation is on a 4 s timer and a pass every 4 s would read as an ambient strobe.</summary>
        internal void SweepBannerSheen(bool force = false)
        {
            try
            {
                if (!Pr4aAmbientAllowed || !AmbientFxCanvas.Env.AllowTransitions) return;
                var now = DateTime.UtcNow;
                if (!BannerRules.SheenDue(now, _lastBannerSheenUtc, force)) return;
                if (Named<Grid>("BannerSheenHost") is not { } host || Named<Border>("BannerSheen") is not { } band) return;
                if (band.RenderTransform is not TransformGroup { Children.Count: 2 } g || g.Children[1] is not TranslateTransform slide) return;
                double width = host.Bounds.Width;
                if (width <= 0) return;
                _lastBannerSheenUtc = now;
                double bandWidth = double.IsNaN(band.Width) || band.Width <= 0 ? 80 : band.Width;
                var ms = (int)(BannerRules.SheenSeconds * 1000);
                _bannerSheenRun?.Cancel();
                _bannerSheenRun = new CancellationTokenSource();
                SampledTween(slide, TranslateTransform.XProperty, ms,
                    u => -bandWidth + (width + bandWidth * 1.25) * (0.5 - 0.5 * Math.Cos(Math.PI * u)),
                    token: _bannerSheenRun.Token);
                _ = SampledAnimation(OpacityProperty, ms, u =>
                        u < 0.20 ? BannerRules.SheenPeak * u / 0.20
                        : u <= 0.75 ? BannerRules.SheenPeak
                        : BannerRules.SheenPeak * (1 - (u - 0.75) / 0.25))
                    .RunAsync(band, _bannerSheenRun.Token);
            }
            catch (Exception ex) { Log.Debug("SweepBannerSheen: {E}", ex.Message); }
        }

        // ============================== curves ==============================

        /// <summary>A one-shot animation whose value at fraction u is f(u), sampled into linear
        /// keyframes (Avalonia takes one easing per animation; WPF eased each key).</summary>
        internal static Animation SampledAnimation(AvaloniaProperty property, int ms, Func<double, double> f, int steps = 16)
        {
            var anim = new Animation { Duration = TimeSpan.FromMilliseconds(ms), FillMode = FillMode.None };
            for (int i = 0; i <= steps; i++)
            {
                var u = (double)i / steps;
                anim.Children.Add(new KeyFrame { Cue = new Cue(u), Setters = { new Setter(property, f(u)) } });
            }
            return anim;
        }

        /// <summary>The same sampled curve written straight onto a Transform. Animation.RunAsync on a
        /// Transform throws (TransformAnimator casts its target to Visual), so every transform target
        /// in this file goes through Helpers/TransformTween. <paramref name="rest"/> is where the
        /// property lands when the run ends (the old FillMode.None); null holds the last sample.</summary>
        internal static DispatcherTimer? SampledTween(AvaloniaObject target, AvaloniaProperty property, int ms,
            Func<double, double> f, double? rest = null, double delayMs = 0, CancellationToken token = default, int steps = 16)
        {
            try
            {
                double total = Math.Max(1, ms + Math.Max(0, delayMs));
                double lead = Math.Max(0, delayMs) / total;
                var keys = new List<(double, AvaloniaProperty, double)>(steps + 3);
                if (lead > 0) keys.Add((0, property, f(0)));
                for (int i = 0; i <= steps; i++)
                {
                    var u = (double)i / steps;
                    keys.Add((Math.Min(rest.HasValue ? 0.9999 : 1, lead + (1 - lead) * u), property, f(u)));
                }
                if (rest.HasValue) keys.Add((1, property, rest.Value));
                return Helpers.TransformTween.Run(target, TimeSpan.FromMilliseconds(total), keys, null, false, token);
            }
            catch (Exception ex) { Log.Debug("SampledTween: {E}", ex.Message); return null; }
        }

        private static double QuadOut(double u) => 1 - (1 - u) * (1 - u);

        /// <summary>WPF BackEase EaseOut with the given amplitude.</summary>
        internal static double BackOut(double u, double amplitude)
        {
            var t = 1 - u;
            return 1 - (t * t * t - t * amplitude * Math.Sin(t * Math.PI));
        }
    }
}
