using System;
using System.Windows;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Flash;

/// <summary>Bubble-to-flash geometry in physical desktop pixels.</summary>
internal static class FlashDelivery
{
    internal const double Duration = 0.32;

    internal static (Rect Rect, double Alpha, double Progress) Sample(
        Point origin, double diameter, Rect target, double seconds, MotionLevel motion)
    {
        if (motion == MotionLevel.Off) return (target, 1, 1);
        if (motion == MotionLevel.Reduced)
            return (target, Math.Clamp(seconds / 0.18, 0, 1), 1);
        var t = Math.Clamp(seconds / Duration, 0, 1);
        var p = 1 - Math.Pow(1 - t, 3);
        var d = Math.Max(1, diameter);
        var start = new Rect(origin.X - d / 2, origin.Y - d / 2, d, d);
        return (new Rect(start.X + (target.X - start.X) * p,
            start.Y + (target.Y - start.Y) * p,
            start.Width + (target.Width - start.Width) * p,
            start.Height + (target.Height - start.Height) * p), 1, p);
    }
}
