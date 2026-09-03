// PARTLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.AchievementsTab.cs (875 lines).
//
// THE THREE NAV HANDLERS WERE NEVER BLOCKED. BtnAchievements_Click, BtnCompanion_Click and
// BtnLeaderboard_Click are one ShowTab call each in WPF (MainWindow.AchievementsTab.cs:94-107) -
// they live in this file only because the buttons sit next to the achievement grid in
// MainWindow.xaml, not because they touch a service. All three are restored below, so the You
// door's three top-level entries lead somewhere.
//
// The rest of the file IS blocked, but the reason is not "services move to Core" - it is that
// the grid it builds does not exist on this head in this shape. WPF builds ~48 achievement cards
// in code (BuildAchievementCard, BuildRewardBand, the filter chips, the tooltip pass) straight
// into MainWindow.xaml's panel. The port gave the tab its own view,
// CCP.Avalonia/Views/Tabs/AchievementsTabView, which draws the same page from XAML with an
// AchievementsTabViewModel supplying the formatted counters. A card builder restored here would
// have nothing to build into.
//
// So the honest blocker list for the achievements page is short and specific, and all of it is
// AchievementsTabView's rather than this file's:
//   - the counts are placeholders in AchievementsTabViewModel (Unlocked/Total, RewardsEarned/
//     RewardsTotal, PatronUnlocked/PatronTotal). Real values need AchievementService, which is
//     head-side; CoreProgression deliberately does NOT carry it - the seam is AddXP and
//     TrackBubbleCountResult, the two calls ported views actually make, not the whole service.
//     "Is achievement X unlocked" is not on it, so IsAchievementUnlocked / RefreshAchievementTile
//     / OnAchievementUnlockedInMainWindow have no source of truth here yet.
//   - the reward map needs Services.WardrobeItem (head-side), and LoadAchievementImage needs the
//     achievement art under the same asset root.
//   - BtnViewSeasonRecap_Click needs Services.SeasonRecapService.HasAnySnapshot(), which is not in
//     Core (only Models/SeasonRecap.cs is) - see BtnLeaderboard_Click below.
//
// Members dropped (48 - the three now restored are marked RESTORED):
//   private const double AchvBadgePx
//   private const double AchvRewardIconPx
//   private static readonly SolidColorBrush AchvMutedBrush
//   private static readonly SolidColorBrush AchvDimBrush
//   private static readonly SolidColorBrush AchvTickBrush
//   private static readonly SolidColorBrush AchvRuleBrush
//   private static readonly SolidColorBrush AchvPatreonFillBrush
//   private static readonly SolidColorBrush AchvPatreonEdgeBrush
//   private static readonly SolidColorBrush AchvPatreonInkBrush
//   private sealed class AchievementCardParts
//   private readonly Dictionary<string, ToggleButton> _achievementCards
//   private readonly Dictionary<ToggleButton, AchievementCardParts> _achievementCardParts
//   private readonly Dictionary<string, Services.WardrobeItem> _achievementRewards
//   private readonly List<ToggleButton> _achievementFilterChips
//   private string _achievementFilter
//   private const string AchvFilterAll
//   private const string AchvFilterUnlocked
//   private const string AchvFilterLocked
//   private const string AchvFilterRewards
//   private void BtnAchievements_Click(…)              RESTORED
//   private void BtnCompanion_Click(…)                 RESTORED
//   private void BtnLeaderboard_Click(…)               RESTORED
//   internal void BtnViewSeasonRecap_Click(…)
//   private void UpdateAchievementCount(…)
//   private void UpdateRewardCount(…)
//   private void PopulateAchievementGrid(…)
//   private void BuildAchievementRewardMap(…)
//   private ToggleButton BuildAchievementCard(…)
//   private void ApplyAchievementInfoText(…)
//   private void BuildRewardBand(…)
//   private FrameworkElement? BuildRewardIcon(…)
//   private static FrameworkElement CategoryGlyphBox(…)
//   private static string CategoryGlyph(…)
//   private void BuildAchievementFilters(…)
//   private void AchievementFilterChip_Changed(…)
//   private void ApplyAchievementFilter(…)
//   private BitmapImage? LoadAchievementImage(…)
//   private void RefreshAchievementTile(…)
//   private void RefreshAllAchievementTiles(…)
//   private void OnAchievementUnlockedInMainWindow(…)
//   private void ApplyAchievementCardTooltip(…)
//   private static bool IsAchievementUnlocked(…)
//   private static string AchName(…)
//   private static string AchReq(…)
//   private static string AchFlavor(…)
//   private static string ModAware(…)
//   private static string LocFmtOr(…)
//   private static SolidColorBrush FrozenBrush(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private void BtnAchievements_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => ShowTab("achievements");

        private void BtnCompanion_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => ShowTab("companion");

        /// <summary>
        /// Opens the Leaderboard.
        ///
        /// <para>ponytail: WPF also reveals LeaderboardTab.BtnViewSeasonRecap here, when
        /// <c>Services.SeasonRecapService.HasAnySnapshot()</c> says a persisted snapshot exists.
        /// That service is not in Core (only <c>CCP.Core/Models/SeasonRecap.cs</c> is), and the
        /// button is authored hidden in LeaderboardTabView.axaml, so the correct state here is
        /// "left hidden" - showing a re-view button with no snapshot to re-view would be the
        /// worse half of the port. One line once the snapshot reader crosses.</para>
        /// </summary>
        private void BtnLeaderboard_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => ShowTab("leaderboard");

    }
}
