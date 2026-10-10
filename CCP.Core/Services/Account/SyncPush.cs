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
    /// ponytail: no restore-from-backup reconcile, 401 recovery or heartbeat adopt - add with the features that need them.
    /// </summary>
    public sealed class SyncPush
    {
        public const SyncBody.Field Sent = SyncBody.Field.UnifiedId | SyncBody.Field.Xp | SyncBody.Field.Level
            | SyncBody.Field.DescentEpoch | SyncBody.Field.Achievements;
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
        private int _privacyVersion, _privacyDelivered;   // a consent switch changed / the last change a sync delivered

        /// <summary>The six consent flags a Privacy and Sharing switch pushes on change (WPF sends them on every
        /// sync because it adopts the server's values on load; this head does not adopt them, so they go ONLY after
        /// the user changed one here, and stay on every push until a sync delivers them - a stale local default can
        /// never overwrite what the account holds).</summary>
        public const SyncBody.Field Privacy = SyncBody.Field.AllowDiscordDm | SyncBody.Field.ShowOnlineStatus
            | SyncBody.Field.ShareProfilePicture | SyncBody.Field.PublicShareAvatar | SyncBody.Field.GoonShareAvatar
            | SyncBody.Field.GoonShareDm;

        /// <summary>A consent change is waiting for a sync to carry it.</summary>
        public bool PendingPrivacy => Volatile.Read(ref _privacyVersion) != Volatile.Read(ref _privacyDelivered);

        /// <summary>WPF ChkAllowDiscordDm_Changed and its five siblings: push now so a REVOKE lands at once. Inside
        /// the cooldown (or with a push in flight) one coalesced push follows it.</summary>
        public async Task<bool> PushPrivacyAsync()
        {
            if (!Loaded || !SignedIn(CoreSettings.Current)) return false;   // signed out: the switch is local, nothing is owed
            Interlocked.Increment(ref _privacyVersion);
            if (await PushAsync("privacy", waitForGate: true)) return true;
            Nudge("privacy");
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
        public void MarkLoaded(IEnumerable<string>? serverAchievements)
        {
            _serverAchievements = serverAchievements?.ToArray();
            Loaded = true;
        }

        /// <summary>Logout: nothing is pushed until the next load.</summary>
        public void Reset()
        {
            StopHeartbeat();
            Loaded = false;
            _serverAchievements = null;
            _pendingCosmeticsClear = false;   // never carry one account's unequip into the next
            Volatile.Write(ref _privacyDelivered, Volatile.Read(ref _privacyVersion));   // nor its consent change
            LastSyncTime = null;
        }

        private static bool SignedIn(AppSettings s) => !s.OfflineMode && !string.IsNullOrEmpty(s.UnifiedId) && CoreAccount.IsLoggedIn;

        /// <summary>The body: known fields only (achievements only when <paramref name="achievements"/> is known, never
        /// shrunk to local-only). Xp is the TOTAL, as WPF sends it.</summary>
        /// <param name="cosmetics">An explicit loadout save (WPF BuildCosmeticsPayload after a load: the sanitized
        /// loadout, the empty one included - that is the unequip-everything clear). Null leaves the key out.</param>
        public static SyncBody Body(AppSettings s, IEnumerable<string>? achievements, ProfileCosmetics? cosmetics = null, bool privacy = false) => new()
        {
            Known = (achievements == null ? Sent & ~SyncBody.Field.Achievements : Sent)
                    | (cosmetics == null ? SyncBody.Field.None : SyncBody.Field.Cosmetics)
                    | (privacy ? Privacy : SyncBody.Field.None),
            UnifiedId = s.UnifiedId,
            Xp = (int)ProfileAdopt.TotalXp(s),
            Level = s.PlayerLevel,
            DescentEpoch = Descent.DescentEpochs.ClientEpoch,
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
                var privacyVersion = Volatile.Read(ref _privacyVersion);
                var privacy = privacyVersion != Volatile.Read(ref _privacyDelivered);
                var body = JsonConvert.SerializeObject(Body(s, server?.Concat(_localAchievements() ?? Array.Empty<string>()), cosmetics, privacy));
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
                    Log.Warning("V2 Profile sync failed: {Status} (error body {Bytes} bytes)", (int)response.StatusCode, json.Length);
                    NoteFailureForBackoff((int)response.StatusCode, tokenUsed);
                    return false;
                }
                LastSyncTime = UtcNow();
                (_backoffFailures, _blockedUntilUtc) = (0, null);
                // The clear has reached the server; an empty loadout goes back to meaning "no change" (WPF).
                if (clearing && cosmetics?.IsEmpty == true) _pendingCosmeticsClear = false;
                if (privacy) Volatile.Write(ref _privacyDelivered, privacyVersion);   // a change made in flight stays pending
                Log.Information("V2 Profile synced successfully ({Bytes} bytes)", json.Length);
                try
                {
                    var reply = JObject.Parse(json);
                    ProfileAdopt.ApplySyncResponse(s, reply, UtcNow());
                    // WPF HandleDescentMigrationAck: settle/heal an account migrated on any device. This head never
                    // takes the offer (no descent_auto), so only the ack half runs here.
                    Descent.DescentMigrationAck.Apply(s, reply);
                }
                catch (Exception ex) { Log.Debug("V2 Sync: Could not parse server flags: {Error}", ex.Message); }
                // THE FUSE's cache, off the RAW body (WPF ProfileSyncService.HandleDescentCountdown).
                if (Countdown != null && Descent.DescentCountdownService.TryReadCeremonyAt(json, out var ceremonyAt))
                    Countdown.ApplyCeremonyAt(ceremonyAt);
                CoreSettings.Save();
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
