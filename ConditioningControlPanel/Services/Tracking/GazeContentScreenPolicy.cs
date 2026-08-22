using System;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Spawn-time placement for gaze-adjacent content that needs a whole-screen surface
    /// (currently only BlinkTrainer overlay tiling). Baseline content (flashes, bubbles,
    /// etc.) does NOT consult this — it spawns freely and the gaze read pipeline in
    /// GazeFocusService.FindBestTarget filters off-cal-screen targets at the input side.
    ///
    /// Precedence (ccp-bugs #681, #979):
    /// <list type="number">
    ///   <item>An explicit "Tracking monitor" pick (AppSettings.WebcamCalibrationScreen naming a
    ///     real display) wins outright — it is the only user-facing control for this.</item>
    ///   <item>Otherwise the setting holds the "Primary" sentinel (the default), which means
    ///     exactly what it says: follow the app-wide placement convention —
    ///     DualMonitorEnabled ? every connected monitor : the Windows primary. Same rule
    ///     FlashService / VideoService / BubbleCountWindow / App.ResolveScreens use.</item>
    /// </list>
    ///
    /// What this deliberately no longer does (#979): pin the overlay to whichever monitor the
    /// webcam calibration blob was recorded on. Blink detection is monitor-independent — it
    /// reads the camera, not the screen — so the Blink Trainer never needed the calibrated
    /// screen. Pinning to it silently stranded the overlay on a display the user never chose
    /// (calibrated before making another display "main", calibrated with the dropdown pointed
    /// elsewhere, or an inherited older calibration) and made the "Primary" sentinel a lie.
    /// Gaze features that genuinely need the calibrated screen still clamp at the input side
    /// via GazeFocusService / WebcamTrackingService.GetCalibratedScreen.
    /// </summary>
    public static class GazeContentScreenPolicy
    {
        public static System.Windows.Forms.Screen[] ResolveGazeReactiveScreens(AppSettings? settings)
        {
            // An explicit monitor pick beats everything. Empty / "Primary" is the default
            // sentinel meaning "no explicit pick" — same interpretation as
            // App.GetWebcamCalibrationScreen.
            var picked = settings?.WebcamCalibrationScreen;
            if (!string.IsNullOrEmpty(picked)
                && !string.Equals(picked, "Primary", StringComparison.OrdinalIgnoreCase))
            {
                var chosen = App.GetWebcamCalibrationScreen();
                if (chosen != null) return new[] { chosen };
            }

            // CLAUDE.md known issue #5: AllScreens can come back empty during display
            // transitions. Never index blind.
            var all = App.GetAllScreensCached();
            if (all.Length == 0)
            {
                var fallback = System.Windows.Forms.Screen.PrimaryScreen;
                return fallback != null
                    ? new[] { fallback }
                    : Array.Empty<System.Windows.Forms.Screen>();
            }

            if (settings?.DualMonitorEnabled == true) return all;

            // Screen.AllScreens is ordered by adapter output (\\.\DISPLAY1 first), which is
            // NOT the user's "main display" — never take all[0] as the primary.
            var primary = System.Windows.Forms.Screen.PrimaryScreen
                ?? all.FirstOrDefault(s => s.Primary)
                ?? all[0];
            return new[] { primary };
        }
    }
}
