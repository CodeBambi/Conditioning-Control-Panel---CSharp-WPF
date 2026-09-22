using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Circe's tab as it actually lays out. A missing StaticResource in a tab view is a crash the
/// first time someone opens the page and nothing at compile time, so the page is realized here:
/// with no service it shows the unlinked state and a dead Link button, and the price list
/// builds one switch per row of the price table.
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
    public void With_no_service_the_page_shows_what_it_is_and_a_link_button_that_cannot_be_pressed()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.OnTabShown();
            Realize(tab, 900, 700);

            Assert.Equal(Visibility.Visible, tab.UnlinkedPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.LinkedPanel.Visibility);
            Assert.False(tab.BtnLink.IsEnabled);
        });
    }

    [Fact]
    public void The_price_list_is_one_switch_per_row_of_the_price_table_and_none_is_clipped()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            tab.PricesExpander.IsExpanded = true;
            tab.CustomizeHost.Visibility = Visibility.Visible;
            tab.BuildPriceRows();
            tab.BuildPriceRows(); // a second visit must not double the rows
            Realize(tab, 700, 2400);

            var toggles = Descendants(tab.CostRows).Concat(Descendants(tab.EarnRows)).OfType<CheckBox>().ToList();
            Assert.Equal(TabPrices.All.Count, toggles.Count);
            Assert.Equal(TabPrices.All.Select(p => p.Id).OrderBy(x => x), toggles.Select(t => (string)t.Tag).OrderBy(x => x));
            Assert.All(toggles, t => Assert.True(t.ActualWidth > 0, "a price switch collapsed to nothing"));
        });
    }

    [Fact]
    public void The_price_list_opens_behind_Customize_so_the_presets_are_what_you_meet_first()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            tab.PricesExpander.IsExpanded = true;
            tab.RefreshPresets();
            Realize(tab, 700, 1400);

            Assert.Equal(Visibility.Collapsed, tab.CustomizeHost.Visibility);
            // With no set on, no chip is lit and the Custom chip is not even there.
            Assert.Equal(Visibility.Collapsed, tab.BtnPresetCustom.Visibility);
            foreach (var chip in new[] { tab.BtnPresetGentle, tab.BtnPresetStrict, tab.BtnPresetCirce })
                Assert.True(chip.ActualWidth > 0, "a preset chip collapsed to nothing");
            Assert.False(string.IsNullOrEmpty(tab.TxtPresetHint.Text), "the preset line says nothing");
        });
    }

    [Fact]
    public void The_consent_card_and_the_hold_line_start_out_of_the_way()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            Realize(tab, 700, 1400);

            // Nothing about the first switch-on is on screen until the switch is pressed.
            Assert.Equal(Visibility.Collapsed, tab.ConsentCard.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.TxtHold.Visibility);
            // The hero draws nothing it cannot know with no service behind it.
            Assert.Equal(Visibility.Collapsed, tab.HeroClockRow.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.TxtHeroEnds.Visibility);
        });
    }

    [Fact]
    public void The_cap_meter_is_two_star_columns_that_always_add_up_to_one()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            Realize(tab, 700, 1400);

            Assert.Equal(GridUnitType.Star, tab.CapFilled.Width.GridUnitType);
            Assert.Equal(GridUnitType.Star, tab.CapRest.Width.GridUnitType);
            Assert.Equal(1.0, tab.CapFilled.Width.Value + tab.CapRest.Width.Value, 6);
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
