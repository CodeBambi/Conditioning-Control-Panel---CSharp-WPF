using System;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Circe's tab in the main window: the flashing "+0:30" over the rail padlock.
    ///
    /// <para>The tab is deliberately quiet - it books a price and says nothing, which is most of
    /// the point. This is the one exception the owner asked for: when time moves, the player sees
    /// the number move, at the padlock the page lives behind, and nowhere else. No sound, no
    /// toast, nothing that has to be dismissed.</para>
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

                chaster.Booked += OnChasterBooked;
                Closed += (_, _) => { try { chaster.Booked -= OnChasterBooked; } catch (Exception ex) { Diag.Swallowed(ex); } };
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
        private void OnChasterBooked(string eventId, TabBooking booking)
        {
            if (booking.AppliedSeconds == 0) return;
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;

            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Normal,
                    new Action(() => ShowBookedFlash(eventId, booking.AppliedSeconds)));
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] booked flash marshal: {E}", ex.Message); }
        }

        private void ShowBookedFlash(string eventId, int seconds)
        {
            try
            {
                // Hidden to the tray or standing behind the launcher: there is no padlock to float
                // off, and a figure nobody can see is a figure the player never gets told about.
                // The tab itself is the record; this is only ever the live word.
                if (!IsVisible) { _chasterFigure = null; _chasterFlash = null; return; }

                var anchor = ChasterRail;
                if (anchor is null) return;

                var (figure, isNew) = BookedFlashPlan.Merge(_chasterFigure, seconds, eventId, Environment.TickCount64);
                _chasterFigure = figure;
                var plan = BookedFlashPlan.For(figure, MotionFx.Level);

                if (isNew || _chasterFlash is null)
                {
                    _chasterFlash?.Dismiss();
                    _chasterFlash = plan is { } fresh ? ChasterBookedFlash.Show(anchor, fresh) : null;
                }
                else if (plan is { } merged)
                {
                    // Same figure, bigger number. The travel is not restarted: a burst of prices
                    // reads as one number settling rather than as a stutter.
                    _chasterFlash.Retitle(merged.Text, merged.Colour);
                }
                else
                {
                    // A price and its credit inside the same breath net to nothing. Take the
                    // figure off rather than float a "+0:00".
                    _chasterFlash.Dismiss();
                    _chasterFlash = null;
                }

                // TODO(merge, lane CHIP): the rail chip gains `public void Pulse(Color tint)` on
                // feat/chaster-ux-chip - a 250ms tint pulse on the ring. This lane must not edit
                // Controls/ChasterRailChip.cs, so the call is left here for whoever merges the two
                // branches. One line, right below, once the method exists:
                //     if (plan is { } pulse) (anchor as ChasterRailChip)?.Pulse(pulse.Colour);
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] booked flash: {E}", ex.Message); }
        }
    }
}
