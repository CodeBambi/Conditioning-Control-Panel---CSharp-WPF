using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel;

/// <summary>
/// The login-streak lifecycle of <see cref="AchievementProgress"/>. The data and the pure rules
/// live in CCP.Core; these read App.SkillTree / App.Settings / App.EmiDesk / the WPF dispatcher,
/// so they stay in the head as extension methods and every call site keeps its old syntax.
/// Root namespace on purpose: every ConditioningControlPanel.* caller sees them with no using.
/// </summary>
public static class AchievementProgressStreak
{
    /// <summary>
    /// Check and update consecutive days streak.
    /// Integrates streak shields, oopsie insurance, milestone rewards, and CurrentStreak sync.
    /// </summary>
    public static void UpdateDailyStreak(this AchievementProgress p)
    {
        // Season Recap (local-only): mark today as an active day this season, and capture the
        // current streak as a season peak. Done BEFORE the same-day early-return below so a
        // relaunch on a day we already counted still records the peak (the end-of-method call
        // only fires when the streak actually changes).
        SeasonRecapService.MarkActiveToday();
        SeasonRecapService.TrackStreakPeak(p.ConsecutiveDays);

        var today = DateTime.Today;
        var lastDate = p.LastLaunchDate.Date;

        // A deferred break decision is in flight (waiting on the first V2 sync / its timeout).
        // ResolveDeferredStreakBreak owns the next write to LastLaunchDate — running the gap
        // math again here would burn the shield the deferral exists to protect.
        if (p.PendingStreakBreak) return;

        // No recorded launch history (LastLaunchDate == default/MinValue). This is either a
        // genuine first run OR a fresh/reset achievements.json after a reinstall, failed update
        // migration, or logout that cleared local data. We cannot tell the two apart here, and
        // the cloud profile hasn't loaded yet — so DON'T break the streak. Stamp today and defer
        // to LoadProfileAsync's take-higher restore (ProfileSyncService.cs:1096/1602), which pulls
        // the real ConsecutiveDays back from the server. A true new user simply starts at 1.
        // Without this guard the gap math sees a ~739771-day gap and resets the streak to 1,
        // which then risks being synced UP over the real cloud value (#344, #345, #331).
        if (lastDate == default)
        {
            App.Logger?.Information("Login streak: no local launch history (fresh/reset install) — deferring to cloud restore, not breaking streak");
            if (p.ConsecutiveDays < 1) p.ConsecutiveDays = 1;
            p.LastLaunchDate = today;
            p.SyncCurrentStreak();
            SeasonRecapService.TrackStreakPeak(p.ConsecutiveDays);
            return;
        }

        if (lastDate == today)
        {
            // Already launched today, no change
            return;
        }
        else if (lastDate == today.AddDays(-1))
        {
            // Launched yesterday, increment streak
            p.ConsecutiveDays++;
            p.PendingStreakBonus = true;

            // EMI Desk (MOMENTS 4.B). The milestone days are the bark's StreakMilestone and reach
            // her through the mirror; this is the ordinary day that kept it alive.
            try { App.EmiDesk?.Fire("streakKept", new { streak = p.ConsecutiveDays }); } catch { }
        }
        else
        {
            var daysMissed = (today - lastDate).Days;
            App.Logger?.Information("Login streak gap detected: {Days} day(s) missed (last launch: {LastDate}, today: {Today}, streak was: {Streak})",
                daysMissed, lastDate.ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd"), p.ConsecutiveDays);

            // Mobile streak parity: a signed-in account may have kept this streak alive on the
            // phone. Hold the break decision (and the shield/Oopsie tokens) until the first V2
            // sync merges the cloud's last_streak_date/consecutive_days, or the timeout gives up.
            // LastLaunchDate is deliberately NOT stamped: the push that races this deferral must
            // carry the honest pre-gap date, not claim today.
            if (CanDeferStreakBreakToCloud())
            {
                p.PendingStreakBreak = true;
                App.Logger?.Information("Login streak: break deferred pending cloud answer (mobile may have covered the gap)");
                p.ScheduleDeferredStreakBreakTimeout();
                return;
            }

            p.ResolveStreakGapNow(lastDate, today, daysMissed);
        }

        p.LastLaunchDate = today;

        // Sync CurrentStreak in AppSettings with ConsecutiveDays
        p.SyncCurrentStreak();

        // Season Recap (local-only): keep the season peak streak. Tracked separately from
        // CurrentStreak because the server-driven season reset can zero CurrentStreak before
        // the recap snapshot runs — the peak must survive that.
        SeasonRecapService.TrackStreakPeak(p.ConsecutiveDays);
    }

    /// <summary>
    /// The actual streak-break spend/reset, extracted so the deferred path and the immediate path
    /// share one implementation: shield first, then Oopsie Insurance, then reset to 1.
    /// Mutates ConsecutiveDays/PendingStreakBonus only — the caller stamps LastLaunchDate.
    /// </summary>
    private static void ResolveStreakGapNow(this AchievementProgress p, DateTime lastDate, DateTime today, int daysMissed)
    {
        // Streak would break - try streak shield first
        if (App.SkillTree?.UseStreakShield() == true)
        {
            // Shield saved the streak! Increment as normal
            p.ConsecutiveDays++;
            App.Logger?.Information("Streak shield protected streak! Now at {Days} days", p.ConsecutiveDays);
            p.PendingStreakBonus = true;

            // EMI Desk: the streak survived, which is a keep and not a break.
            try { App.EmiDesk?.Fire("streakKept", new { streak = p.ConsecutiveDays }); } catch { }

            // Record the missed day(s) that were shielded
            var settings = App.Settings?.Current;
            if (settings != null)
            {
                for (var d = lastDate.AddDays(1); d < today; d = d.AddDays(1))
                {
                    if (!settings.StreakShieldUsedDates.Contains(d.Date))
                        settings.StreakShieldUsedDates.Add(d.Date);
                }
            }
        }
        else if (App.SkillTree?.UseOopsieInsurance() == true)
        {
            // A streak fix charge was spent automatically — keep current streak
            App.Logger?.Information("Oopsie Insurance auto-spent a streak fix, saving streak at {Days} days", p.ConsecutiveDays);

            // EMI Desk: same - a charge was spent, the number did not fall.
            try { App.EmiDesk?.Fire("streakKept", new { streak = p.ConsecutiveDays }); } catch { }
        }
        else
        {
            // Streak broken, reset to 1
            App.Logger?.Warning("Login streak RESET from {OldStreak} to 1 — gap of {Days} day(s), no shield/insurance available (last launch: {LastDate})",
                p.ConsecutiveDays, daysMissed, lastDate.ToString("yyyy-MM-dd"));

            // EMI Desk (MOMENTS 4.B): the streak the user HAD, read before the reset below wipes
            // it - a "you were on 40" line needs the 40, not the 1. common.encourage: never a scold.
            try { App.EmiDesk?.Fire("streakBroken", new { streak = p.ConsecutiveDays }); } catch { }

            p.ConsecutiveDays = 1;
        }
    }

    /// <summary>
    /// The deferral only makes sense when a cloud answer can actually arrive: a V2 identity to
    /// sync with, and sync not deliberately disabled. Everyone else gets the immediate decision.
    /// </summary>
    private static bool CanDeferStreakBreakToCloud()
    {
        var settings = App.Settings?.Current;
        return settings != null
            && !string.IsNullOrEmpty(settings.UnifiedId)
            && settings.OfflineMode != true;
    }

    /// <summary>
    /// Safety net for the deferral: if no sync resolves the pending break (server down, network
    /// gone, sync never attempted), fall back to the immediate decision after a grace window —
    /// exactly the behavior a signed-out user gets at launch.
    /// </summary>
    private static void ScheduleDeferredStreakBreakTimeout(this AchievementProgress p)
    {
        _ = System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(120)).ContinueWith(_ =>
        {
            try
            {
                if (System.Windows.Application.Current?.Dispatcher == null) return;
                p.ResolveDeferredStreakBreak("cloud answer timeout");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Deferred streak-break timeout failed: {Error}", ex.Message);
            }
        });
    }

    /// <summary>
    /// Settle a deferred streak break. Called after the first V2 sync of the launch has merged
    /// the cloud's streak fields (which may have moved <see cref="AchievementProgress.LastLaunchDate"/> forward over
    /// mobile-covered days), on sync failure, and by the timeout. Idempotent; safe from any
    /// thread (marshals itself to the UI dispatcher).
    /// </summary>
    public static void ResolveDeferredStreakBreak(this AchievementProgress p, string reason)
    {
        if (!p.PendingStreakBreak) return;

        // A profile switch / achievements reload can replace App.Achievements.Progress while
        // the 120s timeout closure still holds THIS instance. Resolving from a stale instance
        // would spend the shield/Oopsie tokens of whoever is signed in NOW (App.SkillTree is
        // global) for a gap that belongs to the OLD profile. A superseded instance only clears
        // its own flag and steps aside — the live instance re-detects its own gap at launch.
        if (!ReferenceEquals(App.Achievements?.Progress, p))
        {
            p.PendingStreakBreak = false;
            App.Logger?.Information("Deferred streak break ({Reason}) dropped: progress instance superseded", reason);
            return;
        }

        // Shutting down (or no WPF app at all): there is no dispatcher to marshal to and no
        // point burning tokens into state that may not save. Clear the flag and do nothing —
        // the next launch re-detects the same gap and defers again, which is the designed
        // no-state-to-migrate property of this flag.
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher == null || app.Dispatcher.HasShutdownStarted)
        {
            p.PendingStreakBreak = false;
            return;
        }

        var dispatcher = app.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => p.ResolveDeferredStreakBreak(reason)));
            return;
        }

        if (!p.PendingStreakBreak) return; // re-check after the hop — another caller may have won
        p.PendingStreakBreak = false;

        var today = DateTime.Today;
        var lastDate = p.LastLaunchDate.Date;
        App.Logger?.Information("Deferred streak break resolving ({Reason}): last banked {LastDate}, streak {Streak}",
            reason, lastDate.ToString("yyyy-MM-dd"), p.ConsecutiveDays);

        if (lastDate == today)
        {
            // The cloud merge moved the date all the way to today — the phone already banked this
            // day (and its streak figure was adopted with it). No increment and no bonus HERE:
            // paying the launch bonus for a day another device earned would double-pay it.
        }
        else if (lastDate == today.AddDays(-1))
        {
            // The cloud covered the gap up to yesterday — today is a normal launch increment.
            p.ConsecutiveDays++;
            p.PendingStreakBonus = true;
            p.LastLaunchDate = today;
        }
        else
        {
            // The gap is real even by the cloud's account — make the decision we deferred.
            // SkillTree exists by now (sync runs long after startup), so unlike the old
            // constructor-time path the shield can actually fire here.
            p.ResolveStreakGapNow(lastDate, today, (today - lastDate).Days);
            p.LastLaunchDate = today;
        }

        p.SyncCurrentStreak();
        SeasonRecapService.TrackStreakPeak(p.ConsecutiveDays);
        p.AwardDeferredStreakBonus();
        App.Achievements?.Save();
        App.Settings?.Save();
    }

    /// <summary>
    /// Day-advance entry point for a calendar rollover that happens while the app is RUNNING
    /// (PC left asleep, or CCP simply left open across midnight). Both cases previously lost the
    /// day entirely because <see cref="AchievementProgress.LastLaunchDate"/> was only ever written on the startup path.
    ///
    /// This intentionally delegates to <see cref="UpdateDailyStreak"/> rather than reimplementing
    /// the day-advance: that keeps Streak Shields, Oopsie Insurance, the shielded-date bookkeeping
    /// and the Season Recap hooks on exactly one code path, and it means <see cref="AchievementProgress.LastLaunchDate"/>
    /// still has exactly one writer. Since UpdateDailyStreak early-returns when the day is already
    /// banked, startup and rollover can never both bank the same date.
    ///
    /// Returns true if a day was banked (caller is then responsible for persisting).
    /// </summary>
    public static bool TryAdvanceDayRollover(this AchievementProgress p)
    {
        if (!AchievementProgress.ShouldBankDayRollover(p.LastLaunchDate, DateTime.Today)) return false;

        var before = p.LastLaunchDate.Date;
        p.UpdateDailyStreak();

        // UpdateDailyStreak is the sole writer of LastLaunchDate; if it did not move, nothing was
        // banked (defensive — with the guard above it always moves) and the caller should not save.
        return p.LastLaunchDate.Date != before;
    }

    /// <summary>
    /// Called after SkillTree is initialized to award streak bonus that was deferred during startup.
    /// </summary>
    public static void AwardDeferredStreakBonus(this AchievementProgress p)
    {
        if (!p.PendingStreakBonus) return;
        p.PendingStreakBonus = false;

        var streakXP = App.SkillTree?.GetDailyStreakBonus(p.ConsecutiveDays) ?? 0;
        if (streakXP > 0)
        {
            App.Progression?.AddXP(streakXP, XPSource.Other);
            App.Logger?.Information("Daily streak bonus! {Days} days - awarded {XP} XP", p.ConsecutiveDays, streakXP);
        }
    }

    /// <summary>
    /// Sync AppSettings.CurrentStreak with this.ConsecutiveDays
    /// </summary>
    public static void SyncCurrentStreak(this AchievementProgress p)
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;

        settings.CurrentStreak = p.ConsecutiveDays;
        settings.LastStreakDate = p.LastLaunchDate;
    }
}
