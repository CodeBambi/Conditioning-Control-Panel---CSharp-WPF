using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>WPF App.GetWebcamCalibrationScreen: the tracking monitor picked in Settings > Devices.
    /// "Primary" (the default) follows the system primary; a saved display that is gone falls back to
    /// Primary silently.</summary>
    internal static class WebcamScreen
    {
        /// <summary>Index into <paramref name="screens"/> for the saved name; -1 with no screens.</summary>
        internal static int Pick(IReadOnlyList<(string Name, bool Primary)> screens, string? saved)
        {
            if (screens.Count == 0) return -1;
            int primary = 0;
            for (int i = 0; i < screens.Count; i++) if (screens[i].Primary) { primary = i; break; }
            if (string.IsNullOrEmpty(saved) || string.Equals(saved, "Primary", StringComparison.OrdinalIgnoreCase)) return primary;
            for (int i = 0; i < screens.Count; i++)
                if (string.Equals(screens[i].Name, saved, StringComparison.OrdinalIgnoreCase)) return i;
            return primary;
        }

        /// <summary>The name a screen is saved under (WPF Screen.DeviceName).</summary>
        internal static string NameOf(Screen s, int index) =>
            string.IsNullOrWhiteSpace(s.DisplayName) ? $"Display{index + 1}" : s.DisplayName!;

        internal static Screen? Resolve(Screens? screens)
        {
            try
            {
                if (screens?.All is not { Count: > 0 } all) return null;
                var names = new List<(string, bool)>(all.Count);
                for (int i = 0; i < all.Count; i++) names.Add((NameOf(all[i], i), all[i].IsPrimary));
                int at = Pick(names, CoreSettings.Current.WebcamCalibrationScreen);
                return at < 0 ? null : all[at];
            }
            catch (Exception ex) { Diag.Swallowed(ex); return null; }
        }
    }
}
