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

    // The rule itself (and EMI's streakKept / streakBroken moments) lives in Core LoginStreakRule.
    internal static void UpdateDailyStreak(AchievementProgress p) => LoginStreakRule.UpdateDailyStreak(p);
    internal static void UpdateDailyStreak(AchievementProgress p, DateTime today) => LoginStreakRule.UpdateDailyStreak(p, today);
    internal static bool TryAdvanceDayRollover(AchievementProgress p) => LoginStreakRule.TryAdvanceDayRollover(p);
    internal static bool TryAdvanceDayRollover(AchievementProgress p, DateTime today) => LoginStreakRule.TryAdvanceDayRollover(p, today);
    internal static int DailyStreakBonusXp(int streakDays) => LoginStreakRule.DailyStreakBonusXp(streakDays);

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

    internal static void SyncCurrentStreak(AchievementProgress p) => LoginStreakRule.SyncCurrentStreak(p);
}
