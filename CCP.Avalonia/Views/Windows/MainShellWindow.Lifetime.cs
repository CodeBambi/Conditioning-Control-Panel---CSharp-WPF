// The shell's own lifetime for deferred work: a one-shot timer or an in-flight request started by
// the shell must not outlive it. DispatcherTimer.RunOnce keeps its callback (and so the shell) in
// the dispatcher's timer list until it fires, and an awaited HttpClient call keeps its state
// machine (and so the shell) alive until the server answers; a closed shell stayed rooted for
// that long (ShellMemoryTests: the 5 s / 7 s server banner checks rooted every closed shell).
// Closing the shell stops the pending timers and cancels the requests.

using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private readonly CancellationTokenSource _closedCts = new();
        private readonly List<IDisposable> _openTimers = new();
        private bool _lifetimeHooked;

        /// <summary>Cancelled when the shell closes: pass it to any request the shell awaits.</summary>
        internal CancellationToken ClosedToken
        {
            get { HookLifetime(); return _closedCts.Token; }
        }

        /// <summary>DispatcherTimer.RunOnce, stopped if the shell closes first (never runs after Closed).</summary>
        internal void RunOnceWhileOpen(Action action, TimeSpan delay)
        {
            HookLifetime();
            if (_closedCts.IsCancellationRequested) return;
            IDisposable? handle = null;
            handle = DispatcherTimer.RunOnce(() =>
            {
                if (handle != null) _openTimers.Remove(handle);
                if (!_closedCts.IsCancellationRequested) action();
            }, delay);
            _openTimers.Add(handle);
        }

        private void HookLifetime()
        {
            if (_lifetimeHooked) return;
            _lifetimeHooked = true;
            Closed += (_, _) =>
            {
                _closedCts.Cancel();
                foreach (var timer in _openTimers.ToArray()) timer.Dispose();
                _openTimers.Clear();
            };
        }
    }
}
