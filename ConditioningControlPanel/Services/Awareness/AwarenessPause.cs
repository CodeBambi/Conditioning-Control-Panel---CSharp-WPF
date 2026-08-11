using System;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// "Not right now" — the privacy panel's pause button, held in one place so every reader agrees.
    ///
    /// <para>A pause is a hard drop at the privacy layer (<see cref="AwarenessPrivacyRules.Evaluate"/>
    /// returns <see cref="AwarenessDropReason.Paused"/>), so while it is running nothing is observed,
    /// nothing reaches the ledger and nothing is said. It is deliberately NOT persisted: it is the
    /// quick "give me an hour" control, and a pause that silently survived a reboot would be a
    /// capability the user believes they switched back on. Restarting the app resumes her, and the
    /// panel's copy says so.</para>
    ///
    /// <para>Process-wide static rather than an instance because it is read from the poll, from the
    /// panel and from the legacy service, all of which are singletons here, and because a pause that
    /// only some of them can see is worse than none.</para>
    /// </summary>
    public static class AwarenessPause
    {
        private static readonly object Lock = new();
        private static DateTime? _until;

        /// <summary>What the panel's button asks for.</summary>
        public static readonly TimeSpan DefaultDuration = TimeSpan.FromHours(1);

        /// <summary>The local time the pause lifts, or null when she is not paused.</summary>
        public static DateTime? PausedUntil
        {
            get { lock (Lock) return _until; }
        }

        /// <summary>True while a pause is in force at <paramref name="now"/> (defaults to local now).</summary>
        public static bool IsPaused(DateTime? now = null)
        {
            lock (Lock)
            {
                if (_until == null) return false;
                if ((now ?? DateTime.Now) < _until.Value) return true;
                _until = null;             // expired — clear it so the next read is cheap
                return false;
            }
        }

        /// <summary>How long is left, or <see cref="TimeSpan.Zero"/> when she is not paused.</summary>
        public static TimeSpan Remaining(DateTime? now = null)
        {
            lock (Lock)
            {
                if (_until == null) return TimeSpan.Zero;
                var left = _until.Value - (now ?? DateTime.Now);
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Pauses for <paramref name="duration"/>. A non-positive duration resumes instead of pausing
        /// forever, and a second pause extends rather than shortens — pressing the button twice may
        /// never leave the user less protected than pressing it once.
        /// </summary>
        public static void Pause(TimeSpan duration, DateTime? now = null)
        {
            if (duration <= TimeSpan.Zero) { Resume(); return; }

            var end = (now ?? DateTime.Now) + duration;
            lock (Lock)
            {
                if (_until == null || end > _until.Value) _until = end;
            }

            App.Logger?.Information("Awareness: paused for {Minutes} minute(s)", (int)duration.TotalMinutes);
        }

        /// <summary>Lifts the pause immediately.</summary>
        public static void Resume()
        {
            bool was;
            lock (Lock)
            {
                was = _until != null;
                _until = null;
            }
            if (was) App.Logger?.Information("Awareness: pause lifted");
        }
    }
}
