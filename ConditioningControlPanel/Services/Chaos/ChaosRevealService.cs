using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>Element ids for every gateable surface. UI code references these, never raw strings.</summary>
public static class RevealIds
{
    public const string Dollhouse          = "dollhouse";            // the hub itself (first descent done)
    public const string TabLookingGlass    = "tab_looking_glass";    // Slipping
    public const string SectionToys        = "toybox_toys";          // first toy pocket owned
    public const string SectionAccessories = "toybox_accessories";   // first accessory pocket owned
    public const string HerCorner          = "toybox_her_corner";    // bench stub in the Toybox (run 2+, until Looking Glass reveals)
    public const string PillTeasing        = "pill_teasing";         // Tempted
    public const string PillRelentless     = "pill_relentless";      // Entranced
    public const string PillInescapable    = "pill_inescapable";     // extreme_tier owned (unchanged)
    public const string DraftSkip          = "draft_skip";           // run 3+
    public const string StartPicker        = "start_picker";         // bench: the starting mantra
    public const string Diary              = "diary";                // bench: the Diary
    public const string StatsPanel         = "stats_panel";          // bench: the stats panel
    public const string BenchToyPocket2    = "bench_toy_pocket_2";   // Devoted
    public const string BenchAccPocket2    = "bench_acc_pocket_2";   // Devoted
    public const string VariantVideo       = "variant_video";        // Entranced (run whitelist clamp)
    public const string VariantHtlink      = "variant_htlink";       // Entranced (run whitelist clamp)
    public const string Capstones          = "capstones";            // Devoted (final levels purchasable)
    public const string ExtremeTierRow     = "extreme_tier_buyable"; // Devoted (buyability; lesson stacks on top)
}

/// <summary>Gold purchases at her bench. Ids persist in <see cref="ChaosMetaState.BenchPurchases"/>.</summary>
public static class BenchIds
{
    public const string ToyPocket1 = "toy_pocket_1";
    public const string AccPocket1 = "accessory_pocket_1";
    public const string StartMantra = "start_mantra";
    public const string Diary = "diary";
    public const string StatsPanel = "stats_panel";
    public const string ToyPocket2 = "toy_pocket_2";
    public const string AccPocket2 = "accessory_pocket_2";
}

/// <summary>
/// The reveal framework. The UI starts naked: anything gateable is HIDDEN until its
/// predicate flips, then it enters a persisted pending set (REVEALED_PENDING), flashes
/// once on the next dollhouse open, and settles to SEEN.
///
/// Settings clamp rule: gates CLAMP user settings (effective = setting AND unlocked),
/// never overwrite the saved value — unlocking restores the user's own config untouched.
/// </summary>
public static class RevealService
{
    private static readonly Dictionary<string, Func<bool>> _registry = new()
    {
        [RevealIds.Dollhouse]          = () => ChaosMeta.State.RunsCompleted >= 1,
        [RevealIds.TabLookingGlass]    = () => ChaosMeta.RankIndex >= ChaosRank.Slipping,
        // grab-in-the-tube rework: discovered in the fall, not pocket-gated - open from run 1.
        [RevealIds.SectionToys]        = () => ChaosMeta.State.RunsCompleted >= 1,
        [RevealIds.SectionAccessories] = () => ChaosMeta.State.RunsCompleted >= 1,
        [RevealIds.HerCorner]          = () => ChaosMeta.State.RunsCompleted >= 2 && ChaosMeta.RankIndex < ChaosRank.Slipping,
        [RevealIds.PillTeasing]        = () => ChaosMeta.RankIndex >= ChaosRank.Tempted,
        [RevealIds.PillRelentless]     = () => ChaosMeta.RankIndex >= ChaosRank.Entranced,
        [RevealIds.PillInescapable]    = () => ChaosMeta.State.ExtremeUnlocked,
        [RevealIds.DraftSkip]          = () => ChaosMeta.State.RunsCompleted >= 2,
        [RevealIds.StartPicker]        = () => ChaosMeta.State.BenchPurchases.Contains(BenchIds.StartMantra),
        [RevealIds.Diary]              = () => ChaosMeta.State.BenchPurchases.Contains(BenchIds.Diary),
        [RevealIds.StatsPanel]         = () => ChaosMeta.State.BenchPurchases.Contains(BenchIds.StatsPanel),
        [RevealIds.BenchToyPocket2]    = () => ChaosMeta.RankIndex >= ChaosRank.Devoted,
        [RevealIds.BenchAccPocket2]    = () => ChaosMeta.RankIndex >= ChaosRank.Devoted,
        [RevealIds.VariantVideo]       = () => ChaosMeta.RankIndex >= ChaosRank.Entranced,
        [RevealIds.VariantHtlink]      = () => ChaosMeta.RankIndex >= ChaosRank.Entranced,
        [RevealIds.Capstones]          = () => ChaosMeta.RankIndex >= ChaosRank.Devoted,
        [RevealIds.ExtremeTierRow]     = () => ChaosMeta.RankIndex >= ChaosRank.Devoted,
    };

    /// <summary>Raised when an element first unlocks (toast hook). UI thread not guaranteed.</summary>
    public static event Action<string>? Pending;

    /// <summary>HIDDEN check: render the surface only when this is true.</summary>
    public static bool IsUnlocked(string id) =>
        !_registry.TryGetValue(id, out var pred) || SafePred(pred);

    public static bool IsPending(string id) => ChaosMeta.State.PendingReveals.Contains(id);
    public static bool IsSeen(string id) => ChaosMeta.State.SeenReveals.Contains(id);

    /// <summary>Effective value of a gated user setting: clamped, never overwritten.</summary>
    public static bool Clamp(string id, bool userSetting) => userSetting && IsUnlocked(id);

    /// <summary>
    /// Detect fresh unlocks (predicate true, never flashed, not already pending) and queue
    /// them for the next dollhouse-open flash. Call at run end, on purchase, on lesson
    /// completion, and on dollhouse open. Persists once when anything changed.
    /// </summary>
    public static void Sync(string reason)
    {
        try
        {
            bool changed = false;
            foreach (var (id, pred) in _registry)
            {
                if (!SafePred(pred)) continue;
                if (ChaosMeta.State.SeenReveals.Contains(id)) continue;
                if (!ChaosMeta.State.PendingReveals.Add(id)) continue;
                changed = true;
                App.Logger?.Information("Chaos reveal pending: {Id} ({Reason})", id, reason);
                try { Pending?.Invoke(id); } catch { }
            }
            if (changed) ChaosMeta.Save();
        }
        catch (Exception ex) { App.Logger?.Warning("RevealService.Sync failed ({E})", ex.Message); }
    }

    /// <summary>Snapshot of ids awaiting their flash (dollhouse open). Does not mutate state.</summary>
    public static IReadOnlyList<string> PendingIds() => ChaosMeta.State.PendingReveals.ToList();

    /// <summary>After an element's flash animation played: pending -> seen, persisted.</summary>
    public static void MarkSeen(string id)
    {
        bool changed = ChaosMeta.State.PendingReveals.Remove(id);
        changed |= ChaosMeta.State.SeenReveals.Add(id);
        if (changed) ChaosMeta.Save();
    }

    private static bool SafePred(Func<bool> pred)
    {
        try { return pred(); } catch { return false; }
    }
}
