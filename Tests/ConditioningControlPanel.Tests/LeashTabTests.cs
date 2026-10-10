using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls.Primitives;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Services.Leash;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Social &gt; Leash (nav rework 2026-10-06): the page hosts LeashDrawerSection for both roles,
/// the cut is on it and never priced, the "?" explainer is reachable, and the page is never a
/// way round the punishment gate (the gate is a panel overlay above every page).
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class LeashTabTests
{
    private static void Run(Action body) => WpfRenderHarness.OnStaThread(() =>
    {
        LeashFx.ForceStill = true;
        try { body(); } finally { LeashFx.ForceStill = false; }
    });

    private static FrameworkElement? Find(DependencyObject root, string tag)
    {
        if (root is FrameworkElement fe && fe.Tag as string == tag) return fe;
        foreach (var c in LogicalTreeHelper.GetChildren(root))
            if (c is DependencyObject d && Find(d, tag) is { } hit) return hit;
        return null;
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    [Fact]
    public void Leash_page_hosts_the_cut_and_the_cut_is_never_priced()
    {
        Run(() =>
        {
            var svc = FakeLeashService.Sample();
            var page = new LeashTabView(() => svc);
            Assert.NotNull(page.Section.SelfCard);
            Assert.NotNull(Find(page, "leash-cut"));
            Assert.Single(page.Section.HolderCards);
            Assert.False(page.ShowingEmpty);
            Assert.Contains("leash_cut", TabPrices.NeverPriced);
            SocialShots.Shot(page, "leash", 900, 760);
        });
    }

    [Fact]
    public void Leash_page_explainer_is_one_click_away()
    {
        Run(() =>
        {
            var page = new LeashTabView(() => FakeLeashService.Sample());
            Assert.Equal("leash-help:leashed", page.Help.Tag);
            var asked = new List<LeashExplainRole>();
            void Note(LeashExplainRole r) => asked.Add(r);
            LeashExplainHost.Requested += Note;
            try { page.Help.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
            finally { LeashExplainHost.Requested -= Note; }
            Assert.Contains(LeashExplainRole.Leashed, asked);
        });
    }

    [Fact]
    public void Leash_page_empty_state_points_at_friends()
    {
        Run(() =>
        {
            var svc = new FakeLeashService(LeashSnapshot.Empty);
            var page = new LeashTabView(() => svc);
            Assert.True(page.ShowingEmpty);
            Assert.Equal("leash-page-open-friends", page.EmptyButton.Tag);
            Assert.Null(Find(page, "leash-cut"));
            SocialShots.Shot(page, "leash-empty", 900, 420);
        });
    }

    [Fact]
    public void Leash_page_never_claims_escape()
    {
        Run(() =>
        {
            var page = new LeashTabView(() => FakeLeashService.Sample());
            Assert.False(EscapeClaim.InASurface(page.Section));
        });
    }

    [Fact]
    public void Leash_page_is_no_way_round_the_gate()
    {
        var root = Root();
        // The page and its registration never touch the gate or end a leash task.
        var page = File.ReadAllText(Path.Combine(root, "Views", "Tabs", "LeashTabView.xaml.cs"))
                 + File.ReadAllText(Path.Combine(root, "MainWindow", "MainWindow.SocialTabs.cs"));
        foreach (var banned in new[] { "HideLeashGate", "_leashGate", "LeashGateCard", "EndLeashTask", "Dismiss(" })
            Assert.DoesNotContain(banned, page);

        // The gate takes the remote overlay's cell (rows 1-5, both columns) one layer under it.
        var leash = File.ReadAllText(Path.Combine(root, "MainWindow", "MainWindow.Leash.cs"));
        Assert.Contains("Grid.SetRow(_leashGate, Grid.GetRow(RemoteControlOverlay))", leash);
        Assert.Contains("Grid.SetColumnSpan(_leashGate, Grid.GetColumnSpan(RemoteControlOverlay))", leash);
        Assert.Contains("Panel.SetZIndex(_leashGate, Panel.GetZIndex(RemoteControlOverlay) - 1)", leash);

        var xaml = File.ReadAllText(Path.Combine(root, "MainWindow", "MainWindow.xaml"));
        var overlay = Regex.Match(xaml, "<Border x:Name=\"RemoteControlOverlay\"[^>]*>").Value;
        Assert.Contains("Grid.Row=\"1\"", overlay);
        Assert.Contains("Grid.RowSpan=\"5\"", overlay);
        Assert.Contains("Grid.ColumnSpan=\"2\"", overlay);
        // Lane pages live in LaneTabHost, inside the page cell the overlay covers.
        Assert.Contains("x:Name=\"LaneTabHost\"", xaml);
    }
}
