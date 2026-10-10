using ConditioningControlPanel.Motion;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Depth
{
    /// <summary>How a HUD button stands on the sheet (nav polish wave 10, lane B). Values verbatim.</summary>
    public enum HudPlankKind
    {
        /// <summary>The style draws exactly what it drew before the depth pass.</summary>
        None = 0,
        /// <summary>An ordinary raised plank: DepthRaisedBevel + DepthRaisedSheen, RaisedPx drop.</summary>
        Plank = 1,
        /// <summary>START only: DepthPlankBevel + DepthPlankSheen, a StartPx drop.</summary>
        Chunky = 2,
    }

    /// <summary>
    /// The pure numbers of WPF 7.1.5 Controls/Depth/HudPlank.cs (the START row press): the face
    /// travel, the drop band length, which clock a change rides and the release spring. The WPF
    /// wiring (attached property, template parts DepthFace / DepthDrop) stays in each head.
    /// Motion Off sets values with no clock, so planks look the same at rest.
    /// </summary>
    public static class HudPlankRules
    {
        /// <summary>The drop band's authored height: its longest shadow (hovered).</summary>
        public static double DropBaseHeight(HudPlankKind kind) =>
            (kind == HudPlankKind.Chunky ? DepthRules.StartPx : DepthRules.RaisedPx) + DepthRules.HoverLiftPx;

        /// <summary>The drop length for a state: ShadowFor, stretched to StartPx for the chunky plank.</summary>
        public static double DropLength(HudPlankKind kind, bool enabled, bool pressed, bool hovered)
        {
            if (kind == HudPlankKind.None) return 0;
            double len = DepthRules.ShadowFor(enabled, pressed, active: false, hovered);
            if (len <= 0) return 0;
            return kind == HudPlankKind.Chunky ? len - DepthRules.RaisedPx + DepthRules.StartPx : len;
        }

        /// <summary>The drop band's ScaleY for a state (the band hangs below the plate, origin top).</summary>
        public static double DropScale(HudPlankKind kind, bool enabled, bool pressed, bool hovered) =>
            kind == HudPlankKind.None ? 0 : DropLength(kind, enabled, pressed, hovered) / DropBaseHeight(kind);

        /// <summary>The face's travel: a HUD plank is never "lit", so only hover / press move it.</summary>
        public static double Travel(bool enabled, bool pressed, bool hovered) =>
            DepthRules.TravelFor(enabled, pressed, active: false, hovered);

        /// <summary>Which clock a change rides: press is fast, release springs, hover glides.</summary>
        public static int DurationFor(bool pressed, bool releasing, MotionLevel level) =>
            DepthRules.Ms(pressed ? DepthRules.PressMs : releasing ? DepthRules.ReleaseMs : DepthRules.HoverMs, level);

        /// <summary>
        /// The face's Y track for a change landing on <paramref name="to"/> over <paramref name="ms"/>.
        /// Release: past rest by the overshoot UPWARD at 60% (quad out), then settle (quad in-out).
        /// Anything else: one quad ease-out. The drop band's ScaleY always rides one linear tween.
        /// </summary>
        public static Keyframe[] FaceTrack(double to, bool releasing, int ms) => releasing
            ? Keyframes.ReleaseSpring(to, -DepthRules.ReleaseOvershootPx, ms)
            : new[] { new Keyframe(ms, to, EaseKind.QuadOut) };
    }
}
