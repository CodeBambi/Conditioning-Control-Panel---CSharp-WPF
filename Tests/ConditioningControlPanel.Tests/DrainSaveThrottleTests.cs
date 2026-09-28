using System;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests
{
    /// <summary>
    /// #1311: the Brain Parasite drain wrote settings.json on every 2 s tick. The drain keeps its
    /// cadence; the write is throttled to one per <see cref="DrainSaveThrottle.SaveInterval"/>.
    /// </summary>
    public class DrainSaveThrottleTests
    {
        private static readonly DateTime T0 = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void FirstDrainOfTheRunSaves()
            => Assert.True(DrainSaveThrottle.ShouldSave(null, T0, reachedZero: false));

        [Fact]
        public void TicksInsideTheIntervalDoNotSave()
        {
            // The real cadence: a tick every 2 s must not write on each one.
            for (int s = 2; s < 30; s += 2)
                Assert.False(DrainSaveThrottle.ShouldSave(T0, T0.AddSeconds(s), reachedZero: false));
        }

        [Fact]
        public void SavesOnceTheIntervalHasPassed()
            => Assert.True(DrainSaveThrottle.ShouldSave(T0, T0 + DrainSaveThrottle.SaveInterval, reachedZero: false));

        [Fact]
        public void ReachingZeroSavesAtOnce()
            => Assert.True(DrainSaveThrottle.ShouldSave(T0, T0.AddSeconds(2), reachedZero: true));

        [Fact]
        public void AClockSteppedBackDoesNotStallSaves()
            => Assert.True(DrainSaveThrottle.ShouldSave(T0, T0.AddMinutes(-5), reachedZero: false));

        [Fact]
        public void AHourOfDrainWritesAtMostOncePerInterval()
        {
            DateTime? last = null;
            int writes = 0;
            for (int s = 0; s < 3600; s += 2)
            {
                var now = T0.AddSeconds(s);
                if (DrainSaveThrottle.ShouldSave(last, now, reachedZero: false)) { writes++; last = now; }
            }
            // Was 1800 writes an hour (one per tick).
            Assert.Equal(120, writes);
        }
    }
}
