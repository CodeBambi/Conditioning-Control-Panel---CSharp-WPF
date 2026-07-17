using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>The three permanent-upgrade branches.</summary>
public enum ChaosBranch { Control, Greed, Depth }

/// <summary>
/// One permanent, purchasable upgrade. <see cref="Apply"/> mutates a freshly-built
/// <see cref="ChaosRunConfig"/> at run start — owning the upgrade shapes every run.
/// </summary>
public sealed class ChaosUpgrade
{
    public string Id = "";
    public ChaosBranch Branch;
    public string Name = "";
    /// <summary>One-line hover-card text (lowercase house voice).</summary>
    public string Desc = "";
    /// <summary>One flavorful line, rendered grey italic under the mechanics text.</summary>
    public string Flavor = "";
    /// <summary>Placeholder for the square tile until real art lands at assets/Chaos/upgrades/{id}.png.</summary>
    public string Glyph = "✦";
    public int Cost;                            // in Sparks
    public Action<ChaosRunConfig> Apply = _ => { };
    /// <summary>Optional icon path; null falls back to a vector placeholder. Wired in phase 5.</summary>
    public string? IconPath = null;
}

/// <summary>
/// The v1 upgrade catalogue. Costs are constants up top for easy balancing later.
/// Rows tagged "runtime deferred" only set a config field; the consuming runtime
/// (hitbox hit-test, shield regen, detonation-penalty path) lands in a later session,
/// so those fields are inert for now.
/// </summary>
public static class ChaosUpgrades
{
    // ---- cost constants (tunable) ----
    public const int COST_SLOW_FUSES      = 120;
    public const int COST_SILK_TOUCH      = 180;
    public const int COST_POPUP_NOTIF     = 160;
    public const int COST_PENDULUM        = 220;
    public const int COST_DRAFT4          = 200;
    public const int COST_EXTREME_TIER    = 350;

    public static readonly IReadOnlyList<ChaosUpgrade> All = new List<ChaosUpgrade>
    {
        // ---- Control ----
        new() { Id = "slow_fuses",      Branch = ChaosBranch.Control, Name = "Slower Trance",    Cost = COST_SLOW_FUSES, Glyph = "⏳",
                Desc = "live bubbles hold their trance 15% longer before they trigger.",
                Flavor = "a little more time to change your mind. you won't.",
                Apply = c => c.FuseTimeMult *= 1.15 },
        // bigger_hitboxes (Soft Focus) + magnet (Mesmer Reach) merged into silk_touch 2026-06-10;
        // shield_recharge (Slow Recovery) reborn as the leveled slow_recovery Utility charm
        // (pop-based regen). Owners are refunded at load — never reuse the ids.
        new() { Id = "silk_touch",      Branch = ChaosBranch.Control, Name = "Silk Touch", Cost = COST_SILK_TOUCH, Glyph = "🪶",
                Desc = "bubble hitboxes grow 25%, and a near-miss on a live one still counts as a touch.",
                Flavor = "silk doesn't try. it just lands.",
                Apply = c => { c.HitboxScale = 1.25; c.MagnetEnabled = true; } },
        new() { Id = "popup_notification", Branch = ChaosBranch.Control, Name = "Pop-up Notification", Cost = COST_POPUP_NOTIF, Glyph = "💖",
                Desc = "once per loop, 60% of the time, a heart drifts down mid-loop. catch it for +1 resistance and +10 focus. missing it costs nothing.",
                Flavor = "you opted in. you always opt in.",
                Apply = c => c.PopupHeartEnabled = true },
        // Re-gated 2026-06-11: the swing was briefly a free event for everyone; now it's a
        // trained habit again. NEW id on purpose — "pendulum" sits in the refund-scrub table
        // forever, so reusing it would refund/strip the purchase at every load.
        new() { Id = "pendulum_swing",  Branch = ChaosBranch.Control, Name = "Pendulum", Cost = COST_PENDULUM, Glyph = "🕰",
                Desc = "once per loop, at a random beat, the pendulum swings: 2.5 seconds of slow motion. the \"Focus here...\" mantra turns that swing into a x3 pay window.",
                Flavor = "tick. tock. you looked.",
                Apply = c => c.PendulumSwing = true },
        // More 2026-06-10 conversions (owners refunded at load — never reuse the ids):
        //   start_shield → "It would never work on me..." charm (leveled +1/+2/+3; base is now 0)
        //   collar       → Collar charm (leveled 1/2/3 saves)
        //   pendulum     → free once-per-loop random event (ChaosModeService pendulum swing)
        //   base_mult + golden_touch → Golden Touch charm (leveled baseline mult + calm-pop bonus)
        //   take_more    → Slowburner charm (leveled slower fuses + brink-defuse capstone)
        //   tunnel_vision + max_bubbles CUT outright (refunded — never reuse the ids)
        // spark_gain retired 2026-06-10 pre-release (superseded by the Drip Feed charm); no refund
        // owed — it never shipped. Never reuse the id; owners are silently scrubbed at load.

        // ---- Depth ----
        new() { Id = "draft4",          Branch = ChaosBranch.Depth,   Name = "4-Mantra Draft",    Cost = COST_DRAFT4, Glyph = "🃏",
                Desc = "mantra drafts offer four choices instead of three.",
                Flavor = "more ways to say yes.",
                Apply = c => c.DraftChoices = 4 },
        new() { Id = "extreme_tier",    Branch = ChaosBranch.Depth,   Name = "Inescapable Tier",    Cost = COST_EXTREME_TIER, Glyph = "🌀",
                Desc = "opens the inescapable difficulty in the descent setup.",
                Flavor = "the last door was never locked.",
                Apply = _ => { } },                                       // flag stored at purchase time
    };

    public static ChaosUpgrade? ById(string id) => All.FirstOrDefault(u => u.Id == id);
}

/// <summary>
/// Static facade over the persistent meta state — the API the future hub UI and the
/// run hooks call. <see cref="State"/> is loaded once at startup via <see cref="Init"/>.
/// </summary>
public static class ChaosMeta
{
    public static ChaosMetaState State { get; private set; } = new();

    /// <summary>One-time "first fall" bonus on the very first completed descent
    /// (banked in <see cref="AwardRunRewards"/>, named on the recap card).</summary>
    public const int FIRST_FALL_BONUS = 25;

    public static void Init()
    {
        State = ChaosMetaStore.Load();
        RefundRetiredBoons();
        SanitizePockets();
    }

    /// <summary>Loadout contract: at most 2 toys + 2 accessories down the hole (the bench only
    /// ever sells two pockets per category). Older/inflated saves can carry more pockets and
    /// more equipped boons than that — clamp the counts and unequip the overflow once on load,
    /// keeping catalogue order (habits/charms stay uncapped).</summary>
    private static void SanitizePockets()
    {
        try
        {
            bool changed = false;
            if (State.ToyPockets > MAX_POCKETS_PER_CATEGORY) { State.ToyPockets = MAX_POCKETS_PER_CATEGORY; changed = true; }
            if (State.AccessoryPockets > MAX_POCKETS_PER_CATEGORY) { State.AccessoryPockets = MAX_POCKETS_PER_CATEGORY; changed = true; }
            foreach (var cat in new[] { ChaosBoonCategory.Skill, ChaosBoonCategory.Accessory })
            {
                int keep = SlotsFor(cat);
                foreach (var b in ChaosLifetimeBoons.InCategory(cat))
                {
                    if (!IsBoonActive(b.Id)) continue;
                    if (keep > 0) { keep--; continue; }
                    State.ActiveLifetimeBoons!.Remove(b.Id);
                    changed = true;
                    App.Logger?.Information("Chaos: unequipped {Id} (over the {Cat} pocket cap)", b.Id, cat);
                }
            }
            if (changed) Save();
        }
        catch (Exception ex) { App.Logger?.Warning("Chaos: pocket sanitize failed ({E})", ex.Message); }
    }

    /// <summary>DEBUG ONLY (CCP_CHAOS_DEBUG dev strip): throw the meta state away and start
    /// fresh, persisted. Never called by gameplay code.</summary>
    public static void DebugResetState()
    {
        State = new ChaosMetaState();
        Save();
        App.Logger?.Warning("ChaosMeta: state RESET via debug strip");
    }

    /// <summary>Cumulative Spark cost by level (unlock + upgrades) for boons removed from the
    /// catalogue — owners get their Sparks back on first load after the cut.</summary>
    private static readonly Dictionary<string, int[]> RetiredBoonRefunds = new()
    {
        ["muscle_memory"] = new[] { 200, 400, 700, 1150, 1800 },   // retired 2026-06-10
        ["magic_wand"]    = new[] { 150, 300, 550, 950, 1550 },    // retired 2026-06-10
    };

    private static void RefundRetiredBoons()
    {
        try
        {
            bool changed = false;
            foreach (var (id, costs) in RetiredBoonRefunds)
            {
                if (State.LifetimeBoonLevels != null &&
                    State.LifetimeBoonLevels.TryGetValue(id, out int lvl) && lvl >= 1)
                {
                    int refund = costs[Math.Clamp(lvl, 1, costs.Length) - 1];
                    State.Sparks += refund;
                    State.LifetimeBoonLevels.Remove(id);
                    changed = true;
                    App.Logger?.Information("Chaos: retired boon {Id} (L{Lvl}) refunded ✦{Refund}", id, lvl, refund);
                }
                if (State.ActiveLifetimeBoons?.Remove(id) == true) changed = true;
            }
            // Habits retired pre-release (no refund owed): scrub the id so nothing dangles.
            if (State.PurchasedUpgrades?.Remove("spark_gain") == true) changed = true;
            if (State.DisabledUpgrades?.Remove("spark_gain") == true) changed = true;
            // Habits retired WITH a refund (2026-06-10): Soft Focus + Mesmer Reach merged into
            // silk_touch; the time-based Slow Recovery reborn as the slow_recovery Utility charm.
            // Owners get their Sparks back and re-train the replacements.
            var retiredHabitRefunds = new Dictionary<string, int>
            {
                ["bigger_hitboxes"] = 80, ["magnet"] = 150, ["shield_recharge"] = 200,
                ["start_shield"] = 100, ["collar"] = 200, ["pendulum"] = 220,
                ["base_mult"] = 90, ["golden_touch"] = 130, ["take_more"] = 400,
                ["tunnel_vision"] = 140, ["max_bubbles"] = 110,
            };
            foreach (var (id, refund) in retiredHabitRefunds)
            {
                if (State.PurchasedUpgrades?.Remove(id) == true)
                {
                    State.Sparks += refund;
                    changed = true;
                    App.Logger?.Information("Chaos: retired habit {Id} refunded ✦{Refund}", id, refund);
                }
                if (State.DisabledUpgrades?.Remove(id) == true) changed = true;
            }
            // Accessories moved to habits pre-release (no refund): drop the old boon levels.
            // "collar" left this list 2026-06-10 — it's a lifetime charm again (leveled saves),
            // so its boon levels must survive the load.
            foreach (var id in new[] { "tunnel_vision", "pendulum" })
            {
                if (State.LifetimeBoonLevels?.Remove(id) == true) changed = true;
                if (State.ActiveLifetimeBoons?.Remove(id) == true) changed = true;
            }
            if (changed) Save();
        }
        catch (Exception ex) { App.Logger?.Warning("Chaos: retired-boon refund failed ({E})", ex.Message); }
    }

    /// <summary>Persist the current state (after a mutation made directly on State).</summary>
    public static void Save() => ChaosMetaStore.Save(State);

    // ---- save slots (three independent local saves; picked before the hole opens) ----

    /// <summary>The live save slot (1-3). Backed by <see cref="Models.AppSettings.ChaosActiveSlot"/>.</summary>
    public static int ActiveSlot => ChaosMetaStore.ActiveSlot;

    /// <summary>Commit the chosen save slot for this descent: remember it, then load that slot's
    /// file authoritatively (or a fresh state if the slot is empty) and re-run the on-load
    /// refunds/sanitize so the swapped-in save is as clean as one loaded at startup.
    ///
    /// This deliberately does NOT bank the outgoing slot first — the picker only opens when the
    /// hole is closed, so the active slot's file is already current from the last session's flush,
    /// and banking here would resurrect a slot the player just deleted. Loading is always
    /// file-driven so "delete the active save, then descend into it" correctly starts fresh.</summary>
    public static void SwitchSlot(int slot)
    {
        if (slot < 1 || slot > ChaosMetaStore.SlotCount) slot = 1;
        if (App.Settings?.Current != null)
        {
            App.Settings.Current.ChaosActiveSlot = slot;
            try { App.Settings.Save(); } catch (Exception ex) { App.Logger?.Warning("ChaosMeta.SwitchSlot: settings save failed ({E})", ex.Message); }
        }
        State = ChaosMetaStore.Load(slot);
        RefundRetiredBoons();
        SanitizePockets();
        App.Logger?.Information("ChaosMeta: live save is now slot {Slot}", slot);
    }

    /// <summary>Headline stats for every slot, for the pre-descent picker. Pure read — the active
    /// slot's file is already current (flushed when the hole last closed), so this never writes
    /// and a just-deleted slot stays gone.</summary>
    public static List<SlotSummary> AllSlotSummaries()
    {
        var list = new List<SlotSummary>();
        for (int s = 1; s <= ChaosMetaStore.SlotCount; s++) list.Add(ChaosMetaStore.ReadSummary(s));
        return list;
    }

    /// <summary>Erase a slot's file back to "New Journey". Does not touch the in-memory state:
    /// the next <see cref="SwitchSlot"/> reloads from disk, so deleting the active slot and then
    /// descending into it starts fresh, while descending into a different slot is unaffected.
    /// Returns true if a save was removed.</summary>
    public static bool DeleteSlot(int slot)
    {
        bool removed = ChaosMetaStore.Delete(slot);
        if (removed) App.Logger?.Information("ChaosMeta: deleted save slot {Slot}", slot);
        return removed;
    }

    /// <summary>Rank derived from lifetime <see cref="ChaosMetaState.RunsCompleted"/> (monotonic, simple).</summary>
    public static string Rank => ChaosRanks.Name(RankIndex);

    /// <summary>The rank spine — every progression gate reads this (or RunsCompleted), nothing else.</summary>
    public static ChaosRank RankIndex => ChaosRanks.For(State.RunsCompleted);

    public static bool AtLeast(ChaosRank rank) => RankIndex >= rank;

    // ---- gold (instant in-run currency; spent only at her bench) ----

    /// <summary>Bank gold immediately (instant in-run payouts persist like instant drops did).</summary>
    public static void AddGold(int amount)
    {
        if (amount <= 0) return;
        State.Gold += amount;
        Save();
    }

    /// <summary>Validate + spend gold. Returns false (no mutation) when short.</summary>
    public static bool TrySpendGold(int amount)
    {
        if (amount < 0 || State.Gold < amount) return false;
        State.Gold -= amount;
        Save();
        return true;
    }

    /// <summary>Equip (or clear, with null) the pre-drafted start boon and persist.</summary>
    public static void EquipStartBoon(string? boonId)
    {
        State.EquippedStartBoon = boonId;
        Save();
    }

    /// <summary>Mark a Codex entry encountered. Persists only on the first sighting (bounded writes).</summary>
    public static void MarkDiscovered(string codexId)
    {
        if (string.IsNullOrEmpty(codexId)) return;
        State.DiscoveredCodexIds ??= new();
        if (State.DiscoveredCodexIds.Add(codexId)) Save();
    }

    public static bool IsDiscovered(string codexId) =>
        State.DiscoveredCodexIds != null && State.DiscoveredCodexIds.Contains(codexId);

    public static bool IsOwned(string id) => State.PurchasedUpgrades.Contains(id);

    /// <summary>A trained habit counts at run start unless the player switched it off.</summary>
    public static bool IsUpgradeActive(string id) =>
        IsOwned(id) && (State.DisabledUpgrades == null || !State.DisabledUpgrades.Contains(id));

    /// <summary>Switch a trained habit on/off (no-op on untrained ids). Persists on change.</summary>
    public static void SetUpgradeActive(string id, bool active)
    {
        if (!IsOwned(id)) return;
        State.DisabledUpgrades ??= new();
        bool changed = active ? State.DisabledUpgrades.Remove(id) : State.DisabledUpgrades.Add(id);
        if (changed) Save();
    }

    public static bool CanAfford(string id)
    {
        var u = ChaosUpgrades.ById(id);
        return u != null && !IsOwned(id) && State.Sparks >= u.Cost;
    }

    /// <summary>True while this habit's purchase sits above the player's rank (UI renders the
    /// locked row with <see cref="ChaosRanks.RankLockedTip"/>). Only extreme_tier carries a
    /// rank floor today (Devoted; a lesson gate stacks on top separately).</summary>
    public static bool IsPurchaseRankLocked(string id) =>
        id == "extreme_tier" && !IsOwned(id) && !AtLeast(ChaosRank.Devoted);

    /// <summary>
    /// Validate + buy: checks the id exists, isn't already owned, is affordable, and clears
    /// any rank floor; deducts the cost, records the purchase, sets
    /// <see cref="ChaosMetaState.ExtremeUnlocked"/> for <c>extreme_tier</c>, persists, and
    /// returns whether it succeeded.
    /// </summary>
    public static bool TryPurchase(string id)
    {
        var u = ChaosUpgrades.ById(id);
        if (u == null) return false;
        if (ChaosLessons.IsLessonBlocked(id)) return false;   // the lesson comes before the drops
        if (State.PurchasedUpgrades.Contains(id)) return false;
        if (State.Sparks < u.Cost) return false;
        if (IsPurchaseRankLocked(id)) return false;

        State.Sparks -= u.Cost;
        State.PurchasedUpgrades.Add(id);
        if (id == "extreme_tier") State.ExtremeUnlocked = true;
        ChaosMetaStore.Save(State);
        return true;
    }

    /// <summary>Apply every owned-and-switched-on upgrade's effect to a freshly-built run config.</summary>
    public static void ApplyTo(ChaosRunConfig config)
    {
        if (config == null) return;
        foreach (var id in State.PurchasedUpgrades)
            if (IsUpgradeActive(id))
                ChaosUpgrades.ById(id)?.Apply(config);
    }

    // ---- lifetime boons (Skills / Accessories / Utility): unlock + level + equip ----
    // Permanent, leveled boons bought with Sparks; applied to a run at start when EQUIPPED.
    // Skills and Accessories are pocket-slotted (2 each) — you choose what you take down.
    // Utility is uncapped. They carry no run-mult (utility, not score).

    /// <summary>The most pockets a category can ever hold — the bench sells exactly two per
    /// category, and the loadout contract is 2 toys + 2 accessories max. Guards against
    /// inflated saves on top of <see cref="SanitizePockets"/>.</summary>
    public const int MAX_POCKETS_PER_CATEGORY = 2;

    /// <summary>Grab-in-the-tube rework: the most consumable (active-toy) HUD slots (POCKETS) the
    /// player can ever hold at once. Starts at 1 (<see cref="ChaosMetaState.ConsumableSlots"/>); the
    /// dollhouse sews more with Sparks up to this cap. Must match MAX_CONSUMABLE_SLOTS in catalog.js.</summary>
    public const int MAX_CONSUMABLE_SLOTS = 5;

    /// <summary>Equip pockets for a category: purchase-driven (her bench sews them), starting
    /// at ZERO toys / ZERO accessories on a fresh save, capped at 2; Utility toggles are uncapped.</summary>
    public static int SlotsFor(ChaosBoonCategory cat) => cat switch
    {
        ChaosBoonCategory.Utility   => int.MaxValue,
        ChaosBoonCategory.Skill     => Math.Min(State.ToyPockets, MAX_POCKETS_PER_CATEGORY),
        ChaosBoonCategory.Accessory => Math.Min(State.AccessoryPockets, MAX_POCKETS_PER_CATEGORY),
        _ => 0,
    };

    /// <summary>How many boons of this category are currently equipped (active).</summary>
    public static int EquippedCountIn(ChaosBoonCategory cat) =>
        State.ActiveLifetimeBoons == null ? 0 :
        State.ActiveLifetimeBoons.Count(id => ChaosLifetimeBoons.ById(id)?.Category == cat && IsBoonUnlocked(id));

    /// <summary>True when this category still has a free pocket (Utility always does).</summary>
    public static bool HasFreePocket(ChaosBoonCategory cat) =>
        EquippedCountIn(cat) < SlotsFor(cat);

    /// <summary>Current level of a lifetime boon (0 = locked, >=1 = unlocked at that level).</summary>
    public static int BoonLevel(string id) =>
        State.LifetimeBoonLevels != null && State.LifetimeBoonLevels.TryGetValue(id, out var l) ? l : 0;

    public static bool IsBoonUnlocked(string id) => BoonLevel(id) >= 1;
    public static bool IsBoonActive(string id) =>
        State.ActiveLifetimeBoons != null && State.ActiveLifetimeBoons.Contains(id) && IsBoonUnlocked(id);

    /// <summary>Spark cost to unlock (level 1), or null if the id is unknown/already unlocked.</summary>
    public static int? UnlockCostOf(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        return (b == null || IsBoonUnlocked(id)) ? (int?)null : b.UnlockCost;
    }

    /// <summary>Spark cost of the next level-up, or null if locked / already at max.</summary>
    public static int? NextUpgradeCostOf(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        if (b == null) return null;
        int lvl = BoonLevel(id);
        if (lvl < 1 || lvl >= b.MaxLevel) return null;
        return (lvl - 1) < b.UpgradeCosts.Length ? b.UpgradeCosts[lvl - 1] : (int?)null;
    }

    public static bool CanAffordUnlock(string id)
    {
        var c = UnlockCostOf(id);
        return c.HasValue && State.Sparks >= c.Value;
    }

    public static bool CanAffordUpgrade(string id)
    {
        var c = NextUpgradeCostOf(id);
        return c.HasValue && State.Sparks >= c.Value;
    }

    /// <summary>Happy path: until the_spanker is owned it is the ONLY buyable ACCESSORY —
    /// the others hang locked on the shelf (toys and charms are unaffected). One small,
    /// scripted first purchase so the duo-demo beat has its anchor.</summary>
    public static bool IsAccessoryScriptLocked(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        return b != null
            && b.Category == ChaosBoonCategory.Accessory
            && b.Id != "the_spanker"
            && !IsBoonUnlocked("the_spanker");
    }

    /// <summary>True while this habit's Unlock sits above the player's rank floor (the hub renders
    /// the row locked with <see cref="ChaosRanks.RankLockedTip"/>). Lessons + cost stack on top.
    /// Already-owned habits are never re-locked. (2026-06-12: staggers the catalogue by depth.)</summary>
    public static bool IsBoonRankLocked(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        return b != null && !IsBoonUnlocked(id) && !AtLeast(b.RankFloor);
    }

    /// <summary>Validate + buy level 1: deduct Sparks, record level, auto-activate, persist.</summary>
    public static bool TryUnlockBoon(string id)
    {
        if (IsBoonRankLocked(id)) return false;               // not deep enough yet
        if (ChaosLessons.IsLessonBlocked(id)) return false;   // the lesson comes before the drops
        if (IsAccessoryScriptLocked(id)) return false;        // the spanker comes first (happy path)
        var c = UnlockCostOf(id);
        if (!c.HasValue || State.Sparks < c.Value) return false;
        State.Sparks -= c.Value;
        State.LifetimeBoonLevels ??= new();
        State.LifetimeBoonLevels[id] = 1;
        State.ActiveLifetimeBoons ??= new();
        // A fresh unlock slips into a pocket automatically — but only if one is free.
        var cat = ChaosLifetimeBoons.ById(id)?.Category ?? ChaosBoonCategory.Utility;
        if (HasFreePocket(cat)) State.ActiveLifetimeBoons.Add(id);
        ChaosMetaStore.Save(State);
        return true;
    }

    /// <summary>True while this boon's NEXT purchase is its capstone (final) level and the
    /// player is short of Devoted. The hub renders the deepen row locked with
    /// <see cref="ChaosRanks.CapstoneLockedTip"/>.</summary>
    public static bool IsCapstonePurchaseRankLocked(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        if (b == null) return false;
        int lvl = BoonLevel(id);
        return lvl >= 1 && lvl < b.MaxLevel && lvl + 1 >= b.MaxLevel && !AtLeast(ChaosRank.Devoted);
    }

    /// <summary>Validate + buy the next level: deduct Sparks, bump level (capped at max), persist.
    /// The capstone (final) level additionally needs the Devoted rank.</summary>
    public static bool TryUpgradeBoon(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        var c = NextUpgradeCostOf(id);
        if (b == null || !c.HasValue || State.Sparks < c.Value) return false;
        if (BoonLevel(id) + 1 >= b.MaxLevel && !AtLeast(ChaosRank.Devoted)) return false;
        State.Sparks -= c.Value;
        State.LifetimeBoonLevels[id] = Math.Min(BoonLevel(id) + 1, b.MaxLevel);
        ChaosMetaStore.Save(State);
        return true;
    }

    /// <summary>Equip/unequip a lifetime boon. Equipping fails (returns false) when the boon is
    /// locked or its category's pockets are full. Persists on change.</summary>
    public static bool SetBoonActive(string id, bool active)
    {
        if (active)
        {
            if (!IsBoonUnlocked(id)) return false;
            var cat = ChaosLifetimeBoons.ById(id)?.Category ?? ChaosBoonCategory.Utility;
            if (!IsBoonActive(id) && !HasFreePocket(cat)) return false;   // pockets full
        }
        State.ActiveLifetimeBoons ??= new();
        bool changed = active ? State.ActiveLifetimeBoons.Add(id) : State.ActiveLifetimeBoons.Remove(id);
        if (changed) ChaosMetaStore.Save(State);
        return true;
    }

    /// <summary>Apply every active+unlocked lifetime boon (at its current level) to the run state.</summary>
    public static void ApplyLifetimeBoons(ChaosRunState run)
    {
        if (run == null || State.ActiveLifetimeBoons == null) return;
        foreach (var id in State.ActiveLifetimeBoons)
        {
            int lvl = BoonLevel(id);
            var b = ChaosLifetimeBoons.ById(id);
            if (b != null && lvl >= 1)
            {
                b.Apply(run, b.ValueAt(lvl));
                if (lvl >= b.MaxLevel) run.MaxedBoons.Add(b.Id);   // capstone effects key off this
            }
        }
    }

    /// <summary>
    /// Bank Sparks + update lifetime stats at the end of a completed run, then persist.
    /// Formula (2026-06-12 economy rework, Hades pacing — full collection ≈ 100 descents):
    /// <c>round((1.5·√score + 35·diff·min(1, durMin/3)) * SparkGainMult)</c>.
    /// The square root compresses the late-game multiplier explosion (×10-20 score over a
    /// fresh save → ×3-4 drops) and self-normalizes duration (double-length run ≈ ×1.4).
    /// Difficulty is NOT re-applied to the score part — DifficultyMult already multiplies
    /// every pop inside TotalMult, so the old <c>score/100·diff</c> double-dipped
    /// (Inescapable paid ~×4.8 Gentle). The completion bonus keeps the linear diff scalar
    /// and scales down below 3 minutes so 60-second runs can't farm the flat floor.
    /// Returns the Sparks banked so the recap card can show the haul.
    /// </summary>
    public static int AwardRunRewards(ChaosRunState run)
    {
        if (run == null) return 0;
        return AwardRunRewards(new ChaosRunRewardInput(
            RunDurationSec: run.RunDurationSec,
            DifficultyMult: run.Config.DifficultyMult,
            SparkGainMult: run.Config.SparkGainMult,
            Score: run.Score,
            TrickleDrops: run.TrickleDrops,
            DripFeedMaxed: run.MaxedBoons.Contains("drip_feed"),
            BestCombo: run.BestCombo,
            Defused: run.Defused,
            ElapsedSec: run.ElapsedSec));
    }

    /// <summary>THE SHOT (crafted, repeatable): +4% drops payout per shot in the system,
    /// stacking to +40% at the 10-shot saturation cap. 1.0 with none crafted.</summary>
    public static double ShotSparkMult(ChaosMetaState state)
    {
        int shots = state.CraftedItems.TryGetValue("the_shot", out var n) ? n : 0;
        return 1.0 + 0.04 * Math.Min(10, shots);
    }

    /// <summary>Plain-value award input so run brains that are NOT a ChaosRunState (the DtRH
    /// browser game reports its runs over the bridge) share the exact same formula/banking.</summary>
    public readonly record struct ChaosRunRewardInput(
        double RunDurationSec, double DifficultyMult, double SparkGainMult,
        double Score, double TrickleDrops, bool DripFeedMaxed,
        int BestCombo, int Defused, double ElapsedSec);

    public static int AwardRunRewards(in ChaosRunRewardInput run)
    {
        const double COMPLETION_BONUS_BASE = 35.0;   // the predictable per-descent Spark floor
        const double SCORE_SQRT_SCALE = 1.5;
        const double FULL_BONUS_MINUTES = 3.0;

        double durationMin = Math.Max(0, run.RunDurationSec) / 60.0;
        double completionBonus = COMPLETION_BONUS_BASE * run.DifficultyMult
                                 * Math.Min(1.0, durationMin / FULL_BONUS_MINUTES);
        double scorePart = SCORE_SQRT_SCALE * Math.Sqrt(Math.Max(0, run.Score));
        int sparks = (int)Math.Round((scorePart + completionBonus) * run.SparkGainMult);

        // Drip Feed: the per-pop trickle gathered during the run lands here, and the
        // capstone tips 10% extra on the whole haul.
        sparks += (int)Math.Max(0, run.TrickleDrops);
        if (run.DripFeedMaxed) sparks = (int)Math.Round(sparks * 1.10);

        // THE SHOT (crafted, repeatable): a standing drip on the whole haul.
        sparks = (int)Math.Round(sparks * ShotSparkMult(State));

        // One-time cold-start kindness ("first fall"): +25 the first time RunsCompleted
        // goes 0→1 (guarded so it only ever applies once). Displayed on the recap card.
        if (State.RunsCompleted == 0) sparks += FIRST_FALL_BONUS;

        State.Sparks += Math.Max(0, sparks);
        State.RunsCompleted += 1;
        State.BestScore = Math.Max(State.BestScore, (long)run.Score);
        State.BestCombo = Math.Max(State.BestCombo, run.BestCombo);
        State.TotalDefused += run.Defused;
        State.TotalRunSeconds += Math.Max(0, run.ElapsedSec);
        ChaosMetaStore.Save(State);
        return Math.Max(0, sparks);
    }

    // ============================ next goal (recap signpost) ============================

    /// <summary>One drops-purchasable step toward the collection: id, display name, the ✦
    /// price of its next step, and the lesson still walling it off (null = buyable).</summary>
    public readonly record struct ChaosNextGoal(string Id, string Name, int Cost, string? LessonId)
    {
        public bool Affordable => LessonId == null && Cost <= State.Sparks;
    }

    /// <summary>
    /// The single "what to chase next" for the recap card: every not-yet-finished Toybox
    /// step (habit trainings, boon unlocks, boon level-ups) that the player could ever
    /// reach right now — rank-locked steps and script-held accessories sit out. Affordable
    /// beats cheap: the cheapest BUYABLE step wins; with nothing buyable, the cheapest
    /// step overall (its lesson attached when that's the wall). Null = collection done.
    /// </summary>
    public static ChaosNextGoal? NextGoal()
    {
        ChaosNextGoal? bestAffordable = null, bestAny = null;
        void Consider(string id, string name, int cost, string? lessonId)
        {
            var g = new ChaosNextGoal(id, name, cost, lessonId);
            if (bestAny == null || cost < bestAny.Value.Cost) bestAny = g;
            if (g.Affordable && (bestAffordable == null || cost < bestAffordable.Value.Cost)) bestAffordable = g;
        }

        foreach (var u in ChaosUpgrades.All)
        {
            if (IsOwned(u.Id) || IsPurchaseRankLocked(u.Id)) continue;
            Consider(u.Id, u.Name, u.Cost,
                ChaosLessons.IsLessonBlocked(u.Id) ? u.Id : null);
        }
        foreach (var b in ChaosLifetimeBoons.All)
        {
            int level = BoonLevel(b.Id);
            if (level <= 0)
            {
                if (IsAccessoryScriptLocked(b.Id)) continue;   // she sells these in her own order
                Consider(b.Id, b.Name, b.UnlockCost,
                    ChaosLessons.IsLessonBlocked(b.Id) ? b.Id : null);
            }
            else if (level < b.MaxLevel && !IsCapstonePurchaseRankLocked(b.Id))
            {
                int cost = NextUpgradeCostOf(b.Id) ?? 0;
                if (cost > 0) Consider(b.Id, b.Name, cost, null);
            }
        }
        return bestAffordable ?? bestAny;
    }
}
