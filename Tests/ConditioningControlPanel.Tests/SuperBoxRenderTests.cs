using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The gold V2 box builds offscreen for every effect, lit and dim, hosts a body that the dim
/// state never fades, and no dashboard tile wears a Super switch any more.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class SuperBoxRenderTests
{
    private static void Realize(FrameworkElement element, double width = 520, double height = 300)
    {
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(element);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(new Point(0, 0), new Size(width, height)));
        host.UpdateLayout();
    }

    [Fact]
    public void Box_builds_for_every_effect_dim_without_an_account()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            if (Environment.GetEnvironmentVariable("CCP_SUPER_ALL") == "1") return;
            foreach (var e in Enum.GetValues<SuperEffect>())
            {
                var box = new SuperBox { Effect = e };
                Realize(box);
                box.Refresh();
                Assert.True(box.ActualHeight > 0);
                Assert.False(box.IsLit);
                Assert.True(box.Picker.IsLockedNow);
                Assert.Equal(e, box.Picker.Effect);
                // Locked: Classic is what runs, so Classic is what the row and its line show.
                Assert.Equal(SuperMode.Classic, box.Picker.ShownMode);
                Assert.Equal(ConditioningControlPanel.Localization.Loc.Get(SuperModePick.LineKey(SuperMode.Classic)), box.ModeLine);
            }
        });
    }

    [Fact]
    public void A_locked_press_on_a_super_segment_stores_nothing_and_classic_stays_shown()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            if (Environment.GetEnvironmentVariable("CCP_SUPER_ALL") == "1") return;
            var box = new SuperBox { Effect = SuperEffect.Vortex };
            Realize(box);
            box.Picker.Activate(SuperMode.Both);
            box.Picker.Activate(SuperMode.SuperOnly);
            box.Picker.Activate(SuperMode.Classic); // Classic stays pressable: already shown, a no-op
            Assert.Equal(SuperMode.Classic, box.Picker.ShownMode);
        });
    }

    [Fact]
    public void Lit_and_dim_paint_differently_and_never_fade_the_body()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var body = new TextBlock { Text = "v2 row" };
            var box = new SuperBox { Effect = SuperEffect.FlickerDeck, Body = body };
            Realize(box);

            box.Paint(lit: true);
            var litBorder = ((SolidColorBrush)box.BorderBrush).Color;
            Assert.True(box.IsLit);
            Assert.Equal(1.0, Opacity(body));

            box.Paint(lit: false);
            var dimBorder = ((SolidColorBrush)box.BorderBrush).Color;
            Assert.False(box.IsLit);
            Assert.True(dimBorder.A < litBorder.A);
            // A v2 prize row is lit by its own ownership, never by the tier.
            Assert.Equal(1.0, Opacity(body));
            Assert.NotNull(VisualTreeHelper.GetParent(body));
            Assert.True(body.ActualHeight > 0);
        });
    }

    [Fact]
    public void The_pick_is_one_thin_row_and_super_only_appears_only_where_it_can_replace()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var e in Enum.GetValues<SuperEffect>())
            {
                var picker = new SuperModePicker { Effect = e };
                Realize(picker, 400, 60);
                Assert.Equal(SuperModeRule.Offered(e), picker.Modes);
                Assert.Equal(SuperModeRule.CanReplace(e), picker.Modes.Contains(SuperMode.SuperOnly));
                Assert.InRange(picker.ActualHeight, 16, 26); // one pill row, subtle
                Assert.True(picker.ActualWidth < 300);
                Assert.True(picker.Focusable);
            }
        });
    }

    [Fact]
    public void The_old_switch_and_the_use_only_afterglow_box_are_gone()
    {
        var root = SourceRoot();
        Assert.False(File.Exists(Path.Combine(root, "Controls", "SuperSwitch.cs")));
        var box = File.ReadAllText(Path.Combine(root, "Controls", "Super", "AfterglowOptionsBox.xaml"));
        Assert.DoesNotContain("super_afterglow_only", box);
        Assert.DoesNotContain("ChkOnly", box);
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    /// <summary>Opacity as the element is drawn: its own times every ancestor's up to the box.</summary>
    private static double Opacity(FrameworkElement e)
    {
        double o = 1;
        DependencyObject? d = e;
        while (d is UIElement u)
        {
            o *= u.Opacity;
            if (d is SuperBox) break;
            d = VisualTreeHelper.GetParent(d);
        }
        return o;
    }

    [Fact]
    public void No_dashboard_tile_carries_a_super_switch()
    {
        var root = SourceRoot();
        var dash = File.ReadAllText(Path.Combine(root, "Views", "Tabs", "SettingsTabView.xaml"));
        Assert.DoesNotContain(" Super=\"", dash);
        Assert.DoesNotContain(" SuperA=\"", dash);
        Assert.DoesNotContain(" SuperB=\"", dash);
        Assert.False(File.Exists(Path.Combine(root, "Controls", "SuperTileBadge.cs")));
    }

    [Theory]
    [InlineData(@"Features\FlashFeatureControl.xaml", "FlickerDeck")]
    [InlineData(@"Features\BubblePopFeatureControl.xaml", "InnerBloom")]
    [InlineData(@"Features\SubliminalFeatureControl.xaml", "Afterglow")]
    [InlineData(@"Features\SpiralFeatureControl.xaml", "Vortex")]
    [InlineData(@"Features\PinkFilterFeatureControl.xaml", "Creep")]
    [InlineData(@"Features\VideoFeatureControl.xaml", "LightsDown")]
    [InlineData(@"Features\BouncingTextFeatureControl.xaml", "Scrawl")]
    [InlineData(@"Views\Controls\Studio\BrainDrainFeatureControl.xaml", "Undertow")]
    public void Each_feature_page_hosts_one_gold_box(string page, string effect)
    {
        var root = SourceRoot();
        var xaml = File.ReadAllText(Path.Combine(root, page));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(xaml, "<ctl:SuperBox\\b"));
        Assert.Contains($"Effect=\"{effect}\"", xaml);
        Assert.DoesNotContain("SuperRow", xaml);
    }
}
