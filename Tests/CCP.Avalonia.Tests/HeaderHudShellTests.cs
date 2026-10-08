using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Nav;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The 7.1.5 header and XP row on the shell (lane H): the SP wallet heads the row, the Premium
/// spark reads the profile bubble's tier rule, the cut chrome (level pill, version tag, mod
/// manager capsule, language pill) is gone from sight, the account chip sits beside the bubble,
/// the LVL readout is a raised chip, the XP track a groove with a tube, the marquee a sunken
/// drum, and the HUD's shades take the section hue. CCP_HEADER_SHOTS=dir saves the proofs.
/// </summary>
public sealed class HeaderHudShellTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static string? ShotDir => Environment.GetEnvironmentVariable("CCP_HEADER_SHOTS") is { Length: > 0 } d ? d : null;

    private static void Shoot(Window w, string file)
    {
        if (ShotDir is not { } dir) return;
        for (int i = 0; i < 3; i++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        using var frame = w.CaptureRenderedFrame();
        if (frame == null) return;
        Directory.CreateDirectory(dir);
        frame.Save(System.IO.Path.Combine(dir, file));
    }

    [Fact]
    public Task HeaderCarriesThe715ChromeAndPaintsDepthInTheSectionHue() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.PlayerLevel = 39;
        s.PlayerXP = 1387;
        s.SkillPoints = 10329;
        s.OfflineMode = true;
        s.OfflineUsername = "header-test";
        s.MotionLevel = MotionLevel.Off;
        var oldPremium = CoreAccount.HasPremiumAccessProvider;
        var oldLab = CoreAccount.HasLabAccessProvider;
        CoreAccount.HasPremiumAccessProvider = null;
        CoreAccount.HasLabAccessProvider = null;

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            T Find<T>(string n) where T : Control => shell.FindControl<T>(n)!;

            // ---- the 7.1.5 header row ----
            var wallet = Find<SparkleWallet>("HeaderSparkleWallet");
            Assert.True(wallet.IsVisible);
            Assert.Equal(SparkleWallet.PillWidth, wallet.Bounds.Width, 1);
            Assert.Equal(10329.ToString("N0"), wallet.BalanceShown);
            s.SkillPoints = 10400;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(10400.ToString("N0"), wallet.BalanceShown);

            foreach (var cut in new[] { "TxtLevel", "TxtHeaderVersion", "BtnManageMods", "CmbLanguagePill" })
                Assert.False(Find<Control>(cut).IsEffectivelyVisible, cut + " is cut from the 7.1.5 header");
            var cluster = (Panel)Find<Button>("BtnProfileBubble").Parent!;
            var kids = cluster.Children.ToList();
            Assert.Equal(kids.IndexOf(Find<Button>("BtnAccountChip")) + 1, kids.IndexOf(Find<Button>("BtnProfileBubble")));
            Assert.Equal(0, kids.IndexOf(Find<PremiumSpark>("HeaderPremiumSpark")));

            // ---- the spark reads the bubble's tier rule ----
            var spark = Find<PremiumSpark>("HeaderPremiumSpark");
            Assert.Equal(SparkTier.Free, spark.Tier);
            Assert.False(spark.LoopsRunning);   // Free never runs a clock
            Assert.Equal(34, spark.Bounds.Height + spark.Margin.Top + spark.Margin.Bottom, 1);

            // ---- the XP row ----
            Assert.Equal("LVL 39", Find<TextBlock>("TxtLevelLabel").Text);
            Assert.Equal(24, Find<Border>("LevelChip").Bounds.Height, 1);
            Assert.Equal(10, Find<Border>("XPBarTrack").Bounds.Height, 1);
            Assert.Equal(1.0, Find<Ellipse>("XPTubeBead").Opacity);   // a 39% fill carries the bead
            Assert.Equal(10, Find<Border>("XPMeniscus").Bounds.Width, 1);

            // ---- the drum ----
            Assert.Equal(BannerFxRules.HostHeight, Find<Border>("HeaderBannerHost").Bounds.Height, 1);
            Assert.True(Find<Grid>("BannerDrum").ClipToBounds);
            foreach (var n in new[] { "BannerDrumShade", "BannerDrumLip", "BannerDrumFoot", "BannerFlashRing", "BannerGlass" })
                Assert.False(Find<Border>(n).IsHitTestVisible, n + " never takes the mouse");

            // ---- depth follows the section hue ----
            Shoot(shell, "h-header-signedout-home.png");
            shell.ShowTab("studio");
            Dispatcher.UIThread.RunJobs();
            var hue = NavStripRules.Accent(NavSections.Studio);
            uint First(IBrush? b) => b is LinearGradientBrush g ? ToArgb(g.GradientStops[0].Color) : 0;
            Assert.Equal(HudDepthRules.WellTop(hue), First(Find<Border>("XPGrooveTop").Background));
            Assert.Equal(HudDepthRules.WellTop(hue), First(Find<Border>("BannerDrumShade").Background));
            Assert.Equal(HudDepthRules.WellLeft(hue), First(Find<Border>("XPGrooveLeft").Background));
            Shoot(shell, "h-header-signedout-studio.png");

            // ---- a fake signed-in Prime profile ----
            CoreAccount.HasPremiumAccessProvider = () => true;
            CoreAccount.HasLabAccessProvider = () => true;
            spark.Refresh();
            Assert.Equal(SparkTier.Prime, spark.Tier);
            CoreAccount.HasLabAccessProvider = () => false;
            spark.Refresh();
            Assert.Equal(SparkTier.Basic, spark.Tier);
            CoreAccount.HasLabAccessProvider = () => true;
            spark.Refresh();
            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            Shoot(shell, "h-header-prime-home.png");
        }
        finally
        {
            CoreAccount.HasPremiumAccessProvider = oldPremium;
            CoreAccount.HasLabAccessProvider = oldLab;
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            s.OfflineMode = false;
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TheSparkDrawsEveryTierAndOnlyBasicAndPrimeRunAClock() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var free = new PremiumSpark { Pinned = true };
        var basic = new PremiumSpark { Pinned = true };
        var prime = new PremiumSpark { Pinned = true };
        var row = new StackPanel
        {
            Orientation = global::Avalonia.Layout.Orientation.Horizontal,
            Margin = new Thickness(24, 20),
            Spacing = 30,
            Children = { free, basic, prime },
        };
        var w = new Window { Width = 300, Height = 90, Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x14, 0x2A)), Content = row };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        free.Apply(SparkTier.Free, SparkMotion.Full, true);
        basic.Apply(SparkTier.Basic, SparkMotion.Full, true);
        prime.Apply(SparkTier.Prime, SparkMotion.Full, true);
        Assert.False(free.LoopsRunning);
        Assert.True(basic.LoopsRunning);
        Assert.True(prime.LoopsRunning);
        foreach (var sp in new[] { basic, prime }) { sp.Reseed(11); sp.Prewarm(1.5); sp.Advance(0.4); }
        Assert.True(prime.Field.Alive.Count > 0);
        Assert.Empty(free.Field.Alive);
        basic.Apply(SparkTier.Basic, SparkMotion.Off, true);
        Assert.False(basic.LoopsRunning);   // Off is the static lit card
        basic.Apply(SparkTier.Basic, SparkMotion.Full, true);
        basic.Reseed(11); basic.Prewarm(1.5); basic.Advance(0.4);
        Shoot(w, "h-spark-tiers.png");
        w.Close();
        return Task.CompletedTask;
    });

    private static uint ToArgb(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
}
