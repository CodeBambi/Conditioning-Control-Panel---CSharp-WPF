using System;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>
    /// THE FIRST LIGHT's clock (CONTRACT-FUSE-0816 §2.4, owner ruling 2026-08-16) — the three and a
    /// half seconds that stand between a committed ceremony choice and the first time this account
    /// has ever been allowed to look at the spiral.
    ///
    /// <para><b>Why a table and not timings inside the render loop.</b> Same discipline as
    /// <see cref="DescentFuseTimeline"/> and <see cref="DescentIgnitionTimeline"/>: this is the one
    /// animation in the app that plays ONCE per account, on a night that cannot be repeated, so the
    /// part of it that can be silently wrong (its arithmetic) is pure, has no WPF in it, and is
    /// pinned by tests. The canvas asks "how much of the arm is drawn at 1.8 seconds" and draws
    /// whatever comes back.</para>
    ///
    /// <para><b>The beats.</b> Fog rises out of the dark, the arm draws itself in while the whole
    /// figure turns, embers lift through it, and a bloom takes the screen and hands off. Nothing
    /// here is layout: every number below is consumed as an opacity, a rotation or a fraction of an
    /// arc, which is the only kind of animation this window is allowed to run (the map hosts a
    /// WebView2 and this repo has the render-thread scar tissue to explain why).</para>
    ///
    /// <para><b>Reduced motion gets a SNAPPED state, never a missing one</b> (contract §0). At
    /// anything below MotionLevel.Full the intro is a single 0.8s crossfade: the finished spiral is
    /// simply THERE, lit, from the first frame, and the layer fades off it into the embed. Same
    /// hand-off, same window, no swirl.</para>
    ///
    /// <para><b>THE HOLD.</b> <see cref="HandoffStartSeconds"/> is where the window may stop the
    /// clock. At reveal time the descent block can still be in flight (the commit's sync has not
    /// landed), and the intro is what covers that wait — so the caller advances this clock only up
    /// to the bloom, holds there until the block arrives, and then lets the last half second run.
    /// The wait is therefore invisible: it happens inside a held bloom rather than in front of an
    /// empty window.</para>
    /// </summary>
    public static class SpiralFirstLightTimeline
    {
        // ---- the full sequence -------------------------------------------------

        /// <summary>The fog's rise out of the ground. Nothing else moves during it.</summary>
        public const double FogInSeconds = 0.5;

        /// <summary>Dead air before the arm starts drawing. Lets the fog read as a room first.</summary>
        public const double DrawStartSeconds = 0.30;

        /// <summary>Centre to rim. The longest beat, because it is the one the night is about.</summary>
        public const double DrawSeconds = 2.40;

        /// <summary>The bloom that takes the screen once the arm is finished.</summary>
        public const double BloomSeconds = 0.60;

        /// <summary>The layer fading off the finished figure, handing the window to the embed.</summary>
        public const double HandoffSeconds = 0.50;

        /// <summary>Embers start lifting a beat into the draw and run to the bloom.</summary>
        public const double EmberStartSeconds = 1.00;

        /// <summary>Turns the whole figure makes while it draws. Slow — it is a spiral, not a fan.</summary>
        public const double SwirlTurns = 0.42;

        /// <summary>Reduced motion's whole intro: one crossfade off a spiral that is already lit.</summary>
        public const double ReducedCrossfadeSeconds = 0.80;

        /// <summary>Fog blobs. Four is a room; six is weather.</summary>
        public const int FogBlobs = 4;

        /// <summary>Embers. Few enough to read as individual sparks lifting.</summary>
        public const int EmberCount = 22;

        /// <summary>
        /// Stations along the arm, as fractions of the draw — the same idea as the ignition's
        /// notches, and deliberately the same shape of table so the two spirals in this feature pop
        /// their dots on the same rhythm.
        ///
        /// <para>The last one sits at 0.85 rather than out on the rim so that its pop FINISHES
        /// before the arm does: <c>(1 - 0.85) * DrawSeconds</c> is comfortably longer than
        /// <see cref="DotPopSeconds"/>, and a dot still coming up on the final frame reads as a
        /// glitch rather than as a station.</para>
        /// </summary>
        public static readonly double[] DotAt = { 0.10, 0.24, 0.38, 0.52, 0.65, 0.76, 0.85 };

        /// <summary>How long a dot takes to come up once the arm's head has passed it.</summary>
        public const double DotPopSeconds = 0.30;

        /// <summary>Bloom, in absolute seconds from the start.</summary>
        public static double BloomStartSeconds => DrawStartSeconds + DrawSeconds;

        /// <summary>
        /// Where the hand-off fade begins — and, for the caller, where the clock may be HELD while
        /// the descent block is still in flight. See the class note.
        /// </summary>
        public static double HandoffStartSeconds(bool reducedMotion) => reducedMotion
            ? 0.0
            : BloomStartSeconds + BloomSeconds;

        /// <summary>The whole intro.</summary>
        public static double TotalSeconds(bool reducedMotion) => reducedMotion
            ? ReducedCrossfadeSeconds
            : HandoffStartSeconds(false) + HandoffSeconds;

        /// <summary>True once the layer has finished fading and the embed may take the window.</summary>
        public static bool IsComplete(double elapsedSeconds, bool reducedMotion)
            => Sane(elapsedSeconds) >= TotalSeconds(reducedMotion);

        // ---- the parts ---------------------------------------------------------

        /// <summary>
        /// How much of the arm is drawn, 0..1. Reduced motion is 1 from the first frame: the
        /// snapped state is a finished spiral, present rather than missing.
        /// </summary>
        public static double DrawFraction(double elapsedSeconds, bool reducedMotion)
        {
            if (reducedMotion) return 1.0;

            var t = Sane(elapsedSeconds);
            if (t <= DrawStartSeconds) return 0.0;

            var f = (t - DrawStartSeconds) / DrawSeconds;
            return f >= 1.0 ? 1.0 : f;
        }

        /// <summary>
        /// A station dot's pop, 0 (dark) to 1 (lit), for the dot at arm fraction
        /// <paramref name="dotAt"/>. Reduced motion lights every dot: a finished spiral is finished,
        /// stations and all.
        /// </summary>
        public static double DotGlow(double elapsedSeconds, double dotAt, bool reducedMotion)
        {
            if (reducedMotion) return 1.0;

            var drawn = DrawFraction(elapsedSeconds, reducedMotion);
            if (drawn < dotAt) return 0.0;

            // Seconds since the head passed this station, converted back through the draw's rate —
            // so a dot pops at the same speed wherever on the arm it sits.
            var since = (drawn - dotAt) * DrawSeconds;
            var t = since / DotPopSeconds;
            return t >= 1.0 ? 1.0 : t;
        }

        /// <summary>
        /// The fog's own opacity envelope, 0..1. Rises once and then simply stays — the drifting is
        /// the canvas's business (it runs off an ambient clock so the fog keeps moving while the
        /// intro is HELD waiting for a block), and this is only how present the fog is.
        /// Reduced motion: fully present from frame zero, no rise.
        /// </summary>
        public static double FogOpacity(double elapsedSeconds, bool reducedMotion)
        {
            if (reducedMotion) return 1.0;

            var t = Sane(elapsedSeconds);
            if (t >= FogInSeconds) return 1.0;
            return t / FogInSeconds;
        }

        /// <summary>
        /// The ember field's envelope, 0..1: in over half a second from
        /// <see cref="EmberStartSeconds"/>, out again across the bloom, because a spark still
        /// climbing while the screen blooms reads as a stray pixel rather than as an ember.
        /// Reduced motion has no embers at all — they are pure ambient motion.
        /// </summary>
        public static double EmberOpacity(double elapsedSeconds, bool reducedMotion)
        {
            if (reducedMotion) return 0.0;

            var t = Sane(elapsedSeconds);
            if (t <= EmberStartSeconds) return 0.0;

            var rise = (t - EmberStartSeconds) / 0.5;
            var up = rise >= 1.0 ? 1.0 : rise;

            if (t <= BloomStartSeconds) return up;

            var fall = 1.0 - (t - BloomStartSeconds) / BloomSeconds;
            return fall <= 0 ? 0.0 : up * fall;
        }

        /// <summary>
        /// The bloom, 0..1, from the moment the arm finishes. Reduced motion holds it at full for
        /// the whole crossfade: the light is already out, it is only the layer that leaves.
        /// </summary>
        public static double BloomOpacity(double elapsedSeconds, bool reducedMotion)
        {
            if (reducedMotion) return 1.0;

            var t = Sane(elapsedSeconds);
            if (t <= BloomStartSeconds) return 0.0;

            var f = (t - BloomStartSeconds) / BloomSeconds;
            return f >= 1.0 ? 1.0 : f;
        }

        /// <summary>
        /// The whole intro LAYER's opacity — 1 until the hand-off, then down to nothing.
        ///
        /// <para>This is the fade that ends the reveal, and it is the only one that matters to the
        /// window: a WebView2 is a native child HWND that paints over WPF content no matter what
        /// the z-order says, so the embed cannot be crossfaded UNDER this layer. The layer fades to
        /// nothing over the window's own dark ground, and the embed is shown at the end of it.</para>
        /// </summary>
        public static double LayerOpacity(double elapsedSeconds, bool reducedMotion)
        {
            var t = Sane(elapsedSeconds);
            var start = HandoffStartSeconds(reducedMotion);
            var span = reducedMotion ? ReducedCrossfadeSeconds : HandoffSeconds;

            if (t <= start) return 1.0;

            var f = (t - start) / span;
            return f >= 1.0 ? 0.0 : 1.0 - f;
        }

        /// <summary>
        /// How far the whole figure has turned, in radians. Eased so it decelerates into the bloom
        /// rather than stopping dead. Reduced motion never turns.
        /// </summary>
        public static double SwirlAngle(double elapsedSeconds, bool reducedMotion)
        {
            if (reducedMotion) return 0.0;

            var t = Sane(elapsedSeconds) / Math.Max(0.001, BloomStartSeconds);
            if (t > 1.0) t = 1.0;

            // Cubic ease-out on the ROTATION, not on the draw: the arm draws at a steady rate (a
            // spiral drawn with easing looks like a stutter) while the figure it is drawn on slows.
            var inv = 1 - t;
            var eased = 1 - inv * inv * inv;
            return eased * SwirlTurns * Math.PI * 2;
        }

        /// <summary>
        /// Negatives and NaN read as zero rather than throwing. A Stopwatch read across a system
        /// clock adjustment should show the first frame again, not take down the one window this
        /// account will ever see this animation in.
        /// </summary>
        private static double Sane(double seconds)
            => double.IsNaN(seconds) || seconds < 0 ? 0.0 : seconds;
    }
}
