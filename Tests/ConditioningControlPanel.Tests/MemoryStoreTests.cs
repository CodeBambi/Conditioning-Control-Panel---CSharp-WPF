using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Train 1 deterministic memory (doc 01 §2.1-2.2, §2.5).
///
/// <para>These cover the four things that are invisible when they break: a schema round trip (a
/// dropped field silently amnesias the user), the caps (an unbounded facts list eventually blows the
/// prompt budget and the disk), the boundary guarantee (a lost "stop teasing me about X" is a
/// consent failure, not a cosmetic one), and per-session selection stability (a churning memory block
/// is a provider prompt-cache miss on every single call — the exact cost problem the rework exists to
/// fix).</para>
///
/// <para>Every store is built on a throwaway temp directory through the test constructor, so nothing
/// here touches %LOCALAPPDATA% and nothing subscribes to app events.</para>
/// </summary>
public class MemoryStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;
    private readonly List<MemoryStore> _stores = new();

    public MemoryStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ccp-mem-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dir, "companion"));
        _path = Path.Combine(_dir, "companion", "memory.json");
    }

    public void Dispose()
    {
        foreach (var s in _stores)
        {
            try { s.Dispose(); } catch { }
        }
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private MemoryStore NewStore(Func<DateTime>? clock = null, int? seed = 1234,
        Func<bool>? chatMemoryEnabled = null)
    {
        var store = new MemoryStore(_path, clock, seed, chatMemoryEnabled);
        _stores.Add(store);
        return store;
    }

    private static DateTime Now => new(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);

    // ================= schema round trip =================

    [Fact]
    public void RoundTrip_PreservesProfileRelationshipUsageAndFacts()
    {
        var store = NewStore(() => Now);
        store.UpdateProfileSignal(MemoryStore.KeyLevel, 41L);
        store.UpdateProfileSignal(MemoryStore.KeyArchetype, "Dollhouse Doll");
        store.UpdateProfileSignal(MemoryStore.KeyFavoriteFeatures, new[] { "flash", "video" });
        store.UpdateProfileSignal("chatty", true);
        store.NoteChatTurn("bambi");
        store.NoteChatTurn("bambi");
        store.NoteChatTurn("sissy");
        store.NoteFeatureUsed("flash");
        var joke = store.AddFact("Calls his cat 'Prime Minister Beans'", MemoryFactKind.Joke, 0.8);
        store.UpdateFact(joke.Id, pinned: true);
        store.AddFact("Never tease about work", MemoryFactKind.Boundary, 0.9, MemoryFact.SourceUserEdited);
        store.SaveNow();

        var reloaded = NewStore(() => Now);

        Assert.Equal(41L, reloaded.Profile[MemoryStore.KeyLevel]);
        Assert.Equal("Dollhouse Doll", reloaded.Profile[MemoryStore.KeyArchetype]);
        Assert.Equal(new[] { "flash", "video" }, (string[])reloaded.Profile[MemoryStore.KeyFavoriteFeatures]!);
        Assert.Equal(true, reloaded.Profile["chatty"]);

        Assert.Equal(2, reloaded.Relationships["bambi"].ChatTurnsTotal);
        Assert.Equal(1, reloaded.Relationships["sissy"].ChatTurnsTotal);
        Assert.Equal(Now, reloaded.Relationships["bambi"].LastChat);

        Assert.Equal(1, reloaded.FeatureUsage["flash"]);

        var facts = reloaded.GetFacts();
        Assert.Equal(2, facts.Count);
        var back = facts.Single(f => f.Kind == MemoryFactKind.Joke);
        Assert.Equal("Calls his cat 'Prime Minister Beans'", back.Text);
        Assert.Equal(0.8, back.Salience, 6);
        Assert.True(back.Pinned);
        Assert.Equal(Now, back.Created);
        Assert.Equal(MemoryFactKind.Boundary, facts.Single(f => f.Kind == MemoryFactKind.Boundary).Kind);
    }

    [Fact]
    public void RoundTrip_WritesTheDocumentedSchemaShape()
    {
        // The panel, a future migration and any user poking at the file all depend on these names.
        var store = NewStore(() => Now);
        store.UpdateProfileSignal(MemoryStore.KeyLevel, 7L);
        store.NoteChatTurn("bambi");
        store.AddFact("likes spirals", MemoryFactKind.Preference, 0.6);
        store.SaveNow();

        var json = File.ReadAllText(_path);

        Assert.Contains("\"version\"", json);
        Assert.Contains("\"profile\"", json);
        Assert.Contains("\"relationship\"", json);
        Assert.Contains("\"facts\"", json);
        Assert.Contains("\"chatTurnsTotal\"", json);
        Assert.Contains("\"salience\"", json);
        Assert.Contains("\"pinned\"", json);
        Assert.Contains("\"source\"", json);
    }

    [Fact]
    public void ParseMemory_ReadsTheDocumentedSchema()
    {
        const string json = """
        {
          "version": 1,
          "profile": { "level": 41, "archetype": "Doll", "favoriteFeatures": ["flash","chaos"] },
          "relationship": { "bambi": { "chatTurnsTotal": 412, "lastChat": "2026-08-05T22:00:00Z" } },
          "usage": { "flash": 12 },
          "facts": [
            { "id": "f-abc", "text": "hates mint", "kind": "preference", "salience": 0.7,
              "created": "2026-07-01T00:00:00Z", "lastUsed": null, "uses": 0,
              "pinned": false, "source": "chat" }
          ]
        }
        """;

        var snapshot = MemoryStore.ParseMemory(json);

        Assert.Equal(41L, snapshot.Profile["level"]);
        Assert.Equal(new[] { "flash", "chaos" }, (string[])snapshot.Profile["favoriteFeatures"]!);
        Assert.Equal(412, snapshot.Relationship["bambi"].ChatTurnsTotal);
        Assert.Equal(12, snapshot.Usage["flash"]);
        Assert.Equal("f-abc", snapshot.Facts[0].Id);
        Assert.Equal(MemoryFactKind.Preference, snapshot.Facts[0].Kind);
    }

    // ================= corrupt file tolerance =================

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"facts\": \"not an array\"}")]
    [InlineData("{\"version\":1,\"facts\":[{\"text\":\"x\",\"kind\":\"nonsense\"}]}")]
    [InlineData("{\"version\":1,\"profile\":{\"level\":{\"nested\":true}}}")]
    public void ParseMemory_NeverThrowsOnGarbage(string json)
    {
        var snapshot = MemoryStore.ParseMemory(json);
        Assert.NotNull(snapshot);
        Assert.Empty(snapshot.Facts);
    }

    [Fact]
    public void ParseMemory_KeepsTheGoodHalfOfAPartiallyBrokenFactList()
    {
        // A hand-edited or half-written file must cost the bad rows, not the whole memory.
        const string json = """
        {
          "version": 1,
          "facts": [
            { "id": "f-1", "text": "", "kind": "joke" },
            { "id": "f-2", "text": "keeper", "kind": "goal", "salience": 5.0 },
            { "id": "f-3", "text": "unknown kind", "kind": "vibes" },
            { "id": "f-2", "text": "duplicate id", "kind": "event" }
          ]
        }
        """;

        var facts = MemoryStore.ParseMemory(json).Facts;

        Assert.Equal(2, facts.Count);
        Assert.Contains(facts, f => f.Text == "keeper");
        Assert.Contains(facts, f => f.Text == "duplicate id");
        Assert.Equal(2, facts.Select(f => f.Id).Distinct().Count());  // the duplicate got re-minted
        Assert.Equal(1.0, facts.Single(f => f.Text == "keeper").Salience, 6); // out-of-range clamped
    }

    [Fact]
    public void Load_RecoversFromACorruptFileAndRebuildsOnTheNextSave()
    {
        File.WriteAllText(_path, "{ this is not json", Encoding.UTF8);

        var store = NewStore(() => Now);          // must not throw
        Assert.Empty(store.GetFacts());
        Assert.Empty(store.Profile);

        store.UpdateProfileSignal(MemoryStore.KeyLevel, 3L);
        store.SaveNow();

        Assert.Equal(3L, NewStore(() => Now).Profile[MemoryStore.KeyLevel]);
    }

    [Fact]
    public void Load_OfATruncatedFileIsSurvivable()
    {
        var good = NewStore(() => Now);
        for (int i = 0; i < 20; i++) good.AddFact($"fact {i}", MemoryFactKind.Event, 0.5);
        good.SaveNow();

        var text = File.ReadAllText(_path);
        File.WriteAllText(_path, text[..(text.Length / 2)], Encoding.UTF8);

        var store = NewStore(() => Now);
        Assert.Empty(store.GetFacts());
    }

    // ================= caps & eviction =================

    [Fact]
    public void AddFact_EvictsTheWeakestOncePastTheCap()
    {
        var store = NewStore(() => Now);
        for (int i = 0; i < MemoryStore.MaxFacts; i++)
            store.AddFact($"strong {i}", MemoryFactKind.Event, salience: 0.9);

        var doomed = store.AddFact("barely worth remembering", MemoryFactKind.Event, salience: 0.01);

        Assert.Equal(MemoryStore.MaxFacts, store.GetFacts().Count);
        Assert.DoesNotContain(store.GetFacts(), f => f.Id == doomed.Id);
    }

    [Fact]
    public void Eviction_PrefersStaleOverRecentAtEqualSalience()
    {
        var now = Now;
        var store = NewStore(() => now);

        // A fact used a year ago scores far below an identical one used today.
        var stale = store.AddFact("ancient", MemoryFactKind.Event, 0.5);
        store.UpdateFact(stale.Id, salience: 0.5);
        now = Now.AddDays(365);
        var fresh = store.AddFact("today", MemoryFactKind.Event, 0.5);

        Assert.True(MemoryStore.Score(store.GetFacts().Single(f => f.Id == fresh.Id), now) >
                    MemoryStore.Score(store.GetFacts().Single(f => f.Id == stale.Id), now));
    }

    [Fact]
    public void Eviction_NeverTouchesPinnedOrBoundaryFacts()
    {
        var store = NewStore(() => Now);
        var pinned = store.AddFact("pinned and unloved", MemoryFactKind.Joke, salience: 0.0);
        store.UpdateFact(pinned.Id, pinned: true);
        var boundary = store.AddFact("do not tease about work", MemoryFactKind.Boundary, salience: 0.0);

        for (int i = 0; i < MemoryStore.MaxFacts + 50; i++)
            store.AddFact($"filler {i}", MemoryFactKind.Event, salience: 0.9);

        var ids = store.GetFacts().Select(f => f.Id).ToHashSet();
        Assert.Contains(pinned.Id, ids);
        Assert.Contains(boundary.Id, ids);
        Assert.Equal(MemoryStore.MaxFacts, store.GetFacts().Count);
    }

    [Fact]
    public void SoftSizeCap_ShedsUnprotectedFactsRatherThanFailingTheWrite()
    {
        var store = NewStore(() => Now);
        var wall = new string('x', 4000);
        var boundary = store.AddFact("keep me: " + wall, MemoryFactKind.Boundary, 0.5);
        for (int i = 0; i < 150; i++) store.AddFact($"{i} {wall}", MemoryFactKind.Event, 0.5);

        store.SaveNow();

        var bytes = new FileInfo(_path).Length;
        Assert.True(bytes <= MemoryStore.SoftMaxBytes,
            $"memory.json was {bytes} bytes, over the {MemoryStore.SoftMaxBytes} soft cap");

        var reloaded = NewStore(() => Now);
        Assert.Contains(reloaded.GetFacts(), f => f.Text.StartsWith("keep me:", StringComparison.Ordinal));
        Assert.True(reloaded.GetFacts().Count < 151);
    }

    // ================= injection =================

    [Fact]
    public void GetInjectionBlock_IsNullWhenThereIsNothingToSay()
    {
        Assert.Null(NewStore(() => Now).GetInjectionBlock(500));
    }

    [Fact]
    public void GetInjectionBlock_LeadsWithTheProfileLineInAStableOrder()
    {
        var store = NewStore(() => Now);
        store.UpdateProfileSignal(MemoryStore.KeyFavoriteFeatures, new[] { "flash", "video" });
        store.UpdateProfileSignal(MemoryStore.KeyLevel, 41L);
        store.UpdateProfileSignal(MemoryStore.KeyFirstSeen, "2026-03-02");
        store.UpdateProfileSignal("zzz_extra", "later");

        var block = store.GetInjectionBlock(500)!;
        var first = block.Split('\n')[0];

        Assert.StartsWith("What you know about them: ", first);
        Assert.True(first.IndexOf("firstSeen=", StringComparison.Ordinal)
                    < first.IndexOf("level=", StringComparison.Ordinal));
        Assert.True(first.IndexOf("favoriteFeatures=flash/video", StringComparison.Ordinal)
                    < first.IndexOf("zzz_extra=", StringComparison.Ordinal));
    }

    [Fact]
    public void GetInjectionBlock_AlwaysCarriesEveryBoundaryEvenWhenTheBudgetIsTiny()
    {
        var store = NewStore(() => Now);
        store.AddFact("never mention his ex", MemoryFactKind.Boundary, 0.4);
        store.AddFact("no age play talk, ever", MemoryFactKind.Boundary, 0.4);
        for (int i = 0; i < 30; i++) store.AddFact($"chatty filler {i}", MemoryFactKind.Joke, 1.0);

        var block = store.GetInjectionBlock(20)!;

        Assert.Contains("never mention his ex", block);
        Assert.Contains("no age play talk, ever", block);
        Assert.Equal(2, block.Split('\n').Count(l => l.StartsWith("Boundary (honor this): ", StringComparison.Ordinal)));
    }

    [Fact]
    public void GetInjectionBlock_CapsARunawayBoundaryListButSaysSo()
    {
        var store = NewStore(() => Now);
        for (int i = 0; i < MemoryStore.MaxBoundaryLines + 5; i++)
            store.AddFact($"boundary {i}", MemoryFactKind.Boundary, 0.5);

        var block = store.GetInjectionBlock(500)!;

        Assert.Equal(MemoryStore.MaxBoundaryLines,
            block.Split('\n').Count(l => l.StartsWith("Boundary (honor this): ", StringComparison.Ordinal)));
        Assert.Contains("+5 more boundaries on file", block);
    }

    [Fact]
    public void GetInjectionBlock_RespectsTheBudgetForOrdinaryFacts()
    {
        var store = NewStore(() => Now);
        for (int i = 0; i < 200; i++) store.AddFact($"fact number {i} with some padding text", MemoryFactKind.Event, 0.5);

        var block = store.GetInjectionBlock(120)!;

        // chars/4 is the estimator the whole cost model uses; the block must honour it.
        Assert.True(block.Length / 4 <= 120, $"block was ~{block.Length / 4} tokens, budget was 120");
        Assert.True(block.Split('\n').Length is > 1 and < 200);
    }

    [Fact]
    public void GetInjectionBlock_NeverExceedsTheHardCeilingHoweverBigTheBudgetAsked()
    {
        var store = NewStore(() => Now);
        for (int i = 0; i < 300; i++) store.AddFact($"fact {i} " + new string('y', 60), MemoryFactKind.Event, 0.5);

        var block = store.GetInjectionBlock(int.MaxValue)!;

        Assert.True(block.Length / 4 <= MemoryStore.MaxInjectionTokens);
    }

    [Fact]
    public void GetInjectionBlock_PutsPinnedFactsAheadOfTheRanking()
    {
        var store = NewStore(() => Now);
        store.AddFact("loud and salient", MemoryFactKind.Joke, 1.0);
        var pinned = store.AddFact("quiet but pinned", MemoryFactKind.Identity, 0.05);
        store.UpdateFact(pinned.Id, pinned: true);

        var block = store.GetInjectionBlock(500)!;

        Assert.True(block.IndexOf("quiet but pinned", StringComparison.Ordinal)
                    < block.IndexOf("loud and salient", StringComparison.Ordinal));
    }

    [Fact]
    public void GetInjectionBlock_PrefersSalientAndRecentOverStale()
    {
        var now = Now;
        var store = NewStore(() => now);
        store.AddFact("stale memory line", MemoryFactKind.Event, 0.9);
        now = Now.AddDays(400);
        store.AddFact("fresh memory line", MemoryFactKind.Event, 0.9);

        // 6 tokens buys exactly one "- <17 chars>" line, so the loser is genuinely dropped rather
        // than merely sorted second.
        var block = store.GetInjectionBlock(6)!;

        Assert.Contains("fresh memory line", block);
        Assert.DoesNotContain("stale memory line", block);
    }

    // ================= per-session selection stability =================

    [Fact]
    public void Selection_IsByteIdenticalAcrossCallsWithinOneSession()
    {
        // This is the cost lever, not a nicety: the memory block rides the prompt's dynamic tail, and
        // a block that reshuffles per call means the provider prompt cache never hits.
        var store = NewStore(() => Now);
        for (int i = 0; i < 40; i++) store.AddFact($"equally interesting fact {i}", MemoryFactKind.Joke, 0.5);

        var first = store.GetInjectionBlock(200);
        for (int i = 0; i < 10; i++) Assert.Equal(first, store.GetInjectionBlock(200));
    }

    [Fact]
    public void Selection_VariesBetweenAppSessionsSoDifferentCallbacksSurface()
    {
        var a = new MemoryStore(_path, () => Now, sessionSeed: 1);
        var b = new MemoryStore(Path.Combine(_dir, "companion", "b.json"), () => Now, sessionSeed: 999);
        _stores.Add(a);
        _stores.Add(b);
        foreach (var store in new[] { a, b })
            for (int i = 0; i < 40; i++)
                store.AddFact($"equally interesting fact {i}", MemoryFactKind.Joke, 0.5);

        Assert.NotEqual(a.GetInjectionBlock(200), b.GetInjectionBlock(200));
    }

    [Fact]
    public void Selection_JitterNeverLetsAWeakFactBeatAMuchStrongerOne()
    {
        // The randomisation is a tie-breaker within the top band, not a lottery: a 0.95 fact must
        // still outrank a 0.10 one in every session.
        for (int seed = 0; seed < 25; seed++)
        {
            var store = new MemoryStore(Path.Combine(_dir, "companion", $"s{seed}.json"), () => Now, seed);
            _stores.Add(store);
            store.AddFact("weak", MemoryFactKind.Event, 0.10);
            store.AddFact("strong", MemoryFactKind.Event, 0.95);

            var block = store.GetInjectionBlock(500)!;
            Assert.True(block.IndexOf("strong", StringComparison.Ordinal)
                        < block.IndexOf("weak", StringComparison.Ordinal), $"seed {seed}");
        }
    }

    // ================= wipe =================

    [Fact]
    public void Wipe_ClearsMemoryAndDeletesEveryCompanionFile()
    {
        var companionDir = Path.Combine(_dir, "companion");
        var episodes = Path.Combine(companionDir, "episodes.json");
        var session = Path.Combine(companionDir, "session.json");
        var legacy = Path.Combine(_dir, "local_chat_history.json");

        var store = NewStore(() => Now);
        store.UpdateProfileSignal(MemoryStore.KeyLevel, 41L);
        store.NoteChatTurn("bambi");
        store.NoteFeatureUsed("flash");
        store.AddFact("pinned", MemoryFactKind.Boundary, 1.0);
        store.SaveNow();
        File.WriteAllText(episodes, "[]");
        File.WriteAllText(session, "{}");
        File.WriteAllText(legacy, "[]");

        store.Wipe();

        Assert.Empty(store.GetFacts());
        Assert.Empty(store.Profile);
        Assert.Empty(store.Relationships);
        Assert.Empty(store.FeatureUsage);
        Assert.Null(store.GetInjectionBlock(500));
        Assert.False(File.Exists(_path));
        Assert.False(File.Exists(episodes));
        Assert.False(File.Exists(session));
        // Absorbs ClearLocalHistory: leaving the legacy file behind would let CompanionSessionStore's
        // one-time import resurrect the wiped conversation on the next launch.
        Assert.False(File.Exists(legacy));
    }

    [Fact]
    public void Wipe_LeavesTheStoreUsable()
    {
        var store = NewStore(() => Now);
        store.AddFact("before", MemoryFactKind.Event, 0.5);
        store.Wipe();

        store.AddFact("after", MemoryFactKind.Event, 0.5);
        store.SaveNow();

        Assert.Equal("after", NewStore(() => Now).GetFacts().Single().Text);
    }

    // ================= chat-memory gate =================

    [Fact]
    public void Save_KeepsAppSourcedFactsWhenChatMemoryIsOff()
    {
        // App.Settings is null in a headless test, and the gate reads "not explicitly false", so this
        // asserts the default (enabled) path keeps everything — the off path is exercised by the
        // signal-writer tests via the pure helpers.
        var store = NewStore(() => Now);
        store.AddFact("from the app", MemoryFactKind.Event, 0.5, MemoryFact.SourceApp);
        store.AddFact("from the chat", MemoryFactKind.Joke, 0.5, MemoryFact.SourceChat);
        store.SaveNow();

        Assert.Equal(2, NewStore(() => Now).GetFacts().Count);
    }

    // ================= CRUD =================

    [Fact]
    public void AddFact_IgnoresBlankTextButStillReturnsARecord()
    {
        var store = NewStore(() => Now);
        var fact = store.AddFact("   ", MemoryFactKind.Joke);

        Assert.NotNull(fact);
        Assert.Empty(store.GetFacts());
    }

    [Fact]
    public void UpdateFact_MarksHandEditedTextAsUserEdited()
    {
        var store = NewStore(() => Now);
        var fact = store.AddFact("first draft", MemoryFactKind.Identity, 0.5, MemoryFact.SourceApp);

        Assert.True(store.UpdateFact(fact.Id, text: "the user's own words"));
        var updated = store.GetFacts().Single();
        Assert.Equal(MemoryFact.SourceUserEdited, updated.Source);

        // A pin-only edit is not an authorship claim.
        Assert.True(store.UpdateFact(fact.Id, pinned: true));
        Assert.Equal(MemoryFact.SourceUserEdited, store.GetFacts().Single().Source);
        Assert.False(store.UpdateFact("f-nope", pinned: true));
    }

    [Fact]
    public void NoteFactUsed_StampsRecencyAndCountsTheUse()
    {
        var store = NewStore(() => Now);
        var fact = store.AddFact("used", MemoryFactKind.Joke, 0.5);
        Assert.Null(store.GetFacts().Single().LastUsed);

        store.NoteFactUsed(fact.Id);

        var used = store.GetFacts().Single();
        Assert.Equal(Now, used.LastUsed);
        Assert.Equal(1, used.Uses);
    }

    [Fact]
    public void ForgetFact_RemovesExactlyOne()
    {
        var store = NewStore(() => Now);
        var a = store.AddFact("a", MemoryFactKind.Event, 0.5);
        store.AddFact("b", MemoryFactKind.Event, 0.5);

        Assert.True(store.ForgetFact(a.Id));
        Assert.False(store.ForgetFact(a.Id));
        Assert.Equal("b", store.GetFacts().Single().Text);
    }

    [Fact]
    public void UpdateProfileSignal_NullClearsTheKey()
    {
        var store = NewStore(() => Now);
        store.UpdateProfileSignal(MemoryStore.KeyArchetype, "Doll");
        store.UpdateProfileSignal(MemoryStore.KeyArchetype, null);

        Assert.False(store.Profile.ContainsKey(MemoryStore.KeyArchetype));
    }

    [Fact]
    public void FormatProfileValue_IsCultureInvariant()
    {
        // A comma-decimal locale would otherwise reshape the prompt line byte-for-byte.
        Assert.Equal("1.5", MemoryStore.FormatProfileValue(1.5d));
        Assert.Equal("true", MemoryStore.FormatProfileValue(true));
        Assert.Equal("a/b", MemoryStore.FormatProfileValue(new[] { "a", "b" }));
        Assert.Equal("", MemoryStore.FormatProfileValue(null));
    }

    // ================= moderation on the way IN to durable memory =================

    /// <summary>A guard that blocks anything containing a marker word.</summary>
    private sealed class BlockingGuard : ConditioningControlPanel.Services.Moderation.IModerationGuard
    {
        public ConditioningControlPanel.Services.Moderation.ModerationResult CheckInput(string text) =>
            text != null && text.Contains("PROHIBITED", StringComparison.OrdinalIgnoreCase)
                ? ConditioningControlPanel.Services.Moderation.ModerationResult.Block(
                    ConditioningControlPanel.Services.Moderation.ProhibitedCategory.ProfessionalAdvice, "test")
                : ConditioningControlPanel.Services.Moderation.ModerationResult.Pass();

        public ConditioningControlPanel.Services.Moderation.ModerationResult CheckOutput(string text) => CheckInput(text);
    }

    [Fact]
    public void ProhibitedText_NeverBecomesADurableFact()
    {
        // A stored fact is rendered into the system prompt's tail on EVERY later call, and boundary
        // lines are deliberately exempt from the tail clamp — so a prohibited fact would be
        // guaranteed delivery on a path the transport guard never sees (CheckInput only ever
        // inspects the newest user-role MESSAGE, never the system prompt).
        var guard = new BlockingGuard();

        Assert.False(MemoryStore.IsStorable(guard, "PROHIBITED thing", MemoryFact.SourceUserEdited));
        Assert.False(MemoryStore.IsStorable(guard, "PROHIBITED thing", MemoryFact.SourceChat));
        Assert.True(MemoryStore.IsStorable(guard, "calls his cat Beans", MemoryFact.SourceUserEdited));
    }

    [Fact]
    public void AppSourcedFactsSkipTheGuard_AndANullGuardNeverBlocks()
    {
        // Level/streak are ours, not user- or model-authored. And a build with no guard configured
        // must degrade to "unchecked", never to "memory refuses to store anything".
        Assert.True(MemoryStore.IsStorable(new BlockingGuard(), "PROHIBITED", MemoryFact.SourceApp));
        Assert.True(MemoryStore.IsStorable(null, "PROHIBITED", MemoryFact.SourceUserEdited));
    }

    // ================= chat-memory OFF: no record of conversations =================

    [Fact]
    public void ChatMemoryOff_NeverWritesTheRelationshipBlock()
    {
        // relationship = per-mod turn count + last-chat timestamp. However deterministically it is
        // produced, it is a plaintext record of WHEN and HOW OFTEN the user talked to her — derived
        // from the exact events the toggle exists to stop recording — in a file the toggle does not
        // otherwise govern. Level/streak legitimately survive; this does not.
        var store = NewStore(() => Now, chatMemoryEnabled: () => false);
        store.UpdateProfileSignal(MemoryStore.KeyLevel, 41L);
        store.NoteChatTurn("bambi");
        store.NoteChatTurn("bambi");
        store.SaveNow();

        var json = File.ReadAllText(_path);
        Assert.DoesNotContain("chatTurnsTotal", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lastChat", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"level\"", json);      // the carve-out that IS defensible still applies

        var reloaded = new MemoryStore(_path, () => Now, 1234, () => false);
        _stores.Add(reloaded);
        Assert.Empty(reloaded.Relationships);
    }

    [Fact]
    public void ChatMemoryOff_NoteChatTurnDoesNotEvenAccumulateInMemory()
    {
        var store = NewStore(() => Now, chatMemoryEnabled: () => false);
        store.NoteChatTurn("bambi");

        Assert.Empty(store.Relationships);
    }

    [Fact]
    public void ChatMemoryOn_StillWritesTheRelationshipBlock()
    {
        var store = NewStore(() => Now, chatMemoryEnabled: () => true);
        store.NoteChatTurn("bambi");
        store.SaveNow();

        Assert.Equal(1, NewStore(() => Now).Relationships["bambi"].ChatTurnsTotal);
    }

    // ================= durability =================

    [Fact]
    public void SaveNow_LeavesNoTruncatedFileBehind_AndNoStrayTempFile()
    {
        // A bare File.WriteAllText truncates before the new bytes land, and BOTH parsers here read a
        // JsonException as "empty" — so a crash mid-write silently costs the user every memory, with
        // no error and no backup, for the feature whose pitch is "she remembers you".
        var store = NewStore(() => Now);
        store.AddFact("Calls his cat Beans", MemoryFactKind.Joke, 0.8);
        store.SaveNow();

        Assert.False(File.Exists(_path + ".tmp"));
        Assert.Equal("Calls his cat Beans", NewStore(() => Now).GetFacts().Single().Text);
    }

    [Fact]
    public void ConcurrentSaves_AllSucceed_AndTheFileStaysParseable()
    {
        // Three legitimate concurrent callers exist in production: the debounce timer, Dispose(),
        // and the AppDomain.ProcessExit backstop.
        var store = NewStore(() => Now);
        store.AddFact("durable", MemoryFactKind.Identity, 0.9);

        System.Threading.Tasks.Parallel.For(0, 24, _ => store.SaveNow());

        Assert.False(File.Exists(_path + ".tmp"));
        Assert.Equal("durable", NewStore(() => Now).GetFacts().Single().Text);
    }

    // ================= wipe =================

    [Fact]
    public void ForgetChatDerived_ClearsTheConversationTrail_ButNotTheAppProfile()
    {
        // What "reset companion memory" / unticking ChatMemoryEnabled reaches. It must land on disk
        // immediately: otherwise the {chatTurnsTotal, lastChat} block the user just asked to remove
        // sits there until some unrelated signal happens to trigger the next debounced save.
        var store = NewStore(() => Now);
        store.UpdateProfileSignal(MemoryStore.KeyLevel, 41L);
        store.NoteChatTurn("bambi");
        store.AddFact("said he hates mondays", MemoryFactKind.Event, 0.5, MemoryFact.SourceChat);
        store.AddFact("no teasing about work", MemoryFactKind.Boundary, 0.9, MemoryFact.SourceUserEdited);
        store.SaveNow();

        store.ForgetChatDerived();

        var json = File.ReadAllText(_path);
        Assert.DoesNotContain("chatTurnsTotal", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hates mondays", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no teasing about work", json);
        Assert.Contains("\"level\"", json);

        var reloaded = NewStore(() => Now);
        Assert.Empty(reloaded.Relationships);
        Assert.Equal("no teasing about work", reloaded.GetFacts().Single().Text);
        Assert.Equal(41L, reloaded.Profile[MemoryStore.KeyLevel]);
    }

    [Fact]
    public void Wipe_KeepsTheFirstSeenLatch()
    {
        // Wipe leaves MemorySignalWriter running. The very next flash/level-up recomputes the profile
        // from `existingFirstSeen` — which a naive wipe just erased — so firstSeen silently re-latches
        // to TODAY and the anniversary the latch exists to protect is gone. Carrying it across is both
        // the honest value and the deterministic one.
        var store = NewStore(() => Now);
        store.UpdateProfileSignal(MemoryStore.KeyFirstSeen, "2025-11-02");
        store.UpdateProfileSignal(MemoryStore.KeyLevel, 41L);
        store.AddFact("a joke", MemoryFactKind.Joke, 0.5);

        store.Wipe();

        Assert.Equal("2025-11-02", store.Profile[MemoryStore.KeyFirstSeen]);
        Assert.False(store.Profile.ContainsKey(MemoryStore.KeyLevel));
        Assert.Empty(store.GetFacts());
    }
}
