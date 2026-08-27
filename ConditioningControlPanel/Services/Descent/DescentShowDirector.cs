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

        /// <summary>
        /// THE PRE-ZERO HOLD (0825 hunt, F2). Taken while the fuse is lit and zero has not come,
        /// released only once the live window holds the ceremony itself. Before this, nothing held
        /// offers before zero at all: the server fires on ITS clock, so a client two minutes slow
        /// got the ceremony window while its corner clock still read gold — and then the crack
        /// opened fullscreen over the open ceremony. With the hold, an early offer simply waits
        /// under the bloom, which is the choreography the contract describes.
        /// </summary>
        private bool _preZeroHolding;

        private DispatcherTimer? _retryTimer;
        private int _retryAttempts;

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
                    fuse.ZeroObservedLate += OnZeroObservedLate;
                    fuse.PhaseChanged += OnPhaseChanged;
                }

                var migration = App.DescentMigration;
                if (migration != null) migration.CeremonyClosed += OnCeremonyClosed;

                // Settled by Start() before the first tick, and never true at the same time as a
                // live zero. Read once: the flags behind it are about to be written by the show.
                _catchUpOwed = fuse?.ShouldPlayCatchUp == true;
                if (!_catchUpOwed)
                {
                    EnsurePreZeroHold();
                    return;
                }

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

                // NOTHING TO REVEAL (0825 F2, scenario B). An account that already answered — on
                // this machine before a slow client clock reached zero, or on another device — has
                // had its first light. Playing the crack again would end in forty-five seconds of
                // refused resyncs and "The ceremony awaits." said to someone who just finished it.
                if (AlreadyAnswered() || App.DescentMigration?.IsCeremonyOpen == true)
                {
                    Log.Information("[Fuse] Zero, but the question is already answered or on screen — no live show.");
                    ReleasePreZeroHold();
                    return;
                }

                var window = DescentFuseWindow.Open(DescentShowKind.Live);

                // The window took its own hold in its constructor (released at the bloom), so the
                // pre-zero hold's job is done. Released even when Open() refused — a hold with no
                // show behind it would keep the ceremony shut for nothing.
                ReleasePreZeroHold();
                if (window is null) return;

                window.Closed += (s, _) =>
                {
                    if (s is DescentFuseWindow w && w.HandedOffToCeremony) FocusCeremony();
                    else BeginPostZeroRetry("live handoff timed out");
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Fuse] The live zero show could not start.");
                ReleasePreZeroHold();
            }
        }

        /// <summary>
        /// Zero was noticed late in a process that slept across it (0825 F5). The countdown has
        /// already flipped itself to the away fork, so this is the launch-time catch-up decision
        /// taken again, mid-session: play the condensed crack if it is owed, hold the ceremony
        /// behind it exactly as at launch, and otherwise just make sure the offer conversation
        /// happens.
        /// </summary>
        private void OnZeroObservedLate(object? sender, EventArgs e)
        {
            if (_disposed) return;
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;

            try
            {
                _heartbeat.Stop();

                var fuse = App.DescentCountdown;
                if (fuse?.ShouldPlayCatchUp != true)
                {
                    ReleasePreZeroHold();
                    BeginPostZeroRetry("zero passed during sleep, no crack owed");
                    return;
                }

                Log.Information("[Fuse] The instant passed while this session slept — the catch-up crack is owed.");

                // Convert the pre-zero hold into the catch-up hold rather than stacking a second
                // one: same depth, same release path (the crack's Closed handler).
                if (_preZeroHolding) _preZeroHolding = false;
                else App.DescentMigration?.HoldOffers();
                _catchUpHolding = true;
                _catchUpOwed = true;

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null || dispatcher.HasShutdownStarted) { ReleaseCatchUpHold(); return; }
                _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(PlayCatchUp));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Fuse] The late-zero path failed.");
                ReleasePreZeroHold();
                ReleaseCatchUpHold();
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

                    // The release replays a held offer synchronously; if nothing was in hand the
                    // startup sync that should have carried it may simply have failed (F1).
                    if (App.DescentMigration?.IsCeremonyOpen != true) BeginPostZeroRetry("catch-up closed without an offer");
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
        // The pre-zero hold (0825 F2)
        // ------------------------------------------------------------------

        /// <summary>
        /// Hold offers iff the fuse is lit, zero is still ahead, and nothing else is holding. Safe
        /// to call on every phase change: it only ever takes ONE hold and only ever when the state
        /// says so. Release is deliberately NOT here — the moment to let go is after the live
        /// window holds the ceremony itself (see <see cref="OnZeroReached"/>), never on the phase
        /// change to Zero, which fires on the same tick a few lines BEFORE ZeroReached.
        /// </summary>
        private void EnsurePreZeroHold()
        {
            if (_disposed || _preZeroHolding || _catchUpHolding) return;

            var fuse = App.DescentCountdown;
            var migration = App.DescentMigration;
            if (fuse is null || migration is null) return;
            if (fuse.CeremonyAtUtc is null) return;
            if (fuse.ZeroPassedWhileAway) return;
            if (fuse.Phase >= DescentFusePhase.Zero) return;
            if (AlreadyAnswered()) return;

            try
            {
                migration.HoldOffers();
                _preZeroHolding = true;
                Log.Debug("[Fuse] Holding ceremony offers until zero.");
            }
            catch (Exception ex) { Log.Debug("[Fuse] Could not take the pre-zero hold: {Error}", ex.Message); }
        }

        private void ReleasePreZeroHold()
        {
            if (!_preZeroHolding) return;
            _preZeroHolding = false;
            try { App.DescentMigration?.ReleaseOffers(); }
            catch (Exception ex) { Log.Debug("[Fuse] Releasing the pre-zero hold failed: {Error}", ex.Message); }
        }

        private static bool AlreadyAnswered()
        {
            var s = App.Settings?.Current;
            if (s is null) return false;
            return s.DescentMigrationCompleted || DescentMigrationChoices.IsValid(s.PendingDescentMigrationChoice);
        }

        // ------------------------------------------------------------------
        // The post-zero retry (0825 F1)
        // ------------------------------------------------------------------

        /// <summary>Ask for a sync once a minute, bounded, until the ceremony conversation happens.
        /// Policy in <see cref="DescentPostZeroRetry"/>. Idempotent: a second call while running
        /// is a no-op.</summary>
        private void BeginPostZeroRetry(string why)
        {
            if (_disposed || _retryTimer != null) return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted) return;

            if (!RetryShouldContinue())
            {
                Log.Debug("[Fuse] No post-zero retry needed ({Why}).", why);
                return;
            }

            Log.Information("[Fuse] Post-zero retry armed ({Why}): one sync a minute, up to {Max}.",
                why, DescentPostZeroRetry.MaxAttempts);

            _retryAttempts = 0;
            _retryTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = DescentPostZeroRetry.Every };
            _retryTimer.Tick += OnRetryTick;
            _retryTimer.Start();
        }

        private void OnRetryTick(object? sender, EventArgs e)
        {
            try
            {
                if (_disposed || Application.Current?.Dispatcher?.HasShutdownStarted != false) { StopPostZeroRetry(); return; }

                if (!RetryShouldContinue())
                {
                    Log.Information("[Fuse] Post-zero retry done after {N} attempt(s).", _retryAttempts);
                    StopPostZeroRetry();
                    return;
                }

                _retryAttempts++;
                _ = App.ProfileSync?.SyncProfileAsync();
            }
            catch (Exception ex)
            {
                Log.Debug("[Fuse] Post-zero retry tick failed: {Error}", ex.Message);
            }
        }

        private bool RetryShouldContinue()
        {
            var s = App.Settings?.Current;
            var migration = App.DescentMigration;
            return DescentPostZeroRetry.ShouldContinue(
                _retryAttempts,
                fuseArmed: App.DescentCountdown?.CeremonyAtUtc != null,
                ceremonyOpen: migration?.IsCeremonyOpen == true,
                offerInHand: migration?.LiveOffer != null,
                migrationCompleted: s?.DescentMigrationCompleted == true,
                choicePending: s != null && DescentMigrationChoices.IsValid(s.PendingDescentMigrationChoice));
        }

        private void StopPostZeroRetry()
        {
            try { _retryTimer?.Stop(); } catch { }
            _retryTimer = null;
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
        /// spiral till the ceremony finishes, and have some highlight animation that catches the
        /// user's attention and reveals the spiral, even opens it for them."
        ///
        /// <list type="number">
        /// <item>Bring the main window forward.</item>
        /// <item>Navigate to the SPIRAL ROOM through the real path —
        /// <c>MainWindow.ShowTab("spiral")</c>, the same door the nav rail, the bark rules and the
        /// command palette all use. A parallel navigation would be a second way to change tabs that
        /// the rest of the app does not know about.</item>
        /// <item>Play the reveal INSIDE that tab (<c>SpiralTabView.BeginFirstLight</c>), which is the
        /// door that tolerates the descent block still being in flight.</item>
        /// </list>
        ///
        /// <para><b>2026-08-16: the window and the plate pulse both retired.</b> This used to walk
        /// the user to the profile tab, breathe the Trainer Card's spiral plate at them, and then
        /// open <c>SpiralMapWindow</c> with the reveal inside it. There is no window any more — the
        /// map is a tab — so pointing at a plate on the way past bought a hop and gave nothing back.
        /// The plate itself STAYS as a second door into the room; only the animation is gone.</para>
        ///
        /// <para><b>INTERNAL, AND THE DEMO RIG'S ENTRY POINT.</b> The owner's capture rig calls this
        /// directly to film the reveal without standing up a server, a veteran account and a
        /// ceremony. It is deliberately not public, has no command-line wiring and is not on any
        /// menu — the rig adds its own scratch hook — and it deliberately skips the one-shot guard
        /// so the rig can re-run it; <see cref="BeginFirstLight"/> is the guarded path the ceremony
        /// itself takes. The seam is unchanged by the move: same name, same signature, same
        /// "no arguments, no server" promise — it just lands in a tab now.</para>
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
                    // THE ROOM IS A TAB NOW, so there is genuinely nowhere to play this without a
                    // window — and that is a survivable loss, not a failure. The withhold is already
                    // open, the surfaces are already unlocked, and every ordinary door into the
                    // spiral works the moment the user has a window again. What is lost is an
                    // animation; what is NOT lost is the unlock. (See BeginFirstLight's note on why
                    // the one-shot is deliberately not persisted.)
                    Log.Debug("[Fuse] First light: no main window - the spiral is unlocked, the reveal is skipped.");
                    return;
                }

                try
                {
                    if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
                    main.Activate();
                }
                catch (Exception ex) { Log.Debug("[Fuse] First light could not bring the window forward: {Error}", ex.Message); }

                // Navigate, then reveal. BeginSpiralFirstLight does both in that order for a reason:
                // the tab's own entry repaint has to happen BEFORE the reveal takes the surface, or
                // it would stomp it one frame after it started.
                main.BeginSpiralFirstLight();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Fuse] The first-light reveal failed on the way to the spiral room. The spiral itself is unlocked.");
            }
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

                // The hold follows the fuse (F2): kill switch lets go; a re-arm (or the owner
                // moving the date back ahead of now, F3) takes it again; a date moved into the
                // past has no live show coming, so nothing to hold for. The transition TO Zero on
                // a live night is deliberately left alone — ZeroReached handles that hand-over.
                if (e.Current == DescentFusePhase.Dark) ReleasePreZeroHold();
                else if (e.Current < DescentFusePhase.Zero) EnsurePreZeroHold();
                else if (App.DescentCountdown?.ZeroPassedWhileAway == true) ReleasePreZeroHold();
            }
            catch (Exception ex) { Log.Debug("[Fuse] Phase handling failed: {Error}", ex.Message); }
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
                    fuse.ZeroObservedLate -= OnZeroObservedLate;
                    fuse.PhaseChanged -= OnPhaseChanged;
                }
                var migration = App.DescentMigration;
                if (migration != null) migration.CeremonyClosed -= OnCeremonyClosed;
            }
            catch { /* teardown races are not worth a log line */ }

            StopPostZeroRetry();
            ReleasePreZeroHold();
            ReleaseCatchUpHold();
            _heartbeat.Dispose();
        }
    }
}
