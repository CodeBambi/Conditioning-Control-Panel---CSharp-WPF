using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;


namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// "Do not disturb over my own media player." The user names the apps they watch things in
    /// (VLC, mpv, PotPlayer, ...) and while one of those is the FOREGROUND window the app stops
    /// scheduling its own mandatory videos over the top of it — optionally flashes too.
    ///
    /// <para><b>Why this is not the awareness observer.</b> <c>AwarenessObserver</c> already polls
    /// the foreground window and already has a "DND" of its own, but that one decides whether the
    /// COMPANION may speak, and the whole observer is entitlement-gated to premium
    /// (<c>AwarenessObserver.HasEntitlement</c>, tier 1). Not popping a video over the film someone
    /// is watching is not a premium behaviour — it is basic manners — so this reads the foreground
    /// itself with the cheapest possible call pair rather than borrowing a gated poller. It is also
    /// not a second POLLER: nothing here ticks. The read happens lazily, only when a spawn is about
    /// to fire, and the answer is cached for <see cref="CacheMs"/> so a burst of spawn checks costs
    /// one Win32 round trip.</para>
    ///
    /// <para><b>Cost.</b> On a cache miss: <c>GetForegroundWindow</c> +
    /// <c>GetWindowThreadProcessId</c> (two cheap user32 calls, no allocation) and, only when the
    /// owning pid actually changed, one <c>Process.GetProcessById</c>. Staying in one app — which is
    /// the case this feature exists for — costs the two user32 calls per second and nothing else.
    /// On a cache hit: one <c>Environment.TickCount64</c> compare.</para>
    ///
    /// <para><b>What it deliberately does not do.</b> It never interrupts a video that is already
    /// playing. A privileged app coming to the front mid-video is the user alt-tabbing away from
    /// something they already committed to; tearing it down there would be a worse surprise than
    /// letting it finish. Only the SPAWN is suppressed.</para>
    /// </summary>
    public static class DoNotDisturbGuard
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        /// <summary>How long a foreground read stays good. A second is far shorter than any spawn
        /// interval in the app, so the answer is never stale in a way the user could notice, and it
        /// collapses the spawn-check burst that a session tick can produce into one real read.</summary>
        private const long CacheMs = 1000;

        /// <summary>Debug logging budget. The scheduler paths ask this question on every tick; at one
        /// line per tick a 20-minute film would write hundreds of identical lines. One a minute is
        /// enough to see in a log that DND was the reason nothing fired.</summary>
        private const long LogIntervalMs = 60_000;

        private static long _cacheExpiryTick;
        private static string _cachedProcess = "";
        private static uint _cachedPid;
        private static long _nextLogTick;
        private static readonly object Gate = new();

        /// <summary>
        /// Lower-cased, extension-less name of the process owning the foreground window, or "" when
        /// it cannot be determined (no foreground window, a protected process, an exit between the
        /// handle read and the lookup). Cached for <see cref="CacheMs"/>.
        /// </summary>
        public static string ForegroundProcessName()
        {
            lock (Gate)
            {
                var now = Environment.TickCount64;
                if (now < _cacheExpiryTick) return _cachedProcess;
                _cacheExpiryTick = now + CacheMs;

                try
                {
                    var hwnd = GetForegroundWindow();
                    if (hwnd == IntPtr.Zero) { _cachedProcess = ""; return ""; }

                    GetWindowThreadProcessId(hwnd, out uint pid);
                    if (pid == 0) { _cachedProcess = ""; return ""; }

                    // The expensive call is GetProcessById, and the foreground pid rarely changes
                    // between reads — skip it whenever the window still belongs to the same process.
                    if (pid == _cachedPid && _cachedProcess.Length > 0) return _cachedProcess;

                    using var process = Process.GetProcessById((int)pid);
                    _cachedPid = pid;
                    _cachedProcess = Normalize(process.ProcessName);
                    return _cachedProcess;
                }
                catch
                {
                    // A foreground read that throws is never a crash and never a suppression: an
                    // unknown foreground reads as "not privileged", so spawns behave as they always did.
                    _cachedPid = 0;
                    _cachedProcess = "";
                    return "";
                }
            }
        }

        /// <summary>True when the foreground window belongs to one of the user's do-not-disturb apps.</summary>
        public static bool IsPrivilegedAppForeground()
        {
            try
            {
                var list = App.Settings?.Current?.DndProcessList;
                if (list == null || list.Count == 0) return false;   // the common case: no list, no work

                var foreground = ForegroundProcessName();
                if (foreground.Length == 0) return false;

                foreach (var entry in list)
                {
                    if (string.Equals(entry, foreground, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// True when a scheduled mandatory video must not open right now. Callers reschedule rather
        /// than drop, exactly like the browser-media and cleanup skips beside them.
        /// </summary>
        public static bool ShouldSuppressVideos()
        {
            try
            {
                if (App.Settings?.Current?.DndSuppressVideos != true) return false;
                return IsPrivilegedAppForeground();
            }
            catch { return false; }
        }

        /// <summary>True when an ambient flash must not open right now (opt-in; off by default).</summary>
        public static bool ShouldSuppressFlashes()
        {
            try
            {
                if (App.Settings?.Current?.DndSuppressFlashes != true) return false;
                return IsPrivilegedAppForeground();
            }
            catch { return false; }
        }

        /// <summary>
        /// Writes one Debug line at most once per <see cref="LogIntervalMs"/>, whatever the caller.
        /// Shared across the video and flash paths on purpose — a user watching a film with both
        /// suppressors on should see "DND" in the log, not two interleaved streams of it.
        /// </summary>
        public static void LogSuppressionThrottled(string what)
        {
            try
            {
                var now = Environment.TickCount64;
                lock (Gate)
                {
                    if (now < _nextLogTick) return;
                    _nextLogTick = now + LogIntervalMs;
                }
                App.Logger?.Information("[DND] {What} suppressed - do-not-disturb app in foreground ({Process})",
                    what, ForegroundProcessName());
            }
            catch { /* logging must never be the thing that breaks a spawn path */ }
        }

        /// <summary>
        /// One list entry, cleaned: trimmed, lower-cased, a trailing ".exe" removed, surrounding
        /// quotes dropped. "VLC.exe" and " vlc " are the same app and must compare equal.
        /// </summary>
        public static string Normalize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var name = raw.Trim().Trim('"').Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Parses the settings textbox into the stored list. Accepts one process per line, commas,
        /// semicolons or any mix of the three, because users will type all of them. Entries are
        /// <see cref="Normalize"/>d, blanks dropped and duplicates collapsed, order preserved so the
        /// box reads back the way it was typed.
        /// </summary>
        public static List<string> ParseProcessList(string? raw)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var piece in raw.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = Normalize(piece);
                if (name.Length == 0) continue;
                if (seen.Add(name)) result.Add(name);
            }
            return result;
        }

        /// <summary>Renders the stored list back into the textbox, one process per line.</summary>
        public static string FormatProcessList(IEnumerable<string>? list)
            => list == null ? "" : string.Join(Environment.NewLine, list);

        /// <summary>
        /// Process names of everything that currently owns a visible top-level window, de-duplicated
        /// and sorted, with the app's own process removed — the picker's source, so nobody has to
        /// know that PotPlayer's executable is called <c>PotPlayerMini64</c>.
        /// </summary>
        public static List<string> RunningWindowedProcesses()
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            string self;
            try { self = Normalize(Process.GetCurrentProcess().ProcessName); }
            catch { self = ""; }

            Process[] processes;
            try { processes = Process.GetProcesses(); }
            catch (Exception ex)
            {
                App.Logger?.Debug("[DND] process enumeration failed - {Error}", ex.Message);
                return new List<string>();
            }

            foreach (var p in processes)
            {
                try
                {
                    // MainWindowHandle is 0 for every service and background task, which is most of
                    // the list. What is left is roughly what shows in the taskbar.
                    if (p.MainWindowHandle == IntPtr.Zero) continue;
                    var name = Normalize(p.ProcessName);
                    if (name.Length == 0 || name == self) continue;
                    names.Add(name);
                }
                catch { /* protected or exited between enumeration and read */ }
                finally { try { p.Dispose(); } catch { } }
            }

            return new List<string>(names);
        }

        /// <summary>Test seam: forget the cached foreground read.</summary>
        internal static void ResetCacheForTests()
        {
            lock (Gate)
            {
                _cacheExpiryTick = 0;
                _cachedPid = 0;
                _cachedProcess = "";
                _nextLogTick = 0;
            }
        }
    }
}
