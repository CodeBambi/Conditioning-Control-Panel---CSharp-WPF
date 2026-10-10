using System;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The engine half of WPF <c>ProgressionService.AddXP</c> / <c>SpendXPOnLevels</c>, for a head that seeds
    /// <see cref="CoreProgression.AddXPProvider"/> with <see cref="Add"/>: the login gate, the XP add and the
    /// level loop with <c>HighestLevelEver</c>.
    /// ponytail: of the skill multiplier only Pink Rush's 3x is applied (sparkle boosts, streak power, night
    /// shift, early bird and event boost are not, so owners are under-awarded, which errs safe). Companion XP: CompanionCore.AddCompanionXP gets the base amount (progression#47). Skill points per level: <see cref="SkillPointsBank"/>. Level achievements listen to <see cref="LevelUp"/>; quests listen to <see cref="Awarded"/>
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
            // WPF ProgressionService.cs:70-78 anti-cheat: passive sources are dropped only while the user is
            // idle (ActivityIdle, 3 min without input), never while active.
            if (ActivityIdle.IsIdle && source is "Flash" or "Subliminal" or "BouncingText")
            {
                Log.Debug("XP suppressed (idle): +{Amount} from {Source}", amount, source);
                return;
            }
            // WPF ProgressionService.AddXP:90: the lasting Descent bonus for a migrated account (1.0 otherwise).
            // Awarded still carries the base amount: WPF feeds quests the base too (AddXP:120).
            // WPF AddXP:89 skill multiplier: only Pink Rush's 3x term so far (decisions 2026-10-08).
            var adjusted = amount * Models.PinkRushRules.XpFactor(s) * Services.Descent.DescentCycleXp.XpBonusFor(s);
            s.PlayerXP += adjusted;
            Log.Information("XP awarded: +{Amount} from {Source} (now {Now})", adjusted, source, s.PlayerXP);
            var epoch = ProfileAdopt.Epoch(s);
            var levels = new System.Collections.Generic.List<int>();
            for (var need = XpCurve.GetXPForLevel(s.PlayerLevel, epoch); s.PlayerXP >= need; need = XpCurve.GetXPForLevel(s.PlayerLevel, epoch))
            {
                s.PlayerXP -= need;
                s.PlayerLevel++;
                if (s.PlayerLevel > s.HighestLevelEver) s.HighestLevelEver = s.PlayerLevel;
                SkillPointsBank.OnLevelUp(s, s.PlayerLevel);   // WPF SpendXPOnLevels:303 App.SkillTree.OnLevelUp
                levels.Add(s.PlayerLevel);
                Log.Information("Level up! Now level {Level}", s.PlayerLevel);
            }
            // progression#47: WPF AddXP:73 App.Companion.AddCompanionXP(amount, source, context) - the BASE amount, after the gates.
            try { Services.Companion.CompanionCore.AddCompanionXP(amount, source); }
            catch (Exception ex) { Log.Debug("Companion XP failed: {E}", ex.Message); }
            CoreSettings.Save();
            foreach (var l in levels) LevelUp?.Invoke(l);
            Awarded?.Invoke(amount, source);
        }
    }
}
