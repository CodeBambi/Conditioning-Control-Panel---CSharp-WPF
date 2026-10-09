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
//   SkillCard_Click (+ CelebrateEnhancementPurchase, UpdateTrophyCaseColumns) - purchasing is
//     ProfileSync.PurchaseSkillAsync, a server call with auth recovery and a sync retry, not ported;
//     purchasable nodes draw their WPF state but do not take a click.
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
