using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Offline zero-network integration proof (SP-033 slice c1; contract §11; admission §2
/// rule 8 / §6 rule 3): with NO provider proven available, the interactive, awareness,
/// and command-validation paths perform ZERO outbound send attempts — the SP-019
/// send-attempt-counter discipline as a product test. Loopback and cloud classes degrade
/// independently (§11 rule 4).
/// </summary>
public class AiOfflineIntegrationTests
{
    private static readonly AiRequest Request = new("offline-proof-prompt");

    private sealed class LoopbackFake : IAiProvider
    {
        public AiProviderDescriptor Descriptor { get; } =
            new(AiProviderId.LocalOllama, AiEndpointClass.Loopback);

        public Func<CancellationToken, Task<CapabilityState>>? Probe { get; } =
            _ => Task.FromResult<CapabilityState>(new CapabilityState.Available("fake-loopback"));

        public Task<AiReply> CompleteAsync(AiRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<AiReply>(new AiReply.Generated("loopback-reply", AiEndpointClass.Loopback));
    }

    [Fact]
    public async Task NoProvenProvider_InteractiveAwarenessCommandPaths_ZeroOutboundAttempts()
    {
        var registry = new OperationRegistry();
        var capabilities = new CapabilityRegistry();
        var diagnostics = new CollectingAiDiagnosticsSink();
        var pipeline = new AiOperationPipeline(registry, capabilities, LoopbackOnlyAdmissionPolicy.Instance, diagnostics, new AiModerationBoundary());

        // Cloud inventory registered (typed absence); loopback implementation registered
        // but its probe NEVER runs — nothing is proven. This is the offline posture.
        pipeline.RegisterDescriptor(
            new AiProviderDescriptor(AiProviderId.Cloud, AiEndpointClass.FirstPartyCloud),
            new CapabilityReason(CapabilityReasonCodes.CredentialsAbsent, "no cloud credentials exist"));
        pipeline.RegisterProvider(new LoopbackFake());

        // Interactive path, both selections.
        pipeline.SelectProvider(AiProviderId.Cloud);
        var interactiveCloud = await pipeline.RunInteractiveAsync(Request);
        pipeline.SelectProvider(AiProviderId.LocalOllama);
        var interactiveLocal = await pipeline.RunInteractiveAsync(Request);

        // Awareness path (consent granted — admission reaches the provider checks).
        var awareness = await pipeline.RunAwarenessAsync(Request, AiAwarenessConsent.Given);

        // Command path (c1: strict validation is pure — no execution pipeline exists until c6).
        var envelope = AiEnvelopeValidator.Validate(
            """{"reply":"r","commands":[{"command":"bubbles","data":{"on":true,"frequency":1}}]}""",
            AiEnvelopePolicy.PermitAll);

        // THE PROOF: zero outbound attempts across every path.
        Assert.Equal(0, pipeline.SendAttempts);

        Assert.Equal(AiReplyCodes.ProviderUnproven, Assert.IsType<AiReply.Unavailable>(interactiveCloud.Reply).Code);
        Assert.Equal(AiReplyCodes.ProviderUnproven, Assert.IsType<AiReply.Unavailable>(interactiveLocal.Reply).Code);
        Assert.Equal(AiReplyCodes.ProviderUnproven, Assert.IsType<AiReply.Unavailable>(awareness.Reply).Code);
        Assert.True(envelope.Accepted); // validation is offline-pure by construction
        Assert.All(diagnostics.Records, r => Assert.Equal(AiDiagnosticOutcome.Unavailable, r.Outcome));
    }

    [Fact]
    public async Task LoopbackAndCloud_DegradeIndependently()
    {
        var registry = new OperationRegistry();
        var capabilities = new CapabilityRegistry();
        var pipeline = new AiOperationPipeline(
            registry, capabilities, LoopbackOnlyAdmissionPolicy.Instance, new CollectingAiDiagnosticsSink(), new AiModerationBoundary());

        pipeline.RegisterDescriptor(
            new AiProviderDescriptor(AiProviderId.Cloud, AiEndpointClass.FirstPartyCloud),
            new CapabilityReason(CapabilityReasonCodes.CredentialsAbsent, "no cloud credentials exist"));
        pipeline.RegisterProvider(new LoopbackFake());
        pipeline.SelectProvider(AiProviderId.LocalOllama);

        // Prove ONLY the loopback provider (a cloud outage never blocks loopback — §11 rule 4).
        var runner = new CapabilityProbeRunner(registry.OwnerFor("probes"), capabilities);
        await runner.RunAllAsync(CancellationToken.None);

        var loopback = await pipeline.RunInteractiveAsync(Request);
        Assert.IsType<AiReply.Generated>(loopback.Reply);
        Assert.Equal(1, pipeline.SendAttempts);

        // Switching to the (absent) cloud degrades independently — typed Unavailable,
        // and the loopback class keeps its own state.
        pipeline.SelectProvider(AiProviderId.Cloud);
        var cloud = await pipeline.RunInteractiveAsync(Request);
        Assert.Equal(AiReplyCodes.ProviderUnproven, Assert.IsType<AiReply.Unavailable>(cloud.Reply).Code);
        Assert.Equal(1, pipeline.SendAttempts); // cloud attempt never happened

        pipeline.SelectProvider(AiProviderId.LocalOllama);
        var loopbackAgain = await pipeline.RunInteractiveAsync(Request);
        Assert.IsType<AiReply.Generated>(loopbackAgain.Reply);
        Assert.Equal(2, pipeline.SendAttempts);
    }
}
