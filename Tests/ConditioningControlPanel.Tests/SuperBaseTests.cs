using System;
using System.ComponentModel;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Super follows its base feature (owner, 2026-10-01): base main toggle off = the add-on does not
/// run, and the player's own Super switch is left alone so it comes back with the base.
/// </summary>
public class SuperBaseTests
{
    [Theory]
    [InlineData(SuperEffect.FlickerDeck, nameof(AppSettings.FlashEnabled))]
    [InlineData(SuperEffect.InnerBloom, nameof(AppSettings.BubblesEnabled))]
    [InlineData(SuperEffect.Afterglow, nameof(AppSettings.SubliminalEnabled))]
    [InlineData(SuperEffect.Vortex, nameof(AppSettings.SpiralEnabled))]
    [InlineData(SuperEffect.Creep, nameof(AppSettings.PinkFilterEnabled))]
    [InlineData(SuperEffect.LightsDown, nameof(AppSettings.MandatoryVideosEnabled))]
    [InlineData(SuperEffect.Scrawl, nameof(AppSettings.BouncingTextEnabled))]
    [InlineData(SuperEffect.Undertow, nameof(AppSettings.BrainDrainEnabled))]
    public void Each_effect_rides_its_base_toggle(SuperEffect effect, string setting)
    {
        Assert.Equal(setting, SuperBase.SettingFor(effect));
        Assert.Equal(effect, SuperBase.EffectFor(setting));
    }

    [Fact]
    public void Every_effect_has_its_own_base_and_unrelated_settings_map_to_none()
    {
        var all = Enum.GetValues<SuperEffect>();
        Assert.Equal(all.Length, all.Select(SuperBase.SettingFor).Distinct().Count());
        Assert.Null(SuperBase.EffectFor(nameof(AppSettings.SuperEffectsOn)));
        Assert.Null(SuperBase.EffectFor(null));
        Assert.Null(SuperBase.EffectFor("NoSuchSetting"));
    }

    [Fact]
    public void The_map_reads_the_named_property_and_nothing_else()
    {
        foreach (var effect in Enum.GetValues<SuperEffect>())
        {
            var prop = typeof(AppSettings).GetProperty(SuperBase.SettingFor(effect))!;
            Assert.Equal(typeof(bool), prop.PropertyType);
            var s = new AppSettings();
            prop.SetValue(s, false);
            Assert.False(SuperBase.IsBaseOn(effect, s));
            prop.SetValue(s, true);
            Assert.True(SuperBase.IsBaseOn(effect, s));
        }
    }

    [Fact]
    public void No_settings_means_no_base()
    {
        foreach (var effect in Enum.GetValues<SuperEffect>())
            Assert.False(SuperBase.IsBaseOn(effect, null));
    }

    [Fact]
    public void Base_off_turns_the_add_on_off_whatever_the_switch_or_tier()
    {
        foreach (var e in Enum.GetValues<SuperEffect>())
        {
            Assert.False(SuperBase.IsOn(baseOn: false, hasTier: true, switchedOn: true, e, trying: null));
            // A running weekly try has nothing to ride either.
            Assert.False(SuperBase.IsOn(baseOn: false, hasTier: false, switchedOn: false, e, trying: e));
        }
    }

    [Fact]
    public void Base_on_leaves_the_tier_and_switch_rule_unchanged()
    {
        foreach (var e in Enum.GetValues<SuperEffect>())
        foreach (var tier in new[] { false, true })
        foreach (var sw in new[] { false, true })
        foreach (var trying in new SuperEffect?[] { null, e, (SuperEffect)(((int)e + 1) % 8) })
            Assert.Equal(SuperPreviewRule.IsOn(tier, sw, e, trying), SuperBase.IsOn(true, tier, sw, e, trying));
    }

    [Fact]
    public void Base_toggles_raise_property_changed_with_the_mapped_name()
    {
        // SuperAccess raises Changed off these notifications, so each base setter must notify.
        foreach (var effect in Enum.GetValues<SuperEffect>())
        {
            var s = new AppSettings();
            var prop = typeof(AppSettings).GetProperty(SuperBase.SettingFor(effect))!;
            bool before = (bool)prop.GetValue(s)!;
            string? seen = null;
            ((INotifyPropertyChanged)s).PropertyChanged += (_, a) => { if (a.PropertyName == prop.Name) seen = a.PropertyName; };
            prop.SetValue(s, !before);
            Assert.Equal(effect, SuperBase.EffectFor(seen));
        }
    }
}
