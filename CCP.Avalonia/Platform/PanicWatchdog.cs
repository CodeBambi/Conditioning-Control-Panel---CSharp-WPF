// PORTED from ConditioningControlPanel/MainWindow/MainWindow.xaml.cs ArmPanicWatchdog (:1025) and
// RunEmergencyPanicTeardown (:1046), #919b. The listener (Win32PanicKey / X11PanicKey) runs on its own
// thread here, so a wedged UI thread can no longer lose the key itself; it can still sit on the queued
// handler. This watches that handler from a background thread and, if it has not finished inside
// 2 s, runs the thread-safe part of the stop off the UI thread.
// ponytail: WPF also stops BrainDrain and the screen OCR here and queues the OCR restart; neither has
// a twin on this head yet (keyword triggers / OCR are not ported). Add them when they land.

using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services;

using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class PanicWatchdog
{
    /// <summary>WPF PanicWatchdogTimeout.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static int _fallbackRunning;

    /// <summary>Test seam: the off-thread teardown (swapped to observe it without real services).</summary>
    internal static Action Teardown { get; set; } = RunEmergencyTeardown;

    /// <summary>Queues <paramref name="handler"/> on the UI thread and watches it. Safe from any thread;
    /// returns at once (the hook callback must never wait).</summary>
    internal static void QueueWatched(Action handler)
    {
        DispatcherOperation op;
        try { op = Dispatcher.UIThread.InvokeAsync(handler); }
        catch (Exception ex)
        {
            // The dispatcher refused the post (shutting down or broken): the press must still stop things.
            Log.Error(ex, "Panic: could not queue the handler; tearing down off-thread");
            RunGuarded();
            return;
        }
        Arm(op.GetTask());
    }

    /// <summary>WPF ArmPanicWatchdog: a background watcher, never a wait on the calling thread.</summary>
    internal static Task Arm(Task handler, TimeSpan? timeout = null) => Task.Run(async () =>
    {
        try
        {
            var finished = await Task.WhenAny(handler, Task.Delay(timeout ?? Timeout)).ConfigureAwait(false);
            if (finished == handler)
            {
                try { _ = handler.Exception; } catch { }   // observe a faulted handler
                return;
            }
            RunGuarded();
        }
        catch (Exception ex)
        {
            try { Log.Error(ex, "Panic watchdog failed"); } catch { }
        }
    });

    private static void RunGuarded()
    {
        if (Interlocked.Exchange(ref _fallbackRunning, 1) == 1) return;
        try
        {
            try
            {
                Log.Warning("PANIC FALLBACK: the UI thread did not handle the panic key within {Ms}ms - tearing down off-thread",
                    (int)Timeout.TotalMilliseconds);
            }
            catch { }
            Teardown();
        }
        catch (Exception ex)
        {
            try { Log.Error(ex, "Panic fallback teardown failed"); } catch { }
        }
        finally { Interlocked.Exchange(ref _fallbackRunning, 0); }
    }

    /// <summary>WPF RunEmergencyPanicTeardown: ONLY thread-safe steps, each guarded on its own. No overlay
    /// hiding: overlays are the UI thread's to drop when it comes back and runs the real handler.</summary>
    private static void RunEmergencyTeardown()
    {
        Step("haptics", () => CoreHaptics.Service?.PanicStop());
        Step("remote haptics", RemoteCommands.StopHaptics);
        Step("ai follow-ups", () => ConditioningControlPanel.Services.Commands.AiCommandService.CancelAll());
        // Conditional, as WPF (#668): with the standalone Audio Layers master on, the bed is the user's.
        Step("audio layers", () => { if (!CoreSettings.Current.AudioLayersEnabled) LayeredAudio.Instance?.Stop(); });
        Step("mind wipe", () => CoreMindWipe.StopProvider?.Invoke());
        Step("unduck", () => LibVlcAudio.Instance?.ForceUnduck());
        Log.Information("PANIC FALLBACK complete");
    }

    private static void Step(string name, Action step)
    {
        try { step(); }
        catch (Exception ex) { try { Log.Warning("PANIC FALLBACK: {Step} step failed: {Error}", name, ex.Message); } catch { } }
    }
}
