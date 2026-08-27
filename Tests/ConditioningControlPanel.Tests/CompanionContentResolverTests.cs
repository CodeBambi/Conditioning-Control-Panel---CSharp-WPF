using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Only Drone and Locked ship a bundled .ccpmod, so <c>ModPackage.InstalledPath</c> is null for
/// BambiSleep and SissyHypno. Everything that resolved companion content off InstalledPath alone
/// therefore no-opped for those two and fell through to the shared baseline - which, for the AI
/// companion, is the DEFAULT mod's voice.
///
/// These pin the ladder that replaces that: packaged .ccpmod, then the per-mod folder in the
/// install dir, then the same folder delivered by a downloaded content pack, then (only where one
/// exists) the shared baseline.
/// </summary>
public class CompanionContentResolverTests
{
    private const string InstallRoot = @"C:\app";
    private const string ContentRoot = @"C:\data\content";
    private const string BambiId = "builtin-bambisleep";
    private const string PackagedPath = @"C:\data\builtin_mods\drone-mode";

    private static string PerMod(string root, string modId, string leaf) =>
        Path.Combine(root, "Resources", "sounds", "companion_audio", "mods", modId, leaf);

    private static Func<CompanionContentCandidate, bool> ProbeFor(params string[] present)
    {
        var set = new HashSet<string>(present, System.StringComparer.OrdinalIgnoreCase);
        return c => set.Contains(c.Path);
    }

    // ---------------------------------------------------------------- ladder shape

    [Fact]
    public void ABuiltInModWithNoCcpmodStillGetsBothPerModRungs()
    {
        var candidates = CompanionContentResolver.Candidates(
            CompanionChannel.BarkRules, BambiId, installedPath: null, InstallRoot, ContentRoot);

        Assert.Equal(
            new[] { CompanionContentSource.ModInstallDir, CompanionContentSource.ModContentPack },
            candidates.Select(c => c.Source).ToArray());
        Assert.Equal(PerMod(InstallRoot, BambiId, "bark_rules.json"), candidates[0].Path);
        Assert.Equal(PerMod(ContentRoot, BambiId, "bark_rules.json"), candidates[1].Path);
    }

    [Fact]
    public void APackagedModIsProbedBeforeEitherPerModFolder()
    {
        var candidates = CompanionContentResolver.Candidates(
            CompanionChannel.BarkRules, "drone-mode", PackagedPath, InstallRoot, ContentRoot);

        Assert.Equal(CompanionContentSource.PackagedMod, candidates[0].Source);
        Assert.Equal(
            Path.Combine(PackagedPath, "resources", "sounds", "companion_audio", "bark_rules.json"),
            candidates[0].Path);
        Assert.Equal(3, candidates.Count);
    }

    [Fact]
    public void BarkRulesHaveNoBaselineRungBecauseTheBaseManifestIsAlwaysMergedUnderneath()
    {
        var candidates = CompanionContentResolver.Candidates(
            CompanionChannel.BarkRules, BambiId, null, InstallRoot, ContentRoot);

        Assert.DoesNotContain(candidates, c => c.Source == CompanionContentSource.Baseline);
    }

    [Fact]
    public void NoModAndNoPackageMeansNothingToProbe()
    {
        var candidates = CompanionContentResolver.Candidates(
            CompanionChannel.BarkRules, modId: null, installedPath: null, InstallRoot, ContentRoot);

        Assert.Empty(candidates);
    }

    [Fact]
    public void AMissingContentRootJustShortensTheLadder()
    {
        var candidates = CompanionContentResolver.Candidates(
            CompanionChannel.BarkRules, BambiId, null, InstallRoot, contentRoot: null);

        Assert.Single(candidates);
        Assert.Equal(CompanionContentSource.ModInstallDir, candidates[0].Source);
    }

    [Fact]
    public void VoiceLinesFallBackToTheSharedBaselineFolderAndAreProbedAsDirectories()
    {
        var candidates = CompanionContentResolver.Candidates(
            CompanionChannel.VoiceLines, BambiId, null, InstallRoot, ContentRoot);

        Assert.All(candidates, c => Assert.True(c.IsDirectory));
        Assert.Equal(CompanionContentSource.Baseline, candidates[^1].Source);
        Assert.Equal(Path.Combine(ContentRoot, "Resources", "sounds", "flashes_audio"), candidates[^1].Path);
    }

    [Fact]
    public void BarkAudioFallsBackToTheSharedCompanionAudioRoot()
    {
        var candidates = CompanionContentResolver.Candidates(
            CompanionChannel.BarkAudio, BambiId, null, InstallRoot, ContentRoot, fileName: "giggle_1.mp3");

        Assert.Equal(
            new[]
            {
                CompanionContentSource.ModInstallDir,
                CompanionContentSource.ModContentPack,
                CompanionContentSource.Baseline,
                CompanionContentSource.Baseline
            },
            candidates.Select(c => c.Source).ToArray());
        Assert.Equal(
            Path.Combine(InstallRoot, "Resources", "sounds", "companion_audio", "giggle_1.mp3"),
            candidates[2].Path);
    }

    [Fact]
    public void BarkAudioWithNoFileNameResolvesToNothing()
    {
        var candidates = CompanionContentResolver.Candidates(
            CompanionChannel.BarkAudio, BambiId, null, InstallRoot, ContentRoot, fileName: null);

        Assert.Empty(candidates);
    }

    [Fact]
    public void PersonalitiesSitAtThePackagedModsResourcesRootNotUnderSounds()
    {
        var candidates = CompanionContentResolver.Candidates(
            CompanionChannel.Personalities, "drone-mode", PackagedPath, InstallRoot, ContentRoot);

        Assert.Equal(Path.Combine(PackagedPath, "resources", "personalities.json"), candidates[0].Path);
    }

    // ---------------------------------------------------------------- picking

    [Fact]
    public void TheDownloadedPackWinsWhenTheInstallDirCopyWasStrippedOut()
    {
        var packPath = PerMod(ContentRoot, BambiId, "bark_rules.json");
        var pick = CompanionContentResolver.Resolve(
            CompanionChannel.BarkRules, BambiId, null, InstallRoot, ContentRoot, ProbeFor(packPath));

        Assert.True(pick.Found);
        Assert.Equal(CompanionContentSource.ModContentPack, pick.Source);
        Assert.Equal(packPath, pick.Path);
    }

    [Fact]
    public void TheInstallDirCopyStillWinsWhenBothRootsHaveIt()
    {
        var installPath = PerMod(InstallRoot, BambiId, "bark_rules.json");
        var packPath = PerMod(ContentRoot, BambiId, "bark_rules.json");
        var pick = CompanionContentResolver.Resolve(
            CompanionChannel.BarkRules, BambiId, null, InstallRoot, ContentRoot, ProbeFor(installPath, packPath));

        Assert.Equal(CompanionContentSource.ModInstallDir, pick.Source);
    }

    [Fact]
    public void NothingAnywhereIsReportedAsNoneRatherThanAGuessedPath()
    {
        var pick = CompanionContentResolver.Resolve(
            CompanionChannel.BarkRules, BambiId, null, InstallRoot, ContentRoot, _ => false);

        Assert.False(pick.Found);
        Assert.Equal(CompanionContentSource.None, pick.Source);
        Assert.Null(pick.Path);
    }

    [Fact]
    public void AnUnreadableRootIsSkippedInsteadOfTakingTheCompanionSilent()
    {
        var packPath = PerMod(ContentRoot, BambiId, "bark_rules.json");
        var pick = CompanionContentResolver.Resolve(
            CompanionChannel.BarkRules, BambiId, null, InstallRoot, ContentRoot,
            c => c.Source == CompanionContentSource.ModInstallDir
                ? throw new IOException("drive went away")
                : c.Path == packPath);

        Assert.Equal(CompanionContentSource.ModContentPack, pick.Source);
    }

    // ---------------------------------------------------------------- personality source

    [Fact]
    public void APersonalityFileBeatsTheInCodeManifest()
    {
        Assert.Equal(
            CompanionContentSource.ModContentPack,
            CompanionContentResolver.ResolvePersonalitySource(
                CompanionContentSource.ModContentPack, manifestHasPersonalities: true));
    }

    [Fact]
    public void TheInCodeManifestIsUsedWhenNoFileShipped()
    {
        Assert.Equal(
            CompanionContentSource.BuiltInManifest,
            CompanionContentResolver.ResolvePersonalitySource(
                CompanionContentSource.None, manifestHasPersonalities: true));
    }

    [Fact]
    public void NeitherSourceMeansTheAiIsSpeakingInTheDefaultModsVoice()
    {
        Assert.Equal(
            CompanionContentSource.StockPresets,
            CompanionContentResolver.ResolvePersonalitySource(
                CompanionContentSource.None, manifestHasPersonalities: false));
    }

    [Fact]
    public void EverySourceHasItsOwnLogName()
    {
        var names = System.Enum.GetValues<CompanionContentSource>()
            .Select(CompanionContentResolver.Describe)
            .ToArray();

        Assert.Equal(names.Length, names.Distinct().Count());
        Assert.Equal("stock-presets", CompanionContentResolver.Describe(CompanionContentSource.StockPresets));
    }

    // ---------------------------------------------------------------- personalities.json parsing

    [Fact]
    public void APersonalityFileParsesIntoTheSameShapeAsAManifest()
    {
        var defs = ModCompanionContent.ParsePersonalities(
            """
            [
              { "id": "bambi-bestie", "name": "Bestie", "description": "d",
                "promptSettings": { "Personality": "be a bestie" } }
            ]
            """);

        var one = Assert.Single(defs);
        Assert.Equal("bambi-bestie", one.Id);
        Assert.Equal("Bestie", one.Name);
        Assert.Equal("be a bestie", one.PromptSettings!["Personality"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{ \"id\": \"x\" }")]
    public void AGarbledPersonalityFileYieldsNothingSoTheNextRungIsUsed(string json)
    {
        Assert.Empty(ModCompanionContent.ParsePersonalities(json));
    }

    [Fact]
    public void EntriesWithNoIdOrNoNameAreDroppedRatherThanHalfRegistered()
    {
        var defs = ModCompanionContent.SanitizePersonalities(new List<ModPersonality?>
        {
            new() { Id = "", Name = "nameless id" },
            new() { Id = "no-name", Name = "  " },
            null,
            new() { Id = "keeper", Name = "Keeper" }
        });

        var one = Assert.Single(defs);
        Assert.Equal("keeper", one.Id);
    }

    [Fact]
    public void ADuplicateIdIsIgnoredSoOneEntryCannotShadowAnother()
    {
        var defs = ModCompanionContent.SanitizePersonalities(new List<ModPersonality?>
        {
            new() { Id = "dup", Name = "First" },
            new() { Id = "DUP", Name = "Second" }
        });

        Assert.Equal("First", Assert.Single(defs).Name);
    }

    [Fact]
    public void PromptTextFromAPackIsCappedTheSameWayAManifestIs()
    {
        var defs = ModCompanionContent.SanitizePersonalities(new List<ModPersonality?>
        {
            new()
            {
                Id = "big",
                Name = new string('n', ModCompanionContent.MaxPersonalityNameLength + 40),
                PromptSettings = new Dictionary<string, string>
                {
                    ["Personality"] = new string('p', ModCompanionContent.MaxPromptSettingLength + 500)
                }
            }
        });

        var one = Assert.Single(defs);
        Assert.Equal(ModCompanionContent.MaxPersonalityNameLength, one.Name.Length);
        Assert.Equal(ModCompanionContent.MaxPromptSettingLength, one.PromptSettings!["Personality"].Length);
    }

    [Fact]
    public void APackCannotRegisterMorePersonalitiesThanAManifestCould()
    {
        var many = Enumerable.Range(0, ModCompanionContent.MaxPersonalities + 15)
            .Select(i => (ModPersonality?)new ModPersonality { Id = "p" + i, Name = "P" + i })
            .ToList();

        Assert.Equal(ModCompanionContent.MaxPersonalities,
            ModCompanionContent.SanitizePersonalities(many).Count);
    }
}
