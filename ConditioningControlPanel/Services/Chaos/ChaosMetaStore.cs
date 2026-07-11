using System;
using System.IO;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Disk persistence for <see cref="ChaosMetaState"/>. Mirrors SettingsService's
/// approach: Newtonsoft.Json, stored in <see cref="App.UserDataPath"/>, atomic write
/// via a <c>.tmp</c> file + replace. <see cref="Load(int)"/> never throws — a missing or
/// corrupt file yields a fresh default state (logged), so bad meta data can't brick
/// the app.
///
/// Save SLOTS (2026-07): the hole keeps three independent local saves,
/// <c>chaos_meta.slot1.json</c> .. <c>chaos_meta.slot3.json</c>. Which one is live is
/// chosen in the pre-descent slot picker and remembered in
/// <see cref="Models.AppSettings.ChaosActiveSlot"/>. The no-arg <see cref="Load()"/> /
/// <see cref="Save(ChaosMetaState)"/> resolve that active slot, so every existing caller
/// stays slot-aware for free. A pre-slots <c>chaos_meta.json</c> is migrated into slot 1
/// on first load (see <see cref="MigrateLegacyFile"/>).
/// </summary>
public static class ChaosMetaStore
{
    public const int SlotCount = 3;

    private static string LegacyPath => Path.Combine(App.UserDataPath, "chaos_meta.json");

    /// <summary>Absolute path to a slot's save file (1-based). Public so the slot picker can
    /// show it and reveal the folder.</summary>
    public static string SlotFilePath(int slot) =>
        Path.Combine(App.UserDataPath, $"chaos_meta.slot{Clamp(slot)}.json");

    /// <summary>The folder every save lives in — shown in the picker ("all saves are local").</summary>
    public static string SaveFolder => App.UserDataPath;

    /// <summary>The currently-selected slot (1-3), read from settings. Falls back to slot 1
    /// before settings have loaded.</summary>
    public static int ActiveSlot => Clamp(App.Settings?.Current?.ChaosActiveSlot ?? 1);

    private static int Clamp(int slot) => slot < 1 || slot > SlotCount ? 1 : slot;

    // ---- load ----

    /// <summary>Load the currently-active slot.</summary>
    public static ChaosMetaState Load() => Load(ActiveSlot);

    /// <summary>Load a specific slot (1-3), running compatibility migrations. Never throws.</summary>
    public static ChaosMetaState Load(int slot)
    {
        slot = Clamp(slot);
        try
        {
            if (slot == 1) MigrateLegacyFile();
            var state = LoadFromPath(SlotFilePath(slot));
            if (state == null)
            {
                App.Logger?.Warning("ChaosMetaStore: slot {Slot} parsed to null; using fresh meta state", slot);
                return new ChaosMetaState();
            }
            state.PurchasedUpgrades ??= new();
            state.DisabledUpgrades ??= new();
            state.BenchPurchases ??= new();
            state.PendingReveals ??= new();
            state.SeenReveals ??= new();
            // Grab-in-the-tube rework: old saves predate ConsumableSlots. Newtonsoft keeps the
            // initializer default (1) when the field is absent, but clamp to >=1 in case an old
            // save serialized a literal 0 before the default was in place.
            if (state.ConsumableSlots < 1) state.ConsumableSlots = 1;
            MigrateToV3(state);
            return state;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ChaosMetaStore.Load(slot {Slot}) failed ({Error}); using fresh meta state", slot, ex.Message);
            return new ChaosMetaState();
        }
    }

    /// <summary>Read a slot's headline numbers for the picker WITHOUT running migrations or
    /// touching the live state. A missing file reports <see cref="SlotSummary.Exists"/> = false;
    /// a corrupt one reports Exists = true with zeroed stats so it can still be deleted.</summary>
    public static SlotSummary ReadSummary(int slot)
    {
        slot = Clamp(slot);
        var path = SlotFilePath(slot);
        var summary = new SlotSummary { Slot = slot };
        try
        {
            // A pre-slots save counts as slot 1's content until the real migration runs on load.
            if (slot == 1 && !File.Exists(path) && File.Exists(LegacyPath)) path = LegacyPath;
            if (!File.Exists(path)) return summary;   // Exists = false

            summary.Exists = true;
            summary.LastPlayedUtc = File.GetLastWriteTimeUtc(path);
            var state = JsonConvert.DeserializeObject<ChaosMetaState>(File.ReadAllText(path));
            if (state != null)
            {
                summary.Sparks = state.Sparks;
                summary.Gold = state.Gold;
                summary.RunsCompleted = state.RunsCompleted;
                summary.BestScore = state.BestScore;
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ChaosMetaStore.ReadSummary(slot {Slot}): {E}", slot, ex.Message);
        }
        return summary;
    }

    /// <summary>Wipe a slot back to empty (deletes its file + any stray temp). Returns true if
    /// something was removed. Used by the picker's per-slot Delete.</summary>
    public static bool Delete(int slot)
    {
        slot = Clamp(slot);
        bool removed = false;
        try
        {
            var path = SlotFilePath(slot);
            if (File.Exists(path)) { File.Delete(path); removed = true; }
            var tmp = path + ".tmp";
            if (File.Exists(tmp)) { File.Delete(tmp); removed = true; }
            // Slot 1 shares identity with the pre-slots file — clear that too so it can't reappear.
            if (slot == 1 && File.Exists(LegacyPath)) { File.Delete(LegacyPath); removed = true; }
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ChaosMetaStore.Delete(slot {Slot}) failed: {E}", slot, ex.Message);
        }
        return removed;
    }

    /// <summary>One-time move of a pre-slots <c>chaos_meta.json</c> into slot 1. Idempotent:
    /// once slot 1 exists, the legacy file is left alone (Delete cleans it up).</summary>
    private static void MigrateLegacyFile()
    {
        try
        {
            var slot1 = SlotFilePath(1);
            if (File.Exists(slot1) || !File.Exists(LegacyPath)) return;
            File.Copy(LegacyPath, slot1, overwrite: false);
            App.Logger?.Information("ChaosMetaStore: migrated legacy chaos_meta.json into slot 1");
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ChaosMetaStore: legacy->slot1 migration failed: {E}", ex.Message);
        }
    }

    /// <summary>Read+parse a save file, recovering from an interrupted atomic write. Returns
    /// null on a fresh/missing file so the caller can substitute a default state.</summary>
    private static ChaosMetaState? LoadFromPath(string path)
    {
        var tempPath = path + ".tmp";

        // Recover from an interrupted atomic write (temp present, main missing).
        if (File.Exists(tempPath) && !File.Exists(path))
        {
            try { File.Move(tempPath, path); } catch { }
        }
        else if (File.Exists(tempPath))
        {
            try { File.Delete(tempPath); } catch { }
        }

        if (!File.Exists(path)) return new ChaosMetaState();
        return JsonConvert.DeserializeObject<ChaosMetaState>(File.ReadAllText(path));
    }

    /// <summary>
    /// v3 - the gold cutover (2026-07): dials are bought with gold, pockets are retired
    /// (grab-in-the-tube holds items via consumable HANDS instead). Owned bench pockets
    /// refund at list price - a gift-covered first pocket refunds too (generosity is
    /// cheaper than special-casing), and the gift itself re-arms so "her one gift" beat
    /// can land on the first DIAL instead. Retired reveal ids are pruned so no orphaned
    /// flash sits pending forever. Idempotent via the SchemaVersion stamp; the migrated
    /// state persists on the next regular save.
    /// </summary>
    private static void MigrateToV3(ChaosMetaState state)
    {
        if (state.SchemaVersion >= 3) return;

        var pocketRefunds = new (string Id, int Gold)[]
        {
            ("toy_pocket_1", 50), ("accessory_pocket_1", 150),
            ("toy_pocket_2", 2000), ("accessory_pocket_2", 2500),
        };
        int refund = 0;
        foreach (var (id, gold) in pocketRefunds)
            if (state.BenchPurchases.Remove(id)) refund += gold;
        if (refund > 0)
        {
            state.Gold += refund;
            if (state.GiftGiven) state.GiftGiven = false;   // the gift rode a pocket; re-arm it for the first dial
            App.Logger?.Information("ChaosMetaStore: v3 migration refunded {Refund} gold for retired pockets", refund);
        }
        state.ToyPockets = 0;
        state.AccessoryPockets = 0;

        foreach (var dead in new[] { "toybox_her_corner", "bench_toy_pocket_2", "bench_acc_pocket_2" })
        {
            state.PendingReveals.Remove(dead);
            state.SeenReveals.Remove(dead);
        }

        // Sparks spent on dials stay spent (dials remain owned) - no drops refund.
        state.SchemaVersion = 3;
    }

    // ---- save ----

    /// <summary>Save to the currently-active slot.</summary>
    public static void Save(ChaosMetaState state) => Save(state, ActiveSlot);

    /// <summary>Save to a specific slot (1-3) via an atomic temp-file replace.</summary>
    public static void Save(ChaosMetaState state, int slot)
    {
        try
        {
            var path = SlotFilePath(slot);
            var tempPath = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var json = JsonConvert.SerializeObject(state, Formatting.Indented);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ChaosMetaStore.Save(slot {Slot}) failed: {Error}", slot, ex.Message);
        }
    }
}

/// <summary>Headline stats for one save slot, read cheaply for the pre-descent picker.</summary>
public sealed class SlotSummary
{
    public int Slot { get; set; }
    /// <summary>False = an empty slot ("New Journey"). True = a real save (or an unreadable file).</summary>
    public bool Exists { get; set; }
    public int Sparks { get; set; }
    public int Gold { get; set; }
    public int RunsCompleted { get; set; }
    public long BestScore { get; set; }
    /// <summary>File last-write time — shown as "last played".</summary>
    public DateTime? LastPlayedUtc { get; set; }
}
