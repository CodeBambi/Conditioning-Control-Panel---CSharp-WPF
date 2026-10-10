using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// <c>force_skills_reset</c> on a sync reply, against WPF ProfileSyncService.cs:2017-2046 and :1876: the reset
/// is applied once, armed on disk, acknowledged with <c>force_skills_reset: false</c> on the next push and
/// disarmed when the server stops sending the flag. The wallet only ever rises here.
/// </summary>
public sealed class SkillsResetAckTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static AppSettings With(int sp, params string[] skills) => new()
    {
        UnifiedId = "u_1", SkillPoints = sp, PlayerLevel = 7, UnlockedSkills = new List<string>(skills),
    };

    private static JObject BodyOf(AppSettings s) => JObject.Parse(JsonConvert.SerializeObject(SyncPush.Body(s, null)));

    [Fact]
    public void TheResetClearsTheTree_RefundsThePoints_AndArmsTheAck()
    {
        var s = With(3, "sparkle_boost_1", "pink_rush");
        ProfileAdopt.ApplySyncResponse(s, JObject.Parse("{\"skill_points\":40,\"force_skills_reset\":true}"), Now);
        Assert.Empty(s.UnlockedSkills);
        Assert.Equal(40, s.SkillPoints);
        Assert.True(s.PendingSkillsResetAck);
    }

    [Fact]
    public void ARefundBelowTheWalletNeverLowersIt()
    {
        var s = With(55, "sparkle_boost_1");
        ProfileAdopt.ApplySyncResponse(s, JObject.Parse("{\"skill_points\":40,\"force_skills_reset\":true}"), Now);
        Assert.Empty(s.UnlockedSkills);
        Assert.Equal(55, s.SkillPoints);   // a sync reply is a snapshot: it may only raise
        Assert.True(s.PendingSkillsResetAck);
    }

    [Fact]
    public void WithNoBalanceInTheReply_TheRefundIsOnePointPerLevel()
    {
        var s = With(2, "sparkle_boost_1");
        ProfileAdopt.ApplySyncResponse(s, JObject.Parse("{\"force_skills_reset\":true}"), Now);
        Assert.Equal(7, s.SkillPoints);
        Assert.True(s.PendingSkillsResetAck);
    }

    [Fact]
    public void AnArmedAckIsNotAppliedTwice_AndSkillsBoughtSinceSurvive()
    {
        var s = With(3, "sparkle_boost_1");
        var flagged = JObject.Parse("{\"skill_points\":40,\"force_skills_reset\":true}");
        ProfileAdopt.ApplySyncResponse(s, flagged, Now);
        s.UnlockedSkills.Add("sparkle_boost_1");   // bought again before the ack reached the server
        s.SkillPoints = 35;
        ProfileAdopt.ApplySyncResponse(s, flagged, Now);
        Assert.Single(s.UnlockedSkills);
        Assert.Equal(40, s.SkillPoints);           // WPF's third branch: the plain take-higher adopt
        Assert.True(s.PendingSkillsResetAck);
    }

    [Fact]
    public void TheNextPushAcknowledges_AndAReplyWithoutTheFlagDisarms()
    {
        var s = With(3, "sparkle_boost_1");
        Assert.Null(BodyOf(s)["force_skills_reset"]);                    // nothing to acknowledge: the key is left out
        ProfileAdopt.ApplySyncResponse(s, JObject.Parse("{\"skill_points\":40,\"force_skills_reset\":true}"), Now);

        var ack = BodyOf(s)["force_skills_reset"];
        Assert.NotNull(ack);
        Assert.Equal(JTokenType.Boolean, ack!.Type);
        Assert.False(ack.Value<bool>());

        ProfileAdopt.ApplySyncResponse(s, JObject.Parse("{\"skill_points\":12}"), Now);
        Assert.False(s.PendingSkillsResetAck);
        Assert.Equal(40, s.SkillPoints);                                 // the disarming reply does not lower either
        Assert.Null(BodyOf(s)["force_skills_reset"]);
    }
}
