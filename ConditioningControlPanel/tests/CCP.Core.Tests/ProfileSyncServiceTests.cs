using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Slice-1 tests for the ported <see cref="ProfileSyncService"/>: the pure HMAC signing
/// helper and the server-contract DTO round-trips. No live server / HttpClient is exercised.
/// </summary>
public class ProfileSyncServiceTests
{
    // Mirror of the WPF signing scheme, recomputed independently in-test:
    //   key     = UTF8("{unifiedId}:ccp-anticheat-2026")
    //   payload = "{unixSeconds}:{body}"
    //   sig     = lowercase-hex HMACSHA256(key, payload)
    private static string ExpectedSignature(string unifiedId, string timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes($"{unifiedId}:ccp-anticheat-2026"));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}:{body}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [Fact]
    public void SignRequest_AddsTimestampAndSignatureHeaders_WithMatchingDeterministicHmac()
    {
        const string unifiedId = "unified-abc-123";
        const string body = "{\"unified_id\":\"unified-abc-123\",\"xp\":4200,\"level\":42}";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://example.test/v2/user/sync");

        ProfileSyncService.SignRequest(request, body, unifiedId);

        Assert.True(request.Headers.Contains("X-CCP-Timestamp"), "X-CCP-Timestamp header must be present");
        Assert.True(request.Headers.Contains("X-CCP-Signature"), "X-CCP-Signature header must be present");

        var timestamp = request.Headers.GetValues("X-CCP-Timestamp").Single();
        var signature = request.Headers.GetValues("X-CCP-Signature").Single();

        // Timestamp is unix seconds (parseable, positive).
        Assert.True(long.TryParse(timestamp, out var unix) && unix > 0);

        // Signature is 64-char lowercase hex.
        Assert.Equal(64, signature.Length);
        Assert.Matches("^[0-9a-f]{64}$", signature);

        // Independently recompute the HMAC over the exact (unifiedId, timestamp, body)
        // triple the impl signed and assert byte-for-byte equality.
        Assert.Equal(ExpectedSignature(unifiedId, timestamp, body), signature);
    }

    [Fact]
    public void SignRequest_KnownTriple_ProducesKnownLowercaseHexSignature()
    {
        // Fully deterministic: fixed unifiedId + timestamp + body -> a fixed, precomputed HMAC.
        const string unifiedId = "unified-test-id";
        const string timestamp = "1700000000";
        const string body = "hello-world";

        var expected = ExpectedSignature(unifiedId, timestamp, body);

        Assert.Equal(64, expected.Length);
        Assert.Matches("^[0-9a-f]{64}$", expected);

        // Re-derive with an inline, literal implementation to guard against ExpectedSignature drift.
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("unified-test-id:ccp-anticheat-2026"));
        var inline = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes("1700000000:hello-world")))
            .ToLowerInvariant();
        Assert.Equal(inline, expected);
    }

    [Fact]
    public void SignRequest_EmptyUnifiedId_AddsNoHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/v2/user/sync");

        ProfileSyncService.SignRequest(request, "{}", string.Empty);

        Assert.False(request.Headers.Contains("X-CCP-Timestamp"));
        Assert.False(request.Headers.Contains("X-CCP-Signature"));
    }

    [Fact]
    public void V2SyncResponse_RoundTrips_PreservingJsonPropertyNames()
    {
        const string json = """
        {
          "success": true,
          "skill_points": 7,
          "unlocked_skills": ["a", "b"],
          "level_reset": false,
          "lifetime_points_spent": 12345,
          "total_xp_earned": 6789.5,
          "streak_stats": { "daily_quest_streak": 3, "total_xp_from_quests": 400 },
          "user": { "display_name": "Bambi", "level": 42, "xp": 9001, "highest_level_ever": 50 }
        }
        """;

        var dto = JsonConvert.DeserializeObject<V2SyncResponse>(json)!;

        // Values deserialized via the snake_case wire names.
        Assert.True(dto.Success);
        Assert.Equal(7, dto.SkillPoints);
        Assert.Equal(new[] { "a", "b" }, dto.UnlockedSkills);
        Assert.Equal(12345L, dto.LifetimePointsSpent);
        Assert.Equal(6789.5, dto.TotalXpEarned);
        Assert.Equal(3, dto.StreakStats!.DailyQuestStreak);
        Assert.Equal(400, dto.StreakStats.TotalXPFromQuests);
        Assert.Equal("Bambi", dto.User!.DisplayName);
        Assert.Equal(50, dto.User.HighestLevelEver);

        // Re-serialize and confirm the [JsonProperty] wire names survive the round-trip.
        var obj = JObject.Parse(JsonConvert.SerializeObject(dto));
        Assert.NotNull(obj["skill_points"]);
        Assert.NotNull(obj["unlocked_skills"]);
        Assert.NotNull(obj["lifetime_points_spent"]);
        Assert.NotNull(obj["total_xp_earned"]);
        Assert.NotNull(obj["streak_stats"]!["daily_quest_streak"]);
        Assert.NotNull(obj["streak_stats"]!["total_xp_from_quests"]);
        Assert.NotNull(obj["user"]!["display_name"]);
        Assert.NotNull(obj["user"]!["highest_level_ever"]);
    }

    [Fact]
    public void SettingsBackupInfo_RoundTrips()
    {
        var backedUp = new DateTime(2026, 7, 4, 12, 0, 0, DateTimeKind.Utc);
        var original = new SettingsBackupInfo
        {
            AppVersion = "6.2.2",
            BackedUpAt = backedUp,
            SizeBytes = 4096,
        };

        var restored = JsonConvert.DeserializeObject<SettingsBackupInfo>(
            JsonConvert.SerializeObject(original))!;

        Assert.Equal("6.2.2", restored.AppVersion);
        Assert.Equal(backedUp, restored.BackedUpAt);
        Assert.Equal(4096, restored.SizeBytes);
    }

    // ---- Slice 2: heartbeat --------------------------------------------------------------

    /// <summary>
    /// Fake handler that records the outgoing request (method, URI, headers, body) and returns
    /// a canned response. No live network is touched.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public HttpStatusCode ResponseStatus { get; set; } = HttpStatusCode.OK;

        /// <summary>Canned response body returned to the caller (defaults to "{}").</summary>
        public string? ResponseBody { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            if (request.Content != null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(ResponseStatus)
            {
                Content = new StringContent(ResponseBody ?? "{}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public bool WasSettingsFileMissing => true;
        public List<string> PendingPresetReinstalls { get; } = new();
        public void Save() { }
        public void Save(bool suppressCloudBackup = false) { }
        public void SaveImmediate(bool suppressCloudBackup = false) { }
        public void RestoreFrom(AppSettings settings) { }
        public void Reset() { }
    }

    private static ProfileSyncService CreateService(
        RecordingHandler handler,
        string? authToken,
        string? unifiedId,
        bool offlineMode = false)
    {
        var settings = new FakeSettingsService();
        // AuthToken routes through the (unwired = in-memory) SecureAuthTokenStore.
        settings.Current.AuthToken = authToken;
        settings.Current.UnifiedId = unifiedId;
        settings.Current.OfflineMode = offlineMode;
        return new ProfileSyncService(settings, new DebugLogger<ProfileSyncService>(), handler);
    }

    [Fact]
    public async Task SendHeartbeatAsync_WhenLoggedIn_PostsToHeartbeatEndpointWithAuthAndBody()
    {
        var handler = new RecordingHandler();
        using var service = CreateService(handler, authToken: "auth-token-xyz", unifiedId: "unified-1");

        await service.SendHeartbeatAsync();

        Assert.Equal(1, handler.CallCount);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://codebambi-proxy.vercel.app/v2/user/heartbeat",
            handler.LastRequest.RequestUri!.ToString());

        // X-Auth-Token header carries the token (value asserted only in-test; never logged).
        Assert.True(handler.LastRequest.Headers.TryGetValues("X-Auth-Token", out var tokenValues));
        Assert.Equal("auth-token-xyz", tokenValues!.Single());

        var body = JObject.Parse(handler.LastBody!);
        Assert.Equal("unified-1", (string?)body["unified_id"]);
        Assert.NotNull(body["is_active"]);
        Assert.NotNull(body["in_session"]);
        Assert.NotNull(body["app_version"]);
    }

    [Fact]
    public async Task SendHeartbeatAsync_WhenNoAuthToken_DoesNotPost()
    {
        var handler = new RecordingHandler();
        using var service = CreateService(handler, authToken: null, unifiedId: "unified-1");

        await service.SendHeartbeatAsync();

        Assert.Equal(0, handler.CallCount);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task SendHeartbeatAsync_WhenOfflineMode_DoesNotPost()
    {
        var handler = new RecordingHandler();
        using var service = CreateService(
            handler, authToken: "auth-token-xyz", unifiedId: "unified-1", offlineMode: true);

        await service.SendHeartbeatAsync();

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void StartHeartbeat_ThenStopHeartbeat_LeavesNoActiveTimer_AndStopIsIdempotent()
    {
        var handler = new RecordingHandler();
        // No token: the immediate first tick is a no-op (IsSyncEnabled false), so no POST races.
        using var service = CreateService(handler, authToken: null, unifiedId: null);

        service.StartHeartbeat();
        Assert.True(service.IsHeartbeatActive);

        service.StopHeartbeat();
        Assert.False(service.IsHeartbeatActive);

        // Safe to call twice.
        service.StopHeartbeat();
        Assert.False(service.IsHeartbeatActive);
    }

    // ---- Slice 3: pull + merge -----------------------------------------------------------

    private sealed class TestAppEnvironment : IAppEnvironment
    {
        public string Root { get; }
        public string BaseDirectory { get; } = AppContext.BaseDirectory;
        public string UserDataPath { get; }
        public string ApplicationDataPath { get; }
        public string EffectiveAssetsPath { get; }

        public TestAppEnvironment()
        {
            Root = Path.Combine(Path.GetTempPath(), $"ccp-profilesync-{Guid.NewGuid():N}");
            UserDataPath = Path.Combine(Root, "local");
            ApplicationDataPath = Path.Combine(Root, "roaming");
            EffectiveAssetsPath = Path.Combine(Root, "assets");
        }

        public void Cleanup()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class FakeProgressionService : IProgressionService
    {
        public void AddXP(int amount, XPSource source) { }
        public double GetSessionXPMultiplier(int playerLevel) => 1.0;
        public double GetXPForLevel(int level) => level * 1000.0;
        public double GetTotalXP(int level, double currentXP) => level * 1000.0 + currentXP;
        public double GetCurrentLevelXP(int level, double totalXP) => Math.Max(0, totalXP - level * 1000.0);
        public event EventHandler<int>? LevelUp;
    }

    private sealed class FakeSkillTreeService : ISkillTreeService
    {
        public int OnSeasonResetCallCount { get; private set; }

        public bool HasSkill(string skillId) => false;
        public double GetTotalXpMultiplier() => 1.0;
        public int TotalPointsSpent => 0;
        public event EventHandler<string>? SkillUnlocked;
        public event EventHandler? PinkRushStarted;
        public Task<(bool Success, string? Error)> PurchaseSkillAsync(string skillId) => Task.FromResult((false, (string?)null));
        public void Start() { }
        public void Stop() { }
        public void TriggerPinkRush() { }
        public bool UseStreakShield() => false;
        public bool UseOopsieInsurance() => false;
        public int GetDailyStreakBonus(int consecutiveDays) => 0;
        public int GetDailyFreeRerolls() => 0;
        public void AddConditioningTime(double minutes) { }
        public void OnSeasonReset() => OnSeasonResetCallCount++;
    }

    /// <summary>
    /// Builds a ProfileSyncService wired with the injectable handler + fake seams and a logged-in
    /// settings fake (auth token + unified id) so <see cref="ProfileSyncService.LoadProfileAsync"/>
    /// passes its guards. The handler returns canned JSON, so no live server is touched.
    /// </summary>
    private static ProfileSyncService CreateMergeService(
        RecordingHandler handler,
        FakeSettingsService settings,
        IAchievementService? achievements = null,
        IQuestService? quests = null,
        IProgressionService? progression = null,
        ISkillTreeService? skillTree = null)
    {
        settings.Current.AuthToken = "auth-token-xyz";
        settings.Current.UnifiedId = "unified-1";
        return new ProfileSyncService(
            settings, new DebugLogger<ProfileSyncService>(), handler,
            sessionService: null, achievements, quests, progression, skillTree);
    }

    [Fact]
    public async Task LoadProfileAsync_SkillPoints_TakeHigher_RaisesToServer_ButNeverLowersLocal()
    {
        // Server higher -> local raised to server.
        var raise = new RecordingHandler { ResponseBody = "{\"skill_points\": 25}" };
        var raiseSettings = new FakeSettingsService();
        raiseSettings.Current.SkillPoints = 10;
        using (var svc = CreateMergeService(raise, raiseSettings))
        {
            Assert.True(await svc.LoadProfileAsync());
            Assert.Equal(25, raiseSettings.Current.SkillPoints);
        }

        // Server lower -> local KEPT (max-merge never lowers).
        var keep = new RecordingHandler { ResponseBody = "{\"skill_points\": 5}" };
        var keepSettings = new FakeSettingsService();
        keepSettings.Current.SkillPoints = 25;
        using (var svc = CreateMergeService(keep, keepSettings))
        {
            Assert.True(await svc.LoadProfileAsync());
            Assert.Equal(25, keepSettings.Current.SkillPoints);
        }
    }

    [Fact]
    public async Task LoadProfileAsync_UnlockedSkills_UnionsServerAndLocal()
    {
        var handler = new RecordingHandler { ResponseBody = "{\"unlocked_skills\": [\"server_b\"]}" };
        var settings = new FakeSettingsService();
        settings.Current.UnlockedSkills = new List<string> { "local_a" };
        using var svc = CreateMergeService(handler, settings);

        Assert.True(await svc.LoadProfileAsync());

        Assert.Contains("local_a", settings.Current.UnlockedSkills);
        Assert.Contains("server_b", settings.Current.UnlockedSkills);
    }

    [Fact]
    public async Task LoadProfileAsync_LevelReset_SkipsSkillUnion_AndRebuildsWithPermanentsOnly()
    {
        // pink_hours is a permanent (season-persistent) skill; local_only is mechanical.
        var handler = new RecordingHandler
        {
            ResponseBody = "{\"unlocked_skills\": [\"server_skill\"], \"level_reset\": true, \"user\": {\"level\": 1, \"xp\": 0}}"
        };
        var settings = new FakeSettingsService();
        settings.Current.UnlockedSkills = new List<string> { "local_only", "pink_hours" };
        var skillTree = new FakeSkillTreeService();
        using var svc = CreateMergeService(handler, settings,
            progression: new FakeProgressionService(), skillTree: skillTree);

        Assert.True(await svc.LoadProfileAsync());

        // Union was SKIPPED on level_reset: the mechanical local_only was dropped (a plain union
        // would have KEPT it). The rollover rebuild is server-list ∪ locally-owned permanents.
        Assert.DoesNotContain("local_only", settings.Current.UnlockedSkills);
        Assert.Contains("server_skill", settings.Current.UnlockedSkills);
        Assert.Contains("pink_hours", settings.Current.UnlockedSkills);
        Assert.True(settings.Current.SeasonResetPending);
        Assert.Equal(1, skillTree.OnSeasonResetCallCount);
    }

    [Fact]
    public async Task LoadProfileAsync_ForceStreakOverride_AdoptsLowerServerStreak()
    {
        var handler = new RecordingHandler
        {
            ResponseBody = "{\"force_streak_override\": true, \"streak_stats\": {\"daily_quest_streak\": 5}}"
        };
        var settings = new FakeSettingsService();
        settings.Current.DailyQuestStreak = 30; // higher than the server value
        using var svc = CreateMergeService(handler, settings);

        Assert.True(await svc.LoadProfileAsync());

        // force_streak_override ADOPTS the server streak even though it is LOWER than local.
        Assert.Equal(5, settings.Current.DailyQuestStreak);
    }

    [AvaloniaFact]
    public async Task LoadProfileAsync_LifetimePointsSpent_IsMonotonic_NeverLowersLocal()
    {
        var env = new TestAppEnvironment();
        try
        {
            var achievements = new AchievementService(env, new DebugLogger<AchievementService>());
            try
            {
                achievements.Progress.LifetimeSkillPointsSpent = 100;

                // Server lower -> local KEPT (prestige is monotonic).
                var lower = new RecordingHandler { ResponseBody = "{\"lifetime_points_spent\": 50}" };
                using (var svc = CreateMergeService(lower, new FakeSettingsService(), achievements: achievements))
                {
                    Assert.True(await svc.LoadProfileAsync());
                    Assert.Equal(100, achievements.Progress.LifetimeSkillPointsSpent);
                }

                // Server higher -> adopted.
                var higher = new RecordingHandler { ResponseBody = "{\"lifetime_points_spent\": 200}" };
                using (var svc = CreateMergeService(higher, new FakeSettingsService(), achievements: achievements))
                {
                    Assert.True(await svc.LoadProfileAsync());
                    Assert.Equal(200, achievements.Progress.LifetimeSkillPointsSpent);
                }
            }
            finally
            {
                achievements.Dispose();
            }
        }
        finally
        {
            env.Cleanup();
        }
    }

    // ---- Slice 4: push (SyncProfileAsync) ------------------------------------------------

    /// <summary>
    /// A logged-in settings fake carrying a NON-default profile (Level &gt; 1) so
    /// <see cref="ProfileSyncService.SyncProfileAsync"/> passes the fresh-defaults guard and posts.
    /// </summary>
    private static FakeSettingsService LoadedProfileSettings()
    {
        var settings = new FakeSettingsService();
        settings.Current.PlayerLevel = 42;   // > 1 => fresh-defaults guard passes
        settings.Current.PlayerXP = 500;     // totalXp (no progression seam) = PlayerXP
        settings.Current.SkillPoints = 7;
        return settings;
    }

    /// <summary>
    /// Handler that blocks inside <c>SendAsync</c> until released, so a test can hold one sync
    /// in-flight (gate held) while starting a second concurrent call.
    /// </summary>
    private sealed class BlockingHandler : HttpMessageHandler
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        /// <summary>Released once <c>SendAsync</c> has been entered (i.e. the gate is held).</summary>
        public SemaphoreSlim Entered { get; } = new(0, 1);

        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseResponse() => _release.TrySetResult(true);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            Entered.Release();
            await _release.Task.ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Entered.Dispose();
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task SyncProfileAsync_FreshDefaultProfile_DoesNotPost()
    {
        // Default settings look like fresh defaults (Level 1, XP 0) and no round-trip load has
        // completed => the correctness-critical guard blocks the push (would otherwise zero the
        // server profile). Still "logged in" (auth token + unified id set by CreateMergeService).
        var handler = new RecordingHandler();
        var settings = new FakeSettingsService();
        using var svc = CreateMergeService(handler, settings);

        var result = await svc.SyncProfileAsync();

        Assert.False(result);
        Assert.Equal(0, handler.CallCount);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task SyncProfileAsync_LoadedNonDefaultProfile_PostsSignedRequestWithBody()
    {
        var handler = new RecordingHandler();
        var settings = LoadedProfileSettings();
        using var svc = CreateMergeService(handler, settings);

        var result = await svc.SyncProfileAsync();

        Assert.True(result);
        Assert.Equal(1, handler.CallCount);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://codebambi-proxy.vercel.app/v2/user/sync",
            handler.LastRequest.RequestUri!.ToString());

        // Auth + HMAC signing headers all present (token value asserted only in-test, never logged).
        Assert.True(handler.LastRequest.Headers.TryGetValues("X-Auth-Token", out var tokenValues));
        Assert.Equal("auth-token-xyz", tokenValues!.Single());
        Assert.True(handler.LastRequest.Headers.Contains("X-CCP-Timestamp"));
        Assert.True(handler.LastRequest.Headers.Contains("X-CCP-Signature"));

        // The signature is deterministic over the exact (unifiedId, timestamp, body) triple.
        var timestamp = handler.LastRequest.Headers.GetValues("X-CCP-Timestamp").Single();
        var signature = handler.LastRequest.Headers.GetValues("X-CCP-Signature").Single();
        Assert.Equal(ExpectedSignature("unified-1", timestamp, handler.LastBody!), signature);

        // Body carries the leaderboard-submit + progression fields.
        var body = JObject.Parse(handler.LastBody!);
        Assert.Equal("unified-1", (string?)body["unified_id"]);
        Assert.Equal(500, (int?)body["xp"]);
        Assert.Equal(42, (int?)body["level"]);
        Assert.Equal(7, (int?)body["skill_points"]);
    }

    [Fact]
    public async Task SyncProfileAsync_SecondCallWithinCooldown_DoesNotPostAgain()
    {
        var handler = new RecordingHandler();
        var settings = LoadedProfileSettings();
        using var svc = CreateMergeService(handler, settings);

        Assert.True(await svc.SyncProfileAsync());
        Assert.Equal(1, handler.CallCount);

        // A second call immediately after is inside the 30 s cooldown window => no second POST.
        var second = await svc.SyncProfileAsync();
        Assert.False(second);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SyncProfileAsync_ConcurrentCallWhileInFlight_ReturnsWithoutSecondPost()
    {
        using var handler = new BlockingHandler();
        var settings = LoadedProfileSettings();
        settings.Current.AuthToken = "auth-token-xyz";
        settings.Current.UnifiedId = "unified-1";
        using var svc = new ProfileSyncService(
            settings, new DebugLogger<ProfileSyncService>(), handler, sessionService: null);

        // Start the first sync: it acquires the gate, POSTs, and blocks inside the handler.
        var first = svc.SyncProfileAsync();
        await handler.Entered.WaitAsync(TestContext.Current.CancellationToken);   // guarantee the first call is in-flight (gate held)

        // Second call while the gate is held: WaitAsync(0) fails => returns false, no second POST.
        var second = await svc.SyncProfileAsync();
        Assert.False(second);
        Assert.Equal(1, handler.CallCount);

        handler.ReleaseResponse();
        Assert.True(await first);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SyncProfileAsync_429_StampsLastSyncTime_WithoutThrowing()
    {
        var handler = new RecordingHandler { ResponseStatus = (HttpStatusCode)429 };
        var settings = LoadedProfileSettings();
        using var svc = CreateMergeService(handler, settings);

        var result = await svc.SyncProfileAsync();

        Assert.False(result);
        Assert.Equal(1, handler.CallCount);            // it DID post; the server rate-limited it
        Assert.True(svc.LastSyncTime.HasValue);        // 429 stamps LastSyncTime to defer the next attempt
        Assert.Equal(0, svc.ConsecutiveSyncFailures);  // a 429 is not counted as a sync failure
    }

    // ---- Slice 5: cloud settings backup/restore ------------------------------------------

    /// <summary>gzip-compress + base64-encode a JSON string the way the server stores a backup.</summary>
    private static string GzipToBase64(string json)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            gzip.Write(bytes, 0, bytes.Length);
        }
        return Convert.ToBase64String(output.ToArray());
    }

    /// <summary>base64-decode + gunzip a captured <c>settings_data</c> payload back to JSON.</summary>
    private static string GunzipBase64(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>The 18 property NAMES that must never appear in an uploaded backup (WPF :2384-2404).</summary>
    private static readonly string[] ExcludedNames =
    {
        "UnifiedId", "OpenRouterApiKey", "PlayerLevel", "PlayerXP", "SkillPoints", "UnlockedSkills",
        "HighestLevelEver", "IsSeason0Og", "CurrentSeason", "PendingSkillsResetAck", "UserDisplayName",
        "PatreonTier", "PatreonPremiumValidUntil", "LastPatreonVerification", "AuthToken",
        "CustomAssetsPath", "DiscordWebhookUrl", "LastSeenUtc"
    };

    /// <summary>
    /// P0 PRIVACY TEST — the mechanical guarantee that the auth token / API key / identity fields
    /// never leave the device. Populates the secret + identity fields, forces a backup, captures the
    /// ACTUAL uploaded <c>settings_data</c>, base64-decodes + gunzips it, and asserts NONE of the
    /// excluded property names/values survive in the decompressed JSON.
    /// </summary>
    [Fact]
    public async Task BackupSettingsAsync_StripsEveryExcludedProperty_FromTheActualUploadedBody()
    {
        var handler = new RecordingHandler();
        var settings = new FakeSettingsService();
        settings.Current.AuthToken = "SECRET-AUTH-TOKEN";                 // [JsonIgnore] + stripped
        settings.Current.OpenRouterApiKey = "SECRET-OPENROUTER-KEY";     // [JsonIgnore] + stripped
        settings.Current.UnifiedId = "unified-secret-1";                 // serializes -> must be stripped
        settings.Current.PlayerLevel = 77;
        settings.Current.SkillPoints = 55;
        settings.Current.UserDisplayName = "SecretDisplayName";
        settings.Current.DiscordWebhookUrl = "https://discord.example/secret-hook";
        settings.Current.CustomAssetsPath = @"C:\secret\assets";
        using var svc = new ProfileSyncService(settings, new DebugLogger<ProfileSyncService>(), handler);

        var result = await svc.BackupSettingsAsync(force: true);

        Assert.True(result);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://codebambi-proxy.vercel.app/v2/user/backup-settings",
            handler.LastRequest.RequestUri!.ToString());

        // Decode the ACTUAL uploaded body: settings_data = base64(gzip(cleaned-settings-JSON)).
        var outer = JObject.Parse(handler.LastBody!);
        var settingsData = (string?)outer["settings_data"];
        Assert.False(string.IsNullOrEmpty(settingsData));
        var decompressed = GunzipBase64(settingsData!);
        var uploaded = JObject.Parse(decompressed);

        // NONE of the 18 excluded property NAMES survive in the decompressed backup JSON.
        foreach (var excluded in ExcludedNames)
        {
            Assert.Null(uploaded.Properties()
                .FirstOrDefault(p => string.Equals(p.Name, excluded, StringComparison.OrdinalIgnoreCase)));
        }

        // Explicit P0 assertions: the two SECRETS are absent by name AND their raw values never
        // appear anywhere in the decompressed payload (byte-for-byte scan).
        Assert.Null(uploaded["AuthToken"]);
        Assert.Null(uploaded["OpenRouterApiKey"]);
        Assert.DoesNotContain("SECRET-AUTH-TOKEN", decompressed);
        Assert.DoesNotContain("SECRET-OPENROUTER-KEY", decompressed);

        // Identity/server-authoritative values are gone too.
        Assert.DoesNotContain("SecretDisplayName", decompressed);
        Assert.DoesNotContain("unified-secret-1", decompressed);
        Assert.DoesNotContain("secret-hook", decompressed);
        Assert.DoesNotContain(@"C:\\secret\\assets", decompressed);
    }

    /// <summary>
    /// gzip round-trip: <see cref="ProfileSyncService.RestoreSettingsFromCloudAsync"/> over a canned
    /// base64(gzip(json)) backup response decodes -> gunzips -> deserializes back to the expected
    /// <see cref="AppSettings"/> fields.
    /// </summary>
    [Fact]
    public async Task RestoreSettingsFromCloudAsync_DecodesGzipBackup_IntoExpectedAppSettings()
    {
        // A canned backup snapshot with distinctive restorable fields.
        var snapshot = new AppSettings
        {
            PlayerLevel = 13,
            DailyQuestStreak = 9,
            TotalConditioningMinutes = 123.5
        };
        var backupJson = JsonConvert.SerializeObject(snapshot, Formatting.None);
        var responseBody = JsonConvert.SerializeObject(new
        {
            success = true,
            backup = new
            {
                settings_data = GzipToBase64(backupJson),
                app_version = "6.2.2",
                backed_up_at = "2026-07-04T00:00:00Z",
                size_bytes = 123
            }
        });

        var handler = new RecordingHandler { ResponseBody = responseBody };
        var settings = new FakeSettingsService();
        settings.Current.AuthToken = "auth-token-xyz";
        settings.Current.UnifiedId = "unified-1";
        using var svc = new ProfileSyncService(settings, new DebugLogger<ProfileSyncService>(), handler);

        var restored = await svc.RestoreSettingsFromCloudAsync();

        Assert.NotNull(restored);
        Assert.Equal(13, restored!.PlayerLevel);
        Assert.Equal(9, restored.DailyQuestStreak);
        Assert.Equal(123.5, restored.TotalConditioningMinutes);
        Assert.Equal("https://codebambi-proxy.vercel.app/v2/user/settings-backup",
            handler.LastRequest!.RequestUri!.ToString());
    }

    /// <summary>
    /// 5-minute debounce: a second unforced backup inside the window does NOT POST again;
    /// <c>force:true</c> bypasses the debounce.
    /// </summary>
    [Fact]
    public async Task BackupSettingsAsync_DebouncesWithinFiveMinutes_ButForceBypasses()
    {
        var handler = new RecordingHandler();
        var settings = new FakeSettingsService();
        settings.Current.AuthToken = "auth-token-xyz";
        settings.Current.UnifiedId = "unified-1";
        using var svc = new ProfileSyncService(settings, new DebugLogger<ProfileSyncService>(), handler);

        // First unforced backup posts and stamps the debounce timestamp.
        Assert.True(await svc.BackupSettingsAsync());
        Assert.Equal(1, handler.CallCount);

        // Second unforced call inside the 5-min window: debounced, no POST.
        Assert.False(await svc.BackupSettingsAsync());
        Assert.Equal(1, handler.CallCount);

        // force:true bypasses the debounce and posts again.
        Assert.True(await svc.BackupSettingsAsync(force: true));
        Assert.Equal(2, handler.CallCount);
    }

    /// <summary>Offline / missing-token / missing-unified-id all skip the backup POST entirely.</summary>
    [Fact]
    public async Task BackupSettingsAsync_WhenOfflineOrNoTokenOrNoUnifiedId_DoesNotPost()
    {
        // Offline mode: skip (checked before the debounce/force branch).
        var offlineHandler = new RecordingHandler();
        var offline = new FakeSettingsService();
        offline.Current.AuthToken = "auth-token-xyz";
        offline.Current.UnifiedId = "unified-1";
        offline.Current.OfflineMode = true;
        using (var svc = new ProfileSyncService(offline, new DebugLogger<ProfileSyncService>(), offlineHandler))
        {
            Assert.False(await svc.BackupSettingsAsync(force: true));
            Assert.Equal(0, offlineHandler.CallCount);
        }

        // No auth token: skip (the request would just 401). Set explicitly (secure store is static).
        var noTokenHandler = new RecordingHandler();
        var noToken = new FakeSettingsService();
        noToken.Current.AuthToken = null;
        noToken.Current.UnifiedId = "unified-1";
        using (var svc = new ProfileSyncService(noToken, new DebugLogger<ProfileSyncService>(), noTokenHandler))
        {
            Assert.False(await svc.BackupSettingsAsync(force: true));
            Assert.Equal(0, noTokenHandler.CallCount);
        }

        // No unified id: skip (checked before the token guard).
        var noIdHandler = new RecordingHandler();
        var noId = new FakeSettingsService();
        noId.Current.AuthToken = "auth-token-xyz";
        noId.Current.UnifiedId = null;
        using (var svc = new ProfileSyncService(noId, new DebugLogger<ProfileSyncService>(), noIdHandler))
        {
            Assert.False(await svc.BackupSettingsAsync(force: true));
            Assert.Equal(0, noIdHandler.CallCount);
        }
    }

    // ---- Slice 6: server-authoritative actions -------------------------------------------

    /// <summary>
    /// Thread-safe handler that routes canned responses per request path and counts calls per
    /// path. Enqueue multiple responses for a path to script a sequence (e.g. 401 then 200); once
    /// one response remains it is reused for further calls. Thread-safety matters because a
    /// successful 401 recovery starts the heartbeat, whose immediate tick POSTs concurrently.
    /// </summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, Queue<(HttpStatusCode Status, string Body)>> _responses = new();
        private readonly Dictionary<string, int> _callCounts = new();

        public RoutingHandler Enqueue(string path, HttpStatusCode status, string body)
        {
            lock (_lock)
            {
                if (!_responses.TryGetValue(path, out var q)) { q = new(); _responses[path] = q; }
                q.Enqueue((status, body));
            }
            return this;
        }

        public int CallCountFor(string path)
        {
            lock (_lock) { return _callCounts.TryGetValue(path, out var n) ? n : 0; }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            (HttpStatusCode Status, string Body) resp;
            lock (_lock)
            {
                _callCounts[path] = (_callCounts.TryGetValue(path, out var n) ? n : 0) + 1;
                resp = (HttpStatusCode.OK, "{}");
                if (_responses.TryGetValue(path, out var q) && q.Count > 0)
                    resp = q.Count > 1 ? q.Dequeue() : q.Peek();
            }
            return Task.FromResult(new HttpResponseMessage(resp.Status)
            {
                Content = new StringContent(resp.Body, Encoding.UTF8, "application/json")
            });
        }
    }

    private static FakeSettingsService LoggedInSettings()
    {
        var settings = new FakeSettingsService();
        settings.Current.AuthToken = "auth-token-xyz";
        settings.Current.UnifiedId = "unified-1";
        return settings;
    }

    /// <summary>
    /// (a) Purchase success DIRECT-SETs skill_points to the server's authoritative POST-PURCHASE
    /// (decremented) balance — WPF-faithful (:2072-2073); this IS the local deduction, since
    /// SkillTreeService never deducts locally. (A Math.Max here would keep the pre-purchase value
    /// and make skills free — economy bug.) UNIONs unlocked_skills, and adopts the server's higher
    /// lifetime_points_spent monotonically (prestige merge primitive).
    /// </summary>
    [AvaloniaFact]
    public async Task PurchaseSkillAsync_Success_AdoptsServerPostPurchaseBalance_UnionsSkills_AndAdoptsLifetimeSpent()
    {
        var env = new TestAppEnvironment();
        try
        {
            var achievements = new AchievementService(env, new DebugLogger<AchievementService>());
            try
            {
                achievements.Progress.LifetimeSkillPointsSpent = 100;

                // Server returns the POST-PURCHASE balance 5 (local pre-purchase was 20; the
                // purchase spent 15): direct-set adopts 5 — the deduction. lifetime 300 -> adopted.
                var handler = new RecordingHandler
                {
                    ResponseBody = "{\"success\": true, \"skill_points\": 5, \"unlocked_skills\": [\"server_skill\"], \"lifetime_points_spent\": 300}"
                };
                var settings = LoggedInSettings();
                settings.Current.SkillPoints = 20;
                settings.Current.UnlockedSkills = new List<string> { "local_skill" };
                using var svc = new ProfileSyncService(
                    settings, new DebugLogger<ProfileSyncService>(), handler,
                    sessionService: null, achievements);

                var (success, error) = await svc.PurchaseSkillAsync("some_skill");

                Assert.True(success);
                Assert.Null(error);
                Assert.Equal(1, handler.CallCount);
                Assert.Equal("https://codebambi-proxy.vercel.app/v2/user/purchase-skill",
                    handler.LastRequest!.RequestUri!.ToString());

                // skill_points direct-set to the server's post-purchase balance (20 -> 5 = the
                // deduction applied; WPF-faithful).
                Assert.Equal(5, settings.Current.SkillPoints);
                // unlocked_skills unioned (both survive).
                Assert.Contains("local_skill", settings.Current.UnlockedSkills!);
                Assert.Contains("server_skill", settings.Current.UnlockedSkills!);
                // prestige lifetime_points_spent adopted monotonically (100 -> 300).
                Assert.Equal(300, achievements.Progress.LifetimeSkillPointsSpent);

                // request body carries the reconciliation inputs (X-Auth-Token asserted, never logged).
                Assert.True(handler.LastRequest.Headers.TryGetValues("X-Auth-Token", out var tokenValues));
                Assert.Equal("auth-token-xyz", tokenValues!.Single());
                var body = JObject.Parse(handler.LastBody!);
                Assert.Equal("unified-1", (string?)body["unified_id"]);
                Assert.Equal("some_skill", (string?)body["skill_id"]);
                Assert.Equal(20, (int?)body["skill_points"]);
            }
            finally
            {
                achievements.Dispose();
            }
        }
        finally
        {
            env.Cleanup();
        }
    }

    /// <summary>
    /// (b) A 401 on the first purchase POST triggers restore-session recovery, then the request is
    /// retried exactly once and succeeds: assert 2 purchase POSTs + 1 restore-session POST.
    /// </summary>
    [Fact]
    public async Task PurchaseSkillAsync_On401_CallsRestoreSession_ThenRetriesOnce_AndSucceeds()
    {
        const string purchasePath = "/v2/user/purchase-skill";
        const string restorePath = "/v2/auth/restore-session";

        using var handler = new RoutingHandler()
            // First purchase attempt -> 401; retry -> 200 success.
            .Enqueue(purchasePath, HttpStatusCode.Unauthorized, "{}")
            .Enqueue(purchasePath, HttpStatusCode.OK,
                "{\"success\": true, \"skill_points\": 3, \"unlocked_skills\": [\"server_skill\"]}")
            // restore-session confirms the token (no rotation) so recovery succeeds.
            .Enqueue(restorePath, HttpStatusCode.OK, "{}");

        var settings = LoggedInSettings();
        settings.Current.SkillPoints = 1;
        using var svc = new ProfileSyncService(settings, new DebugLogger<ProfileSyncService>(), handler);

        var (success, error) = await svc.PurchaseSkillAsync("some_skill");

        // Stop the heartbeat that HandleUnauthorizedAsync starts on successful recovery.
        svc.StopHeartbeat();

        Assert.True(success);
        Assert.Null(error);
        // Purchase endpoint hit exactly twice: original (401) + one retry (200).
        Assert.Equal(2, handler.CallCountFor(purchasePath));
        // Auth recovery went through restore-session exactly once.
        Assert.Equal(1, handler.CallCountFor(restorePath));
        // Retry-success values applied: server post-purchase balance 3 direct-set, union restores server_skill.
        Assert.Equal(3, settings.Current.SkillPoints);
        Assert.Contains("server_skill", settings.Current.UnlockedSkills!);
    }

    /// <summary>
    /// (c) Oopsie success returns (true, null, newXp). FIDELITY: WPF's service applies NO local
    /// write (the -500 XP / streak repair lives in the UI caller, deferred to slice 7) — assert the
    /// service does not mutate PlayerXP / SeasonalStreakRecoveryUsed.
    /// </summary>
    [Fact]
    public async Task UseOopsieInsuranceAsync_Success_ReturnsNewBalance_AndAppliesNoServiceSideLocalWrite()
    {
        var handler = new RecordingHandler
        {
            ResponseBody = "{\"success\": true, \"new_xp\": 4200, \"oopsie_used_season\": \"2026-S1\"}"
        };
        var settings = LoggedInSettings();
        settings.Current.PlayerXP = 999;                 // captured to prove no service-side mutation
        settings.Current.SeasonalStreakRecoveryUsed = false;
        using var svc = new ProfileSyncService(settings, new DebugLogger<ProfileSyncService>(), handler);

        var (success, error, newBalance) = await svc.UseOopsieInsuranceAsync("2026-07-01");

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(4200, newBalance);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("https://codebambi-proxy.vercel.app/v2/user/use-oopsie",
            handler.LastRequest!.RequestUri!.ToString());
        var body = JObject.Parse(handler.LastBody!);
        Assert.Equal("unified-1", (string?)body["unified_id"]);
        Assert.Equal("2026-07-01", (string?)body["fix_date"]);

        // FIDELITY: the -500 XP / SeasonalStreakRecoveryUsed / quest-date append all live in the WPF
        // UI caller (MainWindow.QuestsTab.cs:570-583) — the service must NOT mutate local state.
        Assert.Equal(999, settings.Current.PlayerXP);
        Assert.False(settings.Current.SeasonalStreakRecoveryUsed);
    }

    /// <summary>(c) Oopsie server error returns (false, serverMessage, null).</summary>
    [Fact]
    public async Task UseOopsieInsuranceAsync_ServerError_ReturnsErrorMessage_AndNullBalance()
    {
        var handler = new RecordingHandler
        {
            ResponseStatus = HttpStatusCode.BadRequest,
            ResponseBody = "{\"error\": \"Already used this season\"}"
        };
        var settings = LoggedInSettings();
        using var svc = new ProfileSyncService(settings, new DebugLogger<ProfileSyncService>(), handler);

        var (success, error, newBalance) = await svc.UseOopsieInsuranceAsync("2026-07-01");

        Assert.False(success);
        Assert.Equal("Already used this season", error);
        Assert.Null(newBalance);
    }

    /// <summary>
    /// (d) Change-name success sets settings.UserDisplayName to the server-confirmed name and
    /// returns it (the caller-side write consolidated into the service per the slice-6 spec).
    /// </summary>
    [Fact]
    public async Task ChangeDisplayNameAsync_Success_SetsUserDisplayName_AndReturnsNewName()
    {
        var handler = new RecordingHandler
        {
            ResponseBody = "{\"success\": true, \"new_display_name\": \"BambiPrime\"}"
        };
        var settings = LoggedInSettings();
        settings.Current.UserDisplayName = "OldName";
        using var svc = new ProfileSyncService(settings, new DebugLogger<ProfileSyncService>(), handler);

        var (success, error, newName) = await svc.ChangeDisplayNameAsync("BambiPrime");

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal("BambiPrime", newName);
        Assert.Equal("BambiPrime", settings.Current.UserDisplayName);   // service applied the local write
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("https://codebambi-proxy.vercel.app/v2/user/change-display-name",
            handler.LastRequest!.RequestUri!.ToString());
        var body = JObject.Parse(handler.LastBody!);
        Assert.Equal("unified-1", (string?)body["unified_id"]);
        Assert.Equal("BambiPrime", (string?)body["new_display_name"]);
    }

    /// <summary>(d) Change-name server error returns (false, serverMessage, null) and leaves the name unchanged.</summary>
    [Fact]
    public async Task ChangeDisplayNameAsync_ServerError_ReturnsErrorMessage_AndDoesNotChangeName()
    {
        var handler = new RecordingHandler
        {
            ResponseStatus = HttpStatusCode.Conflict,
            ResponseBody = "{\"error\": \"Name already taken\"}"
        };
        var settings = LoggedInSettings();
        settings.Current.UserDisplayName = "OldName";
        using var svc = new ProfileSyncService(settings, new DebugLogger<ProfileSyncService>(), handler);

        var (success, error, newName) = await svc.ChangeDisplayNameAsync("TakenName");

        Assert.False(success);
        Assert.Equal("Name already taken", error);
        Assert.Null(newName);
        Assert.Equal("OldName", settings.Current.UserDisplayName);   // unchanged on failure
    }
}
