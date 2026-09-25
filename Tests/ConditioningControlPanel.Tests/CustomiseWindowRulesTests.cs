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
    // ---- per-mod default presets ----

    private static Dictionary<string, string> Map() => new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void NothingAppliesOnActivationUntilTheUserChooses()
    {
        var map = Map();
        Assert.False(ModPresetDefaults.HasChoice(map, "drone-mode"));
        Assert.Null(ModPresetDefaults.ForActivation(map, "drone-mode"));
    }

    [Fact]
    public void AStoredChoiceAppliesOnEveryActivationOfThatModOnly()
    {
        var map = Map();
        ModPresetDefaults.Store(map, "drone-mode", "default-gentle");
        Assert.Equal("default-gentle", ModPresetDefaults.ForActivation(map, "drone-mode"));
        Assert.Equal("default-gentle", ModPresetDefaults.ForActivation(map, "DRONE-MODE"));
        Assert.Null(ModPresetDefaults.ForActivation(map, "builtin-locked"));
    }

    [Fact]
    public void KeepCurrentIsAChoiceThatAppliesNothing()
    {
        var map = Map();
        ModPresetDefaults.Store(map, "drone-mode", null);
        Assert.True(ModPresetDefaults.HasChoice(map, "drone-mode"));
        Assert.Null(ModPresetDefaults.ForActivation(map, "drone-mode"));
        // and it beats the mod's suggestion in the dropdown
        Assert.Equal(ModPresetDefaults.KeepCurrent, ModPresetDefaults.Initial(map, "drone-mode", "default-gentle"));
    }

    [Fact]
    public void TheSuggestionIsNeverPreselected()
    {
        var map = Map();
        Assert.Equal(ModPresetDefaults.KeepCurrent, ModPresetDefaults.Initial(map, "drone-mode", "default-gentle"));
        Assert.Null(ModPresetDefaults.ForActivation(map, "drone-mode"));
        Assert.Equal(ModPresetDefaults.KeepCurrent, ModPresetDefaults.Initial(map, "drone-mode", null));

        ModPresetDefaults.Store(map, "drone-mode", "default-pink");
        Assert.Equal("default-pink", ModPresetDefaults.Initial(map, "drone-mode", "default-gentle"));
    }

    [Fact]
    public void BlankModIdsAreIgnored()
    {
        var map = Map();
        ModPresetDefaults.Store(map, " ", "x");
        Assert.Empty(map);
        Assert.Null(ModPresetDefaults.ForActivation(map, null));
    }

    [Fact]
    public void PerModDefaultsSurviveASettingsRoundTrip()
    {
        var s = new AppSettings();
        ModPresetDefaults.Store(s.ModDefaultSettingsPreset, "drone-mode", "default-gentle");
        ModPresetDefaults.Store(s.ModDefaultAssetPreset, "drone-mode", null);
        var back = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(s))!;
        Assert.Equal("default-gentle", ModPresetDefaults.ForActivation(back.ModDefaultSettingsPreset, "Drone-Mode"));
        Assert.True(ModPresetDefaults.HasChoice(back.ModDefaultAssetPreset, "drone-mode"));
    }

    // ---- companion perks ----

    [Fact]
    public void EveryPerkHasALineAndOnlyTheDrainReadsAsACost()
    {
        foreach (CompanionBonusType t in Enum.GetValues(typeof(CompanionBonusType)))
        {
            var perk = CompanionPerks.For(t);
            Assert.StartsWith("perk_", perk.LocKey);
            Assert.Equal(t == CompanionBonusType.XPDrain, perk.Negative);
        }
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
        Assert.Equal("Gentle", ModPresetDefaults.ResolveSettings(presets, "Gentle")!.Id);
        Assert.Equal("a1", ModPresetDefaults.ResolveSettings(presets, "gentle")!.Id);
    }

    [Fact]
    public void UnknownOrMissingSuggestionResolvesToNothing()
    {
        var presets = new List<AssetPreset> { new() { Id = "x", Name = "All Assets" } };
        Assert.Null(ModPresetDefaults.ResolveAssets(presets, "nope"));
        Assert.Null(ModPresetDefaults.ResolveAssets(presets, null));
        Assert.Null(ModPresetDefaults.ResolveAssets(null, "x"));
        Assert.Equal("x", ModPresetDefaults.ResolveAssets(presets, " all assets ")!.Id);
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

    // ---- companion preview (a mod not in use) ----

    [Fact]
    public void PreviewCountsEveryStockLookWhenTheModDeclaresNone()
    {
        Assert.Equal(7, CompanionPreview.CountLooks(new ModManifest(), singleEmote: false));
    }

    [Fact]
    public void PreviewCountsSupportedAndCustomLooks()
    {
        var m = new ModManifest
        {
            SupportedAvatarSets = new() { 1, 3, 8 },
            CustomAvatarSets = new() { new CustomAvatarSet { SetNumber = 8 }, new CustomAvatarSet { SetNumber = 9 } }
        };
        Assert.Equal(3, CompanionPreview.CountLooks(m, singleEmote: false));
        Assert.Equal(1, CompanionPreview.CountLooks(m, singleEmote: true));
    }

    [Fact]
    public void PreviewUsesTheModsOwnPersonalities()
    {
        var m = new ModManifest { Name = "Circe's Lock", Identity = new ModIdentity { CompanionName = "Circe" } };
        var info = CompanionPreview.Build(m, new List<ModPersonality>
        {
            new() { Id = "a", Name = "A", SampleLines = new() { "The key is safe." } },
            new() { Id = "b", Name = "B" }
        }, singleEmote: false, neutral: false);
        Assert.Equal("Circe", info.Name);
        Assert.Equal(2, info.Personalities);
        Assert.Equal("The key is safe.", info.SampleLine);
    }

    [Fact]
    public void PreviewFallsBackToTheStockSetAndTheModName()
    {
        var info = CompanionPreview.Build(new ModManifest { Name = "CCP Default" }, null, singleEmote: false, neutral: true);
        Assert.Equal("CCP Default", info.Name);
        // The neutral picker hides the four niche personas (owner, 2026-09-25).
        Assert.Equal(PersonalityPresets.GetAllBuiltIn().Count - PersonalityPresets.HiddenInNeutralIds.Length, info.Personalities);
        Assert.Equal(PersonalitySamples.For(PersonalityPresets.GetNeutralDefault())[0], info.SampleLine);
    }
}
