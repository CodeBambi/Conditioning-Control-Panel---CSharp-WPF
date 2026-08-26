using System;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Pins the login-streak adoption contract (AchievementProgress.DecideLoginStreakAdopt) that the
/// mobile client mirrors in CCPMobile src/lib/sync/contract.ts (decideLoginStreakAdopt). The two
/// implementations must keep answering the same on shared inputs — a drift here spends users'
/// paid shield/Oopsie charges or forks the streak between devices — so every rule the doc
/// comment states gets a table row: the serverStreak &lt;= 0 refusal, contiguity extension in both
/// directions, the future-date clamp, the never-lower ratchet, and the ONE deliberate
/// divergence (dateless server record: desktop take-higher, mobile null).
/// </summary>
public class LoginStreakAdoptTests
{
    private static readonly DateTime Today = new(2026, 8, 26);
    private static DateTime D(int day) => new(2026, 8, day);

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ZeroOrNegativeServerStreakIsRefusedOutright(int serverStreak)
    {
        // Even with a perfectly valid, newer server date: a degenerate record's DATE alone
        // must not move LastLaunchDate forward on the strength of a streak that isn't there.
        var adopt = AchievementProgress.DecideLoginStreakAdopt(5, D(20), serverStreak, D(25), Today);
        Assert.Null(adopt);
    }

    [Fact]
    public void DatelessServerRecordStillTakesHigher_TheDeliberateMobileDivergence()
    {
        // Pre-parity servers wrote consecutive_days without last_streak_date; desktop keeps
        // the date-blind take-higher for those records (mobile answers null here — it has no
        // pre-parity history to stay compatible with). The local date must not move.
        var adopt = AchievementProgress.DecideLoginStreakAdopt(3, D(25), 7, null, Today);
        Assert.Equal((7, D(25)), adopt);

        // A default server date counts as dateless, not as year-1 "older".
        adopt = AchievementProgress.DecideLoginStreakAdopt(3, D(25), 7, default(DateTime), Today);
        Assert.Equal((7, D(25)), adopt);

        // Dateless AND not higher: nothing changes, null.
        Assert.Null(AchievementProgress.DecideLoginStreakAdopt(7, D(25), 3, null, Today));
    }

    [Fact]
    public void SameDayTakesTheHigherStreakAndKeepsTheDate()
    {
        var adopt = AchievementProgress.DecideLoginStreakAdopt(3, D(25), 7, D(25), Today);
        Assert.Equal((7, D(25)), adopt);

        // Same day, server not higher: no change.
        Assert.Null(AchievementProgress.DecideLoginStreakAdopt(7, D(25), 3, D(25), Today));
    }

    [Fact]
    public void ContiguousServerDayExtendsTheLocalRun()
    {
        // Server banked the day right after local's: the runs are one run — local+1, and the
        // date moves forward so the covered day never reads as a gap again.
        var adopt = AchievementProgress.DecideLoginStreakAdopt(5, D(24), 2, D(25), Today);
        Assert.Equal((6, D(25)), adopt);

        // When the server's own figure is larger than local+1, the server figure wins.
        adopt = AchievementProgress.DecideLoginStreakAdopt(5, D(24), 9, D(25), Today);
        Assert.Equal((9, D(25)), adopt);
    }

    [Fact]
    public void ContiguousLocalDayExtendsTheServerRunSymmetrically()
    {
        // Local banked the day right after the server's: server+1, local date kept.
        var adopt = AchievementProgress.DecideLoginStreakAdopt(5, D(25), 9, D(24), Today);
        Assert.Equal((10, D(25)), adopt);
    }

    [Fact]
    public void FutureServerDateIsClampedToToday()
    {
        // A timezone-skewed (or poisoned) record must never push LastLaunchDate into the
        // future — that reads as a negative gap next launch. Clamped to today it lands one
        // day after local's yesterday, i.e. the contiguous branch.
        var adopt = AchievementProgress.DecideLoginStreakAdopt(5, D(25), 9, new DateTime(2999, 1, 1), Today);
        Assert.Equal((9, Today), adopt);

        // Local already at today: clamp makes it same-day, plain max, date unmoved.
        adopt = AchievementProgress.DecideLoginStreakAdopt(5, Today, 9, new DateTime(2999, 1, 1), Today);
        Assert.Equal((9, Today), adopt);
    }

    [Fact]
    public void FreshInstallAdoptsTheServerPairWholesale()
    {
        var adopt = AchievementProgress.DecideLoginStreakAdopt(0, default, 4, D(20), Today);
        Assert.Equal((4, D(20)), adopt);
    }

    [Fact]
    public void WideGapsFallBackToPlainMax_ThePreservedRatchet()
    {
        // Server date newer by a wide margin: max streak, newer date adopted.
        var adopt = AchievementProgress.DecideLoginStreakAdopt(5, D(10), 3, D(25), Today);
        Assert.Equal((5, D(25)), adopt);

        // Local date newer by a wide margin: max streak, local date kept.
        adopt = AchievementProgress.DecideLoginStreakAdopt(2, D(25), 9, D(10), Today);
        Assert.Equal((9, D(25)), adopt);
    }

    [Theory]
    [InlineData(5, 5)]  // identical pair
    [InlineData(9, 3)]  // server lower on an older date
    public void NeverLowersAndNeverChurnsOnNoChange(int localStreak, int serverStreak)
    {
        var adopt = AchievementProgress.DecideLoginStreakAdopt(localStreak, D(25), serverStreak, D(10), Today);
        if (adopt != null)
        {
            Assert.True(adopt.Value.Streak >= localStreak);
            Assert.True(adopt.Value.LastDate >= D(25));
        }
        else
        {
            // Null means "nothing changes" — legal only when the merge would not raise.
            Assert.True(Math.Max(localStreak, serverStreak) == localStreak);
        }
    }
}
