using System;

namespace ConditioningControlPanel.Motion
{
    /// <summary>
    /// The pure half of a head's frame clock (CCP.Avalonia Controls/Fx/FrameClock.cs): given frame
    /// times, say which frames are due so the clock holds its interval on WHOLE frames
    /// (skip-to-interval). Twin of the WPF 7.1.5 FrameClock.OnRendering body, numbers exact: 4 ms slack, a
    /// repeated frame time is ignored.
    /// </summary>
    public sealed class FrameGate
    {
        /// <summary>Slack under the interval that still counts as due, so a tick a few ms early
        /// on a jittery frame is taken instead of slipping a whole frame late.</summary>
        public const double SlackMs = 4.0;

        private TimeSpan _lastTick = TimeSpan.MinValue;
        private TimeSpan _lastFrame = TimeSpan.MinValue;

        public TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(33);

        public void Reset()
        {
            _lastTick = TimeSpan.MinValue;
            _lastFrame = TimeSpan.MinValue;
        }

        /// <summary>True when the frame at <paramref name="now"/> should tick.</summary>
        public bool Due(TimeSpan now)
        {
            // A frame can be reported more than once for the same render time.
            if (now == _lastFrame) return false;
            _lastFrame = now;
            if (_lastTick != TimeSpan.MinValue &&
                (now - _lastTick).TotalMilliseconds < Interval.TotalMilliseconds - SlackMs) return false;
            _lastTick = now;
            return true;
        }
    }
}
