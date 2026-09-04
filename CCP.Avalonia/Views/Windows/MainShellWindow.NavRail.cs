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
// Members dropped (61, two of them since answered - see the annotations):
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
//   private bool _navRailReady          (deliberately NOT ported - see InitializeNavRail below)
//   private int _navRailHoldCount
//   private readonly List<TextBlock> _navRailLabels
//   private readonly List<ButtonBase> _navRailButtons
//   private readonly List<NavDoorRow> _navDoorRows
//   private readonly HashSet<TextBlock> _navDoorLabelTexts
//   private sealed class NavDoorRow
//   private void InitializeNavRail(…)   (PARTLY PORTED below: its RefreshNavPremiumTags call)
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
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
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

            // Its own try, deliberately. Sharing one with the pills meant a throw in them silently
            // took the rail's hover with it - the two have nothing to do with each other, and a
            // catch that swallows a whole feature because an unrelated line above it failed is how
            // this port has already lost work once.
            try { HookNavRailHover(); }
            catch (Exception ex) { Log.Debug("HookNavRailHover: {E}", ex.Message); }
        }

        private const double NavRailCollapsedWidth = 56;   // WPF MainWindow.NavRail.cs:61
        private const double NavRailExpandedWidth = 236;   // WPF MainWindow.NavRail.cs:81
        private const int NavRailAnimMs = 190;             // WPF MainWindow.NavRail.cs:85
        private const int NavRailCollapseAnimMs = 150;     // WPF MainWindow.NavRail.cs:106

        private bool _navRailExpanded;
        private bool _navRailHooked;

        /// <summary>
        /// The rail widens under the pointer and shuts when it leaves, which is how every label in
        /// it becomes readable: the labels are already in the tree and simply clipped by a 56px
        /// rail, so the width IS the feature. WPF drives this from NavSidebar's MouseEnter/
        /// MouseLeave (MainWindow.NavRail.cs:284-310); the same two events here.
        ///
        /// Only the width is ported. WPF also fades the labels and the premium pills on the same
        /// clock so text never paints outside the clip mid-tween - that needs the label cache and
        /// MotionFx, and this file's header lists both as still dropped. The effect of leaving the
        /// fade out is that labels appear at full opacity as soon as there is room for them rather
        /// than easing in; nothing is drawn outside the rail, because the rail clips.
        /// </summary>
        private void HookNavRailHover()
        {
            if (_navRailHooked) return;
            var rail = this.FindControl<Border>("NavSidebar");
            if (rail is null) return;
            _navRailHooked = true;

            rail.Width = NavRailCollapsedWidth;
            rail.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Layoutable.WidthProperty,
                    Duration = TimeSpan.FromMilliseconds(NavRailAnimMs),
                    Easing = new QuadraticEaseOut(),
                },
            };

            // Driven from the WINDOW's pointer moves, not the rail's PointerEntered. NavSidebar is
            // a Border whose Background sits on an inner element, and a Border with no Background of
            // its own is not hit-testable in Avalonia - so PointerEntered never arrives on it and a
            // rail hooked that way silently never opens. WPF hits the same shape and also carries a
            // window-level MouseMove for the collapse (MainWindow.NavRail.cs:292 and 315-321); this
            // is that, used for both edges. Geometry, not hit-testing, so it cannot be defeated by a
            // transparent parent or a child that swallows the event.
            PointerMoved += (_, e) =>
            {
                var p = e.GetPosition(this);
                var r = rail.Bounds;
                SetNavRailExpanded(rail, p.X >= r.X && p.X <= r.X + rail.Width && p.Y >= r.Y && p.Y <= r.Bottom);
            };
            PointerExited += (_, _) => SetNavRailExpanded(rail, false);
        }

        /// <summary>WPF's SetNavRailExpanded, width only. Early-outs on the state it is already
        /// in, as WPF does, so a pointer moving inside the rail is one field read per move.</summary>
        private void SetNavRailExpanded(Border rail, bool expand)
        {
            if (_navRailExpanded == expand) return;
            _navRailExpanded = expand;

            // Closing is quicker than opening on WPF, and that asymmetry is deliberate: the rail
            // overlays the page, so it must get out of the way faster than it arrives.
            if (rail.Transitions is { Count: > 0 } t && t[0] is DoubleTransition d)
                d.Duration = TimeSpan.FromMilliseconds(expand ? NavRailAnimMs : NavRailCollapseAnimMs);

            rail.Width = expand ? NavRailExpandedWidth : NavRailCollapsedWidth;
        }

        /// <summary>Whether the rail is currently open. NavCheck and the click-through driver read
        /// this rather than Width, which mid-tween reports the in-flight value, not the intent.</summary>
        internal bool NavRailExpanded => _navRailExpanded;

        internal bool NavRailHooked => _navRailHooked;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            InitializeNavRail();
        }

        /// <summary>
        /// The rail's search pill. WPF is one line — <c>SettingsPaletteWindow.Toggle(this)</c>
        /// (MainWindow.NavRail.cs:406) — and it stays a stub on purpose, not for want of a service
        /// in this file. <c>SettingsPaletteWindow.Toggle</c> records the refusal itself
        /// (Views/Windows/SettingsPaletteWindow.axaml.cs): its Lockdown check has no Core seam, and
        /// a navigation palette floating over an active lockdown reads as an escape hatch. Its
        /// <c>Refresh</c> also cannot query <c>Services/SettingsPaletteIndex.cs</c>, so today it
        /// draws SAMPLE rows that navigate nowhere. Wiring the pill would open a search box that
        /// finds fake settings and honours no lockdown. Both halves come back together.
        /// </summary>
        private void BtnNavSearch_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

    }
}
