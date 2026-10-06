using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Serilog;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>
    /// THE MIGRATION's runtime: the one thing that knows how to turn a server offer into a
    /// rewritten local ledger, and the only writer of the Descent settings region.
    ///
    /// <para><b>THE CEREMONY IS RETIRED (owner, 2026-10-06).</b> It used to open a fullscreen
    /// window with two doors (Restore / Cycle). "It passed enough time, we can remove the descent
    /// choice and give the 10% boost to anyone that comes back": an offer now applies the RESTORE
    /// ledger at once, silently, and every migrated account gets the lasting XP bonus
    /// (<see cref="DescentMigration.ActiveCycleXpBonus"/>). No window of any kind opens from a sync.
    /// The server only offers to a build that says <c>descent_auto: true</c>, so an older install
    /// never sees an offer it would answer with the old fullscreen screen.</para>
    ///
    /// <para><b>Dormancy.</b> Nothing here runs on its own. <see cref="OfferReceived"/> is called
    /// from exactly one place, ProfileSyncService, when a /v2/user/sync response carries
    /// <c>descent_migration.required</c>.</para>
    ///
    /// <para><b>Threading.</b> The offer arrives on a sync continuation, so the apply is posted to
    /// the dispatcher (CLAUDE.md rules 6/8: bail if it is gone or shutting down). That also keeps
    /// the ordering the ceremony had: the rest of the sync response is handled first, and the
    /// relevel lands after it.</para>
    /// </summary>
    public sealed class DescentMigrationService
    {
        private readonly object _gate = new();
        private DescentMigrationOffer? _liveOffer;
        private int _offerHold;

        /// <summary>Where the settings come from. <c>App.Settings.Current</c> in the app; a plain
        /// <see cref="Models.AppSettings"/> in a test, so the auto-restore runs whole without an
        /// <c>Application</c>.</summary>
        private readonly Func<Models.AppSettings?> _settings;

        /// <summary>Apply on the calling thread instead of posting to the dispatcher. Tests only:
        /// another suite can leave an <c>Application</c> standing on an STA thread, and a post to
        /// it would make the apply land after the assertions.</summary>
        private readonly bool _applyInline;

        public DescentMigrationService() : this(null, applyInline: false) { }

        internal DescentMigrationService(Func<Models.AppSettings?>? settings, bool applyInline = true)
        {
            _settings = settings ?? (() => App.Settings?.Current);
            _applyInline = applyInline;
        }

        /// <summary>
        /// Raised when a queued stage moment comes due: one per login day, after a restore put a
        /// veteran past stages they never watched themselves reach. Nothing subscribes yet; the
        /// companion line in <see cref="RaiseStageCeremonyDue"/> carries the moment.
        /// </summary>
        public event EventHandler<int>? StageCeremonyDue;

        /// <summary>
        /// Always false: there is no ceremony window any more. Kept because the retired fuse shows
        /// (<c>DescentFuseWindow.ShowsRetired</c>) and the post-zero retry policy still take it as
        /// an input, and false is the honest answer for both.
        /// </summary>
        public bool IsCeremonyOpen => false;

        /// <summary>How many holders are currently holding offers back. Test seam.</summary>
        internal int OfferHoldDepth { get { lock (_gate) return _offerHold; } }

        /// <summary>The last offer the server sent this process, or null.</summary>
        public DescentMigrationOffer? LiveOffer { get { lock (_gate) return _liveOffer; } }

        // ------------------------------------------------------------------
        // The withhold (the spiral stays hidden until the migration has landed locally)
        // ------------------------------------------------------------------

        /// <summary>
        /// TRUE while this account is owed the migration and has not had it applied yet, and so
        /// must not see the spiral on any surface. With the auto-restore that window is a moment
        /// long (or the length of a hold): the pending choice that <see cref="ApplyChoice"/> writes
        /// opens the gate.
        /// </summary>
        public bool SpiralWithheld
        {
            get
            {
                try
                {
                    return SpiralWithheldFor(_settings(), LiveOffer is not null, IsCeremonyOpen);
                }
                catch (Exception ex)
                {
                    // "Not withheld" is the state of almost every account, so it is also the right
                    // answer when the question cannot be asked.
                    Log.Debug("[Descent] SpiralWithheld could not be evaluated: {Error}", ex.Message);
                    return false;
                }
            }
        }

        /// <summary>
        /// The withhold, as arithmetic. OUTSTANDING is an offer in hand, a window on screen (never,
        /// now) or the persisted marker from an older build (<see cref="Models.AppSettings.DescentMigrationOffered"/>).
        /// ANSWERED is the server ack or a valid pending choice. ANSWERED WINS. Null settings read
        /// as not withheld.
        /// </summary>
        internal static bool SpiralWithheldFor(Models.AppSettings? settings, bool offerInHand, bool ceremonyOpen)
        {
            if (settings is null) return false;

            if (settings.DescentMigrationCompleted) return false;
            if (DescentMigrationChoices.IsValid(settings.PendingDescentMigrationChoice)) return false;

            return offerInHand || ceremonyOpen || settings.DescentMigrationOffered;
        }

        /// <summary>
        /// Tell every spiral surface to look again. They all re-read their gates on
        /// <c>DescentService.BlockChanged</c>, so re-raising that one signal is the whole
        /// re-evaluation.
        /// </summary>
        private static void NotifySpiralSurfaces(string reason)
        {
            try { App.Descent?.NotifySurfaces(reason); }
            catch (Exception ex) { Log.Debug("[Descent] Could not refresh the spiral surfaces: {Error}", ex.Message); }
        }

        // ------------------------------------------------------------------
        // The offer
        // ------------------------------------------------------------------

        /// <summary>
        /// The server has offered the migration. Take it: the RESTORE ledger, applied at once, with
        /// no window. ProfileSyncService has already ruled out "already migrated" and "a choice is
        /// already pending"; <see cref="ApplyOfferNow"/> checks both again, because a held offer can
        /// be replayed long after the sync that carried it.
        /// </summary>
        public void OfferReceived(DescentMigrationOffer offer)
        {
            if (offer is null) return;

            bool held;
            lock (_gate)
            {
                _liveOffer = offer;
                held = _offerHold > 0;
            }

            if (held)
            {
                Log.Debug("[Descent] Migration offer held - it applies when the hold lifts.");
                return;
            }

            PostApply("offer received");
        }

        /// <summary>
        /// Hold offers back until the matching <see cref="ReleaseOffers"/>. Depth-counted, and a
        /// held offer is never dropped: it stays in <see cref="LiveOffer"/> and applies on release.
        /// The fuse director still takes holds around its (retired) shows; with no window to
        /// protect, a hold now only delays a silent relevel.
        /// </summary>
        public void HoldOffers()
        {
            lock (_gate) _offerHold++;
        }

        /// <summary>
        /// Drop one hold, and apply a held offer once the last hold is gone. Over-releasing is a
        /// no-op rather than a negative depth.
        /// </summary>
        public void ReleaseOffers()
        {
            bool apply;
            lock (_gate)
            {
                if (_offerHold > 0) _offerHold--;
                apply = _offerHold == 0 && _liveOffer is not null;
            }

            if (apply) PostApply("held offer released");
        }

        /// <summary>
        /// Post the apply to the dispatcher, or run it inline where there is none (headless tests,
        /// a helper process). Never inline on a dispatcher that exists: the sync that delivered the
        /// offer is still handling its response, and the relevel belongs after that, exactly where
        /// the ceremony's commit used to land.
        /// </summary>
        private void PostApply(string why)
        {
            var dispatcher = _applyInline ? null : Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                ApplyOfferNow(why);
                return;
            }
            if (dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(new Action(() => ApplyOfferNow(why)));
        }

        /// <summary>
        /// THE AUTO-RESTORE. Idempotent: a re-offer on every sync, a replay after a hold, and an
        /// offer arriving after the choice is already pending all end here and all but the first
        /// do nothing. Returns true when it applied.
        /// </summary>
        internal bool ApplyOfferNow(string why)
        {
            DescentMigrationOffer? offer;
            lock (_gate)
            {
                offer = _liveOffer;
                if (offer is null || _offerHold > 0) return false;
            }

            var settings = _settings();
            if (settings is null) return false;
            if (settings.DescentMigrationCompleted) return false;
            if (DescentMigrationChoices.IsValid(settings.PendingDescentMigrationChoice)) return false;

            Log.Information("[Descent] Migration offered ({Why}) - restoring silently on curve v2, no ceremony.", why);
            var applied = ApplyChoice(DescentMigrationChoices.Restore, offer);
            if (applied) RestoreChromeAfterMigration();
            return applied;
        }

        /// <summary>
        /// The fuse dimmed the chrome toward zero and the ceremony's close used to give the colour
        /// back. <c>DimStep</c> reads 0 for a migrated account, so asking the palette's one writer
        /// to re-derive is the whole restore.
        /// </summary>
        private static void RestoreChromeAfterMigration()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null || dispatcher.HasShutdownStarted) return;
                if (dispatcher.CheckAccess()) MainWindow.RestoreFuseChrome();
                else dispatcher.BeginInvoke(new Action(MainWindow.RestoreFuseChrome));
            }
            catch (Exception ex) { Log.Debug("[Descent] Chrome restore after the migration failed: {Error}", ex.Message); }
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

            var settings = _settings();
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

            // THE BONUS RIDES BOTH DOORS (owner, 2026-10-06: "give the 10% boost to anyone that
            // comes back"). Migrated = bonus. Cycle I itself stays the Cycle door's mark.
            settings.DescentCycleXpBonus = DescentMigration.CycleXpBonus;

            if (choice == DescentMigrationChoices.Cycle)
            {
                settings.DescentCycle = Math.Max(1, settings.DescentCycle);
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

            // THE WITHHOLD'S MEMORY IS SPENT. The question has been answered, so the fact that it
            // was ever asked stops meaning anything — and leaving it set would be one more way for
            // a future reader of the settings file to think this account is still owed a ceremony.
            // The predicate does not depend on this clear (the pending choice below already opens
            // the gate); it is here so the file stays honest.
            settings.DescentMigrationOffered = false;

            // LAST: arm the submit. Written after the ledger so a crash between the two leaves a
            // rewritten-but-unsubmitted client, which the server's next offer simply re-runs to
            // the same answer — rather than an armed submit with a stale ledger behind it.
            settings.PendingDescentMigrationChoice = choice;
            App.Settings?.Save();

            // THE GATE IS OPEN, EFFECTIVE NOW. The spiral belongs to this account from the moment
            // they answered — not from the server's ack, and not from the next block poll — so the
            // surfaces are told immediately. The first-light reveal (§2.4) runs a few seconds later
            // and depends on finding them already unlocked.
            NotifySpiralSurfaces("descent migration committed");

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
