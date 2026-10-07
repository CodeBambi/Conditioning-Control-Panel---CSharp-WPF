using System;
using System.Windows.Media;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Controls.Header
{
    /// <summary>The three looks of the header Premium spark.</summary>
    public enum SparkTier { Free, Basic, Prime }

    /// <summary>How much the spark may move: the app's motion level, folded with the
    /// performance tier (a tier that forbids ambient motion turns Full into Reduced).</summary>
    public enum SparkMotion { Full, Reduced, Off }

    /// <summary>
    /// THE PREMIUM SPARK, the pure half (polish 12, owner 2026-10-07): a die-cut cardstock
    /// four-point sparkle in the header between the welcome banner and the update pill, with
    /// "PREMIUM" cut out underneath.
    ///
    /// <para><b>Tier truth</b> is the one the profile bubble's rim badge reads
    /// (<c>MainWindow.RefreshProfileBubbleTierBadge</c>): HasLabAccess is Prime, else
    /// HasPremiumAccess is Basic, else Free. Both already fold in the whitelist, SubscribeStar and
    /// the offline grace, so the spark tells the same story every gate does.</para>
    ///
    /// <para><b>Looks.</b> Free = a grey card at 40%, no motion at all, and it stays that way.
    /// Basic = gold card, a sheen sweep every few seconds, a gentle breath, the odd gold glint.
    /// Prime = ice cyan card, a pulsing cyan halo behind it, diamond motes drifting off, plus
    /// the sheen. Every colour here is commerce chrome: constant across mods, never FxTheme.</para>
    ///
    /// <para><b>Motion.</b> Full = everything. Reduced = the sheen only, at half pace; no breath,
    /// no glints, no motes, a still halo. Off = the static lit card. Interaction (hover lift,
    /// press travel) follows DepthRules and stops only at Off.</para>
    /// </summary>
    internal static class PremiumSparkRules
    {
        // ---- geometry -------------------------------------------------------------------------

        /// <summary>The cutout star's box (px). The whole control is a 54 x 42 card whose layout
        /// footprint is held to the header row's 34 px by a -4 px margin top and bottom.</summary>
        internal const double StarSize = 30.0;
        internal const double ControlWidth = 54.0;
        internal const double ControlHeight = 42.0;
        internal const double LayoutBleed = 4.0;

        /// <summary>The cutout's static lean (degrees): hand-placed, not templated.</summary>
        internal const double Tilt = -4.0;

        /// <summary>The die-cut white border around the star and the letters (stroke width).</summary>
        internal const double DieCutPx = 3.2;
        internal const double LabelDieCutPx = 2.2;

        /// <summary>The label: font size and the extra space between letters.</summary>
        internal const double LabelSize = 8.5;
        internal const double LabelTracking = 1.1;

        /// <summary>Free sits at this opacity, always.</summary>
        internal const double FreeOpacity = 0.40;

        // ---- timings --------------------------------------------------------------------------

        /// <summary>One sheen pass (seconds) and the rest between passes, at Full.</summary>
        internal const double SheenPassSec = 0.9;
        internal const double SheenRestBasicSec = 3.6;
        internal const double SheenRestPrimeSec = 2.8;

        /// <summary>The breath: a 4% swell over this half period (seconds), Full only.</summary>
        internal const double BreathHalfSec = 2.2;
        internal const double BreathScale = 1.04;

        /// <summary>Prime's halo pulse: opacity range and half period.</summary>
        internal const double HaloLow = 0.45, HaloHigh = 0.95, HaloStill = 0.7;
        internal const double HaloHalfSec = 1.6;

        /// <summary>Basic's glints: one every this many seconds (random inside the range).</summary>
        internal const double GlintMinSec = 2.4, GlintMaxSec = 5.0;

        /// <summary>Prime's motes: spawn gap range (ms), lifetime range (ms), cap alive.</summary>
        internal const int MoteGapMinMs = 420, MoteGapMaxMs = 820;
        internal const int MoteLifeMinMs = 1300, MoteLifeMaxMs = 2200;
        internal const int MaxMotes = 6;

        /// <summary>The hover wobble: a damped sway this many degrees each way over this long.</summary>
        internal const double WobbleDegrees = 5.0;
        internal const int WobbleMs = 460;

        /// <summary>Every timeline the spark runs is capped at this frame rate.</summary>
        internal const int FrameRate = 30;

        // ---- state ----------------------------------------------------------------------------

        /// <summary>The tier from the two canonical gates. Lab wins: a Prime account is also Premium.</summary>
        internal static SparkTier TierFrom(bool hasPremiumAccess, bool hasLabAccess) =>
            hasLabAccess ? SparkTier.Prime : hasPremiumAccess ? SparkTier.Basic : SparkTier.Free;

        /// <summary>The spark's motion from the app level and whether the performance tier allows
        /// ambient motion at all.</summary>
        internal static SparkMotion MotionFrom(MotionLevel level, bool tierAllowsAmbient) => level switch
        {
            MotionLevel.Off => SparkMotion.Off,
            MotionLevel.Reduced => SparkMotion.Reduced,
            _ => tierAllowsAmbient ? SparkMotion.Full : SparkMotion.Reduced,
        };

        internal static double Opacity(SparkTier tier) => tier == SparkTier.Free ? FreeOpacity : 1.0;

        /// <summary>The sheen sweeps on Basic and Prime, at Full and Reduced. Free never moves.</summary>
        internal static bool Sheen(SparkTier tier, SparkMotion motion) =>
            tier != SparkTier.Free && motion != SparkMotion.Off;

        /// <summary>Seconds from one sheen pass's start to the next; Reduced runs at half pace.</summary>
        internal static double SheenCycleSec(SparkTier tier, SparkMotion motion)
        {
            var cycle = SheenPassSec + (tier == SparkTier.Prime ? SheenRestPrimeSec : SheenRestBasicSec);
            return motion == SparkMotion.Reduced ? cycle * 2 : cycle;
        }

        /// <summary>The pass itself; Reduced draws it slower too.</summary>
        internal static double SheenPassFor(SparkMotion motion) =>
            motion == SparkMotion.Reduced ? SheenPassSec * 1.6 : SheenPassSec;

        internal static bool Breath(SparkTier tier, SparkMotion motion) =>
            tier != SparkTier.Free && motion == SparkMotion.Full;

        internal static bool Glints(SparkTier tier, SparkMotion motion) =>
            tier == SparkTier.Basic && motion == SparkMotion.Full;

        /// <summary>The halo is Prime's; it is drawn at every motion level, pulsing only at Full.</summary>
        internal static bool Halo(SparkTier tier) => tier == SparkTier.Prime;
        internal static bool HaloPulse(SparkTier tier, SparkMotion motion) =>
            tier == SparkTier.Prime && motion == SparkMotion.Full;

        /// <summary>Drifting diamond motes: Prime at Full, and only where the tier has a particle budget.</summary>
        internal static bool Motes(SparkTier tier, SparkMotion motion, bool particlesAllowed) =>
            tier == SparkTier.Prime && motion == SparkMotion.Full && particlesAllowed;

        /// <summary>Particles thrown by a click. Free stays still; Off throws nothing.</summary>
        internal static int BurstCount(SparkTier tier, SparkMotion motion) =>
            motion == SparkMotion.Off || tier == SparkTier.Free ? 0
            : tier == SparkTier.Prime ? 14 : 10;

        /// <summary>Hover wobble: Basic and Prime, whenever transitions run.</summary>
        internal static bool Wobble(SparkTier tier, SparkMotion motion) =>
            tier != SparkTier.Free && motion != SparkMotion.Off;

        /// <summary>Where the cutout sits (px down from rest) and how long its paper shadow is.
        /// Straight from DepthRules: pressed sinks 2, hover lifts 2, rest throws 3.</summary>
        internal static double Travel(bool pressed, bool hovered) =>
            Depth.DepthRules.TravelFor(enabled: true, pressed: pressed, active: false, hovered: hovered);

        internal static double ShadowLength(bool pressed, bool hovered) =>
            Depth.DepthRules.ShadowFor(enabled: true, pressed: pressed, active: false, hovered: hovered);

        /// <summary>A random wait inside a range, seconds; rng injectable for tests.</summary>
        internal static double Between(Random rng, double min, double max) =>
            min + rng.NextDouble() * Math.Max(0, max - min);

        /// <summary>The loc key naming the state in the tooltip.</summary>
        internal static string TooltipKey(SparkTier tier) => tier switch
        {
            SparkTier.Prime => "premium_spark_tip_prime",
            SparkTier.Basic => "premium_spark_tip_basic",
            _ => "premium_spark_tip_free",
        };

        /// <summary>The tab the spark opens: "premium" once the nav table knows it, else the old
        /// "exclusives" key, which redirects to wherever the vault lives in this build.</summary>
        internal static string TargetTab(Func<string, string?> sectionForTab) =>
            sectionForTab("premium") != null ? "premium" : "exclusives";

        // ---- livery ---------------------------------------------------------------------------

        /// <summary>One tier's cardstock: face gradient (top, middle, bottom), the label ink, the
        /// particle colour and the paper shadow.</summary>
        internal readonly record struct Livery(Color FaceTop, Color FaceMid, Color FaceBottom,
                                               Color Ink, Color Mote, Color Edge);

        /// <summary>Gold matches tier_badge_t1 (#FFD27A glow); cyan matches tier_badge_t2 (#BDEFFF).</summary>
        internal static Livery LiveryFor(SparkTier tier) => tier switch
        {
            SparkTier.Prime => new Livery(
                Color.FromRgb(0xF0, 0xFD, 0xFF), Color.FromRgb(0x9C, 0xEC, 0xFF), Color.FromRgb(0x38, 0xB6, 0xDA),
                Color.FromRgb(0x5E, 0xE6, 0xFF), Color.FromRgb(0xBD, 0xEF, 0xFF), Color.FromRgb(0xF4, 0xFC, 0xFF)),
            SparkTier.Basic => new Livery(
                Color.FromRgb(0xFF, 0xF2, 0xC6), Color.FromRgb(0xFF, 0xCF, 0x5C), Color.FromRgb(0xC9, 0x8A, 0x1E),
                Color.FromRgb(0xFF, 0xD2, 0x7A), Color.FromRgb(0xFF, 0xE7, 0x9A), Color.FromRgb(0xFF, 0xFB, 0xEF)),
            _ => new Livery(
                Color.FromRgb(0x9A, 0x95, 0xA6), Color.FromRgb(0x77, 0x72, 0x84), Color.FromRgb(0x56, 0x52, 0x62),
                Color.FromRgb(0xA9, 0xA4, 0xB4), Color.FromRgb(0x9A, 0x95, 0xA6), Color.FromRgb(0xC9, 0xC5, 0xD1)),
        };

        /// <summary>The four-point sparkle, in a StarSize box: long vertical points, slightly
        /// shorter horizontal ones, concave flanks.</summary>
        internal const string StarPathData =
            "M15,0 Q16.9,12.6 29,15 Q16.9,17.4 15,30 Q13.1,17.4 1,15 Q13.1,12.6 15,0 Z";
    }
}
