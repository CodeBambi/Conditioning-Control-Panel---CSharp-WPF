using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>
/// PORTED from ConditioningControlPanel/Services/AmbientBubbleMotion.cs (the pure half).
/// Bubbles v2 (Back Room prizes): the picker in the Bubbles options stores a
/// <see cref="BubbleMotionStyle"/>; every spawn resolves it here to ONE concrete style for that
/// bubble. Ownership comes from the prize grants and never from settings, so a synced profile
/// carrying a style the account does not own simply floats up. MotionLevel is honoured on the way
/// through: Off is plain FloatUp whatever the picker says, Reduced halves the v2 speed and the
/// spiral turns. The head supplies the grants (Platform/PrizeOwnership).
/// </summary>
public static class AmbientBubbleMotion
{
    /// <summary>WPF PrizeGrants.BubbleRain / BubbleSpiralIn: wire ids, spelled as the server does.</summary>
    public const string RainGrant = "fx.bubble.rain";
    public const string SpiralInGrant = "fx.bubble.spiral_in";

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
}

/// <summary>
/// PORTED from WPF Services/AmbientBubbleMotion.cs. Spiral In path maths, in DIPs and steps (the
/// bubble step runs at ~30 fps; <c>ts</c> is the field's time scale, 1 normally). A bubble starts
/// on a ring around the screen centre and advances its angle while the radius shrinks at a
/// constant rate, so the angle step is sized to land the requested number of turns exactly when
/// the radius reaches the core. The fade runs over the last <see cref="FadeBandDip"/> DIPs of
/// radius and hits 0 at the core.
/// </summary>
public static class SpiralInPath
{
    public const double StartRadiusFrac = 0.42;
    public const double CoreDip = 24;
    public const double FadeBandDip = 110;
    public const double FullTurns = 2.5;
    public const double RadialPerSpeed = 0.55;

    public readonly record struct State(double Radius, double Angle, double Fade, bool Done);

    public static double StartRadius(double shortDim) => Math.Max(CoreDip * 4, shortDim * StartRadiusFrac);

    public static double Turns(MotionLevel level) => level == MotionLevel.Reduced ? FullTurns * 0.5 : FullTurns;

    public static State Start(double startRadius, double startAngle) => new(startRadius, startAngle, 1.0, false);

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
