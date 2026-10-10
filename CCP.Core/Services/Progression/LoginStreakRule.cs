using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Services;

/// <summary>
/// The daily login streak rule (WPF 7.1.5 Models/AchievementProgress.cs UpdateDailyStreak,
/// ResolveStreakGapNow, TryAdvanceDayRollover, SyncCurrentStreak; SkillTreeService
/// UseOopsieInsurance + GetDailyStreakBonus): yesterday extends, a gap spends a shield, then an
/// Oopsie charge, then resets to 1; no launch history stamps today without breaking anything.
/// EMI hears the ordinary day that kept the streak (<c>streakKept</c>) and the break
/// (<c>streakBroken</c>, with the number the user HAD) through <see cref="EmiDeskBus"/>.
///
/// <para>Plain statics, not extension methods: the in-tree WPF app still has its own
/// AchievementProgressStreak extensions and two sets would be ambiguous there.</para>
/// ponytail: not carried - the deferred break while a V2 sync may answer (PendingStreakBreak is
/// honoured, never set) and the Season Recap peak hooks (seasons are retired).
/// </summary>
public static class LoginStreakRule
{
    public static void UpdateDailyStreak(AchievementProgress p) => UpdateDailyStreak(p, DateTime.Today);

    public static void UpdateDailyStreak(AchievementProgress p, DateTime today)
    {
        today = today.Date;
        var lastDate = p.LastLaunchDate.Date;
        if (p.PendingStreakBreak) return;

        if (lastDate == default)
        {
            Log.Information("Login streak: no local launch history (fresh/reset install) - not breaking streak");
            if (p.ConsecutiveDays < 1) p.ConsecutiveDays = 1;
            p.LastLaunchDate = today;
            SyncCurrentStreak(p);
            return;
        }
        if (lastDate == today) return;
        if (lastDate == today.AddDays(-1))
        {
            p.ConsecutiveDays++;
            p.PendingStreakBonus = true;
            // EMI Desk (MOMENTS 4.B): the ordinary day that kept it alive.
            EmiDeskBus.Fire("streakKept", new { streak = p.ConsecutiveDays });
        }
        else if (lastDate < today)
        {
            var daysMissed = (today - lastDate).Days;
            Log.Information("Login streak gap detected: {Days} day(s) missed (last launch: {LastDate}, streak was: {Streak})",
                daysMissed, lastDate.ToString("yyyy-MM-dd"), p.ConsecutiveDays);
            ResolveStreakGapNow(p, lastDate, today, daysMissed);
        }
        else return;   // the clock went backwards: stand still (WPF ShouldBankDayRollover)

        p.LastLaunchDate = today;
        SyncCurrentStreak(p);
    }

    /// <summary>Shield first, then Oopsie Insurance, then reset to 1. The caller stamps LastLaunchDate.</summary>
    private static void ResolveStreakGapNow(AchievementProgress p, DateTime lastDate, DateTime today, int daysMissed)
    {
        var s = CoreSettings.Current;
        if (CoreQuests.UseStreakShieldProvider?.Invoke() == true)
        {
            p.ConsecutiveDays++;
            p.PendingStreakBonus = true;
            Log.Information("Streak shield protected streak! Now at {Days} days", p.ConsecutiveDays);
            // EMI Desk: the streak survived, which is a keep and not a break.
            EmiDeskBus.Fire("streakKept", new { streak = p.ConsecutiveDays });
            for (var d = lastDate.AddDays(1); d < today; d = d.AddDays(1))
                if (!s.StreakShieldUsedDates.Contains(d.Date)) s.StreakShieldUsedDates.Add(d.Date);
        }
        else if (UseOopsieInsurance(s))
        {
            Log.Information("Oopsie Insurance auto-spent a streak fix, saving streak at {Days} days", p.ConsecutiveDays);
            // EMI Desk: same - a charge was spent, the number did not fall.
            EmiDeskBus.Fire("streakKept", new { streak = p.ConsecutiveDays });
        }
        else
        {
            Log.Warning("Login streak RESET from {OldStreak} to 1 - gap of {Days} day(s), no shield/insurance available",
                p.ConsecutiveDays, daysMissed);
            // EMI Desk: the streak the user HAD, read before the reset wipes it. Never a scold.
            EmiDeskBus.Fire("streakBroken", new { streak = p.ConsecutiveDays });
            p.ConsecutiveDays = 1;
        }
    }

    /// <summary>WPF SkillTreeService.UseOopsieInsurance, local half: spends down to the last charge.</summary>
    private static bool UseOopsieInsurance(AppSettings s)
    {
        if (!SkillTreeRules.HasSkill(s, "oopsie_insurance")) return false;
        if (s.StreakFixCharges < 1) return false;
        s.StreakFixCharges = Math.Max(0, s.StreakFixCharges - 1);
        s.SeasonalStreakRecoveryUsed = true;
        CoreSettings.Save();
        return true;
    }

    /// <summary>WPF AchievementProgress.TryAdvanceDayRollover: true when a day was banked.</summary>
    public static bool TryAdvanceDayRollover(AchievementProgress p) => TryAdvanceDayRollover(p, DateTime.Today);

    public static bool TryAdvanceDayRollover(AchievementProgress p, DateTime today)
    {
        var last = p.LastLaunchDate.Date;
        if (last == default || today.Date <= last) return false;
        UpdateDailyStreak(p, today);
        return p.LastLaunchDate.Date != last;
    }

    /// <summary>WPF SkillTreeService.GetDailyStreakBonus table.</summary>
    public static int DailyStreakBonusXp(int streakDays) => streakDays <= 0 ? 0 : streakDays switch
    {
        <= 3 => 50,
        <= 6 => 100,
        <= 13 => 150,
        <= 29 => 200,
        _ => 300
    };

    /// <summary>WPF SyncCurrentStreak: AppSettings mirrors the login streak (no save).</summary>
    public static void SyncCurrentStreak(AchievementProgress p)
    {
        var s = CoreSettings.Current;
        s.CurrentStreak = p.ConsecutiveDays;
        s.LastStreakDate = p.LastLaunchDate;
    }
}
