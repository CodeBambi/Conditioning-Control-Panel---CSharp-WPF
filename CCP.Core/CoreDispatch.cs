using System;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The UI-thread seam. Engine code that must run a fragment on the head's UI thread asks here
    /// rather than through <c>System.Windows.Application.Current.Dispatcher</c>, which lives on a
    /// WPF type and therefore cannot exist in Core.
    ///
    /// <para>Two call sites need this today and they need the same thing: a bounded hop to the UI
    /// thread with a fall back to running in place. <c>SettingsService</c> retries a serialize
    /// there when it races a UI-thread edit, and <c>ModService.RunOnUi</c> marshals a collection
    /// mutation. Both already treat "no UI thread" as normal, because the test host and the Linux
    /// smoke runner have none.</para>
    ///
    /// <para>Deliberately two delegates and no interface, matching <see cref="CoreMods"/>: every
    /// head seeds them the same way, and an interface with one implementation is speculation.
    /// Promote when a second consumer needs something these cannot express.</para>
    ///
    /// <para><b>Unseeded is a supported state, not a bug.</b> Core initialises before any head
    /// attaches, tests never attach one, and the Linux smoke runner has no UI thread at all. Every
    /// member then behaves as "already on the right thread": <see cref="Post"/> runs the action in
    /// place and <see cref="Invoke{T}"/> calls the function directly. That is what both WPF call
    /// sites did for a null dispatcher, so the fallback is the existing contract, not a new one.</para>
    ///
    /// <para>Volatile for the same reason as <see cref="CoreMods"/>: the head seeds these on its
    /// startup thread while engine code may read them from background threads that never trigger
    /// the head's type initializer, and so get no acquire barrier.</para>
    /// </summary>
    public static class CoreDispatch
    {
        /// <summary>
        /// Runs the action on the head's UI thread and returns without waiting. Null when no head
        /// has seeded it; callers then run the action in place. A head that is shutting down should
        /// seed a provider that drops the action rather than throwing.
        /// </summary>
        public static volatile Action<Action>? PostProvider;

        /// <summary>
        /// Runs the function on the head's UI thread and waits up to the timeout for its result.
        /// Returns the default when the hop could not complete in time, which callers treat as
        /// "skip this one" rather than as an error. Null when no head has seeded it.
        /// </summary>
        public static volatile Func<Func<object?>, TimeSpan, (bool Completed, object? Result)>? InvokeProvider;

        /// <summary>True when a head has attached a UI thread. Callers rarely need this: the
        /// fallbacks below are correct without it.</summary>
        public static bool HasUiThread => PostProvider is not null || InvokeProvider is not null;

        /// <summary>
        /// Runs <paramref name="action"/> on the UI thread, or in place when there is none.
        /// Swallows provider faults: a wedged or torn-down head must never take an engine
        /// operation with it, which is the contract both WPF call sites already had.
        /// </summary>
        public static void Post(Action action)
        {
            if (action is null) return;
            var provider = PostProvider;
            if (provider is null) { action(); return; }
            try { provider(action); }
            catch { /* head is going away; the caller's work is not the head's to fail */ }
        }

        /// <summary>
        /// Runs <paramref name="func"/> on the UI thread and waits up to <paramref name="timeout"/>.
        /// Returns (true, result) when it completed, (false, default) when it timed out or the
        /// provider faulted. With no head attached it calls the function directly and reports
        /// completion, because there is no other thread it could belong to.
        /// </summary>
        public static (bool Completed, T? Result) Invoke<T>(Func<T> func, TimeSpan timeout)
        {
            if (func is null) return (false, default);
            var provider = InvokeProvider;
            if (provider is null) return (true, func());
            try
            {
                var (completed, result) = provider(() => func(), timeout);
                return completed ? (true, (T?)result) : (false, default);
            }
            catch { return (false, default); }
        }
    }
}
