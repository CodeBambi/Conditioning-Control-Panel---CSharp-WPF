using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// THE SPIRAL ROOM — the tab that replaced <c>SpiralMapWindow</c> (CONTRACT-FUSE-0816 §2.4,
    /// owner ruling 2026-08-16). Three states, one surface, and the reveal now plays INSIDE it.
    ///
    /// <para><b>Why the window retired.</b> A top-level HWND had no airspace problem, which is why
    /// the map started as one — but it also meant the one moment this feature was built for opened a
    /// second window over the app instead of opening a room in it. Everything the window did to
    /// dodge the airspace problem is done here instead by NOT BUILDING THE BROWSER: the embed is
    /// constructed when this tab becomes visible in the spiral state and disposed when it stops
    /// being, so there is never a native child HWND sitting under the fog, under the reveal, or
    /// behind a tab nobody is on.</para>
    ///
    /// <para><b>The state selection is not in this file.</b> It is
    /// <see cref="SpiralRoom.StateFor(AppSettings, DescentFusePhase, bool, bool, bool)"/> — pure,
    /// every input passed in, pinned by tests, and shared with the rail entry so the two cannot
    /// disagree about whether there is anything to show. This class reads the world, hands it over,
    /// and paints whatever comes back.</para>
    ///
    /// <para><b>FIRST LIGHT is an event, not a state.</b> It overrides the selection for its three
    /// and a half seconds and then hands back. It fails SOFT in every direction: no block inside
    /// <see cref="FirstLightBlockTimeout"/>, a withdrawn block, a thrown frame or the user
    /// navigating away all end on the ordinary selection rather than on an empty room — there is no
    /// window left to close.</para>
    ///
    /// <para><b>Escape is never handled here.</b> The global panic key must reach its hook
    /// untouched; nothing in this tab looks at the keyboard at all.</para>
    /// </summary>
    public partial class SpiralTabView : UserControl
    {
        /// <summary>~50fps, matching the show's clock. The reveal is some trigonometry and a few
        /// ellipses; a higher rate would buy nothing a projector could show.</summary>
        private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(20);

        /// <summary>
        /// How long the first light waits for a descent block before giving up. The commit fires an
        /// immediate sync and the block rides the profile poll behind it, so twelve seconds covers a
        /// slow network with room to spare — and the intro itself covers the first four. Longer than
        /// this and the user is watching a held bloom wondering what broke.
        /// </summary>
        private static readonly TimeSpan FirstLightBlockTimeout = TimeSpan.FromSeconds(12);

        /// <summary>
        /// Where the fog era parks the reveal's clock.
        ///
        /// <para><b>Not zero, and the arithmetic says why.</b> At elapsed 0 the timeline's
        /// <c>FogOpacity</c> is also 0 — the fog has not risen yet — so a literal hold at zero would
        /// paint the bare radial ground and no weather at all. The other end is just as hard: the
        /// arm starts drawing at <see cref="SpiralFirstLightTimeline.DrawStartSeconds"/>, and a fog
        /// era that showed even a stub of the spiral would spoil the one thing the reveal exists to
        /// show. That instant is therefore the only honest hold: the most fog available before the
        /// first stroke. The blobs keep drifting regardless — they run off the AMBIENT clock, which
        /// is never held.</para>
        /// </summary>
        private static readonly double FogHoldSeconds = SpiralFirstLightTimeline.DrawStartSeconds;

        // ============================== the fog era's FX ==============================
        //
        // Owner verdict on the live demo, 2026-08-16: "could use some FX, a bolder text, maybe some
        // little animation and flair." Everything below is opacity and transform ONLY — the two
        // BlurEffects in the XAML are set once and never touched by a clock, which is the app's
        // standing rule (an animated Effect property re-runs the shader graph every frame).

        /// <summary>The hero digits, at their own size. The "any moment now" phrase shares the
        /// TextBlock and gets <see cref="ImminentFontSize"/> instead: it is a sentence, not a
        /// readout, and at digit size it would wrap and stop being a hero at all.</summary>
        private const double HeroFontSize = 56;
        private const double ImminentFontSize = 30;

        /// <summary>The pulse's whole amplitude. Two percent is the ceiling the owner named, and it
        /// is deliberately the ONLY thing about the pulse that does not change with the phase —
        /// see <see cref="SpiralRoom.FogPulseSecondsFor"/> on why the ladder is tempo, not size.</summary>
        private const double PulseScale = 1.022;

        /// <summary>The glow's resting opacity, and the top of its one-shot flare.</summary>
        private const double GlowRest = 0.42;
        private const double GlowFlarePeak = 1.0;
        private const double GlowFlareSeconds = 0.95;

        private const double HairlineLo = 0.14;
        private const double HairlineHi = 0.46;
        private const double HairlineSeconds = 3.6;

        /// <summary>Capped like every other ambient clock in the app (MotionFx.AmbientFrameRate).
        /// The blurred glow duplicate re-renders with the pulse, so this cap is doing real work.</summary>
        private const int AmbientFrameRate = 24;

        /// <summary>
        /// The embers, as fractions of the fog's own rectangle so they reflow with the window
        /// instead of clustering in a corner on a wide monitor. Curated rather than random: a seeded
        /// RNG would still be one more thing that can differ between two machines watching the same
        /// night, and nine specks is few enough to place by hand.
        ///
        /// <para>Tuple order: x fraction, y fraction, radius, the drift's period in seconds, and the
        /// peak opacity at the middle of that drift.</para>
        /// </summary>
        private static readonly (double Fx, double Fy, double R, double Seconds, double Peak)[] EmberSeeds =
        {
            (0.14, 0.86, 1.6, 19.0, 0.42),
            (0.27, 0.94, 2.4, 24.0, 0.30),
            (0.39, 0.80, 1.3, 16.5, 0.50),
            (0.52, 0.97, 2.0, 27.0, 0.26),
            (0.63, 0.84, 1.5, 21.0, 0.44),
            (0.74, 0.92, 2.6, 25.5, 0.24),
            (0.83, 0.78, 1.4, 17.5, 0.48),
            (0.91, 0.95, 1.9, 22.5, 0.32),
            (0.06, 0.90, 2.1, 28.0, 0.22),
        };

        /// <summary>The waiting panel's motes, in the little canvas's own coordinates rather than
        /// in fractions — that canvas has a fixed size, so there is nothing to reflow.</summary>
        private static readonly (double Fx, double Fy, double R, double Seconds, double Peak)[] MoteSeeds =
        {
            (0.12, 1.0, 1.5, 5.6, 0.50),
            (0.31, 1.0, 1.1, 7.4, 0.62),
            (0.50, 1.0, 1.8, 6.3, 0.38),
            (0.69, 1.0, 1.2, 8.1, 0.55),
            (0.88, 1.0, 1.5, 6.9, 0.44),
        };

        private DriftField? _embers;
        private DriftField? _motes;

        /// <summary>The phase the fog's pulse is currently keeping time to. Held so a repaint that
        /// did not change the phase does not throw away a running clock and start an identical one —
        /// the pulse would visibly jump back to the top of its breath every tick.</summary>
        private DescentFusePhase? _pulsePhase;

        // ============================== the splash ==============================

        /// <summary>
        /// How long the splash will wait for an embed that has neither navigated nor failed before
        /// giving the browser its airspace anyway.
        ///
        /// <para><b>It exists so the splash cannot become the hang it was built to hide.</b> The
        /// embed is held at Visibility.Hidden while it loads, and a hidden WebView2 that somehow
        /// never raises NavigationCompleted (a wedged renderer, a runtime mid-update) would strand
        /// the spiral behind a spinning glyph with nothing on screen ever changing. On this deadline
        /// the room shows whatever the browser actually has, which is either the canvas or its own
        /// dark slab — and the slab at least tells the truth.</para>
        /// </summary>
        private static readonly TimeSpan SplashRevealDeadline = TimeSpan.FromSeconds(10);

        private const double SplashFadeSeconds = 0.4;

        private DispatcherTimer? _splashWatchdog;

        /// <summary>True while the splash owns the surface — set the instant the spiral era is
        /// painted, cleared when the embed is revealed, when it fails, or on leaving the tab.</summary>
        private bool _splashUp;

        /// <summary>One chime per ENTRY, never per retry: a cue that repeated while a browser
        /// struggled would sound exactly like the thing being stuck.</summary>
        private bool _splashChimed;

        /// <summary>
        /// One door sound per entry, spent by the first painted fog.
        ///
        /// <para><b>SPENT, not armed, and the difference is a double whoosh.</b> Walking into this
        /// tab paints it TWICE — the tab system flips Visibility (which refreshes) and then calls
        /// <see cref="OnTabShown"/> (which refreshes again). A flag armed by either of those would be
        /// re-armed by the other between the two paints and the cue would fire on both. So the flag
        /// is only ever cleared on the way OUT, in <see cref="Suspend"/>, where there is exactly one
        /// event.</para>
        /// </summary>
        private bool _entrySfxSpent;

        // ---- the canvas ----
        private SpiralFirstLightVisual? _visual;
        private DispatcherTimer? _frames;
        private readonly Stopwatch _clock = new();
        private double _lastFrameAt;

        // ---- first light ----
        private bool _firstLight;
        private bool _firstLightReduced;
        private double _firstLightElapsed;
        private bool _awaitingFirstBlock;
        private DispatcherTimer? _blockWait;

        // ---- the embed ----
        private SpiralEmbedView? _embed;

        /// <summary>One failure per tab ENTRY, not per session: the map route is not deployed yet,
        /// so a user who comes back tomorrow deserves a fresh attempt rather than a panel that has
        /// decided for the rest of the launch. Cleared by <see cref="OnTabShown"/>.</summary>
        private bool _embedGaveUp;

        private bool _wired;
        private SpiralRoomState _state = SpiralRoomState.Waiting;

        public SpiralTabView()
        {
            InitializeComponent();

            FogEyebrow.Text = DescentFuseCopy.FogEyebrow;
            FogLine.Text = DescentFuseCopy.FogLine;
            FogTail.Text = DescentFuseCopy.FogTail;
            WaitingLine.Text = DescentFuseCopy.WaitingLine;
            SplashLine.Text = SplashCopy;

            SplashGlyph.Data = BuildSpiralGeometry();

            _embers = new DriftField(EmberHost, EmberSeeds);
            _motes = new DriftField(WaitingMotes, MoteSeeds);

            // The embers are laid out in fractions of the fog's rectangle, so they follow the window.
            EmberHost.SizeChanged += (_, _) => _embers?.Reflow();

            Loaded += (_, _) => Wire();
            // Unloaded is the window going away or this view being re-parented - NOT a tab switch
            // (WPF does not raise it for a Visibility change), so it is the right place to hand the
            // two services their subscriptions back. Both of them outlive this window.
            Unloaded += (_, _) => { Suspend(); Unwire(); };
            IsVisibleChanged += OnIsVisibleChanged;
        }

        // ============================== wiring ==============================

        /// <summary>
        /// Subscribe to the two services that can change what belongs on screen. Idempotent.
        ///
        /// <para><c>BlockChanged</c> carries the withhold too: it has no event of its own by design,
        /// and <c>DescentMigrationService</c> re-raises this one whenever the gate moves
        /// (DescentMigrationService.cs:161-165). One subscription, every re-evaluation.</para>
        /// </summary>
        private void Wire()
        {
            if (_wired) return;
            _wired = true;
            try
            {
                if (App.Descent != null) App.Descent.BlockChanged += OnBlockChanged;
                var fuse = App.DescentCountdown;
                if (fuse != null)
                {
                    fuse.PhaseChanged += OnPhaseChanged;
                    fuse.Tick += OnFuseTick;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] room could not wire: {E}", ex.Message); }
        }

        /// <summary>Symmetric teardown. Idempotent, and re-wirable: a re-parented view runs Loaded
        /// again and picks its subscriptions back up.</summary>
        private void Unwire()
        {
            if (!_wired) return;
            _wired = false;
            try
            {
                if (App.Descent != null) App.Descent.BlockChanged -= OnBlockChanged;
                var fuse = App.DescentCountdown;
                if (fuse != null)
                {
                    fuse.PhaseChanged -= OnPhaseChanged;
                    fuse.Tick -= OnFuseTick;
                }
            }
            catch { /* teardown races are not worth a log line */ }
        }

        private void OnPhaseChanged(object? sender, DescentFusePhaseChangedEventArgs e)
        {
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
            try
            {
                if (!IsVisible) return;

                Refresh();

                // The flare rides the repaint rather than replacing it: Refresh may have just moved
                // the room out of the fog entirely (the ceremony landing), and a glow swell over a
                // surface that is no longer the countdown would be a flash from nowhere.
                if (_state == SpiralRoomState.Fog && !_firstLight) FlarePhaseChange();
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] room phase repaint: {E}", ex.Message); }
        }

        private void OnFuseTick(object? sender, TimeSpan remaining)
        {
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
            try
            {
                if (!IsVisible || _state != SpiralRoomState.Fog || _firstLight) return;
                ApplyReadout(DescentFuseCopy.TMinus(remaining), hero: true);
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] room tick: {E}", ex.Message); }
        }

        private void OnBlockChanged(object? sender, EventArgs e)
        {
            // DescentService already marshalled to the UI thread before raising.
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
            try
            {
                var block = App.Descent?.Current;

                if (_firstLight && _awaitingFirstBlock)
                {
                    // THE BLOCK RACE. Until the first block lands, null means "the commit's sync has
                    // not come back yet" — the reveal is deliberately open BEFORE the record exists,
                    // and the withhold's own re-raise arrives with no block behind it. Treating that
                    // as a withdrawal would end the reveal a second after it started.
                    if (block is null) return;

                    _awaitingFirstBlock = false;
                    StopBlockWait();
                    App.Logger?.Information("[Spiral] first light: the descent block landed - the frame loop will hand over.");
                    return;
                }

                if (_firstLight) return;                     // the reveal owns the surface

                if (_embed != null && block != null) _embed.PostState(block);
                if (IsVisible) Refresh();
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] room block change: {E}", ex.Message); }
        }

        // ============================== visibility ==============================

        /// <summary>
        /// The tab system toggles Visibility (Loaded fires once), so this is the real entry and exit
        /// hook — same shape as <see cref="SpiralRailHost"/>'s. Leaving takes the browser with it.
        /// </summary>
        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible) { Wire(); Refresh(); AnnounceEntry(); }
            else Suspend();
        }

        /// <summary>
        /// Let the companion say hello to the room, once ever. The latch is the bark rule's own
        /// (repeatable false, lifetime scope, persisted in AppSettings.BarkLifetimeFired), so both
        /// entry paths may call this freely and only the first arrival can spend it.
        ///
        /// <para>Gated on the room having actually resolved to the spiral, and deliberately so: the
        /// line explains the tap, the jar and the banked day, and burning a once-ever welcome on the
        /// fog era would hand it to somebody looking at a countdown. <see cref="Refresh"/> has
        /// already run, so <c>_state</c> is this entry's answer and not the last one's.</para>
        ///
        /// <para>Never throws, and never blocks the entry it rides on. A bark is decoration.</para>
        /// </summary>
        private void AnnounceEntry()
        {
            if (_state != SpiralRoomState.Spiral) return;
            try { Services.Descent.DescentBarkWatcher.NotifySpiralOpened(); }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] entry bark failed: {E}", ex.Message); }
        }

        /// <summary>
        /// The tab was navigated to. Called from <c>ShowTab("spiral")</c>, which can happen at any
        /// time and from anywhere (the rail entry, the fuse chip, the profile plate, the account
        /// menu, a first light), so the gates are re-read from scratch every time rather than trusted
        /// from whenever this view was last painted.
        /// </summary>
        internal void OnTabShown()
        {
            // A fresh entry earns a fresh attempt at the embed - see _embedGaveUp. The two sound
            // flags are NOT reset here; they are reset on the way out. See _entrySfxSpent.
            _embedGaveUp = false;
            Wire();
            Refresh();
            AnnounceEntry();
        }

        /// <summary>Park everything: no clocks, no browser, nothing holding a frame.</summary>
        private void Suspend()
        {
            StopFrames();
            StopBlockWait();
            StopFogFx();
            HideSplash(fade: false);
            StopWaitingAmbience();
            _firstLight = false;
            TeardownEmbed();

            // The one unambiguous "this tab is no longer on screen" event, and therefore the only
            // safe place to re-arm the two cues that must fire once per entry.
            _entrySfxSpent = false;
            _splashChimed = false;
        }

        // ============================== the state ==============================

        /// <summary>
        /// Read the world, ask <see cref="SpiralRoom"/>, paint the answer. The whole surface is
        /// computed from scratch every time so that arriving in any state lands on a correct,
        /// complete room rather than on the accumulated result of the transitions taken to get here.
        /// </summary>
        private void Refresh()
        {
            if (_firstLight) return;   // the reveal owns the surface until it hands back

            try
            {
                var fuse = App.DescentCountdown;
                var state = SpiralRoom.StateFor(
                    App.Settings?.Current,
                    fuse?.LastAnnouncedPhase ?? DescentFusePhase.Dark,
                    fuse?.IsArmed == true,
                    App.DescentMigration?.SpiralWithheld == true,
                    App.Descent?.Current is not null);

                ApplyState(state);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Spiral] room refresh failed: {E}", ex.Message);
                // A predicate that threw must not leave a half-painted room. The waiting panel is
                // the state with the fewest promises in it.
                try { ApplyState(SpiralRoomState.Waiting); } catch { /* nothing left to do */ }
            }
        }

        private void ApplyState(SpiralRoomState state)
        {
            _state = state;

            // THE "?" FOLLOWS THE STATE. It belongs to the two states that are a room you
            // are being asked to make sense of, and to neither of the two that are a
            // ceremony: the fog says nothing is clickable and means it, and the reveal
            // is a one-shot the user should not be able to interrupt with a help card.
            // Attaching is idempotent and costs nothing until the button is actually shown.
            ApplyHelpChip(state != SpiralRoomState.Fog && !_firstLight);

            switch (state)
            {
                case SpiralRoomState.Fog:
                    // AIRSPACE: the browser must not exist while the fog is up.
                    TeardownEmbed();
                    EmbedHost.Visibility = Visibility.Collapsed;
                    WaitingPanel.Visibility = Visibility.Collapsed;
                    StopWaitingAmbience();
                    HideSplash(fade: false);

                    EnsureVisual();
                    FogHost.Visibility = Visibility.Visible;
                    FogCopy.Visibility = Visibility.Visible;
                    ApplyFogReadout();
                    StartFrames();
                    StartFogFx();
                    if (!_entrySfxSpent)
                    {
                        _entrySfxSpent = true;
                        DescentRoomSfx.PlayFogEntry();
                    }
                    break;

                case SpiralRoomState.Spiral:
                    StopFrames();
                    StopFogFx();
                    FogHost.Visibility = Visibility.Collapsed;
                    FogCopy.Visibility = Visibility.Collapsed;

                    // A REPAINT IS NOT A RE-ENTRY. This state is re-applied on every BlockChanged,
                    // and the spiral era is exactly where those keep arriving — so once this entry
                    // has a browser, whatever state it reached (loading behind the splash, revealed,
                    // or handed over to the waiting panel because it failed) is the state a repaint
                    // must leave alone. Without this, a routine sync would drop the splash back over
                    // a canvas that had already been revealed, with no watchdog left to lift it.
                    if (_embed != null) break;

                    // Same entry, embed already given up: the fallback stands until the NEXT entry
                    // clears _embedGaveUp and earns a fresh attempt.
                    if (_embedGaveUp) { ShowWaitingUnderSpiral(); break; }

                    WaitingPanel.Visibility = Visibility.Collapsed;
                    StopWaitingAmbience();

                    // AIRSPACE, one layer down. HIDDEN, not Visible: the slab is still arranged, so
                    // the browser is handed a real rectangle and navigates exactly as it would
                    // otherwise, but its native HWND cannot paint over the splash while it does.
                    // OnEmbedNavigated is what promotes it.
                    EmbedHost.Visibility = Visibility.Hidden;
                    ShowSplash();
                    EnsureEmbed();
                    break;

                default:
                    StopFrames();
                    StopFogFx();
                    TeardownEmbed();
                    HideSplash(fade: false);
                    FogHost.Visibility = Visibility.Collapsed;
                    FogCopy.Visibility = Visibility.Collapsed;
                    EmbedHost.Visibility = Visibility.Collapsed;
                    WaitingPanel.Visibility = Visibility.Visible;
                    StartWaitingAmbience();
                    break;
            }
        }

        /// <summary>
        /// Is the room actually SHOWING the spiral right now? The gate the first-open intro card
        /// stands on, and it is deliberately the painted state rather than "does a block exist":
        /// a card that explained banked days over a fog layer would be describing a map the user
        /// has not been given yet, and one that fired mid-reveal would land on top of the single
        /// moment this whole feature was built for.
        /// </summary>
        internal bool IsShowingSpiral => _state == SpiralRoomState.Spiral && !_firstLight;

        // ============================== the "?" ==============================

        /// <summary>Attached once per view; the popover itself is cheap but there is no reason
        /// to rebuild it on every repaint, and this state is re-applied on every BlockChanged.</summary>
        private bool _helpAttached;

        /// <summary>
        /// Show or hide the help chip, attaching its popover the first time it is wanted.
        ///
        /// <para>LAZY ON PURPOSE. A user who never reaches the spiral era never builds a
        /// HelpContent, and a user still in the fog never has one attached to a button they
        /// cannot see. Failure is swallowed the same way every other optional affordance in
        /// this room fails: a missing "?" is a smaller problem than a room that would not
        /// paint.</para>
        /// </summary>
        private void ApplyHelpChip(bool show)
        {
            try
            {
                if (BtnSpiralHelp == null) return;

                if (!show)
                {
                    BtnSpiralHelp.Visibility = Visibility.Collapsed;
                    return;
                }

                if (!_helpAttached)
                {
                    _helpAttached = true;
                    BtnSpiralHelp.ToolTip = null;   // popover and ToolTip must never double-render
                    HelpPopover.Attach(BtnSpiralHelp, BuildDescentHelpContent());
                }

                BtnSpiralHelp.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] help chip: {E}", ex.Message); }
        }

        /// <summary>
        /// What the Spiral IS, in the shape <c>HelpTooltipBuilder</c> renders: header, "What It
        /// Does", tips, "How It Works".
        ///
        /// <para><b>Composed from Loc, not from HelpContentService.</b> The 56 topics in that
        /// service are hardcoded English, which was survivable for topics that shipped years ago
        /// and is not survivable for a topic shipping into nine languages this month. The pattern
        /// followed here is MainWindow.UiUpdates.BuildIntakePassHelpContent - build the
        /// <see cref="HelpContent"/> at attach time out of <c>Loc.Get</c> calls - so this topic is
        /// translated on its first day and stays translated when the strings are revised.</para>
        ///
        /// <para>The mental model it has to leave behind, in this order: you earn XP anywhere, it
        /// lands in today's jar, a fifth of a jar banks the day, and banked days are the only
        /// thing that moves you down. The two guarantees underneath it are the tips and the
        /// closing line: nothing resets ever again, and time away is paid back rather than
        /// punished.</para>
        /// </summary>
        private static HelpContent BuildDescentHelpContent() => new()
        {
            SectionId = "Descent",
            // The header icon is a plain TextBlock, which cannot render COLR/CPAL colour
            // emoji - a BMP dingbat is the only glyph that survives. Same constraint the
            // intake pass topic hit; the spiral's own glyph is drawn, not typed.
            Icon = "◌",
            Title = Loc.Get("help_descent_title"),
            WhatItDoes = Loc.Get("help_descent_what"),
            Tips = new List<string>
            {
                Loc.Get("help_descent_tip_1"),
                Loc.Get("help_descent_tip_2"),
                Loc.Get("help_descent_tip_3"),
            },
            HowItWorks = Loc.Get("help_descent_how"),
        };

        /// <summary>
        /// The fog's readout: T-minus while the fuse is still burning, and a phrase once the instant
        /// has gone by without this account's ceremony having reached them. A countdown that ran out
        /// and kept showing 00:00 would read as broken; "any moment now" is what is actually true,
        /// because the server re-offers on every sync until a choice is taken.
        /// </summary>
        private void ApplyFogReadout()
        {
            var fuse = App.DescentCountdown;
            var remaining = fuse?.Remaining;
            if (fuse is null || remaining is null || remaining.Value <= TimeSpan.Zero)
                ApplyReadout(DescentFuseCopy.FogImminent, hero: false);
            else
                ApplyReadout(DescentFuseCopy.TMinus(remaining.Value), hero: true);
        }

        /// <summary>
        /// Type the readout and size it for what it actually is.
        ///
        /// <para><b>The size is passed in, never sniffed off the string.</b> Both callers know which
        /// of the two things they are showing, and a "does it start with a digit" test would be one
        /// copy edit away from rendering a sentence at 56px bold — where it wraps, breaks the
        /// StackPanel's rhythm and stops looking like anything the app meant to draw.</para>
        ///
        /// <para>The blurred glow behind it follows both the text and the size by binding, so there
        /// is nothing to keep in step here.</para>
        /// </summary>
        private void ApplyReadout(string text, bool hero)
        {
            FogDigits.Text = text;
            FogDigits.FontSize = hero ? HeroFontSize : ImminentFontSize;
        }

        // ============================== the canvas ==============================

        private void EnsureVisual()
        {
            if (_visual != null) return;
            _visual = new SpiralFirstLightVisual(ResolveAccent());
            // Full motion for the FOG era whatever the user's setting: the fog is held before its
            // first stroke, so "reduced" here would mean a finished spiral standing in the room -
            // the exact picture the withhold exists to keep back. Reduced motion instead gets a
            // still frame, because StartFrames does not start a clock for it.
            _visual.Begin(false);
            FogHost.Children.Add(_visual);
        }

        /// <summary>
        /// The mod's pink, or the app's own if the resource is missing. Read as a Color rather than a
        /// Brush because the canvas builds thirty-three alpha steps from it.
        ///
        /// <para>This is the one place the room touches the accent, and it is legal: it colours the
        /// SPIRAL, not the fuse. Every fuse element in this tab (the digits, the eyebrow, the
        /// waiting panel's edge) is literal gold - see DescentFuseChrome's "ACCENT IS UNTOUCHABLE".</para>
        /// </summary>
        private Color ResolveAccent()
        {
            try
            {
                if (TryFindResource("PinkColor") is Color c) return c;
                if (TryFindResource("PinkBrush") is SolidColorBrush b) return b.Color;
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] accent lookup: {E}", ex.Message); }
            return Color.FromRgb(0xFF, 0x69, 0xB4);
        }

        /// <summary>
        /// Start the frame loop. Reduced motion paints ONE frame and starts no clock: the fog is a
        /// picture either way, and a held picture is exactly what a reduced-motion user asked for.
        /// </summary>
        private void StartFrames()
        {
            if (_visual == null) return;

            if (!_firstLight && !MotionFx.AllowAmbientLoops)
            {
                StopFrames();
                _visual.SetFrame(FogHoldSeconds, 0);
                return;
            }

            if (_frames != null) { PaintFrame(); return; }

            _clock.Restart();
            _lastFrameAt = 0;
            _frames = new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = FrameInterval };
            _frames.Tick += OnFrame;
            _frames.Start();

            // Paint frame zero now rather than 20ms from now: the tab is already on screen, and one
            // flash of the bare ground is the single artefact a tab switch cannot hide.
            PaintFrame();
        }

        private void StopFrames()
        {
            if (_frames == null) return;
            try
            {
                _frames.Stop();
                _frames.Tick -= OnFrame;
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] frame stop: {E}", ex.Message); }
            _frames = null;
            try { _clock.Stop(); } catch { /* teardown race */ }
        }

        private void OnFrame(object? sender, EventArgs e)
        {
            // CLAUDE.md async rules 6/8: a queued tick can land on a dispatcher that is shutting down.
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;

            try { PaintFrame(); }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Spiral] frame failed: {E}", ex.Message);
                if (_firstLight) FinishFirstLight();
                else StopFrames();
            }
        }

        private void PaintFrame()
        {
            if (_visual == null) return;

            var now = _clock.Elapsed.TotalSeconds;
            var dt = now - _lastFrameAt;
            _lastFrameAt = now;
            if (dt < 0) dt = 0;

            if (!_firstLight)
            {
                // The fog era: the reveal's clock is parked, the ambient one is not.
                _visual.SetFrame(FogHoldSeconds, now);
                return;
            }

            // THE HOLD. The intro runs freely up to the bloom; the last half second - the fade that
            // hands the room over - waits until there is something to hand it to. A user on a slow
            // network watches a held bloom instead of an empty rectangle, and the deadline timer is
            // what stops that hold being forever.
            var holdAt = SpiralFirstLightTimeline.HandoffStartSeconds(_firstLightReduced);
            if (!_awaitingFirstBlock || _firstLightElapsed < holdAt) _firstLightElapsed += dt;

            _visual.SetFrame(_firstLightElapsed, now);

            if (SpiralFirstLightTimeline.IsComplete(_firstLightElapsed, _firstLightReduced))
                FinishFirstLight();
        }

        // ============================== first light ==============================

        /// <summary>
        /// THE FIRST LIGHT, played in the room instead of in a window (owner ruling 2026-08-16).
        /// Called by <c>DescentShowDirector</c> after it has brought the window forward and
        /// navigated here; safe to call when the tab is already showing something else, because the
        /// reveal simply takes the surface for its three and a half seconds.
        ///
        /// <para><b>It does not gate on the block.</b> The commit fires an immediate sync but the
        /// descent record can easily still be in flight, and refusing to open on that would turn the
        /// one moment this feature was built for into nothing happening at all. The intro plays over
        /// the wait and hands over when the block lands — or gives up quietly and lands on the
        /// waiting room, from which every ordinary door still works.</para>
        /// </summary>
        internal void BeginFirstLight()
        {
            try
            {
                if (_firstLight) return;

                // AIRSPACE: whatever was on screen, the browser cannot be under a reveal.
                TeardownEmbed();
                EmbedHost.Visibility = Visibility.Collapsed;
                WaitingPanel.Visibility = Visibility.Collapsed;
                FogCopy.Visibility = Visibility.Collapsed;

                // ...and neither can anything else. The reveal is the one thing on screen for its
                // three and a half seconds: no splash over it, no embers drifting through it, no
                // waiting panel breathing underneath, and no "?" in the corner inviting a click
                // through the middle of it. The chip comes back when the reveal hands the room
                // over to the ordinary state selection.
                ApplyHelpChip(false);
                HideSplash(fade: false);
                StopFogFx();
                StopWaitingAmbience();

                EnsureVisual();
                FogHost.Visibility = Visibility.Visible;

                // Reduced motion is the WHOLE-reveal decision and it is taken ONCE, like the show's:
                // a sequence that re-read the gate every frame could change shape halfway through.
                _firstLightReduced = MotionFx.Level != MotionLevel.Full;
                _visual!.Begin(_firstLightReduced);

                _firstLight = true;
                _firstLightElapsed = 0;
                _awaitingFirstBlock = App.Descent?.Current is null;

                StopFrames();
                StartFrames();

                if (_awaitingFirstBlock)
                {
                    _blockWait = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
                    { Interval = FirstLightBlockTimeout };
                    _blockWait.Tick += OnBlockWaitElapsed;
                    _blockWait.Start();
                }

                App.Logger?.Information("[Spiral] first light opened in the room ({Motion}, block {State}).",
                    _firstLightReduced ? "reduced motion" : "full motion",
                    _awaitingFirstBlock ? "still in flight" : "already in hand");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Spiral] first light could not start: {E}", ex.Message);
                FinishFirstLight();
            }
        }

        /// <summary>
        /// The reveal is over — by completion, by timeout, or by something throwing. Hand the room
        /// back to the ordinary selection, which is where the embed (or the waiting panel) comes
        /// from. Idempotent.
        /// </summary>
        private void FinishFirstLight()
        {
            if (!_firstLight)
            {
                StopBlockWait();
                return;
            }

            _firstLight = false;
            StopFrames();
            StopBlockWait();
            _visual?.Begin(false);   // back to the fog era's posture for any later fog

            Refresh();
        }

        /// <summary>
        /// The block never came. FAIL SOFT: land on the waiting room rather than an empty tab.
        /// Nothing is lost — the withhold is already open, so every ordinary door into the spiral
        /// works the moment the block does land, and this tab repaints on that event.
        /// </summary>
        private void OnBlockWaitElapsed(object? sender, EventArgs e)
        {
            StopBlockWait();
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
            if (!_firstLight || !_awaitingFirstBlock) return;

            App.Logger?.Information("[Spiral] first light: no descent block within {Seconds}s - handing the room back quietly.",
                (int)FirstLightBlockTimeout.TotalSeconds);
            FinishFirstLight();
        }

        private void StopBlockWait()
        {
            if (_blockWait == null) return;
            try
            {
                _blockWait.Stop();
                _blockWait.Tick -= OnBlockWaitElapsed;
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] block wait stop: {E}", ex.Message); }
            _blockWait = null;
            _awaitingFirstBlock = false;
        }

        // ============================== the embed ==============================

        /// <summary>
        /// Build the browser, once, for as long as this tab is the one on screen in the spiral
        /// state. Every failure ends on the waiting room — which is TODAY'S LIVE PATH, because the
        /// canvas's <c>?mode=map</c> route has not deployed yet. That is why the fallback is a room
        /// rather than an apology.
        /// </summary>
        private void EnsureEmbed()
        {
            if (_embedGaveUp) { ShowWaitingUnderSpiral(); return; }
            if (_embed != null) return;
            if (!IsVisible) return;

            try
            {
                var embed = new SpiralEmbedView("map");
                embed.Failed += (_, reason) =>
                {
                    App.Logger?.Debug("[Spiral] room embed unavailable: {Reason}", reason);
                    _embedGaveUp = true;
                    TeardownEmbed();
                    ShowWaitingUnderSpiral();
                };
                embed.Navigated += (_, _) => OnEmbedNavigated();
                _embed = embed;
                EmbedHost.Children.Add(embed);
                embed.Start();
                embed.PostState(App.Descent?.Current);
                StartSplashWatchdog();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Spiral] room embed could not start: {E}", ex.Message);
                _embedGaveUp = true;
                TeardownEmbed();
                ShowWaitingUnderSpiral();
            }
        }

        /// <summary>
        /// The embed navigated. Hand it the airspace it has been held back from and fade the splash
        /// off it. Guarded on the state because the event arrives on the browser's own schedule: a
        /// user who left the tab in the second the page landed must not have a browser promoted to
        /// Visible over whatever they are looking at now.
        /// </summary>
        private void OnEmbedNavigated()
        {
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
            try
            {
                StopSplashWatchdog();
                if (_firstLight || _state != SpiralRoomState.Spiral || _embed is null) return;

                EmbedHost.Visibility = Visibility.Visible;
                HideSplash(fade: true);
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] room embed reveal: {E}", ex.Message); }
        }

        /// <summary>The spiral state with no canvas in it. Deliberately NOT a state change: the
        /// gates still say "spiral", and the next entry retries.</summary>
        private void ShowWaitingUnderSpiral()
        {
            if (_firstLight) return;
            if (_state != SpiralRoomState.Spiral) return;
            StopSplashWatchdog();
            // No fade here, unlike the successful path: the splash is handing over to another
            // held-promise panel rather than to a finished spiral, and cross-fading one apology
            // into another just draws the eye to the swap.
            HideSplash(fade: false);
            EmbedHost.Visibility = Visibility.Collapsed;
            WaitingPanel.Visibility = Visibility.Visible;
            StartWaitingAmbience();
        }

        private void TeardownEmbed()
        {
            StopSplashWatchdog();
            if (_embed == null) return;
            try
            {
                EmbedHost.Children.Remove(_embed);
                _embed.Dispose();
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] room embed teardown: {E}", ex.Message); }
            _embed = null;
        }

        private void StartSplashWatchdog()
        {
            StopSplashWatchdog();
            _splashWatchdog = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
            { Interval = SplashRevealDeadline };
            _splashWatchdog.Tick += OnSplashDeadline;
            _splashWatchdog.Start();
        }

        private void StopSplashWatchdog()
        {
            if (_splashWatchdog == null) return;
            try
            {
                _splashWatchdog.Stop();
                _splashWatchdog.Tick -= OnSplashDeadline;
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] splash watchdog stop: {E}", ex.Message); }
            _splashWatchdog = null;
        }

        /// <summary>
        /// The embed neither navigated nor failed inside the deadline. Reveal it anyway: whatever
        /// the browser has is more honest than a splash that has stopped meaning "loading" and
        /// started meaning "hung".
        /// </summary>
        private void OnSplashDeadline(object? sender, EventArgs e)
        {
            StopSplashWatchdog();
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
            if (_firstLight || _state != SpiralRoomState.Spiral || _embed is null) return;

            App.Logger?.Information(
                "[Spiral] the embed said nothing within {Seconds}s - revealing it behind the splash anyway.",
                (int)SplashRevealDeadline.TotalSeconds);

            try
            {
                EmbedHost.Visibility = Visibility.Visible;
                HideSplash(fade: true);
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] splash deadline: {E}", ex.Message); }
        }

        // ============================== the fog era's FX ==============================

        /// <summary>
        /// Light the fog's own clocks: the hero's heartbeat, the hairline's breath, the embers.
        /// Idempotent and cheap to call again — the pulse only rebuilds when the PHASE it is keeping
        /// time to has actually changed, so the once-a-second repaint the tick causes does not reset
        /// the breath to the top of its cycle every second.
        /// </summary>
        private void StartFogFx()
        {
            var phase = App.DescentCountdown?.LastAnnouncedPhase ?? DescentFusePhase.Dark;

            if (!MotionFx.AllowAmbientLoops)
            {
                // Reduced motion gets the LOOK and none of the clocks: the layered glow, the
                // hairline and the bold digits are all still there, simply held still. The hero is
                // information; only its heartbeat is decoration.
                StopFogFx();
                FogDigitsGlow.Opacity = GlowRest;
                FogHairline.Opacity = HairlineHi;
                return;
            }

            EmberHost.Visibility = Visibility.Visible;
            _embers?.Start();   // idempotent; a running field is left running

            // Everything below is per-PHASE, and this is what makes the whole method idempotent: a
            // repaint that did not move the phase must not restart the hairline's breath or the
            // hero's pulse, or both would visibly jump back to the top of their cycles.
            if (_pulsePhase == phase) return;
            _pulsePhase = phase;

            MotionFx.GlowBreath(FogHairline, HairlineLo, HairlineHi, HairlineSeconds);

            // Half a breath in, half a breath out - AutoReverse doubles the seconds, so the ladder
            // in SpiralRoom is the HALF cycle and that is what the doc there says.
            var pulse = new DoubleAnimation(1.0, PulseScale,
                TimeSpan.FromSeconds(SpiralRoom.FogPulseSecondsFor(phase)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(pulse, AmbientFrameRate);
            FogDigitsPulse.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            FogDigitsPulse.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }

        /// <summary>
        /// Kill every fog clock and park the parts at rest. The nulls are what release the
        /// animations' HOLD on each property — without them the last animated value sticks, and a
        /// tab left mid-breath comes back very slightly too large forever.
        /// </summary>
        private void StopFogFx()
        {
            try
            {
                _pulsePhase = null;
                FogDigitsPulse.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                FogDigitsPulse.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                FogDigitsPulse.ScaleX = FogDigitsPulse.ScaleY = 1.0;

                FogDigitsGlow.BeginAnimation(OpacityProperty, null);
                FogDigitsGlow.Opacity = GlowRest;

                FogHairline.BeginAnimation(OpacityProperty, null);
                FogHairline.Opacity = HairlineHi;

                _embers?.Stop();
                EmberHost.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] fog fx stop: {E}", ex.Message); }
        }

        /// <summary>
        /// THE PHASE FLARE — one glow swell when the countdown crosses into a nearer phase, and the
        /// only moment in the fog era that is an event rather than a loop.
        ///
        /// <para>It is the blurred duplicate's OPACITY that swells, not the blur radius and not the
        /// digits' size: the effect underneath it is set once and never animated, and the letters
        /// themselves never move, so a glance away and back does not find the readout a different
        /// shape. It also re-starts the pulse, because the phase that just changed is the phase the
        /// pulse keeps time to.</para>
        /// </summary>
        private void FlarePhaseChange()
        {
            try
            {
                StartFogFx();   // the tempo ladder just moved a rung

                if (!MotionFx.AllowTransitions) return;

                var flare = new DoubleAnimationUsingKeyFrames
                { Duration = TimeSpan.FromSeconds(GlowFlareSeconds) };
                var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

                flare.KeyFrames.Add(new EasingDoubleKeyFrame(GlowRest,
                    KeyTime.FromTimeSpan(TimeSpan.Zero), ease));
                flare.KeyFrames.Add(new EasingDoubleKeyFrame(GlowFlarePeak,
                    KeyTime.FromTimeSpan(TimeSpan.FromSeconds(GlowFlareSeconds * 0.22)), ease));
                flare.KeyFrames.Add(new EasingDoubleKeyFrame(GlowRest,
                    KeyTime.FromTimeSpan(TimeSpan.FromSeconds(GlowFlareSeconds)), ease));

                // FillBehavior.Stop hands the property back at the end so the resting 0.42 authored
                // in the XAML is what stands afterwards, not a held keyframe.
                flare.FillBehavior = FillBehavior.Stop;
                Timeline.SetDesiredFrameRate(flare, AmbientFrameRate);
                FogDigitsGlow.BeginAnimation(OpacityProperty, flare);
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] phase flare: {E}", ex.Message); }
        }

        // ============================== the splash ==============================

        /// <summary>
        /// The splash's one line. Hardcoded English with the rest of the fuse's copy (CONTRACT-FUSE
        /// 0816 §4) and lower case to match the waiting room's register — it is the same voice
        /// saying the same kind of thing, one step earlier.
        /// </summary>
        private const string SplashCopy = "opening the spiral";

        /// <summary>
        /// Raise the splash over the spiral era and start its clocks. Called every time the spiral
        /// state is painted, so leaving the tab and coming back gets a fresh splash rather than the
        /// faded-out remains of the last one — which is why the opacity is reset here and not only
        /// on the way down.
        /// </summary>
        private void ShowSplash()
        {
            try
            {
                _splashUp = true;
                SpiralSplash.BeginAnimation(OpacityProperty, null);
                SpiralSplash.Opacity = 1;
                SpiralSplash.Visibility = Visibility.Visible;

                if (!_splashChimed)
                {
                    _splashChimed = true;
                    DescentRoomSfx.PlaySplashOpen();
                }

                if (!MotionFx.AllowAmbientLoops)
                {
                    // Held still, not hidden: somebody who turned motion off still has to be able to
                    // tell that something is happening, and the line plus the glyph say so.
                    SplashSpin.BeginAnimation(RotateTransform.AngleProperty, null);
                    SplashSpin.Angle = 0;
                    SplashHalo.Opacity = 0.16;
                    SplashGlyph.Opacity = 0.92;
                    SplashDot1.Opacity = SplashDot2.Opacity = SplashDot3.Opacity = 0.6;
                    return;
                }

                var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(4.6))
                { RepeatBehavior = RepeatBehavior.Forever };
                Timeline.SetDesiredFrameRate(spin, AmbientFrameRate);
                SplashSpin.BeginAnimation(RotateTransform.AngleProperty, spin);

                MotionFx.GlowBreath(SplashHalo, 0.08, 0.30, 2.2);
                MotionFx.GlowBreath(SplashGlyph, 0.58, 0.96, 1.7);

                // The ellipsis, one dot at a time. Same clock, three offsets - a marquee rather than
                // three independent loops that would drift out of order within a minute.
                BeginDot(SplashDot1, 0.00);
                BeginDot(SplashDot2, 0.42);
                BeginDot(SplashDot3, 0.84);
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] splash up: {E}", ex.Message); }
        }

        private static void BeginDot(UIElement dot, double offsetSeconds)
        {
            const double cycle = 1.45;
            var anim = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(cycle),
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(offsetSeconds),
            };
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.15, KeyTime.FromTimeSpan(TimeSpan.Zero), ease));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.95,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(cycle * 0.34)), ease));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.15,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(cycle)), ease));
            Timeline.SetDesiredFrameRate(anim, AmbientFrameRate);
            dot.BeginAnimation(OpacityProperty, anim);
        }

        /// <summary>
        /// Take the splash down — faded when it is handing over to a finished spiral, instantly when
        /// it is handing over to the waiting panel or leaving the tab. Idempotent, and safe to call
        /// on a splash that was never up.
        /// </summary>
        private void HideSplash(bool fade)
        {
            try
            {
                if (!_splashUp && SpiralSplash.Visibility != Visibility.Visible) return;
                _splashUp = false;

                StopSplashClocks();

                if (!fade || !MotionFx.AllowTransitions)
                {
                    SpiralSplash.BeginAnimation(OpacityProperty, null);
                    SpiralSplash.Opacity = 1;   // the resting value, for the next time it is raised
                    SpiralSplash.Visibility = Visibility.Collapsed;
                    return;
                }

                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(SplashFadeSeconds))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                fadeOut.Completed += (_, _) =>
                {
                    // A re-entry can raise the splash again inside these 400ms; if it did, _splashUp
                    // is true again and this stale completion must not collapse the new one.
                    if (_splashUp) return;
                    try
                    {
                        SpiralSplash.BeginAnimation(OpacityProperty, null);
                        SpiralSplash.Opacity = 1;
                        SpiralSplash.Visibility = Visibility.Collapsed;
                    }
                    catch { /* the window went away under a 400ms fade */ }
                };
                SpiralSplash.BeginAnimation(OpacityProperty, fadeOut);
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] splash down: {E}", ex.Message); }
        }

        private void StopSplashClocks()
        {
            try
            {
                SplashSpin.BeginAnimation(RotateTransform.AngleProperty, null);
                SplashSpin.Angle = 0;
                MotionFx.Stop(SplashHalo);
                MotionFx.Stop(SplashGlyph);
                SplashDot1.BeginAnimation(OpacityProperty, null);
                SplashDot2.BeginAnimation(OpacityProperty, null);
                SplashDot3.BeginAnimation(OpacityProperty, null);
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] splash clocks: {E}", ex.Message); }
        }

        /// <summary>
        /// The splash's glyph: an archimedean spiral, three turns, drawn as one open polyline and
        /// frozen. Geometry rather than a path string for the same reason the rail chip's clock is —
        /// the shape is arithmetic, and arithmetic is easier to re-tune than a mini-language.
        /// </summary>
        private static Geometry BuildSpiralGeometry()
        {
            const double turns = 3.0;
            const int steps = 200;
            const double maxRadius = 38;
            var centre = new Point(40, 40);

            var points = new PointCollection();
            for (int i = 1; i <= steps; i++)
            {
                double t = i / (double)steps;
                double angle = t * turns * 2 * Math.PI;
                double radius = t * maxRadius;
                points.Add(new Point(centre.X + radius * Math.Cos(angle),
                                     centre.Y + radius * Math.Sin(angle)));
            }

            var figure = new PathFigure { StartPoint = centre, IsClosed = false, IsFilled = false };
            figure.Segments.Add(new PolyLineSegment(points, true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();
            return geometry;
        }

        // ============================== the waiting room's ambience ==============================

        /// <summary>
        /// Give the held promise a pulse. The line breathes and a few motes drift up behind it,
        /// because a panel that says "the spiral is finding you" and then sits perfectly still reads
        /// as a panel that stopped looking — which is the exact impression this whole pass exists to
        /// remove. Idempotent.
        /// </summary>
        private void StartWaitingAmbience()
        {
            try
            {
                if (!MotionFx.AllowAmbientLoops)
                {
                    WaitingGlow.Opacity = 0.5;
                    _motes?.Stop();
                    return;
                }

                MotionFx.GlowBreath(WaitingGlow, 0.18, 0.70, 3.1);
                _motes?.Start();
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] waiting ambience: {E}", ex.Message); }
        }

        private void StopWaitingAmbience()
        {
            try
            {
                WaitingGlow.BeginAnimation(OpacityProperty, null);
                WaitingGlow.Opacity = 0.5;
                _motes?.Stop();
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] waiting ambience stop: {E}", ex.Message); }
        }

        // ============================== the drift field ==============================

        /// <summary>
        /// A handful of specks drifting up through a Canvas, built once and started/stopped with the
        /// state that owns them. Used twice: the fog's embers (which reflow with the window) and the
        /// waiting panel's motes (which do not, because that canvas has a fixed size).
        ///
        /// <para><b>Transform and opacity only, and one clock per speck.</b> Nothing here touches
        /// Canvas.Top with an animation — that is a layout property, and animating layout is how the
        /// app has burned a frame budget before. The specks are POSITIONED by Canvas.Left/Top once
        /// per size change and MOVED by a TranslateTransform.</para>
        ///
        /// <para><b>It parks empty.</b> Stop() releases every animation's hold and drops the specks
        /// to zero opacity, so a collapsed field costs a few frozen brushes and nothing else.</para>
        /// </summary>
        private sealed class DriftField
        {
            /// <summary>The fuse's gold, and never the mod accent — same law as every other fuse
            /// surface (see DescentFuseChrome's "ACCENT IS UNTOUCHABLE").</summary>
            private static readonly Brush Gold = FrozenGold();

            /// <summary>How far a speck travels in one cycle. Fixed device-independent pixels rather
            /// than a fraction of the host, so a resize repositions the field without having to
            /// rebuild every clock in it.</summary>
            private const double RiseDistance = 190;

            private const int FrameRate = 20;

            private readonly Canvas _host;
            private readonly (double Fx, double Fy, double R, double Seconds, double Peak)[] _seeds;
            private readonly List<Ellipse> _dots = new();
            private bool _running;

            /// <param name="seeds">Positions are FRACTIONS of the host's size, which is what lets
            /// the same field serve a full-bleed canvas that follows the window and a fixed 216px
            /// one inside a Border.</param>
            internal DriftField(Canvas host,
                                (double Fx, double Fy, double R, double Seconds, double Peak)[] seeds)
            {
                _host = host;
                _seeds = seeds;

                foreach (var seed in _seeds)
                {
                    var dot = new Ellipse
                    {
                        Width = seed.R * 2,
                        Height = seed.R * 2,
                        Fill = Gold,
                        Opacity = 0,
                        IsHitTestVisible = false,
                        RenderTransform = new TranslateTransform(),
                    };
                    _dots.Add(dot);
                    _host.Children.Add(dot);
                }

                Reflow();
            }

            /// <summary>Re-seat every speck for the host's current size. Cheap: property sets only,
            /// and the running clocks are on the transforms, so nothing is interrupted.</summary>
            internal void Reflow()
            {
                try
                {
                    double w = _host.ActualWidth;
                    double h = _host.ActualHeight;
                    if (w <= 0 || h <= 0)
                    {
                        // Before the first layout pass. An authored size is the fallback (the
                        // waiting panel's canvas has one); the fog's does not, and it gets a real
                        // size from the SizeChanged hook the moment it is arranged.
                        w = double.IsNaN(_host.Width) ? 0 : _host.Width;
                        h = double.IsNaN(_host.Height) ? 0 : _host.Height;
                        if (w <= 0 || h <= 0) return;
                    }

                    for (int i = 0; i < _dots.Count; i++)
                    {
                        Canvas.SetLeft(_dots[i], _seeds[i].Fx * w - _seeds[i].R);
                        Canvas.SetTop(_dots[i], _seeds[i].Fy * h - _seeds[i].R);
                    }
                }
                catch { /* a canvas mid-teardown is not worth a log line */ }
            }

            /// <summary>Start every speck's drift. Idempotent — a second call while running leaves
            /// the existing clocks alone rather than restarting the whole field in lockstep, which
            /// is the one thing that would make nine hand-placed specks look like a machine.</summary>
            internal void Start()
            {
                if (_running) return;
                _running = true;

                Reflow();

                for (int i = 0; i < _dots.Count; i++)
                {
                    var seed = _seeds[i];
                    var dot = _dots[i];
                    var duration = TimeSpan.FromSeconds(seed.Seconds);

                    // Each speck starts a fraction of its own cycle later, which is what keeps the
                    // field from breathing as one animal.
                    var offset = TimeSpan.FromSeconds(seed.Seconds * (i / (double)Math.Max(1, _dots.Count)));

                    var rise = new DoubleAnimation(0, -RiseDistance, duration)
                    { RepeatBehavior = RepeatBehavior.Forever, BeginTime = offset };
                    Timeline.SetDesiredFrameRate(rise, FrameRate);

                    var fade = new DoubleAnimationUsingKeyFrames
                    {
                        Duration = duration,
                        RepeatBehavior = RepeatBehavior.Forever,
                        BeginTime = offset,
                    };
                    var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
                    fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero), ease));
                    fade.KeyFrames.Add(new EasingDoubleKeyFrame(seed.Peak,
                        KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seed.Seconds * 0.42)), ease));
                    fade.KeyFrames.Add(new EasingDoubleKeyFrame(0,
                        KeyTime.FromTimeSpan(duration), ease));
                    Timeline.SetDesiredFrameRate(fade, FrameRate);

                    if (dot.RenderTransform is TranslateTransform move)
                        move.BeginAnimation(TranslateTransform.YProperty, rise);
                    dot.BeginAnimation(OpacityProperty, fade);
                }
            }

            /// <summary>Release every clock's hold and park the field invisible.</summary>
            internal void Stop()
            {
                _running = false;
                foreach (var dot in _dots)
                {
                    try
                    {
                        if (dot.RenderTransform is TranslateTransform move)
                        {
                            move.BeginAnimation(TranslateTransform.YProperty, null);
                            move.Y = 0;
                        }
                        dot.BeginAnimation(OpacityProperty, null);
                        dot.Opacity = 0;
                    }
                    catch { /* teardown race */ }
                }
            }

            private static Brush FrozenGold()
            {
                var b = new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x52));
                b.Freeze();
                return b;
            }
        }
    }
}
