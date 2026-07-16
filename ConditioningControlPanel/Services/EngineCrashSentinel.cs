using System;
using System.IO;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Dirty-shutdown detector for normal engine sessions (Start button) — the counterpart of
    /// <see cref="Chaos.ChaosCrashSentinel"/> for everything outside Rabbit Hole runs.
    ///
    /// A native process death (heap corruption / OOM / access violation) bypasses every managed
    /// handler: nothing lands in crash.log, the app log just stops, and users report "the app
    /// vanished" or "a memory leak". The 2026-07-12 leak hunt proved exactly that shape for
    /// flash bursts (0xc0000374 in a decode storm). This flag file is armed while the engine is
    /// running and cleared on clean stop/shutdown, so a surviving file at the next launch turns
    /// an invisible vanish into one diagnostic line with the last-known session context.
    /// </summary>
    internal static class EngineCrashSentinel
    {
        private static string FilePath
        {
            get
            {
                // Mirror the app log location (%LOCALAPPDATA%/ConditioningControlPanel/logs).
                string dir = Path.Combine(App.UserDataPath, "logs");
                return Path.Combine(dir, "engine_session.active");
            }
        }

        /// <summary>Arm/refresh the sentinel with the latest session context (idempotent overwrite).</summary>
        public static void Mark(string contextLine)
        {
            try
            {
                string path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, contextLine ?? "");
            }
            catch { /* diagnostics must never throw into a session */ }
        }

        /// <summary>Clear the sentinel — the engine stopped (or the app is shutting down) cleanly.</summary>
        public static void Clear()
        {
            try
            {
                string path = FilePath;
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        /// <summary>
        /// Called once at startup. If a sentinel survived from a prior session, the engine was
        /// running when the process last died without a clean stop — log it loudly and consume
        /// the file so it only reports once.
        /// </summary>
        public static void ConsumeAndReport(Serilog.ILogger? logger)
        {
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return;
                string ctx = "";
                DateTime armed = DateTime.MinValue;
                try { ctx = File.ReadAllText(path).Trim(); } catch { }
                try { armed = File.GetLastWriteTime(path); } catch { }
                logger?.Warning(
                    "[ENGINECRASH] DETECTED: previous session ended abnormally while the engine was running " +
                    "(no clean stop — likely a native crash/OOM, which leaves nothing in crash.log). " +
                    "Sentinel armed {Armed}. Last context: {Context}",
                    armed == DateTime.MinValue ? "(unknown)" : armed.ToString("yyyy-MM-dd HH:mm:ss"),
                    string.IsNullOrWhiteSpace(ctx) ? "(unavailable)" : ctx);
                Clear();
            }
            catch { }
        }
    }
}
