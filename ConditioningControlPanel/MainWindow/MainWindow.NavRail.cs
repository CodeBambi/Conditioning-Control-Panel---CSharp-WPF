using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The nav rail's collapsed/expanded behaviour (owner ask, 2026-08-11: "the sidebar takes too
    /// much space, can we collapse the names and keep only the icons? on hover we can bring it up
    /// and uncollapse it - keep it there for 1 sec before collapsing it back, or on any click
    /// elsewhere").
    ///
    /// <para><b>Why the expanded rail OVERLAYS the page instead of widening the layout.</b> The
    /// whole UI lives in a <c>Viewbox Stretch="Fill"</c> over a fixed <c>DesignCanvas</c>, and the
    /// content column is deliberately pinned at the width every tab was authored against (Phase 1
    /// grew the canvas rather than shrinking that column, because the layouts clip silently
    /// otherwise - fx-inventory §12.8). If the rail widened the canvas on hover, the Viewbox would
    /// re-scale EVERY tab by 176/56 on each mouse-over: the whole app would visibly breathe. So the
    /// rail is sized for the collapsed 56px in the grid and spans both columns, painting over the
    /// page when it opens. Nothing below it ever relayouts.</para>
    ///
    /// <para><b>Why the page still is not buried.</b> Overlaying is mandatory, but it does not have
    /// to be free of charge to the page. Canvas column 0 reserves 96px - the 56px rail plus a 40px
    /// permanent gutter of bare canvas that the left-aligned rail Border refuses to fill - and the
    /// open width is 176 rather than the 190 the rows were authored at. The flyout therefore spends
    /// its first 40px on dead space and only the last 80 on the page, down from 134.
    /// <see cref="NavRailExpandedWidth"/> carries the arithmetic and the label floor.</para>
    ///
    /// <para>Collapsing also shuts the accordion. Seven door icons read as a rail; seven door icons
    /// with four unlabelled child icons wedged among them reads as noise, and the child rows are
    /// exactly the ones whose meaning lives in the label. <see cref="_expandedDoor"/> is left
    /// untouched, so opening the rail restores the door the user was in.</para>
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Icon-only width. Also the width the rail rows' left paddings centre their
        /// icons in (door 17+22+17, entry 21+14+21 - MainWindow.xaml), so it cannot move without
        /// re-centring both. DesignCanvas column 0 is WIDER than this on purpose (see below);
        /// what must stay in step is that the column is never NARROWER, or the page slides
        /// under the collapsed rail.</summary>
        private const double NavRailCollapsedWidth = 56;

        /// <summary>
        /// Open width. Was 190 - the width the rail rows were originally authored against - until
        /// the owner flagged (2026-08-11) that the flyout buries the left edge of the page it is
        /// opened on top of. The fix is split in two, per the owner's own "half and half": canvas
        /// column 0 grew to 96 so the first 40px of the flyout land on a permanent empty gutter
        /// (MainWindow.xaml, DesignCanvas), and the flyout itself gives back 14px here. True
        /// overlay over live UI: 190-56 = 134px before, 176-96 = 80px now.
        ///
        /// <para>176 is a floor, not a preference. The rail must fully show its longest ENTRY
        /// label in every shipped language, and the entry row spends 21 (left padding) + 14 (icon)
        /// + 6 (icon margin) + 8 (right padding) = 49px before the text starts. The worst string
        /// is es "Entrenador de Parpadeo" at 122px (fr and ru sit at 120), so 171px is the true
        /// minimum and 176 is that plus a font-fallback cushion. Shrink this further only by
        /// buying label room first - the labels live in a horizontal StackPanel, which measures
        /// children at infinite width, so TextTrimming cannot save an over-long row here: it just
        /// runs into the Border's ClipToBounds and gets cut with no ellipsis.</para>
        /// </summary>
        private const double NavRailExpandedWidth = 176;

        private const int NavRailAnimMs = 150;

        /// <summary>Owner's number: the rail stays open a beat after the pointer leaves, so a
        /// diagonal slide toward a child row does not shut it mid-reach.</summary>
        private const int NavRailCollapseDelayMs = 1000;

        private bool _navRailExpanded;
        private bool _navRailReady;
        private DispatcherTimer? _navRailCollapseTimer;

        /// <summary>Outstanding <see cref="HoldNavRailOpen"/> claims. While this is above zero the
        /// rail ignores every collapse trigger - the delay timer, the pointer leaving, and the
        /// click-elsewhere - because the caller is showing the user something IN the rail and a
        /// rail that shuts underneath a spotlight is worse than no spotlight at all. Counted, not
        /// a bool: a tutorial step and the palette can be up at once, and the first one to finish
        /// must not release the other's hold.</summary>
        private int _navRailHoldCount;

        /// <summary>Every label in the rail, cached once. Faded rather than collapsed: a
        /// Visibility flip would re-measure the door panels mid-tween and fight the accordion's
        /// own Height animation.</summary>
        private readonly List<TextBlock> _navRailLabels = new();

        /// <summary>Rail buttons, so icons can centre themselves when the labels are gone.</summary>
        private readonly List<ButtonBase> _navRailButtons = new();

        /// <summary>
        /// Called once from the Loaded handler, after templates are applied - the label/button
        /// caches are a visual-tree walk and find nothing before that.
        /// </summary>
        private void InitializeNavRail()
        {
            try
            {
                if (_navRailReady || NavSidebar == null) return;

                CacheNavRailParts(NavSidebar);

                _navRailCollapseTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(NavRailCollapseDelayMs)
                };
                _navRailCollapseTimer.Tick += (_, __) =>
                {
                    _navRailCollapseTimer!.Stop();
                    if (_navRailHoldCount > 0) return;
                    // The pointer may have come back during the delay.
                    if (!NavSidebar.IsMouseOver) SetNavRailExpanded(false);
                };

                NavSidebar.MouseEnter += (_, __) =>
                {
                    _navRailCollapseTimer?.Stop();
                    SetNavRailExpanded(true);
                };
                NavSidebar.MouseLeave += (_, __) =>
                {
                    _navRailCollapseTimer?.Stop();
                    if (_navRailHoldCount > 0) return;
                    _navRailCollapseTimer?.Start();
                };

                // "or on any click elsewhere". Preview, so it lands even when the click is
                // handled by whatever it hit. A click INSIDE the rail is a navigation and keeps
                // the rail open - the delay timer takes over once the pointer leaves.
                PreviewMouseDown += (_, __) =>
                {
                    if (NavSidebar.IsMouseOver) return;
                    if (_navRailHoldCount > 0) return;
                    _navRailCollapseTimer?.Stop();
                    SetNavRailExpanded(false);
                };

                _navRailReady = true;
                SetNavRailExpanded(false, animate: false);
            }
            catch (Exception ex)
            {
                // A rail that fails to initialise stays open at its authored width, which is the
                // pre-2026-08-11 behaviour - degraded, not broken.
                App.Logger?.Warning(ex, "InitializeNavRail failed; rail stays expanded");
            }
        }

        private void CacheNavRailParts(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TextBlock tb) _navRailLabels.Add(tb);
                else if (child is ButtonBase b) _navRailButtons.Add(b);
                CacheNavRailParts(child);
            }
        }

        private void SetNavRailExpanded(bool expand, bool animate = true)
        {
            if (NavSidebar == null) return;
            if (_navRailExpanded == expand && _navRailReady && animate) return;
            _navRailExpanded = expand;

            double to = expand ? NavRailExpandedWidth : NavRailCollapsedWidth;
            animate &= MotionFx.AllowTransitions;

            if (animate)
            {
                var width = new DoubleAnimation(to, TimeSpan.FromMilliseconds(NavRailAnimMs))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                NavSidebar.BeginAnimation(FrameworkElement.WidthProperty, width);

                // Labels trail the width slightly on the way out and lead it on the way in, so
                // text never paints outside the rail's clip.
                var fade = new DoubleAnimation(expand ? 1 : 0, TimeSpan.FromMilliseconds(
                    expand ? NavRailAnimMs : NavRailAnimMs / 2));
                foreach (var label in _navRailLabels)
                    label.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            else
            {
                NavSidebar.BeginAnimation(FrameworkElement.WidthProperty, null);
                NavSidebar.Width = to;
                foreach (var label in _navRailLabels)
                {
                    label.BeginAnimation(UIElement.OpacityProperty, null);
                    label.Opacity = expand ? 1 : 0;
                }
            }

            // Icons centre themselves once the label is gone.
            foreach (var b in _navRailButtons)
                b.HorizontalContentAlignment = expand ? HorizontalAlignment.Left : HorizontalAlignment.Center;

            ApplyNavRailDoorState(animate);
        }

        /// <summary>
        /// Shuts every door panel while the rail is collapsed and restores
        /// <see cref="_expandedDoor"/> when it opens. Deliberately does NOT write
        /// <see cref="_expandedDoor"/>: the accordion's own state is what the user chose, and
        /// hovering the rail must not silently re-home them.
        /// </summary>
        private void ApplyNavRailDoorState(bool animate)
        {
            foreach (var d in NavDoorMap)
            {
                var parts = NavDoorParts(d.Door);
                if (parts.Panel == null) continue;   // pinned Settings door has no panel

                bool open = _navRailExpanded &&
                            string.Equals(d.Door, _expandedDoor, StringComparison.Ordinal);
                SetDoorPanelExpanded(d.Door, parts.Panel, parts.Entries, open, animate);
            }
        }

        /// <summary>
        /// Opens the rail and holds it, for code-driven navigation that needs the user to SEE
        /// where they landed (the tutorial's spotlights, the Ctrl+K palette). Without this the
        /// spotlight would point at a 56px icon strip with the target row shut inside it.
        ///
        /// <para>The hold is a real suspension, not a restart: this used to Stop the collapse
        /// timer and immediately Start it again, so the rail shut one second later exactly as if
        /// nobody had asked - a hold that did not hold. Every caller MUST pair this with
        /// <see cref="ReleaseNavRailOpen"/>, or the rail stays open for the session.</para>
        /// </summary>
        internal void HoldNavRailOpen()
        {
            try
            {
                _navRailHoldCount++;
                _navRailCollapseTimer?.Stop();
                SetNavRailExpanded(true);
            }
            catch (Exception ex) { App.Logger?.Debug("HoldNavRailOpen: {E}", ex.Message); }
        }

        /// <summary>
        /// Drops one <see cref="HoldNavRailOpen"/> claim. The last one out hands the rail back to
        /// the normal delay: the pointer may well be sitting on it (a spotlight usually ends with
        /// a click on the row), so the timer's own IsMouseOver check gets the final say rather
        /// than collapsing on the spot.
        /// </summary>
        internal void ReleaseNavRailOpen()
        {
            try
            {
                if (_navRailHoldCount > 0) _navRailHoldCount--;
                if (_navRailHoldCount > 0) return;
                _navRailCollapseTimer?.Stop();
                _navRailCollapseTimer?.Start();
            }
            catch (Exception ex) { App.Logger?.Debug("ReleaseNavRailOpen: {E}", ex.Message); }
        }
    }
}
