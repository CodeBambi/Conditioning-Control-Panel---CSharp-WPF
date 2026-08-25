using System;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// How the manual "Intensity Ramp" resolves the factor it applies to every linked
    /// feature (community request: range ramping + wind-down).
    ///
    /// <para><see cref="Multiplier"/> is the ORIGINAL behaviour and stays the default:
    /// the factor climbs from 1x to <c>AppSettings.SchedulerMultiplier</c> (capped 3x),
    /// so every linked dial only ever grows. Old settings files and presets that lack
    /// the field deserialize to this, byte-for-byte unchanged.</para>
    ///
    /// <para><see cref="Range"/> interpolates between two PERCENTAGES OF THE FEATURE'S
    /// OWN CONFIGURED VALUE (<c>AppSettings.RampStartPercent</c> ->
    /// <c>AppSettings.RampEndPercent</c>, 0..300, default 100 -> 100 = a no-op). Because
    /// it resolves to the same "factor times the feature's base value" the multiplier path
    /// already used, every linked feature keeps working with no per-feature code - and an
    /// END BELOW START (e.g. 100% -> 10%) gives the wind-down the wakener-audio users
    /// asked for, which the multiplier mode's 1x floor can never express.</para>
    /// </summary>
    public enum RampMode
    {
        Multiplier,
        Range
    }
}

namespace ConditioningControlPanel.Helpers
{
    using ConditioningControlPanel.Models;

    /// <summary>
    /// Resolves the manual intensity ramp's per-tick factor. Pure math, no WPF and no
    /// service access, so it is unit-testable (Tests/ConditioningControlPanel.Tests/
    /// RampFactorTests.cs) and shared by the runtime tick
    /// (MainWindow.StartStop.RampTimer_Tick) and the control's live curve preview
    /// (Features/IntensityRampFeatureControl.xaml.cs).
    ///
    /// <para>The per-session ramp (SessionEngine.UpdateRampingValues) is NOT routed
    /// through here: sessions already ramp start -> end in each feature's own units and
    /// the two systems deliberately stand down for each other (#444). Only the curve is
    /// shared, via <see cref="RampCurves.ApplyCurve"/>.</para>
    /// </summary>
    public static class RampMath
    {
        /// <summary>Lowest legal Range-mode endpoint, as a percent of the feature's own value.</summary>
        public const double MinRangePercent = 0.0;

        /// <summary>Highest legal Range-mode endpoint, as a percent of the feature's own value.</summary>
        public const double MaxRangePercent = 300.0;

        /// <summary>
        /// The factor to multiply each linked feature's BASE value by at linear
        /// <paramref name="progress"/> (0..1, clamped).
        ///
        /// <para>Multiplier mode: <c>1 + (maxMultiplier - 1) * curve(t)</c> - identical to the
        /// pre-range code, so a legacy preset ramps exactly as it always did.</para>
        /// <para>Range mode: <c>(start + (end - start) * curve(t)) / 100</c> - which can drop
        /// below 1.0, the whole point of a wind-down.</para>
        /// </summary>
        public static double ResolveFactor(RampMode mode, double progress, RampCurve curve,
                                           double maxMultiplier, double startPercent, double endPercent)
        {
            var eased = RampCurves.ApplyCurve(progress, curve);

            if (mode == RampMode.Range)
            {
                var start = Math.Clamp(startPercent, MinRangePercent, MaxRangePercent);
                var end = Math.Clamp(endPercent, MinRangePercent, MaxRangePercent);
                return (start + (end - start) * eased) / 100.0;
            }

            // Legacy path, left un-clamped on purpose: AppSettings.SchedulerMultiplier already
            // clamps to 1..3 in its setter, and re-clamping here would be a second place to
            // drift away from "unchanged".
            return 1.0 + (maxMultiplier - 1.0) * eased;
        }

        /// <summary>
        /// Convenience overload reading the live settings object. Used by the runtime tick and
        /// the preview so neither has to remember which four fields feed the formula.
        /// </summary>
        public static double ResolveFactor(AppSettings settings, double progress)
        {
            if (settings == null) return 1.0;
            return ResolveFactor(settings.RampMode, progress, settings.RampCurve,
                                 settings.SchedulerMultiplier,
                                 settings.RampStartPercent, settings.RampEndPercent);
        }
    }
}
