using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The intake window's own lifecycle (WPF IntakeHostService): the remembered window mode and the
    /// page's fullscreen-set, ducking the control panel for the run, and the heartbeat watchdog with
    /// its relaunch-once recovery ladder.
    /// </summary>
    internal sealed partial class IntakeHostWindow
    {
        private static readonly TimeSpan HeartbeatPoll = TimeSpan.FromSeconds(5);   // WPF StartHeartbeatWatch

        /// <summary>WPF _relaunchedOnce: one recovery relaunch per app session; a second failure gives up.</summary>
        internal static bool RelaunchedOnce;

        internal TimeProvider Clock { get; set; } = TimeProvider.System;
        private DateTime Now => Clock.GetUtcNow().UtcDateTime;

        /// <summary>The control panel to minimise while the run is up (WPF DuckMainWindow); null = no duck.</summary>
        internal Window? DuckTarget { get; init; }

        /// <summary>Reopens the intake after a wedge (the opener's own launch, windowed).</summary>
        internal Action? Relaunch { get; set; }

        private WindowState? _duckedFrom;
        private bool _pageReady;
        private DispatcherTimer? _heartbeatWatch;

        private void StartLifecycle()
        {
            Run.Beat(Now);
            _heartbeatWatch = new DispatcherTimer { Interval = HeartbeatPoll };
            _heartbeatWatch.Tick += (_, _) => CheckHeartbeat();
            _heartbeatWatch.Start();
            DuckMain();
        }

        private void EndLifecycle()
        {
            _heartbeatWatch?.Stop();
            _heartbeatWatch = null;
            RestoreMain();   // WPF DisposeAll: give the control panel back before anything else
        }

        internal bool HeartbeatWatchRunning => _heartbeatWatch?.IsEnabled == true;

        /// <summary>WPF heartbeat tick: only once the page is live, never during a wind-down;
        /// 20 s of silence (Core IntakeRun.HeartbeatTimeout) recovers.</summary>
        internal void CheckHeartbeat()
        {
            if (!_pageReady || Run.Exiting || _closed) return;
            if (!Run.IsHeartbeatSilent(Now)) return;
            Log.Warning("IntakeHost: page heartbeat silent >20s - recovering");
            Recover("heartbeat-silent");
        }

        /// <summary>WPF Recover: close, then relaunch once - windowed - carrying the duck choice.</summary>
        private void Recover(string reason)
        {
            var retry = !RelaunchedOnce;
            Log.Warning("IntakeHost: recovery ({Reason}) - {Action}", reason, retry ? "relaunching once" : "giving up");
            Close();
            if (!retry) return;
            RelaunchedOnce = true;
            Relaunch?.Invoke();
        }

        /// <summary>WPF ApplyHostFullscreen: C# owns the borderless toggle, echoes the REAL state and
        /// remembers it so the next launch is built in the mode the player left.</summary>
        internal void ApplyFullscreen(bool on)
        {
            WindowState = on ? WindowState.FullScreen : WindowState.Normal;
            var real = WindowState == WindowState.FullScreen;
            Post(new { type = "fullscreen", on = real });
            var settings = CoreSettings.Current;
            if (settings.IntakeFullscreen == real) return;
            settings.IntakeFullscreen = real;
            CoreSettings.Save();
        }

        /// <summary>WPF DuckMainWindow: a plain minimise, never when main is hidden or already minimised.</summary>
        private void DuckMain()
        {
            var main = DuckTarget;
            if (main == null || !main.IsVisible || main.WindowState == WindowState.Minimized) return;
            _duckedFrom = main.WindowState;   // a maximised panel comes back maximised
            main.WindowState = WindowState.Minimized;
            Activate();   // keep the page focused from the first frame
        }

        /// <summary>WPF RestoreMainWindow: only our own duck, and not if the user moved main since.</summary>
        private void RestoreMain()
        {
            if (_duckedFrom is not { } was) return;
            _duckedFrom = null;
            var main = DuckTarget;
            if (main == null || !main.IsVisible || main.WindowState != WindowState.Minimized) return;
            main.WindowState = was == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            try { main.Activate(); } catch (Exception ex) { Log.Debug("IntakeHost.RestoreMain: {E}", ex.Message); }
        }
    }
}
