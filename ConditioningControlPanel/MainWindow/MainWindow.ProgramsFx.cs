using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Views.Tabs;

namespace ConditioningControlPanel
{
    // -------------------------------------------------------------------------------------------
    // THE IGNITION CURVE - the Programs tab's lighting rig.
    //
    // The panel ignites as the program progresses. Day 1 is deliberately cold and quiet: the tab
    // looks exactly as it did before any of this existed, and holds not one clock. Every day
    // survived earns the interface heat, and a boss day is fully lit whatever the arithmetic says.
    //
    // ONE SCALAR drives all of it (Helpers/ProgramHeat.cs), so there is no way for two surfaces to
    // disagree about how far along a run is, and the tier boundaries are unit-tested rather than
    // eyeballed.
    //
    //   T0 COLD      < 0.20   today's flat UI, zero ambient motion
    //   T1 WARM   0.20-0.40   sigil breathes, session sheen wakes, today's pip pulses
    //   T2 CHARGED0.40-0.60   Today card's gradient border turns, particles begin, rail fill glows
    //   T3 HOT    0.60-0.80   denser particles, the heat wash blooms, the day counter shimmers
    //   T4 IGNITED0.80-1.00   whole-panel edge glow, a comet runs the rail, the boss badge flares
    //                          (+ every boss day, pinned)
    //
    // RULES THIS FILE KEEPS, all of them the app's own:
    //
    //  • Ambient loops (breathing, border rotation, shimmer, comet, particles) ask
    //    MotionFx.AllowAmbientLoops and are frame-capped to AmbientFrameRate. One-shot moments
    //    (the day wave, the seal stamp, the XP float, the pip ignite) ask AllowTransitions and snap
    //    to their end state when it is off. Reduced motion is therefore T0 VISUALS FOREVER with an
    //    identical layout and every piece of information still on screen.
    //
    //  • The FX are a lighting rig OVER the real art, never a replacement for it. The heat wash and
    //    the edge glow wrap the existing hero plate; the sigil is the breathing element; the rail
    //    comet rides the rail that was already there. Nothing here draws a picture.
    //
    //  • No strobe. Every pulse period comes from ProgramHeat, which floors a full luminance cycle
    //    at 2.2s (~0.45Hz) at maximum heat. The boss flare is a soft glow, not a flash.
    //
    //  • No per-credit storyboard churn. A verifier credit rebuilds this tab, and on a busy session
    //    that is ~90 rebuilds. Every ambient clock is therefore behind a SIGNATURE guard
    //    (_programFxSignature): the brushes and transforms are built once per (run, day, tier,
    //    motion level, performance tier, accent) and merely re-pointed on the rebuilds in between.
    //
    //  • Nothing runs behind a hidden tab or a minimised window. The Skia particle layer enforces
    //    that itself; WPF's own clocks do not, so they are stopped explicitly on both signals and
    //    re-armed on the way back in.
    // -------------------------------------------------------------------------------------------
    public partial class MainWindow
    {
        // ---- tuning ----------------------------------------------------------------------

        /// <summary>The sigil halo's resting opacity - the value authored in the XAML.</summary>
        private const double ProgramSigilRestOpacity = 0.55;

        /// <summary>Alpha of the hero band's accent wash, cold to ignited.</summary>
        private const byte ProgramWashAlphaCold = 70;
        private const byte ProgramWashAlphaHot = 130;

        /// <summary>Seconds for the accent crossfade when a chapter hands over.</summary>
        private const double ProgramAccentFadeSeconds = 1.2;

        /// <summary>The seal stamp's whole life, in seconds.</summary>
        private const double ProgramSealSeconds = 1.6;

        /// <summary>Graduation's colour. Literal on purpose: no theme carries a gold.</summary>
        private static readonly Color ProgramGraduationGold = Color.FromRgb(0xFF, 0xD7, 0x00);

        // ---- state -----------------------------------------------------------------------

        /// <summary>
        /// The run's accent, as ONE mutable brush every element on the run view is painted with.
        ///
        /// <para>This is what makes the chapter crossfade possible at all: the tab hands the same
        /// instance to the title, the pips, the task chips, the bar fill and the carriers, so a
        /// single ColorAnimation on it moves the entire surface together. Building a fresh frozen
        /// brush per element (which is what the tab did before) means a palette change can only ever
        /// be a hard cut on the next repaint.</para>
        /// </summary>
        private SolidColorBrush? _programAccent;

        /// <summary>The run ("{programId}:{attempt}") <see cref="_programAccent"/> belongs to.</summary>
        private string? _programAccentRunKey;

        /// <summary>
        /// Where the accent is HEADED. Compared instead of the brush's own Color because a brush
        /// mid-crossfade reports the animated value, so comparing that would restart the fade on
        /// every one of the ~90 rebuilds a busy session produces and it would never arrive.
        /// </summary>
        private Color? _programAccentTarget;

        private double _programHeat;
        private ProgramHeatTier _programTier;

        /// <summary>Today is a boss day - which pins the heat to 1.0 and lights the badge.</summary>
        private bool _programBossToday;

        /// <summary>
        /// "(run, day, tier, motion level, perf tier, accent)". While this is unchanged the ambient
        /// clocks are left alone and a rebuild only re-points brushes the builders overwrote.
        /// </summary>
        private string? _programFxSignature;

        // Cached ambient scenery. Built on a signature change, re-pointed on every rebuild.
        private LinearGradientBrush? _programBorderBrush;
        private RotateTransform? _programBorderRotate;
        private LinearGradientBrush? _programCounterBrush;
        private TranslateTransform? _programCounterSlide;
        private LinearGradientBrush? _programCometBrush;
        private Brush? _programWashBrush;
        private Brush? _programEdgeBrush;
        private Brush? _programSigilGlowBrush;
        private ScaleTransform? _programSigilScale;
        private RadialGradientBrush? _programWaveBrush;
        private DropShadowEffect? _programRailGlow;
        private DropShadowEffect? _programBossGlow;
        private AmbientFxCanvas? _programParticles;

        /// <summary>
        /// How many times the comet has been deferred waiting for a measured rail. Capped by
        /// <see cref="ProgramCometGate.MaxAttempts"/>; a counter rather than the one-shot bool this
        /// replaced, because that bool was cleared before the retry recursed and so guarded nothing
        /// (ccp-bugs #984, #993, #996, #1001). Reset on a successful run and by
        /// <see cref="StopProgramIgnitionLoops"/>, so an in-flight retry can never latch the comet
        /// off for the rest of the session.
        /// </summary>
        private int _programCometAttempts;

        /// <summary>Seconds the live session sheen is currently sweeping at, so heat can retune it.</summary>
        private double _programSheenSeconds;

        /// <summary>One-shot keys, all "{runKey}:..." and all cleared with the run.</summary>
        private string? _programChapterSealed;
        private string? _programGraduationCelebrated;

        /// <summary>
        /// The run whose chapter history has been BASELINED. Separate from the key above and
        /// load-bearing: a run resumed after a restart has chapters that closed days ago and must
        /// not stamp them all on sight, while a run enrolled a minute ago has an empty history and
        /// must stamp the very first chapter it closes. Only a recorded baseline tells those two
        /// apart, and "the key is still null" cannot - it is null in both.
        /// </summary>
        private string? _programChapterBaselineRun;

        /// <summary>
        /// Day index whose rail node should flare on THIS rebuild, or null. Decided before the rail
        /// is built (the rail is built first), consumed by it, and cleared immediately after.
        /// </summary>
        private int? _programIgniteDay;

        /// <summary>True when this rebuild is the one that observed the day flip to complete.</summary>
        private bool _programDayJustCompleted;

        /// <summary>Hooked once: the window's own minimise/restore, which no tab visibility reports.</summary>
        private bool _programWindowFxHooked;

        // =================================================================================
        //  the shared accent
        // =================================================================================

        /// <summary>
        /// The colour behind <see cref="ProgramAccentBrush"/>, without the brush. A chapter that
        /// carries its own accent wins over the program's - that is what the chapter crossfade
        /// actually fades BETWEEN.
        /// </summary>
        private static Color ProgramAccentColor(string? hex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color color)
                    return color;
            }
            catch { /* a bad accent must never break the tab */ }

            if (ProgramThemeBrush("PinkBrush", Brushes.HotPink) is SolidColorBrush solid) return solid.Color;
            return Colors.HotPink;
        }

        /// <summary>
        /// The run view's accent: one shared, MUTABLE brush per run, retargeted (and crossfaded)
        /// when the chapter under it changes.
        ///
        /// <para>Everything else on the run view still goes through the frozen-per-call
        /// <see cref="ProgramAccentBrush"/> - the browse cards in particular must not share a brush,
        /// since they are five different programs at once. This is the run, and the run is one
        /// colour at a time.</para>
        /// </summary>
        private SolidColorBrush ProgramRunAccent(ProgramDefinition program, ProgramChapter? chapter,
                                                 ProgramEnrollment enrollment)
        {
            var hex = !string.IsNullOrWhiteSpace(chapter?.AccentColor) ? chapter!.AccentColor : program.AccentColor;
            var target = ProgramAccentColor(hex);
            var runKey = ProgramRunKey(enrollment);

            // A different run entirely (enroll, restart, withdraw, a mod swapping the library):
            // there is nothing to fade FROM, so this is a hard cut by definition.
            if (_programAccent == null || !string.Equals(_programAccentRunKey, runKey, StringComparison.Ordinal))
            {
                _programAccent = new SolidColorBrush(target);
                _programAccentRunKey = runKey;
                _programAccentTarget = target;
                return _programAccent;
            }

            if (_programAccentTarget is { } current && current == target) return _programAccent;
            _programAccentTarget = target;

            try
            {
                // A palette change is a TRANSITION, not an ambient loop: at Reduced the panel still
                // recolours smoothly, at Off it simply is the new colour.
                if (!MotionFx.AllowTransitions)
                {
                    _programAccent.BeginAnimation(SolidColorBrush.ColorProperty, null);
                    _programAccent.Color = target;
                    return _programAccent;
                }

                var fade = new ColorAnimation(target, TimeSpan.FromSeconds(ProgramAccentFadeSeconds))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(fade, AmbientFrameRate);
                _programAccent.BeginAnimation(SolidColorBrush.ColorProperty, fade);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Program accent crossfade: {E}", ex.Message);
                try { _programAccent.Color = target; } catch { }
            }

            return _programAccent;
        }

        /// <summary>The accent's colour right now, for the derived (frozen) scenery brushes.</summary>
        private Color ProgramAccentColorNow() =>
            _programAccentTarget ?? (_programAccent?.Color ?? Colors.HotPink);

        // =================================================================================
        //  the entry point
        // =================================================================================

        /// <summary>
        /// The scalar for this repaint, computed FIRST - before a single element is dressed.
        ///
        /// <para>Order matters: the day rail is built before the Today panel and it needs the tier
        /// too (today's pip only pulses from T1), so the heat cannot be a by-product of the pass
        /// that dresses the panel. This is the one place it is derived; everything else reads
        /// <see cref="_programHeat"/> and <see cref="_programTier"/>.</para>
        /// </summary>
        private void ComputeProgramHeat(ProgramDefinition program, ProgramEnrollment enrollment, ProgramDay? day)
        {
            _programBossToday = day?.IsBoss == true;
            _programHeat = ProgramHeat.Compute(enrollment.CurrentDay, program.LengthDays,
                                               day?.Intensity ?? 0.0, _programBossToday);
            _programTier = ProgramHeat.TierOf(_programHeat);
        }

        /// <summary>
        /// Dresses the run view for the heat computed by <see cref="ComputeProgramHeat"/>. Called at
        /// the end of every run-view build, which on a live session is once per verifier credit - so
        /// everything expensive in here is behind the signature guard, and everything outside it is
        /// a property write.
        /// </summary>
        private void ApplyProgramIgnition(ProgramsTabView tab, ProgramEnrollment enrollment)
        {
            try
            {
                EnsureProgramWindowFxHooked();

                var boss = _programBossToday;
                var accentColor = ProgramAccentColorNow();
                var ambient = MotionFx.AllowAmbientLoops;

                var signature = string.Join("|",
                    _programAccentRunKey ?? "-",
                    enrollment.CurrentDay,
                    (int)_programTier,
                    Math.Round(_programHeat, 3).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (int)MotionFx.Level,
                    (int)PerformanceProfile.CurrentTier,
                    accentColor.ToString());

                var rearm = !string.Equals(signature, _programFxSignature, StringComparison.Ordinal);
                if (rearm)
                {
                    _programFxSignature = signature;
                    BuildProgramIgnitionScenery(accentColor);
                }

                // ---- re-point what the builders overwrite on every rebuild ----
                if (_programWashBrush != null) tab.TodayHeroGlow.Fill = _programWashBrush;
                if (_programSigilGlowBrush != null && tab.RunSigilGlow.Visibility == Visibility.Visible)
                    tab.RunSigilGlow.Fill = _programSigilGlowBrush;

                if (_programTier >= ProgramHeatTier.Charged && ambient && _programBorderBrush != null)
                {
                    tab.TodayPanel.BorderBrush = _programBorderBrush;
                    tab.TodayPanel.BorderThickness = new Thickness(2);
                }

                if (!rearm) return;

                // ---- ambient clocks, once per signature ----
                ApplyProgramSigilBreath(tab, ambient);
                ApplyProgramBorderRotation(ambient);
                ApplyProgramWashBloom(tab, ambient);
                ApplyProgramRailGlow(tab);
                ApplyProgramCounterShimmer(tab, ambient);
                ApplyProgramEdgeGlow(tab, ambient);
                ApplyProgramBossFlare(tab, boss, ambient);
                ApplyProgramRailComet(tab, ambient);
                ApplyProgramParticles(tab, accentColor);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program ignition pass failed");
            }
        }

        /// <summary>
        /// Rebuilds every cached brush/transform for the current accent and heat. Called only on a
        /// signature change, so the ~90 rebuilds of a busy session allocate nothing.
        /// </summary>
        private void BuildProgramIgnitionScenery(Color accent)
        {
            var heat = _programHeat;

            // Hero-band wash. Alpha climbs with heat, so the band literally gets warmer rather than
            // merely gaining a second effect on top.
            var washAlpha = (byte)Math.Round(ProgramWashAlphaCold
                + (ProgramWashAlphaHot - ProgramWashAlphaCold) * Math.Clamp(heat, 0, 1));
            _programWashBrush = ProgramRadialGlowBrush(new SolidColorBrush(accent), washAlpha, 0.78, 0.2, 0.9);

            _programSigilGlowBrush = ProgramRadialGlowBrush(new SolidColorBrush(accent), 150);

            // The Today card's animated border. WPF has no conic gradient, so this is a linear one
            // ROTATED by its own RelativeTransform - the transform is on the brush, so the border
            // turns without a single element in the tree being transformed and without any layout.
            _programBorderRotate = new RotateTransform(0, 0.5, 0.5);
            var border = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            border.GradientStops.Add(new GradientStop(WithAccentAlpha(accent, 0x30), 0.00));
            border.GradientStops.Add(new GradientStop(WithAccentAlpha(accent, 0xC0), 0.35));
            border.GradientStops.Add(new GradientStop(LightenToward(accent, 0.55), 0.50));
            border.GradientStops.Add(new GradientStop(WithAccentAlpha(accent, 0xC0), 0.65));
            border.GradientStops.Add(new GradientStop(WithAccentAlpha(accent, 0x30), 1.00));
            border.RelativeTransform = _programBorderRotate;
            _programBorderBrush = border;

            // Day-counter shimmer: a highlight band travelling through the TEXT's own fill. The
            // slide is on the brush's RelativeTransform for the same reason as the border - no
            // element is transformed, so nothing re-measures.
            var ink = (ProgramThemeBrush("TextLightBrush", Brushes.White) as SolidColorBrush)?.Color ?? Colors.White;
            _programCounterSlide = new TranslateTransform(-1, 0);
            var counter = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            counter.GradientStops.Add(new GradientStop(ink, 0.00));
            counter.GradientStops.Add(new GradientStop(ink, 0.38));
            counter.GradientStops.Add(new GradientStop(LightenToward(accent, 0.45), 0.50));
            counter.GradientStops.Add(new GradientStop(ink, 0.62));
            counter.GradientStops.Add(new GradientStop(ink, 1.00));
            counter.RelativeTransform = _programCounterSlide;
            _programCounterBrush = counter;

            // Rail comet: a bright head with the accent trailing behind it.
            var comet = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            comet.GradientStops.Add(new GradientStop(WithAccentAlpha(accent, 0x00), 0.00));
            comet.GradientStops.Add(new GradientStop(WithAccentAlpha(accent, 0x66), 0.62));
            comet.GradientStops.Add(new GradientStop(LightenToward(accent, 0.7), 0.90));
            comet.GradientStops.Add(new GradientStop(WithAccentAlpha(accent, 0x00), 1.00));
            comet.Freeze();
            _programCometBrush = comet;

            // Whole-panel edge glow: transparent through the middle, accent only at the rim, so it
            // reads as light leaking in around the panel rather than as a tint over the content.
            try
            {
                var edge = new RadialGradientBrush
                {
                    Center = new Point(0.5, 0.5),
                    GradientOrigin = new Point(0.5, 0.5),
                    RadiusX = 0.72,
                    RadiusY = 0.72,
                };
                edge.GradientStops.Add(new GradientStop(WithAccentAlpha(accent, 0x00), 0.00));
                edge.GradientStops.Add(new GradientStop(WithAccentAlpha(accent, 0x00), 0.62));
                edge.GradientStops.Add(new GradientStop(WithAccentAlpha(accent, 0x8A), 1.00));
                edge.Freeze();
                _programEdgeBrush = edge;
            }
            catch { _programEdgeBrush = null; }

            // Day-complete wave. Deliberately NOT frozen: the moment animates this brush's radii.
            var wave = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.02,
                RadiusY = 0.02,
            };
            wave.GradientStops.Add(new GradientStop(WithAccentAlpha(accent, 0x00), 0.00));
            wave.GradientStops.Add(new GradientStop(WithAccentAlpha(accent, 0xB0), 0.72));
            wave.GradientStops.Add(new GradientStop(WithAccentAlpha(accent, 0x00), 1.00));
            _programWaveBrush = wave;

            // Glow EFFECTS are the one thing here a Performance-tier machine must not pay for, and
            // the blur radius is capped by the tier rather than by taste.
            if (PerformanceProfile.AllowGlow(PerformanceProfile.CurrentTier))
            {
                var cap = PerformanceProfile.MaxGlowBlurRadius(PerformanceProfile.CurrentTier);
                _programRailGlow = new DropShadowEffect
                {
                    Color = accent,
                    ShadowDepth = 0,
                    BlurRadius = Math.Min(cap, 6 + 10 * heat),
                    Opacity = 0.45 + 0.35 * heat,
                };
                _programBossGlow = new DropShadowEffect
                {
                    Color = accent,
                    ShadowDepth = 0,
                    BlurRadius = Math.Min(cap, 14),
                    Opacity = 0.7,
                };
            }
            else
            {
                _programRailGlow = null;
                _programBossGlow = null;
            }
        }

        // =================================================================================
        //  the ambient pieces
        // =================================================================================

        /// <summary>
        /// T1+: the sigil breathes, with both the amplitude and the period scaling with heat. The
        /// sigil is the breathing element on purpose - it is the program's own art, so the panel
        /// looks alive without anything decorative being added to it.
        /// </summary>
        private void ApplyProgramSigilBreath(ProgramsTabView tab, bool ambient)
        {
            var glow = tab.RunSigilGlow;
            try
            {
                glow.BeginAnimation(UIElement.OpacityProperty, null);
                _programSigilScale ??= new ScaleTransform(1, 1);
                _programSigilScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                _programSigilScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

                // Four of the five shipped programs have no sigil art, and the halo is collapsed with
                // it - so this gate is the common case, not the edge one. A Forever clock on a
                // collapsed element is invisible work that runs for the length of the run.
                if (_programTier < ProgramHeatTier.Warm || !ambient
                    || glow.Visibility != Visibility.Visible)
                {
                    glow.Opacity = ProgramSigilRestOpacity;
                    _programSigilScale.ScaleX = _programSigilScale.ScaleY = 1;
                    return;
                }

                glow.RenderTransformOrigin = new Point(0.5, 0.5);
                glow.RenderTransform = _programSigilScale;

                var amp = ProgramHeat.BreathAmplitude(_programHeat);
                var seconds = ProgramHeat.BreathSeconds(_programHeat);

                var breath = new DoubleAnimation(
                    Math.Clamp(ProgramSigilRestOpacity - amp, 0.10, 1.0),
                    Math.Clamp(ProgramSigilRestOpacity + amp, 0.10, 0.95),
                    TimeSpan.FromSeconds(seconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(breath, AmbientFrameRate);
                glow.BeginAnimation(UIElement.OpacityProperty, breath);

                var swell = new DoubleAnimation(1.0, 1.0 + amp * 0.35, TimeSpan.FromSeconds(seconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(swell, AmbientFrameRate);
                _programSigilScale.BeginAnimation(ScaleTransform.ScaleXProperty, swell);
                _programSigilScale.BeginAnimation(ScaleTransform.ScaleYProperty, swell);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Program sigil breath: {E}", ex.Message);
                try { glow.Opacity = ProgramSigilRestOpacity; } catch { }
            }
        }

        /// <summary>T2+: the Today card's gradient border turns, fastest at full heat.</summary>
        private void ApplyProgramBorderRotation(bool ambient)
        {
            var rotate = _programBorderRotate;
            if (rotate == null) return;
            try
            {
                rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                if (_programTier < ProgramHeatTier.Charged || !ambient)
                {
                    // Parked at an angle rather than at zero: a static gradient reading corner to
                    // corner is a lit border, which is the right degraded state for this.
                    rotate.Angle = 35;
                    return;
                }

                var spin = new DoubleAnimation(0, 360,
                    TimeSpan.FromSeconds(ProgramHeat.BorderLapSeconds(_programHeat)))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                };
                Timeline.SetDesiredFrameRate(spin, AmbientFrameRate);
                rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
            }
            catch (Exception ex) { App.Logger?.Debug("Program border rotation: {E}", ex.Message); }
        }

        /// <summary>
        /// The hero band's wash. Present at every tier (it always was), brighter with heat, and from
        /// T3 it blooms - deliberately on a longer period than the sigil so the two never beat
        /// together into something that reads as one pulse.
        /// </summary>
        private void ApplyProgramWashBloom(ProgramsTabView tab, bool ambient)
        {
            var wash = tab.TodayHeroGlow;
            try
            {
                wash.BeginAnimation(UIElement.OpacityProperty, null);
                var peak = ProgramHeat.WashOpacity(_programHeat);

                if (_programTier < ProgramHeatTier.Hot || !ambient)
                {
                    wash.Opacity = peak;
                    return;
                }

                var bloom = new DoubleAnimation(peak * 0.68, peak,
                    TimeSpan.FromSeconds(ProgramHeat.BreathSeconds(_programHeat) * 1.7))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(bloom, AmbientFrameRate);
                wash.BeginAnimation(UIElement.OpacityProperty, bloom);
            }
            catch (Exception ex) { App.Logger?.Debug("Program wash bloom: {E}", ex.Message); }
        }

        /// <summary>T2+: the rail's completed segment starts glowing. Static - no clock at all.</summary>
        private void ApplyProgramRailGlow(ProgramsTabView tab)
        {
            try
            {
                tab.RailProgressFill.Effect =
                    _programTier >= ProgramHeatTier.Charged ? _programRailGlow : null;
            }
            catch (Exception ex) { App.Logger?.Debug("Program rail glow: {E}", ex.Message); }
        }

        /// <summary>T3+: a highlight travels through the day counter's own ink.</summary>
        private void ApplyProgramCounterShimmer(ProgramsTabView tab, bool ambient)
        {
            try
            {
                _programCounterSlide?.BeginAnimation(TranslateTransform.XProperty, null);

                if (_programTier < ProgramHeatTier.Hot || !ambient || _programCounterBrush == null)
                {
                    // Back to the THEME brush, not to a hard-coded white: this text is
                    // {DynamicResource TextLightBrush} in the XAML and must keep re-theming with
                    // the mod once the shimmer lets go of it.
                    tab.TxtRunDayCounter.SetResourceReference(TextBlock.ForegroundProperty, "TextLightBrush");
                    return;
                }

                tab.TxtRunDayCounter.Foreground = _programCounterBrush;

                var sweep = new DoubleAnimation(-1.0, 1.0,
                    TimeSpan.FromSeconds(ProgramHeat.BreathSeconds(_programHeat) * 2.2))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                };
                Timeline.SetDesiredFrameRate(sweep, AmbientFrameRate);
                _programCounterSlide!.BeginAnimation(TranslateTransform.XProperty, sweep);
            }
            catch (Exception ex) { App.Logger?.Debug("Program counter shimmer: {E}", ex.Message); }
        }

        /// <summary>T4 / boss: a soft glow around the inside edge of the whole tab panel.</summary>
        private void ApplyProgramEdgeGlow(ProgramsTabView tab, bool ambient)
        {
            var edge = tab.ProgramsEdgeGlow;
            try
            {
                edge.BeginAnimation(UIElement.OpacityProperty, null);

                if (_programTier < ProgramHeatTier.Ignited || _programEdgeBrush == null)
                {
                    edge.Opacity = 0;
                    edge.Fill = null;
                    return;
                }

                edge.Fill = _programEdgeBrush;
                var peak = ProgramHeat.EdgeGlowOpacity(_programHeat);

                if (!ambient) { edge.Opacity = peak; return; }

                // The slowest loop on the surface, on purpose: this one is the size of the panel,
                // so anything quicker would read as the room lights flickering.
                var breath = new DoubleAnimation(peak * 0.55, peak,
                    TimeSpan.FromSeconds(ProgramHeat.BreathSeconds(_programHeat) * 2.4))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(breath, AmbientFrameRate);
                edge.BeginAnimation(UIElement.OpacityProperty, breath);
            }
            catch (Exception ex) { App.Logger?.Debug("Program edge glow: {E}", ex.Message); }
        }

        /// <summary>Boss days: the badge flares. A soft glow that swells, never a flash.</summary>
        private void ApplyProgramBossFlare(ProgramsTabView tab, bool boss, bool ambient)
        {
            var badge = tab.TodayBossBadge;
            try
            {
                badge.BeginAnimation(UIElement.OpacityProperty, null);
                if (!boss)
                {
                    badge.Effect = null;
                    badge.Opacity = 1;
                    return;
                }

                badge.Effect = _programBossGlow;
                if (!ambient) { badge.Opacity = 1; return; }

                var flare = new DoubleAnimation(0.72, 1.0,
                    TimeSpan.FromSeconds(ProgramHeat.BreathSeconds(_programHeat) * 1.35))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(flare, AmbientFrameRate);
                badge.BeginAnimation(UIElement.OpacityProperty, flare);
            }
            catch (Exception ex) { App.Logger?.Debug("Program boss flare: {E}", ex.Message); }
        }

        /// <summary>
        /// T4: a comet runs the day rail. Skipped (and re-asked, at most
        /// <see cref="ProgramCometGate.MaxAttempts"/> times and always BELOW input priority) while
        /// the rail has no measured width - the travel distance IS that width.
        ///
        /// <para><b>This method used to hard-freeze the app</b> (ccp-bugs #984, #993, #996, #1001).
        /// Three independent faults lined up: the width was read from a host that is authored
        /// Collapsed and only shown further down, so it was structurally always 0; the retry
        /// re-posted at DispatcherPriority.Loaded (6), which outranks Input (5); and it cleared its
        /// own one-shot guard before recursing, with no cap. The result was an unbounded chain of
        /// above-input work - a window that still rendered and never logged a crash, but accepted
        /// no clicks, on every visit to a Programs tab at Ignited heat with Motion FX at Full. All
        /// three are fixed below and each one alone would be enough; the effect is cosmetic, so its
        /// worst failure must be "no comet", never "no app".</para>
        /// </summary>
        private void ApplyProgramRailComet(ProgramsTabView tab, bool ambient)
        {
            try
            {
                tab.RailCometSlide.BeginAnimation(TranslateTransform.XProperty, null);

                if (_programTier < ProgramHeatTier.Ignited || !ambient || _programCometBrush == null)
                {
                    tab.RailCometHost.Visibility = Visibility.Collapsed;
                    tab.RailComet.Opacity = 0;
                    _programCometAttempts = 0;
                    return;
                }

                // LAYER 1. Show the host BEFORE measuring it. WPF never measures a collapsed
                // element, so reading ActualWidth while it is still Collapsed can only ever
                // return 0 - the gate below could not pass, ever.
                tab.RailCometHost.Visibility = Visibility.Visible;

                var gate = ProgramCometGate.Decide(
                    tab.RailCometHost.ActualWidth, tab.ProgramDayRail.ActualWidth, _programCometAttempts);
                _programCometAttempts = gate.Attempts;

                if (gate.Action != ProgramCometAction.Run)
                {
                    tab.RailComet.Opacity = 0;
                    // LAYER 3: out of attempts. Park the host and leave the rail alone.
                    if (gate.Action == ProgramCometAction.GiveUp)
                    {
                        tab.RailCometHost.Visibility = Visibility.Collapsed;
                        return;
                    }

                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                    // LAYER 2: Background (4) sits BELOW Input (5). Layout still runs first
                    // (Render/Loaded outrank it), so the re-ask sees a measured rail - but a
                    // pathological repeat can only ever degrade to "no comet", never to a window
                    // that ignores the mouse. The counter is NOT cleared before recursing.
                    dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                    {
                        try
                        {
                            var live = ProgramsTab;
                            if (live is { IsVisible: true }) ApplyProgramRailComet(live, MotionFx.AllowAmbientLoops);
                        }
                        catch (Exception ex) { App.Logger?.Debug("Program comet retry: {E}", ex.Message); }
                    }));
                    return;
                }

                var width = gate.Width;
                tab.RailComet.Fill = _programCometBrush;
                tab.RailComet.Opacity = 1;

                var run = new DoubleAnimation(-140, width + 40,
                    TimeSpan.FromSeconds(ProgramHeat.CometLapSeconds(_programHeat)))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                };
                Timeline.SetDesiredFrameRate(run, AmbientFrameRate);
                tab.RailCometSlide.BeginAnimation(TranslateTransform.XProperty, run);
            }
            catch (Exception ex) { App.Logger?.Debug("Program rail comet: {E}", ex.Message); }
        }

        /// <summary>
        /// T2+: sparse accent particles drifting over the hero band. Reuses the app's own
        /// <see cref="AmbientFxCanvas"/> rather than a second Skia rig, so the whole "stop when
        /// nobody is looking" rule set (loaded, visible, window active, window not minimised, tier
        /// budget, mini perf governor) comes for free and behaves identically to every other
        /// ambient surface. Density is the heat's share of the TIER's budget, so this can only ever
        /// spend less than the machine already allows.
        /// </summary>
        private void ApplyProgramParticles(ProgramsTabView tab, Color accent)
        {
            try
            {
                var wanted = ProgramHeat.ParticleCount(_programHeat);
                if (wanted <= 0 || !MotionFx.AllowParticles)
                {
                    _programParticles?.Stop();
                    return;
                }

                var canvas = EnsureProgramParticleLayer(tab);
                if (canvas == null) return;

                var budget = Math.Max(1, PerformanceProfile.MaxAmbientParticles(PerformanceProfile.CurrentTier));
                canvas.StartLayers(new AmbientFxConfig
                {
                    Layers = AmbientFxLayers.DustField,
                    Intensity = 0.55 + 0.45 * _programHeat,
                    DustDensity = Math.Clamp(wanted / (double)budget, 0.0, 1.0),
                    Tint = accent,
                });
            }
            catch (Exception ex) { App.Logger?.Debug("Program particles: {E}", ex.Message); }
        }

        /// <summary>Adds the particle canvas to its layer on first use. Null means "just skip".</summary>
        private AmbientFxCanvas? EnsureProgramParticleLayer(ProgramsTabView tab)
        {
            if (_programParticles != null) return _programParticles;
            try
            {
                var canvas = new AmbientFxCanvas { IsHitTestVisible = false };
                tab.TodayFxLayer.Children.Add(canvas);
                _programParticles = canvas;
                // Every other tab's ambient canvas is registered here; this one was not, so it sat
                // outside the tab-switch park/resume governor and leaned on its own visibility
                // watch alone. Registration is idempotent, and a Stop()ped canvas ignores Resume().
                RegisterTabFx("programs", canvas);
                return canvas;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program particle layer could not be built");
                return null;
            }
        }

        // =================================================================================
        //  lifecycle
        // =================================================================================

        /// <summary>
        /// Tab visibility is not enough on its own: a minimised window leaves every tab's IsVisible
        /// true, so the WPF clocks here would keep ticking behind a window nobody can see. The Skia
        /// canvas already watches this signal itself; these are the ones that do not.
        /// </summary>
        private void EnsureProgramWindowFxHooked()
        {
            if (_programWindowFxHooked) return;
            _programWindowFxHooked = true;
            try
            {
                StateChanged += (_, _) =>
                {
                    try
                    {
                        if (WindowState == WindowState.Minimized) StopProgramIgnitionLoops();
                        else if (ProgramsTab is { IsVisible: true }) MarshalProgramRefresh();
                    }
                    catch (Exception ex) { App.Logger?.Debug("Program FX window state: {E}", ex.Message); }
                };
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Program FX window hook failed"); }
        }

        /// <summary>
        /// Detaches every ambient clock this file owns and leaves the surface at its resting state.
        /// Clearing the signature is what re-arms it: the next rebuild on a visible tab sees a
        /// mismatch and builds the scenery again.
        /// </summary>
        private void StopProgramIgnitionLoops()
        {
            _programFxSignature = null;
            // Re-arm the comet with a full attempt budget. Without this a tab hidden mid-retry
            // would come back with the counter already spent and never light the rail again.
            _programCometAttempts = 0;
            try
            {
                _programParticles?.Stop();

                var tab = ProgramsTab;
                if (tab == null) return;

                tab.RunSigilGlow.BeginAnimation(UIElement.OpacityProperty, null);
                tab.RunSigilGlow.Opacity = ProgramSigilRestOpacity;
                if (_programSigilScale != null)
                {
                    _programSigilScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    _programSigilScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    _programSigilScale.ScaleX = _programSigilScale.ScaleY = 1;
                }

                _programBorderRotate?.BeginAnimation(RotateTransform.AngleProperty, null);
                _programCounterSlide?.BeginAnimation(TranslateTransform.XProperty, null);

                tab.TodayHeroGlow.BeginAnimation(UIElement.OpacityProperty, null);
                tab.TodayBossBadge.BeginAnimation(UIElement.OpacityProperty, null);
                tab.TodayBossBadge.Opacity = 1;

                // The two panel-wide surfaces are parked OUTRIGHT, not merely un-animated: they are
                // siblings of the whole scrolling view, so a withdraw that swaps the run panel for
                // the browse list would otherwise leave a lit rim glowing around a list of programs.
                tab.ProgramsEdgeGlow.BeginAnimation(UIElement.OpacityProperty, null);
                tab.ProgramsEdgeGlow.Opacity = 0;
                tab.ProgramsEdgeGlow.Fill = null;

                tab.RailCometSlide.BeginAnimation(TranslateTransform.XProperty, null);
                tab.RailComet.Opacity = 0;
                tab.RailCometHost.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex) { App.Logger?.Debug("StopProgramIgnitionLoops: {E}", ex.Message); }
        }

        // =================================================================================
        //  the discrete moments
        // =================================================================================

        /// <summary>
        /// Decides, once per rebuild and BEFORE anything is built, whether this is the repaint that
        /// caught the day flipping to complete.
        ///
        /// <para>It has to happen here rather than inside the Today panel because the day RAIL is
        /// built first and needs the answer: the node that ignites is the one whose day just
        /// closed. The once-per-day key is the same one the banner pop has always used, so a day
        /// completed behind a hidden tab still shows up settled, with no celebration replayed for
        /// an audience that was not there.</para>
        /// </summary>
        private void NoteProgramDayCompletion(ProgramEnrollment enrollment, bool tabVisible)
        {
            _programIgniteDay = null;
            _programDayJustCompleted = false;

            try
            {
                var record = App.Programs?.TodayRecord;
                if (record?.DayCompleted != true) return;

                var dayKey = $"{ProgramRunKey(enrollment)}:{enrollment.CurrentDay}";
                if (string.Equals(_programDayCompletePopped, dayKey, StringComparison.Ordinal)) return;

                _programDayCompletePopped = dayKey;
                if (!tabVisible) return;

                _programDayJustCompleted = true;
                _programIgniteDay = enrollment.CurrentDay;
            }
            catch (Exception ex) { App.Logger?.Debug("NoteProgramDayCompletion: {E}", ex.Message); }
        }

        /// <summary>
        /// The day-complete moment on top of the banner spring the tab already did: an accent wave
        /// washing out across the hero band, and the day's real XP floating up off it.
        ///
        /// <para>Both are one-shots on the interaction clock, so Reduced keeps them and only Off
        /// removes them - and "removed" means the elements are left parked at opacity 0, never
        /// stranded mid-animation.</para>
        /// </summary>
        private void PlayProgramDayCompleteMoment(ProgramsTabView tab, ProgramDay? day)
        {
            try
            {
                var xp = ProgramHeat.DayXp(day?.Intensity ?? 0.0, day?.IsBoss == true);

                if (!MotionFx.AllowTransitions || _programWaveBrush == null)
                {
                    tab.TodayWave.Opacity = 0;
                    tab.TxtTodayXpFloat.Opacity = 0;
                    return;
                }

                // ---- the wave ----
                // The BRUSH's radii grow, not the element's scale: an ellipse big enough to cover a
                // 1400px band would hang a thousand pixels outside the panel on every side.
                tab.TodayWave.Fill = _programWaveBrush;
                var grow = new DoubleAnimation(0.02, 1.15, TimeSpan.FromSeconds(0.95))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                };
                _programWaveBrush.BeginAnimation(RadialGradientBrush.RadiusXProperty, grow);
                _programWaveBrush.BeginAnimation(RadialGradientBrush.RadiusYProperty, grow);

                var wash = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(0.95) };
                wash.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                wash.KeyFrames.Add(new LinearDoubleKeyFrame(0.75, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.18))));
                wash.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.95))));
                tab.TodayWave.BeginAnimation(UIElement.OpacityProperty, wash);

                // ---- the XP float ----
                // The real number the service just awarded (ProgramHeat.DayXp mirrors AwardDayXp) -
                // a decorative one on a screen full of honest counters would be the odd one out.
                tab.TxtTodayXpFloat.Text = Loc.GetF("label_0_xp_3", xp);
                if (_programAccent != null) tab.TxtTodayXpFloat.Foreground = _programAccent;

                var rise = new DoubleAnimation(18, -46, TimeSpan.FromSeconds(1.5))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                };
                tab.TodayXpFloatSlide.BeginAnimation(TranslateTransform.YProperty, rise);

                var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1.5) };
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.22))));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.95))));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5))));
                tab.TxtTodayXpFloat.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            catch (Exception ex) { App.Logger?.Debug("Program day-complete moment: {E}", ex.Message); }
        }

        /// <summary>
        /// Task complete: a burst on the card that just ticked, sized by heat.
        ///
        /// <para>The card itself is not ours to hold - the list is an ItemsControl and its containers
        /// are generated - so the anchors are looked up from the generator one dispatcher pass later,
        /// once the containers this rebuild produced actually exist. The scale pop and the done-chip
        /// pop are the item template's own storyboards, fired by the JustCompleted carrier; this
        /// only adds the sparks, which is the one part a DataTemplate cannot do.</para>
        /// </summary>
        private void CelebrateProgramTaskCompletions(ProgramsTabView tab, IReadOnlyList<ProgramTaskItem> items)
        {
            try
            {
                if (!tab.IsVisible) return;

                var indices = new List<int>();
                for (var i = 0; i < items.Count; i++)
                    if (items[i].JustCompleted) indices.Add(i);
                if (indices.Count == 0) return;

                var count = ProgramHeat.BurstCount(_programHeat);
                var color = ProgramAccentColorNow();

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    try
                    {
                        var live = ProgramsTab;
                        if (live is not { IsVisible: true }) return;

                        foreach (var index in indices)
                        {
                            var container = live.TodayTaskList.ItemContainerGenerator
                                                .ContainerFromIndex(index) as FrameworkElement;
                            if (container == null) continue;
                            FireBurstAt(container, FxBurstSpot.Center, color, count);
                        }
                    }
                    catch (Exception ex) { App.Logger?.Debug("Program task burst: {E}", ex.Message); }
                }));
            }
            catch (Exception ex) { App.Logger?.Debug("CelebrateProgramTaskCompletions: {E}", ex.Message); }
        }

        /// <summary>
        /// Chapter cleared: the seal stamp, then the palette hands over to the next chapter.
        ///
        /// <para>Detected from the enrollment's own CompletedChapterIds rather than from the
        /// service's ChapterCompleted event, on purpose: the list is persisted state, so this works
        /// identically for a chapter that closed while the tab was hidden or while the app was shut,
        /// and it adds no subscription to a service another lane is editing.</para>
        ///
        /// <para>Called BEFORE the run accent is recomputed, so the stamp wears the colour of the
        /// chapter it is sealing while the panel underneath is already fading to the next one's.</para>
        /// </summary>
        private void NoteProgramChapterSeal(ProgramDefinition program, ProgramEnrollment enrollment, bool tabVisible)
        {
            try
            {
                var ids = enrollment.CompletedChapterIds;
                var runKey = ProgramRunKey(enrollment);
                var latest = ids is { Count: > 0 } ? ids[ids.Count - 1] : "-";
                var key = $"{runKey}:{latest}";

                // First sight of this run in this process - which is a fresh enroll with no history
                // OR a run resumed with three chapters already behind it. Record where it stands and
                // celebrate nothing; only a change from THIS baseline is a chapter closing live.
                if (!string.Equals(_programChapterBaselineRun, runKey, StringComparison.Ordinal))
                {
                    _programChapterBaselineRun = runKey;
                    _programChapterSealed = key;
                    return;
                }

                if (string.Equals(_programChapterSealed, key, StringComparison.Ordinal)) return;
                _programChapterSealed = key;
                if (!tabVisible || latest == "-") return;

                var chapter = program.Chapters.Find(c => string.Equals(c.Id, latest, StringComparison.Ordinal));
                if (chapter == null) return;

                PlayProgramChapterSeal(chapter, ProgramAccentColorNow());
            }
            catch (Exception ex) { App.Logger?.Debug("NoteProgramChapterSeal: {E}", ex.Message); }
        }

        /// <summary>
        /// The stamp itself: the cleared chapter's own name and day range inside an accent ring,
        /// scaled in and faded away over ~1.6s. No new localisation keys - the headline is authored
        /// content and the sub-line reuses the ceremony's own "Days {0}-{1}".
        /// </summary>
        private void PlayProgramChapterSeal(ProgramChapter chapter, Color accent)
        {
            var tab = ProgramsTab;
            if (tab == null) return;

            try
            {
                var ink = new SolidColorBrush(accent);
                ink.Freeze();

                tab.TxtProgramsSealTitle.Text = chapter.Name;
                tab.TxtProgramsSealTitle.Foreground = ink;
                tab.TxtProgramsSealGlyph.Foreground = ink;
                tab.ProgramsSealRing.BorderBrush = ink;

                var days = chapter.Days;
                tab.TxtProgramsSealSub.Text = days is { Count: > 0 }
                    ? Loc.GetF("program_enroll_chapter_days", days[0].DayIndex, days[days.Count - 1].DayIndex)
                    : chapter.Subtitle;

                if (!MotionFx.AllowTransitions)
                {
                    // Motion off: no stamp at all. The chapter change is still stated by the run
                    // header, which is the information; this was only ever the ceremony.
                    tab.ProgramsSealHost.Visibility = Visibility.Collapsed;
                    tab.ProgramsSealHost.Opacity = 0;
                    return;
                }

                tab.ProgramsSealHost.Visibility = Visibility.Visible;

                var life = TimeSpan.FromSeconds(ProgramSealSeconds);
                var fade = new DoubleAnimationUsingKeyFrames { Duration = life };
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.22))));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.15))));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(life)));
                fade.Completed += (_, _) =>
                {
                    // Collapsed, not merely transparent: a transparent full-panel overlay is still an
                    // element WPF measures and renders on every layout pass for the rest of the run.
                    try { tab.ProgramsSealHost.Visibility = Visibility.Collapsed; }
                    catch (Exception ex) { App.Logger?.Debug("Seal teardown: {E}", ex.Message); }
                };
                tab.ProgramsSealHost.BeginAnimation(UIElement.OpacityProperty, fade);

                var stamp = new DoubleAnimationUsingKeyFrames { Duration = life };
                stamp.KeyFrames.Add(new LinearDoubleKeyFrame(1.35, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                stamp.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.3)))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 },
                });
                stamp.KeyFrames.Add(new LinearDoubleKeyFrame(1.06, KeyTime.FromTimeSpan(life)));
                tab.ProgramsSealScale.BeginAnimation(ScaleTransform.ScaleXProperty, stamp);
                tab.ProgramsSealScale.BeginAnimation(ScaleTransform.ScaleYProperty, stamp);

                FireBurstAt(tab.ProgramsSealRing, FxBurstSpot.Center, accent, ProgramHeat.BurstCount(1.0));
                App.Logger?.Information("[FX] program chapter sealed: {Chapter}", chapter.Name);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("PlayProgramChapterSeal: {E}", ex.Message);
                try { tab.ProgramsSealHost.Visibility = Visibility.Collapsed; } catch { }
            }
        }

        /// <summary>
        /// Graduation: the run's accent crossfades to gold and the finish card is painted in it.
        ///
        /// <para>The mockup also re-ignited the rail's pips in sequence. That one is dropped rather
        /// than faked: graduating switches the tab to the graduated panel, which has no rail on it,
        /// and giving it one would mean rebuilding a screen this pass is not allowed to restructure.
        /// The gold hand-over lands on the surface the user is actually looking at.</para>
        /// </summary>
        private void CelebrateProgramGraduation(ProgramsTabView tab, ProgramDefinition program,
                                                ProgramEnrollment enrollment)
        {
            try
            {
                var key = ProgramRunKey(enrollment);
                var already = string.Equals(_programGraduationCelebrated, key, StringComparison.Ordinal);
                _programGraduationCelebrated = key;

                // The card wears the shared accent either way, so a re-entry to a finished run is
                // gold too - it just does not re-play the fade or the sparks. Seeded from the
                // PROGRAM's own accent when there is no brush yet, which is the normal path: a run
                // that graduated before the app was last closed opens straight onto this panel, so
                // the run view never ran and there is nothing to have left one behind.
                _programAccent ??= new SolidColorBrush(ProgramAccentColor(program.AccentColor));
                _programAccentRunKey = key;
                tab.ProgramsGraduatedCard.BorderBrush = _programAccent;

                if (already) return;

                _programAccentTarget = ProgramGraduationGold;
                if (!MotionFx.AllowTransitions)
                {
                    _programAccent.BeginAnimation(SolidColorBrush.ColorProperty, null);
                    _programAccent.Color = ProgramGraduationGold;
                    return;
                }

                var gold = new ColorAnimation(ProgramGraduationGold,
                                              TimeSpan.FromSeconds(ProgramAccentFadeSeconds))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(gold, AmbientFrameRate);
                _programAccent.BeginAnimation(SolidColorBrush.ColorProperty, gold);

                FireBurstAt(tab.ProgramsGraduatedCard, FxBurstSpot.Center,
                            ProgramGraduationGold, ProgramHeat.BurstCount(1.0));
                App.Logger?.Information("[FX] program graduation celebrated");
            }
            catch (Exception ex) { App.Logger?.Debug("CelebrateProgramGraduation: {E}", ex.Message); }
        }

        // =================================================================================
        //  colour helpers
        // =================================================================================

        private static Color WithAccentAlpha(Color accent, byte alpha) =>
            Color.FromArgb(alpha, accent.R, accent.G, accent.B);

        /// <summary>Pulls a colour toward white by a 0-1 amount, keeping it fully opaque.</summary>
        private static Color LightenToward(Color accent, double amount)
        {
            var t = Math.Clamp(amount, 0.0, 1.0);
            return Color.FromRgb(
                (byte)Math.Round(accent.R + (255 - accent.R) * t),
                (byte)Math.Round(accent.G + (255 - accent.G) * t),
                (byte)Math.Round(accent.B + (255 - accent.B) * t));
        }
    }
}
