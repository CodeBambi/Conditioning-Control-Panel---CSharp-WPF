using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Velvet Kit 2, FX lane B: the app's two <b>hero moments</b> - the START button and the XP bar -
    /// plus the Save button's little "absorbed" acknowledgement.
    ///
    /// <para>This is deliberately a separate partial from <c>MainWindow.ChromeFx.cs</c>. Chrome FX
    /// owns the shell's *ambient* dressing (glow breath, sheen passes, tab choreography); everything
    /// here is a <i>moment</i> - a charged idle that says "press me", an ignition that answers the
    /// press, a bar that reads as liquid, a chip that pops when the number changes. The two files
    /// share one funnel: <see cref="ApplyHeroFxLoops"/> is called from ApplyChromeFxLoops, so window
    /// focus, the reduced-motion setting and the performance tier stop these loops through exactly
    /// the same gate as everything else in the chrome.</para>
    ///
    /// <para>House rules kept (identical to ChromeFx, they are not restated per method):</para>
    /// <list type="bullet">
    ///   <item>Ambient clocks (the 9s gradient drift, the 4.2s ring exhale, the 2s meniscus pulse,
    ///   the running-state heartbeat) ask <see cref="ChromeAmbientAllowed"/> BEFORE they start and
    ///   park at a static resting state otherwise. Interaction one-shots (the press ignition, the
    ///   level-up pop, the save tick) ask <see cref="MotionFx.AllowTransitions"/> and are simply
    ///   skipped at MotionLevel.Off.</item>
    ///   <item>RenderTransform and Opacity only. Nothing here animates Width, Height or Margin of a
    ///   panel, and no container gains a DropShadowEffect - the rings ARE the glow.</item>
    ///   <item>Every animation is started with <c>BeginAnimation</c> on the object itself (the
    ///   transform, the gradient stop, the element) with SnapshotAndReplace, never
    ///   Storyboard.SetTargetName - that silently no-ops across namescopes.</item>
    ///   <item>Colour comes from <see cref="FxTheme"/>, so a mod that is not pink does not get a
    ///   pink START button. The default mod's glow IS #FF69B4, which is what makes the shipped
    ///   look the specified FF69B4 / E85CE0 / FF7E6B triple.</item>
    ///   <item>Every entry point is fire-and-forget safe: wrapped, null-tolerant, and it changes
    ///   nothing about the feature it decorates.</item>
    /// </list>
    /// </summary>
    public partial class MainWindow
    {
        // ---- tuning: START ---------------------------------------------------------

        /// <summary>One drift leg. AutoReverse makes the full there-and-back cycle 9s.</summary>
        private const double StartChargeDriftSeconds = 4.5;

        /// <summary>Ring exhale: ~1.35s of travel inside a 4.2s cycle. The flat tail IS the rest -
        /// one clock rather than a timer plus a one-shot (same shape as CardSheenAdorner).</summary>
        private const double StartExhaleCycleSeconds = 4.2;
        private const double StartExhaleTravelSeconds = 1.35;
        private const double StartExhalePeakOpacity = 0.70;

        /// <summary>
        /// Rings grow by a fixed number of PIXELS, not by a fixed scale factor. The CTA is ~50px
        /// tall and several hundred wide, so a uniform 1.10 would push the ring 15px past each side
        /// and swallow the start-options caret while barely clearing the top edge. Growing by px and
        /// deriving ScaleX from the measured width keeps the halo an even band all the way round -
        /// and on a 50px-tall button 5px IS the specified 1.10.
        /// </summary>
        private const double StartExhaleGrowPx = 5.0;
        private const double StartBurstAGrowPx = 8.0;
        private const double StartBurstBGrowPx = 20.0;
        private const int StartBurstAMs = 550;
        private const int StartBurstBMs = 750;
        private const int StartBurstStaggerMs = 80;
        private const double StartBurstPeakOpacity = 0.95;

        private const int StartDipMs = 100;
        private const double StartDipScale = 0.97;
        private const int StartIgnitionFlashMs = 300;
        private const double StartIgnitionFlashPeak = 0.55;

        /// <summary>Running-state heartbeat: 0.6px of swell, i.e. 1.2% on the button's height.</summary>
        private const double StartHeartbeatGrowPx = 0.6;
        private const double StartHeartbeatSeconds = 2.4;

        /// <summary>Fallbacks for the very first apply, before the bar has been measured.</summary>
        private const double StartCtaFallbackWidth = 320;
        private const double StartCtaHeight = 50;

        // ---- tuning: XP bar --------------------------------------------------------

        /// <summary>
        /// Deliberately the same duration AND the same easing as <c>MotionFx.BarFill</c>'s default:
        /// the dot has to ride the fill's edge, and two different curves over the same distance means
        /// a dot that floats ahead of the liquid for a third of a second and reads as broken.
        /// </summary>
        private const int XpMeniscusSlideMs = 600;
        private const double XpMeniscusPulseSeconds = 2.0;
        private const double XpMeniscusMinOpacity = 0.35;
        private const double XpMeniscusMaxOpacity = 0.95;
        private const double XpMeniscusRestOpacity = 0.55;
        /// <summary>Below this fill width the dot would sit on the track's rounded cap rather than
        /// on a surface, so it is simply not shown.</summary>
        private const double XpMeniscusMinFillPx = 5.0;

        private const int LevelChipPopMs = 600;
        private const double LevelChipPopScale = 1.35;
        private const double LevelChipPopDegrees = -4.0;

        // ---- tuning: Save ----------------------------------------------------------

        private const int SaveTickDrawMs = 400;
        private const int SaveRippleMs = 520;
        private const double SaveRippleGrowPx = 14.0;
        private const double SaveAbsorbHoldMs = 1600;
        private const double SaveAbsorbFadeMs = 260;
        private const double SaveButtonHeight = 50;

        // ---- state -----------------------------------------------------------------

        private LinearGradientBrush? _startChargeBrush;
        private GradientStop[]? _startChargeStops;

        /// <summary>
        /// The LOCAL value <c>BtnStart.Background</c> held before the charge took it over, exactly
        /// as ReadLocalValue reported it. Anything that is not a plain Brush (UnsetValue, or the
        /// ResourceReferenceExpression a DynamicResource leaves behind) is released with ClearValue
        /// instead: writing a fresh local Brush where there was none would silently disable the
        /// style's hover trigger from then on. Near-dead code in practice - every caller repaints
        /// the button itself before asking for the release - so falling back to the style's own
        /// pink for one frame is the right trade against guessing at a resource key.
        /// </summary>
        private object? _startChargeRestore;
        private bool _startChargeApplied;
        private bool _startDipInFlight;

        private double _xpMeniscusFillWidth;

        // ============================== lifecycle ==============================

        /// <summary>Called once from InitializeChromeFx, after the window is loaded.</summary>
        private void InitializeHeroFx()
        {
            try
            {
                ApplyHeroFxLoops();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "InitializeHeroFx failed"); }
        }

        /// <summary>
        /// The single re-evaluation point for every hero loop. Rides ApplyChromeFxLoops, so it is
        /// re-run on activate/deactivate/minimise, on a motion-setting change and on a mod switch.
        /// </summary>
        private void ApplyHeroFxLoops()
        {
            ApplyStartHeroState();
            ApplyXpMeniscusPulse();
        }

        // ============================== 1. START: charged idle ==============================

        /// <summary>
        /// Puts the CTA into the state its session status calls for, and is safe to call as often
        /// as you like:
        /// <list type="bullet">
        ///   <item><b>Idle</b> - the charged gradient (drifting when ambient motion is allowed,
        ///   static otherwise) plus the ring exhale.</item>
        ///   <item><b>Running / remote-controlled / disabled</b> - the charge is handed back, the
        ///   exhale is parked, and (running only) the button breathes on the heartbeat instead. The
        ///   caller has already painted the red STOP or green REMOTE background by then; this method
        ///   never fights it, it only ever releases a background it put there itself.</item>
        /// </list>
        /// Called from the tail of UpdateStartButton / UpdateStartButtonForRemoteControl and from
        /// OnSessionStopped (which ClearValue's the background out from under us).
        /// </summary>
        internal void ApplyStartHeroState()
        {
            try
            {
                if (BtnStart == null) return;

                bool remote = false;
                try { remote = App.RemoteControl?.ControllerConnected == true; } catch { }
                bool idle = !_isRunning && !remote && BtnStart.IsEnabled;

                if (!idle)
                {
                    ReleaseStartCharge();
                    StopStartRingExhale();
                    ApplyStartHeartbeat(_isRunning && !remote);
                    return;
                }

                ApplyStartHeartbeat(false);
                ApplyStartCharge();
                ApplyStartRingExhale();
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyStartHeroState: {E}", ex.Message); }
        }

        /// <summary>
        /// Paints the idle CTA with a three-stop pink-family gradient and, when ambient motion is
        /// allowed, drifts its stops so the colour slowly breathes across the button. The brush is
        /// built once and re-tinted in place, so a mod switch costs three Color writes and no
        /// allocation; the drift is three 24fps clocks on gradient stops, which is the cheapest
        /// animated fill WPF has (no intermediate surface, no re-raster of an Effect).
        /// </summary>
        private void ApplyStartCharge()
        {
            var brush = EnsureStartChargeBrush();
            if (brush == null || _startChargeStops == null) return;

            TintStartCharge();

            if (!_startChargeApplied)
            {
                _startChargeRestore = BtnStart.ReadLocalValue(System.Windows.Controls.Control.BackgroundProperty);
                _startChargeApplied = true;
            }
            BtnStart.Background = brush;

            var stops = _startChargeStops;
            if (!ChromeAmbientAllowed)
            {
                foreach (var stop in stops) stop.BeginAnimation(GradientStop.OffsetProperty, null);
                stops[0].Offset = 0.0;
                stops[1].Offset = 0.5;
                stops[2].Offset = 1.0;
                return;
            }

            Drift(stops[0], -0.10, 0.10);
            Drift(stops[1], 0.34, 0.66);
            Drift(stops[2], 0.90, 1.10);

            void Drift(GradientStop stop, double from, double to)
            {
                var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(StartChargeDriftSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(anim, AmbientFrameRate);
                stop.BeginAnimation(GradientStop.OffsetProperty, anim, HandoffBehavior.SnapshotAndReplace);
            }
        }

        /// <summary>Hands the background back to whoever owned it before the charge took over.</summary>
        private void ReleaseStartCharge()
        {
            try
            {
                if (_startChargeStops != null)
                    foreach (var stop in _startChargeStops)
                        stop.BeginAnimation(GradientStop.OffsetProperty, null);

                if (!_startChargeApplied) return;
                _startChargeApplied = false;

                // Only ever undo OUR write. If something else has already repainted the button
                // (the red STOP, the green REMOTE), that value stands.
                if (!ReferenceEquals(BtnStart.Background, _startChargeBrush)) return;

                if (_startChargeRestore is Brush previous && !ReferenceEquals(previous, _startChargeBrush))
                    BtnStart.Background = previous;
                else
                    BtnStart.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            }
            catch (Exception ex) { App.Logger?.Debug("ReleaseStartCharge: {E}", ex.Message); }
        }

        private LinearGradientBrush? EnsureStartChargeBrush()
        {
            if (_startChargeBrush != null) return _startChargeBrush;
            try
            {
                var stops = new[]
                {
                    new GradientStop(Colors.Transparent, 0.0),
                    new GradientStop(Colors.Transparent, 0.5),
                    new GradientStop(Colors.Transparent, 1.0),
                };
                // Slightly off-horizontal: a dead-level gradient on a wide slab reads as a progress
                // bar, a leaning one reads as light on a surface.
                _startChargeBrush = new LinearGradientBrush(new GradientStopCollection(stops),
                                                            new Point(0, 0), new Point(1, 0.35));
                _startChargeStops = stops;
                return _startChargeBrush;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("EnsureStartChargeBrush: {E}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Re-tints the charge from the active mod's glow colour. The two companion stops are hue
        /// rotations of it (-27 deg toward violet, +38 deg toward coral) rather than literals, which
        /// is what makes the default mod's #FF69B4 land on exactly the FF69B4 -> E85CE0 -> FF7E6B
        /// triple the spec asks for while a blue mod gets a coherent blue-family charge instead of
        /// somebody else's pink.
        /// </summary>
        private void TintStartCharge()
        {
            if (_startChargeStops == null) return;
            try
            {
                var (h, s, v) = ToHsv(FxTheme.GlowColor);
                // Never let a desaturated mod colour flatten the charge into three greys.
                s = Math.Max(s, 0.45);
                _startChargeStops[0].Color = FromHsv(h, s, v);
                _startChargeStops[1].Color = FromHsv(h - 27, Math.Min(1, s + 0.02), v * 0.91);
                _startChargeStops[2].Color = FromHsv(h + 38, s, v);
            }
            catch (Exception ex) { App.Logger?.Debug("TintStartCharge: {E}", ex.Message); }
        }

        // ============================== 2. START: ring exhale ==============================

        /// <summary>
        /// The idle "breath": a 2px ring that swells out of the button's edge and fades, every 4.2s.
        /// One keyframe cycle per animated property - the long flat tail is the pause, so there is
        /// no timer to leak and nothing to schedule.
        /// </summary>
        private void ApplyStartRingExhale()
        {
            try
            {
                if (StartRingExhale == null || StartRingExhaleScale == null) return;
                if (!ChromeAmbientAllowed) { StopStartRingExhale(); return; }

                double width = StartCtaWidth;
                double travel = StartExhaleTravelSeconds;
                double cycle = StartExhaleCycleSeconds;

                var alpha = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromSeconds(cycle),
                    RepeatBehavior = RepeatBehavior.Forever,
                };
                alpha.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                alpha.KeyFrames.Add(new LinearDoubleKeyFrame(StartExhalePeakOpacity,
                    KeyTime.FromTimeSpan(TimeSpan.FromSeconds(travel * 0.35))));
                alpha.KeyFrames.Add(new LinearDoubleKeyFrame(0,
                    KeyTime.FromTimeSpan(TimeSpan.FromSeconds(travel))));
                alpha.KeyFrames.Add(new LinearDoubleKeyFrame(0,
                    KeyTime.FromTimeSpan(TimeSpan.FromSeconds(cycle))));
                Timeline.SetDesiredFrameRate(alpha, AmbientFrameRate);
                StartRingExhale.BeginAnimation(OpacityProperty, alpha, HandoffBehavior.SnapshotAndReplace);

                StartRingExhaleScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    ExhaleScale(GrowFactor(width, StartExhaleGrowPx), travel, cycle),
                    HandoffBehavior.SnapshotAndReplace);
                StartRingExhaleScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    ExhaleScale(GrowFactor(StartCtaHeight, StartExhaleGrowPx), travel, cycle),
                    HandoffBehavior.SnapshotAndReplace);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyStartRingExhale: {E}", ex.Message); }
        }

        private static DoubleAnimationUsingKeyFrames ExhaleScale(double to, double travel, double cycle)
        {
            var anim = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(cycle),
                RepeatBehavior = RepeatBehavior.Forever,
            };
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(travel)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.0,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(travel + 0.05))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(cycle))));
            Timeline.SetDesiredFrameRate(anim, AmbientFrameRate);
            return anim;
        }

        private void StopStartRingExhale() => ParkRing(StartRingExhale, StartRingExhaleScale);

        private static void ParkRing(FrameworkElement? ring, ScaleTransform? scale)
        {
            try
            {
                if (ring != null)
                {
                    ring.BeginAnimation(OpacityProperty, null);
                    ring.Opacity = 0;
                }
                if (scale != null)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    scale.ScaleX = scale.ScaleY = 1.0;
                }
            }
            catch { }
        }

        // ============================== 3. START: ignition ==============================

        /// <summary>
        /// The press. Fired from the top of BtnStart_Click as a pure decoration - it reads no state,
        /// takes no decision and cannot change one; if every line of it were deleted the click would
        /// behave identically.
        ///
        /// <para>Three beats, all one-shot: a 0.97 dip so the slab feels physical, a white wash that
        /// says "it caught", and two rings thrown off the edge 80ms apart. The dip's completion
        /// re-applies the hero state, which is what hands the button back to either the charged idle
        /// or the running heartbeat without either of them fighting the dip mid-flight.</para>
        /// </summary>
        internal void FlashStartIgnition()
        {
            try
            {
                if (!MotionFx.AllowTransitions) return;

                double width = StartCtaWidth;

                if (StartPressScale != null)
                {
                    var dip = new DoubleAnimationUsingKeyFrames
                    {
                        Duration = TimeSpan.FromMilliseconds(StartDipMs),
                    };
                    dip.KeyFrames.Add(new EasingDoubleKeyFrame(StartDipScale,
                        KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(StartDipMs * 0.4)))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    });
                    dip.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,
                        KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(StartDipMs)))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    });

                    _startDipInFlight = true;
                    dip.Completed += (_, __) =>
                    {
                        try
                        {
                            _startDipInFlight = false;
                            ApplyStartHeroState();
                        }
                        catch (Exception ex) { App.Logger?.Debug("Start dip completion: {E}", ex.Message); }
                    };
                    StartPressScale.BeginAnimation(ScaleTransform.ScaleXProperty, dip, HandoffBehavior.SnapshotAndReplace);
                    StartPressScale.BeginAnimation(ScaleTransform.ScaleYProperty, dip, HandoffBehavior.SnapshotAndReplace);
                }

                if (StartIgnitionFlash != null)
                {
                    var wash = new DoubleAnimation(StartIgnitionFlashPeak, 0,
                                                   TimeSpan.FromMilliseconds(StartIgnitionFlashMs))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    };
                    StartIgnitionFlash.BeginAnimation(OpacityProperty, wash, HandoffBehavior.SnapshotAndReplace);
                }

                FireStartRing(StartRingBurstA, StartRingBurstAScale, width, StartBurstAGrowPx, StartBurstAMs, 0);
                FireStartRing(StartRingBurstB, StartRingBurstBScale, width, StartBurstBGrowPx, StartBurstBMs,
                              StartBurstStaggerMs);
            }
            catch (Exception ex) { App.Logger?.Debug("FlashStartIgnition: {E}", ex.Message); }
        }

        private static void FireStartRing(FrameworkElement? ring, ScaleTransform? scale, double width,
                                          double growPx, int ms, int delayMs)
        {
            if (ring == null || scale == null) return;
            ScaleTransform rig = scale;
            var delay = TimeSpan.FromMilliseconds(delayMs);
            var span = TimeSpan.FromMilliseconds(ms);

            var alpha = new DoubleAnimation(StartBurstPeakOpacity, 0, span)
            {
                BeginTime = delay,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            ring.BeginAnimation(OpacityProperty, alpha, HandoffBehavior.SnapshotAndReplace);

            Grow(ScaleTransform.ScaleXProperty, GrowFactor(width, growPx));
            Grow(ScaleTransform.ScaleYProperty, GrowFactor(StartCtaHeight, growPx));

            void Grow(DependencyProperty property, double to)
            {
                var anim = new DoubleAnimation(1.0, to, span)
                {
                    BeginTime = delay,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                rig.BeginAnimation(property, anim, HandoffBehavior.SnapshotAndReplace);
            }
        }

        /// <summary>
        /// Barely-there swell while a session runs - 0.6px, on a 2.4s clock. The STOP button should
        /// feel alive without ever advertising itself; anything bigger reads as a second CTA.
        /// Skipped while a press dip is in flight, because the dip's completion restarts it.
        /// </summary>
        private void ApplyStartHeartbeat(bool wanted)
        {
            try
            {
                if (StartPressScale == null) return;
                ScaleTransform rig = StartPressScale;
                if (!wanted || !ChromeAmbientAllowed || _startDipInFlight)
                {
                    // A dip owns this transform for its 100ms and re-applies the state on the way
                    // out - never yank it out from under one.
                    if (_startDipInFlight) return;
                    rig.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    rig.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    rig.ScaleX = rig.ScaleY = 1.0;
                    return;
                }

                Pulse(ScaleTransform.ScaleXProperty, GrowFactor(StartCtaWidth, StartHeartbeatGrowPx));
                Pulse(ScaleTransform.ScaleYProperty, GrowFactor(StartCtaHeight, StartHeartbeatGrowPx));

                void Pulse(DependencyProperty property, double to)
                {
                    var anim = new DoubleAnimation(1.0, to, TimeSpan.FromSeconds(StartHeartbeatSeconds))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    };
                    Timeline.SetDesiredFrameRate(anim, AmbientFrameRate);
                    rig.BeginAnimation(property, anim, HandoffBehavior.SnapshotAndReplace);
                }
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyStartHeartbeat: {E}", ex.Message); }
        }

        /// <summary>Measured CTA width, with a sane fallback before the bar's first layout pass.</summary>
        private double StartCtaWidth
        {
            get
            {
                try
                {
                    double w = StartRingHost?.ActualWidth ?? 0;
                    if (w <= 1) w = BtnStart?.ActualWidth ?? 0;
                    return w > 1 ? w : StartCtaFallbackWidth;
                }
                catch { return StartCtaFallbackWidth; }
            }
        }

        /// <summary>Scale factor that grows an edge of <paramref name="size"/>px by <paramref name="growPx"/>px total.</summary>
        private static double GrowFactor(double size, double growPx)
            => size <= 1 ? 1.0 : (size + growPx) / size;

        // ============================== 4. XP bar: meniscus ==============================

        /// <summary>
        /// Moves the glow dot to the fill's new tip. Called from AnimateXpDisplay with the same
        /// target width the fill itself is tweening to, on the same clock and the same curve, so the
        /// dot rides the surface the whole way instead of teleporting to where the bar will end up.
        /// </summary>
        private void AnimateXpMeniscus(double toWidth)
        {
            try
            {
                _xpMeniscusFillWidth = toWidth;
                if (XPMeniscus == null || XPMeniscusSlide == null) return;

                double dot = double.IsNaN(XPMeniscus.Width) ? 12 : XPMeniscus.Width;
                double target = Math.Max(0, toWidth - dot / 2);
                if (!MotionFx.AllowTransitions)
                {
                    XPMeniscusSlide.BeginAnimation(TranslateTransform.XProperty, null);
                    XPMeniscusSlide.X = target;
                }
                else
                {
                    var slide = new DoubleAnimation(target, TimeSpan.FromMilliseconds(XpMeniscusSlideMs))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    };
                    XPMeniscusSlide.BeginAnimation(TranslateTransform.XProperty, slide,
                                                   HandoffBehavior.SnapshotAndReplace);
                }

                ApplyXpMeniscusPulse();
            }
            catch (Exception ex) { App.Logger?.Debug("AnimateXpMeniscus: {E}", ex.Message); }
        }

        /// <summary>
        /// The dot's own soft pulse. Ambient, so it parks at a static opacity (not at zero - a
        /// still dot on the surface is the reduced-motion version of a breathing one) and hides
        /// outright when there is no fill to sit on.
        /// </summary>
        private void ApplyXpMeniscusPulse()
        {
            try
            {
                if (XPMeniscus == null) return;

                if (_xpMeniscusFillWidth < XpMeniscusMinFillPx)
                {
                    XPMeniscus.BeginAnimation(OpacityProperty, null);
                    XPMeniscus.Opacity = 0;
                    return;
                }

                if (!ChromeAmbientAllowed)
                {
                    XPMeniscus.BeginAnimation(OpacityProperty, null);
                    XPMeniscus.Opacity = XpMeniscusRestOpacity;
                    return;
                }

                var pulse = new DoubleAnimation(XpMeniscusMinOpacity, XpMeniscusMaxOpacity,
                                                TimeSpan.FromSeconds(XpMeniscusPulseSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(pulse, AmbientFrameRate);
                XPMeniscus.BeginAnimation(OpacityProperty, pulse, HandoffBehavior.SnapshotAndReplace);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyXpMeniscusPulse: {E}", ex.Message); }
        }

        // ============================== 5. XP bar: level-up chip ==============================

        /// <summary>
        /// The LVL chip's pop. Added to the EXISTING level-up celebration (CelebrateLevelUp already
        /// blooms the bar and fires the burst) rather than shipped as a second, competing moment:
        /// the number that changed is the one thing the burst does not point at.
        /// </summary>
        internal void PopLevelChip()
        {
            try
            {
                if (!MotionFx.AllowTransitions) return;
                if (LevelChipScale == null || LevelChipRotate == null) return;

                var pop = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromMilliseconds(LevelChipPopMs),
                };
                pop.KeyFrames.Add(new EasingDoubleKeyFrame(LevelChipPopScale,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(LevelChipPopMs * 0.3)))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                });
                pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(LevelChipPopMs)))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 },
                });
                LevelChipScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop, HandoffBehavior.SnapshotAndReplace);
                LevelChipScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop, HandoffBehavior.SnapshotAndReplace);

                var tilt = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromMilliseconds(LevelChipPopMs),
                };
                tilt.KeyFrames.Add(new EasingDoubleKeyFrame(LevelChipPopDegrees,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(LevelChipPopMs * 0.3)))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                });
                tilt.KeyFrames.Add(new EasingDoubleKeyFrame(0.0,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(LevelChipPopMs)))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 },
                });
                LevelChipRotate.BeginAnimation(RotateTransform.AngleProperty, tilt, HandoffBehavior.SnapshotAndReplace);
            }
            catch (Exception ex) { App.Logger?.Debug("PopLevelChip: {E}", ex.Message); }
        }

        // ============================== 6. Save: absorb ==============================

        /// <summary>
        /// The Save button's acknowledgement: a tick that draws itself (animated StrokeDashOffset,
        /// so it is one Path and no Effect) and a single ripple ring off the button's edge, both
        /// fading out ~1.6s later on their own clock - no timer, nothing to revert, and the save
        /// logic is not touched.
        /// </summary>
        internal void FlashSaveAbsorb()
        {
            try
            {
                if (!MotionFx.AllowTransitions) return;

                if (SaveTick != null)
                {
                    var draw = new DoubleAnimation(9, 0, TimeSpan.FromMilliseconds(SaveTickDrawMs))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    };
                    SaveTick.BeginAnimation(Shape.StrokeDashOffsetProperty, draw, HandoffBehavior.SnapshotAndReplace);

                    // One clock for show + hold + fade: no DispatcherTimer can leak here.
                    var life = new DoubleAnimationUsingKeyFrames
                    {
                        Duration = TimeSpan.FromMilliseconds(SaveAbsorbHoldMs + SaveAbsorbFadeMs),
                    };
                    life.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    life.KeyFrames.Add(new LinearDoubleKeyFrame(1,
                        KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(SaveAbsorbHoldMs))));
                    life.KeyFrames.Add(new LinearDoubleKeyFrame(0,
                        KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(SaveAbsorbHoldMs + SaveAbsorbFadeMs))));
                    SaveTick.BeginAnimation(OpacityProperty, life, HandoffBehavior.SnapshotAndReplace);
                }

                if (SaveRipple != null && SaveRippleScale != null)
                {
                    ScaleTransform rig = SaveRippleScale;
                    double width = SaveAbsorbHost?.ActualWidth ?? 0;
                    var span = TimeSpan.FromMilliseconds(SaveRippleMs);

                    var alpha = new DoubleAnimation(0.75, 0, span)
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    };
                    SaveRipple.BeginAnimation(OpacityProperty, alpha, HandoffBehavior.SnapshotAndReplace);

                    Ripple(ScaleTransform.ScaleXProperty, GrowFactor(width, SaveRippleGrowPx));
                    Ripple(ScaleTransform.ScaleYProperty, GrowFactor(SaveButtonHeight, SaveRippleGrowPx));

                    void Ripple(DependencyProperty property, double to)
                    {
                        var anim = new DoubleAnimation(1.0, to, span)
                        {
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                        };
                        rig.BeginAnimation(property, anim, HandoffBehavior.SnapshotAndReplace);
                    }
                }
            }
            catch (Exception ex) { App.Logger?.Debug("FlashSaveAbsorb: {E}", ex.Message); }
        }

        // ============================== colour helpers ==============================

        private static (double H, double S, double V) ToHsv(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double d = max - min;

            double h = 0;
            if (d > 0.0001)
            {
                if (max == r) h = 60 * (((g - b) / d) % 6);
                else if (max == g) h = 60 * (((b - r) / d) + 2);
                else h = 60 * (((r - g) / d) + 4);
            }
            if (h < 0) h += 360;
            double s = max <= 0.0001 ? 0 : d / max;
            return (h, s, max);
        }

        private static Color FromHsv(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            s = Math.Clamp(s, 0, 1);
            v = Math.Clamp(v, 0, 1);

            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return Color.FromRgb((byte)Math.Round((r + m) * 255),
                                 (byte)Math.Round((g + m) * 255),
                                 (byte)Math.Round((b + m) * 255));
        }
    }
}
