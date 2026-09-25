using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Instant unlock for a tier bought on the site: the server's folded <c>effective_tier</c> is
/// read off the heartbeat and the profile, a rise is applied at once, a drop never revokes, and
/// each tier gets its own celebration card.
/// </summary>
public class EntitlementTierRuleTests
{
    [Theory]
    [InlineData("{\"ok\":true,\"effective_tier\":2}", 2)]
    [InlineData("{\"effective_tier\":1,\"progression\":{\"level\":3}}", 1)]
    [InlineData("{\"effective_tier\":0}", 0)]
    public void Heartbeat_ReadsTopLevelTier(string body, int expected)
        => Assert.Equal(expected, EntitlementTierRule.ParseHeartbeatTier(body));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{}")]
    [InlineData("{\"effective_tier\":null}")]
    [InlineData("{\"effective_tier\":\"2\"}")]
    [InlineData("{\"effective_tier\":1.5}")]
    [InlineData("{\"effective_tier\":3}")]
    [InlineData("{\"effective_tier\":-1}")]
    [InlineData("{\"effective_tier\":99999999999999}")]
    [InlineData("{\"progression\":{\"effective_tier\":2}}")]
    public void Heartbeat_GarbageIsNoSignal(string? body)
        => Assert.Null(EntitlementTierRule.ParseHeartbeatTier(body));

    [Fact]
    public void ProfileNode_TierParsesThroughTheSameRule()
    {
        var user = JObject.Parse("{\"patreon_tier\":0,\"effective_tier\":2}");
        Assert.Equal(2, EntitlementTierRule.ParseTier(user["effective_tier"]));
        Assert.Null(EntitlementTierRule.ParseTier(JObject.Parse("{}")["effective_tier"]));
    }

    [Theory]
    [InlineData(0, 1, EntitlementVerdict.Rise)]
    [InlineData(0, 2, EntitlementVerdict.Rise)]
    [InlineData(1, 2, EntitlementVerdict.Rise)]
    [InlineData(1, 1, EntitlementVerdict.Same)]
    [InlineData(0, 0, EntitlementVerdict.Same)]
    [InlineData(2, 1, EntitlementVerdict.Lower)]
    [InlineData(1, 0, EntitlementVerdict.Lower)]
    [InlineData(5, 2, EntitlementVerdict.Same)]   // a corrupt stored tier clamps, never reads as a drop
    [InlineData(-3, 1, EntitlementVerdict.Rise)]
    public void Decide(int stored, int server, EntitlementVerdict expected)
        => Assert.Equal(expected, EntitlementTierRule.Decide(stored, server));

    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    [InlineData(-1)]
    public void Decide_NoReadingIsNoSignal(int? server)
        => Assert.Equal(EntitlementVerdict.NoSignal, EntitlementTierRule.Decide(1, server));

    private static readonly DateTime Now = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Grace_Tier1StampsPremiumOnly()
    {
        var s = new AppSettings();
        EntitlementTierRule.ExtendGrace(s, 1, Now);
        Assert.Equal(Now.AddDays(14), s.PatreonPremiumValidUntil);
        Assert.Null(s.PatreonLabValidUntil);
    }

    [Fact]
    public void Grace_Tier2StampsBoth()
    {
        var s = new AppSettings();
        EntitlementTierRule.ExtendGrace(s, 2, Now);
        Assert.Equal(Now.AddDays(14), s.PatreonPremiumValidUntil);
        Assert.Equal(Now.AddDays(14), s.PatreonLabValidUntil);
    }

    [Fact]
    public void Grace_NeverShortensAndTier0TouchesNothing()
    {
        var later = Now.AddDays(40);
        var s = new AppSettings { PatreonPremiumValidUntil = later, PatreonLabValidUntil = later };
        EntitlementTierRule.ExtendGrace(s, 2, Now);
        Assert.Equal(later, s.PatreonPremiumValidUntil);
        Assert.Equal(later, s.PatreonLabValidUntil);

        var free = new AppSettings();
        EntitlementTierRule.ExtendGrace(free, 0, Now);
        Assert.Null(free.PatreonPremiumValidUntil);
        Assert.Null(free.PatreonLabValidUntil);
    }

    [Fact]
    public void Celebration_KeysPerTier()
    {
        Assert.Null(TierCelebration.KeyFor(0));
        Assert.Equal("premium-celebration-t1", TierCelebration.KeyFor(1));
        Assert.Equal("premium-celebration-t2", TierCelebration.KeyFor(2));
        // The legacy key is the one FeatureIntroPopup has always spent.
        Assert.Equal(FeatureIntroPopup.CelebrationKey, TierCelebration.LegacyKey);
    }

    [Theory]
    // fresh install: owed on launch and on a rise
    [InlineData(new string[0], 1, false, true)]
    [InlineData(new string[0], 2, true, true)]
    [InlineData(new string[0], 0, true, false)]
    // an install celebrated before per-tier keys: the launch re-check does not repeat it...
    [InlineData(new[] { "premium-celebration" }, 1, false, false)]
    [InlineData(new[] { "premium-celebration" }, 2, false, false)]
    // ...but a live purchase does celebrate (a past patron coming back, or Basic -> Prime)
    [InlineData(new[] { "premium-celebration" }, 1, true, true)]
    [InlineData(new[] { "premium-celebration" }, 2, true, true)]
    // Basic seen, Prime owed; each tier only once
    [InlineData(new[] { "premium-celebration-t1" }, 2, true, true)]
    [InlineData(new[] { "premium-celebration-t1" }, 2, false, true)]
    [InlineData(new[] { "premium-celebration-t1" }, 1, true, false)]
    [InlineData(new[] { "premium-celebration-t2" }, 2, true, false)]
    public void Celebration_IsOwed(string[] seen, int tier, bool onRise, bool expected)
        => Assert.Equal(expected, TierCelebration.IsOwed(new List<string>(seen), tier, onRise));

    [Fact]
    public void Celebration_EveryKeyHasACard()
    {
        Assert.True(FeatureIntros.All.ContainsKey(TierCelebration.KeyT1));
        Assert.True(FeatureIntros.All.ContainsKey(TierCelebration.KeyT2));
        Assert.True(FeatureIntros.All.ContainsKey(TierCelebration.LegacyKey));
        Assert.Contains("Prime", FeatureIntros.All[TierCelebration.KeyT2].Title);
    }
}
