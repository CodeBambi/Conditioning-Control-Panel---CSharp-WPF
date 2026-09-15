using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace CCP.Avalonia.Testing;

/// <summary>
/// Runs each Avalonia test assembly on one long-lived dispatcher thread. Windows native controls
/// require the thread that creates them to be STA; keeping the dispatcher alive also keeps every
/// async continuation and later test action on the same thread.
/// </summary>
internal static class AvaloniaTestDispatcher
{
    private static readonly object Gate = new();
    private static readonly CancellationTokenSource Lifetime = new();
    private static Dispatcher? _dispatcher;

    static AvaloniaTestDispatcher() =>
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Lifetime.Cancel();

    internal static bool IsDispatcherThread =>
        Volatile.Read(ref _dispatcher)?.CheckAccess() == true;

    internal static Task RunAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = Volatile.Read(ref _dispatcher);
        if (dispatcher is not null)
            return dispatcher.CheckAccess() ? action() : Enqueue(dispatcher, action);

        var completion = NewCompletion();
        lock (Gate)
        {
            dispatcher = _dispatcher;
            if (dispatcher is null)
            {
                Start(action, completion);
                return completion.Task;
            }
        }

        return dispatcher.CheckAccess() ? action() : Enqueue(dispatcher, action);
    }

    internal static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        RunAsync(() =>
        {
            action();
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
    }

    private static TaskCompletionSource<object?> NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void Start(Func<Task> action, TaskCompletionSource<object?> completion)
    {
        var thread = new Thread(() => Bootstrap(action, completion))
        {
            IsBackground = true,
            Name = "CCP Avalonia test dispatcher",
        };
        if (OperatingSystem.IsWindows())
            thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
    }

    private static void Bootstrap(Func<Task> action, TaskCompletionSource<object?> completion)
    {
        // These are the same public Avalonia primitives used by the desktop head. Installing them
        // before AppBuilder setup means setup, native control attachment, and async continuations
        // all see this thread rather than the xUnit worker that requested the test.
        AvaloniaSynchronizationContext.InstallIfNeeded();
        var dispatcher = Dispatcher.UIThread;
        Volatile.Write(ref _dispatcher, dispatcher);
        var shutdownStarted = 0;
        dispatcher.ShutdownStarted += (_, _) => Volatile.Write(ref shutdownStarted, 1);

        Execute(action, completion);
        try
        {
            dispatcher.MainLoop(Lifetime.Token);
        }
        catch (InvalidOperationException) when (Volatile.Read(ref shutdownStarted) != 0)
        {
            // A test may deliberately shut down its desktop lifetime. Avalonia's MainLoop throws
            // when entered after that shutdown; the action's completion already carried its result.
        }
    }

    private static Task Enqueue(Dispatcher dispatcher, Func<Task> action)
    {
        var completion = NewCompletion();
        try
        {
            dispatcher.Post(() => Execute(action, completion), DispatcherPriority.Normal);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        return completion.Task;
    }

    private static void Execute(Func<Task> action, TaskCompletionSource<object?> completion)
    {
        Task task;
        try
        {
            task = action() ?? Task.CompletedTask;
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            return;
        }

        if (task.IsCompleted)
        {
            Complete(task, completion);
            return;
        }

        _ = task.ContinueWith(
            completed => Complete(completed, completion),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void Complete(Task task, TaskCompletionSource<object?> completion)
    {
        if (task.IsCanceled)
        {
            completion.TrySetCanceled();
            return;
        }

        if (task.IsFaulted)
        {
            completion.TrySetException(task.Exception!.InnerExceptions);
            return;
        }

        completion.TrySetResult(null);
    }
}
