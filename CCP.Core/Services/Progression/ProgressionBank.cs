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
    /// points or companion XP - each lands with its ported feature. Level achievements listen to <see cref="LevelUp"/>; quests listen to <see cref="Awarded"/>
    /// (the Avalonia head feeds it to QuestService.TrackXPEarned, as WPF AddXP:120 does).
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
            // ponytail: no idle tracker on this head, so the passive sources WPF suppresses while idle
            // (ProgressionService.cs:70-78) are never banked; bank them again once an idle tracker exists.
            if (source is "Flash" or "Subliminal" or "BouncingText")
            {
                Log.Debug("XP not banked: +{Amount} from passive {Source} (no idle tracker)", amount, source);
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
