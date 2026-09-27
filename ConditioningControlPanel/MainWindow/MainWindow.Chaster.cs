using System;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Circe's tab in the main window: the "+0:30" that pops where the time was earned.
    ///
    /// <para>The tab is deliberately quiet - it books a price and says nothing, which is most of
    /// the point. This is the one exception the owner asked for: when time moves, the player sees
    /// the number move, where the time was earned (the popped bubble, the flash that showed,
    /// else the cursor), big and above every overlay, with the rail padlock pulsing in the same
    /// colour. No sound, no toast, nothing that has to be dismissed.</para>
    ///
    /// <para><b>Inert without a link.</b> <see cref="ChasterService.Booked"/> only ever fires when
    /// an account is linked, the tab is switched on and a priced row matched, so on every install
    /// that has not touched Chaster this partial costs one event subscription and nothing else.
    /// </para>
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>What the live figure is showing and when its coalescing window opened. Null
        /// between figures.</summary>
        private BookedFlashPlan.Figure? _chasterFigure;

        /// <summary>The adorner currently on the padlock, if any. It removes itself when its fade
        /// completes; the reference is only kept so a booking inside the coalescing window can
        /// re-label it instead of stacking a second figure on top.</summary>
        private ChasterBookedFlash? _chasterFlash;

        /// <summary>The big pop currently standing where the time was earned, if any, so a booking
        /// inside the coalescing window re-labels it instead of stacking a second one.</summary>
        private ChasterBookedPop? _chasterPop;

        /// <summary>
        /// Wire the figure. Called once from the MainWindow constructor beside the other rail and
        /// header initializers.
        /// </summary>
        private void InitializeChasterFlash()
        {
            try
            {
                var chaster = App.Chaster;
                if (chaster is null) return;

                chaster.BookedAt += OnChasterBooked;
                Closed += (_, _) =>
                {
                    try { chaster.BookedAt -= OnChasterBooked; } catch (Exception ex) { Diag.Swallowed(ex); }
                    // An open pop is an unowned visible window: it must not hold OnLastWindowClose.
                    try { ChasterBookedPop.CloseAll(); } catch (Exception ex) { Diag.Swallowed(ex); }
                };
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Chaster] booked flash could not be wired: {E}", ex.Message);
            }
        }

        /// <summary>
        /// A price landed. Raised on whatever thread booked it (a hook on the UI thread, the daily
        /// settle on a timer), so everything below is marshalled first.
        /// </summary>
        private void OnChasterBooked(string eventId, TabBooking booking, Point? originPx)
        {
            if (booking.AppliedSeconds == 0) return;
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;

            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Normal,
                    new Action(() => ShowBookedFlash(eventId, booking.AppliedSeconds, originPx)));
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] booked flash marshal: {E}", ex.Message); }
        }

        private void ShowBookedFlash(string eventId, int seconds, Point? causePx)
        {
            try
            {
                var (figure, isNew) = BookedFlashPlan.Merge(_chasterFigure, seconds, eventId, Environment.TickCount64);
                _chasterFigure = figure;
                var look = BookedFlashPlan.For(figure, MotionFx.Level);
                var anchor = ChasterRail;

                if (!isNew && (_chasterPop is not null || _chasterFlash is not null))
                {
                    // Same figure, bigger number. The travel is not restarted: a burst of prices
                    // reads as one number settling rather than as a stutter.
                    if (look is { } merged)
                    {
                        _chasterPop?.Retitle(merged.Text, merged.Colour);
                        _chasterFlash?.Retitle(merged.Text, merged.Colour);
                    }
                    else
                    {
                        // A price and its credit inside the same breath net to nothing.
                        _chasterPop?.Dismiss(); _chasterPop = null;
                        _chasterFlash?.Dismiss(); _chasterFlash = null;
                    }
                }
                else if (look is { } fresh)
                {
                    // Owner desk note: the pop lands where the time was earned (the popped bubble,
                    // the flash that showed), else at the cursor, and in its own topmost window so
                    // Brain Drain and flash windows cannot sit over it. Earlier pops stay up and
                    // the new one stacks above them. The rail adorner is the last fallback.
                    _chasterFlash?.Dismiss(); _chasterFlash = null;
                    var origin = BookedPopLayout.ResolveOrigin(Valid(causePx), CursorPx());
                    _chasterPop = origin is { } at
                        ? ChasterBookedPop.Show(at, fresh, BookedPopLayout.For(MotionFx.Level))
                        : null;
                    if (_chasterPop is null && IsVisible && anchor is not null)
                        _chasterFlash = ChasterBookedFlash.Show(anchor, fresh);
                }

                // The ring takes the figure's colour for a beat, so the chip and the number read as
                // one event. A net-zero merge has no plan and no pulse.
                if (IsVisible && look is { } pulse) (anchor as ChasterRailChip)?.Pulse(pulse.Colour);
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] booked flash: {E}", ex.Message); }
        }

        private static Point? Valid(Point? p) =>
            p is { } v && !double.IsNaN(v.X) && !double.IsNaN(v.Y) && !double.IsInfinity(v.X) && !double.IsInfinity(v.Y) ? v : null;

        /// <summary>The cursor in physical desktop px (the process is per-monitor aware).</summary>
        private static Point? CursorPx()
        {
            try { return GetCursorPos(out var p) ? new Point(p.X, p.Y) : null; }
            catch (Exception ex) { Diag.Swallowed(ex); return null; }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct CursorPoint { public int X; public int Y; }

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetCursorPos")]
        private static extern bool GetCursorPos(out CursorPoint pt);
    }
}
