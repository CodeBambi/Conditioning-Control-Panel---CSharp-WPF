// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.AchievementsTab.cs (875 lines).
//
// ponytail: wholesale stub. Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// The handlers named by MainShellWindow.axaml are real (empty) methods, because a
// missing one is a XAML compile error, not a runtime gap.
//
// Members dropped (48):
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
//   private void BtnAchievements_Click(…)
//   private void BtnCompanion_Click(…)
//   private void BtnLeaderboard_Click(…)
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
        // ponytail: needs the services in MainWindow.AchievementsTab.cs; wired when they move to Core.
        private void BtnAchievements_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.AchievementsTab.cs; wired when they move to Core.
        private void BtnCompanion_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.AchievementsTab.cs; wired when they move to Core.
        private void BtnLeaderboard_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

    }
}
