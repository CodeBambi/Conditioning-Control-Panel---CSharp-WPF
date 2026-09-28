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
        Assert.False(SkillTreeRules.IsSecretSkillAvailable(s, "eternal_doll"));
        s.HighestLevelEver = 50;
        Assert.True(SkillTreeRules.IsSecretSkillAvailable(s, "eternal_doll"));
        s.NightTimeUsageCount = 10;
        Assert.True(SkillTreeRules.IsSecretSkillAvailable(s, "night_shift"));
        Assert.False(SkillTreeRules.CanPurchaseSkill(s, "no_such_skill"));
    }
}
