using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// "Default AI works, the Bambi mod AI doesn't - she only repeats the same canned phrases."
///
/// <para>The only mod-keyed divergence in the whole companion path is system-prompt SIZE. The
/// cloud proxy rejects any single message over 10,000 chars with <c>input_too_large</c>, the
/// client's own soft ceiling is <see cref="PromptAssembler.SystemMessageCharCeiling"/>, and the
/// Bambi branch of <c>BambiSprite.GetCoreMediaLinks</c> used to spend ~3,200 chars on a block that
/// listed every pool title twice over and re-stated a rule the link floor already carries. That put
/// the stock Bambi prefix ~700 chars over the soft ceiling before the dynamic tail was even built:
/// memory, time-of-day and the anti-repeat set were dropped on every call, all history was shed by
/// the context-fit belt, and one extra knowledge-base link or a taken quiz pushed the whole message
/// past the proxy's hard reject - at which point every cloud call, forever, came back as a canned
/// Idle phrase.</para>
///
/// <para>These tests hold the budget: EVERY built-in personality preset, on the two mods that
/// matter, must build a stable prefix that leaves room for the tail.</para>
/// </summary>
[Collection(PromptPrefixStateCollection.Name)]
public class PromptCharBudgetTests : IDisposable
{
    private readonly ITestOutputHelper _out;

    private readonly object? _priorSettings;
    private readonly object? _priorPersonality;
    private readonly object? _priorMods;
    private readonly AppSettings _settings = new();
    private readonly string _tempDir;

    public PromptCharBudgetTests(ITestOutputHelper output)
    {
        _out = output;

        _priorSettings = GetStatic("Settings");
        _priorPersonality = GetStatic("Personality");
        _priorMods = GetStatic("Mods");

        var service = (SettingsService)RuntimeHelpers.GetUninitializedObject(typeof(SettingsService));
        SetBackingField(service, service.GetType(), "Current", _settings);
        _tempDir = Path.Combine(Path.GetTempPath(), "ccp-prompt-budget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        SetPrivate(service, "_settingsPath", Path.Combine(_tempDir, "settings.json"));
        SetPrivate(service, "_timerLock", new object());
        SetPrivate(service, "_saveLock", new object());
        SetStatic("Settings", service);
        SetStatic("Personality", new PersonalityService());
    }

    public void Dispose()
    {
        BambiSprite.VideoPoolProvider = null;
        BambiSprite.InvalidateStablePrompt();
        SetStatic("Settings", _priorSettings);
        SetStatic("Personality", _priorPersonality);
        SetStatic("Mods", _priorMods);
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    /// <summary>Points App.Mods at the given built-in manifest and seeds its shipped pool.</summary>
    private void UseMod(ModManifest manifest)
    {
        var mods = (ModService)RuntimeHelpers.GetUninitializedObject(typeof(ModService));
        SetPrivate(mods, "_activeMod", new ModPackage(manifest, null, isBuiltIn: true));
        SetStatic("Mods", mods);

        var pool = manifest.Browser?.DefaultVideoLinks;
        BambiSprite.VideoPoolProvider = () => pool;
        BambiSprite.InvalidateStablePrompt();
    }

    private static readonly string[] BuiltInPresetIds =
    {
        PersonalityPresets.BambiSpriteId,
        PersonalityPresets.SlutModeId,
        PersonalityPresets.GentleTrainerId,
        PersonalityPresets.StrictDommeId,
        PersonalityPresets.BimboCoachId,
        PersonalityPresets.HypnoGuideId,
        PersonalityPresets.BimboCowId
    };

    private int PrefixLengthFor(string presetId)
    {
        _settings.ActivePersonalityPresetId = presetId;
        _settings.CompanionPrompt = new CompanionPromptSettings();
        BambiSprite.InvalidateStablePrompt();
        return BambiSprite.GetStablePrompt().Length;
    }

    // ---------- the budget itself ----------

    [Theory]
    [InlineData(BuiltInMods.BambiSleepId)]
    [InlineData(BuiltInMods.CCPDefaultId)]
    public void EveryBuiltInPreset_BuildsAPrefixUnderTheSoftCeiling(string modId)
    {
        UseMod(modId == BuiltInMods.BambiSleepId ? BuiltInMods.BambiSleep : BuiltInMods.CCPDefault);

        var over = new List<string>();
        var report = new StringBuilder();
        foreach (var presetId in BuiltInPresetIds)
        {
            var length = PrefixLengthFor(presetId);
            report.AppendLine($"{modId,-22} {presetId,-16} {length,6}");
            if (length >= PromptAssembler.SystemMessageCharCeiling) over.Add($"{presetId}={length}");
        }

        _out.WriteLine(report.ToString());
        Assert.True(over.Count == 0,
            $"stable prefix at or over the {PromptAssembler.SystemMessageCharCeiling}-char soft " +
            $"ceiling for {modId}: {string.Join(", ", over)}");
    }

    /// <summary>
    /// The worst realistic Bambi config: the spiciest preset AND a taken quiz (which appends a
    /// ~400-char profile block to the prefix). This is the exact shape that used to bust the
    /// proxy's 10,000-char hard reject and pin the companion on canned phrases permanently.
    /// </summary>
    [Fact]
    public void BambiWithSlutModeAndAQuizResult_StaysUnderTheProxyHardRejectCap()
    {
        UseMod(BuiltInMods.BambiSleep);
        _settings.SlutModeEnabled = true;
        _settings.LatestQuizScorePercentage = 87;
        _settings.LatestQuizArchetype = "Eager Bimbo";
        _settings.LatestQuizProfileText = new string('x', 400);

        var length = PrefixLengthFor(PersonalityPresets.SlutModeId);
        _out.WriteLine($"bambi + slutmode + quiz prefix = {length}");
        Assert.True(length < PromptAssembler.ProxyHardRejectCap,
            $"prefix {length} chars is at or over the {PromptAssembler.ProxyHardRejectCap}-char proxy cap");
    }

    /// <summary>
    /// The block that carried the bloat, pinned directly so a future edit to the Bambi media
    /// section cannot quietly re-inflate the whole prefix.
    /// </summary>
    [Fact]
    public void BambiMediaBlock_StaysUnderItsOwnBudget()
    {
        UseMod(BuiltInMods.BambiSleep);
        var block = InvokeCoreMediaLinks();
        _out.WriteLine($"bambi media block = {block.Length} chars");
        // 3,252 chars before this change; ~1,820 after. The floor is not lower because the eight
        // BambiCloud playlist links are ~750 chars on their own and are not compressible - they
        // must reach the model verbatim or it cannot emit a working markdown link. The budget is
        // set above the worst-case title sample (the ten longest pool titles), not the average.
        Assert.True(block.Length < 2000, $"Bambi media block is {block.Length} chars (budget 2000)");

        // Still load-bearing: the playlist markdown examples and the playlist URLs themselves.
        Assert.Contains("HOW TO LINK", block, StringComparison.Ordinal);
        Assert.Contains("bambicloud.com/playlist/", block, StringComparison.Ordinal);
        Assert.Contains("[IQ Programming]", block, StringComparison.Ordinal);
    }

    /// <summary>Other mods keep the generic branch they always had.</summary>
    [Fact]
    public void GenericModBranch_IsUnchanged()
    {
        UseMod(BuiltInMods.CCPDefault);
        var block = InvokeCoreMediaLinks();
        Assert.Contains("VIDEO LINKS (the ONLY videos you may name)", block, StringComparison.Ordinal);
        // Every shipped title, one per line, exactly as before.
        foreach (var title in BuiltInMods.CCPDefault.Browser!.DefaultVideoLinks!.Keys
                     .Where(k => !string.Equals(k, "Movies", StringComparison.OrdinalIgnoreCase)))
            Assert.Contains($"- \"{title}\"", block, StringComparison.Ordinal);
    }

    private static string InvokeCoreMediaLinks()
    {
        var sprite = (BambiSprite)RuntimeHelpers.GetUninitializedObject(typeof(BambiSprite));
        var method = typeof(BambiSprite).GetMethod("GetCoreMediaLinks",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (string)method.Invoke(sprite, null)!;
    }

    // ---------- reflection seams (same shape as PersonaWireFidelityTests) ----------

    private static object? GetStatic(string name) =>
        BackingField(typeof(ConditioningControlPanel.App), name,
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).GetValue(null);

    private static void SetStatic(string name, object? value) =>
        BackingField(typeof(ConditioningControlPanel.App), name,
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).SetValue(null, value);

    private static void SetPrivate(object target, string name, object? value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);

    private static void SetBackingField(object target, Type owner, string name, object? value) =>
        BackingField(owner, name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .SetValue(target, value);

    private static FieldInfo BackingField(Type owner, string name, BindingFlags flags)
    {
        var field = owner.GetField($"<{name}>k__BackingField", flags);
        Assert.NotNull(field);
        return field!;
    }
}

/// <summary>
/// The last-resort salvage path: when the system message alone is over the proxy's per-message cap,
/// <c>AiService.CompactForRetry</c> cannot help (it keeps message 0 verbatim by design), so the
/// request fails identically on the retry and the companion is pinned on canned phrases forever.
/// The middle cut is what turns that into "answers with less context" instead.
/// </summary>
public class MiddleCutSystemPromptTests
{
    private const int Cap = 9900;

    private static string Oversize(int totalChars)
    {
        var head = SafetyComposer.Preamble;
        var foot = SafetyComposer.Floor + "\n\n--- RIGHT NOW ---\nAnswer in one short bubble.";
        var middleLength = Math.Max(1, totalChars - head.Length - foot.Length - 4);
        var middle = "\n\n" + new string('m', middleLength) + "\n\n";
        return head + middle + foot;
    }

    [Fact]
    public void UnderTheCap_IsReturnedUnchanged()
    {
        var input = Oversize(Cap - 500);
        Assert.True(input.Length < Cap);
        Assert.Same(input, AiService.MiddleCutSystemPrompt(input, Cap));
    }

    [Fact]
    public void OverTheCap_FitsTheCap_AndKeepsBothSafetyBlocksIntact()
    {
        var input = Oversize(Cap + 4000);
        Assert.True(input.Length > Cap);

        var cut = AiService.MiddleCutSystemPrompt(input, Cap);

        Assert.True(cut.Length <= Cap, $"cut is {cut.Length} chars, cap is {Cap}");
        Assert.StartsWith(SafetyComposer.Preamble, cut, StringComparison.Ordinal);
        Assert.EndsWith("--- RIGHT NOW ---\nAnswer in one short bubble.", cut, StringComparison.Ordinal);
        Assert.Contains(SafetyComposer.Floor, cut, StringComparison.Ordinal);
        // The floor is still the LAST safety block, immediately before the per-call tail.
        Assert.Contains(SafetyComposer.Floor + "\n\n--- RIGHT NOW ---", cut, StringComparison.Ordinal);
        Assert.Contains(AiService.MiddleCutMarker, cut, StringComparison.Ordinal);
    }

    [Fact]
    public void OverTheCap_CutsFromTheMiddle_KeepingBothEndsOfTheMiddleZone()
    {
        var head = SafetyComposer.Preamble;
        var foot = SafetyComposer.Floor;
        var middle = "\n\nPERSONA-OPENING-CANARY\n" + new string('m', 12000) + "\nOUTPUT-RULES-CANARY\n\n";
        var input = head + middle + foot;
        Assert.True(input.Length > Cap);

        var cut = AiService.MiddleCutSystemPrompt(input, Cap);

        Assert.True(cut.Length <= Cap);
        Assert.Contains("PERSONA-OPENING-CANARY", cut, StringComparison.Ordinal);
        Assert.Contains("OUTPUT-RULES-CANARY", cut, StringComparison.Ordinal);
        Assert.True(cut.Length < input.Length);
    }

    [Fact]
    public void WhenTheSafetyBlocksAloneBustTheCap_NeitherIsTrimmed()
    {
        var input = Oversize(Cap + 2000);
        var tinyCap = SafetyComposer.Preamble.Length + 10;

        var cut = AiService.MiddleCutSystemPrompt(input, tinyCap);

        Assert.StartsWith(SafetyComposer.Preamble, cut, StringComparison.Ordinal);
        Assert.Contains(SafetyComposer.Floor, cut, StringComparison.Ordinal);
    }

    // ---------- the salvage wrapper ----------

    [Fact]
    public void Salvage_ReturnsNull_WhenTheSystemMessageAlreadyFits()
    {
        var messages = new[]
        {
            new ProxyChatMessage { Role = ChatMessage.RoleSystem, Content = Oversize(Cap - 1000) },
            new ProxyChatMessage { Role = ChatMessage.RoleUser, Content = "hi" }
        };
        Assert.Null(AiService.SalvageOversizeSystemMessage(messages, Cap));
    }

    [Fact]
    public void Salvage_ShortensOnlyTheSystemMessage_AndLeavesHistoryAlone()
    {
        var messages = new[]
        {
            new ProxyChatMessage { Role = ChatMessage.RoleSystem, Content = Oversize(Cap + 3000) },
            new ProxyChatMessage { Role = ChatMessage.RoleUser, Content = "what should i watch?" }
        };

        var salvaged = AiService.SalvageOversizeSystemMessage(messages, Cap);

        Assert.NotNull(salvaged);
        Assert.Equal(messages.Length, salvaged!.Length);
        Assert.True(salvaged[0].Content!.Length <= Cap);
        Assert.StartsWith(SafetyComposer.Preamble, salvaged[0].Content!, StringComparison.Ordinal);
        Assert.Equal("what should i watch?", salvaged[1].Content);
        // The originals are not mutated.
        Assert.True(messages[0].Content!.Length > Cap);
    }

    [Fact]
    public void Salvage_ReturnsNull_WhenThereIsNoSystemMessage()
    {
        var messages = new[]
        {
            new ProxyChatMessage { Role = ChatMessage.RoleUser, Content = new string('u', Cap + 100) }
        };
        Assert.Null(AiService.SalvageOversizeSystemMessage(messages, Cap));
    }
}

/// <summary>
/// A duplicate url in the name -> url link map used to throw <see cref="ArgumentException"/> out of
/// the middle of the system-prompt build, which takes the whole companion down rather than one
/// line of the prompt. Aliases, a mod shipping one clip under two names and hand-pasted user link
/// lists all produce duplicates, and nothing validates against them on the way in.
/// </summary>
public class ReverseLinkMapTests
{
    [Fact]
    public void DuplicateUrls_DoNotThrow_AndTheFirstNameWins()
    {
        var nameToUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Yes Brain Loop"] = "https://example.test/one",
            ["Yes Brain Loop (alias)"] = "https://example.test/one",
            ["Overload"] = "https://example.test/two"
        };

        var byUrl = BambiSprite.ReverseByUrlFirstWins(nameToUrl);

        Assert.Equal(2, byUrl.Count);
        Assert.Equal("Yes Brain Loop", byUrl["https://example.test/one"]);
        Assert.Equal("Overload", byUrl["https://example.test/two"]);
    }

    [Fact]
    public void LookupIsCaseInsensitive_AndEmptyUrlsAreSkipped()
    {
        var byUrl = BambiSprite.ReverseByUrlFirstWins(new Dictionary<string, string>
        {
            ["Overload"] = "https://EXAMPLE.test/two",
            ["Nothing"] = "   "
        });

        Assert.Single(byUrl);
        Assert.Equal("Overload", byUrl["https://example.test/TWO"]);
    }

    [Fact]
    public void NullMap_YieldsAnEmptyDictionary() =>
        Assert.Empty(BambiSprite.ReverseByUrlFirstWins(null));
}

/// <summary>
/// The oversize notice. A prompt over the proxy's hard cap changes how the companion behaves - she
/// answers from a middle-cut prompt, or from canned phrases - and until now the only witness was a
/// line in crash.log. This pins the two properties that make it a notice rather than a nuisance:
/// it fires, and it fires ONCE per session no matter how many calls the user makes.
/// </summary>
[Collection(PromptPrefixStateCollection.Name)]
public class OversizeNoticeTests : IDisposable
{
    public OversizeNoticeTests() => PromptAssembler.ResetOversizeNoticeForTests();

    public void Dispose() => PromptAssembler.ResetOversizeNoticeForTests();

    private static string OverCapPrefix() =>
        SafetyComposer.Preamble
        + "\n\n" + new string('k', PromptAssembler.ProxyHardRejectCap) + "\n\n"
        + SafetyComposer.Floor;

    [Fact]
    public void APromptUnderTheHardCap_RaisesNoNotice()
    {
        PromptAssembler.Compose(SafetyComposer.Preamble + "\n\n" + SafetyComposer.Floor,
            PromptAssembler.TailHeader + "\nAnswer in one short bubble.",
            "Answer in one short bubble.");

        Assert.False(PromptAssembler.OversizeNoticeRaised);
    }

    [Fact]
    public void APromptOverTheHardCap_RaisesTheNoticeExactlyOnce()
    {
        var prefix = OverCapPrefix();
        Assert.False(PromptAssembler.OversizeNoticeRaised);

        for (int i = 0; i < 5; i++)
        {
            // No Application.Current in a test host, so the dispatch is a no-op - which is itself
            // the assertion that composing a prompt off the UI thread cannot throw.
            PromptAssembler.Compose(prefix, PromptAssembler.TailHeader + "\nAnswer in one short bubble.",
                "Answer in one short bubble.");
        }

        Assert.True(PromptAssembler.OversizeNoticeRaised);
    }

    [Fact]
    public void TheNoticeTextIsLocalized() =>
        Assert.Equal("companion_prompt_too_long_notice", PromptAssembler.OversizeNoticeLocKey);
}
