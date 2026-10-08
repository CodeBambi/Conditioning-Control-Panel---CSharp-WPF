using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using CCP.Avalonia.Tests.Board;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Home;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests.Home;

/// <summary>
/// The WPF 7.1.5 Home on the real shell (parity lane E3): the logo dial back in the centre, the
/// Tonight Board in the row the folded browser gives back, no premium rail, the Deeper editor tile,
/// the one-line account strip with its bubbles, and the favourites drawer at the right edge.
/// Set CCP_HOME_PNG_DIR to save the proof shots.
/// </summary>
public sealed class HomeLayoutTests
{
    private static void Settle()
    {
        for (int i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(MainShellWindow shell, string name)
    {
        if (Environment.GetEnvironmentVariable("CCP_HOME_PNG_DIR") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        using var frame = shell.CaptureRenderedFrame();
        frame?.Save(Path.Combine(dir, name));
    }

    /// <summary>Runs <paramref name="body"/> on a shown shell with Home's settings saved and restored.</summary>
    private static Task WithHome(Action<MainShellWindow, SettingsTabView> body, Action<ConditioningControlPanel.Models.AppSettings>? arrange = null) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            BoardHeadTests.EnsureApp();
            BoardHeadTests.Pin();
            var s = CoreSettings.Current;
            var saved = (s.DashboardBrowserCollapsed, s.FavoritesDrawerOpen, s.DashboardInvertClicks,
                s.RailFavorites.ToList(), s.RailRecent.ToList(), s.DashboardToggleHintUses);
            MainShellWindow? shell = null;
            try
            {
                arrange?.Invoke(s);
                shell = new MainShellWindow { Width = 1600, Height = 1000 };
                shell.Show();
                Settle();
                body(shell, shell.Named<SettingsTabView>("SettingsTab")!);
            }
            finally
            {
                shell?.Close();
                BoardHeadTests.Unpin();
                (s.DashboardBrowserCollapsed, s.FavoritesDrawerOpen, s.DashboardInvertClicks, var fav, var rec, s.DashboardToggleHintUses) = saved;
                s.RailFavorites = fav;
                s.RailRecent = rec;
            }
            return Task.CompletedTask;
        });

    [Fact]
    public Task Home_is_the_715_layout_dial_centre_board_right_drawer_closed() => WithHome((shell, tab) =>
    {
        // 0 | 640 | * | Auto: the premium rail's column is gone, the drawer has its own.
        var root = (Grid)tab.FindControl<Border>("LogoBrandFrame")!.Parent!.Parent!;
        Assert.Equal(new[] { 0.0, 640.0 }, root.ColumnDefinitions.Take(2).Select(c => c.Width.Value));
        Assert.True(root.ColumnDefinitions[2].Width.IsStar);
        Assert.True(root.ColumnDefinitions[3].Width.IsAuto);
        foreach (var gone in new[] { "PremiumRail", "CompanionStrip", "VelvetHelperButtonRow", "CardJustDrop", "LogoWordmarkPlaceholder" })
            Assert.Null(tab.FindControl<Control>(gone));

        // The centre is the logo dial again.
        Assert.True(tab.FindControl<Border>("LogoBrandFrame")!.IsVisible);
        Assert.IsType<AnimatedLogoDial>(tab.FindControl<Control>("ImgLogo"));

        // Folded by default: the board fills the row the card gives back, in the right column.
        Assert.True(CoreSettings.Current.DashboardBrowserCollapsed);
        var board = tab.FindControl<Border>("DashBillboard")!;
        Assert.True(board.IsVisible);
        Assert.Same(tab.FindControl<Grid>("HomeRightColumn"), board.Parent);
        Assert.Equal(1, Grid.GetRow(board));
        Assert.False(tab.FindControl<Border>("BrowserFoldBody")!.IsVisible);
        Assert.NotNull(shell.DashboardBillboardHost);

        // The drawer: closed, handle only.
        var drawer = tab.FindControl<FavoritesDrawer>("FavoritesDrawer")!;
        Assert.Equal(3, Grid.GetColumn(drawer));
        Assert.False(drawer.IsOpen);
        Assert.Equal(0, drawer.Body.Width);

        // The Deeper editor tile took the tease tile's cell.
        var deeper = tab.FindControl<FeatureCard>("CardDeeperEditor")!;
        Assert.Equal((1, 0), (Grid.GetRow(deeper), Grid.GetColumn(deeper)));

        Shot(shell, "e3-home-drawer-closed.png");
    });

    [Fact]
    public Task The_arrow_folds_and_unfolds_the_browser_and_the_board_trades_places() => WithHome((shell, tab) =>
    {
        var arrow = tab.FindControl<Button>("BtnFoldBrowser")!;
        var body = tab.FindControl<Border>("BrowserFoldBody")!;
        var board = tab.FindControl<Border>("DashBillboard")!;

        arrow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle();
        Assert.False(CoreSettings.Current.DashboardBrowserCollapsed);
        Assert.True(body.IsVisible);
        Assert.False(board.IsVisible);
        Assert.True(tab.BrowserCardRow.Height.IsStar);
        Assert.Equal(0, tab.BrowserFoldRow.Height.Value);
        Assert.Contains(tab.FindControl<TextBlock>("TxtFoldBrowser")!.Text, new[] { BrowserFoldRule.Chevron(false), "▴" });
        Shot(shell, "e3-home-browser-open.png");

        arrow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle();
        Assert.True(CoreSettings.Current.DashboardBrowserCollapsed);
        Assert.False(body.IsVisible);
        Assert.True(board.IsVisible);
        Assert.True(tab.BrowserFoldRow.Height.IsStar);
    });

    [Fact]
    public Task A_site_radio_reveals_a_folded_card_without_touching_the_preference() => WithHome((shell, tab) =>
    {
        tab.FindControl<RadioButton>("RbHypnoTube")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle();
        Assert.True(CoreSettings.Current.DashboardBrowserCollapsed);       // the preference stands
        Assert.True(tab.FindControl<Border>("BrowserFoldBody")!.IsVisible); // but the card is open
        Assert.False(shell.BrowserFolded);
    });

    [Fact]
    public Task The_drawer_opens_from_the_setting_and_lists_favourites_then_recent() => WithHome((shell, tab) =>
    {
        var drawer = tab.FindControl<FavoritesDrawer>("FavoritesDrawer")!;
        Assert.True(drawer.IsOpen);
        Assert.Equal(HomeDashboardRules.FavoritesDrawerWidth, drawer.Body.Width);
        Assert.Equal(new[] { "tab.deeper", "tab.quests" }, drawer.FavoritesList.Children.Select(c => c.Tag as string));
        // RECENT drops what is pinned: deeper is a favourite, so only achievements shows.
        Assert.Equal(new[] { "tab.achievements" }, drawer.RecentList.Children.Select(c => c.Tag as string));
        Assert.False(drawer.FavoritesEmpty.IsVisible);
        Shot(shell, "e3-home-drawer-open.png");

        // Only the handle writes the preference.
        drawer.Handle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle();
        Assert.False(drawer.IsOpen);
        Assert.False(CoreSettings.Current.FavoritesDrawerOpen);
    }, s =>
    {
        s.FavoritesDrawerOpen = true;
        s.RailFavorites = new List<string> { "tab.deeper", "tab.quests" };
        s.RailRecent = new List<string> { "tab.deeper", "tab.achievements" };
    });

    [Fact]
    public Task A_new_pin_peeks_a_closed_drawer_and_writes_nothing() => WithHome((shell, tab) =>
    {
        var drawer = tab.FindControl<FavoritesDrawer>("FavoritesDrawer")!;
        Assert.False(drawer.IsOpen);
        shell.TogglePinned("tab.achievements");
        Settle();
        Assert.True(drawer.IsPeeking);
        Assert.True(drawer.IsOpen);
        Assert.False(CoreSettings.Current.FavoritesDrawerOpen);
        Assert.Contains("tab.achievements", CoreSettings.Current.RailFavorites);
        Assert.NotNull(drawer.FindChip("tab.achievements"));
        Shot(shell, "e3-home-pin-peek.png");
    }, s => { s.FavoritesDrawerOpen = false; s.RailFavorites = new List<string>(); });

    [Fact]
    public Task The_Deeper_editor_tile_opens_the_Deeper_page() => WithHome((shell, tab) =>
    {
        tab.FindControl<FeatureCard>("CardDeeperEditor")!.RaiseEvent(new RoutedEventArgs(FeatureCard.ClickEvent));
        Settle();
        Assert.Equal("deeper", shell.CurrentTab);
    });

    [Fact]
    public Task The_account_strip_is_one_line_of_bubbles_and_one_label_opens_at_a_time() => WithHome((shell, tab) =>
    {
        var bar = tab.FindControl<HoverBubbleBar>("HomeBubbleBar")!;
        var names = bar.Bubbles.Select(b => b.Name).ToArray();
        Assert.Equal(new[] { "BtnLinkPhone", "BtnQuickLogout", "ChkQuickDiscordRichPresence", "VelvetBtnWebcam",
            "VelvetBtnSystem", "VelvetBtnSchedulerRamp", "VelvetBtnCatalogue", "VelvetBtnAppInfo", "BtnOpenDiagnostics" }, names);
        // The Discord pill keeps its label and is not dressed.
        Assert.DoesNotContain(bar.Bubbles, b => b.Name == "BtnDiscord");
        Assert.Same(bar, tab.FindControl<Button>("BtnDiscord")!.Parent);

        var webcam = bar.Bubbles.First(b => b.Name == "VelvetBtnWebcam");
        var system = bar.Bubbles.First(b => b.Name == "VelvetBtnSystem");
        bar.Expand(webcam, animate: false);
        bar.Expand(system, animate: false);
        Assert.False(bar.IsExpanded(webcam));
        Assert.True(bar.IsExpanded(system));
        Assert.False(string.IsNullOrEmpty(bar.LabelOf(system)));
        Assert.Equal("Intensity Ramp", HoverBubbleBar.StripLeadingGlyph("⚡ Intensity Ramp"));
        bar.Collapse(system, animate: false);
        Assert.False(bar.IsExpanded(system));

        // Signed out: Link phone and Logout follow the logged-in face away.
        Assert.False(tab.FindControl<Border>("LoggedInStatusPanel")!.IsVisible);
        Assert.False(bar.Bubbles.First(b => b.Name == "BtnQuickLogout").IsVisible);
    });

    [Fact]
    public Task The_gesture_caption_retires_after_three_toggles_and_says_when_the_clicks_are_swapped() => WithHome((shell, tab) =>
    {
        var hint = tab.FindControl<TextBlock>("DashToggleHint")!;
        Assert.True(hint.IsVisible);
        CoreSettings.Current.DashboardInvertClicks = true;
        tab.RefreshClickPreference();
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("dash_click_swapped"), hint.Text);
        CoreSettings.Current.DashboardToggleHintUses = HomeDashboardRules.ToggleHintMaxUses;
        tab.RefreshClickPreference();
        Assert.False(hint.IsVisible);
    }, s => { s.DashboardToggleHintUses = 0; s.DashboardInvertClicks = false; });
}
