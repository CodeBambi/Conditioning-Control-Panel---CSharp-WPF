using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Circe's tab as it actually lays out. A missing StaticResource in a tab view is a crash the
/// first time someone opens the page and nothing at compile time, so the page is realized here:
/// with no service it shows the unlinked hero and a dead Link button, and the price chips build
/// one per row of the price table.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class ChasterTabRenderTests
{
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
            Assert.False(tab.BtnLink.IsEnabled);
            // The hero draws nothing it cannot know with no service behind it.
            Assert.Equal(Visibility.Collapsed, tab.HeroClockRow.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.TxtHeroEnds.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.HeroPills.Visibility);
        });
    }

    [Fact]
    public void The_price_chips_are_one_per_row_of_the_price_table_and_none_is_clipped()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            tab.CustomizeHost.Visibility = Visibility.Visible;
            tab.BuildPriceRows();
            tab.BuildPriceRows(); // a second visit must not double the chips
            Realize(tab, 1000, 2400);

            var chips = Descendants(tab.CostRows).Concat(Descendants(tab.EarnRows)).OfType<ToggleButton>().ToList();
            Assert.Equal(TabPrices.All.Count, chips.Count);
            Assert.Equal(TabPrices.All.Select(p => p.Id).OrderBy(x => x), chips.Select(t => (string)t.Tag).OrderBy(x => x));
            Assert.All(chips, t => Assert.True(t.ActualWidth > 0, "a price chip collapsed to nothing"));
            // Every chip carries a name and a figure, nothing more.
            Assert.All(chips, t => Assert.Equal(2, Descendants(t).OfType<TextBlock>().Count()));
        });
    }

    [Fact]
    public void The_chips_open_behind_the_fourth_tile_so_the_presets_are_what_you_meet_first()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            tab.RefreshPresets();
            Realize(tab, 1000, 1400);

            Assert.Equal(Visibility.Collapsed, tab.CustomizeHost.Visibility);
            // With no set on, no tile is lit, the fourth included.
            foreach (var tile in new[] { tab.BtnPresetGentle, tab.BtnPresetStrict, tab.BtnPresetCirce, tab.BtnPresetCustom })
            {
                Assert.True(tile.ActualWidth > 0, "a preset tile collapsed to nothing");
                Assert.NotEqual(true, tile.IsChecked);
            }
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
}
