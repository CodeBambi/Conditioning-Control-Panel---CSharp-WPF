using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Seam facts: the three hygiene layers and the UNION moderation rule are LIVE in the
/// real pipeline at the reply seam (`AiOperationPipeline.cs`, `produced is AiReply.Generated`
/// block), upstream of the link strip. Proves: the union rule in BOTH directions (a
/// forbidden token visible only after hygiene blocks; a forbidden token removed by hygiene
/// still blocks), H3 detection yields the typed `MalformedOutput` with the recoverable
/// `"response"` text NEVER displayed, persisted, or executed, the port's own trigger echo is
/// stripped before the bubble AND memory, a hygiene-emptied reply is the existing typed
/// `reply-stripped-empty` with the turn pair never appended (atomic-pair rule), and a
/// legitimate reply passes through byte-identical.
/// </summary>
public class AiReplyHygienePipelineTests
{
    private sealed class StubProvider : IAiProvider
    {
        public StubProvider(AiReply reply) => Reply = reply;

        public AiReply Reply { get; }

        public int Calls;

        public AiProviderDescriptor Descriptor { get; } =
            new(AiProviderId.LocalOllama, AiEndpointClass.Loopback);

        public Func<CancellationToken, Task<CapabilityState>>? Probe { get; } =
            _ => Task.FromResult<CapabilityState>(new CapabilityState.Available("stub-probe"));

        public Task<AiReply> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(Reply);
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly TempDir _dir = new();

        public OperationRegistry Registry { get; } = new();
        public CapabilityRegistry Capabilities { get; } = new();
        public CollectingAiDiagnosticsSink Diagnostics { get; } = new();
        public string MemoryPath { get; }
        public AiMemoryStore Memory { get; }
        public AiOperationPipeline Pipeline { get; }
        public StubProvider Provider { get; }

        public Harness(AiModerationPolicy policy, AiReply reply)
        {
            MemoryPath = _dir.Path(AiMemoryStore.FileName);
            Memory = new AiMemoryStore(
                Registry.OwnerFor("AiMemory"), new ListLogSink(), MemoryPath, () => AiMemoryConsent.Granted);
            Memory.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            Pipeline = new AiOperationPipeline(
                Registry, Capabilities, LoopbackOnlyAdmissionPolicy.Instance, Diagnostics,
                new AiModerationBoundary(policy), Memory);
            Provider = new StubProvider(reply);
            Pipeline.RegisterProvider(Provider);
            Pipeline.SelectProvider(AiProviderId.LocalOllama);
        }

        public async Task AdmitAsync()
        {
            var runner = new CapabilityProbeRunner(Registry.OwnerFor("probes"), Capabilities);
            await runner.RunAllAsync(CancellationToken.None);
        }

        public string? MemoryFileContent() => File.Exists(MemoryPath) ? File.ReadAllText(MemoryPath) : null;

        public void Dispose() => _dir.Dispose();
    }

    private sealed class ListLogSink : ILogSink
    {
        public List<string> Messages { get; } = [];

        public void Log(string message) => Messages.Add(message);
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ccp-sp069-hygiene-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(_root);

        public string Path(string fileName) => System.IO.Path.Combine(_root, fileName);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; the OS temp reaper owns the residue.
            }
        }
    }

    // Block tokens: a hyphenated one (split/joinable across a tag boundary) and a spaced one
    // (joinable by the Ġ→space artifact mapping). Soft token: a spaced phrase.
    private static readonly AiModerationPolicy Policy = new(
    [
        new AiModerationRule("test-block-category", AiModerationAction.Block, ["sensitive-token", "bad phrase"]),
        new AiModerationRule("test-soft-category", AiModerationAction.SoftHit, ["soft token"]),
    ]);

    private static AiReply.Generated Reply(string text) => new(text, AiEndpointClass.Loopback);

    // ---- union rule, forward: forbidden token visible ONLY after hygiene ----

    [Fact]
    public async Task UnionRule_TokenJoinedAcrossTagBoundary_Blocks_NeverPersisted()
    {
        // Raw scan sees "sensi...tive-token" (no hit); hygiene removes the think block and the
        // token assembles → hygienic scan blocks. Sanitized-only moderation would block here
        // too — the point is the hygienic half of the union is wired and live.
        using var h = new Harness(Policy, Reply("sensi<thinking>scratch</thinking>tive-token here"));
        await h.AdmitAsync();

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean prompt"));

        var refused = Assert.IsType<AiReply.Refused>(result.Reply);
        Assert.Equal("test-block-category", refused.Refusal.CategoryCode);
        Assert.Equal(AiModerationSource.Output, refused.Refusal.Source);
        Assert.Null(h.Memory.LastWriteAdmission);
        Assert.Empty(h.Memory.ReadPromptContext());
    }

    [Fact]
    public async Task UnionRule_TokenJoinedByArtifactCharacter_Blocks()
    {
        // Ġ→space (H1): raw "badĠphrase" misses the spaced token; hygienic "bad phrase" hits.
        using var h = new Harness(Policy, Reply("badĠphrase, indeed"));
        await h.AdmitAsync();

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean prompt"));

        Assert.IsType<AiReply.Refused>(result.Reply);
    }

    // ---- union rule, reverse: forbidden token REMOVED by hygiene still blocks ----

    [Fact]
    public async Task UnionRule_TokenInsideStrippedThinkBlock_StillBlocks()
    {
        // Hygiene removes "<thinking>sensitive-token</thinking>", so sanitized-only moderation
        // (what WPF does, OpenAiCompatibleService.cs:630-631) would PASS the remaining "hello".
        // The union blocks on the raw scan — the deliberate fail-closed divergence (record.md §4).
        using var h = new Harness(Policy, Reply("<thinking>sensitive-token</thinking> hello"));
        await h.AdmitAsync();

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean prompt"));

        var refused = Assert.IsType<AiReply.Refused>(result.Reply);
        Assert.Equal(AiModerationSource.Output, refused.Refusal.Source);
        Assert.Null(h.Memory.LastWriteAdmission);
    }

    [Fact]
    public async Task UnionRule_TokenInsideStrippedMetadataEcho_StillBlocks()
    {
        // WPF's own rationale case (:622-629): an echoed awareness tag tripping the output
        // guard. WPF sanitizes first and SHOWS the reply; the port refuses the turn.
        using var h = new Harness(Policy, Reply("[Category: sensitive-token | App: VLC] ok"));
        await h.AdmitAsync();

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean prompt"));

        Assert.IsType<AiReply.Refused>(result.Reply);
        Assert.Null(h.Memory.LastWriteAdmission);
    }

    // ---- H3: detection at the seam, typed unavailable, non-lift pinned ----

    [Fact]
    public async Task H3_LeakedEnvelope_TypedMalformedOutput_TextNeverDisplayedPersistedExecuted()
    {
        // The "response" field contains fully recoverable text. WPF lifts it out and shows it
        // (TryLiftResponseField, AiResponseParser.cs:60); the port REFUSES — the text must not
        // reach the bubble (reply is not Generated), memory (no write admission, empty context,
        // nothing on disk), or any execution path (the port wires no command execution into the
        // reply seam — AiEnvelopeValidator stays unwired, per its board row).
        using var h = new Harness(Policy, Reply("{\"response\":\"recoverable text\",\"commands\":[{\"command\":\"bubbles\"}]}"));
        await h.AdmitAsync();

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean prompt"));

        var unavailable = Assert.IsType<AiReply.Unavailable>(result.Reply);
        Assert.Equal(AiReplyCodes.MalformedOutput, unavailable.Code);
        Assert.Equal(1, h.Provider.Calls);
        Assert.Null(h.Memory.LastWriteAdmission);
        Assert.Empty(h.Memory.ReadPromptContext());
        Assert.IsType<OperationOutcome.Completed>(await h.Memory.SaveImmediate());
        var content = h.MemoryFileContent();
        Assert.NotNull(content);
        Assert.DoesNotContain("recoverable text", content);
        Assert.DoesNotContain("bubbles", content);
    }

    [Fact]
    public async Task H3_DetectionIsUnion_TagTailThatWouldSwallowTheField_StillDetected()
    {
        // Pre-approach consult case: after H1 this starts with '{' and carries "response"
        // INSIDE an unclosed tag tail; H2's end-anchored UnclosedKeyedTag eats that tail, so a
        // post-H2-only check would miss it and raw JSON would reach the bubble.
        using var h = new Harness(Policy, Reply("{\"mood\": [Note: see \"response\" above"));
        await h.AdmitAsync();

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean prompt"));

        var unavailable = Assert.IsType<AiReply.Unavailable>(result.Reply);
        Assert.Equal(AiReplyCodes.MalformedOutput, unavailable.Code);
    }

    [Fact]
    public async Task H3_ComposedWithThePortsOwnTrigger_StillDetected()
    {
        // The trigger echo precedes the envelope: only the post-H2 check sees the leading '{'.
        using var h = new Harness(Policy, Reply("[Category: Media | App: VLC] {\"response\":\"x\"}"));
        await h.AdmitAsync();

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean prompt"));

        var unavailable = Assert.IsType<AiReply.Unavailable>(result.Reply);
        Assert.Equal(AiReplyCodes.MalformedOutput, unavailable.Code);
    }

    // ---- H1/H2 at the seam: the bubble and memory carry the hygienic text ----

    [Fact]
    public async Task TriggerEcho_StrippedBeforeBubbleAndMemory()
    {
        // The exact shape AiAwarenessService.cs:229 sends, echoed back appended to a real reply.
        using var h = new Harness(Policy, Reply("On it! [Category: Media | App: VLC | Title: Some Video | Duration: 12m]"));
        await h.AdmitAsync();

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean prompt"));

        var generated = Assert.IsType<AiReply.Generated>(result.Reply);
        Assert.Equal("On it!", generated.Text);
        Assert.IsType<OperationOutcome.Completed>(await h.Memory.SaveImmediate());
        var content = h.MemoryFileContent();
        Assert.NotNull(content);
        Assert.Contains("On it!", content);
        Assert.DoesNotContain("[Category:", content);
    }

    [Fact]
    public async Task HygieneEmptiedReply_TypedUnavailable_TurnPairNeverAppended()
    {
        // Nothing but a scratchpad: H1 empties the reply; the emptied path answers with
        // the existing typed code and the atomic turn-pair rule holds (neither turn appended).
        using var h = new Harness(Policy, Reply("<think>only scratch</think>"));
        await h.AdmitAsync();

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean prompt"));

        var unavailable = Assert.IsType<AiReply.Unavailable>(result.Reply);
        Assert.Equal("reply-stripped-empty", unavailable.Code);
        Assert.Null(h.Memory.LastWriteAdmission);
        Assert.Empty(h.Memory.ReadPromptContext());
    }

    // ---- negative control at the seam: legitimate reply passes through byte-identical ----

    [Fact]
    public async Task LegitimateReply_BracesAndNonMetadataBrackets_PassesByteIdentical_PersistedVerbatim()
    {
        const string text = "plain reply {with brace} [as it happens] all prose";
        using var h = new Harness(Policy, Reply(text));
        await h.AdmitAsync();

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean prompt"));

        var generated = Assert.IsType<AiReply.Generated>(result.Reply);
        Assert.Equal(text, generated.Text);
        Assert.IsType<OperationOutcome.Completed>(await h.Memory.SaveImmediate());
        Assert.Contains(text, h.MemoryFileContent());
    }

    // ---- soft-hit: the union surfaces the marker without blocking, exactly once ----

    [Fact]
    public async Task SoftHit_VisibleOnlyInRaw_SoftHitCode_ReplyShowsHygienicText()
    {
        // Soft token inside the stripped think block: raw soft-hits, hygienic passes. The
        // reply still shows (soft-hit is typed pass-through) with the hygienic text, and the
        // content-free marker is emitted once.
        using var h = new Harness(Policy, Reply("<thinking>soft token</thinking> plain reply"));
        await h.AdmitAsync();

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean prompt"));

        var generated = Assert.IsType<AiReply.Generated>(result.Reply);
        Assert.Equal("plain reply", generated.Text);
        Assert.Contains(h.Diagnostics.Records, r => r.Outcome == AiDiagnosticOutcome.Completed && r.StableCode == "soft-hit:output");
    }

    [Fact]
    public async Task SoftHit_VisibleOnlyAfterHygiene_SoftHitCode()
    {
        // Ġ→space assembles the spaced soft token only in the hygienic text: the hygienic half
        // of the union surfaces the marker. A raw SoftHit must not short-circuit this.
        using var h = new Harness(Policy, Reply("softĠtoken reply"));
        await h.AdmitAsync();

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean prompt"));

        var generated = Assert.IsType<AiReply.Generated>(result.Reply);
        Assert.Equal("soft token reply", generated.Text);
        Assert.Contains(h.Diagnostics.Records, r => r.Outcome == AiDiagnosticOutcome.Completed && r.StableCode == "soft-hit:output");
    }
}
