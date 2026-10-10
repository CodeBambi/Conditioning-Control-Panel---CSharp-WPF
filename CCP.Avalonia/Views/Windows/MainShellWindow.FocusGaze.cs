using System;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF HookFocusGazeService (MainWindow.LabTab.cs:828): the engine follows the tracker, the
        /// saved intent re-arms it, and the camera comes back at boot only for an entitled, consented and
        /// calibrated profile (RestartFocusGazeCameraAtBoot). Closing the shell stands it down.</summary>
        private void InitializeFocusGaze()
        {
            try
            {
                var focus = Platform.GazeFocusHead.Instance;
                focus.Wire();
                Closed += (_, _) => { focus.MasterEnabled = false; focus.Stop(); };
                if (!CoreSettings.Current.FocusGazeEnabled) return;
                focus.MasterEnabled = true;
                RestartFocusGazeCameraAtBoot();
            }
            catch (Exception ex) { Log.Warning(ex, "Focus Gaze: hook failed"); }
        }

        private async void RestartFocusGazeCameraAtBoot()
        {
            try
            {
                var tracker = Platform.WebcamTracker.Instance;
                if (!CoreWebcam.IsAvailable || tracker.IsRunning) return;
                if (!TierGate.RequiresLab(Loc.Get("label_focus_gaze")).Allowed) return;
                if (!WebcamConsent.IsCurrent(CoreSettings.Current)) return;
                if (!CoreSettings.Current.WebcamCalibrated) return;
                if (!await tracker.StartAsync())
                    Log.Information("Focus Gaze: webcam did not start at boot ({Error})", tracker.LastError);
            }
            catch (Exception ex) { Log.Warning(ex, "Focus Gaze: webcam start at boot failed"); }
        }
    }
}
