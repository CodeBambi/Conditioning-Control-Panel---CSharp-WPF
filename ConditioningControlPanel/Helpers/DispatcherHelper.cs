using System;
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
    /// Synchronous dispatch to UI thread via Invoke.
    /// Safe to call during shutdown — silently no-ops if dispatcher is unavailable.
    /// If already on the UI thread, executes directly to avoid deadlock.
    /// </summary>
    public static void RunOnUISync(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
            return;

        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action, priority);
    }
}
