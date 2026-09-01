using System;

namespace ConditioningControlPanel.Helpers
{
    /// <summary>What the rail comet should do on this pass.</summary>
    public enum ProgramCometAction
    {
        /// <summary>A usable travel distance was measured - start (or restart) the run.</summary>
        Run = 0,
        /// <summary>Nothing measurable yet. Re-ask later, at a priority BELOW input.</summary>
        Retry = 1,
        /// <summary>Out of attempts. No comet this visit; the rest of the tab is untouched.</summary>
        GiveUp = 2,
    }

    /// <summary>The decision, plus the width to animate over and the attempt count to store back.</summary>
    public readonly struct ProgramCometDecision
    {
        public ProgramCometDecision(ProgramCometAction action, double width, int attempts)
        {
            Action = action;
            Width = width;
            Attempts = attempts;
        }

        public ProgramCometAction Action { get; }

        /// <summary>Travel distance for the run. Meaningful only when <see cref="Action"/> is Run.</summary>
        public double Width { get; }

        /// <summary>The attempt counter to WRITE BACK to the caller's field after this decision.</summary>
        public int Attempts { get; }
    }

    /// <summary>
    /// The rail comet's "may I run yet?" gate, extracted from the FX code so the thing that once
    /// hard-froze the app is a pure function with tests instead of a branch inside a dispatcher
    /// callback.
    ///
    /// <para><b>Why this exists.</b> The comet's travel distance IS the rail's measured width, so
    /// the effect cannot start before layout has run. The original gate read that width from a host
    /// that is authored <c>Visibility="Collapsed"</c> and only made visible AFTER the gate, so the
    /// width was structurally always 0; the retry then re-posted itself at
    /// <c>DispatcherPriority.Loaded</c> (6) and cleared its own one-shot guard before recursing,
    /// with no attempt cap. Loaded outranks Input (5), so the retry chain starved user input
    /// forever - a silent hard freeze with no crash log, on every visit to a hot Programs tab
    /// (ccp-bugs #984, #993, #996, #1001).</para>
    ///
    /// <para><b>The contract this encodes.</b> Prefer the host's own width; fall back to the rail's
    /// when the host has not been measured yet; below <see cref="MinTravelWidth"/> retry at most
    /// <see cref="MaxAttempts"/> times and then give up quietly. The counter is returned rather than
    /// mutated so the caller can only ever store a bounded value - "clear the guard, then recurse"
    /// is not expressible here.</para>
    /// </summary>
    public static class ProgramCometGate
    {
        /// <summary>
        /// Shortest run worth animating, in DIPs. The comet head is 130 wide and enters from -140,
        /// so anything under this is a smear inside its own clip rather than a comet.
        /// </summary>
        public const double MinTravelWidth = 60.0;

        /// <summary>
        /// Hard cap on re-asks. Three passes is more than layout has ever needed; past that the
        /// honest answer is that this surface is not going to measure, and a missing cosmetic
        /// effect is the correct failure.
        /// </summary>
        public const int MaxAttempts = 3;

        /// <summary>
        /// Decides what the comet should do given the two candidate widths and how many times this
        /// run has already been deferred.
        /// </summary>
        /// <param name="hostWidth">ActualWidth of the comet's clip host (0 until it is measured).</param>
        /// <param name="railWidth">ActualWidth of the day rail the host is stretched inside.</param>
        /// <param name="attempts">Deferrals already spent on this run. Pass the stored counter.</param>
        public static ProgramCometDecision Decide(double hostWidth, double railWidth, int attempts)
        {
            var width = Usable(hostWidth);
            if (width <= 0) width = Usable(railWidth);

            // A measurable rail resets the counter: the next stall gets a fresh budget rather than
            // inheriting one spent minutes ago on a different layout.
            if (width >= MinTravelWidth)
                return new ProgramCometDecision(ProgramCometAction.Run, width, 0);

            var spent = attempts < 0 ? 0 : attempts;
            if (spent >= MaxAttempts)
                return new ProgramCometDecision(ProgramCometAction.GiveUp, 0, spent);

            return new ProgramCometDecision(ProgramCometAction.Retry, 0, spent + 1);
        }

        /// <summary>NaN/Infinity/negative all mean "not measured", never "animate over that".</summary>
        private static double Usable(double width)
        {
            if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0) return 0;
            return width;
        }
    }
}
