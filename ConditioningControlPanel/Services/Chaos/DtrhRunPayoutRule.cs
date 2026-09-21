using System;

namespace ConditioningControlPanel.Services.Chaos
{
    /// <summary>What an abandoned descent is worth: the duration it is paid for, and whether it
    /// counts as a run at all.</summary>
    /// <param name="PaidDurationSec">Fed to <c>ChaosRunRewardInput.RunDurationSec</c>, which is
    /// what the completion bonus is scaled from.</param>
    /// <param name="CountsAsRun">Whether <c>RunsCompleted</c> moves, and with it the first-fall
    /// bonus, the rank gates, the shelf reveals and the scripted first descent.</param>
    public readonly record struct DtrhRunPayout(double PaidDurationSec, bool CountsAsRun);

    /// <summary>
    /// How a descent that was LEFT rather than finished is paid.
    ///
    /// <para>Two holes open the moment every exit books, and both come from the same place:
    /// <c>AwardRunRewards</c> scales its predictable Spark floor off the run's CONFIGURED length,
    /// not the time actually fallen. An endless descent configures 720 s, so a hold-Escape one
    /// second in would otherwise pay the fully maxed completion bonus, and repeatably; any timed
    /// run of three minutes or more would pay its whole floor at t = 1 s the same way. So an
    /// abandoned run is paid pro rata for the seconds it really fell.</para>
    ///
    /// <para>The second hole is not about money. <c>RunsCompleted</c> is a progression gate -
    /// ranks, Warren shelf reveals, and <c>ChaosHappyPath</c>'s one-shot scripted first descent,
    /// which is keyed on <c>RunsCompleted == 0</c>. A new player who bounced off the tutorial at
    /// five seconds would burn that intro forever and pocket the first-fall bonus for it. So a
    /// bounce pays its pro rata Sparks and XP and nothing else: below
    /// <see cref="MinCountedSec"/> an abandoned descent is not a descent.</para>
    ///
    /// <para>A run that reached its own ending is untouched by all of this.</para>
    /// </summary>
    public static class DtrhRunPayoutRule
    {
        /// <summary>How long an abandoned descent must have lasted before it counts as one.
        /// A minute is the shortest run the game itself deals.</summary>
        public const double MinCountedSec = 60.0;

        /// <summary>
        /// How far the page's own elapsed clock may run ahead of the host's wall clock before it
        /// is disbelieved. The page's clock PAUSES (a covering video, an in-world freeze) and the
        /// wall clock does not, so an honest page always reports LESS than the host saw: the
        /// slack is for rounding and for the seconds between the last progress ping and the
        /// window dying, not for a margin of play.
        /// </summary>
        public const double HostClockSlackSec = 5.0;

        /// <param name="abandoned">The page said so, or the host synthesised the booking from its
        /// last progress snapshot because the window died.</param>
        /// <param name="configuredDurationSec">The run's dealt length; what a completed run is
        /// paid for.</param>
        /// <param name="elapsedSec">Seconds actually fallen, as the PAGE reports them.</param>
        /// <param name="hostElapsedSec">Seconds since the host saw run-started, by its own clock.
        /// Null when the host has none (no run-started was seen), which leaves the page's word as
        /// the only evidence. The host has no clock of its own inside the descent, so without
        /// this every figure the payout rests on comes from the page - and the whole point of the
        /// rule is that one of those figures is worth Sparks.</param>
        public static DtrhRunPayout For(bool abandoned, double configuredDurationSec, double elapsedSec,
            double? hostElapsedSec = null)
        {
            var configured = Math.Max(0, configuredDurationSec);
            if (!abandoned) return new DtrhRunPayout(configured, true);

            var fallen = Math.Clamp(elapsedSec, 0, configured);
            if (hostElapsedSec is double host)
                fallen = Math.Min(fallen, Math.Max(0, host) + HostClockSlackSec);

            return new DtrhRunPayout(fallen, fallen >= MinCountedSec);
        }
    }
}
