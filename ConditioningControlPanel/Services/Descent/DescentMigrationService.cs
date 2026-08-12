using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Serilog;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>
    /// THE MIGRATION CEREMONY's runtime — the one thing that knows how to turn a server offer
    /// into a rewritten local ledger, and the only writer of the Descent settings region.
    ///
    /// <para><b>DORMANCY, stated once so it can be checked.</b> Nothing here runs on its own.
    /// <see cref="OfferReceived"/> is called from exactly one place — ProfileSyncService, when a
    /// /v2/user/sync response carries <c>descent_migration.required</c> — and the server sends
    /// that only with DESCENT_MIGRATION armed. There is no client flag, no local heuristic, no
    /// debug entry point and no timer. On today's server every method below is unreachable.</para>
    ///
    /// <para><b>Threading.</b> The offer arrives on a background sync continuation, so the window
    /// open is marshalled to the dispatcher with the CLAUDE.md rule-6/8 guards (bail if the
    /// dispatcher is gone or shutting down). Everything else is called from the UI thread.</para>
    /// </summary>
    public sealed class DescentMigrationService
    {
        private readonly object _gate = new();
        private DescentMigrationOffer? _liveOffer;
        private bool _ceremonyOpen;

        /// <summary>
        /// Raised when a queued stage ceremony comes due — one per login day, after a "Take it
        /// all back" restored a veteran past stages they never watched themselves reach.
        ///
        /// <para>The desktop stage-ceremony SURFACE does not exist yet (it belongs to the stage
        /// ladder lane, not this one). This event and the queue behind it are the contract that
        /// lane consumes; until it lands, a drip spends itself on a companion line and a log
        /// entry, which is the honest minimum and is still a moment rather than a silent
        /// increment.</para>
        /// </summary>
        public event EventHandler<int>? StageCeremonyDue;

        /// <summary>True while the ceremony window is on screen. Guards a second open.</summary>
        public bool IsCeremonyOpen { get { lock (_gate) return _ceremonyOpen; } }

        /// <summary>The offer the live ceremony is running against, or null.</summary>
        public DescentMigrationOffer? LiveOffer { get { lock (_gate) return _liveOffer; } }

        // ------------------------------------------------------------------
        // The offer
        // ------------------------------------------------------------------

        /// <summary>
        /// The server has offered the ceremony. Open it — once — on the UI thread.
        /// ProfileSyncService has already ruled out "already migrated" and "a choice is already
        /// pending"; this method only has to avoid opening a second window over the first.
        /// </summary>
        public void OfferReceived(DescentMigrationOffer offer)
        {
            if (offer is null) return;

            lock (_gate)
            {
                if (_ceremonyOpen) return;
                _liveOffer = offer;
            }

            // CLAUDE.md async rules 6/8: a fire-and-forget hop onto a dispatcher that has begun
            // shutting down is a crash report nobody can read.
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted) return;

            if (dispatcher.CheckAccess()) OpenCeremonyWindow();
            else dispatcher.BeginInvoke(new Action(OpenCeremonyWindow));
        }

        private void OpenCeremonyWindow()
        {
            try
            {
                if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;

                DescentMigrationOffer? offer;
                lock (_gate)
                {
                    if (_ceremonyOpen) return;
                    offer = _liveOffer;
                    if (offer is null) return;
                    _ceremonyOpen = true;
                }

                // Flat namespace — see the trap note at the top of DescentCeremonyWindow.xaml.cs.
                var window = new DescentCeremonyWindow(offer);
                window.Closed += (_, _) => { lock (_gate) _ceremonyOpen = false; };

                var main = Application.Current?.MainWindow;
                if (main != null && main.IsLoaded) window.Owner = main;

                window.Show();
                window.Activate();

                Log.Information("[Descent] Migration ceremony opened.");
            }
            catch (Exception ex)
            {
                lock (_gate) _ceremonyOpen = false;
                Log.Error(ex, "[Descent] Could not open the migration ceremony window — the server will re-offer on the next sync.");
            }
        }

        // ------------------------------------------------------------------
        // The choice
        // ------------------------------------------------------------------

        /// <summary>
        /// COMMIT. Applies the chosen half of the migration locally and arms the submit that the
        /// next sync carries. One-way, and the confirm step in front of it says so.
        ///
        /// <para><b>Why local-first.</b> The sync body's xp/level fields ARE the submit's ledger
        /// (CONTRACTS §2.2) — there is no separate ledger field to fill — so the settings have to
        /// be rewritten before the POST, not after the ack. What waits for the ack is the
        /// "migrated" marker, and only that. See HandleDescentMigrationAck for why every ordering
        /// of a crash here is survivable.</para>
        /// </summary>
        /// <returns>False if the choice was invalid or settings were unavailable.</returns>
        public bool ApplyChoice(string choice, DescentMigrationOffer offer)
        {
            if (!DescentMigrationChoices.IsValid(choice)) return false;

            var settings = App.Settings?.Current;
            if (settings is null || offer is null) return false;

            if (settings.DescentMigrationCompleted)
            {
                Log.Warning("[Descent] ApplyChoice ignored — this account is already migrated.");
                return false;
            }

            var result = DescentMigration.Resolve(choice, offer);

            // Keepsakes first, from the standing that is about to be replaced.
            settings.DescentPreMigrationLevel = settings.PlayerLevel;
            settings.DescentPreMigrationLifetimeXp = result.LifetimeXp;

            // The ledger. PlayerXP is the progress INTO the current level, and PlayerLevel the
            // level itself — GetTotalXP recombines them, which is what the sync body sends.
            settings.DescentEpoch = DescentEpochs.AccountDescent;   // curve v2 is live from here
            settings.PlayerLevel = result.Level;
            settings.PlayerXP = result.XpIntoLevel;

            // HighestLevelEver is a permanent-unlock key, not a ledger entry, and §6 is explicit
            // that a Cycle "wipes nothing else". It is never lowered here.
            if (settings.PlayerLevel > settings.HighestLevelEver)
                settings.HighestLevelEver = settings.PlayerLevel;

            if (choice == DescentMigrationChoices.Cycle)
            {
                settings.DescentCycle = Math.Max(1, settings.DescentCycle);
                settings.DescentCycleXpBonus = DescentMigration.CycleXpBonus;
                settings.DescentPendingStageCeremonies = new List<int>();   // no ladder to re-walk
            }
            else
            {
                settings.DescentPendingStageCeremonies = BuildStageDripQueue(offer);
            }

            // The keepsake and the anchor land for BOTH choices (§4). The anchor is the ceremony
            // date: for veterans that is the birth of Year One, which is exactly why nobody's
            // spiral arrives pre-lit — the track starts here, tonight, for everyone.
            settings.DescentVeteranArchive = true;
            settings.DescentAnchorUtc = DateTime.UtcNow;
            settings.DescentLastStageDripDate = null;   // first drip may land the very next day

            // THE WATERMARK MUST GO. It records the last total this client and the server agreed
            // on, in PRE-migration terms; leaving it armed would have the send-guard defending a
            // number the ceremony just deliberately retired, and it is scoped to (account,
            // season) so a rollover will not clear it for us. The same helper the admin
            // level_reset path uses — this is the same kind of event: a sanctioned rewrite.
            ProfileSyncService.ClearXpWatermark(settings, "descent migration ceremony");

            // LAST: arm the submit. Written after the ledger so a crash between the two leaves a
            // rewritten-but-unsubmitted client, which the server's next offer simply re-runs to
            // the same answer — rather than an armed submit with a stale ledger behind it.
            settings.PendingDescentMigrationChoice = choice;
            App.Settings?.Save();

            Log.Information("[Descent] Choice '{Choice}' applied locally: Level {OldLevel} -> {NewLevel} on curve v2 (lifetime {Xp} XP). Awaiting server ack.",
                choice, settings.DescentPreMigrationLevel, result.Level, (int)result.LifetimeXp);

            // Push it now rather than waiting for the next scheduled sync. Fire-and-forget with
            // the usual guard: a failed submit costs nothing, because the pending choice stays on
            // disk and rides the next sync instead.
            try
            {
                _ = App.ProfileSync?.SyncProfileAsync();
            }
            catch (Exception ex)
            {
                Log.Debug("[Descent] Immediate submit sync could not be started: {Error}. It will ride the next sync.", ex.Message);
            }

            return true;
        }

        /// <summary>
        /// THE DRIP QUEUE (§6). A restored veteran lands on a stage they never watched themselves
        /// reach; firing every skipped stage ceremony at once would burn the whole ladder in one
        /// unwatchable burst, so they are queued and released one per login day.
        ///
        /// <para>Built from the server's own ladder when one is in hand
        /// (<see cref="DescentService.Current"/>), and from nothing at all when it is not — an
        /// empty queue is a correct answer, and inventing a ladder client-side to fill it would
        /// be exactly the local fabrication DescentModels forbids.</para>
        /// </summary>
        private static List<int> BuildStageDripQueue(DescentMigrationOffer offer)
        {
            var stage = App.Descent?.Current?.Stage;
            var thresholds = stage?.Thresholds;

            int reached;
            if (thresholds is { Count: > 0 })
                reached = thresholds.Count(t => offer.DevotionDays >= t);
            else if (stage != null && stage.N > 0)
                reached = stage.N;
            else
            {
                Log.Information("[Descent] No stage ladder in hand at ceremony time — queueing no stage ceremonies. The restore is unaffected.");
                return new List<int>();
            }

            var queue = Enumerable.Range(1, Math.Max(0, reached)).ToList();
            Log.Information("[Descent] Queued {Count} stage ceremonies to drip one per login day.", queue.Count);
            return queue;
        }

        // ------------------------------------------------------------------
        // The drip
        // ------------------------------------------------------------------

        /// <summary>
        /// Release at most one queued stage ceremony, at most once per LOCAL DAY. Called after a
        /// successful sync — a proven "this account is signed in and awake" moment that needs no
        /// new lifecycle wiring. A user who restarts the app five times gets one; a user who does
        /// not open the app at all loses nothing, because the queue simply waits.
        /// </summary>
        public void TickStageDrip()
        {
            try
            {
                var settings = App.Settings?.Current;
                if (settings is null) return;
                if (!settings.DescentMigrationCompleted) return;

                var queue = settings.DescentPendingStageCeremonies;
                if (queue is null || queue.Count == 0) return;

                var today = DateTime.Now.ToString("yyyy-MM-dd");
                if (string.Equals(settings.DescentLastStageDripDate, today, StringComparison.Ordinal)) return;

                var stage = queue[0];
                settings.DescentPendingStageCeremonies = queue.Skip(1).ToList();
                settings.DescentLastStageDripDate = today;
                App.Settings?.Save();

                Log.Information("[Descent] Stage {Stage} ceremony released ({Left} still queued).",
                    stage, settings.DescentPendingStageCeremonies.Count);

                RaiseStageCeremonyDue(stage);
            }
            catch (Exception ex)
            {
                Log.Debug("[Descent] Stage drip tick failed: {Error}", ex.Message);
            }
        }

        private void RaiseStageCeremonyDue(int stage)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted) return;

            void Fire()
            {
                try
                {
                    StageCeremonyDue?.Invoke(this, stage);

                    // Until the stage-ceremony surface exists, the companion carries the moment.
                    // playSound:false and aiGenerated:false — scripted copy, no AI badge, no
                    // chat-suppression window.
                    App.AvatarWindow?.GigglePriority(
                        $"Stage {DescentCeremonyCopy.RomanNumeral(stage)}. You walked this once already. Walk it again with me.",
                        playSound: false, aiGenerated: false);
                }
                catch (Exception ex)
                {
                    Log.Debug("[Descent] StageCeremonyDue handler threw: {Error}", ex.Message);
                }
            }

            if (dispatcher.CheckAccess()) Fire();
            else dispatcher.BeginInvoke(new Action(Fire));
        }
    }
}
