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

    /// <summary>
    /// The server's offer, parsed off a /v2/user/sync response. Its mere existence is the whole
    /// trigger — there is no local condition that can conjure one.
    /// </summary>
    public sealed class DescentMigrationOffer
    {
        /// <summary>Lifetime XP the SERVER holds for this account. Display; the relevel prices
        /// <see cref="RestoreBasisXp"/>.</summary>
        public double TotalXpEarned { get; init; }

        /// <summary>Server-side devotion days — the backfill ESTIMATE since 2026-08-16, i.e. the
        /// count that will actually survive the ceremony. Display only.</summary>
        public int DevotionDays { get; init; }

        /// <summary>
        /// THE NUMBER OPTION A IS DERIVED FROM: <c>total_xp_earned + 300 × devotion estimate</c>,
        /// computed by the SERVER (the veteran credit, owner ruling 2026-08-16 — recorded XP alone
        /// priced a historic account at level 17). Sent on the wire as <c>restore_basis_xp</c>
        /// precisely so this client never duplicates the arithmetic: the server clamps our claimed
        /// level to ±1 of its own answer, and a locally-recomputed credit with a stale constant
        /// would fight that clamp forever. 0 = an older server that did not send it, and the
        /// relevel falls back to <see cref="TotalXpEarned"/> — the exact pre-credit behaviour.
        /// </summary>
        public double RestoreBasisXp { get; init; }
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
        /// <summary>Alias of <see cref="DescentCycleXp.CycleXpBonus"/>, which lives in Core.</summary>
        public const double CycleXpBonus = DescentCycleXp.CycleXpBonus;

        /// <summary>
        /// The multiplier <see cref="ProgressionService.AddXP"/> actually applies:
        /// <see cref="CycleXpBonus"/> for a migrated account, 1.0 for everyone else.
        /// </summary>
        public static double ActiveCycleXpBonus => XpBonusFor(App.Settings?.Current);

        /// <summary>
        /// ONE RULE: migrated = bonus. Migrated means the server has acked the migration, or a
        /// valid choice is on disk waiting to land (the relevel is already applied by then).
        /// Restore, Cycle and the silent auto-restore all qualify alike.
        ///
        /// <para>NOT <c>DescentEpoch</c> alone. The server stamps curve_epoch 1 on every record born
        /// after the Descent too, and a fresh signup never "came back" from anything: the bonus is
        /// for migrated legacy accounts (owner, 2026-10-06: "anyone that comes back"). An account
        /// migrated on another device is covered by the ack, which rides every sync.</para>
        ///
        /// <para>The persisted <c>DescentCycleXpBonus</c> is no longer an input: a hand-edited
        /// settings file can never buy more than the constant, and an account that lost the field
        /// still gets the bonus its migration earned.</para>
        /// </summary>
        public static double XpBonusFor(Models.AppSettings? settings) => DescentCycleXp.XpBonusFor(settings);

        /// <summary>
        /// What a choice does to the ledger, in one place, with no side effects.
        ///
        /// <para><b>Restore</b> re-derives level from the SERVER's restore basis (lifetime XP plus
        /// the veteran credit, <see cref="DescentMigrationOffer.RestoreBasisXp"/>) under curve v2.
        /// The server's number and not a locally-computed one on purpose: the server independently
        /// derives the same value and clamps our claim to within one level of it (CONTRACTS §2.5),
        /// so deriving from anything else is asking to be clamped. The ledger XP written is the
        /// SAME basis — the next sync's level/XP consistency check re-derives level from xp under
        /// curve v2, and an xp figure without the credit would demote the account right back to
        /// the uncredited level one sync later.</para>
        ///
        /// <para><b>Cycle</b> is level 1, XP 0. Lifetime XP is carried through untouched —
        /// total_xp_earned is lifetime and never decreases, not even here (§2.5), and §6 is
        /// explicit that a Cycle "wipes nothing else". The credit does not ride the Cycle door:
        /// its ledger is a fixed destination with nothing to price.</para>
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

            var basis = offer.RestoreBasisXp;
            if (double.IsNaN(basis) || basis <= 0) basis = lifetime;

            var (level, into) = ProgressionService.DeriveLevelFromLifetimeXp(
                basis, DescentEpochs.AccountDescent);
            return new DescentRelevelResult(level, into, basis);
        }
    }
}
