using ConditioningControlPanel.Models;
using System.Linq;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>How the Companion card shows a perk: a loc key with the real numbers, a badge glyph,
    /// and whether it costs the user (shown plainly, never hidden).</summary>
    public sealed record CompanionPerk(string LocKey, string Glyph, bool Negative);

    /// <summary>
    /// The one-line, read-only description of each companion's XP perk. The numbers in the loc
    /// strings mirror <see cref="CompanionService.CalculateXPModifier"/> and the drain timer; keep
    /// them in step when a perk is retuned. PURE.
    /// </summary>
    public static class CompanionPerks
    {
        public static CompanionBonusType Resolve(CompanionBonusType? saved, CompanionBonusType bundle, bool preview)
            => preview && saved.HasValue && System.Enum.IsDefined(saved.Value) ? saved.Value : bundle;

        public static int RequiredLevel(CompanionBonusType type)
            => CompanionDefinition.AllCompanions.FirstOrDefault(c => c.BonusType == type)?.RequiredLevel ?? int.MaxValue;

        public static bool CanSelect(CompanionBonusType type, int level) => level >= RequiredLevel(type);

        public static string NameKey(CompanionBonusType type) => "companion_perk_name_" + (int)type;

        /// <summary>WPF CompanionService.OnAttentionCheckFailed: with the Trainer's perk (StrictModeBonus)
        /// active, a failed video attention check costs the active companion 25 XP, never below 0.
        /// True when it applied; the caller saves.</summary>
        public static bool ApplyAttentionFailPenalty(AppSettings s)
        {
            var perk = Resolve(s.CompanionPerk, CompanionDefinition.GetById((CompanionId)s.ActiveCompanionId).BonusType, CompanionExperience.IsV2Enabled);
            if (perk != CompanionBonusType.StrictModeBonus) return false;
            var p = s.ActiveCompanionProgress;
            p.CurrentXP = System.Math.Max(0, p.CurrentXP - 25.0);
            return true;
        }

        public static CompanionPerk For(CompanionBonusType type) => type switch
        {
            CompanionBonusType.PinkFilterBonus => new("perk_pink_filter", "❀", false),
            CompanionBonusType.AutonomyBonus => new("perk_autonomy", "⚙", false),
            CompanionBonusType.XPDrain => new("perk_xp_drain", "▼", true),
            CompanionBonusType.StrictModeBonus => new("perk_strict", "◆", false),
            CompanionBonusType.SessionCompletionBonus => new("perk_session", "★", false),
            _ => new("perk_none", "✧", false)
        };
    }
}
