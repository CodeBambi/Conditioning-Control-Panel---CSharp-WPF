using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

                bool started = Volatile.Read(ref _firstBeatSeen) == 1;
                long threshold = started ? HANG_THRESHOLD_MS : STARTUP_HANG_THRESHOLD_MS;
                long silence = Environment.TickCount64 - Volatile.Read(ref _lastBeatTick);
                if (silence > threshold)
                {
                    // Re-check once after a short grace period: a sleep/resume gap or a debugger
                    // break also stalls the heartbeat, but those recover within one beat.
                    Thread.Sleep(3000);
                    if (dispatcher.HasShutdownStarted) return;
                    silence = Environment.TickCount64 - Volatile.Read(ref _lastBeatTick);
                    if (silence <= threshold) continue;

                    if (!_hangLogged)
                    {
                        _hangLogged = true;
                        App.Logger?.Error("[WATCHDOG] UI thread unresponsive for {Sec:F0}s{Phase} — likely render-thread deadlock",
                            silence / 1000.0, started ? "" : " (never pumped — wedged during startup)");
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
                }
            }
            catch { }
        }
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
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel", "logs");
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
                App.Logger?.Error("[WATCHDOG] hang dump written after {Sec:F0}s of silence: {Path}", silenceMs / 1000.0, path);
            else
                App.Logger?.Error("[WATCHDOG] MiniDumpWriteDump failed (error {Err})", Marshal.GetLastWin32Error());
        }
        catch (Exception ex)
        {
            App.Logger?.Error("[WATCHDOG] hang dump failed: {E}", ex.Message);
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
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ConditioningControlPanel", "logs");
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
