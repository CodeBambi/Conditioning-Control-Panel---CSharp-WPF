using System;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Motion;
using ConditioningControlPanel.Nav;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Depth
{
    /// <summary>
    /// The depth law (nav polish wave 10, 2026-10-06), ported from WPF 7.1.5
    /// Controls/Depth/DepthRules.cs with colours as ARGB <see cref="uint"/>: one lamp, three
    /// heights, "on is pressed in". Lanes apply these through the brushes in
    /// <see cref="DepthPalette"/>; nobody invents a shade by hand.
    ///
    ///  * One lamp, top-left and slightly above the panel. Highlights sit on top edges, shadows
    ///    fall down and to the right.
    ///  * Three heights. Raised = you press it. Floating = you read it. Sunken = it holds things.
    ///  * On is pressed in: a lit thing sits down in its socket and glows, an off thing rises.
    ///    Hover lifts, press drops, release springs back.
    /// </summary>
    public static class DepthRules
    {
        /// <summary>What a surface is. Every depth-bearing element picks exactly one.</summary>
        public enum Height { Raised, Floating, Sunken }

        // ---- heights, in design-canvas pixels ---------------------------------------------------

        /// <summary>How far a raised thing stands off the sheet (the length of its drop shadow).</summary>
        public const double RaisedPx = 3.0;
        /// <summary>A hovered raised thing lifts this much more.</summary>
        public const double HoverLiftPx = 2.0;
        /// <summary>A pressed thing travels this far DOWN from rest (then springs back).</summary>
        public const double PressTravelPx = 2.0;
        /// <summary>A lit ("on") thing sits this far down in its socket, at rest.</summary>
        public const double ActiveSinkPx = 1.0;
        /// <summary>A floating card's shadow length.</summary>
        public const double FloatPx = 10.0;
        /// <summary>Only START gets the chunky bevel: the biggest press target in the app.</summary>
        public const double StartPx = 5.0;
        /// <summary>A sunken well's inner shadow depth along its top edge.</summary>
        public const double WellPx = 9.0;
        /// <summary>The rail column throws this much shadow onto the page beside it.</summary>
        public const double RailShadowPx = 6.0;
        /// <summary>A ledge (toolbar under a screen) throws this much shadow upward.</summary>
        public const double LedgeUpPx = 14.0;

        // ---- alphas (0..1) ----------------------------------------------------------------------

        public const double HighlightAlpha = 0.26;
        public const double ShadeAlpha = 0.50;
        public const double SheenAlpha = 0.09;
        public const double ShadowAlpha = 0.62;
        public const double WellTopAlpha = 0.65;
        public const double WellLeftAlpha = 0.35;
        public const double WellFootAlpha = 0.07;
        public const double FloatAlpha = 0.40;
        public const double LedgeUpAlpha = 0.70;
        public const double PressedShadeAlpha = 0.60;

        // ---- timings and motion -----------------------------------------------------------------

        /// <summary>Press down (fast), release spring (slower), hover lift.</summary>
        public const int PressMs = 90, ReleaseMs = 140, HoverMs = 120;
        /// <summary>The spring overshoot on release, in px, before settling at rest.</summary>
        public const double ReleaseOvershootPx = 1.0;
        /// <summary>Hover tilt toward the pointer, the launcher tile's own number.</summary>
        public const double TiltDegrees = 1.2;

        /// <summary>Motion-scaled milliseconds: Full = ms, Reduced = half, Off = 0 (static depth).</summary>
        public static int Ms(int fullMs, MotionLevel level) => MotionGate.Ms(fullMs, level);

        /// <summary>Tilt is allowed only where the launcher allows it: transitions on and a tier
        /// above Performance. Rail coins and Home tiles only.</summary>
        public static bool TiltAllowed(MotionLevel level, PerformanceTier tier) =>
            level != MotionLevel.Off && tier != PerformanceTier.Performance;

        // ---- the shadow colour ------------------------------------------------------------------

        /// <summary>How far a shadow is pulled from black toward the section hue.</summary>
        public const double ShadowHuePull = 0.28;

        /// <summary>The neutral shadow used where no section hue applies (dialogs, the launcher).</summary>
        public static readonly uint NeutralShadowInk = Argb.FromRgb(0x06, 0x02, 0x10);

        /// <summary>A shadow colour for the section: deep ink pulled 28% toward the hue, at ShadowAlpha.</summary>
        public static uint ShadowColor(uint hue) =>
            Argb.WithAlpha(NavStripRules.Mix(NeutralShadowInk, hue, ShadowHuePull), ShadowAlpha);

        /// <summary>The same shadow at another strength (a card's FloatAlpha, a ledge's LedgeUpAlpha).</summary>
        public static uint ShadowColor(uint hue, double alpha) =>
            Argb.WithAlpha(NavStripRules.Mix(NeutralShadowInk, hue, ShadowHuePull), alpha);

        // ---- travel -----------------------------------------------------------------------------

        /// <summary>Where a raised face sits, in px DOWN from rest. Pressed wins; a lit thing sits in
        /// its socket; a hovered idle thing lifts; disabled never moves.</summary>
        public static double TravelFor(bool enabled, bool pressed, bool active, bool hovered)
        {
            if (!enabled) return 0;
            if (pressed) return PressTravelPx;
            if (active) return ActiveSinkPx;
            if (hovered) return -HoverLiftPx;
            return 0;
        }

        /// <summary>The drop shadow length: none while pressed or lit, longer on hover, RaisedPx at rest.</summary>
        public static double ShadowFor(bool enabled, bool pressed, bool active, bool hovered)
        {
            if (!enabled) return 0;
            if (pressed || active) return 0;
            if (hovered) return RaisedPx + HoverLiftPx;
            return RaisedPx;
        }
    }
}
