using System;

namespace ConditioningControlPanel.Services.Banner
{
    /// <summary>
    /// When an interlude is allowed to take the ticker. Pure, clock-injected, no timers of its own:
    /// MainWindow.MarqueeReads.cs owns the beat and just reports loops into this.
    ///
    /// <para>Three rules, all of which must hold. Every fourth completed loop of the ticker is the
    /// earliest a read can want the stage; three minutes must have passed since the last one
    /// actually played; and nothing plays in the first two minutes after launch, because the first
    /// two minutes belong to the startup ladder and the user's own first click.</para>
    ///
    /// <para>Fast mode (the <c>MarqueeReadDebugFast</c> setting or the CCP_MARQUEE_READS_FAST env
    /// var) collapses all three to twenty seconds so the acts can be watched at a desk instead of
    /// waited out.</para>
    /// </summary>
    public sealed class MarqueeReadCadence
    {
        public const int LoopsBetweenReads = 4;
        public static readonly TimeSpan FloorGap = TimeSpan.FromMinutes(3);
        public static readonly TimeSpan LaunchDelay = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan FastGap = TimeSpan.FromSeconds(20);

        private readonly Func<DateTime> _clock;
        private readonly DateTime _armedAt;
        private DateTime? _lastPlayedAt;
        private int _loops;

        /// <param name="clock">UTC now. Injected so the rule is testable without waiting.</param>
        public MarqueeReadCadence(Func<DateTime>? clock = null)
        {
            _clock = clock ?? (() => DateTime.UtcNow);
            _armedAt = _clock();
        }

        /// <summary>Desk mode: 1 loop, 20s floor, 20s launch delay.</summary>
        public bool Fast { get; set; }

        /// <summary>Loops counted since the last interlude. Visible for tests and the debug log.</summary>
        public int LoopsSinceRead => _loops;

        private int LoopsNeeded => Fast ? 1 : LoopsBetweenReads;
        private TimeSpan Gap => Fast ? FastGap : FloorGap;
        private TimeSpan Delay => Fast ? FastGap : LaunchDelay;

        /// <summary>
        /// One ticker loop finished. Returns true when this loop earns an interlude; the caller
        /// still has its own gates (pool enabled, motion, a line to read) to clear after this.
        /// </summary>
        public bool NoteLoop()
        {
            if (_loops < int.MaxValue) _loops++;
            return IsDue();
        }

        /// <summary>Whether an interlude is due right now, without counting a loop.</summary>
        public bool IsDue()
        {
            if (_loops < LoopsNeeded) return false;

            var now = _clock();
            if (now - _armedAt < Delay) return false;
            if (_lastPlayedAt.HasValue && now - _lastPlayedAt.Value < Gap) return false;
            return true;
        }

        /// <summary>An interlude actually played. Resets the loop count and arms the floor.</summary>
        public void NotePlayed()
        {
            _lastPlayedAt = _clock();
            _loops = 0;
        }

        /// <summary>
        /// The interlude was skipped (no eligible read, a gate closed). The loop count is NOT reset,
        /// so the next loop tries again instead of waiting another four, but the floor is untouched
        /// because nothing was on screen to space out from.
        /// </summary>
        public void NoteSkipped()
        {
            _loops = LoopsNeeded;
        }
    }
}
