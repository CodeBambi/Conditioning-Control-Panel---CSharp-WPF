using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    /// Deliberately built from public API only - no reflection into PopupControlService, whose
    /// private state is the only place a string tooltip's popup can be reached directly.
    /// </summary>
    public partial class MainWindow
    {
        private bool _toolTipHygieneHooked;

        /// <summary>The element whose tooltip is currently up. Weak on purpose: a tab rebuild that
        /// throws its controls away must not be kept alive by a bookkeeping field.</summary>
        private WeakReference<DependencyObject>? _openToolTipOwner;

        /// <summary>
        /// Starts watching for tooltips. Window-level and <c>handledEventsToo</c>, so it sees every
        /// tooltip in the shell whichever tab owns it. Hooked lazily from the first
        /// <see cref="CloseStaleToolTip"/> call rather than from a startup path, so it costs nothing
        /// until the user navigates at least once.
        /// </summary>
        private void EnsureToolTipHygiene()
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
                if (e.OriginalSource is DependencyObject owner)
                    _openToolTipOwner = new WeakReference<DependencyObject>(owner);
            }
            catch { }
        }

        private void OnAnyToolTipClosing(object sender, ToolTipEventArgs e) => _openToolTipOwner = null;

        /// <summary>
        /// Closes whatever tooltip is up. Two strokes, because the two kinds of tooltip in this app
        /// need different ones:
        /// <list type="number">
        ///   <item>an explicitly authored <see cref="ToolTip"/> element can simply be told to
        ///   close;</item>
        ///   <item>a plain-string tooltip has no reachable handle - WPF's internal
        ///   PopupControlService owns it. But <see cref="Mouse.Synchronize"/> re-runs the hit test
        ///   under the (unmoved) cursor, and by the time it runs the old tab is collapsed, so it
        ///   raises exactly the MouseLeave that service is waiting for. Deferred to the dispatcher
        ///   so ShowTab's collapse has actually happened first, at Normal priority - Loaded
        ///   priority is starved in this app and would never run.</item>
        /// </list>
        /// Never throws: this runs inside ShowTab, and a cosmetic tidy-up must not be able to break
        /// navigation.
        /// </summary>
        private void CloseStaleToolTip()
        {
            try
            {
                EnsureToolTipHygiene();

                if (_openToolTipOwner != null && _openToolTipOwner.TryGetTarget(out var owner))
                {
                    if (ToolTipService.GetToolTip(owner) is ToolTip tip && tip.IsOpen)
                        tip.IsOpen = false;
                }
                _openToolTipOwner = null;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { Mouse.Synchronize(); }
                    catch (Exception ex) { App.Logger?.Debug("CloseStaleToolTip sync: {E}", ex.Message); }
                }), DispatcherPriority.Normal);
            }
            catch (Exception ex) { App.Logger?.Debug("CloseStaleToolTip: {E}", ex.Message); }
        }
    }
}
