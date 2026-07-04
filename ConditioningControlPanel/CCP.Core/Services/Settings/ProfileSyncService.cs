using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Services;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Core.Services.Settings;

/// <summary>
/// Cross-platform implementation of <see cref="IProfileSyncService"/>. Owns its own
/// <see cref="HttpClient"/> and reaches the server at <see cref="ProxyBaseUrl"/> using the
/// per-user <c>X-Auth-Token</c> (plus HMAC anti-cheat signing on the sync push).
///
/// Ported slice-by-slice from the WPF <c>ProfileSyncService</c>; see
/// <c>docs/profilesync-port-plan.md</c>. This slice (1) lands only the seam, DTOs, and the
/// pure plumbing helpers (auth header, request signing, disposal). The async members remain
/// inherited default-interface no-ops until the slices noted on <see cref="IProfileSyncService"/>.
///
/// SECURITY: the auth token is never logged. It is read transparently (decrypted) from
/// <c>ISettingsService.Current.AuthToken</c> and attached only as a request header.
/// </summary>
public sealed class ProfileSyncService : IProfileSyncService, IDisposable
{
    /// <summary>Server base URL (WPF const, ProfileSyncService.cs:25).</summary>
    private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

    /// <summary>Minimum interval between sync round-trips (WPF <c>SyncCooldown</c>, ~line 415).</summary>
    private static readonly TimeSpan SyncCooldown = TimeSpan.FromSeconds(30);

    /// <summary>Heartbeat cadence (WPF <c>HeartbeatIntervalSeconds</c>, ProfileSyncService.cs:26).</summary>
    private const int HeartbeatIntervalSeconds = 120;

    /// <summary>
    /// Skill points refunded per level on a force-skills-reset (WPF
    /// <c>SkillTreeService.PointsPerLevel</c> = 1; not exposed via <see cref="ISkillTreeService"/>).
    /// </summary>
    private const int PointsPerLevel = 1;

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settings;
    private readonly ILogger<ProfileSyncService> _logger;
    private readonly ISessionService? _sessionService;

    // Optional sibling seams (all-optional pattern): the service still constructs UNWIRED with
    // every merge dependency null. WPF reaches these via App.* statics; here they are injected so
    // the merge is unit-testable and stays out of the live app until slice 7 registers it.
    private readonly IAchievementService? _achievements;
    private readonly IQuestService? _quests;
    private readonly IProgressionService? _progression;
    private readonly ISkillTreeService? _skillTree;

    /// <summary>True after a cloud profile has been pulled + merged (WPF <c>_hasLoadedProfile</c>).</summary>
    private bool _hasLoadedProfile;

    /// <summary>Client version string; reused for the request headers and the heartbeat body.</summary>
    private readonly string _version;

    /// <summary>
    /// Portable heartbeat timer — replaces the WPF <c>DispatcherTimer</c> (Core has no Dispatcher).
    /// Null while the heartbeat is stopped. (WPF <c>_heartbeatTimer</c>, ProfileSyncService.cs:29.)
    /// </summary>
    private Timer? _heartbeatTimer;

    /// <summary>Gates concurrent sync attempts (WPF <c>_syncGate</c>). Used from slice 4.</summary>
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    private bool _syncEnabled = true;
    private bool _disposed;

    /// <inheritdoc />
    public bool IsSyncEnabled => _syncEnabled && !string.IsNullOrEmpty(_settings.Current?.AuthToken);

    /// <inheritdoc />
    public DateTime? LastSyncTime { get; private set; }

    /// <inheritdoc />
    public string? LastSyncError { get; private set; }

    /// <inheritdoc />
    public int ConsecutiveSyncFailures { get; private set; }

    // These events are declared to satisfy IProfileSyncService (events cannot be default
    // interface members). They are raised in later slices (ProfileLoaded: slice 3,
    // SyncHealthChanged: slice 4). Suppress the "never used" warning until then.
#pragma warning disable CS0067
    /// <inheritdoc />
    public event EventHandler<int>? SyncHealthChanged;

    /// <inheritdoc />
    public event EventHandler? ProfileLoaded;
#pragma warning restore CS0067

    /// <summary>
    /// Production constructor. Creates the default HTTP handler and (optionally) takes the
    /// session seam used for the heartbeat <c>in_session</c> flag.
    /// </summary>
    public ProfileSyncService(
        ISettingsService settings,
        ILogger<ProfileSyncService> logger,
        ISessionService? sessionService = null,
        IAchievementService? achievements = null,
        IQuestService? quests = null,
        IProgressionService? progression = null,
        ISkillTreeService? skillTree = null)
        : this(settings, logger, handler: null, sessionService, achievements, quests, progression, skillTree)
    {
    }

    /// <summary>
    /// Test-seam constructor: injects an <see cref="HttpMessageHandler"/> so unit tests can stub
    /// server responses without touching the network. The public constructor passes
    /// <paramref name="handler"/> = <c>null</c>, which falls back to the default handler, so the
    /// public API is unchanged.
    /// </summary>
    internal ProfileSyncService(
        ISettingsService settings,
        ILogger<ProfileSyncService> logger,
        HttpMessageHandler? handler,
        ISessionService? sessionService = null,
        IAchievementService? achievements = null,
        IQuestService? quests = null,
        IProgressionService? progression = null,
        ISkillTreeService? skillTree = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sessionService = sessionService;
        _achievements = achievements;
        _quests = quests;
        _progression = progression;
        _skillTree = skillTree;

        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        _version = GetCurrentVersion();
        _httpClient.DefaultRequestHeaders.Add("X-Client-Version", _version);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{_version}");

        // Never logs any token / auth value.
        _logger.LogDebug("ProfileSyncService initialized (client {Version})", _version);
    }

    /// <summary>
    /// Adds the <c>X-Auth-Token</c> header to a V2 API request if an auth token is available.
    /// The token value is never logged. (WPF <c>AddAuthHeader</c>, ProfileSyncService.cs:2256.)
    /// </summary>
    private void AddAuthHeader(HttpRequestMessage request)
    {
        var token = _settings.Current?.AuthToken;
        if (!string.IsNullOrEmpty(token))
            request.Headers.Add("X-Auth-Token", token);
    }

    /// <summary>
    /// Signs an HTTP request with HMAC-SHA256 for anti-cheat verification, adding the
    /// <c>X-CCP-Timestamp</c> and <c>X-CCP-Signature</c> headers. Applied only to the sync push.
    ///
    /// Scheme (ported verbatim from WPF <c>SignRequest</c>, ProfileSyncService.cs:2355):
    /// key = UTF8(<c>{unifiedId}:ccp-anticheat-2026</c>), payload = <c>{unixSeconds}:{body}</c>,
    /// signature = lowercase-hex HMACSHA256(key, payload). The app key is an embedded
    /// obfuscation constant, not a real secret. No-op when <paramref name="unifiedId"/> is empty.
    /// </summary>
    internal static void SignRequest(HttpRequestMessage request, string body, string unifiedId)
    {
        if (string.IsNullOrEmpty(unifiedId)) return;

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var payload = $"{timestamp}:{body}";

        // Key derived from unified_id + embedded app key.
        const string appKey = "ccp-anticheat-2026";
        var keyBytes = Encoding.UTF8.GetBytes($"{unifiedId}:{appKey}");

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var signature = Convert.ToHexString(hash).ToLowerInvariant();

        request.Headers.Add("X-CCP-Timestamp", timestamp);
        request.Headers.Add("X-CCP-Signature", signature);
    }

    #region Heartbeat

    /// <summary>Test seam: whether the heartbeat timer is currently running.</summary>
    internal bool IsHeartbeatActive => _heartbeatTimer is not null;

    /// <summary>
    /// Whether a preset session is currently running (drives the heartbeat <c>in_session</c> flag).
    /// WPF sends <c>App.IsSessionRunning</c>; in Core <c>ISessionService.State != Idle</c> matches
    /// that contract (session state stays non-Idle while paused). The seam is optional, so this is
    /// <c>false</c> until the head wires <see cref="ISessionService"/> (slice 7).
    /// </summary>
    private bool InSession =>
        (_sessionService?.State ?? SessionState.Idle) != SessionState.Idle;

    /// <inheritdoc />
    public void StartHeartbeat()
    {
        if (_disposed) return;
        if (_heartbeatTimer != null) return;

        // System.Threading.Timer replaces the WPF DispatcherTimer (Core has no Dispatcher).
        // dueTime 0 => immediate first tick (WPF fires one right after Start, line 116); then
        // every HeartbeatIntervalSeconds. The callback runs on a thread-pool thread;
        // SendHeartbeatAsync swallows its own exceptions so nothing goes unobserved.
        _heartbeatTimer = new Timer(
            _ => _ = SendHeartbeatAsync(),
            state: null,
            dueTime: TimeSpan.Zero,
            period: TimeSpan.FromSeconds(HeartbeatIntervalSeconds));

        _logger.LogInformation("Heartbeat started (every {Seconds}s)", HeartbeatIntervalSeconds);
    }

    /// <inheritdoc />
    public void StopHeartbeat()
    {
        // Idempotent + null-safe: fine to call when never started or already stopped.
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        _logger.LogDebug("Heartbeat stopped");
    }

    /// <summary>
    /// Sends a lightweight V2 heartbeat (<c>POST /v2/user/heartbeat</c>) so the user shows as
    /// online. Skips when disposed, offline, not logged in, or missing a unified id — mirroring the
    /// WPF guards (ProfileSyncService.cs:132). SECURITY: never logs the auth token.
    /// </summary>
    internal async Task SendHeartbeatAsync()
    {
        if (_disposed) return;

        // Skip if offline mode is enabled (WPF guard).
        if (_settings.Current?.OfflineMode == true) return;

        // Requires opt-in + a valid auth token (Core's "logged in" proxy, slice 1).
        if (!IsSyncEnabled) return;

        var unifiedId = _settings.Current?.UnifiedId;
        if (string.IsNullOrEmpty(unifiedId)) return;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/user/heartbeat");
            AddAuthHeader(request);
            request.Content = new StringContent(
                JsonConvert.SerializeObject(new
                {
                    unified_id = unifiedId,
                    // ProfileSync slice 7: wire real activity/idle state. WPF uses
                    // App.ActivityTracker?.IsIdle != true; no idle seam exists in Core yet, so
                    // default to active. The heartbeat presence flags are not security-critical.
                    is_active = true,
                    in_session = InSession,
                    app_version = _version
                }),
                Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // ProfileSync slice 4: full HandleUnauthorizedAsync / restore-session recovery.
                // Minimal path for now: count the failure. The token is never cleared or logged.
                ConsecutiveSyncFailures++;
                SyncHealthChanged?.Invoke(this, ConsecutiveSyncFailures);
                _logger.LogWarning("[Auth] Heartbeat unauthorized (401)");
                return;
            }

            _logger.LogDebug("V2 Heartbeat: {Status}", response.StatusCode);
        }
        catch (Exception ex)
        {
            // Heartbeat is best-effort; never surface. Message only, never the token.
            _logger.LogDebug("Heartbeat error: {Error}", ex.Message);
        }
    }

    #endregion

    #region Pull + Merge (slice 3)

    /// <summary>
    /// Pulls the cloud profile and merges it into local progression using take-higher / union
    /// semantics. Returns true on a successful pull. Skips when offline, not authenticated, or
    /// without a unified id (mirrors the WPF guards, ProfileSyncService.cs:206).
    ///
    /// PORT NOTE: WPF is V2-first (it drives the pull through <c>SyncProfileAsync</c>, the push,
    /// then falls back to a V1 <c>GET /user/profile</c>). The push is slice 4, so here the pull is
    /// a READ-ONLY <c>GET</c> (no upload — it cannot clobber the server) authenticated with the V2
    /// <c>X-Auth-Token</c>. WPF's V1 GET used a Bearer OAuth token, which has no Core seam (plan
    /// §10.3); <c>X-Auth-Token</c> is the portable auth. The response body may be a V1
    /// <see cref="ProfileResponse"/> (nested <c>profile</c>) and/or carry the V2 reconciliation
    /// fields at top level; whichever is present is applied (unknown fields are dropped).
    /// </summary>
    public async Task<bool> LoadProfileAsync()
    {
        if (_settings.Current?.OfflineMode == true)
        {
            _logger.LogDebug("Profile sync skipped - offline mode enabled");
            return false;
        }

        if (!IsSyncEnabled)
        {
            _logger.LogDebug("Profile sync skipped - not authenticated");
            return false;
        }

        var unifiedId = _settings.Current?.UnifiedId;
        if (string.IsNullOrEmpty(unifiedId))
        {
            // Core has no OAuth/Bearer fallback seam (plan §10.3): a unified id + X-Auth-Token is
            // the only supported pull path.
            _logger.LogWarning("Profile load skipped - no unified id (V1 OAuth fallback not ported)");
            return false;
        }

        // ProfileSync slice: V2 defaults-heal (WPF TryHealDefaultsFromServerAsync, :355) deferred
        // — it needs the V2Auth + Patreon seams (V2AuthService.ApplyUserDataToSettings /
        // SetWhitelistStatus) which have no Core equivalent. Do NOT invent seams.

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ProxyBaseUrl}/user/profile");
            AddAuthHeader(request);

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // ProfileSync slice 4: full 401 recovery (restore-session). Token never cleared/logged.
                ConsecutiveSyncFailures++;
                SyncHealthChanged?.Invoke(this, ConsecutiveSyncFailures);
                _logger.LogWarning("[Auth] Profile load unauthorized (401)");
                return false;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Profile load failed: {Status}", response.StatusCode);
                LastSyncError = $"Load failed: {response.StatusCode}";
                return false;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            // V1 pull shape: { exists, user_id, profile: { ... } }.
            var v1 = SafeDeserialize<ProfileResponse>(json);
            if (v1?.Profile != null)
                MergeCloudProfile(v1.Profile);

            // V2 reconciliation fields at top level (skill_points, unlocked_skills, level_reset,
            // lifetime_points_spent, force_* ...). The two DTOs partition the wire contract, so a
            // double-parse never double-applies a field.
            var v2 = SafeDeserialize<V2SyncResponse>(json);
            if (v2 != null)
                MergeV2SyncResponse(v2);

            LastSyncTime = DateTime.Now;
            LastSyncError = null;
            _hasLoadedProfile = true;

            _logger.LogInformation("Loaded cloud profile");
            ProfileLoaded?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load cloud profile");
            LastSyncError = ex.Message;
            return false;
        }
    }

    /// <summary>Deserialize <paramref name="json"/> to <typeparamref name="T"/>, returning null on malformed input.</summary>
    private static T? SafeDeserialize<T>(string json) where T : class
    {
        try { return JsonConvert.DeserializeObject<T>(json); }
        catch { return null; }
    }

    /// <summary>
    /// Take-higher / union merge of a pulled V1 <see cref="CloudProfile"/> into local progression.
    /// Ported from WPF <c>MergeCloudProfile</c> (ProfileSyncService.cs:1027). Every non-force stat
    /// is take-higher / union — never a blind overwrite that lowers a local value. Background
    /// sync-UP pushes are deferred to slice 4 (this slice is pull-only).
    /// </summary>
    private void MergeCloudProfile(CloudProfile cloudProfile)
    {
        var settings = _settings.Current;
        var achievements = _achievements;
        if (settings == null) return;

        bool needsSave = false;

        // Cloud stores TOTAL XP; local stores current-level XP.
        var localTotalXp = _progression?.GetTotalXP(settings.PlayerLevel, settings.PlayerXP) ?? settings.PlayerXP;
        var cloudTotalXp = (double)cloudProfile.Xp;

        const double MAX_STARTUP_DELTA = 50000; // Max XP above cloud we trust from local.

        if (cloudTotalXp > localTotalXp)
        {
            var cloudLevelXp = _progression?.GetCurrentLevelXP(cloudProfile.Level, cloudProfile.Xp) ?? 0;
            _logger.LogInformation("Cloud has higher progress - syncing DOWN: Cloud Level {CloudLevel} > Local Level {LocalLevel}",
                cloudProfile.Level, settings.PlayerLevel);
            settings.PlayerLevel = cloudProfile.Level;
            settings.PlayerXP = cloudLevelXp;
            needsSave = true;
            achievements?.CheckLevelAchievements(cloudProfile.Level);
        }
        else if (localTotalXp > cloudTotalXp + MAX_STARTUP_DELTA)
        {
            // Distinguish "cloud legitimately ahead" from an uninitialized/empty cloud read.
            bool looksUninitialized =
                cloudProfile.Level <= 1 &&
                cloudProfile.Xp == 0 &&
                (cloudProfile.Achievements == null || cloudProfile.Achievements.Count == 0) &&
                (cloudProfile.UnlockedSkills == null || cloudProfile.UnlockedSkills.Count == 0) &&
                (cloudProfile.SkillPoints ?? 0) == 0;

            if (looksUninitialized)
            {
                _logger.LogWarning("[Anti-cheat] DEFENDED: cloud looks uninitialized but local has progress (Level {LocalLevel}). Keeping local.",
                    settings.PlayerLevel);
            }
            else
            {
                var cloudLevelXp = _progression?.GetCurrentLevelXP(cloudProfile.Level, cloudProfile.Xp) ?? 0;
                _logger.LogWarning("[Anti-cheat] Local XP suspiciously high on startup vs cloud — forcing cloud values");
                settings.PlayerLevel = cloudProfile.Level;
                settings.PlayerXP = cloudLevelXp;
                needsSave = true;
            }
        }
        else if (localTotalXp > cloudTotalXp)
        {
            _logger.LogInformation("Local has higher progress - keeping local (Level {LocalLevel})", settings.PlayerLevel);
            // ProfileSync slice 4: background sync-UP push deferred to the push slice.
        }
        else
        {
            _logger.LogDebug("Local and cloud progress equal: Level {Level}", settings.PlayerLevel);
        }

        // Merge achievements (union — never lose an unlock).
        if (cloudProfile.Achievements != null && achievements?.Progress != null)
        {
            foreach (var achievementId in cloudProfile.Achievements)
            {
                if (!achievements.Progress.IsUnlocked(achievementId))
                {
                    achievements.Progress.Unlock(achievementId);
                    needsSave = true;
                }
            }
        }

        // Merge lifetime stats (take HIGHER).
        if (cloudProfile.Stats != null && achievements?.Progress != null)
        {
            var progress = achievements.Progress;
            if (TryHigherDouble(cloudProfile.Stats, "longest_session_minutes", progress.LongestSessionMinutes, out var d)) { progress.LongestSessionMinutes = d; needsSave = true; }
            if (TryHigherInt(cloudProfile.Stats, "total_flashes", progress.TotalFlashImages, out var i)) { progress.TotalFlashImages = i; needsSave = true; }
            if (TryHigherInt(cloudProfile.Stats, "consecutive_days", progress.ConsecutiveDays, out i)) { progress.ConsecutiveDays = i; needsSave = true; }
            if (TryHigherInt(cloudProfile.Stats, "total_bubbles_popped", progress.TotalBubblesPopped, out i)) { progress.TotalBubblesPopped = i; needsSave = true; }
            if (TryHigherDouble(cloudProfile.Stats, "total_video_minutes", progress.TotalVideoMinutes, out d)) { progress.TotalVideoMinutes = d; needsSave = true; }
            if (TryHigherInt(cloudProfile.Stats, "total_lock_cards_completed", progress.TotalLockCardsCompleted, out i)) { progress.TotalLockCardsCompleted = i; needsSave = true; }
            if (TryHigherInt(cloudProfile.Stats, "highest_streak", settings.HighestStreak, out i)) { settings.HighestStreak = i; needsSave = true; }
            if (TryHigherInt(cloudProfile.Stats, "total_attention_checks_passed", progress.TotalAttentionChecksPassed, out i)) { progress.TotalAttentionChecksPassed = i; needsSave = true; }
            if (TryHigherInt(cloudProfile.Stats, "video_attention_checks_passed", progress.VideoAttentionChecksPassed, out i)) { progress.VideoAttentionChecksPassed = i; needsSave = true; }
            if (TryHigherInt(cloudProfile.Stats, "video_attention_checks_failed", progress.VideoAttentionChecksFailed, out i)) { progress.VideoAttentionChecksFailed = i; needsSave = true; }
            if (TryHigherInt(cloudProfile.Stats, "total_attention_check_failures", progress.AttentionCheckFailures, out i)) { progress.AttentionCheckFailures = i; needsSave = true; }
            if (TryHigherInt(cloudProfile.Stats, "total_bubble_count_games", progress.TotalBubbleCountGames, out i)) { progress.TotalBubbleCountGames = i; needsSave = true; }
            if (TryHigherInt(cloudProfile.Stats, "total_bubble_count_correct", progress.TotalBubbleCountCorrect, out i)) { progress.TotalBubbleCountCorrect = i; needsSave = true; }
            if (TryHigherInt(cloudProfile.Stats, "total_bubble_count_failed", progress.TotalBubbleCountFailed, out i)) { progress.TotalBubbleCountFailed = i; needsSave = true; }
            if (TryHigherInt(cloudProfile.Stats, "bubble_count_best_streak", progress.BubbleCountBestStreak, out i)) { progress.BubbleCountBestStreak = i; needsSave = true; }
            if (TryHigherInt(cloudProfile.Stats, "total_sessions_started", progress.TotalSessionsStarted, out i)) { progress.TotalSessionsStarted = i; needsSave = true; }
            if (TryHigherInt(cloudProfile.Stats, "total_sessions_abandoned", progress.TotalSessionsAbandoned, out i)) { progress.TotalSessionsAbandoned = i; needsSave = true; }
            if (TryHigherDouble(cloudProfile.Stats, "total_xp_earned", progress.TotalXPEarned, out d)) { progress.TotalXPEarned = d; needsSave = true; }
            if (TryHigherInt(cloudProfile.Stats, "total_skill_points_earned", progress.TotalSkillPointsEarned, out i)) { progress.TotalSkillPointsEarned = i; needsSave = true; }
            if (TryHigherDouble(cloudProfile.Stats, "total_pink_filter_minutes", progress.TotalPinkFilterMinutes, out d)) { progress.TotalPinkFilterMinutes = d; needsSave = true; }
            if (TryHigherDouble(cloudProfile.Stats, "total_spiral_minutes", progress.TotalSpiralMinutes, out d)) { progress.TotalSpiralMinutes = d; needsSave = true; }
        }

        // Merge quest streak data (skipped when force_streak_override is active — handled below).
        if (cloudProfile.Stats != null && cloudProfile.ForceStreakOverride != true)
        {
            if (TryHigherInt(cloudProfile.Stats, "daily_quest_streak", settings.DailyQuestStreak, out var cs))
            {
                settings.DailyQuestStreak = cs;
                needsSave = true;
            }

            if (cloudProfile.Stats.TryGetValue("last_daily_quest_date", out var cloudLastDate)
                && DateTime.TryParse(cloudLastDate?.ToString(), out var cloudDate)
                && (!settings.LastDailyQuestDate.HasValue || cloudDate.Date > settings.LastDailyQuestDate.Value.Date))
            {
                settings.LastDailyQuestDate = cloudDate.Date;
                needsSave = true;
            }

            var questProgress = _quests?.Progress;
            if (questProgress != null && cloudProfile.Stats.TryGetValue("quest_completion_dates", out var cloudDatesObj))
            {
                try
                {
                    var cloudDates = JsonConvert.DeserializeObject<List<string>>(cloudDatesObj?.ToString() ?? "[]");
                    if (cloudDates != null)
                    {
                        var localDates = new HashSet<DateTime>(questProgress.DailyQuestCompletionDates.Select(dt => dt.Date));
                        bool datesChanged = false;
                        foreach (var ds in cloudDates)
                        {
                            if (DateTime.TryParse(ds, out var dt) && !localDates.Contains(dt.Date))
                            {
                                questProgress.DailyQuestCompletionDates.Add(dt.Date);
                                datesChanged = true;
                            }
                        }
                        if (datesChanged)
                        {
                            var cutoff = DateTime.Today.AddDays(-90);
                            questProgress.DailyQuestCompletionDates.RemoveAll(dt => dt.Date < cutoff);
                            needsSave = true;
                            _quests?.RecalculateStreak();

                            if (TryHigherInt(cloudProfile.Stats, "daily_quest_streak", settings.DailyQuestStreak, out var csAfter))
                                settings.DailyQuestStreak = csAfter;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Quest sync: failed to parse cloud completion dates: {Error}", ex.Message);
                }
            }

            if (questProgress != null)
            {
                if (TryHigherInt(cloudProfile.Stats, "total_daily_quests_completed", questProgress.TotalDailyQuestsCompleted, out var cdt)) { questProgress.TotalDailyQuestsCompleted = cdt; needsSave = true; }
                if (TryHigherInt(cloudProfile.Stats, "total_weekly_quests_completed", questProgress.TotalWeeklyQuestsCompleted, out var cwt)) { questProgress.TotalWeeklyQuestsCompleted = cwt; needsSave = true; }
                if (TryHigherInt(cloudProfile.Stats, "total_xp_from_quests", questProgress.TotalXPFromQuests, out var cqx)) { questProgress.TotalXPFromQuests = cqx; needsSave = true; }

                // Restore daily_quests_completed_today (prevents a quest-reset exploit) — only when
                // the cloud reset date is today AND the completion calendar backs it up.
                if (cloudProfile.Stats.TryGetValue("daily_quests_completed_today", out var cloudDailyCompToday))
                {
                    var cloudCount = Convert.ToInt32(cloudDailyCompToday);
                    bool cloudDateIsToday = cloudProfile.Stats.TryGetValue("daily_completion_reset_date", out var cloudResetDate)
                        && DateTime.TryParse(cloudResetDate?.ToString(), out var resetDate)
                        && resetDate.Date == DateTime.Today;
                    if (cloudDateIsToday && cloudCount > questProgress.GetDailyQuestsCompletedToday()
                        && questProgress.DailyQuestCompletionDates.Any(dt => dt.Date == DateTime.Today))
                    {
                        questProgress.DailyQuestsCompletedToday = cloudCount;
                        questProgress.DailyCompletionResetDate = DateTime.Today;
                        needsSave = true;
                    }
                }

                if (questProgress.DailyQuestCompletionDates.Any(dt => dt.Date == DateTime.Today)
                    && questProgress.GetDailyQuestsCompletedToday() == 0)
                {
                    questProgress.DailyQuestsCompletedToday = 1;
                    questProgress.DailyCompletionResetDate = DateTime.Today;
                    needsSave = true;
                }
            }
        }

        // Skill points — take MAX (skill points only increase).
        if (cloudProfile.SkillPoints.HasValue)
        {
            var maxPoints = Math.Max(cloudProfile.SkillPoints.Value, settings.SkillPoints);
            if (maxPoints != settings.SkillPoints)
            {
                _logger.LogInformation("Skill tree sync: skill points server={Server}, local={Local} — taking max", cloudProfile.SkillPoints.Value, settings.SkillPoints);
                settings.SkillPoints = maxPoints;
                needsSave = true;
            }
        }

        // Unlocked skills — union (never lose an unlocked skill).
        if (cloudProfile.UnlockedSkills != null && cloudProfile.UnlockedSkills.Count > 0)
        {
            var localSkills = settings.UnlockedSkills ?? new List<string>();
            var skillsToAdd = cloudProfile.UnlockedSkills.Except(localSkills).ToList();
            if (skillsToAdd.Count > 0)
            {
                foreach (var skill in skillsToAdd)
                    if (!localSkills.Contains(skill)) localSkills.Add(skill);
                settings.UnlockedSkills = localSkills;
                needsSave = true;
            }
        }

        // Conditioning minutes — take HIGHER.
        if (cloudProfile.TotalConditioningMinutes.HasValue
            && cloudProfile.TotalConditioningMinutes.Value > settings.TotalConditioningMinutes)
        {
            settings.TotalConditioningMinutes = cloudProfile.TotalConditioningMinutes.Value;
            needsSave = true;
        }

        // Companion progress — per-companion, higher level wins.
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
                    if ((serverLevel > localLevel || (localProgress == null && serverProgress != null)) && serverProgress != null)
                    {
                        localData[companionId] = serverProgress;
                        needsSave = true;
                    }
                }
            }
        }

        // Server-side quest reset flags.
        if (cloudProfile.ResetWeeklyQuest == true)
        {
            _logger.LogInformation("Server requested weekly quest reset");
            _quests?.ForceRegenerateWeeklyQuest();
            needsSave = true;
            // ProfileSync slice 4: background sync-back to clear the server flag deferred.
        }
        if (cloudProfile.ResetDailyQuest == true)
        {
            _logger.LogInformation("Server requested daily quest reset");
            _quests?.ForceRegenerateDailyQuest();
            needsSave = true;
        }

        achievements?.Progress?.SyncCurrentStreak();

        if (needsSave)
        {
            _settings.Save();
            achievements?.Save();
        }

        // Legacy force_streak_override (the flag rides the V1 profile).
        if (cloudProfile.ForceStreakOverride == true && cloudProfile.Stats != null)
        {
            _logger.LogInformation("Legacy sync: force streak override — adopting server streak values");
            var legacyStreakStats = new V2StreakStats();
            if (TryHigherInt(cloudProfile.Stats, "daily_quest_streak", int.MinValue, out var fStreak)) legacyStreakStats.DailyQuestStreak = fStreak;
            if (cloudProfile.Stats.TryGetValue("last_daily_quest_date", out var fDate)) legacyStreakStats.LastDailyQuestDate = fDate?.ToString();
            if (cloudProfile.Stats.TryGetValue("quest_completion_dates", out var fDates))
            {
                try { legacyStreakStats.QuestCompletionDates = JsonConvert.DeserializeObject<List<string>>(fDates?.ToString() ?? "[]"); }
                catch { }
            }
            if (TryHigherInt(cloudProfile.Stats, "total_daily_quests_completed", int.MinValue, out var fDailyTotal)) legacyStreakStats.TotalDailyQuestsCompleted = fDailyTotal;
            if (TryHigherInt(cloudProfile.Stats, "total_weekly_quests_completed", int.MinValue, out var fWeeklyTotal)) legacyStreakStats.TotalWeeklyQuestsCompleted = fWeeklyTotal;
            if (TryHigherInt(cloudProfile.Stats, "total_xp_from_quests", int.MinValue, out var fXp)) legacyStreakStats.TotalXPFromQuests = fXp;

            ApplyForceStreakOverride(legacyStreakStats);
        }
    }

    /// <summary>
    /// Applies a pulled V2 reconciliation payload (skill-point MAX, unlocked-skills UNION — skipped
    /// on <c>level_reset</c>, monotonic lifetime reconcile, force streak/skills overrides, lifetime
    /// stat merge, quest reset flags, and the season <c>level_reset</c> rebuild).
    ///
    /// PORT NOTE: extracted verbatim from the WPF <c>SyncProfileAsync</c> V2-response block
    /// (ProfileSyncService.cs:578-780) so the pull path and the slice-4 push can share it. The
    /// force helpers (<see cref="ApplyForceStreakOverride"/>, <see cref="ApplyForceSkillsReset"/>)
    /// and <see cref="MergeV2CloudStatsIntoLocalProgress"/> are invoked from here.
    /// </summary>
    private void MergeV2SyncResponse(V2SyncResponse v2Result)
    {
        var settings = _settings.Current;
        if (settings == null) return;

        if (v2Result.ResetWeeklyQuest == true)
        {
            _logger.LogInformation("V2 Sync: server requested weekly quest reset");
            _quests?.ForceRegenerateWeeklyQuest();
        }
        if (v2Result.ResetDailyQuest == true)
        {
            _logger.LogInformation("V2 Sync: server requested daily quest reset");
            _quests?.ForceRegenerateDailyQuest();
        }

        // force_streak_override — adopt server streak even if LOWER.
        if (v2Result.ForceStreakOverride == true && v2Result.StreakStats != null)
        {
            _logger.LogInformation("V2 Sync: force streak override — adopting server streak values");
            ApplyForceStreakOverride(v2Result.StreakStats);
        }

        // force_skills_reset — clear + refund, guarded by PendingSkillsResetAck (survives crashes).
        if (v2Result.ForceSkillsReset == true && !settings.PendingSkillsResetAck)
        {
            _logger.LogInformation("V2 Sync: force skills reset — clearing all skills");
            ApplyForceSkillsReset(v2Result.SkillPoints);
            settings.PendingSkillsResetAck = true;
            _settings.Save();
        }
        else if (settings.PendingSkillsResetAck && v2Result.ForceSkillsReset != true)
        {
            settings.PendingSkillsResetAck = false;
            _settings.Save();
        }
        else if (v2Result.SkillPoints.HasValue)
        {
            // Skill points only increase and never reset by seasons — take MAX.
            var maxPoints = Math.Max(v2Result.SkillPoints.Value, settings.SkillPoints);
            if (maxPoints != settings.SkillPoints)
            {
                _logger.LogInformation("V2 Sync: skill points server={Server}, local={Local} — taking max", v2Result.SkillPoints.Value, settings.SkillPoints);
                settings.SkillPoints = maxPoints;
                _settings.Save();
            }
        }

        // Unlocked skills — union. SKIPPED on level_reset (the rollover legitimately removes
        // mechanical skills; the reset rebuild below applies the authoritative post-rollover list).
        if (v2Result.UnlockedSkills != null && v2Result.UnlockedSkills.Count > 0 && v2Result.LevelReset != true)
        {
            var localSkills = settings.UnlockedSkills ?? new List<string>();
            var skillsToAdd = v2Result.UnlockedSkills.Except(localSkills).ToList();
            if (skillsToAdd.Count > 0)
            {
                foreach (var skill in skillsToAdd)
                    if (!localSkills.Contains(skill)) localSkills.Add(skill);
                settings.UnlockedSkills = localSkills;
                _settings.Save();
            }
        }

        // Prestige lifetime_points_spent — monotonic reconcile (never lowers).
        if (v2Result.LifetimePointsSpent != null)
            _achievements?.ReconcileLifetimePointsSpent(v2Result.LifetimePointsSpent.Value);

        // Oopsie season — compared to the SERVER-authoritative season key (not wall-clock).
        if (v2Result.OopsieUsedSeason != null)
        {
            var oopsieUsed = v2Result.OopsieUsedSeason == SeasonRecapService.CurrentSeasonKey;
            if (settings.SeasonalStreakRecoveryUsed != oopsieUsed)
            {
                settings.SeasonalStreakRecoveryUsed = oopsieUsed;
                _settings.Save();
            }
        }

        // Display name — server authoritative.
        var displayName = v2Result.User?.DisplayName;
        if (!string.IsNullOrEmpty(displayName) && displayName != settings.UserDisplayName)
        {
            settings.UserDisplayName = displayName;
            _settings.Save();
        }

        // OG status — server authoritative.
        if (v2Result.IsSeason0Og != null && settings.IsSeason0Og != v2Result.IsSeason0Og.Value)
        {
            settings.IsSeason0Og = v2Result.IsSeason0Og.Value;
            _settings.Save();
        }

        // Bonus rerolls — admin granted.
        if (v2Result.BonusDailyRerolls != null || v2Result.BonusWeeklyRerolls != null)
        {
            settings.BonusDailyRerolls = v2Result.BonusDailyRerolls ?? 0;
            settings.BonusWeeklyRerolls = v2Result.BonusWeeklyRerolls ?? 0;
            _settings.Save();
        }

        // ProfileSync slice: patreon entitlement heal deferred — WPF sets PatreonPremiumValidUntil
        // + App.Patreon.SetWhitelistStatus(true) on patreon_is_whitelisted here; no Core Patreon seam.

        // highest_level_ever — server authoritative.
        if (v2Result.User?.HighestLevelEver != null)
        {
            var serverHighest = v2Result.User.HighestLevelEver.Value;
            if (serverHighest != settings.HighestLevelEver)
            {
                settings.HighestLevelEver = serverHighest;
                _settings.Save();
            }
        }

        // Achievements — union (never lose an unlock).
        if (v2Result.User?.Achievements != null && v2Result.User.Achievements.Count > 0 && _achievements?.Progress != null)
        {
            var restored = 0;
            foreach (var achievementId in v2Result.User.Achievements)
            {
                if (!_achievements.Progress.IsUnlocked(achievementId))
                {
                    _achievements.Progress.Unlock(achievementId);
                    restored++;
                }
            }
            if (restored > 0)
            {
                _logger.LogInformation("V2 Sync: restored {Count} achievements from server", restored);
                _achievements.Save();
            }
        }

        // Lifetime stats + quest streak merge.
        if (v2Result.User?.Stats != null
            && MergeV2CloudStatsIntoLocalProgress(v2Result.User.Stats, v2Result.ForceStreakOverride == true))
        {
            _achievements?.Progress?.SyncCurrentStreak();
            _settings.Save();
            _achievements?.Save();
        }

        // Conditioning minutes — take HIGHER.
        if (v2Result.TotalConditioningMinutes.HasValue
            && v2Result.TotalConditioningMinutes.Value > settings.TotalConditioningMinutes)
        {
            settings.TotalConditioningMinutes = v2Result.TotalConditioningMinutes.Value;
            _settings.Save();
        }

        // Companion progress — per-companion, higher level (then higher XP) wins.
        if (v2Result.CompanionProgress != null && v2Result.CompanionProgress.Count > 0)
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
                    if ((serverLevel > localLevel || (serverLevel == localLevel && serverXP > localXP)) && serverProgress != null)
                    {
                        localData[companionId] = serverProgress;
                        needsCompanionSave = true;
                    }
                    else if (localProgress == null && serverProgress != null)
                    {
                        localData[companionId] = serverProgress;
                        needsCompanionSave = true;
                    }
                }
            }
            if (needsCompanionSave) _settings.Save();
        }

        // level_reset — admin reset all levels; rebuild the tree as server-list ∪ permanent-owned.
        if (v2Result.LevelReset == true && v2Result.User != null)
        {
            var serverLevel = v2Result.User.Level;
            var serverXp = v2Result.User.Xp;
            var serverLevelXp = _progression?.GetCurrentLevelXP(serverLevel, serverXp) ?? 0;

            _logger.LogInformation("V2 Sync: level reset by admin — forcing Level {Level}, XP {Xp}", serverLevel, serverXp);
            settings.PlayerLevel = serverLevel;
            settings.PlayerXP = serverLevelXp;
            settings.HighestLevelEver = v2Result.User.HighestLevelEver ?? 0;

            // The point BALANCE is never reset (max-merge above keeps the higher value); only the
            // tree resets: server list ∪ locally-owned PERMANENT nodes. Mechanical nodes are dropped.
            var permanentOwned = (settings.UnlockedSkills ?? new List<string>())
                .Where(id => SkillDefinition.PermanentIds.Contains(id));
            settings.UnlockedSkills = (v2Result.UnlockedSkills ?? new List<string>())
                .Union(permanentOwned).ToList();

            _skillTree?.OnSeasonReset();

            settings.SeasonResetPending = true;
            _settings.Save();
            // ProfileSync slice 7: WPF also nudges MainWindow.TryPresentSeasonRecap() here; Core has no window.
        }
        else if (v2Result.User != null)
        {
            // Adopt server XP when it is substantially ahead, or when the server clamped us (anti-cheat).
            var serverTotalXp = (double)v2Result.User.Xp;
            var localTotalXp = _progression?.GetTotalXP(settings.PlayerLevel, settings.PlayerXP) ?? 0;

            if (serverTotalXp > localTotalXp + 5000)
            {
                var serverLevel = v2Result.User.Level;
                var serverLevelXp = _progression?.GetCurrentLevelXP(serverLevel, serverTotalXp) ?? 0;
                _logger.LogInformation("V2 Sync: server XP higher — adopting Level {ServerLevel}", serverLevel);
                settings.PlayerLevel = serverLevel;
                settings.PlayerXP = serverLevelXp;
                _settings.Save();
            }
            else if (localTotalXp > serverTotalXp + 75000)
            {
                var serverLevel = v2Result.User.Level;
                var serverLevelXp = _progression?.GetCurrentLevelXP(serverLevel, serverTotalXp) ?? 0;
                _logger.LogWarning("[Anti-cheat] V2 Sync: server clamped XP — forcing Level {ServerLevel}", serverLevel);
                settings.PlayerLevel = serverLevel;
                settings.PlayerXP = serverLevelXp;
                _settings.Save();
            }
        }
    }

    /// <summary>
    /// Pulls lifetime stats + quest streak data from a V2 stats dict into local
    /// AchievementProgress / Settings / QuestProgress using take-higher / union semantics. Returns
    /// true if anything changed. Ported from WPF <c>MergeV2CloudStatsIntoLocalProgress</c>
    /// (ProfileSyncService.cs:1633). The streak block is skipped when
    /// <paramref name="forceStreakOverride"/> is set (handled by <see cref="ApplyForceStreakOverride"/>).
    /// </summary>
    private bool MergeV2CloudStatsIntoLocalProgress(Dictionary<string, object>? cloudStats, bool forceStreakOverride)
    {
        if (cloudStats == null) return false;
        var settings = _settings.Current;
        if (settings == null) return false;

        bool needsSave = false;

        var progress = _achievements?.Progress;
        if (progress != null)
        {
            if (TryHigherDouble(cloudStats, "longest_session_minutes", progress.LongestSessionMinutes, out var d)) { progress.LongestSessionMinutes = d; needsSave = true; }
            if (TryHigherInt(cloudStats, "total_flashes", progress.TotalFlashImages, out var i)) { progress.TotalFlashImages = i; needsSave = true; }
            if (TryHigherInt(cloudStats, "consecutive_days", progress.ConsecutiveDays, out i)) { progress.ConsecutiveDays = i; needsSave = true; }
            if (TryHigherInt(cloudStats, "total_bubbles_popped", progress.TotalBubblesPopped, out i)) { progress.TotalBubblesPopped = i; needsSave = true; }
            if (TryHigherDouble(cloudStats, "total_video_minutes", progress.TotalVideoMinutes, out d)) { progress.TotalVideoMinutes = d; needsSave = true; }
            if (TryHigherInt(cloudStats, "total_lock_cards_completed", progress.TotalLockCardsCompleted, out i)) { progress.TotalLockCardsCompleted = i; needsSave = true; }
            if (TryHigherInt(cloudStats, "highest_streak", settings.HighestStreak, out i)) { settings.HighestStreak = i; needsSave = true; }
            if (TryHigherInt(cloudStats, "total_attention_checks_passed", progress.TotalAttentionChecksPassed, out i)) { progress.TotalAttentionChecksPassed = i; needsSave = true; }
            if (TryHigherInt(cloudStats, "video_attention_checks_passed", progress.VideoAttentionChecksPassed, out i)) { progress.VideoAttentionChecksPassed = i; needsSave = true; }
            if (TryHigherInt(cloudStats, "video_attention_checks_failed", progress.VideoAttentionChecksFailed, out i)) { progress.VideoAttentionChecksFailed = i; needsSave = true; }
            if (TryHigherInt(cloudStats, "total_attention_check_failures", progress.AttentionCheckFailures, out i)) { progress.AttentionCheckFailures = i; needsSave = true; }
            if (TryHigherInt(cloudStats, "total_bubble_count_games", progress.TotalBubbleCountGames, out i)) { progress.TotalBubbleCountGames = i; needsSave = true; }
            if (TryHigherInt(cloudStats, "total_bubble_count_correct", progress.TotalBubbleCountCorrect, out i)) { progress.TotalBubbleCountCorrect = i; needsSave = true; }
            if (TryHigherInt(cloudStats, "total_bubble_count_failed", progress.TotalBubbleCountFailed, out i)) { progress.TotalBubbleCountFailed = i; needsSave = true; }
            if (TryHigherInt(cloudStats, "bubble_count_best_streak", progress.BubbleCountBestStreak, out i)) { progress.BubbleCountBestStreak = i; needsSave = true; }
            if (TryHigherInt(cloudStats, "total_sessions_started", progress.TotalSessionsStarted, out i)) { progress.TotalSessionsStarted = i; needsSave = true; }
            if (TryHigherInt(cloudStats, "total_sessions_abandoned", progress.TotalSessionsAbandoned, out i)) { progress.TotalSessionsAbandoned = i; needsSave = true; }
            if (TryHigherDouble(cloudStats, "total_xp_earned", progress.TotalXPEarned, out d)) { progress.TotalXPEarned = d; needsSave = true; }
            if (TryHigherInt(cloudStats, "total_skill_points_earned", progress.TotalSkillPointsEarned, out i)) { progress.TotalSkillPointsEarned = i; needsSave = true; }
            if (cloudStats.TryGetValue("lifetime_points_spent", out var lifetimeSpent))
            {
                var ls = Convert.ToInt64(lifetimeSpent);
                if (ls > progress.LifetimeSkillPointsSpent) { progress.LifetimeSkillPointsSpent = ls; needsSave = true; }
            }
            if (TryHigherDouble(cloudStats, "total_pink_filter_minutes", progress.TotalPinkFilterMinutes, out d)) { progress.TotalPinkFilterMinutes = d; needsSave = true; }
            if (TryHigherDouble(cloudStats, "total_spiral_minutes", progress.TotalSpiralMinutes, out d)) { progress.TotalSpiralMinutes = d; needsSave = true; }
        }

        if (forceStreakOverride) return needsSave;

        if (TryHigherInt(cloudStats, "daily_quest_streak", settings.DailyQuestStreak, out var cs))
        {
            settings.DailyQuestStreak = cs;
            needsSave = true;
        }

        if (cloudStats.TryGetValue("last_daily_quest_date", out var cloudLastDate)
            && DateTime.TryParse(cloudLastDate?.ToString(), out var cloudDate)
            && (!settings.LastDailyQuestDate.HasValue || cloudDate.Date > settings.LastDailyQuestDate.Value.Date))
        {
            settings.LastDailyQuestDate = cloudDate.Date;
            needsSave = true;
        }

        var questProgress = _quests?.Progress;
        if (questProgress != null && cloudStats.TryGetValue("quest_completion_dates", out var cloudDatesObj))
        {
            try
            {
                var cloudDates = JsonConvert.DeserializeObject<List<string>>(cloudDatesObj?.ToString() ?? "[]");
                if (cloudDates != null)
                {
                    var localDates = new HashSet<DateTime>(questProgress.DailyQuestCompletionDates.Select(dt => dt.Date));
                    bool datesChanged = false;
                    foreach (var ds in cloudDates)
                    {
                        if (DateTime.TryParse(ds, out var dt) && !localDates.Contains(dt.Date))
                        {
                            questProgress.DailyQuestCompletionDates.Add(dt.Date);
                            datesChanged = true;
                        }
                    }
                    if (datesChanged)
                    {
                        var cutoff = DateTime.Today.AddDays(-90);
                        questProgress.DailyQuestCompletionDates.RemoveAll(dt => dt.Date < cutoff);
                        needsSave = true;
                        _quests?.RecalculateStreak();

                        if (TryHigherInt(cloudStats, "daily_quest_streak", settings.DailyQuestStreak, out var csAfter))
                            settings.DailyQuestStreak = csAfter;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("V2 Quest sync: failed to parse cloud completion dates: {Error}", ex.Message);
            }
        }

        if (questProgress != null)
        {
            if (TryHigherInt(cloudStats, "total_daily_quests_completed", questProgress.TotalDailyQuestsCompleted, out var cdt)) { questProgress.TotalDailyQuestsCompleted = cdt; needsSave = true; }
            if (TryHigherInt(cloudStats, "total_weekly_quests_completed", questProgress.TotalWeeklyQuestsCompleted, out var cwt)) { questProgress.TotalWeeklyQuestsCompleted = cwt; needsSave = true; }
            if (TryHigherInt(cloudStats, "total_xp_from_quests", questProgress.TotalXPFromQuests, out var cqx)) { questProgress.TotalXPFromQuests = cqx; needsSave = true; }

            if (cloudStats.TryGetValue("daily_quests_completed_today", out var cloudDailyCompToday))
            {
                var cloudCount = Convert.ToInt32(cloudDailyCompToday);
                bool cloudDateIsToday = cloudStats.TryGetValue("daily_completion_reset_date", out var cloudResetDate)
                    && DateTime.TryParse(cloudResetDate?.ToString(), out var resetDate)
                    && resetDate.Date == DateTime.Today;
                if (cloudDateIsToday && cloudCount > questProgress.GetDailyQuestsCompletedToday()
                    && questProgress.DailyQuestCompletionDates.Any(dt => dt.Date == DateTime.Today))
                {
                    questProgress.DailyQuestsCompletedToday = cloudCount;
                    questProgress.DailyCompletionResetDate = DateTime.Today;
                    needsSave = true;
                }
            }

            if (questProgress.DailyQuestCompletionDates.Any(dt => dt.Date == DateTime.Today)
                && questProgress.GetDailyQuestsCompletedToday() == 0)
            {
                questProgress.DailyQuestsCompletedToday = 1;
                questProgress.DailyCompletionResetDate = DateTime.Today;
                needsSave = true;
            }
        }

        return needsSave;
    }

    /// <summary>
    /// Force-set local streak values from the server, bypassing take-higher (adopts even when
    /// LOWER). Ported from WPF <c>ApplyForceStreakOverride</c> (ProfileSyncService.cs:1884).
    /// </summary>
    private void ApplyForceStreakOverride(V2StreakStats streakStats)
    {
        var settings = _settings.Current;
        if (settings == null) return;

        _logger.LogInformation("Applying force streak override: streak={Streak}, daily={Daily}, weekly={Weekly}",
            streakStats.DailyQuestStreak, streakStats.TotalDailyQuestsCompleted, streakStats.TotalWeeklyQuestsCompleted);

        // Force-set streak (even if lower than local).
        settings.DailyQuestStreak = streakStats.DailyQuestStreak;

        if (!string.IsNullOrEmpty(streakStats.LastDailyQuestDate)
            && DateTime.TryParse(streakStats.LastDailyQuestDate, out var parsedDate))
        {
            settings.LastDailyQuestDate = parsedDate.Date;
        }

        var questProgress = _quests?.Progress;
        if (questProgress != null)
        {
            if (streakStats.QuestCompletionDates != null)
            {
                questProgress.DailyQuestCompletionDates.Clear();
                foreach (var ds in streakStats.QuestCompletionDates)
                    if (DateTime.TryParse(ds, out var dt)) questProgress.DailyQuestCompletionDates.Add(dt.Date);
            }

            // Force-set totals (even if lower).
            questProgress.TotalDailyQuestsCompleted = streakStats.TotalDailyQuestsCompleted;
            questProgress.TotalWeeklyQuestsCompleted = streakStats.TotalWeeklyQuestsCompleted;
            questProgress.TotalXPFromQuests = streakStats.TotalXPFromQuests;
        }

        _settings.Save();
    }

    /// <summary>
    /// Force-reset all skills and refund points (server <c>force_skills_reset</c>). Ported from WPF
    /// <c>ApplyForceSkillsReset</c> (ProfileSyncService.cs:1929). Refund defaults to
    /// <c>PlayerLevel × PointsPerLevel</c> when the server sends no explicit balance.
    /// </summary>
    private void ApplyForceSkillsReset(int? serverSkillPoints)
    {
        var settings = _settings.Current;
        if (settings == null) return;

        var refundedPoints = serverSkillPoints ?? (settings.PlayerLevel * PointsPerLevel);

        _logger.LogInformation("Applying force skills reset: clearing {Count} skills, setting points to {Points}",
            settings.UnlockedSkills?.Count ?? 0, refundedPoints);

        settings.UnlockedSkills = new List<string>();
        settings.SkillPoints = refundedPoints;
        _settings.Save();
    }

    /// <summary>Take-higher helper: reads an int-ish stat and reports whether it exceeds <paramref name="current"/>.</summary>
    private static bool TryHigherInt(Dictionary<string, object> stats, string key, int current, out int value)
    {
        value = current;
        if (!stats.TryGetValue(key, out var raw)) return false;
        var parsed = Convert.ToInt32(raw);
        if (parsed <= current) return false;
        value = parsed;
        return true;
    }

    /// <summary>Take-higher helper: reads a double-ish stat and reports whether it exceeds <paramref name="current"/>.</summary>
    private static bool TryHigherDouble(Dictionary<string, object> stats, string key, double current, out double value)
    {
        value = current;
        if (!stats.TryGetValue(key, out var raw)) return false;
        var parsed = Convert.ToDouble(raw);
        if (parsed <= current) return false;
        value = parsed;
        return true;
    }

    #endregion

    /// <summary>
    /// Resolves the current client version string for the <c>X-Client-Version</c> / user-agent
    /// headers (mirrors <c>RemoteControlService.GetCurrentVersion</c>).
    /// </summary>
    private static string GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        if (version != null && (version.Major > 0 || version.Minor > 0 || version.Build > 0))
            return $"{version.Major}.{version.Minor}.{version.Build}";

        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(infoVersion))
        {
            var plusIndex = infoVersion.IndexOf('+');
            var clean = plusIndex > 0 ? infoVersion[..plusIndex] : infoVersion;
            if (Version.TryParse(clean, out var parsed))
                return $"{parsed.Major}.{parsed.Minor}.{parsed.Build}";
        }

        return "1.0.0";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopHeartbeat();
        _httpClient.Dispose();
        _syncGate.Dispose();
    }
}
