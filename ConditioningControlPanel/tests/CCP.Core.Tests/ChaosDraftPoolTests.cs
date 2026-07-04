using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Core.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the faithful port of the WPF draft dealer (WPF ChaosModels.cs:404-431 ChaosBoonPool.Draft
/// -> Core ChaosDraftPool.Draft): the 2-4 candidate clamp, duo/trio ReqMet gating
/// (RequiresAny/RequiresAll), Unique-taken exclusion via takenIds, the dedicated sin slot
/// (includeCurse roll + Surrender guarantee + short-circuit), the boon top-up when the pool
/// runs short, and deterministic re-deals (reroll) through the injected Random.
/// </summary>
public class ChaosDraftPoolTests
{
    /// <summary>Deterministic Random: Next()/NextDouble() pop from scripted queues (0/0.0 when
    /// exhausted — an exhausted ints queue keeps the pool's seed order, OrderBy being stable).</summary>
    private sealed class ScriptedRandom : Random
    {
        private readonly Queue<double> _doubles;
        private readonly Queue<int> _ints;

        public ScriptedRandom(double[]? doubles = null, int[]? ints = null) : base(0)
        {
            _doubles = new Queue<double>(doubles ?? Array.Empty<double>());
            _ints = new Queue<int>(ints ?? Array.Empty<int>());
        }

        public override int Next() => _ints.Count > 0 ? _ints.Dequeue() : 0;
        public override double NextDouble() => _doubles.Count > 0 ? _doubles.Dequeue() : 0.0;
    }

    /// <summary>Minimal draft card — the gating surface only (mirrors WPF ChaosBoon fields).</summary>
    private sealed record Card(string Id, bool IsCurse = false, bool Unique = false,
        string[]? RequiresAny = null, string[]? RequiresAll = null) : IChaosDraftCard;

    private static Func<string, bool> Met(params string[] ids) => id => ids.Contains(id);
    private static readonly Func<string, bool> NoneMet = _ => false;

    private static List<Card> Boons(int n) =>
        Enumerable.Range(1, n).Select(i => new Card($"b{i}")).ToList();

    // ================================================================
    // Candidate clamp 2-4 (WPF ChaosModels.cs:406)
    // ================================================================

    [Fact]
    public void Draft_Choices_ClampsLow_To2()
    {
        var deal = ChaosDraftPool.Draft(Boons(5), NoneMet, new ScriptedRandom(),
            allowCurses: false, choices: 1);
        Assert.Equal(2, deal.Count);
    }

    [Fact]
    public void Draft_Choices_ClampsHigh_To4()
    {
        var deal = ChaosDraftPool.Draft(Boons(6), NoneMet, new ScriptedRandom(),
            allowCurses: false, choices: 9);
        Assert.Equal(4, deal.Count);
    }

    [Fact]
    public void Draft_DefaultChoices_DealsThreeDistinctCardsFromPool()
    {
        var pool = Boons(5);
        var deal = ChaosDraftPool.Draft(pool, NoneMet, new ScriptedRandom(), allowCurses: false);
        Assert.Equal(3, deal.Count);
        Assert.Equal(3, deal.Select(c => c.Id).Distinct().Count());
        Assert.All(deal, c => Assert.Contains(c, pool));
    }

    // ================================================================
    // Duo/trio ReqMet gating (WPF ChaosModels.cs:407-410)
    // ================================================================

    [Fact]
    public void Draft_RequiresAny_Unmet_ExcludesCard()
    {
        var pool = new List<Card> { new("plain"), new("overload", RequiresAny: new[] { "e_stim" }) };
        var deal = ChaosDraftPool.Draft(pool, NoneMet, new ScriptedRandom(), allowCurses: false);
        Assert.Single(deal);
        Assert.Equal("plain", deal[0].Id);
    }

    [Fact]
    public void Draft_RequiresAny_OneMet_DealsCard()
    {
        var pool = new List<Card> { new("plain"), new("tail_plug", RequiresAny: new[] { "rabbit_caller", "the_pull", "the_spanker" }) };
        var deal = ChaosDraftPool.Draft(pool, Met("the_pull"), new ScriptedRandom(), allowCurses: false);
        Assert.Contains(deal, c => c.Id == "tail_plug");
    }

    [Fact]
    public void Draft_RequiresAll_PartiallyMet_ExcludesCard()
    {
        var pool = new List<Card> { new("plain"), new("electrified_rabbits", RequiresAll: new[] { "the_spanker", "e_stim" }) };
        var deal = ChaosDraftPool.Draft(pool, Met("e_stim"), new ScriptedRandom(), allowCurses: false);
        Assert.Single(deal);
        Assert.Equal("plain", deal[0].Id);
    }

    [Fact]
    public void Draft_RequiresAll_AllMet_DealsCard()
    {
        var pool = new List<Card> { new("plain"), new("electrified_rabbits", RequiresAll: new[] { "the_spanker", "e_stim" }) };
        var deal = ChaosDraftPool.Draft(pool, Met("the_spanker", "e_stim"), new ScriptedRandom(), allowCurses: false);
        Assert.Contains(deal, c => c.Id == "electrified_rabbits");
    }

    [Fact]
    public void Draft_RequiresAnyAndAll_Combine_BothMustHold()
    {
        // RequiresAny met but RequiresAll not -> excluded (the && of both clauses, WPF :408-409).
        var pool = new List<Card> { new("plain"), new("combo", RequiresAny: new[] { "a" }, RequiresAll: new[] { "b" }) };
        var deal = ChaosDraftPool.Draft(pool, Met("a"), new ScriptedRandom(), allowCurses: false);
        Assert.Single(deal);
        Assert.Equal("plain", deal[0].Id);
    }

    // ================================================================
    // Unique-taken exclusion (WPF ChaosModels.cs:410 + TakenBoonIds, ChaosModeService.cs:1511)
    // ================================================================

    [Fact]
    public void Draft_UniqueTaken_SitsTheRestOut()
    {
        var pool = new List<Card> { new("gold_digger", Unique: true), new("plain") };
        var deal = ChaosDraftPool.Draft(pool, NoneMet, new ScriptedRandom(),
            allowCurses: false, takenIds: new HashSet<string> { "gold_digger" });
        Assert.Single(deal);
        Assert.Equal("plain", deal[0].Id);
    }

    [Fact]
    public void Draft_NonUniqueTaken_StillReoffers()
    {
        // defuse_chain/golden_touch/extra_shield are NOT Unique in WPF — taken ids don't bench them.
        var pool = new List<Card> { new("defuse_chain"), new("plain") };
        var deal = ChaosDraftPool.Draft(pool, NoneMet, new ScriptedRandom(),
            allowCurses: false, takenIds: new HashSet<string> { "defuse_chain" });
        Assert.Equal(2, deal.Count);
        Assert.Contains(deal, c => c.Id == "defuse_chain");
    }

    // ================================================================
    // Sin slot: includeCurse roll + Surrender guarantee (WPF ChaosModels.cs:414-420)
    // ================================================================

    [Fact]
    public void Draft_GuaranteeCurse_ReservesExactlyOneSinSeat()
    {
        var pool = new List<Card> { new("b1"), new("b2"), new("b3"), new("s1", IsCurse: true), new("s2", IsCurse: true) };
        var deal = ChaosDraftPool.Draft(pool, NoneMet, new ScriptedRandom(),
            allowCurses: true, choices: 3, guaranteeCurse: true);
        Assert.Equal(3, deal.Count);
        Assert.Equal(1, deal.Count(c => c.IsCurse));   // boonCount = choices - 1 (WPF :416)
        Assert.Equal(2, deal.Count(c => !c.IsCurse));
    }

    [Fact]
    public void Draft_GuaranteeCurse_DoesNotConsumeTheSinRoll()
    {
        // Short-circuit (WPF :414): guaranteeCurse true -> NextDouble() is never called.
        var pool = new List<Card> { new("b1"), new("b2"), new("b3"), new("s1", IsCurse: true) };
        var rng = new ScriptedRandom(doubles: new[] { 0.99 });   // would REFUSE the sin if consumed
        var deal = ChaosDraftPool.Draft(pool, NoneMet, rng,
            allowCurses: true, choices: 3, guaranteeCurse: true, sinChance: 0.5);
        Assert.Equal(1, deal.Count(c => c.IsCurse));
    }

    [Fact]
    public void Draft_AllowCursesFalse_BlocksEvenTheGuarantee()
    {
        var pool = new List<Card> { new("b1"), new("b2"), new("b3"), new("s1", IsCurse: true) };
        var deal = ChaosDraftPool.Draft(pool, NoneMet, new ScriptedRandom(),
            allowCurses: false, choices: 3, guaranteeCurse: true);
        Assert.Equal(3, deal.Count);
        Assert.DoesNotContain(deal, c => c.IsCurse);
    }

    [Fact]
    public void Draft_SinRoll_BelowChance_DealsACurse()
    {
        var pool = new List<Card> { new("b1"), new("b2"), new("b3"), new("s1", IsCurse: true) };
        var rng = new ScriptedRandom(doubles: new[] { 0.4 });
        var deal = ChaosDraftPool.Draft(pool, NoneMet, rng, choices: 3, sinChance: 0.5);
        Assert.Equal(1, deal.Count(c => c.IsCurse));
    }

    [Fact]
    public void Draft_SinRoll_AtOrAboveChance_DealsNoCurse()
    {
        // Strict < (WPF :414): a roll equal to sinChance refuses the sin slot.
        var pool = new List<Card> { new("b1"), new("b2"), new("b3"), new("s1", IsCurse: true) };
        var rng = new ScriptedRandom(doubles: new[] { 0.5 });
        var deal = ChaosDraftPool.Draft(pool, NoneMet, rng, choices: 3, sinChance: 0.5);
        Assert.DoesNotContain(deal, c => c.IsCurse);
        Assert.Equal(3, deal.Count);
    }

    [Fact]
    public void Draft_NoDraftableCurses_SinSlotCollapsesToBoons()
    {
        // Every sin already taken (all Unique) -> curses.Count == 0 kills includeCurse (WPF :414)
        // and the full deal comes from boons.
        var pool = new List<Card> { new("b1"), new("b2"), new("b3"), new("s1", IsCurse: true, Unique: true) };
        var deal = ChaosDraftPool.Draft(pool, NoneMet, new ScriptedRandom(),
            choices: 3, guaranteeCurse: true, takenIds: new HashSet<string> { "s1" });
        Assert.Equal(3, deal.Count);
        Assert.DoesNotContain(deal, c => c.IsCurse);
    }

    // ================================================================
    // Short pools: top-up + graceful under-deal (WPF ChaosModels.cs:419-428)
    // ================================================================

    [Fact]
    public void Draft_ShortBoonPool_WithSin_DealsWhatExists()
    {
        var pool = new List<Card> { new("b1"), new("s1", IsCurse: true) };
        var deal = ChaosDraftPool.Draft(pool, NoneMet, new ScriptedRandom(),
            choices: 3, guaranteeCurse: true);
        Assert.Equal(2, deal.Count);
        Assert.Contains(deal, c => c.Id == "b1");
        Assert.Contains(deal, c => c.Id == "s1");
    }

    [Fact]
    public void Draft_ShortBoonPool_NoCurse_UnderDealsWithoutCrashing()
    {
        var deal = ChaosDraftPool.Draft(Boons(2), NoneMet, new ScriptedRandom(),
            allowCurses: false, choices: 4);
        Assert.Equal(2, deal.Count);
        Assert.Equal(2, deal.Select(c => c.Id).Distinct().Count());
    }

    // ================================================================
    // Determinism through the injected Random (shuffle keys + re-deal/reroll)
    // ================================================================

    [Fact]
    public void Draft_ScriptedShuffle_OrdersBySortKey()
    {
        // Keys per boon in pool order: b1->3, b2->0, b3->2, b4->1; stable OrderBy ascending
        // gives b2, b4, b3, b1 -> Take(3) = b2, b4, b3 (WPF :411/419).
        var rng = new ScriptedRandom(ints: new[] { 3, 0, 2, 1 }, doubles: new[] { 0.99 });
        var deal = ChaosDraftPool.Draft(Boons(4), NoneMet, rng, choices: 3, sinChance: 0.5);
        Assert.Equal(new[] { "b2", "b4", "b3" }, deal.Select(c => c.Id).ToArray());
    }

    [Fact]
    public void Draft_GuaranteedSin_TakesFirstCurseAfterShuffle()
    {
        // Boon keys: b1->0, b2->1; curse keys: s1->5, s2->1 -> curses[0] is s2 (WPF :420).
        var pool = new List<Card> { new("b1"), new("b2"), new("s1", IsCurse: true), new("s2", IsCurse: true) };
        var rng = new ScriptedRandom(ints: new[] { 0, 1, 5, 1 });
        var deal = ChaosDraftPool.Draft(pool, NoneMet, rng, choices: 3, guaranteeCurse: true);
        Assert.Contains(deal, c => c.Id == "s2");
        Assert.DoesNotContain(deal, c => c.Id == "s1");
    }

    [Fact]
    public void Draft_Redeal_ConsumesTheSameRngStream_Deterministically()
    {
        // Reroll = calling Draft again with the same params (WPF RerollDraft,
        // ChaosModeService.cs:1521-1531): the second deal advances the shared stream.
        var pool = Boons(3);
        var rng = new ScriptedRandom(
            ints: new[] { 0, 1, 2, /* second deal: */ 2, 1, 0 },
            doubles: new[] { 0.99, 0.99 });
        var first = ChaosDraftPool.Draft(pool, NoneMet, rng, choices: 3, sinChance: 0.5);
        var second = ChaosDraftPool.Draft(pool, NoneMet, rng, choices: 3, sinChance: 0.5);
        Assert.Equal(new[] { "b1", "b2", "b3" }, first.Select(c => c.Id).ToArray());
        Assert.Equal(new[] { "b3", "b2", "b1" }, second.Select(c => c.Id).ToArray());
    }

    [Fact]
    public void Draft_SinSeat_ReducesBoonCountByOne_EvenAtFourChoices()
    {
        // draft4 upgrade path: choices 4 + a guaranteed sin -> 3 boons + 1 curse (WPF :416).
        var pool = new List<Card>
        {
            new("b1"), new("b2"), new("b3"), new("b4"), new("b5"),
            new("s1", IsCurse: true),
        };
        var deal = ChaosDraftPool.Draft(pool, NoneMet, new ScriptedRandom(),
            choices: 4, guaranteeCurse: true);
        Assert.Equal(4, deal.Count);
        Assert.Equal(3, deal.Count(c => !c.IsCurse));
        Assert.Equal(1, deal.Count(c => c.IsCurse));
    }
}
