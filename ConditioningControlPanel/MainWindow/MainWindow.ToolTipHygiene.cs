using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ConditioningControlPanel
{
    /// <summary>
    /// One narrow piece of shell hygiene: closing a tooltip that a tab switch left floating.
    ///
    /// WPF dismisses a tooltip when the mouse leaves its owner, and nothing else. This app is
    /// navigated by clicks, by code (~25 <c>ShowTab</c> call sites) and, in the automated
    /// play-test harness, by UI Automation - none of which move the cursor. So a tooltip opened by
    /// a stationary pointer outlives the tab it belongs to and hangs over the NEXT tab until the
    /// user physically twitches the mouse. It looks like a rendering bug and it shows up in every
    /// screenshot sweep.
    ///
    /// <para><b>Why the first attempt at this missed.</b> It closed the tooltip belonging to the
    /// owner recorded from <c>ToolTipService.ToolTipOpening</c>. That is a chain of three
    /// assumptions - that the event fired before the switch, that <c>e.OriginalSource</c> is the
    /// element the tooltip is set on (rather than the child the pointer is actually over), and that
    /// <c>ToolTipService.GetToolTip</c> hands back a <see cref="ToolTip"/> rather than raw content
    /// WPF wrapped in one - and any single link breaking leaves the popup on screen. It also leaned
    /// on <see cref="Mouse.Synchronize"/> to dismiss string tooltips, which cannot be relied on
    /// when the cursor ends up over the dashboard's WebView2 airspace, where WPF has nothing to
    /// hit-test.</para>
    ///
    /// <para><b>What it does now.</b> It stops asking who owns the tooltip and goes looking for the
    /// popup itself: every open <see cref="Popup"/> owns its own <see cref="PresentationSource"/>,
    /// so a bounded sweep of this thread's sources finds any live <see cref="ToolTip"/> whatever its
    /// declaration shape - authored instance, arbitrary content, or a plain string - and closes it.
    /// The owner-based close is kept only as a fast path, and the mouse resync only as the thing
    /// that lets WPF's own bookkeeping catch up.</para>
    ///
    /// Public API throughout - no reflection into PopupControlService.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>How deep below a popup's root a ToolTip is allowed to be before we give up.
        /// In practice it is a child or grandchild of the PopupRoot; the cap is what keeps this
        /// from ever walking into a heavy tree by accident.</summary>
        private const int ToolTipSweepMaxDepth = 6;

        /// <summary>Hard ceiling on visuals examined per sweep. Belt for the depth cap's braces.</summary>
        private const int ToolTipSweepMaxVisuals = 96;

        private bool _toolTipHygieneHooked;

        /// <summary>The element whose tooltip is currently up. Weak on purpose: a tab rebuild that
        /// throws its controls away must not be kept alive by a bookkeeping field.</summary>
        private WeakReference<DependencyObject>? _openToolTipOwner;

        /// <summary>
        /// Starts watching for tooltips. Window-level and <c>handledEventsToo</c>, so it sees every
        /// tooltip in the shell whichever tab owns it. Called from the window's Loaded handler AND
        /// (idempotently) from <see cref="CloseStaleToolTip"/>, so there is no window in which a
        /// tooltip can open untracked.
        /// </summary>
        internal void EnsureToolTipHygiene()
        {
            if (_toolTipHygieneHooked) return;
            _toolTipHygieneHooked = true;
            try
            {
                AddHandler(ToolTipService.ToolTipOpeningEvent,
                           new ToolTipEventHandler(OnAnyToolTipOpening), true);
                AddHandler(ToolTipService.ToolTipClosingEvent,
                           new ToolTipEventHandler(OnAnyToolTipClosing), true);
            }
            catch (Exception ex) { App.Logger?.Debug("EnsureToolTipHygiene: {E}", ex.Message); }
        }

        /// <summary>
        /// Records the owner. Note what this does NOT do: touch <c>e.Handled</c>. Handling
        /// ToolTipOpening cancels the tooltip outright, which would silently kill every tooltip in
        /// the app.
        /// </summary>
        private void OnAnyToolTipOpening(object sender, ToolTipEventArgs e)
        {
            try
            {
                // Source, not OriginalSource: the service raises this on the element the tooltip is
                // set on, but the routed args' OriginalSource can be the child the pointer is
                // physically over, which owns no tooltip of its own.
                var owner = (e.Source ?? e.OriginalSource) as DependencyObject;
                if (owner != null) _openToolTipOwner = new WeakReference<DependencyObject>(owner);
            }
            catch { }
        }

        private void OnAnyToolTipClosing(object sender, ToolTipEventArgs e) => _openToolTipOwner = null;

        /// <summary>
        /// Closes whatever tooltip is up, in three independent strokes - each wrapped separately, so
        /// one failing never stops the next:
        /// <list type="number">
        ///   <item><b>Fast path.</b> The tracked owner's tooltip, whether that property holds a
        ///   <see cref="ToolTip"/> or raw content WPF wrapped in one (we walk the content's logical
        ///   parent to reach the wrapper).</item>
        ///   <item><b>The one that actually guarantees it.</b> <see cref="SweepOpenToolTips"/> -
        ///   every open popup has its own PresentationSource, so the live ToolTip can be found and
        ///   closed without knowing anything about who owns it or how it was declared. This is what
        ///   catches the case the first fix missed.</item>
        ///   <item><b>Let WPF catch up.</b> A synthesized MouseLeave on the owner plus a deferred
        ///   <see cref="Mouse.Synchronize"/>, so PopupControlService drops its own reference to the
        ///   tooltip it thinks is open and normal hovering behaves afterwards. Best-effort by
        ///   design: closing the popup above does not depend on either of them.</item>
        /// </list>
        /// Never throws: this runs inside ShowTab, and a cosmetic tidy-up must not be able to break
        /// navigation.
        /// </summary>
        private void CloseStaleToolTip()
        {
            EnsureToolTipHygiene();

            DependencyObject? owner = null;
            try
            {
                if (_openToolTipOwner != null && _openToolTipOwner.TryGetTarget(out var tracked))
                    owner = tracked;
                _openToolTipOwner = null;
                if (owner != null) CloseToolTipOn(owner);
            }
            catch (Exception ex) { App.Logger?.Debug("CloseStaleToolTip (owner): {E}", ex.Message); }

            try { SweepOpenToolTips(); }
            catch (Exception ex) { App.Logger?.Debug("CloseStaleToolTip (sweep): {E}", ex.Message); }

            try
            {
                SyntheticMouseLeave(owner);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { Mouse.Synchronize(); }
                    catch (Exception ex) { App.Logger?.Debug("CloseStaleToolTip sync: {E}", ex.Message); }
                }), DispatcherPriority.Normal);
            }
            catch (Exception ex) { App.Logger?.Debug("CloseStaleToolTip (resync): {E}", ex.Message); }
        }

        /// <summary>
        /// Closes the tooltip attached to one element. Handles both declaration shapes this app
        /// uses: a real <see cref="ToolTip"/> (the skill-tree nodes build one by hand -
        /// MainWindow.Enhancements.cs) and arbitrary content or a bare string, which WPF wraps in a
        /// ToolTip of its own whose <c>Content</c> is that object - reachable as the content's
        /// logical parent once it has been shown at least once.
        /// </summary>
        private static void CloseToolTipOn(DependencyObject owner)
        {
            var value = ToolTipService.GetToolTip(owner);
            var tip = value as ToolTip;
            if (tip == null && value is FrameworkElement content)
                tip = LogicalTreeHelper.GetParent(content) as ToolTip ?? content.Parent as ToolTip;
            if (tip != null && tip.IsOpen) tip.IsOpen = false;
        }

        /// <summary>
        /// Finds and closes every open <see cref="ToolTip"/> on this thread.
        ///
        /// An open Popup lives in its own HWND with its own <see cref="PresentationSource"/> whose
        /// root visual is a popup root, so the sources list is a complete and cheap index of what is
        /// currently floating - no owner lookup, no assumption about how the tooltip was declared,
        /// and no dependence on a mouse hit-test that WebView2 airspace can swallow.
        ///
        /// Real windows are skipped outright (a tooltip is never a window's root visual), which
        /// keeps the sweep away from the compositor overlay hosts' heavy trees; what remains is
        /// walked to a bounded depth with a bounded visual count. Context menus and other popups are
        /// visited and ignored - only a ToolTip is ever touched.
        /// </summary>
        private static void SweepOpenToolTips()
        {
            PresentationSource[] sources;
            try { sources = PresentationSource.CurrentSources.Cast<PresentationSource>().ToArray(); }
            catch { return; }

            foreach (var source in sources)
            {
                try
                {
                    if (source?.RootVisual is not Visual root) continue;
                    if (root is Window) continue;              // never hosts a tooltip popup
                    foreach (var tip in FindToolTips(root))
                        if (tip.IsOpen) tip.IsOpen = false;
                }
                catch (Exception ex) { App.Logger?.Debug("SweepOpenToolTips: {E}", ex.Message); }
            }
        }

        /// <summary>Bounded visual-tree descent for ToolTips. Iterative, so a pathological tree
        /// cannot recurse the stack away, and it stops descending THROUGH a ToolTip - anything
        /// inside one is that tooltip's content, not another tooltip.</summary>
        private static IEnumerable<ToolTip> FindToolTips(Visual root)
        {
            var found = new List<ToolTip>();
            var queue = new Queue<(Visual Visual, int Depth)>();
            queue.Enqueue((root, 0));
            int visited = 0;

            while (queue.Count > 0 && visited < ToolTipSweepMaxVisuals)
            {
                var (visual, depth) = queue.Dequeue();
                visited++;

                if (visual is ToolTip tip) { found.Add(tip); continue; }
                if (depth >= ToolTipSweepMaxDepth) continue;

                int children;
                try { children = VisualTreeHelper.GetChildrenCount(visual); }
                catch { continue; }
                for (int i = 0; i < children; i++)
                {
                    try
                    {
                        if (VisualTreeHelper.GetChild(visual, i) is Visual child)
                            queue.Enqueue((child, depth + 1));
                    }
                    catch { }
                }
            }
            return found;
        }

        /// <summary>
        /// Tells the owner, in the only public way available, that the pointer is no longer on it.
        /// WPF's PopupControlService keeps its own reference to the tooltip it believes is open; if
        /// that is never cleared, re-hovering the very same control can need one extra real mouse
        /// move before its tooltip comes back. A MouseLeave on the element is the signal that clears
        /// it - and the app's own hover FX handlers, which also listen for it, correctly settle back
        /// to rest at the same time.
        /// </summary>
        private static void SyntheticMouseLeave(DependencyObject? owner)
        {
            if (owner is not UIElement element) return;
            var args = new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
            {
                RoutedEvent = Mouse.MouseLeaveEvent,
                Source = element,
            };
            element.RaiseEvent(args);
        }
    }
}
