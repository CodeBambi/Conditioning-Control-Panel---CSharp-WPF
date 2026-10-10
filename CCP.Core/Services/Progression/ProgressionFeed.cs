using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel
{
    /// <summary>
    /// WPF <c>Services/Tracking/ActivityTracker</c> (7.1.5): the idle gate for passive XP. The head seeds
    /// <see cref="IdleSecondsProvider"/> with GetLastInputInfo (Windows); unseeded, or a probe that throws,
    /// reads as ACTIVE, exactly as WPF's failed GetLastInputInfo does ("assume active").
    /// ponytail: no Linux probe (X11 XScreenSaverQueryInfo) yet, so passive XP is never suppressed there.
    /// </summary>
    public static class ActivityIdle
    {
        /// <summary>WPF ActivityTracker.IdleThresholdSeconds: three minutes without input.</summary>
        public const int IdleThresholdSeconds = 180;

        /// <summary>Seconds since the last keyboard or mouse input, machine-wide.</summary>
        public static volatile Func<int>? IdleSecondsProvider;

        public static bool IsIdle
        {
            get
            {
                var p = IdleSecondsProvider;
                if (p == null) return false;
                try { return p() >= IdleThresholdSeconds; }
                catch { return false; }
            }
        }
    }

    /// <summary>
    /// WPF <c>SkillTreeService.OnLevelUp</c> (SkillTreeService.cs:847) and the 100-bubble milestone of
    /// <c>AchievementService.TrackBubblePopped</c> (:607): the two places Sparkle Points are minted locally.
    /// Callers save. Each mint publishes <see cref="SparklePointRewards"/> (presentation only: the header
    /// wallet's "+N"), so a balance restored or adopted from the server stays quiet, as in WPF.
    /// ponytail: no TrackSkillPointsEarned stat (not in the port's AchievementEngine yet).
    /// </summary>
    public static class SkillPointsBank
    {
        /// <summary>WPF SkillTreeService.PointsPerLevel.</summary>
        public const int PointsPerLevel = 1;

        /// <summary>One level gained: +1 SP and the season peak level. No save (the level loop saves).</summary>
        public static void OnLevelUp(AppSettings s, int newLevel, int levelsGained = 1)
        {
            var points = levelsGained * PointsPerLevel;
            var previous = s.SkillPoints;
            s.SkillPoints += points;
            s.SeasonPeakLevel = Math.Max(s.SeasonPeakLevel, newLevel);
            try { AchievementEngine.Current?.TrackSkillPointsEarned(points); } catch { /* a stat, never the wallet */ }   // WPF SkillTreeService.cs:858
            Log.Information("Level up to {Level}! Awarded {Points} skill points. Total: {Total}", newLevel, points, s.SkillPoints);
            SparklePointRewards.PublishCredit(previous, s.SkillPoints, SparklePointSource.LevelUp);
        }

        /// <summary>Every 100-bubble boundary crossed between <paramref name="before"/> and
        /// <paramref name="after"/> pays 1 SP (WPF TrackBubblePopped / TrackBubblesPopped). Returns the points.</summary>
        public static int CreditBubbleMilestones(AppSettings s, int before, int after)
        {
            var milestones = Math.Max(0, after / 100 - before / 100);
            if (milestones == 0) return 0;
            var previous = s.SkillPoints;
            s.SkillPoints += milestones;
            SparklePointRewards.PublishCredit(previous, s.SkillPoints, SparklePointSource.BubbleMilestone);
            Log.Information("Bubble milestone! {Total} bubbles popped - awarded {N} sparkle point(s) (total: {Points})",
                after, milestones, s.SkillPoints);
            return milestones;
        }
    }

    /// <summary>
    /// WPF <c>MainWindow.StartConditioningTimeTracker</c> / <c>StopConditioningTimeTracker</c>
    /// (MainWindow.UiUpdates.cs:746-840) and <c>SkillTreeService.AddConditioningTime</c> (:804): while the
    /// engine runs, one minute is credited per 60 one-second ticks, and on stop the remainder up to the
    /// wall-clock elapsed time. The ONLY writer of <c>TotalConditioningMinutes</c> (7.1.5 removed the
    /// achievement tick's second credit, which double counted), so every minute lands once.
    /// <see cref="CoreEngine"/> calls <see cref="OnEngineStarted"/> / <see cref="OnEngineStopped"/>; the head
    /// drives <see cref="Tick"/> once a second.
    /// ponytail: no server sync every 15 minutes (SyncPush does not carry total_conditioning_minutes yet)
    /// and no Season Recap mirror (seasons are retired).
    /// </summary>
    public static class ConditioningTime
    {
        private static readonly object _gate = new();
        private static DateTime? _startedAt;
        private static double _baselineMinutes;
        private static int _secondCounter;

        public static bool IsTracking { get { lock (_gate) return _startedAt != null; } }

        /// <summary>WPF AddConditioningTime: nothing to add, nothing to write.</summary>
        public static void Add(double minutes)
        {
            if (!(minutes > 0)) return;
            var s = CoreSettings.Current;
            s.TotalConditioningMinutes += minutes;
            CoreSettings.Save();
        }

        /// <summary>WPF StartConditioningTimeTracker. Already tracking: a no-op.</summary>
        public static void OnEngineStarted(DateTime now)
        {
            lock (_gate)
            {
                if (_startedAt != null) return;
                _startedAt = now;
                _baselineMinutes = CoreSettings.Current.TotalConditioningMinutes;
                _secondCounter = 0;
            }
            Log.Debug("Conditioning time tracker started");
        }

        /// <summary>One one-second tick: every 60th credits a minute.</summary>
        public static void Tick(DateTime now)
        {
            lock (_gate)
            {
                if (_startedAt == null) return;
                if (++_secondCounter < 60) return;
                _secondCounter = 0;
            }
            try { Add(1.0); }
            catch (Exception ex) { Log.Warning(ex, "Error tracking conditioning time"); }
        }

        /// <summary>WPF StopConditioningTimeTracker: credit what the minute ticks have not, measured
        /// from the baseline taken at start. Not tracking: a no-op.</summary>
        public static void OnEngineStopped(DateTime now)
        {
            double remaining;
            lock (_gate)
            {
                if (_startedAt is not { } started) return;
                _startedAt = null;
                var expected = _baselineMinutes + (now - started).TotalMinutes;
                remaining = expected - CoreSettings.Current.TotalConditioningMinutes;
            }
            try
            {
                if (remaining > 0) Add(remaining);
                Log.Debug("Conditioning time tracker stopped (+{Minutes:F2} min on stop)", Math.Max(0, remaining));
            }
            catch (Exception ex) { Log.Warning(ex, "Error finalizing conditioning time"); }
        }
    }
}
