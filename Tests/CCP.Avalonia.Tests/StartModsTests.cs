using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The desktop lifetime's mod block (App.StartMods) in this assembly's sandbox profile: it seeds
/// CoreMods, reads the active mod from settings, and writes the same mod keys the Core golden pins
/// (Tests/CCP.Core.Tests/Fixtures/mod_activation_golden.json, captured from the WPF-era service).
/// </summary>
public sealed class StartModsTests
{
    private static string CoreFixture(string name) => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "CCP.Core.Tests", "Fixtures", name));

    private static readonly FieldInfo[] Providers = typeof(CoreMods)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.Name.EndsWith("Provider", StringComparison.Ordinal)).ToArray();

    [Fact]
    public void StartModsSeedsCoreModsFromTheSavedActiveModAndMatchesTheGolden()
    {
        var root = CorePaths.UserData;
        Assert.Equal(TestUserDataProfile.Root, root);   // never the real profile
        File.WriteAllText(Path.Combine(root, "settings.json"), File.ReadAllText(CoreFixture("settings_mods_premove.json")));
        var saved = Providers.Select(f => f.GetValue(null)).ToArray();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldLinks = CoreModsHooks.KnownVideoLinksProvider;
        var runStart = DateTime.UtcNow;
        SettingsService? svc = null;
        try
        {
            svc = new SettingsService();
            CoreSettings.ServiceProvider = () => svc;
            Assert.Equal(BuiltInMods.CCPDefaultId, CoreMods.ActiveModId);   // unseeded fallback

            AvApp.StartMods();

            Assert.Equal(BuiltInMods.BambiSleepId, CoreMods.ActiveModId);   // from settings.json
            Assert.Same(AvApp.Mods!.ActiveMod, CoreMods.ActiveModPackage);
            Assert.True(CoreMods.InstalledMods.Count > 1);

            // Forwarded: a switch on the service reaches CoreMods.ModChanged subscribers.
            string? switchedTo = null;
            EventHandler<ModPackage> onChanged = (_, p) => switchedTo = p.Id;
            CoreMods.ModChanged += onChanged;
            try { AvApp.Mods.ActivateMod(BuiltInMods.SissyHypnoId); }
            finally { CoreMods.ModChanged -= onChanged; }
            Assert.Equal(BuiltInMods.SissyHypnoId, switchedTo);
            AvApp.Mods.ActivateMod(BuiltInMods.BambiSleepId);

            // The Core golden's capture, on what this head's launch path wrote.
            var json = JObject.Parse(JsonConvert.SerializeObject(svc.Current, Formatting.Indented));
            var golden = JObject.Parse(File.ReadAllText(CoreFixture("mod_activation_golden.json")));
            foreach (var fence in new[] { "PersonaVoiceFenceUtc", "PersonaIdentityFenceUtc" })
                if (json[fence]?.Type == JTokenType.Date && json[fence]!.Value<DateTime>() >= runStart.AddSeconds(-1))
                    json[fence] = "stamped-during-run";
            foreach (var p in golden.Properties())
                Assert.True(JToken.DeepEquals(p.Value, json[p.Name]), $"{p.Name} differs from the Core golden");
        }
        finally
        {
            // ActivateMod queues a debounced save; a late write would land in a later test's file.
            svc?.SaveImmediate();
            svc?.SealForReset();
            CoreSettings.ServiceProvider = oldSettings;
            CoreModsHooks.KnownVideoLinksProvider = oldLinks;
            for (var i = 0; i < Providers.Length; i++) Providers[i].SetValue(null, saved[i]);
            foreach (var f in Directory.GetFiles(root, "settings*")) File.Delete(f);
            foreach (var dir in new[] { "mods", "builtin_mods" })
                if (Directory.Exists(Path.Combine(root, dir))) Directory.Delete(Path.Combine(root, dir), recursive: true);
        }
    }
}
