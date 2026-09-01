using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE PAGE OWNS ITS ADORNERS. Every card FX on a tab is an <c>Adorner</c> - TierFxBorder's tier
/// livery band, PerimeterCometAdorner, PremiumGateFx, the Assets/Exclusives/Quests sheens - and
/// <c>AdornerLayer.GetAdornerLayer</c> hands each one the layer of its nearest
/// <c>AdornerDecorator</c> or <c>ScrollContentPresenter</c> ancestor, falling back to the WINDOW's
/// own decorator when there is neither.
///
/// <para>That fallback is a z-order bug, not a detail: the window's adorner layer paints above
/// every child of MainWindow's root Grid, <c>Panel.ZIndex</c> included. So while the tab views were
/// bare children of that Grid, opening the nav rail flyout (NavSidebar, ZIndex 60) over the page
/// left the tease card's livery band shining straight through the rail - reported 2026-08-13 with a
/// screenshot of the band crossing the open rail beside "Companion".</para>
///
/// <para><b>Why this is a source read.</b> Realizing MainWindow in this suite is not affordable
/// (the reason NavRailFlyoutTests scrapes too), and the defect is a paint-order fact about a live
/// visual tree that no headless assertion could observe anyway. What can rot is the wrapper, so the
/// wrapper is what is pinned: a future hand that moves a tab view back out to the root Grid gets a
/// red test naming the reason instead of a shimmer leaking over the rail again.</para>
/// </summary>
public class PageAdornerScopeTests
{
    private static string MainWindowXaml() => SourceRoots.ReadProductFile("MainWindow", "MainWindow.xaml");

    [Fact]
    public void EveryTabViewSitsInsideThePageAdornerDecorator()
    {
        var xaml = MainWindowXaml();

        var open = xaml.IndexOf("<AdornerDecorator", StringComparison.Ordinal);
        Assert.True(open > 0,
            "MainWindow.xaml has no AdornerDecorator - tab adorners fall back to the window layer, "
            + "which paints over the nav rail flyout");

        var close = xaml.IndexOf("</AdornerDecorator>", open, StringComparison.Ordinal);
        Assert.True(close > open, "the page's AdornerDecorator is never closed");

        // It has to occupy the page cell, or it is scoping something other than the tabs.
        var openTag = xaml.Substring(open, xaml.IndexOf('>', open) - open);
        Assert.Contains("Grid.Row=\"4\"", openTag, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"1\"", openTag, StringComparison.Ordinal);

        var tabs = Regex.Matches(xaml, @"<views:(\w+) x:Name=""(\w+)""");
        Assert.True(tabs.Count >= 20, $"only {tabs.Count} tab views found - the scrape has drifted");

        var strays = tabs.Cast<Match>()
                         .Where(m => m.Index < open || m.Index > close)
                         .Select(m => m.Groups[2].Value)
                         .ToList();

        Assert.True(strays.Count == 0,
            "these tab views are outside the page AdornerDecorator, so their card FX adorners go to "
            + "the window layer and paint over the open nav rail: " + string.Join(", ", strays));
    }

    [Fact]
    public void TheRailStillOutranksThePage()
    {
        var xaml = MainWindowXaml();

        // The whole fix rests on the rail being a higher-ZIndex sibling of the decorator. If the
        // rail ever loses its lift, the flyout goes back UNDER the page and this stops meaning
        // anything - so the two halves of the contract are pinned together.
        var rail = Regex.Match(xaml, @"x:Name=""NavSidebar""[\s\S]{0,400}?Panel\.ZIndex=""(\d+)""");
        Assert.True(rail.Success, "NavSidebar no longer declares a Panel.ZIndex - the flyout is not lifted over the page");
        Assert.True(int.Parse(rail.Groups[1].Value, CultureInfo.InvariantCulture) > 0,
            "NavSidebar's Panel.ZIndex must stay above the page's default 0");

        var open = xaml.IndexOf("<AdornerDecorator", StringComparison.Ordinal);
        var openTag = xaml.Substring(open, xaml.IndexOf('>', open) - open);
        Assert.DoesNotContain("Panel.ZIndex", openTag, StringComparison.Ordinal);
    }
}
