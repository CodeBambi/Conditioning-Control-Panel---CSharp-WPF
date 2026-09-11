using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs#1186 (and its duplicate #1192): a free user who ended up wearing a premium quest could
/// not finish it and could not be given another, because the guard that keeps a LAPSED patron's
/// half-finished quest refused to drop anything with progress on it - for everybody.
///
/// The guard now asks who it is protecting. These pin the four corners of that decision.
/// </summary>
public class QuestPremiumDropTests
{
    [Fact]
    public void LapsedPatron_KeepsTheQuestTheyHaveAlreadyWorkedOn()
    {
        // The case the guard was written for, and the one that must not regress.
        Assert.False(QuestService.CanDropPremiumQuest(
            entitlementResolved: true, wasEverPremium: true, currentProgress: 12));
    }

    [Fact]
    public void NeverPremium_LosesTheQuestEvenWithProgressOnIt()
    {
        // #1186. Progress is not a reason to keep a quest this account can never finish.
        Assert.True(QuestService.CanDropPremiumQuest(
            entitlementResolved: true, wasEverPremium: false, currentProgress: 12));
    }

    [Fact]
    public void AnUntouchedPremiumQuest_IsDroppedForEitherKindOfUser()
    {
        Assert.True(QuestService.CanDropPremiumQuest(
            entitlementResolved: true, wasEverPremium: true, currentProgress: 0));
        Assert.True(QuestService.CanDropPremiumQuest(
            entitlementResolved: true, wasEverPremium: false, currentProgress: 0));
    }

    [Fact]
    public void NothingIsDroppedWhileTheEntitlementIsUnresolved()
    {
        // #889: for the first ~90 seconds of a launch every patron looks free. Deciding then is
        // how a paying subscriber lost their premium quest on every single startup.
        Assert.False(QuestService.CanDropPremiumQuest(
            entitlementResolved: false, wasEverPremium: false, currentProgress: 0));
        Assert.False(QuestService.CanDropPremiumQuest(
            entitlementResolved: false, wasEverPremium: false, currentProgress: 12));
        Assert.False(QuestService.CanDropPremiumQuest(
            entitlementResolved: false, wasEverPremium: true, currentProgress: 12));
    }
}
