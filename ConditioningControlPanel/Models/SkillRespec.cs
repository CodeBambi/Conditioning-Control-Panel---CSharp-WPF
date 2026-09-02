using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Models;

/// <summary>
/// Why one skill cannot be handed back right now. <see cref="SkillRefundBlock.None"/> is the
/// only value that means "go ahead"; every other value carries its own line of copy in the UI,
/// because "you can't" with no reason attached is the most irritating thing a screen can say.
/// </summary>
public enum SkillRefundBlock
{
    /// <summary>Refundable.</summary>
    None,

    /// <summary>No skill in the tree carries this id.</summary>
    UnknownSkill,

    /// <summary>The player does not own it, so there is nothing to hand back.</summary>
    NotOwned,

    /// <summary>
    /// Buying it mints a consumable, so a hand-back-and-buy-again loop would mint another one.
    /// See <see cref="SkillRespec.NonRefundableIds"/>.
    /// </summary>
    MintsAConsumable,

    /// <summary>
    /// Something the player also owns lists this skill as its prerequisite. Handing it back would
    /// leave that purchase dangling above a hole, so the branch unwinds from its tip inward.
    /// </summary>
    HasOwnedDependents
}

/// <summary>
/// The voluntary skill respec: the rules for handing a purchased skill back, and the arithmetic
/// that keeps Prestige from being farmable by doing it in a loop.
///
/// <para><b>Why this exists.</b> Prestige is <c>1 + lifetime points spent / 100</c> and it is fed
/// by skill purchases alone. It used to keep moving because the server wiped the mechanical half
/// of the tree every month and the player bought it back, so the same points were spent over and
/// over. The Descent ended monthly seasons, the wipe is suppressed for good, and PR #467 folded
/// every skill into permanent, which between them mean the tree is bought exactly once and
/// Prestige then stops at a fixed final rank with skill points still piling up behind it. This
/// gives the loop back on the player's own terms: you choose to hand a skill back, you choose to
/// buy it again, and the second purchase counts toward Prestige exactly as the monthly one used
/// to. Nothing here ever fires on a schedule and nothing here ever takes a skill from someone who
/// did not click a confirmation saying so.</para>
///
/// <para><b>The anti-farm rule, which is the whole design.</b> A refund pays back
/// <c>floor(cost / 2)</c>, never the full price. Write <c>R</c> for the refund and <c>C</c> for
/// the cost: one hand-back-and-buy-again cycle adds <c>C</c> to lifetime spend while the point
/// balance falls by <c>C - R</c>, so the most Prestige anyone can wring out of a balance is
/// <c>C / (C - R)</c> times that balance. Halving pins that ratio at 2 and, more importantly,
/// pins it at a finite number at all: a full refund would make <c>C - R</c> zero, the loop would
/// cost nothing, and Prestige would be a button you hold down. Because every cycle strictly
/// burns points, and points only arrive by levelling, lifetime Prestige stays bounded by lifetime
/// play no matter how many times the button is pressed. That bound is what
/// <c>SkillRespecTests</c> pins down rather than the specific rate.</para>
///
/// <para><b>Pure on purpose.</b> Nothing in here reads <c>App</c>, settings or the network, so the
/// rules are exercisable directly in tests and the service layer is left holding only the plumbing.
/// The one thing it does not decide is the actual refund payout at runtime: the server owns the
/// player's balance, computes the refund from its own copy of the catalogue, and answers with the
/// authoritative totals. <see cref="RefundFor(int)"/> is what the client shows on the confirmation
/// so the number the player agreed to is the number they get.</para>
/// </summary>
public static class SkillRespec
{
    /// <summary>
    /// A refund pays back <c>cost / RefundDivisor</c>, rounded down. Two is the smallest divisor
    /// that leaves a real cost behind, and rounding down means the house edge never rounds away:
    /// a 5 point skill hands back 2, not 2.5 and certainly not 3.
    /// </summary>
    public const int RefundDivisor = 2;

    /// <summary>
    /// Skills that hand the player a consumable the moment they are bought, and so may never be
    /// handed back.
    ///
    /// <para>Both of these do something on purchase beyond switching an effect on:
    /// <c>good_girl_streak</c> stocks a streak shield and <c>oopsie_insurance</c> adds a
    /// streak-fix charge, and streak-fix charges accumulate. Refunding either one and buying it
    /// again would mint a fresh consumable for the difference in points, which is a farm for a
    /// currency that has nothing to do with Prestige and is far harder to reason about. Blocking
    /// the two nodes outright is the honest fix; making the grant conditional would mean the
    /// client and the server both having to remember that this account already collected, and a
    /// disagreement between them would either rob a first-time buyer or mint the charge anyway.</para>
    ///
    /// <para>They sit mid-branch, so blocking them also means the streak branch can be unwound
    /// down to them and no further, which is a deliberate and stated limit rather than a bug.</para>
    /// </summary>
    public static IReadOnlyList<string> NonRefundableIds { get; } =
        new List<string> { "good_girl_streak", "oopsie_insurance" }.AsReadOnly();

    /// <summary>The points a refund pays back for a skill of this cost. Always strictly below it.</summary>
    public static int RefundFor(int cost) => cost <= 0 ? 0 : cost / RefundDivisor;

    /// <summary>The points a refund pays back for this skill.</summary>
    public static int RefundFor(SkillDefinition skill) =>
        skill == null ? 0 : RefundFor(skill.Cost);

    /// <summary>The points a refund pays back for this skill id, or zero when the id is unknown.</summary>
    public static int RefundFor(string skillId)
    {
        var skill = SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
        return skill == null ? 0 : RefundFor(skill.Cost);
    }

    /// <summary>Whether the rules allow this skill to be handed back at all, ownership aside.</summary>
    public static bool IsRefundableSkill(string skillId) =>
        !string.IsNullOrEmpty(skillId) &&
        SkillDefinition.All.Any(s => s.Id == skillId) &&
        !NonRefundableIds.Contains(skillId);

    /// <summary>Ids of every skill in the tree that names this one as its prerequisite.</summary>
    public static IReadOnlyList<string> DependentsOf(string skillId) =>
        SkillDefinition.All
            .Where(s => string.Equals(s.PrerequisiteId, skillId, StringComparison.Ordinal))
            .Select(s => s.Id)
            .ToList()
            .AsReadOnly();

    /// <summary>
    /// The full verdict on one hand-back, given the ids the player currently owns. Pure: hand it
    /// any owned set and it answers, which is how the tests walk a whole tree apart node by node.
    /// </summary>
    public static SkillRefundBlock Evaluate(string skillId, IReadOnlyCollection<string>? owned)
    {
        if (string.IsNullOrEmpty(skillId)) return SkillRefundBlock.UnknownSkill;
        if (!SkillDefinition.All.Any(s => s.Id == skillId)) return SkillRefundBlock.UnknownSkill;

        var have = owned ?? Array.Empty<string>();
        if (!have.Contains(skillId)) return SkillRefundBlock.NotOwned;

        if (NonRefundableIds.Contains(skillId)) return SkillRefundBlock.MintsAConsumable;

        // Leaves first. A branch is unwound from its tip because any other order would leave a
        // purchase sitting above a prerequisite the player no longer holds, and the tree has no
        // way to render that honestly.
        if (DependentsOf(skillId).Any(have.Contains)) return SkillRefundBlock.HasOwnedDependents;

        return SkillRefundBlock.None;
    }

    /// <summary>
    /// Decide what the local owned-skills list becomes after the server answers a refund.
    ///
    /// <para>This is the one operation in the whole app that is allowed to <b>subtract</b> a
    /// purchase, so it is deliberately the most suspicious code in the feature. Everywhere else
    /// the client unions the server's list into its own precisely so a bad or stale response can
    /// never cost anybody a skill; here a union would defeat the point, because the refunded skill
    /// is exactly the entry that has to disappear.</para>
    ///
    /// <para>The compromise: take the server's list wholesale, but only once it has proven it
    /// really did the thing by no longer containing the refunded id. A null list, or one that
    /// still holds the skill, means the server did not perform the refund the client thinks it
    /// did, and the answer is <c>null</c> for "trust nothing, change nothing, keep what you have".
    /// The caller treats that as a failed refund rather than quietly half-applying it.</para>
    /// </summary>
    public static List<string>? ResolveOwnedAfterRefund(
        IReadOnlyList<string>? serverOwned,
        string refundedSkillId,
        IReadOnlyList<string>? localOwned)
    {
        if (serverOwned == null) return null;
        if (string.IsNullOrEmpty(refundedSkillId)) return null;
        if (serverOwned.Contains(refundedSkillId)) return null;

        // The server's list is authoritative for this one call. Anything the client held that the
        // server has never heard of is dropped on purpose: a refund is the moment the two are
        // meant to agree, and carrying local strays forward is how a "refunded" skill grows back.
        _ = localOwned;
        return serverOwned.Distinct(StringComparer.Ordinal).ToList();
    }
}
