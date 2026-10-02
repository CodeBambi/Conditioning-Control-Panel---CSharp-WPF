using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Invites;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The header invite ticket: when it shows, and how often it wobbles.</summary>
public class InviteTicketRuleTests
{
    private static InviteMine Mine(params InviteSlotState[] states)
    {
        var slots = new List<InviteSlot>();
        foreach (var s in states) slots.Add(new InviteSlot("ABCD-EFGH", s, null, null));
        return new InviteMine(true, new InviteSnapshot(slots, null, 0));
    }

    [Fact]
    public void Shows_when_this_month_has_an_open_code()
    {
        Assert.True(InviteTicketRule.ShouldShow(Mine(InviteSlotState.Open)));
        Assert.True(InviteTicketRule.ShouldShow(Mine(InviteSlotState.Converted, InviteSlotState.Open)));
    }

    [Fact]
    public void Hidden_when_every_code_is_taken()
    {
        Assert.False(InviteTicketRule.ShouldShow(Mine(InviteSlotState.Trying)));
        Assert.False(InviteTicketRule.ShouldShow(Mine(InviteSlotState.Converted, InviteSlotState.Trying)));
        Assert.False(InviteTicketRule.ShouldShow(Mine()));
    }

    [Fact]
    public void Hidden_offline_signed_out_or_not_a_subscriber()
    {
        Assert.False(InviteTicketRule.ShouldShow(null));
        Assert.False(InviteTicketRule.ShouldShow(InviteMine.Unreachable));
        Assert.False(InviteTicketRule.ShouldShow(new InviteMine(true, null, 3)));
        // A snapshot that somehow arrived on an unreachable read is still not trusted.
        var open = Mine(InviteSlotState.Open);
        Assert.False(InviteTicketRule.ShouldShow(open with { Reachable = false }));
    }

    [Fact]
    public void Invite_link_is_for_subscribers_with_codes()
    {
        Assert.True(InviteTicketRule.OffersInviteLink(true, false));
        Assert.False(InviteTicketRule.OffersInviteLink(true, true));
        Assert.False(InviteTicketRule.OffersInviteLink(false, false));
    }

    [Fact]
    public void Wobble_gap_stays_inside_its_window()
    {
        var rng = new Random(7);
        for (int i = 0; i < 500; i++)
        {
            var gap = InviteTicketRule.NextWobble(rng);
            Assert.InRange(gap, InviteTicketRule.WobbleMin, InviteTicketRule.WobbleMax);
        }
    }
}
