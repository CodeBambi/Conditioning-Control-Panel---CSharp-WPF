using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// <c>POST /v2/user/sync</c> for a head that loaded only <c>GET /v2/user/profile</c> (WPF ProfileSyncService.SyncProfileAsync,
    /// cut to what is known): the body carries ONLY <see cref="Sent"/>, achievements = server-loaded ∪ local. Gates, all
    /// required: signed in, loaded THIS session (<see cref="MarkLoaded"/>; stricter than WPF's defaults-guard), the 30 s
    /// cooldown and the XP watermark. Also the 120 s heartbeat and the coalesced XP nudge. No periodic push (WPF has none).
    /// ponytail: no restore-from-backup reconcile or heartbeat adopt - add with the features that need them.
    /// </summary>
    public sealed class SyncPush
    {
        public const SyncBody.Field Sent = SyncBody.Field.UnifiedId | SyncBody.Field.Xp | SyncBody.Field.Level
            | SyncBody.Field.DescentEpoch | SyncBody.Field.DescentAuto | SyncBody.Field.Achievements;
        public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(120);
        private static readonly TimeSpan NudgeSettle = TimeSpan.FromSeconds(3), NudgeCooldownSlack = TimeSpan.FromSeconds(2);
        private const string ServerUrl = "https://codebambi-proxy.vercel.app";

        private readonly HttpClient _http;
        private readonly HttpMessageHandler? _handler;
        private readonly Func<IEnumerable<string>?> _localAchievements;
        private readonly Func<bool> _inSession;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private string[]? _serverAchievements;   // null: the profile carried none, so achievements are not known
        private Timer? _heartbeat;
        private int _nudgePending;
        private readonly Func<ProfileCosmetics?, ProfileCosmetics>? _sanitizeCosmetics;
        private volatile bool _pendingCosmeticsClear;
        private volatile bool _consentAdopted;   // this load read the account's consent before anything was pushed

        /// <summary>The six consent flags (WPF sends all six on every sync).</summary>
        public const SyncBody.Field Privacy = AdoptedConsent | OwnedConsent;
        /// <summary>The two the profile read returns: adopted on load (ProfileAdopt.AdoptConsent), then on every sync.</summary>
        public const SyncBody.Field AdoptedConsent = SyncBody.Field.AllowDiscordDm | SyncBody.Field.ShowOnlineStatus;
        /// <summary>The four the profile read does not return: on every sync only for the account that set them on
        /// this install, so a local default never overwrites what another device chose.</summary>
        public const SyncBody.Field OwnedConsent = SyncBody.Field.ShareProfilePicture | SyncBody.Field.PublicShareAvatar
            | SyncBody.Field.GoonShareAvatar | SyncBody.Field.GoonShareDm;

        /// <summary>Which consent flags a sync of <paramref name="s"/> carries. None before this load adopted the
        /// account's values: adopt first, push second.</summary>
        public static SyncBody.Field ConsentFields(AppSettings s, bool adopted)
        {
            var owned = !string.IsNullOrEmpty(s.UnifiedId) && string.Equals(s.ConsentOwnedAccount, s.UnifiedId, StringComparison.Ordinal);
            return (adopted ? AdoptedConsent : SyncBody.Field.None) | (owned ? Privacy : SyncBody.Field.None);
        }

        /// <summary>WPF ChkAllowDiscordDm_Changed and its five siblings, and the Goon consent sheet: this account's
        /// consent is now set HERE (persisted), and it is pushed now so a REVOKE lands at once. Inside the cooldown
        /// (or with a push in flight) one coalesced push follows; a restart before it lands still owes it.</summary>
        public async Task<bool> PushPrivacyAsync(string reason = "privacy")
        {
            var s = CoreSettings.Current;
            if (!SignedIn(s)) return false;   // signed out: the switch is local, nothing is owed
            s.ConsentOwnedAccount = s.UnifiedId;
            s.ConsentPushPending = true;
            CoreSettings.Save();
            if (!Loaded) return false;        // the load's own push carries it
            if (await PushAsync(reason, waitForGate: true)) return true;
            Nudge(reason);
            return false;
        }


        /// <summary>WPF ProfileSyncService.PendingCosmeticsClear: set by an EMPTY Customize save (unequip everything),
        /// cleared once a sync carrying the clear succeeds. In memory, as WPF.</summary>
        public bool PendingCosmeticsClear => _pendingCosmeticsClear;

        /// <summary>Tests only.</summary>
        public Func<DateTime> UtcNow = () => DateTime.UtcNow;

        // Failure backoff (release/6.11.5, WPF ProfileSyncService.NoteSyncFailureForBackoff). Touched only inside _gate.
        private int _backoffFailures;
        private DateTime? _blockedUntilUtc;
        private string? _backoffToken;
        private TimeSpan _backoffOffset;

        /// <summary>WPF ProfileSyncService.IsExpectedCancellation: cancelled or disposed, directly or one level down.</summary>
        internal static bool IsExpectedCancellation(Exception ex) =>
            ex is OperationCanceledException or ObjectDisposedException
            || ex.InnerException is OperationCanceledException or ObjectDisposedException;

        private void NoteFailureForBackoff(int? status, string? tokenUsed)
        {
            _backoffFailures++;
            var wait = SyncFailureBackoff.Delay(_backoffFailures);
            _blockedUntilUtc = UtcNow() + wait;
            (_backoffToken, _backoffOffset) = (tokenUsed, ServerClock.Offset);
            Log.Warning("Profile sync backing off {Seconds:F0}s after failure #{Count} (status {Status})",
                wait.TotalSeconds, _backoffFailures, status?.ToString() ?? "none");
        }

        public bool Loaded { get; private set; }
        /// <summary>The fuse fed by each successful response's <c>descent_countdown</c> block.</summary>
        public Descent.DescentCountdownService? Countdown { get; set; }
        /// <summary>The stage ladder in hand (the head's DescentService block), for the migration's drip queue.</summary>
        public Func<Descent.DescentStage?>? StageLadder { get; set; }
        /// <summary>The silent restore wrote the ledger: the head repaints what reads level, XP and the spiral.</summary>
        public Action? MigrationApplied { get; set; }
        /// <summary>A sync the server accepted (WPF asks the descent block again here: today's XP just landed).</summary>
        public Action? Accepted { get; set; }
        public DateTime? LastSyncTime { get; private set; }

        /// <param name="localAchievements">This install's unlocked achievement ids.</param>
        /// <param name="inSession">The heartbeat's in_session.</param>
        /// <param name="handler">Test seam; null is the real network.</param>
        /// <param name="sanitizeCosmetics">The head's SanitizeOwn (registry + your unlocks); null never sends cosmetics.</param>
        public SyncPush(Func<IEnumerable<string>?> localAchievements, Func<bool> inSession, HttpMessageHandler? handler = null,
            Func<ProfileCosmetics?, ProfileCosmetics>? sanitizeCosmetics = null)
        {
            (_localAchievements, _inSession, _handler, _sanitizeCosmetics) = (localAchievements, inSession, handler, sanitizeCosmetics);
            _http = V2AuthService.Configure(new HttpClient(handler ?? new ServerClockHandler()));   // learns the server clock (release/6.11.5)
        }

        /// <summary>The profile load succeeded: the baseline pushes are allowed against.</summary>
        /// <param name="consentAdopted">The load ran ProfileAdopt.AdoptConsent on the account's profile first.</param>
        public void MarkLoaded(IEnumerable<string>? serverAchievements, bool consentAdopted = false)
        {
            _serverAchievements = serverAchievements?.ToArray();
            _consentAdopted = consentAdopted;
            Loaded = true;
        }

        /// <summary>Logout: nothing is pushed until the next load.</summary>
        public void Reset()
        {
            StopHeartbeat();
            Loaded = false;
            _serverAchievements = null;
            _pendingCosmeticsClear = false;   // never carry one account's unequip into the next
            _consentAdopted = false;          // the next account's consent is read before anything is sent
            LastSyncTime = null;
        }

        private static bool SignedIn(AppSettings s) => !s.OfflineMode && !string.IsNullOrEmpty(s.UnifiedId) && CoreAccount.IsLoggedIn;

        /// <summary>The body: known fields only (achievements only when <paramref name="achievements"/> is known, never
        /// shrunk to local-only). Xp is the TOTAL, as WPF sends it.</summary>
        /// <param name="cosmetics">An explicit loadout save (WPF BuildCosmeticsPayload after a load: the sanitized
        /// loadout, the empty one included - that is the unequip-everything clear). Null leaves the key out.</param>
        public static SyncBody Body(AppSettings s, IEnumerable<string>? achievements, ProfileCosmetics? cosmetics = null, SyncBody.Field consent = SyncBody.Field.None) => new()
        {
            Known = (achievements == null ? Sent & ~SyncBody.Field.Achievements : Sent)
                    | (cosmetics == null ? SyncBody.Field.None : SyncBody.Field.Cosmetics)
                    | (consent & Privacy),
            UnifiedId = s.UnifiedId,
            Xp = (int)ProfileAdopt.TotalXp(s),
            Level = s.PlayerLevel,
            DescentEpoch = Descent.DescentEpochs.ClientEpoch,
            DescentAuto = true,   // this head takes the offer silently (DescentMigration.ApplyRestore)
            Achievements = achievements?.Distinct().OrderBy(a => a, StringComparer.Ordinal).ToList(),
            Cosmetics = cosmetics,
            AllowDiscordDm = s.AllowDiscordDm,
            ShowOnlineStatus = s.ShowOnlineStatus,
            ShareProfilePicture = s.ShareProfilePicture,
            PublicShareAvatar = s.PublicShareRealAvatar,
            GoonShareAvatar = s.GoonShareAvatar,
            GoonShareDm = s.GoonShareDiscordDm,
        };

        /// <summary>WPF PersistOwnCosmetics' push, after <paramref name="chosen"/> was saved to settings. Every push
        /// sends the settings loadout (WPF BuildCosmeticsPayload), so a cooldown skip, a logout or a restart cannot
        /// lose it; an empty <paramref name="chosen"/> is the explicit clear, held until a sync delivers it.</summary>
        public Task<bool> PushCosmeticsAsync(ProfileCosmetics chosen)
        {
            _pendingCosmeticsClear = chosen.IsEmpty;
            return PushAsync("cosmetics");
        }

        /// <summary>WPF BuildCosmeticsPayload: the sanitized settings loadout. An empty one goes only as the explicit
        /// clear - WPF also sends it after a load because it adopted the cloud loadout first (AdoptCloudCosmetics);
        /// this head does not adopt, so an empty push would wipe the account's cosmetics from a fresh install.
        /// Any failure leaves the key out ("no change" never destroys anything).</summary>
        private ProfileCosmetics? CosmeticsPayload(AppSettings s, bool clear)
        {
            if (_sanitizeCosmetics == null) return null;
            try
            {
                var clean = _sanitizeCosmetics(s.ProfileCosmetics);
                return !clean.IsEmpty || clear ? clean : null;
            }
            catch (Exception ex) { Log.Debug("SyncPush cosmetics payload: {E}", ex.Message); return null; }
        }

        /// <param name="waitForGate">Logout: wait (bounded) for an in-flight push instead of skipping.</param>
        public async Task<bool> PushAsync(string reason, bool waitForGate = false)
        {
            var s = CoreSettings.Current;
            if (!Loaded || !SignedIn(s)) { Log.Debug("Profile sync skipped ({Reason}) - not loaded this session or not signed in", reason); return false; }
            if (!await _gate.WaitAsync(waitForGate ? TimeSpan.FromSeconds(5) : TimeSpan.Zero)) return false;
            try
            {
                if (LastSyncTime is { } last && UtcNow() - last < Cooldown) { Log.Debug("Profile sync skipped - cooldown active"); return false; }
                if (SyncFailureBackoff.ShouldSkip(UtcNow(), _blockedUntilUtc, _backoffToken, s.AuthToken, _backoffOffset, ServerClock.Offset))
                {
                    Log.Debug("Profile sync skipped - backing off after {Failures} failure(s)", _backoffFailures);
                    return false;
                }
                var totalXp = ProfileAdopt.TotalXp(s);
                var watermark = ProfileAdopt.ActiveXpWatermark(s);
                if (watermark > 0 && totalXp < watermark)
                {
                    Log.Error("[XP watermark] Sync REFUSED — would push {Xp} XP, below the {Watermark} XP last agreed", (int)totalXp, (int)watermark);
                    return false;
                }
                var id = s.UnifiedId!;
                if (!Loaded || !SignedIn(s)) return false;   // a logout between the check above and the gate
                var server = _serverAchievements;
                var clearing = _pendingCosmeticsClear;
                var cosmetics = CosmeticsPayload(s, clearing);
                var consent = ConsentFields(s, _consentAdopted);
                // Snapshot what this body says, to tell a switch flipped while it was in flight.
                var consentSent = (s.AllowDiscordDm, s.ShowOnlineStatus, s.ShareProfilePicture, s.PublicShareRealAvatar, s.GoonShareAvatar, s.GoonShareDiscordDm);
                var body = JsonConvert.SerializeObject(Body(s, server?.Concat(_localAchievements() ?? Array.Empty<string>()), cosmetics, consent));
                // THE CHOICE SUBMIT (WPF SyncProfileAsync): grafted on only while a choice waits for its ack, so
                // every other sync is byte-identical. The re-derived ledger rides the ordinary xp/level fields.
                var pendingChoice = s.PendingDescentMigrationChoice;
                var migrationInFlight = Descent.DescentMigrationChoices.IsValid(pendingChoice);
                if (migrationInFlight)
                {
                    var grafted = JObject.Parse(body);
                    grafted["descent_migration"] = new JObject { ["choice"] = pendingChoice };
                    body = grafted.ToString(Formatting.None);
                }
                var tokenUsed = s.AuthToken;
                HttpRequestMessage NewRequest()
                {
                    var r = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/v2/user/sync");
                    if (!string.IsNullOrEmpty(tokenUsed)) r.Headers.Add("X-Auth-Token", tokenUsed);
                    r.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    SyncBody.SignRequest(r, id, body);
                    return r;
                }
                Log.Information("Syncing profile ({Reason}) - Level: {Level}, TotalXP: {Xp}", reason, s.PlayerLevel, (int)totalXp);
                using var request = NewRequest();
                var response = await _http.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                // A 403 for clock skew: the signature was right, our timestamp was not. Learn the
                // server's time from the body and re-sign ONCE (release/6.11.5).
                if (response.StatusCode == HttpStatusCode.Forbidden
                    && string.Equals(ServerClock.ParseRefusal(json, out var serverTime), "clock_skew", StringComparison.OrdinalIgnoreCase))
                {
                    if (serverTime != null) ServerClock.Observe(serverTime, DateTimeOffset.UtcNow);
                    Log.Warning("V2 Profile sync refused for clock skew; re-signing with server offset {Seconds:F0}s and retrying once", ServerClock.Offset.TotalSeconds);
                    response.Dispose();
                    using var retry = NewRequest();
                    response = await _http.SendAsync(retry);
                    json = await response.Content.ReadAsStringAsync();
                }
                using var _ = response;
                // Signed out (or into another account) while this was in flight: the answer is not ours to apply.
                if (!Loaded || !string.Equals(s.UnifiedId, id, StringComparison.Ordinal))
                {
                    Log.Information("Profile sync response dropped - the account changed while it was in flight");
                    return false;
                }
                if (!response.IsSuccessStatusCode)
                {
                    // Contract D: merged tombstone; the head's recovery re-signs in and reloads.
                    if (V2AuthService.MergedRecovery is { } merged && await merged(response, json)) return false;
                    // 429, and a 409 no recovery handled: cool down rather than retry on every trigger.
                    if (response.StatusCode is (HttpStatusCode)429 or HttpStatusCode.Conflict)
                    {
                        LastSyncTime = UtcNow();
                        Log.Warning("V2 Profile sync refused by server ({Status}), will retry after the cooldown", (int)response.StatusCode);
                        return false;
                    }
                    // WPF SyncProfileAsync: a 401 runs the recovery (no retry here; the next trigger pushes with
                    // whatever it recovered). Real network only: a test transport must never reach the proxy.
                    if (_handler == null && response.StatusCode == HttpStatusCode.Unauthorized)
                        await AuthRecovery.HandleUnauthorizedAsync(response);
                    Log.Warning("V2 Profile sync failed: {Status} (error body {Bytes} bytes)", (int)response.StatusCode, json.Length);
                    NoteFailureForBackoff((int)response.StatusCode, tokenUsed);
                    return false;
                }
                LastSyncTime = UtcNow();
                (_backoffFailures, _blockedUntilUtc) = (0, null);
                // The clear has reached the server; an empty loadout goes back to meaning "no change" (WPF).
                if (clearing && cosmetics?.IsEmpty == true) _pendingCosmeticsClear = false;
                // The six were delivered as sent: nothing is owed unless a switch moved while this was in flight.
                if ((consent & Privacy) == Privacy && s.ConsentPushPending
                    && consentSent == (s.AllowDiscordDm, s.ShowOnlineStatus, s.ShareProfilePicture, s.PublicShareRealAvatar, s.GoonShareAvatar, s.GoonShareDiscordDm))
                    s.ConsentPushPending = false;
                Log.Information("V2 Profile synced successfully ({Bytes} bytes)", json.Length);
                try
                {
                    var reply = JObject.Parse(json);
                    // While a submit is unacked the server still quotes the pre-migration ledger: only a response
                    // carrying the ack may move level, XP or the watermark (WPF "holding the ceremony's ledger").
                    if (migrationInFlight && !Descent.DescentMigration.IsAck(reply))
                        Log.Warning("[Descent] Migration submit was not acknowledged in this response - holding Level {Level}, will re-submit on the next sync.", s.PlayerLevel);
                    else
                        ProfileAdopt.ApplySyncResponse(s, reply, UtcNow());
                    // WPF HandleDescentMigrationAck: settle/heal an account migrated on any device.
                    Descent.DescentMigrationAck.Apply(s, reply);
                    // WPF HandleDescentMigrationOffer -> ApplyOfferNow: the offer is taken at once, as "restore",
                    // with no window. The submit rides the next sync (the pending choice is on disk).
                    if (Descent.DescentMigration.ReadOffer(reply) is { } offer
                        && Descent.DescentMigration.ApplyRestore(s, offer, StageLadder?.Invoke(), UtcNow()))
                    {
                        LastSyncTime = null;   // the submit is not held behind the cooldown
                        try { MigrationApplied?.Invoke(); } catch (Exception ex) { Log.Debug("MigrationApplied: {E}", ex.Message); }
                        var submit = Task.Run(() => PushAsync("descent migration submit", waitForGate: true));
                    }
                }
                catch (Exception ex) { Log.Debug("V2 Sync: Could not parse server flags: {Error}", ex.Message); }
                // THE FUSE's cache, off the RAW body (WPF ProfileSyncService.HandleDescentCountdown).
                if (Countdown != null && Descent.DescentCountdownService.TryReadCeremonyAt(json, out var ceremonyAt))
                    Countdown.ApplyCeremonyAt(ceremonyAt);
                CoreSettings.Save();
                try { Accepted?.Invoke(); } catch (Exception ex) { Log.Debug("SyncPush.Accepted: {E}", ex.Message); }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Profile sync failed");
                // WPF ProfileSyncService: a shutdown cancellation is not a server failure, but HttpClient.Timeout
                // (a cancellation wrapping a TimeoutException) is a hung proxy and backs off like any other.
                if (!IsExpectedCancellation(ex) || ex.InnerException is TimeoutException)
                    NoteFailureForBackoff(null, s.AuthToken);
                return false;
            }
            finally { _gate.Release(); }
        }

        /// <summary>WPF ProfileSyncService.SyncBeforeRetryAsync (#1300): one real sync before a balance refusal is
        /// asked again. Waits for a push already running; inside the 30 s cooldown waits out the rest of it once.
        /// True only when a sync reached the server.</summary>
        public async Task<bool> SyncBeforeRetryAsync()
        {
            var before = LastSyncTime;
            if (await PushAsync("purchase-retry", waitForGate: true)) return true;
            if (LastSyncTime != before) return true;   // the push we waited behind landed
            var left = LastSyncTime is { } last ? Cooldown - (UtcNow() - last) : TimeSpan.Zero;
            if (left <= TimeSpan.Zero || left > Cooldown) return false;
            await Task.Delay(left + TimeSpan.FromMilliseconds(250));
            return await PushAsync("purchase-retry", waitForGate: true);
        }

        /// <summary>WPF NudgeSyncSoon: one coalesced push, 3 s out or just past the cooldown.</summary>
        public void Nudge(string reason)
        {
            if (!Loaded || Interlocked.Exchange(ref _nudgePending, 1) == 1) return;
            var wait = NudgeSettle;
            if (LastSyncTime is { } last && UtcNow() - last < Cooldown) wait = Cooldown - (UtcNow() - last) + NudgeCooldownSlack;
            _ = Task.Delay(wait).ContinueWith(async _ =>
            {
                Interlocked.Exchange(ref _nudgePending, 0);
                await PushAsync(reason);
            }, TaskScheduler.Default);
        }

        /// <summary>WPF StartHeartbeat: one now, then every 120 s.</summary>
        public void StartHeartbeat()
        {
            if (_heartbeat != null) return;
            _heartbeat = new Timer(_ => _ = HeartbeatAsync(), null, TimeSpan.Zero, HeartbeatInterval);
            Log.Information("Heartbeat started (every {Seconds}s)", HeartbeatInterval.TotalSeconds);
        }

        internal bool HeartbeatRunning => Volatile.Read(ref _heartbeat) != null;

        public void StopHeartbeat()
        {
            Interlocked.Exchange(ref _heartbeat, null)?.Dispose();
        }

        /// <summary>WPF's four fields (Core <see cref="SyncBody.Heartbeat"/>); is_active true, as WPF with no idle tracker.</summary>
        public Task<bool> HeartbeatAsync()
        {
            var s = CoreSettings.Current;
            if (!SignedIn(s)) return Task.FromResult(false);
            return new V2AuthService(() => s, _handler).SendHeartbeatAsync(s.UnifiedId!, true, _inSession());
        }
    }
}
