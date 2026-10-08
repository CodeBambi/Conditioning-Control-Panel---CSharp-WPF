using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// WPF 7.1.5 PremiumPageTests (the shelf-order half): the Premium page groups the roster Basic,
/// Prime, Free, open doors first in each group, every roster entry exactly once. Pure, so it is
/// pinned in Core where both heads read it.
/// </summary>
public class PremiumShelfOrderTests
{
    [Fact]
    public void Every_roster_entry_stands_on_exactly_one_shelf()
    {
        var groups = PremiumShelfOrder.Arrange(ExclusiveFeature.All, f => f.Key, f => f.Tier, _ => false);
        var keys = groups.SelectMany(g => g.Items.Select(f => f.Key)).ToList();
        Assert.Equal(ExclusiveFeature.All.Count, keys.Count);
        Assert.Equal(ExclusiveFeature.All.Select(f => f.Key).OrderBy(k => k), keys.OrderBy(k => k));
        Assert.Equal(new[] { PremiumGroup.Basic, PremiumGroup.Prime, PremiumGroup.Free }, groups.Select(g => g.Group));
    }

    [Fact]
    public void The_groups_follow_the_price_tag_and_Graded_Intake_is_Prime()
    {
        foreach (var f in ExclusiveFeature.All)
        {
            var g = PremiumShelfOrder.GroupOf(f.Key, f.Tier);
            if (f.Tier == 1) Assert.Equal(PremiumGroup.Basic, g);
            else if (f.Tier == 2) Assert.Equal(PremiumGroup.Prime, g);
        }
        Assert.Equal(PremiumGroup.Prime, PremiumShelfOrder.GroupOf("gradedintake", 0));
        Assert.Equal(PremiumGroup.Free, PremiumShelfOrder.GroupOf("backroom", 0));
        Assert.Equal(PremiumGroup.Free, PremiumShelfOrder.GroupOf("justdrop", 0));
    }

    [Fact]
    public void Open_doors_lead_their_group_and_roster_order_holds_in_each_half()
    {
        var roster = new[] { ("a", 1), ("b", 1), ("c", 1), ("d", 2), ("e", 2), ("f", 0) };
        var open = new HashSet<string> { "c", "e" };
        var groups = PremiumShelfOrder.Arrange(roster, r => r.Item1, r => r.Item2, r => open.Contains(r.Item1));
        Assert.Equal(new[] { "c", "a", "b" }, groups[0].Items.Select(r => r.Item1));
        Assert.Equal(new[] { "e", "d" }, groups[1].Items.Select(r => r.Item1));
        Assert.Equal(new[] { "f" }, groups[2].Items.Select(r => r.Item1));
    }

    [Fact]
    public void An_empty_group_is_left_out()
    {
        var groups = PremiumShelfOrder.Arrange(new[] { ("x", 2) }, r => r.Item1, r => r.Item2, _ => true);
        Assert.Single(groups);
        Assert.Equal(PremiumGroup.Prime, groups[0].Group);
    }

    [Theory]
    [InlineData(true, null, RemoteSubAddOutcome.Added)]
    [InlineData(false, null, RemoteSubAddOutcome.NotCarried)]
    [InlineData(false, "offline", RemoteSubAddOutcome.Unreachable)]
    [InlineData(false, "invalid", RemoteSubAddOutcome.NotAName)]
    [InlineData(false, "timeout", RemoteSubAddOutcome.Unreachable)]
    public void A_sub_probe_reads_as_one_outcome(bool ok, string? error, RemoteSubAddOutcome expected)
        => Assert.Equal(expected, RemoteSubAddMessages.Classify(ok, error));
}
