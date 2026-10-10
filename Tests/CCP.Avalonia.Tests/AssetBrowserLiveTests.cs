using System;
using System.Diagnostics;
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
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Library > Asset Browser on a real folder (owner bug 2026-10-09: the live page showed sample
/// tiles spiral_01.png / loop_soft.mp4 and a tree reading "Images (842)" over an empty folder).
/// The tree, counts and grid must come from disk, exactly as WPF 7.1.5's MainWindow.Assets.cs.
/// </summary>
public sealed class AssetBrowserLiveTests
{
    private static readonly string[] SampleNames =
        { "spiral_01.png", "spiral_02.png", "loop_soft.mp4", "drop_03.png", "loading_now.png", "Starter Pack" };

    // A real 1x1 PNG so the async decode has something to produce.
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    [Fact]
    public Task TreeCountsAndTilesComeFromTheFolder() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var root = Path.Combine(Path.GetTempPath(), "ccp-assetbrowser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "images", "sub"));
        Directory.CreateDirectory(Path.Combine(root, "videos"));
        Directory.CreateDirectory(Path.Combine(root, "audio"));
        Directory.CreateDirectory(Path.Combine(root, "wallpapers"));
        File.WriteAllBytes(Path.Combine(root, "images", "a.png"), Convert.FromBase64String(OnePixelPng));
        File.WriteAllBytes(Path.Combine(root, "images", "sub", "b.jpg"), new byte[] { 1, 2, 3 });
        File.WriteAllText(Path.Combine(root, "images", "notes.txt"), "not media");
        File.WriteAllBytes(Path.Combine(root, "videos", "c.mp4"), new byte[] { 0 });
        File.WriteAllBytes(Path.Combine(root, "audio", "x.mp3"), new byte[] { 0 });
        File.WriteAllBytes(Path.Combine(root, "wallpapers", "w.png"), Convert.FromBase64String(OnePixelPng));

        var s = CoreSettings.Current;
        var view = new AssetsTabView { AssetsRootOverride = root };
        var host = new Window { Width = 1500, Height = 900, Content = view };
        try
        {
            // Before any scan: nothing fake on the page.
            Assert.Empty(view.Browser.Thumbnails);
            Assert.Empty(view.Browser.Folders);

            host.Show();
            view.RefreshAssetBrowser();
            Dispatcher.UIThread.RunJobs();

            // Two roots, as WPF: images and videos. audio / wallpapers are not media folders.
            Assert.Equal(new[] { "images", "videos" }, view.Browser.Folders.Select(f => f.Name));
            var images = view.Browser.Folders[0];
            Assert.Equal(1, images.FileCount);                       // a.png (txt ignored)
            Assert.Equal("sub", Assert.Single(images.Children).Name);
            Assert.Equal(1, images.Children[0].FileCount);
            Assert.Equal(1, view.Browser.Folders[1].FileCount);
            Assert.Equal("2 images, 1 videos active", view.Browser.CountsText);
            Assert.Equal("2 images, 1 videos active", view.FindControl<TextBlock>("TxtAssetCounts")!.Text);
            Assert.True(view.Browser.ShowEmpty);

            // Open images: one real tile, decoded off the UI thread.
            view.SelectFolder(images);
            Dispatcher.UIThread.RunJobs();
            var tile = Assert.Single(view.Browser.Thumbnails);
            Assert.Equal("a.png", tile.Name);
            Assert.Equal("images/a.png", tile.RelativePath);
            Assert.True(tile.IsChecked);
            Assert.False(view.Browser.ShowEmpty);
            var sw = Stopwatch.StartNew();
            while (tile.Thumbnail == null && sw.ElapsedMilliseconds < 5000)
            {
                Dispatcher.UIThread.RunJobs();
                System.Threading.Thread.Sleep(20);
            }
            Assert.NotNull(tile.Thumbnail);

            // Untick it: the blacklist carries the key and the header follows.
            tile.IsChecked = false;
            view.UpdateFileCheckState(tile);
            Assert.Contains("images/a.png", s.DisabledAssetPaths);
            Assert.Equal("1 images, 1 videos active", view.Browser.CountsText);
            view.SetFolderChecked(images, true);
            Assert.DoesNotContain("images/a.png", s.DisabledAssetPaths);
            Assert.True(tile.IsChecked);

            // Videos: the clapper tile, no decode.
            view.SelectFolder(view.Browser.Folders[1]);
            Assert.True(Assert.Single(view.Browser.Thumbnails).IsVideo);

            // Presets: the default "All Assets" row is there and counts the folder.
            var def = s.AssetPresets.First(p => p.IsDefault);
            Assert.Equal(2, def.EnabledImageCount);
            Assert.Equal(1, def.EnabledVideoCount);

            // Never a sample name anywhere.
            var names = view.Browser.Thumbnails.Select(t => t.Name)
                .Concat(Flatten(view.Browser.Folders).Select(f => f.Name)).ToList();
            foreach (var sample in SampleNames) Assert.DoesNotContain(sample, names);
            Assert.Empty(view.Browser.Packs);
        }
        finally
        {
            host.Close();
            s.DisabledAssetPaths.Remove("images/a.png");
            try { Directory.Delete(root, true); } catch { }
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EmptyFolderShowsTheEmptyLine() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var root = Path.Combine(Path.GetTempPath(), "ccp-assetbrowser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "images"));
        var view = new AssetsTabView { AssetsRootOverride = root };
        var host = new Window { Width = 1500, Height = 900, Content = view };
        try
        {
            host.Show();
            view.RefreshAssetBrowser();
            Dispatcher.UIThread.RunJobs();
            var images = Assert.Single(view.Browser.Folders);
            Assert.Equal(0, images.FileCount);
            Assert.Equal("0 images, 0 videos active", view.Browser.CountsText);
            view.SelectFolder(images);
            Assert.Empty(view.Browser.Thumbnails);
            Assert.True(view.Browser.ShowEmpty);
            Assert.False(string.IsNullOrWhiteSpace(view.Browser.EmptyText));
        }
        finally
        {
            host.Close();
            try { Directory.Delete(root, true); } catch { }
        }
        return Task.CompletedTask;
    });

    private static System.Collections.Generic.IEnumerable<AssetTreeItem> Flatten(System.Collections.Generic.IEnumerable<AssetTreeItem> items)
        => items.SelectMany(i => new[] { i }.Concat(Flatten(i.Children)));

    private static void EnsureAvalonia()
    {
        if (Application.Current is not null) return;
        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }
}
