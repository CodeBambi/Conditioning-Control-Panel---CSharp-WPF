using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ConditioningControlPanel.Helpers;

/// <summary>
/// Safe dispatcher helpers that guard against null dispatcher and shutdown scenarios.
/// </summary>
public static class DispatcherHelper
{
    /// <summary>
    /// Fire-and-forget dispatch to UI thread via BeginInvoke.
    /// Safe to call during shutdown — silently no-ops if dispatcher is unavailable.
    /// </summary>
    public static void RunOnUI(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
            return;

        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action, priority);
    }

    /// <summary>
    /// Awaitable dispatch to UI thread via InvokeAsync.
    /// Safe to call during shutdown — silently no-ops if dispatcher is unavailable.
    /// If already on the UI thread, executes directly.
    /// </summary>
    public static async Task RunOnUIAsync(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
            return;

        if (dispatcher.CheckAccess())
            action();
        else
            await dispatcher.InvokeAsync(action, priority);
    }

    /// <summary>
    /// How long a cross-thread <see cref="RunOnUISync"/> will wait for the UI thread before it
    /// gives up. Bounded so a wedged dispatcher can never hang a background caller forever (#684):
    /// generous enough that ordinary UI-thread work (window realization, layout, LibVLC handoff)
    /// finishes well inside it, short enough that a caller notices a hang instead of joining it.
    /// </summary>
    private static readonly TimeSpan SyncInvokeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Synchronous dispatch to UI thread via Invoke, bounded by <see cref="SyncInvokeTimeout"/>.
    /// Safe to call during shutdown — silently no-ops if dispatcher is unavailable.
    /// If already on the UI thread, executes directly to avoid deadlock.
    /// On timeout the queued operation is abandoned and a Warning naming the caller is logged;
    /// the method returns normally rather than throwing, matching the existing contract where
    /// every call site either wraps this in a try/catch or treats the work as best-effort UI
    /// side effects. Exceptions thrown by <paramref name="action"/> still propagate as before.
    /// </summary>
    public static void RunOnUISync(Action action, DispatcherPriority priority = DispatcherPriority.Normal,
        [CallerMemberName] string caller = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
            return;

        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        // The bounded overload gives up on the wait and aborts the still-queued operation instead of
        // blocking forever. WPF signals that by simply returning (the item is dropped, no exception),
        // so the elapsed time is what tells us a timeout happened; the TimeoutException catch is a
        // belt-and-braces guard in case a framework path surfaces it as a throw instead.
        var sw = Stopwatch.StartNew();
        try
        {
            dispatcher.Invoke(action, priority, CancellationToken.None, SyncInvokeTimeout);
        }
        catch (TimeoutException)
        {
            // Don't rethrow: an exception here would surface as an unrelated failure in callers
            // that only queue best-effort UI side effects.
            LogSyncTimeout(caller, callerFile, callerLine);
            return;
        }
        if (sw.Elapsed >= SyncInvokeTimeout)
            LogSyncTimeout(caller, callerFile, callerLine);
    }

    // [WATCHDOG]-tagged so BugReportService's diagnostic-marker rescue keeps it in bug reports
    // even when the plain last-100-line app-log tail has scrolled past the hang.
    private static void LogSyncTimeout(string caller, string callerFile, int callerLine)
    {
        try
        {
            App.Logger?.Warning(
                "[WATCHDOG] DispatcherHelper: RunOnUISync gave up after {Timeout}s waiting for the UI thread (caller {Caller} at {File}:{Line}) - UI work abandoned",
                SyncInvokeTimeout.TotalSeconds, caller,
                string.IsNullOrEmpty(callerFile) ? "?" : System.IO.Path.GetFileName(callerFile), callerLine);
        }
        catch { /* logging must never take down the caller */ }
    }
}
