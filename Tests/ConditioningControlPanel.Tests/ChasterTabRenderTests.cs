using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Circe's tab as it actually lays out. A missing StaticResource in a tab view is a crash the
/// first time someone opens the page and nothing at compile time, so the page is realized here:
/// with no service it shows the unlinked hero and a dead Link button; linked, the menu builds one
/// row per price on two boards, the chain draws a link a day, the paper tag says what is on the
/// tab, and the trailer dresses itself for whichever row it is aimed at.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class ChasterTabRenderTests
{
    static ChasterTabRenderTests()
    {
        // The trailer's scene is a WebView2; with no window behind the page there is no browser
        // to build, and the still picture under it is the trailer.
        ChasterTrailerView.BrowserEnabled = false;
    }

    [Fact]
    public void Every_row_maps_to_a_scene_the_trailers_page_defines_and_the_page_can_mount_one()
    {
        var page = System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "web", "chaster", "trailers.html");
        Assert.True(File.Exists(page), "trailers.html is missing");
        var html = File.ReadAllText(page);
        Assert.Contains("window.__mount", html);
        Assert.Contains("CT.run=function(wrap,id)", html);
        Assert.Contains("CT.I.receipt=", html);
        foreach (var id in TabPrices.All.Select(p => p.Id).Append(TabMenuCopy.JackpotId))
        {
            var scene = TabMenuCopy.VignetteFor(id);
            Assert.Contains("V." + scene + "=", html);
        }
        Assert.Equal("typo", TabMenuCopy.VignetteFor("lockcard"));
        Assert.Equal("program", TabMenuCopy.VignetteFor("program_skipped"));
        Assert.Equal("bubbles", TabMenuCopy.VignetteFor("natasha"));
        Assert.Equal("escape", TabMenuCopy.VignetteFor("escape"));
        // the mount script never lets a quote through to the page
        Assert.Equal("window.__mount && window.__mount('typo')", ChasterTrailerView.MountScript("typo"));
        Assert.DoesNotContain("'", ChasterTrailerView.MountScript("a'b").Replace("__mount('", "").Replace("')", ""));
    }

    private static void Realize(FrameworkElement element, double width, double height)
    {
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(element);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(new Point(0, 0), new Size(width, height)));
        host.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    private static List<ToggleButton> Rows(ChasterTabView tab) =>
        Descendants(tab.CostRows).Concat(Descendants(tab.EarnRows)).OfType<ToggleButton>().ToList();

    [Fact]
    public void With_no_service_the_page_is_the_ask_and_a_link_button_that_cannot_be_pressed()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.OnTabShown();
            Realize(tab, 1000, 700);

            Assert.Equal(Visibility.Visible, tab.UnlinkedPanel.Visibility);
            Assert.Equal(Visibility.Visible, tab.FactRow.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.LinkedPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.SwitchPill.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.PaperTag.Visibility);
            Assert.False(tab.BtnLink.IsEnabled);
            // The hero draws nothing it cannot know with no service behind it.
            Assert.Equal(Visibility.Collapsed, tab.HeroClockRow.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.TxtHeroEnds.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.HeroPills.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.ChainRow.Visibility);
            Assert.False(tab.Trailer.IsOpen);
        });
    }

    [Fact]
    public void The_menu_is_one_row_per_price_on_two_boards_and_every_row_is_a_picture_a_name_a_where_and_a_stamp()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            tab.BuildMenu();
            tab.BuildMenu(); // a second visit must not double the rows
            Realize(tab, 1000, 2400);

            var rows = Rows(tab);
            Assert.Equal(TabPrices.All.Count, rows.Count);
            Assert.Equal(TabPrices.All.Select(p => p.Id).OrderBy(x => x), rows.Select(t => (string)t.Tag).OrderBy(x => x));
            Assert.All(rows, t => Assert.True(t.ActualWidth > 0, "a menu row collapsed to nothing"));

            // Costs on the red board, earn-backs on the mint one, nothing on both.
            var costIds = Descendants(tab.CostRows).OfType<ToggleButton>().Select(t => (string)t.Tag).ToList();
            var earnIds = Descendants(tab.EarnRows).OfType<ToggleButton>().Select(t => (string)t.Tag).ToList();
            Assert.All(costIds, id => Assert.True(TabPrices.Find(id)!.Seconds > 0, id + " is on the wrong board"));
            Assert.All(earnIds, id => Assert.True(TabPrices.Find(id)!.Seconds < 0, id + " is on the wrong board"));

            foreach (var row in rows)
            {
                var id = (string)row.Tag;
                var texts = Descendants(row).OfType<TextBlock>().Select(t => t.Text).ToList();
                // a name, a where line and a price stamp; the tier sign is a picture, not text
                Assert.True(texts.Count >= 3, id + " lost a line");
                Assert.Contains(texts, t => t.StartsWith("+") || t.StartsWith("-"));
                Assert.Contains(texts, t => t == Localization.Loc.Get(TabMenuCopy.WhereKey(id)));
                Assert.True(Descendants(row).OfType<Image>().Any(), id + " has no picture");
                var wantsSign = TabMenuCopy.BadgeTier(TabPrices.Find(id)!.Gate) > 0;
                Assert.Equal(wantsSign, Descendants(row).OfType<TierBadge>().Any());
            }
        });
    }

    [Fact]
    public void Every_row_with_a_picture_points_at_a_png_that_ships()
    {
        var resources = System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources");
        foreach (var id in TabMenuCopy.ArtIds)
        {
            var art = TabMenuCopy.ArtFor(id)!;
            Assert.True(File.Exists(System.IO.Path.Combine(resources, art.Replace("/", System.IO.Path.DirectorySeparatorChar.ToString()))), id + " points at a missing " + art);
        }
        // and every price row has one
        Assert.All(TabPrices.All, p => Assert.NotNull(TabMenuCopy.ArtFor(p.Id)));
    }

    [Fact]
    public void A_row_with_no_copy_still_prints_its_id_and_the_sign_follows_the_gate()
    {
        Assert.Equal("nobody_wrote_this", TabMenuCopy.ShortName("nobody_wrote_this", _ => null));
        Assert.Equal("nobody_wrote_this", TabMenuCopy.ShortName("nobody_wrote_this", key => key));
        Assert.Equal("Typo", TabMenuCopy.ShortName("typo", _ => "Typo"));
        Assert.Equal(0, TabMenuCopy.BadgeTier(TabPriceGate.Free));
        Assert.Equal(0, TabMenuCopy.BadgeTier(TabPriceGate.Sparkles));
        Assert.Equal(1, TabMenuCopy.BadgeTier(TabPriceGate.Tier1));
        Assert.Equal(2, TabMenuCopy.BadgeTier(TabPriceGate.Tier2));
    }

    [Fact]
    public void The_keys_start_dark_with_nothing_on_and_the_menu_is_in_view_without_pressing_anything()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            tab.RefreshPresets();
            tab.BuildMenu();
            Realize(tab, 1000, 2400);

            foreach (var key in new[] { tab.BtnPresetGentle, tab.BtnPresetStrict, tab.BtnPresetCirce, tab.BtnPresetCustom })
            {
                Assert.True(key.ActualWidth > 0, "a key collapsed to nothing");
                Assert.NotEqual(true, key.IsChecked);
            }
            Assert.True(tab.CostBoard.ActualHeight > 0 && tab.EarnBoard.ActualHeight > 0, "a board did not lay out");
            Assert.True(tab.JackpotRow.ActualWidth > 0, "the jackpot row did not lay out");
        });
    }

    [Fact]
    public void The_chain_is_a_link_a_day_plus_the_one_that_opens_and_shrinks_for_a_long_lock()
    {
        Assert.Equal(0, ChasterTabView.ChainLinksFor(null));
        Assert.Equal(0, ChasterTabView.ChainLinksFor(TimeSpan.Zero));
        Assert.Equal(2, ChasterTabView.ChainLinksFor(TimeSpan.FromHours(3)));
        Assert.Equal(14, ChasterTabView.ChainLinksFor(TimeSpan.FromDays(12.1)));
        Assert.Equal(ChasterTabView.ChainMaxLinks, ChasterTabView.ChainLinksFor(TimeSpan.FromDays(400)));

        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            tab.BuildChain(TimeSpan.FromDays(12.1));
            Realize(tab, 1000, 1400);

            Assert.Equal(Visibility.Visible, tab.ChainRow.Visibility);
            var links = tab.Chain.Children.OfType<FrameworkElement>().ToList();
            Assert.Equal(14, links.Count);
            Assert.All(links, l => Assert.True(l.ActualWidth > 0));
            // the last link is the dashed one that opens, the first is tonight's
            Assert.IsType<Rectangle>(links[^1]);
            Assert.NotNull(((Rectangle)links[^1]).StrokeDashArray);
            Assert.IsType<Border>(links[0]);

            var wide = links[0].ActualWidth;
            tab.BuildChain(TimeSpan.FromDays(35));
            tab.UpdateLayout();
            var longLinks = tab.Chain.Children.OfType<FrameworkElement>().ToList();
            Assert.Equal(36, longLinks.Count);
            Assert.True(longLinks[0].ActualWidth < wide, "a long lock's links did not shrink");
            Assert.True(tab.Chain.ActualWidth <= 1000, "the chain ran off the page");

            tab.BuildChain(null);
            Assert.Equal(Visibility.Collapsed, tab.ChainRow.Visibility);
        });
    }

    [Fact]
    public void The_paper_tag_says_what_is_on_the_tab_and_stamps_it_unpaid_clear_or_credit()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            tab.PaperTag.Visibility = Visibility.Visible;
            tab.BuildChain(TimeSpan.FromDays(3));
            Realize(tab, 1000, 1400);

            tab.RefreshTag(750);
            Assert.Equal("+12:30", tab.TxtTagAmount.Text);
            Assert.Equal("UNPAID", tab.TxtTagStamp.Text);
            Assert.Equal("+12:30", tab.TxtChainTag.Text);
            Assert.Equal(Visibility.Visible, tab.ChainTag.Visibility);

            tab.RefreshTag(0);
            Assert.Equal("0:00", tab.TxtTagAmount.Text);
            Assert.Equal("CLEAR", tab.TxtTagStamp.Text);
            Assert.Equal(Visibility.Collapsed, tab.ChainTag.Visibility);

            tab.RefreshTag(-90);
            Assert.Equal("-1:30", tab.TxtTagAmount.Text);
            Assert.Equal("CREDIT", tab.TxtTagStamp.Text);
            Assert.True(tab.PaperTag.ActualWidth > 0 && tab.PaperTag.ActualHeight > 0, "the tag did not lay out");
        });
    }

    [Fact]
    public void The_trailer_dresses_itself_for_the_row_it_is_aimed_at_and_closes_clean()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            tab.BuildMenu();
            Realize(tab, 1000, 2400);

            var escape = Rows(tab).Single(r => (string)r.Tag == "escape");
            tab.OpenTrailer(escape);
            // A popup with no window behind it may not open for real; the aim and the dressing must hold anyway.
            Assert.Equal("escape", tab.TrailerId);
            Assert.Equal(Localization.Loc.Get(TabMenuCopy.FlavourKey("escape")), tab.TxtTrailerFlavour.Text);
            Assert.Equal(Localization.Loc.Get(TabMenuCopy.WhyKey("escape")), tab.TxtTrailerWhy.Text);
            Assert.NotNull(tab.TrailerArt.Source);
            Assert.Equal(Visibility.Collapsed, tab.TrailerWeb.Visibility); // no browser in the harness: the still picture is the trailer
            Assert.IsType<TierBadge>(tab.TrailerBadgeHost.Child); // Lockdown is tier 1

            var typo = Rows(tab).Single(r => (string)r.Tag == "typo");
            tab.OpenTrailer(typo);
            Assert.Equal("typo", tab.TrailerId);
            Assert.Null(tab.TrailerBadgeHost.Child); // a free row wears no sign

            tab.HideTrailer();
            Assert.False(tab.Trailer.IsOpen);
            Assert.Null(tab.TrailerId);
            tab.HideTrailer(); // twice is fine
        });
    }

    [Fact]
    public void The_consent_card_and_the_receipt_start_out_of_the_way()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            Realize(tab, 1000, 1400);

            // Nothing about the first switch-on is on screen until the switch is pressed.
            Assert.Equal(Visibility.Collapsed, tab.ConsentCard.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.ReceiptHost.Visibility);
        });
    }

    [Fact]
    public void The_cap_meter_is_a_fill_inside_a_measured_track_and_starts_empty()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            Realize(tab, 1000, 1400);

            Assert.True(tab.CapTrack.ActualWidth > 0, "the cap track did not lay out");
            Assert.Equal(0, tab.CapFill.Width);
            Assert.True(tab.CapFill.Width <= tab.CapTrack.ActualWidth);
        });
    }

    [Fact]
    public void The_bill_realizes_as_a_receipt_with_its_lines_its_figures_and_a_net_stamp()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var start = new DateTime(2026, 9, 22, 8, 0, 0, DateTimeKind.Utc);
            var bill = TabBill.Build(new[]
            {
                new TabEntry { AtUtc = start.AddMinutes(1), EventId = "typo", Seconds = 45, Count = 3 },
                new TabEntry { AtUtc = start.AddMinutes(2), EventId = "session", Seconds = -600, Count = 1 },
            }, start, 120);

            var receipt = new ChasterReceiptView();
            receipt.Show(bill);
            Realize(receipt, 500, 500);

            var text = Descendants(receipt).OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("+0:45", text);
            Assert.Contains("-10:00", text);
            Assert.Contains(text, t => t.Contains("NET") && t.Contains("-9:15"));
            Assert.True(receipt.ActualWidth > 0 && receipt.ActualHeight > 0, "the receipt did not lay out");
        });
    }

    [Fact]
    public void An_empty_run_prints_one_line_and_no_stamp()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var receipt = new ChasterReceiptView();
            receipt.Show(TabBill.Build(Array.Empty<TabEntry>(), DateTime.UtcNow, 0));
            receipt.Show(null); // and a missing service is the same thing, not a crash
            Realize(receipt, 500, 500);

            var text = Descendants(receipt).OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.DoesNotContain(text, t => t.Contains("NET"));
            Assert.True(receipt.ActualHeight > 0, "the empty receipt did not lay out");
        });
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
