using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Core.Content.Tests;

/// <summary>
/// The installed half of WPF ContentPackService on Core (ContentPackStore): discovery from a
/// scratch <c>.packs</c> folder, activation as WPF stores it, per-file opt-outs, and the identity
/// rule (a decrypt is a FRESH temp path; dedupe on the source key, never the path).
/// </summary>
public sealed class ContentPackStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-packstore-" + Guid.NewGuid().ToString("N"));
    private readonly AppSettings _settings = new();
    private int _saves;

    public ContentPackStoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private ContentPackStore NewStore() =>
        new(() => _root, () => _settings, () => _saves++, tempDir: () => Path.Combine(_root, ".temp"));

    /// <summary>Writes an encrypted pack exactly as WPF InstallPackAsync lays it out.</summary>
    internal static (string Guid, Dictionary<string, byte[]> Plain) WritePack(string assetsRoot, string packId, string name,
        params (string File, string Type)[] files)
    {
        var guid = Guid.NewGuid().ToString("N");
        var dir = Path.Combine(assetsRoot, ".packs", guid);
        Directory.CreateDirectory(Path.Combine(dir, "content"));
        var manifest = new InstalledPackManifest { PackId = packId, PackGuid = guid, PackName = name, InstalledDate = DateTime.UtcNow };
        var plain = new Dictionary<string, byte[]>();
        foreach (var (file, type) in files)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(packId + "/" + file);
            var obf = PackEncryptionService.GenerateObfuscatedFilename() + ".enc";
            File.WriteAllBytes(Path.Combine(dir, "content", obf), PackEncryptionService.Encrypt(bytes));
            manifest.Files.Add(new PackFileEntry { OriginalName = file, ObfuscatedName = obf, FileType = type, Extension = Path.GetExtension(file) });
            plain[file] = bytes;
        }
        PackEncryptionService.SaveEncryptedManifest(JsonConvert.SerializeObject(manifest), Path.Combine(dir, ".manifest.enc"));
        return (guid, plain);
    }

    [Fact]
    public void Discovery_RegistersOrphanedPacks_AndActivatesThem()
    {
        var (guid, _) = WritePack(_root, "p1", "Pack One", ("a.png", "image"), ("b.jpg", "image"), ("c.mp4", "video"));
        Directory.CreateDirectory(Path.Combine(_root, ".packs", "no-manifest"));
        var junk = Path.Combine(_root, ".packs", "junk");
        Directory.CreateDirectory(junk);
        File.WriteAllBytes(Path.Combine(junk, ".manifest.enc"), new byte[40]);

        var store = NewStore();

        Assert.Equal(new[] { "p1" }, _settings.InstalledPackIds);
        Assert.Equal(guid, _settings.PackGuidMap["p1"]);
        Assert.Equal(new[] { "p1" }, _settings.ActivePackIds);
        Assert.Equal(1, _saves);
        Assert.Equal(Path.Combine(_root, ".packs"), store.PacksFolder);
        Assert.True(store.IsPackInstalled("p1"));
        Assert.True(store.IsPackActive("p1"));
        Assert.Equal("Pack One", store.GetPackName("p1"));
        Assert.Equal(2, store.GetAllActivePackImages().Count);
        Assert.Equal("c.mp4", Assert.Single(store.GetAllActivePackVideos()).File.OriginalName);

        // A second scan of a known pack does not re-register or save.
        store.Rescan();
        Assert.Equal(1, _saves);
    }

    [Fact]
    public void OptOutsAndDeactivation_ShrinkThePools()
    {
        WritePack(_root, "p1", "One", ("a.png", "image"), ("b.png", "image"));
        WritePack(_root, "p2", "Two", ("z.png", "image"));
        var store = NewStore();
        Assert.Equal(3, store.GetAllActivePackImages().Count);

        _settings.DisabledAssetPaths.Add("pack:p1/a.png");
        Assert.Equal(new[] { "b.png", "z.png" }, store.GetAllActivePackImages().Select(p => p.File.OriginalName).OrderBy(n => n));

        var changed = 0;
        store.PacksChanged += () => changed++;
        store.DeactivatePack("p2");
        Assert.Equal(new[] { "p1" }, store.GetActivePackIds());
        Assert.Equal("b.png", Assert.Single(store.GetAllActivePackImages()).File.OriginalName);
        store.ActivatePack("p2");
        Assert.Equal(2, store.GetAllActivePackImages().Count);
        Assert.Equal(2, changed);

        // A pack whose folder vanished is not active even if settings say so (WPF GetActivePackIds).
        Directory.Delete(Path.Combine(_root, ".packs", _settings.PackGuidMap["p2"]), true);
        Assert.Equal(new[] { "p1" }, store.GetActivePackIds());
    }

    [Fact]
    public void DecryptToTemp_IsAFreshPathEachTime_ButOneSourceIdentity()
    {
        var (_, plain) = WritePack(_root, "p1", "One", ("a.png", "image"), ("b.png", "image"));
        var store = NewStore();
        var a = store.GetPackFiles("p1").Single(f => f.OriginalName == "a.png");
        var b = store.GetPackFiles("p1").Single(f => f.OriginalName == "b.png");

        var t1 = store.GetPackFileTempPath("p1", a)!;
        var t2 = store.GetPackFileTempPath("p1", a)!;
        var t3 = store.GetPackFileTempPath("p1", b)!;
        Assert.NotEqual(t1, t2);                                   // path strings never identify a picture
        Assert.EndsWith(".png", t1);
        Assert.Equal(plain["a.png"], File.ReadAllBytes(t1));
        Assert.Equal(plain["a.png"], File.ReadAllBytes(t2));

        Assert.True(store.TryGetSourceKey(t1, out var k1));
        Assert.True(store.TryGetSourceKey(t2, out var k2));
        Assert.True(store.TryGetSourceKey(t3, out var k3));
        Assert.Equal(k1, k2);
        Assert.NotEqual(k1, k3);
        Assert.Equal(ContentPackStore.SourceKey("p1", a), k1);
        Assert.Equal($"pack:p1/{a.ObfuscatedName}", k1);          // WPF FlashService.PackEntryKey
        Assert.Equal("pack:p1/a.png", ContentPackStore.SelectionKey("p1", a));
        Assert.False(store.TryGetSourceKey(Path.Combine(_root, "elsewhere.png"), out _));

        using (var ms = store.GetPackFileStream("p1", b)!) Assert.Equal(plain["b.png"], ms.ToArray());

        store.DeleteTempFile(t1);
        Assert.False(File.Exists(t1));
        Assert.False(store.TryGetSourceKey(t1, out _));
        store.CleanupTempFiles();
        Assert.False(File.Exists(t2));
        Assert.False(File.Exists(t3));
    }

    [Fact]
    public void Uninstall_DeletesTheFolder_AndForgetsThePack()
    {
        var (guid, _) = WritePack(_root, "p1", "One", ("a.png", "image"));
        var store = NewStore();
        store.UninstallPack("p1");
        Assert.False(Directory.Exists(Path.Combine(_root, ".packs", guid)));
        Assert.Empty(_settings.InstalledPackIds);
        Assert.Empty(_settings.ActivePackIds);
        Assert.False(_settings.PackGuidMap.ContainsKey("p1"));
        Assert.Empty(store.GetAllActivePackImages());
        Assert.Empty(store.InstalledPackIds);
    }

    [Fact]
    public void MissingAssetsRoot_IsQuiet()
    {
        var store = new ContentPackStore(() => Path.Combine(_root, "nope"), () => _settings, () => _saves++);
        Assert.Empty(store.GetActivePackIds());
        Assert.Empty(store.GetAllActivePackImages());
        Assert.Equal(0, _saves);
    }
}
