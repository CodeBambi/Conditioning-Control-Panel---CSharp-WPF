using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The sync/heartbeat/HMAC wire, pinned to strings RECORDED from WPF's pre-SyncBody code
/// (the anonymous object in ProfileSyncService). Recipe: Fixtures/Sync/record-goldens.cs.txt.
/// </summary>
public class SyncBodyGoldenTests
{
    private const string Head = "{\"unified_id\":\"u_abc123\",\"xp\":12345,\"level\":40,\"achievements\":[\"first_flash\",\"night_owl\"],\"stats\":{\"completed_sessions\":3,\"total_video_minutes\":12.3,\"last_streak_date\":\"2026-09-01\",\"quest_completion_dates\":[\"2026-08-31\"]},\"unlocked_skills\":[\"skill_a\"],\"skill_points\":2,\"total_conditioning_minutes\":90.5,";
    private const string Consents = "\"allow_discord_dm\":true,\"show_online_status\":false,\"share_profile_picture\":true,\"public_share_avatar\":false,\"goon_share_avatar\":true,\"goon_share_dm\":false,";

    private const string Full = Head + "\"companion_progress\":{\"0\":{\"CompanionId\":0,\"Level\":1,\"CurrentXP\":0.0,\"TotalXPEarned\":0.0,\"FirstActivated\":\"0001-01-01T00:00:00\",\"TotalActiveTime\":\"00:00:00\"}}," + Consents
        + "\"cosmetics\":{\"banner_id\":\"b1\",\"accent\":\"#FF69B4\",\"title_id\":null,\"pinned_achievements\":[],\"avatar_deco\":null,\"charms\":[]},\"web_xp_claim_ack\":\"claim-7\",\"install_date\":\"2025-01-02\",\"descent_epoch\":1,\"reset_weekly_quest\":false,\"reset_daily_quest\":false,\"force_streak_override\":false,\"force_skills_reset\":false}";

    private const string Fresh = Head + "\"companion_progress\":{}," + Consents
        + "\"cosmetics\":null,\"web_xp_claim_ack\":null,\"install_date\":null,\"descent_epoch\":1,\"reset_weekly_quest\":false,\"reset_daily_quest\":false,\"force_streak_override\":false,\"force_skills_reset\":null}";

    private static SyncBody Body(bool fresh) => new()
    {
        Known = SyncBody.Field.All,
        UnifiedId = "u_abc123",
        Xp = 12345,
        Level = 40,
        Achievements = new List<string> { "first_flash", "night_owl" },
        Stats = new Dictionary<string, object>
        {
            ["completed_sessions"] = 3,
            ["total_video_minutes"] = System.Math.Round(12.34, 1),
            ["last_streak_date"] = "2026-09-01",
            ["quest_completion_dates"] = new List<string> { "2026-08-31" },
        },
        UnlockedSkills = new List<string> { "skill_a" },
        SkillPoints = 2,
        TotalConditioningMinutes = 90.5,
        CompanionProgress = fresh ? new Dictionary<int, CompanionProgress>() : new Dictionary<int, CompanionProgress> { [0] = new CompanionProgress() },
        AllowDiscordDm = true,
        ShareProfilePicture = true,
        GoonShareAvatar = true,
        Cosmetics = fresh ? null : new ProfileCosmetics { BannerId = "b1", Accent = "#FF69B4" },
        WebXpClaimAck = fresh ? null : "claim-7",
        InstallDate = fresh ? null : "2025-01-02",
        DescentEpoch = 1,
        ForceSkillsReset = fresh ? null : false,
    };

    [Fact]
    public void FullBodyMatchesWpfBytes() => Assert.Equal(Full, JsonConvert.SerializeObject(Body(false)));

    [Fact]
    public void FreshBodyKeepsWpfExplicitNulls() => Assert.Equal(Fresh, JsonConvert.SerializeObject(Body(true)));

    [Fact]
    public void MigrationGraftMatchesWpfBytes()
    {
        var payload = JObject.Parse(JsonConvert.SerializeObject(Body(true)));
        payload["descent_migration"] = new JObject { ["choice"] = "keep" };
        Assert.Equal(Fresh[..^1] + ",\"descent_migration\":{\"choice\":\"keep\"}}", payload.ToString(Formatting.None));
    }

    [Fact]
    public void UnknownFieldsAreOmittedNotNull()
    {
        var body = Body(true);
        body.Known = SyncBody.Field.UnifiedId | SyncBody.Field.Xp | SyncBody.Field.Cosmetics;
        Assert.Equal("{\"unified_id\":\"u_abc123\",\"xp\":12345,\"cosmetics\":null}", JsonConvert.SerializeObject(body));
    }

    [Fact]
    public void SignatureMatchesWpfHmac() =>
        Assert.Equal("71ba2d4340c65c928cf05df8b11d17e44d903ebd1ae63425b9b574bc4786ea50",
            SyncBody.Signature("u_abc123", "1760000000", "{\"xp\":1}"));

    [Fact]
    public void HeartbeatIsWpfsFourFieldBody() =>
        Assert.Equal("{\"unified_id\":\"u_abc123\",\"is_active\":true,\"in_session\":false,\"app_version\":\"6.11.3\"}",
            SyncBody.Heartbeat("u_abc123", true, false, "6.11.3"));
}
