using System;
using Serilog;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// Do-not-disturb for the Core schedulers: WPF <c>Services/UI/DoNotDisturbGuard.cs</c>'s decision
    /// with the foreground read a head seam instead of user32. Read only when a spawn is due, cached
    /// a second; no provider (or one that throws) reads as "not privileged".
    /// </summary>
    public static class DndGuard
    {
        /// <summary>Head seam: the normalised name of the process owning the foreground window, or "".</summary>
        public static Func<string>? ForegroundProcess { get; set; }

        private const long CacheMs = 1000, LogIntervalMs = 60_000;
        private static long _cacheExpiryTick, _nextLogTick;
        private static string _cached = "";
        private static readonly object Gate = new();

        /// <summary>Cached foreground process name (WPF ForegroundProcessName).</summary>
        public static string ForegroundProcessName()
        {
            lock (Gate)
            {
                var now = Environment.TickCount64;
                if (now < _cacheExpiryTick) return _cached;
                _cacheExpiryTick = now + CacheMs;
                try { _cached = DndProcessList.Normalize(ForegroundProcess?.Invoke()); }
                catch { _cached = ""; }
                return _cached;
            }
        }

        public static bool IsPrivilegedAppForeground()
        {
            try
            {
                var list = CoreSettings.Current.DndProcessList;
                if (list == null || list.Count == 0) return false;   // the common case: no list, no work
                var foreground = ForegroundProcessName();
                if (foreground.Length == 0) return false;
                foreach (var entry in list)
                    if (string.Equals(entry, foreground, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            catch { return false; }
        }

        public static bool ShouldSuppressVideos() => CoreSettings.Current.DndSuppressVideos && IsPrivilegedAppForeground();

        public static bool ShouldSuppressFlashes() => CoreSettings.Current.DndSuppressFlashes && IsPrivilegedAppForeground();

        /// <summary>One line a minute at most, shared by the video and flash paths.</summary>
        public static void LogSuppressionThrottled(string what)
        {
            var now = Environment.TickCount64;
            lock (Gate)
            {
                if (now < _nextLogTick) return;
                _nextLogTick = now + LogIntervalMs;
            }
            Log.Information("[DND] {What} suppressed - do-not-disturb app in foreground ({Process})", what, ForegroundProcessName());
        }

        /// <summary>Test seam: forget the cached foreground read.</summary>
        internal static void ResetCacheForTests()
        {
            lock (Gate) { _cacheExpiryTick = 0; _nextLogTick = 0; _cached = ""; }
        }
    }
}
