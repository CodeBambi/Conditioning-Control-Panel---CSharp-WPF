using System;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Shape applied to a 0..1 intensity-ramp progress value (suggestion #660).
    /// Linear preserves the original behaviour; the others let a ramp start gently
    /// and finish hard (or the reverse) WITHOUT changing the ramp's start/end values
    /// or its length — only the path between the endpoints changes.
    ///
    /// Persisted by ordinal (default = Linear = 0) in AppSettings.RampCurve — old
    /// settings files that lack the field deserialize to Linear, so behaviour is
    /// unchanged on upgrade. A nullable per-session override lives on
    /// SessionSettings.RampCurve (null = fall back to the global setting).
    /// </summary>
    public enum RampCurve
    {
        Linear,
        EaseIn,
        EaseOut,
        SCurve,
        Exponential
    }
}

namespace ConditioningControlPanel.Helpers
{
    using ConditioningControlPanel.Models;

    /// <summary>
    /// Re-shapes a linear intensity-ramp progress value. Shared by BOTH ramp
    /// systems: the manual "Intensity Ramp" (MainWindow.StartStop.RampTimer_Tick)
    /// and preset/session ramps (SessionEngine.UpdateRampingValues). The two are
    /// mutually exclusive by design (#444) — this helper only shapes the progress
    /// each one already computes, it does not couple them.
    /// </summary>
    public static class RampCurves
    {
        /// <summary>
        /// Maps a linear 0..1 <paramref name="progress"/> to an eased 0..1 value
        /// according to <paramref name="curve"/>. Endpoints are always preserved
        /// (0 -> 0, 1 -> 1) so a ramp still reaches exactly its configured maximum
        /// at completion; only the path between changes. Input is clamped to 0..1.
        /// </summary>
        public static double ApplyCurve(double progress, RampCurve curve)
        {
            var p = Math.Clamp(progress, 0.0, 1.0);
            switch (curve)
            {
                case RampCurve.EaseIn:
                    // Slow start, strong finish.
                    return p * p * p;

                case RampCurve.EaseOut:
                    // Fast start, gentle finish.
                    var inv = 1.0 - p;
                    return 1.0 - inv * inv * inv;

                case RampCurve.SCurve:
                    // Smoothstep: gentle at both ends, quickest through the middle.
                    return p * p * (3.0 - 2.0 * p);

                case RampCurve.Exponential:
                    // Very back-loaded: almost nothing until late, then a hard surge.
                    const double k = 3.0;
                    return (Math.Exp(k * p) - 1.0) / (Math.Exp(k) - 1.0);

                case RampCurve.Linear:
                default:
                    return p;
            }
        }
    }
}
