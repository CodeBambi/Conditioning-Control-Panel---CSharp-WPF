using System;

namespace ConditioningControlPanel.Services.Flash;

/// <summary>
/// Flashes v2 wave 2: the one place that decides how round a flash GIF's corners are. Pure maths
/// so the three render paths (compositor, glow WPF, no-glow WPF/solid host) cannot drift apart and
/// so the rule is unit-testable without WPF or Skia.
///
/// The rule: a glow flash has always worn a 12 px card radius, so that stays its baseline. Turning
/// the Rounded corners switch on - which only counts while the account owns a Flashes v2 grant -
/// raises every flash to 14 px, glow or not. The radius never exceeds a quarter of the shorter
/// side, so a small or thin GIF gets a soft edge instead of a lozenge.
/// </summary>
public static class FlashCorners
{
    /// <summary>Radius a glow flash has always drawn at, in px at 96 dpi.</summary>
    public const double GlowBaseDip = 12.0;

    /// <summary>Radius the Rounded corners switch asks for, in px at 96 dpi.</summary>
    public const double RoundedDip = 14.0;

    /// <summary>The radius may never eat more than this fraction of the shorter side.</summary>
    public const double MaxShorterSideFraction = 0.25;

    /// <summary>
    /// Corner radius for one flash, in the SAME units as <paramref name="shorterSide"/>. The
    /// compositor passes world px with the monitor's <paramref name="dpiScale"/>; the WPF paths
    /// pass DIPs with a scale of 1 (WPF scales DIPs itself).
    /// </summary>
    /// <param name="roundedOn">The FlashRoundedCorners setting.</param>
    /// <param name="ownsV2">True when the account owns any Flashes v2 grant (drift or pendulum).</param>
    /// <param name="hasGlow">True for a lucky or sparkle-boosted flash (the 12 px card).</param>
    /// <param name="shorterSide">Shorter side of the drawn image, same units as the result.</param>
    /// <param name="dpiScale">Scale applied to the base radius; 1 for DIPs.</param>
    public static double Resolve(bool roundedOn, bool ownsV2, bool hasGlow, double shorterSide, double dpiScale)
    {
        var wantRounded = roundedOn && ownsV2;
        var baseDip = wantRounded ? RoundedDip : hasGlow ? GlowBaseDip : 0.0;
        if (baseDip <= 0) return 0.0;

        // A degenerate box (a decode that produced nothing) rounds to nothing rather than
        // guessing a radius for a picture with no side to measure it against.
        if (shorterSide <= 0) return 0.0;

        var scale = dpiScale > 0 ? dpiScale : 1.0;
        var radius = Math.Min(baseDip * scale, shorterSide * MaxShorterSideFraction);
        return radius > 0 ? radius : 0.0;
    }
}
