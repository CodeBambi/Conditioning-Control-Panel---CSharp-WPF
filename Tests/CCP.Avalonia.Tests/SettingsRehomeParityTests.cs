using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Nav;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// WPF 7.1.5 nav rework, REHOME lane on this head: Settings is one page with a Monitors section
/// and an Account &amp; Plans section hosting the vault's PlansMode copy; the Library's media
/// picker is a segmented source bar, six flavour cards and one Fine-tune fold. Set
/// CCP_E2_SHOTS to a directory to also save the frames.
/// </summary>
public sealed class SettingsRehomeParityTests
{
    [Fact]
    public Task SettingsCarriesMonitorsAndAccountAndPlans() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        Assert.Equal(new[] { "general", "audio", "devices", "monitors", "performance",
                             "notifications", "emidesk", "account", "data", "updates" },
                     AppSettingsTabView.SectionKeys);
        // Every Settings pill in the Core table is a section on the page.
        foreach (var t in NavSections.Find(NavSections.Settings)!.Tabs)
            Assert.Contains(t.Key, AppSettingsTabView.SectionKeys);

        var page = new AppSettingsTabView();
        var host = new Window { Width = 1300, Height = 860, Content = page };
        try
        {
            host.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(page.FindControl<MonitorsSettingsSection>("SectionMonitors"));
            // The monitor picker has one home: Monitors, not General.
            Assert.Null(page.FindControl<GeneralSettingsSection>("SectionGeneral")!
                            .FindControl<Control>("ContentMonitorPicker"));
            Assert.NotNull(page.FindControl<MonitorsSettingsSection>("SectionMonitors")!
                               .FindControl<Control>("ContentMonitorPicker"));
            Assert.NotNull(page.FindControl<DataSettingsSection>("SectionData")!.FindControl<Button>("BtnOpenLogsFolder"));

            page.FocusSection("account");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("account", page.CurrentSectionKey);
            Assert.True(page.FindControl<RadioButton>("SectionPillAccount")!.IsChecked);

            var plans = page.FindControl<AccountSettingsSection>("SectionAccount")!.Plans;
            Assert.True(plans.PlansMode);
            Assert.False(plans.FindControl<ItemsControl>("ExclusivesShelf")!.IsVisible);
            Assert.False(plans.FindControl<Border>("SpotlightCard")!.IsVisible);
            Assert.True(plans.FindControl<Button>("BtnSeePremium")!.IsVisible);
            Assert.Equal(Loc.Get("plans_header_title"), plans.FindControl<TextBlock>("TxtVaultTitle")!.Text);
            // The content is lifted out of its scroller, so the page's own wheel reaches it.
            Assert.Null(plans.FindControl<ScrollViewer>("ContentScroll")!.Content);
            Shoot(host, "e2-settings-account.png");

            page.FocusSection("monitors");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("monitors", page.CurrentSectionKey);
            Shoot(host, "e2-settings-monitors.png");
        }
        finally { host.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LibraryMediaPickerIsSourceFlavourAndOneFold() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var s = CoreSettings.Current;
        var (src, consent) = (s.MediaSource, s.RemoteMediaConsented);
        var view = new AssetsTabView();
        var host = new Window { Width = 1500, Height = 900, Content = view };
        try
        {
            s.MediaSource = "mixed";
            host.Show();
            view.RefreshRemoteMediaPicker();
            Dispatcher.UIThread.RunJobs();

            var segments = view.FindControl<StackPanel>("RemoteSourceChips")!.Children.OfType<ToggleButton>().ToList();
            Assert.Equal(new[] { "local", "online", "mixed" }, segments.Select(c => (string)c.Tag!));
            Assert.Equal(new[] { false, false, true }, segments.Select(c => c.IsChecked == true));
            Assert.True(view.FindControl<StackPanel>("RemoteRatioRow")!.IsVisible);
            Assert.True(view.FindControl<StackPanel>("RemoteMediaDetails")!.IsVisible);

            // Six cards: the five flavours plus Mine, from Core FlavourPresets.
            var tiles = view.FindControl<UniformGrid>("RemoteFlavourTiles")!.Children.OfType<Button>().ToList();
            Assert.Equal(FlavourPresets.All.Select(f => f.Id).Append(FlavourPresets.MineId), tiles.Select(t => (string)t.Tag!));
            Assert.Equal(ConditioningControlPanel.Services.Fyp.Online.FypOnlineCoordinator.Catalog.Length,
                         view.FindControl<UniformGrid>("RemoteNicheChips")!.Children.Count);
            Assert.StartsWith("Fine-tune", view.FindControl<TextBlock>("TxtRemoteSummary")!.Text);

            // The Folders chip names the folder the app reads.
            Assert.Equal(CorePaths.EffectiveAssets, view.FindControl<TextBlock>("TxtAssetsFolderPath")!.Text);

            // Fine-tune is one fold; open, the browser steps aside.
            Assert.False(view.FindControl<ScrollViewer>("RemoteFineTuneScroll")!.IsVisible);
            Shoot(host, "e2-library-picker.png");
            view.FindControl<Button>("BtnRemoteFineTune")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FindControl<ScrollViewer>("RemoteFineTuneScroll")!.IsVisible);
            Assert.False(view.FindControl<Border>("AssetBrowserSection")!.IsVisible);
            Shoot(host, "e2-library-finetune.png");
        }
        finally
        {
            (s.MediaSource, s.RemoteMediaConsented) = (src, consent);
            host.Close();
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PremiumPageShotsGrouped() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var view = new ExclusivesTabView();
        var host = new Window { Width = 1500, Height = 1400, Content = view };
        try
        {
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var groups = view.FindControl<ItemsControl>("ExclusivesShelf")!.Items.Cast<ExclusiveShelfGroup>().ToList();
            Assert.Equal(Loc.Get("premium_group_basic"), groups[0].Title);
            Assert.Equal(string.Format(Loc.Get("premium_group_count"), groups[0].Cards.Count(c => c.IsMine), groups[0].Cards.Count),
                         groups[0].CountText);
            Assert.False(view.FindControl<Button>("BtnSeePremium")!.IsVisible);
            Shoot(host, "e2-premium-page.png");
        }
        finally { host.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });

    private static void Shoot(Window w, string name)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_E2_SHOTS");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame()?.Save(Path.Combine(dir, name));
    }

    private static void EnsureAvalonia()
    {
        if (Application.Current is not null) return;
        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }
}
