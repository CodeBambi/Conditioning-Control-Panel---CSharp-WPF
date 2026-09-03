using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
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
    private void UseMod(ModManifest manifest, Dictionary<string, string>? poolOverride = null)
    {
        var mods = (ModService)RuntimeHelpers.GetUninitializedObject(typeof(ModService));
        SetPrivate(mods, "_activeMod", new ModPackage(manifest, null, isBuiltIn: true));
        SetStatic("Mods", mods);

        var pool = poolOverride ?? manifest.Browser?.DefaultVideoLinks;
        BambiSprite.VideoPoolProvider = () => pool;
        BambiSprite.InvalidateStablePrompt();
    }

    /// <summary>
    /// The ANALYTIC worst case, not an observed one. The title sample is drawn once per app-session
    /// from a per-launch seed, so a plain run measures ONE permutation out of many and the spread
    /// between the cheapest and the dearest runs to hundreds of chars: wide enough to hide a
    /// regression, or to red-fail CI on an unlucky seed. Handing the sprite a pool holding only the
    /// longest <see cref="BambiSprite.BambiTitleSample"/> titles forces the sample to be exactly the
    /// most expensive one that can ever occur, whatever the seed does.
    /// </summary>
    private static Dictionary<string, string> WorstCaseBambiPool()
    {
        var links = BuiltInMods.BambiSleep.Browser!.DefaultVideoLinks!;
        return links
            .Where(kvp => !string.Equals(kvp.Key, "Movies", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kvp => kvp.Key.Length)
            .Take(BambiSprite.BambiTitleSample)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
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
        // Bambi runs against the worst-case title permutation, so this cannot pass on a lucky seed.
        if (modId == BuiltInMods.BambiSleepId) UseMod(BuiltInMods.BambiSleep, WorstCaseBambiPool());
        else UseMod(BuiltInMods.CCPDefault);

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
    /// The compound worst case: the longest preset (Slut Mode) AND a taken quiz (whose profile
    /// block the settings setter caps at 200 chars) AND the most expensive title permutation. This
    /// is the exact shape that used to bust the proxy's 10,000-char hard reject and pin the
    /// companion on canned phrases permanently.
    ///
    /// <para>It lands ~280 chars OVER the 9,000-char soft ceiling and stays there on purpose. The
    /// only lever left inside this block is the title count, and cutting the list back under the
    /// ceiling means listing fewer titles than
    /// <see cref="RecentRecommendations.MaxTracked"/> - a prompt that names N videos while the tail
    /// forbids all N. Losing the tail on this one configuration is the cheaper failure than having
    /// nothing legal to suggest, so the pin here is the hard cap plus a tight ceiling on the
    /// overshoot: the request still succeeds, and the block cannot quietly regrow.</para>
    /// </summary>
    [Fact]
    public void BambiWithSlutModeAndAQuizResult_StaysUnderTheProxyHardRejectCap()
    {
        UseMod(BuiltInMods.BambiSleep, WorstCaseBambiPool());
        _settings.SlutModeEnabled = true;
        _settings.LatestQuizScorePercentage = 87;
        _settings.LatestQuizArchetype = "Eager Bimbo";
        _settings.LatestQuizProfileText = new string('x', 400);

        var length = PrefixLengthFor(PersonalityPresets.SlutModeId);
        _out.WriteLine($"bambi + slutmode + quiz prefix (worst-case titles) = {length}");
        // 10,295 before this change, and every cloud call 400'd.
        Assert.True(length < PromptAssembler.ProxyHardRejectCap,
            $"prefix {length} chars is at or over the {PromptAssembler.ProxyHardRejectCap}-char proxy cap");

        // Between the soft ceiling and the hard cap the request still goes through, but Compose can
        // only shed the tail, so memory and the anti-repeat set are dropped. Bound how far into that
        // band the compound worst case is allowed to reach; every other configuration, this preset
        // included, stays fully under the ceiling.
        const int OvershootAllowance = 400;
        Assert.True(length < PromptAssembler.SystemMessageCharCeiling + OvershootAllowance,
            $"prefix {length} chars is more than {OvershootAllowance} over the " +
            $"{PromptAssembler.SystemMessageCharCeiling}-char soft ceiling");
    }

    /// <summary>
    /// The block that carried the bloat, pinned directly so a future edit to the Bambi media
    /// section cannot quietly re-inflate the whole prefix.
    /// </summary>
    [Fact]
    public void BambiMediaBlock_StaysUnderItsOwnBudget()
    {
        UseMod(BuiltInMods.BambiSleep, WorstCaseBambiPool());
        var block = InvokeCoreMediaLinks();
        _out.WriteLine($"bambi media block (worst-case titles) = {block.Length} chars");
        // 3,252 chars before this change. The floor is not far below because the eight BambiCloud
        // playlist links are ~750 chars on their own and are not compressible - they must reach the
        // model verbatim or it cannot emit a working markdown link. Measured against the worst-case
        // title permutation, not an average one.
        Assert.True(block.Length < 2400, $"Bambi media block is {block.Length} chars (budget 2400)");

        // Still load-bearing: the playlist URLs, and a worked [Title](url) example whose SHAPE a
        // small model can copy. Instructions without a demonstration are what produced invented
        // URLs before, so the example is pinned by pattern rather than by prose.
        Assert.Contains("HOW TO LINK", block, StringComparison.Ordinal);
        Assert.Contains("bambicloud.com/playlist/", block, StringComparison.Ordinal);
        Assert.Contains("[IQ Programming]", block, StringComparison.Ordinal);
        Assert.Matches(@"Example:[^\n]*\[[^\]]+\]\(https://bambicloud\.com/playlist/[0-9a-fA-F-]+\)", block);

        // The one behaviour the deleted obsolete-audio paragraph carried, kept in a single line: an
        // ask by a retired audio name is steered to the playlist that replaced it.
        Assert.Contains("Bambi IQ Lock", block, StringComparison.Ordinal);
        Assert.Contains("Programming playlist", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sample has to stay wider than the anti-repeat window. The dynamic tail bans the last
    /// <see cref="RecentRecommendations.MaxTracked"/> picks by name for 24h; if the media block
    /// listed that many titles or fewer, a long session would arrive at a prompt that names N
    /// videos and forbids all N in the same breath, leaving the model nothing legal to suggest.
    /// The constant is written as MaxTracked + margin, so this pins the relationship, not a number.
    /// </summary>
    [Fact]
    public void TheTitleSample_IsWiderThanTheAntiRepeatWindow()
    {
        Assert.True(BambiSprite.BambiTitleSample > RecentRecommendations.MaxTracked,
            $"sample {BambiSprite.BambiTitleSample} does not exceed the {RecentRecommendations.MaxTracked}-pick " +
            "exclusion window, so a long session can ban every title the model can see");

        // And it has to be drawable from the shipped pool, or the margin is imaginary.
        var pool = BuiltInMods.BambiSleep.Browser!.DefaultVideoLinks!.Keys
            .Count(k => !string.Equals(k, "Movies", StringComparison.OrdinalIgnoreCase));
        Assert.True(pool >= BambiSprite.BambiTitleSample,
            $"pool has {pool} titles, sample wants {BambiSprite.BambiTitleSample}");
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

    /// <summary>
    /// When the two safety blocks alone bust the cap there is nothing left to cut - neither may be
    /// trimmed - so the salvage declines rather than spending a third round trip on a request that
    /// is certain to come back 400. The user gets the canned-phrase fallback either way; this only
    /// decides whether they wait through another proxy call first.
    /// </summary>
    [Fact]
    public void Salvage_ReturnsNull_WhenTheSafetyBlocksAloneBustTheCap()
    {
        var tinyCap = SafetyComposer.Preamble.Length + 10;
        var messages = new[]
        {
            new ProxyChatMessage { Role = ChatMessage.RoleSystem, Content = Oversize(Cap + 2000) },
            new ProxyChatMessage { Role = ChatMessage.RoleUser, Content = "hi" }
        };

        // The cut itself still refuses to trim either block, and so still exceeds the cap...
        Assert.True(AiService.MiddleCutSystemPrompt(messages[0].Content!, tinyCap).Length > tinyCap);
        // ...which is exactly why the salvage refuses to send it.
        Assert.Null(AiService.SalvageOversizeSystemMessage(messages, tinyCap));
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

    public void Dispose()
    {
        PromptAssembler.NoticeDispatcherForTests = null;
        PromptAssembler.ResetOversizeNoticeForTests();
    }

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

    /// <summary>
    /// With no surface to show it on, the session's single notice must NOT be spent: an ambient
    /// compose during startup happens before there is an Application, and burning the notice there
    /// would leave the user with a degraded companion and no reason ever given. Composing has to
    /// stay safe off the UI thread all the same, so this doubles as the assertion that an oversize
    /// prompt on a background thread cannot throw.
    /// </summary>
    [Fact]
    public void WithNoUiToShowItOn_TheNoticeIsNotSpent()
    {
        PromptAssembler.NoticeDispatcherForTests = () => null;
        var prefix = OverCapPrefix();

        for (int i = 0; i < 5; i++)
            PromptAssembler.Compose(prefix, PromptAssembler.TailHeader + "\nAnswer in one short bubble.",
                "Answer in one short bubble.");

        Assert.False(PromptAssembler.OversizeNoticeRaised);
        // Still unspent: the first compose that DOES have a surface gets to show it.
        Assert.True(PromptAssembler.ClaimOversizeNotice());
    }

    /// <summary>
    /// And once a surface exists, an oversize prompt raises it - once, however many calls follow.
    /// </summary>
    [Fact]
    public void OnceThereIsAUi_TheNoticeIsRaisedExactlyOnce()
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        PromptAssembler.NoticeDispatcherForTests = () => dispatcher;
        var prefix = OverCapPrefix();

        for (int i = 0; i < 5; i++)
            PromptAssembler.Compose(prefix, PromptAssembler.TailHeader + "\nAnswer in one short bubble.",
                "Answer in one short bubble.");

        Assert.True(PromptAssembler.OversizeNoticeRaised);
        // Spent exactly once: nothing is left for a sixth call to claim.
        Assert.False(PromptAssembler.ClaimOversizeNotice());
    }

    /// <summary>
    /// The once-per-session property itself, on the latch the raise takes after its guard passes.
    /// Five hundred oversize calls in one session are worth exactly one toast.
    /// </summary>
    [Fact]
    public void TheNotice_IsClaimableExactlyOncePerSession()
    {
        Assert.True(PromptAssembler.ClaimOversizeNotice());
        Assert.True(PromptAssembler.OversizeNoticeRaised);

        for (int i = 0; i < 500; i++) Assert.False(PromptAssembler.ClaimOversizeNotice());
    }

    /// <summary>
    /// The key has to resolve to real English, or the toast reads "companion_prompt_too_long_notice"
    /// at the one moment the user needs a sentence. Read from the shipped file rather than through
    /// Loc, which wants an App behind it.
    /// </summary>
    [Fact]
    public void TheNoticeKey_ResolvesToRealEnglishText()
    {
        var en = EnglishStrings();
        Assert.True(en.TryGetValue(PromptAssembler.OversizeNoticeLocKey, out var text),
            $"{PromptAssembler.OversizeNoticeLocKey} is missing from en.json");
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.NotEqual(PromptAssembler.OversizeNoticeLocKey, text);
        // It has to say what happened AND what to do about it, or it is only an alarm.
        Assert.Contains("too long", text!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trim", text!, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> EnglishStrings()
    {
        var path = Path.Combine(SourceRoots.LanguagesDirectory, "en.json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
               ?? new Dictionary<string, string>();
    }
}
