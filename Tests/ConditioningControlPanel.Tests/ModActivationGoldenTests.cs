using System;
using System.IO;
using System.Reflection;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Behaviour golden captured from the WPF-era <see cref="ModService"/> before it moves to Core:
/// a settings fixture goes through SettingsService load, Initialize(bambi), ActivateMod(sissy),
/// ActivateMod(bambi), and the resulting mod keys must equal the committed golden. The Core test
/// after the move reuses the same fixture and golden (Tests/CCP.Core.Tests/Fixtures).
///
/// <para>Runs only in TestLocalizationBootstrap.SandboxDir, the fresh profile that module initializer
/// always creates, and refuses to run if CorePaths resolved anywhere else. Set CCP_UPDATE_GOLDEN=1 to rewrite the golden.</para>
/// </summary>
[Collection(ModActivationGoldenCollection.Name)]
public sealed class ModActivationGoldenTests
{
    private static readonly string[] CapturedKeys =
    {
        "ActiveModId",
        "SubliminalPool", "AttentionPool", "LockCardPhrases", "CustomTriggers", "BouncingTextPool",
        "SubliminalPoolByMod", "AttentionPoolByMod", "LockCardPhrasesByMod", "CustomTriggersByMod", "BouncingTextPoolByMod",
        "VideoLinksByMod", "TubeLayoutOverridesByMod",
        "RemovedDefaultSubliminals", "UserAddedSubliminals", "UserAddedCustomTriggers",
        "sissy_bambi_trigger_migration_done",
        "PersonaVoiceFenceUtc", "PersonaIdentityFenceUtc",
        "hypnotube_links_bambi_sleep", "hypnotube_links_sissy_hypno",
    };

    private static string Fixture(string name) =>
        Path.Combine(SourceRoots.RepoRoot, "Tests", "CCP.Core.Tests", "Fixtures", name);

    [Fact]
    public void BambiToSissyToBambiMatchesTheGolden()
    {
        // Capture deletes under the profile: only ever this process's own fresh sandbox.
        Assert.NotNull(TestLocalizationBootstrap.SandboxDir);
        Assert.Equal(TestLocalizationBootstrap.SandboxDir, CorePaths.UserData);

        var actual = Capture(File.ReadAllText(Fixture("settings_mods_premove.json")));
        var goldenPath = Fixture("mod_activation_golden.json");
        if (Environment.GetEnvironmentVariable("CCP_UPDATE_GOLDEN") == "1")
            File.WriteAllText(goldenPath, actual);

        // Text, not DeepEquals: pool order (dictionary key order) is part of what is pinned.
        Assert.Equal(JToken.Parse(File.ReadAllText(goldenPath)).ToString(Formatting.Indented),
                     JToken.Parse(actual).ToString(Formatting.Indented));
    }

    /// <summary>pack.json (builtin_mods/&lt;id&gt;/pack.json) is written by ModService.WritePackStamp through a
    /// private type, so the round trip reaches it by name.</summary>
    [Fact]
    public void PackStampSurvivesLoadAndSave()
    {
        var stampType = typeof(ModService).GetNestedType("BuiltInModPackStamp", BindingFlags.NonPublic);
        Assert.NotNull(stampType);
        var json = File.ReadAllText(Fixture("pack_stamp_premove.json"));
        var saved = JsonConvert.SerializeObject(JsonConvert.DeserializeObject(json, stampType!), Formatting.Indented);

        Assert.True(JToken.DeepEquals(JToken.Parse(json), JToken.Parse(saved)), saved);
    }

    private static string Capture(string settingsJson)
    {
        var root = CorePaths.UserData;
        Directory.CreateDirectory(root);
        foreach (var f in Directory.GetFiles(root, "settings*")) File.Delete(f);
        foreach (var dir in new[] { "mods", "builtin_mods", "content" })
            if (Directory.Exists(Path.Combine(root, dir))) Directory.Delete(Path.Combine(root, dir), recursive: true);
        File.WriteAllText(Path.Combine(root, "settings.json"), settingsJson);

        // ModService.Initialize subscribes App.Settings.CurrentReplaced once CoreSettings has a
        // provider, so App.Settings (private setter) must hold the same service.
        var appSettings = typeof(App).GetProperty(nameof(App.Settings), BindingFlags.Public | BindingFlags.Static)!;
        var oldApp = appSettings.GetValue(null);
        var oldProvider = CoreSettings.ServiceProvider;
        var runStart = DateTime.UtcNow;
        try
        {
            var svc = new SettingsService();
            appSettings.SetValue(null, svc);
            CoreSettings.ServiceProvider = () => svc;

            var mods = new ModService();
            mods.Initialize(svc.Current.ActiveModId);
            mods.ActivateMod(BuiltInMods.SissyHypnoId);
            mods.ActivateMod(BuiltInMods.BambiSleepId);

            // Same call SettingsService.SaveImmediate makes.
            var saved = JObject.Parse(JsonConvert.SerializeObject(svc.Current, Formatting.Indented));
            var result = new JObject();
            foreach (var key in CapturedKeys)
                result[key] = saved[key]?.DeepClone();

            // ActivateMod stamps DateTime.UtcNow into the fences; pin "stamped by this run", not the clock.
            foreach (var fence in new[] { "PersonaVoiceFenceUtc", "PersonaIdentityFenceUtc" })
                if (saved[fence]?.Type == JTokenType.Date && saved[fence]!.Value<DateTime>() >= runStart.AddSeconds(-1))
                    result[fence] = "stamped-during-run";

            return result.ToString(Formatting.Indented);
        }
        finally
        {
            CoreSettings.ServiceProvider = oldProvider;
            appSettings.SetValue(null, oldApp);
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ModActivationGoldenCollection
{
    public const string Name = "ModActivationGolden";
}
