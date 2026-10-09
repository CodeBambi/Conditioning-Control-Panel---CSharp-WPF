// PORTED from ConditioningControlPanel/MainWindow/MainWindow.StartStop.cs: the Intensity Ramp timer
// (StartRampTimer :575, StopRampTimer :601, RampTimer_Tick :649) and the Scheduler (timer + 60 s grace
// MainWindow.xaml.cs:637, CheckSchedulerOnStartup :747, SchedulerTimer_Tick :786, manual-stop memory
// BtnStart_Click :101-111). The rules live in Core (Helpers/IntensityRampRun, Helpers/SchedulerRun);
// this partial owns the two DispatcherTimers and the engine calls.
// Scheduler stays OFF-only from quick-toggle gestures (SchedulerOffGestureTests): nothing here turns
// it on; it only acts on the saved SchedulerEnabled flag.

using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Helpers;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        internal readonly IntensityRampRun Ramp = new();
        internal readonly SchedulerRun Scheduler = new();
        private DispatcherTimer? _rampTimer;
        private DispatcherTimer? _schedulerTimer;

        /// <summary>WPF StartEngine :398: the ramp starts with the engine when the card is on.</summary>
        internal void StartRampIfEnabled()
        {
            var s = CoreSettings.Current;
            if (s == null || !s.IntensityRampEnabled) return;
            StopRampTimer();
            Ramp.Start(s, DateTime.Now);
            _rampTimer = new DispatcherTimer { Interval = IntensityRampRun.TickInterval };
            _rampTimer.Tick += RampTimer_Tick;
            _rampTimer.Start();
            Serilog.Log.Information("Ramp timer started - Duration: {Duration}min, Mode: {Mode}, Multiplier: {Mult}x, Range: {Start}%->{End}%",
                s.RampDurationMinutes, s.RampMode, s.SchedulerMultiplier, s.RampStartPercent, s.RampEndPercent);
        }

        /// <summary>WPF StopRampTimer: stop the clock and put every linked value back.</summary>
        internal void StopRampTimer()
        {
            _rampTimer?.Stop();
            _rampTimer = null;
            var s = CoreSettings.Current;
            if (s != null && Ramp.IsActive)
            {
                Ramp.Stop(s);
                Serilog.Log.Information("Ramp timer stopped - values reset to base");
            }
        }

        private void RampTimer_Tick(object? sender, EventArgs e)
        {
            var s = CoreSettings.Current;
            if (s == null) return;
            if (!Ramp.Tick(s, DateTime.Now, App.Sessions?.IsRunning == true)) return;
            Serilog.Log.Information("Ramp complete - ending session");
            Platform.OsNotifications.Show("Session Complete", "Intensity ramp finished. Stopping...");
            StopEngine();
        }

        /// <summary>WPF MainWindow.xaml.cs:637: the 30 s scheduler poll, armed after a 60 s grace
        /// period, then the startup check.</summary>
        internal void StartSchedulerClock()
        {
            if (_schedulerTimer != null) return;
            _schedulerTimer = new DispatcherTimer { Interval = SchedulerRun.PollInterval };
            _schedulerTimer.Tick += SchedulerTimer_Tick;
            Closed += (_, _) => _schedulerTimer?.Stop();   // WPF WindowChrome.cs:186
            Serilog.Log.Information("Scheduler will start after {Seconds}s grace period", (int)SchedulerRun.GracePeriod.TotalSeconds);
            _ = Task.Delay(SchedulerRun.GracePeriod).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
            {
                if (_exitRequested) return;
                _schedulerTimer?.Start();
                CheckSchedulerOnStartup();
                Serilog.Log.Information("Scheduler grace period complete - scheduler now active");
            }));
        }

        private void CheckSchedulerOnStartup()
        {
            var s = CoreSettings.Current;
            if (s == null) return;
            Serilog.Log.Information("Scheduler startup check: Enabled={Enabled}, InWindow={InWindow}",
                s.SchedulerEnabled, SchedulerRun.IsInWindow(s, DateTime.Now));
            if (Scheduler.CheckOnStartup(s, DateTime.Now) != SchedulerRun.Action.Start) return;
            Serilog.Log.Information("Scheduler: App started within scheduled time window - auto-starting");
            MinimizeToTrayForScheduler();
            Platform.OsNotifications.Show("Scheduler Active", "Session auto-started based on schedule.");
            if (!CoreEngine.IsRunning) StartEngine();
        }

        internal void SchedulerTimer_Tick(object? sender, EventArgs e)
        {
            var s = CoreSettings.Current;
            if (s == null) return;
            switch (Scheduler.Tick(s, DateTime.Now, CoreEngine.IsRunning))
            {
                case SchedulerRun.Action.Start:
                    Serilog.Log.Information("Scheduler: Entering scheduled time window - auto-starting");
                    MinimizeToTrayForScheduler();
                    Platform.OsNotifications.Show("Scheduler Active", "Session auto-started based on schedule.");
                    StartEngine();
                    break;
                case SchedulerRun.Action.Stop:
                    Serilog.Log.Information("Scheduler: Exiting scheduled time window - auto-stopping");
                    StopEngine();
                    Platform.OsNotifications.Show("Scheduler", "Scheduled session ended.");
                    break;
            }
        }

        /// <summary>WPF TrayIconService.MinimizeToTray: hide to the tray only where a tray exists.</summary>
        /// <summary>WPF BtnStart_Click :101: a hand stop inside the window is remembered until it ends.</summary>
        private void NoteSchedulerManualStop()
        {
            if (CoreSettings.Current is { } s) Scheduler.NoteManualStop(s, DateTime.Now);
        }

        private void MinimizeToTrayForScheduler()
        {
            if (Tray is null || !TrayHostPresent()) return;
            Hide();
            HideAvatarTube();
        }
    }
}
