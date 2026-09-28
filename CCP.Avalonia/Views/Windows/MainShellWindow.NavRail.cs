// PORTED from ConditioningControlPanel/MainWindow/MainWindow.NavRail.cs (1107 lines): the rail's
// setup pass (premium pills), the hover flyout (56 -> 236 px, 190/150 ms QuadraticEaseOut), the
// global label fade, the medallion tile/icon growth, the staggered door-name fade + rise, the
// scrollbar flip and the popup hold latch. See HookNavRailHover.
//
// ponytail: still dropped, each needing a service/effect this head lacks - door-name glow breath
// and shimmer (BuildNavDoorLabelFx/Start/Stop), the hue glow ellipse and active tile tint
// (BuildNavDoorGlow/RefreshNavDoorActive/SetNavDoorGlow), mod-aware ApplyDoorArt, the possession
// reroute seam, the WebView airspace holds, ApplyNavRailDoorState, the stuck-rail watchdog and
// the MotionFx reduced-motion snap. The handlers named by MainShellWindow.axaml are real methods,
// because a missing one is a XAML compile error, not a runtime gap.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// The rail's one-time setup pass, cut down to what this head can answer. WPF calls this
        /// from MainWindow_Loaded (MainWindow.NavRail.cs:263) after templates are applied; this
        /// head calls it from the constructor, right after XAML load, which is enough: the pills
        /// are namescope lookups and CacheNavRailParts walks the
        /// LOGICAL tree, which XAML load has already built (WPF walked the visual tree, so it
        /// needed Loaded).
        ///
        /// <para>Internal and repeatable so NavCheck can call it directly. WPF's
        /// <c>_navRailReady</c> latch is split: the pills repaint on every call (an assertion can
        /// force them on and watch this put them back), the hover hook latches on
        /// <c>_navRailHooked</c>.</para>
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

        // WPF MainWindow.NavRail.cs:61-145. Sizes the XAML authors as the shut state.
        private const double NavRailCollapsedWidth = 56;
        private const double NavRailExpandedWidth = 236;
        private const int NavRailAnimMs = 190;
        private const int NavRailCollapseAnimMs = 150;
        private const double NavDoorTileCollapsed = 44, NavDoorTileExpanded = 50;
        private const double NavDoorIconCollapsed = 40, NavDoorIconExpanded = 46;
        private const double NavDoorLabelRise = 14;
        private const int NavDoorLabelFadeMs = 220, NavDoorLabelSlideMs = 260, NavDoorLabelStaggerMs = 30;
        private const string NavRailStaticTextTag = "navrailstatic";

        private bool _navRailExpanded;
        private bool _navRailHooked;
        private Border? _navRail;
        private readonly List<Control> _navRailLabels = new();
        private readonly List<(Border Tile, Viewbox Icon, Control Host, TranslateTransform Slide)> _navDoorRows = new();
        private readonly HashSet<object> _navRailHolds = new();

        /// <summary>
        /// WPF's InitializeNavRail hover half (MainWindow.NavRail.cs:308-402): cache the rail's
        /// parts, author the shut state, then open on pointer-over and shut on leave / outside
        /// press / deactivation. The pointer test is <c>rail.IsPointerOver</c> (WPF IsMouseOver):
        /// hit-test aware and immune to the Viewbox scale, where window coordinates against the
        /// rail's parent-local Bounds were not. Tunnel + handledEventsToo so no child can swallow
        /// the move. Popups opened from the rail hold it
        /// through <see cref="HoldNavRailOpen"/>; the WPF watchdog is not ported (every trigger
        /// here is level, re-read on each move).
        /// </summary>
        private void HookNavRailHover()
        {
            if (_navRailHooked) return;
            var rail = this.FindControl<Border>("NavSidebar");
            if (rail is null) return;
            _navRailHooked = true;
            _navRail = rail;

            CacheNavRailParts(rail);
            _navRailLabels.AddRange(NavPremiumTagElements);

            // Shut state first, THEN the transitions, so the first frame does not tween.
            ApplyNavRail(false);
            rail.Transitions = Eased(Layoutable.WidthProperty);
            // Linear, as WPF's label/pill fade (SetNavRailExpanded builds it with no easing).
            foreach (var l in _navRailLabels)
                l.Transitions = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty } };
            foreach (var r in _navDoorRows)
            {
                r.Tile.Transitions = Eased(Layoutable.WidthProperty, Layoutable.HeightProperty);
                r.Icon.Transitions = Eased(Layoutable.WidthProperty, Layoutable.HeightProperty);
                r.Host.Transitions = Eased(Visual.OpacityProperty);
                r.Slide.Transitions = Eased(TranslateTransform.YProperty);
            }

            // IsPointerOver is hit-test aware, like WPF's IsMouseOver: an overlay covering the rail
            // (tutorial, remote control, fullscreen browser) owns the pointer, so the rail stays shut.
            void Sync(PointerEventArgs e)
            {
                bool over = rail.IsPointerOver;
                if (over || _navRailHolds.Count == 0) SetNavRailExpanded(over);
            }
            AddHandler(PointerMovedEvent, (_, e) => Sync(e), RoutingStrategies.Tunnel, handledEventsToo: true);
            AddHandler(PointerPressedEvent, (_, e) => Sync(e), RoutingStrategies.Tunnel, handledEventsToo: true);
            PointerExited += (_, _) => { if (_navRailHolds.Count == 0) SetNavRailExpanded(false); };
            Deactivated += (_, _) => { if (_navRailHolds.Count == 0) SetNavRailExpanded(false); };
        }

        private static Transitions Eased(params AvaloniaProperty[] props)
        {
            var t = new Transitions();
            foreach (var p in props)
                t.Add(new DoubleTransition { Property = p, Easing = new QuadraticEaseOut() });
            return t;
        }

        /// <summary>WPF CacheNavRailParts + CacheNavDoorRows (:545-620): a door medallion is a
        /// Button whose content Grid is Ellipse, Border (tile), Viewbox (icon), Grid (name host),
        /// picked by type as WPF does. Every other TextBlock is a label faded with the rail,
        /// except the navrailstatic ones (the lens and the entry icons - Images on WPF).</summary>
        private void CacheNavRailParts(ILogical root)
        {
            foreach (var child in root.LogicalChildren)
            {
                if (child is Button { Content: Grid { Children: [Ellipse, Border tile, Viewbox icon, Grid host] } })
                {
                    var slide = new TranslateTransform();
                    host.RenderTransform = slide;
                    _navDoorRows.Add((tile, icon, host, slide));
                }
                else if (child is TextBlock tb)
                {
                    if (tb.Tag as string != NavRailStaticTextTag) _navRailLabels.Add(tb);
                }
                else CacheNavRailParts(child);
            }
        }

        /// <summary>WPF SetNavRailExpanded + ApplyNavDoorRows (:837-960). Early-outs on the state
        /// it is already in, so a pointer moving inside the rail is one field read per move.</summary>
        private void SetNavRailExpanded(bool expand)
        {
            if (_navRailExpanded == expand || _navRail is null) return;
            _navRailExpanded = expand;

            // Durations first: a transition reads them when the value changes.
            int ms = expand ? NavRailAnimMs : NavRailCollapseAnimMs;
            Time(_navRail, ms);
            foreach (var l in _navRailLabels) Time(l, expand ? ms : ms / 2);   // labels lead in, trail out
            for (int i = 0; i < _navDoorRows.Count; i++)
            {
                var r = _navDoorRows[i];
                int delay = expand ? i * NavDoorLabelStaggerMs : 0;
                Time(r.Tile, ms);
                Time(r.Icon, ms);
                Time(r.Host, expand ? NavDoorLabelFadeMs : ms / 2, delay);
                Time(r.Slide, expand ? NavDoorLabelSlideMs : ms / 2, delay);
            }
            ApplyNavRail(expand);
        }

        private static void Time(Animatable a, int ms, int delayMs = 0)
        {
            foreach (var t in a.Transitions ?? new Transitions())
                if (t is DoubleTransition d)
                {
                    d.Duration = TimeSpan.FromMilliseconds(ms);
                    d.Delay = TimeSpan.FromMilliseconds(delayMs);
                }
        }

        private void ApplyNavRail(bool expand)
        {
            _navRail!.Width = expand ? NavRailExpandedWidth : NavRailCollapsedWidth;
            foreach (var l in _navRailLabels) l.Opacity = expand ? 1 : 0;
            foreach (var r in _navDoorRows)
            {
                r.Tile.Width = r.Tile.Height = expand ? NavDoorTileExpanded : NavDoorTileCollapsed;
                r.Icon.Width = r.Icon.Height = expand ? NavDoorIconExpanded : NavDoorIconCollapsed;
                r.Host.Opacity = expand ? 1 : 0;
                r.Slide.Y = expand ? 0 : NavDoorLabelRise;
            }
            // Shut, a bar would sit over the medallions (WPF :955-958).
            if (this.FindControl<ScrollViewer>("NavRailScroll") is { } sv)
                sv.VerticalScrollBarVisibility = expand ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
        }

        /// <summary>WPF HoldNavRailOpen/ReleaseNavRailOpen (:1309-1351): a popup opened from the
        /// rail keeps it out until the last holder lets go; then it shuts unless the pointer is
        /// on it. No popup on this head calls it yet (the favorites rail and friends chip that do on
        /// WPF are not ported).</summary>
        internal void HoldNavRailOpen(object owner)
        {
            if (_navRailHolds.Add(owner)) SetNavRailExpanded(true);
        }

        internal void ReleaseNavRailOpen(object owner)
        {
            if (_navRailHolds.Remove(owner) && _navRailHolds.Count == 0 && _navRail?.IsPointerOver != true)
                SetNavRailExpanded(false);
        }

        /// <summary>Whether the rail is currently open. NavCheck and the click-through driver read
        /// this rather than Width, which mid-tween reports the in-flight value, not the intent.</summary>
        internal bool NavRailExpanded => _navRailExpanded;

        internal bool NavRailHooked => _navRailHooked;

        /// <summary>
        /// The rail's search pill, one line as in WPF (MainWindow.NavRail.cs:480-484). WPF's Toggle
        /// also refuses during Lockdown; LockdownService is not on this head, so Lockdown cannot be
        /// active here and there is nothing to refuse.
        /// </summary>
        private void BtnNavSearch_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => SettingsPaletteWindow.Toggle(this);

    }
}
