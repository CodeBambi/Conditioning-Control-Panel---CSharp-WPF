using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The voluntary skill respec, and above all the rule that keeps it from turning Prestige into a
/// button you hold down.
///
/// <para>Prestige is <c>1 + lifetime points spent / 100</c> and skill purchases are the only thing
/// that feeds it. It used to keep climbing because the server wiped the mechanical half of the tree
/// every month and the player bought it back. Seasons are over, PR #467 folded every skill into
/// permanent, and the loop stopped: a finished tree meant a fixed final rank and skill points
/// piling up with nothing to spend them on. The respec hands the loop back on the player's terms.
/// It also hands back the obvious exploit, which is why most of this file is about arithmetic
/// rather than about buttons.</para>
///
/// <para>The anti-farm suites below pin the <b>bound</b>, not the rate. Whether a refund pays half
/// or a third is a tuning call the owner may want to make differently; that a refund-and-rebuy
/// cycle must always cost the player points, and that lifetime spend can therefore never outrun
/// lifetime earning by more than a fixed multiple, is the part that must never be tuned away.</para>
/// </summary>
public class SkillRespecTests
{
    private static IReadOnlyList<SkillDefinition> Tree => SkillDefinition.All;

    private static List<string> WholeTree() => Tree.Select(s => s.Id).ToList();

    // ============================== anti-farm ==============================

    /// <summary>
    /// The load-bearing assertion of the whole feature. If a refund ever pays the full price back,
    /// a cycle costs nothing, the point balance never falls, and Prestige becomes unbounded.
    /// </summary>
    [Fact]
    public void ARefundAlwaysPaysBackStrictlyLessThanThePurchaseCost()
    {
        foreach (var skill in Tree)
        {
            var refund = SkillRespec.RefundFor(skill);
            Assert.True(refund < skill.Cost,
                $"'{skill.Id}' refunds {refund} of its {skill.Cost}, so handing it back and buying " +
                "it again would be free and Prestige could be farmed by clicking.");
            Assert.True(refund >= 0, $"'{skill.Id}' refunds a negative {refund}.");
        }
    }

    /// <summary>
    /// One cycle adds <c>C</c> to lifetime spend and costs <c>C - R</c> from the balance, so the
    /// Prestige squeezed out of a balance is amplified by <c>C / (C - R)</c>. Capping the refund at
    /// half the cost caps that amplification at two, for every node in the tree, forever.
    /// </summary>
    [Fact]
    public void TheAmplificationFromOneCycleNeverExceedsTwo()
    {
        foreach (var skill in Tree)
        {
            var refund = SkillRespec.RefundFor(skill);
            var netCost = skill.Cost - refund;

            Assert.True(netCost > 0, $"'{skill.Id}' costs nothing to cycle.");
            Assert.True(skill.Cost <= 2 * netCost,
                $"'{skill.Id}' turns {netCost} spent points into {skill.Cost} of lifetime spend, " +
                "which is more than the 2x the design allows.");
        }
    }

    /// <summary>Rounding is downward, so the house edge is never rounded away on an odd price.</summary>
    [Fact]
    public void OddPricesRoundTheRefundDown()
    {
        Assert.Equal(2, SkillRespec.RefundFor(5));
        Assert.Equal(7, SkillRespec.RefundFor(15));
        Assert.Equal(0, SkillRespec.RefundFor(1));
        Assert.Equal(0, SkillRespec.RefundFor(0));
        Assert.Equal(0, SkillRespec.RefundFor(-40));
    }

    /// <summary>
    /// The farm, run for real. A player parks on a finished tree with a balance and does nothing
    /// but hand the cheapest refundable leaf back and buy it again, forever, which is the exact
    /// thing the owner asked to be impossible. The loop has to stop on its own, and the Prestige it
    /// bought has to be inside the bound.
    /// </summary>
    [Fact]
    public void SpammingTheButtonRunsOutOfPointsAndCannotInflatePrestige()
    {
        const int startingBalance = 1_000;

        var owned = WholeTree();
        var cheapest = Tree
            .Where(s => SkillRespec.Evaluate(s.Id, owned) == SkillRefundBlock.None)
            .OrderBy(s => s.Cost)
            .First();

        var balance = startingBalance;
        long lifetimeSpendAdded = 0;
        var cycles = 0;

        while (balance >= cheapest.Cost - SkillRespec.RefundFor(cheapest))
        {
            // hand it back
            balance += SkillRespec.RefundFor(cheapest);
            // buy it again: this, and only this, is what moves Prestige
            balance -= cheapest.Cost;
            lifetimeSpendAdded += cheapest.Cost;

            cycles++;
            Assert.True(cycles < 100_000, "The refund loop never ran out of points.");
        }

        Assert.True(cycles > 0, "Test is vacuous: the loop never ran once.");
        Assert.True(balance < cheapest.Cost - SkillRespec.RefundFor(cheapest));
        Assert.True(lifetimeSpendAdded <= 2L * startingBalance,
            $"Spamming turned {startingBalance} points into {lifetimeSpendAdded} of lifetime spend.");
    }

    /// <summary>
    /// The same claim stated as the invariant the owner actually cares about: no matter which
    /// skills are cycled, in what order, or how many times, the Prestige bought is bounded by the
    /// points the player earned by playing. Walked over every refundable node rather than one.
    /// </summary>
    [Fact]
    public void LifetimeSpendIsAlwaysBoundedByTwiceThePointsEverEarned()
    {
        const int pointsEverEarned = 4_000;

        var owned = WholeTree();
        var refundable = Tree
            .Where(s => SkillRespec.Evaluate(s.Id, owned) == SkillRefundBlock.None)
            .ToList();

        Assert.NotEmpty(refundable);

        // Deterministic churn: cycle each refundable node in turn, round after round, until the
        // balance can no longer afford the cheapest net cost.
        var balance = pointsEverEarned;
        long lifetimeSpendAdded = 0;
        var minNet = refundable.Min(s => s.Cost - SkillRespec.RefundFor(s));
        var guard = 0;

        while (balance >= minNet)
        {
            foreach (var skill in refundable)
            {
                var net = skill.Cost - SkillRespec.RefundFor(skill);
                if (balance < net) continue;
                balance -= net;
                lifetimeSpendAdded += skill.Cost;
            }
            Assert.True(guard++ < 100_000, "The churn never terminated.");
        }

        Assert.True(lifetimeSpendAdded <= 2L * pointsEverEarned,
            $"{pointsEverEarned} earned points became {lifetimeSpendAdded} of lifetime spend.");
    }

    /// <summary>
    /// A refund is not an un-spend. Nothing in the respec touches lifetime points spent, which is
    /// what lets Prestige stay monotonic while the loop underneath it turns over.
    /// </summary>
    [Fact]
    public void PrestigeOnlyMovesOnThePurchaseHalfOfTheCycle()
    {
        var skill = Tree.First(s => s.Id == "certified_data_bimbo");
        var refund = SkillRespec.RefundFor(skill);

        var balance = 0;
        long lifetimeSpent = 9_950;                       // rank 100, one purchase short of 101
        static long Rank(long spent) => 1 + spent / 100;

        var rankAtRest = Rank(lifetimeSpent);

        // Hand it back. The balance grows by the refund and the record does not move at all, so a
        // player cannot even bank a rank by refunding, let alone lose one.
        balance += refund;
        Assert.Equal(rankAtRest, Rank(lifetimeSpent));
        Assert.Equal(refund, balance);

        // Buy it again. Only now does Prestige move, and it moves by the full price rather than by
        // the net cost, which is exactly the loop the seasons used to provide.
        balance -= skill.Cost;
        lifetimeSpent += skill.Cost;

        Assert.True(Rank(lifetimeSpent) > rankAtRest);
        Assert.Equal(refund - skill.Cost, balance);
        Assert.True(balance < 0, "The cycle has to leave the player out of pocket.");
    }

    // ============================== scope: which nodes ==============================

    /// <summary>
    /// Handing back a skill that something else is built on would leave a purchase dangling above
    /// a prerequisite the player no longer holds, so branches unwind from the tip inward.
    /// </summary>
    [Fact]
    public void ANodeHoldingUpAnOwnedSkillCannotBeHandedBack()
    {
        var owned = WholeTree();

        foreach (var skill in Tree)
        {
            var dependents = SkillRespec.DependentsOf(skill.Id);
            if (dependents.Count == 0) continue;
            if (SkillRespec.NonRefundableIds.Contains(skill.Id)) continue;

            Assert.Equal(SkillRefundBlock.HasOwnedDependents, SkillRespec.Evaluate(skill.Id, owned));
        }
    }

    /// <summary>Once the dependents are gone the node frees up, which is the whole unwind story.</summary>
    [Fact]
    public void ALeafFreesUpOnceWhatSatOnTopOfItIsGone()
    {
        var owned = WholeTree();
        Assert.Equal(SkillRefundBlock.HasOwnedDependents, SkillRespec.Evaluate("pink_hours", owned));

        var leafOnly = new List<string> { "pink_hours" };
        Assert.Equal(SkillRefundBlock.None, SkillRespec.Evaluate("pink_hours", leafOnly));
    }

    /// <summary>
    /// The tree really can be taken apart from the tips, one confirmed click at a time, and the
    /// only things left standing are the two nodes that mint a consumable plus whatever sits under
    /// them. A stuck unwind would mean the leaf rule had quietly become a wall.
    /// </summary>
    [Fact]
    public void TheTreeUnwindsFromItsTipsDownToTheConsumableNodes()
    {
        var owned = WholeTree();
        var guard = 0;

        while (true)
        {
            var next = owned.FirstOrDefault(id => SkillRespec.Evaluate(id, owned) == SkillRefundBlock.None);
            if (next == null) break;
            owned.Remove(next);
            Assert.True(guard++ < 1_000, "The unwind never terminated.");
        }

        // Everything still owned is either a consumable-minting node or something underneath one.
        foreach (var id in owned)
        {
            var verdict = SkillRespec.Evaluate(id, owned);
            Assert.True(verdict is SkillRefundBlock.MintsAConsumable or SkillRefundBlock.HasOwnedDependents,
                $"'{id}' survived the unwind for the unexpected reason {verdict}.");
        }

        Assert.Contains("good_girl_streak", owned);
        Assert.Contains("oopsie_insurance", owned);
        Assert.Contains("pink_hours", owned); // holds up good_girl_streak

        // ...and the far ends really did come off.
        Assert.DoesNotContain("certified_data_bimbo", owned);
        Assert.DoesNotContain("perfect_bimbo_week", owned);
        Assert.DoesNotContain("night_shift", owned);
    }

    /// <summary>
    /// Both blocked nodes hand the player a consumable at purchase time, so a hand-back-and-buy
    /// loop on either would mint streak shields or streak fixes for the price of the difference.
    /// This is a second, separate farm from the Prestige one and it is closed by refusing outright.
    /// </summary>
    [Fact]
    public void TheNodesThatMintAConsumableAreNeverRefundable()
    {
        Assert.Contains("good_girl_streak", SkillRespec.NonRefundableIds);
        Assert.Contains("oopsie_insurance", SkillRespec.NonRefundableIds);

        foreach (var id in SkillRespec.NonRefundableIds)
        {
            Assert.True(Tree.Any(s => s.Id == id), $"Test is stale: '{id}' is not a skill any more.");
            Assert.False(SkillRespec.IsRefundableSkill(id));
            Assert.Equal(SkillRefundBlock.MintsAConsumable,
                SkillRespec.Evaluate(id, new List<string> { id }));
        }
    }

    /// <summary>
    /// A skill nobody owns cannot be handed back, and neither can one the tree has never heard of.
    /// Trivial, and worth pinning: the block that answers "not owned" is what keeps a stale UI
    /// click from asking the server to pay for something twice.
    /// </summary>
    [Fact]
    public void NothingUnownedOrUnknownCanBeHandedBack()
    {
        Assert.Equal(SkillRefundBlock.NotOwned, SkillRespec.Evaluate("pink_hours", new List<string>()));
        Assert.Equal(SkillRefundBlock.NotOwned, SkillRespec.Evaluate("pink_hours", null));
        Assert.Equal(SkillRefundBlock.UnknownSkill, SkillRespec.Evaluate("no_such_skill", WholeTree()));
        Assert.Equal(SkillRefundBlock.UnknownSkill, SkillRespec.Evaluate("", WholeTree()));
        Assert.Equal(0, SkillRespec.RefundFor("no_such_skill"));
    }

    // ============================== the one place that subtracts ==============================

    /// <summary>
    /// Every other sync path unions the server's skills into the local list so a bad response can
    /// never cost anyone a purchase. The refund is the single exception, so it only subtracts once
    /// the server's own list has proven the skill is gone from it.
    /// </summary>
    [Fact]
    public void TheOwnedListIsOnlyRewrittenWhenTheServerProvesTheRefundHappened()
    {
        var local = new List<string> { "pink_hours", "ditzy_data", "hive_mind" };

        var applied = SkillRespec.ResolveOwnedAfterRefund(
            new List<string> { "pink_hours", "ditzy_data" }, "hive_mind", local);
        Assert.NotNull(applied);
        Assert.DoesNotContain("hive_mind", applied!);
        Assert.Equal(2, applied!.Count);
    }

    [Fact]
    public void AServerAnswerThatStillHoldsTheSkillIsRefusedRatherThanHalfApplied()
    {
        var local = new List<string> { "pink_hours", "ditzy_data", "hive_mind" };

        Assert.Null(SkillRespec.ResolveOwnedAfterRefund(
            new List<string> { "pink_hours", "ditzy_data", "hive_mind" }, "hive_mind", local));
        Assert.Null(SkillRespec.ResolveOwnedAfterRefund(null, "hive_mind", local));
        Assert.Null(SkillRespec.ResolveOwnedAfterRefund(new List<string>(), "", local));
    }

    /// <summary>
    /// An empty server list is a legitimate answer for someone handing back their only skill, so it
    /// must not be mistaken for "no answer". This is the case where a defensive null check written
    /// one line higher would silently strand a player's tree.
    /// </summary>
    [Fact]
    public void HandingBackTheLastSkillLeavesAnEmptyTreeRatherThanFailing()
    {
        var applied = SkillRespec.ResolveOwnedAfterRefund(
            new List<string>(), "pink_hours", new List<string> { "pink_hours" });

        Assert.NotNull(applied);
        Assert.Empty(applied!);
    }

    // ============================== copy ==============================

    private static readonly string[] NewKeys =
    {
        "dialog_refund_enhancement", "msg_refund_skill", "msg_refund_skill_note",
        "dialog_refund_failed", "label_skill_refund_hint", "label_skill_refund_locked_branch",
        "label_skill_refund_never", "skill_err_cannot_refund", "skill_err_refund_login_required",
        "skill_err_refund_not_ready", "skill_err_refund_signed_out"
    };

    [Fact]
    public void TheRespecCopyReachedAllNineLanguages()
    {
        foreach (var lang in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(lang);
            foreach (var key in NewKeys)
            {
                Assert.True(file.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text),
                    $"{lang}.json is missing {key}.");
            }
        }
    }

    /// <summary>House copy law: no em-dashes and no en-dashes in anything a user reads.</summary>
    [Fact]
    public void TheRespecCopyCarriesNoDashes()
    {
        foreach (var lang in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(lang);
            foreach (var key in NewKeys)
            {
                Assert.DoesNotContain("—", file[key]);
                Assert.DoesNotContain("–", file[key]);
            }
        }
    }

    /// <summary>
    /// Rule 11 of CLAUDE.md, checked from the other side: a value may carry an escaped break, but
    /// none of these strings needs one, and a literal break in the file would have failed the
    /// strict parse behind <see cref="CompanionLocMasters"/> before reaching here anyway.
    /// </summary>
    [Fact]
    public void TheRespecCopyIsSingleLine()
    {
        foreach (var lang in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(lang);
            foreach (var key in NewKeys)
            {
                Assert.DoesNotContain("\n", file[key]);
                Assert.DoesNotContain("\r", file[key]);
            }
        }
    }

    /// <summary>
    /// The confirmation has to be able to say what was paid and what comes back, or the player is
    /// agreeing to a number nobody showed them. Both placeholders, and the two the tooltip needs.
    /// </summary>
    [Fact]
    public void TheConfirmationQuotesBothThePriceAndTheRefund()
    {
        foreach (var lang in CompanionLocMasters.Languages)
        {
            var confirm = CompanionLocMasters.For(lang)["msg_refund_skill"];
            foreach (var slot in new[] { "{0}", "{1}", "{2}", "{3}" })
                Assert.Contains(slot, confirm);

            var hint = CompanionLocMasters.For(lang)["label_skill_refund_hint"];
            foreach (var slot in new[] { "{0}", "{1}" })
                Assert.Contains(slot, hint);

            Assert.Contains("{0}", CompanionLocMasters.For(lang)["skill_err_refund_signed_out"]);
        }
    }

    /// <summary>
    /// The prestige tooltip was the one string in the app still describing a number that could
    /// never move again. It now says the loop exists, and it still may not mention the seasons
    /// that are gone, which is the law the existing permanence suite already enforces.
    /// </summary>
    [Fact]
    public void ThePrestigeTooltipNowExplainsTheLoop()
    {
        var text = CompanionLocMasters.English["tooltip_prestige"];

        Assert.Contains("hand", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("again", text, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("~", text);

        foreach (var word in new[] { "season", "reset", "rollover", "re-buy", "rebuy" })
        {
            Assert.False(text.Contains(word, StringComparison.OrdinalIgnoreCase),
                $"tooltip_prestige says \"{word}\": {text}");
        }
    }

    /// <summary>
    /// The neighbouring skill strings end on a "~" and the respec lines are read in the same
    /// breath, so the warm ones match. The error strings are excluded on purpose: an apology for a
    /// dead session should not be winking.
    /// </summary>
    [Fact]
    public void TheWarmRespecLinesKeepTheTrailingTilde()
    {
        var warm = new[]
        {
            "msg_refund_skill", "msg_refund_skill_note", "label_skill_refund_hint",
            "label_skill_refund_locked_branch", "label_skill_refund_never", "skill_err_cannot_refund",
            "skill_err_refund_not_ready"
        };

        foreach (var lang in CompanionLocMasters.Languages)
        {
            foreach (var key in warm)
            {
                Assert.EndsWith("~", CompanionLocMasters.For(lang)[key]);
            }
        }
    }
}
