using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Centralized "which screens should gaze-reactive content appear on?"
    /// policy. All gaze-reactive spawn sites (Bubble Pop, Blink Trainer,
    /// Flash, etc.) route through here so the calibration clamp is applied
    /// uniformly. Behavior under each combination:
    ///
    ///   RestrictGazeContentToCalibratedScreen = true  + calibration loaded
    ///     → pin to calibrated screen, overrides DualMonitorEnabled
    ///   RestrictGazeContentToCalibratedScreen = true  + no calibration
    ///     → fall through to DualMonitorEnabled logic (no restriction)
    ///   RestrictGazeContentToCalibratedScreen = false
    ///     → fall through to DualMonitorEnabled logic (no restriction)
    ///
    /// The "no calibration → no restriction" branch is the uniform fail-open
    /// behavior — call sites do not need to handle calibration-missing
    /// themselves.
    /// </summary>
    public static class GazeContentScreenPolicy
    {
        public static System.Windows.Forms.Screen[] ResolveScreens(AppSettings? settings)
        {
            if (settings != null && settings.RestrictGazeContentToCalibratedScreen)
            {
                var cal = App.Webcam?.GetCalibratedScreen();
                if (cal != null) return new[] { cal };
            }
            return settings != null && settings.DualMonitorEnabled
                ? App.GetAllScreensCached()
                : new[] { System.Windows.Forms.Screen.PrimaryScreen! };
        }
    }
}
