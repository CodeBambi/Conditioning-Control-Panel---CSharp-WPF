using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Nav;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Polish wave 8 readability pass (main baff475e4 / 337517fff / 23c284a3c / 4f22d0478):
/// the type scale, the section ink and the token greys, measured on the pages the user opens.
/// Twin of WPF TypeScaleContrastTests / ReadabilityRenderTests.</summary>
public sealed class TypeScaleReadabilityTests
{
    // The ink follows the section (7.1.5 nav: MainWindow.SectionChrome PaintSectionInk repaints the SectionInk family from
    // NavStripRules on every section change), so each page is measured against its own section's hue, not the lilac default.
    private static Color SectionInk(string section) => Color.FromUInt32(NavStripRules.Ink(section));
    private static readonly Color TextLight = Color.Parse("#FFF0F0F5");
    private static readonly Color TextSecondary = Color.Parse("#FFCCCCE0");

    private static Color Ink(TextBlock t) => Assert.IsAssignableFrom<ISolidColorBrush>(t.Foreground).Color;

    private static TextBlock ByText(Control root, string key)
    {
        var text = Loc.Get(key);
        return root.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Text == text);
    }

    [Fact]
    public async Task SettingsAndPlayPagesWearTheTypeScaleAndTheSectionInk()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;

            // The card lifts off the page (#222240 -> #28284C).
            Assert.True(Application.Current!.TryFindResource("ElevatedSurface", out var lift));
            Assert.Equal(Color.Parse("#FF28284C"), (Color)lift!);

            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                shell.ShowTab("appsettings");
                Dispatcher.UIThread.RunJobs();

                var page = shell.AppSettingsPage!;
                var title = ByText(page, "set2_settings_title");
                Assert.Equal(26, title.FontSize);                        // Type.PageTitle
                Assert.Equal(TextLight, Ink(title));
                var subtitle = ByText(page, "set2_settings_subtitle");
                Assert.Equal(13.5, subtitle.FontSize);                   // Type.PageSubtitle
                Assert.Equal(TextSecondary, Ink(subtitle));

                // SectionHeader is the eyebrow now: 11 Bold Consolas, no local 14 (the Settings door
                // sections keep their own SectionHue ink, as in WPF).
                var eyebrow = ByText(page, "section_audio");
                Assert.Equal(11, eyebrow.FontSize);
                Assert.Equal(FontWeight.Bold, eyebrow.FontWeight);

                shell.ShowTab("play");
                Dispatcher.UIThread.RunJobs();
                var play = shell.GetLogicalDescendants().OfType<global::ConditioningControlPanel.Avalonia.Views.Tabs.PlayTabView>().First();
                var zone = ByText(play, "rf_play_zone_games");           // PlayZoneTag on Type.SectionHeader
                Assert.Equal(11, zone.FontSize);
                Assert.Equal(SectionInk(NavSections.Play), Ink(zone));
                // GAMES gets its rule too, painted with the section rule through a fade mask.
                var rule = Assert.IsType<Border>(((Grid)zone.Parent!).Children[1]);
                Assert.Equal(Color.FromUInt32(NavStripRules.Rule(NavSections.Play)), Assert.IsAssignableFrom<ISolidColorBrush>(rule.Background).Color);
                Assert.NotNull(rule.OpacityMask);
                var cardTitle = ByText(play, "launcher_game_breakout_title"); // PlayCardTitle on Type.CardTitle
                Assert.Equal(18, cardTitle.FontSize);
                var blurb = ByText(play, "launcher_game_breakout_blurb");     // PlayCardBlurb on Type.Body
                Assert.Equal(13, blurb.FontSize);
                Assert.Equal(TextSecondary, Ink(blurb));
            }
            finally { shell.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task CompanionThemeAndMakeHerYoursRideTheTypeScale()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var view = new global::ConditioningControlPanel.Avalonia.Views.Controls.Companion.MakeHerYoursView();
            var window = new Window { Content = view, Width = 900, Height = 900 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                // CompanionTheme's zone header is an eyebrow now (23c284a3c): 11 Bold Consolas, section ink, no glow.
                MainShellWindow.PaintSectionInk(NavSections.Companion);   // no shell here: paint the section the page lives in
                var probe = new TextBlock { Text = "probe", Theme = (ControlTheme)view.FindResource("CmpSectionTitleStyle")! };
                window.Content = new StackPanel { Children = { probe } };
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(11, probe.FontSize);
                Assert.Equal(FontWeight.Bold, probe.FontWeight);
                Assert.Equal(SectionInk(NavSections.Companion), Ink(probe));
                Assert.Null(probe.Effect);
                // Make Her Yours (19f5285e8): the Active line is Type.Body (13, TextSecondary).
                window.Content = view;
                Dispatcher.UIThread.RunJobs();
                var active = view.GetLogicalDescendants().OfType<TextBlock>()
                    .First(t => t.Theme == view.FindResource("Type.Body") && t.FontSize == 13 && Ink(t) == TextSecondary);
                Assert.NotNull(active);
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        });
    }
}
