using System;
using System.Collections.Generic;
using System.Windows;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>
/// Where the "+3:00" pops and how it moves, with no window, no dispatcher and no clock.
///
/// <para>Owner desk note (2026-09-23): the figure on the rail padlock was too easy to miss,
/// "especially under Brain Drain", and it should land "near where the user clicks". So the pop
/// now happens AT the thing that cost the time (the bubble that was popped, the flash that
/// showed), falling back to the cursor, and only then to the rail. It is big, outlined and
/// lives in its own topmost window so no overlay can sit over it.</para>
///
/// <para>Positions are PHYSICAL desktop pixels, the same space bubble centres and flash origins
/// use. Sizes of the figure itself are DIPs of the monitor it lands on.</para>
/// </summary>
public static class BookedPopLayout
{
    /// <summary>The pop window, in DIPs. Room for the figure, its rise and its sparks.</summary>
    public const double WindowWidthDip = 440;
    public const double WindowHeightDip = 280;

    /// <summary>Where the figure's centre sits inside the window, as a fraction of its height.
    /// Low in the window, so the rise has somewhere to go.</summary>
    public const double FigureAnchorY = 0.66;

    /// <summary>Pops born this close to a live one (physical px) stack instead of overlapping.</summary>
    public const double StackRadiusPx = 160;

    /// <summary>How far up each stacked pop sits above the one below it, in DIPs.</summary>
    public const double StackStepDip = 62;

    /// <summary>Never more than this many rungs: a long burst reuses the top rung rather than
    /// climbing off the screen.</summary>
    public const int MaxSlots = 4;

    /// <summary>How one pop looks and moves at a motion level.</summary>
    /// <param name="FontDip">Figure size.</param>
    /// <param name="RiseDip">How far it lifts over its life (0 = still).</param>
    /// <param name="TotalMs">Whole life, birth to gone.</param>
    /// <param name="ScaleInMs">The overshoot pop at birth (0 = born at full size).</param>
    /// <param name="Overshoot">BackEase amplitude of the scale-in.</param>
    /// <param name="FadeMs">Fade at the end of life (0 = it simply goes).</param>
    /// <param name="Particles">Sparks thrown at birth.</param>
    public readonly record struct PopPlan(double FontDip, double RiseDip, int TotalMs, int ScaleInMs,
        double Overshoot, int FadeMs, int Particles);

    /// <summary>
    /// Full: punch in with an overshoot, lift, fade, sparks. Reduced: no motion at all, the number
    /// holds and fades (opacity is not motion). Off: the number is there for ~1.2 s and then gone.
    /// The number is information at every level; only the manners change.
    /// </summary>
    public static PopPlan For(MotionLevel motion) => motion switch
    {
        MotionLevel.Off => new PopPlan(56, 0, 1200, 0, 0, 0, 0),
        MotionLevel.Reduced => new PopPlan(56, 0, 1400, 0, 0, 350, 0),
        _ => new PopPlan(58, 64, 1500, 260, 0.55, 520, 12),
    };

    /// <summary>The point the figure is born at: the thing that caused it, else the cursor, else
    /// nothing (the caller falls back to the rail padlock).</summary>
    public static Point? ResolveOrigin(Point? cause, Point? cursor) => cause ?? cursor;

    /// <summary>A pop that is still on screen: where it was born, when, and which rung it took.</summary>
    public readonly record struct LivePop(Point OriginPx, long BornMs, int LifeMs, int Slot);

    /// <summary>
    /// The lowest rung no live pop near <paramref name="originPx"/> is standing on. Pops far away
    /// (another bubble across the screen) do not push this one up. Past <see cref="MaxSlots"/>
    /// the top rung is reused.
    /// </summary>
    public static int PickSlot(IEnumerable<LivePop> live, Point originPx, long nowMs)
    {
        var taken = new bool[MaxSlots];
        foreach (var pop in live)
        {
            if (nowMs - pop.BornMs >= pop.LifeMs) continue;
            var dx = pop.OriginPx.X - originPx.X;
            var dy = pop.OriginPx.Y - originPx.Y;
            if (dx * dx + dy * dy > StackRadiusPx * StackRadiusPx) continue;
            if (pop.Slot >= 0 && pop.Slot < MaxSlots) taken[pop.Slot] = true;
        }
        for (var i = 0; i < MaxSlots; i++) if (!taken[i]) return i;
        return MaxSlots - 1;
    }

    /// <summary>
    /// Top-left of the pop window in physical px, so the figure's centre lands on the origin
    /// (raised by its rung), kept inside <paramref name="monitorPx"/> when one is known.
    /// <paramref name="scale"/> is that monitor's DPI scale (1.0 = 96 dpi).
    /// </summary>
    public static Point TopLeftPx(Point originPx, int slot, double scale, Rect? monitorPx)
    {
        if (!(scale > 0) || double.IsNaN(scale)) scale = 1.0;
        var w = WindowWidthDip * scale;
        var h = WindowHeightDip * scale;
        var x = originPx.X - w / 2;
        var y = originPx.Y - h * FigureAnchorY - Math.Max(0, slot) * StackStepDip * scale;

        if (monitorPx is { IsEmpty: false } m)
        {
            // Clamp the figure, not the window: a bubble popped at the very top edge still shows
            // its number on screen, a little lower than the bubble.
            x = Math.Clamp(x, m.Left - w * 0.25, Math.Max(m.Left - w * 0.25, m.Right - w * 0.75));
            var minY = m.Top - h * (FigureAnchorY - 0.2);
            var maxY = m.Bottom - h * (FigureAnchorY + 0.12);
            y = Math.Clamp(y, minY, Math.Max(minY, maxY));
        }
        return new Point(Math.Round(x), Math.Round(y));
    }
}
