using System;
using System.Threading;

namespace ConditioningControlPanel.Core.Services
{
    /// <summary>
    /// Central "a display change is in progress" signal used to briefly quiesce the layered-window
    /// spawn paths (flash images, bubbles, overlay recreation).
    ///
    /// Why: the app keeps many WS_EX_LAYERED / AllowsTransparency top-level windows alive at once, and
    /// each owns a DWM composition surface. When the main window crosses onto a monitor with a different
    /// DPI, WPF synchronously rebuilds every visible window's composition surface (the
    /// HwndTarget.UpdateWindowSettings/OnShowWindow path). If a fresh flash/bubble window is *also*
    /// created in that same window (or overlays are torn down and recreated), the burst of surface
    /// allocation can exhaust desktop-heap / GPU-committed memory — the "Not enough quota is available"
    /// crash — or wedge the render thread (the freeze on monitor move).
    ///
    /// This does NOT stop existing windows from being rebuilt (the UI framework owns that); it just
    /// avoids ADDING new layered surfaces during the volatile window right after a display change.
    /// Callers that skip a spawn simply drop one transient effect — invisible to the user, and
    /// everything resumes ~1s later.
    /// </summary>
    public static class DisplayChangeCoordinator
    {
        // Monotonic tick (ms) until which layered-window spawns should be skipped. Read/written from the
        // UI thread and from spawn callbacks that may run on worker threads, so accessed via Interlocked.
        private static long _quietUntilTick;

        /// <summary>How long to hold spawns after a display change. A DPI transition + surface rebuild
        /// settles well under a second; a drag across monitors fires repeated changes that each extend
        /// the window, so the quiet period naturally covers the whole move.</summary>
        private const long QuietMs = 900;

        /// <summary>
        /// Optional diagnostic sink. Platforms wire this to their logger (e.g. Serilog / Microsoft.Extensions.Logging);
        /// null = silent. Core has no logger of its own.
        /// </summary>
        public static Action<string>? DebugLog { get; set; }

        /// <summary>Note that the display topology / DPI just changed; suppress spawns for a short window.</summary>
        public static void NotifyDisplayChange(string reason)
        {
            Interlocked.Exchange(ref _quietUntilTick, Environment.TickCount64 + QuietMs);
            DebugLog?.Invoke($"[DISPLAY] change ({reason}) — pausing layered-window spawns for {QuietMs}ms");
        }

        /// <summary>True while spawns should be skipped (a display change happened within the last <see cref="QuietMs"/> ms).</summary>
        public static bool SpawnsSuppressed => Environment.TickCount64 < Interlocked.Read(ref _quietUntilTick);
    }
}
