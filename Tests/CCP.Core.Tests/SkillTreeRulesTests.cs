using System.Collections.Generic;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests;

public class SkillTreeRulesTests
{
    [Fact]
    public void OwnershipQueriesReadUnlockedSkills()
    {
        var s = new AppSettings { UnlockedSkills = new List<string> { "sparkle_boost_1", "sparkle_boost_2", "reroll_addict" } };
        Assert.True(SkillTreeRules.HasSkill(s, "reroll_addict"));
        Assert.False(SkillTreeRules.HasSkill(s, "quest_refresh"));
        Assert.Equal(2, SkillTreeRules.GetSparkleBoostTier(s));
        Assert.Equal(2, SkillTreeRules.GetDailyFreeRerolls(s));
        Assert.Equal(1.0, SkillTreeRules.GetRerollBonusMultiplier(s));
        s.UnlockedSkills = new List<string> { "better_quests", "quest_refresh" };
        Assert.Equal(1.25, SkillTreeRules.GetRerollBonusMultiplier(s));
        s.UnlockedSkills.Add("sparkle_boost_3");
        Assert.Equal(3, SkillTreeRules.GetSparkleBoostTier(s));
        Assert.Equal(1, SkillTreeRules.GetDailyFreeRerolls(s));
    }

    [Fact]
    public void CanPurchaseNeedsPointsPrerequisiteAndSecretRequirement()
    {
        var s = new AppSettings { SkillPoints = 1000, UnlockedSkills = new List<string> { "pink_hours" } };
        Assert.True(SkillTreeRules.CanPurchaseSkill(s, "sparkle_boost_1"));
        Assert.False(SkillTreeRules.CanPurchaseSkill(s, "sparkle_boost_2")); // prerequisite missing
        s.UnlockedSkills.Add("sparkle_boost_1");
        Assert.False(SkillTreeRules.CanPurchaseSkill(s, "sparkle_boost_1")); // owned
        Assert.True(SkillTreeRules.CanPurchaseSkill(s, "sparkle_boost_2"));
        s.SkillPoints = 0;
        Assert.False(SkillTreeRules.CanPurchaseSkill(s, "sparkle_boost_2")); // unaffordable

        s.SkillPoints = 1000;
        s.HighestLevelEver = 49;
        Assert.False(SkillTreeRules.CanPurchaseSkill(s, "eternal_doll"));
        s.HighestLevelEver = 50;
        Assert.True(SkillTreeRules.CanPurchaseSkill(s, "eternal_doll"));
        s.EarlyMorningUsageCount = 9;
        Assert.False(SkillTreeRules.IsSecretSkillAvailable(s, "early_bird_bimbo"));
        s.NightTimeUsageCount = 10;
        Assert.True(SkillTreeRules.IsSecretSkillAvailable(s, "night_shift"));
        Assert.False(SkillTreeRules.CanPurchaseSkill(s, "no_such_skill"));
    }

    [Fact]
    public void MultiplierAndBreakdownStayInStep()
    {
        var s = new AppSettings { CurrentStreak = 40, UnlockedSkills = new List<string>
            { "sparkle_boost_1", "sparkle_boost_2", "streak_power", "night_shift", "early_bird_bimbo", "pink_rush" } };
        // 1 + .10 + .15 + .15 (streak capped) + .50 (night at 23:00)
        Assert.Equal(1.90, SkillTreeRules.GetTotalXpMultiplier(s, 23, 0), 6);
        Assert.Equal(1.90, SkillTreeRules.GetTotalXpMultiplier(s, 6, 0), 6);   // early bird instead
        Assert.Equal(1.40, SkillTreeRules.GetTotalXpMultiplier(s, 12, 0), 6);  // neither
        Assert.Equal(1.40, SkillTreeRules.GetTotalXpMultiplier(s, 12, -5), 6); // clamped event
        var b = SkillTreeRules.GetMultiplierBreakdown(s, 23, 0);
        Assert.Equal(new[] { 1.0, 0.10, 0.15, 0.15, 0.50 }, b.ConvertAll(x => x.Value));
        s.PinkRushActive = true;
        Assert.Equal(1.90 * 3, SkillTreeRules.GetTotalXpMultiplier(s, 23, 0), 6);
        Assert.Equal(2.0, SkillTreeRules.GetMultiplierBreakdown(s, 12, 0)[^1].Value);

        s.TotalConditioningMinutes = 25 * 60 + 7;
        Assert.Equal(ConditioningControlPanel.Localization.Loc.GetF("skill_time_dhm", 1, 1, 7), SkillTreeRules.FormatConditioningTime(s));
    }
}
