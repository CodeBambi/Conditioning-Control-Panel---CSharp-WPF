using System.Linq;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Prizes;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The one rule behind every champagne "v2" pill: the dashboard's Flashes tile, its Bubble Pop
/// tile and the side rail's chips. Three surfaces reading three copies of "which grants count"
/// is how a wall and a rail end up disagreeing about the same account, so the matrix is pinned
/// here. Pure rules only - no WPF, no App, no PrizeGrants static to attach.
/// </summary>
public class V2BadgesTests
{
    // ---------------------------------------------------------------- the two rules

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    // Jackpot Remix is a FLASH prize, so it lights the flashes pill on its own.
    [InlineData(false, false, true, true)]
    [InlineData(true, true, true, true)]
    public void FlashRule_lights_for_any_flash_prize(bool drift, bool pendulum, bool remix, bool expected)
        => Assert.Equal(expected, V2Badges.FlashRule(drift, pendulum, remix));

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void BubbleRule_lights_for_either_ambient_motion(bool rain, bool spiral, bool expected)
        => Assert.Equal(expected, V2Badges.BubbleRule(rain, spiral));

    // ---------------------------------------------------------------- the rail's table

    [Theory]
    [InlineData("door.studio")]
    [InlineData("tab.studio")]
    public void The_Studio_chip_fronts_both_families(string id)
    {
        // Flashes and Bubble Pop are rack modules, not palette rows: both mosaic tiles call
        // OpenStudioModule and land on the rack, so the Studio chip is the one a user can pin
        // for either. That makes it the only chip with an opinion - and it has both opinions.
        Assert.Equal(V2Family.Flash | V2Family.Bubble, V2Badges.FamiliesFor(id));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void The_Studio_chip_wears_the_pill_for_either_family(bool flash, bool bubble, bool expected)
    {
        Assert.Equal(expected, V2Badges.ChipWearsV2Pill("tab.studio", flash, bubble));
        Assert.Equal(expected, V2Badges.ChipWearsV2Pill("door.studio", flash, bubble));
    }

    [Fact]
    public void Every_other_destination_stays_bare_however_much_is_owned()
    {
        var loud = SettingsPaletteIndex.All
            .Select(e => e.Id)
            .Where(FavoritesRailRule.IsDestination)
            .Where(id => V2Badges.ChipWearsV2Pill(id, flashOwned: true, bubbleOwned: true))
            .ToList();
        Assert.Equal(new[] { "door.studio", "tab.studio" }, loud.OrderBy(x => x, System.StringComparer.Ordinal));
    }

    [Fact]
    public void An_id_the_table_never_heard_of_is_bare_and_does_not_throw()
    {
        Assert.Equal(V2Family.None, V2Badges.FamiliesFor(null));
        Assert.Equal(V2Family.None, V2Badges.FamiliesFor("no.such.row"));
        Assert.False(V2Badges.ChipWearsV2Pill(null, true, true));
        Assert.False(V2Badges.ChipWearsV2Pill("no.such.row", true, true));
    }

    // ---------------------------------------------------------------- live wrappers

    [Fact]
    public void With_nothing_attached_no_surface_lights()
    {
        // PrizeGrants answers false before App attaches an OwnershipService, and the live
        // wrappers must inherit that rather than throwing on a null service. This is also the
        // state every other test in the assembly runs in, so it is the one safe live assertion.
        Assert.False(V2Badges.FlashOwned());
        Assert.False(V2Badges.BubbleOwned());
        Assert.False(V2Badges.ChipWearsV2Pill("tab.studio"));

        // And the bubbles read the mosaic already used now forwards to the same rule.
        Assert.Equal(V2Badges.BubbleOwned(), AmbientBubbleMotion.AnyV2Owned);
    }
}
