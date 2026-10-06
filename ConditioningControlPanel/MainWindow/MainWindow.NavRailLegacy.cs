using System.Windows.Controls;
using System.Windows.Media;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Nav rework (2026-10-06): the old rail's door rows are GONE from MainWindow.xaml (their
    /// pages now carry them as a pill strip). These names were x:Named rows; other files still
    /// read them (ChromeFx's NavButtons and NavButtonForTab, the Deeper/Programs first-visit
    /// pulses, the Spiral Room row, Just Drop's availability, the Skill Tree tooltip), every one
    /// of them behind a null check. Each now answers null, so those call sites compile and do
    /// nothing. Delete a line here when its last caller is gone; never put a row back.
    /// </summary>
    public partial class MainWindow
    {
        internal Button BtnSettings => null!;
        internal Button BtnNavStudio => null!;
        internal Button BtnPresets => null!;
        internal Button BtnNavHaptics => null!;
        internal Button BtnNavJustDrop => null!;
        internal Button BtnCompanion => null!;
        internal Button BtnNavBambiTakeover => null!;
        internal Button BtnNavSheListening => null!;
        internal Button BtnNavAwareness => null!;
        internal Button BtnLab => null!;
        internal Button BtnDeeper => null!;
        internal ScaleTransform BtnDeeperScale => null!;
        internal Button BtnPatreonExclusives => null!;
        internal Button BtnNavGradedIntake => null!;
        internal Button BtnNavLockdown => null!;
        internal Button BtnNavBlinkTrainer => null!;
        internal Button BtnNavRemoteControl => null!;
        internal Button BtnAvailableSubjects => null!;
        internal Button BtnDiscordTab => null!;
        internal Button BtnNavSpiral => null!;
        internal TextBlock TxtNavSpiral => null!;
        internal Button BtnQuests => null!;
        internal Button BtnAchievements => null!;
        internal Button BtnEnhancements => null!;
        internal Button BtnPrograms => null!;
        internal ScaleTransform BtnProgramsScale => null!;
        internal Button BtnLeaderboard => null!;
        internal Button BtnOpenAssetsTop => null!;
        internal Button BtnNavMods => null!;
        internal Button BtnNavCatalogue => null!;
        internal Button BtnNavPhrases => null!;
        internal Button BtnNavMediaLog => null!;
        /// <summary>The Web App door left the rail; Play > Games carries a Web App tile.</summary>
        internal Button DoorWebApp => null!;
    }
}
