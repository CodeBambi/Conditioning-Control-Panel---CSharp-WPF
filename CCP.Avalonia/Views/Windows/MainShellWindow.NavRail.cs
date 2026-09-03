// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.NavRail.cs (1107 lines).
//
// ponytail: wholesale stub. Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// The handlers named by MainShellWindow.axaml are real (empty) methods, because a
// missing one is a XAML compile error, not a runtime gap.
//
// Members dropped (61):
//   private const double NavRailCollapsedWidth
//   private const double NavRailExpandedWidth
//   private const int NavRailAnimMs
//   private const int NavRailCollapseAnimMs
//   private const double NavDoorTileCollapsed
//   private const double NavDoorTileExpanded
//   private const double NavDoorIconCollapsed
//   private const double NavDoorIconExpanded
//   private const double NavDoorGlowCollapsed
//   private const double NavDoorGlowExpanded
//   private const double NavDoorGlowActive
//   private const double NavDoorGlowOpen
//   private const int NavDoorGlowFadeMs
//   private const double NavDoorTileIdleOpacity
//   private const double NavDoorLabelRise
//   private const int NavDoorLabelFadeMs
//   private const int NavDoorLabelSlideMs
//   private const int NavDoorLabelStaggerMs
//   private const double NavDoorLabelGlowLo
//   private const double NavDoorLabelGlowHi
//   private const double NavDoorLabelGlowStatic
//   private const int NavDoorLabelGlowBreathMs
//   private const int NavDoorLabelShimmerSweepMs
//   private const int NavDoorLabelShimmerPeriodMs
//   private const int NavDoorLabelFxStaggerMs
//   private const string NavDoorLabelHostTag
//   private const string NavRailStaticTextTag
//   private bool _navRailExpanded
//   private bool _navRailReady
//   private int _navRailHoldCount
//   private readonly List<TextBlock> _navRailLabels
//   private readonly List<ButtonBase> _navRailButtons
//   private readonly List<NavDoorRow> _navDoorRows
//   private readonly HashSet<TextBlock> _navDoorLabelTexts
//   private sealed class NavDoorRow
//   private void InitializeNavRail(…)
//   internal static Func<string, string?>? PossessionReroute
//   private void HookNavDoorRerouteSeam(…)
//   private void NavDoor_PossessionReroute(…)
//   private void BtnNavSearch_Click(…)
//   private const int NavDoorArtDecodeWidth
//   private void ApplyDoorArt(…)
//   private void CacheNavRailParts(…)
//   private void CacheNavDoorRows(…)
//   private void BuildNavDoorLabelFx(…)
//   private static void StartNavDoorLabelFx(…)
//   private static void StopNavDoorLabelFx(…)
//   private static Brush? BuildNavDoorGlow(…)
//   private void RefreshNavDoorActive(…)
//   private void SetNavDoorGlow(…)
//   private void ApplyNavDoorRows(…)
//   private static void SetNavRailSize(…)
//   private void SetNavRailExpanded(…)
//   private readonly List<(…)
//   private bool _navRailAirspaceLogged
//   private void ApplyNavRailAirspace(…)
//   private void HoldOverlappingBrowsers(…)
//   private void ApplyNavRailDoorState(…)
//   internal void SyncNavRailToPointer(…)
//   internal void HoldNavRailOpen(…)
//   internal void ReleaseNavRailOpen(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: needs the services in MainWindow.NavRail.cs; wired when they move to Core.
        private void BtnNavSearch_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

    }
}
