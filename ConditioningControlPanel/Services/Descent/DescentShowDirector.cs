using System;
using System.Windows;
using System.Windows.Threading;
using Serilog;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>
    /// THE ZERO SHOW's stage manager (CONTRACT-FUSE-0816 §2.3/§2.4) — the one place that decides
    /// WHICH show plays, WHEN, and what has to be true first.
    ///
    /// <para><b>Why a director and not three subscriptions.</b> The three shows are mutually
    /// exclusive and they are exclusive with the ceremony too: a catch-up crack must finish before
    /// a migration offer opens, an ignition must not start while a catch-up is still fading, and a
    /// live zero and a catch-up can never both be owed. Spread across the window, the countdown
    /// service and the sync path, those rules would be three half-answers; here they are one
    /// readable sequence with one guard.</para>
    ///
    /// <para><b>THE CATCH-UP ORDERING, which is the only genuinely racy part of the feature.</b> On
    /// a launch that owes the condensed crack, the ceremony offer is arriving on the SAME startup
    /// sync that the crack is about to play over. The ordering is made deterministic like this:</para>
    /// <list type="number">
    /// <item><see cref="Arm"/> runs synchronously inside <c>App.OnStartup</c>, on the UI thread,
    /// and takes <c>DescentMigrationService.HoldOffers()</c> BEFORE returning. An offer that is
    /// already in flight cannot beat it: <c>OfferReceived</c> marshals its window-open onto this
    /// same dispatcher, and the dispatcher does not pump until OnStartup has returned.</item>
    /// <item>The crack opens on the first pumped callback and runs its six seconds.</item>
    /// <item>On its close the crack marks itself played and calls <c>ReleaseOffers()</c>, which
    /// REPLAYS any offer that landed while the hold was up. The ceremony therefore opens strictly
    /// after the fuse window is gone — never under it, never over it.</item>
    /// <item>If no offer landed, the release is a no-op and the next sync's offer opens the
    /// ceremony normally, with the hold at zero.</item>
    /// </list>
    /// <para>The hold is depth-counted and always released in a <c>finally</c>-shaped path (the
    /// window's Closed handler fires on every exit, including the panic key's ForceCloseAll), so
    /// there is no arrangement of crashes and panics that leaves offers permanently held.</para>
    ///
    /// <para><b>Dormant with the service.</b> No cached ceremony timestamp ⇒ no ZeroReached, no
    /// ZeroPassedWhileAway, no catch-up, and <see cref="Arm"/> costs two event subscriptions.</para>
    /// </summary>
    public sealed class DescentShowDirector : IDisposable
    {
        private readonly DescentHeartbeat _heartbeat = new();

        private bool _armed;
        private bool _disposed;
        private bool _catchUpOwed;
        private bool _catchUpHolding;

        /// <summary>The heartbeat hook's player, exposed so a teardown can silence it.</summary>
        public DescentHeartbeat Heartbeat => _heartbeat;

        // ------------------------------------------------------------------
        // Arming
        // ------------------------------------------------------------------

        /// <summary>
        /// Wire the shows. Called once from App.OnStartup immediately after
        /// <c>DescentCountdownService.Start()</c>, which is what makes the catch-up hold above
        /// unbeatable — see the ordering note on the class.
        /// </summary>
        public void Arm()
        {
            if (_disposed || _armed) return;
            _armed = true;

            try
            {
                var fuse = App.DescentCountdown;
                if (fuse != null)
                {
                    fuse.ZeroReached += OnZeroReached;
                    fuse.PhaseChanged += OnPhaseChanged;
                }

                var migration = App.DescentMigration;
                if (migration != null) migration.CeremonyClosed += OnCeremonyClosed;

                // Settled by Start() before the first tick, and never true at the same time as a
                // live zero. Read once: the flags behind it are about to be written by the show.
                _catchUpOwed = fuse?.ShouldPlayCatchUp == true;
                if (!_catchUpOwed) return;

                Log.Information("[Fuse] The instant passed while the app was closed — the catch-up crack is owed.");

                // THE HOLD, taken here and nowhere else. See the ordering note.
                if (migration != null)
                {
                    migration.HoldOffers();
                    _catchUpHolding = true;
                }

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null || dispatcher.HasShutdownStarted)
                {
                    ReleaseCatchUpHold();
                    return;
                }

                // Normal priority, NOT Loaded: a Loaded-priority post can be starved indefinitely
                // by a busy startup (the repo has been bitten by exactly that), and a show that
                // never opens would strand the offer hold with it.
                _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(PlayCatchUp));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Fuse] The show director could not arm — the countdown is unaffected.");
                ReleaseCatchUpHold();
            }
        }

        // ------------------------------------------------------------------
        // The live zero (§2.3)
        // ------------------------------------------------------------------

        private void OnZeroReached(object? sender, EventArgs e)
        {
            // CLAUDE.md rule 8: the app may be shutting down under a queued event.
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;

            try
            {
                // The countdown's own sound stops the instant the show has its own. Also covers the
                // case where Terminal started it and the phase never announced Zero separately.
                _heartbeat.Stop();

                var window = DescentFuseWindow.Open(DescentShowKind.Live);
                if (window is null) return;

                window.Closed += (s, _) =>
                {
                    if (s is DescentFuseWindow w && w.HandedOffToCeremony) FocusCeremony();
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Fuse] The live zero show could not start.");
            }
        }

        // ------------------------------------------------------------------
        // The catch-up (§2.4)
        // ------------------------------------------------------------------

        private void PlayCatchUp()
        {
            try
            {
                if (_disposed) { ReleaseCatchUpHold(); return; }
                if (Application.Current?.Dispatcher?.HasShutdownStarted != false) { ReleaseCatchUpHold(); return; }

                var window = DescentFuseWindow.Open(DescentShowKind.CatchUp);
                if (window is null)
                {
                    // Nothing played, so nothing is marked — the next launch is still owed it.
                    Log.Warning("[Fuse] The catch-up crack could not open; releasing the offer hold so the ceremony is not stuck behind it.");
                    ReleaseCatchUpHold();
                    return;
                }

                window.Closed += (_, _) =>
                {
                    // ORDER MATTERS. Mark first: if releasing the hold throws, the crack has still
                    // played and must not play again. Then release, which replays a held offer and
                    // opens the ceremony over a screen this window has already left.
                    try { App.DescentCountdown?.MarkCatchUpCrackPlayed(); }
                    catch (Exception ex) { Log.Debug("[Fuse] Catch-up flag write failed: {Error}", ex.Message); }

                    ReleaseCatchUpHold();
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Fuse] The catch-up crack failed to start.");
                ReleaseCatchUpHold();
            }
        }

        private void ReleaseCatchUpHold()
        {
            if (!_catchUpHolding) return;
            _catchUpHolding = false;
            try { App.DescentMigration?.ReleaseOffers(); }
            catch (Exception ex) { Log.Debug("[Fuse] Releasing the offer hold failed: {Error}", ex.Message); }
        }

        // ------------------------------------------------------------------
        // The ignition (§2.4)
        // ------------------------------------------------------------------

        /// <summary>
        /// The ceremony window closed. <paramref name="committed"/> is the whole decision: a
        /// "Not tonight" close is a deferral and gets nothing, because there is no Year One to
        /// light yet. Runs for BOTH the live and the catch-up ceremonies — the ignition belongs to
        /// the choice, not to the night.
        /// </summary>
        private void OnCeremonyClosed(object? sender, bool committed)
        {
            if (!committed) return;
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;

            try
            {
                var window = DescentFuseWindow.Open(DescentShowKind.Ignition);
                if (window is null) { RestoreChrome(); BeginFirstLight(); return; }

                window.Closed += (_, _) => { RestoreChrome(); BeginFirstLight(); };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Fuse] The Year One ignition could not start.");
                RestoreChrome();
                BeginFirstLight();
            }
        }

        // ------------------------------------------------------------------
        // The first light (§2.4, owner ruling 2026-08-16)
        // ------------------------------------------------------------------

        /// <summary>
        /// ONE PER PROCESS. The reveal is the payment for taking the ceremony, and paying it twice
        /// in one session would be a bug rather than a bonus — the ignition can close more than once
        /// in theory (a second ceremony cannot happen, but a handler firing twice is the kind of
        /// thing that is only ever discovered on the night).
        ///
        /// <para><b>Deliberately NOT persisted.</b> If the app dies between the commit and the
        /// reveal, the next launch simply has an open gate and a visible spiral — the surfaces are
        /// unlocked, every ordinary door works, and the user has lost an animation rather than an
        /// unlock. A settings flag would buy replaying a four-second intro at some random later
        /// launch, out of the context that gave it its meaning, at the cost of another line of
        /// account state that has to be right forever.</para>
        /// </summary>
        private static bool _firstLightPlayed;

        /// <summary>The commit path's entry: the one-shot guard, then the reveal.</summary>
        private static void BeginFirstLight()
        {
            if (_firstLightPlayed) return;
            _firstLightPlayed = true;
            RunFirstLightReveal();
        }

        /// <summary>
        /// THE FIRST LIGHT, whole (CONTRACT-FUSE-0816 §2.4, owner ruling 2026-08-16): "hide the
        /// spiral till the ceremony finishes, and go to the profile page, and have some highlight
        /// animation that catches the user's attention and reveals the spiral, even opens it for
        /// them."
        ///
        /// <list type="number">
        /// <item>Bring the main window forward and navigate to the profile tab THROUGH THE REAL
        /// PATH — <c>MainWindow.ShowTab("discord")</c>, the same door the nav rail, the bark rules
        /// and the command palette all use. A parallel navigation would be a second way to change
        /// tabs that the rest of the app does not know about.</item>
        /// <item>Repaint the spiral surfaces (the withhold opened at the commit) and pulse the
        /// Trainer Card's plate — opacity and transform only, skipped under reduced motion.</item>
        /// <item>Open the map FOR them, with the reveal playing inside it
        /// (<c>SpiralMapWindow.ShowMapFirstLight</c>), which is the door that tolerates the descent
        /// block still being in flight.</item>
        /// </list>
        ///
        /// <para><b>INTERNAL, AND THE DEMO RIG'S ENTRY POINT.</b> The owner's capture rig calls this
        /// directly to film the reveal without standing up a server, a veteran account and a
        /// ceremony. It is deliberately not public, has no command-line wiring and is not on any
        /// menu — the rig adds its own scratch hook — and it deliberately skips the one-shot guard
        /// so the rig can re-run it; <see cref="BeginFirstLight"/> is the guarded path the ceremony
        /// itself takes.</para>
        /// </summary>
        internal static void RunFirstLightReveal()
        {
            // CLAUDE.md async rules 6/8 on every hop: this is reached from a window's Closed
            // handler, from a fallback path and from a test rig, and none of them can promise a
            // living dispatcher.
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted) return;

            if (dispatcher.CheckAccess()) FirstLightOnUiThread();
            else _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(FirstLightOnUiThread));
        }

        private static void FirstLightOnUiThread()
        {
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;

            try
            {
                Log.Information("[Fuse] First light: the spiral is being revealed to this account for the first time.");

                if (Application.Current?.MainWindow is not MainWindow main || !main.IsLoaded)
                {
                    // No window to walk them through, but the map is its own top-level HWND and does
                    // not need one. Better a reveal with no walk-up than no reveal.
                    Log.Debug("[Fuse] First light: no main window — opening the map on its own.");
                    OpenFirstLightMap();
                    return;
                }

                try
                {
                    if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
                    main.Activate();
                }
                catch (Exception ex) { Log.Debug("[Fuse] First light could not bring the window forward: {Error}", ex.Message); }

                main.ShowTab("discord");

                // The pulse owns the hand-off so the map opens AFTER the plate has finished asking
                // for attention — and the callback runs on every path, including the ones where
                // there is no plate to pulse.
                main.PlayFirstLightHighlight(OpenFirstLightMap);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Fuse] The first-light reveal failed on the way to the profile tab — opening the map directly.");
                OpenFirstLightMap();
            }
        }

        private static void OpenFirstLightMap()
        {
            try { SpiralMapWindow.ShowMapFirstLight(); }
            catch (Exception ex) { Log.Error(ex, "[Fuse] The first-light map could not open."); }
        }

        /// <summary>
        /// Give the room its colour back.
        ///
        /// <para>The dimming holds step 4 through zero on purpose, so the chrome does not brighten
        /// while the show is opening. Once the ignition is over that reason has expired, and
        /// <c>DescentCountdownService.DimStep</c> reads 0 for a migrated account — so the ONLY thing
        /// left to do is ask the app's single writer of the neutral palette to re-derive. Never a
        /// private colour cache: <c>RefreshThemeAwareElements</c> re-reads the active mod every
        /// time, which is exactly why the restore needs no saved "original" to get out of sync
        /// with.</para>
        /// </summary>
        private static void RestoreChrome()
        {
            try { MainWindow.RestoreFuseChrome(); }
            catch (Exception ex) { Log.Debug("[Fuse] Chrome restore failed: {Error}", ex.Message); }
        }

        // ------------------------------------------------------------------
        // The heartbeat hook (§2.4)
        // ------------------------------------------------------------------

        private void OnPhaseChanged(object? sender, DescentFusePhaseChangedEventArgs e)
        {
            try
            {
                if (e.Current == DescentFusePhase.Terminal) _heartbeat.Start();
                else _heartbeat.Stop();
            }
            catch (Exception ex) { Log.Debug("[Fuse] Heartbeat phase handling failed: {Error}", ex.Message); }
        }

        // ------------------------------------------------------------------

        /// <summary>Find the ceremony and hand it the keyboard, now that the fuse is out of the way.</summary>
        private static void FocusCeremony()
        {
            try
            {
                foreach (Window w in Application.Current?.Windows ?? new WindowCollection())
                {
                    if (w is not DescentCeremonyWindow ceremony) continue;
                    ceremony.Activate();
                    return;
                }
            }
            catch (Exception ex) { Log.Debug("[Fuse] Could not focus the ceremony: {Error}", ex.Message); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                var fuse = App.DescentCountdown;
                if (fuse != null)
                {
                    fuse.ZeroReached -= OnZeroReached;
                    fuse.PhaseChanged -= OnPhaseChanged;
                }
                var migration = App.DescentMigration;
                if (migration != null) migration.CeremonyClosed -= OnCeremonyClosed;
            }
            catch { /* teardown races are not worth a log line */ }

            ReleaseCatchUpHold();
            _heartbeat.Dispose();
        }
    }
}
