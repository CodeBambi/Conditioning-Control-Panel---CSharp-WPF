using System;
using System.Threading;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The ambient flash schedule: the timing half of WPF <c>FlashService.ScheduleNextFlash</c> /
    /// <c>SchedulerTimer_Tick</c> / <c>TriggerFlash</c> (ConditioningControlPanel/Services/Flash/FlashService.cs),
    /// shaped like <see cref="CoreSubliminal"/>. Deciding when lives here; drawing a burst is the
    /// head's <see cref="ShowProvider"/>. Unseeded, the schedule runs and draws nothing.
    /// </summary>
    public static class CoreFlash
    {
        /// <summary>Draw one ambient burst. Null when no head has a flash surface.</summary>
        public static volatile Action? ShowProvider;

        /// <summary>True while the head's previous burst is still going (WPF <c>_isBusy</c>).</summary>
        public static volatile Func<bool>? IsBusyProvider;

        private static readonly object Gate = new();
        private static readonly Random Rng = new();
        private static Timer? _timer;
        private static volatile bool _isRunning;

        public static bool IsRunning => _isRunning;

        /// <summary>
        /// Seconds until the next ambient flash. FlashFrequency is flashes per HOUR:
        /// 3600/frequency, jittered +-30% by <paramref name="roll"/> (uniform [0,1)), floored at 3 s.
        /// </summary>
        public static double NextIntervalSeconds(int frequency, double roll)
        {
            var baseInterval = 3600.0 / Math.Max(1, frequency);
            var variance = baseInterval * 0.3;
            return Math.Max(3, baseInterval + (roll * variance * 2 - variance));
        }

        /// <summary>
        /// WPF <c>SchedulerTimer_Tick</c> + <c>TriggerFlash</c> guards: a tick fires only while
        /// running, enabled, not busy, and not during a display change. A skipped tick still re-arms.
        /// </summary>
        public static bool ShouldFire(bool running, bool enabled, bool busy, bool displaySettling)
            => running && enabled && !busy && !displaySettling;

        /// <summary>Arm the schedule. Idempotent.</summary>
        public static void Start()
        {
            lock (Gate)
            {
                // Already running: re-arm anyway, so a tick that saw the feature disabled (and so did
                // not re-arm) cannot leave a started schedule dead.
                if (_isRunning) { ArmLocked(); return; }
                _isRunning = true;
                _timer ??= new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
                ArmLocked();
            }
            Log.Information("Flash schedule started");
        }

        /// <summary>Disarm the schedule. Idempotent; bursts already on screen finish on their own.</summary>
        public static void Stop()
        {
            lock (Gate)
            {
                if (!_isRunning) return;
                _isRunning = false;
                try { _timer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
            }
            Log.Information("Flash schedule stopped");
        }

        /// <summary>Re-roll the pending interval after FlashFrequency changes (WPF <c>RefreshSchedule</c>).</summary>
        public static void RefreshSchedule()
        {
            lock (Gate) ArmLocked();
        }

        private static void ArmLocked()
        {
            if (!_isRunning || !CoreSettings.Current.FlashEnabled) return;
            var seconds = NextIntervalSeconds(CoreSettings.Current.FlashFrequency, Rng.NextDouble());
            try { _timer?.Change(TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan); } catch { }
        }

        private static void OnTick(object? _)
        {
            CoreDispatch.Post(() =>
            {
                bool busy;
                try { busy = IsBusyProvider?.Invoke() ?? false; } catch { busy = false; }
                bool fire = ShouldFire(_isRunning, CoreSettings.Current.FlashEnabled, busy, DisplayChangeCoordinator.SpawnsSuppressed);
                // WPF FlashService :682: a do-not-disturb app in front skips the scheduled spawn only.
                if (fire && Services.UI.DndGuard.ShouldSuppressFlashes())
                {
                    Services.UI.DndGuard.LogSuppressionThrottled("flash");
                    fire = false;
                }
                if (fire)
                {
                    try { ShowProvider?.Invoke(); }
                    catch (Exception ex) { Log.Debug("Flash show provider failed: {Error}", ex.Message); }
                }
                lock (Gate) ArmLocked();
            });
        }
    }
}
