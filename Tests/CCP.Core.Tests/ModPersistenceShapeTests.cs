using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// Pins the on-disk shape of the mod state before <c>ModService</c> moves to Core: every mod key
/// in settings.json and a full mod.json must survive load -> save through the same serializer calls
/// the WPF head uses. A renamed key, a new converter or a dropped field fails here. The pack.json
/// stamp type is still private to the head, so its round trip lives in the WPF suite
/// (ModActivationGoldenTests) until the move.
/// </summary>
public sealed class ModPersistenceShapeTests
{
    internal static readonly string[] SettingsModKeys =
    {
        "ActiveModId",
        "SubliminalPoolByMod", "AttentionPoolByMod", "LockCardPhrasesByMod", "CustomTriggersByMod", "BouncingTextPoolByMod",
        "VideoLinksByMod", "TubeLayoutOverridesByMod",
        "RemovedDefaultSubliminals", "UserAddedSubliminals", "UserAddedCustomTriggers",
        "sissy_bambi_trigger_migration_done",
        "PersonaVoiceFenceUtc", "PersonaIdentityFenceUtc",
        "hypnotube_links_bambi_sleep", "hypnotube_links_sissy_hypno",
    };

    private static string Fixture(string name, [CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", name);

    [Fact]
    public void SettingsModKeysSurviveSettingsServiceLoadAndSave()
    {
        // CorePaths.UserData is this assembly's temp profile (RoadmapTestProfile), never the real one.
        var settingsPath = Path.Combine(CorePaths.UserData, "settings.json");
        Directory.CreateDirectory(CorePaths.UserData);
        var golden = JObject.Parse(File.ReadAllText(Fixture("settings_mods_premove.json")));
        File.WriteAllText(settingsPath, golden.ToString());
        try
        {
            new SettingsService().SaveImmediate(suppressCloudBackup: true);
            var saved = JObject.Parse(File.ReadAllText(settingsPath));

            Assert.All(SettingsModKeys, key => Assert.True(golden.ContainsKey(key), "fixture lacks " + key));
            Assert.All(SettingsModKeys, key =>
                Assert.True(JToken.DeepEquals(golden[key], saved[key]),
                    $"{key}: expected {golden[key]?.ToString(Formatting.None)}, saved {saved[key]?.ToString(Formatting.None)}"));
        }
        finally
        {
            foreach (var f in Directory.GetFiles(CorePaths.UserData, "settings*")) File.Delete(f);
        }
    }

    [Fact]
    public void FullModManifestSurvivesLoadAndSave()
    {
        // ModService reads mod.json with DeserializeObject<ModManifest> and writes it with
        // SerializeObject(manifest, Formatting.Indented) (export and template). ModArtFraming.IsDefault
        // has no [JsonIgnore], so WPF-written artFraming entries carry "IsDefault"; the fixture keeps it.
        var json = File.ReadAllText(Fixture("mod_manifest_premove.json"));
        var golden = JObject.Parse(json);
        var saved = JObject.Parse(JsonConvert.SerializeObject(
            JsonConvert.DeserializeObject<ModManifest>(json), Formatting.Indented));

        Assert.Equal(golden.Properties().Select(p => p.Name), saved.Properties().Select(p => p.Name));
        Assert.True(JToken.DeepEquals(golden, saved), saved.ToString());
    }
}
