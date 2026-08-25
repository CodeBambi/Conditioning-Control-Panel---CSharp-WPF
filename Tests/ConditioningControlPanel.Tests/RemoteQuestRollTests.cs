using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Two community threads: a solo user could roll "Take 25 remote commands" and be unable to
/// touch it all day, because a remote-RECEIVING quest only moves when someone else drives them;
/// and nothing rewarded the GIVING side, which is what the Available Subjects matchmaking pool
/// is short of. The daily roll now skips QuestCategory.Remote, and take_the_reins_d counts
/// commands issued to another subject for every tier.
///
/// These exercise the same pure predicates GenerateNewDailyQuest calls, so a future refactor
/// that re-adds the receive quests to the roll fails here rather than in the field.
/// </summary>
public class RemoteQuestRollTests
{
    private static readonly DateTime AnyDay = new(2026, 8, 25);

    /// <summary>The receive-remote daily quests as they ship, by id.</summary>
    public static TheoryData<string> ReceiveRemoteDailyIds => new() { "handed_over_d", "remote_hands_d" };

    [Theory]
    [MemberData(nameof(ReceiveRemoteDailyIds))]
    public void ReceiveRemoteQuest_IsStillDefined_SoPersistedProgressSurvives(string id)
    {
        // Deliberately NOT deleted: a quest already rolled today still resolves through
        // GetCurrentDailyDefinition and completes, and quests.json naming it does not
        // regenerate-and-lose the day's progress.
        var def = QuestDefinition.DailyQuests.FirstOrDefault(q => q.Id == id);
        Assert.NotNull(def);
        Assert.Equal(QuestCategory.Remote, def!.Category);
    }

    [Theory]
    [MemberData(nameof(ReceiveRemoteDailyIds))]
    public void ReceiveRemoteQuest_IsNeverRollableAsDaily(string id)
    {
        var def = QuestDefinition.DailyQuests.First(q => q.Id == id);
        Assert.False(QuestService.IsRollableAsDaily(def));
    }

    [Theory]
    [InlineData(true)]   // premium: the blended pool, where the remote quests used to live
    [InlineData(false)]  // free: they were already filtered by RequiresPremium, belt and braces
    public void DailyRoll_NeverYieldsAReceiveRemoteQuest(bool hasPremium)
    {
        var pool = QuestService.FilterDailyRollPool(
            QuestDefinition.DailyQuests, excludeId: null, hasPremium: hasPremium,
            today: AnyDay, applyDateWindow: true);

        Assert.NotEmpty(pool);
        Assert.DoesNotContain(pool, q => q.Category == QuestCategory.Remote);
        Assert.DoesNotContain(pool, q => q.Id == "handed_over_d");
        Assert.DoesNotContain(pool, q => q.Id == "remote_hands_d");
    }

    [Fact]
    public void DailyRoll_StarvationFallback_AlsoSkipsReceiveRemoteQuests()
    {
        // The date-window fallback drops IsQuestInDateWindow but must keep every other
        // predicate, or an empty seasonal window would hand the player back the dead quest.
        var pool = QuestService.FilterDailyRollPool(
            QuestDefinition.DailyQuests, excludeId: null, hasPremium: true,
            today: AnyDay, applyDateWindow: false);

        Assert.NotEmpty(pool);
        Assert.DoesNotContain(pool, q => q.Category == QuestCategory.Remote);
    }

    [Fact]
    public void ServerPublishedDailyRemoteQuest_IsAlsoFilteredOut()
    {
        // The gate is by CATEGORY, not by id, so the definitions channel cannot reintroduce
        // the bug by publishing a new remote-receive daily.
        var published = new QuestDefinition(
            "some_future_remote_d", "Future Remote", "Take 5 remote commands",
            QuestType.Daily, QuestCategory.Remote, 5, 200, "📡");

        var pool = QuestService.FilterDailyRollPool(
            new[] { published }.Concat(QuestDefinition.DailyQuests), excludeId: null,
            hasPremium: true, today: AnyDay, applyDateWindow: true);

        Assert.DoesNotContain(pool, q => q.Id == "some_future_remote_d");
    }

    [Fact]
    public void WeeklyRemoteQuests_AreUntouched()
    {
        // A week is long enough to find a Controller, so the weekly slot keeps both.
        Assert.Contains(QuestDefinition.WeeklyQuests, q => q.Id == "puppet_strings_w");
        Assert.Contains(QuestDefinition.WeeklyQuests, q => q.Id == "fully_remote_w");
    }

    // ---------------------------------------------------------------- giving side

    [Fact]
    public void TakeTheReins_IsFreeTierButHeldOutOfTheRollUntilTheControllerReportsBack()
    {
        var def = QuestDefinition.DailyQuests.FirstOrDefault(q => q.Id == "take_the_reins_d");
        Assert.NotNull(def);
        Assert.False(def!.RequiresPremium);
        Assert.True(QuestService.IsQuestAvailableForTier(def, hasPremium: false));
        // The desktop app never issues commands (the controller is the web page), so until the
        // server reports issued commands back this quest must NOT roll - it could never move past 0.
        Assert.False(QuestService.IsRollableAsDaily(def));

        var freePool = QuestService.FilterDailyRollPool(
            QuestDefinition.DailyQuests, excludeId: null, hasPremium: false,
            today: AnyDay, applyDateWindow: true);

        Assert.DoesNotContain(freePool, q => q.Id == "take_the_reins_d");
    }

    [Fact]
    public void TakeTheReins_CountsIssuedCommands_AtTheAgreedMagnitude()
    {
        var def = QuestDefinition.DailyQuests.First(q => q.Id == "take_the_reins_d");
        Assert.Equal(QuestCategory.RemoteIssue, def.Category);
        Assert.Equal(QuestType.Daily, def.Type);
        Assert.Equal(10, def.TargetValue);
        // Mid-band daily XP, in line with pink_haze_d (175) / screen_trance_d (200).
        Assert.Equal(200, def.XPReward);
    }

    [Fact]
    public void RemoteIssueCategory_ParsesFromTheServerStrings()
    {
        Assert.Equal(QuestCategory.RemoteIssue, QuestDefinition.ParseCategory("remoteissue"));
        Assert.Equal(QuestCategory.RemoteIssue, QuestDefinition.ParseCategory("RemoteIssued"));
        Assert.Equal(QuestCategory.RemoteIssue, QuestDefinition.ParseCategory("remotegiven"));
        // The receiving category must not have moved underneath the old server payloads.
        Assert.Equal(QuestCategory.Remote, QuestDefinition.ParseCategory("remote"));
    }

    [Theory]
    // target,             self,               counts?
    [InlineData("subject-b", "subject-a", true)]
    [InlineData("subject-a", "subject-a", false)]  // self-control never counts
    [InlineData("SUBJECT-A", "subject-a", false)]  // ...case-insensitively
    [InlineData(" subject-a ", "subject-a", false)]// ...and whitespace cannot smuggle it through
    [InlineData(null, "subject-a", false)]         // unattributable: dropped, never credited
    [InlineData("", "subject-a", false)]
    [InlineData("   ", "subject-a", false)]
    [InlineData("subject-b", null, true)]          // no local id yet: still someone else's session
    public void SelfControlNeverCounts(string? target, string? self, bool expected)
    {
        Assert.Equal(expected, QuestService.CountsAsForeignSubject(target, self));
    }
}
