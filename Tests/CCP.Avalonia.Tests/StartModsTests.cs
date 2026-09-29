using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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

    [Fact]
    public void StartModsSeedsCoreModsFromTheSavedActiveModAndMatchesTheGolden()
    {
        var root = CorePaths.UserData;
        Assert.Equal(TestUserDataProfile.Root, root);   // never the real profile
        File.WriteAllText(Path.Combine(root, "settings.json"), File.ReadAllText(CoreFixture("settings_mods_premove.json")));
        var mods = new CoreModsSnapshot();
        var oldSettings = CoreSettings.ServiceProvider;
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
            mods.Dispose();
            foreach (var f in Directory.GetFiles(root, "settings*")) File.Delete(f);
            foreach (var dir in new[] { "mods", "builtin_mods" })
                if (Directory.Exists(Path.Combine(root, dir))) Directory.Delete(Path.Combine(root, dir), recursive: true);
        }
    }
    /// <summary>Mod manager's Activate (WPF ModManagerDialog.xaml.cs:578-582): switches the service and saves settings.ActiveModId.</summary>
    [Fact]
    public async System.Threading.Tasks.Task ModManagerActivatePersistsTheSelectedModToSettings()
    {
        await CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia()
                    .UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var root = CorePaths.UserData;
            Assert.Equal(TestUserDataProfile.Root, root);
            var mods = new CoreModsSnapshot();
            var oldSettings = CoreSettings.ServiceProvider;
            SettingsService? svc = null;
            try
            {
                svc = new SettingsService();
                CoreSettings.ServiceProvider = () => svc;
                AvApp.StartMods();
                var dialog = new global::ConditioningControlPanel.Avalonia.Views.Dialogs.ModManagerDialog();
                var list = dialog.FindControl<global::Avalonia.Controls.ListBox>("ModList")!;
                list.SelectedItem = list.Items.OfType<global::Avalonia.Controls.ListBoxItem>()
                    .First(i => (string?)i.Tag == BuiltInMods.SissyHypnoId);
                dialog.FindControl<global::Avalonia.Controls.Button>("BtnActivate")!
                    .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));

                Assert.Equal(BuiltInMods.SissyHypnoId, AvApp.Mods!.ActiveModId);
                Assert.True(dialog.ModWasChanged);
                svc.SaveImmediate();
                Assert.Equal(BuiltInMods.SissyHypnoId,
                    JObject.Parse(File.ReadAllText(Path.Combine(root, "settings.json")))["ActiveModId"]?.Value<string>());
            }
            finally
            {
                svc?.SaveImmediate();
                svc?.SealForReset();
                CoreSettings.ServiceProvider = oldSettings;
                mods.Dispose();
                foreach (var f in Directory.GetFiles(root, "settings*")) File.Delete(f);
                foreach (var dir in new[] { "mods", "builtin_mods" })
                    if (Directory.Exists(Path.Combine(root, dir))) Directory.Delete(Path.Combine(root, dir), recursive: true);
            }
            return System.Threading.Tasks.Task.CompletedTask;
        });
    }
}
