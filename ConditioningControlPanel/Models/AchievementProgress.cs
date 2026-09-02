using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Models;

/// <summary>
/// Tracks progress towards all achievements
/// </summary>
public class AchievementProgress
{
    // ========== UNLOCKED ACHIEVEMENTS ==========
    public HashSet<string> UnlockedAchievements { get; set; } = new();
    
    // ========== PROGRESSION STATS ==========
    // (Level is tracked in AppSettings.PlayerLevel)
    
    // ========== TIME TRACKING ==========
    /// <summary>Total minutes with Pink Filter active</summary>
    public double TotalPinkFilterMinutes { get; set; }
    
    /// <summary>Total minutes with Spiral Overlay active</summary>
    public double TotalSpiralMinutes { get; set; }
    
    /// <summary>Current continuous spiral minutes (resets when disabled)</summary>
    public double ContinuousSpiralMinutes { get; set; }
    
    /// <summary>Total flash images shown</summary>
    public int TotalFlashImages { get; set; }
    
    /// <summary>Consecutive days app was launched</summary>
    public int ConsecutiveDays { get; set; }
    
    /// <summary>Last date the app was launched (for streak tracking)</summary>
    public DateTime LastLaunchDate { get; set; }
    
    // ========== SESSION TRACKING ==========
    /// <summary>Whether Alt+Tab was pressed during current session</summary>
    public bool AltTabPressedThisSession { get; set; }
    
    /// <summary>Time when ESC/Panic was last pressed (for Relapse tracking)</summary>
    public DateTime? LastPanicPressTime { get; set; }
    
    /// <summary>Longest session completed in minutes</summary>
    public double LongestSessionMinutes { get; set; }
    
    // ========== MINIGAME STATS ==========
    /// <summary>Total bubbles popped</summary>
    public int TotalBubblesPopped { get; set; }
    
    /// <summary>Current streak of correct bubble count guesses</summary>
    public int BubbleCountCorrectStreak { get; set; }
    
    /// <summary>Best bubble count correct streak</summary>
    public int BubbleCountBestStreak { get; set; }
    
    /// <summary>Times attention check failed (for Mercy Beggar)</summary>
    public int AttentionCheckFailures { get; set; }
    
    /// <summary>Current continuous Mind Wipe seconds</summary>
    public double ContinuousMindWipeSeconds { get; set; }
    
    /// <summary>Has achieved 100% accuracy on a Lock Card</summary>
    public bool HasPerfectLockCard { get; set; }
    
    /// <summary>Fastest Lock Card completion time in seconds (3 phrases)</summary>
    public double FastestLockCardSeconds { get; set; } = double.MaxValue;

    /// <summary>Total minutes of video watched</summary>
    public double TotalVideoMinutes { get; set; }

    /// <summary>Total lock cards completed</summary>
    public int TotalLockCardsCompleted { get; set; }

    /// <summary>Whether bouncing text has hit a corner</summary>
    public bool HasHitCorner { get; set; }

    // ========== ATTENTION CHECK STATS ==========
    /// <summary>Total attention checks passed (all types)</summary>
    public int TotalAttentionChecksPassed { get; set; }

    /// <summary>Total video attention checks passed</summary>
    public int VideoAttentionChecksPassed { get; set; }

    /// <summary>Total video attention checks failed</summary>
    public int VideoAttentionChecksFailed { get; set; }

    // ========== BUBBLE COUNT STATS ==========
    /// <summary>Total bubble count games played</summary>
    public int TotalBubbleCountGames { get; set; }

    /// <summary>Total bubble count games completed correctly</summary>
    public int TotalBubbleCountCorrect { get; set; }

    /// <summary>Total bubble count games failed</summary>
    public int TotalBubbleCountFailed { get; set; }

    // ========== SESSION STATS ==========
    /// <summary>Total sessions started (may not be completed)</summary>
    public int TotalSessionsStarted { get; set; }

    /// <summary>Total sessions abandoned (started but not completed)</summary>
    public int TotalSessionsAbandoned { get; set; }

    // ========== XP & PROGRESSION STATS ==========
    /// <summary>All-time total XP earned (across all levels)</summary>
    public double TotalXPEarned { get; set; }

    /// <summary>All-time total skill points earned</summary>
    public int TotalSkillPointsEarned { get; set; }

    /// <summary>
    /// All-time sparkle points SPENT on enhancements — the Prestige metric. Monotonic:
    /// never reset by seasons, logout-safe via achievements.json, server-reconciled
    /// upward from lifetime_points_spent.
    ///
    /// <para>It used to be fed by the monthly re-buy of the mechanical nodes. The Descent ended
    /// seasons and every skill is permanent now, so this counts a tree that is bought once and
    /// then stops: an honest record of lifetime spend with no recurring sink behind it. Whether
    /// Prestige gets a new one is an open design question, deliberately unanswered here.</para>
    /// </summary>
    public long LifetimeSkillPointsSpent { get; set; }
    
    /// <summary>Avatar click count for rapid clicking detection</summary>
    public int AvatarClickCount { get; set; }
    
    /// <summary>Time of first avatar click in current rapid sequence</summary>
    public DateTime? AvatarClickStartTime { get; set; }

    /// <summary>Click count toward the "needy doll" easter egg (150 clicks in 60 seconds)</summary>
    public int NeedyDollClickCount { get; set; }

    /// <summary>Start of the current needy-doll click window</summary>
    public DateTime? NeedyDollClickStartTime { get; set; }
    
    // ========== SESSION COMPLETION TRACKING ==========
    public HashSet<string> CompletedSessions { get; set; } = new();
    
    /// <summary>Sessions completed with specific conditions</summary>
    public bool CompletedGoodGirlsWithStrictLock { get; set; }
    public bool CompletedMorningDriftInMorning { get; set; }
    public bool CompletedGamerGirlNoAltTab { get; set; }
    public bool CompletedSessionWithNoPanic { get; set; }
    
    // ========== COMBINATION TRACKING ==========
    /// <summary>Has had Strict Lock + No Panic + Pink Filter all active</summary>
    public bool HasTotalLockdown { get; set; }

    /// <summary>Has had Bubbles + Bouncing Text + Spiral all active</summary>
    public bool HasSystemOverload { get; set; }

    // ========== GAMIFICATION BRIDGE STATS (achievements v2) ==========
    // Persisted lifetime counters fed by GamificationBridge subscriptions.

    /// <summary>Deeper enhancements played to completion (Phase 2)</summary>
    public int EnhancementsPlayed { get; set; }

    /// <summary>Total minutes spent in the Deeper player (Phase 2)</summary>
    public double DeeperMinutes { get; set; }

    /// <summary>Enhancements built/saved in the Deeper editor</summary>
    public int EnhancementsBuilt { get; set; }

    /// <summary>Mods installed (proxied by first activation today)</summary>
    public int ModsInstalled { get; set; }

    /// <summary>Distinct mod ids ever activated (for the Curator count)</summary>
    public HashSet<string> ActivatedModIds { get; set; } = new();

    /// <summary>Distinct community (non-builtin) mod ids activated (for Community Supported)</summary>
    public HashSet<string> CommunityModIds { get; set; } = new();

    /// <summary>Quiz category ids the user has perfected (for Honor Roll)</summary>
    public HashSet<string> PerfectedQuizCategories { get; set; } = new();

    /// <summary>Lifetime keyword triggers fired</summary>
    public int KeywordTriggersFired { get; set; }

    /// <summary>Messages the user has sent to the companion</summary>
    public int CompanionMessages { get; set; }

    /// <summary>
    /// One-shot latch for the #877 retroactive chat backfill. The companion-chat counter was
    /// only ever fed by the tube's legacy send handler, so every message routed through the
    /// modern brain funnel (and everything sent from Her Room) counted for nothing. On the
    /// first launch after the fix, <see cref="Services.GamificationBridge"/> reconstructs the
    /// counter from what the companion actually persisted and sets this. Persisted precisely
    /// so it runs ONCE — the evidence it reads (the restored turn log) is a rolling window,
    /// so re-running it every launch would flip the counter up and down forever.
    /// </summary>
    public bool CompanionChatBackfilled { get; set; }

    /// <summary>Quizzes passed (Phase 2)</summary>
    public int QuizzesPassed { get; set; }

    /// <summary>Consecutive quizzes failed; resets to 0 on a pass (Phase 2)</summary>
    public int QuizFailStreak { get; set; }

    /// <summary>Consecutive intakes quit before they reported a result; resets to 0 on any
    /// finished graded run. The intake has no fail state, so this is what feeds "Held Back"
    /// now that the classic quiz is retired (the fail streak above still counts for anyone
    /// still playing it).</summary>
    public int IntakeQuitStreak { get; set; }

    /// <summary>Blinks logged while the Blink Trainer is running</summary>
    public int BlinkTrainerBlinks { get; set; }

    /// <summary>Bubbles/flashes popped by gaze dwell (Phase 2 — needs GazePopped event)</summary>
    public int GazePops { get; set; }

    // ----- transient per-run trackers (not persisted) -----

    /// <summary>Remote commands received in the current remote session</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int RemoteCommandsThisSession { get; set; }

    /// <summary>Distinct Deeper trigger types fired during the current play (Phase 2)</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public HashSet<string> DistinctTriggerTypesThisPlay { get; set; } = new();

    // ========== HELPER METHODS ==========
    
    public bool IsUnlocked(string achievementId) => UnlockedAchievements.Contains(achievementId);
    
    public void Unlock(string achievementId)
    {
        if (!UnlockedAchievements.Contains(achievementId))
        {
            UnlockedAchievements.Add(achievementId);
        }
    }
    
    /// <summary>
    /// Whether a streak bonus needs to be awarded after SkillTree is initialized.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool PendingStreakBonus { get; set; }

    /// <summary>
    /// Mobile streak parity: a launch-time gap was detected but the break decision (shield burn /
    /// Oopsie spend / reset) is DEFERRED until the first V2 sync answers — the phone may have kept
    /// the streak alive on days this machine never launched, and burning a shield for a gap the
    /// cloud says never happened wastes a real, finite token. Deliberately NOT persisted: if the
    /// app dies while pending, <see cref="LastLaunchDate"/> was never stamped, so the next launch
    /// re-detects the same gap and defers again — no state to migrate, nothing to get stuck.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool PendingStreakBreak { get; private set; }

    /// <summary>
    /// Check and update consecutive days streak.
    /// Integrates streak shields, oopsie insurance, milestone rewards, and CurrentStreak sync.
    /// </summary>
    public void UpdateDailyStreak()
    {
        // Season Recap (local-only): mark today as an active day this season, and capture the
        // current streak as a season peak. Done BEFORE the same-day early-return below so a
        // relaunch on a day we already counted still records the peak (the end-of-method call
        // only fires when the streak actually changes).
        SeasonRecapService.MarkActiveToday();
        SeasonRecapService.TrackStreakPeak(ConsecutiveDays);

        var today = DateTime.Today;
        var lastDate = LastLaunchDate.Date;

        // A deferred break decision is in flight (waiting on the first V2 sync / its timeout).
        // ResolveDeferredStreakBreak owns the next write to LastLaunchDate — running the gap
        // math again here would burn the shield the deferral exists to protect.
        if (PendingStreakBreak) return;

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
            if (ConsecutiveDays < 1) ConsecutiveDays = 1;
            LastLaunchDate = today;
            SyncCurrentStreak();
            SeasonRecapService.TrackStreakPeak(ConsecutiveDays);
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
            ConsecutiveDays++;
            PendingStreakBonus = true;

            // EMI Desk (MOMENTS 4.B). The milestone days are the bark's StreakMilestone and reach
            // her through the mirror; this is the ordinary day that kept it alive.
            try { App.EmiDesk?.Fire("streakKept", new { streak = ConsecutiveDays }); } catch { }
        }
        else
        {
            var daysMissed = (today - lastDate).Days;
            App.Logger?.Information("Login streak gap detected: {Days} day(s) missed (last launch: {LastDate}, today: {Today}, streak was: {Streak})",
                daysMissed, lastDate.ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd"), ConsecutiveDays);

            // Mobile streak parity: a signed-in account may have kept this streak alive on the
            // phone. Hold the break decision (and the shield/Oopsie tokens) until the first V2
            // sync merges the cloud's last_streak_date/consecutive_days, or the timeout gives up.
            // LastLaunchDate is deliberately NOT stamped: the push that races this deferral must
            // carry the honest pre-gap date, not claim today.
            if (CanDeferStreakBreakToCloud())
            {
                PendingStreakBreak = true;
                App.Logger?.Information("Login streak: break deferred pending cloud answer (mobile may have covered the gap)");
                ScheduleDeferredStreakBreakTimeout();
                return;
            }

            ResolveStreakGapNow(lastDate, today, daysMissed);
        }

        LastLaunchDate = today;

        // Sync CurrentStreak in AppSettings with ConsecutiveDays
        SyncCurrentStreak();

        // Season Recap (local-only): keep the season peak streak. Tracked separately from
        // CurrentStreak because the server-driven season reset can zero CurrentStreak before
        // the recap snapshot runs — the peak must survive that.
        SeasonRecapService.TrackStreakPeak(ConsecutiveDays);
    }

    /// <summary>
    /// The actual streak-break spend/reset, extracted so the deferred path and the immediate path
    /// share one implementation: shield first, then Oopsie Insurance, then reset to 1.
    /// Mutates ConsecutiveDays/PendingStreakBonus only — the caller stamps LastLaunchDate.
    /// </summary>
    private void ResolveStreakGapNow(DateTime lastDate, DateTime today, int daysMissed)
    {
        // Streak would break - try streak shield first
        if (App.SkillTree?.UseStreakShield() == true)
        {
            // Shield saved the streak! Increment as normal
            ConsecutiveDays++;
            App.Logger?.Information("Streak shield protected streak! Now at {Days} days", ConsecutiveDays);
            PendingStreakBonus = true;

            // EMI Desk: the streak survived, which is a keep and not a break.
            try { App.EmiDesk?.Fire("streakKept", new { streak = ConsecutiveDays }); } catch { }

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
            App.Logger?.Information("Oopsie Insurance auto-spent a streak fix, saving streak at {Days} days", ConsecutiveDays);

            // EMI Desk: same - a charge was spent, the number did not fall.
            try { App.EmiDesk?.Fire("streakKept", new { streak = ConsecutiveDays }); } catch { }
        }
        else
        {
            // Streak broken, reset to 1
            App.Logger?.Warning("Login streak RESET from {OldStreak} to 1 — gap of {Days} day(s), no shield/insurance available (last launch: {LastDate})",
                ConsecutiveDays, daysMissed, lastDate.ToString("yyyy-MM-dd"));

            // EMI Desk (MOMENTS 4.B): the streak the user HAD, read before the reset below wipes
            // it - a "you were on 40" line needs the 40, not the 1. common.encourage: never a scold.
            try { App.EmiDesk?.Fire("streakBroken", new { streak = ConsecutiveDays }); } catch { }

            ConsecutiveDays = 1;
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
    private void ScheduleDeferredStreakBreakTimeout()
    {
        _ = System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(120)).ContinueWith(_ =>
        {
            try
            {
                if (System.Windows.Application.Current?.Dispatcher == null) return;
                ResolveDeferredStreakBreak("cloud answer timeout");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Deferred streak-break timeout failed: {Error}", ex.Message);
            }
        });
    }

    /// <summary>
    /// Settle a deferred streak break. Called after the first V2 sync of the launch has merged
    /// the cloud's streak fields (which may have moved <see cref="LastLaunchDate"/> forward over
    /// mobile-covered days), on sync failure, and by the timeout. Idempotent; safe from any
    /// thread (marshals itself to the UI dispatcher).
    /// </summary>
    public void ResolveDeferredStreakBreak(string reason)
    {
        if (!PendingStreakBreak) return;

        // A profile switch / achievements reload can replace App.Achievements.Progress while
        // the 120s timeout closure still holds THIS instance. Resolving from a stale instance
        // would spend the shield/Oopsie tokens of whoever is signed in NOW (App.SkillTree is
        // global) for a gap that belongs to the OLD profile. A superseded instance only clears
        // its own flag and steps aside — the live instance re-detects its own gap at launch.
        if (!ReferenceEquals(App.Achievements?.Progress, this))
        {
            PendingStreakBreak = false;
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
            PendingStreakBreak = false;
            return;
        }

        var dispatcher = app.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => ResolveDeferredStreakBreak(reason)));
            return;
        }

        if (!PendingStreakBreak) return; // re-check after the hop — another caller may have won
        PendingStreakBreak = false;

        var today = DateTime.Today;
        var lastDate = LastLaunchDate.Date;
        App.Logger?.Information("Deferred streak break resolving ({Reason}): last banked {LastDate}, streak {Streak}",
            reason, lastDate.ToString("yyyy-MM-dd"), ConsecutiveDays);

        if (lastDate == today)
        {
            // The cloud merge moved the date all the way to today — the phone already banked this
            // day (and its streak figure was adopted with it). No increment and no bonus HERE:
            // paying the launch bonus for a day another device earned would double-pay it.
        }
        else if (lastDate == today.AddDays(-1))
        {
            // The cloud covered the gap up to yesterday — today is a normal launch increment.
            ConsecutiveDays++;
            PendingStreakBonus = true;
            LastLaunchDate = today;
        }
        else
        {
            // The gap is real even by the cloud's account — make the decision we deferred.
            // SkillTree exists by now (sync runs long after startup), so unlike the old
            // constructor-time path the shield can actually fire here.
            ResolveStreakGapNow(lastDate, today, (today - lastDate).Days);
            LastLaunchDate = today;
        }

        SyncCurrentStreak();
        SeasonRecapService.TrackStreakPeak(ConsecutiveDays);
        AwardDeferredStreakBonus();
        App.Achievements?.Save();
        App.Settings?.Save();
    }

    /// <summary>
    /// Pure merge rule for adopting the cloud's login-streak pair (consecutive_days +
    /// last_streak_date) into the local one. Mirrors the mobile client's decideLoginStreakAdopt
    /// (CCPMobile src/lib/sync/contract.ts) so the two clients converge on the same answer:
    ///  - a zero/negative server streak carries no run to merge — refuse it outright (both
    ///    clients), or a degenerate record's DATE alone could move LastLaunchDate forward;
    ///  - never lowers the local streak (the server ratchet is preserved deliberately);
    ///  - a server date exactly one day AFTER the local one means the runs are contiguous — the
    ///    local run extends it (max(server, local+1)), not just max;
    ///  - symmetrically, a local date one day after the server's takes max(local, server+1);
    ///  - a newer server date is adopted (clamped to today so a timezone-skewed phone can never
    ///    push LastLaunchDate into the future, which would read as a negative gap next launch);
    ///  - a wide gap in either direction falls back to plain max — the preserved ratchet.
    /// One deliberate divergence from the mobile twin: with NO usable server date the desktop
    /// still does a date-blind take-higher (the pre-parity behavior, kept for records written
    /// by servers older than the parity deploy); mobile answers null there and leaves local
    /// alone, because it has no pre-parity history to stay compatible with.
    /// Returns null when nothing changes. Static and clock-free for testability.
    /// </summary>
    public static (int Streak, DateTime LastDate)? DecideLoginStreakAdopt(
        int localStreak, DateTime localLastDate, int serverStreak, DateTime? serverLastDate, DateTime today)
    {
        if (serverStreak <= 0) return null;

        var local = localLastDate.Date;
        int nextStreak;
        DateTime nextDate;

        if (serverLastDate == null || serverLastDate.Value.Date == default)
        {
            // No usable server date — date-blind take-higher, the pre-parity behavior.
            nextStreak = Math.Max(localStreak, serverStreak);
            nextDate = local;
        }
        else
        {
            var server = serverLastDate.Value.Date;
            if (server > today.Date) server = today.Date;

            if (local == default)
            {
                nextStreak = Math.Max(localStreak, serverStreak);
                nextDate = server;
            }
            else if (server == local)
            {
                nextStreak = Math.Max(localStreak, serverStreak);
                nextDate = local;
            }
            else if (server == local.AddDays(1))
            {
                nextStreak = Math.Max(serverStreak, localStreak + 1);
                nextDate = server;
            }
            else if (server > local)
            {
                nextStreak = Math.Max(serverStreak, localStreak);
                nextDate = server;
            }
            else if (local == server.AddDays(1))
            {
                nextStreak = Math.Max(localStreak, serverStreak + 1);
                nextDate = local;
            }
            else
            {
                nextStreak = Math.Max(localStreak, serverStreak);
                nextDate = local;
            }
        }

        // Backstop only — every branch above max()es with localStreak, so this cannot fire
        // today. It stays to keep the "never lowers" contract true against future edits.
        if (nextStreak < localStreak) return null;
        if (nextStreak == localStreak && nextDate == local) return null;
        return (nextStreak, nextDate);
    }

    /// <summary>
    /// Pure decision function for the midnight-rollover path: given the last banked day and the
    /// current day, should the day-advance path run right now?
    ///
    /// Deliberately pure (no clock, no settings, no services) so the correctness argument is a
    /// property of two dates rather than of wall-clock timing, which cannot be tested by waiting.
    ///
    /// Returns false when:
    ///  - <paramref name="lastBankedDate"/> is default/MinValue. That is the "no local launch
    ///    history" case which <see cref="UpdateDailyStreak"/>'s startup path owns exclusively
    ///    (it defers to the cloud restore). The rollover path must never claim a first run.
    ///  - the day has not advanced (<c>today == lastBanked</c>) — today is already banked, so
    ///    running again would be the double-count we must never produce.
    ///  - the day went BACKWARDS (<c>today &lt; lastBanked</c>) — a clock correction or a
    ///    timezone change. Re-running the gap logic there would compute a negative gap and reset
    ///    a healthy streak to 1, so we simply stand still and let the next real day advance it.
    /// </summary>
    public static bool ShouldBankDayRollover(DateTime lastBankedDate, DateTime today)
    {
        var last = lastBankedDate.Date;
        if (last == default) return false;
        return today.Date > last;
    }

    /// <summary>
    /// Day-advance entry point for a calendar rollover that happens while the app is RUNNING
    /// (PC left asleep, or CCP simply left open across midnight). Both cases previously lost the
    /// day entirely because <see cref="LastLaunchDate"/> was only ever written on the startup path.
    ///
    /// This intentionally delegates to <see cref="UpdateDailyStreak"/> rather than reimplementing
    /// the day-advance: that keeps Streak Shields, Oopsie Insurance, the shielded-date bookkeeping
    /// and the Season Recap hooks on exactly one code path, and it means <see cref="LastLaunchDate"/>
    /// still has exactly one writer. Since UpdateDailyStreak early-returns when the day is already
    /// banked, startup and rollover can never both bank the same date.
    ///
    /// Returns true if a day was banked (caller is then responsible for persisting).
    /// </summary>
    public bool TryAdvanceDayRollover()
    {
        if (!ShouldBankDayRollover(LastLaunchDate, DateTime.Today)) return false;

        var before = LastLaunchDate.Date;
        UpdateDailyStreak();

        // UpdateDailyStreak is the sole writer of LastLaunchDate; if it did not move, nothing was
        // banked (defensive — with the guard above it always moves) and the caller should not save.
        return LastLaunchDate.Date != before;
    }

    /// <summary>
    /// Called after SkillTree is initialized to award streak bonus that was deferred during startup.
    /// </summary>
    public void AwardDeferredStreakBonus()
    {
        if (!PendingStreakBonus) return;
        PendingStreakBonus = false;

        var streakXP = App.SkillTree?.GetDailyStreakBonus(ConsecutiveDays) ?? 0;
        if (streakXP > 0)
        {
            App.Progression?.AddXP(streakXP, XPSource.Other);
            App.Logger?.Information("Daily streak bonus! {Days} days - awarded {XP} XP", ConsecutiveDays, streakXP);
        }
    }

    /// <summary>
    /// Sync AppSettings.CurrentStreak with this.ConsecutiveDays
    /// </summary>
    public void SyncCurrentStreak()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;

        settings.CurrentStreak = ConsecutiveDays;
        settings.LastStreakDate = LastLaunchDate;
    }
    
    /// <summary>
    /// Reset session-specific tracking
    /// </summary>
    public void ResetSessionTracking()
    {
        AltTabPressedThisSession = false;
    }
    
    /// <summary>
    /// Track avatar click for rapid clicking achievement
    /// </summary>
    public bool TrackAvatarClick()
    {
        var now = DateTime.Now;
        
        // 20 clicks in 10 seconds (instead of 5 - more achievable)
        if (AvatarClickStartTime == null || (now - AvatarClickStartTime.Value).TotalSeconds > 10)
        {
            // Start new sequence
            AvatarClickStartTime = now;
            AvatarClickCount = 1;
        }
        else
        {
            // Continue sequence
            AvatarClickCount++;
        }
        
        // Check if 20 clicks in 10 seconds
        return AvatarClickCount >= 20;
    }

    /// <summary>
    /// Track avatar click for the "needy doll" easter egg (150 clicks in 60 seconds).
    /// Independent window from the 20-in-10s neon-obsession tracker.
    /// </summary>
    public bool TrackNeedyDollClick()
    {
        var now = DateTime.Now;
        if (NeedyDollClickStartTime == null || (now - NeedyDollClickStartTime.Value).TotalSeconds > 60)
        {
            NeedyDollClickStartTime = now;
            NeedyDollClickCount = 1;
        }
        else
        {
            NeedyDollClickCount++;
        }
        return NeedyDollClickCount >= 150;
    }
}
