using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// ModService on Linux, in this assembly's temp profile (RoadmapTestProfile sets CCP_USERDATA_DIR).
/// The activation golden was captured from the WPF-era service before the move
/// (Fixtures/mod_activation_golden.json); Core must reproduce it byte for byte with no head seeded.
/// Set CCP_UPDATE_GOLDEN=1 to rewrite it.
/// </summary>
public sealed class ModServiceCoreTests
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

    private static string Fixture(string name, [CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", name);

    private static string Root => CorePaths.UserData;

    /// <summary>Capture deletes under the profile: only ever this assembly's own temp sandbox.</summary>
    /// <summary>ActivateMod queues a debounced save on a pool thread; flush (waits for an in-flight
    /// write) and seal it so no temp file is open when ResetProfile deletes the profile.</summary>
    private static void Quiesce(SettingsService? svc)
    {
        if (svc == null) return;
        svc.SaveImmediate();
        svc.SealForReset();
    }

    private static void ResetProfile()
    {
        Assert.Equal(RoadmapTestProfile.DirectoryPath, Root);
        Directory.CreateDirectory(Root);
        foreach (var f in Directory.GetFiles(Root, "settings*")) File.Delete(f);
        foreach (var dir in new[] { "mods", "builtin_mods", "content" })
            if (Directory.Exists(Path.Combine(Root, dir))) Directory.Delete(Path.Combine(Root, dir), recursive: true);
    }

    [Fact]
    public void BambiToSissyToBambiMatchesTheGolden()
    {
        ResetProfile();
        File.WriteAllText(Path.Combine(Root, "settings.json"), File.ReadAllText(Fixture("settings_mods_premove.json")));
        var oldProvider = CoreSettings.ServiceProvider;
        var runStart = DateTime.UtcNow;
        string actual;
        SettingsService? svc = null;
        try
        {
            svc = new SettingsService();
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
            actual = result.ToString(Formatting.Indented);
        }
        finally
        {
            Quiesce(svc);
            CoreSettings.ServiceProvider = oldProvider;
            ResetProfile();
        }

        var goldenPath = Fixture("mod_activation_golden.json");
        if (Environment.GetEnvironmentVariable("CCP_UPDATE_GOLDEN") == "1")
            File.WriteAllText(goldenPath, actual);
        // Text, not DeepEquals: pool order (dictionary key order) is part of what is pinned.
        Assert.Equal(JToken.Parse(File.ReadAllText(goldenPath)).ToString(Formatting.Indented),
                     JToken.Parse(actual).ToString(Formatting.Indented));
    }

    /// <summary>pack.json (builtin_mods/&lt;id&gt;/pack.json) is written through a private type, so the
    /// round trip reaches it by name.</summary>
    [Fact]
    public void PackStampSurvivesLoadAndSave()
    {
        var stampType = typeof(ModService).GetNestedType("BuiltInModPackStamp", BindingFlags.NonPublic);
        Assert.NotNull(stampType);
        var json = File.ReadAllText(Fixture("pack_stamp_premove.json"));
        var saved = JsonConvert.SerializeObject(JsonConvert.DeserializeObject(json, stampType!), Formatting.Indented);
        Assert.True(JToken.DeepEquals(JToken.Parse(json), JToken.Parse(saved)), saved);
    }

    // ---- install / uninstall from a .ccpmod built here ----

    private static string Manifest(string id, string? minAppVersion = null) =>
        JsonConvert.SerializeObject(new ModManifest
        {
            Id = id, Name = "Test " + id, Version = "1.0.0", Author = "tests", MinAppVersion = minAppVersion,
        });

    private static string BuildCcpmod(string dir, string manifest, params string[] extraEntries)
    {
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".ccpmod");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var w = new StreamWriter(zip.CreateEntry("mod.json").Open(), Encoding.UTF8)) w.Write(manifest);
        foreach (var name in extraEntries)
            using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write("x");
        return path;
    }

    /// <summary>Runs <paramref name="body"/> with a fresh profile, a scratch dir and the app at 6.10.3.</summary>
    private static void WithModService(Action<ModService, string> body)
    {
        ResetProfile();
        var scratch = Directory.CreateTempSubdirectory("ccp-ccpmod-").FullName;
        var oldVersion = CoreReleaseContent.AppVersionProvider;
        var oldProvider = CoreSettings.ServiceProvider;
        SettingsService? svc = null;
        try
        {
            // Activation writes pools into settings: a throwaway service, not the shared fallback.
            svc = new SettingsService();
            CoreSettings.ServiceProvider = () => svc;
            CoreReleaseContent.AppVersionProvider = () => "6.10.3";
            body(new ModService(), scratch);
        }
        finally
        {
            Quiesce(svc);
            CoreReleaseContent.AppVersionProvider = oldVersion;
            CoreSettings.ServiceProvider = oldProvider;
            Directory.Delete(scratch, recursive: true);
            ResetProfile();
        }
    }

    [Fact]
    public void BackslashEntryNamesLandInFolders()
    {
        // Archives zipped by Windows tools can store "resources\sounds\a.mp3"; on Linux that must still be a folder.
        // Every .ccpmod caller (ModService, ContentPackService, ReleaseContentService, both ModCreatorWindows) uses this.
        var scratch = Directory.CreateTempSubdirectory("ccp-ccpmod-").FullName;
        try
        {
            var dir = Path.Combine(scratch, "out");
            CcpmodArchive.Extract(BuildCcpmod(scratch, Manifest("slash-mod"), @"resources\sounds\a.mp3"), dir);
            Assert.True(File.Exists(Path.Combine(dir, "resources", "sounds", "a.mp3")),
                string.Join(", ", Directory.GetFileSystemEntries(dir, "*", SearchOption.AllDirectories)));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    [Fact]
    public void TraversalEntryIsRejected() => WithModService((mods, scratch) =>
    {
        // The install extracts into a folder directly under the temp dir, so one ".." lands in it.
        var escaped = "ccp-escaped-" + Guid.NewGuid().ToString("N") + ".txt";
        var r = mods.InstallModAsync(BuildCcpmod(scratch, Manifest("evil-mod"), "../" + escaped))
            .GetAwaiter().GetResult();
        Assert.False(r.Success);
        Assert.False(mods.InstalledMods.ContainsKey("evil-mod"));
        Assert.False(Directory.Exists(Path.Combine(Root, "mods", "evil-mod")));
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), escaped)));
    });

    [Fact]
    public void ReservedIdIsRejected() => WithModService((mods, scratch) =>
    {
        var r = mods.InstallModAsync(BuildCcpmod(scratch, Manifest(BuiltInMods.DronificationId)))
            .GetAwaiter().GetResult();
        Assert.False(r.Success);
        Assert.Contains("built-in mod ID", r.ErrorMessage);
        Assert.True(mods.InstalledMods[BuiltInMods.DronificationId].IsBuiltIn);
        Assert.False(Directory.Exists(Path.Combine(Root, "mods", BuiltInMods.DronificationId)));
    });

    [Theory]
    [InlineData("6.10.4", false)]
    [InlineData("7.0", false)]
    [InlineData("6.10.3", true)]
    [InlineData("6.9.9", true)]
    public void MinAppVersionGatesInstall(string minAppVersion, bool installs) => WithModService((mods, scratch) =>
    {
        var r = mods.InstallModAsync(BuildCcpmod(scratch, Manifest("versioned-mod", minAppVersion)))
            .GetAwaiter().GetResult();
        Assert.Equal(installs, r.Success);
        if (!installs) Assert.Contains(minAppVersion, r.ErrorMessage);
        Assert.Equal(installs, Directory.Exists(Path.Combine(Root, "mods", "versioned-mod")));
    });

    [Fact]
    public void UninstallDeletesOnlyItsOwnFolder() => WithModService((mods, scratch) =>
    {
        Assert.True(mods.InstallModAsync(BuildCcpmod(scratch, Manifest("gone-mod"))).GetAwaiter().GetResult().Success);
        Assert.True(mods.InstallModAsync(BuildCcpmod(scratch, Manifest("kept-mod"))).GetAwaiter().GetResult().Success);
        var sentinel = Path.Combine(Root, "mods", "sentinel.txt");
        File.WriteAllText(sentinel, "stay");
        mods.ActivateMod("gone-mod");

        Assert.True(mods.UninstallMod("gone-mod"));

        Assert.False(Directory.Exists(Path.Combine(Root, "mods", "gone-mod")));
        Assert.True(File.Exists(Path.Combine(Root, "mods", "kept-mod", "mod.json")));
        Assert.True(File.Exists(sentinel));
        Assert.Equal(BuiltInMods.CCPDefaultId, mods.ActiveModId);
        Assert.False(mods.UninstallMod(BuiltInMods.BambiSleepId)); // built-ins never uninstall
    });
}
