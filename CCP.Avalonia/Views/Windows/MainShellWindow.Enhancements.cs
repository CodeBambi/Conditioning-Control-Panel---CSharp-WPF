// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.Enhancements.cs (2318 lines).
//
// The drawing half lives on the tab itself (Views/Tabs/EnhancementsTabView.axaml.cs), because
// nothing in it needs the window: RefreshEnhancementsUI, DrawSkillTree, CreateSkillTreeHeader
// (title, capstone chip, active bonuses, points, prestige, Ditzy Data stats toggle, XP multiplier,
// conditioning time), CreateAnimatedSkillTreeBrush, DrawConnectionLines, CreateSkillNode,
// CreateSkillPlaceholderGradient, PopulateSecretSkills, CreateHiddenSecretCard,
// CreateSecretSkillCard and RefreshActiveBonuses. The multiplier/breakdown/time math is Core
// SkillTreeRules (WPF SkillTreeService delegates to it). OnPinkRushStarted/Ended: PinkRushHost.
//
// ponytail: still missing, each a whole feature:
//   (SkillCard_Click is live: Views/Tabs/EnhancementsTabView.Purchase.cs over Core SkillPurchase, page wave k2.
//     Left out of it: the prestige sheen sweep, 401 auth recovery, the skill_unlock bark.)
//   AddProSection + the Ditzy Data PRO panels (BuildProLifetimePanel, BuildSeasonRewindPanel,
//     BuildBestieRecordsPanel, BuildBrainDrainPanel, the season/sparkline/heatmap charts).
//   OnLuckyProc / EnsureLuckyToast / PlaceLuckyToast / CloseLuckyToast - nothing raises a lucky
//     proc on this head (RollLuckyFlash/RollLuckyBubble are SkillTreeService, head-side in WPF).
//   The prestige rank-up burst (_prestigeRowBorder) rides on the purchase above.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
