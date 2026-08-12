using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handles syncing user progression (XP, level, achievements) to the cloud.
    /// Supports both Patreon and Discord authentication.
    /// </summary>
    public class ProfileSyncService : IDisposable
    {
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
        private const int HeartbeatIntervalSeconds = 120; // Send heartbeat every 2 minutes

        /// <summary>
        /// Total (cumulative) XP below which a profile is indistinguishable from fresh defaults.
        /// Same number the boot defaults-guard in <see cref="SyncProfileAsync"/> uses, so "looks
        /// like defaults" means one thing everywhere.
        /// </summary>
        public const double MeaningfulProgressXp = 100;

        /// <summary>
        /// Does a profile the SERVER handed us look like an empty/uninitialized record rather than
        /// a real account? Used only in the anti-cheat clamp branches, where local is already far
        /// ahead of the server: true means "do not clamp, keep local".
        ///
        /// #865: this used to additionally require no achievements, no unlocked skills AND
        /// <c>skill_points == 0</c>. Every one of those is an unrelated field, and any single
        /// non-zero one flipped the answer to "the server record is real" — so a user whose server
        /// row had been emptied of level/XP but still carried, say, a banked skill point or one
        /// achievement failed the test and got CLAMPED to Level 1 / 0 XP. The clamp writes local
        /// settings, the next launch reads them back, and the server row never changes, so it
        /// repeated on EVERY launch: the every-launch progression reset.
        ///
        /// The only two fields that can say "this account has progressed" are level and XP, so
        /// they are the only two consulted. A profile with a meaningful level or meaningful XP is
        /// never uninitialized; a Level&lt;=1, &lt;100 XP record always is, whatever else rides
        /// along with it. Being wrong in this direction costs nothing: local is kept and the next
        /// successful sync pushes it back up. Being wrong the other way destroys the account.
        /// </summary>
        public static bool ServerProfileLooksUninitialized(int serverLevel, double serverTotalXp)
            => serverLevel <= 1 && serverTotalXp < MeaningfulProgressXp;

        /// <summary>
        /// Is this server record too empty for the anti-cheat CLAMP to adopt? Asked only at the
        /// two clamp sites, where local is already 50-75k total XP AHEAD of the server.
        ///
        /// <see cref="ServerProfileLooksUninitialized"/> narrows #865 but does not close it. Its
        /// XP floor is 100, so a server row emptied down to Level 1 with 150 XP reads as a "real"
        /// record — and the clamp then resets a Level 40 local to Level 1 on EVERY launch, exactly
        /// the original bug with one extra digit of survivorship.
        ///
        /// The clamp does not actually need to know whether the row is pristine. It needs to know
        /// whether the row can plausibly be the truth for THIS account, and a Level 1 record never
        /// can when the local profile is tens of thousands of XP ahead of it: no legitimate
        /// server-side correction lands a progressed account back on Level 1. The server zeroes a
        /// season through the explicit <c>level_reset</c> flag, which is handled on its own branch
        /// well before this one — it does not do it by quietly answering "Level 1" to a sync.
        ///
        /// So the clamp refuses any level &lt;= 1 record whatever XP rides along with it. This
        /// strictly subsumes <see cref="ServerProfileLooksUninitialized"/> (which also requires
        /// level &lt;= 1); that predicate stays for its other callers and for the 100 XP floor it
        /// shares with the boot defaults-guard.
        ///
        /// The residual cost is that a genuinely fresh Level 1 account cannot be clamped back down
        /// over a hand-edited local file. That is the cheap direction to be wrong in: local is kept
        /// and pushed, and the SERVER's own anti-cheat is what actually decides what the account is
        /// worth. Being wrong the other way destroys a real account on every launch.
        /// </summary>
        public static bool ServerProfileTooEmptyToClampTo(int serverLevel)
            => serverLevel <= 1;

        #region Server-confirmed XP watermark (#865 regression guard)

        /// <summary>
        /// The watermark that currently applies, or 0 when none does.
        ///
        /// A stored watermark only counts when it belongs to BOTH the account we are syncing and
        /// the season we are in. Season scope matters because a rollover lowers seasonal XP on
        /// purpose - an unscoped watermark would fight the rollover every launch, forever. Account
        /// scope matters because two accounts sharing a machine have nothing to say about each
        /// other's totals.
        ///
        /// V2 identities only. A legacy user has neither a unified_id nor a season key, so their
        /// scope would be the pair ("", "") - which never changes, is therefore never voided by a
        /// rollover, and is SHARED by every legacy account on the machine. See
        /// <see cref="RecordAgreedServerXp"/> for why arming them was dropped rather than fixed.
        /// </summary>
        public static double ActiveXpWatermark(Models.AppSettings settings)
        {
            if (settings.LastConfirmedServerXp <= 0) return 0;

            var account = settings.UnifiedId ?? string.Empty;
            if (account.Length == 0) return 0;   // legacy/V1 identity - out of scope entirely
            if (!string.Equals(settings.LastConfirmedServerXpAccount ?? string.Empty, account, StringComparison.Ordinal))
                return 0;

            var season = settings.CurrentSeason ?? string.Empty;
            if (!string.Equals(settings.LastConfirmedServerXpSeason ?? string.Empty, season, StringComparison.Ordinal))
                return 0;

            return settings.LastConfirmedServerXp;
        }

        /// <summary>
        /// Record the total this client and the server now AGREE on, for this (account, season).
        ///
        /// The watermark models the LAST AGREED figure, not "the highest the server ever said".
        /// That distinction is the entire fix. The first draft only ever let the number rise, while
        /// the send-guard enforced it as "the lowest the client may send" - two different things
        /// wearing one field. So the moment a client legitimately adopted a LOWER server figure -
        /// an anti-cheat correction, which is exactly what the clamp branch in
        /// <see cref="SyncProfileAsync"/> does - the watermark stayed at the old high and every
        /// subsequent sync failed the send-guard against it. Not just the XP: the guard sits before
        /// the POST, so achievements, quests and cosmetics stopped going up too, "Cloud sync issue"
        /// latched in the title bar, and because the watermark is persisted it survived restarts.
        /// A guard that latches like that is worse than the regression it was written for.
        ///
        /// Setting it downward on agreement makes it self-healing: adopt, agree, carry on. It still
        /// cannot be moved by a local calculation - every caller passes a figure that came out of a
        /// server response - so a corrupted settings file still cannot talk it down.
        ///
        /// Agreement is decided here, once, rather than at each call site: we agree when this
        /// client is NOT holding more than the server just reported. When it IS holding more it has
        /// deliberately kept local (the clamp's defend branch, take-higher keeping a higher local),
        /// which is a disagreement, and the previously agreed figure stands.
        /// </summary>
        /// <param name="serverTotalXp">Cumulative XP as the server just reported it.</param>
        /// <param name="clientTotalXp">Cumulative XP this client holds AFTER reconciling.</param>
        public static void RecordAgreedServerXp(Models.AppSettings settings, double serverTotalXp, double clientTotalXp, string site)
        {
            if (serverTotalXp < MeaningfulProgressXp) return;   // nothing worth defending

            // V1/legacy users get no watermark at all - see ActiveXpWatermark. Arming one for them
            // would create an ("", "") scope with no rollover escape: their seasonal reset has no
            // season key to notice, so the send-guard would block their pushes with nothing but a
            // manual logout to clear it.
            if (string.IsNullOrEmpty(settings.UnifiedId))
            {
                App.Logger?.Debug("[XP watermark] {Site}: not arming — legacy identity has no account/season scope", site);
                return;
            }

            if (clientTotalXp > serverTotalXp + 0.01)
            {
                App.Logger?.Debug("[XP watermark] {Site}: not recording {Sx} — this client kept a higher local total ({Cx}), so there is no agreement to record",
                    site, (int)serverTotalXp, (int)clientTotalXp);
                return;
            }

            var account = settings.UnifiedId!;
            var season = settings.CurrentSeason ?? string.Empty;
            var previous = ActiveXpWatermark(settings);

            settings.LastConfirmedServerXpAccount = account;
            settings.LastConfirmedServerXpSeason = season;
            settings.LastConfirmedServerXp = serverTotalXp;

            if (previous > 0 && serverTotalXp < previous)
                App.Logger?.Information("[XP watermark] {Site}: LOWERED {Old} -> {New} — this client adopted the server's figure, so that is what both sides now agree on. The send-guard follows it down.",
                    site, (int)previous, (int)serverTotalXp);
            else
                App.Logger?.Debug("[XP watermark] {Site}: set to {Xp} for account {Account} season {Season}",
                    site, (int)serverTotalXp, account, season);
        }

        /// <summary>
        /// Void the watermark. Called when the account's XP is legitimately allowed to fall: a
        /// season rollover or an explicit logout/account switch (MainWindow.ClearProgressionData).
        /// Without this the guard would block the very resets it is supposed to let through.
        /// </summary>
        public static void ClearXpWatermark(Models.AppSettings? settings, string reason)
        {
            if (settings == null) return;
            if (settings.LastConfirmedServerXp <= 0 && settings.LastConfirmedServerXpAccount == null) return;

            App.Logger?.Information("[XP watermark] cleared ({Reason}) - was {Xp} for season {Season}",
                reason, (int)settings.LastConfirmedServerXp, settings.LastConfirmedServerXpSeason ?? "(none)");
            settings.LastConfirmedServerXp = 0;
            settings.LastConfirmedServerXpAccount = null;
            settings.LastConfirmedServerXpSeason = null;
        }

        #endregion

        private readonly HttpClient _httpClient;
        private DispatcherTimer? _heartbeatTimer;
        private bool _disposed;
        private bool _syncEnabled = true;
        private bool _pendingQuestResetClear;
        private DateTime _lastAuthRecoveryAttempt = DateTime.MinValue;
        private bool _hasLoadedProfile; // true after first successful LoadProfileAsync/SyncProfileAsync round-trip
        private readonly SemaphoreSlim _syncGate = new(1, 1);

        /// <summary>
        /// Set by the Customize dialog when the user saves an EMPTY loadout, i.e. they deliberately
        /// unequipped everything. See <see cref="BuildCosmeticsPayload"/>: without this flag an
        /// empty loadout is sent as null ("no change") so a fresh machine cannot wipe the account's
        /// cosmetics before it has ever read them. Cleared once a sync carrying the clear succeeds.
        /// </summary>
        public bool PendingCosmeticsClear { get; set; }

        /// <summary>
        /// Whether using Patreon auth (vs Discord)
        /// </summary>
        private bool IsPatreonAuth => !string.IsNullOrEmpty(App.Patreon?.GetAccessToken());

        /// <summary>
        /// Whether using Discord auth
        /// </summary>
        private bool IsDiscordAuth => !IsPatreonAuth && !string.IsNullOrEmpty(App.Discord?.GetAccessToken());

        /// <summary>
        /// Get the appropriate access token (Patreon preferred, then Discord)
        /// </summary>
        private string? GetAccessToken() => App.Patreon?.GetAccessToken() ?? App.Discord?.GetAccessToken();

        /// <summary>
        /// Whether cloud sync is enabled (checks for either Patreon or Discord token)
        /// </summary>
        public bool IsSyncEnabled => _syncEnabled && App.IsLoggedIn;

        /// <summary>
        /// Last sync time
        /// </summary>
        public DateTime? LastSyncTime { get; private set; }

        /// <summary>
        /// Last sync error (if any)
        /// </summary>
        public string? LastSyncError { get; private set; }

        /// <summary>
        /// Number of consecutive sync failures. Reset to 0 on success.
        /// </summary>
        public int ConsecutiveSyncFailures { get; private set; }

        /// <summary>
        /// Raised when sync health changes (failure count goes up or resets to 0).
        /// Parameter is the current failure count.
        /// </summary>
        public event EventHandler<int>? SyncHealthChanged;

        /// <summary>
        /// Event raised when cloud profile is loaded and merged with local data.
        /// MainWindow should subscribe to this to refresh UI.
        /// </summary>
        public event EventHandler? ProfileLoaded;

        /// <summary>
        /// Repaint the header if this sync round-trip changed the level/XP on screen.
        ///
        /// <see cref="ProfileLoaded"/> used to be raised from <see cref="LoadProfileAsync"/> only,
        /// but <see cref="SyncProfileAsync"/> ADOPTS server progression in four places - the
        /// restore reconcile, the level_reset handler, the server-is-ahead branch and the
        /// anti-cheat clamp - and none of them told anybody. The level pill, XP bar, rank title and
        /// unlockables are written imperatively by MainWindow.UpdateLevelDisplay, not bound, so an
        /// adoption mid-run left the whole header showing the pre-sync numbers until the next
        /// restart: the "level/XP display is wrong after a purchase / sign-in" half of #879.
        ///
        /// Comparing a snapshot rather than flagging each write site means a future adopt path
        /// cannot forget to opt in. A false positive (the user earned XP while the request was in
        /// flight) costs one idempotent repaint.
        /// </summary>
        private void RaiseProfileLoadedIfProgressionChanged(Models.AppSettings? settings, int preLevel, double preLevelXp, string source)
        {
            try
            {
                if (settings == null) return;
                if (settings.PlayerLevel == preLevel && Math.Abs(settings.PlayerXP - preLevelXp) < 0.01) return;

                App.Logger?.Information("{Source}: progression changed Level {OldLevel} ({OldXp} XP) -> Level {NewLevel} ({NewXp} XP) — repainting header",
                    source, preLevel, (int)preLevelXp, settings.PlayerLevel, (int)settings.PlayerXP);
                RaiseProfileLoadedSafely(source);
            }
            catch (Exception ex)
            {
                // A subscriber throwing must never fail the sync that already succeeded.
                App.Logger?.Warning(ex, "{Source}: ProfileLoaded notification failed", source);
            }
        }

        /// <summary>
        /// Raise <see cref="ProfileLoaded"/> without ever blocking the calling thread.
        ///
        /// The one subscriber, <c>MainWindow.OnProfileLoaded</c>, wraps its whole body in a
        /// BLOCKING <c>Dispatcher.Invoke</c>. The exit path in <c>App.OnExit</c> runs the final
        /// sync as <c>Task.Run(() =&gt; SyncProfileAsync()).Wait(2s)</c> ON the UI thread. Raising
        /// the event inline from the pool thread therefore deadlocked every quit that adopted
        /// server progression: the pool thread parked waiting for a dispatcher that was itself
        /// parked inside <c>Wait</c>, the full two-second timeout burned on every such exit, and
        /// the sync's <c>finally</c> — including <c>_syncGate.Release()</c> — never ran before
        /// teardown.
        ///
        /// Marshalling with <c>BeginInvoke</c> keeps the event contract intact (subscribers still
        /// run on the UI thread and may touch UI) while making the raise fire-and-forget, so the
        /// sync can complete and unwind. Same shape as <see cref="NudgeSeasonRecap"/>: no
        /// dispatcher, or one that has already begun shutting down, means there is no UI left to
        /// repaint, so the notification is dropped rather than queued into a dead queue.
        /// </summary>
        private void RaiseProfileLoadedSafely(string source)
        {
            try
            {
                var handler = ProfileLoaded;
                if (handler == null) return;

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted)
                {
                    App.Logger?.Debug("{Source}: ProfileLoaded not raised — no live dispatcher", source);
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        handler(this, EventArgs.Empty);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "{Source}: ProfileLoaded subscriber threw", source);
                    }
                }));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "{Source}: ProfileLoaded could not be dispatched", source);
            }
        }

        public ProfileSyncService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("X-Client-Version", UpdateService.AppVersion);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}");
        }

        #region Heartbeat

        /// <summary>
        /// Start the heartbeat timer to keep user showing as online.
        /// Call this after successful Patreon authentication.
        /// </summary>
        public void StartHeartbeat()
        {
            if (_heartbeatTimer != null) return;

            _heartbeatTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(HeartbeatIntervalSeconds)
            };
            _heartbeatTimer.Tick += async (s, e) => await SendHeartbeatAsync();
            _heartbeatTimer.Start();

            // Send initial heartbeat immediately
            _ = SendHeartbeatAsync();

            App.Logger?.Information("Heartbeat started (every {Seconds}s)", HeartbeatIntervalSeconds);
        }

        /// <summary>
        /// Stop the heartbeat timer.
        /// Call this on logout or app shutdown.
        /// </summary>
        public void StopHeartbeat()
        {
            _heartbeatTimer?.Stop();
            _heartbeatTimer = null;
            App.Logger?.Debug("Heartbeat stopped");
        }

        /// <summary>
        /// Forget that this session already completed a profile round-trip. Call on logout.
        /// Logout zeroes local progression (ClearProgressionData), so without this the
        /// defaults-push guard in SyncProfileAsync stays disarmed for the rest of the app
        /// session and the first sync after a re-login can PUSH the zeroed streak/XP before
        /// the READ lands — the streak then flashes 0 until a later sync repaints it.
        /// </summary>
        public void ResetLoadedProfileState()
        {
            _hasLoadedProfile = false;
            App.Logger?.Debug("Profile sync: loaded-profile flag reset (logout) - defaults guard re-armed");
        }

        /// <summary>
        /// Send a lightweight heartbeat to keep user showing as online.
        /// Only updates last_seen timestamp, doesn't sync full profile.
        /// </summary>
        private async Task SendHeartbeatAsync()
        {
            if (_disposed) return;

            // Skip if offline mode is enabled
            if (App.Settings?.Current?.OfflineMode == true) return;

            if (!IsSyncEnabled) return;

            try
            {
                // V2 heartbeat — uses auth token, NOT OAuth
                var unifiedId = App.Settings?.Current?.UnifiedId;
                if (!string.IsNullOrEmpty(unifiedId))
                {
                    var v2Request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/user/heartbeat");
                    AddAuthHeader(v2Request);
                    v2Request.Content = new StringContent(
                        JsonConvert.SerializeObject(new
                        {
                            unified_id = unifiedId,
                            is_active = App.ActivityTracker?.IsIdle != true,
                            in_session = App.IsSessionRunning,
                            app_version = UpdateService.AppVersion
                        }),
                        Encoding.UTF8, "application/json");

                    var v2Response = await _httpClient.SendAsync(v2Request);
                    var recovered = await HandleUnauthorizedAsync(v2Response);
                    if (v2Response.StatusCode == HttpStatusCode.Unauthorized && !recovered)
                    {
                        // Recovery failed or is on cooldown. This used to be gated on
                        // IsNullOrEmpty(AuthToken) as well, which made the whole block dead code:
                        // HandleUnauthorizedAsync deliberately KEEPS the token on failure (see its
                        // "Don't clear the auth token" comment), so the inner test could never pass
                        // and the heartbeat carried on 401ing every 60s forever.
                        //
                        // The token still being present is not evidence that a further tick can
                        // succeed — the server just rejected that exact token. Stop. Recovery is
                        // not lost: any other endpoint that later recovers the session calls
                        // StartHeartbeat() from inside HandleUnauthorizedAsync, and a sign-in
                        // restarts it too.
                        App.Logger?.Warning("[Auth] Heartbeat: auth recovery failed or on cooldown, stopping heartbeat");
                        StopHeartbeat();
                    }
                    App.Logger?.Debug("V2 Heartbeat: {Status}", v2Response.StatusCode);
                    return;
                }

                // Legacy heartbeat — requires OAuth
                var accessToken = GetAccessToken();
                if (string.IsNullOrEmpty(accessToken)) return;

                var endpoint = IsPatreonAuth ? "/user/heartbeat" : "/user/heartbeat-discord";
                var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}{endpoint}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    App.Logger?.Debug("Heartbeat sent successfully");
                }
                else
                {
                    App.Logger?.Debug("Heartbeat failed: {Status}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                // Silently fail - heartbeat is not critical
                App.Logger?.Debug("Heartbeat error: {Error}", ex.Message);
            }
        }

        #endregion

        /// <summary>
        /// Load profile from cloud and merge with local data.
        /// Called on startup after Patreon authentication.
        /// </summary>
        public async Task<bool> LoadProfileAsync()
        {
            // Skip if offline mode is enabled
            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("Profile sync skipped - offline mode enabled");
                return false;
            }

            if (!IsSyncEnabled)
            {
                App.Logger?.Debug("Profile sync skipped - not authenticated");
                return false;
            }

            try
            {
                // V2-first: if user has a V2 identity, try V2 sync regardless of OAuth state
                var unifiedId = App.Settings?.Current?.UnifiedId;
                if (!string.IsNullOrEmpty(unifiedId))
                {
                    // READ BEFORE WRITE. SyncProfileAsync is a POST: it uploads local state and
                    // only then reads the merged result back out of the response. That made LOAD a
                    // write — the first thing a machine did on launch was tell the server what it
                    // thought was true, which is precisely the wrong order when the local file is
                    // the thing that might be wrong (#865). Fetch the server's own copy first and
                    // adopt it (take-higher, so this can never LOWER a legitimate local), then push.
                    if (!await ReadServerProfileBeforePushAsync(unifiedId!) &&
                        ShouldSkipPushAfterFailedRead(App.Settings?.Current))
                    {
                        App.Logger?.Warning("Load blocked — the server profile could not be read AND this local profile looks emptied with no watermark to defend the push. Not asserting it upward; retrying on the next sync.");
                        return false;
                    }

                    App.Logger?.Information("V2 user — pushing local state after the server read");
                    var v2Success = await SyncProfileAsync();
                    if (v2Success)
                    {
                        _hasLoadedProfile = true;
                        RaiseProfileLoadedSafely("V2 profile load");
                        return true;
                    }

                    // V2 sync returned false. The most common benign cause is the
                    // defaults guard inside SyncProfileAsync: when local progress looks
                    // like fresh defaults (Level 1, <100 XP) it refuses to PUSH so a
                    // settings reset can't zero the server — but that also skips the
                    // only authoritative READ a V2 user gets (the sync response),
                    // leaving them stuck at Level 1 until they grind 100 XP to release
                    // the guard (#293). Heal with a READ-ONLY profile fetch (no upload,
                    // so it cannot clobber the server) + take-higher apply. The V1
                    // fallback below can't cover this: V2-native users have no record
                    // in the V1 store, so it returns empty defaults and no-ops.
                    if (await TryHealDefaultsFromServerAsync(unifiedId!))
                    {
                        _hasLoadedProfile = true;
                        RaiseProfileLoadedSafely("V2 defaults heal");
                        return true;
                    }

                    // V2 failed — fall through to V1 if OAuth is available
                    App.Logger?.Warning("V2 sync failed, attempting V1 fallback");
                }

                var accessToken = GetAccessToken();
                if (string.IsNullOrEmpty(accessToken))
                {
                    if (!string.IsNullOrEmpty(unifiedId))
                        App.Logger?.Warning("V2 sync failed and no OAuth token available — sync unavailable");
                    else
                        App.Logger?.Warning("No access token available for profile sync");
                    return false;
                }

                // V1 fallback — use appropriate endpoint based on auth type
                var endpoint = IsPatreonAuth ? "/user/profile" : "/user/profile-discord";
                var request = new HttpRequestMessage(HttpMethod.Get, $"{ProxyBaseUrl}{endpoint}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    App.Logger?.Warning("Profile load failed: {Status} - {Error}", response.StatusCode, error);
                    LastSyncError = $"Load failed: {response.StatusCode}";
                    return false;
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<ProfileResponse>(json);

                if (result == null)
                {
                    App.Logger?.Warning("Profile load returned null");
                    return false;
                }

                if (!result.Exists || result.Profile == null)
                {
                    // Cloud profile doesn't exist - check if we have local progress to sync UP
                    var settings = App.Settings?.Current;
                    var localLevel = settings?.PlayerLevel ?? 1;
                    var localXp = settings?.PlayerXP ?? 0;

                    if (localLevel > 1 || localXp > 100)
                    {
                        // We have local progress but no cloud profile - sync UP immediately
                        // This handles cases where cloud profile was deleted/corrupted
                        App.Logger?.Warning("No cloud profile found but local has progress (Level {Level}, {XP} XP) - syncing UP to create cloud profile",
                            localLevel, (int)localXp);

                        // Trigger sync UP to create the cloud profile with local data
                        _ = Task.Run(async () =>
                        {
                            try { await Task.Delay(500); await SyncProfileAsync(); }
                            catch (Exception ex) { App.Logger?.Error(ex, "Background sync-up failed"); }
                        });
                    }
                    else
                    {
                        App.Logger?.Information("No cloud profile found for user {UserId} (new user)", result.UserId);
                    }

                    return true; // Not an error, just no profile yet
                }

                // Merge cloud profile with local
                MergeCloudProfile(result.Profile);

                LastSyncTime = DateTime.Now;
                LastSyncError = null;

                App.Logger?.Information("Loaded cloud profile: Level {Level}, {Xp} XP, {Achievements} achievements, {SkillPoints} skill points, {UnlockedSkills} skills",
                    result.Profile.Level, result.Profile.Xp, result.Profile.Achievements?.Count ?? 0,
                    result.Profile.SkillPoints ?? 0, result.Profile.UnlockedSkills?.Count ?? 0);

                _hasLoadedProfile = true;

                // Notify listeners (MainWindow) to refresh UI
                RaiseProfileLoadedSafely("V1 profile load");

                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to load cloud profile");
                LastSyncError = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Ask the UI to present the season recap if it is due. No-ops harmlessly when MainWindow
        /// is not up yet (early boot) — the startup call in MainWindow covers that case, and by
        /// then the season key we just adopted is already saved.
        /// </summary>
        private static void NudgeSeasonRecap()
        {
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        (System.Windows.Application.Current?.MainWindow as ConditioningControlPanel.MainWindow)?.TryPresentSeasonRecap();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Season recap nudge failed");
                    }
                }));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Season recap nudge could not be dispatched");
            }
        }

        /// <summary>
        /// When local progression looks like fresh defaults at boot (Level 1, &lt;100 XP),
        /// the push-guard in <see cref="SyncProfileAsync"/> skips the round-trip, so a
        /// settings reset / restore never pulls the real level back down (#293). This does
        /// a READ-ONLY V2 profile fetch (GET, no upload — cannot clobber the server) and
        /// adopts it via the take-higher <see cref="V2AuthService.ApplyUserDataToSettings"/>.
        /// Returns true only if the server had real progress that was adopted. Safe to call
        /// for any failure of the V2 sync — it no-ops when local already has progress or the
        /// server record is itself empty.
        /// </summary>
        /// <summary>
        /// READ-BEFORE-WRITE for the V2 load path (#865).
        ///
        /// <see cref="LoadProfileAsync"/> used to "load" a V2 profile by calling
        /// <see cref="SyncProfileAsync"/>, which is a POST: local state went UP first and the
        /// server's answer was only read out of the response afterwards. So the opening move of
        /// every launch was an assertion, made by the party least entitled to make it — a settings
        /// file that a crashed update, a half-restored backup or a corrupt write may have emptied.
        /// The server's take-higher merge absorbs most of that, but "most" is not a guarantee, and
        /// several down-merge paths on this side then reconcile TOWARD whatever came back.
        ///
        /// This does the GET first (<see cref="V2AuthService.GetUserProfileAsync"/>, no body, no
        /// upload — it cannot change anything server-side) and adopts the result take-higher, so it
        /// can raise a stale local profile but never lower a legitimate one. The push that follows
        /// then starts from reconciled state.
        ///
        /// The adopt is written out here rather than reusing
        /// <see cref="V2AuthService.ApplyUserDataToSettings"/>, which is a LOGIN-path routine and
        /// unconditionally rewrites UnifiedId, UserDisplayName, IsSeason0Og, CurrentSeason,
        /// HighestLevelEver, HasLinkedDiscord, HasLinkedPatreon and PatreonTier — and extends the
        /// cached-premium window. Correct exactly once, at sign-in, against the login response.
        /// Wrong on a path that runs on EVERY launch against a different, narrower projection:
        /// - <c>display_name</c> is privacy-filtered by the server (nulled when it still equals the
        ///   Patreon patron name), so this would blank UserDisplayName every launch for those users;
        /// - the int fields on the DTO are non-nullable, so anything a future projection stops
        ///   sending silently deserializes to 0 — HighestLevelEver=0 would manufacture a fresh #879
        ///   and a missing unified_id would sign the user out;
        /// - none of it is progression, which is the only thing this path has any business
        ///   reconciling before a push.
        ///
        /// So: level/XP take-higher and the season key, nothing else. Identity, link state and tier
        /// stay whatever sign-in and the sync response made them. (Verified against ccp-server
        /// <c>proxy/server.js</c> GET /v2/user/profile: it does currently carry unified_id,
        /// display_name, level, xp, current_season, highest_level_ever, is_season0_og, patreon_tier
        /// and raw discord_id/patreon_id — but "currently carries" is not a contract this path
        /// should be betting a user's identity on once per launch.)
        ///
        /// Deliberately NOT fatal in general: a failed read logs and returns false, and the caller
        /// pushes anyway rather than stranding a flaky connection at zero cloud contact. The one
        /// exception is the highest-risk shape — see the caller in <see cref="LoadProfileAsync"/>
        /// and <see cref="ShouldSkipPushAfterFailedRead"/>.
        /// </summary>
        /// <summary>
        /// The read-before-write GET failed. Should we abort the push too, or push anyway? (#865, B-5)
        ///
        /// Push-anyway is the general policy and stays that way: a user on a flaky connection must
        /// not be locked out of cloud sync entirely, and the send-guard already refuses any push
        /// below the last agreed total. But that guard needs an ARMED watermark, and there is one
        /// launch where it has none — the first launch after upgrading into this code. That is also
        /// the single highest-risk launch: read failed, so we know nothing about the server, and if
        /// the local file is the emptied one, pushing it is exactly the #865 overwrite the
        /// read-before-write was added to prevent.
        ///
        /// So the abort is narrowed to that intersection: no watermark in scope AND local looks
        /// like defaults. It matches <see cref="ReconcileRestoredProfileAsync"/>'s policy for the
        /// same reason — an unverifiable local profile is not something to assert upward — and it
        /// costs nothing, because a profile at Level 1 with under 100 XP has nothing to sync that
        /// the next successful round-trip will not carry.
        ///
        /// Note this is a strictly wider net than the existing <c>!_hasLoadedProfile</c> defaults
        /// guard inside <see cref="SyncProfileAsync"/>: that one disarms itself for the rest of the
        /// session after any successful round-trip, whereas an emptied file with a failed read is
        /// dangerous on every attempt.
        /// </summary>
        private static bool ShouldSkipPushAfterFailedRead(Models.AppSettings? settings)
        {
            if (settings == null) return false;
            if (ActiveXpWatermark(settings) > 0) return false;   // the send-guard can defend the push

            var localTotalXp = App.Progression?.GetTotalXP(settings.PlayerLevel, settings.PlayerXP) ?? settings.PlayerXP;
            return settings.PlayerLevel <= 1 && localTotalXp < MeaningfulProgressXp;
        }

        private async Task<bool> ReadServerProfileBeforePushAsync(string unifiedId)
        {
            var settings = App.Settings?.Current;
            if (settings == null || string.IsNullOrEmpty(unifiedId)) return false;

            try
            {
                var v2Auth = new V2AuthService();
                var user = await v2Auth.GetUserProfileAsync(unifiedId);
                if (user == null)
                {
                    App.Logger?.Warning("Read-before-write: server profile could not be read for {Id} — pushing local state unreconciled (the XP watermark still guards it).", unifiedId);
                    return false;
                }

                var preLevel = settings.PlayerLevel;
                var preLevelXp = settings.PlayerXP;
                var localTotalXp = App.Progression?.GetTotalXP(preLevel, preLevelXp) ?? preLevelXp;

                // The season key is the one non-progression field this path DOES touch, and only
                // forward: a server answering with a stale key would otherwise walk the season
                // backwards and re-arm the recap every launch.
                var keepSeason = settings.CurrentSeason;
                var seasonAdvances = Services.SeasonRecapService.ShouldAdoptServerSeason(user.CurrentSeason, keepSeason);

                // Take-higher on level/XP, and nothing else — see the summary above for why this
                // no longer routes through V2AuthService.ApplyUserDataToSettings.
                var serverTotalXp = (double)user.Xp;
                if (user.Level > 0 && serverTotalXp >= localTotalXp)
                {
                    settings.PlayerLevel = user.Level;
                    settings.PlayerXP = App.Progression?.GetCurrentLevelXP(user.Level, serverTotalXp) ?? 0;
                }

                if (seasonAdvances)
                {
                    App.Logger?.Information("Read-before-write: season key advanced {Old} -> {New}",
                        string.IsNullOrEmpty(keepSeason) ? "(none)" : keepSeason, user.CurrentSeason);
                    settings.CurrentSeason = user.CurrentSeason;
                    ClearXpWatermark(settings, "season rollover (read-before-write)");
                    NudgeSeasonRecap();
                }

                // B-7: only record the watermark when the server's season is the one it will later
                // be CHECKED under. When the server answers with an OLDER key we keep ours (above),
                // so filing the server's total against our key would scope a foreign season's total
                // to this one — and the send-guard would then measure this season's XP against last
                // season's total.
                var scopedSeason = settings.CurrentSeason ?? string.Empty;
                if (string.Equals(user.CurrentSeason ?? string.Empty, scopedSeason, StringComparison.Ordinal))
                {
                    var clientTotalXp = App.Progression?.GetTotalXP(settings.PlayerLevel, settings.PlayerXP) ?? settings.PlayerXP;
                    RecordAgreedServerXp(settings, serverTotalXp, clientTotalXp, "read-before-write");
                }
                else
                {
                    App.Logger?.Debug("Read-before-write: server season {SS} is not the scope we sync under ({LS}) — not recording a watermark",
                        string.IsNullOrEmpty(user.CurrentSeason) ? "(none)" : user.CurrentSeason,
                        string.IsNullOrEmpty(scopedSeason) ? "(none)" : scopedSeason);
                }

                App.Settings?.Save();

                var adopted = settings.PlayerLevel != preLevel || Math.Abs(settings.PlayerXP - preLevelXp) > 0.01;
                if (adopted)
                {
                    App.Logger?.Information("Read-before-write: adopted the server profile BEFORE pushing — Level {LL} ({LX} XP) -> Level {SL} ({SX} XP).",
                        preLevel, (int)localTotalXp, settings.PlayerLevel, user.Xp);
                    // The header is written imperatively; without this it shows the pre-adopt
                    // numbers until something else happens to repaint it (#879).
                    RaiseProfileLoadedIfProgressionChanged(settings, preLevel, preLevelXp, "read-before-write");
                }
                else
                {
                    App.Logger?.Debug("Read-before-write: server profile (Level {SL}, {SX} XP) is not ahead of local (Level {LL}, {LX} XP) — nothing to adopt.",
                        user.Level, user.Xp, preLevel, (int)localTotalXp);
                }

                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Read-before-write: server profile read failed — pushing local state unreconciled");
                return false;
            }
        }

        private async Task<bool> TryHealDefaultsFromServerAsync(string unifiedId)
        {
            var settings = App.Settings?.Current;
            if (settings == null || string.IsNullOrEmpty(unifiedId)) return false;

            // Only heal genuine-looking defaults; a real local profile needs no help.
            var localTotalXp = App.Progression?.GetTotalXP(settings.PlayerLevel, settings.PlayerXP) ?? settings.PlayerXP;
            if (settings.PlayerLevel > 1 || localTotalXp >= 100) return false;

            try
            {
                var v2Auth = new V2AuthService();
                var user = await v2Auth.GetUserProfileAsync(unifiedId);
                if (user == null) return false;

                // Adopt the season key here, ahead of every early return below. This fetch already
                // carries current_season (the profile projection always included it), but the
                // "server is also at defaults" return further down threw the whole payload away —
                // and a season reset is PRECISELY when local and server both sit at Level 1 / 0 XP,
                // so the one path that could have healed a stale key bailed out exactly when it
                // mattered. Doing it before ApplyUserDataToSettings also means it lands even when
                // there is no level/XP progress to adopt.
                if (Services.SeasonRecapService.ShouldAdoptServerSeason(user.CurrentSeason, settings.CurrentSeason))
                {
                    App.Logger?.Information("Boot heal: season key advanced {Old} -> {New} via read-only profile fetch",
                        string.IsNullOrEmpty(settings.CurrentSeason) ? "(none)" : settings.CurrentSeason, user.CurrentSeason);
                    settings.CurrentSeason = user.CurrentSeason;
                    App.Settings?.Save();
                    NudgeSeasonRecap();
                }

                // Apply server-side whitelist even when level/xp are at season-reset
                // defaults. The flag normally rides the sync POST response, but the
                // defaults-guard blocks that POST for season-reset users — leaving
                // whitelisted Discord-only users with no access. This read-only path is
                // the safety net. Sticky-true: only ever promote (mirrors the server).
                if (user.PatreonIsWhitelisted)
                {
                    if (settings != null)
                    {
                        settings.PatreonPremiumValidUntil = DateTime.UtcNow.AddHours(25);
                    }
                    App.Patreon?.SetWhitelistStatus(true);
                    App.Logger?.Information("Boot heal (#293): whitelisted user — premium access applied via read-only fetch");
                }

                // Server record is itself at defaults (Level 1 / 0 XP) — no progress to
                // adopt, but the record EXISTS. Mark the profile loaded (return true) so
                // the boot defaults-guard releases on the next sync; otherwise a
                // season-reset (or brand-new) user whose local also looks like defaults
                // can never push and stays stuck at Level 1 — and never acknowledges
                // pending server flags like force_skills_reset (#293).
                if (user.Level <= 1 && user.Xp <= 0)
                {
                    App.Settings?.Save();
                    return true;
                }

                App.Logger?.Warning("Boot heal (#293): local looked like defaults (Level {LL}, {LX} XP) but server has Level {SL}, {SX} XP — adopting server profile via read-only fetch (no upload).",
                    settings.PlayerLevel, (int)localTotalXp, user.Level, user.Xp);

                v2Auth.ApplyUserDataToSettings(user); // take-higher; cannot lower a legit local
                App.Settings?.Save();
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Boot heal (#293): read-only profile fetch failed");
                return false;
            }
        }

        /// <summary>
        /// One-shot guard for settings recovered from a rolling daily backup
        /// (<see cref="SettingsService.RestoredFromBackup"/>). Such a backup restores progression
        /// WHOLESALE and can be up to three calendar days old, so it may carry a previous season's
        /// level, XP and skill tree. Uploading that state makes the rollback permanent — the
        /// server takes the higher level, and every down-merge here is max/union, so it can never
        /// self-correct (#761). This does a READ-ONLY V2 profile fetch (GET, no upload) first and:
        ///
        /// - server season is NEWER than the restored one while local still sits above it: the
        ///   restore reverted across a rollover, and <c>level_reset</c> is one-shot server-side
        ///   (the pre-corruption client already consumed it), so nothing else will ever apply it —
        ///   adopt the server's post-rollover level/XP and drop the mechanical skills the backup
        ///   resurrected, exactly like the <c>level_reset</c> branch in <see cref="SyncProfileAsync"/>;
        /// - server is simply ahead: adopt it via the take-higher apply;
        /// - otherwise keep local (the server is genuinely behind) and let the push proceed.
        ///
        /// Returns false ONLY when the server profile could not be read — the caller then skips
        /// the whole sync rather than risk uploading stale progression, and retries next tick.
        /// </summary>
        private async Task<bool> ReconcileRestoredProfileAsync(string unifiedId)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return false;

            try
            {
                var v2Auth = new V2AuthService();
                var user = await v2Auth.GetUserProfileAsync(unifiedId);
                if (user == null)
                {
                    App.Logger?.Warning("Restore reconcile: read-only profile fetch returned nothing for {Id}", unifiedId);
                    return false;
                }

                var localTotalXp = App.Progression?.GetTotalXP(settings.PlayerLevel, settings.PlayerXP) ?? settings.PlayerXP;
                var serverTotalXp = (double)user.Xp;

                // Compare against the key the BACKUP carried, not the live one: a login or the
                // #293 boot heal can have advanced settings.CurrentSeason since the restore, which
                // would hide the rollover the restored level/XP still belong to.
                var restoredSeason = App.Settings?.RestoredSeason ?? settings.CurrentSeason;
                var seasonAdvanced = Services.SeasonRecapService.ShouldAdoptServerSeason(user.CurrentSeason, restoredSeason);

                if (seasonAdvanced && localTotalXp > serverTotalXp)
                {
                    App.Logger?.Warning("Restore reconcile: restored settings belong to season {Old} but the server is on {New} — the backup reverted a rollover. Adopting the server profile: Level {LL} ({LX} XP) -> Level {SL} ({SX} XP).",
                        string.IsNullOrEmpty(restoredSeason) ? "(none)" : restoredSeason,
                        user.CurrentSeason, settings.PlayerLevel, (int)localTotalXp, user.Level, user.Xp);

                    settings.PlayerLevel = user.Level;
                    settings.PlayerXP = App.Progression?.GetCurrentLevelXP(user.Level, serverTotalXp) ?? 0;
                    settings.HighestLevelEver = user.HighestLevelEver;
                    settings.CurrentSeason = user.CurrentSeason;

                    // Same policy as the level_reset branch: the POINT BALANCE is never reset,
                    // only the tree — keep permanent nodes, drop the mechanical ones the backup
                    // brought back. (The profile projection carries no unlocked_skills list, so
                    // there is nothing to union in here; the next sync response re-adds anything
                    // the server still holds.)
                    settings.UnlockedSkills = (settings.UnlockedSkills ?? new List<string>())
                        .Where(id => Models.SkillDefinition.PermanentIds.Contains(id)).ToList();
                    App.SkillTree?.OnSeasonReset();

                    settings.SeasonResetPending = true;
                    NudgeSeasonRecap();
                }
                else if (serverTotalXp > localTotalXp)
                {
                    App.Logger?.Warning("Restore reconcile: server is ahead of the restored backup (Level {SL}, {SX} XP vs local Level {LL}, {LX} XP) — adopting the server profile.",
                        user.Level, user.Xp, settings.PlayerLevel, (int)localTotalXp);
                    v2Auth.ApplyUserDataToSettings(user); // take-higher; saves internally
                }
                else
                {
                    App.Logger?.Information("Restore reconcile: server (Level {SL}, {SX} XP, season {SS}) is not ahead of the restored local profile (Level {LL}, {LX} XP, season {LS}) — keeping local.",
                        user.Level, user.Xp, user.CurrentSeason, settings.PlayerLevel, (int)localTotalXp, settings.CurrentSeason);
                }

                // Flush before dropping the guard. The branches above save through the 500ms
                // debounce, so a crash inside that window would leave the PRE-reconcile level on
                // disk with the marker already gone — and the next run would push it for real.
                App.Settings?.SaveImmediate();
                App.Settings?.ClearRestoredFromBackupFlag();
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Restore reconcile failed — leaving the guard armed and skipping this sync");
                return false;
            }
        }

        /// <summary>
        /// Sync local progression to cloud.
        /// Called after sessions and periodically.
        /// </summary>
        private static readonly TimeSpan SyncCooldown = TimeSpan.FromSeconds(30);

        public async Task<bool> SyncProfileAsync()
        {
            // Skip if offline mode is enabled
            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("Profile sync skipped - offline mode enabled");
                return false;
            }

            if (!IsSyncEnabled)
            {
                App.Logger?.Debug("Profile sync skipped - not authenticated");
                return false;
            }

            // Prevent concurrent sync calls from racing past the cooldown check
            if (!await _syncGate.WaitAsync(0))
            {
                App.Logger?.Debug("Profile sync skipped - another sync in progress");
                return false;
            }

            var syncSucceeded = false;

            // Progression as it stood before this call could rewrite it, plus a label for the log
            // line. Declared out here so the finally can compare against it on EVERY exit path —
            // see the raise at the bottom of this method. Null until the snapshot is actually
            // taken, which is the signal that nothing in this call could have adopted anything yet.
            int? preSyncLevel = null;
            double preSyncLevelXp = 0;
            var raiseSource = "sync";

            try
            {
            // Client-side sync cooldown to match server-side enforcement
            if (LastSyncTime.HasValue && DateTime.Now - LastSyncTime.Value < SyncCooldown)
            {
                App.Logger?.Debug("Profile sync skipped - cooldown active ({Remaining}s remaining)",
                    Math.Ceiling((SyncCooldown - (DateTime.Now - LastSyncTime.Value)).TotalSeconds));
                return false;
            }

            try
            {
                var accessToken = GetAccessToken();
                if (string.IsNullOrEmpty(accessToken))
                {
                    // For V2 users (invite-code or expired OAuth): allow sync if we have unified_id + auth token
                    var fallbackUnifiedId = App.Settings?.Current?.UnifiedId;
                    if (!string.IsNullOrEmpty(fallbackUnifiedId) && !string.IsNullOrEmpty(App.Settings?.Current?.AuthToken))
                    {
                        App.Logger?.Debug("No OAuth token — proceeding with V2 sync for unified user {Id}", fallbackUnifiedId);
                    }
                    else
                    {
                        App.Logger?.Warning("No access token available for profile sync");
                        return false;
                    }
                }

                // Gather local progression data from Settings
                var settings = App.Settings?.Current;
                var achievements = App.Achievements;

                if (settings == null)
                {
                    App.Logger?.Warning("Settings not available for profile sync");
                    return false;
                }

                // Snapshot the displayed progression BEFORE anything in this method can rewrite it
                // (the restore reconcile below, the level_reset handler, the server-ahead adopt and
                // the anti-cheat clamp all do). Compared again at the end to decide whether the
                // header needs repainting — see RaiseProfileLoadedIfProgressionChanged (#879).
                preSyncLevel = settings.PlayerLevel;
                preSyncLevelXp = settings.PlayerXP;

                // This session's settings came out of a rolling daily backup, so the level/XP/skills
                // below may be up to three days stale and can predate a season rollover. Reconcile
                // against the server BEFORE the push: the sync POST is the first server contact of
                // a run, and once a stale-but-higher profile is up there every down-merge rule is
                // max/union, so nothing can ever bring it back down again (#761).
                if (App.Settings?.RestoredFromBackup == true)
                {
                    var restoredUnifiedId = settings.UnifiedId;
                    if (!string.IsNullOrEmpty(restoredUnifiedId) &&
                        !await ReconcileRestoredProfileAsync(restoredUnifiedId!))
                    {
                        App.Logger?.Warning("Sync blocked — settings were restored from a local backup and the server profile could not be read to reconcile them. Retrying on the next sync.");
                        return false;
                    }
                }

                // Get achievement stats for additional tracking
                var achievementProgress = achievements?.Progress;

                // Calculate total accumulated XP (sum of all levels + current progress).
                // Read AFTER the restore reconcile above, which can rewrite level/XP.
                var totalXp = App.Progression?.GetTotalXP(settings.PlayerLevel, settings.PlayerXP) ?? settings.PlayerXP;

                // Guard: if local data looks like fresh defaults (Level 1, near-zero XP) and we
                // haven't completed a round-trip load yet this session, skip sending XP/level.
                // This prevents a settings reset (update crash, corruption) from zeroing the server.
                // THE ONE SANCTIONED DOWNWARD WRITE (CONTRACTS-0812 §2.5). A migration choice the
                // user has made but the server has not acked yet suspends BOTH XP-regression
                // guards below for this sync — and only for this sync, because the flag is
                // cleared by the ack. Without it the two guards do exactly what they were built
                // to do and refuse the ceremony: "Descend again" pushes Level 1 / 0 XP, which is
                // the defaults-guard's whole signature, and both choices push a total under the
                // watermark this client and the server last agreed on.
                //
                // Suspending them is safe here precisely because this is not a local calculation
                // that went wrong: the figure was derived from the SERVER's own total_xp_earned
                // in response to the SERVER's own offer, and the server re-derives and clamps it
                // on arrival. Nothing else in the app can set this flag.
                var pendingMigrationChoice = settings.PendingDescentMigrationChoice;
                var migrationSubmitInFlight = DescentMigrationChoices.IsValid(pendingMigrationChoice);
                if (migrationSubmitInFlight)
                {
                    App.Logger?.Information("[Descent] Migration submit riding this sync (choice={Choice}, Level {Level}, XP {Xp}) — XP regression guards suspended for it.",
                        pendingMigrationChoice, settings.PlayerLevel, (int)totalXp);
                }

                if (!migrationSubmitInFlight && !_hasLoadedProfile && settings.PlayerLevel <= 1 && totalXp < MeaningfulProgressXp)
                {
                    App.Logger?.Warning("Sync blocked — local looks like defaults (Level {Level}, XP {Xp}) and profile not yet loaded. Waiting for LoadProfileAsync.",
                        settings.PlayerLevel, (int)totalXp);
                    return false;
                }

                // XP regression guard (#865). The defaults-guard above only covers the case where
                // local looks like a FRESH install and we have not loaded yet. It says nothing
                // about a local file that lost half its progress, or about the second sync of a
                // session after a bad adopt — both of which would happily push a lower number and
                // ask the server to agree. The watermark is the last total the two sides AGREED
                // on, so a payload below it cannot be right: refuse to send and say so loudly
                // rather than talking the server down to a local mistake.
                //
                // "Last agreed", not "highest ever seen", is what makes this survivable. Every
                // legitimate way for the number to fall moves the watermark with it: a season
                // rollover and an admin level_reset clear it outright, an account switch puts it
                // out of scope, and any adopt of a lower server figure re-records at that figure
                // (RecordAgreedServerXp). So the guard can only ever block the one case it is for
                // — a local total that fell with no server-side explanation — and it cannot latch,
                // which matters because this check sits in front of the whole payload, not just
                // the XP field.
                var watermark = migrationSubmitInFlight ? 0 : ActiveXpWatermark(settings);
                if (watermark > 0 && totalXp < watermark)
                {
                    App.Logger?.Error("[XP watermark] Sync REFUSED — would push {Xp} XP, below the {Watermark} XP this client and the server last agreed on for this account this season (Level {Level}). This local profile has LOST progress; not asking the server to match it. Fix the local file or reset the account deliberately.",
                        (int)totalXp, (int)watermark, settings.PlayerLevel);
                    LastSyncError = "Sync refused: local XP is below the last server-agreed total";
                    return false;
                }

                App.Logger?.Information("Syncing profile - Level: {Level}, TotalXP: {Xp}, VideoMinutes: {VideoMin:F1}, LockCards: {LockCards}",
                    settings.PlayerLevel,
                    (int)totalXp,
                    achievementProgress?.TotalVideoMinutes ?? 0,
                    achievementProgress?.TotalLockCardsCompleted ?? 0);

                // Use V2 sync if user has unified_id (new v5.5 system)
                var unifiedId = App.Settings?.Current?.UnifiedId;
                if (!string.IsNullOrEmpty(unifiedId))
                {
                    raiseSource = "V2 sync";
                    var questProgress = App.Quests?.Progress;
                    var v2SyncData = new
                    {
                        unified_id = unifiedId,
                        xp = (int)totalXp,
                        level = settings.PlayerLevel,
                        achievements = achievementProgress?.UnlockedAchievements?.ToList() ?? new List<string>(),
                        stats = new Dictionary<string, object>
                        {
                            ["completed_sessions"] = achievementProgress?.CompletedSessions?.Count ?? 0,
                            ["longest_session_minutes"] = achievementProgress?.LongestSessionMinutes ?? 0,
                            ["highest_streak"] = settings.HighestStreak,
                            ["total_flashes"] = achievementProgress?.TotalFlashImages ?? 0,
                            ["consecutive_days"] = achievementProgress?.ConsecutiveDays ?? 0,
                            ["total_bubbles_popped"] = achievementProgress?.TotalBubblesPopped ?? 0,
                            ["total_video_minutes"] = Math.Round(achievementProgress?.TotalVideoMinutes ?? 0, 1),
                            ["total_lock_cards_completed"] = achievementProgress?.TotalLockCardsCompleted ?? 0,
                            // Prestige (advisory — server value is authoritative and monotonic)
                            ["lifetime_points_spent"] = achievementProgress?.LifetimeSkillPointsSpent ?? 0,
                            // Quest streak data
                            ["daily_quest_streak"] = settings.DailyQuestStreak,
                            ["last_daily_quest_date"] = settings.LastDailyQuestDate?.ToString("o") ?? "",
                            ["quest_completion_dates"] = questProgress?.DailyQuestCompletionDates?
                                .Select(d => d.ToString("yyyy-MM-dd")).ToList() ?? new List<string>(),
                            ["total_daily_quests_completed"] = questProgress?.TotalDailyQuestsCompleted ?? 0,
                            ["total_weekly_quests_completed"] = questProgress?.TotalWeeklyQuestsCompleted ?? 0,
                            ["total_xp_from_quests"] = questProgress?.TotalXPFromQuests ?? 0,
                            ["daily_quests_completed_today"] = questProgress?.GetDailyQuestsCompletedToday() ?? 0,
                            ["daily_completion_reset_date"] = questProgress?.DailyCompletionResetDate?.ToString("yyyy-MM-dd") ?? ""
                        },
                        unlocked_skills = settings.UnlockedSkills?.ToList() ?? new List<string>(),
                        skill_points = settings.SkillPoints,
                        total_conditioning_minutes = settings.TotalConditioningMinutes,
                        companion_progress = settings.CompanionProgressData,
                        allow_discord_dm = settings.AllowDiscordDm,
                        show_online_status = settings.ShowOnlineStatus,
                        share_profile_picture = settings.ShareProfilePicture,
                        // Goon Game consent flags (GOON_DISCORD_CONTRACT §2). Sharer-only;
                        // the server snapshots these into the room card at invite/join and
                        // drops the cached avatar bytes when a flag is revoked.
                        // GoonRichPresence is deliberately NOT sent — local-only.
                        goon_share_avatar = settings.GoonShareAvatar,
                        goon_share_dm = settings.GoonShareDiscordDm,
                        // Trainer Card customization (Profile redesign Phase 2). Sanitized on the
                        // way out so a hand-edited settings.json cannot push ids nothing renders,
                        // and so the server's own validation has less to reject. NULL while the
                        // local loadout is empty and unconfirmed - see BuildCosmeticsPayload, an
                        // all-empty object means "unequip everything" to the server.
                        cosmetics = BuildCosmeticsPayload(settings),
                        // Web XP claim ack: the id of the claim this client last APPLIED. Sent on
                        // every sync, not just the one after a claim - the server settles the pending
                        // bucket when it sees its own id come back, and ignores stale/unknown ones.
                        // Null until the first claim ever lands, which is a perfectly good "nothing
                        // applied yet" to the server.
                        web_xp_claim_ack = settings.LastWebXpClaimId,
                        // One Descent (PLAN.md Phase A): best on-disk evidence of this install's
                        // age, stamped once at startup (App.EnsureInstallDateRecorded). The server
                        // stores it once as legacy_install_date and silently drops anything it
                        // cannot parse, so a null here — every sync before the field was ever
                        // recorded — is a no-op, not an error. Fallback data for the Year One
                        // anchor only; nothing reads it back.
                        install_date = string.IsNullOrWhiteSpace(settings.InstallDate) ? null : settings.InstallDate,
                        // THE EPOCH ECHO (CONTRACTS-0812 §1). Unconditional, on every body, from
                        // every build that carries this line — it identifies the CLIENT, not the
                        // account, so it does not wait for a flag and it does not read settings.
                        //
                        // Server side it is the resurrection guard: once a record is migrated, a
                        // sync body arriving without this number has its xp/level/total_xp_earned
                        // ignored outright, which is the only thing standing between a migrated
                        // account and an unsynced old-curve phone pushing a pre-migration level
                        // back up through the take-higher merge. Legacy clients see no error and
                        // no wire change; their level writes just stop landing.
                        descent_epoch = DescentEpochs.ClientEpoch,
                        // Send false to clear server-side reset flags only when acknowledging
                        reset_weekly_quest = false,
                        reset_daily_quest = false,
                        force_streak_override = false,
                        force_skills_reset = settings.PendingSkillsResetAck ? (bool?)false : null
                    };

                    var v2Request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/user/sync");
                    AddAuthHeader(v2Request);
                    var v2Body = JsonConvert.SerializeObject(v2SyncData);

                    // THE CHOICE SUBMIT (CONTRACTS-0812 §2.2), grafted on rather than declared in
                    // the anonymous object above, because an anonymous property would serialize as
                    // `"descent_migration": null` on the 100% of syncs that carry no choice — and
                    // §0.4 says flag-off is BYTE-IDENTICAL wire, not "identical apart from a null".
                    // No pending choice, no re-serialization, no new bytes.
                    //
                    // The re-derived ledger rides in the ORDINARY xp/level fields (already built
                    // above from the settings the ceremony rewrote); this object carries nothing
                    // but the choice.
                    if (migrationSubmitInFlight)
                    {
                        var payload = JObject.Parse(v2Body);
                        payload["descent_migration"] = new JObject { ["choice"] = pendingMigrationChoice };
                        v2Body = payload.ToString(Formatting.None);
                    }

                    v2Request.Content = new StringContent(v2Body, Encoding.UTF8, "application/json");
                    SignRequest(v2Request, v2Body);

                    var v2Response = await _httpClient.SendAsync(v2Request);

                    if (!v2Response.IsSuccessStatusCode)
                    {
                        // On 429 (cooldown), set LastSyncTime to prevent immediate retry
                        if (v2Response.StatusCode == (System.Net.HttpStatusCode)429)
                        {
                            LastSyncTime = DateTime.Now;
                            App.Logger?.Debug("V2 Profile sync rate-limited by server, will retry later");
                            return false;
                        }
                        await HandleUnauthorizedAsync(v2Response);
                        var error = await v2Response.Content.ReadAsStringAsync();
                        App.Logger?.Warning("V2 Profile sync failed: {Status} - {Error}", v2Response.StatusCode, error);
                        LastSyncError = $"Sync failed: {v2Response.StatusCode}";
                        return false;
                    }

                    LastSyncTime = DateTime.Now;
                    LastSyncError = null;
                    // The unequip-everything intent has now reached the server; further syncs from
                    // an empty loadout go back to meaning "no change".
                    PendingCosmeticsClear = false;

                    var v2Json = await v2Response.Content.ReadAsStringAsync();
                    App.Logger?.Information("V2 Profile synced successfully: {Response}", v2Json);

                    // Check for server-side flags in V2 sync response
                    try
                    {
                        var v2Result = JsonConvert.DeserializeObject<V2SyncResponse>(v2Json);
                        if (v2Result?.ResetWeeklyQuest == true)
                        {
                            App.Logger?.Information("V2 Sync: Server requested weekly quest reset");
                            App.Quests?.ForceRegenerateWeeklyQuest();
                        }
                        if (v2Result?.ResetDailyQuest == true)
                        {
                            App.Logger?.Information("V2 Sync: Server requested daily quest reset");
                            App.Quests?.ForceRegenerateDailyQuest();
                        }

                        // Handle force_streak_override - adopt server values even if lower
                        if (v2Result?.ForceStreakOverride == true && v2Result.StreakStats != null)
                        {
                            App.Logger?.Information("V2 Sync: Force streak override - adopting server streak values");
                            ApplyForceStreakOverride(v2Result.StreakStats);
                        }

                        // Handle force_skills_reset - clear all skills and refund points
                        // Guard: only apply if we haven't already acknowledged (survives crashes)
                        if (v2Result?.ForceSkillsReset == true && !settings.PendingSkillsResetAck)
                        {
                            App.Logger?.Information("V2 Sync: Force skills reset - clearing all skills");
                            ApplyForceSkillsReset(v2Result.SkillPoints);
                            settings.PendingSkillsResetAck = true;
                            App.Settings?.Save();
                        }
                        else if (settings.PendingSkillsResetAck && v2Result?.ForceSkillsReset != true)
                        {
                            // Server flag was cleared by our acknowledgment
                            settings.PendingSkillsResetAck = false;
                            App.Settings?.Save();
                        }
                        else if (v2Result?.SkillPoints.HasValue == true)
                        {
                            // Take max of server/local — skill points only increase (level-ups, bubble
                            // pops) and are NEVER reset by seasons (policy: the balance is permanent;
                            // only mechanical skills reset), so the higher value is always correct.
                            // This also shields the balance from an older server that still zeroes
                            // skill_points at rollover.
                            var maxPoints = Math.Max(v2Result.SkillPoints.Value, settings.SkillPoints);
                            if (maxPoints != settings.SkillPoints)
                            {
                                App.Logger?.Information("V2 Sync: Skill points server={Server}, local={Local} — taking max ({Max})",
                                    v2Result.SkillPoints.Value, settings.SkillPoints, maxPoints);
                                settings.SkillPoints = maxPoints;
                                App.Settings?.Save();
                            }
                        }

                        // Merge unlocked skills from server (union — never lose skills).
                        // Skipped on level_reset: the rollover legitimately REMOVES mechanical
                        // skills; union-merging here would resurrect them (the reset handler
                        // below applies the authoritative post-rollover list instead).
                        if (v2Result?.UnlockedSkills != null && v2Result.UnlockedSkills.Count > 0 && v2Result?.LevelReset != true)
                        {
                            var localSkills = settings.UnlockedSkills ?? new List<string>();
                            var skillsToAdd = v2Result.UnlockedSkills.Except(localSkills).ToList();
                            if (skillsToAdd.Count > 0)
                            {
                                App.Logger?.Information("V2 Sync: Adding {Count} unlocked skills from server: {Skills}",
                                    skillsToAdd.Count, string.Join(", ", skillsToAdd));
                                foreach (var skill in skillsToAdd)
                                {
                                    if (!localSkills.Contains(skill))
                                        localSkills.Add(skill);
                                }
                                settings.UnlockedSkills = localSkills;
                                App.Settings?.Save();
                            }
                        }

                        // Trainer Card cosmetics: fill-if-empty only, so a fresh machine inherits
                        // the look and an established one is never undressed by a stale echo.
                        if (AdoptCloudCosmetics(v2Result?.Cosmetics)) App.Settings?.Save();

                        // WEB XP CLAIM. The server mints XP for verified web activity into a pending
                        // bucket; it never touches the ledger the client authors (xp/level). It hands
                        // that bucket over one claim at a time - {id, amount} on this response - and
                        // THIS is the only door web XP walks through into real progression, so it
                        // gets the normal level-up experience on the way in.
                        //
                        // The handshake is deliberately lopsided. We persist the id BEFORE adding the
                        // XP, and we ack that id on every sync from then on. A crash in the gap costs
                        // the player one claim; the other order would pay it twice on the next launch,
                        // and under-paying once is the failure we can live with. The server re-offers
                        // an unacked claim indefinitely, so nothing is lost by skipping a round -
                        // which is exactly why level_reset skips: a season-reset response is about to
                        // overwrite level and XP wholesale, and XP applied into that is XP thrown away.
                        //
                        // The whole block is a no-op when `web_xp` is absent (server flag off).
                        var webXpClaim = v2Result?.WebXp?.Claim;
                        if (webXpClaim != null &&
                            !string.IsNullOrEmpty(webXpClaim.Id) &&
                            webXpClaim.Amount > 0 &&
                            webXpClaim.Id != settings.LastWebXpClaimId &&
                            v2Result?.LevelReset != true)
                        {
                            App.Logger?.Information("V2 Sync: Web XP claim {ClaimId} — applying +{Amount} XP (pending {Pending}, lifetime {Total})",
                                webXpClaim.Id, webXpClaim.Amount, v2Result?.WebXp?.Pending ?? 0, v2Result?.WebXp?.Total ?? 0);

                            // Order is load-bearing — see above.
                            settings.LastWebXpClaimId = webXpClaim.Id;
                            App.Settings?.Save();

                            App.Progression?.AddClaimedXP(webXpClaim.Amount);
                        }

                        // THE MIGRATION HANDSHAKE, both halves (CONTRACTS-0812 §2). Absent block =
                        // nothing happens, which is the state of every account in the world until
                        // the owner arms DESCENT_MIGRATION server-side. There is no client flag to
                        // find and no local condition that reaches this code on its own.
                        //
                        // Ack FIRST, offer second, and the order matters: a submit's own response
                        // carries the ack, and settling it before looking at `required` means a
                        // server that (wrongly) sent both in one breath cannot re-open a ceremony
                        // the user just finished.
                        HandleDescentMigrationAck(settings, v2Result?.DescentMigration);
                        HandleDescentMigrationOffer(settings, v2Result?.DescentMigration);

                        // The stage-ceremony drip (§6). A successful sync is the cheapest honest
                        // proxy for "signed in and awake today" that needs no new lifecycle
                        // wiring, and the tick is a same-local-day no-op, so syncing forty times
                        // still releases exactly one. Returns immediately for the ~100% of
                        // accounts with an empty queue.
                        App.DescentMigration?.TickStageDrip();

                        // Prestige: adopt the server's lifetime_points_spent when ahead (other
                        // device / migration backfill). Monotonic — never lowered.
                        if (v2Result?.LifetimePointsSpent != null)
                        {
                            App.Achievements?.ReconcileLifetimePointsSpent(v2Result.LifetimePointsSpent.Value);
                        }

                        // Sync oopsie insurance season usage from server
                        if (v2Result?.OopsieUsedSeason != null)
                        {
                            // Server-authoritative season key (not wall-clock): OopsieUsedSeason is in the
                            // server's season terms, so comparing it to a local wall-clock month mis-clears
                            // the flag whenever the server season lags the calendar month (e.g. the 1st).
                            var currentSeason = SeasonRecapService.CurrentSeasonKey;
                            var oopsieUsed = v2Result.OopsieUsedSeason == currentSeason;
                            if (settings.SeasonalStreakRecoveryUsed != oopsieUsed)
                            {
                                settings.SeasonalStreakRecoveryUsed = oopsieUsed;
                                App.Settings?.Save();
                                App.Logger?.Information("V2 Sync: Oopsie insurance season sync - used={Used} (season={Season})", oopsieUsed, v2Result.OopsieUsedSeason);
                            }
                        }

                        // Sync the cumulable streak-fix charge balance. The server grants +1 per season
                        // rollover and decrements on spend, so it is authoritative in both directions.
                        // The assignment raises the settings INPC, which is what repaints the quests
                        // tab (MainWindow.OnSettingsPropertyChangedForQuests) — the tile and the
                        // "Fix Day (n)" caption are written imperatively, not bound, so without that
                        // a user parked on the tab keeps seeing the stale balance.
                        if (v2Result?.OopsieCredits != null && settings.StreakFixCharges != v2Result.OopsieCredits.Value)
                        {
                            var oldCharges = settings.StreakFixCharges;
                            settings.StreakFixCharges = v2Result.OopsieCredits.Value;
                            App.Settings?.Save();
                            App.Logger?.Information("V2 Sync: Streak fix charges synced from server: {Old} -> {New}",
                                oldCharges, v2Result.OopsieCredits.Value);
                        }

                        // Sync display name from server (server is authoritative — admin renames, etc.)
                        if (!string.IsNullOrEmpty(v2Result?.User?.DisplayName) &&
                            v2Result.User.DisplayName != settings.UserDisplayName)
                        {
                            App.Logger?.Information("V2 Sync: display name updated from server: \"{Old}\" -> \"{New}\"",
                                settings.UserDisplayName, v2Result.User.DisplayName);
                            settings.UserDisplayName = v2Result.User.DisplayName;
                            App.Settings?.Save();
                        }

                        // Sync OG status from server (server is authoritative)
                        if (v2Result?.IsSeason0Og != null && settings.IsSeason0Og != v2Result.IsSeason0Og.Value)
                        {
                            settings.IsSeason0Og = v2Result.IsSeason0Og.Value;
                            App.Settings?.Save();
                            App.Logger?.Information("V2 Sync: OG status synced from server: {IsOg}", v2Result.IsSeason0Og.Value);
                        }

                        // Sync bonus rerolls from server (admin-granted)
                        if (v2Result?.BonusDailyRerolls != null || v2Result?.BonusWeeklyRerolls != null)
                        {
                            settings.BonusDailyRerolls = v2Result.BonusDailyRerolls ?? 0;
                            settings.BonusWeeklyRerolls = v2Result.BonusWeeklyRerolls ?? 0;
                            App.Settings?.Save();
                        }

                        // Sync whitelist status from server — enables Patreon features for whitelisted users
                        // even if they never did Patreon OAuth (e.g. Discord-only users)
                        if (v2Result?.PatreonIsWhitelisted == true)
                        {
                            // Refresh the cached premium access window (25h > sync interval)
                            settings.PatreonPremiumValidUntil = DateTime.UtcNow.AddHours(25);
                            App.Settings?.Save();

                            // Set whitelist + tier on PatreonService so Lab access works
                            // even if Patreon OAuth validation failed
                            App.Patreon?.SetWhitelistStatus(true);

                            App.Logger?.Information("V2 Sync: Whitelisted user — premium access + tier 2 granted via sync");
                        }

                        // Sync highest_level_ever from server (server is authoritative)
                        if (v2Result?.User?.HighestLevelEver != null)
                        {
                            var serverHighest = v2Result.User.HighestLevelEver.Value;
                            if (serverHighest != settings.HighestLevelEver)
                            {
                                App.Logger?.Information("V2 Sync: highest_level_ever server={Server} local={Local} — using server value",
                                    serverHighest, settings.HighestLevelEver);
                                settings.HighestLevelEver = serverHighest;
                                App.Settings?.Save();
                            }
                        }

                        // Merge achievements from server (union — never lose achievements)
                        if (v2Result?.User?.Achievements != null && v2Result.User.Achievements.Count > 0)
                        {
                            var achievementSvc = App.Achievements;
                            if (achievementSvc?.Progress != null)
                            {
                                var restoredCount = 0;
                                foreach (var achievementId in v2Result.User.Achievements)
                                {
                                    if (!achievementSvc.Progress.IsUnlocked(achievementId))
                                    {
                                        achievementSvc.Progress.Unlock(achievementId);
                                        restoredCount++;
                                    }
                                }
                                if (restoredCount > 0)
                                {
                                    App.Logger?.Information("V2 Sync: Restored {Count} achievements from server", restoredCount);
                                    achievementSvc.Save();
                                }
                            }
                        }

                        // Pull lifetime stats and quest streak data down from server. The V2 path
                        // historically only synced UP - local progress (TotalBubblesPopped, TotalFlashImages,
                        // ConsecutiveDays, daily_quest_streak, completion dates, etc.) was never refreshed
                        // from cloud, so admin restores / cross-device progress stayed invisible until the
                        // V1 fallback ran. Mirror MergeCloudProfile's stats merge for V2.
                        if (v2Result?.User?.Stats != null)
                        {
                            if (MergeV2CloudStatsIntoLocalProgress(v2Result.User.Stats, v2Result.ForceStreakOverride == true))
                            {
                                // SyncCurrentStreak mutates settings.CurrentStreak/LastStreakDate without
                                // saving, so it must run BEFORE Save (matches MergeCloudProfile order).
                                App.Achievements?.Progress?.SyncCurrentStreak();
                                App.Settings?.Save();
                                App.Achievements?.Save();
                            }
                        }

                        // Merge total conditioning minutes from server (take higher)
                        if (v2Result?.TotalConditioningMinutes.HasValue == true && v2Result.TotalConditioningMinutes.Value > settings.TotalConditioningMinutes)
                        {
                            App.Logger?.Information("V2 Sync: Conditioning time server={Server:F1} > local={Local:F1} — using server value",
                                v2Result.TotalConditioningMinutes.Value, settings.TotalConditioningMinutes);
                            settings.TotalConditioningMinutes = v2Result.TotalConditioningMinutes.Value;
                            App.Settings?.Save();
                        }

                        // Merge companion progress from server (per-companion, higher level wins)
                        if (v2Result?.CompanionProgress != null && v2Result.CompanionProgress.Count > 0)
                        {
                            var needsCompanionSave = false;
                            foreach (var (key, serverProgress) in v2Result.CompanionProgress)
                            {
                                if (int.TryParse(key, out var companionId))
                                {
                                    var localData = settings.CompanionProgressData;
                                    localData.TryGetValue(companionId, out var localProgress);

                                    var localLevel = localProgress?.Level ?? 0;
                                    var serverLevel = serverProgress?.Level ?? 0;
                                    var localXP = localProgress?.TotalXPEarned ?? 0;
                                    var serverXP = serverProgress?.TotalXPEarned ?? 0;

                                    if (serverLevel > localLevel || (serverLevel == localLevel && serverXP > localXP))
                                    {
                                        App.Logger?.Information("V2 Sync: Companion {Id} server Lv.{SLv} > local Lv.{LLv} — using server",
                                            companionId, serverLevel, localLevel);
                                        localData[companionId] = serverProgress!;
                                        needsCompanionSave = true;
                                    }
                                    else if (localProgress == null && serverProgress != null)
                                    {
                                        localData[companionId] = serverProgress;
                                        needsCompanionSave = true;
                                    }
                                }
                            }
                            if (needsCompanionSave) App.Settings?.Save();
                        }

                        // Adopt the server's season key BEFORE the level_reset handler below, because
                        // that handler is what nudges the recap — and the recap's whole decision is a
                        // comparison against this key. Getting these the wrong way round is the actual
                        // August 1 bug: the rollover arrived, the recap ran, and it compared the old
                        // key with itself and concluded the season had not changed.
                        var serverSeason = v2Result?.User?.CurrentSeason;
                        if (Services.SeasonRecapService.ShouldAdoptServerSeason(serverSeason, settings.CurrentSeason))
                        {
                            App.Logger?.Information("V2 Sync: season key advanced {Old} -> {New} (server-authoritative)",
                                string.IsNullOrEmpty(settings.CurrentSeason) ? "(none)" : settings.CurrentSeason, serverSeason);
                            settings.CurrentSeason = serverSeason;

                            // A rollover is the one moment seasonal XP is SUPPOSED to fall, so the
                            // previous season's watermark stops applying here — before the
                            // level_reset handler below, which is the thing that does the falling.
                            ClearXpWatermark(settings, "season rollover");
                            App.Settings?.Save();

                            // Nudge the recap on the key change itself, not only on level_reset.
                            // level_reset is one-shot from the server, so relying on it alone means a
                            // client that misses that single response can never recap that season.
                            // Cheap to call: TryPresentSeasonRecap shows at most once per app run.
                            NudgeSeasonRecap();
                        }

                        // Handle level_reset — server admin reset all levels, force client to accept.
                        //
                        // This condition used to also call !RefuseToZeroAConfirmedProfile(...), a
                        // side-effecting check inlined into the `if`. Two things went wrong with
                        // that. First, a mid-season admin reset IS a zeroing of a confirmed profile
                        // — that is what an admin reset is — so the guard refused it, and refused
                        // it permanently, because level_reset is one-shot: the server never sends it
                        // again. Second, a refusal did not stop there; it fell through into the
                        // `else if` clamp chain below, where the same zeroed row reads as
                        // "uninitialized", local is kept, and the next push writes the pre-reset
                        // profile back over the admin's work.
                        //
                        // An explicit level_reset is the server EXPLAINING the zeroing, which is
                        // precisely what the watermark guard exists to distinguish from an
                        // unexplained one. So it is adopted unconditionally and the watermark is
                        // cleared to match (#865, B-2). No spoof guard is kept: forging this flag
                        // means forging an authenticated sync response, and anyone who can do that
                        // can set `xp` to whatever they like in the same body — the guard bought
                        // nothing and cost a permanent, silent divergence from the server.
                        //
                        // What is left in this condition is a flag test and a null check — nothing
                        // that can DECIDE anything. Any future guard belongs in an explicit
                        // if/else INSIDE the branch, where a refusal stays a refusal instead of
                        // falling through into the clamp chain below.
                        if (v2Result?.LevelReset == true && v2Result.User != null)
                        {
                            // A reset is a licensed fall: the previously agreed total no longer
                            // describes this account. Clear before adopting so the send-guard does
                            // not block the very push that carries the reset upward.
                            ClearXpWatermark(settings, "admin level_reset");

                            var serverLevel = v2Result.User.Level;
                            var serverXp = v2Result.User.Xp;
                            var serverLevelXp = App.Progression?.GetCurrentLevelXP(serverLevel, serverXp) ?? 0;

                            App.Logger?.Information("V2 Sync: Level reset by admin — forcing Level {Level}, XP {Xp}", serverLevel, serverXp);
                            settings.PlayerLevel = serverLevel;
                            settings.PlayerXP = serverLevelXp;
                            // Use server's highest_level_ever (preserved across resets for permanent unlocks)
                            settings.HighestLevelEver = v2Result.User.HighestLevelEver ?? 0;

                            // Seasonal skill reset: the POINT BALANCE is never reset (policy — points
                            // persist across seasons; the max-merge above keeps the higher value).
                            // Only the tree resets: rebuild it as server list ∪ locally-owned
                            // PERMANENT nodes. The union half protects stat/analytics purchases
                            // against an older server that still wipes unlocked_skills to [] at
                            // rollover; mechanical nodes are dropped and must be re-bought
                            // (the Prestige loop).
                            var permanentOwned = (settings.UnlockedSkills ?? new List<string>())
                                .Where(id => Models.SkillDefinition.PermanentIds.Contains(id));
                            settings.UnlockedSkills = (v2Result.UnlockedSkills ?? new List<string>())
                                .Union(permanentOwned).ToList();

                            // Clear seasonal flags and tear down effects of dropped mechanical skills
                            App.SkillTree?.OnSeasonReset();

                            // Season Recap: a level_reset IS the reset — flag the recap so it
                            // surfaces even mid-month (monthly rollover otherwise also triggers it
                            // via the month check). Then nudge the UI to present it now if MainWindow
                            // is already up (e.g. reset arrived during a running session). level_reset
                            // is one-shot from the server (subsequent syncs return false once the
                            // server advances the user's season), so this won't loop.
                            settings.SeasonResetPending = true;
                            App.Settings?.Save();

                            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                (System.Windows.Application.Current?.MainWindow as ConditioningControlPanel.MainWindow)?.TryPresentSeasonRecap();
                            }));
                        }
                        // THE CEREMONY'S LEDGER IS NOT UP FOR NEGOTIATION until the server acks it.
                        //
                        // This is the nastiest interaction in the whole migration. The adopt block
                        // below exists to pull a client UP to a server that is 5k ahead — and on
                        // the sync that submits a Cycle, the server's pre-migration record is
                        // hundreds of thousands of XP ahead of the Level 1 ledger we just wrote.
                        // If the server did NOT process the submit (flag off mid-flight, an older
                        // deploy, a partial write), the adopt would cheerfully resurrect the
                        // pre-ceremony level while the pending choice sat on disk waiting to
                        // re-submit — a client and a server disagreeing about whether a one-way
                        // ceremony happened.
                        //
                        // So: while a submit is in flight, the ONLY response allowed to move the
                        // ledger is one carrying the ack. With the ack, the server's figures ARE
                        // the post-migration truth (including the §2.5 ±1-level clamp on a
                        // restore) and adopting them is exactly right. Without it, we keep what
                        // the ceremony wrote and try again next sync.
                        else if (migrationSubmitInFlight && v2Result?.DescentMigration?.Completed != true)
                        {
                            App.Logger?.Warning("[Descent] Migration submit was not acknowledged in this response — holding the ceremony's ledger (Level {Level}) and ignoring the server's pre-migration figures. Will re-submit on the next sync.",
                                settings.PlayerLevel);
                        }
                        // Adopt server XP after sync. Two cases:
                        // 1. Server > local: server has more (admin boost, other device). Adopt.
                        // 2. Server significantly < local: server clamped us (anti-cheat). Adopt to
                        //    kill the file-edit exploit where inflated local persists across syncs.
                        // Small local > server gaps (<5K) are normal race conditions during active
                        // sessions (XP earned while sync was in-flight) — don't force those down.
                        else if (v2Result?.User != null)
                        {
                            var serverTotalXp = (double)v2Result.User.Xp;
                            var localTotalXp = App.Progression?.GetTotalXP(settings.PlayerLevel, settings.PlayerXP) ?? 0;

                            if (serverTotalXp > localTotalXp + 5000)
                            {
                                // Server has substantially more — adopt server values (admin boost, other device)
                                var serverLevel = v2Result.User.Level;
                                var serverLevelXp = App.Progression?.GetCurrentLevelXP(serverLevel, serverTotalXp) ?? 0;

                                App.Logger?.Information("V2 Sync: Server XP higher — adopting Level {ServerLevel} XP {ServerXp} (local was {LocalXp})",
                                    serverLevel, serverTotalXp, localTotalXp);
                                settings.PlayerLevel = serverLevel;
                                settings.PlayerXP = serverLevelXp;
                                App.Settings?.Save();
                            }
                            else if (localTotalXp > serverTotalXp + 75000)
                            {
                                // Server significantly below local — normally an anti-cheat clamp.
                                // BUT mirror the V1 defense (see the legacy path below): distinguish
                                // "server clamped a real inflated profile" from "server returned an
                                // uninitialized/empty record" (e.g. a broken Discord link so the
                                // account resolves to a pristine profile, or a failed server read).
                                // The latter looks like Level<=1 with no meaningful XP — nothing a
                                // genuinely progressed user could have. Clamping to that resets the
                                // player to Level 1 on EVERY sync (repeat data loss, #865).
                                // The bar here is deliberately HIGHER than
                                // ServerProfileLooksUninitialized: a Level 1 row is refused
                                // whatever XP it carries, because no legitimate clamp puts an
                                // account that is 75k XP ahead back on Level 1. See
                                // ServerProfileTooEmptyToClampTo.
                                bool serverLooksUninitialized =
                                    ServerProfileTooEmptyToClampTo(v2Result.User.Level);

                                if (serverLooksUninitialized)
                                {
                                    App.Logger?.Warning("[Anti-cheat] V2 Sync DEFENDED: server profile is too empty to clamp to (Level {SL}, {SX} XP) but local has progress (Level {LL}, {LX} XP). Refusing to clamp — likely a failed/empty server read or broken account link, not an exploit. Local kept.",
                                        v2Result.User.Level, serverTotalXp, settings.PlayerLevel, localTotalXp);
                                    // Keep local values; a later good sync (or admin action) reconciles.
                                }
                                else
                                {
                                    // Server clamped our XP significantly — force adopt to prevent exploit
                                    var serverLevel = v2Result.User.Level;
                                    var serverLevelXp = App.Progression?.GetCurrentLevelXP(serverLevel, serverTotalXp) ?? 0;

                                    App.Logger?.Warning("[Anti-cheat] V2 Sync: Server clamped XP — forcing Level {ServerLevel} XP {ServerXp} (local was {LocalXp})",
                                        serverLevel, serverTotalXp, localTotalXp);
                                    settings.PlayerLevel = serverLevel;
                                    settings.PlayerXP = serverLevelXp;
                                    App.Settings?.Save();
                                }
                            }
                        }

                        // Last, once the season key and every adopt above have settled: record what
                        // the two sides now agree this account holds. The response is the server's
                        // own account of it, which is what makes the watermark worth anything — a
                        // corrupted local file cannot manufacture the figure. (#865)
                        //
                        // Passing the client's POST-reconcile total is what makes this "last
                        // agreed" rather than "highest ever": where a branch above adopted the
                        // server's number the two match and the watermark follows it, DOWN as well
                        // as up (the anti-cheat clamp is the case that matters — the old monotonic
                        // rule latched there and blocked every later sync). Where a branch above
                        // deliberately KEPT a higher local — the clamp's defend branch — the totals
                        // differ, RecordAgreedServerXp sees the disagreement and leaves the
                        // previously agreed figure standing.
                        //
                        // Suppressed entirely while a migration submit is unacked, for the same
                        // reason the adopt above is: there is no agreement to record. The server
                        // is still quoting a pre-ceremony total and this client is deliberately
                        // holding a lower one. Writing that down as "agreed" would arm the
                        // send-guard at a figure the ceremony just retired.
                        if (v2Result?.User != null &&
                            (!migrationSubmitInFlight || v2Result.DescentMigration?.Completed == true))
                        {
                            var agreedClientXp = App.Progression?.GetTotalXP(settings.PlayerLevel, settings.PlayerXP) ?? settings.PlayerXP;
                            RecordAgreedServerXp(settings, v2Result.User.Xp, agreedClientXp, "V2 sync");
                            App.Settings?.Save();
                        }
                    }
                    catch (Exception parseEx)
                    {
                        App.Logger?.Debug("V2 Sync: Could not parse server flags: {Error}", parseEx.Message);
                    }

                    // THE VAT'S ONE UNAVOIDABLE SECOND REQUEST. An accepted sync is the
                    // moment today's XP lands in the server vat, and the sync RESPONSE
                    // does not carry the `descent` block (attachDescentBlocks is wired
                    // to /v2/user/profile and /v2/user/me only), so the meter can only
                    // learn about its own pour by asking again. Fire-and-forget, rate-
                    // floored inside the service, and it can never disturb this method:
                    // deliberately placed AFTER the catch above so nothing it does is
                    // swallowed as "could not parse server flags".
                    //
                    // GATED ON HasSeenBlock — no key, no heartbeat. The block ships
                    // only inside the server's rollout dial, so for every account
                    // outside it this second request can only ever answer "still no
                    // key": a wasted GET on every sync, for ~all users. The Trainer
                    // Card's one-shot on open is what lights a dark key up; this hook
                    // only has to keep an ALREADY-lit vat current.
                    if (App.Descent?.HasSeenBlock == true)
                        App.Descent.RequestRefresh("v2 sync accepted");

                    syncSucceeded = true;
                    return true;
                }

                // Legacy sync for users without unified_id
                raiseSource = "V1 sync";
                var legacyQuestProgress = App.Quests?.Progress;
                var syncData = new ProfileSyncData
                {
                    Xp = (int)totalXp,
                    Level = settings.PlayerLevel,
                    Achievements = achievementProgress?.UnlockedAchievements?.ToList() ?? new List<string>(),
                    Stats = new Dictionary<string, object>
                    {
                        ["completed_sessions"] = achievementProgress?.CompletedSessions?.Count ?? 0,
                        ["longest_session_minutes"] = achievementProgress?.LongestSessionMinutes ?? 0,
                        ["highest_streak"] = settings.HighestStreak,
                        ["total_flashes"] = achievementProgress?.TotalFlashImages ?? 0,
                        ["consecutive_days"] = achievementProgress?.ConsecutiveDays ?? 0,
                        ["total_bubbles_popped"] = achievementProgress?.TotalBubblesPopped ?? 0,
                        ["total_video_minutes"] = Math.Round(achievementProgress?.TotalVideoMinutes ?? 0, 1),
                        ["total_lock_cards_completed"] = achievementProgress?.TotalLockCardsCompleted ?? 0,
                        // Attention check stats
                        ["total_attention_checks_passed"] = achievementProgress?.TotalAttentionChecksPassed ?? 0,
                        ["video_attention_checks_passed"] = achievementProgress?.VideoAttentionChecksPassed ?? 0,
                        ["video_attention_checks_failed"] = achievementProgress?.VideoAttentionChecksFailed ?? 0,
                        ["total_attention_check_failures"] = achievementProgress?.AttentionCheckFailures ?? 0,
                        // Bubble count stats
                        ["total_bubble_count_games"] = achievementProgress?.TotalBubbleCountGames ?? 0,
                        ["total_bubble_count_correct"] = achievementProgress?.TotalBubbleCountCorrect ?? 0,
                        ["total_bubble_count_failed"] = achievementProgress?.TotalBubbleCountFailed ?? 0,
                        ["bubble_count_best_streak"] = achievementProgress?.BubbleCountBestStreak ?? 0,
                        // Session stats
                        ["total_sessions_started"] = achievementProgress?.TotalSessionsStarted ?? 0,
                        ["total_sessions_abandoned"] = achievementProgress?.TotalSessionsAbandoned ?? 0,
                        // XP & Progression stats
                        ["total_xp_earned"] = Math.Round(achievementProgress?.TotalXPEarned ?? 0, 0),
                        ["total_skill_points_earned"] = achievementProgress?.TotalSkillPointsEarned ?? 0,
                        ["lifetime_points_spent"] = achievementProgress?.LifetimeSkillPointsSpent ?? 0,
                        // Time stats
                        ["total_pink_filter_minutes"] = Math.Round(achievementProgress?.TotalPinkFilterMinutes ?? 0, 1),
                        ["total_spiral_minutes"] = Math.Round(achievementProgress?.TotalSpiralMinutes ?? 0, 1),
                        // Quest streak data
                        ["daily_quest_streak"] = settings.DailyQuestStreak,
                        ["last_daily_quest_date"] = settings.LastDailyQuestDate?.ToString("o") ?? "",
                        ["quest_completion_dates"] = legacyQuestProgress?.DailyQuestCompletionDates?
                            .Select(d => d.ToString("yyyy-MM-dd")).ToList() ?? new List<string>(),
                        ["total_daily_quests_completed"] = legacyQuestProgress?.TotalDailyQuestsCompleted ?? 0,
                        ["total_weekly_quests_completed"] = legacyQuestProgress?.TotalWeeklyQuestsCompleted ?? 0,
                        ["total_xp_from_quests"] = legacyQuestProgress?.TotalXPFromQuests ?? 0
                    },
                    LastSession = DateTime.Now.ToString("o"),
                    AllowDiscordDm = settings.AllowDiscordDm,
                    ShareProfilePicture = settings.ShareProfilePicture,
                    ShowOnlineStatus = settings.ShowOnlineStatus,
                    // Goon Game consent flags (GOON_DISCORD_CONTRACT §2). GoonRichPresence
                    // is local-only and never rides the body.
                    GoonShareAvatar = settings.GoonShareAvatar,
                    GoonShareDm = settings.GoonShareDiscordDm,
                    // Trainer Card customization (Profile redesign Phase 2) — same object, and the
                    // same never-wipe rule, on both sync paths so a V1 user's look survives a move
                    // to a V2 identity.
                    Cosmetics = BuildCosmeticsPayload(settings),
                    DiscordId = App.Discord?.UserId,  // Include Discord ID even when syncing via Patreon
                    AvatarUrl = App.Discord?.GetAvatarUrl(256),  // Include Discord avatar URL
                    SkillPoints = settings.SkillPoints,
                    UnlockedSkills = settings.UnlockedSkills?.ToList() ?? new List<string>(),
                    TotalConditioningMinutes = settings.TotalConditioningMinutes
                };

                // Use appropriate endpoint based on auth type
                var endpoint = IsPatreonAuth ? "/user/sync" : "/user/sync-discord";
                var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}{endpoint}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(syncData),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    App.Logger?.Warning("Profile sync failed: {Status} - {Error}", response.StatusCode, error);
                    LastSyncError = $"Sync failed: {response.StatusCode}";
                    return false;
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<SyncResponse>(json);

                LastSyncTime = DateTime.Now;
                LastSyncError = null;
                // The unequip-everything intent has now reached the server (see
                // BuildCosmeticsPayload); an empty loadout goes back to meaning "no change".
                PendingCosmeticsClear = false;

                App.Logger?.Information("Profile synced to cloud: Level {Level}, {Xp} XP (merged: {Merged})",
                    result?.Profile?.Level, result?.Profile?.Xp, result?.Merged);

                // If server had higher values, update local
                if (result?.Profile != null && result.Merged)
                {
                    MergeCloudProfile(result.Profile);
                }

                syncSucceeded = true;
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to sync profile to cloud");
                LastSyncError = ex.Message;
                return false;
            }
            }
            finally
            {
                // Track sync health — only count actual failures, not skips (cooldown, gate, offline)
                if (syncSucceeded)
                {
                    if (ConsecutiveSyncFailures > 0)
                    {
                        ConsecutiveSyncFailures = 0;
                        SyncHealthChanged?.Invoke(this, 0);
                    }
                }
                else if (LastSyncError != null)
                {
                    ConsecutiveSyncFailures++;
                    SyncHealthChanged?.Invoke(this, ConsecutiveSyncFailures);
                }

                // Repaint the header if ANY exit path from this call changed the displayed
                // progression — not just the two success paths that used to raise explicitly.
                //
                // #879: ReconcileRestoredProfileAsync adopts server level/XP BEFORE the POST is
                // even built. When that POST then 429s or fails — exactly the flaky-network case
                // the reconcile exists for — the old code returned without telling anyone, so the
                // level pill and XP bar kept showing the pre-adopt numbers until the next restart.
                // Comparing the snapshot here catches every early return, every failure branch and
                // the catch, and cannot be forgotten by a future exit path the way an explicit
                // call at each site could. A null snapshot means we bailed before anything could
                // have been adopted (offline, cooldown, no token, no settings).
                if (preSyncLevel.HasValue)
                    RaiseProfileLoadedIfProgressionChanged(App.Settings?.Current, preSyncLevel.Value, preSyncLevelXp, raiseSource);

                _syncGate.Release();
            }
        }

        /// <summary>
        /// Adopt a server-side Trainer Card loadout — but ONLY into an empty local one.
        ///
        /// Cosmetics are not progression: there is no "higher" value to merge toward, and the
        /// local copy is what the subject picked in the Customize dialog seconds ago. A blind
        /// down-merge would let a stale server echo silently undress their card between the pick
        /// and the next push. Filling an EMPTY loadout is the one case where the server is the
        /// only source of truth: a fresh install or a second machine.
        ///
        /// Returns true when settings were changed (caller saves).
        /// </summary>
        private static bool AdoptCloudCosmetics(Models.ProfileCosmetics? cloud)
        {
            try
            {
                if (cloud == null) return false;
                var settings = App.Settings?.Current;
                if (settings == null) return false;
                if (!settings.ProfileCosmetics.IsEmpty) return false;

                var clean = CosmeticsCatalog.SanitizeOwn(cloud);
                if (clean.IsEmpty) return false;

                settings.ProfileCosmetics = clean;
                App.Logger?.Information("Adopted cloud profile cosmetics (banner {Banner}, accent {Accent}, {Pins} pinned) into an empty local loadout",
                    clean.BannerId ?? "none", clean.Accent ?? "none", clean.PinnedAchievements.Count);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("AdoptCloudCosmetics skipped: {E}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// What to put in the sync payload's <c>cosmetics</c> field — the sanitized local loadout,
        /// or <c>null</c>.
        ///
        /// Null matters: the server reads an absent/null object as "no change" and an object whose
        /// every field is empty as "the user unequipped everything" (ccp-server proxy/cosmetics.js
        /// <c>resolveCosmetics</c> → <c>delete user.cosmetics</c>). Because LoadProfileAsync's V2
        /// branch SYNCS BEFORE it reads, always sending the object meant a fresh machine's very
        /// first POST wiped the account's loadout server-side, the response then echoed
        /// <c>cosmetics: null</c>, and <see cref="AdoptCloudCosmetics"/> could never fire — so
        /// reinstalling, or simply logging in on a second PC, permanently stripped everyone's card.
        ///
        /// So: send the loadout while there is one; send an explicit empty object only when the
        /// user actually unequipped everything in the Customize dialog
        /// (<see cref="PendingCosmeticsClear"/>) or a round-trip has already confirmed we are in
        /// sync with the server; otherwise send nothing and let the server keep what it has.
        /// </summary>
        private Models.ProfileCosmetics? BuildCosmeticsPayload(Models.AppSettings settings)
        {
            try
            {
                var clean = CosmeticsCatalog.SanitizeOwn(settings.ProfileCosmetics);
                if (!clean.IsEmpty) return clean;
                return PendingCosmeticsClear || _hasLoadedProfile ? clean : null;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BuildCosmeticsPayload: {E}", ex.Message);
                return null;   // the safe direction - "no change" never destroys anything
            }
        }

        /// <summary>
        /// Merge cloud profile with local data, taking the HIGHER values to prevent progress loss.
        /// This protects against cloud data corruption, sync issues, or stale cloud profiles.
        /// </summary>
        private void MergeCloudProfile(CloudProfile cloudProfile)
        {
            var settings = App.Settings?.Current;
            var achievements = App.Achievements;

            if (settings == null) return;

            bool needsSave = false;

            // Calculate total XP for both local and cloud to compare properly
            // Cloud stores TOTAL XP, local stores current-level XP
            var localTotalXp = App.Progression?.GetTotalXP(settings.PlayerLevel, settings.PlayerXP) ?? settings.PlayerXP;
            var cloudTotalXp = (double)cloudProfile.Xp;

            // Cloud is authoritative on startup. Allow a small grace delta for unsynced
            // progress from a crash, but reject suspiciously large local values (file edits).
            const double MAX_STARTUP_DELTA = 50000; // Max XP above cloud we trust from local

            if (cloudTotalXp > localTotalXp)
            {
                // Cloud has more progress - use cloud values
                var cloudLevelXp = App.Progression?.GetCurrentLevelXP(cloudProfile.Level, cloudProfile.Xp) ?? 0;

                App.Logger?.Information("Cloud has higher progress - syncing DOWN: Cloud Level {CloudLevel} ({CloudXP} total XP) > Local Level {LocalLevel} ({LocalXP} total XP)",
                    cloudProfile.Level, (int)cloudTotalXp, settings.PlayerLevel, (int)localTotalXp);

                settings.PlayerLevel = cloudProfile.Level;
                settings.PlayerXP = cloudLevelXp;
                needsSave = true;

                // Check for level-based achievements with the new level
                App.Achievements?.CheckLevelAchievements(cloudProfile.Level);
            }
            else if (localTotalXp > cloudTotalXp + MAX_STARTUP_DELTA)
            {
                // Local is suspiciously higher than cloud — would normally adopt cloud to prevent file-edit exploits.
                // BUT: distinguish "real cloud says you're ahead of yourself" from "cloud read fell through to an
                // uninitialized record" (e.g. V2 sync rate-limited, V1 fallback returns empty defaults for a
                // V2-native user). The latter looks like Level<=1 with no meaningful XP — a pristine record
                // that no real progressed user could have. Treat that as a misload and keep local.
                // #865: the achievements/skills/skill-points clauses that used to be ANDed in here made a
                // single stray non-zero field read as "the cloud record is real", and the clamp below then
                // reset the player on every launch. Same predicate as the V2 path now — and, like
                // the V2 path, the clamp now refuses ANY Level<=1 record regardless of the XP it
                // carries, because a row emptied to Level 1 / 150 XP would otherwise sail past the
                // 100 XP floor and reset a Level 40 local every launch anyway. See
                // ServerProfileTooEmptyToClampTo.
                // (The watermark adds nothing here anyway: it is a V2-only mechanism, and this
                // predicate already covers every empty-row shape it would have caught.)
                bool looksUninitialized =
                    ServerProfileTooEmptyToClampTo(cloudProfile.Level);

                if (looksUninitialized)
                {
                    App.Logger?.Warning("[Anti-cheat] DEFENDED: cloud profile is too empty to clamp to (Level {CloudLevel}, {CloudXP} XP) but local has progress (Level {LocalLevel}, {LocalXP} XP). Refusing to clobber — likely a failed/empty cloud read, not an exploit. Local kept.",
                        cloudProfile.Level, (int)cloudTotalXp, settings.PlayerLevel, (int)localTotalXp);
                    // Fall through without modifying settings — keep local values.
                }
                else
                {
                    // Cloud has real data and local is well above it — adopt cloud (file-edit exploit guard).
                    var cloudLevelXp = App.Progression?.GetCurrentLevelXP(cloudProfile.Level, cloudProfile.Xp) ?? 0;

                    App.Logger?.Warning("[Anti-cheat] Local XP suspiciously high on startup: local={LocalXP} vs cloud={CloudXP} (delta={Delta}) — forcing cloud values",
                        (int)localTotalXp, (int)cloudTotalXp, (int)(localTotalXp - cloudTotalXp));

                    settings.PlayerLevel = cloudProfile.Level;
                    settings.PlayerXP = cloudLevelXp;
                    needsSave = true;
                }
            }
            else if (localTotalXp > cloudTotalXp)
            {
                // Small delta - likely unsynced progress from a crash. Sync UP.
                App.Logger?.Information("Local has higher progress - keeping local: Local Level {LocalLevel} ({LocalXP} total XP) > Cloud Level {CloudLevel} ({CloudXP} total XP)",
                    settings.PlayerLevel, (int)localTotalXp, cloudProfile.Level, (int)cloudTotalXp);

                // Trigger an immediate sync UP so cloud gets the correct data
                _ = Task.Run(async () =>
                {
                    try { await Task.Delay(1000); await SyncProfileAsync(); }
                    catch (Exception ex) { App.Logger?.Error(ex, "Background sync-up failed"); }
                });
            }
            else
            {
                App.Logger?.Debug("Local and cloud progress are equal: Level {Level}, Total XP {XP}",
                    settings.PlayerLevel, (int)localTotalXp);
            }

            // The cloud just told us what it holds. Record it as the agreed figure — AFTER the
            // branches above, so the client total passed in is the post-merge one and
            // RecordAgreedServerXp can tell an adopt from a "kept local" (#865).
            //
            // B-4: this arms nothing for an actual V1/legacy user. They have no unified_id and no
            // season key, so their scope would be ("", "") — never invalidated by a rollover,
            // shared with every other legacy account on the machine, and with no escape but a
            // manual logout if the send-guard ever caught them. RecordAgreedServerXp refuses them
            // outright. The call stays because this method is also the V1 FALLBACK for a V2
            // identity, and that user does have a scope worth arming.
            var mergedClientTotalXp = App.Progression?.GetTotalXP(settings.PlayerLevel, settings.PlayerXP) ?? settings.PlayerXP;
            RecordAgreedServerXp(settings, cloudTotalXp, mergedClientTotalXp, "V1 merge");

            // Merge achievements
            if (cloudProfile.Achievements != null && achievements?.Progress != null)
            {
                foreach (var achievementId in cloudProfile.Achievements)
                {
                    if (!achievements.Progress.IsUnlocked(achievementId))
                    {
                        App.Logger?.Information("Unlocking achievement from cloud: {AchievementId}", achievementId);
                        achievements.Progress.Unlock(achievementId);
                        needsSave = true;
                    }
                }
            }

            // Merge stats - take HIGHER values to prevent progress loss
            if (cloudProfile.Stats != null && achievements?.Progress != null)
            {
                var progress = achievements.Progress;

                if (cloudProfile.Stats.TryGetValue("longest_session_minutes", out var minutes))
                {
                    var m = Convert.ToDouble(minutes);
                    if (m > progress.LongestSessionMinutes)
                    {
                        App.Logger?.Debug("Stats sync: LongestSessionMinutes cloud ({Cloud}) > local ({Local})", m, progress.LongestSessionMinutes);
                        progress.LongestSessionMinutes = m;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("total_flashes", out var flashes))
                {
                    var f = Convert.ToInt32(flashes);
                    if (f > progress.TotalFlashImages)
                    {
                        App.Logger?.Debug("Stats sync: TotalFlashImages cloud ({Cloud}) > local ({Local})", f, progress.TotalFlashImages);
                        progress.TotalFlashImages = f;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("consecutive_days", out var streak))
                {
                    var st = Convert.ToInt32(streak);
                    if (st > progress.ConsecutiveDays)
                    {
                        App.Logger?.Debug("Stats sync: ConsecutiveDays cloud ({Cloud}) > local ({Local})", st, progress.ConsecutiveDays);
                        progress.ConsecutiveDays = st;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("total_bubbles_popped", out var bubbles))
                {
                    var b = Convert.ToInt32(bubbles);
                    if (b > progress.TotalBubblesPopped)
                    {
                        App.Logger?.Debug("Stats sync: TotalBubblesPopped cloud ({Cloud}) > local ({Local})", b, progress.TotalBubblesPopped);
                        progress.TotalBubblesPopped = b;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("total_video_minutes", out var videoMin))
                {
                    var v = Convert.ToDouble(videoMin);
                    if (v > progress.TotalVideoMinutes)
                    {
                        progress.TotalVideoMinutes = v;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("total_lock_cards_completed", out var lockCards))
                {
                    var lc = Convert.ToInt32(lockCards);
                    if (lc > progress.TotalLockCardsCompleted)
                    {
                        progress.TotalLockCardsCompleted = lc;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("highest_streak", out var hStreak))
                {
                    var hs = Convert.ToInt32(hStreak);
                    var settings2 = App.Settings?.Current;
                    if (settings2 != null && hs > settings2.HighestStreak)
                    {
                        settings2.HighestStreak = hs;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("total_attention_checks_passed", out var attPassed))
                {
                    var ap = Convert.ToInt32(attPassed);
                    if (ap > progress.TotalAttentionChecksPassed)
                    {
                        progress.TotalAttentionChecksPassed = ap;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("video_attention_checks_passed", out var vidAttPassed))
                {
                    var vap = Convert.ToInt32(vidAttPassed);
                    if (vap > progress.VideoAttentionChecksPassed)
                    {
                        progress.VideoAttentionChecksPassed = vap;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("video_attention_checks_failed", out var vidAttFailed))
                {
                    var vaf = Convert.ToInt32(vidAttFailed);
                    if (vaf > progress.VideoAttentionChecksFailed)
                    {
                        progress.VideoAttentionChecksFailed = vaf;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("total_attention_check_failures", out var attFail))
                {
                    var af = Convert.ToInt32(attFail);
                    if (af > progress.AttentionCheckFailures)
                    {
                        progress.AttentionCheckFailures = af;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("total_bubble_count_games", out var bcGames))
                {
                    var bg = Convert.ToInt32(bcGames);
                    if (bg > progress.TotalBubbleCountGames)
                    {
                        progress.TotalBubbleCountGames = bg;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("total_bubble_count_correct", out var bcCorrect))
                {
                    var bc = Convert.ToInt32(bcCorrect);
                    if (bc > progress.TotalBubbleCountCorrect)
                    {
                        progress.TotalBubbleCountCorrect = bc;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("total_bubble_count_failed", out var bcFailed))
                {
                    var bf = Convert.ToInt32(bcFailed);
                    if (bf > progress.TotalBubbleCountFailed)
                    {
                        progress.TotalBubbleCountFailed = bf;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("bubble_count_best_streak", out var bcStreak))
                {
                    var bs = Convert.ToInt32(bcStreak);
                    if (bs > progress.BubbleCountBestStreak)
                    {
                        progress.BubbleCountBestStreak = bs;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("total_sessions_started", out var sessStarted))
                {
                    var ss = Convert.ToInt32(sessStarted);
                    if (ss > progress.TotalSessionsStarted)
                    {
                        progress.TotalSessionsStarted = ss;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("total_sessions_abandoned", out var sessAbandoned))
                {
                    var sa = Convert.ToInt32(sessAbandoned);
                    if (sa > progress.TotalSessionsAbandoned)
                    {
                        progress.TotalSessionsAbandoned = sa;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("total_xp_earned", out var xpEarned))
                {
                    var xe = Convert.ToDouble(xpEarned);
                    if (xe > progress.TotalXPEarned)
                    {
                        progress.TotalXPEarned = xe;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("total_skill_points_earned", out var spEarned))
                {
                    var sp = Convert.ToInt32(spEarned);
                    if (sp > progress.TotalSkillPointsEarned)
                    {
                        progress.TotalSkillPointsEarned = sp;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("total_pink_filter_minutes", out var pinkMin))
                {
                    var pm = Convert.ToDouble(pinkMin);
                    if (pm > progress.TotalPinkFilterMinutes)
                    {
                        progress.TotalPinkFilterMinutes = pm;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("total_spiral_minutes", out var spiralMin))
                {
                    var sm = Convert.ToDouble(spiralMin);
                    if (sm > progress.TotalSpiralMinutes)
                    {
                        progress.TotalSpiralMinutes = sm;
                        needsSave = true;
                    }
                }
            }

            // Merge quest streak data (skip if force_streak_override is active - handled separately)
            if (cloudProfile.Stats != null && cloudProfile.ForceStreakOverride != true)
            {
                // Take higher streak
                if (cloudProfile.Stats.TryGetValue("daily_quest_streak", out var cloudStreak))
                {
                    var cs = Convert.ToInt32(cloudStreak);
                    if (cs > settings.DailyQuestStreak)
                    {
                        App.Logger?.Debug("Quest sync: DailyQuestStreak cloud ({Cloud}) > local ({Local})", cs, settings.DailyQuestStreak);
                        settings.DailyQuestStreak = cs;
                        needsSave = true;
                    }
                }

                // Take most recent last_daily_quest_date
                if (cloudProfile.Stats.TryGetValue("last_daily_quest_date", out var cloudLastDate))
                {
                    var dateStr = cloudLastDate?.ToString();
                    if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var cloudDate))
                    {
                        if (!settings.LastDailyQuestDate.HasValue || cloudDate.Date > settings.LastDailyQuestDate.Value.Date)
                        {
                            App.Logger?.Debug("Quest sync: LastDailyQuestDate cloud ({Cloud}) > local ({Local})", cloudDate.Date, settings.LastDailyQuestDate);
                            settings.LastDailyQuestDate = cloudDate.Date;
                            needsSave = true;
                        }
                    }
                }

                // Merge completion dates (union of both sets)
                var questProgress = App.Quests?.Progress;
                if (questProgress != null && cloudProfile.Stats.TryGetValue("quest_completion_dates", out var cloudDatesObj))
                {
                    try
                    {
                        var cloudDates = JsonConvert.DeserializeObject<List<string>>(cloudDatesObj?.ToString() ?? "[]");
                        if (cloudDates != null)
                        {
                            var localDates = new HashSet<DateTime>(questProgress.DailyQuestCompletionDates.Select(d => d.Date));
                            bool datesChanged = false;
                            foreach (var ds in cloudDates)
                            {
                                if (DateTime.TryParse(ds, out var d) && !localDates.Contains(d.Date))
                                {
                                    questProgress.DailyQuestCompletionDates.Add(d.Date);
                                    datesChanged = true;
                                }
                            }
                            if (datesChanged)
                            {
                                // Trim to last 90 days (supports long streaks)
                                var cutoff = DateTime.Today.AddDays(-90);
                                questProgress.DailyQuestCompletionDates.RemoveAll(d => d.Date < cutoff);
                                App.Logger?.Debug("Quest sync: Merged completion dates from cloud ({Count} total dates)",
                                    questProgress.DailyQuestCompletionDates.Count);
                                needsSave = true;

                                // Recompute streak from the merged calendar
                                // RecalculateStreak now never decreases the streak, so this is safe
                                App.Quests?.RecalculateStreak();

                                // Also take cloud streak if it's higher (server may know about
                                // dates we don't have locally, e.g. from another device)
                                if (cloudProfile.Stats.TryGetValue("daily_quest_streak", out var cloudStreakAfter))
                                {
                                    var csAfter = Convert.ToInt32(cloudStreakAfter);
                                    if (csAfter > settings.DailyQuestStreak)
                                    {
                                        App.Logger?.Debug("Quest sync: Adopting cloud streak {Cloud} (local was {Local})", csAfter, settings.DailyQuestStreak);
                                        settings.DailyQuestStreak = csAfter;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("Quest sync: Failed to parse cloud completion dates: {Error}", ex.Message);
                    }
                }

                // Take higher quest totals
                if (questProgress != null)
                {
                    if (cloudProfile.Stats.TryGetValue("total_daily_quests_completed", out var cloudDailyTotal))
                    {
                        var cdt = Convert.ToInt32(cloudDailyTotal);
                        if (cdt > questProgress.TotalDailyQuestsCompleted)
                        {
                            questProgress.TotalDailyQuestsCompleted = cdt;
                            needsSave = true;
                        }
                    }
                    if (cloudProfile.Stats.TryGetValue("total_weekly_quests_completed", out var cloudWeeklyTotal))
                    {
                        var cwt = Convert.ToInt32(cloudWeeklyTotal);
                        if (cwt > questProgress.TotalWeeklyQuestsCompleted)
                        {
                            questProgress.TotalWeeklyQuestsCompleted = cwt;
                            needsSave = true;
                        }
                    }
                    if (cloudProfile.Stats.TryGetValue("total_xp_from_quests", out var cloudQuestXp))
                    {
                        var cqx = Convert.ToInt32(cloudQuestXp);
                        if (cqx > questProgress.TotalXPFromQuests)
                        {
                            questProgress.TotalXPFromQuests = cqx;
                            needsSave = true;
                        }
                    }

                    // Restore daily_quests_completed_today from cloud (prevents quest reset exploit)
                    if (cloudProfile.Stats.TryGetValue("daily_quests_completed_today", out var cloudDailyCompToday))
                    {
                        var cloudCount = Convert.ToInt32(cloudDailyCompToday);
                        bool cloudDateIsToday = false;
                        if (cloudProfile.Stats.TryGetValue("daily_completion_reset_date", out var cloudResetDate))
                        {
                            if (DateTime.TryParse(cloudResetDate?.ToString(), out var resetDate))
                                cloudDateIsToday = resetDate.Date == DateTime.Today;
                        }
                        if (cloudDateIsToday && cloudCount > questProgress.GetDailyQuestsCompletedToday())
                        {
                            // Cross-reference: only accept cloud counter if completion dates actually
                            // show evidence of today's quests. This prevents stale max-merged server
                            // values from marking quests as completed when they weren't done today.
                            bool hasCompletionEvidence = questProgress.DailyQuestCompletionDates
                                .Any(d => d.Date == DateTime.Today);
                            if (hasCompletionEvidence)
                            {
                                questProgress.DailyQuestsCompletedToday = cloudCount;
                                questProgress.DailyCompletionResetDate = DateTime.Today;
                                needsSave = true;
                                App.Logger?.Debug("Quest sync: Restored daily counter to {Count} (verified by completion dates)", cloudCount);
                            }
                            else
                            {
                                App.Logger?.Debug("Quest sync: Rejected cloud daily counter {Count} — no completion evidence for today", cloudCount);
                            }
                        }
                    }

                    // Defensive fallback: if today is in completion dates but counter is 0
                    if (questProgress.DailyQuestCompletionDates.Any(d => d.Date == DateTime.Today)
                        && questProgress.GetDailyQuestsCompletedToday() == 0)
                    {
                        questProgress.DailyQuestsCompletedToday = 1;
                        questProgress.DailyCompletionResetDate = DateTime.Today;
                        needsSave = true;
                    }
                }
            }

            // Merge skill tree data - take max of server/local (skill points only increase)
            if (cloudProfile.SkillPoints.HasValue)
            {
                var maxPoints = Math.Max(cloudProfile.SkillPoints.Value, settings.SkillPoints);
                if (maxPoints != settings.SkillPoints)
                {
                    App.Logger?.Information("Skill tree sync: Skill points server={Server}, local={Local} — taking max ({Max})",
                        cloudProfile.SkillPoints.Value, settings.SkillPoints, maxPoints);
                    settings.SkillPoints = maxPoints;
                    needsSave = true;
                }
            }

            // Merge unlocked skills - union of both (never lose unlocked skills)
            if (cloudProfile.UnlockedSkills != null && cloudProfile.UnlockedSkills.Count > 0)
            {
                var localSkills = settings.UnlockedSkills ?? new List<string>();
                var skillsToAdd = cloudProfile.UnlockedSkills.Except(localSkills).ToList();

                if (skillsToAdd.Count > 0)
                {
                    App.Logger?.Information("Skill tree sync: Adding {Count} unlocked skills from cloud: {Skills}",
                        skillsToAdd.Count, string.Join(", ", skillsToAdd));

                    // Add all cloud skills that aren't in local
                    foreach (var skill in skillsToAdd)
                    {
                        if (!localSkills.Contains(skill))
                        {
                            localSkills.Add(skill);
                        }
                    }

                    settings.UnlockedSkills = localSkills;
                    needsSave = true;
                }
            }

            // Trainer Card cosmetics: fill-if-empty only (see AdoptCloudCosmetics for why this is
            // deliberately NOT a merge).
            if (AdoptCloudCosmetics(cloudProfile.Cosmetics)) needsSave = true;

            // Merge conditioning time - take HIGHER value to prevent loss
            if (cloudProfile.TotalConditioningMinutes.HasValue)
            {
                if (cloudProfile.TotalConditioningMinutes.Value > settings.TotalConditioningMinutes)
                {
                    App.Logger?.Information("Conditioning time sync: Cloud has more time ({Cloud:F1} min) > local ({Local:F1} min), syncing DOWN",
                        cloudProfile.TotalConditioningMinutes.Value, settings.TotalConditioningMinutes);
                    settings.TotalConditioningMinutes = cloudProfile.TotalConditioningMinutes.Value;
                    needsSave = true;
                }
                else if (settings.TotalConditioningMinutes > cloudProfile.TotalConditioningMinutes.Value)
                {
                    App.Logger?.Information("Conditioning time sync: Local has more time ({Local:F1} min) > cloud ({Cloud:F1} min), will sync UP",
                        settings.TotalConditioningMinutes, cloudProfile.TotalConditioningMinutes.Value);
                    // Will sync up on next SyncProfileAsync
                }
            }

            // Merge companion progress from cloud (per-companion, higher level wins)
            if (cloudProfile.CompanionProgress != null && cloudProfile.CompanionProgress.Count > 0)
            {
                foreach (var (key, serverProgress) in cloudProfile.CompanionProgress)
                {
                    if (int.TryParse(key, out var companionId))
                    {
                        var localData = settings.CompanionProgressData;
                        localData.TryGetValue(companionId, out var localProgress);

                        var localLevel = localProgress?.Level ?? 0;
                        var serverLevel = serverProgress?.Level ?? 0;

                        if (serverLevel > localLevel || (localProgress == null && serverProgress != null))
                        {
                            localData[companionId] = serverProgress!;
                            needsSave = true;
                        }
                    }
                }
            }

            // Handle server-side quest reset flags
            if (cloudProfile.ResetWeeklyQuest == true)
            {
                App.Logger?.Information("Server requested weekly quest reset for this user");
                App.Quests?.ForceRegenerateWeeklyQuest();
                needsSave = true;
                // Trigger sync to clear the flag on server
                _pendingQuestResetClear = true;
            }
            if (cloudProfile.ResetDailyQuest == true)
            {
                App.Logger?.Information("Server requested daily quest reset for this user");
                App.Quests?.ForceRegenerateDailyQuest();
                needsSave = true;
                _pendingQuestResetClear = true;
            }

            // Sync CurrentStreak (used by streak power skill) with ConsecutiveDays from cloud
            achievements?.Progress?.SyncCurrentStreak();

            // Save merged data
            if (needsSave)
            {
                App.Settings?.Save();
                achievements?.Save();
            }

            // Handle force_streak_override for legacy path (profile includes the flag)
            if (cloudProfile.ForceStreakOverride == true && cloudProfile.Stats != null)
            {
                App.Logger?.Information("Legacy sync: Force streak override - adopting server streak values");
                var legacyStreakStats = new V2StreakStats();
                if (cloudProfile.Stats.TryGetValue("daily_quest_streak", out var fStreak))
                    legacyStreakStats.DailyQuestStreak = Convert.ToInt32(fStreak);
                if (cloudProfile.Stats.TryGetValue("last_daily_quest_date", out var fDate))
                    legacyStreakStats.LastDailyQuestDate = fDate?.ToString();
                if (cloudProfile.Stats.TryGetValue("quest_completion_dates", out var fDates))
                {
                    try { legacyStreakStats.QuestCompletionDates = JsonConvert.DeserializeObject<List<string>>(fDates?.ToString() ?? "[]"); }
                    catch { }
                }
                if (cloudProfile.Stats.TryGetValue("total_daily_quests_completed", out var fDailyTotal))
                    legacyStreakStats.TotalDailyQuestsCompleted = Convert.ToInt32(fDailyTotal);
                if (cloudProfile.Stats.TryGetValue("total_weekly_quests_completed", out var fWeeklyTotal))
                    legacyStreakStats.TotalWeeklyQuestsCompleted = Convert.ToInt32(fWeeklyTotal);
                if (cloudProfile.Stats.TryGetValue("total_xp_from_quests", out var fXp))
                    legacyStreakStats.TotalXPFromQuests = Convert.ToInt32(fXp);

                ApplyForceStreakOverride(legacyStreakStats);
                needsSave = true;
                // Trigger sync to clear the flag on server
                _pendingQuestResetClear = true;
            }

            // If quest reset flags or force streak override were processed, sync back to clear them on server
            if (_pendingQuestResetClear)
            {
                _pendingQuestResetClear = false;
                _ = Task.Run(async () =>
                {
                    try { await Task.Delay(500); await SyncProfileAsync(); }
                    catch (Exception ex) { App.Logger?.Error(ex, "Background quest-reset sync failed"); }
                });
            }
        }

        /// <summary>
        /// Pull lifetime stats and quest streak data down from a V2 sync response and merge into
        /// local AchievementProgress / Settings / QuestProgress using max-merge semantics.
        /// Mirrors the stats portion of MergeCloudProfile but for the V2 sync path, which previously
        /// only synced stats UP and never pulled cloud values DOWN.
        /// Returns true if any local data was modified.
        /// </summary>
        private bool MergeV2CloudStatsIntoLocalProgress(Dictionary<string, object>? cloudStats, bool forceStreakOverride)
        {
            if (cloudStats == null) return false;
            var settings = App.Settings?.Current;
            var achievements = App.Achievements;
            if (settings == null) return false;

            bool needsSave = false;

            // --- Lifetime stats merge (AchievementProgress) ---
            if (achievements?.Progress != null)
            {
                var progress = achievements.Progress;

                if (cloudStats.TryGetValue("longest_session_minutes", out var minutes))
                {
                    var m = Convert.ToDouble(minutes);
                    if (m > progress.LongestSessionMinutes) { progress.LongestSessionMinutes = m; needsSave = true; }
                }
                if (cloudStats.TryGetValue("total_flashes", out var flashes))
                {
                    var f = Convert.ToInt32(flashes);
                    if (f > progress.TotalFlashImages) { progress.TotalFlashImages = f; needsSave = true; }
                }
                if (cloudStats.TryGetValue("consecutive_days", out var streak))
                {
                    var st = Convert.ToInt32(streak);
                    if (st > progress.ConsecutiveDays) { progress.ConsecutiveDays = st; needsSave = true; }
                }
                if (cloudStats.TryGetValue("total_bubbles_popped", out var bubbles))
                {
                    var b = Convert.ToInt32(bubbles);
                    if (b > progress.TotalBubblesPopped) { progress.TotalBubblesPopped = b; needsSave = true; }
                }
                if (cloudStats.TryGetValue("total_video_minutes", out var videoMin))
                {
                    var v = Convert.ToDouble(videoMin);
                    if (v > progress.TotalVideoMinutes) { progress.TotalVideoMinutes = v; needsSave = true; }
                }
                if (cloudStats.TryGetValue("total_lock_cards_completed", out var lockCards))
                {
                    var lc = Convert.ToInt32(lockCards);
                    if (lc > progress.TotalLockCardsCompleted) { progress.TotalLockCardsCompleted = lc; needsSave = true; }
                }
                if (cloudStats.TryGetValue("highest_streak", out var hStreak))
                {
                    var hs = Convert.ToInt32(hStreak);
                    if (hs > settings.HighestStreak) { settings.HighestStreak = hs; needsSave = true; }
                }
                if (cloudStats.TryGetValue("total_attention_checks_passed", out var attPassed))
                {
                    var ap = Convert.ToInt32(attPassed);
                    if (ap > progress.TotalAttentionChecksPassed) { progress.TotalAttentionChecksPassed = ap; needsSave = true; }
                }
                if (cloudStats.TryGetValue("video_attention_checks_passed", out var vidAttPassed))
                {
                    var vap = Convert.ToInt32(vidAttPassed);
                    if (vap > progress.VideoAttentionChecksPassed) { progress.VideoAttentionChecksPassed = vap; needsSave = true; }
                }
                if (cloudStats.TryGetValue("video_attention_checks_failed", out var vidAttFailed))
                {
                    var vaf = Convert.ToInt32(vidAttFailed);
                    if (vaf > progress.VideoAttentionChecksFailed) { progress.VideoAttentionChecksFailed = vaf; needsSave = true; }
                }
                if (cloudStats.TryGetValue("total_attention_check_failures", out var attFail))
                {
                    var af = Convert.ToInt32(attFail);
                    if (af > progress.AttentionCheckFailures) { progress.AttentionCheckFailures = af; needsSave = true; }
                }
                if (cloudStats.TryGetValue("total_bubble_count_games", out var bcGames))
                {
                    var bg = Convert.ToInt32(bcGames);
                    if (bg > progress.TotalBubbleCountGames) { progress.TotalBubbleCountGames = bg; needsSave = true; }
                }
                if (cloudStats.TryGetValue("total_bubble_count_correct", out var bcCorrect))
                {
                    var bc = Convert.ToInt32(bcCorrect);
                    if (bc > progress.TotalBubbleCountCorrect) { progress.TotalBubbleCountCorrect = bc; needsSave = true; }
                }
                if (cloudStats.TryGetValue("total_bubble_count_failed", out var bcFailed))
                {
                    var bf = Convert.ToInt32(bcFailed);
                    if (bf > progress.TotalBubbleCountFailed) { progress.TotalBubbleCountFailed = bf; needsSave = true; }
                }
                if (cloudStats.TryGetValue("bubble_count_best_streak", out var bcStreak))
                {
                    var bs = Convert.ToInt32(bcStreak);
                    if (bs > progress.BubbleCountBestStreak) { progress.BubbleCountBestStreak = bs; needsSave = true; }
                }
                if (cloudStats.TryGetValue("total_sessions_started", out var sessStarted))
                {
                    var ss = Convert.ToInt32(sessStarted);
                    if (ss > progress.TotalSessionsStarted) { progress.TotalSessionsStarted = ss; needsSave = true; }
                }
                if (cloudStats.TryGetValue("total_sessions_abandoned", out var sessAbandoned))
                {
                    var sa = Convert.ToInt32(sessAbandoned);
                    if (sa > progress.TotalSessionsAbandoned) { progress.TotalSessionsAbandoned = sa; needsSave = true; }
                }
                if (cloudStats.TryGetValue("total_xp_earned", out var xpEarned))
                {
                    var xe = Convert.ToDouble(xpEarned);
                    if (xe > progress.TotalXPEarned) { progress.TotalXPEarned = xe; needsSave = true; }
                }
                if (cloudStats.TryGetValue("total_skill_points_earned", out var spEarned))
                {
                    var sp = Convert.ToInt32(spEarned);
                    if (sp > progress.TotalSkillPointsEarned) { progress.TotalSkillPointsEarned = sp; needsSave = true; }
                }
                if (cloudStats.TryGetValue("lifetime_points_spent", out var lifetimeSpent))
                {
                    var ls = Convert.ToInt64(lifetimeSpent);
                    if (ls > progress.LifetimeSkillPointsSpent) { progress.LifetimeSkillPointsSpent = ls; needsSave = true; }
                }
                if (cloudStats.TryGetValue("total_pink_filter_minutes", out var pinkMin))
                {
                    var pm = Convert.ToDouble(pinkMin);
                    if (pm > progress.TotalPinkFilterMinutes) { progress.TotalPinkFilterMinutes = pm; needsSave = true; }
                }
                if (cloudStats.TryGetValue("total_spiral_minutes", out var spiralMin))
                {
                    var sm = Convert.ToDouble(spiralMin);
                    if (sm > progress.TotalSpiralMinutes) { progress.TotalSpiralMinutes = sm; needsSave = true; }
                }
            }

            // --- Quest streak data merge (skip if force_streak_override active — handled separately by ApplyForceStreakOverride) ---
            if (!forceStreakOverride)
            {
                if (cloudStats.TryGetValue("daily_quest_streak", out var cloudStreak))
                {
                    var cs = Convert.ToInt32(cloudStreak);
                    if (cs > settings.DailyQuestStreak)
                    {
                        App.Logger?.Debug("V2 Quest sync: DailyQuestStreak cloud ({Cloud}) > local ({Local})", cs, settings.DailyQuestStreak);
                        settings.DailyQuestStreak = cs;
                        needsSave = true;
                    }
                }

                if (cloudStats.TryGetValue("last_daily_quest_date", out var cloudLastDate))
                {
                    var dateStr = cloudLastDate?.ToString();
                    if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var cloudDate))
                    {
                        if (!settings.LastDailyQuestDate.HasValue || cloudDate.Date > settings.LastDailyQuestDate.Value.Date)
                        {
                            settings.LastDailyQuestDate = cloudDate.Date;
                            needsSave = true;
                        }
                    }
                }

                var questProgress = App.Quests?.Progress;
                if (questProgress != null && cloudStats.TryGetValue("quest_completion_dates", out var cloudDatesObj))
                {
                    try
                    {
                        var cloudDates = JsonConvert.DeserializeObject<List<string>>(cloudDatesObj?.ToString() ?? "[]");
                        if (cloudDates != null)
                        {
                            var localDates = new HashSet<DateTime>(questProgress.DailyQuestCompletionDates.Select(d => d.Date));
                            bool datesChanged = false;
                            foreach (var ds in cloudDates)
                            {
                                if (DateTime.TryParse(ds, out var d) && !localDates.Contains(d.Date))
                                {
                                    questProgress.DailyQuestCompletionDates.Add(d.Date);
                                    datesChanged = true;
                                }
                            }
                            if (datesChanged)
                            {
                                var cutoff = DateTime.Today.AddDays(-90);
                                questProgress.DailyQuestCompletionDates.RemoveAll(d => d.Date < cutoff);
                                needsSave = true;
                                App.Quests?.RecalculateStreak();

                                if (cloudStats.TryGetValue("daily_quest_streak", out var cloudStreakAfter))
                                {
                                    var csAfter = Convert.ToInt32(cloudStreakAfter);
                                    if (csAfter > settings.DailyQuestStreak)
                                    {
                                        settings.DailyQuestStreak = csAfter;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("V2 Quest sync: Failed to parse cloud completion dates: {Error}", ex.Message);
                    }
                }

                if (questProgress != null)
                {
                    if (cloudStats.TryGetValue("total_daily_quests_completed", out var cloudDailyTotal))
                    {
                        var cdt = Convert.ToInt32(cloudDailyTotal);
                        if (cdt > questProgress.TotalDailyQuestsCompleted) { questProgress.TotalDailyQuestsCompleted = cdt; needsSave = true; }
                    }
                    if (cloudStats.TryGetValue("total_weekly_quests_completed", out var cloudWeeklyTotal))
                    {
                        var cwt = Convert.ToInt32(cloudWeeklyTotal);
                        if (cwt > questProgress.TotalWeeklyQuestsCompleted) { questProgress.TotalWeeklyQuestsCompleted = cwt; needsSave = true; }
                    }
                    if (cloudStats.TryGetValue("total_xp_from_quests", out var cloudQuestXp))
                    {
                        var cqx = Convert.ToInt32(cloudQuestXp);
                        if (cqx > questProgress.TotalXPFromQuests) { questProgress.TotalXPFromQuests = cqx; needsSave = true; }
                    }

                    if (cloudStats.TryGetValue("daily_quests_completed_today", out var cloudDailyCompToday))
                    {
                        var cloudCount = Convert.ToInt32(cloudDailyCompToday);
                        bool cloudDateIsToday = false;
                        if (cloudStats.TryGetValue("daily_completion_reset_date", out var cloudResetDate))
                        {
                            if (DateTime.TryParse(cloudResetDate?.ToString(), out var resetDate))
                                cloudDateIsToday = resetDate.Date == DateTime.Today;
                        }
                        if (cloudDateIsToday && cloudCount > questProgress.GetDailyQuestsCompletedToday())
                        {
                            bool hasCompletionEvidence = questProgress.DailyQuestCompletionDates.Any(d => d.Date == DateTime.Today);
                            if (hasCompletionEvidence)
                            {
                                questProgress.DailyQuestsCompletedToday = cloudCount;
                                questProgress.DailyCompletionResetDate = DateTime.Today;
                                needsSave = true;
                            }
                        }
                    }

                    if (questProgress.DailyQuestCompletionDates.Any(d => d.Date == DateTime.Today)
                        && questProgress.GetDailyQuestsCompletedToday() == 0)
                    {
                        questProgress.DailyQuestsCompletedToday = 1;
                        questProgress.DailyCompletionResetDate = DateTime.Today;
                        needsSave = true;
                    }
                }
            }

            return needsSave;
        }

        /// <summary>
        /// Force-set local streak values from server (bypasses "take higher" logic).
        /// Used when admin has force-set streak values via /admin/set-streak.
        /// </summary>
        private void ApplyForceStreakOverride(V2StreakStats streakStats)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            App.Logger?.Information("Applying force streak override: streak={Streak}, date={Date}, daily={Daily}, weekly={Weekly}, xp={Xp}",
                streakStats.DailyQuestStreak, streakStats.LastDailyQuestDate,
                streakStats.TotalDailyQuestsCompleted, streakStats.TotalWeeklyQuestsCompleted,
                streakStats.TotalXPFromQuests);

            // Force-set streak (even if lower than local)
            settings.DailyQuestStreak = streakStats.DailyQuestStreak;

            // Force-set last daily quest date
            if (!string.IsNullOrEmpty(streakStats.LastDailyQuestDate) && DateTime.TryParse(streakStats.LastDailyQuestDate, out var parsedDate))
            {
                settings.LastDailyQuestDate = parsedDate.Date;
            }

            // Force-set completion dates
            var questProgress = App.Quests?.Progress;
            if (questProgress != null)
            {
                if (streakStats.QuestCompletionDates != null)
                {
                    questProgress.DailyQuestCompletionDates.Clear();
                    foreach (var ds in streakStats.QuestCompletionDates)
                    {
                        if (DateTime.TryParse(ds, out var d))
                            questProgress.DailyQuestCompletionDates.Add(d.Date);
                    }
                }

                // Force-set totals (even if lower)
                questProgress.TotalDailyQuestsCompleted = streakStats.TotalDailyQuestsCompleted;
                questProgress.TotalWeeklyQuestsCompleted = streakStats.TotalWeeklyQuestsCompleted;
                questProgress.TotalXPFromQuests = streakStats.TotalXPFromQuests;
            }

            App.Settings?.Save();
        }

        /// <summary>
        /// Force-reset all skills and refund points. Used when admin resets skills via /admin/reset-skills.
        /// </summary>
        private void ApplyForceSkillsReset(int? serverSkillPoints)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var refundedPoints = serverSkillPoints ?? (settings.PlayerLevel * SkillTreeService.PointsPerLevel);

            App.Logger?.Information("Applying force skills reset: clearing {Count} skills, setting points to {Points}",
                settings.UnlockedSkills?.Count ?? 0, refundedPoints);

            settings.UnlockedSkills = new List<string>();
            settings.SkillPoints = refundedPoints;
            App.Settings?.Save();
        }

        /// <summary>
        /// Spend one streak-fix charge ("Oopsie Insurance") via server-side validation.
        /// The spend itself is free: the server decrements the account's cumulable charge balance
        /// (oopsie_credits), records the fixed day and marks the season flag. No XP is deducted.
        /// </summary>
        /// <param name="fixDate">The date to fix, in YYYY-MM-DD format</param>
        /// <returns>Tuple of (success, error message, the account XP total the server echoed back,
        /// the account's remaining charge balance — null when an older server omits it)</returns>
        public async Task<(bool success, string? error, int? newXp, int? credits)> UseOopsieInsuranceAsync(string fixDate)
        {
            var unifiedId = App.Settings?.Current?.UnifiedId;
            if (string.IsNullOrEmpty(unifiedId))
            {
                return (false, "Oopsie Insurance requires a cloud account. Please log in first.", null, null);
            }

            try
            {
                var requestData = new { unified_id = unifiedId, fix_date = fixDate };
                var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/user/use-oopsie");
                AddAuthHeader(request);
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(requestData),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    await HandleUnauthorizedAsync(response);
                    var errorResult = JsonConvert.DeserializeObject<OopsieErrorResponse>(json);
                    var errorMsg = errorResult?.Error ?? $"Server error: {response.StatusCode}";
                    App.Logger?.Warning("Oopsie insurance failed: {Error}", errorMsg);
                    return (false, errorMsg, null, null);
                }

                var result = JsonConvert.DeserializeObject<OopsieSuccessResponse>(json);
                App.Logger?.Information("Oopsie insurance used via server: {Credits} charge(s) left (server XP echo = {NewXP})",
                    result?.OopsieCredits, result?.NewXp);
                return (true, null, result?.NewXp, result?.OopsieCredits);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Oopsie insurance request failed");
                return (false, $"Connection failed: {ex.Message}", null, null);
            }
        }

        /// <summary>
        /// Purchase a skill via server-authoritative endpoint.
        /// Server validates cost, prerequisites, and deducts points.
        /// Returns (success, error) — on success, updates local SkillPoints and UnlockedSkills from server response.
        /// </summary>
        public async Task<(bool success, string? error)> PurchaseSkillAsync(string skillId)
        {
            var settings = App.Settings?.Current;
            var unifiedId = settings?.UnifiedId;
            if (string.IsNullOrEmpty(unifiedId))
            {
                return (false, "Purchasing enhancements requires a cloud account. Please log in first.");
            }

            try
            {
                var requestBody = JsonConvert.SerializeObject(new
                {
                    unified_id = unifiedId,
                    skill_id = skillId,
                    // Send local points so server can reconcile (bubble pop points may not be synced yet)
                    skill_points = settings.SkillPoints
                });
                var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/user/purchase-skill");
                AddAuthHeader(request);
                request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    // On 401, attempt auth recovery and retry once — but ONLY if the session was
                    // genuinely recovered. HandleUnauthorizedAsync used to answer true for a failed
                    // recovery too, so this retried the identical POST with the identical dead
                    // token and burned a second round-trip to reach the same 401 (#879).
                    if (await HandleUnauthorizedAsync(response) && !string.IsNullOrEmpty(App.Settings?.Current?.AuthToken))
                    {
                        App.Logger?.Information("Skill purchase: retrying after auth token recovery");
                        var retryRequest = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/user/purchase-skill");
                        AddAuthHeader(retryRequest);
                        retryRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                        response = await _httpClient.SendAsync(retryRequest);
                        json = await response.Content.ReadAsStringAsync();
                    }
                }

                if (!response.IsSuccessStatusCode)
                {
                    // Show user-friendly message for auth failures instead of raw server error
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        App.Logger?.Warning("Skill purchase failed: auth token invalid/missing after recovery attempt");
                        // Point at a sign-in the user can actually reach. Since the restructure the
                        // always-present login surface is Settings ▸ Account (Phase 2 gave it a real
                        // page; the old ⭐ Exclusives advice from #879 predates the Play-door fold).
                        // The door NAME comes from the same loc key the rail renders, so the
                        // instruction matches what the user is looking at in their language.
                        var settingsDoorName = Localization.Loc.Get("nav_door_settings");
                        return (false, $"Your session has expired. Open ⚙️ {settingsDoorName} → Account and sign in again to purchase skills.");
                    }

                    string errorMsg;
                    try
                    {
                        var errorResult = JsonConvert.DeserializeObject<PurchaseSkillResponse>(json);
                        errorMsg = errorResult?.Error ?? $"Server error: {response.StatusCode}";
                        // Don't overwrite local points from error responses — server may return 0
                        // for users whose points weren't properly backfilled. Let sync handle reconciliation.
                    }
                    catch
                    {
                        errorMsg = $"Server error: {response.StatusCode}";
                    }
                    App.Logger?.Warning("Skill purchase failed: {Error}", errorMsg);
                    return (false, errorMsg);
                }

                var result = JsonConvert.DeserializeObject<PurchaseSkillResponse>(json);
                if (result == null)
                    return (false, "Invalid server response");

                if (!result.Success)
                {
                    // Don't overwrite local points on failed purchase — server may have stale/missing
                    // point data for users who leveled before server-authoritative system was deployed.
                    // Sync endpoint handles proper reconciliation with backfill.
                    App.Logger?.Warning("Skill purchase rejected: {Error}, server says {Points} points",
                        result.Error, result.SkillPoints);
                    return (false, result.Error ?? "Purchase failed");
                }

                // Apply server's authoritative values
                if (result.SkillPoints.HasValue)
                    settings.SkillPoints = result.SkillPoints.Value;
                if (result.UnlockedSkills != null)
                {
                    // Merge: take union to never lose skills
                    var merged = new HashSet<string>(settings.UnlockedSkills ?? new List<string>());
                    foreach (var skill in result.UnlockedSkills)
                        merged.Add(skill);
                    settings.UnlockedSkills = merged.ToList();
                }

                // Prestige: count the spend locally, then adopt the server total when it's
                // ahead (it already includes this purchase, so this never double-counts —
                // reconcile only raises). Also feed the season's spend bucket for the recap.
                var purchasedSkill = Models.SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
                if (purchasedSkill != null)
                {
                    App.Achievements?.TrackSkillPointsSpent(purchasedSkill.Cost);
                    SeasonRecapService.TrackPointsSpent(purchasedSkill.Cost);
                }
                if (result.LifetimePointsSpent.HasValue)
                    App.Achievements?.ReconcileLifetimePointsSpent(result.LifetimePointsSpent.Value);

                App.Settings?.Save();

                App.Logger?.Information("Skill purchased via server: {SkillId}, {Points} points remaining",
                    skillId, settings.SkillPoints);
                return (true, null);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Skill purchase request failed");
                return (false, "Connection failed. Please check your internet connection.");
            }
        }

        /// <summary>
        /// Change the user's display name via server-side validation.
        /// Name must be unique (case-insensitive). Case-only changes are allowed.
        /// </summary>
        public async Task<(bool success, string? error, string? newName)> ChangeDisplayNameAsync(string newName)
        {
            var unifiedId = App.Settings?.Current?.UnifiedId;
            if (string.IsNullOrEmpty(unifiedId))
            {
                return (false, "You must be logged in to change your name", null);
            }

            try
            {
                var requestData = new { unified_id = unifiedId, new_display_name = newName };
                var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/user/change-display-name");
                AddAuthHeader(request);
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(requestData),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    await HandleUnauthorizedAsync(response);
                    var errorResult = JsonConvert.DeserializeObject<ChangeDisplayNameErrorResponse>(json);
                    var errorMsg = errorResult?.Error ?? $"Server error: {response.StatusCode}";
                    App.Logger?.Warning("Change display name failed: {Error}", errorMsg);
                    return (false, errorMsg, null);
                }

                var result = JsonConvert.DeserializeObject<ChangeDisplayNameResponse>(json);
                App.Logger?.Information("Display name changed to: {NewName}", result?.NewDisplayName);
                return (true, null, result?.NewDisplayName);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Change display name request failed");
                return (false, "Name change requires an internet connection", null);
            }
        }

        /// <summary>
        /// Delete the user's account and all server-side data (GDPR).
        /// Requires confirmation string "DELETE".
        /// </summary>
        public async Task<(bool success, string? error)> DeleteAccountAsync()
        {
            var unifiedId = App.Settings?.Current?.UnifiedId;
            if (string.IsNullOrEmpty(unifiedId))
            {
                return (false, "You must be logged in to delete your account");
            }

            try
            {
                var requestData = new { unified_id = unifiedId, confirmation = "DELETE" };
                var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/user/delete-account");
                AddAuthHeader(request);
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(requestData),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    await HandleUnauthorizedAsync(response);
                    var errorResult = JsonConvert.DeserializeObject<DeleteAccountErrorResponse>(json);
                    var errorMsg = errorResult?.Error ?? $"Server error: {response.StatusCode}";
                    App.Logger?.Warning("Delete account failed: {Error}", errorMsg);
                    return (false, errorMsg);
                }

                var result = JsonConvert.DeserializeObject<DeleteAccountResponse>(json);
                App.Logger?.Information("Account deleted: {UnifiedId} ({Name})", result?.DeletedUnifiedId, result?.DeletedDisplayName);
                return (true, null);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Delete account request failed");
                return (false, "Account deletion requires an internet connection");
            }
        }

        /// <summary>
        /// Export all user data from the server (GDPR data access request).
        /// Returns the raw JSON string for saving to file.
        /// </summary>
        public async Task<(bool success, string? error, string? jsonData)> ExportDataAsync()
        {
            var unifiedId = App.Settings?.Current?.UnifiedId;
            if (string.IsNullOrEmpty(unifiedId))
            {
                return (false, "You must be logged in to export your data", null);
            }

            try
            {
                var requestData = new { unified_id = unifiedId };
                var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/user/export-data");
                AddAuthHeader(request);
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(requestData),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    await HandleUnauthorizedAsync(response);
                    var errorResult = JsonConvert.DeserializeObject<DeleteAccountErrorResponse>(json);
                    var errorMsg = errorResult?.Error ?? $"Server error: {response.StatusCode}";
                    App.Logger?.Warning("Export data failed: {Error}", errorMsg);
                    return (false, errorMsg, null);
                }

                // Pretty-print the JSON for readability
                var parsed = Newtonsoft.Json.Linq.JToken.Parse(json);
                var prettyJson = parsed.ToString(Formatting.Indented);

                App.Logger?.Information("Data exported for user: {UnifiedId}", unifiedId);
                return (true, null, prettyJson);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Export data request failed");
                return (false, "Data export requires an internet connection", null);
            }
        }

        /// <summary>
        /// Adds the X-Auth-Token header to a V2 API request if an auth token is available.
        /// </summary>
        private static void AddAuthHeader(HttpRequestMessage request)
        {
            var token = App.Settings?.Current?.AuthToken;
            if (!string.IsNullOrEmpty(token))
                request.Headers.Add("X-Auth-Token", token);
        }

        /// <summary>
        /// Handles a 401 Unauthorized response. Attempts token recovery via restore-session with a
        /// 5-minute cooldown between attempts. The token is preserved on failure.
        ///
        /// Returns TRUE only when the session was actually recovered and it is safe to carry on
        /// with the request that 401'd. It used to return true for any 401 - including "recovery
        /// failed" and "still inside the cooldown, so recovery was never even attempted" - while
        /// every caller reads the result as "recovered, proceed": the skill purchase retried the
        /// POST with the same dead token, and the heartbeat's stop-on-failure branch keyed off the
        /// same true. Failure and success have to be distinguishable (#879).
        ///
        /// Note the asymmetry: a non-401 response also returns false, because "there was nothing
        /// to recover from" is likewise not "a session was recovered". Callers that need to know
        /// whether the response WAS a 401 must check <c>response.StatusCode</c> themselves.
        /// </summary>
        private async Task<bool> HandleUnauthorizedAsync(HttpResponseMessage response)
        {
            if (response.StatusCode != HttpStatusCode.Unauthorized)
                return false;

            // Attempt recovery with a 5-minute cooldown to prevent concurrent 401s from spam-recovering
            // while still allowing retry if a transient server issue resolves later.
            if (DateTime.Now - _lastAuthRecoveryAttempt > TimeSpan.FromMinutes(5))
            {
                _lastAuthRecoveryAttempt = DateTime.Now;
                App.Logger?.Information("[Auth] 401 received — attempting token recovery via restore-session");
                var recovered = await TryRecoverAuthTokenAsync();
                if (recovered)
                {
                    App.Logger?.Information("[Auth] Token recovered successfully");
                    StartHeartbeat();
                    return true;
                }
            }

            // Don't clear the auth token — it may still be valid for other endpoints or after
            // a transient server issue. The 5-minute cooldown prevents recovery spam.
            App.Logger?.Warning("[Auth] 401 — recovery failed or on cooldown, token kept for retry");
            return false;
        }

        /// <summary>
        /// Attempts to recover the auth token by calling /v2/auth/restore-session.
        /// Returns true if the token was successfully recovered.
        /// Must NOT call HandleUnauthorizedAsync on the response (would recurse).
        /// </summary>
        private async Task<bool> TryRecoverAuthTokenAsync()
        {
            try
            {
                var unifiedId = App.Settings?.Current?.UnifiedId;
                var storedToken = App.Settings?.Current?.AuthToken;
                if (string.IsNullOrEmpty(unifiedId) || string.IsNullOrEmpty(storedToken))
                    return false;

                var body = JsonConvert.SerializeObject(new
                {
                    unified_id = unifiedId,
                    client_version = UpdateService.AppVersion
                });
                var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/auth/restore-session");
                request.Headers.Add("X-Auth-Token", storedToken);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    App.Logger?.Warning("[Auth] restore-session failed: {Status}", response.StatusCode);
                    return false;
                }

                // restore-session succeeded — the token is still valid on the server.
                // The original 401 was transient. Server does NOT return a new auth_token
                // (rotation during restore-session causes race conditions), so we keep
                // the existing token. If the response does include a new token, adopt it.
                var json = await response.Content.ReadAsStringAsync();
                var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
                var newToken = obj["auth_token"]?.ToString();
                if (!string.IsNullOrEmpty(newToken) && App.Settings?.Current != null)
                {
                    App.Settings.Current.AuthToken = newToken;
                    App.Settings.Save(suppressCloudBackup: true);
                    App.Logger?.Information("[Auth] Auth token refreshed from restore-session");
                }
                else
                {
                    App.Logger?.Information("[Auth] restore-session confirmed token is still valid (transient 401)");
                }
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("[Auth] restore-session recovery failed: {Error}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Signs an HTTP request with HMAC-SHA256 for anti-cheat verification.
        /// Adds X-CCP-Timestamp and X-CCP-Signature headers.
        /// </summary>
        private static void SignRequest(HttpRequestMessage request, string body)
        {
            var unifiedId = App.Settings?.Current?.UnifiedId;
            if (string.IsNullOrEmpty(unifiedId)) return;

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var payload = $"{timestamp}:{body}";

            // Key derived from unified_id + embedded app key
            const string appKey = "ccp-anticheat-2026";
            var keyMaterial = $"{unifiedId}:{appKey}";
            var keyBytes = Encoding.UTF8.GetBytes(keyMaterial);

            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var signature = Convert.ToHexString(hash).ToLowerInvariant();

            request.Headers.Add("X-CCP-Timestamp", timestamp);
            request.Headers.Add("X-CCP-Signature", signature);
        }

        #region Settings Backup/Restore

        private long _lastSettingsBackupTicks = 0;
        private static readonly long SettingsBackupDebounceTicks = TimeSpan.FromMinutes(5).Ticks;

        /// <summary>
        /// Properties to exclude from settings backup (server-authoritative or identity fields).
        /// </summary>
        private static readonly HashSet<string> ExcludedBackupProperties = new(StringComparer.OrdinalIgnoreCase)
        {
            nameof(AppSettings.UnifiedId),
            nameof(AppSettings.OpenRouterApiKey),
            nameof(AppSettings.PlayerLevel),
            nameof(AppSettings.PlayerXP),
            nameof(AppSettings.SkillPoints),
            nameof(AppSettings.UnlockedSkills),
            nameof(AppSettings.HighestLevelEver),
            nameof(AppSettings.IsSeason0Og),
            nameof(AppSettings.CurrentSeason),
            nameof(AppSettings.PendingSkillsResetAck),
            nameof(AppSettings.UserDisplayName),
            nameof(AppSettings.PatreonTier),
            nameof(AppSettings.PatreonPremiumValidUntil),
            nameof(AppSettings.LastPatreonVerification),
            nameof(AppSettings.AuthToken),
            nameof(AppSettings.CustomAssetsPath),
            nameof(AppSettings.DiscordWebhookUrl),
            nameof(AppSettings.LastSeenUtc), // Local-only greeting timestamp — must never leave the device.
        };

        /// <summary>
        /// Backup current settings to the cloud. Debounced to 5 minutes unless forced.
        /// </summary>
        public async Task<bool> BackupSettingsAsync(bool force = false)
        {
            if (App.Settings?.Current?.OfflineMode == true) return false;

            var unifiedId = App.Settings?.Current?.UnifiedId;
            if (string.IsNullOrEmpty(unifiedId)) return false;

            // Settings recovered from a rolling daily backup can be up to three days stale. An
            // automatic upload here would overwrite the cloud copy — the one snapshot that still
            // holds the pre-corruption state — before the sync reconcile has run (#761). A forced
            // (user-initiated) backup still goes through; that is an explicit choice.
            if (!force && App.Settings?.RestoredFromBackup == true)
            {
                App.Logger?.Debug("Settings cloud backup skipped — local settings came from a daily backup and have not been reconciled yet");
                return false;
            }

            // Debounce: skip if backed up recently (unless forced)
            // Uses Interlocked for thread safety — multiple async paths can call this concurrently
            var nowTicks = DateTime.UtcNow.Ticks;
            if (force)
            {
                // Forced backup (user-initiated): skip debounce, just stamp the time
                Interlocked.Exchange(ref _lastSettingsBackupTicks, nowTicks);
            }
            else
            {
                var lastTicks = Interlocked.Read(ref _lastSettingsBackupTicks);
                if ((nowTicks - lastTicks) < SettingsBackupDebounceTicks)
                {
                    App.Logger?.Debug("Settings backup skipped (debounce, last backup {Ago}s ago)",
                        (nowTicks - lastTicks) / TimeSpan.TicksPerSecond);
                    return false;
                }

                // Atomically claim this backup slot — if another thread won the race, bail out.
                // Set timestamp BEFORE the HTTP call to prevent concurrent/retry storms.
                if (Interlocked.CompareExchange(ref _lastSettingsBackupTicks, nowTicks, lastTicks) != lastTicks)
                {
                    App.Logger?.Debug("Settings backup skipped (another thread claimed the slot)");
                    return false;
                }
            }

            try
            {
                var settings = App.Settings?.Current;
                if (settings == null) return false;

                // Bail early if no auth token — request would just 401
                var authToken = settings.AuthToken;
                if (string.IsNullOrEmpty(authToken))
                {
                    App.Logger?.Debug("Settings backup skipped (no auth token)");
                    return false;
                }

                // Serialize settings, then strip excluded properties
                var fullJson = JsonConvert.SerializeObject(settings, Formatting.None);
                var obj = Newtonsoft.Json.Linq.JObject.Parse(fullJson);

                foreach (var prop in ExcludedBackupProperties)
                {
                    // Remove by JSON property name (which may differ from C# property name)
                    // Find the matching key case-insensitively
                    var key = obj.Properties()
                        .FirstOrDefault(p => string.Equals(p.Name, prop, StringComparison.OrdinalIgnoreCase))?.Name;
                    if (key != null) obj.Remove(key);
                }

                var strippedJson = obj.ToString(Formatting.None);

                // Gzip compress
                byte[] compressedBytes;
                using (var output = new MemoryStream())
                {
                    using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                    {
                        var jsonBytes = Encoding.UTF8.GetBytes(strippedJson);
                        await gzip.WriteAsync(jsonBytes, 0, jsonBytes.Length);
                    }
                    compressedBytes = output.ToArray();
                }

                var base64Data = Convert.ToBase64String(compressedBytes);

                var requestData = new
                {
                    unified_id = unifiedId,
                    settings_data = base64Data,
                    app_version = UpdateService.AppVersion
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/user/backup-settings");
                AddAuthHeader(request);
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(requestData),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    await HandleUnauthorizedAsync(response);
                    var error = await response.Content.ReadAsStringAsync();
                    App.Logger?.Warning("Settings backup failed: {Status} - {Error}", response.StatusCode, error);
                    return false;
                }

                App.Logger?.Information("Settings backed up to cloud ({Size} bytes compressed)", compressedBytes.Length);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Settings backup failed");
                return false;
            }
        }

        /// <summary>
        /// Check if a settings backup exists in the cloud and return its metadata.
        /// </summary>
        public async Task<SettingsBackupInfo?> GetSettingsBackupInfoAsync()
        {
            var unifiedId = App.Settings?.Current?.UnifiedId;
            if (string.IsNullOrEmpty(unifiedId)) return null;

            try
            {
                var requestData = new { unified_id = unifiedId };
                var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/user/settings-backup");
                AddAuthHeader(request);
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(requestData),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    await HandleUnauthorizedAsync(response);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<SettingsBackupResponse>(json);

                if (result?.Backup == null) return null;

                return new SettingsBackupInfo
                {
                    AppVersion = result.Backup.AppVersion,
                    BackedUpAt = DateTime.TryParse(result.Backup.BackedUpAt, out var dt) ? dt : null,
                    SizeBytes = result.Backup.SizeBytes
                };
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Settings backup info check failed: {Error}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Download and decompress settings from the cloud.
        /// Returns deserialized AppSettings (with excluded properties at their defaults), or null on failure.
        /// </summary>
        public async Task<AppSettings?> RestoreSettingsFromCloudAsync()
        {
            var unifiedId = App.Settings?.Current?.UnifiedId;
            if (string.IsNullOrEmpty(unifiedId)) return null;

            try
            {
                var requestData = new { unified_id = unifiedId };
                var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/user/settings-backup");
                AddAuthHeader(request);
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(requestData),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    await HandleUnauthorizedAsync(response);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<SettingsBackupResponse>(json);

                if (result?.Backup?.SettingsData == null) return null;

                // Decompress: base64 → gzip → JSON
                var compressedBytes = Convert.FromBase64String(result.Backup.SettingsData);
                string settingsJson;
                using (var input = new MemoryStream(compressedBytes))
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var reader = new StreamReader(gzip, Encoding.UTF8))
                {
                    settingsJson = await reader.ReadToEndAsync();
                }

                var serializerSettings = new JsonSerializerSettings
                {
                    ObjectCreationHandling = ObjectCreationHandling.Replace
                };
                var restored = JsonConvert.DeserializeObject<AppSettings>(settingsJson, serializerSettings);

                App.Logger?.Information("Settings restored from cloud (v{Version}, {Size} bytes)",
                    result.Backup.AppVersion, result.Backup.SizeBytes);

                return restored;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Settings restore from cloud failed");
                return null;
            }
        }

        /// <summary>
        /// Records that the current user found the easter egg and returns the total reader count.
        /// If logged in: adds user to the unique readers set and returns count.
        /// If not logged in: returns count only (read-only).
        /// Returns -1 on failure.
        /// </summary>
        public async Task<int> RecordEasterEggReadAsync()
        {
            try
            {
                var unifiedId = App.Settings?.Current?.UnifiedId;

                var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/easter-egg");

                if (!string.IsNullOrEmpty(unifiedId))
                {
                    AddAuthHeader(request);
                    request.Content = new StringContent(
                        JsonConvert.SerializeObject(new { unified_id = unifiedId }),
                        Encoding.UTF8,
                        "application/json"
                    );
                }
                else
                {
                    request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                }

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    App.Logger?.Warning("Easter egg endpoint returned {Status}", response.StatusCode);
                    return -1;
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<EasterEggResponse>(json);
                return result?.Count ?? -1;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Easter egg request failed");
                return -1;
            }
        }

        #endregion

        #region The Descent — migration handshake (CONTRACTS-0812 §2)

        /// <summary>
        /// Settle a submit. THE ACK IS THE ONLY THING THAT MAY WRITE
        /// <see cref="AppSettings.DescentMigrationCompleted"/> — this method is the one place that
        /// touches it, and it is deliberately the mirror image of the web-XP claim handshake a few
        /// hundred lines up.
        ///
        /// <para>The lopsidedness is the point. The client applies its half of the migration
        /// BEFORE the submit (it has to — the ledger it sends must be denominated in the curve it
        /// claims), and marks itself done only when the server says so. Crash in the gap and the
        /// server is still unmigrated, so it re-offers; the pending choice is still on disk, so
        /// the very next sync re-submits it; and the server treats a repeat submit as a silent
        /// no-op. Nothing is lost in any ordering, because both choices are pure functions of a
        /// lifetime XP total that never moves.</para>
        /// </summary>
        private static void HandleDescentMigrationAck(AppSettings settings, V2DescentMigration? block)
        {
            if (block?.Completed != true) return;
            if (settings.DescentMigrationCompleted) return;   // already settled; idempotent

            // Prefer the server's echo of the choice; fall back to what we submitted. They can
            // only differ if the account migrated on another device, and the server's word wins.
            var choice = DescentMigrationChoices.IsValid(block.Choice)
                ? block.Choice
                : settings.PendingDescentMigrationChoice;

            settings.DescentMigrationCompleted = true;
            settings.DescentMigrationChoice = choice;
            settings.PendingDescentMigrationChoice = null;
            App.Settings?.Save();

            App.Logger?.Information("[Descent] Migration ACKNOWLEDGED by server (choice={Choice}). Curve v2 is now this account's curve, permanently.",
                choice ?? "unknown");
        }

        /// <summary>
        /// Open the ceremony when the server offers it. Every condition here is a reason NOT to:
        /// the block has to be present, it has to say required, the account must not already be
        /// migrated, and there must be no choice already made and waiting to land.
        ///
        /// <para>That last one is what stops the ceremony re-opening in front of a user who has
        /// already chosen but whose ack has not arrived — the server will keep saying "required"
        /// until the submit lands, and asking a one-way question twice is the one thing this
        /// ceremony must never do.</para>
        /// </summary>
        private static void HandleDescentMigrationOffer(AppSettings settings, V2DescentMigration? block)
        {
            if (block?.Required != true) return;
            if (settings.DescentMigrationCompleted) return;
            if (DescentMigrationChoices.IsValid(settings.PendingDescentMigrationChoice))
            {
                App.Logger?.Debug("[Descent] Offer re-sent but a choice is already pending server ack — not re-opening the ceremony.");
                return;
            }

            var offer = new DescentMigrationOffer
            {
                TotalXpEarned = block.TotalXpEarned ?? 0,
                DevotionDays = block.DevotionDays ?? 0
            };

            App.Logger?.Information("[Descent] Server is offering the migration ceremony (lifetime {Xp} XP, {Days} devotion days).",
                (int)offer.TotalXpEarned, offer.DevotionDays);

            App.DescentMigration?.OfferReceived(offer);
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopHeartbeat();
            _httpClient.Dispose();
        }

        #region DTOs

        private class EasterEggResponse
        {
            [JsonProperty("count")]
            public int Count { get; set; }
        }

        private class ProfileResponse
        {
            [JsonProperty("exists")]
            public bool Exists { get; set; }

            [JsonProperty("user_id")]
            public string? UserId { get; set; }

            [JsonProperty("profile")]
            public CloudProfile? Profile { get; set; }
        }

        private class SyncResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("user_id")]
            public string? UserId { get; set; }

            [JsonProperty("profile")]
            public CloudProfile? Profile { get; set; }

            [JsonProperty("merged")]
            public bool Merged { get; set; }
        }

        private class CloudProfile
        {
            [JsonProperty("xp")]
            public int Xp { get; set; }

            [JsonProperty("level")]
            public int Level { get; set; }

            [JsonProperty("achievements")]
            public List<string>? Achievements { get; set; }

            [JsonProperty("stats")]
            public Dictionary<string, object>? Stats { get; set; }

            [JsonProperty("last_session")]
            public string? LastSession { get; set; }

            [JsonProperty("updated_at")]
            public string? UpdatedAt { get; set; }

            [JsonProperty("skill_points")]
            public int? SkillPoints { get; set; }

            [JsonProperty("unlocked_skills")]
            public List<string>? UnlockedSkills { get; set; }

            [JsonProperty("total_conditioning_minutes")]
            public double? TotalConditioningMinutes { get; set; }

            [JsonProperty("companion_progress")]
            public Dictionary<string, Models.CompanionProgress>? CompanionProgress { get; set; }

            [JsonProperty("reset_weekly_quest")]
            public bool? ResetWeeklyQuest { get; set; }

            [JsonProperty("reset_daily_quest")]
            public bool? ResetDailyQuest { get; set; }

            [JsonProperty("force_streak_override")]
            public bool? ForceStreakOverride { get; set; }

            /// <summary>
            /// Trainer Card customization echoed by /user/profile and /user/sync. Absent on any
            /// server that predates Phase 2, which is exactly why it is nullable and why
            /// <see cref="AdoptCloudCosmetics"/> only ever fills an EMPTY local loadout.
            /// </summary>
            [JsonProperty("cosmetics")]
            public Models.ProfileCosmetics? Cosmetics { get; set; }
        }

        private class ProfileSyncData
        {
            [JsonProperty("xp")]
            public int Xp { get; set; }

            [JsonProperty("level")]
            public int Level { get; set; }

            [JsonProperty("achievements")]
            public List<string>? Achievements { get; set; }

            [JsonProperty("stats")]
            public Dictionary<string, object>? Stats { get; set; }

            [JsonProperty("last_session")]
            public string? LastSession { get; set; }

            [JsonProperty("allow_discord_dm")]
            public bool AllowDiscordDm { get; set; }

            [JsonProperty("share_profile_picture")]
            public bool ShareProfilePicture { get; set; }

            [JsonProperty("show_online_status")]
            public bool ShowOnlineStatus { get; set; } = true;

            // Goon Game consent flags (GOON_DISCORD_CONTRACT §2) — sharer-only, default off.
            // No goon_rich_presence here on purpose: that flag is local-only.
            [JsonProperty("goon_share_avatar")]
            public bool GoonShareAvatar { get; set; }

            [JsonProperty("goon_share_dm")]
            public bool GoonShareDm { get; set; }

            [JsonProperty("discord_id")]
            public string? DiscordId { get; set; }

            [JsonProperty("avatar_url")]
            public string? AvatarUrl { get; set; }

            [JsonProperty("skill_points")]
            public int SkillPoints { get; set; }

            [JsonProperty("unlocked_skills")]
            public List<string>? UnlockedSkills { get; set; }

            [JsonProperty("total_conditioning_minutes")]
            public double TotalConditioningMinutes { get; set; }

            [JsonProperty("reset_weekly_quest")]
            public bool ResetWeeklyQuest { get; set; }

            [JsonProperty("reset_daily_quest")]
            public bool ResetDailyQuest { get; set; }

            [JsonProperty("force_streak_override")]
            public bool ForceStreakOverride { get; set; }

            /// <summary>Trainer Card customization (Profile redesign Phase 2).</summary>
            [JsonProperty("cosmetics")]
            public Models.ProfileCosmetics? Cosmetics { get; set; }
        }

        private class V2SyncResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("reset_weekly_quest")]
            public bool? ResetWeeklyQuest { get; set; }

            [JsonProperty("reset_daily_quest")]
            public bool? ResetDailyQuest { get; set; }

            [JsonProperty("force_streak_override")]
            public bool? ForceStreakOverride { get; set; }

            [JsonProperty("streak_stats")]
            public V2StreakStats? StreakStats { get; set; }

            [JsonProperty("force_skills_reset")]
            public bool? ForceSkillsReset { get; set; }

            [JsonProperty("skill_points")]
            public int? SkillPoints { get; set; }

            [JsonProperty("unlocked_skills")]
            public List<string>? UnlockedSkills { get; set; }

            [JsonProperty("oopsie_used_season")]
            public string? OopsieUsedSeason { get; set; }

            [JsonProperty("oopsie_credits")]
            public int? OopsieCredits { get; set; }

            [JsonProperty("is_season0_og")]
            public bool? IsSeason0Og { get; set; }

            [JsonProperty("patreon_is_whitelisted")]
            public bool? PatreonIsWhitelisted { get; set; }

            [JsonProperty("bonus_daily_rerolls")]
            public int? BonusDailyRerolls { get; set; }

            [JsonProperty("bonus_weekly_rerolls")]
            public int? BonusWeeklyRerolls { get; set; }

            /// <summary>Trainer Card customization echoed back by /v2/user/sync (Phase 2).</summary>
            [JsonProperty("cosmetics")]
            public Models.ProfileCosmetics? Cosmetics { get; set; }

            [JsonProperty("level_reset")]
            public bool? LevelReset { get; set; }

            [JsonProperty("lifetime_points_spent")]
            public long? LifetimePointsSpent { get; set; }

            [JsonProperty("total_xp_earned")]
            public double? TotalXpEarned { get; set; }

            [JsonProperty("total_conditioning_minutes")]
            public double? TotalConditioningMinutes { get; set; }

            [JsonProperty("companion_progress")]
            public Dictionary<string, Models.CompanionProgress>? CompanionProgress { get; set; }

            /// <summary>
            /// Web XP the server has minted for verified web activity, plus at most one claim to
            /// hand over. Absent entirely while the server-side flag is off — nullable for exactly
            /// that reason, and the claim handler treats absence as "nothing to do".
            /// </summary>
            [JsonProperty("web_xp")]
            public V2WebXp? WebXp { get; set; }

            /// <summary>
            /// The Descent migration handshake, offer AND ack on the same key (CONTRACTS §2).
            /// Absent unless the server has DESCENT_MIGRATION armed, which is why it is nullable
            /// and why every reader treats absence as "there is no ceremony".
            /// </summary>
            [JsonProperty("descent_migration")]
            public V2DescentMigration? DescentMigration { get; set; }

            [JsonProperty("user")]
            public V2SyncUser? User { get; set; }
        }

        /// <summary>
        /// One shape, two directions. On an ordinary sync the server may fill
        /// <see cref="Required"/> + the two figures (the OFFER); on the response to a submit it
        /// fills <see cref="Completed"/> + <see cref="Choice"/> (the ACK). Nullable throughout:
        /// a missing field is never a zero, it is a server that did not speak.
        /// </summary>
        private class V2DescentMigration
        {
            /// <summary>The offer. True = this account has not migrated and the flag is on.</summary>
            [JsonProperty("required")]
            public bool? Required { get; set; }

            /// <summary>Lifetime XP the SERVER holds — the sole input to the relevel (§2.5).</summary>
            [JsonProperty("total_xp_earned")]
            public double? TotalXpEarned { get; set; }

            /// <summary>Server-side devotion days, already backfilled. Display only.</summary>
            [JsonProperty("devotion_days")]
            public int? DevotionDays { get; set; }

            /// <summary>The ack. The ONLY thing that may mark this client migrated (§2.4).</summary>
            [JsonProperty("completed")]
            public bool? Completed { get; set; }

            /// <summary>The choice the server recorded: "restore" or "cycle".</summary>
            [JsonProperty("choice")]
            public string? Choice { get; set; }
        }

        private class V2WebXp
        {
            /// <summary>XP minted but not yet handed to this client.</summary>
            [JsonProperty("pending")]
            public int Pending { get; set; }

            /// <summary>Lifetime web XP for this account (informational — never applied directly).</summary>
            [JsonProperty("total")]
            public long Total { get; set; }

            /// <summary>The one claim on offer, or null when there is nothing to settle.</summary>
            [JsonProperty("claim")]
            public V2WebXpClaim? Claim { get; set; }
        }

        private class V2WebXpClaim
        {
            /// <summary>Idempotency key — persisted locally once applied and echoed back as the ack.</summary>
            [JsonProperty("id")]
            public string? Id { get; set; }

            [JsonProperty("amount")]
            public int Amount { get; set; }
        }

        private class V2SyncUser
        {
            [JsonProperty("display_name")]
            public string? DisplayName { get; set; }

            [JsonProperty("level")]
            public int Level { get; set; }

            [JsonProperty("xp")]
            public int Xp { get; set; }

            [JsonProperty("highest_level_ever")]
            public int? HighestLevelEver { get; set; }

            // The sync endpoint is what PERFORMS the season rollover, so it is the first thing
            // that knows the new key — but it used to omit it from its response projection while
            // the auth projections included it, leaving sync-only clients permanently on the old
            // season. Null on servers older than that fix; ShouldAdoptServerSeason ignores null.
            [JsonProperty("current_season")]
            public string? CurrentSeason { get; set; }

            [JsonProperty("achievements")]
            public List<string>? Achievements { get; set; }

            [JsonProperty("stats")]
            public Dictionary<string, object>? Stats { get; set; }
        }

        private class OopsieSuccessResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("new_xp")]
            public int NewXp { get; set; }

            [JsonProperty("oopsie_used_season")]
            public string? OopsieUsedSeason { get; set; }

            [JsonProperty("oopsie_credits")]
            public int? OopsieCredits { get; set; }
        }

        private class OopsieErrorResponse
        {
            [JsonProperty("error")]
            public string? Error { get; set; }
        }

        private class PurchaseSkillResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("error")]
            public string? Error { get; set; }

            [JsonProperty("skill_points")]
            public int? SkillPoints { get; set; }

            [JsonProperty("unlocked_skills")]
            public List<string>? UnlockedSkills { get; set; }

            [JsonProperty("lifetime_points_spent")]
            public long? LifetimePointsSpent { get; set; }
        }

        private class ChangeDisplayNameResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("new_display_name")]
            public string? NewDisplayName { get; set; }
        }

        private class ChangeDisplayNameErrorResponse
        {
            [JsonProperty("error")]
            public string? Error { get; set; }
        }

        private class DeleteAccountResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("deleted_unified_id")]
            public string? DeletedUnifiedId { get; set; }

            [JsonProperty("deleted_display_name")]
            public string? DeletedDisplayName { get; set; }
        }

        private class DeleteAccountErrorResponse
        {
            [JsonProperty("error")]
            public string? Error { get; set; }
        }

        private class SettingsBackupResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("backup")]
            public SettingsBackupData? Backup { get; set; }
        }

        private class SettingsBackupData
        {
            [JsonProperty("settings_data")]
            public string? SettingsData { get; set; }

            [JsonProperty("app_version")]
            public string? AppVersion { get; set; }

            [JsonProperty("backed_up_at")]
            public string? BackedUpAt { get; set; }

            [JsonProperty("size_bytes")]
            public int SizeBytes { get; set; }
        }

        private class V2StreakStats
        {
            [JsonProperty("daily_quest_streak")]
            public int DailyQuestStreak { get; set; }

            [JsonProperty("last_daily_quest_date")]
            public string? LastDailyQuestDate { get; set; }

            [JsonProperty("quest_completion_dates")]
            public List<string>? QuestCompletionDates { get; set; }

            [JsonProperty("total_daily_quests_completed")]
            public int TotalDailyQuestsCompleted { get; set; }

            [JsonProperty("total_weekly_quests_completed")]
            public int TotalWeeklyQuestsCompleted { get; set; }

            [JsonProperty("total_xp_from_quests")]
            public int TotalXPFromQuests { get; set; }
        }

        #endregion
    }

    /// <summary>
    /// Public metadata about a cloud settings backup (for UI display).
    /// </summary>
    public class SettingsBackupInfo
    {
        public string? AppVersion { get; set; }
        public DateTime? BackedUpAt { get; set; }
        public int SizeBytes { get; set; }
    }
}
