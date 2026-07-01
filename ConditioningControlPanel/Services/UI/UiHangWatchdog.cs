using System;
using System.Diagnostics;
using System.IO;
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
/// </summary>
public static class UiHangWatchdog
{
    private const int HEARTBEAT_MS = 2000;
    private const int HANG_THRESHOLD_MS = 10000;

    private static long _lastBeatTick;
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
        while (true)
        {
            try
            {
                Thread.Sleep(HEARTBEAT_MS);
                if (dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(DispatcherPriority.Normal,
                    () => Volatile.Write(ref _lastBeatTick, Environment.TickCount64));

                long silence = Environment.TickCount64 - Volatile.Read(ref _lastBeatTick);
                if (silence > HANG_THRESHOLD_MS)
                {
                    // Re-check once after a short grace period: a sleep/resume gap or a debugger
                    // break also stalls the heartbeat, but those recover within one beat.
                    Thread.Sleep(3000);
                    if (dispatcher.HasShutdownStarted) return;
                    silence = Environment.TickCount64 - Volatile.Read(ref _lastBeatTick);
                    if (silence <= HANG_THRESHOLD_MS) continue;

                    if (!_hangLogged)
                    {
                        _hangLogged = true;
                        App.Logger?.Error("[WATCHDOG] UI thread unresponsive for {Sec:F0}s — likely render-thread deadlock", silence / 1000.0);
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
        // dumper process (rundll32 + comsvcs MiniDump) reads us from outside instead; the
        // in-proc MiniDumpWriteDump remains as fallback.
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
                var rundll = Path.Combine(Environment.SystemDirectory, "rundll32.exe");
                var comsvcs = Path.Combine(Environment.SystemDirectory, "comsvcs.dll");
                // Compact minidump (NO "full"): comsvcs "full" writes MiniDumpWithFullMemory — the
                // ENTIRE process memory (multi-GB for this app), which piles IO/commit pressure onto a
                // process that is often ALREADY at desktop-heap/GPU-surface exhaustion when the watchdog
                // fires, and can itself produce 0-byte/partial dumps. A normal minidump still carries
                // every thread's stack + module list — enough to identify the render-thread deadlock
                // that these hangs are. (If deep managed-heap inspection is ever needed, the in-proc
                // fallback below captures private RW memory.)
                using var dumper = Process.Start(new ProcessStartInfo
                {
                    FileName = rundll,
                    Arguments = $"\"{comsvcs}\", MiniDump {proc.Id} \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (dumper != null && dumper.WaitForExit(60_000)
                    && File.Exists(path) && new FileInfo(path).Length > 100_000)
                {
                    App.Logger?.Error("[WATCHDOG] hang dump written (external) after {Sec:F0}s of silence: {Path}", silenceMs / 1000.0, path);
                    return;
                }
                try { dumper?.Kill(); } catch { }
                App.Logger?.Error("[WATCHDOG] external dumper failed or produced nothing — falling back to in-proc dump");
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
            catch (Exception ex)
            {
                App.Logger?.Error("[WATCHDOG] external dumper error: {E} — falling back to in-proc dump", ex.Message);
            }

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

            // Enough memory for managed stack walking (!clrstack) without a multi-GB full dump:
            // private RW memory covers the managed heap + stacks; thread/handle info names the waits.
            const uint type = MiniDumpWithDataSegs | MiniDumpWithHandleData | MiniDumpWithUnloadedModules
                            | MiniDumpWithProcessThreadData | MiniDumpWithPrivateReadWriteMemory | MiniDumpWithThreadInfo;
            bool ok = MiniDumpWriteDump(proc.Handle, (uint)proc.Id, fs.SafeFileHandle, type,
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

    private const uint MiniDumpWithDataSegs               = 0x00000001;
    private const uint MiniDumpWithHandleData             = 0x00000004;
    private const uint MiniDumpWithUnloadedModules        = 0x00000020;
    private const uint MiniDumpWithProcessThreadData      = 0x00000100;
    private const uint MiniDumpWithPrivateReadWriteMemory = 0x00000200;
    private const uint MiniDumpWithThreadInfo             = 0x00001000;

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(IntPtr hProcess, uint processId, SafeFileHandle hFile,
        uint dumpType, IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);
}
