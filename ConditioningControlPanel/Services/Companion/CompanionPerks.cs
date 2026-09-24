using ConditioningControlPanel.Models;

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
