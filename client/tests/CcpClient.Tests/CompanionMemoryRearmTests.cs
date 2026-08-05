using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-046 regression guard: persist-after-clear-after-panic. Written while chasing a
/// headed-run inspection FALSE ALARM (the inspection script read the camelCase JSON field
/// with the wrong case — the live behavior was correct all along). Kept as the regression
/// guard for the clear → panic → re-arm → re-persist sequence through the real pipeline
/// + store.
/// </summary>
public class CompanionMemoryRearmTests
{
    private sealed class StubProvider : IAiProvider
    {
        private TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StubProvider(bool gated) => Gated = gated;

        public bool Gated { get; }

        public AiProviderDescriptor Descriptor { get; } =
            new(AiProviderId.LocalOllama, AiEndpointClass.Loopback);

        public Func<CancellationToken, Task<CapabilityState>>? Probe { get; } =
            _ => Task.FromResult<CapabilityState>(new CapabilityState.Available("stub-probe"));

        public async Task<AiReply> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
        {
            if (Gated)
            {
                await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return new AiReply.Generated("reply text", AiEndpointClass.Loopback);
        }

        public void Release() => _gate.TrySetResult();

        public void Reset() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [Fact]
    public async Task Clear_ThenPanicRearm_ThenSend_Repersists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp046-repro-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, AiMemoryStore.FileName);
        var registry = new OperationRegistry();
        var capabilities = new CapabilityRegistry();
        var memory = new AiMemoryStore(registry.OwnerFor("AiMemory"), new DebugLogSink(), path, () => AiMemoryConsent.Granted);
        await memory.StartAsync(CancellationToken.None);
        var pipeline = new AiOperationPipeline(
            registry, capabilities, LoopbackOnlyAdmissionPolicy.Instance,
            new CollectingAiDiagnosticsSink(), new AiModerationBoundary(), memory);
        var provider = new StubProvider(gated: true);
        pipeline.RegisterProvider(provider);
        pipeline.SelectProvider(AiProviderId.LocalOllama);
        await new CapabilityProbeRunner(registry.OwnerFor("probes"), capabilities).RunAllAsync(CancellationToken.None);

        // 1. send → persist pair
        provider.Release();
        var r1 = await pipeline.RunInteractiveAsync(new AiRequest("first"));
        Assert.IsType<OperationOutcome.Completed>(r1.Outcome);
        Assert.NotNull(memory.LastWriteCompletion);
        await memory.LastWriteCompletion!;
        Assert.True(File.Exists(path));
        Assert.Equal(2, memory.ReadRecent(10).Count);

        // 2. clear (the live VM path: off-thread, blocking SaveImmediate inside)
        memory.Clear();
        Assert.Equal(AiMemoryClearOutcome.Cleared, memory.LastClearOutcome);
        Assert.False(File.Exists(path));

        // 3. panic + re-arm (the live Stop path)
        provider.Reset(); // the slow operation is genuinely in flight
        var inFlight = pipeline.RunInteractiveAsync(new AiRequest("slow"));
        await pipeline.PanicAsync(TimeSpan.FromSeconds(2));
        pipeline.SelectProvider(AiProviderId.LocalOllama); // re-arm
        var panicResult = await inFlight;
        Assert.IsType<OperationOutcome.Cancelled>(panicResult.Outcome);
        provider.Release(); // the provider gate serves the next operation too

        // 4. send again (re-armed) → MUST re-persist
        var r2 = await pipeline.RunInteractiveAsync(new AiRequest("after panic"));
        Assert.IsType<OperationOutcome.Completed>(r2.Outcome);
        Assert.NotNull(memory.LastWriteCompletion);
        var writeOutcome = await memory.LastWriteCompletion!;
        Assert.IsType<OperationOutcome.Completed>(writeOutcome);
        Assert.True(File.Exists(path));
        Assert.Equal(2, memory.ReadRecent(10).Count);

        await memory.StopAsync();
    }
}
