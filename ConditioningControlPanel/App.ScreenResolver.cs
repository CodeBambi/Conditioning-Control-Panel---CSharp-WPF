using System;
using System.Linq;
using WinScreen = System.Windows.Forms.Screen;

namespace ConditioningControlPanel
{
    public partial class App
    {
        // Sentinel values for the per-effect monitor target settings (suggestion #639).
        /// <summary>Follow the global <see cref="Models.AppSettings.DualMonitorEnabled"/> behavior (default).</summary>
        public const int MonitorTargetFollowGlobal = -1;
        /// <summary>Render on every connected monitor regardless of DualMonitorEnabled.</summary>
        public const int MonitorTargetAll = -2;

        /// <summary>
        /// Resolve a per-effect monitor target (see <see cref="Models.AppSettings.SpiralTargetMonitor"/>) to the
        /// set of screens the effect should render on. Semantics:
        /// <list type="bullet">
        ///   <item>-1 (<see cref="MonitorTargetFollowGlobal"/>): legacy behavior — all screens when
        ///     <see cref="Models.AppSettings.DualMonitorEnabled"/> is on, else just the primary.</item>
        ///   <item>-2 (<see cref="MonitorTargetAll"/>): every connected monitor.</item>
        ///   <item>0..N: the single monitor at that index in <see cref="WinScreen.AllScreens"/>. If the index is
        ///     out of range (the monitor was disconnected), falls back to the -1 behavior — the saved setting is
        ///     never mutated, so the target survives a reconnect.</item>
        /// </list>
        /// Guards the documented transient-empty enumeration hazard (CLAUDE.md known issue #5): when the cache is
        /// momentarily empty it returns the primary screen (or an empty array only if even that is unavailable),
        /// never throwing. Never returns null.
        /// </summary>
        public static WinScreen[] ResolveScreens(int target)
        {
            try
            {
                var all = GetAllScreensCached();
                if (all.Length == 0)
                {
                    // Transient empty enumeration (display transition). Err toward showing on primary.
                    var primary = WinScreen.PrimaryScreen;
                    return primary != null ? new[] { primary } : Array.Empty<WinScreen>();
                }

                switch (target)
                {
                    case MonitorTargetAll:
                        return all;

                    case MonitorTargetFollowGlobal:
                        return ResolveFollowGlobal(all);

                    default:
                        // Specific monitor index. Out-of-range (unplugged monitor, or a nonsense negative
                        // that isn't a sentinel) falls back to the -1 behavior without mutating the setting.
                        if (target >= 0 && target < all.Length)
                            return new[] { all[target] };
                        return ResolveFollowGlobal(all);
                }
            }
            catch (Exception ex)
            {
                Logger?.Debug("ResolveScreens({Target}) failed: {Error}", target, ex.Message);
                var primary = WinScreen.PrimaryScreen;
                return primary != null ? new[] { primary } : Array.Empty<WinScreen>();
            }
        }

        private static WinScreen[] ResolveFollowGlobal(WinScreen[] all)
        {
            if (Settings?.Current?.DualMonitorEnabled == true) return all;
            var primary = WinScreen.PrimaryScreen ?? all.FirstOrDefault(s => s.Primary) ?? all[0];
            return new[] { primary };
        }

        /// <summary>
        /// Per-monitor render predicate for the shared compositor hosts (the compositor hosts EVERY screen; a
        /// fill layer opts a monitor out here). True when the monitor whose device-pixel bounds are
        /// <paramref name="screenBoundsPx"/> is in the resolved set for <paramref name="target"/>. Compared by
        /// bounds origin so a mixed-DPI width/height rounding difference can't misidentify the screen. Errs
        /// toward showing on any enumeration hiccup rather than blanking the effect.
        /// </summary>
        public static bool ShouldRenderTargetOnScreen(int target, System.Drawing.Rectangle screenBoundsPx)
        {
            try
            {
                var screens = ResolveScreens(target);
                if (screens.Length == 0) return true; // couldn't resolve: err toward showing
                foreach (var s in screens)
                {
                    if (screenBoundsPx.X == s.Bounds.X && screenBoundsPx.Y == s.Bounds.Y)
                        return true;
                }
                return false;
            }
            catch { return true; }
        }
    }
}
