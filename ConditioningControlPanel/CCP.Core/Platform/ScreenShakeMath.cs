using System;

namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Pure, deterministic math for the screen-shake effect, extracted from the WPF
/// <c>ScreenShakeService</c> so the clamp + amplitude curve can be unit-tested
/// without a UI dispatcher. All values are byte-for-byte the WPF formulas.
///
/// WPF ground truth (Services/UI/ScreenShakeService.cs):
///   - <c>TickIntervalMs = 30</c> (:24), <c>MaxOffsetPx = 28.0</c> at intensity 1.0 (:25).
///   - intensity clamped to 0..1; intensity &lt;= 0 or durationMs &lt;= 0 short-circuits (:46-47).
///   - peak amplitude = <c>MaxOffsetPx * intensity</c> (:84).
///   - per-tick offset = <c>(rng.NextDouble() * 2 - 1) * amplitude</c>, symmetric in
///     [-amp, +amp] (:202-203).
///
/// NOTE: the WPF service has NO decay curve. The amplitude is CONSTANT for the
/// whole duration and is zeroed only when the shake stops. Reproduced faithfully.
/// </summary>
public static class ScreenShakeMath
{
    /// <summary>Max per-axis offset in px at intensity 1.0 (WPF ScreenShakeService.cs:25).</summary>
    public const double MaxOffsetPx = 28.0;

    /// <summary>Jitter tick cadence in ms (WPF ScreenShakeService.cs:24).</summary>
    public const int TickIntervalMs = 30;

    /// <summary>Clamp intensity into the WPF-legal 0..1 band
    /// (WPF ScreenShakeService.cs:46, <c>Math.Clamp(intensity, 0, 1)</c>).</summary>
    public static double ClampIntensity(double intensity) => Math.Clamp(intensity, 0.0, 1.0);

    /// <summary>True when a shake request is a no-op: the clamped intensity is
    /// &lt;= 0 or the duration is &lt;= 0 (WPF ScreenShakeService.cs:46-47).</summary>
    public static bool IsNoOp(double intensity, int durationMs)
        => ClampIntensity(intensity) <= 0.0 || durationMs <= 0;

    /// <summary>Peak per-axis amplitude in px, from the clamped intensity
    /// (WPF ScreenShakeService.cs:84, <c>_amplitude = MaxOffsetPx * intensity</c>).</summary>
    public static double Amplitude(double intensity) => MaxOffsetPx * ClampIntensity(intensity);

    /// <summary>Map a uniform random value in [0,1) onto a symmetric per-axis
    /// offset in [-amplitude, +amplitude] (WPF ScreenShakeService.cs:202-203,
    /// <c>(rng.NextDouble() * 2 - 1) * amplitude</c>).</summary>
    public static double Offset(double amplitude, double random01)
        => (random01 * 2.0 - 1.0) * amplitude;
}
