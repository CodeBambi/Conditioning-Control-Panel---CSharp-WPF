using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The installer ships a NEUTRAL baseline; themed vocabulary arrives with a mod. These tests pin
/// the parts of that promise that are one careless edit away from being false again - a default
/// that quietly goes back to the BambiSleep list would put the themed triggers in front of a user
/// who never chose a mod, and nothing else in the suite would notice.
/// </summary>
public class NeutralInstallerBaselineTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string AppText(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    /// <summary>
    /// Words a stranger reading a fresh install's phrase list must not find there. Substring match,
    /// case-insensitive, so "BAMBI SLEEP" and "COCK ZOMBIE NOW" are both caught by one entry each.
    /// </summary>
    private static readonly string[] ThemedWords =
        { "BAMBI", "BIMBO", "COCK", "SISSY", "SLUT", "GOOD GIRL", "GIGGLETIME" };

    private static void AssertNeutral(System.Collections.Generic.IEnumerable<string> phrases, string what)
    {
        foreach (var phrase in phrases)
        foreach (var word in ThemedWords)
            Assert.False(phrase.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0,
                $"{what} carries the themed phrase '{phrase}' - the out-of-box baseline is neutral");
    }

    [Fact]
    public void TheCcpDefaultManifestIsNeutral()
    {
        AssertNeutral(BuiltInMods.CCPDefault.SubliminalPool!.Keys, "CCPDefault.SubliminalPool");
        AssertNeutral(BuiltInMods.CCPDefault.LockCardPhrases!.Keys, "CCPDefault.LockCardPhrases");
        AssertNeutral(BuiltInMods.CCPDefault.CustomTriggers!, "CCPDefault.CustomTriggers");
    }

    /// <summary>
    /// The themed lists still exist - they just belong to the mod that owns them. If this ever goes
    /// green with an empty pool, someone has neutralised BambiSleep itself rather than the default.
    /// </summary>
    [Fact]
    public void TheBambiManifestStillOwnsTheThemedVocabulary()
    {
        Assert.Contains("BAMBI SLEEP", BuiltInMods.BambiSleep.SubliminalPool!.Keys);
        Assert.True(BuiltInMods.BambiSleep.SubliminalPool!.Count >= 20,
            "the BambiSleep pool lost phrases - it is the mod's content, not a default to trim");
    }

    /// <summary>
    /// AppSettings' fresh-install pools are COPIES of the neutral manifest, never the themed one and
    /// never a duplicated literal list (a second copy is a second thing to forget).
    /// </summary>
    [Fact]
    public void FreshInstallPoolsAreSeededFromTheNeutralManifest()
    {
        var settings = AppText("Models", "AppSettings.cs");

        Assert.Contains("_subliminalPool = new(BuiltInMods.CCPDefault.SubliminalPool", settings, StringComparison.Ordinal);
        Assert.Contains("_lockCardPhrases = new(BuiltInMods.CCPDefault.LockCardPhrases", settings, StringComparison.Ordinal);
        Assert.Contains("_customTriggers = new(BuiltInMods.CCPDefault.CustomTriggers", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("BuiltInMods.BambiSleep.SubliminalPool", settings, StringComparison.Ordinal);
    }

    /// <summary>
    /// The "which phrases are defaults" lookup behind the Manage Messages editor. Both copies of it
    /// fall back to the NEUTRAL pool when the mod layer is not up yet; either one still naming
    /// BambiSleep would tell an unmodded user the themed list is their factory set.
    /// </summary>
    [Fact]
    public void TheManageMessagesEditorFallsBackToTheNeutralPool()
    {
        foreach (var text in new[]
                 {
                     AppText("MainWindow", "MainWindow.UiUpdates.cs"),
                     AppText("Features", "SubliminalFeatureControl.xaml.cs"),
                 })
        {
            Assert.Contains("?? Models.BuiltInMods.CCPDefault.SubliminalPool", text, StringComparison.Ordinal);
            Assert.DoesNotContain("?? Models.BuiltInMods.BambiSleep.SubliminalPool", text, StringComparison.Ordinal);
        }
    }


    // ---- trigger audio leaves the box (item 2) -----------------------------------------

    /// <summary>
    /// The csproj strips a file set; build-content-packs.ps1 packs a file set; the two are kept in
    /// step only by $CsprojStripPatterns, a HAND-MAINTAINED mirror the pack build hard-fails on.
    /// This is the cheap half of that guarantee: every entry the csproj excludes for trigger audio
    /// also appears in the script's mirror, so a one-sided edit is caught at `dotnet test` rather
    /// than at release time (or, worse, as audio that ships in neither the installer nor a pack).
    /// </summary>
    [Fact]
    public void TheTriggerAudioStripSetIsMirroredInThePackScript()
    {
        var entries = TriggerAudioStripEntries();
        var script = AppText("Scripts", "build-content-packs.ps1");

        Assert.True(entries.Count >= 18,
            $"expected the sub_audio folder plus 17 named root files, found {entries.Count}");

        foreach (var entry in entries)
            Assert.True(script.Contains("'" + entry.Replace("'", "''") + "'", StringComparison.Ordinal),
                $"the csproj strips '{entry}' but $CsprojStripPatterns does not list it");
    }

    /// <summary>
    /// The neutral SFX share Resources\sounds with the trigger mp3s, which is exactly why that half
    /// of the strip set is written out by name. A glob creeping in here would silence the chimes,
    /// the giggles, the faucet and the level-up sting on every install.
    /// </summary>
    [Fact]
    public void TheNeutralSoundEffectsStayInTheBox()
    {
        var block = string.Join("\n", TriggerAudioStripEntries());
        Assert.DoesNotContain(@"Resources\sounds\**", block, StringComparison.Ordinal);

        foreach (var keeper in new[] { "chime1.mp3", "giggle1.MP3", "lvup.mp3", "result.mp3", "faucet_pour.wav" })
        {
            Assert.DoesNotContain(keeper, block, StringComparison.OrdinalIgnoreCase);
            Assert.True(
                File.Exists(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "sounds", keeper)),
                keeper + @" is gone from Resources\sounds - it is a neutral SFX and ships in the box");
        }
    }

    private static List<string> TriggerAudioStripEntries()
    {
        var csproj = AppText("ConditioningControlPanel.csproj");
        var start = csproj.IndexOf("<ContentPackTriggerAudioExclude>", StringComparison.Ordinal);
        var end = csproj.IndexOf("</ContentPackTriggerAudioExclude>", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "the trigger-audio strip property is gone from the csproj");

        return csproj.Substring(start, end - start)
            .Split('\n')
            .Select(l => l.Trim().Trim(';').Trim())
            .Where(l => l.StartsWith("Resources", StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Nothing may anchor the whisper clips to the install directory any more: they ride in the
    /// mod-bambi pack, so every consumer has to ask ContentLocator which root holds them. The test
    /// reads the line around each BaseDirectory mention rather than the whole file, because these
    /// services legitimately use BaseDirectory for content that DID stay in the box.
    /// </summary>
    [Theory]
    [InlineData("Services/Subliminal/SubliminalService.cs")]
    [InlineData("Services/KeywordTriggerService.cs")]
    [InlineData("Services/AudioService.cs")]
    [InlineData("Services/Arcademy/ArcademyHostService.cs")]
    [InlineData("Services/Quiz/IntakeHostService.cs")]
    public void NoConsumerHardcodesTheInstallDirForWhisperClips(string relPath)
    {
        var text = AppText(relPath.Split('/'));
        Assert.Contains("sub_audio", text, StringComparison.Ordinal);

        foreach (var line in text.Split('\n'))
        {
            if (!line.Contains("sub_audio", StringComparison.Ordinal)) continue;
            Assert.DoesNotContain("AppContext.BaseDirectory", line, StringComparison.Ordinal);
            Assert.DoesNotContain("AppDomain.CurrentDomain.BaseDirectory", line, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Startup used to scaffold an empty Resources\sub_audio beside the exe, and SubliminalService's
    /// constructor did it again. That folder is the first root ContentLocator finds, so it would
    /// shadow the downloaded pack for every directory-shaped resolve - the ccp.subaudio virtual
    /// host and the intake inliner both.
    /// </summary>
    [Fact]
    public void StartupDoesNotScaffoldAnEmptySubAudioFolder()
    {
        Assert.DoesNotContain("resourcesPath, \"sub_audio\"", AppText("App.xaml.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.CreateDirectory(_audioPath)",
            AppText("Services", "Subliminal", "SubliminalService.cs"), StringComparison.Ordinal);
    }
    // ---- audio-base follows the mod that owns it (item 3) ------------------------------

    /// <summary>
    /// Resources\sounds\flashes_audio is BambiSleep's recorded VO, not a neutral library, and
    /// CompanionContentResolver.OwnsBaselineVoiceLines is the single place that says so. The
    /// download decision has to defer to that predicate rather than keep a second list.
    /// </summary>
    [Fact]
    public void OnlyTheModThatOwnsTheBaselineVoiceAsksForIt()
    {
        Assert.True(ReleaseContentService.ModOwnsBaselineVoice(BuiltInMods.BambiSleepId));

        foreach (var other in new[]
                 {
                     BuiltInMods.CCPDefaultId, BuiltInMods.SissyHypnoId, BuiltInMods.LockedId,
                     BuiltInMods.DronificationId, BuiltInMods.InfectionControlId, "some-creator-mod",
                 })
            Assert.False(ReleaseContentService.ModOwnsBaselineVoice(other),
                other + " would pull 46MB of another character's voice lines at first launch");
    }

    /// <summary>
    /// The empty-id case diverges from OwnsBaselineVoiceLines deliberately: that predicate answers
    /// "keep the baseline" so a resolver called before the mod layer is up still gets a real folder,
    /// but spending a user's bandwidth on the same guess is a different question.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnknownModDownloadsNothing(string? modId)
        => Assert.False(ReleaseContentService.ModOwnsBaselineVoice(modId));

    /// <summary>
    /// Startup only pays for the baseline voice when the active mod speaks it, and the mod that DOES
    /// speak it still gets it outside startup - otherwise a mid-session switch to BambiSleep leaves
    /// the idle voice channel silent until the next launch.
    /// </summary>
    [Fact]
    public void TheBaselineFetchIsGatedOnTheActiveMod()
    {
        var text = AppText("Services", "Content", "ReleaseContentService.cs");
        Assert.Contains("var wantsBaseline = ModOwnsBaselineVoice(", text, StringComparison.Ordinal);
        Assert.Contains("var needsBaseline = wantsBaseline", text, StringComparison.Ordinal);

        Assert.Contains("ModOwnsBaselineVoice(modId)", AppText("App.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("ModOwnsBaselineVoice(card.ModId)",
            AppText("Dialogs", "ModPickerDialog.xaml.cs"), StringComparison.Ordinal);
    }
    // ---- themed session presets travel with their mod (item 4) --------------------------

    private static readonly string[] ThemedSessionFiles =
        { "distant_doll.session.json", "gamer_girl.session.json", "good_girls_dont_cum.session.json" };

    /// <summary>
    /// Three of the four shipped presets are BambiSleep programmes down to their phrase lists, so
    /// they leave the installer with the rest of that mod's content. The files stay in the repo -
    /// they are the pack build's inputs - so this checks the BUILD, not the tree.
    /// </summary>
    [Fact]
    public void TheThemedSessionPresetsAreStrippedFromTheBuild()
    {
        var csproj = AppText("ConditioningControlPanel.csproj");
        var script = AppText("Scripts", "build-content-packs.ps1");

        foreach (var file in ThemedSessionFiles)
        {
            Assert.Contains(@"assets\sessions\" + file, csproj, StringComparison.Ordinal);
            Assert.Contains(@"'assets\sessions\" + file + "'", script, StringComparison.Ordinal);
            Assert.True(
                File.Exists(Path.Combine(RepoRoot(), "ConditioningControlPanel", "assets", "sessions", file)),
                file + " is gone from the repo - it is the pack build's input, not build output");
        }
    }

    /// <summary>
    /// The property has to be declared ABOVE the ItemGroup that uses it. MSBuild evaluates a project
    /// body in document order, so a ContentPack* property defined with the others at the bottom of
    /// the file expands to nothing here and strips nothing at all - a silent no-op that ships the
    /// themed presets anyway.
    /// </summary>
    [Fact]
    public void TheSessionStripPropertyIsDeclaredBeforeItIsUsed()
    {
        var csproj = AppText("ConditioningControlPanel.csproj");
        var declared = csproj.IndexOf("<ContentPackSessionsExclude>", StringComparison.Ordinal);
        var used = csproj.IndexOf("$(ContentPackSessionsExclude)", StringComparison.Ordinal);

        Assert.True(declared > 0, "the session strip property is gone");
        Assert.True(used > declared,
            "$(ContentPackSessionsExclude) is used before it is declared - it would expand to nothing");
    }

    /// <summary>
    /// morning_drift is the one that stays in the box, so the rack is never empty on a fresh
    /// install, and the loader reads BOTH roots so an installed pack's presets rejoin it.
    /// </summary>
    [Fact]
    public void TheNeutralSessionStaysAndTheLoaderReadsBothRoots()
    {
        var csproj = AppText("ConditioningControlPanel.csproj");
        var start = csproj.IndexOf("<ContentPackSessionsExclude>", StringComparison.Ordinal);
        var end = csproj.IndexOf("</ContentPackSessionsExclude>", StringComparison.Ordinal);
        Assert.DoesNotContain("morning_drift", csproj.Substring(start, end - start), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(
            RepoRoot(), "ConditioningControlPanel", "assets", "sessions", "morning_drift.session.json")));

        var loader = AppText("Services", "Session", "SessionFileService.cs");
        Assert.Contains("ContentLocator.EnumerateFiles(BuiltInSessionsRelativeDir", loader, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.GetFiles(BuiltInSessionsFolder", loader, StringComparison.Ordinal);
    }
}
