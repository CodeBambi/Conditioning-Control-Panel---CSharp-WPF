using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// Binds CoreDispatch to Avalonia's desktop dispatcher without letting a background caller wait
    /// on the UI thread; calls already on that thread run inline, matching WPF. After shutdown,
    /// queued callbacks are canceled or ignored rather than falling back to Core's in-place behavior.
    /// </summary>
    internal sealed class AvaloniaCoreDispatch
    {
        private readonly Dispatcher _dispatcher;
        private readonly CancellationTokenSource _shutdown = new();
        private int _stopped;

        public AvaloniaCoreDispatch(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public void Attach()
        {
            CoreDispatch.PostProvider = Post;
            CoreDispatch.InvokeProvider = Invoke;
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0) return;

            try { _shutdown.Cancel(); }
            catch { /* a shutdown callback must not stop the rest of application teardown */ }

            // Do not clear the providers: Core's null-provider fallback runs in place, which is
            // unsafe once the UI is going away. These providers make late calls harmless.
            CoreDispatch.PostProvider = _ => { };
            CoreDispatch.InvokeProvider = (_, _) => (false, null);
        }

        private void Post(Action action)
        {
            if (Volatile.Read(ref _stopped) != 0) return;

            if (_dispatcher.CheckAccess())
            {
                if (Volatile.Read(ref _stopped) == 0) action();
                return;
            }

            _dispatcher.Post(() =>
            {
                if (Volatile.Read(ref _stopped) == 0) action();
            }, DispatcherPriority.Normal);
        }

        private (bool Completed, object? Result) Invoke(Func<object?> func, TimeSpan timeout)
        {
            if (Volatile.Read(ref _stopped) != 0) return (false, null);
            if (_dispatcher.CheckAccess())
            {
                if (Volatile.Read(ref _stopped) != 0) return (false, null);
                return (true, func());
            }

            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            var token = cancellation.Token;
            var operation = _dispatcher.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                if (Volatile.Read(ref _stopped) != 0)
                    throw new OperationCanceledException(token);
                return func();
            }, DispatcherPriority.Normal, token);
            var task = operation.GetTask();

            // A timed-out operation may still fault if it already started. Observe it even after
            // this bounded wait returns, so shutdown does not surface an unobserved task fault.
            _ = task.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            if (task.Wait(timeout)) return (true, task.Result);

            cancellation.Cancel();
            operation.Abort();
            return (false, null);
        }
    }
}
