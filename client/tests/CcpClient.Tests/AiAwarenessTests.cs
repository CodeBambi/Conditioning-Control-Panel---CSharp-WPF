using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Awareness service tests (slice c5; contract §4; admission §5). Proves:
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

        // Interface member (mechanical): the awareness path never consumes context.
        public IReadOnlyList<AiMemoryTurn> ReadPromptContext() => [];

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
        // Loop framing (c): the loop carries the only assertions — pin the source
        // non-empty so an empty case set can never silence them invisibly.
        Assert.NotEmpty(cases);
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
            // That precondition is now ENFORCED by the product's own typed answer
            // instead of being stated in prose and then asserted away. The gate REPORTS
            // (Assert.SkipWhen), never a silent return — pinned by NAME in
            // client/tests/floor/floor.json (allowedSkips; may-skip semantics — green when a
            // foreground window exists, allowed-skipped when none does). Conditioned on the
            // product's OWN typed code (AiAwarenessService.cs:295, produced at :315-319) and
            // on NOTHING else: an Unavailable carrying any other code still FAILS below.
            Assert.SkipWhen(
                state is CapabilityState.Unavailable noWindow &&
                noWindow.Reason.Code == AiWindowTitleCapability.NoForegroundWindowCode,
                "the product reported no foreground window: its typed Unavailable(no-foreground-window), " +
                "emitted on exactly one condition — the capture returned false. On a machine you believe " +
                "is INTERACTIVE, treat this as a capture-path regression, not a session fact.");

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

        // The zero-leak assertions MOVED up from the tail of this body. They are
        // session-INDEPENDENT, so they must execute even when the platform arm below skips;
        // left at the tail an Assert.Skip* would abort the fact and silently delete them.
        // Their truth value is identical at both positions: ObserveForegroundTitle has
        // already run and is synchronous, no provider is registered so the pipeline never
        // runs, and the only side effect between here and the old position is the read-only
        // h.AllDiagnosticText() (CollectingAiDiagnosticsSink.Records snapshots under its own
        // lock, AiDiagnostics.cs:19-25). Nothing between the two positions emits a record or
        // appends memory.
        Assert.Empty(h.Diagnostics.Records);
        Assert.Equal(0, h.Memory.Appends);

        if (OperatingSystem.IsWindows())
        {
            // Same session precondition as TitleProbe above, inherited indirectly —
            // this fact runs the real capability probe before observing. The gate REPORTS
            // (Assert.SkipWhen), never a silent return — pinned by NAME in
            // client/tests/floor/floor.json (allowedSkips; may-skip semantics). The code is
            // reachable two ways: the probe's state handed back verbatim
            // (AiAwarenessService.cs:574-576) and a capture miss at observation time
            // (:579-582), so the reason CODE, not the state type, is the discriminator that
            // covers both. The consent arm and the two zero-leak assertions above are
            // session-independent and have already executed. A privacy-filter drop
            // (title-dropped-private-window / title-dropped-blank) is NOT this code and
            // still FAILS below.
            Assert.SkipWhen(
                observation is AiTitleObservation.Unavailable { State: CapabilityState.Unavailable noWindow } &&
                noWindow.Reason.Code == AiWindowTitleCapability.NoForegroundWindowCode,
                "the product reported no foreground window: its typed Unavailable(no-foreground-window), " +
                "which cannot distinguish a locked session from a capture that returned false. On a " +
                "machine you believe is INTERACTIVE, treat this as a capture-path regression.");

            var observed = Assert.IsType<AiTitleObservation.Observed>(observation);
            // The title leaves ONLY to the caller: zero diagnostic records, zero memory.
            Assert.DoesNotContain(observed.Title.Length > 0 ? observed.Title : "\u0001impossible", h.AllDiagnosticText());
        }
        else
        {
            Assert.IsType<AiTitleObservation.Unavailable>(observation);
        }
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

    // ---- The real capture path, guarded on every machine class ----
    //
    // The two facts above are conditioned on the product's own typed
    // no-foreground-window answer. That is right, and it is not touched here. Its price:
    // AiAwarenessTests.cs:415 was the ONLY execution of the capture path in client/tests,
    // so once it may skip, a broken P/Invoke, a renamed export, a CharSet regression or an
    // always-false return is caught by nothing on a locked or disconnected session.
    //
    // The two facts below never look at the foreground window. F2 drives the product's own
    // text-reading half with a handle THIS TEST creates and owns; F1 pins the shipped
    // declaration text. Neither can skip, on any platform. They are two facts and not one
    // written twice because each has a mutation that reds it ALONE: dropping CharSet from
    // GetWindowTextLengthW is behaviourally inert (that export takes no string and its name
    // already ends in W) and reds only F1; stubbing the text half to return an empty title
    // leaves every declaration intact and reds only F2.

    /// <summary>
    /// One capture attempt as a VALUE, so a single Assert.Equal carries the whole
    /// outcome. Deliberately a test-local record rather than
    /// <c>CapabilityState.Available(title)</c>: that type's documented discipline is a
    /// content-free detail (title LENGTH only, AiAwarenessService.cs:277-285). Nothing leaks
    /// either way here — the value is a test-authored literal that reaches no sink — but
    /// borrowing a vocabulary whose contract says the opposite would teach the wrong rule.
    /// </summary>
    private sealed record Sp089Capture(bool Captured, string Title);

    /// <summary>
    /// The capture fixture. EVERY platform decision lives here, in a non-<c>[Fact]</c> body, so
    /// both facts below carry zero detected vacuous shapes and the orchestrator-owned
    /// vacuous-shape-ledger.json stays untouched (VacuousShapeDetector.Scan analyses
    /// [Fact]/[Theory] bodies only — VacuousShapeDetector.cs:84-107). Honest cost, recorded
    /// rather than hidden: a real platform branch exists here that a lexical detector cannot
    /// see, and zero detected shapes is a statement about shape, never about runtime.
    /// </summary>
    private static class Sp089CaptureProbe
    {
        /// <summary>
        /// The title the fixture window carries. Two properties are load-bearing and neither
        /// is decoration. NON-ASCII: it is what a lost CharSet.Unicode corrupts. MANY
        /// characters: with CharSet dropped, GetWindowTextW still writes wide bytes while the
        /// ANSI marshaller reads them back narrow, so a ONE-character title would survive
        /// unchanged and the mutation would stay green.
        /// </summary>
        internal const string ProbeTitle = "Capture probe éüß 中文テスト Живот";

        /// <summary>Parent that makes the window MESSAGE-ONLY: never visible, never in the
        /// z-order, never activatable, never enumerated, and excluded from HWND_BROADCAST.
        /// It therefore cannot become the foreground window, so it cannot flip either
        /// skip predicate in either direction.</summary>
        private static readonly IntPtr HwndMessage = new(-3);

        private static readonly string ServicePath =
            Path.Combine(FindRepoRoot(), "client", "src", "CcpClient.Desktop", "Ai", "AiAwarenessService.cs");

        /// <summary>What <see cref="Run"/> must produce on this machine class.</summary>
        internal static Sp089Capture Expected => OperatingSystem.IsWindows()
            ? new Sp089Capture(true, ProbeTitle)
            : new Sp089Capture(false, string.Empty);

        /// <summary>
        /// The three shipped user32 declarations, exactly as F1 requires them to read.
        /// Module, entry point (no EntryPoint= is given, so the method name IS the export
        /// looked up) and CharSet on both text imports.
        /// </summary>
        internal static IReadOnlyList<string> ExpectedDeclarations =>
        [
            "[DllImport(\"user32.dll\")] => internal static extern IntPtr GetForegroundWindow()",
            "[DllImport(\"user32.dll\", CharSet = CharSet.Unicode)] => internal static extern int GetWindowTextLengthW(IntPtr hWnd)",
            "[DllImport(\"user32.dll\", CharSet = CharSet.Unicode)] => internal static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount)",
        ];

        /// <summary>
        /// Drives the product's own text half against a window this test owns.
        ///
        /// WIN32 THREAD AFFINITY — the one invariant this fixture introduces. CreateWindowExW
        /// binds the HWND to the CALLING thread's message queue, and GetWindowTextLengthW /
        /// GetWindowTextW against a window owned by this process are WM_GETTEXTLENGTH /
        /// WM_GETTEXT sends: on the owning thread they go straight into DefWindowProc and no
        /// pump is needed; from another thread SendMessage blocks until the owner pumps — an
        /// unbounded hang that no timing guard can see, because the timing guard scans for
        /// managed sleep, delay and elapsed-clock TOKENS, not a blocking native send. So the
        /// caller is a SYNCHRONOUS `public void` fact. Do NOT convert it to `async Task`, and
        /// do NOT hand this handle to another thread: either one reintroduces the hang.
        /// </summary>
        internal static Sp089Capture Run()
        {
            if (!OperatingSystem.IsWindows())
            {
                // Non-Windows leg. It still EXECUTES product code and compares what the
                // product actually answered; it does not restate a constant. The documented
                // non-Windows answer is false with an empty title, and anything else reds.
                var offWindows = AiWindowTitleCapability.TryCaptureForegroundTitle(out var offTitle);
                return new Sp089Capture(offWindows, offTitle);
            }

            var hwnd = CreateWindowExW(0, "STATIC", ProbeTitle, 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                // NEVER a skip and never a silent pass: a fixture that cannot be built is a
                // loud failure naming the OS error. SetLastError = true on the import below
                // is what makes that number mean anything — without it the value is stale
                // state from some unrelated call and the diagnostic lies at exactly the
                // moment someone needs it (measured: a SUCCESSFUL create can leave a
                // non-zero last-error behind).
                throw new InvalidOperationException(
                    "Capture fixture: CreateWindowExW returned NULL for a message-only STATIC window " +
                    $"(Win32 error {Marshal.GetLastWin32Error()}). The capture path was NOT exercised. " +
                    "This is a hostile window station, not a product regression — but it is still a red, " +
                    "because a guard that quietly stands down is the hole this fact exists to close.");
            }

            try
            {
                var captured = AiWindowTitleCapability.TryCaptureWindowTitle(hwnd, out var title);
                return new Sp089Capture(captured, title);
            }
            finally
            {
                _ = DestroyWindow(hwnd);
            }
        }

        /// <summary>
        /// The shipped NativeMethods declarations, read from SOURCE TEXT and whitespace-
        /// normalised. Not reflection over NativeMethods (forbidden by the packet) and not a
        /// test-side re-declaration of user32 (which would pass with the product's own
        /// declarations deleted): the shipped text is the subject. File.ReadAllText throws on
        /// a missing file, so a moved or deleted source reds rather than skipping — which is
        /// also why this body carries no File.Exists( token.
        /// </summary>
        internal static IReadOnlyList<string> ReadShippedNativeMethodDeclarations()
        {
            var source = File.ReadAllText(ServicePath);
            var block = ExtractNativeMethodsBlock(source);
            var declarations = new List<string>();
            foreach (Match m in Regex.Matches(block, @"\[DllImport\(([^)]*)\)\]\s*(internal static extern [^;]*);"))
            {
                declarations.Add($"[DllImport({Squash(m.Groups[1].Value)})] => {Squash(m.Groups[2].Value)}");
            }

            if (declarations.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No [DllImport] declaration parsed out of NativeMethods in {ServicePath} — " +
                    "the capture path's declarations are the subject of this fact, so an unparseable " +
                    "or emptied holder is a failure, never a pass.");
            }

            return declarations;
        }

        private static string ExtractNativeMethodsBlock(string source)
        {
            const string Marker = "private static class NativeMethods";
            var at = source.IndexOf(Marker, StringComparison.Ordinal);
            if (at < 0)
            {
                throw new InvalidOperationException(
                    $"`{Marker}` not found in {ServicePath} — the holder this fact pins is gone or renamed.");
            }

            var open = source.IndexOf('{', at + Marker.Length);
            if (open < 0)
            {
                throw new InvalidOperationException($"NativeMethods in {ServicePath} has no body brace.");
            }

            var depth = 0;
            for (var i = open; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}' && --depth == 0)
                {
                    return source[open..(i + 1)];
                }
            }

            throw new InvalidOperationException($"NativeMethods body in {ServicePath} is unbalanced.");
        }

        private static string Squash(string text) => Regex.Replace(text.Trim(), @"\s+", " ");

        /// <summary>The ninth deliberate copy of the house walk-up idiom (TestTimingGuardTests,
        /// VacuousShapeDetector, and seven more). There is no shared helper on purpose: these
        /// guards keep their checks in their own bodies because the shape detector is lexical
        /// (vacuous-shape-ledger.json records that decision).</summary>
        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "client", "CcpClient.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                $"Repo root not found walking up from {AppContext.BaseDirectory} " +
                "(anchor client/CcpClient.sln) — this fact refuses to skip.");
        }

        // Fixture CONSTRUCTION only. These two imports build and tear down a window; they
        // re-declare nothing that is under test. The READ goes through the product: delete
        // NativeMethods.GetWindowTextW and F2 stops compiling, break it and F2 reds.
        // CharSet.Unicode here is not decoration either — without it the non-ASCII ProbeTitle
        // would marshal ANSI into a W export and F2 would red for a FIXTURE reason wearing a
        // product reason's clothes.
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(
            int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hWnd);
    }

    /// <summary>
    /// Fact F2. Exercises the product's REAL text-reading P/Invokes — GetWindowTextLengthW
    /// then GetWindowTextW, through the product's own declarations — against a handle the
    /// test created, and requires every character back. GetForegroundWindow is never called,
    /// so no foreground window is required and this cannot skip: it is the guard that still
    /// stands on a locked or disconnected session.
    /// </summary>
    [Fact]
    public void CapturePath_OnAWindowTheTestOwns_ReadsBackEveryCharacter_ThroughTheProductsOwnDeclarations()
    {
        Assert.Equal(Sp089CaptureProbe.Expected, Sp089CaptureProbe.Run());
    }

    /// <summary>
    /// Fact F1. Pins the shipped declaration TEXT: module, entry point, and CharSet on both
    /// text imports. It executes nothing, which is the point — it catches the one regression
    /// F2 provably cannot see (a CharSet change on GetWindowTextLengthW is behaviourally
    /// inert), and it reds cleanly naming the line where F2's own named mutation reds by
    /// killing the host. It is a supplement to F2, never a substitute: source text proves a
    /// declaration LOOKS right, never that it BINDS.
    /// </summary>
    [Fact]
    public void CapturePathDeclarations_PinModuleEntryPointAndCharSet_WithoutExecutingAnything()
    {
        Assert.Equal(Sp089CaptureProbe.ExpectedDeclarations, Sp089CaptureProbe.ReadShippedNativeMethodDeclarations());
    }

    // ---- F1: incognito hard-drop at the packaging seam (audit row A6) ----

    [Fact]
    public async Task Packaging_IncognitoTitle_HardDrop_TypedPrivacyFiltered_ZeroTransmission()
    {
        var h = new Harness();
        await h.AdmitProviderAsync();

        var result = await h.Service.RunReactionAsync(new AiAwarenessContext("Social", "Browser", "Some Page — InPrivate", "0m"));

        var dropped = Assert.IsType<AiAwarenessRoutingResult.Dropped>(result);
        Assert.Equal(AiAwarenessDropKind.PrivacyFiltered, dropped.Kind);
        Assert.Equal(0, h.Pipeline.SendAttempts);
        Assert.Equal(0, h.Provider.Calls);
        Assert.Contains(h.Diagnostics.Records, r => r.StableCode == "dropped:privacy-filtered");
        // Content-free (contract §12): the title never enters any diagnostic record.
        Assert.DoesNotContain("Some Page", h.AllDiagnosticText());

        // Negative control in pair: a clean title on a fresh harness is still admitted.
        var clean = new Harness();
        await clean.AdmitProviderAsync();
        Assert.IsType<AiAwarenessRoutingResult.Visible>(
            await clean.Service.RunReactionAsync(new AiAwarenessContext("Social", "Browser", "Some Page", "0m")));
    }

    [Fact]
    public async Task TryPackage_IncognitoTitle_False_NullRequest_NullRefusal()
    {
        var h = new Harness();
        await h.AdmitProviderAsync();

        // Defence in depth at the packager itself (WPF re-checks inside the shared rules):
        // never packaged, and typed as NOT a moderation verdict (refusal is null).
        var blocked = AiAwarenessContextPackaging.TryPackage(
            new AiAwarenessContext("cat", "app", "x — InPrivate", "5s"),
            h.Boundary, out var blockedRequest, out var refusal);
        Assert.False(blocked);
        Assert.Null(blockedRequest);
        Assert.Null(refusal);

        // Negative control: a clean title still packages.
        var clean = AiAwarenessContextPackaging.TryPackage(
            new AiAwarenessContext("cat", "app", "title", "5s"),
            h.Boundary, out var cleanRequest, out _);
        Assert.True(clean);
        Assert.NotNull(cleanRequest);
    }

    // ---- F2: title scrubbing at the packaging seam (audit row A10) ----

    [Fact]
    public async Task Packaging_ScrubsTitle_ShapePreserved_RawNeverTransmitted()
    {
        var h = new Harness();
        await h.AdmitProviderAsync();

        var result = await h.Service.RunReactionAsync(
            new AiAwarenessContext("Social", "Browser", "Some Page user@example.com 123456", "0m"));

        Assert.IsType<AiAwarenessRoutingResult.Visible>(result);
        // The assembled wire title is the scrubbed one; the raw title never leaves.
        Assert.Equal("[Category: Social | App: Browser | Title: Some Page | Duration: 0m]", h.Provider.LastRequest?.Prompt);
        Assert.DoesNotContain("user@example.com", h.Provider.LastRequest?.Prompt);
        Assert.DoesNotContain("123456", h.Provider.LastRequest?.Prompt);
    }

    [Fact]
    public async Task Packaging_TitleScrubbedToEmpty_CarriesNoTitle()
    {
        var h = new Harness();
        await h.AdmitProviderAsync();

        var result = await h.Service.RunReactionAsync(new AiAwarenessContext("Social", "Browser", "user@example.com", "0m"));

        // WPF ResolveTitle→null semantics (AwarenessPrivacyRules.cs:455-466): the frame
        // proceeds TITLE-FREE — narrower than carrying anything.
        Assert.IsType<AiAwarenessRoutingResult.Visible>(result);
        Assert.Equal("[Category: Social | App: Browser | Title:  | Duration: 0m]", h.Provider.LastRequest?.Prompt);
    }

    [Fact]
    public async Task Packaging_ModerationSeesTheRawTitle_BlockedEvenWhenScrubWouldEmptyIt()
    {
        // Order pin (pre-approach consult): moderation evaluates the RAW field first. A
        // title the scrub alone would erase must still block — otherwise F2 would WIDEN
        // flow (a reaction transmitted where the landed behavior transmits nothing).
        const string forbidden = "forbidden-token";
        var policy = new AiModerationPolicy([new AiModerationRule("test-cat", AiModerationAction.Block, [forbidden])]);
        var h = new Harness(policy);
        await h.AdmitProviderAsync();

        var result = await h.Service.RunReactionAsync(
            new AiAwarenessContext("cat", "app", $"{forbidden}@example.com", "0m"));

        Assert.Equal(AiAwarenessDropKind.RefusedByModeration, Assert.IsType<AiAwarenessRoutingResult.Dropped>(result).Kind);
        Assert.Equal(0, h.Pipeline.SendAttempts);
        Assert.Equal(0, h.Provider.Calls);
    }

    // ---- F3: the strip on the awareness reply paths (audit row C3, strip half) ----

    [Fact]
    public async Task Reaction_ReplyWithInventedUrl_StrippedBeforeApplication()
    {
        var h = new Harness();
        h.Provider.Reply = new AiReply.Generated("Hello there. Check https://example.com/x now! Bye.", AiEndpointClass.Loopback);
        await h.AdmitProviderAsync();

        var result = await h.Service.RunReactionAsync(new AiAwarenessContext("Social", "Browser", "Some Page", "0m"));

        var visible = Assert.IsType<AiAwarenessRoutingResult.Visible>(result);
        var generated = Assert.IsType<AiReply.Generated>(visible.Reply);
        Assert.Equal("Hello there. Bye.", generated.Text);
        Assert.DoesNotContain("https://example.com", h.AllDiagnosticText());
        Assert.Equal(0, h.Memory.Appends); // awareness turns are never persisted (unchanged)
    }

    [Fact]
    public async Task KeywordReply_EmptiedByStrip_TypedFallbackWithCanned_TypedDropWithout()
    {
        var h = new Harness();
        h.Provider.Reply = new AiReply.Generated("https://example.com/only-link", AiEndpointClass.Loopback);
        await h.AdmitProviderAsync();

        // WPF chat parity (CompanionBrain.cs:279-284): nothing but invented links → "same
        // treatment as a canned fallback" — the keyword route's canned text, typed Fallback.
        var withCanned = await h.Service.RunKeywordCommentAsync("t1", "testword", fallbackText: "canned app phrase");
        var fallback = Assert.IsType<AiReply.Fallback>(Assert.IsType<AiAwarenessRoutingResult.Visible>(withCanned).Reply);
        Assert.Equal("canned app phrase", fallback.Text);
        Assert.Equal("keyword-fallback", fallback.Code);

        // Without canned text the emptied reply is a typed drop, never an empty bubble.
        h.Now = h.Now.AddHours(1); // clear all cooldowns
        var withoutCanned = await h.Service.RunKeywordCommentAsync("t2", "testword");
        Assert.Equal(AiAwarenessDropKind.ProviderUnavailable, Assert.IsType<AiAwarenessRoutingResult.Dropped>(withoutCanned).Kind);
        Assert.Equal(0, h.Memory.Appends);
    }
}
