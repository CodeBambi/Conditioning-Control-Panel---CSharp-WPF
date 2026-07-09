using System;
using System.IO;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Disk persistence for <see cref="ChaosMetaState"/>. Mirrors SettingsService's
/// approach: Newtonsoft.Json, stored in <see cref="App.UserDataPath"/>, atomic write
/// via a <c>.tmp</c> file + replace. <see cref="Load"/> never throws — a missing or
/// corrupt file yields a fresh default state (logged), so bad meta data can't brick
/// the app.
/// </summary>
public static class ChaosMetaStore
{
    private static string FilePath => Path.Combine(App.UserDataPath, "chaos_meta.json");

    public static ChaosMetaState Load()
    {
        try
        {
            var path = FilePath;
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

            var json = File.ReadAllText(path);
            var state = JsonConvert.DeserializeObject<ChaosMetaState>(json);
            if (state == null)
            {
                App.Logger?.Warning("ChaosMetaStore: chaos_meta.json parsed to null; using fresh meta state");
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
            App.Logger?.Warning("ChaosMetaStore.Load failed ({Error}); using fresh meta state", ex.Message);
            return new ChaosMetaState();
        }
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

    public static void Save(ChaosMetaState state)
    {
        try
        {
            var path = FilePath;
            var tempPath = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var json = JsonConvert.SerializeObject(state, Formatting.Indented);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ChaosMetaStore.Save failed: {Error}", ex.Message);
        }
    }
}
