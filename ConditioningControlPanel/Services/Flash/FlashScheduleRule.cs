using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The ambient flash scheduler's rhythm. ccp-bugs #1377: the engine starts FlashService
    /// whatever FlashEnabled says, and the scheduler used to stop for good when it found flashes
    /// switched off. Switching them on later went through Start(), which returned at once because
    /// the service was already running, so no flash ever came until the engine was stopped and
    /// started again. Now a switched-off scheduler keeps a slow recheck going, and Start() on a
    /// running service re-arms it. Pure so it can be tested.
    /// </summary>
    internal static class FlashScheduleRule
    {
        /// <summary>How often a switched-off scheduler looks again.</summary>
        internal static readonly TimeSpan DisabledRecheck = TimeSpan.FromSeconds(5);

        /// <summary>Shortest gap between two ambient flashes.</summary>
        internal const double MinIntervalSeconds = 3;

        /// <summary>Seconds to the next tick. <paramref name="unit01"/> is a random draw in [0, 1)
        /// for the +-30% variance around 3600 / flashes-per-hour.</summary>
        internal static double IntervalSeconds(bool enabled, int flashesPerHour, double unit01)
        {
            if (!enabled) return DisabledRecheck.TotalSeconds;
            var baseInterval = 3600.0 / Math.Max(1, flashesPerHour);
            var variance = baseInterval * 0.3;
            var interval = baseInterval + (unit01 * variance * 2 - variance);
            return Math.Max(MinIntervalSeconds, interval);
        }

        /// <summary>Does a scheduler tick show a flash? Never while switched off or busy.</summary>
        internal static bool ShouldFire(bool running, bool busy, bool enabled) => running && !busy && enabled;
    }
}
