using System;

namespace ConditioningControlPanel.Services.Descent
{
    // ============================================================================
    // THE MIGRATION CEREMONY — wire constants, the offer, and the two choices.
    //
    // Spec: planning/one-descent/CONTRACTS-0812-FINISH.md §1 (epoch), §2 (handshake),
    // §3 (curve v2), §4 (ceremony UX). Soul: DESIGN-SNAPSHOT-v2.1.html §6.
    //
    // THERE IS NO CLIENT FLAG, and that is the design. Every code path below is
    // dormant until a sync response carries `descent_migration.required: true`,
    // which the server only ever sends with DESCENT_MIGRATION armed. A build that
    // ships today ships this whole feature dark; the owner turns it on server-side
    // with nothing to re-release.
    // ============================================================================

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
    }

    /// <summary>The two doors of §6. Wire values are lowercase and exact — the server matches on them.</summary>
    public static class DescentMigrationChoices
    {
        /// <summary>Option A, "Take it all back": level re-derived from lifetime XP under curve v2.</summary>
        public const string Restore = "restore";

        /// <summary>Option B, "Descend again": Cycle I, level 1, permanent mark, lasting XP bonus.</summary>
        public const string Cycle = "cycle";

        public static bool IsValid(string? choice) =>
            choice == Restore || choice == Cycle;
    }

    /// <summary>
    /// The server's offer, parsed off a /v2/user/sync response. Its mere existence is the whole
    /// trigger — there is no local condition that can conjure one.
    /// </summary>
    public sealed class DescentMigrationOffer
    {
        /// <summary>Lifetime XP the SERVER holds for this account. The relevel's only input.</summary>
        public double TotalXpEarned { get; init; }

        /// <summary>Server-side devotion days, already backfilled. Display only.</summary>
        public int DevotionDays { get; init; }
    }

    /// <summary>
    /// The re-derived ledger a choice produces. Pure output of <see cref="DescentMigration.Resolve"/>
    /// — computing it never touches settings, so the ceremony can PREVIEW both options side by
    /// side before the user commits to either.
    /// </summary>
    public readonly record struct DescentRelevelResult(int Level, double XpIntoLevel, double LifetimeXp)
    {
        /// <summary>The `xp` field of the sync body: cumulative XP under curve v2 at this standing.</summary>
        public double LedgerXp => LifetimeXp;
    }

    /// <summary>
    /// The migration's pure arithmetic and the tunables that ride with it. Everything here is
    /// static and side-effect free so it can be exercised without an App.
    /// </summary>
    public static class DescentMigration
    {
        /// <summary>
        /// The Cycle XP bonus. TUNABLE AND UNBLESSED — CONTRACTS §3 records that the owner has
        /// not signed off on 1.10, only on "there is a lasting bonus". It ships dark with the
        /// ceremony; changing it is a one-line edit here and nowhere else.
        /// </summary>
        public const double CycleXpBonus = 1.10;

        /// <summary>
        /// The multiplier <see cref="ProgressionService.AddXP"/> actually applies. 1.0 for every
        /// account that has not taken a Cycle, which today is every account in existence.
        /// Defensive clamp on the persisted value: a hand-edited settings.json must not be able
        /// to write itself a 50x XP tap.
        /// </summary>
        public static double ActiveCycleXpBonus
        {
            get
            {
                var stored = App.Settings?.Current?.DescentCycleXpBonus ?? 1.0;
                if (double.IsNaN(stored) || stored < 1.0) return 1.0;
                return Math.Min(stored, CycleXpBonus);
            }
        }

        /// <summary>
        /// What a choice does to the ledger, in one place, with no side effects.
        ///
        /// <para><b>Restore</b> re-derives level from the SERVER's lifetime figure under curve v2.
        /// The server's number and not the client's on purpose: the server independently derives
        /// the same value and clamps our claim to within one level of it (CONTRACTS §2.5), so
        /// deriving from anything else is asking to be clamped.</para>
        ///
        /// <para><b>Cycle</b> is level 1, XP 0. Lifetime XP is carried through untouched —
        /// total_xp_earned is lifetime and never decreases, not even here (§2.5), and §6 is
        /// explicit that a Cycle "wipes nothing else".</para>
        ///
        /// <para>Both are IDEMPOTENT, which is what makes the crash-before-ack case survivable:
        /// re-running either against the same server offer produces the same ledger.</para>
        /// </summary>
        public static DescentRelevelResult Resolve(string choice, DescentMigrationOffer offer)
        {
            double lifetime = offer.TotalXpEarned;
            if (double.IsNaN(lifetime) || lifetime < 0) lifetime = 0;

            if (choice == DescentMigrationChoices.Cycle)
                return new DescentRelevelResult(1, 0, lifetime);

            var (level, into) = ProgressionService.DeriveLevelFromLifetimeXp(
                lifetime, DescentEpochs.AccountDescent);
            return new DescentRelevelResult(level, into, lifetime);
        }
    }
}
