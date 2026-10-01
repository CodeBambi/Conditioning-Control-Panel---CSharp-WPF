using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Classic / Both / Super only: the pure rule and the stored map.</summary>
public class SuperModeRuleTests
{
    [Fact]
    public void Nothing_stored_means_Both_for_every_effect()
    {
        foreach (SuperEffect e in System.Enum.GetValues(typeof(SuperEffect)))
            Assert.Equal(SuperMode.Both, SuperModeRule.Resolve(e, null));
        Assert.Equal(SuperMode.Both, SuperModeRule.Resolve(SuperEffect.Vortex, new Dictionary<string, int>()));
    }

    [Fact]
    public void Only_effects_with_a_look_of_their_own_offer_Super_only()
    {
        Assert.Equal(new[] { SuperMode.Classic, SuperMode.Both, SuperMode.SuperOnly }, SuperModeRule.Offered(SuperEffect.Vortex));
        Assert.Equal(new[] { SuperMode.Classic, SuperMode.Both, SuperMode.SuperOnly }, SuperModeRule.Offered(SuperEffect.Creep));
        Assert.Equal(new[] { SuperMode.Classic, SuperMode.Both, SuperMode.SuperOnly }, SuperModeRule.Offered(SuperEffect.Afterglow));
        foreach (var e in new[] { SuperEffect.FlickerDeck, SuperEffect.Scrawl, SuperEffect.Undertow, SuperEffect.LightsDown, SuperEffect.InnerBloom })
            Assert.Equal(new[] { SuperMode.Classic, SuperMode.Both }, SuperModeRule.Offered(e));
    }

    [Fact]
    public void A_stored_value_is_clamped_to_a_legal_mode()
    {
        Assert.Equal(SuperMode.Classic, SuperModeRule.Resolve(SuperEffect.Vortex, new Dictionary<string, int> { ["Vortex"] = 0 }));
        Assert.Equal(SuperMode.SuperOnly, SuperModeRule.Resolve(SuperEffect.Vortex, new Dictionary<string, int> { ["Vortex"] = 2 }));
        Assert.Equal(SuperMode.Both, SuperModeRule.Resolve(SuperEffect.Scrawl, new Dictionary<string, int> { ["Scrawl"] = 2 }));
        Assert.Equal(SuperMode.Both, SuperModeRule.Resolve(SuperEffect.Vortex, new Dictionary<string, int> { ["Vortex"] = 99 }));
    }

    [Fact]
    public void The_base_hides_only_in_a_real_Super_only_run()
    {
        Assert.True(SuperModeRule.HidesBase(SuperEffect.Creep, SuperMode.SuperOnly, superIsOn: true, trying: false));
        Assert.False(SuperModeRule.HidesBase(SuperEffect.Creep, SuperMode.Both, true, false));
        Assert.False(SuperModeRule.HidesBase(SuperEffect.Creep, SuperMode.Classic, true, false));
        Assert.False(SuperModeRule.HidesBase(SuperEffect.Creep, SuperMode.SuperOnly, superIsOn: false, trying: false));   // locked, or base off
        Assert.False(SuperModeRule.HidesBase(SuperEffect.Creep, SuperMode.SuperOnly, true, trying: true));                 // a try is a taste
        Assert.False(SuperModeRule.HidesBase(SuperEffect.Scrawl, SuperMode.SuperOnly, true, false));                        // nothing to hide
    }

    [Fact]
    public void With_keeps_the_other_effects_and_refuses_Super_only_where_it_has_no_meaning()
    {
        var a = SuperModeRule.With(null, SuperEffect.Vortex, SuperMode.SuperOnly);
        var b = SuperModeRule.With(a, SuperEffect.Scrawl, SuperMode.SuperOnly);
        Assert.Equal(2, b["Vortex"]);
        Assert.Equal(1, b["Scrawl"]);
        Assert.Equal(2, a["Vortex"]);   // the input is not mutated
    }

    [Fact]
    public void The_map_round_trips_through_the_settings_file()
    {
        var s = new AppSettings { SuperModes = new Dictionary<string, int> { ["Creep"] = 2, ["Afterglow"] = 0 } };
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(s))!;
        Assert.Equal(2, back.SuperModes["Creep"]);
        Assert.Equal(0, back.SuperModes["Afterglow"]);
        Assert.Empty(new AppSettings().SuperModes);
    }
}
