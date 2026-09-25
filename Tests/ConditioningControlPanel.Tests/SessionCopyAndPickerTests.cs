using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// CCP Default pivot, owner 2026-09-25: built-in session descriptions and phases read neutral
/// copy under CCP Default and keep their exact wording under every other mod; the CCP Default
/// personality picker hides the four niche personas without stranding a user who picked one.
/// </summary>
public class SessionCopyAndPickerTests
{
    private static readonly string[] Languages = { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    private static readonly string[] ThemedMods =
    {
        BuiltInMods.BambiSleepId, BuiltInMods.SissyHypnoId, BuiltInMods.LockedId,
        BuiltInMods.DronificationId, BuiltInMods.InfectionControlId, "community-some-mod",
    };

    private static readonly string[] Banned =
    {
        "girl", "doll", "bambi", "bimbo", "sissy", "cock", "clit", "pretty", "sweetheart", "cum",
    };

    private static Dictionary<string, string> LoadLanguage(string lang)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        var path = Path.Combine(dir!.FullName, "ConditioningControlPanel", "Localization", "Languages", lang + ".json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var map = new Dictionary<string, string>();
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String) map[p.Name] = p.Value.GetString()!;
        return map;
    }

    [Fact]
    public void EveryCopyKeyHasPlainTextInEveryLanguage()
    {
        foreach (var lang in Languages)
        {
            var map = LoadLanguage(lang);
            foreach (var key in PresetNaming.AllCopyKeys())
            {
                Assert.True(map.TryGetValue(key, out var v), $"{lang}.json is missing {key}");
                Assert.False(string.IsNullOrWhiteSpace(v), $"{lang}.json has an empty {key}");
                Assert.DoesNotContain("—", v);
                Assert.DoesNotContain("–", v);
                Assert.DoesNotContain("!", v);
            }
        }
    }

    [Fact]
    public void CopyKeysAreHandedOutOnlyUnderCcpDefault()
    {
        foreach (var session in Session.GetAllSessions())
        {
            foreach (var mod in ThemedMods)
            {
                Assert.Null(PresetNaming.SessionDescriptionKey(session.Id, mod));
                foreach (var phase in session.Phases)
                {
                    Assert.Null(PresetNaming.PhaseKey(session.Id, phase.Name, mod, description: false));
                    Assert.Null(PresetNaming.PhaseKey(session.Id, phase.Name, mod, description: true));
                }
            }
        }
        Assert.NotNull(PresetNaming.SessionDescriptionKey("distant_doll", BuiltInMods.CCPDefaultId));
        Assert.NotNull(PresetNaming.SessionDescriptionKey("distant_doll", null));
        Assert.Null(PresetNaming.SessionDescriptionKey("not_a_session", BuiltInMods.CCPDefaultId));
    }

    [Fact]
    public void EveryPhaseWithCopyExistsInItsSession()
    {
        var handed = new HashSet<string>();
        foreach (var session in Session.GetAllSessions())
            foreach (var phase in session.Phases)
                foreach (var desc in new[] { false, true })
                {
                    var key = PresetNaming.PhaseKey(session.Id, phase.Name, BuiltInMods.CCPDefaultId, desc);
                    if (key != null) handed.Add(key);
                }
        var phaseKeys = PresetNaming.AllCopyKeys().Where(k => k.Contains("_phase_"));
        Assert.All(phaseKeys, k => Assert.Contains(k, handed));
    }

    [Theory]
    [InlineData("GG!", "gg")]
    [InlineData("Empty Doll", "empty_doll")]
    [InlineData("Half Way", "half_way")]
    [InlineData(null, "")]
    public void PhaseSlugFollowsTheAuthoredName(string? name, string slug) =>
        Assert.Equal(slug, PresetNaming.PhaseSlug(name));

    [Fact]
    public void CcpDefaultSessionTextIsNeutral()
    {
        var en = LoadLanguage("en");
        string Resolve(string? key, string raw) => key != null && en.TryGetValue(key, out var v) ? v : raw;

        foreach (var session in Session.GetAllSessions())
        {
            var texts = new List<string>
            {
                Resolve(PresetNaming.SessionDescriptionKey(session.Id, BuiltInMods.CCPDefaultId), session.Description),
            };
            foreach (var phase in session.Phases)
            {
                texts.Add(Resolve(PresetNaming.PhaseKey(session.Id, phase.Name, BuiltInMods.CCPDefaultId, false), phase.Name));
                texts.Add(Resolve(PresetNaming.PhaseKey(session.Id, phase.Name, BuiltInMods.CCPDefaultId, true), phase.Description));
            }
            foreach (var text in texts)
                foreach (var word in Banned)
                    Assert.False(text.Contains(word, StringComparison.OrdinalIgnoreCase),
                        $"{session.Id}: \"{text}\" says {word} under CCP Default");
        }
    }

    [Fact]
    public void ThemedModsKeepTheAuthoredWording()
    {
        // The themed wording is still in the model, and no key points away from it.
        var dd = Session.GetAllSessions().Single(s => s.Id == "distant_doll");
        Assert.Contains(dd.Phases, p => p.Name == "Empty Doll");
        Assert.Null(PresetNaming.PhaseKey("distant_doll", "Empty Doll", BuiltInMods.BambiSleepId, false));
        Assert.Null(PresetNaming.SessionDescriptionKey("good_girls_dont_cum", BuiltInMods.SissyHypnoId));
    }

    [Fact]
    public void NeutralPickerHidesTheNichePersonas()
    {
        var stock = PersonalityPresets.GetAllBuiltIn();
        var picked = PersonalityPresets.ForPicker(stock, neutral: true).Select(p => p.Id).ToList();

        foreach (var id in new[] { "bambisprite", "slutmode", "bimbo-coach", "bimbo-cow" })
            Assert.DoesNotContain(id, picked);
        Assert.Contains(PersonalityPresets.NeutralDefaultId, picked);
        Assert.Equal(stock.Count - 4, picked.Count);
        Assert.Equal(PersonalityPresets.NeutralDefaultId, picked[0]);
    }

    [Fact]
    public void ThemedPickerKeepsEveryStockPersona()
    {
        var stock = PersonalityPresets.GetAllBuiltIn();
        Assert.Equal(stock.Select(p => p.Id), PersonalityPresets.ForPicker(stock, neutral: false).Select(p => p.Id));
    }

    [Fact]
    public void AHiddenPersonaAlreadySelectedStaysListedAndResolvable()
    {
        var stock = PersonalityPresets.GetAllBuiltIn();
        var picked = PersonalityPresets.ForPicker(stock, neutral: true, keepId: PersonalityPresets.BimboCowId)
            .Select(p => p.Id).ToList();

        Assert.Contains(PersonalityPresets.BimboCowId, picked);
        Assert.DoesNotContain(PersonalityPresets.BambiSpriteId, picked);
        // Hidden is not removed: the id still resolves and still counts as built in.
        foreach (var id in PersonalityPresets.HiddenInNeutralIds)
        {
            Assert.NotNull(PersonalityPresets.GetBuiltInById(id));
            Assert.Contains(id, PersonalityPresets.BuiltInIds);
        }
    }
}
