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
        private int _offerHold;

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

        /// <summary>
        /// The ceremony window has closed. The bool is TRUE when a choice was COMMITTED — which is
        /// the only thing the Year One ignition (CONTRACT-FUSE-0816 §2.4) cares about, since a
        /// "Not tonight" close is a free deferral with nothing to light.
        ///
        /// <para><b>Additive, and it changes nothing about the offer flow.</b> The raise sits in the
        /// window's existing Closed handler beside the guard reset; with no subscriber it is a null
        /// check. It exists because the alternative — the show polling
        /// <see cref="IsCeremonyOpen"/> forever and then guessing at the settings — would be both
        /// more invasive and less correct.</para>
        ///
        /// <para>"Committed" is read off the settings rather than passed down from the window,
        /// because the window's own <c>_committed</c> flag is private and, more importantly, the
        /// settings are the thing that is actually true: <c>ApplyChoice</c> writes the pending
        /// choice, and the server ack later turns it into <c>DescentMigrationCompleted</c>. Either
        /// of those means the choice was taken.</para>
        /// </summary>
        public event EventHandler<bool>? CeremonyClosed;

        /// <summary>True while the ceremony window is on screen. Guards a second open.</summary>
        public bool IsCeremonyOpen { get { lock (_gate) return _ceremonyOpen; } }

        /// <summary>How many holders are currently suppressing the ceremony. Test seam.</summary>
        internal int OfferHoldDepth { get { lock (_gate) return _offerHold; } }

        /// <summary>
        /// Whether an offer could open the ceremony RIGHT NOW: one is in hand, none is on screen,
        /// and nothing is holding it back. Read by <see cref="ReleaseOffers"/> to decide whether a
        /// release has anything to replay, and by the tests — the hold's whole behaviour is this
        /// one predicate, and a state machine that can only be observed through a window opening is
        /// a state machine nobody can test.
        /// </summary>
        internal bool CanOpenCeremony()
        {
            lock (_gate) return !_ceremonyOpen && _offerHold == 0 && _liveOffer != null;
        }

        /// <summary>The offer the live ceremony is running against, or null.</summary>
        public DescentMigrationOffer? LiveOffer { get { lock (_gate) return _liveOffer; } }

        // ------------------------------------------------------------------
        // The withhold (the spiral stays hidden until the question is answered)
        // ------------------------------------------------------------------

        /// <summary>
        /// TRUE while this account is OWED the ceremony — and therefore must not see the spiral
        /// yet, on any surface.
        ///
        /// <para><b>The hole this closes.</b> Every spiral surface used to gate on block presence
        /// alone (<c>App.Descent?.Current</c>). At zero the server's block dial auto-promotes to
        /// 'all' (CONTRACT-FUSE-0816 §1.4), so the very sync that carries a veteran's migration
        /// OFFER also carries their first descent BLOCK. Gated on presence alone, the rail and the
        /// Trainer Card plate would light up beside — or under — a ceremony window that is still
        /// asking them which half of their history they want to keep. The reveal is the payment for
        /// answering; it cannot arrive before the question.</para>
        ///
        /// <para><b>It is not "is there an offer".</b> It is "is there an offer AND no answer": the
        /// gate opens the instant <see cref="ApplyChoice"/> writes the pending choice, which is what
        /// lets the first-light reveal (§2.4) find the surfaces already unlocked when it runs a
        /// second later. Nothing waits for the server ack — the user answered, and the spiral is
        /// theirs from that moment.</para>
        ///
        /// <para><b>Fresh accounts are never withheld.</b> A post-zero signup is offered no
        /// migration at all (there is no history to migrate), so all three "outstanding" inputs are
        /// false and this reads false forever — they see the spiral the moment the block lands,
        /// which is the whole point of the dial promoting.</para>
        /// </summary>
        public bool SpiralWithheld
        {
            get
            {
                try
                {
                    return SpiralWithheldFor(App.Settings?.Current, LiveOffer is not null, IsCeremonyOpen);
                }
                catch (Exception ex)
                {
                    // A predicate that throws must not blank a surface that was fine a moment ago:
                    // "not withheld" is the state of every account on today's server, so it is also
                    // the correct answer when the question cannot be asked.
                    Log.Debug("[Descent] SpiralWithheld could not be evaluated: {Error}", ex.Message);
                    return false;
                }
            }
        }

        /// <summary>
        /// The withhold, as arithmetic — every input passed in, so the personas can be pinned in a
        /// test without an <c>Application</c>, a settings singleton or a server.
        ///
        /// <para>Read it as two halves. OUTSTANDING is "a ceremony is in the room": an offer in
        /// hand (<paramref name="offerInHand"/> — note <c>_liveOffer</c> is never cleared, so this
        /// stays true across a "Not tonight" deferral for the rest of the session), the window
        /// actually on screen (<paramref name="ceremonyOpen"/>, which covers the one frame between
        /// the open and the offer being readable), or the persisted memory that one was offered on
        /// an earlier launch (<see cref="AppSettings.DescentMigrationOffered"/> — see that field for
        /// why memory is required at all). ANSWERED is the account having committed: the server has
        /// acked (<see cref="AppSettings.DescentMigrationCompleted"/>) or a valid choice is on disk
        /// waiting to be submitted.</para>
        ///
        /// <para>ANSWERED WINS. That ordering is not decoration: after a commit the ceremony window
        /// is still closing and <c>_liveOffer</c> is still set, so a predicate that let OUTSTANDING
        /// win would hold the spiral shut through the exact seconds the reveal is trying to open
        /// it.</para>
        ///
        /// <para>Null settings (headless, or a settings load that failed) read as NOT withheld, for
        /// the same reason the property's catch does: no settings means no account, no block and no
        /// surface to withhold anything from.</para>
        /// </summary>
        internal static bool SpiralWithheldFor(Models.AppSettings? settings, bool offerInHand, bool ceremonyOpen)
        {
            if (settings is null) return false;

            if (settings.DescentMigrationCompleted) return false;
            if (DescentMigrationChoices.IsValid(settings.PendingDescentMigrationChoice)) return false;

            return offerInHand || ceremonyOpen || settings.DescentMigrationOffered;
        }

        /// <summary>
        /// Tell every spiral surface to look again. The withhold has no event of its own on purpose:
        /// all three surfaces (the rail host, the Trainer Card plate, the profile menu row) already
        /// subscribe to <c>DescentService.BlockChanged</c> and re-read their gates from scratch when
        /// it fires, so re-raising that one signal is the whole re-evaluation — no new event bus, no
        /// new subscription to leak, and no surface that can be added later and forget to listen.
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

            // REMEMBER THAT THE QUESTION WAS ASKED, before anything opens. This is what withholds
            // the spiral on the NEXT launch if the subject defers tonight — see
            // AppSettings.DescentMigrationOffered for why in-memory state cannot answer that. Cheap
            // and idempotent: a re-offer on every sync writes the file once.
            try
            {
                var settings = App.Settings?.Current;
                if (settings is not null && !settings.DescentMigrationOffered)
                {
                    settings.DescentMigrationOffered = true;
                    App.Settings?.Save();
                }
            }
            catch (Exception ex) { Log.Debug("[Descent] Could not persist the offer marker: {Error}", ex.Message); }

            // The withhold has just flipped ON. Any spiral surface already drawn — a plate that
            // caught an earlier block, a rail armed a second ago — has to retract now rather than
            // stand beside the question.
            NotifySpiralSurfaces("descent migration offered");

            // CLAUDE.md async rules 6/8: a fire-and-forget hop onto a dispatcher that has begun
            // shutting down is a crash report nobody can read.
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted) return;

            if (dispatcher.CheckAccess()) OpenCeremonyWindow();
            else dispatcher.BeginInvoke(new Action(OpenCeremonyWindow));
        }

        /// <summary>
        /// Suppress ceremony opens until the matching <see cref="ReleaseOffers"/>.
        ///
        /// <para><b>Additive, and inert with no holder</b> (CONTRACT-FUSE-0816 §2.4). It exists for
        /// exactly one situation: the catch-up crack plays over the same startup sync that carries
        /// the offer, and the ceremony must open AFTER that fullscreen window has left rather than
        /// underneath it. A held offer is not dropped — it stays in <c>_liveOffer</c> and is
        /// replayed on release, so the hold delays the ceremony and never cancels it.</para>
        ///
        /// <para>Depth-counted so nested or duplicated holders cannot release each other's, and
        /// paired in the show director with a Closed handler that fires on every exit path
        /// including the panic key's ForceCloseAll.</para>
        /// </summary>
        public void HoldOffers()
        {
            lock (_gate) _offerHold++;
        }

        /// <summary>
        /// Drop one hold, and open the ceremony now if an offer arrived while it was up.
        /// Over-releasing is a no-op rather than a negative depth — a teardown path that calls this
        /// twice must not arm the gate backwards.
        /// </summary>
        public void ReleaseOffers()
        {
            lock (_gate) { if (_offerHold > 0) _offerHold--; }

            if (!CanOpenCeremony()) return;

            Log.Information("[Descent] An offer arrived while the ceremony was held — opening it now.");

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
                    // HELD. Note what is NOT done here: _ceremonyOpen stays false and _liveOffer
                    // stays set, which is precisely what lets ReleaseOffers replay this call.
                    if (_offerHold > 0)
                    {
                        Log.Debug("[Descent] Ceremony offer held behind a fullscreen show — it will open when the hold lifts.");
                        return;
                    }
                    offer = _liveOffer;
                    if (offer is null) return;
                    _ceremonyOpen = true;
                }

                // Flat namespace — see the trap note at the top of DescentCeremonyWindow.xaml.cs.
                var window = new DescentCeremonyWindow(offer);
                window.Closed += (_, _) =>
                {
                    lock (_gate) _ceremonyOpen = false;
                    RaiseCeremonyClosed();
                };

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

        /// <summary>
        /// Announce the close, with whether a choice was actually taken. Isolated from the caller
        /// so a subscriber that throws cannot leave the ceremony's own guard half-reset.
        /// </summary>
        private void RaiseCeremonyClosed()
        {
            bool committed;
            try
            {
                var s = App.Settings?.Current;
                committed = s != null &&
                            (s.DescentMigrationCompleted ||
                             DescentMigrationChoices.IsValid(s.PendingDescentMigrationChoice));
            }
            catch { committed = false; }

            try { CeremonyClosed?.Invoke(this, committed); }
            catch (Exception ex) { Log.Debug("[Descent] A CeremonyClosed handler threw: {Error}", ex.Message); }
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
