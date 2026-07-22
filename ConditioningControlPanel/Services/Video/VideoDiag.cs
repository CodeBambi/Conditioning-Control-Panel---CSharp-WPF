using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Flush-on-write diagnostic trace for the mandatory-video show / self-heal path.
    ///
    /// WHY THIS EXISTS (#616/#617/#621/#622/#623 — v6.5.0 "fullscreen black or white screen,
    /// app frozen, panic key does nothing, had to hard-reset the PC"):
    ///   * crash.log is EMPTY in all five reports, so nothing threw — it is a HANG.
    ///   * The opt-in activity log attached to each report only contains app-startup lines, so we
    ///     have zero data from the freeze window itself. Two independent reasons for that, both
    ///     addressed here:
    ///       1. The report attaches the tail of the ROLLING Serilog file. A user who hard-resets
    ///          their PC has to relaunch the app to file the report — and the relaunch writes
    ///          hundreds of startup lines, pushing the whole freeze window out of the 100-line
    ///          tail. The freeze evidence was collected and then scrolled off.
    ///       2. A hard power reset (not a clean kill) can roll a buffered file write back on NTFS.
    ///          Serilog's file sink buffers and its flushToDiskInterval only flushes once a second.
    ///     This writer uses its OWN small file, so the relaunch cannot scroll the freeze out of it,
    ///     and every line is written with WriteThrough + FlushFileBuffers so it is on the platter
    ///     before the call returns — a yanked power cord cannot take the last lines with it.
    ///
    /// THREADING CONTRACT (this is the part that must not regress the very hang it diagnoses):
    ///   * <see cref="Log"/> only timestamps a string and enqueues it. It NEVER touches the disk,
    ///     never takes a lock that a writer holds across I/O, and never blocks. It is safe to call
    ///     from the UI thread, from a LibVLC native callback thread, and from a threadpool timer.
    ///   * A single dedicated background thread owns the file handle and does all I/O. If the UI
    ///     thread wedges, that thread keeps draining and the trace keeps landing on disk — which is
    ///     the whole point: the last line before the silence tells us where the dispatcher died.
    ///   * The queue is bounded. If the writer somehow can't keep up we drop lines rather than grow
    ///     memory during a freeze.
    ///
    /// UI HEARTBEAT: a second background thread posts a stamp to the dispatcher every second and
    /// records when it last came back. That gives the next bug report the exact second the UI
    /// thread stopped running, and (unlike <see cref="UiHangWatchdog"/>, which only fires once at
    /// 10s and writes a minidump) it logs the whole stall timeline including the recovery.
    /// </summary>
    public static class VideoDiag
    {
        private const int MaxQueued = 4000;          // hard cap; drop rather than balloon during a freeze
        private const long MaxFileBytes = 1_000_000; // ~1MB, then roll to .1 (two files kept, ever)

        private static readonly ConcurrentQueue<string> _queue = new();
        private static readonly AutoResetEvent _signal = new(false);
        private static int _started;
        private static int _dropped;
        private static string _path = "";

        // UI heartbeat state. _lastUiBeatTicks is stamped ON the dispatcher, so its age is exactly
        // "how long since the UI thread last ran anything at Normal priority".
        private static long _lastUiBeatTicks;
        private static volatile bool _uiBeatSeen;

        /// <summary>Milliseconds since the UI thread last ran a posted callback (0 if never started).</summary>
        public static long UiStallMs
        {
            get
            {
                var last = Interlocked.Read(ref _lastUiBeatTicks);
                if (last == 0) return 0;
                return (DateTime.UtcNow.Ticks - last) / TimeSpan.TicksPerMillisecond;
            }
        }

        /// <summary>
        /// Start the writer + UI heartbeat. Called once from App.OnStartup, next to the
        /// UiHangWatchdog. Idempotent; never throws.
        /// </summary>
        public static void Start(Dispatcher dispatcher)
        {
            if (Interlocked.Exchange(ref _started, 1) == 1) return;
            try
            {
                var dir = Path.Combine(App.UserDataPath, "logs");
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "video-diag.log");
                RollIfNeeded();
            }
            catch { /* diagnostics must never take the app down */ }

            var writer = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "VideoDiagWriter",
                Priority = ThreadPriority.BelowNormal,
            };
            writer.Start();

            var beat = new Thread(() => HeartbeatLoop(dispatcher))
            {
                IsBackground = true,
                Name = "VideoDiagHeartbeat",
                Priority = ThreadPriority.BelowNormal,
            };
            beat.Start();

            Log("SESSION", $"---- app start, v{UpdateService.AppVersion}, pid {Environment.ProcessId} ----");
        }

        /// <summary>
        /// Record one trace line. Enqueue-only: safe from ANY thread including the UI thread and
        /// LibVLC's native callback threads. <paramref name="tag"/> is a short routing key
        /// (VIDEO, VOUT, WEDGE, HEAL, CLOSE, PANIC, UI).
        /// </summary>
        public static void Log(string tag, string message)
        {
            if (_started == 0) return;
            try
            {
                if (_queue.Count >= MaxQueued)
                {
                    Interlocked.Increment(ref _dropped);
                    return;
                }
                // Thread id + UI-stall age on every line: when the trace stops, the last line's
                // thread tells us WHO was running, and the stall column shows whether the UI thread
                // was already gone before that point.
                var line = string.Concat(
                    DateTime.Now.ToString("HH:mm:ss.fff"),
                    " [t", Environment.CurrentManagedThreadId.ToString(), "]",
                    _uiBeatSeen ? " (ui+" + UiStallMs.ToString() + "ms)" : "",
                    " ", tag, ": ", message);
                _queue.Enqueue(line);
                _signal.Set();
            }
            catch { }
        }

        /// <summary>Last <paramref name="maxLines"/> lines of the trace, for the bug report attachment.</summary>
        public static string Tail(int maxLines)
        {
            try
            {
                if (string.IsNullOrEmpty(_path) || !File.Exists(_path)) return string.Empty;
                using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                var buf = new List<string>();
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    buf.Add(line);
                    if (buf.Count > maxLines * 3) buf.RemoveRange(0, buf.Count - maxLines);
                }
                int start = Math.Max(0, buf.Count - maxLines);
                return string.Join(Environment.NewLine, buf.GetRange(start, buf.Count - start));
            }
            catch { return string.Empty; }
        }

        private static void WriterLoop()
        {
            while (true)
            {
                try
                {
                    _signal.WaitOne(1000);
                    if (_queue.IsEmpty) continue;
                    if (string.IsNullOrEmpty(_path)) { while (_queue.TryDequeue(out _)) { } continue; }

                    // WriteThrough + Flush(true) => the bytes are past the OS cache when we return.
                    // Opened per drain rather than held open so an external reader (the bug-report
                    // tail, or the user zipping their logs mid-freeze) always sees a complete file.
                    using var fs = new FileStream(_path, FileMode.Append, FileAccess.Write,
                        FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.WriteThrough);
                    using var sw = new StreamWriter(fs, Encoding.UTF8);
                    while (_queue.TryDequeue(out var line))
                        sw.WriteLine(line);
                    int dropped = Interlocked.Exchange(ref _dropped, 0);
                    if (dropped > 0) sw.WriteLine($"(diag queue overflow — {dropped} line(s) dropped)");
                    sw.Flush();
                    fs.Flush(true);
                    RollIfNeeded();
                }
                catch
                {
                    // Disk full, AV lock, folder deleted — drop this batch and keep the app alive.
                    try { while (_queue.Count > MaxQueued / 2 && _queue.TryDequeue(out _)) { } } catch { }
                }
            }
        }

        private static void RollIfNeeded()
        {
            try
            {
                if (string.IsNullOrEmpty(_path) || !File.Exists(_path)) return;
                if (new FileInfo(_path).Length < MaxFileBytes) return;
                var prev = _path + ".1";
                try { if (File.Exists(prev)) File.Delete(prev); } catch { }
                File.Move(_path, prev);
            }
            catch { }
        }

        /// <summary>
        /// Posts a stamp to the dispatcher every second and reports the stall timeline. Escalating
        /// thresholds keep the file quiet during normal use (one line per minute) while giving a
        /// second-resolution picture of a freeze: when it began, how deep it got, whether it ever
        /// came back.
        /// </summary>
        private static void HeartbeatLoop(Dispatcher dispatcher)
        {
            long lastReported = 0;
            long aliveMarkerTicks = 0;

            // Escalating stall thresholds (ms). One line each, so a 10-minute freeze costs 6 lines.
            var thresholds = new long[] { 3_000, 10_000, 30_000, 60_000, 300_000, 900_000 };

            Interlocked.Exchange(ref _lastUiBeatTicks, DateTime.UtcNow.Ticks);

            while (true)
            {
                try
                {
                    Thread.Sleep(1000);
                    if (dispatcher.HasShutdownStarted) return;

                    dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                    {
                        Interlocked.Exchange(ref _lastUiBeatTicks, DateTime.UtcNow.Ticks);
                        _uiBeatSeen = true;
                    }));

                    long stall = UiStallMs;

                    if (stall < 2000)
                    {
                        if (lastReported > 0)
                        {
                            Log("UI", $"dispatcher RECOVERED after {lastReported}ms+ of silence");
                            lastReported = 0;
                        }
                        // Low-rate "still alive" marker so a trace that simply ends tells us the
                        // process died, while a trace whose markers stop tells us the UI thread did.
                        if (DateTime.UtcNow.Ticks - aliveMarkerTicks > TimeSpan.TicksPerMinute)
                        {
                            aliveMarkerTicks = DateTime.UtcNow.Ticks;
                            Log("UI", "dispatcher alive");
                        }
                        continue;
                    }

                    foreach (var t in thresholds)
                    {
                        if (stall >= t && lastReported < t)
                        {
                            lastReported = t;
                            Log("UI", $"dispatcher UNRESPONSIVE for {stall}ms (no posted callback has run)");
                            break;
                        }
                    }
                }
                catch { }
            }
        }
    }
}
