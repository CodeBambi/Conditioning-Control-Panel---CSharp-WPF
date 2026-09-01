using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// THE BANK's counter arithmetic: given a held display value, a pot and a stream of landings,
    /// what number should the XP readout be showing right now.
    ///
    /// <para><b>Why this is not inline in the shell.</b> Every one of these four decisions is a
    /// place where the display can quietly start lying, which is the one thing House Law I forbids
    /// - the ledger never lies, only its presentation is staged. A step that divides the pot into
    /// equal slices and adds them up drifts off the true total by a fraction of an XP per landing;
    /// a target computed from "whatever the ledger says right now" hands one flight the XP a later
    /// flight is going to be seen to deliver; a step tween longer than the stagger leaves landings
    /// queueing behind an animation that has not finished. None of that is visible in a play-test,
    /// all of it is trivially assertable here.</para>
    ///
    /// <para>Pure and static: no clock, no WPF, no state. The shell owns the timers and the
    /// elements; this owns only the numbers they carry.</para>
    /// </summary>
    public static class BankCounterScript
    {
        // ---- DIALS ----

        /// <summary>
        /// Longest a single step tween may last. The House Book wants a TICK per landing, not a
        /// count-up: past about a tenth of a second the eye stops reading the step as an arrival
        /// and starts reading it as an odometer that happens to be slow.
        /// </summary>
        public const double MaxStepMs = 90;

        /// <summary>
        /// Fraction of the SHORTEST possible gap between two landings that a mid-flight step is
        /// allowed to occupy. Below 1.0 by construction: a step still running when the next token
        /// lands is replaced mid-tween, and the counter visibly stutters instead of ticking.
        /// </summary>
        public const double StepSafetyFactor = 0.9;

        /// <summary>
        /// The value the counter is standing at when a flight begins - i.e. what the flight has to
        /// count UP FROM. Mirrors <c>AnimateXpDisplay</c>'s own "from" rule deliberately: a level
        /// change wraps the readout back to zero, so a remembered value from the previous level is
        /// not a starting point, it is a wrong number that the first token would appear to correct.
        /// </summary>
        /// <param name="lastShown">The last value the odometer was driven to (NaN if never).</param>
        /// <param name="lastLevelShown">The level that value belonged to.</param>
        /// <param name="level">The level the flight is being staged at.</param>
        public static double StartValue(double lastShown, int lastLevelShown, int level)
        {
            if (double.IsNaN(lastShown) || double.IsInfinity(lastShown) || lastShown < 0) return 0;
            return level == lastLevelShown ? lastShown : 0;
        }

        /// <summary>
        /// Where THIS flight's last token must land: the pot it is carrying, added to where the
        /// counter started - never "whatever the ledger says now".
        ///
        /// <para>The two clamps are the honesty rails. The ceiling is the live ledger: a flight may
        /// never show more XP than the player actually has, which is what would happen if the
        /// display had already been credited with part of this pot (the first award of a pot lands
        /// before the pot exists - see MainWindow.BankFx.cs). The floor is the start: a pot only
        /// ever adds, so a counter that would run backwards simply does not move, and the release
        /// to truth that follows the flight owns the correction.</para>
        ///
        /// <para>Awards that arrive mid-flight are deliberately NOT swept in. They belong to the
        /// next pot, and the next flight is what will be seen to deliver them.</para>
        /// </summary>
        /// <param name="start">Where the counter stood when the flight left.</param>
        /// <param name="potXp">The pot this flight is carrying.</param>
        /// <param name="truthXp">The ledger's current XP for this level.</param>
        public static double Target(double start, double potXp, double truthXp)
        {
            if (double.IsNaN(start) || double.IsInfinity(start)) return 0;
            if (double.IsNaN(truthXp) || double.IsInfinity(truthXp)) return start;
            if (double.IsNaN(potXp) || double.IsInfinity(potXp) || potXp <= 0) return start;

            double ceiling = Math.Max(start, truthXp);
            return Math.Clamp(start + potXp, start, ceiling);
        }

        /// <summary>
        /// The number to show after the <paramref name="landingOrdinal"/>-th token of
        /// <paramref name="count"/> has landed (ordinal counts LANDINGS from 0, not plan order -
        /// tokens land out of order and only the arrival count is safe to divide by).
        ///
        /// <para>The last landing returns <paramref name="target"/> EXACTLY rather than the sum of
        /// its own slices. Slices are floating point and the ledger is the authority; a flight that
        /// ends on 1249.9999999 has told a lie for the sake of arithmetic tidiness.</para>
        /// </summary>
        public static double StepValue(double start, double target, int landingOrdinal, int count)
        {
            if (count <= 0) return target;
            if (double.IsNaN(start) || double.IsInfinity(start)) return target;
            if (double.IsNaN(target) || double.IsInfinity(target)) return start;

            if (landingOrdinal < 0) landingOrdinal = 0;
            if (landingOrdinal >= count - 1) return target;

            return start + (target - start) * (landingOrdinal + 1) / count;
        }

        /// <summary>
        /// How long one step's mini-tween may run. A mid-flight step is capped under the shortest
        /// legal stagger so it has finished before the next token can possibly arrive; the last
        /// step has nothing chasing it and gets the full <see cref="MaxStepMs"/>, which is also the
        /// beat the thud and the counter pop are timed against.
        /// </summary>
        public static double StepMs(bool isLast)
            => isLast ? MaxStepMs
                      : Math.Min(MaxStepMs, BankFlightPlan.StaggerMinMs * StepSafetyFactor);
    }
}
