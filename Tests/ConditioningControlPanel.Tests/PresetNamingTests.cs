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
/// Built-in preset and session names are mod-aware with variants (owner, 2026-09-25). The
/// resolver is pure; these pin that every built-in has a name in every mod table and language,
/// that the pick is stable, that anything not built in is left alone, and that CCP Default stays
/// neutral.
/// </summary>
public class PresetNamingTests
{
    private static readonly string[] Languages = { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    private static readonly string[] Mods =
    {
        BuiltInMods.CCPDefaultId, BuiltInMods.BambiSleepId, BuiltInMods.SissyHypnoId,
        BuiltInMods.LockedId, BuiltInMods.DronificationId, BuiltInMods.InfectionControlId,
    };

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    private static Dictionary<string, string> LoadLanguage(string lang)
    {
        var path = Path.Combine(AppDir(), "Localization", "Languages", lang + ".json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var map = new Dictionary<string, string>();
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String) map[p.Name] = p.Value.GetString()!;
        return map;
    }

    [Fact]
    public void TablesCoverEveryShippedBuiltIn()
    {
        var presetIds = Preset.GetDefaultPresets().Select(p => p.Id).OrderBy(x => x);
        Assert.Equal(presetIds, PresetNaming.BuiltInPresetIds.OrderBy(x => x));

        var sessionIds = Session.GetAllSessions().Select(s => s.Id).OrderBy(x => x);
        Assert.Equal(sessionIds, PresetNaming.BuiltInSessionIds.OrderBy(x => x));
    }

    [Fact]
    public void EveryBuiltInHasANameForEveryModAndSeed()
    {
        foreach (var mod in Mods)
            foreach (var seed in new[] { null, "", "2026-01-01", "2025-07-19" })
            {
                foreach (var id in PresetNaming.BuiltInPresetIds)
                    Assert.NotNull(PresetNaming.PresetKey(id, mod, seed));
                foreach (var id in PresetNaming.BuiltInSessionIds)
                    Assert.NotNull(PresetNaming.SessionKey(id, mod, seed));
            }
    }

    [Fact]
    public void EveryKeyHasANonEmptyStringInEveryLanguage()
    {
        foreach (var lang in Languages)
        {
            var map = LoadLanguage(lang);
            foreach (var key in PresetNaming.AllKeys())
            {
                Assert.True(map.TryGetValue(key, out var v), $"{lang}.json is missing {key}");
                Assert.False(string.IsNullOrWhiteSpace(v), $"{lang}.json has an empty {key}");
                Assert.DoesNotContain("—", v);
                Assert.DoesNotContain("!", v);
            }
        }
    }

    [Fact]
    public void ModsWithoutATableReadTheCcpDefaultNames()
    {
        foreach (var mod in new[] { null, "", "some-community-mod" })
        {
            Assert.Equal(PresetNaming.DefaultModTag, PresetNaming.ModTag(mod));
            Assert.Equal(PresetNaming.PresetKey("default-deep", BuiltInMods.CCPDefaultId, "s"),
                PresetNaming.PresetKey("default-deep", mod, "s"));
        }
    }

    [Fact]
    public void ThePickIsStableForTheSameSeed()
    {
        foreach (var mod in Mods)
            foreach (var id in PresetNaming.BuiltInSessionIds)
                Assert.Equal(PresetNaming.SessionKey(id, mod, "2026-03-14"), PresetNaming.SessionKey(id, mod, "2026-03-14"));
    }

    [Fact]
    public void DifferentSeedsReachBothVariants()
    {
        var seeds = Enumerable.Range(0, 60).Select(d => new DateTime(2026, 1, 1).AddDays(d).ToString("yyyy-MM-dd")).ToList();
        foreach (var id in PresetNaming.BuiltInPresetIds)
        {
            var picked = seeds.Select(s => PresetNaming.PresetKey(id, BuiltInMods.CCPDefaultId, s)).Distinct().Count();
            Assert.Equal(PresetNaming.VariantsPerMod, picked);
        }
    }

    [Fact]
    public void AnythingNotBuiltInGetsNoKey()
    {
        Assert.Null(PresetNaming.PresetKey(Guid.NewGuid().ToString(), BuiltInMods.CCPDefaultId, "s"));
        Assert.Null(PresetNaming.PresetKey(null, BuiltInMods.CCPDefaultId, "s"));
        Assert.Null(PresetNaming.SessionKey("my_custom_session", BuiltInMods.BambiSleepId, "s"));
        Assert.Null(PresetNaming.SessionKey(null, BuiltInMods.BambiSleepId, "s"));
    }

    [Fact]
    public void UserPresetsAndCustomSessionsKeepTheirOwnName()
    {
        // No App in tests, so DisplayName falls through to the raw name for anything it must not rename.
        var user = new Preset { Id = "default-gentle", Name = "My Own", IsDefault = false };
        Assert.Equal("My Own", PresetNaming.DisplayName(user, BuiltInMods.CCPDefaultId));

        var custom = new Session { Id = "morning_drift", Name = "Mine", Source = SessionSource.Custom };
        Assert.Equal("Mine", PresetNaming.DisplayName(custom, BuiltInMods.CCPDefaultId));
    }

    [Fact]
    public void CcpDefaultNamesStayNeutral()
    {
        string[] banned = { "bambi", "bimbo", "sissy", "doll", "girl", "lace", "pant", "she ", "her ", "circe", "drone", "fever", "infect" };
        var keys = PresetNaming.KeysForMod(BuiltInMods.CCPDefaultId).ToList();
        Assert.Equal(12 * PresetNaming.VariantsPerMod, keys.Count);
        var en = LoadLanguage("en");
        foreach (var key in keys)
        {
            var name = en[key].ToLowerInvariant() + " ";
            foreach (var word in banned)
                Assert.False(name.Contains(word), $"{key} = '{en[key]}' contains '{word.Trim()}'");
        }
        foreach (var lang in Languages)
        {
            var map = LoadLanguage(lang);
            foreach (var key in keys)
                foreach (var word in new[] { "bambi", "bimbo", "sissy" })
                    Assert.DoesNotContain(word, map[key].ToLowerInvariant());
        }
    }
}
