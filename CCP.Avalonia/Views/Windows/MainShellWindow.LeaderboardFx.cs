// PORTED-AS-A-NOTE from ConditioningControlPanel/MainWindow/MainWindow.LeaderboardFx.cs (405 lines).
//
// Nothing is wired here. The Leaderboard tab carries no AmbientFxCanvas - both of its effects are
// data-driven decoration, and the data is the blocker in each case:
//
//   * the podium glow breath (PodiumGlowMinOpacity..MaxOpacity over PodiumGlowSeconds on the #1
//     card). The host exists - LeaderboardTabView.axaml:531, ItemsControl x:Name="PodiumHost" -
//     but ApplyPodiumFx picks the glow card out of the generated containers, and the containers
//     on this head are placeholder rows. A breath on a sample card is decoration attached to
//     nothing, and it would need re-attaching on every real refresh anyway.
//   * the rank flash (CollectMovedRows -> FlashMovedRows, a staggered rise+fade on up to
//     RankFlashMaxRows rows that CHANGED position since the last pull). This one is not merely
//     unattached, it is undefined: "moved" is a diff between two service pulls, and
//     Services.LeaderboardEntry plus the leaderboard client are still in the WPF head. With no
//     previous pull to diff against, every row is either always flashing or never - and the
//     first is the failure mode a reviewer would never catch from a screenshot.
//
// Neither is storyboard-only: Avalonia can express both (an Animation over OpacityProperty for
// the breath, per-row Animations with a start delay for the stagger). They are blocked on the
// leaderboard service, and they should land in the same layer that brings it.
//
// LeaderboardAmbientAllowed and the Activated/Deactivated/StateChanged parking funnel go with
// them: both loops are plain animations rather than canvases, so unlike the two tabs this batch
// turned on they cannot lean on AmbientFxCanvas.Evaluate() for their gate.
//
// Members dropped (30):
//   private const double PodiumGlowMinOpacity
//   private const double PodiumGlowMaxOpacity
//   private const double PodiumGlowSeconds
//   private const double RankFlashPeakOpacity
//   private const int RankFlashMs
//   private const int RankFlashRiseMs
//   private const int RankFlashStaggerMs
//   private const int RankFlashMaxRows
//   private bool _leaderboardFxInitialized
//   private FrameworkElement? _lbPodiumGlowCard
//   private double _lbPodiumGlowRest
//   private readonly List<FrameworkElement> _lbPodiumStaggered
//   private readonly List<FrameworkElement> _lbFlashDecorated
//   private bool _lbRankFlashPending
//   private bool LeaderboardAmbientAllowed
//   private void EnsureLeaderboardFx(…)
//   private void OnLeaderboardFxWindowStateish(…)
//   private void OnLeaderboardTabVisibilityChanged(…)
//   private void ApplyLeaderboardFxLoops(…)
//   private void RunLeaderboardFxPass(…)
//   private void ApplyPodiumFx(…)
//   private void ApplyPodiumGlowBreath(…)
//   private void ReleasePodiumGlow(…)
//   private void ClearPodiumFx(…)
//   private static List<Services.LeaderboardEntry> CollectMovedRows(…)
//   private void FlashMovedRows(…)
//   private void FlashRow(…)
//   private void ClearRankFlashes(…)
//   private static void StripRowFlash(…)
//   private static Color ThemeColor(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
