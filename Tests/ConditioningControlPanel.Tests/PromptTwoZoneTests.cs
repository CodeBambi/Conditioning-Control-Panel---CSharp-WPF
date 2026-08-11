using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The prefix cache and the video-pool seam are process-wide statics, so these tests must not run
/// beside anything else that builds a system prompt.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class PromptPrefixStateCollection
{
    public const string Name = "prompt-prefix-state";
}

/// <summary>
/// Train 1 — the two-zone prompt layout (doc 01 §5.2), the biggest cost lever in the rework.
///
/// Providers discount the longest common prefix of a prompt, and the client used to guarantee that
/// prefix was worthless: <c>SampleVideoTitles()</c> Fisher-Yates-shuffled example titles into the
/// middle of the system prompt on EVERY build, so no two consecutive calls shared more than a few
/// hundred tokens. These tests hold the fix in place — a byte-identical stable prefix, rebuilt only
/// when one of its inputs really changed, with everything per-call pushed into a small bounded tail.
///
/// If any of this regresses the app still works; it just quietly costs several times more per user,
/// which is exactly the kind of bug nobody notices until the bill arrives.
/// </summary>
[Collection(PromptPrefixStateCollection.Name)]
public class PromptTwoZoneTests : IDisposable
{
    private static readonly Dictionary<string, string> PoolA = new()
    {
        ["Yes Brain Loop"] = "https://example.test/yes",
        ["Bambi Bae"] = "https://example.test/bae",
        ["Movies"] = "https://example.test/movies",   // a folder, never a suggestion
        ["Naughty Bambi"] = "https://example.test/naughty",
        ["Overload"] = "https://example.test/overload",
        ["Day 1"] = "https://example.test/day1",
        ["Bambi Slay"] = "https://example.test/slay",
        ["TikTok Loop"] = "https://example.test/tiktok"
    };

    private static readonly Dictionary<string, string> PoolB = new()
    {
        ["Locked Away"] = "https://example.test/locked",
        ["Keyholder"] = "https://example.test/key",
        ["Denial Drill"] = "https://example.test/denial"
    };

    public PromptTwoZoneTests()
    {
        BambiSprite.VideoPoolProvider = () => PoolA;
        BambiSprite.InvalidateStablePrompt();
    }

    public void Dispose()
    {
        BambiSprite.VideoPoolProvider = null;
        BambiSprite.InvalidateStablePrompt();
    }

    // ---------- zone 1: the stable prefix ----------

    [Fact]
    public void StablePrompt_IsByteIdenticalAcrossCalls()
    {
        var first = BambiSprite.GetStablePrompt();
        var second = BambiSprite.GetStablePrompt();
        var third = BambiSprite.GetStablePrompt();

        Assert.Equal(first, second, StringComparer.Ordinal);
        Assert.Equal(first, third, StringComparer.Ordinal);
    }

    [Fact]
    public void StablePrompt_IsBuiltOnceWhileNothingChanges()
    {
        int before = BambiSprite.PrefixBuildCount;

        BambiSprite.GetStablePrompt();
        BambiSprite.GetStablePrompt();
        BambiSprite.GetStablePrompt();

        Assert.Equal(before + 1, BambiSprite.PrefixBuildCount);
    }

    [Fact]
    public void StablePrompt_RebuildsAndChanges_WhenTheModsVideoPoolSwaps()
    {
        var bambi = BambiSprite.GetStablePrompt();
        int builds = BambiSprite.PrefixBuildCount;

        // A mod switch is exactly this: a different pool (and a different mod id, covered by the
        // fingerprint tests below). No explicit invalidation call — the fingerprint must notice.
        BambiSprite.VideoPoolProvider = () => PoolB;
        var locked = BambiSprite.GetStablePrompt();

        Assert.Equal(builds + 1, BambiSprite.PrefixBuildCount);
        Assert.NotEqual(bambi, locked);
        Assert.Contains("Locked Away", locked);
        Assert.DoesNotContain("Yes Brain Loop", locked);
    }

    [Fact]
    public void StableMediaTitles_AreAlphabetical_AndDropTheMoviesFolder()
    {
        var titles = BambiSprite.StableMediaTitles();

        Assert.DoesNotContain("Movies", titles);
        Assert.Equal(PoolA.Count - 1, titles.Count);
        Assert.Equal(titles.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray(), titles.ToArray());
    }

    [Fact]
    public void StableSample_IsDeterministicForASeed_AndDistinct()
    {
        var titles = new[] { "a", "b", "c", "d", "e", "f" };

        var first = BambiSprite.StableSample(titles, 3, seed: 4242);
        var again = BambiSprite.StableSample(titles, 3, seed: 4242);

        Assert.Equal(first, again);
        Assert.Equal(3, first.Count);
        Assert.Equal(3, first.Distinct().Count());
        Assert.All(first, t => Assert.Contains(t, titles));
    }

    [Fact]
    public void LegacyBuild_StillShufflesPerCall_SoTheKillSwitchRestoresTodaysBehaviour()
    {
        // The kill switch (UseCompanionBrain=false) must land on the ORIGINAL builder, randomness
        // and all. If this ever goes stable too, the flag stops being a real rollback.
        var legacy = new BambiSprite();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 30; i++) seen.Add(legacy.BuildSystemPrompt());

        Assert.True(seen.Count > 1,
            "legacy build produced 30 byte-identical prompts - the per-call sampling is gone");
    }

    [Fact]
    public void BothBuilds_KeepTheSafetySandwich()
    {
        // Layer 2 of the moderation spine. The two-zone work reorders what is INSIDE the sandwich;
        // it must never lift a prompt out of it.
        var stable = BambiSprite.GetStablePrompt();
        var legacy = new BambiSprite().BuildSystemPrompt();

        Assert.StartsWith(SafetyComposer.Preamble, stable, StringComparison.Ordinal);
        Assert.EndsWith(SafetyComposer.Floor, stable, StringComparison.Ordinal);
        Assert.StartsWith(SafetyComposer.Preamble, legacy, StringComparison.Ordinal);
        Assert.EndsWith(SafetyComposer.Floor, legacy, StringComparison.Ordinal);
    }

    // ---------- the fingerprint: what counts as "something changed" ----------

    private static BambiSprite.PrefixInputs Inputs(
        string modId = "bambi",
        string personality = "You are a bubbly bimbo.",
        string presetId = "bambisprite",
        bool slutMode = false,
        int quizPercent = -1,
        IReadOnlyList<string>? pool = null,
        IReadOnlyList<string>? globalLinks = null) =>
        new(
            ModId: modId,
            IsBambiMode: true,
            SlutMode: slutMode,
            PresetId: presetId,
            CommunityPromptId: string.Empty,
            UseCustomPrompt: false,
            PromptSections: new[] { personality, "", "", "kb", "reactions", "rules" },
            GlobalLinks: globalLinks ?? Array.Empty<string>(),
            BambiLinks: "",
            SissyLinks: "",
            QuizPercent: quizPercent,
            QuizArchetype: "",
            QuizProfile: "",
            VideoPoolTitles: pool ?? new[] { "Bambi Bae", "Overload" });

    [Fact]
    public void Fingerprint_IsStableForUnchangedInputs()
    {
        Assert.Equal(
            BambiSprite.ComputeFingerprint(Inputs()),
            BambiSprite.ComputeFingerprint(Inputs()));
    }

    public static IEnumerable<object[]> InvalidatingChanges()
    {
        yield return new object[] { "mod switch", Inputs(modId: "locked") };
        yield return new object[] { "personality edit", Inputs(personality: "You are a stern trainer.") };
        yield return new object[] { "preset switch", Inputs(presetId: "gentletrainer") };
        yield return new object[] { "slut mode", Inputs(slutMode: true) };
        yield return new object[] { "quiz result", Inputs(quizPercent: 72) };
        yield return new object[] { "video pool", Inputs(pool: new[] { "Bambi Bae" }) };
        yield return new object[] { "global links", Inputs(globalLinks: new[] { "- a new link" }) };
    }

    [Theory]
    [MemberData(nameof(InvalidatingChanges))]
    public void Fingerprint_ChangesFor(string what, object changed)
    {
        // Typed as object because the inputs record is internal and a public test signature
        // cannot name it.
        Assert.NotEqual(
            BambiSprite.ComputeFingerprint(Inputs()),
            BambiSprite.ComputeFingerprint((BambiSprite.PrefixInputs)changed));
        Assert.False(string.IsNullOrEmpty(what));
    }

    [Fact]
    public void Fingerprint_DoesNotCollideOnFieldBoundaries()
    {
        // "ab" + "" must not hash the same as "a" + "b" — the classic concatenation bug, which here
        // would mean an edit that silently keeps a stale prompt for the rest of the launch.
        var left = Inputs(modId: "ab", presetId: "");
        var right = Inputs(modId: "a", presetId: "b");

        Assert.NotEqual(BambiSprite.ComputeFingerprint(left), BambiSprite.ComputeFingerprint(right));
    }

    [Fact]
    public void Fingerprint_DoesNotCollideOnListBoundaries()
    {
        // One title in the pool and no global links must not hash the same as the mirror image.
        var left = Inputs(pool: new[] { "x" }, globalLinks: Array.Empty<string>());
        var right = Inputs(pool: Array.Empty<string>(), globalLinks: new[] { "x" });

        Assert.NotEqual(BambiSprite.ComputeFingerprint(left), BambiSprite.ComputeFingerprint(right));
    }

    // ---------- zone 2: the dynamic tail ----------

    private sealed class FixedPrefix
    {
        public const string Text = "STABLE PREFIX BYTES";
        public int Calls;
        public string Get() { Calls++; return Text; }
    }

    private static PromptAssembler Assembler(IMemoryStore? memory = null,
        RecentRecommendations? recs = null, Func<string>? prefix = null, DateTime? now = null) =>
        new(memory ?? new InertMemoryStore(), recs ?? new RecentRecommendations(), prefix,
            now.HasValue ? () => now.Value : () => new DateTime(2026, 8, 6, 15, 30, 0));

    [Fact]
    public void BuildRequest_KeepsAByteIdenticalPrefixAcrossCallsAndPurposes()
    {
        var recs = new RecentRecommendations();
        var prefix = new FixedPrefix();
        var assembler = Assembler(recs: recs, prefix: prefix.Get);

        var session = new ChatSession();
        session.Append(TurnKind.UserChat, "hi");
        var chat = assembler.BuildRequest(AiPurpose.Chat, session, "hi");

        // Everything that could plausibly move between two calls: a new turn, a new recommendation,
        // a different purpose.
        session.Append(TurnKind.AssistantChat, "hi yourself~");
        session.Append(TurnKind.AmbientEvent, "user opened YouTube");
        recs.Note("Bambi Bae");
        var reaction = assembler.BuildRequest(AiPurpose.Reaction, session, "user opened YouTube");

        Assert.StartsWith(FixedPrefix.Text, chat.SystemPrompt, StringComparison.Ordinal);
        Assert.StartsWith(FixedPrefix.Text, reaction.SystemPrompt, StringComparison.Ordinal);
        Assert.Equal(
            chat.SystemPrompt[..FixedPrefix.Text.Length],
            reaction.SystemPrompt[..FixedPrefix.Text.Length],
            StringComparer.Ordinal);
        Assert.NotEqual(chat.SystemPrompt, reaction.SystemPrompt);   // the tails DID differ
        Assert.Equal(2, prefix.Calls);
    }

    [Fact]
    public void BuildRequest_RewritesAssistantHistoryToThePool_OnTheWireOnly()
    {
        // Root cause 0807: session.json carried ~97 turns of invented titles, and the model
        // imitated its own few-shot instead of the in-prompt pool list. The wire copy of every
        // assistant turn must only ever demonstrate on-pool titles — while the stored session
        // (what the user actually saw in bubbles) stays byte-identical.
        var pool = new (string Title, string Url)[]
        {
            ("Bambi Bae", "https://example.test/bae"),
            ("Naughty Bambi", "https://example.test/naughty"),
        };
        var assembler = new PromptAssembler(new InertMemoryStore(), new RecentRecommendations(),
            () => "PREFIX", () => new DateTime(2026, 8, 7, 16, 0, 0), () => pool);

        var session = new ChatSession();
        session.Append(TurnKind.UserChat, "any video for me?");
        session.Append(TurnKind.AssistantChat,
            "Here's a video called \"Bimbo Love - Nonstop Compilation 1-3\" from Dvdhurytwuios. Enjoy!");
        session.Append(TurnKind.UserChat, "another one?");

        var request = assembler.BuildRequest(AiPurpose.Chat, session, "another one?");

        var assistant = request.Messages.Single(m => m.Role == ChatMessage.RoleAssistant);
        Assert.DoesNotContain("Bimbo Love", assistant.Content);
        Assert.DoesNotContain("from Dvdhurytwuios", assistant.Content);
        Assert.Contains(pool, e => assistant.Content.Contains("\"" + e.Title + "\""));

        // Wire-only: the user's words and the STORED session are untouched.
        Assert.Contains(request.Messages, m => m.Content.Contains("any video for me?"));
        Assert.Contains(session.Turns, t => t.Text.Contains("Bimbo Love - Nonstop Compilation 1-3"));
    }

    [Fact]
    public void BuildRequest_LeavesOnPoolAssistantHistoryByteIdentical()
    {
        var pool = new (string Title, string Url)[] { ("Bambi Bae", "https://example.test/bae") };
        var assembler = new PromptAssembler(new InertMemoryStore(), new RecentRecommendations(),
            () => "PREFIX", () => new DateTime(2026, 8, 7, 16, 0, 0), () => pool);

        var session = new ChatSession();
        session.Append(TurnKind.UserChat, "any video?");
        const string clean = "Try \"Bambi Bae\" tonight~";
        session.Append(TurnKind.AssistantChat, clean);
        session.Append(TurnKind.UserChat, "ok");

        var request = assembler.BuildRequest(AiPurpose.Chat, session, "ok");
        Assert.Contains(request.Messages, m => m.Role == ChatMessage.RoleAssistant && m.Content == clean);
    }

    [Fact]
    public void Tail_EndsWithThePurposeInstruction()
    {
        var assembler = Assembler();
        var session = new ChatSession();
        session.Append(TurnKind.UserChat, "hey");

        Assert.EndsWith(PromptAssembler.ChatInstruction,
            assembler.BuildRequest(AiPurpose.Chat, session, "hey").SystemPrompt, StringComparison.Ordinal);
        Assert.EndsWith(PromptAssembler.ReactionInstruction,
            assembler.BuildRequest(AiPurpose.Reaction, session, null).SystemPrompt, StringComparison.Ordinal);
        Assert.EndsWith(PromptAssembler.MemoryInstruction,
            assembler.BuildRequest(AiPurpose.Memory, session, null).SystemPrompt, StringComparison.Ordinal);
        Assert.EndsWith(PromptAssembler.SummaryInstruction,
            assembler.BuildRequest(AiPurpose.Summary, session, null).SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Tail_CarriesAntiRepeatOnlyForAmbientCalls()
    {
        var assembler = Assembler();
        var session = new ChatSession();
        session.Append(TurnKind.UserChat, "hey");

        Assert.DoesNotContain(PromptAssembler.AntiRepeatLine, assembler.BuildTail(AiPurpose.Chat, session.Turns));
        Assert.Contains(PromptAssembler.AntiRepeatLine, assembler.BuildTail(AiPurpose.Reaction, session.Turns));
    }

    [Fact]
    public void Tail_CarriesTheExclusionSetAndTheVaryRule_NotAShuffle()
    {
        var recs = new RecentRecommendations();
        recs.Note("Bambi Bae");
        recs.Note("Overload");

        var tail = Assembler(recs: recs).BuildTail(AiPurpose.Chat, Array.Empty<CompanionTurn>());

        Assert.Contains("Bambi Bae", tail);
        Assert.Contains("Overload", tail);
        Assert.Contains(PromptAssembler.VaryPicksRule, tail);
    }

    [Fact]
    public void Tail_ExplainsSpokenAloudLinesOnlyWhenTheWindowHasThem()
    {
        var assembler = Assembler();
        var quiet = new ChatSession();
        quiet.Append(TurnKind.UserChat, "hey");

        var voiced = new ChatSession();
        voiced.Append(TurnKind.UserChat, "hey");
        voiced.Append(TurnKind.BarkEcho, CompanionTurn.FormatBarkEcho("Bambi", "good girl~"), voiced: true);

        Assert.DoesNotContain(PromptAssembler.SpokenAloudRule, assembler.BuildTail(AiPurpose.Chat, quiet.Turns));
        Assert.Contains(PromptAssembler.SpokenAloudRule, assembler.BuildTail(AiPurpose.Chat, voiced.Turns));
    }

    [Fact]
    public void Tail_HonoursItsTokenCeiling_EvenWhenMemoryIgnoresItsOwn()
    {
        var greedy = new GreedyMemory(tokens: 5000);
        var recs = new RecentRecommendations();
        for (int i = 0; i < 6; i++) recs.Note($"A rather long video title number {i}");

        var tail = Assembler(memory: greedy, recs: recs).BuildTail(AiPurpose.Chat, Array.Empty<CompanionTurn>());

        Assert.True(ChatSession.ApproxTokens(tail) <= PromptAssembler.TailTokenBudget,
            $"tail was ~{ChatSession.ApproxTokens(tail)} tokens, ceiling is {PromptAssembler.TailTokenBudget}");
        // The instruction is the one line that must survive the trim.
        Assert.EndsWith(PromptAssembler.ChatInstruction, tail, StringComparison.Ordinal);
    }

    [Fact]
    public void MemoryBlock_IsClampedToItsOwnBudget()
    {
        var clamped = PromptAssembler.ClampToTokens(
            string.Join("\n", Enumerable.Repeat("- a remembered fact about them", 400)),
            PromptAssembler.MemoryTokenBudget);

        Assert.NotNull(clamped);
        Assert.True(ChatSession.ApproxTokens(clamped) <= PromptAssembler.MemoryTokenBudget);
        Assert.StartsWith("- a remembered fact", clamped, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyMemory_LeavesTheTailAtItsFloor()
    {
        // A stock Train 1 build has no facts: the tail must stay tiny, not grow an empty section.
        var tail = Assembler().BuildTail(AiPurpose.Chat, Array.Empty<CompanionTurn>());

        Assert.True(ChatSession.ApproxTokens(tail) < 100, $"floor tail was ~{ChatSession.ApproxTokens(tail)} tokens");
        Assert.Contains(PromptAssembler.TailHeader, tail);
    }

    [Theory]
    [InlineData(2, "the middle of the night")]
    [InlineData(7, "early morning")]
    [InlineData(10, "morning")]
    [InlineData(13, "midday")]
    [InlineData(16, "afternoon")]
    [InlineData(20, "evening")]
    [InlineData(23, "late night")]
    public void TimeOfDayLine_NamesTheBand(int hour, string expected)
    {
        var line = PromptAssembler.TimeOfDayLine(new DateTime(2026, 8, 6, hour, 5, 0));

        Assert.Contains(expected, line);
        Assert.Contains("Thursday", line);
        Assert.True(ChatSession.ApproxTokens(line) < 30);
    }

    [Fact]
    public void TimeOfDayLine_IsStableWithinTheHour()
    {
        // At HH:mm resolution this line changes every minute, and it sits inside message 0 AHEAD of
        // the whole history window — so two chat turns a minute apart share no cacheable prefix and
        // the ~1,600 tokens of history Train 1 added get re-billed in full on every single turn.
        var a = PromptAssembler.TimeOfDayLine(new DateTime(2026, 8, 6, 22, 1, 0));
        var b = PromptAssembler.TimeOfDayLine(new DateTime(2026, 8, 6, 22, 58, 0));
        var nextHour = PromptAssembler.TimeOfDayLine(new DateTime(2026, 8, 6, 23, 1, 0));

        Assert.Equal(a, b);
        Assert.NotEqual(a, nextHour);
    }

    // ---------- the proxy's 10,000-char per-message cap ----------

    [Fact]
    public void Compose_KeepsTheSystemMessageUnderTheProxyCap_ByTrimmingTheTail()
    {
        // proxy/server.js rejects any single message over 10,000 chars with input_too_large, and the
        // client packs the whole prefix AND the tail into one system message. A long knowledge base
        // plus Train 1's new tail is enough to cross that line — and every cloud call then returns an
        // unbadged canned phrase with no user-visible reason.
        var prefix = new string('p', 8600);
        var tail = PromptAssembler.TailHeader + "\n"
                   + string.Join("\n", Enumerable.Repeat("a tail line about right now", 40)) + "\n"
                   + PromptAssembler.ChatInstruction;

        var composed = PromptAssembler.Compose(prefix, tail, PromptAssembler.ChatInstruction);

        Assert.True(composed.Length <= PromptAssembler.SystemMessageCharCeiling,
            $"composed was {composed.Length} chars, ceiling is {PromptAssembler.SystemMessageCharCeiling}");
        Assert.StartsWith(prefix, composed, StringComparison.Ordinal);
        // The purpose instruction is never what we drop.
        Assert.EndsWith(PromptAssembler.ChatInstruction, composed, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_NeverTrimsThePrefix_BecauseTheSafetyFloorLivesAtItsEnd()
    {
        // Cutting the prefix to satisfy a length cap would trade a compliance control for a cost
        // control: SafetyComposer.Floor is the LAST thing in the stable prefix.
        var prefix = new string('p', 9500) + SafetyComposer.Floor;
        var tail = PromptAssembler.TailHeader + "\nsomething\n" + PromptAssembler.ChatInstruction;

        var composed = PromptAssembler.Compose(prefix, tail, PromptAssembler.ChatInstruction);

        Assert.StartsWith(prefix, composed, StringComparison.Ordinal);
        Assert.Contains(SafetyComposer.Floor, composed, StringComparison.Ordinal);
        Assert.EndsWith(PromptAssembler.ChatInstruction, composed, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_LeavesAnOrdinaryPromptExactlyAsItWas()
    {
        Assert.Equal("prefix\n\ntail", PromptAssembler.Compose("prefix", "tail", PromptAssembler.ChatInstruction));
        Assert.Equal("prefix", PromptAssembler.Compose("prefix", "", PromptAssembler.ChatInstruction));
    }

    /// <summary>A store that ignores the budget it is handed — the case the assembler must survive.</summary>
    private sealed class GreedyMemory : IMemoryStore
    {
        private readonly string _block;
        public GreedyMemory(int tokens) => _block = string.Join("\n", Enumerable.Repeat("- filler fact", tokens / 4));

        public string? GetInjectionBlock(int tokenBudget) => _block;
        public void UpdateProfileSignal(string key, object? value) { }
        public IReadOnlyDictionary<string, object?> Profile => new Dictionary<string, object?>();
        public IReadOnlyList<MemoryFact> GetFacts() => Array.Empty<MemoryFact>();
        public MemoryFact AddFact(string text, MemoryFactKind kind, double salience = 0.5,
            string source = MemoryFact.SourceChat) =>
            new("f-x", text, kind, salience, DateTime.UtcNow, null, 0, false, source);
        public bool UpdateFact(string id, string? text = null, double? salience = null, bool? pinned = null) => false;
        public bool ForgetFact(string id) => false;
        public void NoteFactUsed(string id) { }
        public void Wipe() { }
    }
}
