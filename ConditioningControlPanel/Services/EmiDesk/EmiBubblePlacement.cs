using System;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// Which side of Emi her speech bubble goes, and where it lands, so the whole line stays inside
/// the work area of the monitor she is on. Pure: every number is in the desk window's own
/// local DIPs (0 = the window's left edge), the caller converts the work area into that space.
///
/// Ticket 2026-09-24 (Issa): docked at the left edge of her monitor, the line ran off the
/// screen ("ords! your eyes will do"). The work area used to be converted with Left x scale,
/// which is only right when the window's DPI and the monitor's agree; the caller now asks the
/// window itself (PointFromScreen), and this helper keeps the side-and-clamp rule in one place.
/// </summary>
public static class EmiBubblePlacement
{
    /// <summary>Gap kept between the bubble and the window or screen edge, in DIPs.</summary>
    public const double EdgeGap = 2.0;

    /// <param name="rightLeft">Bubble left when it sits on her right (the default side).</param>
    /// <param name="flippedLeft">Bubble left when it sits on her left.</param>
    /// <param name="width">The bubble's measured width.</param>
    /// <param name="preferFlip">True when something (her book) wants the bubble on her left.</param>
    /// <param name="windowWidth">The desk window's width; the bubble cannot draw past it.</param>
    /// <param name="workLeft">Monitor work area left edge, window-local. -Infinity when unknown.</param>
    /// <param name="workRight">Monitor work area right edge, window-local. +Infinity when unknown.</param>
    public static (double Left, bool Flip) Resolve(
        double rightLeft, double flippedLeft, double width, bool preferFlip,
        double windowWidth, double workLeft, double workRight)
    {
        bool flip = preferFlip;

        // How far each side hangs off the work area, 0 when it fits.
        double spillRight = double.IsInfinity(workRight) ? 0 : Math.Max(0, rightLeft + width - workRight);
        double spillLeft = double.IsInfinity(workLeft) ? 0 : Math.Max(0, workLeft - flippedLeft);

        // The preference only holds while its side fits better than the other one.
        if (flip && spillLeft > spillRight) flip = false;
        else if (!flip && spillRight > 0 && spillRight >= spillLeft) flip = true;

        double left = flip ? flippedLeft : rightLeft;

        // Clamp into the window first, then into the work area. When both cannot hold, the
        // LEFT edge wins: a line whose start you can read is recoverable, one that starts off
        // screen is not.
        double lo = EdgeGap;
        double hi = Math.Max(lo, windowWidth - width - EdgeGap);
        if (!double.IsInfinity(workLeft)) lo = Math.Max(lo, workLeft + EdgeGap);
        if (!double.IsInfinity(workRight)) hi = Math.Min(hi, workRight - width - EdgeGap);
        left = hi < lo ? lo : Math.Max(lo, Math.Min(hi, left));

        return (left, flip);
    }
}
