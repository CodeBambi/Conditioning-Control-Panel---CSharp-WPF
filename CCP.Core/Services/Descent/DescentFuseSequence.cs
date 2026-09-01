using System;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>Which show the fuse window is playing. The window renders one of these and closes.</summary>
    public enum DescentShowKind
    {
        /// <summary>The instant passed with the app open and watching (§2.3). The full sequence.</summary>
        Live = 0,
        /// <summary>The app launched to find the instant already behind it (§2.4). Same beats, compressed.</summary>
        CatchUp = 1,
        /// <summary>The Year One ignition, after a committed ceremony choice (§2.4).</summary>
        Ignition = 2,
    }

    /// <summary>
    /// The beats of the crack, named. Ordered, so <c>stage &gt;= DescentFuseStage.Bloom</c> reads as
    /// "the light is out" — which is the question every caller actually asks.
    /// </summary>
    public enum DescentFuseStage
    {
        /// <summary>A hanging field of gold sparks. Everything still.</summary>
        Freeze = 0,
        /// <summary>Gold hairlines split the sealed glyph, and one brief flash.</summary>
        Crack = 1,
        /// <summary>All the light collapses into the centre point.</summary>
        Drain = 2,
        /// <summary>Pure ground. Nothing at all.</summary>
        Black = 3,
        /// <summary>The ceremony glow ignites outward from the same centre.</summary>
        Bloom = 4,
        /// <summary>The bloom, held. The window waits here for the ceremony.</summary>
        Held = 5,
    }

    /// <summary>Where the show is right now: which beat, and how far into it (0..1).</summary>
    public readonly struct DescentFuseFrame
    {
        public DescentFuseFrame(DescentFuseStage stage, double progress, double sinceBloom)
        {
            Stage = stage;
            Progress = progress;
            SinceBloom = sinceBloom;
        }

        /// <summary>The beat.</summary>
        public DescentFuseStage Stage { get; }

        /// <summary>How far through THAT beat, 0..1. Held sits at 1.</summary>
        public double Progress { get; }

        /// <summary>
        /// Seconds since the bloom began, or a negative number before it. The handoff clock reads
        /// this and nothing else, so a reduced-motion show and a full show hand off on the same
        /// schedule measured from the same event.
        /// </summary>
        public double SinceBloom { get; }
    }

    /// <summary>
    /// THE CRACK's clock (CONTRACT-FUSE-0816 §2.3/§2.4) — a pure stage table, so the one thing about
    /// this show that can be wrong in a way nobody notices until the night itself (its timing) is
    /// arithmetic that a test can pin.
    ///
    /// <para><b>Three shapes, one table.</b> The live sequence is the contract's:
    /// freeze 1.5 → crack 1.0 → drain 1.7 → black 1.0 → bloom 2.5, then hold. The catch-up plays the
    /// SAME five beats in the same order at roughly 6/7.7 speed, because a condensed crack that
    /// dropped a beat would be a different animation wearing the same name. Reduced motion is a
    /// single 1.5s crossfade straight into the held bloom.</para>
    ///
    /// <para><b>Reduced motion gets a SNAPPED state, never a missing one</b> (contract §0, the faucet
    /// PR #37 lesson). That is why the reduced path still resolves to <see cref="DescentFuseStage.Bloom"/>
    /// and then <see cref="DescentFuseStage.Held"/>, with a real <see cref="DescentFuseFrame.SinceBloom"/>
    /// clock behind it: a reduced-motion subject gets the ceremony handoff on exactly the same
    /// schedule as everyone else, just without the ninety sparks.</para>
    ///
    /// <para><b>Nothing here touches WPF, a dispatcher or a clock.</b> The window feeds it an elapsed
    /// span and draws whatever comes back.</para>
    /// </summary>
    public static class DescentFuseTimeline
    {
        // ---- the live sequence, verbatim from the contract ---------------------

        public const double FreezeSeconds = 1.5;
        public const double CrackSeconds  = 1.0;
        public const double DrainSeconds  = 1.7;
        public const double BlackSeconds  = 1.0;
        public const double BloomSeconds  = 2.5;

        /// <summary>Freeze through the end of the bloom. 7.7s.</summary>
        public const double LiveSeconds =
            FreezeSeconds + CrackSeconds + DrainSeconds + BlackSeconds + BloomSeconds;

        /// <summary>
        /// The catch-up's whole run, per §2.4's "condensed 6s crack". Every beat is scaled by
        /// <see cref="CatchUpScale"/> rather than re-authored, so the two shows cannot drift.
        /// </summary>
        public const double CatchUpSeconds = 6.0;

        /// <summary>The compression factor. Just under 0.78 — fast, and still legible.</summary>
        public const double CatchUpScale = CatchUpSeconds / LiveSeconds;

        /// <summary>The reduced-motion crossfade: dark to the held bloom, once, and done.</summary>
        public const double ReducedCrossfadeSeconds = 1.5;

        /// <summary>
        /// The frame for an elapsed time.
        /// </summary>
        /// <param name="kind">Live or CatchUp. <see cref="DescentShowKind.Ignition"/> has its own
        /// table (<see cref="DescentIgnitionTimeline"/>) and is not accepted here.</param>
        /// <param name="elapsedSeconds">Seconds since the window's clock started. Negatives are
        /// treated as zero rather than rejected: a stopwatch read across a system clock adjustment
        /// should show the first frame again, not throw in front of a fullscreen show.</param>
        /// <param name="reducedMotion">The snapped path.</param>
        public static DescentFuseFrame FrameAt(DescentShowKind kind, double elapsedSeconds, bool reducedMotion)
        {
            if (double.IsNaN(elapsedSeconds) || elapsedSeconds < 0) elapsedSeconds = 0;

            if (reducedMotion)
            {
                // ONE crossfade, then the hold. The bloom clock starts at zero so the handoff
                // window opens the instant the show does — a reduced-motion subject must not wait
                // longer for the ceremony than anyone else.
                if (elapsedSeconds < ReducedCrossfadeSeconds)
                    return new DescentFuseFrame(DescentFuseStage.Bloom,
                        elapsedSeconds / ReducedCrossfadeSeconds, elapsedSeconds);
                return new DescentFuseFrame(DescentFuseStage.Held, 1.0, elapsedSeconds);
            }

            var scale = kind == DescentShowKind.CatchUp ? CatchUpScale : 1.0;

            double freeze = FreezeSeconds * scale;
            double crack  = CrackSeconds  * scale;
            double drain  = DrainSeconds  * scale;
            double black  = BlackSeconds  * scale;
            double bloom  = BloomSeconds  * scale;

            double bloomStart = freeze + crack + drain + black;
            double sinceBloom = elapsedSeconds - bloomStart;

            double t = elapsedSeconds;
            if (t < freeze) return new DescentFuseFrame(DescentFuseStage.Freeze, t / freeze, sinceBloom);
            t -= freeze;
            if (t < crack)  return new DescentFuseFrame(DescentFuseStage.Crack, t / crack, sinceBloom);
            t -= crack;
            if (t < drain)  return new DescentFuseFrame(DescentFuseStage.Drain, t / drain, sinceBloom);
            t -= drain;
            if (t < black)  return new DescentFuseFrame(DescentFuseStage.Black, t / black, sinceBloom);
            t -= black;
            if (t < bloom)  return new DescentFuseFrame(DescentFuseStage.Bloom, t / bloom, sinceBloom);

            return new DescentFuseFrame(DescentFuseStage.Held, 1.0, sinceBloom);
        }

        /// <summary>
        /// When the bloom begins, for a given shape. This is the moment the keepsake flag is written
        /// on the live path (§2.3: "when the live sequence reached bloom"), so it is worth having as
        /// a number rather than as a comparison buried in a render loop.
        /// </summary>
        public static double BloomStartSeconds(DescentShowKind kind, bool reducedMotion)
        {
            if (reducedMotion) return 0.0;
            var scale = kind == DescentShowKind.CatchUp ? CatchUpScale : 1.0;
            return (FreezeSeconds + CrackSeconds + DrainSeconds + BlackSeconds) * scale;
        }
    }

    /// <summary>
    /// THE YEAR ONE IGNITION's clock (CONTRACT-FUSE-0816 §2.4) — the spiral drawing itself outward
    /// after a committed choice.
    ///
    /// <para>Same discipline as the crack's table: pure, testable, and the single source of "when
    /// does the line appear" so the copy and the drawing cannot disagree.</para>
    ///
    /// <para><b>Reduced motion snaps to the finished spiral</b> and holds it for three seconds
    /// before the fade — the contract's wording, and the reason the reduced path is longer on the
    /// hold than the full path is that a subject who never saw it draw needs longer to read it.</para>
    /// </summary>
    public static class DescentIgnitionTimeline
    {
        /// <summary>A beat of black before the first light. Lets the ceremony's close land.</summary>
        public const double LeadSeconds = 0.4;

        /// <summary>The draw. Centre to rim.</summary>
        public const double DrawSeconds = 3.0;

        /// <summary>The finished spiral, held under the line.</summary>
        public const double HoldSeconds = 2.0;

        /// <summary>Out.</summary>
        public const double FadeSeconds = 1.0;

        /// <summary>Reduced motion's longer hold on a spiral it never watched arrive.</summary>
        public const double ReducedHoldSeconds = 3.0;

        /// <summary>How long a notch takes to pop once the draw reaches it.</summary>
        public const double NotchPopSeconds = 0.35;

        /// <summary>
        /// Stations along the arm, as fractions of the draw. Seven, spaced so the last one lands
        /// before the arm finishes — a notch popping on the final frame reads as a glitch.
        /// </summary>
        public static readonly double[] NotchAt = { 0.10, 0.24, 0.38, 0.52, 0.66, 0.79, 0.91 };

        public static double TotalSeconds(bool reducedMotion) => reducedMotion
            ? ReducedHoldSeconds + FadeSeconds
            : LeadSeconds + DrawSeconds + HoldSeconds + FadeSeconds;

        /// <summary>
        /// How much of the arm is drawn, 0..1. Reduced motion is 1 from the first frame — the
        /// snapped state, present rather than missing.
        /// </summary>
        public static double DrawFraction(double elapsedSeconds, bool reducedMotion)
        {
            if (reducedMotion) return 1.0;
            if (double.IsNaN(elapsedSeconds) || elapsedSeconds <= LeadSeconds) return 0.0;
            var t = (elapsedSeconds - LeadSeconds) / DrawSeconds;
            return t >= 1.0 ? 1.0 : t;
        }

        /// <summary>
        /// A notch's pop, 0 (unlit) to 1 (fully gold), for the notch at arm fraction
        /// <paramref name="notchAt"/>. Reduced motion returns 1 for every notch: the finished
        /// spiral is finished, notches and all.
        /// </summary>
        public static double NotchGlow(double elapsedSeconds, double notchAt, bool reducedMotion)
        {
            if (reducedMotion) return 1.0;
            var drawn = DrawFraction(elapsedSeconds, reducedMotion);
            if (drawn < notchAt) return 0.0;

            // Seconds since the arm passed this station, converted back through the draw's rate.
            var since = (drawn - notchAt) * DrawSeconds;
            var t = since / NotchPopSeconds;
            return t >= 1.0 ? 1.0 : t;
        }

        /// <summary>The line's opacity, 0..1. It arrives with the hold and leaves with the fade.</summary>
        public static double LineOpacity(double elapsedSeconds, bool reducedMotion)
        {
            var lineIn = reducedMotion ? 0.0 : LeadSeconds + DrawSeconds;
            var fadeStart = FadeStartSeconds(reducedMotion);

            if (elapsedSeconds < lineIn) return 0.0;
            if (elapsedSeconds >= fadeStart) return 0.0;

            // A half-second rise, then flat for the rest of the hold.
            var t = (elapsedSeconds - lineIn) / 0.5;
            return t >= 1.0 ? 1.0 : t;
        }

        public static double FadeStartSeconds(bool reducedMotion) => reducedMotion
            ? ReducedHoldSeconds
            : LeadSeconds + DrawSeconds + HoldSeconds;

        /// <summary>The whole window's opacity during the closing fade, 1 until it starts.</summary>
        public static double ShowOpacity(double elapsedSeconds, bool reducedMotion)
        {
            var fadeStart = FadeStartSeconds(reducedMotion);
            if (elapsedSeconds <= fadeStart) return 1.0;
            var t = (elapsedSeconds - fadeStart) / FadeSeconds;
            return t >= 1.0 ? 0.0 : 1.0 - t;
        }
    }
}
