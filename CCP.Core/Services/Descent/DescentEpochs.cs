using System;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>Epoch constants. Two of them, and they are NOT the same number twice.</summary>
    public static class DescentEpochs
    {
        /// <summary>
        /// THE BUILD'S epoch, sent as <c>descent_epoch</c> in every /v2/user/sync body
        /// (CONTRACTS §1). It says "this client understands the Descent" — nothing about the
        /// account. The server uses it as the resurrection guard: once a record is migrated, a
        /// body arriving WITHOUT this number has its xp/level/total_xp_earned ignored, which is
        /// what stops an unsynced old-curve phone from resurrecting a pre-migration level.
        ///
        /// <para>Unconditional on the wire by design — it is the one field CONTRACTS §1 exempts
        /// from the flag-off-is-byte-identical rule, because it is the flag that identifies the
        /// build.</para>
        /// </summary>
        public const int ClientEpoch = 1;

        /// <summary>An account that has not been through the ceremony. Curve v1, no cycle mark.</summary>
        public const int AccountLegacy = 0;

        /// <summary>An account that has. Curve v2 from this point on.</summary>
        public const int AccountDescent = 1;

        /// <summary>
        /// THE DAY MONTHLY SEASONS STOPPED EXISTING: 2026-09-01 UTC.
        ///
        /// <para>This is a DATE, and the header above says there is no client flag — so read the
        /// difference carefully, because it is the reason this is allowed to exist. A flag would
        /// turn the ceremony ON, and nothing here does: the offer still arrives only from a server
        /// with DESCENT_MIGRATION armed, exactly as before. What this date does is turn SEASON-ERA
        /// behaviour OFF once that behaviour can no longer be true — a countdown to a season end
        /// that will never arrive, and a "your season was reset" that no server can honestly send
        /// again.</para>
        ///
        /// <para>The two risks are not symmetrical, which is what settles it. The ceremony must
        /// never fire early, so it gets no local date. These two must never fire LATE, and their
        /// failure mode is a user being told their level was wiped, or having it actually wiped,
        /// by a server whose suppression failed open. Being wrong-and-early here costs a hidden
        /// countdown line and a refused reset that an admin can redo by hand.</para>
        /// </summary>
        public static readonly DateTime SeasonsEndUtc =
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The first "yyyy-MM" season key on the far side of <see cref="SeasonsEndUtc"/>.</summary>
        public const string FirstPostDescentSeasonKey = "2026-09";

        /// <summary>True once wall-clock UTC has reached <see cref="SeasonsEndUtc"/>.</summary>
        public static bool SeasonsHaveEnded => DateTime.UtcNow >= SeasonsEndUtc;

        /// <summary>
        /// True when a well-formed "yyyy-MM" key names a month at or after the Descent. Ordinal
        /// compare is chronological for zero-padded month keys, the same trick the recap uses.
        ///
        /// <para>A malformed or absent key is NOT post-Descent. It cannot be dated at all, and
        /// reading a refusal into garbage would let a typo decide someone's level; callers that
        /// need an answer for an undateable key must date it some other way (the wall clock).</para>
        /// </summary>
        public static bool IsPostDescentSeasonKey(string? seasonKey) =>
            !string.IsNullOrWhiteSpace(seasonKey)
            && ProfileAdopt.LooksLikeMonthKey(seasonKey!)
            && string.CompareOrdinal(seasonKey!, FirstPostDescentSeasonKey) >= 0;
    }
}
