using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Motion
{
    /// <summary>One keyframe: reach <paramref name="Value"/> at <paramref name="TimeMs"/>, riding
    /// <paramref name="Ease"/> from the previous keyframe (WPF EasingDoubleKeyFrame /
    /// LinearDoubleKeyFrame / DiscreteDoubleKeyFrame).</summary>
    public readonly record struct Keyframe(double TimeMs, double Value, EaseKind Ease);

    /// <summary>
    /// Samples a WPF-style keyframe track: segment i runs from keyframe i-1 (or the base value at
    /// time 0) to keyframe i. A Discrete keyframe holds the previous value until its own time.
    /// Past the last keyframe the track holds its last value (WPF FillBehavior.HoldEnd).
    /// </summary>
    public static class Keyframes
    {
        public static double Sample(IReadOnlyList<Keyframe> frames, double baseValue, double timeMs)
        {
            if (frames == null || frames.Count == 0) return baseValue;
            double prevT = 0, prevV = baseValue;
            foreach (var k in frames)
            {
                if (timeMs < k.TimeMs)
                {
                    if (k.Ease == EaseKind.Discrete) return prevV;
                    double span = k.TimeMs - prevT;
                    if (span <= 0) return k.Value;
                    double p = Math.Clamp((timeMs - prevT) / span, 0, 1);
                    return prevV + (k.Value - prevV) * Easings.Apply(k.Ease, p);
                }
                prevT = k.TimeMs;
                prevV = k.Value;
            }
            return prevV;
        }

        /// <summary>The house pop (LauncherWindow.Choreo Pop): scale to <paramref name="peak"/> by 35%
        /// of <paramref name="ms"/> on a quad ease-out, then an eased settle back to 1.</summary>
        public static Keyframe[] Pop(double peak, int ms = MotionTimings.PopMs) => new[]
        {
            new Keyframe(ms * MotionTimings.PopPeakAt, peak, EaseKind.QuadOut),
            new Keyframe(ms, 1.0, EaseKind.QuadInOut),
        };

        /// <summary>The depth release spring (HudPlank, the rail coins): past <paramref name="to"/> by
        /// <paramref name="overshootPx"/> at 60% of <paramref name="ms"/> (quad out), then settle on
        /// <paramref name="to"/> (quad in-out). For a release UP to rest the overshoot is negative.</summary>
        public static Keyframe[] ReleaseSpring(double to, double overshootPx, int ms) => new[]
        {
            new Keyframe(ms * 0.6, to + overshootPx, EaseKind.QuadOut),
            new Keyframe(ms, to, EaseKind.QuadInOut),
        };
    }
}
