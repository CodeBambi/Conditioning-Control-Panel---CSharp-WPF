using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Animation.Easings;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Services.Descent;
using Serilog;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// THE FUSE AT ZERO (CONTRACT-FUSE-0816 §2.3/§2.4) — the fullscreen show the countdown has been
    /// counting down to, and the two shows that hang off it.
    ///
    /// PORTED from ConditioningControlPanel/Windows/DescentFuseWindow.xaml.cs. The timeline itself
    /// (<see cref="DescentFuseTimeline"/>, <see cref="DescentIgnitionTimeline"/>,
    /// <see cref="DescentFuseHandoff"/>) already lives in Core, so the clock and every beat boundary
    /// are the real ones. Deviations:
    ///  - <c>DescentFuseStageVisual</c> is a WPF <c>DrawingVisual</c> in the other head, so
    ///    <c>StageHost</c> stays empty and the per-frame handoff to it is a no-op. The backdrop,
    ///    the vignette, the line and the fades are all real.
    ///  - <c>DispatcherTimer</c> is Avalonia's; <c>DispatcherPriority.Render</c> exists on both.
    ///  - WPF's <c>BeginAnimation(OpacityProperty, …)</c> becomes an Avalonia <c>Animation</c>
    ///    run from code (<see cref="FadeTo"/>), with <c>FillMode.Forward</c> so the ramp sticks at
    ///    its end value the way WPF's does.
    ///  - <c>MotionFx.Level</c> is <see cref="AmbientFxCanvas.Env"/>.<c>Level</c> here, which reads
    ///    the same <c>CoreSettings.Current.MotionLevel</c>. Reduced motion is therefore REAL on this
    ///    head and the Core timelines get the flag they expect.
    ///  - <c>Application.Current.MainWindow</c> is the desktop lifetime's <c>MainWindow</c>, and
    ///    Avalonia has no settable <c>Owner</c>, so ownership is <c>Show(parent)</c>.
    ///  - <c>DescentRoomSfx</c>, <c>App.DescentMigration</c>, <c>App.DescentCountdown</c> and
    ///    <c>App.ProfileSync</c> are still in the WPF head, so each is a stub below.
    ///  - <c>DescentFuseCopy</c> is likewise still in the WPF head; its two sentences are inlined.
    ///
    /// <para><b>STILL UNREACHABLE — blocked on unported callers, not on this window.</b>
    /// <see cref="Open"/> is complete and takes its owner correctly; what is missing is everything
    /// that would call it. All four WPF call sites were checked:</para>
    /// <list type="bullet">
    ///   <item><c>DescentShowDirector</c>
    ///   (ConditioningControlPanel/Services/Descent/DescentShowDirector.cs:156/233/406) opens the
    ///   Live, CatchUp and Ignition shows. CCP.Core/Services/Descent/ carries the SHOW — the
    ///   timelines and the handoff this file already uses — but not the director and not
    ///   <c>DescentCountdownService</c>, so nothing on this head knows a countdown reached zero.
    ///   That is also what MainShellWindow.DescentFuse.cs is waiting on.</item>
    ///   <item><see cref="ForceCloseAll"/> is the panic path's
    ///   (MainWindow.StartStop.cs:525), and this head has no <c>StopEngine</c> to call it from.</item>
    /// </list>
    /// <para>The gate is the director's, not this window's: it opens the Live show only when the
    /// countdown phase reaches Zero. Opening the show from anywhere else would put a fullscreen
    /// topmost surface over the user with no countdown behind it, so no substitute entry point
    /// was invented.</para>
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

        /// <summary>How long the catch-up holds its bloom before dissolving.</summary>
        private const double CatchUpHoldSeconds = 1.2;

        private const double CatchUpFadeSeconds = 0.8;

        // ponytail: needs ConditioningControlPanel/Services/Descent/DescentFuseCopy.cs, inlined
        // verbatim until it moves to Core. Two sentences, and between them every word the show
        // says. The class itself is portable - static strings, no WPF - so this is a git mv, not a
        // port; Core's own DescentFuseHandoff already cites DescentFuseCopy.ShowAwaits by name.
        private const string ShowAwaitsLine = "The ceremony awaits.";
        private const string IgnitionCopyLine = "Year One. The spiral is yours.";

        private readonly DescentShowKind _kind;
        private readonly bool _reduced;
        private readonly Stopwatch _clock = new();
        private readonly DescentFuseHandoff _handoff = new();

        private readonly Border _backdropLayer;
        private readonly TextBlock _showLine;

        private DispatcherTimer? _timer;
        private bool _witnessedMarked;
        private bool _backdropRaised;
        private bool _crackSounded;
        private bool _closing;

        private bool _holdingOffers;

        /// <summary>
        /// TRUE once this window saw the ceremony come up and started dissolving into it.
        /// </summary>
        public bool HandedOffToCeremony { get; private set; }

        /// <summary>Render constructor: the live show, so --render-all can discover the window.</summary>
        internal DescentFuseWindow() : this(DescentShowKind.Live)
        {
            // The show is wordless until the handoff times out, so a bare render would be a black
            // rectangle. Stand the room up at its bloom instead: the backdrop at full and the
            // standing line visible, which is the one frame of this window that has anything in it.
            _backdropLayer.Opacity = 1.0;
            _showLine.Text = ShowAwaitsLine;
            _showLine.Opacity = 1.0;
        }

        private DescentFuseWindow(DescentShowKind kind)
        {
            _kind = kind;

            AvaloniaXamlLoader.Load(this);

            _backdropLayer = this.FindControl<Border>("BackdropLayer")!;
            _showLine = this.FindControl<TextBlock>("ShowLine")!;

            // MotionFx.Level != MotionLevel.Full, verbatim. MotionFx itself is WPF Storyboard code
            // and stays head-side, but its DECISION is CoreSettings.Current.MotionLevel, which this
            // head already reads through AmbientFxCanvas.Env. The flag is threaded into every Core
            // timeline call below, so "Reduced" now really does shorten the show here.
            // The one WPF input with no twin is the OS animation-effects cap (MotionFx.ResolveLevel
            // forces Reduced when it is off); Avalonia exposes no cross-platform equivalent, and
            // that cap can only ever REMOVE motion, so its absence shows what the user picked.
            _reduced = AmbientFxCanvas.Env.Level != MotionLevel.Full;

            // ponytail: needs ConditioningControlPanel/Controls/DescentFuseStageVisual.cs, a WPF
            // DrawingVisual that cannot cross - it is redrawn for this head, not moved. StageHost
            // is left empty; every other layer is real.

            if (kind == DescentShowKind.Ignition)
            {
                // The ignition opens INTO the room rather than out of the dark: the ceremony that
                // just closed was already this backdrop at full.
                _backdropLayer.Opacity = 1.0;
                _backdropRaised = true;
            }

            if (kind == DescentShowKind.Live)
            {
                // ponytail: needs DescentMigrationService.HoldOffers from
                // ConditioningControlPanel/Services/Descent/DescentMigrationService.cs (reached as
                // App.DescentMigration). No Core seam exposes it.
                // Holding the ceremony back until the light returns is choreography, not defence:
                // without it an offer answered during the drain drops the ceremony on top of a
                // crack that is four seconds from finishing.
                _holdingOffers = true;
            }

            Loaded += OnLoadedInternal;
            Closed += OnClosedInternal;

            lock (Live) Live.Add(this);
        }

        /// <summary>
        /// Open a show, or return null if one is already up. The single-open guard is the point:
        /// two fullscreen topmost windows over each other is unrecoverable without the panic key.
        /// </summary>
        public static DescentFuseWindow? Open(DescentShowKind kind)
        {
            try
            {
                lock (Live)
                {
                    if (Live.Count > 0)
                    {
                        Log.Information("[Fuse] A show is already on screen — not opening the {Kind} show over it.", kind);
                        return null;
                    }
                }

                var window = new DescentFuseWindow(kind);

                // WPF: `if (main != null && main.IsLoaded && !ReferenceEquals(main, window))
                // window.Owner = main;`. Avalonia's Owner is read-only - ownership is passed to
                // Show - so the same guards pick between the two overloads.
                //
                // IsVisible, not WPF's IsLoaded, and the difference matters: Avalonia's
                // Show(parent) THROWS when the parent is not visible, and a shell minimized to the
                // tray is loaded but not visible - which is exactly the state the app is in when a
                // countdown reaches zero unattended. A throw here is caught below and answers null,
                // i.e. the show silently never opens. Ownerless is a fine fallback: this is a
                // fullscreen topmost window, not a dialog.
                var main = (global::Avalonia.Application.Current?.ApplicationLifetime
                    as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (main is { IsVisible: true } && !ReferenceEquals(main, window)) window.Show(main);
                else window.Show();
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

        /// <summary>Clear the show off the screen for the panic path. Safe at any beat.</summary>
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

        private void OnLoadedInternal(object? sender, EventArgs e)
        {
            try
            {
                _clock.Restart();

                _timer = new DispatcherTimer(FrameInterval, DispatcherPriority.Render, OnFrame);
                _timer.Start();

                // Paint frame zero immediately rather than waiting 20ms for the first tick.
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

            ReleaseOfferHold();

            try
            {
                _timer?.Stop();
                _timer = null;
                _clock.Stop();
            }
            catch (Exception ex) { Log.Debug("[Fuse] Show teardown: {Error}", ex.Message); }
        }

        // ------------------------------------------------------------------
        // The clock
        // ------------------------------------------------------------------

        private void OnFrame(object? sender, EventArgs e)
        {
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

            // The sting lands ON the crack, not before it. Reduced motion never reaches the Crack
            // stage, so the crossfade stays silent by construction.
            if (frame.Stage == DescentFuseStage.Crack && !_crackSounded)
            {
                _crackSounded = true;
                // ponytail: needs DescentRoomSfx.PlayCrack from
                // ConditioningControlPanel/Services/Descent/DescentRoomSfx.cs. It cannot come over
                // as-is: it reads App.DescentCountdown.AudioEnabled and resolves its file through
                // ChaosSfx.ResolvePath, neither of which is in Core. CoreAudio.PlayOneShot is the
                // seam it would use - and is itself unseeded on this head (App.axaml.cs), so the
                // sting would still be silent.
            }

            if (frame.Stage >= DescentFuseStage.Bloom && !_backdropRaised)
            {
                _backdropRaised = true;
                RaiseBackdrop();

                // THE KEEPSAKE HOOK (§2.3): written at the START of the bloom, not at its end.
                // Live only — the catch-up is deliberately NOT a witnessed night.
                if (_kind == DescentShowKind.Live && !_witnessedMarked)
                {
                    _witnessedMarked = true;
                    // ponytail: needs DescentCountdownService.MarkLastNightWitnessed from
                    // ConditioningControlPanel/Services/Descent/DescentCountdownService.cs. The
                    // keepsake it writes is a settings field, but nothing in Core owns the write.
                }

                // The light is back, so the door may open.
                ReleaseOfferHold();
            }

            if (_kind == DescentShowKind.CatchUp)
            {
                // No handoff clock here: the catch-up's whole job is to be OVER before the ceremony
                // offer opens, so it holds its bloom for a moment and dissolves.
                var total = _reduced
                    ? DescentFuseTimeline.ReducedCrossfadeSeconds
                    : DescentFuseTimeline.CatchUpSeconds;

                if (elapsed >= total + CatchUpHoldSeconds) FadeOutAndClose(CatchUpFadeSeconds);
                return;
            }

            if (frame.SinceBloom < 0) return;

            // ponytail: needs DescentMigrationService.IsCeremonyOpen
            // (ConditioningControlPanel/Services/Descent/DescentMigrationService.cs) — false here,
            // so this head always walks the timeout branch and shows the standing line rather than
            // crossfading. That is the contract's own no-offer path, not a fake one: the ceremony
            // really is still coming on the next sync, so nothing is written and nothing loops.
            switch (_handoff.Advance(frame.SinceBloom, ceremonyOpen: false))
            {
                case DescentHandoffAction.Resync:
                    // ponytail: needs ProfileSyncService.SyncProfileAsync from
                    // ConditioningControlPanel/Services/Settings/ProfileSyncService.cs. No Core seam.
                    break;

                case DescentHandoffAction.CrossfadeToCeremony:
                    BeginCeremonyHandoff();
                    break;

                case DescentHandoffAction.SpeakAwaits:
                    Log.Information("[Fuse] No ceremony offer within {Seconds}s — showing the standing line and closing.",
                        DescentFuseHandoff.TimeoutSeconds);
                    _showLine.Text = ShowAwaitsLine;
                    FadeTo(_showLine, _showLine.Opacity, 1.0, 0.9);
                    break;

                case DescentHandoffAction.Close:
                    // After a handoff the crossfade has already run this window down to nothing, so
                    // it just goes. After the standing line, "closes softly" means a fade.
                    if (HandedOffToCeremony) SafeClose();
                    else FadeOutAndClose(0.9);
                    break;
            }
        }

        /// <summary>
        /// The ceremony is up. Dissolve into it. WPF re-asserted Topmost here to push this window
        /// back to the front of the topmost band without stealing focus; Avalonia has no
        /// SWP_NOACTIVATE equivalent to lean on, and the ceremony would lose focus, so the Z
        /// re-assert is deliberately left out.
        /// </summary>
        private void BeginCeremonyHandoff()
        {
            HandedOffToCeremony = true;
            Log.Information("[Fuse] The ceremony is up — dissolving into it.");
            FadeTo(this, Opacity, 0.0, DescentFuseHandoff.CrossfadeSeconds);
        }

        // ------------------------------------------------------------- the ignition

        private void FrameIgnition(double elapsed)
        {
            var lineOpacity = DescentIgnitionTimeline.LineOpacity(elapsed, _reduced);
            if (lineOpacity > 0 && (_showLine.Text?.Length ?? 0) == 0) _showLine.Text = IgnitionCopyLine;
            _showLine.Opacity = lineOpacity;

            Opacity = DescentIgnitionTimeline.ShowOpacity(elapsed, _reduced);

            if (elapsed >= DescentIgnitionTimeline.TotalSeconds(_reduced)) SafeClose();
        }

        // ------------------------------------------------------------------
        // Fades
        // ------------------------------------------------------------------

        /// <summary>
        /// WPF's <c>BeginAnimation(OpacityProperty, new DoubleAnimation(from, to, seconds))</c>,
        /// in Avalonia's terms. FillMode.Forward is what makes the ramp stick at <paramref name="to"/>
        /// once it ends, which is the WPF behaviour the show depends on at the bloom.
        /// </summary>
        private static void FadeTo(Visual target, double from, double to, double seconds)
        {
            try
            {
                _ = new Animation
                {
                    Duration = TimeSpan.FromSeconds(seconds),
                    Easing = new QuadraticEaseOut(),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, from) } },
                        new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, to) } },
                    },
                }.RunAsync(target);
            }
            catch (Exception ex)
            {
                Log.Debug("[Fuse] Fade failed: {Error}", ex.Message);
                target.Opacity = to;
            }
        }

        /// <summary>
        /// Bring the ceremony's backdrop up with the bloom, so the room the show is standing in
        /// arrives at the same moment the light does.
        /// </summary>
        private void RaiseBackdrop()
        {
            var seconds = _reduced ? DescentFuseTimeline.ReducedCrossfadeSeconds : 1.6;
            FadeTo(_backdropLayer, _backdropLayer.Opacity, 1.0, seconds);
        }

        private void FadeOutAndClose(double seconds)
        {
            if (_closing) return;
            _closing = true;

            FadeTo(this, Opacity, 0.0, seconds);
            DispatcherTimer.RunOnce(SafeClose, TimeSpan.FromSeconds(seconds), DispatcherPriority.Normal);
        }

        /// <summary>Drop this window's hold, at most once.</summary>
        private void ReleaseOfferHold()
        {
            if (!_holdingOffers) return;
            _holdingOffers = false;
            // ponytail: needs DescentMigrationService.ReleaseOffers - the paired release for the
            // HoldOffers above, same file, same missing seam.
        }

        private void SafeClose()
        {
            try { Close(); }
            catch (Exception ex) { Log.Debug("[Fuse] Show close failed: {Error}", ex.Message); }
        }
    }
}
