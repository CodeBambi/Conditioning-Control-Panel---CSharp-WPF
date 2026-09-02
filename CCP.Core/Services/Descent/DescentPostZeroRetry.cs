using System;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>
    /// THE POST-ZERO RETRY's policy (0825 hunt, F1) — pure, so the tests can pin it.
    ///
    /// <para><b>The hole.</b> Profile sync is event-driven: login, level-up, quest, preset, exit.
    /// The only periodic sync runs during a session and only for OAuth accounts. So when the zero
    /// show's handoff timed out ("The ceremony awaits.") the app made no further attempt of its
    /// own — the subject sat in a step-4-dimmed room, spiral fogged, until they happened to level
    /// up or restart. On a night when the server is slow under everyone syncing at once, that is
    /// the common case, not the edge.</para>
    ///
    /// <para><b>The shape.</b> One sync a minute for a bounded while, and every reason to stop is
    /// listed here: the ceremony opened (the sync did its job), an offer is in hand (the migration
    /// service owns it from here), the account is migrated or has a choice pending (nothing to
    /// ask), the fuse went dark (kill switch), or the budget ran out. ProfileSyncService's own
    /// thirty-second cooldown and offline guard sit underneath; a refused attempt costs nothing
    /// and still counts, which is what bounds the loop.</para>
    /// </summary>
    internal static class DescentPostZeroRetry
    {
        /// <summary>Fifteen minutes of asking, then the standing re-offer contract takes over.</summary>
        public const int MaxAttempts = 15;

        /// <summary>Well outside the sync cooldown, so every attempt is a real request.</summary>
        public static readonly TimeSpan Every = TimeSpan.FromSeconds(60);

        /// <summary>Whether attempt number <paramref name="attemptsSoFar"/> + 1 should be made.</summary>
        public static bool ShouldContinue(
            int attemptsSoFar,
            bool fuseArmed,
            bool ceremonyOpen,
            bool offerInHand,
            bool migrationCompleted,
            bool choicePending)
        {
            if (attemptsSoFar >= MaxAttempts) return false;
            if (!fuseArmed) return false;
            if (ceremonyOpen) return false;
            if (offerInHand) return false;
            if (migrationCompleted) return false;
            if (choicePending) return false;
            return true;
        }
    }
}
