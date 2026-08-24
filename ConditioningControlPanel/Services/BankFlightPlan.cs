using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// THE BANK's flight arithmetic and nothing else: how many tokens a pot is worth, when each one
    /// leaves, how long it flies, and how far its arc bows off the straight line to the counter.
    ///
    /// <para>Pure and static on purpose. The canvas half of THE BANK can only be judged by eye, but
    /// the numbers underneath it are exactly where the House Book's feel spec can drift silently - a
    /// stagger that creeps to 200ms reads as a queue instead of a spill, and a token count that
    /// scales linearly with XP turns a session claim into confetti. Keeping the arithmetic here
    /// makes the spec a test rather than a comment.</para>
    ///
    /// <para><b>Why the cap is 7.</b> The House Book's line is "the FEELING scales, not the particle
    /// count": past seven the eye stops counting tokens and starts seeing a spray, so a bigger award
    /// buys a longer, fuller flight and never a crowd.</para>
    ///
    /// <para>Deterministic from its seed - same seed, same flight - so a plan can be asserted
    /// headlessly, and so nothing about the look of a moment depends on a clock.</para>
    /// </summary>
    public static class BankFlightPlan
    {
        // ---- DIALS (House Book: 3-7 tokens, 500-650ms ease-in arc, 60-80ms stagger) ----

        /// <summary>Floor on the token count: fewer than three does not read as "value", it reads as a glitch.</summary>
        public const int MinTokens = 3;

        /// <summary>Ceiling on the token count. See the class remarks - this is a feel decision, not a budget one.</summary>
        public const int MaxTokens = 7;

        public const double StaggerMinMs = 60;
        public const double StaggerMaxMs = 80;
        public const double DurationMinMs = 500;
        public const double DurationMaxMs = 650;

        /// <summary>Smallest bow magnitude. Below this the arc is invisible and the flight reads as a straight slide.</summary>
        public const double ArcBowMin = 0.10;

        /// <summary>Largest bow magnitude. Above this the token arcs far enough to look thrown rather than drawn in.</summary>
        public const double ArcBowMax = 0.22;

        /// <summary>
        /// How many tokens a pot of <paramref name="xpSum"/> XP spends. A step table and not a
        /// formula: the steps are the only thing a subject can actually perceive, and a table makes
        /// the boundaries assertable. NaN is treated as the smallest pot rather than throwing -
        /// this sits downstream of a live XP figure and must never be the thing that breaks an award.
        /// </summary>
        public static int TokenCount(double xpSum)
        {
            if (double.IsNaN(xpSum)) return MinTokens;
            if (xpSum < 15) return 3;
            if (xpSum < 50) return 4;
            if (xpSum < 120) return 5;
            if (xpSum < 300) return 6;
            return MaxTokens;
        }

        /// <summary>
        /// One token's slot in a flight. <paramref name="DelayMs"/> is measured from the flight's
        /// start, not from the previous token, so a consumer never has to accumulate.
        /// <paramref name="ArcBow"/> is a SIGNED fraction of the origin-to-target distance that the
        /// bezier control point sits off the straight line - the sign is which side it bows to.
        /// </summary>
        public readonly record struct Token(double DelayMs, double DurationMs, double ArcBow);

        /// <summary>
        /// Lay out <paramref name="count"/> tokens (clamped to <see cref="MaxTokens"/>) from
        /// <paramref name="seed"/>. Every random draw is inside the House Book's bands, so any plan
        /// this returns is a legal flight no matter what seed it was handed.
        ///
        /// <para>Bow signs ALTERNATE from a seeded start rather than being drawn independently: a
        /// run of same-sign draws would send every token round the same side, which reads as one
        /// thick stream instead of a spill. Alternating guarantees the fan; the seed only decides
        /// which side it opens on.</para>
        /// </summary>
        public static Token[] Plan(int count, int seed)
        {
            if (count <= 0) return Array.Empty<Token>();
            count = Math.Min(count, MaxTokens);

            var rng = new Random(seed);
            var plan = new Token[count];

            double delay = 0;
            int sign = rng.Next(2) == 0 ? -1 : 1;

            for (int i = 0; i < count; i++)
            {
                double duration = DurationMinMs + rng.NextDouble() * (DurationMaxMs - DurationMinMs);
                double bow = ArcBowMin + rng.NextDouble() * (ArcBowMax - ArcBowMin);

                plan[i] = new Token(delay, duration, bow * sign);

                sign = -sign;
                delay += StaggerMinMs + rng.NextDouble() * (StaggerMaxMs - StaggerMinMs);
            }

            return plan;
        }

        /// <summary>
        /// Milliseconds from the flight's start until the LAST token lands. A max and not
        /// "last delay + last duration": durations vary by up to 150ms while the stagger is only
        /// 60-80ms, so tokens genuinely can land out of order and the final token in the array is
        /// not always the final one to arrive. Anything timing a watchdog off this needs the true
        /// envelope, not the tail.
        /// </summary>
        public static double EnvelopeMs(Token[] plan)
        {
            if (plan == null || plan.Length == 0) return 0;

            double max = 0;
            for (int i = 0; i < plan.Length; i++)
            {
                double end = plan[i].DelayMs + plan[i].DurationMs;
                if (end > max) max = end;
            }
            return max;
        }
    }
}
