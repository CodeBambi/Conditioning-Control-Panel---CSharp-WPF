// PORTED from ConditioningControlPanel/MainWindow/MainWindow.CirceLines.cs: Circe's lines (Core
// CirceLines) for a booked red bubble, a push landing on the lock and her mood moving. One speech
// bubble at a time: on Circe's tab page when it is open, else beside the rail padlock. Nothing when
// the window is hidden. Text only, never spoken.
using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>The picker and its clock (WPF CirceLines.Shared + DateTime.UtcNow); tests step both.</summary>
        internal CirceLines CircePicker { get; set; } = CirceLines.Shared;
        internal Func<DateTime> CirceNow { get; set; } = () => DateTime.UtcNow;

        /// <summary>Wired from <see cref="InitializeChasterFlash"/>, as WPF.</summary>
        private void InitializeCirceLines(ChasterService chaster)
        {
            chaster.Booked += OnCirceBooked;
            chaster.MoodChanged += OnCirceMood;
            chaster.PushLanded += OnCirceLanded;
            Closed += (_, _) =>
            {
                chaster.Booked -= OnCirceBooked;
                chaster.MoodChanged -= OnCirceMood;
                chaster.PushLanded -= OnCirceLanded;
            };
        }

        private void OnCirceBooked(string eventId, TabBooking booking)
        {
            if (CirceLines.ForBooking(eventId, booking) is { } moment) CirceSpeak(moment);
        }

        private void OnCirceMood(CircesMood before, CircesMood after)
        {
            if (CirceLines.ForMood(before, after) is { } moment) CirceSpeak(moment);
        }

        private void OnCirceLanded(int seconds) => CirceSpeak(CirceMoment.Landed);

        // Raised on whatever thread booked. Marshalled in order (a pop, then the mood it moved), and
        // picked on the UI thread so a line nobody could see does not spend the throttle.
        private void CirceSpeak(CirceMoment moment) => Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (!IsVisible || WindowState == WindowState.Minimized) return;
                if (CircePicker.Pick(moment, CirceNow()) is not { } key) return;
                var text = Loc.Get(key);
                if (Named<Tabs.ChasterTabView>("ChasterTab") is { IsVisible: true } tab) tab.SayLine(text);
                else if (Named<ChasterRailChip>("ChasterRail") is { IsVisible: true } rail) CirceSaysAdorner.Show(rail, text);
            }
            catch (Exception ex) { Serilog.Log.Debug("[Chaster] Circe's line: {E}", ex.Message); }
        });
    }
}
