using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services
{
    // Pure pieces of the WPF SessionEngine (ConditioningControlPanel/Services/Session/SessionEngine.cs),
    // ported rather than moved: the engine is pinned to the head by MainWindow/DispatcherTimer/App.*,
    // and WPF is frozen, so this duplication is deliberate (docs/avalonia-decisions.md).

    /// <summary>SessionEngine.ElapsedTime (SessionEngine.cs:103-127), minus the running/paused guards
    /// the runner owns.</summary>
    public static class SessionClock
    {
        /// <summary>Wall clock vs stopwatch divergence (either direction) beyond which the stopwatch wins (#369).</summary>
        public const double MaxDivergenceSeconds = 30;

        public static TimeSpan Elapsed(TimeSpan pausedElapsed, DateTime startTime, DateTime now, TimeSpan stopwatch)
        {
            var dateTimeElapsed = pausedElapsed + (now - startTime);
            var divergence = dateTimeElapsed - stopwatch;
            if (Math.Abs(divergence.TotalSeconds) > MaxDivergenceSeconds)
            {
                Log.Warning("Timer integrity: DateTime elapsed {DateTimeElapsed} vs Stopwatch {StopwatchElapsed} — divergence {Divergence}s, using Stopwatch",
                    dateTimeElapsed, stopwatch, divergence.TotalSeconds);
                return stopwatch;
            }
            return dateTimeElapsed < TimeSpan.Zero ? TimeSpan.Zero : dateTimeElapsed;
        }
    }

    /// <summary>The completed-session award, SessionEngine.cs:416-426.</summary>
    public static class SessionXp
    {
        public const int PausePenalty = 100;

        /// <summary>ProgressionService.GetSessionXPMultiplier (ProgressionService.cs:549).</summary>
        public static double Multiplier(int level)
        {
            if (level < 30) return 1.0;
            if (level < 80) return 1.0 + ((level - 30) * 0.01);
            if (level < 125) return 1.5 + ((level - 80) * 0.02);
            if (level < 150) return 2.4 + ((level - 125) * 0.03);
            return Math.Min(5.0, 3.15 + ((level - 150) * 0.03));
        }

        public static int Compute(int bonusXp, int pauseCount, int level, TimeSpan elapsed)
        {
            int baseXP = Math.Max(0, Math.Min(2500, bonusXp) - pauseCount * PausePenalty);
            double durationMinutes = Math.Max(0, elapsed.TotalMinutes - 2); // under 2 min earns no bonus
            int durationBonus = (int)Math.Round(durationMinutes * (8 + level * 0.15));
            return Math.Max(0, (int)Math.Round(baseXP * Multiplier(level)) + durationBonus);
        }
    }

    public static class SessionTimeline
    {
        /// <summary>SessionEngine.CheckPhaseTransition's lookup (SessionEngine.cs:664-677): the last
        /// phase whose StartMinute has arrived, else 0.</summary>
        public static int PhaseIndexAt(IReadOnlyList<SessionPhase>? phases, double elapsedMinutes)
        {
            if (phases == null) return 0;
            for (int i = phases.Count - 1; i >= 0; i--)
                if (elapsedMinutes >= phases[i].StartMinute) return i;
            return 0;
        }
    }

    /// <summary>Timeline "start at minute X" events (#483): SessionEngine._pendingFeatureStarts,
    /// DeferFeatureStart, IsFeaturePending and the loop in CheckDelayedFeatures (SessionEngine.cs:1326-1335, 818-847).</summary>
    public sealed class DeferredStartQueue
    {
        private readonly List<(string Name, int StartMinute, Action Start)> _pending = new();

        public void Defer(string name, int startMinute, Action start)
        {
            _pending.Add((name, startMinute, start));
            Log.Information("Session: {Feature} start deferred to minute {Minute}", name, startMinute);
        }

        public bool IsPending(string name) => _pending.Exists(p => p.Name == name);

        public void Clear() => _pending.Clear();

        /// <summary>Starts every entry whose minute has arrived. Reverse order, as WPF: firing removes in place.</summary>
        public void FireDue(double elapsedMinutes)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var pending = _pending[i];
                if (elapsedMinutes < pending.StartMinute) continue;
                _pending.RemoveAt(i);
                try
                {
                    pending.Start();
                    Log.Information("Session: {Feature} started at {Minutes:F1} minutes (target was {Target})",
                        pending.Name, elapsedMinutes, pending.StartMinute);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Session: deferred start of {Feature} failed", pending.Name);
                }
            }
        }
    }
}
