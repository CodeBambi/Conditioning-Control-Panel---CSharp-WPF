using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>
    /// THE DESCENT — the desktop app's reader for the server's `descent` block.
    ///
    /// ONE REQUEST, ONE TRUTH. The block rides inside the `user` node of
    /// GET /v2/user/profile (ccp-server server.js :14334), which is the endpoint
    /// desktop's own auth already fits: X-Auth-Token out of
    /// App.Settings.Current.AuthToken, unified_id on the query string. It is NOT on
    /// /v2/user/sync's response, so the moment today's XP lands in the server vat is
    /// the moment this service has to go and ask again — see RequestRefresh's callers.
    ///
    /// READ-ONLY, ALWAYS. Nothing here POSTs, and nothing downstream of it is
    /// allowed to either: progression is server-authoritative and a client that can
    /// write its own vat is a client that can fill it.
    ///
    /// TRI-STATE: <see cref="Current"/> is null both for "never fetched" and for
    /// "the server sent no usable block". Both mean the same thing to every surface
    /// — render nothing — so they are deliberately not distinguished.
    /// </summary>
    public sealed class DescentService
    {
        /// <summary>One fetch in the air at a time.</summary>
        private readonly SemaphoreSlim _gate = new(1, 1);

        /// <summary>
        /// The 10s floor, the credentials the last fetch carried, and every want this service
        /// could not serve on the spot (<see cref="DescentRefreshGate"/>). Locked on itself:
        /// Ask runs on the pool, <see cref="Reset"/> on the UI thread, and the background poll
        /// reads the floor.
        /// </summary>
        private readonly DescentRefreshGate _refreshGate = new();

        /// <summary>
        /// Bumped by <see cref="Reset"/>. A fetch that went out for the account that just
        /// signed out lands after Reset carrying the wrong account's block; the mismatch is
        /// how it is discarded instead of standing on user B's Trainer Card.
        /// </summary>
        private int _generation;

        /// <summary>
        /// 1 while a deferred retry is in the air. One is enough: when it lands it re-asks
        /// the gate, which remembers every want made in the meantime.
        /// </summary>
        private int _deferredArmed;

        /// <summary>The last well-formed block, or null. See the tri-state note above.</summary>
        public DescentBlock? Current { get; private set; }

        /// <summary>
        /// NO KEY, NO HEARTBEAT. True once this account has been seen to carry a
        /// descent block at least once in this app session.
        ///
        /// The block ships only to accounts inside the server's rollout dial, so for
        /// everybody else a 60s poll and a post-sync refresh are a GET that can only
        /// ever return the same "no key" answer — the overwhelming majority of
        /// installs paying network for a feature they cannot see. Every RECURRING
        /// caller gates on this; the one-shot on Trainer Card open deliberately does
        /// not, because that single request is how a dark key lights up.
        ///
        /// In memory only, and deliberately so: persisting it would mean deciding
        /// what to do when the dial narrows, and the cost of being wrong for one
        /// card-open is one request.
        /// </summary>
        public bool HasSeenBlock { get; private set; }

        /// <summary>
        /// Raised on the UI thread after every accepted fetch, including one that
        /// turned the block off (payload arrived without it). Surfaces subscribe and
        /// re-read <see cref="Current"/>.
        /// </summary>
        public event EventHandler? BlockChanged;

        /// <summary>
        /// Fire-and-forget refresh. Safe to call from event handlers and from the
        /// sync path — it swallows everything, because a failed vat read must never
        /// disturb the thing that asked for it.
        /// </summary>
        public void RequestRefresh(string reason)
        {
            _ = Task.Run(async () =>
            {
                try { await RefreshAsync(reason).ConfigureAwait(false); }
                catch (Exception ex) { Log.Debug("[Descent] refresh '{Reason}' failed: {Error}", reason, ex.Message); }
            });
        }

        /// <summary>
        /// THE SIGN-IN POKE. Called from ProfileSyncService.StartHeartbeat, which every
        /// login path reaches once the account is usable (the three startup restores, the
        /// login dialog, the AccountService flows, the 401 token self-heal); the mirror of
        /// <see cref="Reset"/> on the way out.
        ///
        /// Why the vat needed one: the Trainer Card asks exactly once on open, and when the
        /// card is on screen before the auth token lands (a launch that opens on the card, a
        /// login made from it) that one ask used to die silently in <see cref="RefreshAsync"/>.
        /// ProfileLoaded is not a sign-in signal: it fires only when a sync succeeds or the
        /// progression moved, so a login that took any other branch never re-asked, and the
        /// jar, its tooltip and the XP readout stayed dark until a sign-out or the 60s
        /// background poll. Now the ask is remembered and this is what fires it.
        ///
        /// Safe to call more than once per login (a Patreon and a Discord restore both reach
        /// StartHeartbeat): the gate answers a second ask inside the floor with Skip when a
        /// fetch with these same credentials already answered, so nothing doubles on the wire.
        /// </summary>
        public void OnSignedIn() => RequestRefresh("signed in");

        /// <summary>
        /// Fetch and parse the block. Returns true when a fetch actually happened
        /// (whether or not it produced a block); false when it was skipped: offline
        /// mode, no account or token yet (remembered, fired by <see cref="OnSignedIn"/>),
        /// a fetch already in flight or inside the floor (remembered, retried when the
        /// floor clears), or a request the floor coalesced into a fetch that already
        /// answered with these same credentials. See <see cref="DescentRefreshGate"/>.
        /// </summary>
        public async Task<bool> RefreshAsync(string reason, bool force = false)
        {
            // OFFLINE MODE IS A HARD FLOOR, not a preference this feature gets to
            // outrank. It is the same check every other network path takes
            // (ProfileSyncService.LoadProfileAsync / SyncProfileAsync /
            // SendHeartbeatAsync): a user who turned the app's networking off must
            // not be able to see a request leave for a decorative meter. The gate
            // answers it with Skip and, unlike the other skips, does not remember it.
            var settings = App.Settings?.Current;
            bool offline = settings?.OfflineMode == true;
            string? unifiedId = settings?.UnifiedId;
            string? authToken = settings?.AuthToken;

            bool haveGate = _gate.Wait(0);
            try
            {
                DescentRefreshVerdict verdict;
                TimeSpan retryIn;
                lock (_refreshGate)
                    verdict = _refreshGate.Ask(DateTime.UtcNow, unifiedId, authToken, offline, inFlight: !haveGate, force, reason, out retryIn);

                switch (verdict)
                {
                    case DescentRefreshVerdict.Skip:
                        return false;
                    case DescentRefreshVerdict.WaitForSignIn:
                        Log.Debug("[Descent] refresh '{Reason}' waits for sign-in", reason);
                        return false;
                    case DescentRefreshVerdict.Defer:
                        ScheduleDeferredRetry(retryIn, reason);
                        return false;
                }

                int generation = Volatile.Read(ref _generation);
                lock (_refreshGate) _refreshGate.BeginFetch(DateTime.UtcNow, unifiedId!, authToken!);

                var userNode = await new V2AuthService().GetUserProfileNodeAsync(unifiedId!).ConfigureAwait(false);

                if (generation != Volatile.Read(ref _generation))
                {
                    // Reset ran while this was in the air: the answer belongs to the account
                    // that signed out. Not applied, not counted as an answer for whoever is
                    // signed in now (their own ask, if any, is still standing in the gate).
                    Log.Debug("[Descent] profile read landed after logout, discarded ({Reason})", reason);
                    return true;
                }

                if (userNode is null)
                {
                    // A failed read is NOT "the user has no descent". Leave the last
                    // known block standing rather than blanking a vat over a flaky
                    // network — the alternative is a meter that flickers out of
                    // existence every time Vercel hiccups. It is not an answer either:
                    // a want made during this fetch stays standing for the retry.
                    lock (_refreshGate) _refreshGate.EndFetch(answered: false);
                    Log.Debug("[Descent] profile read returned nothing ({Reason})", reason);
                    return true;
                }
                lock (_refreshGate) _refreshGate.EndFetch(answered: true);

                // THE POLL'S SECOND READING (feat/xp-economy). The same response carries the
                // account's level/xp/current_season, which this service used to throw away —
                // making the 60s cadence blind to XP earned on another device until the next
                // restart. Hand them to the sync service's clean-ledger adopt; it owns every
                // rule (watermark, season scope, migration hold) and this stays read-only.
                TryHandOffProgression(userNode);

                var block = DescentReader.ParseFromUserNode(userNode);
                bool had = Current is not null;
                Current = block;
                if (block is not null) HasSeenBlock = true;   // arms the recurring callers

                if (block is null && had)
                    Log.Information("[Descent] block withdrawn by server ({Reason})", reason);
                else if (block is not null && !had)
                    Log.Information("[Descent] block armed for this account ({Reason})", reason);

                RaiseBlockChanged();
                return true;
            }
            finally
            {
                if (haveGate) _gate.Release();
            }
        }

        /// <summary>
        /// Come back for a want the gate could not serve on the spot. One retry in the air
        /// at a time; when it lands it asks <see cref="DescentRefreshGate.Pending"/> first,
        /// so a fetch that answered in the meantime (the in-flight one, a poll) makes it a
        /// no-op, and a logout in the meantime (which clears the want) does too. It does not
        /// loop on its own: a retry that fails on the wire stands down until somebody asks
        /// again, which the 60s polls always do.
        /// </summary>
        private void ScheduleDeferredRetry(TimeSpan delay, string reason)
        {
            if (Interlocked.CompareExchange(ref _deferredArmed, 1, 0) != 0) return;

            Log.Debug("[Descent] refresh '{Reason}' deferred {Seconds:F1}s", reason, delay.TotalSeconds);
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay).ConfigureAwait(false);
                    Interlocked.Exchange(ref _deferredArmed, 0);

                    string? wanted;
                    lock (_refreshGate) wanted = _refreshGate.Pending ? _refreshGate.PendingReason ?? reason : null;
                    if (wanted is null) return;

                    await RefreshAsync("deferred: " + wanted).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref _deferredArmed, 0);
                    Log.Debug("[Descent] deferred refresh '{Reason}' failed: {Error}", reason, ex.Message);
                }
            });
        }

        /// <summary>
        /// Pull level/xp/current_season off the raw user node and offer them to
        /// <see cref="ProfileSyncService.TryAdoptFromProfilePoll"/>. Absent or
        /// malformed fields mean no offer — never a guess.
        /// </summary>
        private static void TryHandOffProgression(JObject userNode)
        {
            try
            {
                var level = userNode["level"]?.Value<int?>() ?? 0;
                var xp = userNode["xp"]?.Value<double?>();
                if (level <= 0 || xp is null) return;

                var season = userNode["current_season"]?.Type == JTokenType.String
                    ? userNode["current_season"]?.Value<string>()
                    : null;

                App.ProfileSync?.TryAdoptFromProfilePoll(level, xp.Value, season);
            }
            catch (Exception ex)
            {
                Log.Debug("[Descent] progression hand-off skipped: {Error}", ex.Message);
            }
        }

        // ==================== the background profile poll ====================

        /// <summary>
        /// THE UNGATED 60s POLL (feat/xp-economy). The vat's own poll is triply gated —
        /// Trainer Card on screen, window presenting, block seen — which was right when the
        /// response only fed a meter, but the same response now feeds the cross-device XP
        /// adopt, and that applies to EVERY logged-in account on any tab. So this timer runs
        /// for the life of the app; <see cref="RefreshAsync"/> itself is the logged-in &amp;&amp;
        /// !OfflineMode gate (it bails without a request on offline mode, no unified_id, or
        /// no auth token, so a logged-out install pays nothing but a timer tick).
        /// </summary>
        private DispatcherTimer? _backgroundPollTimer;

        /// <summary>
        /// The double-poll guard: when the vat's timer (or the post-sync hook) fetched within
        /// this window, the adopt already ran on that fetch's response and this tick has
        /// nothing to add. Slightly under the 60s cadence so the two timers cannot phase into
        /// two fetches a minute.
        /// </summary>
        private static readonly TimeSpan BackgroundPollFreshEnough = TimeSpan.FromSeconds(55);

        /// <summary>Start the background poll. Idempotent; call once from App startup.</summary>
        public void StartBackgroundProfilePoll()
        {
            if (_backgroundPollTimer != null) return;

            _backgroundPollTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(60),
            };
            _backgroundPollTimer.Tick += (_, _) =>
            {
                try
                {
                    DateTime lastFetchUtc;
                    lock (_refreshGate) lastFetchUtc = _refreshGate.LastFetchUtc;
                    if (DateTime.UtcNow - lastFetchUtc < BackgroundPollFreshEnough) return;
                    RequestRefresh("background profile poll");
                }
                catch (Exception ex) { Log.Debug("[Descent] background poll tick failed: {Error}", ex.Message); }
            };
            _backgroundPollTimer.Start();
        }

        /// <summary>
        /// FORGET THE ACCOUNT. Called on logout (MainWindow.Login.cs, beside
        /// ProfileSyncService.ResetLoadedProfileState).
        ///
        /// A shared install is the whole reason this exists: user A's vat must not
        /// still be standing on the Trainer Card when user B signs in, and the
        /// block-seen flag must go dark with it or B's keyless account inherits A's
        /// 60s poll. Raising BlockChanged with a null Current is what disarms the
        /// surface — the host's coordinator resets itself on a null block.
        /// </summary>
        public void Reset()
        {
            Interlocked.Increment(ref _generation);     // a fetch still in the air belongs to the old account
            lock (_refreshGate) _refreshGate.Reset();   // the floor (a re-login may ask at once), its credentials, any want
            Current = null;
            HasSeenBlock = false;
            Log.Debug("[Descent] state cleared (logout)");
            RaiseBlockChanged();
        }

        /// <summary>
        /// RE-ASK THE GATES, WITHOUT RE-ASKING THE SERVER. The block has not moved; something
        /// ELSE that every spiral surface gates on has — today that is the migration withhold
        /// flipping when a ceremony is offered or a choice is committed
        /// (<see cref="DescentMigrationService.SpiralWithheld"/>).
        ///
        /// <para><b>Why re-raise BlockChanged rather than mint an event.</b> Every spiral surface
        /// already subscribes to it, and every one of them re-reads its WHOLE gate from scratch
        /// when it fires — the rail's Arm, the Trainer Card plate, the profile menu row. A second
        /// event would be a second subscription list to keep in step, and the first surface added
        /// after it would forget to join. This costs one synchronous handler pass and no network.
        /// </para>
        ///
        /// <para>Deliberately does NOT touch <see cref="Current"/> or <see cref="HasSeenBlock"/>:
        /// this is a re-evaluation, not a fetch. A caller that wants fresh numbers wants
        /// <see cref="RequestRefresh"/>.</para>
        /// </summary>
        public void NotifySurfaces(string reason)
        {
            Log.Debug("[Descent] surfaces re-evaluating ({Reason})", reason);
            RaiseBlockChanged();
        }

        /// <summary>
        /// Marshal to the UI thread the way the rest of the app does: bail out
        /// entirely if the dispatcher is gone or shutting down (CLAUDE.md async
        /// rules 6/8 — a fire-and-forget callback on a dead dispatcher is a crash
        /// report nobody can read).
        /// </summary>
        private void RaiseBlockChanged()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null || dispatcher.HasShutdownStarted) return;

                if (dispatcher.CheckAccess())
                {
                    BlockChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try { BlockChanged?.Invoke(this, EventArgs.Empty); }
                    catch (Exception ex) { Log.Debug("[Descent] BlockChanged handler threw: {Error}", ex.Message); }
                }));
            }
            catch (Exception ex)
            {
                Log.Debug("[Descent] could not raise BlockChanged: {Error}", ex.Message);
            }
        }
    }
}
