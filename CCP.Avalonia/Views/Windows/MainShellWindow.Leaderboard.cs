// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.Leaderboard.cs (1452 lines) - and
// for most of it, NOT HERE either. The old header said "the bodies come back when the services
// move to Core", which is wrong twice: it names the wrong destination for a third of the file, and
// the wrong blocker for the pure half.
//
// WHERE THIS WORK NOW GOES. WPF's MainWindow owned the board because the leaderboard markup was
// inline in MainWindow.xaml. The port gave it a view, CCP.Avalonia/Views/Tabs/LeaderboardTabView,
// which already owns the parts that need no service: the season countdown and its timer
// start/stop discipline, SetLeaderboardMode + the Level-column relabel for the All-Time board, the
// tier band construction (its Band(index) is BuildTierBand with the same four bound tables), and a
// placeholder board so the page renders finished rather than gapped. Anything restored HERE that
// paints a row would be a second copy no click can reach - the tab's own axaml names the tab's
// handlers, not this window's, and NO member of this partial is referenced from
// MainShellWindow.axaml at all. That is why the class body below is deliberately empty.
//
// WHAT IS ACTUALLY BLOCKED, and by what:
//   The rows. RefreshLeaderboardAsync / RankLeaderboardEntries / RebuildLeaderboardView /
//   UpdateYouBar / UpdateYourRankDisplay / UpdateTrophyCaseColumns all start from
//   Services.LeaderboardEntry over the account API (and the trophy-case columns additionally need
//   SkillService). Neither is in Core. Sorting, searching and filtering are pure list work over
//   those rows and are blocked only by having no rows - they are a half-hour once the fetch exists,
//   and they belong in the VIEW when it happens.
//   BtnLeaderboardDiscord_Click needs the Discord DM path; ReleaseLinks in Core carries links, not
//   the DM.
//   BtnJumpToMe_Click and the whole _lbFx* group are WPF-specific: an attached DependencyProperty
//   (LbScrollOffsetProperty) animated to drive ScrollViewer offset, plus FindVisualDescendant,
//   ScrollViewer clip fiddling and an overscroll bounce. Avalonia has no attached-DP animation
//   twin; the port is Offset transitions on the ListBox's ScrollViewer, and it is view work, not
//   window work. Explicitly a rewrite, not a move.
//
// Members dropped (54 - all of them; see above for which are simply in the wrong file now):
//   private List<Services.LeaderboardEntry> _leaderboardRanked
//   private string _leaderboardSortKey
//   private string _leaderboardFilter
//   private string _leaderboardSearch
//   private int? _youPreviousRank
//   private bool _youPreviousRankKnown
//   private static int EarnableAchievementCount
//   internal async void BtnRefreshLeaderboard_Click(…)
//   internal void LeaderboardSortHeader_Click(…)
//   internal void LeaderboardSearch_TextChanged(…)
//   internal void LeaderboardFilter_Checked(…)
//   internal void BtnJumpToMe_Click(…)
//   private void SetLeaderboardStatus(…)
//   private int _lbFxGeneration
//   private readonly List<FrameworkElement> _lbFxDecorated
//   private ScrollViewer? _lbFxScroller
//   private bool? _lbFxClipWas
//   private static readonly DependencyProperty LbScrollOffsetProperty
//   private static void OnLbScrollOffsetChanged(…)
//   private Color LeaderboardAccentColor(…)
//   private static T? FindVisualDescendant<T>(…)
//   private void JumpToMyRow(…)
//   private static double? TryMeasureCenteredOffset(…)
//   private void QueueCenterAndPulse(…)
//   private void PulseMyRow(…)
//   private void BounceToBoardEnd(…)
//   private void PlayOverscrollBounce(…)
//   private const double BounceOvershootPx
//   private void PulseYouBar(…)
//   private void PulseElement(…)
//   private void ClearFxDecoration(…)
//   private static void StripFxDecoration(…)
//   private void CancelJumpToMeFx(…)
//   private void AnimateScrollOffset(…)
//   private static string OutsideBoardMessage(…)
//   private void UpdateYourRankDisplay(…)
//   internal async void BtnLeaderboardMode_Click(…)
//   private void UpdateLeaderboardModeButtons(…)
//   private void ApplyLeaderboardTheme(…)
//   internal void BtnLeaderboardDiscord_Click(…)
//   internal void LstLeaderboard_MouseDoubleClick(…)
//   private async Task RefreshLeaderboardAsync(…)
//   private void RankLeaderboardEntries(…)
//   private void ApplyLeaderboardSort(…)
//   private void RebuildLeaderboardView(…)
//   private static int TierIndexForRank(…)
//   private static readonly string[] TierNameKeys
//   private static readonly string[] TierSubKeys
//   private static readonly int[] TierLowerBounds
//   private static readonly int[] TierUpperBounds
//   private static LeaderboardTierBand BuildTierBand(…)
//   private void UpdateYouBar(…)
//   private static string FormatCompact(…)
//   private void UpdateTrophyCaseColumns(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Deliberately empty - see the header. No member of this partial is referenced from
        // MainShellWindow.axaml, and the parts that are not service-bound already live on
        // CCP.Avalonia/Views/Tabs/LeaderboardTabView.
    }
}
