using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Train 1 integration seam — where <see cref="MemoryStore.GetInjectionBlock"/> meets
/// <see cref="PromptAssembler.BuildTail"/>.
///
/// <para>The two were built independently against one number, <c>MemoryTokenBudget = 500</c>, and
/// they disagree about it on purpose: the store renders boundary facts OUTSIDE the budget it was
/// handed (consent hygiene must not lose a race to a joke about the user's cat), so the block it
/// returns can legally come back over budget. The assembler used to clamp that block flat at 500,
/// which silently undid the exemption — the truncation landed on whichever lines came last, and a
/// user with a long memory file could have "never mention his ex" quietly trimmed off every single
/// request. Nothing would look broken; she would just start crossing lines she was told about.</para>
///
/// <para>So these tests pin both halves and, critically, the LINK between them: the store's
/// <see cref="MemoryStore.IsUnbudgetedInjectionLine"/> predicate must keep matching the bytes the
/// store actually emits. That is the part that rots — the day someone rewords the boundary prefix,
/// the predicate stops matching and the clamp fails OPEN, back to trimmable boundaries.</para>
///
/// <para>These tests never build a real system prompt (every assembler here is handed a fixed
/// prefix), so unlike <see cref="PromptTwoZoneTests"/> they need no collection serialization.</para>
/// </summary>
public class MemoryTailBudgetTests : IDisposable
{
    private readonly string _dir;
    private readonly List<MemoryStore> _stores = new();
    private int _next;

    public MemoryTailBudgetTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ccp-tail-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        foreach (var s in _stores)
        {
            try { s.Dispose(); } catch { }
        }
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private MemoryStore NewStore()
    {
        var store = new MemoryStore(Path.Combine(_dir, $"memory{_next++}.json"), sessionSeed: 1234);
        _stores.Add(store);
        return store;
    }

    private static PromptAssembler Assembler(IMemoryStore memory) =>
        new(memory, new RecentRecommendations(), () => "PREFIX",
            () => new DateTime(2026, 8, 6, 15, 30, 0));

    private static string Tail(IMemoryStore memory, AiPurpose purpose = AiPurpose.Chat)
        => Assembler(memory).BuildTail(purpose, Array.Empty<CompanionTurn>());

    private static string BoundaryLine(string text) => MemoryStore.BoundaryLinePrefix + text;

    // ---------- the link between the two halves ----------

    [Fact]
    public void IsUnbudgetedInjectionLine_RecognisesEveryBoundaryTheStoreEmits()
    {
        // The drift guard. If the store's wording and the predicate ever part company this fails,
        // instead of boundaries silently becoming trimmable again.
        var store = NewStore();
        store.AddFact("never mention his ex", MemoryFactKind.Boundary, 0.4);
        store.AddFact("no age play talk, ever", MemoryFactKind.Boundary, 0.9);
        store.AddFact("likes the spiral", MemoryFactKind.Preference, 0.8);

        var lines = (store.GetInjectionBlock(PromptAssembler.MemoryTokenBudget) ?? string.Empty)
            .Split('\n');

        var exempt = lines.Where(MemoryStore.IsUnbudgetedInjectionLine).ToArray();
        Assert.Equal(2, exempt.Length);
        Assert.All(exempt, l => Assert.StartsWith(MemoryStore.BoundaryLinePrefix, l, StringComparison.Ordinal));

        // ...and nothing else is exempt. An over-broad predicate is the other failure mode: it would
        // hand the whole block the overshoot allowance and defeat the ceiling.
        Assert.DoesNotContain(lines.Where(l => !MemoryStore.IsUnbudgetedInjectionLine(l)),
            l => l.StartsWith(MemoryStore.BoundaryLinePrefix, StringComparison.Ordinal));
    }

    [Fact]
    public void IsUnbudgetedInjectionLine_AlsoRecognisesTheOverflowNotice()
    {
        // The "(+N more boundaries on file)" line is the store telling the model its list was cut.
        // Losing THAT to the clamp is the worst of both worlds: trimmed boundaries and no warning.
        var store = NewStore();
        for (int i = 0; i < MemoryStore.MaxBoundaryLines + 3; i++)
            store.AddFact($"boundary {i}", MemoryFactKind.Boundary, 0.5);

        var lines = (store.GetInjectionBlock(PromptAssembler.MemoryTokenBudget) ?? string.Empty)
            .Split('\n');
        var overflow = Assert.Single(lines.Where(l => l.StartsWith("(+", StringComparison.Ordinal)));

        Assert.True(MemoryStore.IsUnbudgetedInjectionLine(overflow));
        Assert.Contains("3", overflow);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("- an ordinary fact")]
    [InlineData("What you know about them: level 41")]
    [InlineData("(+ not actually the overflow line)")]
    public void IsUnbudgetedInjectionLine_IsFalseForEverythingElse(string? line)
    {
        Assert.False(MemoryStore.IsUnbudgetedInjectionLine(line));
    }

    // ---------- the clamp ----------

    [Fact]
    public void ClampToTokens_ChargesBoundariesToTheirOwnBucket()
    {
        // One fat ordinary fact eats the main budget on its own. Without a second bucket the
        // boundary that follows it would be dropped for being "over budget".
        var block = string.Join("\n", new[]
        {
            "- " + new string('x', 400),
            BoundaryLine("never mention his ex"),
            "- " + new string('y', 400),
            BoundaryLine("no age play talk, ever")
        });

        var clamped = PromptAssembler.ClampToTokens(block, tokenBudget: 110, exemptTokenBudget: 300);

        Assert.NotNull(clamped);
        Assert.Contains(BoundaryLine("never mention his ex"), clamped!, StringComparison.Ordinal);
        Assert.Contains(BoundaryLine("no age play talk, ever"), clamped!, StringComparison.Ordinal);
        Assert.Equal(2, clamped!.Split('\n').Count(l => MemoryStore.IsUnbudgetedInjectionLine(l)));
    }

    [Fact]
    public void ClampToTokens_SkipsAnOversizeLineInsteadOfEndingTheScan()
    {
        // The old clamp `break`-ed on the first line that did not fit, so one long fact could hide
        // every shorter line behind it - including boundaries.
        var block = string.Join("\n", new[]
        {
            "- " + new string('x', 4000),
            "- short and useful"
        });

        var clamped = PromptAssembler.ClampToTokens(block, tokenBudget: 100, exemptTokenBudget: 0);

        Assert.Equal("- short and useful", clamped);
    }

    [Fact]
    public void ClampToTokens_StillBoundsAPathologicalBoundaryList()
    {
        // The exemption is an allowance, not an exemption from arithmetic: a file full of enormous
        // boundaries must not be able to inflate every request forever.
        var block = string.Join("\n",
            Enumerable.Range(0, 50).Select(i => BoundaryLine(new string((char)('a' + i % 26), 200))));

        var clamped = PromptAssembler.ClampToTokens(block, tokenBudget: 500, exemptTokenBudget: 300);

        Assert.NotNull(clamped);
        Assert.True(ChatSession.ApproxTokens(clamped!) <= 800,
            $"clamped block was ~{ChatSession.ApproxTokens(clamped!)} tokens, over the 500+300 ceiling");
    }

    [Fact]
    public void ClampToTokens_WithNoExemptionIsTheOldFlatCeiling()
    {
        var block = string.Join("\n", Enumerable.Repeat(BoundaryLine("no teasing about work"), 40));

        var clamped = PromptAssembler.ClampToTokens(block, tokenBudget: 100);

        Assert.NotNull(clamped);
        Assert.True(ChatSession.ApproxTokens(clamped!) <= 100);
    }

    // ---------- the tail ----------

    [Fact]
    public void Tail_KeepsEveryBoundaryWhenOrdinaryFactsBlowTheBudget()
    {
        var store = NewStore();
        store.UpdateProfileSignal(MemoryStore.KeyLevel, 41L);
        for (int i = 0; i < 40; i++)
            store.AddFact($"ordinary fact number {i} with some padding text on it", MemoryFactKind.Joke, 0.9);
        store.AddFact("never mention his ex", MemoryFactKind.Boundary, 0.1);
        store.AddFact("no age play talk, ever", MemoryFactKind.Boundary, 0.1);

        var tail = Tail(store);

        Assert.Contains(BoundaryLine("never mention his ex"), tail, StringComparison.Ordinal);
        Assert.Contains(BoundaryLine("no age play talk, ever"), tail, StringComparison.Ordinal);
    }

    [Fact]
    public void Tail_NeverDropsTheMemoryBlockToFitAReminderLine()
    {
        // The memory block is charged against the tail budget but is not itself droppable. A generic
        // "spend until full" loop would happily delete the user's boundaries to make room for
        // "Vary your picks".
        var store = NewStore();
        for (int i = 0; i < 60; i++)
            store.AddFact($"fact {i} padded out so the block is comfortably over the tail budget", MemoryFactKind.Joke, 0.9);
        store.AddFact("never mention his ex", MemoryFactKind.Boundary, 0.5);

        var tail = Tail(store);

        Assert.Contains(BoundaryLine("never mention his ex"), tail, StringComparison.Ordinal);
        // The instruction is the other undroppable: a call with no instruction is a call with no purpose.
        Assert.EndsWith(PromptAssembler.ChatInstruction, tail, StringComparison.Ordinal);
        Assert.StartsWith(PromptAssembler.TailHeader, tail, StringComparison.Ordinal);
    }

    [Fact]
    public void Tail_StaysWithinTheDocumentedWorstCase()
    {
        var store = NewStore();
        for (int i = 0; i < 60; i++)
            store.AddFact($"fact {i} padded out so the block is comfortably over the tail budget", MemoryFactKind.Joke, 0.9);
        for (int i = 0; i < MemoryStore.MaxBoundaryLines + 5; i++)
            store.AddFact($"boundary {i} with a realistic amount of wording on it", MemoryFactKind.Boundary, 0.5);

        var tail = Tail(store, AiPurpose.Reaction);

        int worstCase = PromptAssembler.TailTokenBudget + PromptAssembler.BoundaryOvershootTokens;
        Assert.True(ChatSession.ApproxTokens(tail) <= worstCase,
            $"tail was ~{ChatSession.ApproxTokens(tail)} tokens, over the documented {worstCase} ceiling");
    }

    [Fact]
    public void Tail_OfAnEmptyStoreIsJustTheHeaderAndTheStandingRules()
    {
        var tail = Tail(new InertMemoryStore());

        Assert.StartsWith(PromptAssembler.TailHeader, tail, StringComparison.Ordinal);
        Assert.EndsWith(PromptAssembler.ChatInstruction, tail, StringComparison.Ordinal);
        Assert.DoesNotContain(MemoryStore.BoundaryLinePrefix, tail, StringComparison.Ordinal);
    }

    // ---------- wiring ----------

    [Fact]
    public void Constructor_RejectsANullMemoryStore()
    {
        // Deliberately not a silent `?? new MemoryStore()`: the production store constructor loads
        // memory.json and starts a signal writer, so a fallback would give the process a SECOND
        // store racing the brain's on the same file. A null here is a wiring bug, and says so.
        Assert.Throws<ArgumentNullException>(() =>
            new PromptAssembler(null!, new RecentRecommendations()));
    }
}
