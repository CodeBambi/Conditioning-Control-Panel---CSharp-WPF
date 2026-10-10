using System;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Flash;

/// <summary>
/// One flash on its way out after a pop. Owned by <see cref="Compositor.FlashLayer"/> once the
/// FlashService is finished with the flash (XP, hydra and the active list have all run).
/// </summary>
public sealed class FlashExitState
{
    public FlashExitStyle Style { get; init; }
    public double DurationSec { get; init; }
    /// <summary>Reduced motion: no swell, no spin, no flicker, fewer sparks.</summary>
    public bool Reduced { get; init; }
    public int Seed { get; init; }
    public double ElapsedSec;

    public double Progress => DurationSec <= 0 ? 1 : Math.Clamp(ElapsedSec / DurationSec, 0, 1);
    public bool Done => ElapsedSec >= DurationSec;
}

/// <summary>
/// What an exiting flash looks like at one moment, relative to its drawn rect.
/// Scale and rotation are about (0.5, PivotY) of the rect; OffsetY is a fraction of its height.
/// </summary>
public readonly record struct FlashExitSample(
    double ScaleX, double ScaleY, double RotationDeg, double PivotY, double OffsetY,
    double Alpha, double Whiten, double Glitch);

/// <summary>
/// Leave animations for a popped flash (owner, 2026-09-28, after "rn they just disappear"):
/// Pop swells and collapses with a spray of pink sparks, TV off squashes to a bright line then a
/// dot, Spiral spins inward, Melt stretches down and drips away, Glitch slices and flickers out.
/// Mix picks a different one each pop. Pure maths, drawn by FlashLayer, tested on its own.
///
/// <para>Motion setting: Off is a 120 ms fade whatever the pick. Reduced keeps the style but drops
/// the swell, the spin and the glitch flicker, and runs a quarter shorter.</para>
/// </summary>
public static class FlashExit
{
    /// <summary>The styles Mix draws from (everything except Mix and None).</summary>
    public static readonly FlashExitStyle[] Pool =
        { FlashExitStyle.Pop, FlashExitStyle.TvOff, FlashExitStyle.Spiral, FlashExitStyle.Melt, FlashExitStyle.Glitch };

    public static double DurationOf(FlashExitStyle style) => style switch
    {
        FlashExitStyle.Pop => 0.22,
        FlashExitStyle.TvOff => 0.24,
        FlashExitStyle.Spiral => 0.38,
        FlashExitStyle.Melt => 0.48,
        FlashExitStyle.Glitch => 0.28,
        _ => 0.12,   // the Motion Off fade
    };

    /// <summary>
    /// The concrete style for this pop, or null for the plain cut. Mix never repeats the last
    /// pick back to back, so a run of pops reads as a mix and not as one style twice.
    /// </summary>
    public static FlashExitStyle? Pick(FlashExitStyle setting, FlashExitStyle? last, Random rng)
    {
        if (setting == FlashExitStyle.None) return null;
        if (setting != FlashExitStyle.Mix) return setting;
        var choices = last is { } l && Array.IndexOf(Pool, l) >= 0
            ? Pool.Where(s => s != l).ToArray()
            : Pool;
        return choices[rng.Next(choices.Length)];
    }

    /// <summary>Start an exit. Motion Off turns every style into the short fade (Style = Mix marks it).</summary>
    public static FlashExitState Begin(FlashExitStyle style, MotionLevel motion, int seed)
    {
        if (motion == MotionLevel.Off)
            return new FlashExitState { Style = FlashExitStyle.Mix, DurationSec = DurationOf(FlashExitStyle.Mix), Seed = seed };
        bool reduced = motion == MotionLevel.Reduced;
        return new FlashExitState
        {
            Style = style,
            DurationSec = DurationOf(style) * (reduced ? 0.75 : 1.0),
            Reduced = reduced,
            Seed = seed,
        };
    }

    /// <summary>Advance the clock. True when something visible changed (the layer goes dirty).</summary>
    public static bool Step(FlashExitState s, double dt)
    {
        if (dt <= 0 || s.Done) return false;
        s.ElapsedSec += dt;
        return true;
    }

    private static double EaseIn(double t) => t * t;
    private static double EaseOut(double t) => 1 - (1 - t) * (1 - t);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public static FlashExitSample Sample(FlashExitState s)
    {
        double p = s.Progress;
        switch (s.Style)
        {
            case FlashExitStyle.Pop:
            {
                double scale;
                if (s.Reduced) scale = 1 - EaseIn(p);
                else if (p < 0.3) scale = 1 + 0.08 * EaseOut(p / 0.3);
                else scale = 1.08 * (1 - EaseIn((p - 0.3) / 0.7));
                double alpha = p < 0.6 ? 1 : 1 - (p - 0.6) / 0.4;
                return new(scale, scale, 0, 0.5, 0, alpha, 0, 0);
            }
            case FlashExitStyle.TvOff:
            {
                // Squash to a bright line, then pinch the line to a dot.
                if (p < 0.55)
                {
                    double q = p / 0.55;
                    return new(1 + 0.04 * q, Lerp(1, 0.02, EaseIn(q)), 0, 0.5, 0, 1, 0.9 * q, 0);
                }
                double r = (p - 0.55) / 0.45;
                return new(Lerp(1.04, 0.0, EaseIn(r)), 0.02, 0, 0.5, 0, 1 - r * r * r, 0.9, 0);
            }
            case FlashExitStyle.Spiral:
            {
                double e = EaseIn(p);
                double dir = (s.Seed & 1) == 0 ? 1 : -1;
                double rot = s.Reduced ? 0 : 300 * e * dir;
                double alpha = p < 0.7 ? 1 : 1 - (p - 0.7) / 0.3;
                return new(1 - e, 1 - e, rot, 0.5, 0, alpha, 0, 0);
            }
            case FlashExitStyle.Melt:
            {
                double e = EaseIn(p);
                double stretch = s.Reduced ? 0.3 : 0.7;
                return new(1 - 0.15 * e, 1 + stretch * e, 0, 0.0, 0.35 * e, 1 - e, 0, 0);
            }
            case FlashExitStyle.Glitch:
            {
                double alpha;
                if (p < 0.5 || s.Reduced) alpha = 1 - EaseIn(p);
                else alpha = ((int)(p * 20) % 2 == 0) ? (1 - p) : 0.25 * (1 - p);   // a few hard flickers out
                return new(1, 1 - 0.2 * p, 0, 0.5, 0, alpha, 0, p);
            }
            default:
                return new(1, 1, 0, 0.5, 0, 1 - p, 0, 0);
        }
    }

    /// <summary>How many sparks this exit throws (Pop only).</summary>
    public static int SparkCount(FlashExitState s) => s.Style == FlashExitStyle.Pop ? (s.Reduced ? 5 : 10) : 0;

    /// <summary>
    /// Spark <paramref name="i"/>: direction (unit), distance as a multiple of the flash's half size,
    /// radius in px and alpha. Angles are jittered by the seed so no two pops spray the same.
    /// </summary>
    public static (double Dx, double Dy, double Distance, double RadiusPx, double Alpha) Spark(FlashExitState s, int i)
    {
        int n = Math.Max(1, SparkCount(s));
        double jitter = ((s.Seed * 7919 + i * 104729) & 0xFFFF) / 65535.0 - 0.5;
        double angle = 2 * Math.PI * (i + 0.5 * jitter) / n;
        double p = s.Progress;
        double dist = 0.55 + 0.75 * EaseOut(p) * (1 + 0.3 * jitter);
        return (Math.Cos(angle), Math.Sin(angle), dist, (3 + 3 * Math.Abs(jitter) * 2) * (1 - p), 1 - p);
    }

    /// <summary>
    /// Glitch: horizontal offset of slice <paramref name="k"/> as a fraction of the flash width.
    /// Jumps a dozen times over the exit rather than sliding, which is what reads as a glitch.
    /// </summary>
    public static double SliceOffset(FlashExitState s, int k)
    {
        var sample = Sample(s);
        if (sample.Glitch <= 0) return 0;
        int step = (int)(s.Progress * 12);
        double wave = Math.Sin(s.Seed * 13.1 + k * 7.1 + step * 3.7);
        return wave * sample.Glitch * 0.12;
    }
}
