using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Train 1 — <see cref="CompanionBrain"/> turn bookkeeping.
///
/// The load-bearing case here is the P2/H5 invariant: a moderation-refused turn is rolled back out
/// of the log and never reaches disk. That is a compliance property, not a nicety — the previous
/// generation of this bug persisted a prohibited assistant turn and replayed it into the prompt on
/// the next launch, laundering it past the guard.
/// </summary>
public class CompanionBrainTests
{
    // ---------- fakes ----------

    /// <summary>Scriptable transport. Records what was actually put on the wire.</summary>
    private sealed class FakeTransport : IAiService
    {
        public Func<IReadOnlyList<ChatMessage>, AiCallOptions, AiReplyResult> Respond { get; set; } =
            (_, _) => new AiReplyResult("ok~", IsAiGenerated: true, Refusal: null);

        public List<(IReadOnlyList<ChatMessage> Messages, AiCallOptions Options)> Sends { get; } = new();
        public Func<CancellationToken, Task<AiReplyResult>>? AsyncReply { get; set; }

        public bool IsAvailable => true;
        public int DailyRequestsRemaining => -1;

        public Task<AiReplyResult> SendAsync(IReadOnlyList<ChatMessage> messages, AiCallOptions options,
            CancellationToken cancellationToken = default)
        {
            Sends.Add((messages, options));
            return AsyncReply?.Invoke(cancellationToken) ?? Task.FromResult(Respond(messages, options));
        }

        // Legacy one-shot surface — unused by the brain, present so the fake is a real IAiService.
        public Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false) => Task.FromResult("");
        public Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false)
            => Task.FromResult(new AiReplyResult("", false, null));
        public Task<string?> GetAwarenessReactionAsync(string detectedName, string category,
            string serviceName = "", string pageTitle = "", TimeSpan? duration = null) => Task.FromResult<string?>(null);
        public Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration)
            => Task.FromResult<string?>(null);
        public Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null)
            => Task.FromResult<string?>(null);
        public Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null)
            => Task.FromResult<string?>(null);
        public Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null)
            => Task.FromResult<string?>(null);
        public void Dispose() { }
    }

    /// <summary>In-memory session store. <see cref="Saved"/> is "what reached disk".</summary>
    private sealed class FakeStore : ICompanionSessionStore
    {
        public CompanionSessionSnapshot Snapshot { get; set; } = CompanionSessionSnapshot.Empty;
        public List<IReadOnlyList<CompanionTurn>> Writes { get; } = new();
        public int WipeCount { get; private set; }

        public IReadOnlyList<CompanionTurn> Saved =>
            Writes.Count == 0 ? Array.Empty<CompanionTurn>() : Writes[^1];

        public CompanionSessionSnapshot Load() => Snapshot;
        public void Save(IReadOnlyList<CompanionTurn> dialogueTurns) { lock (Writes) Writes.Add(dialogueTurns.ToList()); }
        public void Wipe() { WipeCount++; }
    }

    /// <summary>Assembler that skips BambiSprite (and therefore the whole personality/mod stack).</summary>
    private sealed class StubAssembler : IPromptAssembler
    {
        public PromptRequest BuildRequest(AiPurpose purpose, ChatSession session, string? input)
        {
            var spec = purpose == AiPurpose.Chat ? ChatWindowSpec.Chat : ChatWindowSpec.Ambient;
            var messages = new List<ChatMessage> { ChatMessage.System("SYSTEM") };
            messages.AddRange(ChatSession.ToMessages(session.BuildWindow(spec)));
            return new PromptRequest("SYSTEM", messages);
        }
    }

    private static CompanionBrain Build(FakeTransport transport, FakeStore store, bool preview = false) =>
        new(transport, new StubAssembler(), new InertMemoryStore(), store, preview: () => preview);

    private static CompanionBrain Build(FakeTransport transport, FakeStore store,
        RecentRecommendations recommendations, params string[] mediaTitles) =>
        new(transport, new StubAssembler(), new InertMemoryStore(), store, recommendations,
            () => mediaTitles);

    /// <summary>Drains the fire-and-forget persistence Task.Run without a fixed sleep.</summary>
    private static async Task<IReadOnlyList<CompanionTurn>> WaitForWrite(FakeStore store, int expectedWrites)
    {
        for (int i = 0; i < 200; i++)
        {
            lock (store.Writes) { if (store.Writes.Count >= expectedWrites) return store.Saved; }
            await Task.Delay(10);
        }
        lock (store.Writes) return store.Saved;
    }

    [Fact]
    public async Task Preview_AccountSwitchSeparatesHistoryFactsAndVisibleMemoryWithoutDeletingEitherOwner()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ccp-account-memory-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        string account = "account-A";
        var legacyPath = Path.Combine(directory, "memory.json");
        File.WriteAllText(legacyPath, "legacy explicit facts preserved");
        try
        {
            using (var owned = MemoryStore.ForPreviewAccount(account, directory))
                owned.AddFact("A_ONLY_PREFERENCE_733 violet tea", MemoryFactKind.Preference, source: MemoryFact.SourceUserEdited);
            var aPath = MemoryStore.PreviewAccountDirectory(account, directory);
            new CompanionSessionStore(Path.Combine(aPath, "session.json"), Path.Combine(aPath, "no-import.json")).Save(new[]
            {
                CompanionTurn.Create(TurnKind.UserChat, "A_ONLY_HISTORY_733 from yesterday"),
                CompanionTurn.Create(TurnKind.AssistantChat, "I remember that detail")
            });
            var transport = new FakeTransport();
            using (var brain = new CompanionBrain(transport, preview: () => true,
                contextStamp: () => account, accountIdentity: () => account, accountDirectory: directory))
            {
                Assert.Single(brain.Memory.GetFacts());
                Assert.Equal(2, brain.Session.Turns.Count);
                account = "account-B";
                brain.EnsureCurrentAccount(); // Same seam used before opening the memory UI.
                Assert.Empty(brain.Memory.GetFacts());
                Assert.Empty(brain.Session.Turns);
                var result = await brain.ChatAsync("Hello from B");
                Assert.True(result.IsAiGenerated);
                Assert.DoesNotContain("A_ONLY_", string.Join("\n", transport.Sends.Last().Messages.Select(m => m.Content)));
                brain.Flush();
                account = "account-A";
                brain.EnsureCurrentAccount();
                Assert.Contains("A_ONLY_PREFERENCE_733", Assert.Single(brain.Memory.GetFacts()).Text);
                Assert.Contains(brain.Session.Turns, t => t.Text.Contains("A_ONLY_HISTORY_733"));
                Assert.DoesNotContain(brain.Session.Turns, t => t.Text.Contains("Hello from B"));
            }
            Assert.Equal("legacy explicit facts preserved", File.ReadAllText(legacyPath));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void StaleMemorySheetCannotWipeAnotherAccount()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ccp-stale-memory-sheet-" + Guid.NewGuid());
        string account = "A";
        try
        {
            using var brain = new CompanionBrain(new FakeTransport(), preview: () => true,
                accountIdentity: () => account, contextStamp: () => account, accountDirectory: directory);
            brain.Memory.AddFact("A fact", MemoryFactKind.Preference);
            var oldMemory = brain.Memory;
            var oldSheetAction = brain.CaptureForgetAction(oldMemory);
            account = "B";
            brain.EnsureCurrentAccount();
            brain.Memory.AddFact("B fact", MemoryFactKind.Preference);
            oldSheetAction();
            brain.CaptureForgetAction(oldMemory)();
            Assert.Equal("B fact", Assert.Single(brain.Memory.GetFacts()).Text);
            account = "A";
            brain.EnsureCurrentAccount();
            Assert.Equal("A fact", Assert.Single(brain.Memory.GetFacts()).Text);
            brain.CaptureForgetAction()();
            Assert.Empty(brain.Memory.GetFacts());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Preview_ForegroundDoesNotWaitForUncooperativeSummaryTransport()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ccp-summary-preemption-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var summaryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSummary = new TaskCompletionSource<AiReplyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var memory = new MemoryStore(Path.Combine(directory, "memory.json"));
            var transport = new FakeTransport();
            transport.AsyncReply = _ =>
            {
                if (transport.Sends.Last().Options.Purpose == AiPurpose.Summary)
                {
                    summaryEntered.TrySetResult();
                    return releaseSummary.Task;
                }
                return Task.FromResult(new AiReplyResult("A completed answer", true, null));
            };
            using var brain = new CompanionBrain(transport, new StubAssembler(), memory, new FakeStore(), preview: () => true);
            for (int i = 0; i < 8; i++) await brain.ChatAsync("I am growing a small garden");
            await summaryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var foreground = brain.ChatAsync("Can we keep talking?");
            Assert.True(foreground.IsCompleted);
            Assert.True((await foreground).IsAiGenerated);
            releaseSummary.TrySetResult(new AiReplyResult("{\"context\":[]}", true, null));
        }
        finally
        {
            releaseSummary.TrySetResult(AiReplyResult.Failed(AiFailureKind.Cancelled));
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(true, true, 1)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    public async Task Preview_RelationshipCountsOnlyAcceptedMemoryEnabledExchanges(bool accepted, bool memoryOn, int count)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ccp-accepted-count-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            using var memory = new MemoryStore(Path.Combine(directory, "memory.json"), chatMemoryEnabled: () => memoryOn);
            var transport = new FakeTransport { Respond = (_, _) => accepted
                ? new AiReplyResult("hello", true, null) : AiReplyResult.Failed(AiFailureKind.Offline) };
            using var brain = new CompanionBrain(transport, new StubAssembler(), memory, new FakeStore(), preview: () => true);
            await brain.ChatAsync("hello");
            Assert.Equal(count, memory.Relationships.Values.Sum(r => r.ChatTurnsTotal));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Preview_TransportException_RollsBackAndReturnsStatus()
    {
        var transport = new FakeTransport { Respond = (_, _) => throw new InvalidOperationException("fake") };
        using var brain = Build(transport, new FakeStore(), preview: true);
        var reply = await brain.ChatAsync("hello");
        Assert.Equal(AiFailureKind.Unavailable, reply.Failure);
        Assert.Empty(reply.Text);
        Assert.Empty(brain.Session.Turns);
    }

    [Fact]
    public async Task Preview_CancelledAfterTransport_DoesNotCommitReply()
    {
        using var cancel = new CancellationTokenSource();
        var transport = new FakeTransport { Respond = (_, _) =>
        {
            cancel.Cancel();
            return new AiReplyResult("too late", true, null);
        }};
        using var brain = Build(transport, new FakeStore(), preview: true);
        var reply = await brain.ChatAsync("hello", cancel.Token);
        Assert.Equal(AiFailureKind.Cancelled, reply.Failure);
        Assert.Empty(brain.Session.Turns);
    }

    [Fact]
    public async Task Preview_CannedFailure_IsNotCharacterSpeech()
    {
        var transport = new FakeTransport { Respond = (_, _) => new AiReplyResult("generic praise", false, null) };
        using var brain = Build(transport, new FakeStore(), preview: true);
        var reply = await brain.ChatAsync("hello");
        Assert.Equal(AiFailureKind.Unavailable, reply.Failure);
        Assert.Empty(reply.Text);
        Assert.Empty(brain.Session.Turns);
        Assert.Equal(240, Assert.Single(transport.Sends).Options.MaxTokens);
        Assert.True(Guid.TryParse(transport.Sends[0].Options.RequestId, out _));
    }

    [Fact]
    public async Task Preview_ConcurrentSend_IsRejectedWithoutSecondGeneration()
    {
        var transport = new FakeTransport();
        using var brain = Build(transport, new FakeStore(), preview: true);
        transport.Respond = (_, _) =>
        {
            var second = brain.ChatAsync("duplicate").GetAwaiter().GetResult();
            Assert.Equal(AiFailureKind.Busy, second.Failure);
            return new AiReplyResult("hello", true, null);
        };
        await brain.ChatAsync("first");
        Assert.Single(transport.Sends);
        Assert.Equal(2, brain.Session.Turns.Count);
    }

    [Fact]
    public async Task Preview_ForgetWhileReplyPending_DoesNotRestoreConversation()
    {
        var pending = new TaskCompletionSource<AiReplyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new FakeTransport { AsyncReply = _ => pending.Task };
        var store = new FakeStore();
        using var brain = Build(transport, store, preview: true);
        var turn = brain.ChatAsync("remember this");
        brain.ForgetThread();
        pending.SetResult(new AiReplyResult("an old-context answer", true, null));
        var reply = await turn;
        brain.Flush();
        Assert.Equal(AiFailureKind.Cancelled, reply.Failure);
        Assert.Empty(brain.Session.Turns);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task Preview_CharacterChangesWhileReplyPending_DiscardsOldVoice()
    {
        var pending = new TaskCompletionSource<AiReplyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new FakeTransport { AsyncReply = _ => pending.Task };
        var identity = "old";
        using var brain = new CompanionBrain(transport, new StubAssembler(), new InertMemoryStore(), new FakeStore(),
            preview: () => true, contextStamp: () => identity);
        var turn = brain.ChatAsync("hello");
        identity = "new";
        pending.SetResult(new AiReplyResult("old voice", true, null));
        Assert.Equal(AiFailureKind.Cancelled, (await turn).Failure);
        Assert.Empty(brain.Session.Turns);
    }

    [Theory]
    [InlineData("accepted", false)]
    [InlineData("forget", false)]
    [InlineData("changed", false)]
    [InlineData("cancelled", false)]
    [InlineData("refused", false)]
    [InlineData("accepted", true)]
    [InlineData("forget", true)]
    [InlineData("changed", true)]
    [InlineData("cancelled", true)]
    [InlineData("refused", true)]
    public async Task Preview_OnlyAcceptedModeratedTurnExecutesProposedEffects(string outcome, bool ambient)
    {
        var pending = new TaskCompletionSource<AiReplyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new FakeTransport { AsyncReply = _ => pending.Task };
        var context = "original";
        var executed = 0;
        using var cancellation = new CancellationTokenSource();
        using var brain = new CompanionBrain(transport, new StubAssembler(), new InertMemoryStore(), new FakeStore(),
            preview: () => true, contextStamp: () => context, executeCommands: commands => executed += commands.Count);
        var turn = ambient ? brain.ReactAsync("a game finished", cancellation.Token) : brain.ChatAsync("hello", cancellation.Token);
        Assert.Equal(0, executed);
        if (outcome == "forget") brain.ForgetThread();
        if (outcome == "changed") context = "new character or account";
        if (outcome == "cancelled") cancellation.Cancel();
        var refusal = outcome == "refused" ? new ModerationRefusalInfo(null, ModerationSource.Output) : null;
        pending.SetResult(new AiReplyResult("a complete reply", refusal == null, refusal,
            ProposedCommands: new[] { new ConditioningControlPanel.Models.AiCommandData() }));
        var result = await turn;
        Assert.Equal(outcome == "accepted" ? 1 : 0, executed);
        Assert.Equal(outcome == "accepted", result.IsAiGenerated);
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("forget")]
    [InlineData("changed")]
    [InlineData("cancelled")]
    public async Task Preview_QueuedEffects_RecheckContextWhenUiRunsThem(string outcome)
    {
        Action? queued = null;
        var executed = 0;
        var context = "original";
        using var cancellation = new CancellationTokenSource();
        var transport = new FakeTransport { Respond = (_, _) => new AiReplyResult("complete reply", true, null,
            ProposedCommands: new[] { new ConditioningControlPanel.Models.AiCommandData() }) };
        using var brain = new CompanionBrain(transport, new StubAssembler(), new InertMemoryStore(), new FakeStore(),
            preview: () => true, contextStamp: () => context, executeCommands: commands => executed += commands.Count,
            scheduleEffects: action => queued = action);
        Assert.True((await brain.ChatAsync("hello", cancellation.Token)).IsAiGenerated);
        Assert.Equal(0, executed);
        Assert.NotNull(queued);
        if (outcome == "forget") brain.ForgetThread();
        if (outcome == "changed") context = "new";
        if (outcome == "cancelled") cancellation.Cancel();
        queued();
        Assert.Equal(outcome == "accepted" ? 1 : 0, executed);
    }

    // ---------- happy path ----------

    [Fact]
    public async Task ChatAsync_AppendsBothTurns_AndPersistsOnlyDialogue()
    {
        var transport = new FakeTransport();
        var store = new FakeStore();
        using var brain = Build(transport, store);

        var result = await brain.ChatAsync("hi bambi");

        Assert.True(result.IsAiGenerated);
        Assert.Equal("ok~", result.Text);

        var turns = brain.Session.Turns;
        Assert.Equal(2, turns.Count);
        Assert.Equal(TurnKind.UserChat, turns[0].Kind);
        Assert.Equal("hi bambi", turns[0].Text);
        Assert.Equal(TurnKind.AssistantChat, turns[1].Kind);

        var saved = await WaitForWrite(store, 1);
        Assert.Equal(2, saved.Count);
        Assert.All(saved, t => Assert.True(t.IsDialogue));
    }

    [Fact]
    public async Task ChatAsync_SendsTheHistoryWindow_SoASecondTurnCarriesTheFirst()
    {
        // The whole point of Train 1: the default (cloud) path stops being [system, user] per call.
        var transport = new FakeTransport();
        var store = new FakeStore();
        using var brain = Build(transport, store);

        await brain.ChatAsync("first");
        await brain.ChatAsync("second");

        var second = transport.Sends[^1].Messages;
        Assert.Equal(ChatMessage.RoleSystem, second[0].Role);
        Assert.Equal(4, second.Count);                            // system + user, assistant, user
        Assert.Equal("first", second[1].Content);
        Assert.Equal("ok~", second[2].Content);
        Assert.Equal("second", second[3].Content);
        Assert.Equal(AiPurpose.Chat, transport.Sends[^1].Options.Purpose);
        Assert.True(transport.Sends[^1].Options.Interactive);
    }

    // ---------- P2/H5 ----------

    [Fact]
    public async Task RefusedInput_RollsBackTheUserTurn_AndNeverPersists()
    {
        var transport = new FakeTransport
        {
            Respond = (_, _) => new AiReplyResult(string.Empty, IsAiGenerated: false,
                Refusal: new ModerationRefusalInfo(ProhibitedCategory.ProfessionalAdvice, ModerationSource.Input))
        };
        var store = new FakeStore();
        using var brain = Build(transport, store);

        var result = await brain.ChatAsync("something prohibited");

        Assert.NotNull(result.Refusal);
        Assert.Equal(ModerationSource.Input, result.Refusal!.Source);
        Assert.False(result.IsAiGenerated);            // a refusal never wears the AI badge
        Assert.Empty(brain.Session.Turns);             // rolled back
        Assert.Empty(store.Writes);                    // and never reached disk
    }

    [Fact]
    public async Task RefusedOutput_RollsBackTheUserTurn_AndNeverPersists()
    {
        var transport = new FakeTransport
        {
            Respond = (_, _) => new AiReplyResult(string.Empty, IsAiGenerated: false,
                Refusal: new ModerationRefusalInfo(null, ModerationSource.Output))
        };
        var store = new FakeStore();
        using var brain = Build(transport, store);

        var result = await brain.ChatAsync("innocuous prompt, prohibited reply");

        Assert.Equal(ModerationSource.Output, result.Refusal!.Source);
        Assert.Empty(brain.Session.Turns);
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task RefusedTurn_DoesNotContaminateTheNextRequest()
    {
        // The rollback only matters if the refused text is also gone from the NEXT prompt window.
        var transport = new FakeTransport();
        var store = new FakeStore();
        using var brain = Build(transport, store);

        transport.Respond = (_, _) => new AiReplyResult(string.Empty, false,
            new ModerationRefusalInfo(null, ModerationSource.Input));
        await brain.ChatAsync("the prohibited line");

        transport.Respond = (_, _) => new AiReplyResult("ok~", true, null);
        await brain.ChatAsync("a clean line");

        var sent = transport.Sends[^1].Messages;
        Assert.DoesNotContain(sent, m => m.Content.Contains("prohibited"));
        Assert.Equal(2, sent.Count); // system + the one clean user turn
    }

    // ---------- canned / failure ----------

    [Fact]
    public async Task CannedReply_RollsBackTheUnansweredUserTurn()
    {
        // Ollama down / not logged in / proxy 5xx. The user's turn has no answer, so it must not
        // stay in the log: retries would stack consecutive user messages with nothing between them,
        // and the first later success would persist the whole block. The legacy path rolled this
        // back explicitly and so do we.
        var transport = new FakeTransport
        {
            Respond = (_, _) => new AiReplyResult("Good girl~", IsAiGenerated: false, Refusal: null)
        };
        var store = new FakeStore();
        using var brain = Build(transport, store);

        var result = await brain.ChatAsync("hello?");

        Assert.False(result.IsAiGenerated);
        Assert.Empty(brain.Session.Turns);
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task RepeatedTransportFailures_NeverPersistABlockOfUnansweredUserTurns()
    {
        var transport = new FakeTransport
        {
            Respond = (_, _) => new AiReplyResult("(Can't reach Ollama)", IsAiGenerated: false, Refusal: null)
        };
        var store = new FakeStore();
        using var brain = Build(transport, store);

        for (int i = 0; i < 5; i++) await brain.ChatAsync("hey");

        transport.Respond = (_, _) => new AiReplyResult("hi~", IsAiGenerated: true, Refusal: null);
        await brain.ChatAsync("you back?");

        var saved = await WaitForWrite(store, 1);
        Assert.Equal(2, saved.Count);                       // the one real exchange, nothing else
        Assert.Equal("you back?", saved[0].Text);
    }

    [Fact]
    public async Task EmptyInput_IsANoOp()
    {
        var transport = new FakeTransport();
        var store = new FakeStore();
        using var brain = Build(transport, store);

        var result = await brain.ChatAsync("   ");

        Assert.Empty(transport.Sends);
        Assert.Empty(brain.Session.Turns);
        Assert.False(result.IsAiGenerated);
    }

    // ---------- ambient ----------

    [Fact]
    public async Task ReactAsync_UsesTheReactionPurpose_AndAppendsTheReplyAsAmbientReply()
    {
        var transport = new FakeTransport();
        var store = new FakeStore();
        using var brain = Build(transport, store);

        var result = await brain.ReactAsync("finished mandatory video 'Bambi Bae'");

        Assert.True(result.IsAiGenerated);
        Assert.Equal(AiPurpose.Reaction, transport.Sends[0].Options.Purpose);
        Assert.False(transport.Sends[0].Options.Interactive);

        var turns = brain.Session.Turns;
        Assert.Equal(TurnKind.AmbientEvent, turns[0].Kind);
        // AmbientReply, not AssistantChat: it shapes the live window (so a follow-up "why'd you say
        // that?" has context) but is not dialogue and so never reaches disk.
        Assert.Equal(TurnKind.AmbientReply, turns[1].Kind);
        Assert.False(turns[1].IsDialogue);
        Assert.Equal(ChatMessage.RoleAssistant, turns[1].Role);

        // The event went on the wire wearing the sigil, as a user-role message.
        var evt = transport.Sends[0].Messages[^1];
        Assert.Equal(ChatMessage.RoleUser, evt.Role);
        Assert.Equal("«event: finished mandatory video 'Bambi Bae'»", evt.Content);
    }

    [Fact]
    public async Task AmbientReplies_NeverReachDisk_AndCannotEvictRealDialogue()
    {
        // Awareness fires all day. Persisting her ambient one-liners (while their triggering events
        // are dropped) would fill the 100-turn cap with orphaned assistant lines and push the actual
        // conversation off the front — and it would put browsing commentary on disk under a toggle
        // labelled *chat* memory.
        var transport = new FakeTransport();
        var store = new FakeStore();
        using var brain = Build(transport, store);

        await brain.ChatAsync("hi");
        for (int i = 0; i < 20; i++) await brain.ReactAsync($"user opened Reddit ({i})");

        brain.Flush();

        Assert.All(store.Saved, t => Assert.True(t.IsDialogue));
        Assert.Equal(2, store.Saved.Count);                       // just the one real exchange
        Assert.DoesNotContain(store.Saved, t => t.Kind == TurnKind.AmbientReply);
    }

    [Fact]
    public async Task ReactAsync_DropsTheEventTurn_WhenNothingUsableComesBack()
    {
        // A dead moment must not sit in the window shaping the next reply.
        var transport = new FakeTransport
        {
            Respond = (_, _) => new AiReplyResult(string.Empty, IsAiGenerated: false, Refusal: null)
        };
        var store = new FakeStore();
        using var brain = Build(transport, store);

        var result = await brain.ReactAsync("user opened Amazon");

        Assert.False(result.IsAiGenerated);
        Assert.Empty(brain.Session.Turns);
    }

    [Fact]
    public void CompanionEvent_ClampsDescriptorsToTwentyFiveTokens()
    {
        var evt = new CompanionEvent(new string('a', 400));
        var normalized = evt.Normalized();

        Assert.True(ChatSession.ApproxTokens(normalized) <= CompanionEvent.MaxTokens + 1);
        Assert.EndsWith("…", normalized);

        // Newlines are folded so an event can never look like several messages.
        Assert.Equal("a b", new CompanionEvent("a\r\nb").Normalized());
    }

    // ---------- bark echo ----------

    [Fact]
    public void BarkEchoes_AreNeverPersisted()
    {
        var store = new FakeStore();
        using var brain = Build(new FakeTransport(), store);

        brain.Session.Append(TurnKind.UserChat, "u");
        brain.Session.Append(TurnKind.AssistantChat, "a");
        brain.Session.Append(TurnKind.BarkEcho, "«Bambi said aloud: \"good girl~\"»", voiced: true);

        brain.Flush();

        Assert.Single(store.Writes);
        Assert.Equal(2, store.Saved.Count);
        Assert.DoesNotContain(store.Saved, t => t.Kind == TurnKind.BarkEcho);
    }

    [Fact]
    public void FormatBarkEcho_WearsTheSaidAloudSigil()
    {
        var echo = CompanionTurn.FormatBarkEcho("Bambi", "  the rabbit hole~  ");
        Assert.Equal("«Bambi said aloud: \"the rabbit hole~\"»", echo);
    }

    // ---------- single flight ----------

    [Fact]
    public async Task AmbientRequests_AreDroppedWhileAUserCallIsInFlight()
    {
        var release = new TaskCompletionSource();
        var transport = new FakeTransport();
        var store = new FakeStore();
        using var brain = Build(transport, store);

        transport.Respond = (_, opts) =>
        {
            if (opts.Purpose == AiPurpose.Chat) release.Task.GetAwaiter().GetResult();
            return new AiReplyResult("ok~", true, null);
        };

        var chat = Task.Run(() => brain.ChatAsync("hold the line"));
        // Wait until the chat call is genuinely inside the transport.
        for (int i = 0; i < 200 && transport.Sends.Count == 0; i++) await Task.Delay(10);

        var reaction = await brain.ReactAsync("user opened Amazon");

        Assert.False(reaction.IsAiGenerated);            // dropped, not queued
        Assert.Single(transport.Sends);                  // never reached the wire
        Assert.DoesNotContain(brain.Session.Turns, t => t.Kind == TurnKind.AmbientEvent);

        release.SetResult();
        await chat;
    }

    // ---------- restore / she_remembers ----------

    [Fact]
    public void Construction_RestoresTheStoredSessionAndReportsIt()
    {
        var store = new FakeStore
        {
            Snapshot = new CompanionSessionSnapshot(new[]
            {
                CompanionTurn.Create(TurnKind.UserChat, "from yesterday"),
                CompanionTurn.Create(TurnKind.AssistantChat, "i remember~")
            }, ImportedFromLegacy: true)
        };

        using var brain = Build(new FakeTransport(), store);

        Assert.Equal(2, brain.RestoredTurnCount);
        Assert.Equal(2, brain.Session.Count);
    }

    [Fact]
    public async Task RestoredTurns_RideAlongInTheNextRequest()
    {
        // This is what "she remembers across launches" actually means on the wire, and it is now
        // true for cloud users, not just local Ollama ones.
        var store = new FakeStore
        {
            Snapshot = new CompanionSessionSnapshot(new[]
            {
                CompanionTurn.Create(TurnKind.UserChat, "i'm scared of the spiral"),
                CompanionTurn.Create(TurnKind.AssistantChat, "aww~")
            }, ImportedFromLegacy: false)
        };
        var transport = new FakeTransport();
        using var brain = Build(transport, store);

        await brain.ChatAsync("hi again");

        Assert.Contains(transport.Sends[0].Messages, m => m.Content == "i'm scared of the spiral");
    }

    // ---------- anti-fixation (RecentRecommendations producer) ----------

    [Fact]
    public async Task AReplyThatNamesAPoolTitle_BansItFromTheNextPrompt()
    {
        // The structural half of the anti-fixation fix. The per-call title shuffle is gone (the
        // stable prefix lists the pool alphabetically and resolves its {{VIDEO}} examples once per
        // launch), so if nothing ever WRITES into RecentRecommendations the exclusion line is null
        // forever and the only mitigation left is one sentence of prose.
        var recommendations = new RecentRecommendations();
        var transport = new FakeTransport
        {
            Respond = (_, _) => new AiReplyResult("go watch Bambi Bae, good girl~", true, null)
        };
        using var brain = Build(transport, new FakeStore(), recommendations, "Bambi Bae", "IQ Programming");

        await brain.ChatAsync("give me something to watch");

        Assert.Contains("Bambi Bae", recommendations.Current());
        Assert.DoesNotContain("IQ Programming", recommendations.Current());
        Assert.Contains("Bambi Bae", recommendations.BuildExclusionLine());
    }

    [Fact]
    public async Task AnAmbientQuipThatNamesAPoolTitle_AlsoBansIt()
    {
        // Ambient calls carry history now, so an ambient "watch X~" is exactly the few-shot bait
        // that fixated the model on one title before. It has to burn a slot too.
        var recommendations = new RecentRecommendations();
        var transport = new FakeTransport
        {
            Respond = (_, _) => new AiReplyResult("still doomscrolling? go watch Bambi Bae instead~", true, null)
        };
        using var brain = Build(transport, new FakeStore(), recommendations, "Bambi Bae");

        await brain.ReactAsync("user has been on Reddit 22m");

        Assert.Contains("Bambi Bae", recommendations.Current());
    }

    [Fact]
    public async Task ALongerTitleWins_SoASubstringDoesNotBurnASecondSlot()
    {
        var recommendations = new RecentRecommendations();
        var transport = new FakeTransport
        {
            Respond = (_, _) => new AiReplyResult("try Bambi Bae 2~", true, null)
        };
        using var brain = Build(transport, new FakeStore(), recommendations, "Bambi Bae", "Bambi Bae 2");

        await brain.ChatAsync("something new?");

        Assert.Equal(new[] { "Bambi Bae 2" }, recommendations.Current());
    }

    [Fact]
    public async Task ACannedFallbackNeverBurnsARecommendationSlot()
    {
        var recommendations = new RecentRecommendations();
        var transport = new FakeTransport
        {
            Respond = (_, _) => new AiReplyResult("Bambi Bae is fun~", IsAiGenerated: false, Refusal: null)
        };
        using var brain = Build(transport, new FakeStore(), recommendations, "Bambi Bae");

        await brain.ChatAsync("what should i watch");

        Assert.Empty(recommendations.Current());
    }

    [Fact]
    public void Forget_ClearsSession_Recommendations_MemoryAndDisk()
    {
        var store = new FakeStore();
        using var brain = Build(new FakeTransport(), store);
        brain.Session.Append(TurnKind.UserChat, "u");
        brain.NoteRecommendation("Bambi Bae");
        brain.Memory.AddFact("likes the spiral", MemoryFactKind.Preference);

        brain.Forget();

        Assert.Empty(brain.Session.Turns);
        Assert.Empty(brain.Recommendations.Current());
        Assert.Empty(brain.Memory.GetFacts());
        Assert.Equal(1, store.WipeCount);
    }

    [Fact]
    public async Task Forget_IsNotUndoneByTheNextTurnOrTheShutdownFlush()
    {
        // The wipe's whole job. Deleting session.json without clearing the live log is cosmetic:
        // the next reply re-persists the entire pre-wipe conversation, and even if the user never
        // sends one, Dispose() -> Flush() writes it back at shutdown.
        var transport = new FakeTransport();
        var store = new FakeStore();
        var brain = Build(transport, store);

        await brain.ChatAsync("something private");
        await WaitForWrite(store, 1);

        brain.Forget();

        await brain.ChatAsync("hello again");
        var saved = await WaitForWrite(store, 2);
        Assert.DoesNotContain(saved, t => t.Text == "something private");

        brain.Dispose();
        Assert.DoesNotContain(store.Saved, t => t.Text == "something private");
        Assert.Equal(0, brain.RestoredTurnCount);
    }

    [Fact]
    public async Task ForgetConversation_ClearsTheTranscriptButKeepsDurableMemory()
    {
        // What the chat-memory surfaces call. Turning off "chat memory" is a promise about
        // dialogue — it is not a request to forget what level you are.
        var store = new FakeStore();
        var memory = new InertMemoryStore();
        using var brain = new CompanionBrain(new FakeTransport(), new StubAssembler(), memory, store);

        await brain.ChatAsync("hi");
        brain.ForgetConversation();

        Assert.Empty(brain.Session.Turns);
        Assert.Equal(1, store.WipeCount);
        Assert.Equal(0, memory.WipeCalls);   // durable memory is NOT what this button forgets

        brain.Forget();
        Assert.Equal(1, memory.WipeCalls);   // the panel's all-or-nothing wipe still does
    }

    /// <summary>
    /// The Engine Room's "clear conversation" tells the user, in nine languages, that "what she
    /// knows about you is untouched". It calls <see cref="CompanionBrain.ForgetThread"/> for exactly
    /// that reason: <see cref="CompanionBrain.ForgetConversation"/> also runs
    /// <c>MemoryStore.ForgetChatDerived</c>, whose <c>RemoveAll</c> ignores <c>IsProtected</c> and
    /// takes pinned and Boundary facts with it — an irreversible action contradicting its own
    /// confirmation, on the two surfaces whose whole job is trust.
    /// </summary>
    [Fact]
    public async Task ForgetThread_DropsTheThreadOnly_WhileForgetConversationTakesChatDerivedMemory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-forget-scope", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new FakeStore();
            using var memory = new MemoryStore(Path.Combine(dir, "memory.json"), () => DateTime.UtcNow, 1234);

            // A chat-sourced joke the user pinned, and a chat-sourced Boundary — the two kinds
            // MemoryStore treats as un-evictable everywhere else.
            memory.AddFact("calls me kitten", MemoryFactKind.Joke, 0.6, MemoryFact.SourceChat);
            memory.AddFact("no teasing about work", MemoryFactKind.Boundary, 0.9, MemoryFact.SourceChat);
            foreach (var f in memory.GetFacts().ToList()) memory.UpdateFact(f.Id, pinned: true);

            using var brain = new CompanionBrain(new FakeTransport(), new StubAssembler(), memory, store);
            await brain.ChatAsync("hi");

            brain.ForgetThread();

            Assert.Empty(brain.Session.Turns);          // the thread is gone…
            Assert.Equal(1, store.WipeCount);           // …in memory and on disk…
            Assert.Equal(2, memory.GetFacts().Count);   // …and she still knows what she knew.

            // The wider scope, which the diary's copy DOES announce, still takes them.
            brain.ForgetConversation();
            Assert.Empty(memory.GetFacts());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    // ---------- kill switch ----------

    [Fact]
    public void ShouldRoute_IsFalseWheneverTheKillSwitchIsOffOrTheBrainIsMissing()
    {
        var store = new FakeStore();
        using var brain = Build(new FakeTransport(), store);

        Assert.True(CompanionBrain.ShouldRoute(brain, killSwitchOn: true));
        // UseCompanionBrain=false -> every call site takes the legacy stateless IAiService path.
        Assert.False(CompanionBrain.ShouldRoute(brain, killSwitchOn: false));
        // A brain that failed to construct is the same fallback, not a crash.
        Assert.False(CompanionBrain.ShouldRoute(null, killSwitchOn: true));
        Assert.False(CompanionBrain.ShouldRoute(null, killSwitchOn: false));
    }

    [Fact]
    public void KillSwitchOff_LeavesTheTransportAndSessionCompletelyUntouched()
    {
        // The legacy path never enters the brain, so nothing is recorded and no request is made.
        var transport = new FakeTransport();
        var store = new FakeStore();
        using var brain = Build(transport, store);

        if (CompanionBrain.ShouldRoute(brain, killSwitchOn: false))
            Assert.Fail("routing predicate must send the call to the legacy path");

        Assert.Empty(transport.Sends);
        Assert.Empty(brain.Session.Turns);
        Assert.Empty(store.Writes);
    }

    // ---------- mod switch ----------

    [Fact]
    public async Task OnModSwitched_PurgesBarkEchoes_AndKeepsTheDialogue()
    {
        // 0813: bark echoes carry the companion name they were recorded under, and the persona
        // fence deliberately keeps BarkEcho turns — so after Bambi → Drone the window held
        // «BambiSprite said aloud: …» next to «DroneOS said aloud: …» and the model answered
        // with a two-speaker roleplay transcript. The mod switch must drop the stale echoes
        // (they replay from the new mod's bark_rules.json anyway) without touching dialogue.
        var transport = new FakeTransport();
        var store = new FakeStore();
        using var brain = Build(transport, store);

        await brain.ChatAsync("hi");
        brain.Session.Append(TurnKind.BarkEcho,
            CompanionTurn.FormatBarkEcho("BambiSprite", "such a good girl~"), voiced: true);
        brain.Session.Append(TurnKind.BarkEcho,
            CompanionTurn.FormatBarkEcho("BambiSprite", "spiral time~"), voiced: true);

        brain.OnModSwitched();

        Assert.DoesNotContain(brain.Session.Turns, t => t.Kind == TurnKind.BarkEcho);
        Assert.Equal(2, brain.Session.Turns.Count(t => t.IsDialogue)); // user + reply survive
    }
}
