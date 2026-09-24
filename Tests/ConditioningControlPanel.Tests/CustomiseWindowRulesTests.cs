using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The pure rules behind the Customise window: a mod's recommended setup is offered once and
/// only when it resolves, and the personality picker shows lines in the personality's voice.
/// </summary>
public class CustomiseWindowRulesTests
{
    // ---- ask once ----

    [Fact]
    public void AsksTheFirstTimeAModWithASuggestionIsActivated()
    {
        var asked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.True(ModSuggestions.ShouldAsk("drone-mode", true, asked));
    }

    [Fact]
    public void NeverAsksTwiceWhateverTheAnswerWas()
    {
        var asked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.True(ModSuggestions.MarkAsked("drone-mode", asked));
        Assert.False(ModSuggestions.ShouldAsk("drone-mode", true, asked));
        Assert.False(ModSuggestions.ShouldAsk("DRONE-MODE", true, asked));
        Assert.False(ModSuggestions.MarkAsked("drone-mode", asked));
    }

    [Fact]
    public void NeverAsksWhenTheModSuggestsNothingThatResolves()
    {
        Assert.False(ModSuggestions.ShouldAsk("drone-mode", false, new HashSet<string>()));
    }

    [Fact]
    public void NeverAsksForABlankModId()
    {
        Assert.False(ModSuggestions.ShouldAsk("", true, null));
        Assert.False(ModSuggestions.ShouldAsk(null, true, null));
    }

    [Fact]
    public void OneModsAnswerDoesNotSilenceAnother()
    {
        var asked = new HashSet<string> { "builtin-locked" };
        Assert.True(ModSuggestions.ShouldAsk("drone-mode", true, asked));
    }

    // ---- resolving a suggestion ----

    [Fact]
    public void SettingsSuggestionMatchesByIdThenByName()
    {
        var presets = new List<Preset>
        {
            new() { Id = "a1", Name = "Gentle" },
            new() { Id = "Gentle", Name = "Other" },
        };
        Assert.Equal("Gentle", ModSuggestions.ResolveSettings(presets, "Gentle")!.Id);
        Assert.Equal("a1", ModSuggestions.ResolveSettings(presets, "gentle")!.Id);
    }

    [Fact]
    public void UnknownOrMissingSuggestionResolvesToNothing()
    {
        var presets = new List<AssetPreset> { new() { Id = "x", Name = "All Assets" } };
        Assert.Null(ModSuggestions.ResolveAssets(presets, "nope"));
        Assert.Null(ModSuggestions.ResolveAssets(presets, null));
        Assert.Null(ModSuggestions.ResolveAssets(null, "x"));
        Assert.Equal("x", ModSuggestions.ResolveAssets(presets, " all assets ")!.Id);
    }

    // ---- manifest stays tolerant ----

    [Fact]
    public void OldManifestsWithoutTheNewFieldsStillParse()
    {
        var m = JsonConvert.DeserializeObject<ModManifest>("{\"id\":\"old\",\"name\":\"Old\",\"previewImage\":\"p.png\"}")!;
        Assert.Null(m.BannerImage);
        Assert.Null(m.SuggestedSettingsPreset);
        Assert.Null(m.SuggestedAssetPreset);
        Assert.Equal("p.png", m.PreviewImage);
    }

    [Fact]
    public void NewManifestFieldsRoundTrip()
    {
        var m = JsonConvert.DeserializeObject<ModManifest>(
            "{\"id\":\"n\",\"name\":\"N\",\"bannerImage\":\"b.png\",\"suggestedSettingsPreset\":\"Gentle\",\"suggestedAssetPreset\":\"Mine\"}")!;
        Assert.Equal("b.png", m.BannerImage);
        Assert.Equal("Gentle", m.SuggestedSettingsPreset);
        Assert.Equal("Mine", m.SuggestedAssetPreset);
    }

    // ---- sample lines ----

    [Fact]
    public void PresetOwnLinesWinOverTheStockTable()
    {
        var p = new PersonalityPreset { Id = PersonalityPresets.StrictDommeId, SampleLines = new() { "  Mine.  ", "", "Two", "Three" } };
        Assert.Equal(new[] { "Mine.", "Two" }, PersonalitySamples.For(p));
    }

    [Fact]
    public void EveryStockPresetHasTwoLines()
    {
        foreach (var id in new[]
                 {
                     PersonalityPresets.NeutralDefaultId, PersonalityPresets.BambiSpriteId, PersonalityPresets.SlutModeId,
                     PersonalityPresets.GentleTrainerId, PersonalityPresets.StrictDommeId, PersonalityPresets.BimboCoachId,
                     PersonalityPresets.HypnoGuideId, PersonalityPresets.BimboCowId
                 })
        {
            Assert.Equal(2, PersonalitySamples.For(new PersonalityPreset { Id = id }).Count);
        }
    }

    [Fact]
    public void UnknownPresetWithoutLinesHasNone()
    {
        Assert.Empty(PersonalitySamples.For(new PersonalityPreset { Id = "user-guid" }));
        Assert.Empty(PersonalitySamples.For(null));
    }

    [Fact]
    public void AuthorLinesAreCappedInLength()
    {
        var cleaned = PersonalitySamples.Clean(new[] { new string('a', 500) })!;
        Assert.Equal(PersonalitySamples.MaxLineLength, cleaned[0].Length);
        Assert.Null(PersonalitySamples.Clean(null));
    }

    [Fact]
    public void ModPersonalitySampleLinesSurviveSanitising()
    {
        var clean = ModCompanionContent.SanitizePersonalities(new[]
        {
            new ModPersonality { Id = "p", Name = "P", SampleLines = new() { " hi ", "there", "extra" } }
        });
        Assert.Equal(new[] { "hi", "there" }, clean[0].SampleLines);
    }

    [Fact]
    public void BuiltInModsHaveBannerArtAndUserModsDoNot()
    {
        Assert.Equal("ccp_banner.png", ModManagerDialog.BuiltInBannerFor(BuiltInMods.CCPDefaultId));
        Assert.Null(ModManagerDialog.BuiltInBannerFor("someone-elses-mod"));
    }
}
