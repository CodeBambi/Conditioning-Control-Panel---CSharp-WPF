using System;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// When the Brain Parasite drain may write settings.json.
    ///
    /// <para><b>The bug this exists for (#1311).</b> The drain takes 3 XP every 2 s by design, and
    /// every tick called <c>Settings.Save()</c>. The save debounce is 500 ms, shorter than the tick,
    /// so it never coalesced anything: the whole settings graph was serialized and fsynced to disk
    /// every 2 s for as long as the companion was equipped, all session long. The drain keeps its
    /// cadence; only the WRITE is throttled. The XP lives in memory between writes, any other save
    /// (an XP award, a toggle) carries it, and shutdown's SaveImmediate flushes the rest. At worst a
    /// hard crash forgets up to <see cref="SaveInterval"/> of drain, which only favours the player.</para>
    /// </summary>
    public static class DrainSaveThrottle
    {
        /// <summary>At most one drain-driven settings write per this long.</summary>
        public static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(30);

        /// <summary>
        /// True when this drain tick should save. <paramref name="lastSaveUtc"/> is null before the
        /// first drain save of the run. <paramref name="reachedZero"/> saves at once: the drain stops
        /// there, so no later tick would ever write the final value.
        /// </summary>
        public static bool ShouldSave(DateTime? lastSaveUtc, DateTime nowUtc, bool reachedZero)
        {
            if (reachedZero) return true;
            if (lastSaveUtc is not { } last) return true;
            // A clock stepped backwards must not stall saves forever.
            if (nowUtc < last) return true;
            return nowUtc - last >= SaveInterval;
        }
    }
}
