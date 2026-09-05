using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Win32.SafeHandles;

namespace ConditioningControlPanel.Services;

/// <summary>
/// UI-thread hang detector. The app's freezes report as Application Hang 1002 with no
/// crash.log entry (a render-thread deadlock blocks the dispatcher without throwing), so
/// post-mortem there is nothing to debug. A background thread posts a heartbeat to the
/// dispatcher every 2s; if no heartbeat lands for 10s the process is presumed wedged and
/// ONE minidump per session is written next to the logs (hang_*.dmp — open in VS/WinDbg
/// for all thread stacks, managed included). The hang and any later recovery are also
/// logged, which timestamps the freeze exactly even if the dump fails.
///
/// STARTUP PHASE: OnStartup runs the whole service init synchronously on the UI thread,
/// so the dispatcher cannot pump a single heartbeat until startup completes — that
/// silence is BY DESIGN, not a hang. Until the first heartbeat ever lands, a much longer
/// threshold applies. Without this, every cold first launch that took >13s (cold disk
/// cache, AV rescan after an update) fired the watchdog mid-init and the dump write froze
/// the app for minutes — or forever — which WAS the "stuck on splash, relaunch a couple
/// of times" report wave (v6.2.x). The relaunch "worked" because warm caches kept the
/// second start under the threshold.
/// </summary>
public static class UiHangWatchdog
{
    private const int HEARTBEAT_MS = 2000;
    private const int HANG_THRESHOLD_MS = 10000;
    // Before the dispatcher has EVER pumped a heartbeat we are still inside OnStartup's
    // synchronous init: silence is expected. Only a truly extreme stall (a real wedge in
    // MainWindow creation, a dead-forever init step) should fire during this phase.
    private const int STARTUP_HANG_THRESHOLD_MS = 120000;

    private static long _lastBeatTick;
    private static int _firstBeatSeen;       // 0 until the dispatcher pumps its first heartbeat
    private static bool _dumpWritten;
    private static bool _hangLogged;
    private static Thread? _thread;

    // ── USER/GDI resource sampler (bug #627) ───────────────────────────────
    // CreateWindowEx failing with "Not enough memory resources are available to process
    // this command" after a long session is the signature of USER-object / desktop-heap
    // exhaustion, NOT managed memory. The failing call site is always an innocent victim
    // (whatever tried to make the next window), so a post-mortem stack tells us nothing
    // about WHICH subsystem leaked. Sampling the three counters that actually matter every
    // few minutes turns the next long-session report into a straight read: whichever line
    // climbs monotonically over 4h is the leak.
    private const int SAMPLE_EVERY_BEATS = 60;          // 60 × 2s ≈ every 2 minutes
    private const uint GR_GDIOBJECTS = 0;
    private const uint GR_USEROBJECTS = 1;
    private const uint USER_GDI_WARN = 6000;            // per-process default quota is 10,000
    private const uint USER_GDI_CRITICAL = 9000;
    private static int _beatsSinceSample;
    private static uint _firstUser, _firstGdi;
    private static int _firstHandles, _firstThreads;
    // Memory baselines (bytes). #634 "RAM grew to 100%": a RAM leak is invisible in the
    // USER/GDI/handle counters, so sample private bytes, working set and the managed heap too
    // — whichever climbs monotonically over a long session names the leak (native vs managed).
    private static long _firstPriv, _firstWs, _firstGcHeap;
    private const double MB = 1024.0 * 1024.0;
    private static bool _baselineTaken;
    private static bool _resourceAlarmed;

    public static void Start(Dispatcher dispatcher)
    {
        if (_thread != null) return;
        _lastBeatTick = Environment.TickCount64;
        _thread = new Thread(() => Loop(dispatcher))
        {
            IsBackground = true,
            Name = "UiHangWatchdog",
            Priority = ThreadPriority.BelowNormal
        };
        _thread.Start();
    }

    /// <summary>
    /// Which silence budget applies: the ordinary one once the dispatcher has pumped at least one
    /// heartbeat, or the much longer startup budget before that (OnStartup runs the whole service
    /// init synchronously on the UI thread, so pre-first-beat silence is by design — see the type
    /// remarks and the v6.2.x "stuck on splash" wave that firing early caused).
    /// </summary>
    internal static long ThresholdFor(bool hasPumpedOnce)
        => hasPumpedOnce ? HANG_THRESHOLD_MS : STARTUP_HANG_THRESHOLD_MS;

    /// <summary>
    /// The whole hang verdict, as a pure function of "how long has the dispatcher been silent" and
    /// "has it ever pumped". Extracted so the decision is testable headlessly — the loop it came
    /// from can only be exercised by actually wedging a real dispatcher.
    /// </summary>
    internal static bool IsHung(long silenceMs, bool hasPumpedOnce)
        => silenceMs > ThresholdFor(hasPumpedOnce);

    private static void Loop(Dispatcher dispatcher)
    {
        CleanupOldDumps();

        while (true)
        {
            try
            {
                Thread.Sleep(HEARTBEAT_MS);
                if (dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
                {
                    Volatile.Write(ref _lastBeatTick, Environment.TickCount64);
                    Volatile.Write(ref _firstBeatSeen, 1);
                });

                if (++_beatsSinceSample >= SAMPLE_EVERY_BEATS)
                {
                    _beatsSinceSample = 0;
                    SampleResources();
                }

                bool started = Volatile.Read(ref _firstBeatSeen) == 1;
                long threshold = ThresholdFor(started);
                long silence = Environment.TickCount64 - Volatile.Read(ref _lastBeatTick);
                if (IsHung(silence, started))
                {
                    // Re-check once after a short grace period: a sleep/resume gap or a debugger
                    // break also stalls the heartbeat, but those recover within one beat.
                    Thread.Sleep(3000);
                    if (dispatcher.HasShutdownStarted) return;
                    silence = Environment.TickCount64 - Volatile.Read(ref _lastBeatTick);
                    if (!IsHung(silence, started)) continue;

                    if (!_hangLogged)
                    {
                        _hangLogged = true;
                        // VideoDiag.UiMarkDescription names the call the UI thread never returned
                        // from (screen capture, layered-window Show/Hide/Close, bitmap lock). It is
                        // "(idle)" when the wedge is somewhere uninstrumented, which is itself a
                        // useful negative — see the breadcrumb contract in VideoDiag.
                        string mark = VideoDiag.UiMarkDescription;
                        // ...and HangContext names WHICH FEATURE was live when it happened, which the
                        // stack alone does not (the program-freeze wave all reported mark="(idle)").
                        string state = HangContext.DescribeCompact();
                        App.Logger?.Error("[WATCHDOG] UI thread unresponsive for {Sec:F0}s{Phase} — likely render-thread deadlock; last UI mark: {Mark}; {State}",
                            silence / 1000.0, started ? "" : " (never pumped — wedged during startup)", mark, state);
                        // Mirror into the video trace. These lines only ever went to the ROLLING
                        // Serilog file, which a post-freeze relaunch scrolls straight out of the
                        // 100-line tail a bug report attaches — the exact evidence loss VideoDiag
                        // exists to prevent (#750-#753). Log() is a no-op until VideoDiag.Start ran.
                        VideoDiag.Log("WATCHDOG", string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "UI thread unresponsive for {0:F0}s{1} - last UI mark: {2}; {3}",
                            silence / 1000.0, started ? "" : " (never pumped - wedged during startup)", mark, state));

                        // Durable, human-readable report + the cross-session sentinel. Written
                        // BEFORE the minidump because the dump can take up to 60s (or never finish)
                        // and a task-kill during it must still leave evidence behind.
                        WriteHangReport(silence, started, mark);
                    }
                    if (!_dumpWritten)
                    {
                        _dumpWritten = true;   // set BEFORE writing — never dump twice even if the write itself hangs
                        WriteDump(silence);
                    }
                }
                else if (_hangLogged)
                {
                    _hangLogged = false;
                    App.Logger?.Warning("[WATCHDOG] UI thread recovered");
                    VideoDiag.Log("WATCHDOG", "UI thread recovered");
                    // The app came back, so this session will log its own story and exit cleanly.
                    // Disarm the sentinel: it exists ONLY to speak for a session that was
                    // task-killed while wedged and therefore never got to say anything.
                    ClearSentinel();
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Log one line of USER objects / GDI objects / kernel handles / thread count / private
    /// bytes / working set / managed heap, with the delta from the first sample of the session.
    /// Cheap (a handful of counter reads, no allocation beyond the log line) and never throws.
    /// Runs on the watchdog thread — deliberately does
    /// NOT touch the dispatcher or any WPF object, so it keeps sampling even while the UI is
    /// wedged (a resource-starved app hangs before it crashes).
    /// </summary>
    private static void SampleResources()
    {
        try
        {
            IntPtr self = GetCurrentProcess();       // pseudo-handle: nothing to close
            uint user = GetGuiResources(self, GR_USEROBJECTS);
            uint gdi = GetGuiResources(self, GR_GDIOBJECTS);

            int handles = 0, threads = 0;
            long priv = 0, ws = 0;
            long gcHeap = GC.GetTotalMemory(false);   // managed heap; never throws
            try
            {
                using var p = Process.GetCurrentProcess();
                handles = p.HandleCount;
                threads = p.Threads.Count;
                priv = p.PrivateMemorySize64;         // total committed (native + managed)
                ws = p.WorkingSet64;                  // resident set — what the OS calls "RAM in use"
            }
            catch { }

            if (!_baselineTaken)
            {
                _baselineTaken = true;
                _firstUser = user; _firstGdi = gdi;
                _firstHandles = handles; _firstThreads = threads;
                _firstPriv = priv; _firstWs = ws; _firstGcHeap = gcHeap;
                App.Logger?.Information(
                    "[RES] baseline user={User} gdi={Gdi} handles={Handles} threads={Threads} privMB={PrivMB:F0} wsMB={WsMB:F0} gcMB={GcMB:F1}",
                    user, gdi, handles, threads, priv / MB, ws / MB, gcHeap / MB);
                return;
            }

            App.Logger?.Information(FormatResLine(
                user, gdi, handles, threads, priv, ws, gcHeap,
                _firstUser, _firstGdi, _firstHandles, _firstThreads,
                _firstPriv, _firstWs, _firstGcHeap));

            uint worst = Math.Max(user, gdi);
            if (worst >= USER_GDI_CRITICAL)
            {
                // Past this point CreateWindowEx starts failing app-wide (and, once the desktop
                // heap goes with it, in Explorer too — see bug #627).
                App.Logger?.Error(
                    "[RES] CRITICAL: {Kind} objects at {Count} (per-process quota is 10000) - window creation is about to start failing",
                    user >= gdi ? "USER" : "GDI", worst);
            }
            else if (worst >= USER_GDI_WARN && !_resourceAlarmed)
            {
                _resourceAlarmed = true;
                App.Logger?.Warning(
                    "[RES] {Kind} objects crossed {Threshold} ({Count}) - a handle leak is in progress",
                    user >= gdi ? "USER" : "GDI", USER_GDI_WARN, worst);
            }
        }
        catch { }
    }

    /// <summary>
    /// Render the delta [RES] line: current USER/GDI/handle/thread counts and MB-scaled private
    /// bytes / working set / managed heap, each with the signed delta from the session's first
    /// sample. privMB/wsMB use F0, gcMB uses F1 (mirrors the Serilog template rendered to the log
    /// file). Invariant-culture so the grep-able text is deterministic. Extracted for headless
    /// regression tests — a monotonic climb in these deltas is how a #634-class leak is spotted.
    /// </summary>
    internal static string FormatResLine(
        uint user, uint gdi, int handles, int threads, long priv, long ws, long gcHeap,
        uint firstUser, uint firstGdi, int firstHandles, int firstThreads,
        long firstPriv, long firstWs, long firstGcHeap)
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        return string.Format(c,
            "[RES] user={0} (+{1}) gdi={2} (+{3}) handles={4} (+{5}) threads={6} (+{7}) " +
            "privMB={8:F0} (+{9:F0}) wsMB={10:F0} (+{11:F0}) gcMB={12:F1} (+{13:F1})",
            user, (long)user - firstUser,
            gdi, (long)gdi - firstGdi,
            handles, handles - firstHandles,
            threads, threads - firstThreads,
            priv / MB, (priv - firstPriv) / MB,
            ws / MB, (ws - firstWs) / MB,
            gcHeap / MB, (gcHeap - firstGcHeap) / MB);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    // ── DURABLE HANG REPORT + CROSS-SESSION SENTINEL ─────────────────────────────────────────
    //
    // WHY (program-freeze wave, v6.8.2 — day-14 corner GIF, day-2 lock card, #984): every one of
    // those reports is a HARD freeze, i.e. the user task-kills the app. That kill costs us
    // everything the watchdog just learned unless it is already on the platter:
    //   * The Serilog line above goes to the ROLLING app log, and the relaunch the user needs in
    //     order to FILE the report writes hundreds of startup lines over it — the freeze scrolls
    //     out of the 100-line tail the bug reporter attaches. (Same evidence loss VideoDiag was
    //     built for; we mirror there too, but that file rolls at 1MB.)
    //   * The minidump is the best evidence and the least likely to arrive: it is tens of MB, the
    //     user is never told it exists, and the bug reporter cannot attach it.
    // So: one small, self-contained, immediately-flushed text file per hang episode, plus a
    // sentinel that the NEXT launch consumes and re-logs loudly. After a task-kill the very first
    // lines of the fresh session's log then say "you froze, here is what was running" — which is
    // in the tail, so it reaches us through the ordinary bug-report path with no user effort.
    private const int MaxReportsPerSession = 5;
    private static int _reportsWritten;

    private static string SentinelPath => Path.Combine(App.UserDataPath, "logs", "ui_hang.pending");

    private static void WriteHangReport(long silenceMs, bool startedPumping, string mark)
    {
        try
        {
            if (_reportsWritten >= MaxReportsPerSession) return;
            _reportsWritten++;

            string dir = Path.Combine(App.UserDataPath, "logs");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"hang_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            var body = new StringBuilder(2048);
            body.AppendLine("CCP UI-hang report");
            body.Append("when       : ").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            body.Append("version    : ").AppendLine(Safe(() => UpdateService.AppVersion));
            body.Append("pid        : ").AppendLine(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            body.Append("silence    : ").Append((silenceMs / 1000.0).ToString("F0", System.Globalization.CultureInfo.InvariantCulture)).AppendLine("s");
            body.Append("phase      : ").AppendLine(startedPumping ? "running" : "STARTUP (dispatcher never pumped)");
            body.Append("lastUiMark : ").AppendLine(mark);
            body.AppendLine();
            body.AppendLine(HangContext.Describe());

            string text = body.ToString();

            // WriteThrough: a hard power-off (several of these reports end that way) must not be
            // able to take the file back with it.
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write,
                       FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.WriteThrough))
            using (var sw = new StreamWriter(fs, Encoding.UTF8))
            {
                sw.Write(text);
                sw.Flush();
                fs.Flush(true);
            }

            ArmSentinel(text);
            CleanupOldReports();

            App.Logger?.Error("[WATCHDOG] hang report written: {Path}", path);
            VideoDiag.Log("WATCHDOG", "hang report written: " + path);

            // The hang report is a snapshot of the moment; the flight recorder is the run-up to it.
            // A frozen UI is exactly the case where the last few hundred Debug lines - which have
            // never reached a disk before - say which subsystem stopped answering.
            Services.Logging.FlightRecorderSink.DumpIfActive("hang");
        }
        catch (Exception ex)
        {
            // Last resort: if the file could not be written, at least get the state into the log.
            try
            {
                App.Logger?.Error("[WATCHDOG] hang report file failed ({E}); state inline:{NL}{State}",
                    ex.Message, Environment.NewLine, HangContext.Describe());
            }
            catch { }
        }
    }

    private static void ArmSentinel(string reportText)
    {
        try
        {
            string path = SentinelPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.WriteThrough);
            using var sw = new StreamWriter(fs, Encoding.UTF8);
            sw.Write(reportText);
            sw.Flush();
            fs.Flush(true);
        }
        catch { }
    }

    private static void ClearSentinel()
    {
        try
        {
            string path = SentinelPath;
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    /// <summary>
    /// Clean shutdown — even a slow one — is not a hang. Called from App.OnExit next to the other
    /// dirty-shutdown sentinels so the next launch does not false-report a freeze the user
    /// actually sat through and then quit normally.
    /// </summary>
    public static void ClearSentinelOnCleanExit() => ClearSentinel();

    /// <summary>
    /// Called once at startup. If a sentinel survived, the previous session was wedged when the
    /// process died (task-kill / hard reset) and never got to log anything more. Re-log the whole
    /// captured state at Error level — the production Serilog floor is Information, so a Debug
    /// line here would be invisible in the field — and consume the file so it reports once.
    /// </summary>
    public static void ConsumeAndReportPreviousHang(Serilog.ILogger? logger)
    {
        try
        {
            string path = SentinelPath;
            if (!File.Exists(path)) return;

            string body = "";
            DateTime armed = DateTime.MinValue;
            try { body = File.ReadAllText(path).Trim(); } catch { }
            try { armed = File.GetLastWriteTime(path); } catch { }

            logger?.Error(
                "[WATCHDOG] PREVIOUS SESSION HUNG: the UI thread was unresponsive at {Armed} and the process " +
                "died without recovering (task-kill / hard reset), so nothing else was logged. " +
                "Captured state follows; a matching hang_*.dmp/.txt may be next to this log.{NL}{Body}",
                armed == DateTime.MinValue ? "(unknown)" : armed.ToString("yyyy-MM-dd HH:mm:ss"),
                Environment.NewLine,
                string.IsNullOrWhiteSpace(body) ? "(state unavailable)" : body);

            ClearSentinel();
        }
        catch { }
    }

    /// <summary>Keep the four newest text reports; they are tiny but should not accumulate forever.</summary>
    private static void CleanupOldReports()
    {
        try
        {
            string dir = Path.Combine(App.UserDataPath, "logs");
            foreach (var f in new DirectoryInfo(dir).GetFiles("hang_*.txt")
                         .OrderByDescending(f => f.LastWriteTimeUtc).Skip(4))
            {
                try { f.Delete(); } catch { }
            }
        }
        catch { }
    }

    private static string Safe(Func<string> probe)
    {
        try { return probe() ?? "(null)"; }
        catch { return "(unavailable)"; }
    }

    private static void WriteDump(long silenceMs)
    {
        // Self-dumping a wedged process is unreliable: dbghelp suspends/walks our own threads
        // while they churn, which has produced 0-byte dumps (process died mid-write) and
        // ERROR_PARTIAL_COPY dumps missing the system-info stream (unloadable). An EXTERNAL
        // dumper process reads us from outside instead: we relaunch OUR OWN exe with
        // --write-hang-dump (handled at the very top of App.OnStartup, before the splash and
        // the single-instance mutex). The previous external dumper (rundll32 + comsvcs
        // MiniDump) had been dead since v6.1.5: comsvcs' MiniDump entry point requires the
        // literal third argument "full" and silently does nothing without it, so every hang
        // fell back to the in-proc path below — whose old flag set included
        // MiniDumpWithPrivateReadWriteMemory and wrote 1-2GB with ALL threads suspended,
        // freezing the app for 30s..minutes (or forever when the write itself wedged).
        // Both paths now write the COMPACT stream set only.
        string dir = Path.Combine(App.UserDataPath, "logs");
        string path = Path.Combine(dir, $"hang_{DateTime.Now:yyyyMMdd_HHmmss}.dmp");
        try
        {
            Directory.CreateDirectory(dir);
            using var proc = Process.GetCurrentProcess();

            try
            {
                var selfExe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(selfExe) && File.Exists(selfExe))
                {
                    using var dumper = Process.Start(new ProcessStartInfo
                    {
                        FileName = selfExe,
                        Arguments = $"--write-hang-dump {proc.Id} \"{path}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    });
                    if (dumper != null && dumper.WaitForExit(60_000)
                        && File.Exists(path) && new FileInfo(path).Length > 100_000)
                    {
                        App.Logger?.Error("[WATCHDOG] hang dump written (external) after {Sec:F0}s of silence: {Path}", silenceMs / 1000.0, path);
                        VideoDiag.Log("WATCHDOG", "hang dump written (external): " + path);
                        return;
                    }
                    // Do NOT Kill() a dumper that is mid-write: MiniDumpWriteDump suspends the
                    // target's (our) threads and killing the dumper would leave them suspended
                    // forever. A dumper that got this far but produced nothing has already exited
                    // or failed before suspending; just fall through.
                    App.Logger?.Error("[WATCHDOG] external dumper failed or produced nothing — falling back to in-proc dump");
                    try { if (File.Exists(path)) File.Delete(path); } catch { }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error("[WATCHDOG] external dumper error: {E} — falling back to in-proc dump", ex.Message);
            }

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            bool ok = MiniDumpWriteDump(proc.Handle, (uint)proc.Id, fs.SafeFileHandle, CompactDumpType,
                                        IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (ok)
            {
                App.Logger?.Error("[WATCHDOG] hang dump written after {Sec:F0}s of silence: {Path}", silenceMs / 1000.0, path);
                VideoDiag.Log("WATCHDOG", "hang dump written (in-proc): " + path);
            }
            else
            {
                App.Logger?.Error("[WATCHDOG] MiniDumpWriteDump failed (error {Err})", Marshal.GetLastWin32Error());
                VideoDiag.Log("WATCHDOG", "MiniDumpWriteDump failed (error " + Marshal.GetLastWin32Error() + ")");
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Error("[WATCHDOG] hang dump failed: {E}", ex.Message);
            VideoDiag.Log("WATCHDOG", "hang dump failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Dump-writer mode, invoked as `CCP.exe --write-hang-dump &lt;pid&gt; &lt;path&gt;` from the
    /// watchdog of a WEDGED sibling process. Runs in a healthy process, so dbghelp can
    /// suspend and walk the target reliably (no ERROR_PARTIAL_COPY self-dump races).
    /// Returns true if a plausible dump landed on disk.
    /// </summary>
    public static bool TryWriteDumpOfProcess(int pid, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var target = Process.GetProcessById(pid);
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            bool ok = MiniDumpWriteDump(target.Handle, (uint)pid, fs.SafeFileHandle, CompactDumpType,
                                        IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            fs.Flush();
            return ok && fs.Length > 100_000;
        }
        catch
        {
            return false;
        }
    }

    // Every thread's stack + module list + handle data — enough to identify the
    // render-thread deadlocks these hangs are, at tens of MB instead of the 1-2GB
    // that MiniDumpWithPrivateReadWriteMemory produced.
    // 2026-07-13: the previous set produced a dump WinDbg bucketed as bad_dump!missing_teb —
    // top-of-stack frames only, no unwind (it still nailed the render thread inside
    // CWGXBitmapLockState::LockRead, but not who held the write side). Indirectly-referenced
    // memory + full memory info pulls in the pages the thread stacks point at, which is what
    // stack unwinding actually needs; dumps land in the tens of MB, still far from full-RW.
    private const uint CompactDumpType = MiniDumpWithDataSegs | MiniDumpWithHandleData
        | MiniDumpWithUnloadedModules | MiniDumpWithProcessThreadData | MiniDumpWithThreadInfo
        | MiniDumpWithIndirectlyReferencedMemory | MiniDumpWithFullMemoryInfo;

    // The old full-RW dumps left multi-GB files behind (3.8GB observed in one logs folder).
    // Keep the two newest dumps, drop the rest.
    private static void CleanupOldDumps()
    {
        try
        {
            string dir = Path.Combine(App.UserDataPath, "logs");
            var dumps = new DirectoryInfo(dir).GetFiles("hang_*.dmp")
                .OrderByDescending(f => f.LastWriteTimeUtc).Skip(2);
            foreach (var f in dumps)
            {
                try { f.Delete(); } catch { }
            }
        }
        catch { }
    }

    private const uint MiniDumpWithDataSegs               = 0x00000001;
    private const uint MiniDumpWithHandleData             = 0x00000004;
    private const uint MiniDumpWithUnloadedModules        = 0x00000020;
    private const uint MiniDumpWithProcessThreadData      = 0x00000100;
    private const uint MiniDumpWithThreadInfo             = 0x00001000;
    private const uint MiniDumpWithIndirectlyReferencedMemory = 0x00000040;
    private const uint MiniDumpWithFullMemoryInfo         = 0x00000800;

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(IntPtr hProcess, uint processId, SafeFileHandle hFile,
        uint dumpType, IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);
}
