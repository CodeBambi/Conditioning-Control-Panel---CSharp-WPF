using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Guards the Companion tab redesign's staged localization hand-off
/// (<c>ConditioningControlPanel/Views/Controls/Companion/loc-staging-companion-tab.json</c>).
///
/// The nine <c>Localization/Languages/*.json</c> files are owned by another workstream, so this
/// package ships its EN masters in a staging file that the loc pass merges later. Two things can
/// silently rot in that arrangement, and both are caught here:
///
/// <list type="bullet">
///   <item>The C# masters and the JSON hand-off drifting apart — the translators would then be
///   handed strings the app never shows.</item>
///   <item>The JSON going non-strict. This repo has been bitten before: until 2026-07-29, eight of
///   the nine language files carried raw newlines inside tooltip values, so only Newtonsoft's
///   leniency parsed them and System.Text.Json / jq / Python all rejected every one. This suite
///   parses with <see cref="JsonSerializer"/> — strict on purpose — and additionally refuses any
///   literal line break in a value.</item>
/// </list>
/// </summary>
public class CompanionLocStagingTests
{
    /// <summary>
    /// Walks up from the test assembly to the repo, then to the staging file. Throws with the
    /// searched path rather than skipping, so a moved file cannot make this suite pass vacuously.
    /// </summary>
    private static string StagingFilePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "ConditioningControlPanel",
                                         CompanionLocStaging.StagingFileRelativePath);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Could not locate {CompanionLocStaging.StagingFileRelativePath} walking up from " +
            AppContext.BaseDirectory);
    }

    private static Dictionary<string, string> LoadStagingFile()
    {
        var json = File.ReadAllText(StagingFilePath());
        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        Assert.NotNull(parsed);
        return parsed!;
    }

    [Fact]
    public void StagingFile_ParsesStrictly()
    {
        // Deserialize throws on anything System.Text.Json dislikes: trailing commas, comments,
        // unescaped control characters. That is the whole point of asserting here.
        var parsed = LoadStagingFile();
        Assert.NotEmpty(parsed);
    }

    [Fact]
    public void StagingFile_MatchesTheCSharpMastersExactly()
    {
        var onDisk = LoadStagingFile();

        var missingFromFile = CompanionLocStaging.English.Keys.Except(onDisk.Keys).ToArray();
        Assert.True(missingFromFile.Length == 0,
            "keys in CompanionLocStaging.English but not in the JSON hand-off: " +
            string.Join(", ", missingFromFile));

        var strayInFile = onDisk.Keys.Except(CompanionLocStaging.English.Keys).ToArray();
        Assert.True(strayInFile.Length == 0,
            "keys in the JSON hand-off but not in CompanionLocStaging.English: " +
            string.Join(", ", strayInFile));

        foreach (var kv in CompanionLocStaging.English)
        {
            Assert.True(string.Equals(kv.Value, onDisk[kv.Key], StringComparison.Ordinal),
                $"EN master drift for '{kv.Key}': code has \"{kv.Value}\", file has \"{onDisk[kv.Key]}\"");
        }
    }

    [Fact]
    public void StagingFile_IsByteForByteWhatToJsonProduces()
    {
        // Keeps the hand-off regenerable: the loc pass can rebuild the file from code and get a
        // zero-diff, which is what makes reviewing a later copy change trivial.
        var onDisk = File.ReadAllText(StagingFilePath()).Replace("\r\n", "\n");
        Assert.Equal(CompanionLocStaging.ToJson(), onDisk);
    }

    [Fact]
    public void NoValueContainsALiteralLineBreak()
    {
        foreach (var kv in CompanionLocStaging.English)
        {
            Assert.False(kv.Value.Contains('\n') || kv.Value.Contains('\r'),
                $"'{kv.Key}' carries a literal line break — write \\n, never an actual newline");
        }
    }

    [Fact]
    public void EveryKeyIsInTheCompanionFamily_AndSnakeCase()
    {
        foreach (var key in CompanionLocStaging.English.Keys)
        {
            Assert.StartsWith("companion_", key, StringComparison.Ordinal);
            Assert.True(key.All(c => char.IsLower(c) || char.IsDigit(c) || c == '_'),
                $"'{key}' is not lower snake_case");
        }
    }

    [Fact]
    public void NoMasterIsBlank()
    {
        foreach (var kv in CompanionLocStaging.English)
        {
            Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"'{kv.Key}' has an empty EN master");
        }
    }

    [Fact]
    public void Resolve_FallsBackToTheStagedMaster_WhenTheLanguageFilesLackTheKey()
    {
        // No LocalizationManager.Initialize has run in this process, so every lookup misses and
        // the staged master is what the UI would render.
        Assert.Equal(CompanionLocStaging.English["companion_chat_title"],
                     CompanionLocStaging.Resolve("companion_chat_title"));
    }

    [Fact]
    public void Resolve_EchoesAnUnknownKeySoTyposAreVisible()
    {
        Assert.Equal("companion_not_a_real_key", CompanionLocStaging.Resolve("companion_not_a_real_key"));
        Assert.Equal(string.Empty, CompanionLocStaging.Resolve(null));
        Assert.Equal(string.Empty, CompanionLocStaging.Resolve(string.Empty));
    }

    [Fact]
    public void ChipCopy_StaysShortEnoughToSurviveGerman()
    {
        // German runs roughly 30% longer than English. The chips and pills on this page have a
        // MinWidth and TextTrimming, but a master that is already long will trim in every
        // language, which reads as a bug. 40 chars is the design's practical ceiling for these.
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

        foreach (var key in chipKeys)
        {
            Assert.True(CompanionLocStaging.English.TryGetValue(key, out var value),
                $"chip key '{key}' has no EN master");
            Assert.True(value!.Length <= 40, $"'{key}' is {value.Length} chars — too long for a chip");
        }
    }
}
