using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The dashboard's FAVORITES + RECENT column through the shell (WPF MainWindow.FavoritesRail.cs):
/// a visit lands in RECENT, the rail row's right-click menu pins it into FAVORITES (and out of
/// RECENT), the chip wears its feature art, and clicking it navigates like the rail does.
/// </summary>
public sealed class FavoritesRailShellTests
{
    [Fact]
    public async Task VisitPinAndClickTravelThroughTheRealRail()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var s = CoreSettings.Current;
            var premBefore = CoreEntitlement.HasPremiumProvider;
            var freeBefore = CoreEntitlement.IsFreeTodayProvider;
            bool premium = false;
            CoreEntitlement.HasPremiumProvider = () => premium;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            var favBefore = s.RailFavorites.ToList();
            var recentBefore = s.RailRecent.ToList();
            MainShellWindow? w = null;
            try
            {
                s.RailFavorites.Clear();
                s.RailRecent.Clear();
                w = new MainShellWindow();
                w.Show();
                Dispatcher.UIThread.RunJobs();
                var dash = w.Named<SettingsTabView>("SettingsTab")!;
                var favs = dash.FindControl<StackPanel>("FavoritesList")!;
                var recent = dash.FindControl<StackPanel>("RecentList")!;
                Assert.Empty(favs.Children);
                Assert.True(dash.FindControl<TextBlock>("FavoritesEmpty")!.IsVisible);

                w.ShowTab("haptics");          // a visit
                w.ShowTab("settings");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(new[] { "tab.haptics" }, Tags(recent));
                Assert.False(dash.FindControl<TextBlock>("RecentEmpty")!.IsVisible);

                // Right-click the Studio strip's Haptics pill (WPF ea2d4cfca; the rail rows left):
                // the menu reads "Pin to favorites" and pins it.
                w.ShowTab("haptics");
                Dispatcher.UIThread.RunJobs();
                var row = w.PageStrip!.PillFor("haptics")!;
                var menu = row.ContextMenu!;
                row.RaiseEvent(new ContextRequestedEventArgs());   // what a right-click raises
                Dispatcher.UIThread.RunJobs();
                var item = Assert.IsType<MenuItem>(Assert.Single(menu.Items));
                Assert.Equal(global::ConditioningControlPanel.Localization.Loc.Get("rail_pin"), item.Header);
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                menu.Close();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(new[] { "tab.haptics" }, Tags(favs));
                Assert.Empty(recent.Children);   // a pinned destination leaves RECENT
                var chip = (Button)favs.Children[0];
                Assert.IsType<ImageBrush>(chip.Background);   // features/vibe.png, cover-cropped
                Assert.True(HasPadlock(chip), "a locked destination's chip wears no padlock");

                // A tier change repaints through RefreshNavPremiumTags (App.axaml.cs RepaintVeils),
                // exactly as WPF NavPremiumTags.cs:158 repaints the chips.
                premium = true;
                w.RefreshNavPremiumTags();
                chip = (Button)favs.Children[0];
                Assert.False(HasPadlock(chip), "padlock kept after the door opened");

                chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("haptics", w.CurrentTab);
            }
            finally
            {
                w?.Close();
                CoreEntitlement.HasPremiumProvider = premBefore;
                CoreEntitlement.IsFreeTodayProvider = freeBefore;
                s.RailFavorites.Clear(); s.RailFavorites.AddRange(favBefore);
                s.RailRecent.Clear(); s.RailRecent.AddRange(recentBefore);
            }
            return Task.CompletedTask;
        });
    }

    /// <summary>WPF MainWindow.Marquee.cs:464 BannerWebLink_Click: the One Account beat's link opens
    /// the web app through the one opener and retires the beat. Driven by the keyboard (P17).</summary>
    [Fact]
    public async Task BannerWebLinkOpensTheWebAppAndRetiresTheBeat()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var seen = CoreSettings.Current.SeenFeatureIntros;
            bool hadKey = seen.Remove(MainShellWindow.WebBannerSeenKey);
            var previous = global::ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell;
            // Guard only: the open itself is SafeHyperlinkButton's (ExternalOpenerTests); never a real browser.
            global::ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell = _ => true;
            MainShellWindow? w = null;
            try
            {
                w = new MainShellWindow();
                w.Show();
                Dispatcher.UIThread.RunJobs();
                var link = w.Named<Button>("BannerWebLink")!;
                // The inline link must fit inside its line (review P2: "web app" drew as "web ap").
                var line = w.Named<TextBlock>("TxtBannerWeb")!;
                Assert.True(link.Bounds.Right <= line.Bounds.Width,
                    $"banner link {link.Bounds} overruns its line {line.Bounds}");
                Assert.True(link.Focus());
                w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(MainShellWindow.WebBannerSeenKey, seen);
            }
            finally
            {
                w?.Close();
                global::ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell = previous;
                if (!hadKey) seen.Remove(MainShellWindow.WebBannerSeenKey);
                else if (!seen.Contains(MainShellWindow.WebBannerSeenKey)) seen.Add(MainShellWindow.WebBannerSeenKey);
            }
            return Task.CompletedTask;
        });
    }

    private static bool HasPadlock(Button chip) =>
        ((Grid)chip.Content!).Children.OfType<TextBlock>().Any(t => t.Text == "🔒");

    private static IEnumerable<string?> Tags(Panel p) => p.Children.Select(c => c.Tag as string).ToArray();
}
