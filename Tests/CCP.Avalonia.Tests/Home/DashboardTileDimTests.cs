using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests.Home;

/// <summary>
/// Owner, 2026-10-09: "The buttons on dashboard for the features should be dimmed when off."
/// WPF 7.1.5 FeatureCard / SplitFeatureCard: an OFF feature tile rests at 62%, drains to grey and
/// mutes its title to 72%; an ON tile is full colour with its glow. A setting written from anywhere
/// repaints the wall live (HookWallRings), and a tile that never opted in (the Vault) stays lit.
/// </summary>
public sealed class DashboardTileDimTests
{
    [Fact]
    public Task An_off_feature_tile_dims_and_greys_and_comes_back_lit() => Run(shell =>
    {
        var s = CoreSettings.Current;
        var dash = shell.SettingsPage!;
        var flash = dash.CardFlash;
        Assert.True(flash.DimWhenInactive);
        Assert.True(flash.HasGreyTwin, "the flash art has no grey twin");

        s.FlashEnabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(flash.IsActive);
        Assert.Equal(0.62, flash.RestOpacity, 3);
        Assert.True(flash.IsMuted);
        Assert.Equal(0.72, flash.TitleOpacity, 3);
        Assert.Equal(0, flash.GlowOpacity);
        Assert.False(flash.IsBreathing);

        s.FlashEnabled = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(flash.IsActive);
        Assert.Equal(1.0, flash.RestOpacity, 3);
        Assert.False(flash.IsMuted);
        Assert.Equal(1.0, flash.TitleOpacity, 3);

        s.FlashEnabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.62, flash.RestOpacity, 3);
        Assert.True(flash.IsMuted);
        Assert.Equal(0, flash.GlowOpacity);

        // A navigation tile never dims: IsActive means nothing there.
        Assert.False(dash.CardVault.DimWhenInactive);
        Assert.Equal(1.0, dash.CardVault.RestOpacity, 3);
        Assert.False(dash.CardVault.IsMuted);
    });

    [Fact]
    public Task A_split_tile_dims_and_greys_only_its_off_half() => Run(shell =>
    {
        var s = CoreSettings.Current;
        var combo = shell.SettingsPage!.ComboSpiralPink;
        s.SpiralEnabled = false;
        s.PinkFilterEnabled = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((0.62, 1.0), combo.HalfRestOpacity);
        Assert.Equal((true, false), combo.HalfMuted);

        s.SpiralEnabled = true;
        s.PinkFilterEnabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((1.0, 0.62), combo.HalfRestOpacity);
        Assert.Equal((false, true), combo.HalfMuted);
    });

    [Fact]
    public void The_grey_twin_is_grey_and_keeps_alpha()
    {
        // B, G, R, A: a saturated orange at half alpha, and pure blue.
        var px = new byte[] { 0, 128, 255, 128, 255, 0, 0, 255 };
        ArtDesaturate.ToGrey(px);
        Assert.Equal(px[0], px[1]);
        Assert.Equal(px[1], px[2]);
        Assert.Equal(128, px[3]);
        Assert.Equal(px[4], px[5]);
        Assert.Equal(px[5], px[6]);
        Assert.Equal(255, px[7]);
        Assert.Equal(151, px[0]);   // 0.587 * 128 + 0.299 * 255
        Assert.Equal(29, px[4]);    // 0.114 * 255
    }

    private static Task Run(System.Action<MainShellWindow> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var snapshot = Preset.FromSettings(s, "snapshot");
        var shell = new MainShellWindow();
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        try { body(shell); }
        finally
        {
            shell.Close();
            snapshot.ApplyTo(s);
        }
        return Task.CompletedTask;
    });
}
