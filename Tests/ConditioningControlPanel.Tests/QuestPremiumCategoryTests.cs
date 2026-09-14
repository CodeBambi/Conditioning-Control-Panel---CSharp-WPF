using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs#1186 / #1192: free users dealt premium-only quests, so the day's XP was unreachable.
///
/// The drop guard (QuestPremiumDropTests) fixed who may LOSE a premium quest. These pin the other
/// half - what makes a quest premium in the first place. It used to be the hand-authored
/// requiresPremium flag and nothing else, so a definition published without it landed on every
/// free board no matter what feature it pointed at.
/// </summary>
public class QuestPremiumCategoryTests
{
    private static QuestDefinition Def(string id, QuestCategory category, bool flag = false) =>
        new(id, id, id, QuestType.Daily, category, 10, 100, "*", "", flag);

    // ---- THE HOLE ITSELF -------------------------------------------------------------------

    [Theory]
    [InlineData(QuestCategory.Autonomy)]       // Bambi Takeover, named in #1192
    [InlineData(QuestCategory.BlinkTrainer)]   // Blink Trainer, named in #1192
    [InlineData(QuestCategory.Lockdown)]
    [InlineData(QuestCategory.Remote)]
    [InlineData(QuestCategory.KeywordTrigger)]
    public void APremiumFeatureQuestThatForgotTheFlag_IsStillPremium(QuestCategory category)
    {
        var unflagged = Def("published_without_the_flag", category);

        Assert.True(unflagged.NeedsPremium);
        Assert.False(QuestService.IsQuestAvailableForTier(unflagged, hasPremium: false));
        Assert.True(QuestService.IsQuestAvailableForTier(unflagged, hasPremium: true));
    }

    [Theory]
    [InlineData(QuestCategory.Flash)]
    [InlineData(QuestCategory.Video)]
    [InlineData(QuestCategory.Spiral)]
    [InlineData(QuestCategory.PinkFilter)]
    [InlineData(QuestCategory.Bubbles)]
    [InlineData(QuestCategory.LockCard)]
    [InlineData(QuestCategory.Session)]
    [InlineData(QuestCategory.Streak)]
    [InlineData(QuestCategory.BubbleCount)]
    [InlineData(QuestCategory.Mantra)]
    [InlineData(QuestCategory.Combined)]
    public void AFreeCategoryStaysFree(QuestCategory category)
    {
        var free = Def("free_quest", category);

        Assert.False(free.NeedsPremium);
        Assert.True(QuestService.IsQuestAvailableForTier(free, hasPremium: false));
    }

    [Fact]
    public void TheGivingSideOfRemoteControl_IsNotPremium()
    {
        // Deliberate: browsing Available Subjects is free by design, and the matchmaking pool
        // needs Controllers. Only the RECEIVING side (QuestCategory.Remote) is gated.
        var giving = Def("take_the_reins_d", QuestCategory.RemoteIssue);

        Assert.False(giving.NeedsPremium);
        Assert.True(QuestService.IsQuestAvailableForTier(giving, hasPremium: false));
    }

    [Fact]
    public void TheFlagStillWorksOnItsOwn_ForAFreeCategory()
    {
        // Additive, not replaced: the channel can still gate a flash quest by hand.
        var flagged = Def("gated_flash", QuestCategory.Flash, flag: true);

        Assert.True(flagged.NeedsPremium);
        Assert.False(QuestService.IsQuestAvailableForTier(flagged, hasPremium: false));
    }

    // ---- THE ROLL -------------------------------------------------------------------------

    [Fact]
    public void AFreeUserNeverRollsAnUnflaggedPremiumQuest_EvenWhenItIsTheWholePool()
    {
        // The shape of the bug report: the pool the client was handed was all premium. The free
        // user must come out with nothing dealt rather than with a quest they cannot finish -
        // RollDailyQuest treats an empty result as "leave the seat alone", never as a reason to
        // deal an impossible one.
        var poisoned = new List<QuestDefinition>
        {
            Def("takeover_drift_d", QuestCategory.Autonomy),
            Def("blink_drill_d", QuestCategory.BlinkTrainer),
            Def("locked_away_d", QuestCategory.Lockdown)
        };

        var free = QuestService.FilterDailyRollPool(
            poisoned, (ICollection<string>?)null, hasPremium: false,
            DateTime.Today, applyDateWindow: false);
        Assert.Empty(free);

        var premium = QuestService.FilterDailyRollPool(
            poisoned, (ICollection<string>?)null, hasPremium: true,
            DateTime.Today, applyDateWindow: false);
        Assert.Equal(3, premium.Count);
    }

    [Fact]
    public void AFreeUserStillGetsTheFreeQuestsOutOfAMixedPool()
    {
        // The safety net must narrow the pool, never empty a board that had legal quests in it.
        var mixed = new List<QuestDefinition>
        {
            Def("takeover_drift_d", QuestCategory.Autonomy),
            Def("blink_drill_d", QuestCategory.BlinkTrainer),
            Def("flash_rush_d", QuestCategory.Flash),
            Def("spiral_sink_d", QuestCategory.Spiral),
            Def("pop_parade_d", QuestCategory.Bubbles)
        };

        var free = QuestService.FilterDailyRollPool(
            mixed, (ICollection<string>?)null, hasPremium: false,
            DateTime.Today, applyDateWindow: false);

        Assert.Equal(
            new[] { "flash_rush_d", "spiral_sink_d", "pop_parade_d" },
            free.Select(q => q.Id).ToArray());
    }

    // ---- THE SHIPPED CATALOGUE ------------------------------------------------------------

    [Fact]
    public void EveryEmbeddedPremiumCategoryQuest_AlsoCarriesTheFlag()
    {
        // The flag and the category must agree in the definitions this build ships, or the
        // server payload generated from them (tools/quests_payload.json) reintroduces the hole
        // for every older client that only reads the flag.
        var mismatched = QuestDefinition.DailyQuests
            .Concat(QuestDefinition.WeeklyQuests)
            .Where(q => QuestDefinition.IsPremiumCategory(q.Category) && !q.RequiresPremium)
            .Select(q => q.Id)
            .ToArray();

        Assert.Empty(mismatched);
    }

    [Fact]
    public void AFreeUserAlwaysHasSomethingToRollFromTheShippedDailyPool()
    {
        var free = QuestService.FilterDailyRollPool(
            QuestDefinition.DailyQuests, (ICollection<string>?)null, hasPremium: false,
            DateTime.Today, applyDateWindow: true);

        // Three seats on the board: the free pool has to be able to fill all three.
        Assert.True(free.Count >= QuestService.MaxDailyQuestsPerDay,
            "free daily pool is only " + free.Count + " quest(s) deep");
        Assert.All(free, q => Assert.False(q.NeedsPremium));
    }

    // ---- THE SEASON HEADER ----------------------------------------------------------------

    [Fact]
    public void AStaleSeasonTitle_IsNotTrusted()
    {
        // "Airhead August" on the Quests panel in September (UI thread, 2026-09-13): the server's
        // season_config was never rotated, and the fetched-this-month guard cannot see that.
        Assert.False(QuestDefinitionService.IsSeasonKeyCurrent("2026-08"));
        Assert.False(QuestDefinitionService.IsSeasonKeyCurrent("2025-09"));
    }

    [Fact]
    public void TheCurrentSeasonKey_IsTrusted_InEitherZone()
    {
        Assert.True(QuestDefinitionService.IsSeasonKeyCurrent(DateTime.UtcNow.ToString("yyyy-MM")));
        Assert.True(QuestDefinitionService.IsSeasonKeyCurrent(DateTime.Now.ToString("yyyy-MM")));
        Assert.True(QuestDefinitionService.IsSeasonKeyCurrent("  " + DateTime.UtcNow.ToString("yyyy-MM") + " "));
    }

    [Fact]
    public void AMissingOrUnusableSeasonKey_FailsOpen()
    {
        // An older server sends no key, and a hand-edited config can send nonsense. Neither is a
        // reason to blank a title that is probably right.
        Assert.True(QuestDefinitionService.IsSeasonKeyCurrent(null));
        Assert.True(QuestDefinitionService.IsSeasonKeyCurrent(""));
        Assert.True(QuestDefinitionService.IsSeasonKeyCurrent("   "));
        Assert.True(QuestDefinitionService.IsSeasonKeyCurrent("summer"));
    }
}
