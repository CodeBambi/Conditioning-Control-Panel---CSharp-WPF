using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The hub draws its real art through ChaosArt (WPF ChaosHubWindow: row icons, loadout tiles,
/// the menu flipbook), and falls back to the glyphs when a folder has none.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class ChaosHubArtTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static void Png(string root, params string[] parts)
    {
        var path = Path.Combine(new[] { root }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var src = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/nav/door_home.png"));
        using var dst = File.Create(path);
        src.CopyTo(dst);
    }

    [Fact]
    public Task TheHubWearsItsArt_AndTheMenuFlipbookStepsOnlyWithMotionOn() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var saved = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.MotionLevel = MotionLevel.Off;
        var root = Directory.CreateTempSubdirectory("ccp-hubart-").FullName;
        ChaosHubWindow? hub = null, bare = null;
        try
        {
            // No art anywhere: glyph squares, no pictures, the gradient panel (as before).
            ChaosArt.ResetForTest();
            ChaosArt.RootsOverride = () => new[] { Path.Combine(root, "none") };
            bare = new ChaosHubWindow();
            Assert.Equal(0, bare.MenuFlipFrames);
            Assert.Null((bare.FindControl<Border>("MenuArtBaseBox")!.Background as ImageBrush)?.Source);
            Assert.DoesNotContain(bare.GetLogicalDescendants().OfType<Image>(), i => i.Source != null && i.Name == null);
            bare.Close();
            bare = null;

            // Art on disk: every upgrade and boon id, the keyhole and four menu frames.
            foreach (var u in ChaosUpgrades.All) Png(root, "upgrades", u.Id + ".png");
            Png(root, "hub", "tile_unknown.png");
            for (int i = 1; i <= 4; i++) Png(root, $"menu_{i}.png");
            ChaosArt.ResetForTest();
            ChaosArt.RootsOverride = () => new[] { root };

            hub = new ChaosHubWindow();
            hub.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, hub.MenuFlipFrames);
            Assert.NotNull((hub.FindControl<Border>("MenuArtBaseBox")!.Background as ImageBrush)?.Source);
            Assert.Equal(0, hub.MenuFrameShown);
            Assert.False(hub.MenuFlipRunning);                       // Motion Off: the idle frame, still
            Assert.Contains(hub.GetLogicalDescendants().OfType<Image>(), i => i.Source != null && i.Name == null);   // row icons

            service.Current.MotionLevel = MotionLevel.Full;
            ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();   // Settings > Performance raises it
            await Task.Delay(30);
            Dispatcher.UIThread.RunJobs();
            Assert.True(hub.MenuFlipRunning);                        // the gate reopened: it steps
            Assert.InRange(hub.FindControl<Border>("MenuArtTopBox")!.Opacity, 0.0, 1.0);

            hub.Close();
            Assert.False(hub.MenuFlipRunning);                       // closing rests it
            hub = null;
        }
        finally
        {
            bare?.Close();
            hub?.Close();
            ChaosArt.ResetForTest();
            CoreSettings.ServiceProvider = saved;
            ChaosMeta.Init();
            try { Directory.Delete(root, true); } catch { /* a decoded file may still be open */ }
        }
    });
}
