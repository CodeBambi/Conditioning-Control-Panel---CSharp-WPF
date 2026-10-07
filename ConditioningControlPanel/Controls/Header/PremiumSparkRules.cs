using System;
using System.Windows;
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
    /// Basic = gold card, a sheen sweep every few seconds, a gentle breath, a steady trickle of
    /// gold glitter spilling off the points, twinkles orbiting the card, white glints on the arms.
    /// Prime = ice cyan card, a pulsing cyan halo behind it, a dense field of diamonds drifting
    /// off, twinkling stars on the tips and an occasional bigger flare, plus the sheen. The star
    /// is cut and folded: slim concave arms, eight facets lit from the top-left lamp. Every colour
    /// here is commerce chrome: constant across mods, never FxTheme.</para>
    ///
    /// <para><b>Motion.</b> Full = everything. Reduced = the sheen at half pace and a slow few
    /// particles; no breath, orbits or flares, a still halo. Off = the static lit card. One 30 fps
    /// clock drives all of it (PremiumSparkField holds the particles). Interaction (hover lift,
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
        internal const double DieCutPx = 2.4;
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

        // ---- particles (round 2, owner 2026-10-07: "there aren't enough particles") ------------

        /// <summary>Ambient particles alive at once. Full: Basic keeps a steady trickle of gold
        /// glitter, Prime a denser diamond field. Reduced (and a performance tier with no particle
        /// budget) keeps a slow few. Click bursts ride on top (PremiumSparkField.BurstHeadroom).</summary>
        internal const int BasicCap = 18, PrimeCap = 24, FewCap = 4;
        internal const int MaxAmbientCap = PrimeCap;

        /// <summary>Reduced spawns at this fraction of the Full pace.</summary>
        internal const double ReducedPace = 0.3;

        /// <summary>Seconds between ambient spawns (glitter for Basic, diamonds for Prime).</summary>
        internal static double SpawnGapMin(SparkTier tier) => 0.07;
        internal static double SpawnGapMax(SparkTier tier) => tier == SparkTier.Prime ? 0.14 : 0.13;

        /// <summary>Seconds between tip twinkles: Prime's stars come quicker than Basic's glints.</summary>
        internal static double GlintGapMin(SparkTier tier) => tier == SparkTier.Prime ? 0.28 : 0.55;
        internal static double GlintGapMax(SparkTier tier) => tier == SparkTier.Prime ? 0.62 : 1.3;

        /// <summary>Basic keeps this many twinkles orbiting the card at Full.</summary>
        internal const int Orbiters = 3;

        /// <summary>Prime's bigger flare: one every this many seconds, lasting FlareSec.</summary>
        internal const double FlareGapMin = 3.2, FlareGapMax = 6.0, FlareSec = 0.75;

        /// <summary>The ambient cap for a look. Free and Off spawn nothing, ever.</summary>
        internal static int AmbientCap(SparkTier tier, SparkMotion motion, bool particlesAllowed) =>
            tier == SparkTier.Free || motion == SparkMotion.Off ? 0
            : motion == SparkMotion.Reduced || !particlesAllowed ? FewCap
            : tier == SparkTier.Prime ? PrimeCap : BasicCap;

        /// <summary>Basic's orbiting twinkles and Prime's flares are Full only.</summary>
        internal static bool Orbits(SparkTier tier, SparkMotion motion) =>
            tier == SparkTier.Basic && motion == SparkMotion.Full;
        internal static bool Flares(SparkTier tier, SparkMotion motion) =>
            tier == SparkTier.Prime && motion == SparkMotion.Full;

        /// <summary>The one clock runs whenever anything can move: Basic or Prime, not Off.</summary>
        internal static bool Clock(SparkTier tier, SparkMotion motion) =>
            tier != SparkTier.Free && motion != SparkMotion.Off;

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

        /// <summary>The halo is Prime's; it is drawn at every motion level, pulsing only at Full.</summary>
        internal static bool Halo(SparkTier tier) => tier == SparkTier.Prime;
        internal static bool HaloPulse(SparkTier tier, SparkMotion motion) =>
            tier == SparkTier.Prime && motion == SparkMotion.Full;

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

        /// <summary>The four-point sparkle, in a StarSize box: long vertical points, shorter
        /// horizontal ones, slim concave arms (round 2: the waist sits about 5.3 px from the heart,
        /// it was 6.7). Built from the four cubic flanks in <see cref="Flanks"/>.</summary>
        internal const string StarPathData =
            "M15,0 C15.9,10 18.8,14 28,15 C18.8,16 15.9,20 15,30 C14.1,20 11.2,16 2,15 C11.2,14 14.1,10 15,0 Z";

        /// <summary>The star's four flanks as cubic Beziers (start, c1, c2, end), clockwise from
        /// the top tip, in the StarSize box. Same numbers as StarPathData.</summary>
        internal static readonly (Point p0, Point c1, Point c2, Point p3)[] Flanks =
        {
            (new Point(15, 0), new Point(15.9, 10), new Point(18.8, 14), new Point(28, 15)),
            (new Point(28, 15), new Point(18.8, 16), new Point(15.9, 20), new Point(15, 30)),
            (new Point(15, 30), new Point(14.1, 20), new Point(11.2, 16), new Point(2, 15)),
            (new Point(2, 15), new Point(11.2, 14), new Point(14.1, 10), new Point(15, 0)),
        };

        /// <summary>The heart of the star, where the folds meet.</summary>
        internal static readonly Point Heart = new(15, 15);

        /// <summary>Splits a cubic at t (de Casteljau): the two halves, each (p0, c1, c2, p3).</summary>
        internal static ((Point, Point, Point, Point) a, (Point, Point, Point, Point) b) Split(
            (Point p0, Point c1, Point c2, Point p3) c, double t)
        {
            static Point L(Point a, Point b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            var p01 = L(c.p0, c.c1, t); var p12 = L(c.c1, c.c2, t); var p23 = L(c.c2, c.p3, t);
            var p012 = L(p01, p12, t); var p123 = L(p12, p23, t);
            var mid = L(p012, p123, t);
            return ((c.p0, p01, p012, mid), (mid, p123, p23, c.p3));
        }

        /// <summary>How high the folded heart stands off the card (px), for facet lighting.</summary>
        internal const double FoldHeight = 7.0;

        /// <summary>The lamp: top-left and above, the house lamp (Depth law).</summary>
        private static readonly (double x, double y, double z) Lamp = Normalise(-0.55, -0.8, 1.0);

        /// <summary>How lit one facet is: the facet is the triangle (raised heart, a, b) with a and
        /// b on the card. Positive = brighter than a flat card under the same lamp, negative =
        /// darker. Range about -1..1.</summary>
        internal static double FacetShade(Point a, Point b)
        {
            double cx = Heart.X, cy = Heart.Y, cz = FoldHeight;
            double ux = a.X - cx, uy = a.Y - cy, uz = -cz;
            double vx = b.X - cx, vy = b.Y - cy, vz = -cz;
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            if (nz < 0) { nx = -nx; ny = -ny; nz = -nz; }
            var n = Normalise(nx, ny, nz);
            var lit = n.x * Lamp.x + n.y * Lamp.y + n.z * Lamp.z;
            // Brighter than flat scales into the headroom above it, darker into the room below it.
            var shade = lit >= Lamp.z ? (lit - Lamp.z) / (1 - Lamp.z) : (lit - Lamp.z) / Lamp.z;
            return Math.Clamp(shade, -1, 1);
        }

        private static (double x, double y, double z) Normalise(double x, double y, double z)
        {
            var len = Math.Sqrt(x * x + y * y + z * z);
            return len <= 0 ? (0, 0, 1) : (x / len, y / len, z / len);
        }

        // ---- the clock: every ambient value as a function of time ---------------------------

        /// <summary>The sheen band's x (StarSize px) at time t; parked at -20 between passes.</summary>
        internal static double SheenAt(double t, SparkTier tier, SparkMotion motion)
        {
            const double from = -20, to = StarSize + 12;
            if (!Sheen(tier, motion)) return from;
            var cycle = SheenCycleSec(tier, motion);
            var pass = SheenPassFor(motion);
            var u = t % cycle;
            if (u >= pass) return from;
            var k = u / pass;
            var eased = 0.5 - 0.5 * Math.Cos(Math.PI * k);
            return from + (to - from) * eased;
        }

        /// <summary>The breath scale at time t (1.0 when the look does not breathe).</summary>
        internal static double BreathAt(double t, SparkTier tier, SparkMotion motion) =>
            Breath(tier, motion)
                ? 1 + (BreathScale - 1) * (0.5 - 0.5 * Math.Cos(Math.PI * t / BreathHalfSec))
                : 1.0;

        /// <summary>Prime's halo opacity at time t, lifted by a flare's glow (0..1).</summary>
        internal static double HaloAt(double t, SparkTier tier, SparkMotion motion, double flareGlow)
        {
            if (!Halo(tier)) return 0;
            if (!HaloPulse(tier, motion)) return HaloStill;
            var o = HaloLow + (HaloHigh - HaloLow) * (0.5 - 0.5 * Math.Cos(Math.PI * t / HaloHalfSec));
            return Math.Min(1.0, o + 0.35 * flareGlow);
        }
    }
}
