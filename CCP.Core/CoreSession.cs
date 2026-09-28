using System;
using System.Collections.Generic;

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

        // The running session's hold on the phrase pools, for the mod service's per-mod backup
        // (#906). WPF reads each through SessionEngine.Active, so "no active session" is folded
        // into every delegate: unseeded, or seeded with no session running, answers no-op /
        // false / null, which is what SessionEngine.Active == null gave. A future Core session
        // runner MUST seed these, or a mid-session mod switch backs up the session's phrases.

        /// <summary>Teach the session's pool snapshot about a user pool edit (property name).</summary>
        public static volatile Action<string?>? NoteUserPhrasePoolEdit;

        /// <summary>Re-assert the pools the session prescribes after a mod switch restored others.</summary>
        public static volatile Action? ReapplyPhrasePoolOverrides;

        /// <summary>The user's own pre-session pools (each may be null) while a session has replaced
        /// the live phrase pools with its own; null when no session is overriding them. One
        /// delegate, not four, so the backup reads "overriding" and the pools from ONE session
        /// snapshot - a StopSession between separate reads would bring back #906.</summary>
        public static volatile Func<(Dictionary<string, bool>? Subliminal,
                                     Dictionary<string, bool>? LockCard,
                                     Dictionary<string, bool>? BouncingText)?>? UserPhrasePoolsWhileOverriding;
    }
}
