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

        /// <summary>
        /// The screens the app's full-screen content should cover right now, per the global
        /// "Show content on" picker (<see cref="Models.AppSettings.GlobalTargetMonitor"/>) and the
        /// multi-monitor toggle behind it. Every service that spawns a fullscreen surface should
        /// ask this instead of reading <c>DualMonitorEnabled</c> raw - reading the flag raw is what
        /// made "Primary only" mean "the Windows primary" and nothing else. Never null, never empty
        /// unless the display set itself is unenumerable.
        /// </summary>
        public static WinScreen[] GetGlobalScreens() => ResolveScreens(MonitorTargetFollowGlobal);

        /// <summary>
        /// The one screen that carries "the main copy" of a piece of content (the video window with
        /// audio, a single message window): the Windows primary when it is in the targeted set,
        /// else the first targeted screen. Null only when nothing can be enumerated.
        /// </summary>
        public static WinScreen? GetPrimaryContentScreen()
        {
            var screens = GetGlobalScreens();
            if (screens.Length == 0) return WinScreen.PrimaryScreen;
            return screens.FirstOrDefault(s => s.Primary) ?? screens[0];
        }

        /// <summary>Warn once per missing index, so an unplugged monitor doesn't spam the log every spawn.</summary>
        private static int _warnedMissingGlobalMonitor = int.MinValue;

        private static WinScreen[] ResolveFollowGlobal(WinScreen[] all)
        {
            var pick = Settings?.Current?.GlobalTargetMonitor ?? MonitorTargetFollowGlobal;

            if (pick == MonitorTargetAll) return all;

            if (pick >= 0)
            {
                if (pick < all.Length)
                {
                    if (_warnedMissingGlobalMonitor == pick) _warnedMissingGlobalMonitor = int.MinValue; // it came back
                    return new[] { all[pick] };
                }

                // The picked monitor is gone (unplugged, or the adapter re-ordered). Fall back to
                // the primary and say so ONCE - the setting is deliberately not rewritten, so
                // plugging the screen back in restores the user's choice with no re-pick.
                if (_warnedMissingGlobalMonitor != pick)
                {
                    _warnedMissingGlobalMonitor = pick;
                    Logger?.Warning("Monitor {Index} from Settings is not connected ({Count} monitor(s) present) - " +
                                    "showing content on the primary until it is back.", pick + 1, all.Length);
                }
                return PrimaryOnly(all);
            }

            if (Settings?.Current?.DualMonitorEnabled == true) return all;
            return PrimaryOnly(all);
        }

        private static WinScreen[] PrimaryOnly(WinScreen[] all)
        {
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
