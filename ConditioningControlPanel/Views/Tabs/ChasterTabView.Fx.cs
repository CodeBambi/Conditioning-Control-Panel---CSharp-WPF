using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Circe's tab, the motion. The launcher's playbook, page-sized: an ambient fog and dust
    /// canvas behind the cards, a spiral watermark turning once every 90 s, a sheen crossing the
    /// hero every dozen seconds, the art on a slow drift, the cards arriving in a stagger, and one
    /// event vocabulary - a pop, a burst and a ring - for every press and every booking. On top of
    /// that, the lock's own beats: the paper tag swings, the title's padlocks breathe, the
    /// calendar's marks draw in and tonight's square breathes with a comet running the key into
    /// it, a turned key throws a ring and lights its rows one after another, and
    /// the trailer's picture drifts under the app's own floating figure.
    ///
    /// <para>Rails, the same as the launcher's: every tween is gated on <see cref="MotionFx"/>
    /// (loops on <c>AllowAmbientLoops</c>, particles on <c>AllowParticles</c>, the rest on
    /// <c>AllowTransitions</c>), uses <c>BeginAnimation</c> on transforms and opacity only, and
    /// sits inside a try/catch so decoration never throws into the page. The ambient canvas is
    /// registered with the window so ShowTab parks it on the way out, and it parks itself when
    /// the page hides.</para>
    /// </summary>
    public partial class ChasterTabView
    {
        private const double SpiralTurns = 6.5;
        private const double SpiralInnerRadius = 8;
        private const double SpiralTurnSeconds = 90;
        private const int SpiralFps = 10;
        private const double ArtDriftTo = 1.06;
        private const double ArtDriftSeconds = 38;
        private const int ArtDriftFps = 10;
        private const int PopMs = 380;
        private const int ShockwaveMs = 480;
        private const double ShockwaveFrom = 16;
        private const double ShockwaveTo = 260;
        private const double HeroRadius = 18;
        private const double TagSwingFrom = -7;
        private const double TagSwingTo = -3;
        private const double TagSwingSeconds = 3.6;
        private const double CalendarBobPx = 3;
        private const double CalendarBobSeconds = 1.1;
        private const int CalendarCometEveryMs = 6500;
        private const int CalendarCometMs = 1600;
        private const int CalendarDrawStepMs = 45;
        private const int CalendarStrokeMs = 200;
        private const double RingPulseTo = 1.04;
        private const double RingPulseSeconds = 2.2;
        private const double SheetTiltDegrees = -1.5;
        private const int KeyFlickerStepMs = 28;
        private const double TrailerDriftTo = 1.09;
        private const double TrailerDriftSeconds = 7;
        private const int TrailerDriftFps = 20;
        private const int TrailerFigureEveryMs = 1700;

        private bool _fxStarted;
        private bool _spiralBuilt;
        private CardSheenAdorner? _heroSheen;
        private PerimeterCometAdorner? _linkComet;
        private DispatcherTimer? _calendarComet;
        private DispatcherTimer? _trailerFigure;
        private ChasterBookedFlash? _trailerFlash;
        private TabPrice? _trailerPrice;

        /// <summary>Once, from the constructor: the hover lifts, the press squishes, the key hues.</summary>
        private void FxInit()
        {
            try
            {
                foreach (var card in new FrameworkElement[] { StatTab, StatToday, StatRun, BtnPresetGentle, BtnPresetStrict, BtnPresetCirce, BtnPresetCustom })
                {
                    var self = card;
                    self.MouseEnter += (_, _) => MotionFx.HoverLift(self, true);
                    self.MouseLeave += (_, _) => MotionFx.HoverLift(self, false);
                }
                foreach (var button in new[] { BtnLink, BtnConsentOk })
                {
                    var self = button;
                    self.PreviewMouseLeftButtonDown += (_, _) => MotionFx.PressSquish(self, true);
                    self.PreviewMouseLeftButtonUp += (_, _) => MotionFx.PressSquish(self, false);
                    self.MouseLeave += (_, _) => MotionFx.PressSquish(self, false);
                }
                // Every card that pops, lifts or arrives carries a scale AND a slide from the
                // start, so MotionFx's helpers and the pop here all find their transform instead of
                // replacing each other's (MotionFx swaps a lone identity transform for its own).
                foreach (var moving in new FrameworkElement[]
                         {
                             HeroCard, StatTab, StatToday, StatRun, PricesHeader, SwitchPill, TxtBalance, ConsentCard,
                             BtnPresetGentle, BtnPresetStrict, BtnPresetCirce, BtnPresetCustom, BtnLink, BtnConsentOk,
                             CostBoard, EarnBoard, JackpotRow, FootRow,
                         }.Concat(FactRow.Children.OfType<FrameworkElement>()))
                {
                    moving.RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform() } };
                }
                Tint(BtnPresetGentle, EarnColour);
                Tint(BtnPresetStrict, CostColour);
                Tint(BtnPresetCirce, JackpotColour);
                Tint(BtnPresetCustom, CustomColour);

                IsVisibleChanged += (_, _) => { if (!IsVisible) FxPark(); };
                Unloaded += (_, _) => FxPark();
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster fx init"); }
        }

        private static void Tint(ToggleButton tile, Color hue)
        {
            var brush = new SolidColorBrush(hue);
            brush.Freeze();
            tile.Background = brush;
            tile.BorderBrush = brush;
        }

        /// <summary>Every visit: the loops start (or resume), the cards arrive, the lead digit counts up.</summary>
        private void FxOnShown()
        {
            try
            {
                if (!_spiralBuilt)
                {
                    _spiralBuilt = true;
                    SpiralPath.Data = BuildSpiral(SpiralLayer.Width, SpiralTurns, SpiralInnerRadius);
                }
                if (!_fxStarted)
                {
                    _fxStarted = true;
                    Ambient.StartLayers(new AmbientFxConfig
                    {
                        Layers = AmbientFxLayers.FogDrift | AmbientFxLayers.DustField,
                        Intensity = 0.85,
                        FogPuffs = 3,
                    });
                    App.MainWindowRef?.RegisterTabFx("chaster", Ambient);
                }
                else Ambient.Resume();

                GlowDigits();
                MotionFx.StaggerIn(ArrivingCards());
                StartSpiralTurn();
                StartHeroSheen();
                StartArtDrift();
                StartTagSwing();
                StartCalendarLoops();
                HeroTitle.PlayEntry();
                HeroTitle.Start();
                if (_clockLead is { } lead) MotionFx.Odometer(lead.Block, 0, lead.Value, "{0:0}", 0.9);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster fx on shown"); }
        }

        /// <summary>The digits carry the tile glow: one effect on one small element, only where
        /// the performance tier allows glow at all, capped at the tier's blur.</summary>
        private void GlowDigits()
        {
            try
            {
                if (!PerformanceProfile.AllowGlow(PerformanceProfile.CurrentTier)) { HeroClock.Effect = null; return; }
                if (HeroClock.Effect != null) return;
                HeroClock.Effect = new DropShadowEffect
                {
                    Color = FxTheme.GlowColor, ShadowDepth = 0, Opacity = 0.45,
                    BlurRadius = Math.Min(22, PerformanceProfile.MaxGlowBlurRadius(PerformanceProfile.CurrentTier)),
                    RenderingBias = RenderingBias.Performance,
                };
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private IEnumerable<FrameworkElement> ArrivingCards()
        {
            yield return HeroCard;
            if (FactRow.Visibility == Visibility.Visible)
            {
                foreach (var fact in FactRow.Children.OfType<FrameworkElement>()) yield return fact;
                yield break;
            }
            yield return StatTab;
            yield return StatToday;
            yield return StatRun;
            yield return PricesHeader;
            yield return BtnPresetGentle;
            yield return BtnPresetStrict;
            yield return BtnPresetCirce;
            yield return BtnPresetCustom;
            yield return CostBoard;
            yield return EarnBoard;
            yield return JackpotRow;
            yield return FootRow;
        }

        /// <summary>The page hid: park every loop where it is, keep the composed state.</summary>
        private void FxPark()
        {
            try
            {
                Ambient.Pause();
                HoldSpiral();
                DetachSheen();
                StopArtDrift();
                StopTagSwing();
                StopCalendarLoops();
                HeroTitle.Stop();
                FxTrailerStop();
                PerimeterCometAdorner.Detach(_linkComet);
                _linkComet = null;
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster fx park"); }
        }

        // ------------------------------------------------------------------ the loops

        /// <summary>An Archimedean spiral (r grows linearly with the angle) as one frozen polyline.
        /// The launcher's own recipe.</summary>
        private static Geometry BuildSpiral(double size, double turns, double innerRadius)
        {
            var geometry = new StreamGeometry();
            double c = size / 2;
            double maxTheta = turns * Math.PI * 2;
            double growth = (c - innerRadius - 4) / maxTheta;
            const double step = Math.PI / 45;
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(c + innerRadius, c), false, false);
                for (double theta = step; theta <= maxTheta; theta += step)
                {
                    double r = innerRadius + growth * theta;
                    ctx.LineTo(new Point(c + r * Math.Cos(theta), c + r * Math.Sin(theta)), true, true);
                }
            }
            geometry.Freeze();
            return geometry;
        }

        private void StartSpiralTurn()
        {
            if (!MotionFx.AllowAmbientLoops) { HoldSpiral(); return; }
            double from = SpiralTurn.Angle;
            var spin = new DoubleAnimation(from, from + 360, TimeSpan.FromSeconds(SpiralTurnSeconds))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            Timeline.SetDesiredFrameRate(spin, SpiralFps);
            SpiralTurn.BeginAnimation(RotateTransform.AngleProperty, spin);
        }

        /// <summary>Freezes the spiral where it is; the next start continues from that angle.</summary>
        private void HoldSpiral()
        {
            var angle = SpiralTurn.Angle;
            SpiralTurn.BeginAnimation(RotateTransform.AngleProperty, null);
            SpiralTurn.Angle = angle;
        }

        private void StartHeroSheen()
        {
            DetachSheen();
            if (!MotionFx.AllowAmbientLoops) return;
            var layer = AdornerLayer.GetAdornerLayer(HeroCard);
            if (layer == null) return;
            _heroSheen = new CardSheenAdorner(HeroCard, HeroRadius);
            layer.Add(_heroSheen);
            _heroSheen.Start();
        }

        private void DetachSheen()
        {
            if (_heroSheen == null) return;
            try
            {
                _heroSheen.Stop();
                AdornerLayer.GetAdornerLayer(_heroSheen.AdornedElement)?.Remove(_heroSheen);
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
            _heroSheen = null;
        }

        /// <summary>The neon art breathes in and out over half a minute: a texture scale, ten frames a second.</summary>
        private void StartArtDrift()
        {
            if (!MotionFx.AllowAmbientLoops) { StopArtDrift(); return; }
            var drift = new DoubleAnimation(1.0, ArtDriftTo, TimeSpan.FromSeconds(ArtDriftSeconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(drift, ArtDriftFps);
            HeroArtScale.BeginAnimation(ScaleTransform.ScaleXProperty, drift);
            HeroArtScale.BeginAnimation(ScaleTransform.ScaleYProperty, drift);
        }

        private void StopArtDrift()
        {
            HeroArtScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            HeroArtScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            HeroArtScale.ScaleX = HeroArtScale.ScaleY = 1;
        }

        /// <summary>The paper tag swings a few degrees on its string, slowly, like a tag on a hook does.</summary>
        private void StartTagSwing()
        {
            if (!MotionFx.AllowAmbientLoops) { StopTagSwing(); return; }
            var swing = new DoubleAnimation(TagSwingFrom, TagSwingTo, TimeSpan.FromSeconds(TagSwingSeconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(swing, ArtDriftFps * 2);
            TagSwing.BeginAnimation(RotateTransform.AngleProperty, swing);
        }

        private void StopTagSwing()
        {
            TagSwing.BeginAnimation(RotateTransform.AngleProperty, null);
            TagSwing.Angle = (TagSwingFrom + TagSwingTo) / 2;
        }

        /// <summary>The art plate: rounded on the card's right corners only, and a fade from the
        /// card's own surface on its left edge so the picture emerges instead of starting.</summary>
        private void HeroArtPlate_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                var w = HeroArtPlate.ActualWidth;
                var h = HeroArtPlate.ActualHeight;
                HeroArtPlate.Clip = new RectangleGeometry(new Rect(-HeroRadius, 0, w + HeroRadius, h), HeroRadius, HeroRadius);
                var surface = ((SolidColorBrush)FindResource("SurfaceBgBrush")).Color;
                HeroArtFade.Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
                    GradientStops =
                    {
                        new GradientStop(surface, 0),
                        new GradientStop(Color.FromArgb(0xD0, surface.R, surface.G, surface.B), 0.28),
                        new GradientStop(Color.FromArgb(0x40, surface.R, surface.G, surface.B), 0.62),
                        new GradientStop(Colors.Transparent, 1),
                    },
                };
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        // ------------------------------------------------------------------ the calendar

        /// <summary>A sheet was rebuilt: forget the old squares and their clocks.</summary>
        private void FxCalendarReset()
        {
            StopCalendarLoops();
            _tonightCell = null;
            _tonightMark = null;
            _keyCell = null;
            _calendarDraws.Clear();
        }

        /// <summary>The marker draws the crosses one after another, 45 ms apart: each stroke runs
        /// its length (a dash the length of the stroke, offset to nothing, pulled to zero) and the
        /// splat lands where the second stroke lifts. Reduced motion and Off get the final state.</summary>
        private void FxCalendarDrawIn()
        {
            if (MotionFx.Level != MotionLevel.Full) return;
            try
            {
                var i = 0;
                foreach (var cross in _calendarDraws.OfType<Canvas>())
                {
                    var begin = TimeSpan.FromMilliseconds(CalendarDrawStepMs * i++);
                    var strokes = cross.Children.OfType<Path>().ToList();
                    var s = 0;
                    foreach (var stroke in strokes)
                    {
                        if (stroke.Tag is not double length || length <= 0) continue;
                        var self = stroke;
                        var units = length / self.StrokeThickness;
                        self.StrokeDashArray = new DoubleCollection { units, units };
                        self.StrokeDashOffset = units;
                        var draw = new DoubleAnimation(units, 0, TimeSpan.FromMilliseconds(CalendarStrokeMs))
                        {
                            BeginTime = begin + TimeSpan.FromMilliseconds(CalendarStrokeMs * 0.9 * s++),
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                        };
                        draw.Completed += (_, _) =>
                        {
                            try
                            {
                                self.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
                                self.StrokeDashArray = null;
                                self.StrokeDashOffset = 0;
                            }
                            catch (Exception ex) { Diag.Swallowed(ex); }
                        };
                        self.BeginAnimation(Shape.StrokeDashOffsetProperty, draw);
                    }
                    if (cross.Children.OfType<Ellipse>().FirstOrDefault() is { } splat)
                    {
                        var target = splat.Opacity;
                        splat.Opacity = 0;
                        var land = new DoubleAnimation(0, target, TimeSpan.FromMilliseconds(90))
                        {
                            BeginTime = begin + TimeSpan.FromMilliseconds(CalendarStrokeMs * 0.9 * Math.Max(0, strokes.Count - 1) + CalendarStrokeMs * 0.8),
                        };
                        land.Completed += (_, _) =>
                        {
                            try { splat.BeginAnimation(UIElement.OpacityProperty, null); splat.Opacity = target; }
                            catch (Exception ex) { Diag.Swallowed(ex); }
                        };
                        splat.BeginAnimation(UIElement.OpacityProperty, land);
                    }
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster fx calendar draw"); }
        }

        /// <summary>The tag under tonight's square bobs, the red ring pulses very slightly, and
        /// every few seconds a comet runs from the sticker into tonight and lands with a pulse.</summary>
        private void StartCalendarLoops()
        {
            StopCalendarLoops();
            if (_tonightCell == null || !MotionFx.AllowAmbientLoops) return;
            var bob = new DoubleAnimation(0, CalendarBobPx, TimeSpan.FromSeconds(CalendarBobSeconds))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(bob, 20);
            CalendarTagBob.BeginAnimation(TranslateTransform.YProperty, bob);
            if (_tonightMark?.RenderTransform is ScaleTransform ring)
            {
                var pulse = new DoubleAnimation(1.0, RingPulseTo, TimeSpan.FromSeconds(RingPulseSeconds))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(pulse, 20);
                ring.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
                ring.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
            }
            _calendarComet = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(CalendarCometEveryMs) };
            _calendarComet.Tick += (_, _) => CalendarComet();
            _calendarComet.Start();
        }

        private void StopCalendarLoops()
        {
            _calendarComet?.Stop();
            _calendarComet = null;
            CalendarTagBob.BeginAnimation(TranslateTransform.YProperty, null);
            CalendarTagBob.Y = 0;
            if (_tonightMark?.RenderTransform is ScaleTransform ring)
            {
                ring.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                ring.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                ring.ScaleX = ring.ScaleY = 1;
            }
        }

        /// <summary>The pointer crossed the sheet: it flutters on its pin and settles back.</summary>
        private void FxSheetFlutter() => SheetSwing(-0.5, 300);

        /// <summary>A price landed: the sheet takes a small nudge on its pin.</summary>
        private void FxSheetNudge() => SheetSwing(-2.7, 320);

        private void SheetSwing(double to, int ms)
        {
            if (MotionFx.Level != MotionLevel.Full || CalendarRow.Visibility != Visibility.Visible) return;
            try
            {
                var swing = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ms) };
                swing.KeyFrames.Add(new EasingDoubleKeyFrame(SheetTiltDegrees, KeyTime.FromPercent(0)));
                swing.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromPercent(0.45), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                swing.KeyFrames.Add(new EasingDoubleKeyFrame(SheetTiltDegrees, KeyTime.FromPercent(1), new SineEase { EasingMode = EasingMode.EaseInOut }));
                swing.Completed += (_, _) =>
                {
                    try { SheetTilt.BeginAnimation(RotateTransform.AngleProperty, null); SheetTilt.Angle = SheetTiltDegrees; }
                    catch (Exception ex) { Diag.Swallowed(ex); }
                };
                SheetTilt.BeginAnimation(RotateTransform.AngleProperty, swing);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster fx sheet"); }
        }

        /// <summary>One comet: a bright dot that runs from the sticker to tonight's square and pops the ring.</summary>
        private void CalendarComet()
        {
            try
            {
                if (!IsVisible || !MotionFx.AllowAmbientLoops || _tonightCell == null || _keyCell == null) return;
                var open = _keyCell;
                var from = open.TransformToVisual(ShockwaveCanvas).Transform(new Point(open.ActualWidth / 2, open.ActualHeight / 2));
                var to = _tonightCell.TransformToVisual(ShockwaveCanvas).Transform(new Point(_tonightCell.ActualWidth * 0.3, _tonightCell.ActualHeight * 0.25));
                if (double.IsNaN(from.X) || double.IsNaN(to.X)) return;

                var slide = new TranslateTransform(from.X, from.Y);
                var comet = new Ellipse
                {
                    Width = 8, Height = 8, Fill = Brushes.White, Opacity = 0, IsHitTestVisible = false,
                    RenderTransform = slide, Margin = new Thickness(-4, -4, 0, 0),
                };
                if (PerformanceProfile.AllowGlow(PerformanceProfile.CurrentTier))
                    comet.Effect = new DropShadowEffect { Color = CostColour, ShadowDepth = 0, BlurRadius = 12, Opacity = 0.9, RenderingBias = RenderingBias.Performance };
                ShockwaveCanvas.Children.Add(comet);

                var duration = TimeSpan.FromMilliseconds(CalendarCometMs);
                var run = new DoubleAnimation(from.X, to.X, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
                var drop = new DoubleAnimation(from.Y, to.Y, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
                var fade = new DoubleAnimationUsingKeyFrames { Duration = duration };
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.1)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.92)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                fade.Completed += (_, _) =>
                {
                    try
                    {
                        ShockwaveCanvas.Children.Remove(comet);
                        if (_tonightMark != null) FxPop(_tonightMark, 1.25);
                    }
                    catch (Exception ex) { Diag.Swallowed(ex); }
                };
                slide.BeginAnimation(TranslateTransform.XProperty, run);
                slide.BeginAnimation(TranslateTransform.YProperty, drop);
                comet.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster fx comet"); }
        }

        // ------------------------------------------------------------------ the trailer

        /// <summary>The trailer opened: the picture drifts, and every couple of seconds the app's
        /// own floating figure says the row's price off the plate.</summary>
        private void FxTrailerStart(TabPrice price)
        {
            FxTrailerStop();
            _trailerPrice = price;
            try
            {
                if (MotionFx.AllowAmbientLoops)
                {
                    var drift = new DoubleAnimation(1.0, TrailerDriftTo, TimeSpan.FromSeconds(TrailerDriftSeconds))
                    {
                        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    };
                    var pan = new DoubleAnimation(6, -6, TimeSpan.FromSeconds(TrailerDriftSeconds * 1.3))
                    {
                        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    };
                    Timeline.SetDesiredFrameRate(drift, TrailerDriftFps);
                    Timeline.SetDesiredFrameRate(pan, TrailerDriftFps);
                    TrailerArtScale.BeginAnimation(ScaleTransform.ScaleXProperty, drift);
                    TrailerArtScale.BeginAnimation(ScaleTransform.ScaleYProperty, drift);
                    TrailerArtPan.BeginAnimation(TranslateTransform.XProperty, pan);
                }
                // The first figure lands as soon as the popup has a layer; then on a loop.
                _trailerFigure = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(TrailerFigureEveryMs) };
                _trailerFigure.Tick += (_, _) => TrailerFigure();
                _trailerFigure.Start();
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(TrailerFigure));
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster fx trailer"); }
        }

        /// <summary>One "+0:30" off the plate, the exact figure the padlock throws when this row books.</summary>
        private void TrailerFigure()
        {
            try
            {
                if (!_trailerShown || _trailerPrice is not { } price) return;
                var plan = BookedFlashPlan.For(price.Id, price.Seconds, MotionFx.Level);
                if (plan is not { } fresh) return;
                _trailerFlash?.Dismiss();
                _trailerFlash = ChasterBookedFlash.Show(TrailerFigureAnchor, fresh);
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void FxTrailerStop()
        {
            try
            {
                _trailerFigure?.Stop();
                _trailerFigure = null;
                _trailerFlash?.Dismiss();
                _trailerFlash = null;
                _trailerPrice = null;
                TrailerArtScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                TrailerArtScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                TrailerArtPan.BeginAnimation(TranslateTransform.XProperty, null);
                TrailerArtScale.ScaleX = TrailerArtScale.ScaleY = 1;
                TrailerArtPan.X = 0;
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster fx trailer stop"); }
        }

        // ------------------------------------------------------------------ the events

        /// <summary>A price landed: the balance pops in the booking's colour, sparks off it, a ring
        /// out of its card, and the paper tag jumps on its string.</summary>
        private void FxBooked(int seconds, string eventId)
        {
            try
            {
                var colour = eventId == CircesTab.JackpotEventId ? JackpotColour : seconds > 0 ? CostColour : EarnColour;
                FxPop(TxtBalance, 1.14);
                BurstAt(TxtBalance, colour, 36);
                Shockwave(StatTab, colour);
                FxPop(PaperTag, 1.06);
                FxPop(TxtTagAmount, 1.12);
                if (CalendarTag.Visibility == Visibility.Visible) FxPop(CalendarTag, 1.2);
                if (_tonightMark != null) FxPop(_tonightMark, 1.18);
                FxSheetNudge();
                // the padlocks in the title take a tug, and the last one throws sparks
                HeroTitle.Jolt();
                if (HeroTitle.Visibility == Visibility.Visible && HeroTitle.Padlocks.LastOrDefault() is { } padlock)
                    BurstAt(padlock, CostColour, 18);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster fx booked"); }
        }

        private void FxPreset(ToggleButton tile, Color hue)
        {
            try
            {
                FxPop(tile, 1.05);
                BurstAt(tile, hue, 44);
                Shockwave(tile, hue);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster fx preset"); }
        }

        /// <summary>A key turned: the rows it opens light one after another, top to bottom.</summary>
        private void FxKeyTurned(IEnumerable<ToggleButton> lit)
        {
            if (!MotionFx.AllowTransitions) return;
            try
            {
                var i = 0;
                foreach (var row in lit)
                {
                    var self = row;
                    var delay = TimeSpan.FromMilliseconds(KeyFlickerStepMs * i++);
                    var scale = EnsureScale(self);
                    if (scale == null) continue;
                    var flick = new DoubleAnimationUsingKeyFrames { BeginTime = delay, Duration = TimeSpan.FromMilliseconds(260) };
                    flick.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
                    flick.KeyFrames.Add(new EasingDoubleKeyFrame(1.06, KeyTime.FromPercent(0.4), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                    flick.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, flick);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, flick);
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster fx key"); }
        }

        private void FxRow(ToggleButton row, bool on, Color hue)
        {
            try
            {
                FxPop(row, 1.06);
                if (on) BurstAt(row, hue, 16);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster fx row"); }
        }

        private void FxSwitch(bool on)
        {
            try
            {
                FxPop(SwitchPill, 1.08);
                if (on) { BurstAt(SwitchPill, CostColour, 30); Shockwave(SwitchPill, CostColour); }
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster fx switch"); }
        }

        private void FxConsentShown()
        {
            try { MotionFx.StaggerIn(new FrameworkElement[] { ConsentCard }); }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster fx consent"); }
        }

        private void FxConsentOk()
        {
            try
            {
                BurstAt(BtnConsentOk, JackpotColour, 44);
                Shockwave(BtnConsentOk, JackpotColour);
                FxPop(SwitchPill, 1.1);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster fx consent ok"); }
        }

        /// <summary>While the browser is open: a comet laps the hero's rim, so the wait reads as waiting.</summary>
        private void FxLinking(bool on)
        {
            try
            {
                PerimeterCometAdorner.Detach(_linkComet);
                _linkComet = null;
                if (on && IsVisible && MotionFx.AllowAmbientLoops)
                    _linkComet = PerimeterCometAdorner.Attach(HeroCard, HeroRadius, 6);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster fx linking"); }
        }

        /// <summary>The link landed: the hero throws a shower.</summary>
        private void FxLinked()
        {
            try
            {
                BurstAt(HeroCard, JackpotColour, 70);
                Shockwave(HeroCard, JackpotColour);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster fx linked"); }
        }

        // ------------------------------------------------------------------ the vocabulary

        private static void FxPop(FrameworkElement element, double peak)
        {
            if (!MotionFx.AllowTransitions) return;
            var scale = EnsureScale(element);
            if (scale == null) return;
            var pop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(PopMs) };
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromPercent(0.35),
                new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1),
                new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

        /// <summary>The scale on an element, made if there is none. A group gets one inserted
        /// first so the stagger's translate keeps working; an element carrying some other
        /// transform is left alone.</summary>
        private static ScaleTransform? EnsureScale(FrameworkElement element)
        {
            switch (element.RenderTransform)
            {
                case ScaleTransform s:
                    return s;
                case TransformGroup g:
                    var found = g.Children.OfType<ScaleTransform>().FirstOrDefault();
                    if (found != null) return found;
                    if (g.IsFrozen) return null;
                    var added = new ScaleTransform(1, 1);
                    g.Children.Insert(0, added);
                    return added;
                case null:
                case MatrixTransform { Matrix.IsIdentity: true }:
                    var fresh = new ScaleTransform(1, 1);
                    element.RenderTransform = fresh;
                    return fresh;
                case TranslateTransform t:
                    // The stagger's slide: keep it, add the scale beside it.
                    var pair = new ScaleTransform(1, 1);
                    element.RenderTransform = new TransformGroup { Children = { pair, t } };
                    return pair;
                default:
                    return null;
            }
        }

        private void BurstAt(FrameworkElement? anchor, Color colour, int count)
        {
            if (anchor == null || !MotionFx.AllowParticles) return;
            if (!anchor.IsVisible || anchor.ActualWidth <= 0 || anchor.ActualHeight <= 0) return;
            var bounds = anchor.TransformToVisual(BurstHost)
                               .TransformBounds(new Rect(0, 0, anchor.ActualWidth, anchor.ActualHeight));
            var centre = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            // Park the small canvas on the anchor and burst from its middle.
            Canvas.SetLeft(BurstLayer, centre.X - BurstLayer.Width / 2);
            Canvas.SetTop(BurstLayer, centre.Y - BurstLayer.Height / 2);
            BurstLayer.Burst(BurstLayer.Width / 2, BurstLayer.Height / 2, colour, count);
        }

        /// <summary>Two rings out of the anchor's centre: a bright one and a thin echo 90 ms behind.</summary>
        private void Shockwave(FrameworkElement anchor, Color colour)
        {
            if (!MotionFx.AllowTransitions || !anchor.IsVisible || anchor.ActualWidth <= 0) return;
            var bounds = anchor.TransformToVisual(ShockwaveCanvas)
                               .TransformBounds(new Rect(0, 0, anchor.ActualWidth, anchor.ActualHeight));
            var centre = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            Ring(centre, colour, 0, 2.2, 0.85);
            Ring(centre, colour, 90, 1.5, 0.45);
        }

        private void Ring(Point centre, Color colour, int delayMs, double thickness, double alpha)
        {
            double start = ShockwaveFrom / ShockwaveTo;
            var scale = new ScaleTransform(start, start);
            var ring = new Ellipse
            {
                Width = ShockwaveTo, Height = ShockwaveTo,
                Stroke = new SolidColorBrush(colour), StrokeThickness = thickness,
                Opacity = 0, IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale,
            };
            Canvas.SetLeft(ring, centre.X - ShockwaveTo / 2);
            Canvas.SetTop(ring, centre.Y - ShockwaveTo / 2);
            ShockwaveCanvas.Children.Add(ring);

            var duration = TimeSpan.FromMilliseconds(ShockwaveMs);
            var begin = TimeSpan.FromMilliseconds(delayMs);
            var grow = new DoubleAnimation(start, 1.0, duration)
            {
                BeginTime = begin,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin, Duration = duration };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(alpha, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(alpha * 0.8, KeyTime.FromPercent(0.35)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            fade.Completed += (_, _) =>
            {
                try { ShockwaveCanvas.Children.Remove(ring); } catch (Exception ex) { Diag.Swallowed(ex); }
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            ring.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}
