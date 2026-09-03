// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.NavRail.cs (1107 lines),
// with ONE exception: the rail's one-time setup pass now exists, and it does the one thing in
// WPF's InitializeNavRail that resolves on this head - painting the premium pills.
//
// ponytail: the rest is still a wholesale stub. Every other member below reaches App.*, a service,
// a device, a WebView2 or Win32 - none of which this head may touch. The file exists and each
// member is NAMED so nothing disappears silently; the bodies come back when the services move to
// Core.
//
// The rail on this head does not expand: it is authored 56px wide in MainShellWindow.axaml and
// nothing widens it, so SetNavRailExpanded and with it the label/pill collapse fade have no port
// and no caller. MainShellWindow.NavPremiumTags.NavPremiumTagElements - which exists only to be
// faded by that method - therefore stays uncalled, deliberately, rather than being given an
// invented caller. It returns the pills that resolved; the fade returns with the rail's width
// animation, the label cache (_navRailLabels) and MotionFx.AllowTransitions.
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

using System;
using Avalonia;
using Avalonia.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// The rail's one-time setup pass, cut down to what this head can answer. WPF calls this
        /// from MainWindow_Loaded (MainWindow.NavRail.cs:263) after templates are applied; this
        /// window's OnLoaded override is owned by MainShellWindow.Marquee.cs and OnOpened by
        /// .WorkAreaFit.cs, so the hook here is OnAttachedToVisualTree - which is EARLIER than
        /// Loaded and is enough, because everything below is a namescope lookup by x:Name rather
        /// than a visual-tree walk (WPF needed Loaded for CacheNavRailParts, which is a walk and
        /// is not ported).
        ///
        /// <para>Internal and repeatable so NavCheck can call it directly. WPF's
        /// <c>_navRailReady</c> latch is NOT ported: it guards the caches and the pointer
        /// subscriptions, none of which are here, and the one thing that IS here is eight
        /// namescope lookups that are correct however many times they run. The latch returns with
        /// what it protects - and without it, an assertion can force the pills on and watch this
        /// put them back, which a latched one-shot would silently decline to do.</para>
        ///
        /// <para>ponytail: CacheNavDoorRows, HookNavDoorRerouteSeam, ApplyDoorArt, the two pointer
        /// subscriptions that call SetNavRailExpanded and ApplyNavRailAirspace all still need
        /// MainWindow.NavRail.cs's services and the rail's width animation.</para>
        /// </summary>
        internal void InitializeNavRail()
        {
            try
            {
                // LAYER A: the gold stars on the sold rows. Authored IsVisible=False, so a rail
                // that never got here shows none rather than all - see the NavEntryPremiumTag
                // theme. The four subscriptions that take them away again are still head-side
                // (MainShellWindow.NavPremiumTags.cs's header names all four services).
                RefreshNavPremiumTags();
            }
            catch (Exception ex) { Log.Debug("InitializeNavRail: {E}", ex.Message); }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            InitializeNavRail();
        }

        // ponytail: needs the services in MainWindow.NavRail.cs; wired when they move to Core.
        private void BtnNavSearch_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

    }
}
