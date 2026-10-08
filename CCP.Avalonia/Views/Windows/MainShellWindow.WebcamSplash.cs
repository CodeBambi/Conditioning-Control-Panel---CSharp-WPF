using System;
using ConditioningControlPanel.Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>WPF InstallWebcamLoadingSplash (MainWindow.LabTab.cs:600): the movable splash follows the
    /// tracker's startup progress, so every entry point that starts the camera gets it. Subscribed while
    /// the shell is open (the tracker is process-wide).</summary>
    public partial class MainShellWindow
    {
        private WebcamLoadingSplash? _webcamLoadingSplash;
        internal WebcamLoadingSplash? WebcamLoadingSplashForTests => _webcamLoadingSplash;

        private void InitializeWebcamLoadingSplash()
        {
            var tracker = WebcamTracker.Instance;
            Opened += (_, _) => { tracker.OnStartupProgress += OnWebcamStartupProgress; tracker.StateChanged += OnWebcamStartupState; };
            Closed += (_, _) => { tracker.OnStartupProgress -= OnWebcamStartupProgress; tracker.StateChanged -= OnWebcamStartupState; };
        }

        private void OnWebcamStartupProgress(double progress, string status)
        {
            try
            {
                if (progress >= 1.0)
                {
                    // Engine is up: the bar full for a beat, then fade.
                    _webcamLoadingSplash?.SetProgress(1.0, status);
                    _webcamLoadingSplash?.CloseSplash();
                    return;
                }
                if (_webcamLoadingSplash == null)
                {
                    // No splash when the shell is not on screen (minimized to tray, background start).
                    if (!IsVisible) return;
                    var splash = new WebcamLoadingSplash();
                    splash.Closed += (_, _) => { if (ReferenceEquals(_webcamLoadingSplash, splash)) _webcamLoadingSplash = null; };
                    _webcamLoadingSplash = splash;
                    splash.Show(this);
                }
                _webcamLoadingSplash.SetProgress(progress, status);
            }
            catch (Exception ex) { Log.Warning(ex, "MainShellWindow: webcam loading splash update failed"); }
        }

        /// <summary>A start that failed before 1.0 says why (#300/#311); one a Stop overtook just closes.</summary>
        private void OnWebcamStartupState()
        {
            var tracker = WebcamTracker.Instance;
            if (_webcamLoadingSplash == null || tracker.IsRunning) return;
            if (tracker.StartWasStopped || tracker.LastError == null) _webcamLoadingSplash.CloseSplash();
            else _webcamLoadingSplash.ShowErrorAndClose(tracker.LastError);
        }
    }
}
