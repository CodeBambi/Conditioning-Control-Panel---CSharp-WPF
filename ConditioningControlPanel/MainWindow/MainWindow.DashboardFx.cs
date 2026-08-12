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

                // 6. The vault CTA's breath follows the tab in and out. The ? box's breath has
                //    its own copy of this hook (EnsureMysteryTileFx) because it is armed from the
                //    rail repaint rather than from here; this one has no other entry point.
                tab.IsVisibleChanged += (_, _) => ApplyVaultCtaBreath();

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
                var tab = SettingsTab;
                var scale = tab?.VaultCtaScale;
                if (scale == null || scale.IsFrozen) return;

                // Same gate as the ? box's breath, and for the same reason: a Forever clock on a
                // collapsed tab burns a composition slot for the rest of the session with nothing
                // on screen to show for it. The tab-visibility hook that re-asks this lives in
                // InitializeDashboardFx.
                if (!ChromeAmbientAllowed || !tab!.IsLoaded || !tab.IsVisible)
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
        // Two owner specs (2026-08-11 #3, revised the same day) live here:
        //
        //  * THE BREATH - the gold badge + the gold ring around the tile breathe together, and
        //    they keep breathing for as long as today's gift is UNOPENED. It is the box's
        //    doorbell, so it rings until somebody answers it: the first HOVER of the day latches
        //    AppSettings.DailyGiftLastRevealDate and stops the breath for good until tomorrow,
        //    when the settings date stops matching and it re-arms by itself. Reduced-motion and
        //    low-perf users never get the loop and keep the static gold, which still pops
        //    against a wall that only glows in Fx colours.
        //
        //  * THE HOVER FLIP - the tile is a two-sided plate. Hovering it turns the plate on its
        //    axis (the intake plate's ScaleX fake-spin: 1 -> 0, swap the visible face at the
        //    zero crossing, 0 -> 1) onto a full-art card of today's feature; unhovering turns it
        //    back to the ?. ONE quick half-turn per direction at interaction tempo, not the old
        //    once-a-day ceremony - the surprise is now a thing you do, so it can neither be
        //    missed by looking away nor spent off screen while the tab is hidden.
        //
        // Rapid enter/leave is the interesting case, and the rule is: never snap, never stick
        // edge-on. Every hover state change bumps the monotonic generation token (which orphans
        // the stale chain's Completed callbacks) and starts the new turn FROM the transform's
        // current animated value, over a duration scaled to the travel that is actually left.
        // The wanted face (_mysteryWantReveal) and the shown face (_mysteryShowingReveal) are
        // tracked separately, so a reversal mid-turn is resolved against both rather than
        // assuming the plate started flat. The other half of "robust to a twitchy pointer" is
        // the PHANTOM LEAVE an edge-on plate provokes out of WPF's hit testing - see the hover
        // hooks in EnsureMysteryTileFx and ReconcileMysteryHover.
        //
        // Same house rules as the intake plate: BeginAnimation only (Storyboard.SetTargetName
        // silently no-ops across the SettingsTabView namescope), every callback wrapped, and the
        // badge's two scale axes share ONE clock so the pill's text cannot shear.

        private const double MysteryGlowRestOpacity = 0.55;   // must match MysteryGlow's XAML Opacity
        private const double MysteryPopGlowMax = 0.95;
        private const double MysteryPopBadgeTo = 1.12;
        private static readonly TimeSpan MysteryPopHalfCycle = TimeSpan.FromMilliseconds(900);

        /// <summary>One half of the hover flip (the close, or the open) at full travel. A turn
        /// that begins part-way through a reversal gets a proportional slice of this instead, so
        /// a flick of the pointer costs a flick of animation. Interaction tempo by the FX plan's
        /// two-clock rule - the plate has to be done before the pointer has moved on.</summary>
        private const int MysteryFlipHalfMs = 150;
        /// <summary>Skew (degrees) at the thin point of the turn. Purely cosmetic fake
        /// perspective - it always animates back to 0 and is torn down with the flip.</summary>
        private const double MysterySkewDeg = 6.0;

        /// <summary>Monotonic flip token. Every chained Completed callback re-checks it, so a
        /// reversal (or a teardown) mid-turn can be certain the stale tail of the old chain will
        /// not repaint a superseded face or leave the plate half-scaled.</summary>
        private int _mysterySpinGen;
        /// <summary>Which face is actually on screen right now.</summary>
        private bool _mysteryShowingReveal;
        /// <summary>Which face the pointer says SHOULD be on screen. It diverges from the line
        /// above for the length of one turn - closing that gap is the whole job of the flip.</summary>
        private bool _mysteryWantReveal;
        /// <summary>One-shot latches for the subscriptions below (the entry point is called on
        /// every rail repaint and every FX re-evaluation, so it has to be safe to hammer).</summary>
        private bool _mysteryVisHooked;
        private bool _mysteryHoverHooked;
        /// <summary>True from the first frame of a turn until the plate is flat again. Only
        /// consulted to spot the PHANTOM MouseLeave the turn causes - see the hover hooks.</summary>
        private bool _mysteryFlipping;
        /// <summary>True while the breath's clocks are attached, so teardown stays idempotent.</summary>
        private bool _mysteryPopPlaying;

        /// <summary>Is today's gift still unopened? The settings date is the ONLY latch now -
        /// there is no in-memory "played this session" flag, so the breath survives a tab bounce
        /// and a restart, and stops the moment the user hovers the box.</summary>
        private bool MysteryGiftUnopened
        {
            get
            {
                var s = App.Settings?.Current;
                return s != null && s.DailyGiftLastRevealDate != DateTime.Now.ToString("yyyy-MM-dd");
            }
        }

        /// <summary>The MotionLevel/focus funnel's view of the ? box: it arms or parks the
        /// breath, and settles an in-flight turn when transitions get switched off underneath
        /// it. The flip itself is pointer-driven, so there is nothing here to start.</summary>
        private void ApplyMysteryFx()
        {
            try
            {
                // Transitions switched off mid-turn: end the turn now, on the face the pointer
                // last asked for - not on the ?, which would read as the tile fighting the mouse.
                if (!MotionFx.AllowTransitions) SettleMysteryPlate(_mysteryWantReveal);

                EnsureMysteryTileFx();   // hooks + the breath's own gate
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyMysteryFx: {E}", ex.Message); }
        }

        /// <summary>The breath's single gate, re-asked on every pass through either entry point.
        /// Start/Stop are both idempotent, so this is a pure function of the four conditions.</summary>
        private void ApplyMysteryBreath()
        {
            var tab = SettingsTab;
            if (tab == null) return;

            ApplyMysteryBadgeVisibility();

            // A forever loop on a hidden tile burns a composition slot for the rest of the
            // session with nothing on screen to show for it (intake CTA lesson), so visibility
            // gates the breath as hard as the motion setting and the date latch do.
            if (!ChromeAmbientAllowed || !tab.IsLoaded || !tab.IsVisible || !MysteryGiftUnopened)
                StopMysteryPop();
            else
                StartMysteryPop();
        }

        /// <summary>
        /// The "NEW TODAY!" badge answers to the SAME date latch the breath does, because it is
        /// the same doorbell said in words: it is on while today's gift is unopened and gone for
        /// the rest of the day the moment the box is hovered, re-arming by itself tomorrow when
        /// the settings date stops matching. A badge that never clears is not a notice, it is
        /// decoration - and this one sat outside the flip host where nothing else could hide it.
        /// Deliberately NOT gated on ambient motion or tab visibility: those decide whether the
        /// badge BREATHES, not whether there is news to announce.
        /// </summary>
        private void ApplyMysteryBadgeVisibility()
        {
            try
            {
                var badge = SettingsTab?.MysteryBadge;
                if (badge == null) return;
                badge.Visibility = MysteryGiftUnopened ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyMysteryBadgeVisibility: {E}", ex.Message); }
        }

        /// <summary>
        /// The tile's front door: installs the two hooks it needs (hover flip, tab-hide
        /// teardown) and re-asks the breath's gate. Hammer-safe - called from RefreshMysteryTile
        /// (every rail repaint), from the FX funnel above and from the tile becoming visible;
        /// the one-shot latches and the idempotent Start/Stop pair make every extra call free.
        /// It arms the breath itself rather than leaving that to the funnel, because the rail
        /// repaint is the one entry point that fires on a plain tab switch.
        /// </summary>
        internal void EnsureMysteryTileFx()
        {
            try
            {
                if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted)
                    return;

                var tab = SettingsTab;
                if (tab?.MysteryFlipHost == null || tab.MysteryFlipScale == null || tab.MysteryFlipSkew == null
                    || tab.CardMystery == null || tab.MysteryRevealFace == null)
                    return;

                // Hover lives on the HOST, not on either face: the faces swap Visibility at every
                // zero crossing, and a handler on a control that is about to be Collapsed would
                // hand us a MouseLeave that the pointer never performed. Neither handler marks
                // the event handled, so both faces' click paths (CardMystery_Click /
                // MysteryRevealFace_Click) go on working exactly as before.
                //
                // THE PHANTOM LEAVE: WPF hit-tests against the render-transformed visual, so a
                // plate turning edge-on stops being hit-testable UNDER A STATIONARY POINTER and
                // WPF raises a MouseLeave nobody performed. Acting on it would start a turn
                // back, which reopens the plate, which raises a MouseEnter... i.e. a flip loop
                // for as long as the mouse rests on the tile. So a leave arriving mid-turn is
                // believed only if the pointer really has left the tile's layout box, and the
                // truth is re-established from scratch (ReconcileMysteryHover) once the plate is
                // flat again and hit testing means something.
                if (!_mysteryHoverHooked)
                {
                    _mysteryHoverHooked = true;
                    tab.MysteryFlipHost.MouseEnter += (_, _) => RequestMysteryFace(showReveal: true);
                    tab.MysteryFlipHost.MouseLeave += (_, _) =>
                    {
                        if (_mysteryFlipping && MysteryPointerOverPlate()) return;
                        RequestMysteryFace(showReveal: false);
                    };
                }

                // A hidden plate must not be left mid-turn and a hidden badge must not keep
                // breathing; coming back is just another pass through the funnel, which is
                // idempotent. This also catches the window being minimised, where IsVisible goes
                // false without a tab change.
                if (!_mysteryVisHooked)
                {
                    _mysteryVisHooked = true;
                    tab.IsVisibleChanged += (_, args) =>
                    {
                        try
                        {
                            if (args.NewValue is bool visible && !visible)
                            {
                                CancelMysteryReveal();
                                StopMysteryPop();
                                return;
                            }
                            ApplyMysteryFx();
                        }
                        catch (Exception ex) { App.Logger?.Debug("Mystery visibility: {E}", ex.Message); }
                    };
                }

                ApplyMysteryBreath();
            }
            catch (Exception ex) { App.Logger?.Debug("EnsureMysteryTileFx: {E}", ex.Message); }
        }

        /// <summary>
        /// The pointer's request for a face. Hover-in is ALSO the moment the gift counts as
        /// opened: the date latch and the end of the breath happen here and nowhere else, so a
        /// plate that turned for a teardown, a repaint or a reduced-motion swap can never spend
        /// the day's surprise on the user's behalf.
        /// </summary>
        private void RequestMysteryFace(bool showReveal)
        {
            try
            {
                if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted)
                    return;

                _mysteryWantReveal = showReveal;

                if (showReveal)
                {
                    var s = App.Settings?.Current;
                    var today = DateTime.Now.ToString("yyyy-MM-dd");
                    if (s != null && s.DailyGiftLastRevealDate != today)
                    {
                        s.DailyGiftLastRevealDate = today;
                        App.Settings?.Save();
                    }
                    StopMysteryPop();              // doorbell answered
                    ApplyMysteryBadgeVisibility(); // ...and the notice it was ringing about
                }

                // Reduced motion still reveals - it just does not turn. The swap is the content;
                // the flip was only ever the flourish around it.
                if (!MotionFx.AllowTransitions) { SettleMysteryPlate(showReveal); return; }

                _mysterySpinGen++;
                RunMysteryFlip(_mysterySpinGen);
            }
            catch (Exception ex) { App.Logger?.Debug("RequestMysteryFace: {E}", ex.Message); }
        }

        /// <summary>
        /// The closing half of a turn, started from wherever the plate happens to be. Three
        /// cases, and none of them may snap: already flat on the wanted face (settle and stop),
        /// mid-turn but on the wanted face already (just open back up - this is the reversal
        /// caught before the zero crossing), or the wrong face is up (finish closing, swap at
        /// zero, then open on the other side).
        /// </summary>
        private void RunMysteryFlip(int gen)
        {
            if (gen != _mysterySpinGen) return;
            if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted) return;

            var tab = SettingsTab;
            var scale = tab?.MysteryFlipScale;
            var skew = tab?.MysteryFlipSkew;
            if (scale == null || skew == null) return;

            // GetValue on an animated property returns the CURRENT animated value, which is the
            // whole trick: every reversal starts from where the eye last saw the plate.
            var at = Math.Max(0.0, Math.Min(1.0, scale.ScaleX));

            if (_mysteryShowingReveal == _mysteryWantReveal)
            {
                if (at >= 0.999) SettleMysteryPlate(_mysteryWantReveal);
                else OpenMysteryPlate(gen, at);
                return;
            }

            var ms = Math.Max(1, (int)Math.Round(MysteryFlipHalfMs * at));
            var duration = TimeSpan.FromMilliseconds(ms);
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            _mysteryFlipping = true;

            var close = new DoubleAnimation(at, 0.0, duration) { EasingFunction = ease };
            var lean = new DoubleAnimation(skew.AngleY, MysterySkewDeg, duration) { EasingFunction = ease };

            close.Completed += (_, _) =>
            {
                try
                {
                    if (gen != _mysterySpinGen) return;
                    if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted) return;
                    SetMysteryFace(_mysteryWantReveal);   // the zero crossing
                    OpenMysteryPlate(gen, 0.0);
                }
                catch (Exception ex) { App.Logger?.Debug("Mystery flip close: {E}", ex.Message); }
            };

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, close);
            skew.BeginAnimation(SkewTransform.AngleYProperty, lean);
        }

        /// <summary>The opening half: widen back out to a flat plate and unwind the skew. Coming
        /// off a zero crossing the lean starts on the FAR side (-deg) so the two halves read as
        /// one continuous turn; coming off an interrupted close it just carries on from
        /// wherever the skew already is.</summary>
        private void OpenMysteryPlate(int gen, double from)
        {
            if (gen != _mysterySpinGen) return;

            var tab = SettingsTab;
            var scale = tab?.MysteryFlipScale;
            var skew = tab?.MysteryFlipSkew;
            if (scale == null || skew == null) return;

            var ms = Math.Max(1, (int)Math.Round(MysteryFlipHalfMs * (1.0 - from)));
            var duration = TimeSpan.FromMilliseconds(ms);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            _mysteryFlipping = true;

            var open = new DoubleAnimation(from, 1.0, duration) { EasingFunction = ease };
            var unlean = new DoubleAnimation(from <= 0.001 ? -MysterySkewDeg : skew.AngleY, 0.0, duration)
            {
                EasingFunction = ease,
            };

            open.Completed += (_, _) =>
            {
                try
                {
                    if (gen != _mysterySpinGen) return;
                    if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted) return;
                    SettleMysteryPlate(_mysteryShowingReveal);
                    ReconcileMysteryHover();
                }
                catch (Exception ex) { App.Logger?.Debug("Mystery flip open: {E}", ex.Message); }
            };

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, open);
            skew.BeginAnimation(SkewTransform.AngleYProperty, unlean);
        }

        /// <summary>Ends any turn immediately and parks the plate flat on the given face. Bumps
        /// the token FIRST so the in-flight chain's callbacks are orphaned, and writes the base
        /// values only AFTER BeginAnimation(prop, null) - a held animation pins its target, so a
        /// base value written before the detach never sticks.</summary>
        private void SettleMysteryPlate(bool showReveal)
        {
            _mysterySpinGen++;
            _mysteryFlipping = false;

            var tab = SettingsTab;
            if (tab?.MysteryFlipScale == null || tab.MysteryFlipSkew == null) return;
            tab.MysteryFlipScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            tab.MysteryFlipSkew.BeginAnimation(SkewTransform.AngleYProperty, null);
            tab.MysteryFlipScale.ScaleX = 1;
            tab.MysteryFlipSkew.AngleY = 0;
            SetMysteryFace(showReveal);
        }

        /// <summary>
        /// Is the pointer inside the tile, GEOMETRICALLY? Deliberately not IsMouseOver: this is
        /// asked exactly when the plate is edge-on and therefore not hit-testable, which is the
        /// state that makes IsMouseOver lie. The layout SLOT is measured in the parent grid's
        /// coordinate space, which the plate's own flip transform cannot distort (asking
        /// Mouse.GetPosition of the host itself would divide the answer by a ScaleX heading for
        /// zero). Errs toward "inside": the slot is the whole cell, the card only its 6px-inset
        /// middle - and a false "inside" only ever suppresses a phantom leave that the settle
        /// reconcile re-decides a few frames later.
        /// </summary>
        private bool MysteryPointerOverPlate()
        {
            try
            {
                var host = SettingsTab?.MysteryFlipHost;
                if (host == null || !host.IsVisible) return false;
                if (host.Parent is not UIElement parent) return false;

                var slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(host);
                if (slot.IsEmpty) return false;
                return slot.Contains(Mouse.GetPosition(parent));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("MysteryPointerOverPlate: {E}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Re-establishes the truth about the pointer once the plate is flat again. Two things
        /// can have gone stale while it was edge-on: WPF's own mouse-over tracking (nothing was
        /// hit-testable, and a pointer that never moves again would never be re-tested), and our
        /// wanted face (the pointer may have walked off during the turn, silently). Mouse
        /// .Synchronize re-runs the hit test - it may raise the enter/leave we missed, in which
        /// case the handlers above have already fixed things and the comparison below is a
        /// no-op. Convergent: it only acts on a disagreement, and each turn it starts ends here.
        /// </summary>
        private void ReconcileMysteryHover()
        {
            try
            {
                var host = SettingsTab?.MysteryFlipHost;
                if (host == null || !host.IsVisible) return;

                Mouse.Synchronize();
                var over = host.IsMouseOver;    // authoritative again: the plate is flat
                if (over != _mysteryWantReveal) RequestMysteryFace(over);
            }
            catch (Exception ex) { App.Logger?.Debug("ReconcileMysteryHover: {E}", ex.Message); }
        }

        /// <summary>Which face of the plate is up. Called at the zero crossing and by every
        /// settle path, and it is the only place <see cref="_mysteryShowingReveal"/> moves.</summary>
        private void SetMysteryFace(bool showReveal)
        {
            var tab = SettingsTab;
            if (tab?.CardMystery == null || tab.MysteryRevealFace == null) return;
            tab.MysteryRevealFace.Visibility = showReveal ? Visibility.Visible : Visibility.Collapsed;
            tab.CardMystery.Visibility = showReveal ? Visibility.Collapsed : Visibility.Visible;
            _mysteryShowingReveal = showReveal;
        }

        /// <summary>Teardown for the plate: kills an in-flight turn, forgets the hover, and puts
        /// the ? face back at rest. Does NOT touch the date latch - that belongs to hover-in
        /// alone, so a plate closed by a tab switch owes nothing and spends nothing.</summary>
        private void CancelMysteryReveal()
        {
            _mysteryWantReveal = false;
            SettleMysteryPlate(showReveal: false);
        }

        /// <summary>
        /// The gold breath: ring opacity and badge scale swell together, forever, until the gift
        /// is opened (hover) or the tile goes away - <see cref="StopMysteryPop"/> is the only
        /// exit, which is why it detaches exactly the way each animation was attached. The
        /// badge's two scale axes share ONE clock - two BeginAnimation calls would mint two
        /// clocks that drift, and on text that reads as shearing (intake CTA lesson).
        /// </summary>
        private void StartMysteryPop()
        {
            try
            {
                var tab = SettingsTab;
                if (tab?.MysteryGlow == null || tab.MysteryBadgeScale == null || tab.MysteryBadgeScale.IsFrozen) return;
                if (_mysteryPopPlaying) return;   // already breathing; restarting only resets phase
                _mysteryPopPlaying = true;

                var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

                // FillBehavior stays at its default: a forever animation never completes, so
                // .Stop would only describe a moment that never arrives, and the teardown path
                // below is the thing that actually restores the resting values.
                var glow = new DoubleAnimation(MysteryGlowRestOpacity, MysteryPopGlowMax, MysteryPopHalfCycle)
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = ease,
                };

                var swell = new DoubleAnimation(1.0, MysteryPopBadgeTo, MysteryPopHalfCycle)
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
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
