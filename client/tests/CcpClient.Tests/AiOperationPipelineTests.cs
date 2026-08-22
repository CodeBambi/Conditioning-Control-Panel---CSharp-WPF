using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Pipeline mechanics tests (slice c1; ai-companion-admission.md §8 c1;
/// ai-operation-contract.md §§2-4, 11). Proves: owned operations, switch = generation
/// invalidation + cancel + stale discard (cooperative AND uncooperative providers),
/// selection ≠ availability (typed Unavailable + capability state), endpoint
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

        /// <summary>Deterministic first-call signal (class-1 conversion): set at
        /// CompleteAsync entry so in-flight waits need no wall-clock poll.</summary>
        public TaskCompletionSource FirstCall { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<CancellationToken, Task<CapabilityState>>? Probe { get; init; } =
            _ => Task.FromResult<CapabilityState>(new CapabilityState.Available("fake-probe"));

        public async Task<AiReply> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            FirstCall.TrySetResult();
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
            Pipeline = new AiOperationPipeline(Registry, Capabilities, LoopbackOnlyAdmissionPolicy.Instance, Diagnostics, new AiModerationBoundary());
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
        // Probe registered but NOT run: registration/selection never yield availability.

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

    // ---- endpoint admission: remote rejected before any socket (named item 7 shape) ----

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
        await TestWait.Until(provider.FirstCall.Task, "the in-flight operation reaching the provider", () => $"calls={provider.Calls}");
        Assert.Equal(1, provider.Calls); // exactly one send in flight (the earlier poll waited for Calls == 1 — restored after the pre-completion consult caught the >= 1 drift)

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
        await TestWait.Until(provider.FirstCall.Task, "the in-flight operation reaching the provider", () => $"calls={provider.Calls}");
        Assert.Equal(1, provider.Calls); // exactly one send in flight (the earlier poll waited for Calls == 1 — restored after the pre-completion consult caught the >= 1 drift)

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
        await TestWait.Until(provider.FirstCall.Task, "the in-flight operation reaching the provider", () => $"calls={provider.Calls}");
        Assert.Equal(1, provider.Calls); // exactly one send in flight (the earlier poll waited for Calls == 1 — restored after the pre-completion consult caught the >= 1 drift)

        var started = TestWait.MonotonicNow();
        await h.Pipeline.PanicAsync(TimeSpan.FromSeconds(10));
        var elapsed = TimeSpan.FromMilliseconds(TestWait.MonotonicNow() - started);

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
        await TestWait.Until(provider.FirstCall.Task, "the in-flight operation reaching the provider", () => $"calls={provider.Calls}");
        Assert.Equal(1, provider.Calls); // exactly one send in flight (the earlier poll waited for Calls == 1 — restored after the pre-completion consult caught the >= 1 drift)

        // Panic with a SHORT bound: the uncooperative op is still blocked; drain hits the bound.
        var started = TestWait.MonotonicNow();
        await h.Pipeline.PanicAsync(TimeSpan.FromMilliseconds(200));
        var elapsed = TimeSpan.FromMilliseconds(TestWait.MonotonicNow() - started);
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

        var result = await h.Pipeline.RunAwarenessAsync(Request, AiAwarenessConsent.NotGiven);

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
        // The serialized shape is the schema proof's domain; here: no field holds
        // the prompt text or any free text.
        var json = System.Text.Json.JsonSerializer.Serialize(record);
        Assert.DoesNotContain("prompt-text-never-in-diagnostics", json);
        Assert.Matches("^[a-z-]+$", record.StableCode!);
    }

    // ---- The two halves of the reply-hygiene output union, distinguishable in the ONE
    // channel this seam owns (the diagnostic stable code composed at
    // AiOperationPipeline.cs:427). NOT a moderation surface: no AiModerationSurface is
    // constructed here, both EvaluateOutput calls still pass the same outputSurface, and the
    // closed 6-Wired/5-Reserved inventory is untouched (record.md §1).

    private const string BlockCategory = "test-block-category";

    /// <summary>
    /// Both block tokens sit in ONE Block rule so the raw/hygienic split is the only variable;
    /// the SoftHit rule exists solely to drive the soft-hit precedence fact.
    /// </summary>
    private static readonly AiModerationPolicy ModerationPolicy = new(
    [
        new AiModerationRule(BlockCategory, AiModerationAction.Block, ["sensitive-token", "forbidden-token"]),
        new AiModerationRule("test-soft-category", AiModerationAction.SoftHit, ["soft-token"]),
    ]);

    /// <summary>The canonical HYGIENIC-ONLY shape: the raw text carries no policy token (the
    /// tag splits it), H1 removes the reasoning block, the token joins, and only the hygienic
    /// scan can see it.</summary>
    private const string HygienicOnlyBlockText = "sensi<thinking>scratch</thinking>tive-token here";

    /// <summary>Hygiene is byte-identical on this text, so the RAW scan blocks and returns
    /// before the :360 guard is ever evaluated.</summary>
    private const string RawBlockText = "model said forbidden-token";

    /// <summary>The reverse direction (the BLOCK-MORE shape): the token is INSIDE a
    /// stripped reasoning block, so the hygienic text is "hello" and passes. Only the raw scan
    /// can refuse this, which is why deleting it would ADMIT text refused today.</summary>
    private const string RawOnlyBlockText = "<thinking>sensitive-token</thinking> hello";

    /// <summary>Raw SOFT-hits, and hygiene joins a split BLOCK token so the hygienic half
    /// blocks: the interleaving that pins the ?? chain order.</summary>
    private const string SoftThenHygienicBlockText = "soft-token and forbi<thinking>x</thinking>dden-token";

    private sealed class TextProvider : IAiProvider
    {
        public AiReply Reply { get; set; } = new AiReply.Generated("clean reply", AiEndpointClass.Loopback);

        public AiProviderDescriptor Descriptor { get; } =
            new(AiProviderId.LocalOllama, AiEndpointClass.Loopback);

        public Func<CancellationToken, Task<CapabilityState>>? Probe { get; } =
            _ => Task.FromResult<CapabilityState>(new CapabilityState.Available("text-probe"));

        /// <summary>Synchronous by construction: no wall-clock wait, no gate to release.</summary>
        public Task<AiReply> CompleteAsync(AiRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Reply);
    }

    private sealed class ModeratedHarness
    {
        public OperationRegistry Registry { get; } = new();
        public CapabilityRegistry Capabilities { get; } = new();
        public CollectingAiDiagnosticsSink Diagnostics { get; } = new();
        public TextProvider Provider { get; } = new();
        public AiOperationPipeline Pipeline { get; }

        public ModeratedHarness(AiModerationPolicy policy)
        {
            Pipeline = new AiOperationPipeline(
                Registry, Capabilities, LoopbackOnlyAdmissionPolicy.Instance, Diagnostics, new AiModerationBoundary(policy));
        }

        public async Task AdmitAsync()
        {
            Pipeline.RegisterProvider(Provider);
            Pipeline.SelectProvider(AiProviderId.LocalOllama);
            var runner = new CapabilityProbeRunner(Registry.OwnerFor("probes"), Capabilities);
            await runner.RunAllAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task HygienicOnlyOutputBlock_Interactive_EmitsTheHygienicHalfsOwnRefusalCode()
    {
        var h = new ModeratedHarness(ModerationPolicy);
        await h.AdmitAsync();
        h.Provider.Reply = new AiReply.Generated(HygienicOnlyBlockText, AiEndpointClass.Loopback);

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean input"));

        var refused = Assert.IsType<AiReply.Refused>(result.Reply);
        Assert.Equal(AiModerationSource.Output, refused.Refusal.Source);
        var record = Assert.Single(h.Diagnostics.Records, r => r.Outcome == AiDiagnosticOutcome.Refused);
        Assert.Equal("refused:output-hygienic", record.StableCode);
        Assert.DoesNotContain(h.Diagnostics.Records, r => r.StableCode == "refused:output");
        // Content-free (contract §12) on a path AiModerationCoverageTests.cs:332-334 does not
        // exercise: this record is the only one this mechanism writes. Ridden here rather
        // than standing alone, because alone it would pass with the mechanism reverted.
        var serialized = System.Text.Json.JsonSerializer.Serialize(record);
        Assert.DoesNotContain(BlockCategory, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-token", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(HygienicOnlyBlockText, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RawOutputBlock_Interactive_KeepsTheUnsuffixedRefusedOutputCode()
    {
        var h = new ModeratedHarness(ModerationPolicy);
        await h.AdmitAsync();
        h.Provider.Reply = new AiReply.Generated(RawBlockText, AiEndpointClass.Loopback);

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean input"));

        var refused = Assert.IsType<AiReply.Refused>(result.Reply);
        Assert.Equal(AiModerationSource.Output, refused.Refusal.Source);
        var record = Assert.Single(h.Diagnostics.Records, r => r.Outcome == AiDiagnosticOutcome.Refused);
        Assert.Equal("refused:output", record.StableCode);
        Assert.DoesNotContain(h.Diagnostics.Records, r => r.StableCode == "refused:output-hygienic");
    }

    [Fact]
    public async Task RawOnlyToken_StrippedByHygiene_StillRefuses_TheUnionBlocksMore()
    {
        var h = new ModeratedHarness(ModerationPolicy);
        await h.AdmitAsync();
        h.Provider.Reply = new AiReply.Generated(RawOnlyBlockText, AiEndpointClass.Loopback);

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean input"));

        // The hygienic text is "hello" and passes. Deleting the raw scan ADMITS this reply,
        // which is the union invariant this packet inherits: the union may only refuse MORE.
        var refused = Assert.IsType<AiReply.Refused>(result.Reply);
        Assert.Equal(AiModerationSource.Output, refused.Refusal.Source);
        var record = Assert.Single(h.Diagnostics.Records, r => r.Outcome == AiDiagnosticOutcome.Refused);
        Assert.Equal("refused:output", record.StableCode);
    }

    [Fact]
    public async Task HygienicOnlyOutputBlock_Awareness_EmitsTheHygienicHalfsOwnRefusalCode()
    {
        var h = new ModeratedHarness(ModerationPolicy);
        await h.AdmitAsync();
        h.Provider.Reply = new AiReply.Generated(HygienicOnlyBlockText, AiEndpointClass.Loopback);

        var result = await h.Pipeline.RunAwarenessAsync(new AiRequest("clean context"), AiAwarenessConsent.Given);

        var refused = Assert.IsType<AiReply.Refused>(result.Reply);
        Assert.Equal(AiModerationSource.Output, refused.Refusal.Source);
        var record = Assert.Single(h.Diagnostics.Records, r => r.Outcome == AiDiagnosticOutcome.Refused);
        Assert.Equal(AiOperationClass.Awareness, record.OperationClass);
        Assert.Equal("refused:output-hygienic", record.StableCode);
    }

    [Fact]
    public async Task RawOutputBlock_Awareness_KeepsTheUnsuffixedRefusedOutputCode()
    {
        var h = new ModeratedHarness(ModerationPolicy);
        await h.AdmitAsync();
        h.Provider.Reply = new AiReply.Generated(RawBlockText, AiEndpointClass.Loopback);

        var result = await h.Pipeline.RunAwarenessAsync(new AiRequest("clean context"), AiAwarenessConsent.Given);

        var refused = Assert.IsType<AiReply.Refused>(result.Reply);
        Assert.Equal(AiModerationSource.Output, refused.Refusal.Source);
        var record = Assert.Single(h.Diagnostics.Records, r => r.Outcome == AiDiagnosticOutcome.Refused);
        Assert.Equal(AiOperationClass.Awareness, record.OperationClass);
        Assert.Equal("refused:output", record.StableCode);
    }

    [Fact]
    public async Task RawSoftHitThenHygienicBlock_SoftHitCodeKeepsFirstPlaceInTheChain()
    {
        var h = new ModeratedHarness(ModerationPolicy);
        await h.AdmitAsync();
        h.Provider.Reply = new AiReply.Generated(SoftThenHygienicBlockText, AiEndpointClass.Loopback);

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean input"));

        // The honest limit on this packet's claim, made mechanical (record.md §6 item 1):
        // when the same operation ALSO soft-hits, the soft-hit code still masks the refusal, so
        // the two halves are NOT distinguishable on this path. Pre-existing and deliberately
        // preserved; this fact is what stops a future reorder of the ?? chain from being silent.
        var refused = Assert.IsType<AiReply.Refused>(result.Reply);
        Assert.Equal(AiModerationSource.Output, refused.Refusal.Source);
        var record = Assert.Single(h.Diagnostics.Records, r => r.Outcome == AiDiagnosticOutcome.Refused);
        Assert.Equal("soft-hit:output", record.StableCode);
    }
}
