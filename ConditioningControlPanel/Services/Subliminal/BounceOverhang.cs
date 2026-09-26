using System;

namespace ConditioningControlPanel.Services;

/// <summary>
/// How far a bouncing text's drawn box reaches past its measured box once its effect transform
/// (breathing, squash, corner pop, wobble, spin, tilt) is applied about its centre, per side.
///
/// Ticket 2026-09-24 (Issa): the bounce walls were the measured text box, but the glyphs are drawn
/// through a scale + rotate transform centred on that box, so a breathing or tilted line hit the
/// wall with part of itself already off the screen. The bounce now insets each wall by this
/// overhang. Pure so the geometry can be tested.
/// </summary>
public static class BounceOverhang
{
    /// <returns>Extra reach on each side (X) and on the top and bottom (Y), never negative.</returns>
    public static (double X, double Y) Of(double width, double height, double scaleX, double scaleY, double angleDeg)
    {
        if (!(width > 0) || !(height > 0)) return (0, 0);
        double sx = Math.Abs(double.IsFinite(scaleX) ? scaleX : 1.0);
        double sy = Math.Abs(double.IsFinite(scaleY) ? scaleY : 1.0);
        double rad = (double.IsFinite(angleDeg) ? angleDeg : 0.0) * Math.PI / 180.0;
        double c = Math.Abs(Math.Cos(rad)), s = Math.Abs(Math.Sin(rad));

        double w = width * sx, h = height * sy;
        double boxW = w * c + h * s;
        double boxH = w * s + h * c;

        return (Math.Max(0, (boxW - width) / 2.0), Math.Max(0, (boxH - height) / 2.0));
    }

    /// <summary>
    /// The overhang, capped so the text still has a range to travel in: a line spun upright can be
    /// taller than the screen, and a wall past the opposite wall would pin it in place.
    /// </summary>
    public static double Capped(double overhang, double span, double size)
        => Math.Max(0, Math.Min(overhang, (span - size) / 2.0));
}
