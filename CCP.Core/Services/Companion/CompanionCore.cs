using System;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// The settings half of WPF 7.1.5 <c>Services/Companion/CompanionService.cs</c> (ai#5 / progression#47):
    /// which companion is active, switching it, companion XP with the perk modifier and the level loop,
    /// the Leech drain tick and the active-time tally. Same numbers as WPF. Timers and UI live in the
    /// head (it drives <see cref="DrainTick"/> every <see cref="DrainIntervalSeconds"/> and
    /// <see cref="UpdateActiveTime"/> every minute). Named CompanionCore so it never collides with the
    /// WPF head's own CompanionService type.
    /// </summary>
    public static class CompanionCore
    {
        public const double DrainXpPerTick = 3.0;          // WPF DRAIN_XP_PER_TICK
        public const double DrainIntervalSeconds = 2.0;    // WPF DRAIN_INTERVAL_SECONDS
        public static readonly TimeSpan DrainSaveInterval = TimeSpan.FromSeconds(30);   // WPF DrainSaveThrottle (#1311)

        /// <summary>WPF CompanionLevelUp: companion and its new level, once per level gained.</summary>
        public static event Action<CompanionId, int>? LevelUp;
        /// <summary>WPF CompanionSwitched.</summary>
        public static event Action<CompanionId>? Switched;
        /// <summary>WPF XPDrained (the Leech perk took player XP).</summary>
        public static event Action<double>? XpDrained;
        /// <summary>WPF XPAwarded: companion, final amount, modifier.</summary>
        public static event Action<CompanionId, double, double>? XpAwarded;

        /// <summary>WPF XPContext.TriggeredByAutonomy (App.Autonomy.IsActionInProgress). Unseeded = false.</summary>
        public static volatile Func<bool>? AutonomyActionInProgress;

        /// <summary>WPF ApplyCompanionPrompt's App.CommunityPrompts.ActivatePrompt(id). Unseeded = the
        /// head has no community prompt service, so an assigned prompt is not applied.</summary>
        public static volatile Action<string>? ActivatePrompt;

        private static DateTime _lastActiveTimeUpdate = DateTime.Now;
        private static DateTime? _lastDrainSaveUtc;

        public static CompanionId ActiveCompanion => (CompanionId)CoreSettings.Current.ActiveCompanionId;
        public static CompanionDefinition ActiveDef => CompanionDefinition.GetById(ActiveCompanion);
        public static CompanionProgress ActiveProgress => GetProgress(ActiveCompanion);

        public static CompanionBonusType ActivePerk => CompanionPerks.Resolve(
            CoreSettings.Current.CompanionPerk, ActiveDef.BonusType, CompanionExperience.IsV2Enabled);

        /// <summary>WPF ctor: a v2 user with no saved perk gets the bundle perk of the active companion.</summary>
        public static void EnsurePerkSeeded()
        {
            var s = CoreSettings.Current;
            if (CompanionExperience.IsV2Enabled && s.CompanionPerk == null)
            {
                s.CompanionPerk = ActiveDef.BonusType;
                CoreSettings.Save();
            }
            _lastActiveTimeUpdate = DateTime.Now;
        }

        public static CompanionProgress GetProgress(CompanionId id)
        {
            var s = CoreSettings.Current;
            if (!s.CompanionProgressData.TryGetValue((int)id, out var progress))
            {
                progress = CompanionProgress.CreateNew(id);
                s.CompanionProgressData[(int)id] = progress;
            }
            return progress;
        }

        /// <summary>WPF SwitchCompanion (CompanionService.cs:185).</summary>
        public static bool SwitchCompanion(CompanionId newCompanion)
        {
            var def = CompanionDefinition.GetById(newCompanion);
            var old = ActiveCompanion;
            if (old == newCompanion) return true;

            UpdateActiveTime();
            CoreSettings.Current.ActiveCompanionId = (int)newCompanion;
            CoreSettings.Save();

            var progress = GetProgress(newCompanion);
            if (progress.FirstActivated == DateTime.MinValue) progress.FirstActivated = DateTime.Now;

            ApplyCompanionPrompt(newCompanion);
            try { Switched?.Invoke(newCompanion); } catch (Exception ex) { Log.Debug("CompanionSwitched handler: {E}", ex.Message); }
            Log.Information("Switched companion: {Old} -> {New}", CompanionDefinition.GetById(old).Name, def.Name);
            return true;
        }

        /// <summary>WPF CalculateXPModifier (CompanionService.cs:226), pure.</summary>
        public static double CalculateXPModifier(CompanionBonusType perk, bool triggeredByAutonomy,
            bool strict, bool noEscape, bool attentionChecks, int pinkOpacity)
        {
            switch (perk)
            {
                case CompanionBonusType.PinkFilterBonus:
                    return pinkOpacity > 0 ? 1.0 + pinkOpacity / 100.0 : 1.0;
                case CompanionBonusType.AutonomyBonus:
                    return triggeredByAutonomy ? 1.5 : 1.0;
                case CompanionBonusType.StrictModeBonus:
                    if (!strict) return 0.5;
                    return noEscape && attentionChecks ? 2.0 : 1.0;
                default:
                    return 1.0;
            }
        }

        /// <summary>WPF XPContext.FromCurrentSettings + CalculateXPModifier.</summary>
        public static double CurrentModifier()
        {
            var s = CoreSettings.Current;
            bool autonomy = false;
            try { autonomy = AutonomyActionInProgress?.Invoke() == true; } catch { }
            return CalculateXPModifier(ActivePerk, autonomy, s.StrictLockEnabled, !s.PanicKeyEnabled,
                s.AttentionChecksEnabled, s.PinkFilterEnabled ? s.PinkFilterOpacity : 0);
        }

        /// <summary>WPF AddCompanionXP (CompanionService.cs:276). Fed the BASE amount by the XP bank,
        /// after its login and idle gates, exactly as ProgressionService.AddXP:73.</summary>
        public static void AddCompanionXP(double baseAmount, string source)
        {
            var id = ActiveCompanion;
            var progress = GetProgress(id);
            if (progress.IsMaxLevel) return;

            var modifier = CurrentModifier();
            var finalAmount = baseAmount * modifier;
            progress.CurrentXP += finalAmount;
            progress.TotalXPEarned += finalAmount;
            Log.Debug("Companion XP: {Companion} +{Amount:F1} (base: {Base}, modifier: {Modifier:F2}x, source: {Source})",
                id, finalAmount, baseAmount, modifier, source);

            var levels = new System.Collections.Generic.List<int>();
            while (progress.CurrentXP >= progress.XPForNextLevel && !progress.IsMaxLevel)
            {
                progress.CurrentXP -= progress.XPForNextLevel;
                progress.Level++;
                levels.Add(progress.Level);
                Log.Information("Companion {Companion} leveled up to {Level}!", ActiveDef.Name, progress.Level);
                _ = CoreHaptics.Service?.LevelUpPatternAsync();
            }
            CoreSettings.Save();
            foreach (var l in levels)
                try { LevelUp?.Invoke(id, l); } catch (Exception ex) { Log.Debug("CompanionLevelUp handler: {E}", ex.Message); }
            try { XpAwarded?.Invoke(id, finalAmount, modifier); } catch { }
        }

        /// <summary>WPF OnDrainTick: the Leech perk takes 3 player XP every 2 s, never below 0; the save is
        /// throttled to 30 s (or at zero). True when it drained.</summary>
        public static bool DrainTick(DateTime nowUtc)
        {
            if (ActivePerk != CompanionBonusType.XPDrain) return false;
            var s = CoreSettings.Current;
            if (s.PlayerXP <= 0) return false;
            s.PlayerXP = Math.Max(0, s.PlayerXP - DrainXpPerTick);
            bool reachedZero = s.PlayerXP <= 0;
            if (reachedZero || _lastDrainSaveUtc is not { } last || nowUtc < last || nowUtc - last >= DrainSaveInterval)
            {
                _lastDrainSaveUtc = nowUtc;
                CoreSettings.Save();
            }
            try { XpDrained?.Invoke(DrainXpPerTick); } catch { }
            return true;
        }

        /// <summary>WPF UpdateActiveTime: the active companion's TotalActiveTime.</summary>
        public static void UpdateActiveTime()
        {
            var now = DateTime.Now;
            ActiveProgress.TotalActiveTime += now - _lastActiveTimeUpdate;
            _lastActiveTimeUpdate = now;
        }

        /// <summary>WPF ApplyCompanionPrompt: activate the companion's assigned community prompt, if any.</summary>
        public static void ApplyCompanionPrompt(CompanionId companion)
        {
            try
            {
                var promptId = CoreSettings.Current.GetCompanionPromptId((int)companion);
                if (string.IsNullOrEmpty(promptId)) return;
                var activate = ActivatePrompt;
                if (activate == null) { Log.Debug("Companion {Companion}: assigned prompt {Id} not applied (no community prompts on this head)", companion, promptId); return; }
                activate(promptId!);
            }
            catch (Exception ex) { Log.Warning(ex, "Failed to apply companion prompt"); }
        }
    }
}
