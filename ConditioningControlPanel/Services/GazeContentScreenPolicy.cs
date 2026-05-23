using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Spawn-time placement clamp for gaze-reactive content where the visual must appear
    /// on the calibrated screen (currently only BlinkTrainer overlay tiling). Baseline
    /// content (flashes, bubbles, etc.) does NOT consult this — it spawns freely and the
    /// gaze read pipeline in GazeFocusService.FindBestTarget filters off-cal-screen
    /// targets at the input side.
    /// </summary>
    public static class GazeContentScreenPolicy
    {
        public static System.Windows.Forms.Screen[] ResolveGazeReactiveScreens(AppSettings? settings)
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
