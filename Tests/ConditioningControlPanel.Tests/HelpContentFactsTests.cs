using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The "?" popovers are the only place in the app that explains a rule in prose, and prose drifts
/// away from code silently. The v6.9.4 copy pass corrected the localised strings (the Quests board
/// now says "Resets Monday", the Bouncing Text card says "+15 XP per bounce") and left these two
/// cards saying Sunday and +25, one "?" button apart from the corrected text. These tests pin the
/// two numbers that were wrong against the code that owns them:
/// <list type="bullet">
/// <item>weekly reset day - <c>QuestService.GetStartOfWeek</c> rolls back to Monday.</item>
/// <item>bounce award and cap - <c>BouncingTextService</c> adds 15 XP, capped at 150 a minute.</item>
/// </list>
/// </summary>
public class HelpContentFactsTests
{
    [Fact]
    public void QuestsTopic_SaysWeeklyQuestsResetOnMonday()
    {
        var quests = HelpContentService.GetContent("Quests");

        Assert.Contains("Monday", quests.HowItWorks, StringComparison.Ordinal);
        Assert.DoesNotContain("Sunday", quests.HowItWorks, StringComparison.Ordinal);
    }

    [Fact]
    public void BouncingTextTopic_PaysFifteenPerBounce_CappedAtOneFifty()
    {
        var bouncing = HelpContentService.GetContent("BouncingText");
        var all = bouncing.WhatItDoes + " " + bouncing.HowItWorks + " " + string.Join(" ", bouncing.Tips);

        Assert.Contains("15 XP", all, StringComparison.Ordinal);
        Assert.Contains("150", all, StringComparison.Ordinal);
        Assert.DoesNotContain("25 XP", all, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryTopic_Resolves_SoTheseIdsCannotRotAway()
    {
        Assert.True(HelpContentService.HasContent("Quests"));
        Assert.True(HelpContentService.HasContent("BouncingText"));
    }
}
