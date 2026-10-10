using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The one-time "What moved" card (nav rework, 2026-10-06): upgrades see it once, fresh installs
/// never do, every row lands on a real destination, and the card realizes offscreen.
/// </summary>
public class WhatMovedCardTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null);
        return dir!.FullName;
    }

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(1, false, false)]
    [InlineData(0, true, false)]
    [InlineData(3, true, false)]
    public void Offered_once_and_only_to_upgrades(int shown, bool fresh, bool expected)
        => Assert.Equal(expected, WhatMovedPlan.ShouldOffer(shown, fresh));

    [Fact]
    public void Three_to_five_rows_each_landing_somewhere_real()
    {
        var rows = WhatMovedPlan.Rows;
        Assert.InRange(rows.Count, 3, 5);
        Assert.Equal(rows.Count, rows.Select(r => r.Id).Distinct().Count());

        var en = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(
            Source("Localization", "Languages", "en.json"))!;
        foreach (var row in rows)
        {
            Assert.True(en.ContainsKey(row.TextKey), row.TextKey + " is not in en.json");
            Assert.False(row.Text.Contains('!'), "no exclamation marks in chrome: " + row.TextKey);
            if (row.SettingsSection != null)
            {
                Assert.Equal("appsettings", row.Tab);
                Assert.Contains(NavSections.Find(NavSections.Settings)!.Tabs, t => t.Key == row.SettingsSection);
            }
            else
            {
                Assert.NotNull(NavSections.SectionForTab(row.Tab));
            }
        }
    }

    [Fact]
    public void Goes_through_the_passive_ladder_and_help_can_replay_it()
    {
        var code = Source("MainWindow", "MainWindow.WhatMoved.cs");
        Assert.Contains("PresentOrInbox(", code);
        Assert.DoesNotContain("ShowDialog(", code);
        Assert.Contains("partial void GlowNavTarget(string tabKey);", code);

        var ctor = Source("MainWindow", "MainWindow.xaml.cs");
        Assert.Contains("OfferWhatMovedIfNeeded(freshInstall: true)", ctor);
        Assert.Contains("OfferWhatMovedIfNeeded(freshInstall: false)", ctor);

        var xaml = Source("MainWindow", "MainWindow.xaml");
        Assert.Contains("Click=\"BtnWhatMovedReplay_Click\"", xaml);
        Assert.Contains("{loc:Str help_whatmoved}", xaml);
    }

    [Fact]
    public void Realizes_offscreen()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var card = new WhatMovedCard(null, readMode: true);
            var root = (FrameworkElement)card.Content;
            card.Content = null;
            var host = new System.Windows.Controls.Grid { Width = 520 };
            host.Children.Add(root);
            host.Measure(new Size(520, double.PositiveInfinity));
            host.Arrange(new Rect(host.DesiredSize));
            host.UpdateLayout();
            Assert.True(host.ActualHeight > 200, "card collapsed to " + host.ActualHeight);

            // Desk look: CCP_RENDER_OUT=<dir> writes the card as PNG.
            var outDir = Environment.GetEnvironmentVariable("CCP_RENDER_OUT");
            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);
                var bmp = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight),
                                                 96, 96, PixelFormats.Pbgra32);
                bmp.Render(host);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var fs = File.Create(Path.Combine(outDir, "whatmoved-card.png"));
                enc.Save(fs);
            }
        });
    }
}
