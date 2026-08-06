using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Train 1 — the <c>[Obsolete]</c> adapter layer that lets <c>KeywordTriggerService</c>,
/// <c>AutonomyService</c> and <c>AvatarTubeWindow.Reactions</c> keep calling the six one-shot methods
/// while the brain quietly takes over underneath.
///
/// <para>Two properties matter. <b>Equivalence:</b> with the kill switch off, nothing routes and the
/// legacy path runs untouched. <b>Contract preservation:</b> when it does route, the brain's typed
/// <see cref="AiReplyResult"/> has to come back out as the legacy ambient contract — model text, or
/// null and the caller uses its own preset phrase — because the callers were written against that
/// and one of them decides whether to show the pink AI badge from it.</para>
/// </summary>
public class LegacyAdapterRoutingTests
{
    // ---------- fakes (mirrors CompanionBrainTests so the brain behaves like the real one) ----------

    private sealed class FakeTransport : IAiService
    {
        public Func<IReadOnlyList<ChatMessage>, AiCallOptions, AiReplyResult> Respond { get; set; } =
            (_, _) => new AiReplyResult("mmh~ good girl", IsAiGenerated: true, Refusal: null);

        public List<(IReadOnlyList<ChatMessage> Messages, AiCallOptions Options)> Sends { get; } = new();

        public bool IsAvailable => true;
        public int DailyRequestsRemaining => -1;

        public Task<AiReplyResult> SendAsync(IReadOnlyList<ChatMessage> messages, AiCallOptions options,
            CancellationToken cancellationToken = default)
        {
            Sends.Add((messages, options));
            return Task.FromResult(Respond(messages, options));
        }

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

    private sealed class NullStore : ICompanionSessionStore
    {
        public List<IReadOnlyList<CompanionTurn>> Writes { get; } = new();
        public CompanionSessionSnapshot Load() => CompanionSessionSnapshot.Empty;
        public void Save(IReadOnlyList<CompanionTurn> dialogueTurns) { lock (Writes) Writes.Add(dialogueTurns.ToList()); }
        public void Wipe() { }
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

    private static CompanionBrain Build(FakeTransport transport) =>
        new(transport, new StubAssembler(), new MemoryStore(), new NullStore());

    // ---------- routing gates: ambient ----------

    [Fact]
    public void Ambient_RoutesToTheBrainOnlyWhenEverythingLinesUp()
    {
        using var brain = Build(new FakeTransport());

        Assert.True(BrainAdapter.ShouldRouteAmbient(brain, killSwitchOn: true, providerAvailable: true, promptTemplate: null));
    }

    [Fact]
    public void Ambient_KillSwitchOff_TakesTheLegacyPath()
    {
        // UseCompanionBrain=false must restore today's stateless behaviour at every call site.
        using var brain = Build(new FakeTransport());

        Assert.False(BrainAdapter.ShouldRouteAmbient(brain, killSwitchOn: false, providerAvailable: true, promptTemplate: null));
    }

    [Fact]
    public void Ambient_MissingBrain_TakesTheLegacyPath()
    {
        // A brain that failed to construct is a fallback, not a crash.
        Assert.False(BrainAdapter.ShouldRouteAmbient(null, killSwitchOn: true, providerAvailable: true, promptTemplate: null));
    }

    [Fact]
    public void Ambient_NoProviderEntitlement_TakesTheLegacyPath()
    {
        // Otherwise every logged-out user burns an event turn per ambient moment for nothing.
        using var brain = Build(new FakeTransport());

        Assert.False(BrainAdapter.ShouldRouteAmbient(brain, killSwitchOn: true, providerAvailable: false, promptTemplate: null));
    }

    [Fact]
    public void Ambient_UserAuthoredTemplate_TakesTheLegacyPath()
    {
        // A keyword trigger's custom prompt is an instruction; the event sigil plus the 25-token
        // clamp would mangle it. Those calls stay exactly as they are today.
        using var brain = Build(new FakeTransport());

        Assert.False(BrainAdapter.ShouldRouteAmbient(brain, killSwitchOn: true, providerAvailable: true,
            promptTemplate: "React to {keyword} like a brat."));
    }

    // ---------- routing gates: chat ----------

    [Fact]
    public void Chat_RoutesOnlyForGenuineUserSpeech()
    {
        using var brain = Build(new FakeTransport());

        Assert.True(BrainAdapter.ShouldRouteChat(brain, killSwitchOn: true, isUserMessage: true));
        // App-authored prompts (autonomy nudge, "random thought", GetBackToMe) must NOT become
        // interactive turns: interactive is what escalates the user's Content Policy Notice.
        Assert.False(BrainAdapter.ShouldRouteChat(brain, killSwitchOn: true, isUserMessage: false));
        Assert.False(BrainAdapter.ShouldRouteChat(brain, killSwitchOn: false, isUserMessage: true));
        Assert.False(BrainAdapter.ShouldRouteChat(null, killSwitchOn: true, isUserMessage: true));
    }

    // ---------- result mapping ----------

    [Fact]
    public void ToAmbientLine_PassesGenuineModelText()
    {
        Assert.Equal("mmh~", BrainAdapter.ToAmbientLine(new AiReplyResult("mmh~", IsAiGenerated: true, Refusal: null)));
    }

    [Fact]
    public void ToAmbientLine_DropsRefusals()
    {
        // An ambient moment the user never prompted must never pop a POLICY bubble; the hit is
        // already in the compliance log at the point of detection.
        var refusal = new AiReplyResult(string.Empty, IsAiGenerated: false,
            Refusal: new ModerationRefusalInfo(ProhibitedCategory.Minor, ModerationSource.Output));

        Assert.Null(BrainAdapter.ToAmbientLine(refusal));
    }

    [Fact]
    public void ToAmbientLine_DropsCannedFallbacksAndDiagnostics()
    {
        // This also closes a latent badge bug: the local provider used to hand its canned fallback
        // and its "(Ollama isn't running)" diagnostic back to reaction call sites, which rendered
        // them under the pink AI badge as if the model had spoken.
        Assert.Null(BrainAdapter.ToAmbientLine(
            new AiReplyResult("Bambi's head is so empty right now~ *giggles*", IsAiGenerated: false, Refusal: null)));
        Assert.Null(BrainAdapter.ToAmbientLine(
            new AiReplyResult("(Can't reach Ollama at http://localhost:11434/)", IsAiGenerated: false, Refusal: null)));
    }

    [Fact]
    public void ToAmbientLine_DropsEmptyAndNull()
    {
        Assert.Null(BrainAdapter.ToAmbientLine(new AiReplyResult("   ", IsAiGenerated: true, Refusal: null)));
        Assert.Null(BrainAdapter.ToAmbientLine(null));
    }

    // ---------- end to end through a real brain ----------

    [Fact]
    public async Task RoutedAmbientCall_ReachesTheTransportAsAReactionAndComesBackAsALine()
    {
        var transport = new FakeTransport();
        using var brain = Build(transport);

        var line = await BrainAdapter.ReactAsync(brain, FrameFormatter.VideoDoneEvent("Bambi Bae"));

        Assert.Equal("mmh~ good girl", line);
        Assert.Equal(AiPurpose.Reaction, transport.Sends[0].Options.Purpose);
        Assert.False(transport.Sends[0].Options.Interactive);   // never escalates the counter
        Assert.Equal("«event: user finished the mandatory video \"Bambi Bae\"»",
            transport.Sends[0].Messages[^1].Content);
    }

    [Fact]
    public async Task RoutedAmbientCall_ReturnsNullWhenTheBrainHasNothingToSay()
    {
        // Callers treat null as "use my preset phrase", which is what the legacy providers returned
        // for every unavailable / failed / refused ambient call.
        var transport = new FakeTransport
        {
            Respond = (_, _) => new AiReplyResult(string.Empty, IsAiGenerated: false, Refusal: null)
        };
        using var brain = Build(transport);

        Assert.Null(await BrainAdapter.ReactAsync(brain, FrameFormatter.KeywordEvent("bimbo")));
    }

    [Fact]
    public async Task RoutedAmbientCalls_AccumulateContext_WhichIsThePointOfTheTrain()
    {
        // Two ambient moments in a row: the second request carries the first exchange, so a
        // follow-up "why'd you say that?" in the chat box has something to stand on.
        var transport = new FakeTransport();
        using var brain = Build(transport);

        await BrainAdapter.ReactAsync(brain, FrameFormatter.KeywordEvent("bimbo"));
        await BrainAdapter.ReactAsync(brain, FrameFormatter.VideoDoneEvent("Bambi Bae"));

        var second = transport.Sends[^1].Messages;
        Assert.Contains(second, m => m.Content.Contains("bimbo"));
        Assert.Contains(second, m => m.Content == "mmh~ good girl");
    }
}
