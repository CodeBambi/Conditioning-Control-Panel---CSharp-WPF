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
            // Grab-in-the-tube rework: old saves predate ConsumableSlots. Newtonsoft keeps the
            // initializer default (1) when the field is absent, but clamp to >=1 in case an old
            // save serialized a literal 0 before the default was in place.
            if (state.ConsumableSlots < 1) state.ConsumableSlots = 1;
            return state;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ChaosMetaStore.Load failed ({Error}); using fresh meta state", ex.Message);
            return new ChaosMetaState();
        }
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
