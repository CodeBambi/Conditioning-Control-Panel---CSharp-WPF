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
    // ---- the themed mark belongs to the mod it names (item 5) ---------------------------

    /// <summary>
    /// Two marks, same dial: `logo.png` reads BAMBI SLEEP across the bottom, `logo2.png` reads
    /// CONDITIONING CONTROL PANEL. Both ship, because the first is BambiSleep's own art. The bug
    /// this pins is the DEFAULT: every site that picks between them used to name the mods that
    /// should NOT see Bambi's brand, which left Drone, Locked, Infection and every user mod
    /// showing it. Both files have to exist for the choice to mean anything.
    /// </summary>
    [Fact]
    public void BothMarksShip()
    {
        var resources = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources");
        Assert.True(File.Exists(Path.Combine(resources, "logo.png")));
        Assert.True(File.Exists(Path.Combine(resources, "logo2.png")));

        var csproj = AppText("ConditioningControlPanel.csproj");
        Assert.Contains(@"<Resource Include=""Resources\logo.png"" />", csproj, StringComparison.Ordinal);
        Assert.Contains(@"<Resource Include=""Resources\logo2.png"" />", csproj, StringComparison.Ordinal);
    }

    /// <summary>
    /// CCP Default's card in the mod picker is the baseline's own face, shown next to the four
    /// themed cards on the first screen a new user sees. It was drawing BambiSleep's mark, so the
    /// picker offered another mod's brand as the "no mod" option.
    /// </summary>
    [Fact]
    public void TheBaselinePickerCardUsesTheNeutralMark()
    {
        var catalogue = AppText("Dialogs", "ModPackCatalog.cs");
        var entry = catalogue.IndexOf("ModId = BuiltInMods.CCPDefaultId", StringComparison.Ordinal);
        Assert.True(entry > 0, "the CCP Default entry is gone from ModPackCatalog");

        var block = catalogue.Substring(entry, Math.Min(900, catalogue.Length - entry));
        Assert.Contains("pack://application:,,,/Resources/logo2.png", block, StringComparison.Ordinal);
        Assert.DoesNotContain("pack://application:,,,/Resources/logo.png", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// The three sites that choose between the marks must all key on "is the active mod the one
    /// this file is named after", never on an allow-list of mods to spare. An allow-list is what
    /// let three built-in mods and every creator mod keep showing the Bambi dial.
    /// </summary>
    [Theory]
    [InlineData("MainWindow/MainWindow.xaml.cs")]
    [InlineData("Windows/FirstRunWizard.xaml.cs")]
    public void TheMarkIsChosenByNamingTheModItBelongsTo(string relPath)
    {
        var text = AppText(relPath.Split('/'));
        var pick = text.IndexOf("var logoFile =", StringComparison.Ordinal);
        Assert.True(pick > 0, relPath + " no longer picks a logo file");

        var block = text.Substring(Math.Max(0, pick - 700), Math.Min(900, text.Length - Math.Max(0, pick - 700)));
        Assert.Contains("BuiltInMods.BambiSleepId", block, StringComparison.Ordinal);
        Assert.DoesNotContain("IsCCPDefault", block, StringComparison.Ordinal);

        // A creator mod that ships its own logo.png still outranks both bundled files.
        Assert.Contains("HasModOverride(\"logo.png\")", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// The uncategorised quest fallback resolves "logo.png"
    /// (QuestDefinitionService.GetFallbackImagePath), so the same inversion has to hold on the
    /// quest tiles - and it must stay BELOW the mod-override check, or it would paint over a
    /// creator mod's own art.
    /// </summary>
    [Fact]
    public void TheQuestArtFallbackDoesNotHandOutAnotherModsBrand()
    {
        var text = AppText("MainWindow", "MainWindow.xaml.cs");

        var over = text.IndexOf("ModResourceResolver.HasModOverride(relativePath)", StringComparison.Ordinal);
        var swap = text.IndexOf("return \"pack://application:,,,/Resources/logo2.png\";", StringComparison.Ordinal);
        Assert.True(over > 0 && swap > over,
            "the logo swap must come after the mod-override check, or a creator mod loses its art");

        var block = text.Substring(over, swap - over);
        Assert.Contains("BuiltInMods.BambiSleepId", block, StringComparison.Ordinal);
        Assert.DoesNotContain("IsCCPDefault", block, StringComparison.Ordinal);
    }

    // ---- the way out is written on the tab (item 6) -------------------------------------

    private static readonly string[] LanguageFiles =
        { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    /// <summary>
    /// A safety valve nobody can find is not a safety valve. The exit hint has to be on the tab for
    /// the whole lockdown, in every language, and it has to name the phrase the code actually
    /// compares against - LockdownService.TryExitWithPhrase matches the literal English "let me
    /// out", so that half of the string is not translatable and must survive in all nine files.
    /// </summary>
    [Fact]
    public void TheExitHintIsOnTheLockdownTabInEveryLanguage()
    {
        var xaml = AppText("Views", "Tabs", "LockdownTabView.xaml");
        Assert.Contains("{loc:Str lockdown_exit_hint}", xaml, StringComparison.Ordinal);

        // Inside the panel that is only visible while a lockdown runs, not the setup panel.
        var active = xaml.IndexOf("x:Name=\"LockdownActivePanel\"", StringComparison.Ordinal);
        var hint = xaml.IndexOf("lockdown_exit_hint", StringComparison.Ordinal);
        Assert.True(active > 0 && hint > active, "the exit hint is outside LockdownActivePanel");

        foreach (var lang in LanguageFiles)
        {
            var path = Path.Combine(RepoRoot(), "ConditioningControlPanel",
                "Localization", "Languages", lang + ".json");
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(doc.RootElement.TryGetProperty("lockdown_exit_hint", out var value),
                lang + ".json has no lockdown_exit_hint - the tab would render the raw key");
            Assert.Contains("let me out", value.GetString() ?? "", StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The hint states the mechanic; it must not become the mechanic. Possession rewrites visible
    /// text during a lockdown, so this one line is excluded the same way the emergency-exit button
    /// is - the way out may not be edited by the thing the user is trying to get out of.
    /// </summary>
    [Fact]
    public void TheExitMechanicIsUnchangedAndTheHintCannotBePossessed()
    {
        var xaml = AppText("Views", "Tabs", "LockdownTabView.xaml");
        var hint = xaml.IndexOf("lockdown_exit_hint", StringComparison.Ordinal);
        var block = xaml.Substring(Math.Max(0, hint - 200), Math.Min(400, xaml.Length - Math.Max(0, hint - 200)));
        Assert.Contains("poss:Possession.Exclude=\"True\"", block, StringComparison.Ordinal);

        Assert.Contains("\"let me out\"", AppText("Services", "Haptics", "LockdownService.cs"), StringComparison.Ordinal);
        Assert.Contains("_lockdownTimerClickCount >= 5", AppText("MainWindow", "MainWindow.Lab.cs"), StringComparison.Ordinal);
    }
    // ---- a remote participant can never enable Strict Lock (item 7) ----------------------

    /// <summary>
    /// Owner decision: no. Strict Lock removes SKIP and CLOSE from a mandatory video - every other
    /// Full-tier command changes what the subject sees, this one changes what they can still do
    /// about it. The client refuses it whatever the server's tier allowlist says, so this reads the
    /// dispatch directly: neither entry point may still write StrictLockEnabled = true.
    /// </summary>
    [Fact]
    public void NoRemoteCommandPathTurnsStrictLockOn()
    {
        var svc = AppText("Services", "RemoteControlService.cs");

        Assert.DoesNotContain("StrictLockEnabled = true", svc, StringComparison.Ordinal);
        Assert.Contains("case \"enable_strict_lock\":", svc, StringComparison.Ordinal);
        Assert.Contains("ReportCommandRefused(action, StrictLockRefusal)", svc, StringComparison.Ordinal);

        // The other half: start_session used to carry a strict_lock parameter that bypassed the
        // command entirely. It is refused, not honoured.
        var start = svc.IndexOf("parameters?[\"strict_lock\"]", StringComparison.Ordinal);
        Assert.True(start > 0, "the start_session strict_lock parameter handling is gone - check it was not silently dropped");
        Assert.Contains("ReportCommandRefused", svc.Substring(start, 220), StringComparison.Ordinal);
    }

    /// <summary>
    /// Turning it OFF stays remote-controllable. The asymmetry is the point: that direction only
    /// ever hands the subject a way out of a clip.
    /// </summary>
    [Fact]
    public void TurningStrictLockOffIsStillAllowed()
    {
        var svc = AppText("Services", "RemoteControlService.cs");
        var off = svc.IndexOf("case \"disable_strict_lock\":", StringComparison.Ordinal);
        Assert.True(off > 0);
        Assert.Contains("StrictLockEnabled = false", svc.Substring(off, 250), StringComparison.Ordinal);
    }

    /// <summary>
    /// A refused command must not be announced as a change. The toast and the command log both read
    /// CommandLabels, so enable_strict_lock has to map to the refusal string in all 9 languages or
    /// the subject is told a safety setting flipped when it did not.
    /// </summary>
    [Fact]
    public void ARefusedStrictLockIsNotReportedAsEnabled()
    {
        Assert.Contains("[\"enable_strict_lock\"] = \"cmd_strict_lock_refused\"",
            AppText("MainWindow", "MainWindow.xaml.cs"), StringComparison.Ordinal);

        foreach (var lang in LanguageFiles)
        {
            var path = Path.Combine(RepoRoot(), "ConditioningControlPanel",
                "Localization", "Languages", lang + ".json");
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(doc.RootElement.TryGetProperty("cmd_strict_lock_refused", out _),
                lang + ".json has no cmd_strict_lock_refused");
        }
    }

    /// <summary>
    /// The waiver is where the subject decides how far this goes, so it must no longer promise a
    /// power the app refuses, and it should say what the controller can never do.
    /// </summary>
    [Fact]
    public void TheWaiverNoLongerOffersStrictLock()
    {
        var waiver = AppText("MainWindow", "MainWindow.RemoteControl.cs");
        Assert.DoesNotContain("- Enable strict lock", waiver, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("can NEVER enable Strict Lock", waiver, StringComparison.Ordinal);
    }
    // ---- the app icon is the neutral dial (item 8) --------------------------------------

    /// <summary>
    /// Reads the ICONDIR at the head of an .ico and returns (width, height, bitsPerPixel) per
    /// frame. Deliberately parses the bytes rather than going through System.Drawing: the Icon
    /// class silently picks ONE frame, which is the opposite of what needs checking here, and it
    /// will not hand back a 256px frame at all.
    /// </summary>
    private static List<(int W, int H, int Bpp)> IconFrames(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var count = BitConverter.ToUInt16(bytes, 4);
        var frames = new List<(int, int, int)>(count);
        for (var i = 0; i < count; i++)
        {
            var o = 6 + i * 16;
            // 0 in the byte-wide dimension fields means 256 - the field cannot hold the value.
            var w = bytes[o] == 0 ? 256 : bytes[o];
            var h = bytes[o + 1] == 0 ? 256 : bytes[o + 1];
            frames.Add((w, h, BitConverter.ToUInt16(bytes, o + 6)));
        }
        return frames;
    }

    /// <summary>
    /// app.ico is the mark a person meets BEFORE the app opens: taskbar, tray, Alt-Tab, the
    /// shortcuts Inno creates, and the installer's own icon. It used to be the BAMBI SLEEP dial.
    /// Seven frames at 32bpp, covering every size Windows asks for from a 16px list row to the
    /// 256px Explorer preview.
    /// </summary>
    [Fact]
    public void TheAppIconCoversEverySizeWindowsAsksFor()
    {
        var ico = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "app.ico");
        Assert.True(File.Exists(ico));

        var frames = IconFrames(ico);
        Assert.Equal(new[] { 16, 24, 32, 48, 64, 128, 256 }, frames.Select(f => f.W).ToArray());

        foreach (var f in frames)
        {
            Assert.Equal(f.W, f.H);
            Assert.Equal(32, f.Bpp);
        }
    }

    /// <summary>
    /// The filename is the contract. Three separate consumers hardcode it, and keeping it is the
    /// whole reason swapping the art needed no path change anywhere: the SDK embeds it into the
    /// exe, Inno stamps it on Setup, and the tray service loads it at runtime.
    /// </summary>
    [Fact]
    public void EverythingStillPointsAtResourcesAppIco()
    {
        var csproj = AppText("ConditioningControlPanel.csproj");
        Assert.Contains(@"<ApplicationIcon>Resources\app.ico</ApplicationIcon>", csproj, StringComparison.Ordinal);
        Assert.Contains(@"<Resource Include=""Resources\app.ico"" />", csproj, StringComparison.Ordinal);

        var iss = File.ReadAllText(Path.Combine(RepoRoot(), "installer.iss"));
        Assert.Contains(@"SetupIconFile=ConditioningControlPanel\Resources\app.ico", iss, StringComparison.Ordinal);

        Assert.Contains("Resources/app.ico",
            AppText("Services", "Notifications", "TrayIconService.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The generator has to build from logo2.png. Pointing it back at logo.png would rebuild the
    /// BAMBI SLEEP icon from a script that looks like it is doing the right thing.
    /// </summary>
    [Fact]
    public void TheIconIsGeneratedFromTheNeutralArt()
    {
        var script = AppText("Scripts", "make-app-icon.ps1");
        Assert.Contains(@"'Resources\logo2.png'", script, StringComparison.Ordinal);
        Assert.DoesNotContain(@"'Resources\logo.png'", script, StringComparison.Ordinal);

        // The small frames drop the lettering, which is mush below 128. If someone raises this
        // to include every size, the 16px taskbar icon goes back to three illegible pink bars.
        Assert.Contains("$LetteringLegibleAbove = 64", script, StringComparison.Ordinal);
    }
}
