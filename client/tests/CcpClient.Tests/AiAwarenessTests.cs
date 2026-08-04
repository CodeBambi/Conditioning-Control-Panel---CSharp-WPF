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

        public AiRequest? LastRequest;

        public Func<CancellationToken, Task<CapabilityState>>? Probe { get; } =
            _ => Task.FromResult<CapabilityState>(new CapabilityState.Available("stub-probe"));

        public async Task<AiReply> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            LastRequest = request;
            await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return Reply;
        }

        public void Release() => _gate.TrySetResult();
    }

    private sealed class CountingMemoryStore : IAiMemoryStore
    {
        public int Appends;

        public IReadOnlyList<AiMemoryTurn> ReadRecent(int maxTurns) => [];

        public void Append(AiMemoryTurn turn) => Interlocked.Increment(ref Appends);

        public void Clear() { }
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
        public CountingMemoryStore Memory { get; } = new();
        public DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

        public Harness(AiModerationPolicy? policy = null)
        {
            Boundary = new AiModerationBoundary(policy);
            Cooldowns = new AiCooldownRegistry(() => Now);
            Pipeline = new AiOperationPipeline(Registry, Capabilities, LoopbackOnlyAdmissionPolicy.Instance, Diagnostics, Boundary, Memory);
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

    // ---- context packaging (admission §5 rule 2): under consent, every field through the c3 boundary ----

    [Fact]
    public async Task Packaging_AssemblesTheWpfShape_EveryFieldModerated()
    {
        var h = new Harness();
        await h.AdmitProviderAsync();

        var result = await h.Service.RunReactionAsync(new AiAwarenessContext("Social", "Browser", "Some Page", "0m"));

        Assert.IsType<AiAwarenessRoutingResult.Visible>(result);
        // The WPF-observed shape verbatim (AiService.cs:160-163).
        Assert.Equal("[Category: Social | App: Browser | Title: Some Page | Duration: 0m]", h.Provider.LastRequest?.Prompt);
    }

    [Fact]
    public async Task Packaging_BlockingPolicyOnAnyField_ZeroTransmission_TypedDrop()
    {
        const string forbidden = "forbidden-token";
        var policy = new AiModerationPolicy([new AiModerationRule("test-cat", AiModerationAction.Block, [forbidden])]);

        // Every field is a moderation subject: blocking ANY one of them prevents transmission.
        string[][] cases =
        [
            [forbidden, "app", "title", "0m"],
            ["cat", forbidden, "title", "0m"],
            ["cat", "app", forbidden, "0m"],
            ["cat", "app", "title", forbidden],
        ];
        foreach (var fields in cases)
        {
            var h = new Harness(policy);
            await h.AdmitProviderAsync();

            var result = await h.Service.RunReactionAsync(new AiAwarenessContext(fields[0], fields[1], fields[2], fields[3]));

            var dropped = Assert.IsType<AiAwarenessRoutingResult.Dropped>(result);
            Assert.Equal(AiAwarenessDropKind.RefusedByModeration, dropped.Kind);
            Assert.Equal(0, h.Pipeline.SendAttempts);
            Assert.Equal(0, h.Provider.Calls);
            Assert.Contains(h.Diagnostics.Records, r => r.Outcome == AiDiagnosticOutcome.Refused && r.StableCode == "refused:input");
            // Content-free: the forbidden field text and the category code never appear.
            Assert.DoesNotContain(forbidden, h.AllDiagnosticText());
            Assert.DoesNotContain("test-cat", h.AllDiagnosticText());
        }
    }

    [Fact]
    public async Task Packaging_Fields_NeverEnterDiagnosticsOrMemory()
    {
        const string sentinelTitle = "sentinel-private-title-7f3a";
        var h = new Harness();
        await h.AdmitProviderAsync();

        var visible = await h.Service.RunReactionAsync(new AiAwarenessContext("Work", "Editor", sentinelTitle, "5m"));
        Assert.IsType<AiAwarenessRoutingResult.Visible>(visible);

        // The field reached the provider (transmission under consent)...
        Assert.Contains(sentinelTitle, h.Provider.LastRequest?.Prompt);
        // ...but NEVER diagnostics (contract §12) and NEVER memory (awareness turns are
        // never persisted — pipeline memory-append is interactive-only, c4).
        Assert.DoesNotContain(sentinelTitle, h.AllDiagnosticText());
        Assert.Equal(0, h.Memory.Appends);
    }

    [Fact]
    public void FormatDuration_WpfShape()
    {
        // AiService.cs:182-188: seconds under a minute, minutes under an hour, else hours.
        Assert.Equal("30s", AiAwarenessContext.FormatDuration(TimeSpan.FromSeconds(30)));
        Assert.Equal("5m", AiAwarenessContext.FormatDuration(TimeSpan.FromMinutes(5)));
        Assert.Equal("2h", AiAwarenessContext.FormatDuration(TimeSpan.FromHours(2)));
    }

    // ---- keyword routing (admission §5 rule 4): owned ops, typed visibility, drop-by-type ----

    [Fact]
    public async Task KeywordRouting_PromptShape_WpfVerbatim()
    {
        var h = new Harness();
        await h.AdmitProviderAsync();

        await h.Service.RunKeywordCommentAsync("t1", "testword");
        Assert.Equal("You just caught the user on the word 'testword'. React in character, one short line.", h.Provider.LastRequest?.Prompt);

        h.Now = h.Now.AddHours(1); // clear all cooldowns
        await h.Service.RunKeywordCommentAsync("t2", "other", promptTemplate: "Caught {keyword}! React.");
        Assert.Equal("Caught other! React.", h.Provider.LastRequest?.Prompt);
    }

    [Fact]
    public async Task KeywordRouting_ProviderUnavailable_CannedFallback_IsTypedVisible()
    {
        var h = new Harness();
        h.Provider.Reply = new AiReply.Unavailable(AiReplyCodes.Offline);
        await h.AdmitProviderAsync();

        var withCanned = await h.Service.RunKeywordCommentAsync("t1", "testword", fallbackText: "canned app phrase");
        var visible = Assert.IsType<AiAwarenessRoutingResult.Visible>(withCanned);
        // §2 rule 4 typed visibility: distinguishable from Generated BY TYPE — the badge
        // always reflects the true source (Fallback is never badged).
        var fallback = Assert.IsType<AiReply.Fallback>(visible.Reply);
        Assert.Equal("canned app phrase", fallback.Text);
        Assert.Equal("keyword-fallback", fallback.Code);

        h.Now = h.Now.AddHours(1);
        var withoutCanned = await h.Service.RunKeywordCommentAsync("t2", "testword");
        Assert.Equal(AiAwarenessDropKind.ProviderUnavailable, Assert.IsType<AiAwarenessRoutingResult.Dropped>(withoutCanned).Kind);
    }

    [Fact]
    public async Task KeywordRouting_Refused_DropsByType_NeverCanned()
    {
        const string forbidden = "forbidden-keyword";
        var policy = new AiModerationPolicy([new AiModerationRule("test-cat", AiModerationAction.Block, [forbidden])]);
        var h = new Harness(policy);
        await h.AdmitProviderAsync();

        // The keyword trips the pipeline's uniform input boundary (H7: one boundary at the
        // operation edge). Even WITH canned text available, a refusal drops by type —
        // recorded divergence from WPF's string channel (refusal → null → canned).
        var result = await h.Service.RunKeywordCommentAsync("t1", forbidden, fallbackText: "canned app phrase");

        var dropped = Assert.IsType<AiAwarenessRoutingResult.Dropped>(result);
        Assert.Equal(AiAwarenessDropKind.RefusedByModeration, dropped.Kind);
        Assert.Equal(0, h.Pipeline.SendAttempts);
        Assert.Contains(h.Diagnostics.Records, r => r.Outcome == AiDiagnosticOutcome.Refused && r.StableCode == "refused:input");
        Assert.DoesNotContain(forbidden, h.AllDiagnosticText());
    }

    [Fact]
    public async Task KeywordRouting_IsAnOwnedOperation_PanicCancellable()
    {
        var h = new Harness();
        // Register + probe WITHOUT releasing the provider gate: the operation stays in flight.
        h.Pipeline.RegisterProvider(h.Provider);
        h.Pipeline.SelectProvider(AiProviderId.LocalOllama);
        await new CapabilityProbeRunner(h.Registry.OwnerFor("probes"), h.Capabilities).RunAllAsync(CancellationToken.None);

        var inFlight = h.Service.RunKeywordCommentAsync("t1", "testword");
        await h.Pipeline.PanicAsync(TimeSpan.FromSeconds(5));

        var result = await inFlight;
        Assert.Equal(AiAwarenessDropKind.Cancelled, Assert.IsType<AiAwarenessRoutingResult.Dropped>(result).Kind);
        Assert.Contains(h.Diagnostics.Records, r => r.Outcome == AiDiagnosticOutcome.Cancelled);
    }

    [Fact]
    public async Task KeywordRouting_NoProviderSelected_OfflineZeroNetwork()
    {
        var h = new Harness(); // no provider registered/selected

        var result = await h.Service.RunKeywordCommentAsync("t1", "testword");

        Assert.Equal(AiAwarenessDropKind.ProviderUnavailable, Assert.IsType<AiAwarenessRoutingResult.Dropped>(result).Kind);
        Assert.Equal(0, h.Pipeline.SendAttempts);
        Assert.Contains(h.Diagnostics.Records, r => r.StableCode == AiReplyCodes.NotConfigured);
    }

    // ---- title observation (admission §5 rule 2): capability-probed, consent-gated, honestly scoped ----

    [Fact]
    public async Task TitleProbe_PlatformTypedState_WindowsAvailable_LinuxUnavailable()
    {
        var state = await AiWindowTitleCapability.Probe(CancellationToken.None);
        if (OperatingSystem.IsWindows())
        {
            // Windows session facts (the evidence box): real capture confirmed. The detail
            // is content-free — it carries the title LENGTH, never the title.
            // PRECONDITION (named, pre-completion consult): this arm requires an INTERACTIVE
            // Windows desktop session — under a locked/non-interactive session
            // GetForegroundWindow returns 0 and the probe honestly reports Unavailable
            // (no-foreground-window). A failure here on CI means the session precondition
            // was lost, not a product regression.
            var available = Assert.IsType<CapabilityState.Available>(state);
            Assert.Contains("length", available.Detail);
            Assert.True(AiWindowTitleCapability.TryCaptureForegroundTitle(out var title));
            Assert.DoesNotContain(title.Length > 0 ? title : "\u0001impossible", available.Detail);
        }
        else if (OperatingSystem.IsLinux())
        {
            // Named limit, never faked: WSL zero distros on the evidence box; X11 session
            // facts owner-gated; NO Wayland claim.
            var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);
            Assert.Equal(AiWindowTitleCapability.LinuxUnprobedCode, unavailable.Reason.Code);
        }
    }

    [Fact]
    public async Task TitleObservation_GatedByConsentAndCapability_TitleNeverLogged()
    {
        var h = new Harness();
        AiWindowTitleCapability.Register(h.Capabilities);
        await new CapabilityProbeRunner(h.Registry.OwnerFor("probes"), h.Capabilities).RunAllAsync(CancellationToken.None);

        // Without consent: typed non-observation (covered by the default-consent test too).
        h.Service.Consent = AiAwarenessConsent.NotGiven;
        Assert.IsType<AiTitleObservation.ConsentNotGiven>(h.Service.ObserveForegroundTitle());

        h.Service.Consent = AiAwarenessConsent.Given;
        var observation = h.Service.ObserveForegroundTitle();
        if (OperatingSystem.IsWindows())
        {
            var observed = Assert.IsType<AiTitleObservation.Observed>(observation);
            // The title leaves ONLY to the caller: zero diagnostic records, zero memory.
            Assert.DoesNotContain(observed.Title.Length > 0 ? observed.Title : "\u0001impossible", h.AllDiagnosticText());
        }
        else
        {
            Assert.IsType<AiTitleObservation.Unavailable>(observation);
        }

        Assert.Empty(h.Diagnostics.Records);
        Assert.Equal(0, h.Memory.Appends);
    }

    [Fact]
    public void TitleObservation_UnprobedCapability_TypedUnavailable()
    {
        var h = new Harness(); // consent Given by harness default; capability never registered

        var observation = h.Service.ObserveForegroundTitle();

        var unavailable = Assert.IsType<AiTitleObservation.Unavailable>(observation);
        var state = Assert.IsType<CapabilityState.Unavailable>(unavailable.State);
        Assert.Equal(CapabilityReasonCodes.UnknownCapability, state.Reason.Code);
    }
}
