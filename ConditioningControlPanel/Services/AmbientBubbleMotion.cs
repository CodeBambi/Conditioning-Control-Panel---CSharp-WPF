using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Bubbles v2 (Back Room prizes): the pure half of the ambient bubble motion choice.
///
/// The picker in the Bubbles options stores a <see cref="BubbleMotionStyle"/>; every spawn
/// resolves it here to ONE concrete style for that bubble. Ownership comes from
/// <see cref="PrizeGrants"/> and never from settings, so a synced profile carrying a style the
/// account does not own simply floats up. MotionLevel is honoured on the way through: Off is
/// plain FloatUp whatever the picker says, Reduced halves the v2 speed and the spiral turns.
///
/// Nothing in here touches WPF; the live wrappers at the bottom are the only App reads.
/// </summary>
public static class AmbientBubbleMotion
{
    /// <summary>True when the concrete style is usable with the given grants (Mix needs any v2 style).</summary>
    public static bool IsOwned(BubbleMotionStyle style, bool rainOwned, bool spiralOwned) => style switch
    {
        BubbleMotionStyle.FloatUp => true,
        BubbleMotionStyle.Rain => rainOwned,
        BubbleMotionStyle.SpiralIn => spiralOwned,
        BubbleMotionStyle.Mix => rainOwned || spiralOwned,
        _ => false,
    };

    /// <summary>
    /// Resolve the picked style to the concrete motion of one bubble. <paramref name="roll"/> is a
    /// uniform 0..1 draw used only by Mix, which picks with equal weight among FloatUp plus the
    /// owned v2 styles. Never returns Mix.
    /// </summary>
    public static BubbleMotionStyle Resolve(BubbleMotionStyle picked, bool rainOwned, bool spiralOwned,
                                            MotionLevel level, double roll)
    {
        if (level == MotionLevel.Off) return BubbleMotionStyle.FloatUp;
        switch (picked)
        {
            case BubbleMotionStyle.Rain:
                return rainOwned ? BubbleMotionStyle.Rain : BubbleMotionStyle.FloatUp;
            case BubbleMotionStyle.SpiralIn:
                return spiralOwned ? BubbleMotionStyle.SpiralIn : BubbleMotionStyle.FloatUp;
            case BubbleMotionStyle.Mix:
            {
                int n = 1 + (rainOwned ? 1 : 0) + (spiralOwned ? 1 : 0);
                int idx = (int)Math.Clamp(Math.Floor(Math.Clamp(roll, 0.0, 0.999999) * n), 0, n - 1);
                if (idx == 0) return BubbleMotionStyle.FloatUp;
                if (rainOwned && idx == 1) return BubbleMotionStyle.Rain;
                return spiralOwned ? BubbleMotionStyle.SpiralIn : BubbleMotionStyle.Rain;
            }
            default:
                return BubbleMotionStyle.FloatUp;
        }
    }

    /// <summary>Speed multiplier for the v2 styles at a motion level (FloatUp is never scaled).</summary>
    public static double SpeedMult(MotionLevel level) => level == MotionLevel.Reduced ? 0.5 : 1.0;

    // ---- live reads (UI thread, at spawn / repaint) ----

    public static bool RainOwned => PrizeGrants.IsGranted(PrizeGrants.BubbleRain);
    public static bool SpiralInOwned => PrizeGrants.IsGranted(PrizeGrants.BubbleSpiralIn);
    public static bool AnyV2Owned => RainOwned || SpiralInOwned;

    /// <summary>The concrete style for one ambient spawn, from the current picker + grants + motion level.</summary>
    public static BubbleMotionStyle RollForSpawn(Random random) =>
        Resolve(App.Settings?.Current?.BubbleMotionStyle ?? BubbleMotionStyle.FloatUp,
                RainOwned, SpiralInOwned, MotionFx.Level, random.NextDouble());
}

/// <summary>
/// Spiral In path maths, in DIPs and frames (the bubble tick runs at ~30 fps; <c>ts</c> is the
/// field's time scale, 1 normally). A bubble starts on a ring around the screen centre and
/// advances its angle while the radius shrinks at a constant rate, so the angle step is sized
/// to land the requested number of turns exactly when the radius reaches the core. The fade
/// runs over the last <see cref="FadeBandDip"/> DIPs of radius and hits 0 at the core.
/// </summary>
public static class SpiralInPath
{
    /// <summary>Start radius as a fraction of the shorter screen dimension.</summary>
    public const double StartRadiusFrac = 0.42;
    /// <summary>The core: reaching this radius ends the path.</summary>
    public const double CoreDip = 24;
    /// <summary>Radius band over which the bubble fades before the core.</summary>
    public const double FadeBandDip = 110;
    /// <summary>Turns over the whole path at MotionLevel.Full.</summary>
    public const double FullTurns = 2.5;
    /// <summary>Radial DIPs per frame per unit of the bubble's natural speed (1..2 px/frame).</summary>
    public const double RadialPerSpeed = 0.55;

    public readonly record struct State(double Radius, double Angle, double Fade, bool Done);

    public static double StartRadius(double shortDim) => Math.Max(CoreDip * 4, shortDim * StartRadiusFrac);

    public static double Turns(MotionLevel level) => level == MotionLevel.Reduced ? FullTurns * 0.5 : FullTurns;

    public static State Start(double startRadius, double startAngle) => new(startRadius, startAngle, 1.0, false);

    /// <summary>One frame: radius in by <paramref name="radialPerFrame"/> (times <paramref name="ts"/>),
    /// angle on by the share of the total turns that frame represents, direction +1 or -1.</summary>
    public static State Step(State s, double startRadius, double radialPerFrame, double turns, double direction, double ts)
    {
        if (s.Done) return s;
        double radial = Math.Max(0.01, radialPerFrame);
        double r = Math.Max(CoreDip, s.Radius - radial * ts);
        double totalFrames = Math.Max(1.0, (startRadius - CoreDip) / radial);
        double dir = direction < 0 ? -1.0 : 1.0;
        double a = s.Angle + dir * (2.0 * Math.PI * turns / totalFrames) * ts;
        double fade = Math.Clamp((r - CoreDip) / FadeBandDip, 0.0, 1.0);
        bool done = r <= CoreDip + 1e-9;
        return new State(r, a, fade, done);
    }
}
