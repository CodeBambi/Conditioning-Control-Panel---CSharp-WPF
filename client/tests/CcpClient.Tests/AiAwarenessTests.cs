using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Awareness service tests (SP-042 slice c5; contract §4; admission §5). Proves:
/// code-enforced typed consent at admission (default NOT GIVEN; denied = typed Suppressed,
/// observable, zero network), cooldown-suppressed typed outcomes, context packaging under
/// consent with every field through the c3 input boundary (blocking = zero transmission),
/// keyword routing as OWNED panic-cancellable operations with typed Fallback visibility
/// and drop-by-type, and title-observation gating. Content-free diagnostics throughout —
/// keywords, titles, and context fields NEVER appear in any emitted record.
/// </summary>
public class AiAwarenessTests
{
    private sealed class StubProvider : IAiProvider
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AiProviderDescriptor Descriptor { get; } = new(AiProviderId.LocalOllama, AiEndpointClass.Loopback);

        public AiReply Reply { get; set; } = new AiReply.Generated("generated-line", AiEndpointClass.Loopback);

        public int Calls;

        public Func<CancellationToken, Task<CapabilityState>>? Probe { get; } =
            _ => Task.FromResult<CapabilityState>(new CapabilityState.Available("stub-probe"));

        public async Task<AiReply> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return Reply;
        }

        public void Release() => _gate.TrySetResult();
    }

    private sealed class Harness
    {
        public OperationRegistry Registry { get; } = new();
        public CapabilityRegistry Capabilities { get; } = new();
        public CollectingAiDiagnosticsSink Diagnostics { get; } = new();
        public AiModerationBoundary Boundary { get; }
        public AiCooldownRegistry Cooldowns { get; }
        public AiOperationPipeline Pipeline { get; }
        public AiAwarenessService Service { get; }
        public StubProvider Provider { get; } = new();
        public DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

        public Harness(AiModerationPolicy? policy = null)
        {
            Boundary = new AiModerationBoundary(policy);
            Cooldowns = new AiCooldownRegistry(() => Now);
            Pipeline = new AiOperationPipeline(Registry, Capabilities, LoopbackOnlyAdmissionPolicy.Instance, Diagnostics, Boundary);
            Service = new AiAwarenessService(Pipeline, Boundary, Diagnostics, Capabilities, Cooldowns);
            Service.Consent = AiAwarenessConsent.Given;
        }

        public async Task AdmitProviderAsync()
        {
            Pipeline.RegisterProvider(Provider);
            Pipeline.SelectProvider(AiProviderId.LocalOllama);
            await new CapabilityProbeRunner(Registry.OwnerFor("probes"), Capabilities).RunAllAsync(CancellationToken.None);
            Provider.Release();
        }

        public string AllDiagnosticText() =>
            string.Join('\n', Diagnostics.Records.Select(r =>
                $"{r.OperationClass}|{r.EndpointClass}|{r.Outcome}|{r.StableCode}|{r.Generation}|{r.DurationMilliseconds}|{r.CommandCount}|{string.Join(",", r.CommandVerdictCodes)}"));
    }

    // ---- consent: code-enforced at admission (contract §4 rule 1; admission §5 rule 1) ----

    [Fact]
    public async Task Consent_Default_IsNotGiven_AndNoOperationRuns()
    {
        var h = new Harness();
        h.Service.Consent = AiAwarenessConsent.NotGiven; // re-assert the default explicitly
        await h.AdmitProviderAsync();

        var keyword = await h.Service.RunKeywordCommentAsync("trigger-1", "testword");
        var dropped = Assert.IsType<AiAwarenessRoutingResult.Dropped>(keyword);
        Assert.Equal(AiAwarenessDropKind.ConsentDenied, dropped.Kind);
        var admission = Assert.IsType<AiAdmission.Suppressed>(dropped.Admission);
        Assert.Equal(AiSuppressionKind.ConsentDenied, admission.Kind);

        var reaction = await h.Service.RunReactionAsync(new AiAwarenessContext("cat", "app", "title", "0m"));
        Assert.Equal(AiAwarenessDropKind.ConsentDenied, Assert.IsType<AiAwarenessRoutingResult.Dropped>(reaction).Kind);

        var observation = h.Service.ObserveForegroundTitle();
        Assert.IsType<AiTitleObservation.ConsentNotGiven>(observation);

        // Observable + zero network: typed diagnostics, SendAttempts untouched.
        Assert.Equal(0, h.Pipeline.SendAttempts);
        Assert.Equal(0, h.Provider.Calls);
        Assert.Equal(2, h.Diagnostics.Records.Count(r => r.StableCode == "suppressed:consent-denied" && r.Outcome == AiDiagnosticOutcome.Completed));
        Assert.All(h.Diagnostics.Records, r => Assert.Equal(-1, r.Generation));
    }

    [Fact]
    public async Task Consent_Given_AdmitsTheOwnedOperation()
    {
        var h = new Harness();
        await h.AdmitProviderAsync();

        var result = await h.Service.RunKeywordCommentAsync("trigger-1", "testword");

        var visible = Assert.IsType<AiAwarenessRoutingResult.Visible>(result);
        Assert.IsType<AiReply.Generated>(visible.Reply);
        Assert.Equal(1, h.Pipeline.SendAttempts);
        Assert.Equal(1, h.Provider.Calls);
    }

    [Fact]
    public async Task Pipeline_TypedConsentOverload_EnforcesTheSameAdmission()
    {
        var h = new Harness();
        await h.AdmitProviderAsync();

        var denied = await h.Pipeline.RunAwarenessAsync(new AiRequest("ctx"), AiAwarenessConsent.NotGiven);
        var suppressed = Assert.IsType<AiAdmission.Suppressed>(denied.Admission);
        Assert.Equal(AiSuppressionKind.ConsentDenied, suppressed.Kind);
        Assert.Null(denied.Reply);
        Assert.Equal(0, h.Pipeline.SendAttempts);

        var admitted = await h.Pipeline.RunAwarenessAsync(new AiRequest("ctx"), AiAwarenessConsent.Given);
        Assert.IsType<AiAdmission.Admitted>(admitted.Admission);
        Assert.IsType<AiReply.Generated>(admitted.Reply);
        Assert.Equal(1, h.Pipeline.SendAttempts);
    }

    // ---- cooldown-suppressed outcomes: typed + observable, never silent (contract §4 rule 2) ----

    [Fact]
    public async Task CooldownSuppressed_KeywordComment_TypedOutcome_ZeroNetwork()
    {
        var h = new Harness();
        await h.AdmitProviderAsync();
        h.Cooldowns.Extend(AiCooldownKind.Global, "(global)", TimeSpan.FromSeconds(30));

        var result = await h.Service.RunKeywordCommentAsync("trigger-1", "testword");

        var dropped = Assert.IsType<AiAwarenessRoutingResult.Dropped>(result);
        Assert.Equal(AiAwarenessDropKind.CooldownSuppressed, dropped.Kind);
        var admission = Assert.IsType<AiAdmission.Suppressed>(dropped.Admission);
        Assert.Equal(AiSuppressionKind.Cooldown, admission.Kind);
        Assert.Equal(0, h.Pipeline.SendAttempts);
        Assert.Equal(0, h.Provider.Calls);
        Assert.Contains(h.Diagnostics.Records, r => r.StableCode == "suppressed:cooldown" && r.Outcome == AiDiagnosticOutcome.Completed);
        // The keyword never enters diagnostics (content-free, contract §12).
        Assert.DoesNotContain("testword", h.AllDiagnosticText());
    }

    [Fact]
    public async Task CooldownGates_EachClassSuppresses_AndRecordFireGatesTheNextFire()
    {
        var h = new Harness();
        await h.AdmitProviderAsync();

        // First fire admitted → RecordFire stamps global/per-keyword/loop-protection/per-trigger.
        var first = await h.Service.RunKeywordCommentAsync("trigger-1", "testword", perTriggerCooldown: TimeSpan.FromSeconds(30));
        Assert.IsType<AiAwarenessRoutingResult.Visible>(first);
        Assert.Equal(1, h.Provider.Calls);

        // Same keyword: per-keyword + loop-protection + global all live.
        var second = await h.Service.RunKeywordCommentAsync("trigger-2", "testword");
        Assert.Equal(AiAwarenessDropKind.CooldownSuppressed, Assert.IsType<AiAwarenessRoutingResult.Dropped>(second).Kind);

        // Different keyword: global still live (WPF hard ceiling on ANY two reactions).
        var third = await h.Service.RunKeywordCommentAsync("trigger-3", "otherword");
        Assert.Equal(AiAwarenessDropKind.CooldownSuppressed, Assert.IsType<AiAwarenessRoutingResult.Dropped>(third).Kind);

        // After the global window passes (but inside per-keyword/loop windows): a DIFFERENT
        // keyword on a DIFFERENT trigger is admitted; the SAME keyword is still suppressed
        // (per-keyword 15s and loop-protection 5s baselines).
        h.Now = h.Now.AddSeconds(11);
        var sameKeyword = await h.Service.RunKeywordCommentAsync("trigger-4", "testword");
        Assert.Equal(AiAwarenessDropKind.CooldownSuppressed, Assert.IsType<AiAwarenessRoutingResult.Dropped>(sameKeyword).Kind);
        var fresh = await h.Service.RunKeywordCommentAsync("trigger-5", "thirdword");
        Assert.IsType<AiAwarenessRoutingResult.Visible>(fresh);

        Assert.Equal(2, h.Provider.Calls);
    }

    [Fact]
    public async Task CooldownSuppressed_Reaction_TypedOutcome_ZeroNetwork()
    {
        var h = new Harness();
        await h.AdmitProviderAsync();
        h.Cooldowns.Extend(AiCooldownKind.PerTrigger, "(window-reaction)", TimeSpan.FromSeconds(60));

        var result = await h.Service.RunReactionAsync(new AiAwarenessContext("cat", "app", "title", "0m"));

        var dropped = Assert.IsType<AiAwarenessRoutingResult.Dropped>(result);
        Assert.Equal(AiAwarenessDropKind.CooldownSuppressed, dropped.Kind);
        Assert.Equal(0, h.Pipeline.SendAttempts);
        Assert.Contains(h.Diagnostics.Records, r => r.StableCode == "suppressed:cooldown");
    }
}
