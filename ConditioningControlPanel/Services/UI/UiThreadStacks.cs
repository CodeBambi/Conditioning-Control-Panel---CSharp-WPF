using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace ConditioningControlPanel.Services;

/// <summary>
/// The missing half of every sudden-freeze report (ccp-bugs #1189 / #1179 / #1159 / #984): the UI
/// thread's MANAGED STACK, captured in-process at the moment the watchdog declares the hang, and
/// written into hang_*.txt where the bug reporter can carry it.
///
/// WHY NOT THE MINIDUMP. The watchdog already writes one hang_*.dmp per session and it does contain
/// the stacks - but nobody ever sees it: tens of MB, the user is never told it exists, the in-app
/// bug reporter cannot attach it, and reading it takes WinDbg plus the matching runtime. Across the
/// whole #1189 family not one dump has reached us. A dozen lines of text inside the report that
/// DOES reach us beats a perfect dump that does not.
///
/// WHY CLRMD SNAPSHOT-AND-ATTACH. <c>DataTarget.CreateSnapshotAndAttach</c> takes a PSS
/// (copy-on-write) snapshot of our own process and walks THAT, so it never suspends, interrupts or
/// mutates live threads - which matters here, because the process being inspected is already wedged
/// and must not be made worse. A passive <c>AttachToProcess</c> on self, or any dbghelp self-walk,
/// reads memory the runtime is still mutating. Measured: ~95ms for the snapshot plus the full
/// managed stack of a blocked thread.
///
/// SAFETY CONTRACT. Runs ONLY from the watchdog after its existing 10s trigger (there is no new
/// setting), on its own thread, under a hard time budget. A forensics routine that itself hangs
/// would turn a recoverable freeze into a permanent one, so the budget is enforced by abandoning
/// the worker thread rather than by trusting ClrMD to return.
/// </summary>
internal static class UiThreadStacks
{
    /// <summary>Frames kept for the UI thread. Deep enough for a WPF stack with room to spare.</summary>
    private const int MaxUiFrames = 64;
    /// <summary>Other managed threads summarised, newest-first as the runtime lists them.</summary>
    private const int MaxOtherThreads = 12;
    private const int MaxOtherFrames = 8;

    /// <summary>
    /// Capture the managed stack of the thread with <paramref name="uiManagedThreadId"/> (plus a
    /// short summary of the other managed threads, which is where the other end of a deadlock
    /// lives) and return it as report-ready text. Never throws; always returns something printable,
    /// including when the capture fails or runs out of budget.
    /// </summary>
    private static int _abandoned;

    /// <summary>Tests share one process; the once-per-session latch must not leak between them.</summary>
    internal static void ResetAbandonedLatchForTests() => Volatile.Write(ref _abandoned, 0);

    public static string CaptureWithBudget(int uiManagedThreadId, int budgetMs)
    {
        // The worker thread exists purely so the budget is ENFORCEABLE. .NET 8 has no
        // Thread.Abort, so if ClrMD blocks (a snapshot that cannot be taken, a DAC that will not
        // load) the only way to keep our promise is to stop waiting and walk away. The worker is a
        // background thread, so an abandoned one cannot hold the process open either.
        string result = "(stack capture produced nothing)";
        // One abandoned capture per process, ever. An abandoned worker still owns its PSS
        // snapshot (a copy-on-write clone of the address space) until ClrMD returns, which on a
        // machine that is already wedged may be never. Repeating that up to MaxReportsPerSession
        // times would stack clones on the box we are trying to diagnose.
        if (Volatile.Read(ref _abandoned) != 0)
            return "(stack capture skipped: an earlier capture this session ran out of budget and still holds its snapshot; see the hang_*.dmp beside this file)";
        try
        {
            var done = new ManualResetEventSlim(false);
            var worker = new Thread(() =>
            {
                try { result = CaptureCore(uiManagedThreadId); }
                catch (Exception ex) { result = "(stack capture failed: " + ex.GetType().Name + ": " + ex.Message + ")"; }
                finally { try { done.Set(); } catch { } }
            })
            {
                IsBackground = true,
                Name = "UiHangStackCapture",
                Priority = ThreadPriority.BelowNormal,
            };
            worker.Start();

            // `done` is deliberately never disposed on either path: an abandoned worker may still
            // Set() it, and even on the happy path Wait can return while the worker is inside
            // Set(). Disposing under it would throw there, for nothing.
            if (!done.Wait(budgetMs))
            {
                Volatile.Write(ref _abandoned, 1);
                return "(stack capture exceeded its "
                       + (budgetMs / 1000).ToString(CultureInfo.InvariantCulture)
                       + "s budget and was abandoned; see the hang_*.dmp beside this file)";
            }
            return result;
        }
        catch (Exception ex)
        {
            return "(stack capture unavailable: " + ex.Message + ")";
        }
    }

    /// <summary>
    /// The ClrMD work itself. NoInlining is load-bearing: it keeps every reference to the ClrMD
    /// types inside THIS method, so if the assembly is missing from a build the JIT failure lands
    /// as a catchable exception at the call above instead of taking the caller down with it.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string CaptureCore(int uiManagedThreadId)
    {
        var sb = new StringBuilder(4096);

        using var target = Microsoft.Diagnostics.Runtime.DataTarget
            .CreateSnapshotAndAttach(Environment.ProcessId);

        var clr = target.ClrVersions.FirstOrDefault();
        if (clr == null) return "(no CLR found in the process snapshot)";

        using var runtime = clr.CreateRuntime();

        var threads = runtime.Threads;
        var ui = threads.FirstOrDefault(t => t.ManagedThreadId == uiManagedThreadId);
        if (ui == null)
        {
            sb.Append("(UI thread managed id ")
              .Append(uiManagedThreadId.ToString(CultureInfo.InvariantCulture))
              .Append(" not present in the snapshot; ")
              .Append(threads.Length.ToString(CultureInfo.InvariantCulture))
              .AppendLine(" managed threads seen)");
        }
        else
        {
            sb.Append("UI thread  managed=").Append(ui.ManagedThreadId.ToString(CultureInfo.InvariantCulture))
              .Append(" os=0x").AppendLine(ui.OSThreadId.ToString("x", CultureInfo.InvariantCulture));
            int frames = 0;
            foreach (var line in Frames(ui, MaxUiFrames))
            {
                sb.Append("  ").AppendLine(line);
                frames++;
            }
            if (frames == 0) sb.AppendLine("  (no managed frames - the thread is parked in native code; read the .dmp)");
        }

        // The other end of a deadlock is on some OTHER thread, and it is never in the reports.
        // A short head of each remaining managed stack is usually enough to name it.
        sb.AppendLine();
        sb.AppendLine("other managed threads (top frames):");
        int shown = 0;
        foreach (var t in threads)
        {
            if (shown >= MaxOtherThreads) break;
            if (t.ManagedThreadId == uiManagedThreadId) continue;
            var head = Frames(t, MaxOtherFrames).ToList();
            if (head.Count == 0) continue;      // idle pool threads with empty stacks are noise
            shown++;
            sb.Append("  managed=").Append(t.ManagedThreadId.ToString(CultureInfo.InvariantCulture))
              .Append(" os=0x").AppendLine(t.OSThreadId.ToString("x", CultureInfo.InvariantCulture));
            foreach (var line in head) sb.Append("    ").AppendLine(line);
        }
        if (shown == 0) sb.AppendLine("  (none with managed frames)");

        return sb.ToString();
    }

    /// <summary>
    /// Frame text for one thread, capped. Each frame is independently guarded: one unresolvable
    /// method must not cost us the rest of the stack, which is the part that names the wedge.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IEnumerable<string> Frames(Microsoft.Diagnostics.Runtime.ClrThread thread, int max)
    {
        var outp = new List<string>(max);
        try
        {
            foreach (var frame in thread.EnumerateStackTrace())
            {
                if (outp.Count >= max) { outp.Add("... (truncated)"); break; }
                string text;
                try { text = frame.Method?.Signature ?? frame.FrameName ?? "(unknown frame)"; }
                catch { text = "(frame unreadable)"; }
                outp.Add(text);
            }
        }
        catch (Exception ex)
        {
            outp.Add("(stack walk stopped: " + ex.GetType().Name + ")");
        }
        return outp;
    }
}
