using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// How long profile sync waits after a refused or failed attempt.
    ///
    /// <para>Why: <see cref="ProfileSyncService.SyncProfileAsync"/> only stamped its 30 s cooldown on a
    /// SUCCESS (and on a 429). Every other answer left the door open, and a dozen event-driven
    /// callers (XP nudges, level ups, quest resets, Goon prefs, leaderboard refreshes, 401 recovery)
    /// each fired another sync the moment something happened. Sep 2026 telemetry: one desktop with a
    /// five-hour clock skew sent ~126 signed syncs a minute, every one of them a 403.</para>
    ///
    /// <para>The schedule doubles from <see cref="First"/> to <see cref="Cap"/>, so a client that can
    /// never succeed settles at one attempt every few minutes. It is keyed to what the failure was
    /// earned with: a new auth token (a sign-in, a 401 heal) or a clock offset that moved (the 403
    /// that taught us the server's time) opens the gate at once, because the next attempt is no
    /// longer the same attempt.</para>
    /// </summary>
    public static class SyncFailureBackoff
    {
        public static readonly TimeSpan First = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan Cap = TimeSpan.FromMinutes(15);

        /// <summary>The wait after the <paramref name="consecutiveFailures"/>-th failure in a row (1-based).</summary>
        public static TimeSpan Delay(int consecutiveFailures)
        {
            if (consecutiveFailures <= 0) return TimeSpan.Zero;
            // 30, 60, 120, 240, 480, 900 (capped). Shift bounded so the long never overflows.
            var shift = Math.Min(consecutiveFailures - 1, 16);
            var seconds = First.TotalSeconds * (1L << shift);
            return seconds >= Cap.TotalSeconds ? Cap : TimeSpan.FromSeconds(seconds);
        }

        /// <summary>
        /// Whether a sync attempted at <paramref name="nowUtc"/> should be skipped.
        /// </summary>
        /// <param name="blockedUntilUtc">When the current backoff ends, or null for none.</param>
        /// <param name="tokenAtFailure">The auth token the last failure was earned with.</param>
        /// <param name="currentToken">The auth token the next attempt would carry.</param>
        /// <param name="offsetAtFailure">The server clock offset when the last failure landed.</param>
        /// <param name="currentOffset">The server clock offset now.</param>
        public static bool ShouldSkip(
            DateTime nowUtc,
            DateTime? blockedUntilUtc,
            string? tokenAtFailure,
            string? currentToken,
            TimeSpan offsetAtFailure,
            TimeSpan currentOffset)
        {
            if (blockedUntilUtc == null || nowUtc >= blockedUntilUtc.Value) return false;
            if (!string.Equals(tokenAtFailure ?? "", currentToken ?? "", StringComparison.Ordinal)) return false;
            if ((currentOffset - offsetAtFailure).Duration() > ServerClock.Deadband) return false;
            return true;
        }
    }
}
