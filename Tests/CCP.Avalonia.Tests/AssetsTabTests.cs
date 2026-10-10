using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.Assets.cs (asset browser + presets) and MainWindow.AssetsFx.cs on the
/// Avalonia Assets tab, entered through the shell's Library door like a user.</summary>
public sealed class AssetsTabTests
{
    [Fact]
    public Task DoorBuildsTreeAndSpaceOnAFolderCheckboxDisablesItsFiles() => Run(async (shell, tab, s) =>
    {
        var images = tab.AssetTree.Single(n => n.Name == "images");
        // The local roots lead; a Content Packs node follows them only when packs are installed.
        Assert.Equal(new[] { "images", "videos" }, tab.AssetTree.Select(n => n.Name).Take(2));
        Assert.Equal(2, images.FileCount);
        Assert.Equal(Loc.GetF("label_0_images_1_videos_active", 3, 2), tab.TxtAssetCounts.Text);

        var cb = await Realized(shell, () => tab.AssetTreeView.GetVisualDescendants().OfType<CheckBox>()
            .FirstOrDefault(c => c.DataContext == images));
        PressSpace(shell, cb);

        Assert.Contains("images/a.png", s.DisabledAssetPaths);
        Assert.Contains("images/sub/c.png", s.DisabledAssetPaths);
        Assert.Contains("images", s.DisabledAssetFolders);
        Assert.False(images.Children.Single().IsChecked);
        Assert.Equal(Loc.GetF("label_0_images_1_videos_active", 0, 2), tab.TxtAssetCounts.Text);
    });

    [Fact]
    public Task SelectingAFolderShowsDecodedThumbnailsAndAThumbnailToggleUnchecksTheFolder() => Run(async (shell, tab, s) =>
    {
        var sub = tab.AssetTree.Single(n => n.Name == "images").Children.Single();
        tab.AssetTreeView.SelectedItem = sub;
        Assert.Single(tab.CurrentFolderFiles);
        await tab.ThumbnailLoads;
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(tab.CurrentFolderFiles[0].Thumbnail);

        var cb = await Realized(shell, () => tab.ThumbnailsItemsControl.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault());
        PressSpace(shell, cb);
        Assert.Contains("images/sub/c.png", s.DisabledAssetPaths);
        Assert.False(sub.IsChecked);

        tab.BtnSelectAllAssets.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.DoesNotContain("images/sub/c.png", s.DisabledAssetPaths);
        Assert.True(tab.CurrentFolderFiles[0].IsChecked);
    });

    [Fact]
    public Task PresetsSaveSwitchRefuseDefaultAndDelete() => Run(async (shell, tab, s) =>
    {
        var told = new List<string>();
        tab.AskText = (_, _, def) => Task.FromResult<string?>("Mine");
        tab.Inform = (title, _) => { told.Add(title); return Task.CompletedTask; };
        tab.Confirm = (_, _) => Task.FromResult(true);
        var resets = 0;
        var oldReset = AssetPresetService.OnlineChannelsReset;
        AssetPresetService.OnlineChannelsReset = () => resets++;
        try
        {
            // Everything off, saved as "Mine".
            tab.SelectFolder(tab.AssetTree.Single(n => n.Name == "images"));
            tab.BtnDeselectAllAssets.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            tab.BtnSaveAssetPreset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Pump(() => s.AssetPresets.Any(p => p.Name == "Mine"));
            var mine = s.AssetPresets.Single(p => p.Name == "Mine");
            Assert.Contains("images/a.png", mine.DisabledAssetPaths);
            Assert.Same(mine, tab.CmbAssetPresets.SelectedItem);
            mine.OnlineNiches = new List<string> { "niche-from-preset" };

            // The combo switches the live selection, and the online half re-deals the channels.
            tab.CmbAssetPresets.SelectedItem = s.AssetPresets.Single(p => p.IsDefault);
            Assert.Empty(s.DisabledAssetPaths);
            tab.CmbAssetPresets.SelectedItem = mine;
            Assert.Contains("images/a.png", s.DisabledAssetPaths);
            Assert.Equal(1, resets);

            // The default row refuses Update and Delete.
            tab.CmbAssetPresets.SelectedItem = s.AssetPresets.Single(p => p.IsDefault);
            tab.BtnUpdateAssetPreset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            tab.BtnDeleteAssetPreset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Pump(() => told.Count >= 3);
            Assert.Equal(new[] { Loc.Get("title_preset_saved"), Loc.Get("title_cannot_update_default"), Loc.Get("title_cannot_delete_default") }, told);

            tab.CmbAssetPresets.SelectedItem = mine;
            tab.BtnDeleteAssetPreset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Pump(() => s.AssetPresets.All(p => p.Name != "Mine"));
            Assert.Equal(s.AssetPresets.Single(p => p.IsDefault).Id, s.CurrentAssetPresetId);
        }
        finally { AssetPresetService.OnlineChannelsReset = oldReset; }
    });

    [Fact]
    public Task HoveringATreeRowNudgesIt() => Run(async (shell, tab, s) =>
    {
        var cb = await Realized(shell, () => tab.AssetTreeView.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault());
        var row = (Control)cb.GetVisualParent()!;
        var on = cb.TranslatePoint(new Point(cb.Bounds.Width / 2, cb.Bounds.Height / 2), shell)!.Value;
        void Hover(Point p)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();   // hit testing reads the last rendered frame
            shell.MouseMove(p, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        // Motion on: a 130 ms slide on X toward 3 px (WPF AssetTreeRowNudgeMs / Px).
        Hover(on);
        var slide = Assert.IsType<TranslateTransform>(row.RenderTransform);
        var t = Assert.IsType<DoubleTransition>(Assert.Single(slide.Transitions!));
        Assert.Equal(TranslateTransform.XProperty, t.Property);
        Assert.Equal(TimeSpan.FromMilliseconds(130), t.Duration);
        Assert.InRange(slide.X, 0.0001, 3.0);
        Hover(new Point(1, 1));

        // Motion off: the same nudge lands at once, and leaving puts it back.
        var old = s.MotionLevel;
        s.MotionLevel = MotionLevel.Off;
        try
        {
            Hover(on);
            Assert.Null(slide.Transitions);
            Assert.Equal(3.0, slide.X);
            Hover(new Point(1, 1));
            Assert.Equal(0.0, slide.X);
        }
        finally { s.MotionLevel = old; }
    });

    [Fact]
    public Task MediaLogPulsesOnEntryWhenTheLogGrewAndStopsWhenTheTabHides() => Run(async (shell, tab, s) =>
    {
        var old = global::ConditioningControlPanel.Avalonia.App.MediaHistory;
        var dir = Directory.CreateTempSubdirectory("ccp-assets-log-").FullName;
        var history = new MediaHistoryService(Path.Combine(dir, "media_history.json"));
        global::ConditioningControlPanel.Avalonia.App.MediaHistory = history;
        try
        {
            shell.ShowTab("settings");
            shell.ShowTab("assets");
            Assert.False(tab.MediaLogPulsing);                  // nothing logged since last opened

            history.RecordImages(new[] { "/x/a.png" });
            shell.ShowTab("settings");
            shell.ShowTab("assets");
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.MediaLogPulsing);

            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            Assert.False(tab.MediaLogPulsing);
            Assert.Equal(1.0, tab.BtnMediaLog.Opacity);
        }
        finally
        {
            global::ConditioningControlPanel.Avalonia.App.MediaHistory = old;
            history.Dispose();
            try { Directory.Delete(dir, true); } catch { }
        }
        await Task.CompletedTask;
    });

    [Fact]
    public Task UntickingVideosDropsTheDealtVideoAndBubbleQueues() => Run(async (shell, tab, s) =>
    {
        var overlay = global::ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay.Instance;
        var bubbles = BubbleCountHost.Instance;
        var (oldVideo, oldBubbles) = (overlay.Scheduler, bubbles.Scheduler);
        overlay.Scheduler = new MandatoryVideoScheduler(overlay);
        bubbles.Scheduler = new BubbleCountScheduler(bubbles);
        try
        {
            // Both deal a two-clip queue and play one; one clip is left queued.
            Assert.NotNull(overlay.Scheduler.PickNext());
            Assert.NotNull(bubbles.Scheduler.PickNext());

            var videos = tab.AssetTree.Single(n => n.Name == "videos");
            var cb = await Realized(shell, () => tab.AssetTreeView.GetVisualDescendants().OfType<CheckBox>()
                .FirstOrDefault(c => c.DataContext == videos));
            PressSpace(shell, cb);
            Assert.Contains("videos/v.mp4", s.DisabledAssetPaths);

            // WPF #130: the very next pick honours the untick instead of draining the old queue.
            Assert.Null(overlay.Scheduler.PickNext());
            Assert.Null(bubbles.Scheduler.PickNext());
        }
        finally
        {
            overlay.Scheduler = oldVideo;
            bubbles.Scheduler = oldBubbles;
        }
    });

    [Fact]
    public Task RefreshKeepsTheOpenFolderAndItsThumbnails() => Run(async (shell, tab, s) =>
    {
        tab.Inform = (_, _) => Task.CompletedTask;
        var sub = tab.AssetTree.Single(n => n.Name == "images").Children.Single();
        tab.AssetTreeView.SelectedItem = sub;
        tab.BtnRefreshAssets.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Single(tab.CurrentFolderFiles);
        Assert.Same(tab.AssetTreeView.SelectedItem, tab.AssetTree.Single(n => n.Name == "images").Children.Single());
        Assert.False(tab.TxtThumbnailsEmpty.IsVisible);

        tab.BtnDeselectAllAssets.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Contains("images/sub/c.png", s.DisabledAssetPaths);
        Assert.DoesNotContain("images/a.png", s.DisabledAssetPaths);
        await tab.ThumbnailLoads;
    });

    // ---- harness ----------------------------------------------------------------------

    private static void PressSpace(Window shell, Control target)
    {
        target.Focus();
        shell.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        shell.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<T> Realized<T>(Window shell, Func<T?> probe) where T : class
    {
        for (var i = 0; i < 50; i++)
        {
            Dispatcher.UIThread.RunJobs();
            shell.UpdateLayout();
            if (probe() is { } hit) return hit;
            await Task.Yield();
        }
        throw new InvalidOperationException("control never realized");
    }

    private static async Task Pump(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
        Assert.True(done());
    }

    private static void Png(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bmp = new SkiaSharp.SKBitmap(8, 8);
        bmp.Erase(SkiaSharp.SKColors.HotPink);
        using var data = bmp.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private static Task Run(Func<MainShellWindow, AssetsTabView, AppSettings, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var root = Directory.CreateTempSubdirectory("ccp-assets-tab-").FullName;
        var assets = Path.Combine(root, "assets");
        Png(Path.Combine(assets, "images", "a.png"));
        Png(Path.Combine(assets, "images", "b.png"));
        Png(Path.Combine(assets, "images", "sub", "c.png"));
        Directory.CreateDirectory(Path.Combine(assets, "videos"));
        File.WriteAllBytes(Path.Combine(assets, "videos", "v.mp4"), new byte[16]);
        File.WriteAllBytes(Path.Combine(assets, "videos", "w.mp4"), new byte[16]);
        var provider = ConditioningControlPanel.CorePaths.EffectiveAssetsProvider;
        var oldSettings = ConditioningControlPanel.CoreSettings.ServiceProvider;
        var svc = new SettingsService();
        ConditioningControlPanel.CoreSettings.ServiceProvider = () => svc;
        ConditioningControlPanel.CorePaths.EffectiveAssetsProvider = () => assets;
        svc.Current.DisabledAssetPaths.Clear();
        svc.Current.DisabledAssetFolders.Clear();
        svc.Current.AssetPresets.Clear();
        svc.Current.CurrentAssetPresetId = null;
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            shell.ShowTab("assets");   // the Library door (7.1.5 nav: rail > Library > Assets)
            Dispatcher.UIThread.RunJobs();
            var tab = shell.Named<AssetsTabView>("AssetsTab")!;
            Assert.True(tab.IsVisible);
            await body(shell, tab, svc.Current);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            shell.Close();
            svc.SealForReset();
            ConditioningControlPanel.CoreSettings.ServiceProvider = oldSettings;
            ConditioningControlPanel.CorePaths.EffectiveAssetsProvider = provider;
            try { Directory.Delete(root, true); } catch { }
        }
    });
}
