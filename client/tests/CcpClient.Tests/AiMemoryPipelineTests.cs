using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-040 slice c4 pipeline persist proofs (ai-companion-admission.md §4 rules 1/5, §8 c4;
/// contract §5). Proves the moderation-gated persist with rollback discipline: per-turn
/// pairs persist ONLY after c3's output boundary passes (file-content proof); a blocked
/// turn is typed, rolled back, and NEVER hits disk (zero file content); awareness turns
/// are never persisted (negative proof); a provider switch never implicitly clears memory
/// (contract §5 rule 3); consent-denied persist is a typed no-op; a stale-discarded
/// completion is never persisted. Discharges c3 inventory row 6's Reserved→Wired seam.
/// </summary>
public class AiMemoryPipelineTests
{
    private const string Forbidden = "forbidden-token";

    private sealed class StubProvider : IAiProvider
    {
        private readonly TaskCompletionSource? _gate;

        public StubProvider(AiReply reply, bool gated = false)
        {
            Reply = reply;
            _gate = gated ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        }

        public AiReply Reply { get; }

        public AiProviderDescriptor Descriptor { get; } =
            new(AiProviderId.LocalOllama, AiEndpointClass.Loopback);

        public Func<CancellationToken, Task<CapabilityState>>? Probe { get; } =
            _ => Task.FromResult<CapabilityState>(new CapabilityState.Available("stub-probe"));

        public async Task<AiReply> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
        {
            if (_gate is not null)
            {
                await _gate.Task.ConfigureAwait(false); // uncooperative: ignores the token, replies late
            }

            return Reply;
        }

        public void Release() => _gate?.TrySetResult();
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

        public Harness(AiModerationPolicy? policy = null, Func<AiMemoryConsent>? consent = null)
        {
            MemoryPath = _dir.Path(AiMemoryStore.FileName);
            Memory = new AiMemoryStore(Registry.OwnerFor("AiMemory"), new ListLogSink(), MemoryPath, consent ?? (() => AiMemoryConsent.Granted));
            Memory.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            Pipeline = new AiOperationPipeline(
                Registry, Capabilities, LoopbackOnlyAdmissionPolicy.Instance, Diagnostics,
                new AiModerationBoundary(policy), Memory);
        }

        public async Task<StubProvider> AdmitProviderAsync(AiReply reply, bool gated = false)
        {
            var provider = new StubProvider(reply, gated);
            Pipeline.RegisterProvider(provider);
            Pipeline.SelectProvider(AiProviderId.LocalOllama);
            var runner = new CapabilityProbeRunner(Registry.OwnerFor("probes"), Capabilities);
            await runner.RunAllAsync(CancellationToken.None);
            return provider;
        }

        public string? MemoryFileContent() => File.Exists(MemoryPath) ? File.ReadAllText(MemoryPath) : null;

        public void Dispose() => _dir.Dispose();
    }

    private static readonly AiReply CleanReply = new AiReply.Generated("clean reply", AiEndpointClass.Loopback);

    private static readonly AiModerationPolicy OutputBlockPolicy = new(
    [
        new AiModerationRule("test-block-category", AiModerationAction.Block, [Forbidden]),
    ]);

    [Fact]
    public async Task InteractiveGeneratedTurn_PersistsPair_AfterOutputPasses_FileContentProof()
    {
        using var h = new Harness();
        await h.AdmitProviderAsync(CleanReply);

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("hello companion"));

        Assert.IsType<OperationOutcome.Completed>(result.Outcome);
        Assert.IsType<AiReply.Generated>(result.Reply);
        Assert.Equal(AiMemoryWriteAdmission.Admitted, h.Memory.LastWriteAdmission);
        Assert.IsType<OperationOutcome.Completed>(await h.Memory.SaveImmediate());

        // File-content proof: the pair is ON DISK, user turn then assistant turn.
        var content = h.MemoryFileContent();
        Assert.NotNull(content);
        Assert.Contains("hello companion", content);
        Assert.Contains("clean reply", content);
        Assert.Equal(
            [new AiMemoryTurn(AiMemoryRole.User, "hello companion"), new AiMemoryTurn(AiMemoryRole.Assistant, "clean reply")],
            h.Memory.ReadRecent(10));
    }

    [Fact]
    public async Task OutputBlockedTurn_TypedRefusal_RolledBack_NeverPersisted_ZeroFileContent()
    {
        using var h = new Harness(OutputBlockPolicy);
        await h.AdmitProviderAsync(new AiReply.Generated($"reply with {Forbidden}", AiEndpointClass.Loopback));

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean question"));

        var refused = Assert.IsType<AiReply.Refused>(result.Reply);
        Assert.Equal(AiModerationSource.Output, refused.Refusal.Source);
        await h.Memory.FlushAsync(TimeSpan.FromSeconds(5)); // quiescence: nothing may be in flight

        // File-content proof: the blocked turn NEVER hit disk — the document was never created.
        Assert.Null(h.MemoryFileContent());
        Assert.Empty(h.Memory.ReadRecent(10));
    }

    [Fact]
    public async Task OutputBlockedTurn_TypedRefusal_RolledBack_PriorKnownCleanStateSurvives()
    {
        using var h = new Harness(OutputBlockPolicy);
        await h.AdmitProviderAsync(CleanReply);

        // Prior KNOWN-CLEAN state on disk (WPF P2/H5 claim shape, LocalAiService.cs:624-630:
        // "the file on disk remains at the prior known-clean state") — pre-completion consult A:
        // an absence-of-file proof on a virgin store would not prove the rollback claim.
        await h.Pipeline.RunInteractiveAsync(new AiRequest("prior clean question"));
        Assert.IsType<OperationOutcome.Completed>(await h.Memory.SaveImmediate());
        var priorContent = h.MemoryFileContent();
        Assert.NotNull(priorContent);
        Assert.Contains("prior clean question", priorContent);

        h.Pipeline.SelectProvider(AiProviderId.LocalOllama); // fresh generation (no memory effect)
        var blockedProvider = new StubProvider(new AiReply.Generated($"reply with {Forbidden}", AiEndpointClass.Loopback));
        h.Pipeline.RegisterProvider(blockedProvider); // same id: reply now trips the OUTPUT boundary
        var blocked = await h.Pipeline.RunInteractiveAsync(new AiRequest("second clean question"));

        var refused = Assert.IsType<AiReply.Refused>(blocked.Reply);
        Assert.Equal(AiModerationSource.Output, refused.Refusal.Source);
        await h.Memory.FlushAsync(TimeSpan.FromSeconds(5));

        // The blocked turn NEVER hit disk: the file is byte-identical to the prior clean state.
        Assert.Equal(priorContent, h.MemoryFileContent());
        Assert.Equal(2, h.Memory.ReadRecent(10).Count); // the prior pair only
    }

    [Fact]
    public async Task InputBlockedTurn_NeverPersisted()
    {
        using var h = new Harness(OutputBlockPolicy);
        await h.AdmitProviderAsync(CleanReply);

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest($"user typed {Forbidden}"));

        Assert.IsType<AiReply.Refused>(result.Reply);
        await h.Memory.FlushAsync(TimeSpan.FromSeconds(5));
        Assert.Null(h.MemoryFileContent());
        Assert.Empty(h.Memory.ReadRecent(10));
    }

    [Fact]
    public async Task AwarenessTurn_NeverPersisted_NegativeProof()
    {
        using var h = new Harness();
        await h.AdmitProviderAsync(CleanReply);

        var result = await h.Pipeline.RunAwarenessAsync(new AiRequest("ambient context"), awarenessConsent: true);

        Assert.IsType<OperationOutcome.Completed>(result.Outcome);
        Assert.IsType<AiReply.Generated>(result.Reply); // the awareness reply exists...
        await h.Memory.FlushAsync(TimeSpan.FromSeconds(5));
        Assert.Null(h.MemoryFileContent());             // ...but ambient turns NEVER persist (WPF stateless path)
        Assert.Empty(h.Memory.ReadRecent(10));
    }

    [Fact]
    public async Task ProviderSwitch_NeverImplicitlyClearsMemory()
    {
        using var h = new Harness();
        await h.AdmitProviderAsync(CleanReply);
        await h.Pipeline.RunInteractiveAsync(new AiRequest("remembered question"));
        await h.Memory.SaveImmediate();
        Assert.NotNull(h.MemoryFileContent());

        h.Pipeline.SelectProvider(AiProviderId.LocalOllama); // switch = generation invalidation, NOT a memory operation

        Assert.Equal(2, h.Memory.ReadRecent(10).Count);
        Assert.NotNull(h.MemoryFileContent()); // memory survives the switch (contract §5 rule 3)
    }

    [Fact]
    public async Task ConsentDenied_OperationSucceeds_PersistIsTypedNoOp_NothingOnDisk()
    {
        using var h = new Harness(consent: () => AiMemoryConsent.Denied);
        await h.AdmitProviderAsync(CleanReply);

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("hello companion"));

        Assert.IsType<AiReply.Generated>(result.Reply); // the operation itself is unaffected
        Assert.Equal(AiMemoryWriteAdmission.ConsentDenied, h.Memory.LastWriteAdmission); // typed, never silent
        await h.Memory.FlushAsync(TimeSpan.FromSeconds(5));
        Assert.Null(h.MemoryFileContent());
        Assert.Empty(h.Memory.ReadRecent(10));
    }

    [Fact]
    public async Task StaleCompletion_Discarded_NeverPersisted()
    {
        using var h = new Harness();
        var provider = await h.AdmitProviderAsync(CleanReply, gated: true);

        var operation = h.Pipeline.RunInteractiveAsync(new AiRequest("stale question"));
        h.Pipeline.SelectProvider(AiProviderId.LocalOllama); // switch mid-flight: generation invalidated
        provider.Release();
        var result = await operation;

        Assert.IsType<OperationOutcome.Cancelled>(result.Outcome); // stale discard (c1 semantics)
        Assert.Null(result.Reply);
        await h.Memory.FlushAsync(TimeSpan.FromSeconds(5));
        Assert.Null(h.MemoryFileContent()); // a discarded reply is never remembered (contract §2 rule 2)
        Assert.Empty(h.Memory.ReadRecent(10));
    }

    private sealed class ListLogSink : ILogSink
    {
        public List<string> Messages { get; } = [];

        public void Log(string message) => Messages.Add(message);
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ccp-aimem-pipe-" + Guid.NewGuid().ToString("N"));

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
}
