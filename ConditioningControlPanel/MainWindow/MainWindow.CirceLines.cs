using System;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Circe's lines in the main window (<see cref="CirceLines"/>): a red bubble held or popped, a
    /// push landing on the lock, her mood moving. One speech bubble at a time: on Circe's tab
    /// page when it is open, else beside the rail padlock. Nothing when the panel is hidden.
    /// Text only, never spoken.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Wired from <see cref="InitializeChasterFlash"/>.</summary>
        private void InitializeCirceLines()
        {
            try
            {
                var chaster = App.Chaster;
                if (chaster is null) return;
                chaster.Booked += OnCirceBooked;
                chaster.MoodChanged += OnCirceMood;
                chaster.PushLanded += OnCirceLanded;
                Closed += (_, _) =>
                {
                    try
                    {
                        chaster.Booked -= OnCirceBooked;
                        chaster.MoodChanged -= OnCirceMood;
                        chaster.PushLanded -= OnCirceLanded;
                    }
                    catch (Exception ex) { Diag.Swallowed(ex); }
                };
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] Circe's lines could not be wired: {E}", ex.Message); }
        }

        private void OnCirceBooked(string eventId, TabBooking booking)
        {
            if (CirceLines.ForBooking(eventId, booking.AppliedSeconds) is { } moment) CirceSpeak(moment);
        }

        private void OnCirceMood(CircesMood before, CircesMood after)
        {
            if (CirceLines.ForMood(before, after) is { } moment) CirceSpeak(moment);
        }

        private void OnCirceLanded(int seconds) => CirceSpeak(CirceMoment.Landed);

        // Raised on whatever thread booked. Marshalled in order (a pop, then the mood it moved),
        // and picked on the UI thread so a line nobody could see does not spend the throttle.
        private void CirceSpeak(CirceMoment moment)
        {
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    try
                    {
                        if (!IsVisible || WindowState == WindowState.Minimized) return;
                        if (CirceLines.Shared.Pick(moment, DateTime.UtcNow) is not { } key) return;
                        var text = Loc.Get(key);
                        if (ChasterTab?.IsVisible == true) ChasterTab.SayLine(text);
                        else if (ChasterRail is { IsVisible: true } rail) CirceSaysAdorner.Show(rail, text);
                    }
                    catch (Exception ex) { App.Logger?.Debug("[Chaster] Circe's line: {E}", ex.Message); }
                }));
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] Circe's line marshal: {E}", ex.Message); }
        }
    }
}
