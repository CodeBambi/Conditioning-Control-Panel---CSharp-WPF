using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// PR-2 of the FX overhaul: the *Dashboard* pass. Everything in here decorates the mosaic tab
    /// (Views/Tabs/SettingsTabView.xaml) and nothing in here touches the shell - the chrome is
    /// PR-1's and lives in MainWindow.ChromeFx.cs.
    ///
    /// Six regions, and exactly ONE of them owns a continuous canvas:
    ///   1. behind the mosaic  - a single <see cref="AmbientFxCanvas"/> (FogDrift + sparse
    ///      DustField). The only looping ambient layer on the tab.
    ///   2. the 12 FeatureCards - hover lift + rim-light, breathing ring/glow when active. Those
    ///      clocks live in Features/FeatureCard.xaml.cs; this file only re-evaluates them.
    ///   3. the centre logo    - Ken Burns drift + an occasional sheen pass.
    ///   4. the premium rail   - hover lift + art nudge; status dots pulse only while live.
    ///   5. the browser card   - frame/header only. WebView2 is a native HWND: airspace means
    ///      nothing WPF draws can ever cover it, so no FX goes over the web surface.
    ///   6. the Program "Today" card - its hand-rolled fog/sheen/sparkles stay in
    ///      MainWindow.ProgramBanner.cs (see the note on StartDashboardProgramBannerFx below).
    ///
    /// House rules (FX plan section 2) this file obeys:
    ///   * two clocks - interaction 80-400ms, ambient 3.5-40s. Nothing in between.
    ///   * every ambient clock asks <see cref="ChromeAmbientAllowed"/> (window focus + the
    ///     reduced-motion setting + the performance tier) BEFORE it starts and parks at a static
    ///     resting state otherwise;
    ///   * colour only ever from the Fx* dynamic resources / <see cref="FxTheme"/>;
    ///   * <c>element.BeginAnimation</c> only - Storyboard.SetTargetName silently no-ops across
    ///     the SettingsTabView namescope;
    ///   * every callback wrapped. Decoration may never be why the dashboard throws.
    /// </summary>
    public partial class MainWindow
    {
        // ---- tuning ----------------------------------------------------------------

        /// <summary>Fog/dust alpha multiplier. Low: this is air in the room, not weather.</summary>
        private const double MosaicFxIntensity = 0.62;
        private const int MosaicFogPuffs = 3;

        private const double LogoDriftScaleTo = 1.04;
        private const double LogoDriftSeconds = 40;
        /// <summary>A 40s scale ramp moves ~0.001 of a unit per frame; 10fps is invisible from 24
        /// and costs less than half as much.</summary>
        private const int LogoDriftFrameRate = 10;
        private const double LogoSheenSeconds = 1.1;
        private const int LogoSheenMinGapSeconds = 25;
        private const int LogoSheenMaxGapSeconds = 40;

        private const double RailHoverArtNudge = -0.03;   // brush-relative: ~1.3px on a 42px chip
        private const int RailHoverMs = 150;

        private const double DotPulseMinOpacity = 0.45;
        private const double DotPulseSeconds = 3.4;

        /// <summary>Seconds for one pass of the browser frame's travelling light, and the full
        /// cycle including its rest. The long flat tail IS the pause - one clock, nothing to
        /// schedule.</summary>
        private const double BrowserFrameSweepSeconds = 2.4;
        private const double BrowserFrameCycleSeconds = 14.0;
        private const double BrowserStatusPulseSeconds = 1.6;

        // ---- state -----------------------------------------------------------------

        private bool _dashboardFxInitialized;
        private DispatcherTimer? _logoSheenTimer;
        private readonly Random _dashboardFxRng = new();
        private readonly List<Ellipse> _pulsingRailDots = new();
        private TextBlock? _browserStatusWatched;

        /// <summary>
        /// The mosaic tiles. Null-tolerant - the view is a partial rewire.
        ///
        /// <para>Phase 3: <c>CardBrainDrain</c> joined (the G2 rescue's front door), and
        /// <c>CardSystem</c> was kept in the tree Collapsed. Phase 8 deleted that tile - "System"
        /// is not a feature and its entry point is the quick-toggles row's pill - so the array is
        /// twelve real tiles now, all visible.</para>
        /// </summary>
        private IEnumerable<FeatureCard> DashboardFeatureCards
        {
            get
            {
                var tab = SettingsTab;
                if (tab == null) return Enumerable.Empty<FeatureCard>();
                // The 4x4 hybrid wall (2026-08-11, redesign #2). Every SINGLE tile on the mosaic
                // belongs here or it silently keeps its motion running after a MotionLevel
                // change - the same omission family as ChipFyp missing from PremiumRailItems.
                // The three diagonal tiles are DashboardSplitCards, just below.
                var all = new[]
                {
                    tab.CardFlash, tab.CardSubliminal, tab.CardBouncingText,
                    tab.CardBubblePop, tab.CardLockCard,
                    tab.CardJustDrop, tab.CardMystery, tab.CardVault,
                };
                return all.Where(c => c != null)!;
            }
        }

        /// <summary>The three diagonal tiles - same motion contract as the singles above.</summary>
        private IEnumerable<SplitFeatureCard> DashboardSplitCards
        {
            get
            {
                var tab = SettingsTab;
                if (tab == null) return Enumerable.Empty<SplitFeatureCard>();
                var all = new[] { tab.ComboVideoBubble, tab.ComboSpiralPink, tab.ComboMindDrain };
                return all.Where(c => c != null)!;
            }
        }

        /// <summary>
        /// The premium rail's 9 hoverable items (7 chips + the 2 launcher cards).
        ///
        /// <para>ChipFyp was missing here until the Phase-3 verify pass: For You got no hover lift
        /// and no art nudge, the same omission family as the ArtFyp-missing-from-railArtMap bug
        /// Phase 0 fixed. Every element that carries an Art* brush belongs in this list.</para>
        /// </summary>
        private IEnumerable<FrameworkElement> PremiumRailItems
        {
            get
            {
                var tab = SettingsTab;
                if (tab == null) return Enumerable.Empty<FrameworkElement>();
                var all = new FrameworkElement?[]
                {
                    tab.ChipTakeover, tab.ChipAwareness, tab.ChipHaptics, tab.ChipGradedIntake,
                    tab.ChipVoice, tab.ChipFyp, tab.CardLockdown, tab.CardBlink, tab.ChipRemote,
                };
                return all.Where(c => c != null)!;
            }
        }

        // ============================== lifecycle ==============================

        /// <summary>
        /// Wires the dashboard FX up once, after the window is loaded (so every templated chip and
        /// tile is real before we touch it). Safe to call twice.
        /// </summary>
        internal void InitializeDashboardFx()
        {
            if (_dashboardFxInitialized) return;
            _dashboardFxInitialized = true;
            try
            {
                var tab = SettingsTab;
                if (tab == null) return;

                // 1. The one ambient canvas, registered against the dashboard's tab key so
                //    ShowTab parks it on the way out and resumes it on the way in for free.
                if (tab.MosaicFx != null)
                {
                    tab.MosaicFx.StartLayers(new AmbientFxConfig
                    {
                        Layers = AmbientFxLayers.FogDrift | AmbientFxLayers.DustField,
                        Intensity = MosaicFxIntensity,
                        FogPuffs = MosaicFogPuffs,
                    });
                    RegisterTabFx("settings", tab.MosaicFx);
                }

                // 3. Centre logo: the drift follows the wordmark FACE, so the intake flip
                //    collapsing it stops the clock instead of animating something invisible.
                if (tab.LogoFaceLogo != null)
                    tab.LogoFaceLogo.IsVisibleChanged += LogoFace_IsVisibleChanged;
                _logoSheenTimer = new DispatcherTimer();
                _logoSheenTimer.Tick += (_, __) => SweepLogoSheen();

                // 4. Premium rail hover. The chip's ART is its Background brush, so the nudge is a
                //    RelativeTransform on a private CLONE of it - the shared resource brush is
                //    left alone (and may well be frozen).
                foreach (var item in PremiumRailItems)
                {
                    PrepareRailArtNudge(item);
                    item.MouseEnter += RailItem_MouseEnter;
                    item.MouseLeave += RailItem_MouseLeave;
                }

                // 5. Browser card: watch the status text rather than editing the ~10 places that
                //    write it. One hook, and it cannot get out of step with them.
                HookBrowserStatusWatcher(tab.TxtBrowserStatus);

                ApplyDashboardFxLoops();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "InitializeDashboardFx failed"); }
        }

        /// <summary>
        /// Re-evaluates every dashboard loop. Called from <see cref="ApplyChromeFxLoops"/>, which
        /// is itself the single funnel for window activation, the mod switch (RefreshChromeFx) and
        /// the reduced-motion picker - so the dashboard follows all three without its own hooks.
        /// </summary>
        private void ApplyDashboardFxLoops()
        {
            if (!_dashboardFxInitialized) return;
            try
            {
                var tab = SettingsTab;
                if (tab == null) return;

                // The canvas parks itself on deactivate/minimise/tab-hide. This call is for the
                // other direction: coming back UP from Reduced/Off, nothing else would poke it.
                if (tab.MosaicFx != null &&
                    string.Equals(_activeTabKey, "settings", StringComparison.OrdinalIgnoreCase))
                    tab.MosaicFx.Resume();

                foreach (var card in DashboardFeatureCards) card.RefreshFx();
                foreach (var combo in DashboardSplitCards) combo.RefreshFx();
                ApplyVaultCtaBreath();
                ApplyMysteryFx();

                ApplyLogoDrift();
                ApplyLogoSheenTimer();
                ApplyBrowserFrameSweep();
                ApplyBrowserStatusPulse();
                foreach (var dot in _pulsingRailDots.ToList()) ApplyRailDotPulse(dot, true, force: true);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyDashboardFxLoops: {E}", ex.Message); }
        }

        // ============================== 2b. the vault CTA ==============================

        private const double VaultCtaBreathTo = 1.07;
        private const double VaultCtaBreathSeconds = 2.2;

        /// <summary>
        /// The vault tile's "Check out all the other premium features!" stamp (owner spec):
        /// a slow scale breath on the slanted text. Driven here and not by a XAML Storyboard for
        /// the same namescope reason as the intake CTA pulse; parks at 1.0 when ambient motion is
        /// off, so reduced-motion users get a static slanted stamp - which still reads.
        /// </summary>
        private void ApplyVaultCtaBreath()
        {
            try
            {
                var scale = SettingsTab?.VaultCtaScale;
                if (scale == null || scale.IsFrozen) return;

                if (!ChromeAmbientAllowed)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    scale.ScaleX = scale.ScaleY = 1.0;
                    return;
                }

                var breath = new DoubleAnimation(1.0, VaultCtaBreathTo, TimeSpan.FromSeconds(VaultCtaBreathSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(breath, AmbientFrameRate);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, breath);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, breath);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyVaultCtaBreath: {E}", ex.Message); }
        }

        // ============================== 2c. the ? box ==============================
        //
        // Two owner specs (2026-08-11 #3) live here:
        //
        //  * THE POP - the gold badge + the gold ring around the tile breathe together for the
        //    first few seconds of the day's first look at the Dashboard, then rest at their
        //    static XAML values. Finite by construction (RepeatBehavior counts, FillBehavior
        //    .Stop), so there is nothing to park later - reduced-motion/perf users simply keep
        //    the static gold, which still pops against a wall that only glows in Fx colours.
        //
        //  * THE REVEAL - once a day, the first time the Dashboard is seen, the tile turns
        //    (the intake plate's ScaleX fake-spin, same half-turn tempo) to a full-art card of
        //    today's feature, holds, and turns back. The date latch (AppSettings
        //    .DailyGiftLastRevealDate) is written when the reveal face LANDS - the "they saw
        //    it" moment - so a ceremony cancelled mid-spin is owed again, not lost. Reduced
        //    motion latches without turning: the box's title already names the feature.
        //
        // Same house rules as the intake plate: BeginAnimation only (Storyboards no-op across
        // the SettingsTabView namescope), a monotonic generation token orphans every stale
        // Completed callback, and the hold is a 1->1 animation on the same property the spin
        // uses so ONE teardown path covers both.

        private const double MysteryGlowRestOpacity = 0.55;   // must match MysteryGlow's XAML Opacity
        private const double MysteryPopGlowMax = 0.95;
        private const double MysteryPopBadgeTo = 1.12;
        private static readonly TimeSpan MysteryPopHalfCycle = TimeSpan.FromMilliseconds(900);
        private const int MysteryPopCycles = 4;               // 4 in-and-outs ~= 7s of "look here"

        private const int MysteryOpeningDwellMs = 1200;
        private const int MysteryRevealHoldMs = 6000;
        /// <summary>Same widening tempo as the intake plate; the COUNT MUST STAY ODD so each
        /// spin lands on the opposite face from the one it started on.</summary>
        private static readonly int[] MysteryHalfTurnMs = { 105, 115, 130, 150, 560 };
        private const double MysterySkewDeg = 6.0;

        private int _mysterySpinGen;
        private bool _mysteryCeremonyRunning;
        private bool _mysteryShowingReveal;
        /// <summary>Local date the pop last played - in-memory on purpose, so each day's first
        /// look per app session gets one, and a restart on the same day gets one more.</summary>
        private string? _mysteryPopDate;
        private bool _mysteryVisHooked;
        private bool _mysteryPopPlaying;

        /// <summary>The MotionLevel/focus funnel's view of the ? box. The pop and the spin are
        /// finite and event-driven, so this only ever tears down (and retries the reveal when
        /// conditions came back).</summary>
        private void ApplyMysteryFx()
        {
            try
            {
                if (!ChromeAmbientAllowed) StopMysteryPop();
                if (!MotionFx.AllowTransitions) CancelMysteryReveal();
                else MaybeRunMysteryReveal();
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyMysteryFx: {E}", ex.Message); }
        }

        /// <summary>
        /// The daily ceremony's front door. Hammer-safe: called from RefreshMysteryTile (every
        /// rail repaint), from the FX funnel above, and from the tile becoming visible - the
        /// date latch, the running flag and the pop date make every extra call a no-op.
        /// </summary>
        internal void MaybeRunMysteryReveal()
        {
            try
            {
                if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted)
                    return;

                var tab = SettingsTab;
                if (tab?.MysteryFlipHost == null || tab.MysteryFlipScale == null || tab.MysteryFlipSkew == null
                    || tab.CardMystery == null || tab.MysteryRevealFace == null)
                    return;

                // A hidden plate must not keep turning (and must not spend the day's ceremony
                // off screen). Cancelling before the reveal landed leaves the date unlatched,
                // so the turn is owed again on the next visit.
                if (!_mysteryVisHooked)
                {
                    _mysteryVisHooked = true;
                    tab.IsVisibleChanged += (_, args) =>
                    {
                        if (args.NewValue is bool visible && !visible)
                        {
                            CancelMysteryReveal();
                            StopMysteryPop();
                        }
                    };
                }

                if (!tab.IsLoaded || !tab.IsVisible) return;

                var today = DateTime.Now.ToString("yyyy-MM-dd");

                // The pop rides along with the day's first look whether or not the reveal is
                // still owed - it is the box's doorbell, not the ceremony's.
                if (_mysteryPopDate != today && ChromeAmbientAllowed)
                {
                    _mysteryPopDate = today;
                    StartMysteryPop();
                }

                var settings = App.Settings?.Current;
                if (settings == null || settings.DailyGiftLastRevealDate == today) return;

                if (!MotionFx.AllowTransitions)
                {
                    // Reduced motion: no turn, and no debt - the tile's title already names
                    // today's feature, so nothing was withheld.
                    settings.DailyGiftLastRevealDate = today;
                    App.Settings?.Save();
                    return;
                }

                if (_mysteryCeremonyRunning) return;
                _mysteryCeremonyRunning = true;
                _mysterySpinGen++;
                var gen = _mysterySpinGen;

                SetMysteryFace(showReveal: false);

                // Dwell on the ? so the turn is an event, then: turn, latch, hold, turn back.
                RunMysteryHold(gen, MysteryOpeningDwellMs, () =>
                    RunMysterySpinPhase(gen, 0, () =>
                    {
                        try
                        {
                            var s = App.Settings?.Current;
                            if (s != null)
                            {
                                s.DailyGiftLastRevealDate = DateTime.Now.ToString("yyyy-MM-dd");
                                App.Settings?.Save();
                            }
                        }
                        catch (Exception ex) { App.Logger?.Debug("Mystery reveal latch: {E}", ex.Message); }

                        RunMysteryHold(gen, MysteryRevealHoldMs, () =>
                            RunMysterySpinPhase(gen, 0, () =>
                            {
                                if (gen != _mysterySpinGen) return;
                                _mysteryCeremonyRunning = false;   // landed back on the ?
                            }));
                    }));
            }
            catch (Exception ex) { App.Logger?.Debug("MaybeRunMysteryReveal: {E}", ex.Message); }
        }

        /// <summary>Which face of the plate is up. The spin toggles off the tracked bool, so the
        /// same phase chain drives both directions - intake plate rules.</summary>
        private void SetMysteryFace(bool showReveal)
        {
            var tab = SettingsTab;
            if (tab?.CardMystery == null || tab.MysteryRevealFace == null) return;
            tab.MysteryRevealFace.Visibility = showReveal ? Visibility.Visible : Visibility.Collapsed;
            tab.CardMystery.Visibility = showReveal ? Visibility.Collapsed : Visibility.Visible;
            _mysteryShowingReveal = showReveal;
        }

        /// <summary>A 1-&gt;1 no-op animation as the dwell clock, on the SAME property the spin
        /// animates - so CancelMysteryReveal's one BeginAnimation(null) tears off whichever of
        /// the two is live. A DispatcherTimer would need its own teardown path.</summary>
        private void RunMysteryHold(int gen, int ms, Action then)
        {
            if (gen != _mysterySpinGen) return;
            var scale = SettingsTab?.MysteryFlipScale;
            if (scale == null) { _mysteryCeremonyRunning = false; return; }

            var hold = new DoubleAnimation(1.0, 1.0, TimeSpan.FromMilliseconds(ms));
            hold.Completed += (_, _) =>
            {
                if (gen != _mysterySpinGen) return;
                if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted) return;
                then();
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, hold);
        }

        /// <summary>
        /// One ScaleX ramp of a turn; chains itself through Completed. Even phases close
        /// (1-&gt;0), odd phases open (0-&gt;1), the face swaps at each zero crossing, and the odd
        /// half-turn count guarantees the spin lands on the opposite face. When the last
        /// half-turn settles the transform is torn down and <paramref name="done"/> runs.
        /// </summary>
        private void RunMysterySpinPhase(int gen, int phase, Action done)
        {
            if (gen != _mysterySpinGen) return;
            if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted) return;

            var tab = SettingsTab;
            if (tab?.MysteryFlipScale == null || tab.MysteryFlipSkew == null) { _mysteryCeremonyRunning = false; return; }

            var halfTurn = phase / 2;
            var closing = (phase % 2) == 0;

            if (halfTurn >= MysteryHalfTurnMs.Length)
            {
                // Settle: a held animation pins the render target, so base values only stick
                // after BeginAnimation(prop, null).
                tab.MysteryFlipScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                tab.MysteryFlipSkew.BeginAnimation(SkewTransform.AngleYProperty, null);
                tab.MysteryFlipScale.ScaleX = 1;
                tab.MysteryFlipSkew.AngleY = 0;
                done();
                return;
            }

            var ms = Math.Max(1, MysteryHalfTurnMs[halfTurn] / 2);
            var duration = TimeSpan.FromMilliseconds(ms);

            var scaleAnim = new DoubleAnimation(closing ? 1.0 : 0.0, closing ? 0.0 : 1.0, duration);
            var skewAnim = closing
                ? new DoubleAnimation(0.0, MysterySkewDeg, duration)
                : new DoubleAnimation(-MysterySkewDeg, 0.0, duration);

            // Ease only the final slow half-turn - the quick ones are over before the eye can
            // resolve an easing curve, and easing them reads as stalling (intake plate note).
            if (halfTurn >= MysteryHalfTurnMs.Length - 1)
            {
                var ease = new CubicEase { EasingMode = closing ? EasingMode.EaseIn : EasingMode.EaseOut };
                scaleAnim.EasingFunction = ease;
                skewAnim.EasingFunction = ease;
            }

            scaleAnim.Completed += (_, _) =>
            {
                if (gen != _mysterySpinGen) return;
                if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted) return;
                if (closing) SetMysteryFace(!_mysteryShowingReveal);
                RunMysterySpinPhase(gen, phase + 1, done);
            };

            tab.MysteryFlipScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            tab.MysteryFlipSkew.BeginAnimation(SkewTransform.AngleYProperty, skewAnim);
        }

        /// <summary>Kills an in-flight ceremony and puts the plate back on the ? face at rest.
        /// Does NOT latch the date - an unseen reveal is owed, not spent.</summary>
        private void CancelMysteryReveal()
        {
            var tab = SettingsTab;
            _mysterySpinGen++;                    // orphans every pending Completed callback
            _mysteryCeremonyRunning = false;

            if (tab?.MysteryFlipScale == null || tab.MysteryFlipSkew == null) return;
            tab.MysteryFlipScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            tab.MysteryFlipSkew.BeginAnimation(SkewTransform.AngleYProperty, null);
            tab.MysteryFlipScale.ScaleX = 1;
            tab.MysteryFlipSkew.AngleY = 0;
            SetMysteryFace(showReveal: false);
        }

        /// <summary>
        /// The finite gold breath: ring opacity and badge scale swell together, four times, then
        /// FillBehavior.Stop snaps both back to their XAML resting values with no teardown owed.
        /// The badge's two scale axes share ONE clock - two BeginAnimation calls would mint two
        /// clocks that drift, and on text that reads as shearing (intake CTA lesson).
        /// </summary>
        private void StartMysteryPop()
        {
            try
            {
                var tab = SettingsTab;
                if (tab?.MysteryGlow == null || tab.MysteryBadgeScale == null || tab.MysteryBadgeScale.IsFrozen) return;
                if (_mysteryPopPlaying) return;
                _mysteryPopPlaying = true;

                var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
                var repeats = new RepeatBehavior(MysteryPopCycles);

                var glow = new DoubleAnimation(MysteryGlowRestOpacity, MysteryPopGlowMax, MysteryPopHalfCycle)
                {
                    AutoReverse = true,
                    RepeatBehavior = repeats,
                    FillBehavior = FillBehavior.Stop,
                    EasingFunction = ease,
                };
                glow.Completed += (_, _) => _mysteryPopPlaying = false;

                var swell = new DoubleAnimation(1.0, MysteryPopBadgeTo, MysteryPopHalfCycle)
                {
                    AutoReverse = true,
                    RepeatBehavior = repeats,
                    FillBehavior = FillBehavior.Stop,
                    EasingFunction = ease,
                };

                var swellClock = swell.CreateClock();
                tab.MysteryBadgeScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, swellClock);
                tab.MysteryBadgeScale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, swellClock);
                tab.MysteryGlow.BeginAnimation(UIElement.OpacityProperty, glow);
            }
            catch (Exception ex) { App.Logger?.Debug("StartMysteryPop: {E}", ex.Message); }
        }

        /// <summary>Idempotent teardown to the static gold. Matches how each animation was
        /// attached (clock vs BeginAnimation - the two detach paths are not interchangeable).</summary>
        private void StopMysteryPop()
        {
            _mysteryPopPlaying = false;
            var tab = SettingsTab;
            if (tab?.MysteryGlow == null || tab.MysteryBadgeScale == null || tab.MysteryBadgeScale.IsFrozen) return;
            try
            {
                tab.MysteryBadgeScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, null);
                tab.MysteryBadgeScale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, null);
                tab.MysteryGlow.BeginAnimation(UIElement.OpacityProperty, null);
                tab.MysteryBadgeScale.ScaleX = tab.MysteryBadgeScale.ScaleY = 1;
                tab.MysteryGlow.Opacity = MysteryGlowRestOpacity;
            }
            catch (Exception ex) { App.Logger?.Debug("StopMysteryPop: {E}", ex.Message); }
        }

        // ============================== 3. centre logo ==============================

        private void LogoFace_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                ApplyLogoDrift();
                ApplyLogoSheenTimer();
            }
            catch (Exception ex) { App.Logger?.Debug("LogoFace_IsVisibleChanged: {E}", ex.Message); }
        }

        /// <summary>
        /// Ken Burns idle drift on the wordmark: 1.00 to 1.04 and back over 40s. RenderTransform
        /// only, so it never takes layout with it, and the tile's 6px margin is the headroom the
        /// 4% growth paints into. The transform is LogoFaceLogo's own - see the long note in the
        /// view about why it cannot be ImgLogo's or LogoFlipHost's.
        /// </summary>
        private void ApplyLogoDrift()
        {
            try
            {
                var tab = SettingsTab;
                var scale = tab?.LogoDriftScale;
                if (scale == null || scale.IsFrozen) return;

                bool wanted = ChromeAmbientAllowed && tab!.LogoFaceLogo?.IsVisible == true;
                if (!wanted)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    scale.ScaleX = scale.ScaleY = 1.0;
                    return;
                }

                var drift = new DoubleAnimation(1.0, LogoDriftScaleTo, TimeSpan.FromSeconds(LogoDriftSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(drift, LogoDriftFrameRate);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, drift);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, drift);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyLogoDrift: {E}", ex.Message); }
        }

        private void ApplyLogoSheenTimer()
        {
            try
            {
                if (_logoSheenTimer == null) return;
                var tab = SettingsTab;
                bool wanted = ChromeAmbientAllowed && tab?.LogoFaceLogo?.IsVisible == true;
                if (!wanted)
                {
                    _logoSheenTimer.Stop();
                    ParkSheen(tab?.LogoSheen, tab?.LogoSheenSlide);
                    return;
                }
                if (_logoSheenTimer.IsEnabled) return;
                _logoSheenTimer.Interval = NextLogoSheenGap();
                _logoSheenTimer.Start();
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyLogoSheenTimer: {E}", ex.Message); }
        }

        /// <summary>25-40s apart, re-rolled each pass: a fixed gap reads as a metronome.</summary>
        private TimeSpan NextLogoSheenGap() =>
            TimeSpan.FromSeconds(_dashboardFxRng.Next(LogoSheenMinGapSeconds, LogoSheenMaxGapSeconds + 1));

        private void SweepLogoSheen()
        {
            try
            {
                if (_logoSheenTimer != null) _logoSheenTimer.Interval = NextLogoSheenGap();
                var tab = SettingsTab;
                if (tab?.LogoFaceLogo?.IsVisible != true) return;
                if (!ChromeAmbientAllowed) return;
                SweepSheen(tab.LogoFaceLogo, tab.LogoSheen, tab.LogoSheenSlide, LogoSheenSeconds, 0.26);
            }
            catch (Exception ex) { App.Logger?.Debug("SweepLogoSheen: {E}", ex.Message); }
        }

        // ============================== 4. premium rail ==============================

        /// <summary>
        /// Source resource brush -> the private clone a rail item actually paints with. Populated
        /// by <see cref="PrepareRailArtNudge"/>; drained by <see cref="RefreshRailArtClones"/>.
        /// See that method for why the pair has to be remembered at all.
        /// </summary>
        private readonly List<(ImageBrush Source, ImageBrush Clone)> _railArtClones = new();

        /// <summary>
        /// Gives a rail item its own copy of its art brush with a translate transform on it. The
        /// brushes are shared XAML resources and may be frozen, so we clone once at init rather
        /// than reach into the resource (which would also nudge every other user of it).
        /// </summary>
        private void PrepareRailArtNudge(FrameworkElement item)
        {
            try
            {
                var brush = item switch
                {
                    Control c => c.Background,
                    Border b => b.Background,
                    _ => null,
                };
                if (brush == null || brush.RelativeTransform is TranslateTransform) return;
                if (brush.RelativeTransform != null && !brush.RelativeTransform.Value.IsIdentity) return;

                var copy = brush.Clone();
                copy.RelativeTransform = new TranslateTransform();
                // Cloning severs the {StaticResource} link, which is exactly what railArtMap
                // (MainWindow.xaml.cs, LoadFeatureImages) mutates on a mod switch. Remember the
                // pair so the clone can be re-synced; see RefreshRailArtClones.
                if (brush is ImageBrush src && copy is ImageBrush dst)
                    _railArtClones.Add((src, dst));
                switch (item)
                {
                    case Control c: c.Background = copy; break;
                    case Border b: b.Background = copy; break;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("PrepareRailArtNudge: {E}", ex.Message); }
        }

        /// <summary>
        /// Re-points every rail chip's private art clone at whatever the shared resource brush is
        /// showing now. Called at the end of <c>LoadFeatureImages()</c>.
        ///
        /// <para>Why this exists: the rail's mod-awareness is <b>brush mutation, not brush
        /// reassignment</b> - <c>railArtMap</c> writes <c>ImageSource</c> into the eight
        /// <c>Art*</c> resources and relies on every chip's <c>{StaticResource}</c> reference to
        /// repaint from that one write. <see cref="PrepareRailArtNudge"/> hands each chip a
        /// <c>Clone()</c> so the hover nudge does not shove every other user of the brush, and a
        /// clone does not observe the source's later edits. At startup the order hides it
        /// (<c>LoadFeatureImages()</c> runs in the ctor, the clones are made on Loaded, so they
        /// capture the correct art), but a RUNTIME mod switch repainted the resources only and the
        /// chips kept the previous mod's art until restart. Pre-existing; found by the Phase-3
        /// verify pass, which has "mod switch repaints the rail" as an exit criterion.</para>
        /// </summary>
        private void RefreshRailArtClones()
        {
            try
            {
                foreach (var (source, clone) in _railArtClones)
                {
                    if (clone.IsFrozen) continue;
                    if (!ReferenceEquals(clone.ImageSource, source.ImageSource))
                        clone.ImageSource = source.ImageSource;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("RefreshRailArtClones: {E}", ex.Message); }
        }

        private void RailItem_MouseEnter(object sender, MouseEventArgs e) => ApplyRailHover(sender, true);

        private void RailItem_MouseLeave(object sender, MouseEventArgs e) => ApplyRailHover(sender, false);

        /// <summary>Hover on a rail item: the 1.02 lift plus a ~1px push on the art behind it.
        /// The chips sit in a ScrollViewer that clips, so the lift is deliberately the shared
        /// 1.02 and not the tiles' old 1.03 - the rail only has 4px of margin to grow into.</summary>
        private void ApplyRailHover(object sender, bool on)
        {
            try
            {
                if (sender is not FrameworkElement item) return;
                MotionFx.HoverLift(item, on);

                var brush = item switch
                {
                    Control c => c.Background,
                    Border b => b.Background,
                    _ => null,
                };
                if (brush?.RelativeTransform is not TranslateTransform nudge || nudge.IsFrozen) return;

                double to = on ? RailHoverArtNudge : 0;
                if (!MotionFx.AllowTransitions)
                {
                    nudge.BeginAnimation(TranslateTransform.YProperty, null);
                    nudge.Y = to;
                    return;
                }
                var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(RailHoverMs))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                nudge.BeginAnimation(TranslateTransform.YProperty, anim);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyRailHover: {E}", ex.Message); }
        }

        /// <summary>
        /// Status-dot pulse. Event-driven on purpose: <see cref="RefreshPremiumRail"/> calls this
        /// through SetDot whenever a feature's state actually changes, so an all-off rail holds no
        /// clocks at all. A dot that is off never breathes - a pulse on a dead indicator is a lie
        /// told with animation.
        /// </summary>
        private void ApplyRailDotPulse(Ellipse? dot, bool on, bool force = false)
        {
            if (dot == null) return;
            try
            {
                bool known = _pulsingRailDots.Contains(dot);
                // RefreshPremiumRail repaints all five dots on every state change; re-arming a
                // Forever breath each time would visibly reset its phase.
                if (on && known && !force) return;
                if (on && !known) _pulsingRailDots.Add(dot);
                else if (!on) _pulsingRailDots.Remove(dot);

                if (!on || !ChromeAmbientAllowed)
                {
                    dot.BeginAnimation(UIElement.OpacityProperty, null);
                    dot.Opacity = 1;
                    return;
                }
                var pulse = new DoubleAnimation(DotPulseMinOpacity, 1.0, TimeSpan.FromSeconds(DotPulseSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(pulse, AmbientFrameRate);
                dot.BeginAnimation(UIElement.OpacityProperty, pulse);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyRailDotPulse: {E}", ex.Message); }
        }

        // ============================== 5. browser card ==============================

        /// <summary>
        /// A slow light travelling around the card frame: three gradient stops on one 14s clock,
        /// 2.4s of travel and the rest at rest. At rest the stops sit entirely before the brush's
        /// 0 mark, which pads to a flat GlassBorder - the border that shipped.
        /// </summary>
        private void ApplyBrowserFrameSweep()
        {
            try
            {
                var tab = SettingsTab;
                if (tab?.BrowserFrameStopLead == null || tab.BrowserFrameStopCore == null
                    || tab.BrowserFrameStopTail == null) return;

                var stops = new[] { tab.BrowserFrameStopLead, tab.BrowserFrameStopCore, tab.BrowserFrameStopTail };
                double[] rest = { -0.30, -0.15, 0.0 };

                if (!ChromeAmbientAllowed)
                {
                    for (int i = 0; i < stops.Length; i++)
                    {
                        if (stops[i].IsFrozen) continue;
                        stops[i].BeginAnimation(GradientStop.OffsetProperty, null);
                        stops[i].Offset = rest[i];
                    }
                    return;
                }

                for (int i = 0; i < stops.Length; i++)
                {
                    if (stops[i].IsFrozen) continue;
                    double from = rest[i];
                    double to = from + 1.35;
                    var sweep = new DoubleAnimationUsingKeyFrames
                    {
                        Duration = TimeSpan.FromSeconds(BrowserFrameCycleSeconds),
                        RepeatBehavior = RepeatBehavior.Forever,
                    };
                    sweep.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    sweep.KeyFrames.Add(new LinearDoubleKeyFrame(to,
                        KeyTime.FromTimeSpan(TimeSpan.FromSeconds(BrowserFrameSweepSeconds))));
                    sweep.KeyFrames.Add(new DiscreteDoubleKeyFrame(from,
                        KeyTime.FromTimeSpan(TimeSpan.FromSeconds(BrowserFrameSweepSeconds + 0.05))));
                    sweep.KeyFrames.Add(new LinearDoubleKeyFrame(from,
                        KeyTime.FromTimeSpan(TimeSpan.FromSeconds(BrowserFrameCycleSeconds))));
                    Timeline.SetDesiredFrameRate(sweep, AmbientFrameRate);
                    stops[i].BeginAnimation(GradientStop.OffsetProperty, sweep);
                }
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyBrowserFrameSweep: {E}", ex.Message); }
        }

        /// <summary>
        /// Watches the browser status text so the "loading" pulse follows it without editing the
        /// ~10 sites in MainWindow.Browser.cs that write it. The descriptor keeps a strong
        /// reference to the handler - harmless here, the TextBlock is a dashboard element that
        /// lives exactly as long as the window does.
        /// </summary>
        private void HookBrowserStatusWatcher(TextBlock? status)
        {
            if (status == null || ReferenceEquals(status, _browserStatusWatched)) return;
            try
            {
                var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
                descriptor?.AddValueChanged(status, BrowserStatus_TextChanged);
                _browserStatusWatched = status;
            }
            catch (Exception ex) { App.Logger?.Debug("HookBrowserStatusWatcher: {E}", ex.Message); }
        }

        private void BrowserStatus_TextChanged(object? sender, EventArgs e) => ApplyBrowserStatusPulse();

        /// <summary>
        /// A gentle pulse on the status badge while the browser is still coming up, and nothing at
        /// all once it is connected (or failed - a pulsing error reads as "retrying", which it is
        /// not). Interaction-length rather than a breath: this is a progress cue, not ambience.
        /// </summary>
        private void ApplyBrowserStatusPulse()
        {
            try
            {
                var status = SettingsTab?.TxtBrowserStatus;
                if (status == null) return;

                bool loading = string.Equals(status.Text?.Trim(),
                                             Loc.Get("label_loading")?.Trim(),
                                             StringComparison.OrdinalIgnoreCase);
                if (!loading || !ChromeAmbientAllowed || !MotionFx.AllowTransitions)
                {
                    status.BeginAnimation(UIElement.OpacityProperty, null);
                    status.Opacity = 1;
                    return;
                }
                var pulse = new DoubleAnimation(0.35, 1.0, TimeSpan.FromSeconds(BrowserStatusPulseSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(pulse, AmbientFrameRate);
                status.BeginAnimation(UIElement.OpacityProperty, pulse);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyBrowserStatusPulse: {E}", ex.Message); }
        }
    }
}
