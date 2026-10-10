using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Library > Asset Browser third root, "Content Packs" (WPF MainWindow.Assets.cs:516-622): one node
/// per active installed pack with images / videos leaves, tiles keyed pack:&lt;id&gt;/&lt;name&gt;, and a
/// tick that the store's active pool honours.
/// </summary>
public sealed class AssetBrowserPacksTests
{
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    [Fact]
    public Task ContentPacksNode_ListsActivePacks_AndTicksReachThePool() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var root = Path.Combine(Path.GetTempPath(), "ccp-assetpacks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "images"));
        var packId = "test-pack-" + Guid.NewGuid().ToString("N")[..8];
        var guid = Guid.NewGuid().ToString("N");
        var dir = Path.Combine(root, ".packs", guid);
        Directory.CreateDirectory(Path.Combine(dir, "content"));
        var manifest = new InstalledPackManifest { PackId = packId, PackGuid = guid, PackName = "Test Pack" };
        foreach (var (name, type, bytes) in new[]
                 {
                     ("a.png", "image", Convert.FromBase64String(OnePixelPng)),
                     ("b.png", "image", Convert.FromBase64String(OnePixelPng)),
                     ("c.mp4", "video", new byte[] { 0, 1 }),
                 })
        {
            var obf = PackEncryptionService.GenerateObfuscatedFilename() + ".enc";
            File.WriteAllBytes(Path.Combine(dir, "content", obf), PackEncryptionService.Encrypt(bytes));
            manifest.Files.Add(new PackFileEntry { OriginalName = name, ObfuscatedName = obf, FileType = type, Extension = Path.GetExtension(name) });
        }
        PackEncryptionService.SaveEncryptedManifest(JsonConvert.SerializeObject(manifest), Path.Combine(dir, ".manifest.enc"));

        var s = CoreSettings.Current;
        var store = new ContentPackStore(() => root, () => CoreSettings.Current, () => { });
        var view = new AssetsTabView { AssetsRootOverride = root, PackStoreOverride = store };
        var host = new Window { Width = 1500, Height = 900, Content = view };
        try
        {
            host.Show();
            view.RefreshAssetBrowser();
            Dispatcher.UIThread.RunJobs();

            var packs = view.Browser.Folders.Last();
            Assert.True(packs.IsPackFolder);
            Assert.Equal(3, packs.FileCount);
            var pack = Assert.Single(packs.Children);
            Assert.Equal("Test Pack", pack.Name);
            Assert.Equal(new[] { "images", "videos" }, pack.Children.Select(c => c.Name));
            Assert.Equal("2 images, 1 videos active", view.Browser.CountsText);

            var images = pack.Children[0];
            view.SelectFolder(images);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "a.png", "b.png" }, view.Browser.Thumbnails.Select(t => t.Name));
            var tile = view.Browser.Thumbnails[0];
            Assert.True(tile.IsPackFile);
            Assert.Equal($"pack:{packId}/a.png", tile.RelativePath);
            Assert.False(tile.IsVideo);

            // Untick a.png: the selection key lands in DisabledAssetPaths and the pool drops it.
            tile.IsChecked = false;
            view.UpdateFileCheckState(tile);
            Assert.Contains($"pack:{packId}/a.png", s.DisabledAssetPaths);
            Assert.Equal("b.png", Assert.Single(store.GetAllActivePackImages(), p => p.PackId == packId).File.OriginalName);
            Assert.Equal(1, images.CheckedFileCount);
            Assert.Equal("1 images, 1 videos active", view.Browser.CountsText);

            // Whole-folder tick back on.
            view.SetFolderChecked(images, true);
            Assert.DoesNotContain($"pack:{packId}/a.png", s.DisabledAssetPaths);
            Assert.Equal(2, store.GetAllActivePackImages().Count(p => p.PackId == packId));

            // A deactivated pack leaves the tree.
            store.DeactivatePack(packId);
            view.RefreshAssetBrowser();
            Assert.DoesNotContain(view.Browser.Folders, f => f.IsPackFolder);
        }
        finally
        {
            host.Close();
            s.DisabledAssetPaths.RemoveWhere(k => k.StartsWith($"pack:{packId}/", StringComparison.Ordinal));
            s.InstalledPackIds.Remove(packId);
            s.ActivePackIds.Remove(packId);
            s.PackGuidMap.Remove(packId);
            try { Directory.Delete(root, true); } catch { }
        }
        return Task.CompletedTask;
    });

    private static void EnsureAvalonia()
    {
        if (Application.Current is not null) return;
        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }
}
