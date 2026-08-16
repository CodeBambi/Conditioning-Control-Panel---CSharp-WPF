using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Serilog;

// NAMESPACE TRAP, and it is not a style preference. Every window under Windows/ lives in the
// FLAT ConditioningControlPanel namespace — not one of them declares ConditioningControlPanel.Windows.
// Declaring it here compiles for a moment and then breaks ScreenOcrService, whose
// `Windows.Graphics.Imaging.BitmapDecoder` is resolved relative to the enclosing
// ConditioningControlPanel namespace: the instant a ConditioningControlPanel.Windows exists, it
// shadows the WinRT `Windows` root and the OCR service stops finding its decoder. Keep it flat.
namespace ConditioningControlPanel
{
    /// <summary>
    /// THE FUSE AT ZERO (CONTRACT-FUSE-0816 §2.3/§2.4) — the fullscreen show the countdown has been
    /// counting down to, and the two shows that hang off it.
    ///
    /// <para><b>Three shows, one window.</b> <see cref="DescentShowKind.Live"/> is the night itself:
    /// freeze, crack, drain, black, bloom, then the handoff to the migration ceremony.
    /// <see cref="DescentShowKind.CatchUp"/> is the same five beats compressed to six seconds for a
    /// subject who was not at the machine when the instant passed. <see cref="DescentShowKind.Ignition"/>
    /// is the Year One spiral, after the ceremony's choice is committed. They share a window because
    /// they share a room — and because one window means one panic path, one single-open guard and
    /// one place where a fullscreen surface can possibly be left on screen.</para>
    ///
    /// <para><b>This window never opens on its own.</b> Every entry point runs through
    /// <see cref="DescentShowDirector"/>, which only acts on <c>DescentCountdownService</c>'s events
    /// — and that service raises nothing without a server-set ceremony timestamp. There is no menu
    /// item, no setting and no debug hook. Unset <c>DESCENT_CEREMONY_AT</c> and this file is dead
    /// code.</para>
    ///
    /// <para><b>Escape is not handled here, on purpose.</b> Escape is the app's panic key and it is
    /// delivered by a global low-level hook regardless of focus; swallowing it would put a
    /// fullscreen topmost window between a user and the one key they are promised always works.
    /// The panic path reaches this window through <see cref="ForceCloseAll"/> instead, registered
    /// beside the ceremony's in MainWindow.StartStop.</para>
    ///
    /// <para><b>Reduced motion gets a SNAPPED state, never a missing one</b> (contract §0). At
    /// anything below MotionLevel.Full the crack is a single 1.5s crossfade to the held bloom and
    /// the ignition snaps to the finished lit spiral — the same beats, the same handoff clock, the
    /// same keepsake, without the ninety sparks. The show is never simply skipped: a subject who
    /// turned motion down still gets the night.</para>
    ///
    /// <para><b>The service is disarmed by the time this opens.</b> The countdown stops ticking at
    /// zero, so there are no Tick events to drive anything here — this window runs its own clock, a
    /// single DispatcherTimer against a Stopwatch, and stops it in <c>Closed</c>.</para>
    /// </summary>
    public partial class DescentFuseWindow : Window
    {
        /// <summary>
        /// Live instances, so the panic path can clear the screen. Static because panic has no
        /// reference to hand: it is a global keystroke, not a UI interaction. It doubles as the
        /// single-open guard — see <see cref="Open"/>.
        /// </summary>
        private static readonly List<DescentFuseWindow> Live = new();

        /// <summary>The clock. ~50fps; the show is four brush ramps and some trigonometry, and a
        /// higher rate would buy nothing a projector could show.</summary>
        private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(20);

        /// <summary>How long the catch-up holds its bloom before dissolving. Long enough to land,
        /// short enough that it is not mistaken for the ceremony being late.</summary>
        private const double CatchUpHoldSeconds = 1.2;

        private const double CatchUpFadeSeconds = 0.8;

        private readonly DescentShowKind _kind;
        private readonly bool _reduced;
        private readonly DescentFuseStageVisual _visual;
        private readonly Stopwatch _clock = new();
        private readonly DescentFuseHandoff _handoff = new();

        private DispatcherTimer? _timer;
        private bool _witnessedMarked;
        private bool _backdropRaised;
        private bool _crackSounded;
        private bool _closing;

        /// <summary>
        /// TRUE while this window is holding the ceremony back (live path only — the catch-up's
        /// hold belongs to the director, which takes it before this window exists). Tracked as a
        /// bool rather than trusting the depth counter, so a release cannot run twice and decrement
        /// somebody else's hold.
        /// </summary>
        private bool _holdingOffers;

        /// <summary>
        /// TRUE once this window saw the ceremony come up and started dissolving into it. The
        /// director reads it on Closed to decide whether to hand focus over.
        /// </summary>
        public bool HandedOffToCeremony { get; private set; }

        // ------------------------------------------------------------------
        // Construction / lifecycle
        // ------------------------------------------------------------------

        private DescentFuseWindow(DescentShowKind kind)
        {
            _kind = kind;

            InitializeComponent();

            // Reduced motion is the whole-show decision and it is taken ONCE, here: a show that
            // re-read the gate every frame could change shape halfway through if the user opened
            // settings behind it, which is a worse outcome than either state.
            _reduced = MotionFx.Level != MotionLevel.Full;

            // THE MOD'S ACCENT, resolved once from the live dictionary. The bloom is the ceremony's
            // glow, so it has to be the ceremony's pink; the visual freezes it because a mod cannot
            // change underneath a fullscreen topmost window in the six seconds this lives. See the
            // note in DescentFuseStageVisual — this is the one place that exemption applies.
            _visual = new DescentFuseStageVisual(ResolveAccent());
            _visual.Begin(kind, _reduced);
            StageHost.Children.Add(_visual);

            if (kind == DescentShowKind.Ignition)
            {
                // The ignition opens INTO the room rather than out of the dark: the ceremony that
                // just closed was already this backdrop at full.
                BackdropLayer.Opacity = 1.0;
                _backdropRaised = true;
            }

            if (kind == DescentShowKind.Live)
            {
                // HOLD THE CEREMONY UNTIL THE LIGHT COMES BACK, and this is choreography rather
                // than defence. The show asks for a sync the moment its bloom starts, but the
                // app's own sixty-second heartbeat is running the whole time too — so without a
                // hold, an offer answered during the drain would drop the ceremony window on top
                // of a crack that is four seconds from finishing, and the night's one animation
                // would be replaced mid-beat by a dialog.
                //
                // Released at the bloom (see FrameCrack) and, as a backstop, on Closed — so a
                // panic key or a throw during the crack cannot leave the ceremony held forever.
                // The catch-up does NOT take a hold here: the director already holds one on its
                // behalf, from before this window existed.
                try
                {
                    App.DescentMigration?.HoldOffers();
                    _holdingOffers = true;
                }
                catch (Exception ex) { Log.Debug("[Fuse] Could not hold the ceremony for the crack: {Error}", ex.Message); }
            }

            Loaded += OnLoadedInternal;
            Closed += OnClosedInternal;

            lock (Live) Live.Add(this);
        }

        /// <summary>
        /// Open a show, or return null if one is already up.
        ///
        /// <para><b>The single-open guard is the point.</b> Two fullscreen topmost windows over each
        /// other is unrecoverable without the panic key, and the paths into here genuinely can
        /// overlap — a live zero and a catch-up cannot coexist (the service settles that before the
        /// first tick), but an ignition firing while a slow catch-up is still fading absolutely
        /// can.</para>
        /// </summary>
        public static DescentFuseWindow? Open(DescentShowKind kind)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null || dispatcher.HasShutdownStarted) return null;

                lock (Live)
                {
                    if (Live.Count > 0)
                    {
                        Log.Information("[Fuse] A show is already on screen — not opening the {Kind} show over it.", kind);
                        return null;
                    }
                }

                var window = new DescentFuseWindow(kind);

                var main = Application.Current?.MainWindow;
                if (main != null && main.IsLoaded && !ReferenceEquals(main, window)) window.Owner = main;

                window.Show();
                window.Activate();

                Log.Information("[Fuse] {Kind} show opened ({Motion}).",
                    kind, window._reduced ? "reduced motion" : "full motion");
                return window;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Fuse] Could not open the {Kind} show.", kind);
                return null;
            }
        }

        /// <summary>
        /// Clear the show off the screen for the panic path. Safe at any beat: the only thing this
        /// window persists is the keepsake flag, which is written the moment the bloom starts — so
        /// a panic during the crack costs a keepsake and nothing else, and a panic after it costs
        /// nothing at all.
        /// </summary>
        public static void ForceCloseAll()
        {
            List<DescentFuseWindow> snapshot;
            lock (Live) snapshot = new List<DescentFuseWindow>(Live);

            foreach (var w in snapshot)
            {
                try { w.Close(); }
                catch (Exception ex) { Log.Debug("[Fuse] Show force-close failed: {Error}", ex.Message); }
            }
        }

        private void OnLoadedInternal(object sender, RoutedEventArgs e)
        {
            try
            {
                _clock.Restart();

                _timer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = FrameInterval };
                _timer.Tick += OnFrame;
                _timer.Start();

                // Paint frame zero immediately rather than waiting 20ms for the first tick: the
                // window is already visible, and a single flash of the bare backdrop before the
                // field appears is the one artefact a fullscreen open cannot hide.
                OnFrame(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Fuse] The show could not start its clock — closing rather than holding the screen.");
                SafeClose();
            }
        }

        private void OnClosedInternal(object? sender, EventArgs e)
        {
            lock (Live) Live.Remove(this);

            // THE BACKSTOP, and it has to be here rather than only at the bloom: Closed fires on
            // every exit path including the panic key's ForceCloseAll and a throw during the crack,
            // and a ceremony held forever by a window that no longer exists would be the one bug
            // this feature could not recover from without a restart.
            ReleaseOfferHold();

            try
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Tick -= OnFrame;
                    _timer = null;
                }
                _clock.Stop();
                BeginAnimation(OpacityProperty, null);
                BackdropLayer.BeginAnimation(OpacityProperty, null);
                ShowLine.BeginAnimation(OpacityProperty, null);
            }
            catch (Exception ex) { Log.Debug("[Fuse] Show teardown: {Error}", ex.Message); }
        }

        // ------------------------------------------------------------------
        // The clock
        // ------------------------------------------------------------------

        private void OnFrame(object? sender, EventArgs e)
        {
            // CLAUDE.md async rules 6/8: a queued tick can land on a dispatcher that has begun
            // shutting down, and a fullscreen window is exactly where that crash is least readable.
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
            if (_closing) return;

            try
            {
                var elapsed = _clock.Elapsed.TotalSeconds;
                if (_kind == DescentShowKind.Ignition) FrameIgnition(elapsed);
                else FrameCrack(elapsed);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Fuse] A show frame threw — closing rather than freezing the screen.");
                SafeClose();
            }
        }

        // ---------------------------------------------------------- crack + handoff

        private void FrameCrack(double elapsed)
        {
            var frame = DescentFuseTimeline.FrameAt(_kind, elapsed, _reduced);
            _visual.SetFrame(frame);

            // The sting lands ON the crack, not before it (owner note 0816: it used to play at the
            // chip's flash-out, 1.5s of freeze ahead of the first hairline). Reduced motion never
            // reaches the Crack stage, so the crossfade stays silent by construction.
            if (frame.Stage == DescentFuseStage.Crack && !_crackSounded)
            {
                _crackSounded = true;
                DescentRoomSfx.PlayCrack();
            }

            if (frame.Stage >= DescentFuseStage.Bloom && !_backdropRaised)
            {
                _backdropRaised = true;
                RaiseBackdrop();

                // THE KEEPSAKE HOOK (§2.3): "persist when the live sequence reached bloom". Written
                // at the START of the bloom, not at its end — the subject has seen the crack, the
                // drain and the light come back by this point, and holding the flag hostage to the
                // last 2.5 seconds would lose the night to a panic key or a power cut for no reason.
                //
                // Live only. The catch-up is deliberately NOT a witnessed night — it is the
                // consolation for having missed one, and the two flags stay honest about which
                // happened (DescentCountdownService.ShouldPlayCatchUp depends on that).
                if (_kind == DescentShowKind.Live && !_witnessedMarked)
                {
                    _witnessedMarked = true;
                    App.DescentCountdown?.MarkLastNightWitnessed();
                }

                // The light is back, so the door may open. If an offer landed during the crack it
                // is replayed right here and the ceremony comes up over the held bloom — which is
                // the choreography the contract describes, and it is why the handoff's first poll
                // can already find it.
                ReleaseOfferHold();
            }

            if (_kind == DescentShowKind.CatchUp)
            {
                // No handoff clock here. The catch-up's whole job is to be OVER before the ceremony
                // offer opens — see DescentShowDirector for the ordering — so it holds its bloom for
                // a moment and dissolves. The offer, held or not yet arrived, lands after.
                var total = DescentFuseTimeline.CatchUpSeconds;
                if (_reduced) total = DescentFuseTimeline.ReducedCrossfadeSeconds;

                if (elapsed >= total + CatchUpHoldSeconds) FadeOutAndClose(CatchUpFadeSeconds);
                return;
            }

            if (frame.SinceBloom < 0) return;

            switch (_handoff.Advance(frame.SinceBloom, App.DescentMigration?.IsCeremonyOpen == true))
            {
                case DescentHandoffAction.Resync:
                    RequestSync();
                    break;

                case DescentHandoffAction.CrossfadeToCeremony:
                    BeginCeremonyHandoff();
                    break;

                case DescentHandoffAction.SpeakAwaits:
                    Log.Information("[Fuse] No ceremony offer within {Seconds}s — showing the standing line and closing.",
                        DescentFuseHandoff.TimeoutSeconds);
                    ShowLine.Text = DescentFuseCopy.ShowAwaits;
                    FadeIn(ShowLine, 0.9);
                    break;

                case DescentHandoffAction.Close:
                    // Two ways to get here. After a handoff the crossfade has already run this
                    // window down to nothing, so it just goes. After the standing line, "closes
                    // softly" is the contract's wording and it means a fade, not a vanish.
                    if (HandedOffToCeremony) SafeClose();
                    else FadeOutAndClose(0.9);
                    break;
            }
        }

        /// <summary>
        /// Ask for a sync. This is the whole "force an immediate sync" of §2.3 — the fuse does not
        /// open the ceremony, it just makes sure the conversation that would offer it happens.
        ///
        /// <para>Fire-and-forget, and every failure mode is fine: offline returns false, the
        /// thirty-second cooldown returns false, a dead network throws inside and is logged. The
        /// handoff clock simply asks again in eight seconds.</para>
        /// </summary>
        private static void RequestSync()
        {
            try { _ = App.ProfileSync?.SyncProfileAsync(); }
            catch (Exception ex) { Log.Debug("[Fuse] Handoff sync could not be started: {Error}", ex.Message); }
        }

        /// <summary>
        /// The ceremony is up. Dissolve into it.
        ///
        /// <para><b>THE Z STORY, and it is the subtle part.</b> Both windows are Topmost, and the
        /// ceremony was shown second — so it is currently ABOVE this one, which would make a
        /// crossfade invisible. Re-asserting <see cref="Window.Topmost"/> pushes this window back to
        /// the front of the topmost band, and WPF's setter does that with SWP_NOACTIVATE, so the
        /// ceremony KEEPS keyboard focus the whole way through. The fade then reveals the ceremony
        /// underneath, and the director activates it once this window is gone.</para>
        /// </summary>
        private void BeginCeremonyHandoff()
        {
            HandedOffToCeremony = true;
            Log.Information("[Fuse] The ceremony is up — dissolving into it.");

            try
            {
                Topmost = false;
                Topmost = true;
            }
            catch (Exception ex) { Log.Debug("[Fuse] Z re-assert failed: {Error}", ex.Message); }

            FadeOut(DescentFuseHandoff.CrossfadeSeconds);
        }

        // ------------------------------------------------------------- the ignition

        private void FrameIgnition(double elapsed)
        {
            _visual.SetIgnition(elapsed);

            var lineOpacity = DescentIgnitionTimeline.LineOpacity(elapsed, _reduced);
            if (lineOpacity > 0 && ShowLine.Text.Length == 0) ShowLine.Text = DescentFuseCopy.IgnitionLine;
            ShowLine.Opacity = lineOpacity;

            Opacity = DescentIgnitionTimeline.ShowOpacity(elapsed, _reduced);

            if (elapsed >= DescentIgnitionTimeline.TotalSeconds(_reduced)) SafeClose();
        }

        // ------------------------------------------------------------------
        // Fades
        // ------------------------------------------------------------------

        /// <summary>
        /// Bring the ceremony's backdrop up with the bloom, so the room the show is standing in
        /// arrives at the same moment the light does.
        /// </summary>
        private void RaiseBackdrop()
        {
            try
            {
                var seconds = _reduced ? DescentFuseTimeline.ReducedCrossfadeSeconds : 1.6;
                var up = new DoubleAnimation(BackdropLayer.Opacity, 1.0, TimeSpan.FromSeconds(seconds))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                BackdropLayer.BeginAnimation(OpacityProperty, up);
            }
            catch (Exception ex) { Log.Debug("[Fuse] Backdrop raise failed: {Error}", ex.Message); }
        }

        private static void FadeIn(UIElement element, double seconds)
        {
            try
            {
                element.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromSeconds(seconds)));
            }
            catch (Exception ex) { Log.Debug("[Fuse] Line fade failed: {Error}", ex.Message); }
        }

        /// <summary>
        /// Fade the whole window out. Window opacity below 1 makes the HWND layered with a CONSTANT
        /// alpha (SetLayeredWindowAttributes) — which is a very different animal from
        /// AllowsTransparency's per-pixel compositing, and is not the path that wedges the render
        /// thread. The window stays opaque as far as WPF's compositor is concerned.
        /// </summary>
        private void FadeOut(double seconds)
        {
            try
            {
                BeginAnimation(OpacityProperty,
                    new DoubleAnimation(Opacity, 0.0, TimeSpan.FromSeconds(seconds)));
            }
            catch (Exception ex)
            {
                Log.Debug("[Fuse] Window fade failed: {Error}", ex.Message);
                Opacity = 0;
            }
        }

        private void FadeOutAndClose(double seconds)
        {
            if (_closing) return;
            _closing = true;
            FadeOut(seconds);

            var wait = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
            {
                Interval = TimeSpan.FromSeconds(seconds),
            };
            wait.Tick += (s, _) =>
            {
                try { ((DispatcherTimer)s!).Stop(); } catch { /* teardown race */ }
                _closing = false;
                SafeClose();
            };
            wait.Start();
        }

        /// <summary>Drop this window's hold, at most once.</summary>
        private void ReleaseOfferHold()
        {
            if (!_holdingOffers) return;
            _holdingOffers = false;
            try { App.DescentMigration?.ReleaseOffers(); }
            catch (Exception ex) { Log.Debug("[Fuse] Releasing the ceremony hold failed: {Error}", ex.Message); }
        }

        private void SafeClose()
        {
            try { Close(); }
            catch (Exception ex) { Log.Debug("[Fuse] Show close failed: {Error}", ex.Message); }
        }

        // ------------------------------------------------------------------
        // Theme
        // ------------------------------------------------------------------

        /// <summary>
        /// The mod's pink, or the app's own if the resource is missing. Read as a Color rather than
        /// a Brush because the show builds thirty-three alpha steps from it and a SolidColorBrush
        /// would only be step thirty-three.
        /// </summary>
        private Color ResolveAccent()
        {
            try
            {
                if (TryFindResource("PinkColor") is Color c) return c;
                if (TryFindResource("PinkBrush") is SolidColorBrush b) return b.Color;
            }
            catch (Exception ex) { Log.Debug("[Fuse] Accent lookup failed: {Error}", ex.Message); }

            return Color.FromRgb(0xFF, 0x69, 0xB4);
        }
    }
}
