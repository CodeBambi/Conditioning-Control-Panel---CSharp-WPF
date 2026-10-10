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

    /// <summary>The fold eases for 180 ms on a real clock with the body out of the way
    /// (MainShellWindow.DashboardFold.cs); the landing state is read once the ease has settled.</summary>
    private static void SettleFold(MainShellWindow shell)
    {
        Settle();
        for (int i = 0; i < 200 && shell.BrowserFoldAnimating; i++)
        {
            System.Threading.Thread.Sleep(10);
            Settle();
        }
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
                s.RailFavorites.ToList(), s.RailRecent.ToList(), s.DashboardToggleHintUses, s.PerformanceMode);
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
                (s.DashboardBrowserCollapsed, s.FavoritesDrawerOpen, s.DashboardInvertClicks, var fav, var rec, s.DashboardToggleHintUses, s.PerformanceMode) = saved;
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
        SettleFold(shell);
        Assert.False(CoreSettings.Current.DashboardBrowserCollapsed);
        Assert.True(body.IsVisible);
        Assert.False(board.IsVisible);
        Assert.True(tab.BrowserCardRow.Height.IsStar);
        Assert.Equal(0, tab.BrowserFoldRow.Height.Value);
        Assert.Contains(tab.FindControl<TextBlock>("TxtFoldBrowser")!.Text, new[] { BrowserFoldRule.Chevron(false), "▴" });
        Shot(shell, "e3-home-browser-open.png");

        arrow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        SettleFold(shell);
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
    }, s =>
    {
        s.FavoritesDrawerOpen = false;
        s.RailFavorites = new List<string>();
        // The 2.5 s peek runs on real time; ambient Forever loops make headless RunJobs spin past it.
        s.PerformanceMode = true;
    });

    [Fact]
    public Task The_Deeper_editor_tile_opens_the_Deeper_page() => WithHome((shell, tab) =>
    {
        tab.FindControl<FeatureCard>("CardDeeperEditor")!.RaiseEvent(new RoutedEventArgs(FeatureCard.ClickEvent));
        Settle();
        Assert.Equal("deeper", shell.CurrentTab);
    });

    // k4 HA4: a Tonight Board "tab:<key>" button reaches lane pages and redirect keys, as ShowTab does.
    [Fact]
    public Task A_board_tab_button_reaches_a_lane_page_and_a_moved_key() => WithHome((shell, tab) =>
    {
        shell.ShowBillboardTab("friends");
        Settle();
        Assert.Equal("friends", shell.CurrentTab);
        shell.ShowBillboardTab("exclusives");
        Settle();
        Assert.Equal("premium", shell.CurrentTab);
        shell.ShowBillboardTab("no-such-page");
        Settle();
        Assert.Equal("premium", shell.CurrentTab);
    });

    [Fact]
    public Task The_account_strip_is_one_line_of_bubbles_and_one_label_opens_at_a_time() => WithHome((shell, tab) =>
    {
        var bar = tab.FindControl<HoverBubbleBar>("HomeBubbleBar")!;
        var names = bar.Bubbles.Select(b => b.Name).ToArray();
        Assert.Equal(new[] { "BtnLinkPhone", "BtnQuickLogout", "ChkQuickDiscordRichPresence", "VelvetBtnWebcam",
            "VelvetBtnSystem", "VelvetBtnSchedulerRamp", "VelvetBtnCatalogue", "VelvetBtnAppInfo", "BtnOpenDiagnostics" }, names);
#if !DEBUG
        // HA9: the diagnostics bubble is a developer affordance, hidden outside DEBUG.
        Assert.False(tab.FindControl<Button>("BtnOpenDiagnostics")!.IsVisible);
#endif
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

    [Fact]
    public Task The_logo_dial_moves_at_rest_on_the_live_shell() => WithHome((shell, tab) =>
    {
        var dial = tab.FindControl<AnimatedLogoDial>("ImgLogo")!;
        int p0 = dial.Surface.PaintCount;
        using var a = dial.Surface.CopyBacking();
        for (int i = 0; i < 20; i++) { System.Threading.Thread.Sleep(25); Settle(); }
        using var b = dial.Surface.CopyBacking();
        Assert.NotNull(a); Assert.NotNull(b);
        long diff = 0;
        for (int y = 0; y < a!.Height; y += 2) for (int x = 0; x < a.Width; x += 2)
        { var ca = a.GetPixel(x, y); var cb = b!.GetPixel(x, y); diff += Math.Abs(ca.Red - cb.Red) + Math.Abs(ca.Green - cb.Green) + Math.Abs(ca.Blue - cb.Blue); }
        Assert.True(diff > 1000, $"two frames ~0.5 s apart must differ (diff {diff})");
        Assert.True(dial.HasArtwork, "the dial art must load (else the still wordmark shows)");
        Assert.True(dial.IsAnimating, "the clock must run at Full motion");
        Assert.True(dial.Surface.PaintCount > p0 + 3, $"the dial must repaint over frames ({p0} -> {dial.Surface.PaintCount})");
    });
    [Fact]
    public Task Hovering_a_bubble_opens_its_label_to_the_left_and_leaving_closes_it() => WithHome((shell, tab) =>
    {
        // WPF 7.1.5 Controls/HoverBubbleBar.cs: hover slides the label out to the LEFT of the glyph
        // (160 ms CubicEaseOut, 1.06 pop) while the button's slot stays 34 px; leaving closes it.
        var bar = tab.FindControl<HoverBubbleBar>("HomeBubbleBar")!;
        var webcam = bar.Bubbles.First(b => b.Name == "VelvetBtnWebcam");
        var plate = (Border)webcam.Content!;
        double rest = plate.Bounds.Width;
        double slot = webcam.Bounds.Width;
        // A spot inside the open plate, left of the button's own slot (over its neighbour at rest).
        var probe = webcam.TranslatePoint(new Point(-30, webcam.Bounds.Height / 2), shell)!.Value;
        using var restFrame = shell.CaptureRenderedFrame()!;
        uint restPx = Px(restFrame, probe);

        shell.MouseMove(webcam.TranslatePoint(new Point(slot / 2, webcam.Bounds.Height / 2), shell)!.Value,
            global::Avalonia.Input.RawInputModifiers.None);
        Wait();
        Assert.True(bar.IsExpanded(webcam), "hover must open the bubble");
        Assert.False(string.IsNullOrEmpty(bar.LabelOf(webcam)));
        Assert.True(plate.Bounds.Width > rest + 20, $"the plate must grow to show its label ({rest} -> {plate.Bounds.Width})");
        Assert.Equal(slot, webcam.Bounds.Width, 1);
        // Drawn, not just laid out: the Fluent Button theme clipped the open plate to the slot.
        Assert.False(webcam.ClipToBounds);
        Shot(shell, "bubble-open.png");
        using var openFrame = shell.CaptureRenderedFrame()!;
        Assert.NotEqual(restPx, Px(openFrame, probe));

        shell.MouseMove(new Point(5, 5), global::Avalonia.Input.RawInputModifiers.None);
        Wait();
        Assert.False(bar.IsExpanded(webcam));
        Assert.Equal(rest, plate.Bounds.Width, 1);
    });

    /// <summary>Lets the 160 ms transitions run out on the headless clock.</summary>
    private static void Wait()
    {
        for (int i = 0; i < 30; i++) { Settle(); System.Threading.Thread.Sleep(10); }
    }

    private static uint Px(global::Avalonia.Media.Imaging.WriteableBitmap frame, Point p)
    {
        var px = BoardHeadTests.Pixels(frame, out int w);
        return px[(int)p.Y * w + (int)p.X];
    }
}
