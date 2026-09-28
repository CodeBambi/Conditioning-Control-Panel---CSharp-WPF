using System;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The engine half of WPF <c>ProgressionService.AddXP</c> / <c>SpendXPOnLevels</c>, for a head that seeds
    /// <see cref="CoreProgression.AddXPProvider"/> with <see cref="Add"/>: the login gate, the XP add and the
    /// level loop with <c>HighestLevelEver</c>.
    /// ponytail: no skill/Cycle multipliers (skill-tree owners are under-awarded, which errs safe), no skill
    /// points, companion XP, quests, idle suppression or achievements - each lands with its ported feature.
    /// </summary>
    public static class ProgressionBank
    {
        /// <summary>The new level, once per level gained (WPF ProgressionService.LevelUp).</summary>
        public static event Action<int>? LevelUp;

        /// <summary>Amount and source after the award (WPF XPAwarded; the sync nudge listens).</summary>
        public static event Action<double, string>? Awarded;

        public static void Add(double amount, string source)
        {
            var s = CoreSettings.Current;
            var offline = s.OfflineMode && !string.IsNullOrWhiteSpace(s.OfflineUsername);
            if (!CoreAccount.IsLoggedIn && !offline)
            {
                Log.Debug("XP not awarded - user not logged in and not in offline mode");
                return;
            }
            s.PlayerXP += amount;
            Log.Information("XP awarded: +{Amount} from {Source} (now {Now})", amount, source, s.PlayerXP);
            var epoch = ProfileAdopt.Epoch(s);
            var levels = new System.Collections.Generic.List<int>();
            for (var need = XpCurve.GetXPForLevel(s.PlayerLevel, epoch); s.PlayerXP >= need; need = XpCurve.GetXPForLevel(s.PlayerLevel, epoch))
            {
                s.PlayerXP -= need;
                s.PlayerLevel++;
                if (s.PlayerLevel > s.HighestLevelEver) s.HighestLevelEver = s.PlayerLevel;
                levels.Add(s.PlayerLevel);
                Log.Information("Level up! Now level {Level}", s.PlayerLevel);
            }
            CoreSettings.Save();
            foreach (var l in levels) LevelUp?.Invoke(l);
            Awarded?.Invoke(amount, source);
        }
    }
}
