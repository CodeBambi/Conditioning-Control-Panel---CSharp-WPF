using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Pipeline wiring tests for the moderation boundary (SP-038 slice c3; contract §7;
/// admission §3). Proves the boundary is LIVE in the real pipeline: input at admission
/// (post provider-admission, pre-send — zero network on blocked paths), output inside the
/// owned operation before application, typed refusal surfacing per operation class
/// (interactive surfaces the typed refusal + escalates; awareness produces the same typed
/// refusal WITHOUT escalation — dropped by type downstream, contract §4 rule 3), and the
/// escalation cooldown consulted at interactive admission (typed, never silently allowed).
/// </summary>
public class AiModerationPipelineBoundaryTests
{
    private const string Forbidden = "forbidden-token";

    private sealed class StubProvider : IAiProvider
    {
        public StubProvider(AiReply reply) => Reply = reply;

        public AiReply Reply { get; set; }

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

    private sealed class Harness
    {
        public OperationRegistry Registry { get; } = new();
        public CapabilityRegistry Capabilities { get; } = new();
        public CollectingAiDiagnosticsSink Diagnostics { get; } = new();
        public AiModerationEscalation Escalation { get; } = new();
        public AiModerationBoundary Boundary { get; }
        public AiOperationPipeline Pipeline { get; }
        public StubProvider Provider { get; } = new(new AiReply.Generated("clean reply", AiEndpointClass.Loopback));

        public Harness(AiModerationPolicy policy)
        {
            Boundary = new AiModerationBoundary(policy, Escalation);
            Pipeline = new AiOperationPipeline(Registry, Capabilities, LoopbackOnlyAdmissionPolicy.Instance, Diagnostics, Boundary);
        }

        public async Task AdmitProviderAsync()
        {
            Pipeline.RegisterProvider(Provider);
            Pipeline.SelectProvider(AiProviderId.LocalOllama);
            var runner = new CapabilityProbeRunner(Registry.OwnerFor("probes"), Capabilities);
            await runner.RunAllAsync(CancellationToken.None);
        }
    }

    private static readonly AiModerationPolicy Policy = new(
    [
        new AiModerationRule("test-block-category", AiModerationAction.Block, [Forbidden]),
        new AiModerationRule("test-soft-category", AiModerationAction.SoftHit, ["sensitive-token"]),
    ]);

    // ---- input boundary: interactive surfaces typed refusal + escalates ----

    [Fact]
    public async Task Interactive_InputBlock_TypedRefusal_ZeroNetwork_EscalationHit()
    {
        var h = new Harness(Policy);
        await h.AdmitProviderAsync();

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest($"user typed {Forbidden}"));

        Assert.IsType<OperationOutcome.Completed>(result.Outcome);
        var refused = Assert.IsType<AiReply.Refused>(result.Reply);
        Assert.Equal("test-block-category", refused.Refusal.CategoryCode);
        Assert.Equal(AiModerationSource.Input, refused.Refusal.Source);
        Assert.Equal(0, h.Pipeline.SendAttempts);
        Assert.Equal(0, h.Provider.Calls);
        Assert.Equal(1, h.Escalation.GetState().HitsInWindow);
        var diagnostic = Assert.Single(h.Diagnostics.Records);
        Assert.Equal(AiDiagnosticOutcome.Refused, diagnostic.Outcome);
        Assert.Equal("refused:input", diagnostic.StableCode);
    }

    // ---- input boundary: awareness drops by type — same typed refusal, NO escalation ----

    [Fact]
    public async Task Awareness_InputBlock_TypedRefusal_NoEscalation_ZeroNetwork()
    {
        var h = new Harness(Policy);
        await h.AdmitProviderAsync();

        var result = await h.Pipeline.RunAwarenessAsync(new AiRequest($"context carries {Forbidden}"), awarenessConsent: true);

        // The typed refusal exists (the consumer drops it BY TYPE — contract §4 rule 3);
        // it is never an escalation hit (WPF: only user-typed content escalates).
        var refused = Assert.IsType<AiReply.Refused>(result.Reply);
        Assert.Equal(AiModerationSource.Input, refused.Refusal.Source);
        Assert.Equal(0, h.Escalation.GetState().HitsInWindow);
        Assert.Equal(0, h.Pipeline.SendAttempts);
        Assert.Equal(0, h.Provider.Calls);
    }

    // ---- output boundary: before application, both classes, never escalates ----

    [Fact]
    public async Task Interactive_OutputBlock_TypedRefusal_NoEscalation()
    {
        var h = new Harness(Policy);
        await h.AdmitProviderAsync();
        h.Provider.Reply = new AiReply.Generated($"model said {Forbidden}", AiEndpointClass.Loopback);

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean input"));

        var refused = Assert.IsType<AiReply.Refused>(result.Reply);
        Assert.Equal("test-block-category", refused.Refusal.CategoryCode);
        Assert.Equal(AiModerationSource.Output, refused.Refusal.Source);
        Assert.Equal(1, h.Pipeline.SendAttempts); // the request went out; the OUTPUT was blocked
        Assert.Equal(0, h.Escalation.GetState().HitsInWindow); // output is never the user's doing (WPF)
        Assert.Contains(h.Diagnostics.Records, r => r.Outcome == AiDiagnosticOutcome.Refused && r.StableCode == "refused:output");
    }

    [Fact]
    public async Task Awareness_OutputBlock_TypedRefusal_DroppedByType()
    {
        var h = new Harness(Policy);
        await h.AdmitProviderAsync();
        h.Provider.Reply = new AiReply.Generated($"model said {Forbidden}", AiEndpointClass.Loopback);

        var result = await h.Pipeline.RunAwarenessAsync(new AiRequest("clean context"), awarenessConsent: true);

        var refused = Assert.IsType<AiReply.Refused>(result.Reply);
        Assert.Equal(AiModerationSource.Output, refused.Refusal.Source);
        Assert.Equal(0, h.Escalation.GetState().HitsInWindow);
    }

    // ---- soft-hit: typed pass-through, content-free diagnostic marker ----

    [Fact]
    public async Task SoftHit_Input_PassesThrough_StableCodeDiagnostic()
    {
        var h = new Harness(Policy);
        await h.AdmitProviderAsync();

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("a sensitive-token question"));

        Assert.IsType<AiReply.Generated>(result.Reply);
        Assert.Equal(1, h.Provider.Calls);
        Assert.Contains(h.Diagnostics.Records, r => r.Outcome == AiDiagnosticOutcome.Completed && r.StableCode == "soft-hit:input");
    }

    [Fact]
    public async Task SoftHit_Output_PassesThrough_StableCodeDiagnostic()
    {
        var h = new Harness(Policy);
        await h.AdmitProviderAsync();
        h.Provider.Reply = new AiReply.Generated("a sensitive-token reply", AiEndpointClass.Loopback);

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean"));

        var generated = Assert.IsType<AiReply.Generated>(result.Reply);
        Assert.Equal("a sensitive-token reply", generated.Text);
        Assert.Contains(h.Diagnostics.Records, r => r.Outcome == AiDiagnosticOutcome.Completed && r.StableCode == "soft-hit:output");
    }

    // ---- escalation cooldown consulted at interactive admission (typed, never silent) ----

    [Fact]
    public async Task EscalationCooldown_InteractiveAdmissionDenied_Typed_AwarenessNotConsulted()
    {
        var h = new Harness(Policy);
        await h.AdmitProviderAsync();
        var thresholds = AiEscalationThresholds.WpfBaselinePlaceholder; // 5-hit cooldown

        // Drive the counter to cooldown through REAL interactive input blocks.
        for (var i = 0; i < thresholds.CooldownAt; i++)
        {
            await h.Pipeline.RunInteractiveAsync(new AiRequest(Forbidden));
        }

        Assert.True(h.Escalation.GetState().CooldownActive);

        // Interactive admission is now TYPED-denied, never silently allowed.
        var cooling = await h.Pipeline.RunInteractiveAsync(new AiRequest("perfectly clean input"));
        var unavailable = Assert.IsType<AiReply.Unavailable>(cooling.Reply);
        Assert.Equal(AiReplyCodes.ModerationCooldown, unavailable.Code);
        Assert.Equal(0, h.Provider.Calls); // never reached the provider

        // Awareness is NOT consulted (WPF baseline: user chat input only; extension is an
        // owner-pending VALUES question) — a clean awareness operation still runs.
        var awareness = await h.Pipeline.RunAwarenessAsync(new AiRequest("clean context"), awarenessConsent: true);
        Assert.IsType<AiReply.Generated>(awareness.Reply);
        Assert.Equal(1, h.Provider.Calls);
    }

    // ---- one-hit-per-event accounting (H7 lesson: the boundary is uniform) ----

    [Fact]
    public async Task Escalation_ExactlyOneHitPerBlockedEvent()
    {
        var h = new Harness(Policy);
        await h.AdmitProviderAsync();

        await h.Pipeline.RunInteractiveAsync(new AiRequest($"{Forbidden} and {Forbidden} again"));

        // Two tokens in one message = ONE blocked event = ONE hit (the H7 double-count
        // lesson: no layered pre-checks, one boundary at the operation edge).
        Assert.Equal(1, h.Escalation.GetState().HitsInWindow);
    }
}
