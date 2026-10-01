using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>One gold purchase at her bench (moved out of the WPF hub's Bench partial so every
/// head sells the same rows at the same prices).</summary>
public sealed class ChaosBenchItem
{
    public string Id = "";
    public string Glyph = "👝";
    public string Label = "";
    public string Line = "";
    public int Cost;
    /// <summary>Rank required to buy (row shows rank-locked below it).</summary>
    public ChaosRank? RankNeed;
    /// <summary>Reveal id that keeps the row hazy (???) until it unlocks.</summary>
    public string? RevealGate;
    public Action<ChaosMetaState>? ApplyEffect;
}

public enum ChaosBenchBuy { Denied, Bought, Gift }

/// <summary>Her bench — the gold shop. Gold never buys power: pockets, the diary, the stats
/// panel, the starting mantra.</summary>
public static class ChaosBench
{
    // ---- gold prices (tunable) ----
    public const int GOLD_TOY_POCKET_1 = 50;
    public const int GOLD_ACC_POCKET_1 = 150;
    public const int GOLD_START_MANTRA = 200;
    public const int GOLD_DIARY        = 150;
    public const int GOLD_STATS_PANEL  = 100;
    public const int GOLD_TOY_POCKET_2 = 2000;
    public const int GOLD_ACC_POCKET_2 = 2500;

    public static readonly IReadOnlyList<ChaosBenchItem> Items = new List<ChaosBenchItem>
    {
        new() { Id = BenchIds.ToyPocket1, Glyph = "👝", Label = "first toy pocket",
                Line = "she sews you a pocket.", Cost = GOLD_TOY_POCKET_1,
                ApplyEffect = s => s.ToyPockets++ },
        new() { Id = BenchIds.AccPocket1, Glyph = "👝", Label = "first accessory pocket",
                Line = "she only has two hands. she found a third.", Cost = GOLD_ACC_POCKET_1,
                ApplyEffect = s => s.AccessoryPockets++ },
        new() { Id = BenchIds.StartMantra, Glyph = "◈", Label = "the starting mantra",
                Line = "fall in holding something.", Cost = GOLD_START_MANTRA },
        new() { Id = BenchIds.Diary, Glyph = "📓", Label = "the diary",
                Line = "she keeps notes on what you meet down there.", Cost = GOLD_DIARY },
        new() { Id = BenchIds.StatsPanel, Glyph = "🕰", Label = "the stats panel",
                Line = "the numbers, if you want them.", Cost = GOLD_STATS_PANEL },
        new() { Id = BenchIds.ToyPocket2, Glyph = "👝", Label = "second toy pocket",
                Line = "she found room for one more.", Cost = GOLD_TOY_POCKET_2,
                RankNeed = ChaosRank.Devoted, RevealGate = RevealIds.BenchToyPocket2,
                ApplyEffect = s => s.ToyPockets++ },
        new() { Id = BenchIds.AccPocket2, Glyph = "👝", Label = "second accessory pocket",
                Line = "a fourth hand. don't ask.", Cost = GOLD_ACC_POCKET_2,
                RankNeed = ChaosRank.Devoted, RevealGate = RevealIds.BenchAccPocket2,
                ApplyEffect = s => s.AccessoryPockets++ },
    };

    /// <summary>Reserved hazy rows: names on the bench, nothing behind them yet.</summary>
    public static readonly string[] ReservedRows =
    {
        "the clocks", "descent ledger", "payout eyes", "the fine print",
        "fall right in", "held breath", "soft landing", "no countdown",
        "dollhouse wallpapers", "recap frames", "a chattier companion", "the pact",
    };

    /// <summary>Claimed-reserved rows: visible only at Devoted+.</summary>
    public static readonly string[] ClaimedReservedRows = { "daily descent", "leaderboard", "prestige" };

    public static bool IsOwned(ChaosBenchItem item) => ChaosMeta.State.BenchPurchases.Contains(item.Id);
    public static bool IsRankShort(ChaosBenchItem item) => item.RankNeed.HasValue && !ChaosMeta.AtLeast(item.RankNeed.Value);
    public static bool IsHazy(ChaosBenchItem item) => item.RevealGate != null && !RevealService.IsUnlocked(item.RevealGate);

    /// <summary>Buy a bench row: owned / rank-short / hazy / short on gold is Denied, except
    /// THE GIFT — the very first short buy on the first toy pocket, she covers it, once.
    /// Gold, the purchase and its effect land in memory together and persist in ONE save
    /// (the WPF hub saved twice: gold first, purchase second).</summary>
    public static ChaosBenchBuy TryBuy(string id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item == null || IsOwned(item) || IsRankShort(item) || IsHazy(item)) return ChaosBenchBuy.Denied;

        var s = ChaosMeta.State;
        var result = ChaosBenchBuy.Bought;
        if (s.Gold >= item.Cost) s.Gold -= item.Cost;
        else if (item.Id == BenchIds.ToyPocket1 && !s.GiftGiven) { s.GiftGiven = true; s.Gold = 0; result = ChaosBenchBuy.Gift; }
        else return ChaosBenchBuy.Denied;

        s.BenchPurchases.Add(item.Id);
        try { item.ApplyEffect?.Invoke(s); }
        catch (Exception ex) { Log.Warning("Bench effect {Id} failed ({E})", item.Id, ex.Message); }
        ChaosMeta.Save();
        return result;
    }
}
