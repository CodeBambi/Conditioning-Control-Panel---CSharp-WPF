using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Views.Controls.AppSettingsSections;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav rework (2026-10-06), REHOME lane: one home per thing.
/// <list type="bullet">
/// <item>Settings › Monitors holds the monitor picker and the four video rows that lived in the
/// Home System pill popup (Features/SystemFeatureControl, deleted).</item>
/// <item>Settings › Account &amp; Plans hosts the retired Premium page (vault header, tier plates,
/// spotlight, invites) in PlansMode: no shelf, and no inner ScrollViewer to eat the wheel.</item>
/// <item>Play › Games carries the five games whose only panel door was the Premium page, and the
/// three zones the section strip scrolls to.</item>
/// <item>Library › Folders is a chip on the Assets page; Settings › Data opens the logs folder.</item>
/// </list>
/// Set <c>CCP_NAV_PNG_DIR</c> to also write offscreen renders of the three surfaces.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class NavRehomeTests
{
    private static void OnStaThread(Action body) => WpfRenderHarness.OnStaThread(body);

    private static string ProductDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "repo root not found");
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { ProductDir() }.Concat(parts).ToArray()));

    private static Grid Realize(FrameworkElement element, double width, double height = double.PositiveInfinity)
    {
        var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)) };
        host.Children.Add(element);
        host.Measure(new Size(width, height));
        var h = double.IsInfinity(height) ? Math.Max(1, host.DesiredSize.Height) : height;
        host.Arrange(new Rect(0, 0, width, h));
        host.UpdateLayout();
        Assert.True(element.DesiredSize.Height > 0, $"{element.GetType().Name} measured to zero height");
        return host;
    }

    private static void MaybeRender(FrameworkElement host, string name)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_NAV_PNG_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var w = (int)Math.Ceiling(host.ActualWidth);
        var h = (int)Math.Ceiling(Math.Min(host.ActualHeight, 4000));
        var bmp = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(host);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }

    // ------------------------------------------------------------------ Settings › Monitors

    [Fact]
    public void MonitorsSectionCarriesThePickerAndTheFourVideoRows() => OnStaThread(() =>
    {
        var section = new MonitorsSettingsSection();
        var host = Realize(section, 760);
        Assert.NotNull(section.ContentMonitorPicker);
        foreach (var box in new[] { section.ChkFillAllMon, section.ChkVideoGpuDecode, section.ChkVideoBlurBg, section.ChkBrowserVideoEngine })
        {
            Assert.NotNull(box);
            Assert.True(box.IsEnabled);
        }
        MaybeRender(host, "rehome-monitors.png");
    });

    [Fact]
    public void MonitorsSectionWritesTheSameSettingsKeysThePopupDid()
    {
        var code = Read("Views", "Controls", "AppSettings", "MonitorsSettingsSection.xaml.cs");
        foreach (var key in new[] { "FillAllMonitorsWithVideo", "VideoForceHardwareDecoding",
                                    "VideoBlurredBackgroundEnabled", "BrowserVideoEngineEnabled" })
            Assert.Contains("s." + key + " =", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSystemPopupIsGoneAndNothingReachesForIt()
    {
        Assert.False(File.Exists(Path.Combine(ProductDir(), "Features", "SystemFeatureControl.xaml")));
        Assert.False(File.Exists(Path.Combine(ProductDir(), "Features", "SystemFeatureControl.xaml.cs")));
        Assert.DoesNotContain("new Features.SystemFeatureControl", Read("MainWindow", "MainWindow.Presets.cs"),
                              StringComparison.Ordinal);
    }

    [Fact]
    public void TheMonitorsSectionIsAKnownSettingsKey() =>
        Assert.Contains("monitors", AppSettingsTabView.SectionKeys);

    // ------------------------------------------------------------------ Settings › Account & Plans

    [Fact]
    public void AccountAndPlansHostsTheVaultInPlansMode() => OnStaThread(() =>
    {
        var section = new AccountSettingsSection();
        var host = Realize(section, 760);
        var plans = section.PlansView;
        Assert.NotNull(plans);
        Assert.True(plans.PlansMode);
        Assert.Equal(Visibility.Collapsed, plans.ExclusivesShelf.Visibility);
        Assert.Equal(Visibility.Collapsed, plans.CollectionLabel.Visibility);
        // Lifted out of its ScrollViewer: a ScrollViewer marks every wheel notch Handled.
        Assert.Null(plans.ContentScroll.Content);
        Assert.Null(plans.ContentScroll.Parent);
        // What carries over is still there to paint.
        Assert.NotNull(plans.TierPlate1);
        Assert.NotNull(plans.TierPlate2);
        Assert.NotNull(plans.SpotlightCard);
        Assert.NotNull(plans.InvitesHost);
        Assert.True(plans.SpotlightCard.IsDescendantOf(plans));
        MaybeRender(host, "rehome-account-plans.png");
    });

    [Fact]
    public void TheAccountPillAndHeaderReadAccountAndPlans()
    {
        Assert.Contains("{loc:Str settings_section_plans}", Read("Views", "Tabs", "AppSettingsTabView.xaml"), StringComparison.Ordinal);
        Assert.Contains("{loc:Str settings_section_plans}", Read("Views", "Controls", "AppSettings", "AccountSettingsSection.xaml"),
                        StringComparison.Ordinal);
    }

    [Fact]
    public void TheVaultTileAndTheInviteTicketOpenAccountAndPlans()
    {
        var presets = Read("MainWindow", "MainWindow.Presets.cs");
        Assert.Contains("CardVault_Click(object sender, RoutedEventArgs e) => OpenAppSettingsSection(\"account\")", presets,
                        StringComparison.Ordinal);
        var ticket = Read("MainWindow", "MainWindow.InviteTicket.cs");
        Assert.Contains("OpenAppSettingsSection(\"account\")", ticket, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowTab(\"exclusives\")", ticket, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePremiumPainterTargetsTheHostedCopyNotTheRetiredTab()
    {
        var vault = Read("MainWindow", "MainWindow.Exclusives.cs");
        Assert.DoesNotContain("ExclusivesTab.", vault, StringComparison.Ordinal);
        Assert.DoesNotContain("ExclusivesTab?", vault, StringComparison.Ordinal);
        Assert.Contains("SectionAccount?.PlansView", vault, StringComparison.Ordinal);
        // Launch calls other surfaces depend on stay alive.
        Assert.Contains("void OpenExclusiveFeature(", vault, StringComparison.Ordinal);
        Assert.Contains("void OpenExclusiveSpotlight(", vault, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ Play › Games + zones

    [Fact]
    public void PlayHasAZoneHeaderForEveryStripZone() => OnStaThread(() =>
    {
        var page = new PlayTabView();
        var host = Realize(page, 1469, 891);
        foreach (var zone in PlayTabView.ZoneKeys)
            Assert.NotNull(page.ZoneHeader(zone));
        Assert.Null(page.ZoneHeader("nope"));
        page.ScrollToZone("nope");   // unknown key is a no-op, never a throw
        Assert.Equal(new[] { "games", "sessions", "eyes" }, PlayTabView.ZoneKeys);
        MaybeRender(host, "rehome-play.png");
    });

    [Fact]
    public void TheGamesZoneHasACardForEveryGameThePremiumPageWasTheOnlyDoorFor() => OnStaThread(() =>
    {
        var page = new PlayTabView();
        Realize(page, 1469, 891);
        foreach (var slot in new[] { "SlotBackRoom", "SlotDtrh", "SlotArcademy", "SlotRacingThoughts", "SlotWebApp" })
        {
            var g = page.FindName(slot) as Grid;
            Assert.True(g != null && g.Children.Count > 0, slot + " is missing or empty");
        }
    });

    [Fact]
    public void TheGamesCardsRunTheLaunchersOwnCalls()
    {
        var cards = Read("Views", "Tabs", "PlayTabView.Cards.cs");
        Assert.Contains("LaunchPlayLauncherGame(\"dtrh\")", cards, StringComparison.Ordinal);
        Assert.Contains("LaunchPlayLauncherGame(\"arcademy\")", cards, StringComparison.Ordinal);
        Assert.Contains("LaunchPlayLauncherGame(\"race\")", cards, StringComparison.Ordinal);
        var play = Read("MainWindow", "MainWindow.PlayTab.cs");
        Assert.Contains("LaunchPlayBackRoom() => BtnStartBackRoom_Click(", play, StringComparison.Ordinal);
        Assert.Contains("LaunchPlayLauncherGame(string id) => LaunchExclusiveGame(id)", play, StringComparison.Ordinal);
        Assert.Contains("BrowserLauncher.OpenUrlOrPrompt(WebAppUrl", play, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ Library › Folders, Data › logs

    [Fact]
    public void TheAssetsPageHasAFoldersZone() => OnStaThread(() =>
    {
        var page = new AssetsTabView();
        Realize(page, 1469, 891);
        Assert.NotNull(page.ZoneFolders);
        Assert.NotNull(page.BtnPickAssetsFolder);
        Assert.True(page.ZoneFolders.IsAncestorOf(page.BtnPickAssetsFolder));
        Assert.Equal(new[] { "folders" }, AssetsTabView.ZoneKeys);
        page.ScrollToZone("other");
    });

    [Fact]
    public void TheDataSectionOpensTheLogsFolder() => OnStaThread(() =>
    {
        var section = new DataSettingsSection();
        Realize(section, 760);
        Assert.NotNull(section.BtnOpenLogsFolder);
        Assert.Contains("mw.BtnViewLog_Click(", Read("Views", "Controls", "AppSettings", "DataSettingsSection.xaml.cs"),
                        StringComparison.Ordinal);
    });

    [Fact]
    public void TheSubliminalColourEditorStillHasADoor() =>
        Assert.Contains("new ColorEditorDialog", Read("Features", "SubliminalFeatureControl.xaml.cs"), StringComparison.Ordinal);
}
