using System;
using System.Windows.Media;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// A frame-locked replacement for a DispatcherTimer driving an effect (perf pass, 2026-10-07).
    /// It ticks from <see cref="CompositionTarget.Rendering"/>, so every tick lands right before a
    /// frame is composed, and it skips whole frames to hold <see cref="Interval"/>: at 30 fps on a
    /// 60 Hz screen that is exactly every second frame, where a free-running 33 ms timer drifts
    /// against the refresh and shows as an uneven step (judder) however fast the PC is.
    /// Same surface as the timer it replaces: Interval, IsEnabled, Start, Stop, Tick.
    /// </summary>
    public sealed class FrameClock
    {
        /// <summary>Slack under the interval that still counts as due, so a tick a few ms early
        /// on a jittery frame is taken instead of slipping a whole frame late.</summary>
        private const double SlackMs = 4.0;

        private TimeSpan _lastTick = TimeSpan.MinValue;
        private TimeSpan _lastFrame = TimeSpan.MinValue;

        public TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(33);
        public bool IsEnabled { get; private set; }
        public event EventHandler? Tick;

        public void Start()
        {
            if (IsEnabled) return;
            IsEnabled = true;
            _lastTick = TimeSpan.MinValue;
            _lastFrame = TimeSpan.MinValue;
            CompositionTarget.Rendering += OnRendering;
        }

        public void Stop()
        {
            if (!IsEnabled) return;
            IsEnabled = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var now = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.FromMilliseconds(Environment.TickCount64);
            // Rendering can be raised more than once for the same frame.
            if (now == _lastFrame) return;
            _lastFrame = now;
            if (_lastTick != TimeSpan.MinValue &&
                (now - _lastTick).TotalMilliseconds < Interval.TotalMilliseconds - SlackMs) return;
            _lastTick = now;
            Tick?.Invoke(this, EventArgs.Empty);
        }
    }
}
