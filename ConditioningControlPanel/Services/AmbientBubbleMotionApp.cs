using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Services;

/// <summary>
/// The live reads of the ambient bubble styles (UI thread, at spawn / repaint). The rules
/// themselves (<see cref="AmbientBubbleMotion"/>, the Spiral In path) live once in CCP.Core;
/// these four lines are the WPF app's own answers and stayed here when the rules moved.
/// </summary>
public static class AmbientBubbleMotionApp
{
    public static bool RainOwned => PrizeGrants.IsGranted(PrizeGrants.BubbleRain);
    public static bool SpiralInOwned => PrizeGrants.IsGranted(PrizeGrants.BubbleSpiralIn);
    /// <summary>Either ambient prize style. One spelling, in <see cref="V2Badges"/>, because the
    /// dashboard tile and the side rail's chips light off the very same question.</summary>
    public static bool AnyV2Owned => V2Badges.BubbleOwned();

    /// <summary>The concrete style for one ambient spawn, from the current picker + grants + motion level.</summary>
    public static BubbleMotionStyle RollForSpawn(Random random) =>
        AmbientBubbleMotion.Resolve(App.Settings?.Current?.BubbleMotionStyle ?? BubbleMotionStyle.FloatUp,
                RainOwned, SpiralInOwned, MotionFx.Level, random.NextDouble());
}
