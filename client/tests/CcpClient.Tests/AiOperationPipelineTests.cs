using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Pipeline mechanics tests (SP-033 slice c1; ai-companion-admission.md §8 c1;
/// ai-operation-contract.md §§2-4, 11). Proves: owned operations, switch = generation
/// invalidation + cancel + stale discard (cooperative AND uncooperative providers),
/// selection ≠ availability (typed Unavailable + SP-006 capability state), endpoint
/// admission pre-socket (send-attempt counter zero), awareness consent suppression,
/// panic (typed Cancelled + bounded drain + post-panic stale discard), and content-free
/// diagnostics emission.
/// </summary>
public class AiOperationPipelineTests
{
    private static readonly AiRequest Request = new("prompt-text-never-in-diagnostics");

    private sealed class FakeProvider : IAiProvider
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeProvider(AiProviderDescriptor descriptor, bool cooperative)
        {
            Descriptor = descriptor;
            Cooperative = cooperative;
        }

        public AiProviderDescriptor Descriptor { get; }

        public bool Cooperative { get; }

        public int Calls;

        public Func<CancellationToken, Task<CapabilityState>>? Probe { get; init; } =
            _ => Task.FromResult<CapabilityState>(new CapabilityState.Available("fake-probe"));

        public async Task<AiReply> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            if (Cooperative)
            {
                // Honors the token: blocks until cancelled, then throws OCE.
                await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new AiReply.Generated("cooperative-reply", Descriptor.EndpointClass);
            }

            // Uncooperative: ignores the token, returns a LATE reply when released.
            await _gate.Task.ConfigureAwait(false);
            return new AiReply.Generated("late-reply", Descriptor.EndpointClass);
        }

        public void Release() => _gate.TrySetResult();
    }

    private sealed class Harness
    {
        public OperationRegistry Registry { get; } = new();
        public CapabilityRegistry Capabilities { get; } = new();
        public CollectingAiDiagnosticsSink Diagnostics { get; } = new();
        public AiOperationPipeline Pipeline { get; }

        public Harness()
        {
            Pipeline = new AiOperationPipeline(Registry, Capabilities, LoopbackOnlyAdmissionPolicy.Instance, Diagnostics);
        }

        public FakeProvider RegisterLoopback(AiProviderId? id = null)
        {
            var provider = new FakeProvider(
                new AiProviderDescriptor(id ?? AiProviderId.LocalOllama, AiEndpointClass.Loopback), cooperative: true);
            Pipeline.RegisterProvider(provider);
            return provider;
        }

        public async Task RunProbesAsync()
        {
            var runner = new CapabilityProbeRunner(Registry.OwnerFor("probes"), Capabilities);
            await runner.RunAllAsync(CancellationToken.None);
        }
    }

    // ---- selection ≠ availability (contract §3 rule 3; admission §2 rule 3) ----

    [Fact]
    public async Task NoSelection_YieldsUnavailable_NotConfigured_ZeroSendAttempts()
    {
        var h = new Harness();
        var result = await h.Pipeline.RunInteractiveAsync(Request);

        Assert.IsType<OperationOutcome.Completed>(result.Outcome);
        var reply = Assert.IsType<AiReply.Unavailable>(result.Reply);
        Assert.Equal(AiReplyCodes.NotConfigured, reply.Code);
        Assert.Equal(0, h.Pipeline.SendAttempts);
        Assert.Contains(h.Diagnostics.Records, r => r.Outcome == AiDiagnosticOutcome.Unavailable && r.StableCode == AiReplyCodes.NotConfigured);
    }

    [Fact]
    public async Task SelectedButUnproven_YieldsUnavailable_ProviderUnproven_ZeroSendAttempts()
    {
        var h = new Harness();
        var provider = h.RegisterLoopback();
        h.Pipeline.SelectProvider(AiProviderId.LocalOllama);
        // Probe registered but NOT run: registration/selection never yield availability (SP-006).

        var result = await h.Pipeline.RunInteractiveAsync(Request);

        var reply = Assert.IsType<AiReply.Unavailable>(result.Reply);
        Assert.Equal(AiReplyCodes.ProviderUnproven, reply.Code);
        Assert.Equal(0, h.Pipeline.SendAttempts);
        Assert.Equal(0, provider.Calls);
        var state = h.Capabilities.GetState(AiOperationPipeline.CapabilityName(AiProviderId.LocalOllama));
        var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);
        Assert.Equal(CapabilityReasonCodes.NotProbed, unavailable.Reason.Code);
    }

    [Fact]
    public async Task CloudAbsence_TypedProof_SelectedCloudIsUnavailableWithCapabilityState()
    {
        var h = new Harness();
        // Inventory, not admission (contract §6 rule 4): descriptor exists, NO implementation,
        // credentials-absent probe — no credentials exist and none are invented (admission §2 rule 6).
        h.Pipeline.RegisterDescriptor(
            new AiProviderDescriptor(AiProviderId.Cloud, AiEndpointClass.FirstPartyCloud),
            new CapabilityReason(CapabilityReasonCodes.CredentialsAbsent, "no cloud credentials exist on this box"));
        h.Pipeline.SelectProvider(AiProviderId.Cloud);
        await h.RunProbesAsync();

        var state = h.Capabilities.GetState(AiOperationPipeline.CapabilityName(AiProviderId.Cloud));
        var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);
        Assert.Equal(CapabilityReasonCodes.CredentialsAbsent, unavailable.Reason.Code);

        var result = await h.Pipeline.RunInteractiveAsync(Request);
        var reply = Assert.IsType<AiReply.Unavailable>(result.Reply);
        // Capability is checked BEFORE the admission policy (record.md §4.1): the typed
        // absence surfaces as provider-unproven even though FirstPartyCloud is also
        // non-loopback under the placeholder policy.
        Assert.Equal(AiReplyCodes.ProviderUnproven, reply.Code);
        Assert.Equal(0, h.Pipeline.SendAttempts);
    }

    // ---- endpoint admission: remote rejected before any socket (SP-019 item 7 shape) ----

    [Fact]
    public async Task ProvenRemoteProvider_RejectedByPolicy_BeforeSocket_ZeroSendAttempts()
    {
        var h = new Harness();
        // A PROVEN but non-loopback provider (remote Ollama is remote — the rejected
        // "local AI = local-only data" assumption). The loopback-only placeholder policy
        // rejects it pre-socket even though its capability probe says Available.
        var provider = new FakeProvider(
            new AiProviderDescriptor(AiProviderId.LocalOllama, AiEndpointClass.RemoteHostOllama), cooperative: true);
        h.Pipeline.RegisterProvider(provider);
        h.Pipeline.SelectProvider(AiProviderId.LocalOllama);
        await h.RunProbesAsync();

        var result = await h.Pipeline.RunInteractiveAsync(Request);

        var reply = Assert.IsType<AiReply.Unavailable>(result.Reply);
        Assert.Equal(AiReplyCodes.EndpointNotAdmitted, reply.Code);
        Assert.Equal(0, h.Pipeline.SendAttempts);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task FirstPartyCloud_ProvenHypothetical_RejectedByPlaceholderPolicy()
    {
        var h = new Harness();
        // Even IF a cloud implementation+probe existed, the loopback-only placeholder
        // rejects FirstPartyCloud pre-socket (allow-list governance owner-pending §9.2 #2).
        var provider = new FakeProvider(
            new AiProviderDescriptor(AiProviderId.Cloud, AiEndpointClass.FirstPartyCloud), cooperative: true);
        h.Pipeline.RegisterProvider(provider);
        h.Pipeline.SelectProvider(AiProviderId.Cloud);
        await h.RunProbesAsync();

        var result = await h.Pipeline.RunInteractiveAsync(Request);
        Assert.Equal(AiReplyCodes.EndpointNotAdmitted, Assert.IsType<AiReply.Unavailable>(result.Reply).Code);
        Assert.Equal(0, h.Pipeline.SendAttempts);
    }

    // ---- switch = generation invalidation + cancel + stale discard (contract §3 rule 2) ----

    [Fact]
    public async Task Switch_CancelsInFlight_CooperativeProvider_TypedCancelled()
    {
        var h = new Harness();
        var provider = h.RegisterLoopback();
        h.Pipeline.SelectProvider(AiProviderId.LocalOllama);
        await h.RunProbesAsync();

        var inFlight = h.Pipeline.RunInteractiveAsync(Request);
        await WaitForAsync(() => provider.Calls == 1);

        h.Pipeline.SelectProvider(AiProviderId.Cloud); // the switch IS the cancellation

        var result = await inFlight;
        Assert.IsType<OperationOutcome.Cancelled>(result.Outcome);
        Assert.Null(result.Reply); // a reply under A can never surface under B
    }

    [Fact]
    public async Task Switch_StaleDiscard_UncooperativeProvider_LateReplyNeverApplied()
    {
        var h = new Harness();
        var provider = new FakeProvider(
            new AiProviderDescriptor(AiProviderId.LocalOllama, AiEndpointClass.Loopback), cooperative: false);
        h.Pipeline.RegisterProvider(provider);
        h.Pipeline.SelectProvider(AiProviderId.LocalOllama);
        await h.RunProbesAsync();

        var inFlight = h.Pipeline.RunInteractiveAsync(Request);
        await WaitForAsync(() => provider.Calls == 1);

        h.Pipeline.SelectProvider(AiProviderId.Cloud);
        provider.Release(); // the LATE reply arrives after the switch

        var result = await inFlight;
        Assert.IsType<OperationOutcome.Cancelled>(result.Outcome);
        Assert.Null(result.Reply);
        Assert.True(h.Registry.DiscardedStaleCompletions >= 1, "stale completion must be discarded at the point of application");
        Assert.DoesNotContain(h.Diagnostics.Records, r => r.Outcome == AiDiagnosticOutcome.Completed);
    }

    // ---- panic at pipeline level (contract §2 rule 3; admission §7) ----

    [Fact]
    public async Task Panic_CancelsInFlight_TypedCancelled_BoundedDrain()
    {
        var h = new Harness();
        var provider = h.RegisterLoopback();
        h.Pipeline.SelectProvider(AiProviderId.LocalOllama);
        await h.RunProbesAsync();

        var inFlight = h.Pipeline.RunInteractiveAsync(Request);
        await WaitForAsync(() => provider.Calls == 1);

        var started = DateTime.UtcNow;
        await h.Pipeline.PanicAsync(TimeSpan.FromSeconds(10));
        var elapsed = DateTime.UtcNow - started;

        Assert.True(elapsed < TimeSpan.FromSeconds(5), $"drain must be bounded by cancellation, not the wait bound: {elapsed}");
        var result = await inFlight;
        Assert.IsType<OperationOutcome.Cancelled>(result.Outcome);
        Assert.Null(result.Reply);
    }

    [Fact]
    public async Task Panic_UncooperativeProvider_LateReplyDiscarded_AndPostPanicOpsCancel()
    {
        var h = new Harness();
        var provider = new FakeProvider(
            new AiProviderDescriptor(AiProviderId.LocalOllama, AiEndpointClass.Loopback), cooperative: false);
        h.Pipeline.RegisterProvider(provider);
        h.Pipeline.SelectProvider(AiProviderId.LocalOllama);
        await h.RunProbesAsync();

        var inFlight = h.Pipeline.RunInteractiveAsync(Request);
        await WaitForAsync(() => provider.Calls == 1);

        // Panic with a SHORT bound: the uncooperative op is still blocked; drain hits the bound.
        var started = DateTime.UtcNow;
        await h.Pipeline.PanicAsync(TimeSpan.FromMilliseconds(200));
        var elapsed = DateTime.UtcNow - started;
        Assert.True(elapsed < TimeSpan.FromSeconds(5), $"bounded drain must not hang: {elapsed}");

        provider.Release(); // late reply AFTER panic: IsLive is false (cancelled) → discarded
        var result = await inFlight;
        Assert.IsType<OperationOutcome.Cancelled>(result.Outcome);
        Assert.Null(result.Reply);

        // Post-panic semantics (record.md §4.1): operations terminate Cancelled until the
        // next SelectProvider begins a new generation.
        var postPanic = await h.Pipeline.RunInteractiveAsync(Request);
        Assert.IsType<OperationOutcome.Cancelled>(postPanic.Outcome);

        // Recovery: a new selection re-arms the generation. The fake's gate already fired,
        // so it answers immediately under the new (live) generation.
        h.Pipeline.SelectProvider(AiProviderId.LocalOllama);
        var recovered = await h.Pipeline.RunInteractiveAsync(Request);
        Assert.IsType<OperationOutcome.Completed>(recovered.Outcome);
        Assert.IsType<AiReply.Generated>(recovered.Reply);
    }

    // ---- awareness consent: code-enforced at admission (contract §4 rule 1) ----

    [Fact]
    public async Task Awareness_WithoutConsent_Suppressed_TypedObservable_NeverSilent()
    {
        var h = new Harness();
        h.RegisterLoopback();
        h.Pipeline.SelectProvider(AiProviderId.LocalOllama);
        await h.RunProbesAsync();

        var result = await h.Pipeline.RunAwarenessAsync(Request, awarenessConsent: false);

        Assert.IsType<OperationOutcome.Completed>(result.Outcome); // suppressed ≠ failed
        var admission = Assert.IsType<AiAdmission.Suppressed>(result.Admission);
        Assert.Equal(AiSuppressionKind.ConsentDenied, admission.Kind);
        Assert.Null(result.Reply);
        Assert.Equal(0, h.Pipeline.SendAttempts);
        Assert.Contains(h.Diagnostics.Records, r => r.OperationClass == AiOperationClass.Awareness);
    }

    // ---- happy path + diagnostics content shape ----

    [Fact]
    public async Task ProvenLoopback_GeneratesReply_WithProvenance_AndContentFreeDiagnostic()
    {
        var h = new Harness();
        var provider = h.RegisterLoopback();
        h.Pipeline.SelectProvider(AiProviderId.LocalOllama);
        await h.RunProbesAsync();
        provider.Release();

        var result = await h.Pipeline.RunInteractiveAsync(Request);

        Assert.IsType<OperationOutcome.Completed>(result.Outcome);
        var reply = Assert.IsType<AiReply.Generated>(result.Reply);
        Assert.Equal(AiEndpointClass.Loopback, reply.Provenance);
        Assert.Equal(1, h.Pipeline.SendAttempts);
        var record = Assert.Single(h.Diagnostics.Records, r => r.Outcome == AiDiagnosticOutcome.Completed);
        Assert.Equal(AiOperationClass.Interactive, record.OperationClass);
        Assert.Equal(AiEndpointClass.Loopback, record.EndpointClass);
        Assert.Null(record.StableCode);
    }

    [Fact]
    public async Task Diagnostics_NeverCarryText_StableCodesOnly()
    {
        var h = new Harness();
        await h.Pipeline.RunInteractiveAsync(Request); // not-configured path

        var record = Assert.Single(h.Diagnostics.Records);
        // The serialized shape is the SP-016 schema proof's domain; here: no field holds
        // the prompt text or any free text.
        var json = System.Text.Json.JsonSerializer.Serialize(record);
        Assert.DoesNotContain("prompt-text-never-in-diagnostics", json);
        Assert.Matches("^[a-z-]+$", record.StableCode!);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("condition not met within 2s");
    }
}
