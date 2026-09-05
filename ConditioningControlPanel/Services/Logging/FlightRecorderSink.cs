using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Serilog.Core;
using Serilog.Events;

namespace ConditioningControlPanel.Services.Logging
{
    /// <summary>
    /// Keeps the last 4,096 log events in memory and writes them out when something goes wrong.
    ///
    /// <para>The app makes 2,927 Debug calls that have never once reached a disk, because the floor
    /// has been Information since someone (rightly, at the time) worried about what Debug would put
    /// in a file. So every report of a freeze or a black video arrives with the one level of detail
    /// that would have explained it already discarded - and turning Debug on for everyone would
    /// trade that for a log nobody can read and a privacy problem.</para>
    ///
    /// <para>This is the third option: Debug is enabled, but only into a ring buffer in memory. It
    /// costs a slot in an array per event and touches no disk. When an Error fires, the UI hangs,
    /// the app crashes, or the user files a bug report, the ring is written to
    /// <c>logs/diag-*.log</c> - so the minutes BEFORE the failure are in the report, which is the
    /// half that was always missing. Values are already redacted by the enricher upstream.</para>
    /// </summary>
    public sealed class FlightRecorderSink : ILogEventSink
    {
        public const int Capacity = 4096;
        public const int KeepDumps = 5;

        /// <summary>At most one automatic dump a minute: a crash loop must not become a disk loop.</summary>
        private const int AutoDumpCooldownMs = 60_000;

        private readonly LogEvent?[] _ring = new LogEvent?[Capacity];
        private readonly object _gate = new();
        private readonly string _logsDir;
        private readonly string _sessionId;
        private int _next;
        private long _lastAutoDump = -AutoDumpCooldownMs;

        /// <summary>The live recorder, or null before the pipeline is built (very early startup).</summary>
        public static FlightRecorderSink? Instance { get; internal set; }

        public FlightRecorderSink(string logsDir, string sessionId)
        {
            _logsDir = logsDir;
            _sessionId = sessionId;
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null) return;
            bool autoDump = false;
            try
            {
                lock (_gate)
                {
                    _ring[_next] = logEvent;
                    _next = (_next + 1) % Capacity;

                    if (logEvent.Level >= LogEventLevel.Error && !IsShutdownCancellation(logEvent))
                    {
                        long now = Environment.TickCount64;
                        if (now - _lastAutoDump >= AutoDumpCooldownMs)
                        {
                            _lastAutoDump = now;
                            autoDump = true;
                        }
                    }
                }
            }
            catch { /* swallow: never throw out of a sink */ }

            // Outside the lock: writing 4,096 lines must not block the thread that is logging.
            if (autoDump) Dump("error");
        }

        /// <summary>
        /// A cancellation thrown while the app is closing is the normal shape of shutdown in this
        /// codebase (HTTP calls in flight, timers being torn down). Dumping the ring for those
        /// would mean a diag file on every clean exit, which buries the ones that mean something.
        /// </summary>
        private static bool IsShutdownCancellation(LogEvent e) =>
            e.Exception is OperationCanceledException && ShuttingDown();

        /// <summary>
        /// "Is the app closing?" A seam, because the honest answer lives in a WPF dispatcher that a
        /// test host either does not have or shares with another suite's harness window.
        /// </summary>
        internal static Func<bool> ShuttingDown { get; set; } = DefaultShuttingDown;

        private static bool DefaultShuttingDown()
        {
            try
            {
                var app = System.Windows.Application.Current;
                return app == null || app.Dispatcher == null || app.Dispatcher.HasShutdownStarted;
            }
            catch
            {
                return true; // swallow: if we cannot tell, assume shutdown and stay quiet
            }
        }

        /// <summary>Dump the live recorder if there is one. Safe to call from anywhere.</summary>
        public static string? DumpIfActive(string reason)
        {
            try { return Instance?.Dump(reason); }
            catch { return null; /* swallow: a diagnostic must never break the thing it diagnoses */ }
        }

        /// <summary>
        /// Write the ring, oldest first, to <c>logs/diag-yyyyMMdd-HHmmss-reason.log</c>. Returns the
        /// path, or null if nothing could be written.
        /// </summary>
        public string? Dump(string reason)
        {
            try
            {
                var events = Snapshot();
                if (events.Count == 0) return null;

                Directory.CreateDirectory(_logsDir);
                var safeReason = Sanitise(reason);
                var path = Path.Combine(_logsDir,
                    $"diag-{DateTime.Now:yyyyMMdd-HHmmss}-{safeReason}.log");

                var formatter = new CcpLineFormatter();
                var sb = new StringBuilder(64 * 1024);
                sb.Append("== CCP diag dump reason=").Append(safeReason)
                  .Append(" v").Append(SafeVersion())
                  .Append(" session ").Append(_sessionId)
                  .Append(' ').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"))
                  .Append(" events=").Append(events.Count).AppendLine(" ==");

                using (var sw = new StringWriter(sb))
                {
                    foreach (var e in events)
                    {
                        try { formatter.Format(e, sw); }
                        catch { /* swallow: one bad event must not cost the whole dump */ }
                    }
                }

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                Prune();
                return path;
            }
            catch
            {
                return null; // swallow: best effort by design
            }
        }

        /// <summary>Newest diag file, or null. Used by the bug reporter.</summary>
        public static string? NewestDump(string logsDir)
        {
            try
            {
                var files = Directory.GetFiles(logsDir, "diag-*.log");
                if (files.Length == 0) return null;
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                return files[^1];
            }
            catch { return null; /* swallow */ }
        }

        private List<LogEvent> Snapshot()
        {
            var list = new List<LogEvent>(Capacity);
            lock (_gate)
            {
                // Oldest first: start at the write cursor and wrap.
                for (int i = 0; i < Capacity; i++)
                {
                    var e = _ring[(_next + i) % Capacity];
                    if (e != null) list.Add(e);
                }
            }
            return list;
        }

        /// <summary>Keep the newest <see cref="KeepDumps"/>. A hang that repeats would otherwise
        /// fill the logs folder with dumps of the same minute.</summary>
        private void Prune()
        {
            try
            {
                var files = Directory.GetFiles(_logsDir, "diag-*.log");
                if (files.Length <= KeepDumps) return;
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Length - KeepDumps; i++)
                {
                    try { File.Delete(files[i]); } catch { /* swallow: locked file, try next time */ }
                }
            }
            catch { /* swallow */ }
        }

        private static string SafeVersion()
        {
            try { return UpdateService.AppVersion ?? "?"; }
            catch { return "?"; }
        }

        private static string Sanitise(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return "manual";
            var sb = new StringBuilder(reason.Length);
            foreach (var c in reason)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-')
                    sb.Append(c);
            }
            return sb.Length == 0 ? "manual" : sb.ToString();
        }
    }
}
