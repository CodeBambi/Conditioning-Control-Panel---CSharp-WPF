using System;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The session seam: whether the main engine is currently running.
    ///
    /// <para>A dozen ported feature cards gate their live-apply on this - "the user moved a
    /// slider; do I push it into the running service, or only save it?". On Windows the answer is
    /// <c>App.IsEngineRunning</c>, a plain flag <c>MainWindow.StartEngine</c>/<c>StopEngine</c>
    /// writes. The engine itself stays in the head; only the flag crosses.</para>
    ///
    /// <para>No change event, deliberately: the WPF original raises none and nothing subscribes to
    /// one. Every call site reads the flag at the moment it needs it.</para>
    ///
    /// <para>Unseeded answers <c>false</c>, which is the truth and not merely the safe answer: a
    /// head with no session engine is not running one. Callers land on their save-only branch and
    /// attempt no live-apply against a service that does not exist.</para>
    /// </summary>
    public static class CoreSession
    {
        public static volatile Func<bool>? IsEngineRunningProvider;

        /// <summary>True while the main engine is running - plain runs and AI sessions alike.</summary>
        public static bool IsEngineRunning
        {
            get { try { return IsEngineRunningProvider?.Invoke() ?? false; } catch { return false; } }
        }
    }
}
