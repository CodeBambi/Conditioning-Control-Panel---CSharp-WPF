using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.FirstShow;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class FirstShowDeckTests
{
    [Fact]
    public void Visible_pictures_cannot_be_dealt_twice_even_across_effect_types()
    {
        var deck = new FirstShowDeck(8,42);
        var visible = new List<(int Media,int Slot)>();
        for (int i=0;i<8;i++) visible.Add(deck.Next(visible)!.Value);
        Assert.Equal(8,visible.Select(p => p.Media).Distinct().Count());
        Assert.Null(deck.Next(visible));
        var expired = visible[0]; visible.RemoveAt(0);
        var next = deck.Next(visible)!.Value;
        Assert.Equal(expired.Media,next.Media);
        Assert.NotEqual(expired.Slot,next.Slot);
    }

    [Fact]
    public void Returning_pictures_change_position_and_every_picture_gets_a_turn()
    {
        var deck = new FirstShowDeck(8,19);
        var previous = new Dictionary<int,int>();
        for(int cycle=0;cycle<10;cycle++)
        {
            var cycleMedia = new HashSet<int>();
            for(int i=0;i<8;i++)
            {
                var next=deck.Next(new List<(int Media,int Slot)>())!.Value;
                Assert.True(cycleMedia.Add(next.Media));
                if(previous.TryGetValue(next.Media,out int slot)) Assert.NotEqual(slot,next.Slot);
                previous[next.Media]=next.Slot;
            }
        }
    }
}
