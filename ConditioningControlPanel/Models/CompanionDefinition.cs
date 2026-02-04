using System;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Unique identifier for each companion type.
    /// Companions unlock at specific player levels and use avatar titles as names.
    /// </summary>
    public enum CompanionId
    {
        OGBambiSprite = 0,   // Level 50 - Synthetic Blowdoll - Pink filter bonus (Avatar Set 3)
        CultBunny = 1,       // Level 100 - Perfect Fuckpuppet - Autonomy bonus (Avatar Set 4)
        BrainParasite = 2,   // Level 125 - Brainwashed Slavedoll - XP drain (Avatar Set 5)
        BambiTrainer = 3,    // Level 150 - Platinum Puppet - Strict mode bonuses (Avatar Set 6)
        BimboCow = 4         // Level 75 - Bambi Cow - Session completion bonus (Avatar Set 7)
    }

    /// <summary>
    /// The type of XP bonus/modifier this companion provides.
    /// </summary>
    public enum CompanionBonusType
    {
        /// <summary>OG: XP multiplied by (1 + pinkFilterOpacity/100)</summary>
        PinkFilterBonus,

        /// <summary>Bunny: +50% XP when autonomy triggers actions</summary>
        AutonomyBonus,

        /// <summary>Parasite: -5 XP/min constant drain</summary>
        XPDrain,

        /// <summary>Trainer: -50% without strict, +100% with No Escape, -25 XP on attention fail</summary>
        StrictModeBonus,

        /// <summary>Cow: +25% XP from session completion rewards</summary>
        SessionCompletionBonus
    }

    /// <summary>
    /// Defines a companion's identity, requirements, and behavior.
    /// </summary>
    public class CompanionDefinition
    {
        /// <summary>Unique identifier for this companion.</summary>
        public CompanionId Id { get; set; }

        /// <summary>Display name shown in UI (normal mode).</summary>
        public string Name { get; set; } = "";

        /// <summary>Display name shown in slut mode. If empty, uses Name.</summary>
        public string SlutModeName { get; set; } = "";

        /// <summary>Short description of the companion's personality and mechanics.</summary>
        public string Description { get; set; } = "";

        /// <summary>Longer description explaining the XP mechanic.</summary>
        public string XPMechanicDescription { get; set; } = "";

        /// <summary>Minimum player level required to unlock this companion.</summary>
        public int RequiredLevel { get; set; }

        /// <summary>The type of XP bonus this companion provides.</summary>
        public CompanionBonusType BonusType { get; set; }

        /// <summary>
        /// Resource path for the companion's icon/avatar.
        /// Used for selection UI and avatar display.
        /// </summary>
        public string IconResourcePath { get; set; } = "";

        /// <summary>
        /// Avatar set number to use (4-6) for this companion's visual.
        /// Maps to avatar_pose files for levels 50, 100, 125, 150.
        /// </summary>
        public int AvatarSet { get; set; } = 4;

        /// <summary>
        /// Static definitions for all companions available in v5.3.
        /// Companions unlock at specific player levels (50, 100, 125, 150).
        /// Each companion corresponds to a unique avatar set.
        /// </summary>
        public static CompanionDefinition[] AllCompanions => new[]
        {
            new CompanionDefinition
            {
                Id = CompanionId.OGBambiSprite,
                Name = "Synthetic Blowdoll",
                Description = "Your bubbly, giggly bestie who loves all things pink.",
                XPMechanicDescription = "Bonus XP from pink filter. The pinker your screen, the faster she levels!",
                RequiredLevel = 50,
                BonusType = CompanionBonusType.PinkFilterBonus,
                IconResourcePath = "pack://application:,,,/Resources/avatar3_pose1.png",
                AvatarSet = 3
            },
            new CompanionDefinition
            {
                Id = CompanionId.CultBunny,
                Name = "Perfect Fuckpuppet",
                Description = "A devoted follower who thrives when you surrender control.",
                XPMechanicDescription = "+50% XP when Autonomy Mode triggers actions. Let go and let her grow!",
                RequiredLevel = 100,
                BonusType = CompanionBonusType.AutonomyBonus,
                IconResourcePath = "pack://application:,,,/Resources/avatar4_pose1.png",
                AvatarSet = 4
            },
            new CompanionDefinition
            {
                Id = CompanionId.BrainParasite,
                Name = "Brainwashed Slavedoll",
                Description = "A sinister presence that feeds on your mind. Keep training or fall behind!",
                XPMechanicDescription = "Drains 5 XP/min. You must actively train to outpace the drain!",
                RequiredLevel = 125,
                BonusType = CompanionBonusType.XPDrain,
                IconResourcePath = "pack://application:,,,/Resources/avatar5_pose1.png",
                AvatarSet = 5
            },
            new CompanionDefinition
            {
                Id = CompanionId.BambiTrainer,
                Name = "Platinum Puppet",
                Description = "A strict taskmaster who demands your full attention and commitment.",
                XPMechanicDescription = "-50% XP without Strict Mode, +100% with No Escape. -25 XP on attention fail.",
                RequiredLevel = 150,
                BonusType = CompanionBonusType.StrictModeBonus,
                IconResourcePath = "pack://application:,,,/Resources/avatar6_pose1.png",
                AvatarSet = 6
            },
            new CompanionDefinition
            {
                Id = CompanionId.BimboCow,
                Name = "Bimbo Cow",
                SlutModeName = "Bambi Cow",
                Description = "A ditzy, docile cow who rewards you for completing your training sessions.",
                XPMechanicDescription = "+25% bonus XP from session completion rewards. Finish what you start!",
                RequiredLevel = 75,
                BonusType = CompanionBonusType.SessionCompletionBonus,
                IconResourcePath = "pack://application:,,,/Resources/avatar7_pose1.png",
                AvatarSet = 7
            }
        };

        /// <summary>
        /// Gets the display name based on current mode (slut mode or normal).
        /// </summary>
        public string GetDisplayName(bool isSlutMode)
        {
            if (isSlutMode && !string.IsNullOrEmpty(SlutModeName))
                return SlutModeName;
            return Name;
        }

        /// <summary>
        /// Gets a companion definition by ID.
        /// </summary>
        public static CompanionDefinition GetById(CompanionId id)
        {
            var index = (int)id;
            if (index >= 0 && index < AllCompanions.Length)
                return AllCompanions[index];
            return AllCompanions[0]; // Default to OG
        }

        /// <summary>
        /// Gets a companion definition by integer ID.
        /// </summary>
        public static CompanionDefinition GetById(int id)
        {
            if (id >= 0 && id < AllCompanions.Length)
                return AllCompanions[id];
            return AllCompanions[0]; // Default to OG
        }
    }
}
