// PORTED from ConditioningControlPanel/Models/AchievementProgressStreak.cs (UpdateDailyStreak,
// ResolveStreakGapNow, TryAdvanceDayRollover, AwardDeferredStreakBonus, SyncCurrentStreak),
// AchievementService.cs:149 (startup) + :331 CheckDayRollover, SkillTreeService.cs:410
// UseOopsieInsurance + :478 GetDailyStreakBonus, App.xaml.cs:2732 - progression#42.
// ponytail: not carried - the deferred break while a V2 sync may answer (PendingStreakBreak, mobile
// parity) and the cloud adoption of consecutive_days / last_streak_date (ProfileSyncService.cs:3713,
// DecideLoginStreakAdopt is in Core): this head pulls no profile stats yet ("cloud sync body" row).
// So the gap decision is the immediate one, exactly what a signed-out WPF user gets at launch. The
// server half of Oopsie Insurance (UseOopsieInsuranceAsync) is not called either: the local charge
// is spent and the next sync's balance adoption corrects it. Season Recap peak hooks are not ported.

using System;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class LoginStreak
{
    private static DispatcherTimer? _rollover;

    /// <summary>WPF AchievementService ctor (:149) + App.OnStartup's AwardDeferredStreakBonus, then
    /// the once-a-minute midnight rollover (WPF rides its 1 s tracking timer; the check is stateless).</summary>
    internal static void Start(AchievementEngine engine)
    {
        try
        {
            var p = engine.Progress;
            UpdateDailyStreak(p);
            SyncCurrentStreak(p);   // even when already launched today
            engine.IsDirty = true;
            engine.Save();
            CoreSettings.Save();
            AwardDeferredStreakBonus(p);
        }
        catch (Exception ex) { Log.Error(ex, "Login streak: startup update failed"); }

        _rollover ??= new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => CheckDayRollover(engine));
        _rollover.Start();
    }

    /// <summary>WPF AchievementService.CheckDayRollover.</summary>
    internal static void CheckDayRollover(AchievementEngine engine)
    {
        try
        {
            var p = engine.Progress;
            if (!TryAdvanceDayRollover(p)) return;
            Log.Information("Login streak: day rolled over while running - banked {Date}, streak now {Days} day(s)",
                p.LastLaunchDate.ToString("yyyy-MM-dd"), p.ConsecutiveDays);
            SyncCurrentStreak(p);   // before Save (same ordering as WPF)
            CoreSettings.Save();
            engine.IsDirty = true;
            engine.Save();
            AwardDeferredStreakBonus(p);
        }
        catch (Exception ex) { Log.Error(ex, "Login streak: midnight rollover check failed"); }
    }

    internal static void UpdateDailyStreak(AchievementProgress p) => UpdateDailyStreak(p, DateTime.Today);

    /// <summary>WPF UpdateDailyStreak: yesterday extends, a gap spends a shield, then an Oopsie
    /// charge, then resets to 1; no history stamps today without breaking anything.</summary>
    internal static void UpdateDailyStreak(AchievementProgress p, DateTime today)
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

    private static void ResolveStreakGapNow(AchievementProgress p, DateTime lastDate, DateTime today, int daysMissed)
    {
        var s = CoreSettings.Current;
        if (CoreQuests.UseStreakShieldProvider?.Invoke() == true)
        {
            p.ConsecutiveDays++;
            p.PendingStreakBonus = true;
            Log.Information("Streak shield protected streak! Now at {Days} days", p.ConsecutiveDays);
            for (var d = lastDate.AddDays(1); d < today; d = d.AddDays(1))
                if (!s.StreakShieldUsedDates.Contains(d.Date)) s.StreakShieldUsedDates.Add(d.Date);
        }
        else if (UseOopsieInsurance(s))
        {
            Log.Information("Oopsie Insurance auto-spent a streak fix, saving streak at {Days} days", p.ConsecutiveDays);
        }
        else
        {
            Log.Warning("Login streak RESET from {OldStreak} to 1 - gap of {Days} day(s), no shield/insurance available",
                p.ConsecutiveDays, daysMissed);
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
    internal static bool TryAdvanceDayRollover(AchievementProgress p) => TryAdvanceDayRollover(p, DateTime.Today);

    internal static bool TryAdvanceDayRollover(AchievementProgress p, DateTime today)
    {
        var last = p.LastLaunchDate.Date;
        if (last == default || today.Date <= last) return false;
        UpdateDailyStreak(p, today);
        return p.LastLaunchDate.Date != last;
    }

    /// <summary>WPF SkillTreeService.GetDailyStreakBonus table.</summary>
    internal static int DailyStreakBonusXp(int streakDays) => streakDays <= 0 ? 0 : streakDays switch
    {
        <= 3 => 50,
        <= 6 => 100,
        <= 13 => 150,
        <= 29 => 200,
        _ => 300
    };

    /// <summary>WPF AwardDeferredStreakBonus + ShowDailyStreakNotification (a toast here; the WPF
    /// achievement-style popup is not ported).</summary>
    internal static void AwardDeferredStreakBonus(AchievementProgress p)
    {
        if (!p.PendingStreakBonus) return;
        p.PendingStreakBonus = false;
        var xp = DailyStreakBonusXp(p.ConsecutiveDays);
        if (xp <= 0) return;
        CoreProgression.AddXP(xp, "Other");
        Log.Information("Daily streak bonus! {Days} days - awarded {XP} XP", p.ConsecutiveDays, xp);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                App.Notifications.Show($"🎁 {Loc.Get("skill_daily_streak_bonus_header")}  {Loc.GetF("skill_streak_bonus_flavor", xp, p.ConsecutiveDays)}",
                    Helpers.NotificationType.Info, TimeSpan.FromSeconds(6));
            }
            catch (Exception ex) { Log.Debug(ex, "Streak bonus toast failed"); }
        }, DispatcherPriority.Background);
    }

    /// <summary>WPF SyncCurrentStreak: AppSettings mirrors the login streak (no save).</summary>
    internal static void SyncCurrentStreak(AchievementProgress p)
    {
        var s = CoreSettings.Current;
        s.CurrentStreak = p.ConsecutiveDays;
        s.LastStreakDate = p.LastLaunchDate;
    }
}
