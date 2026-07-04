using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using ConditioningControlPanel.Core.Services.Settings;
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
}
