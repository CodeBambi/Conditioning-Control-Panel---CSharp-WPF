using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
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
            try { if (IsVisible) Refresh(); }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] room phase repaint: {E}", ex.Message); }
        }

        private void OnFuseTick(object? sender, TimeSpan remaining)
        {
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
            try
            {
                if (!IsVisible || _state != SpiralRoomState.Fog || _firstLight) return;
                FogDigits.Text = DescentFuseCopy.TMinus(remaining);
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
            if (IsVisible) { Wire(); Refresh(); }
            else Suspend();
        }

        /// <summary>
        /// The tab was navigated to. Called from <c>ShowTab("spiral")</c>, which can happen at any
        /// time and from anywhere (the rail entry, the fuse chip, the profile plate, the account
        /// menu, a first light), so the gates are re-read from scratch every time rather than trusted
        /// from whenever this view was last painted.
        /// </summary>
        internal void OnTabShown()
        {
            // A fresh entry earns a fresh attempt at the embed - see _embedGaveUp.
            _embedGaveUp = false;
            Wire();
            Refresh();
        }

        /// <summary>Park everything: no clocks, no browser, nothing holding a frame.</summary>
        private void Suspend()
        {
            StopFrames();
            StopBlockWait();
            _firstLight = false;
            TeardownEmbed();
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

            switch (state)
            {
                case SpiralRoomState.Fog:
                    // AIRSPACE: the browser must not exist while the fog is up.
                    TeardownEmbed();
                    EmbedHost.Visibility = Visibility.Collapsed;
                    WaitingPanel.Visibility = Visibility.Collapsed;

                    EnsureVisual();
                    FogHost.Visibility = Visibility.Visible;
                    FogCopy.Visibility = Visibility.Visible;
                    FogDigits.Text = FogReadout();
                    StartFrames();
                    break;

                case SpiralRoomState.Spiral:
                    StopFrames();
                    FogHost.Visibility = Visibility.Collapsed;
                    FogCopy.Visibility = Visibility.Collapsed;
                    WaitingPanel.Visibility = Visibility.Collapsed;
                    EmbedHost.Visibility = Visibility.Visible;
                    EnsureEmbed();
                    break;

                default:
                    StopFrames();
                    TeardownEmbed();
                    FogHost.Visibility = Visibility.Collapsed;
                    FogCopy.Visibility = Visibility.Collapsed;
                    EmbedHost.Visibility = Visibility.Collapsed;
                    WaitingPanel.Visibility = Visibility.Visible;
                    break;
            }
        }

        /// <summary>
        /// The fog's digits: T-minus while the fuse is still burning, and a phrase once the instant
        /// has gone by without this account's ceremony having reached them. A countdown that ran out
        /// and kept showing 00:00 would read as broken; "any moment now" is what is actually true,
        /// because the server re-offers on every sync until a choice is taken.
        /// </summary>
        private static string FogReadout()
        {
            var fuse = App.DescentCountdown;
            var remaining = fuse?.Remaining;
            if (fuse is null || remaining is null || remaining.Value <= TimeSpan.Zero)
                return DescentFuseCopy.FogImminent;
            return DescentFuseCopy.TMinus(remaining.Value);
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
                _embed = embed;
                EmbedHost.Children.Add(embed);
                embed.Start();
                embed.PostState(App.Descent?.Current);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Spiral] room embed could not start: {E}", ex.Message);
                _embedGaveUp = true;
                TeardownEmbed();
                ShowWaitingUnderSpiral();
            }
        }

        /// <summary>The spiral state with no canvas in it. Deliberately NOT a state change: the
        /// gates still say "spiral", and the next entry retries.</summary>
        private void ShowWaitingUnderSpiral()
        {
            if (_firstLight) return;
            if (_state != SpiralRoomState.Spiral) return;
            EmbedHost.Visibility = Visibility.Collapsed;
            WaitingPanel.Visibility = Visibility.Visible;
        }

        private void TeardownEmbed()
        {
            if (_embed == null) return;
            try
            {
                EmbedHost.Children.Remove(_embed);
                _embed.Dispose();
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] room embed teardown: {E}", ex.Message); }
            _embed = null;
        }
    }
}
