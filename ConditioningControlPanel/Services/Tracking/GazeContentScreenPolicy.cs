using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Spawn-time placement clamp for gaze-reactive content where the visual must appear
    /// on the calibrated screen (currently only BlinkTrainer overlay tiling). Baseline
    /// content (flashes, bubbles, etc.) does NOT consult this — it spawns freely and the
    /// gaze read pipeline in GazeFocusService.FindBestTarget filters off-cal-screen
    /// targets at the input side.
    ///
    /// Precedence: an explicit Monitor-dropdown pick (AppSettings.WebcamCalibrationScreen)
    /// beats the monitor recorded inside the calibration blob, so users are not stuck on
    /// whichever screen they happened to calibrate on (ccp-bugs #681).
    /// </summary>
    public static class GazeContentScreenPolicy
    {
        public static System.Windows.Forms.Screen[] ResolveGazeReactiveScreens(AppSettings? settings)
        {
            if (settings != null && settings.RestrictGazeContentToCalibratedScreen)
            {
                // The Monitor dropdown is the only user-facing control for this, so an
                // explicit pick wins. Empty / "Primary" is the default sentinel meaning
                // "no explicit pick" — same interpretation as App.GetWebcamCalibrationScreen.
                var picked = settings.WebcamCalibrationScreen;
                if (!string.IsNullOrEmpty(picked)
                    && !string.Equals(picked, "Primary", StringComparison.OrdinalIgnoreCase))
                {
                    var chosen = App.GetWebcamCalibrationScreen();
                    if (chosen != null) return new[] { chosen };
                }

                var cal = App.Webcam?.GetCalibratedScreen();
                if (cal != null) return new[] { cal };
            }
            return settings != null && settings.DualMonitorEnabled
                ? App.GetAllScreensCached()
                : new[] { System.Windows.Forms.Screen.PrimaryScreen! };
        }
    }
}
