using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
            tab.BuildPriceRows();
            tab.BuildPriceRows(); // a second visit must not double the rows
            Realize(tab, 700, 2000);

            var toggles = Descendants(tab.CostRows).Concat(Descendants(tab.EarnRows)).OfType<CheckBox>().ToList();
            Assert.Equal(TabPrices.All.Count, toggles.Count);
            Assert.Equal(TabPrices.All.Select(p => p.Id).OrderBy(x => x), toggles.Select(t => (string)t.Tag).OrderBy(x => x));
            Assert.All(toggles, t => Assert.True(t.ActualWidth > 0, "a price switch collapsed to nothing"));
        });
    }
}
