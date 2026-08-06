using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Guards the "Her Room" copy now that it lives in the nine shipping language files.
///
/// <para><b>History.</b> While the redesign was its own branch it could not touch
/// <c>Localization/Languages/*.json</c>, so it staged its EN masters in <c>CompanionLocStaging</c>
/// plus a JSON hand-off, and an earlier suite guarded that pair. The loc pass merged the keys into
/// all nine files and deleted the vehicle, so the guarantees move here — and get stronger, because
/// the assertions now read the files the app actually loads.</para>
///
/// <para><b>What can rot.</b></para>
/// <list type="bullet">
///   <item>A language file going non-strict. This repo has been bitten: until 2026-07-29 eight of
///   the nine carried raw newlines inside tooltip values, so only Newtonsoft's leniency parsed
///   them and System.Text.Json / jq / Python rejected every one. Every file here is parsed with
///   <see cref="JsonSerializer"/> — strict on purpose — and no value may carry a literal break.</item>
///   <item>A translation losing a <c>{0}</c>. The placeholder is what carries the live number; drop
///   it and the card renders a sentence with a hole in it, silently.</item>
///   <item>A translation losing the trailing space on the two-part flavour lines, which would weld
///   the accent half onto the previous word.</item>
///   <item>A key reaching English but not the other eight, which is how a page ends up half
///   translated.</item>
/// </list>
/// </summary>
public class CompanionRoomLocalizationTests
{
    /// <summary>
    /// Every <c>companion_*</c> key in <c>en.json</c>: this page's copy plus Train 1's memory
    /// panel family, which shares the prefix and wants the same invariants anyway.
    /// </summary>
    private static IReadOnlyDictionary<string, string> RoomKeys() => CompanionLocMasters.Companion;

    [Fact]
    public void EveryLanguageFileParsesStrictly()
    {
        foreach (var lang in CompanionLocMasters.Languages)
        {
            // Deserialize throws on anything System.Text.Json dislikes: trailing commas, comments,
            // unescaped control characters. That is the whole point of asserting here.
            var parsed = CompanionLocMasters.For(lang);
            Assert.NotEmpty(parsed);
        }
    }

    [Fact]
    public void NoCompanionValueCarriesALineBreakAtAll()
    {
        // Two things, one assertion. The house rule — never a raw newline inside a language-file
        // string, always "\n" — is already enforced by the strict parse above, because JSON
        // forbids unescaped control characters in a string and System.Text.Json says so out loud.
        //
        // What is left is this page's own rule: its multi-line copy is split into numbered keys
        // ("…_body_1", "…_body_2", the three clear-conversation keys) rather than one value with
        // escapes in it, so a translator is handed sentences and cannot weld them together or
        // lose a break. A companion value that grew a "\n" means someone rejoined them.
        //
        // The one exemption is Train 1's forget-everything MessageBox body, which predates the
        // page and is a genuine two-paragraph dialog rather than layout copy.
        var exempt = new HashSet<string>(StringComparer.Ordinal) { "companion_memory_forget_all_body" };

        foreach (var lang in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(lang);
            foreach (var key in RoomKeys().Keys.Where(k => file.ContainsKey(k) && !exempt.Contains(k)))
            {
                Assert.False(file[key].Contains('\n') || file[key].Contains('\r'),
                    $"{lang}.json '{key}' carries a line break — split the copy into two keys instead");
            }
        }
    }

    [Fact]
    public void EveryCompanionKeyReachedAllNineLanguages()
    {
        var english = RoomKeys();
        Assert.True(english.Count >= 200,
            $"only {english.Count} companion_* keys in en.json — the page's copy did not land");

        var gaps = new List<string>();
        foreach (var lang in CompanionLocMasters.Languages.Where(l => l != "en"))
        {
            var file = CompanionLocMasters.For(lang);
            gaps.AddRange(english.Keys.Where(k => !file.ContainsKey(k)).Select(k => $"{lang}: {k}"));
        }

        Assert.True(gaps.Count == 0, "companion keys missing from a language file: " + string.Join(", ", gaps));
    }

    [Fact]
    public void NoCompanionValueIsBlankInAnyLanguage()
    {
        foreach (var lang in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(lang);
            foreach (var key in RoomKeys().Keys.Where(file.ContainsKey))
                Assert.False(string.IsNullOrWhiteSpace(file[key]), $"{lang}.json '{key}' is empty");
        }
    }

    [Fact]
    public void EveryPlaceholderSurvivedTranslation()
    {
        var english = RoomKeys();
        var broken = new List<string>();

        foreach (var lang in CompanionLocMasters.Languages.Where(l => l != "en"))
        {
            var file = CompanionLocMasters.For(lang);
            foreach (var kv in english.Where(kv => file.ContainsKey(kv.Key)))
            {
                foreach (var placeholder in new[] { "{0}", "{1}" })
                {
                    if (kv.Value.Contains(placeholder, StringComparison.Ordinal) !=
                        file[kv.Key].Contains(placeholder, StringComparison.Ordinal))
                    {
                        broken.Add($"{lang}: {kv.Key} ({placeholder})");
                    }
                }
            }
        }

        Assert.True(broken.Count == 0, "placeholder drift between en and a translation: " + string.Join(", ", broken));
    }

    [Fact]
    public void TheTwoPartFlavourLinesKeptTheirTrailingSpace()
    {
        // Z1 renders "<flavor><accent>" as two runs so the accent half can be styled. The space
        // that separates them lives at the end of the first key; a translator trimming it welds
        // the two halves into one word.
        string[] split =
        {
            "companion_constellation_flavor",
            "companion_constellation_flavor_new",
            "companion_constellation_flavor_final",
            "companion_awareness_wire_prefix"
        };

        foreach (var lang in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(lang);
            foreach (var key in split)
            {
                Assert.True(file.ContainsKey(key), $"{lang}.json is missing '{key}'");
                Assert.EndsWith(" ", file[key], StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void NoDuplicateCompanionKeyWasAdded()
    {
        // en.json carries 14 known duplicates from before this page existed. They are load-bearing
        // history, not a licence to add more: Newtonsoft keeps the LAST value, so a duplicated key
        // silently shadows the one the author was looking at.
        foreach (var lang in CompanionLocMasters.Languages)
        {
            var duplicates = DuplicateKeys(CompanionLocMasters.PathFor(lang))
                .Where(k => k.StartsWith("companion_", StringComparison.Ordinal))
                .ToArray();

            Assert.True(duplicates.Length == 0,
                $"{lang}.json declares a companion key twice: " + string.Join(", ", duplicates));
        }
    }

    /// <summary>
    /// Keys declared more than once in a file. <see cref="JsonSerializer"/> collapses them, so the
    /// raw document has to be walked to see them at all.
    /// </summary>
    private static IEnumerable<string> DuplicateKeys(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!seen.Add(property.Name)) yield return property.Name;
        }
    }

    // =====================================================================================
    //  the invariants that used to live on the staging table
    // =====================================================================================

    [Fact]
    public void EveryRoomKeyIsLowerSnakeCase()
    {
        foreach (var key in RoomKeys().Keys)
        {
            Assert.True(key.All(c => char.IsLower(c) || char.IsDigit(c) || c == '_'),
                $"'{key}' is not lower snake_case");
        }
    }

    [Fact]
    public void ChipCopyStaysShortEnoughToSurviveGerman()
    {
        // German runs roughly 30% longer than English. The chips and pills on this page have a
        // MinWidth and TextTrimming, but copy that is already long will trim in every language,
        // which reads as a bug. 40 chars is the design's practical ceiling for these — and now
        // that the translations exist, it is checked against them too.
        string[] chipKeys =
        {
            "companion_hero_switch", "companion_hero_detach", "companion_hero_chat",
            "companion_chat_open_full", "companion_chat_history",
            "companion_awareness_dial_off", "companion_awareness_dial_broad",
            "companion_awareness_dial_everything",
            "companion_engine_provider_off", "companion_engine_provider_cloud",
            "companion_engine_provider_local", "companion_engine_provider_custom",
            "companion_stage_0", "companion_stage_1", "companion_stage_2",
            "companion_stage_3", "companion_stage_4"
        };

        foreach (var lang in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(lang);
            foreach (var key in chipKeys)
            {
                Assert.True(file.TryGetValue(key, out var value), $"{lang}.json has no '{key}'");
                Assert.True(value!.Length <= 40,
                    $"{lang}.json '{key}' is {value.Length} chars — too long for a chip");
            }
        }
    }

    // =====================================================================================
    //  the runtime path
    // =====================================================================================

    [Fact]
    public void LocGetResolvesTheRoomsKeys_RatherThanEchoingThem()
    {
        // The page dropped its private staging resolver for the house lookup. If the keys had not
        // reached en.json this would return the key itself and every label on the page would read
        // as an identifier.
        Assert.Equal(CompanionLocMasters.Get("companion_chat_title"), Loc.Get("companion_chat_title"));
        Assert.NotEqual("companion_chat_title", Loc.Get("companion_chat_title"));
    }

    [Fact]
    public void LocGetEchoesAnUnknownKeySoTyposAreVisible()
    {
        Assert.Equal("companion_not_a_real_key", Loc.Get("companion_not_a_real_key"));
        Assert.Equal(string.Empty, Loc.Get(string.Empty));
    }

    [Fact]
    public void LocGetFSurvivesAMalformedTemplate()
    {
        // A bad translation is cosmetic; it may never take a card down.
        Assert.Equal("companion_not_a_real_key", Loc.GetF("companion_not_a_real_key", 1, 2, 3));
    }

    [Fact]
    public void TheAttentionCopyLadderIsFullyTranslated()
    {
        // AttentionCopy picks one of four rungs at runtime; a rung missing from a language file
        // would show an identifier at exactly the moment the meter is trying to reassure.
        foreach (var fraction in new[] { 1.0, 0.30, 0.08, 0.0 })
        {
            var key = AttentionCopy.CopyKeyFor(fraction);
            foreach (var lang in CompanionLocMasters.Languages)
                Assert.True(CompanionLocMasters.For(lang).ContainsKey(key), $"{lang}.json has no '{key}'");
        }
    }
}
