using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

internal static class BubbleFace
{
    internal const float EdgeFadeStart = .80f;

    // The base sprite has transparent padding. Keep the picture inside its visible glass rim.
    internal static double Diameter(double spriteSize, bool tease) => spriteSize * (tease ? .86 : .60);

    internal static int FrameAt(long elapsedMs, double frameMs, int count, MotionLevel motion)
    {
        if (count <= 1 || frameMs <= 0 || motion == MotionLevel.Off) return 0;
        return (int)((Math.Max(0, elapsedMs) / frameMs) % count);
    }
}
