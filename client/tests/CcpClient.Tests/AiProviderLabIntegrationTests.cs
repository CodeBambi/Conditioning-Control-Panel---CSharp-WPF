using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// LAB matrix (SP-035 slice c2; admission §8 c2): the REAL LoopbackOllamaProvider behind
/// the REAL c1 pipeline against the deterministic loopback lab — real sockets on
/// 127.0.0.1, zero external network. Rows: ok round-trip, mid-stream cancel (lab observes
/// client-gone), timeout classification through the pipeline (token not poisoned), 429 /
/// 500 / refusal / malformed / truncated with exact lab hit counts, slow-ok late
/// completion → exactly 1 stale discard at the application seam (test-side
/// uncooperative-transport decorator, SP-019 RequestDetachedAsync shape), LIVE panic
/// against a real in-flight network operation, remote-host pre-socket rejection in-product
/// (both layers), and the offline zero-network re-verify.
/// </summary>
public class AiProviderLabIntegrationTests
{
    private static readonly AiRequest Request = new("lab-prompt");

    /// <summary>
    /// Test-side UNCOOPERATIVE transport (SP-019's RequestDetachedAsync dual-transport
    /// shape): the token is swallowed so the REAL network operation completes LATE; the
    /// pipeline's application seam — never the transport — is where the stale result dies.
    /// The socket, the lab, and the response body are fully real.
    /// </summary>
    private sealed class UncooperativeTransportDecorator(LoopbackOllamaProvider inner) : IAiProvider
    {
        public AiProviderDescriptor Descriptor => inner.Descriptor;

        public Func<CancellationToken, Task<CapabilityState>>? Probe => inner.Probe;

        public Task<AiReply> CompleteAsync(AiRequest request, CancellationToken cancellationToken) =>
            inner.CompleteAsync(request, CancellationToken.None);
    }

    private sealed class Harness : IDisposable
    {
        public AiProviderLab Lab { get; } = new();
        public OperationRegistry Registry { get; } = new();
        public CapabilityRegistry Capabilities { get; } = new();
        public CollectingAiDiagnosticsSink Diagnostics { get; } = new();
        public AiOperationPipeline Pipeline { get; }
        public LoopbackOllamaProvider Provider { get; }

        public Harness(AiRetryPolicy? retry = null, bool uncooperative = false, Uri? host = null)
        {
            Provider = new LoopbackOllamaProvider(new LoopbackOllamaProviderOptions
            {
                Host = host ?? Lab.Host,
                RequestTimeout = TimeSpan.FromMilliseconds(800),
                ProbeTimeout = TimeSpan.FromMilliseconds(800),
                Retry = retry ?? AiRetryPolicy.Off,
            });
            Pipeline = new AiOperationPipeline(Registry, Capabilities, LoopbackOnlyAdmissionPolicy.Instance, Diagnostics, new AiModerationBoundary());
            Pipeline.RegisterProvider(uncooperative ? new UncooperativeTransportDecorator(Provider) : Provider);
        }

        public async Task SelectAndProbeAsync()
        {
            Pipeline.SelectProvider(AiProviderId.LocalOllama);
            var runner = new CapabilityProbeRunner(Registry.OwnerFor("probes"), Capabilities);
            await runner.RunAllAsync(CancellationToken.None);
        }

        public void Dispose() => Lab.Dispose();
    }

    private static async Task<AiLabRequestRecord> WaitForRecordAsync(AiProviderLab lab, AiLabMode mode)
    {
        var deadline = Environment.TickCount64 + 8000;
        while (Environment.TickCount64 < deadline)
        {
            var record = lab.Records.LastOrDefault(r => r.Mode == mode);
            if (record is not null)
            {
                return record;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        throw new InvalidOperationException($"lab never recorded a {mode} request");
    }

    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        var deadline = Environment.TickCount64 + 8000;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition(), $"timed out waiting for {what}");
    }

    [Fact]
    public async Task Ok_ThroughPipeline_Completed_WithProvenance_AndContentFreeDiagnostic()
    {
        using var h = new Harness();
        await h.SelectAndProbeAsync();
        h.Lab.Inject(AiLabMode.Ok);

        var result = await h.Pipeline.RunInteractiveAsync(Request);

        Assert.IsType<OperationOutcome.Completed>(result.Outcome);
        var generated = Assert.IsType<AiReply.Generated>(result.Reply);
        Assert.Equal(h.Lab.OkReplyText, generated.Text);
        Assert.Equal(AiEndpointClass.Loopback, generated.Provenance);
        Assert.Equal(1, h.Pipeline.SendAttempts);
        var record = Assert.Single(h.Diagnostics.Records);
        Assert.Equal(AiDiagnosticOutcome.Completed, record.Outcome);
        Assert.Equal(AiEndpointClass.Loopback, record.EndpointClass);
        Assert.Null(record.StableCode);
    }

    [Fact]
    public async Task MidStreamCancel_Live_TypedCancelledFast_GenerationAdvanced_LabSeesClientGone()
    {
        using var h = new Harness();
        await h.SelectAndProbeAsync();
        h.Lab.Inject(AiLabMode.HangStream);
        Assert.Equal(0, h.Pipeline.SendAttempts); // nothing sent yet

        var operation = h.Pipeline.RunInteractiveAsync(Request);
        await WaitForAsync(() => h.Provider.BytesReadSoFar > 0, "a true mid-stream partial-body position");

        // Switch mid-stream: generation invalidation → token cancellation (contract §3 rule 2).
        var started = Environment.TickCount64;
        h.Pipeline.SelectProvider(null);
        var result = await operation;
        var elapsed = Environment.TickCount64 - started;

        Assert.IsType<OperationOutcome.Cancelled>(result.Outcome);
        Assert.Null(result.Reply); // zero applied
        Assert.True(elapsed < 2000, $"mid-stream cancel must be fast, took {elapsed}ms");
        Assert.Equal(1, h.Pipeline.SendAttempts); // exactly one real send, cancelled mid-stream

        // The lab observes client-gone: a cancelled transport cannot deliver a late result.
        var record = await WaitForRecordAsync(h.Lab, AiLabMode.HangStream);
        Assert.Equal("client-gone", record.Outcome);
    }

    [Fact]
    public async Task Timeout_ThroughPipeline_TypedUnavailable_TokenNotPoisoned_LabSeesClientGone()
    {
        using var h = new Harness();
        await h.SelectAndProbeAsync();
        h.Lab.Inject(AiLabMode.Timeout);

        var started = Environment.TickCount64;
        var result = await h.Pipeline.RunInteractiveAsync(Request);
        var elapsed = Environment.TickCount64 - started;

        Assert.IsType<OperationOutcome.Completed>(result.Outcome);
        var unavailable = Assert.IsType<AiReply.Unavailable>(result.Reply);
        Assert.Equal(AiReplyCodes.Timeout, unavailable.Code);
        Assert.True(elapsed < 800 + 2500, $"timeout classification must be bounded, took {elapsed}ms");

        // The external token was NOT cancelled: the pipeline still serves the next operation.
        h.Lab.Inject(AiLabMode.Ok);
        var second = await h.Pipeline.RunInteractiveAsync(Request);
        Assert.IsType<AiReply.Generated>(second.Reply);

        var held = await WaitForRecordAsync(h.Lab, AiLabMode.Timeout);
        Assert.Equal("client-gone", held.Outcome);
    }

    [Fact]
    public async Task Rate429_RetryEnabled_ExactlyTwoHits_QuotaExhausted()
    {
        using var h = new Harness(AiRetryPolicy.WpfObservedPlaceholder);
        await h.SelectAndProbeAsync();
        h.Lab.Inject(AiLabMode.Rate429, AiLabMode.Rate429);

        var started = Environment.TickCount64;
        var result = await h.Pipeline.RunInteractiveAsync(Request);
        var elapsed = Environment.TickCount64 - started;

        var unavailable = Assert.IsType<AiReply.Unavailable>(result.Reply);
        Assert.Equal(AiReplyCodes.QuotaExhausted, unavailable.Code);
        Assert.Equal(2, h.Lab.HitsFor(AiLabMode.Rate429)); // server-side hit count, not client-side hope
        Assert.True(elapsed >= 900, $"Retry-After: 1 must be honored, gap was {elapsed}ms");
        Assert.Equal(1, h.Pipeline.SendAttempts); // the c1 gateway counts once per OPERATION
        Assert.Equal(2, h.Provider.SendAttempts); // the provider-side seam counts per attempt
    }

    [Fact]
    public async Task Error500_RetryEnabled_Bounded_TwoHits()
    {
        using var h = new Harness(AiRetryPolicy.WpfObservedPlaceholder);
        await h.SelectAndProbeAsync();
        h.Lab.Inject(AiLabMode.Error500, AiLabMode.Error500);

        var result = await h.Pipeline.RunInteractiveAsync(Request);

        var unavailable = Assert.IsType<AiReply.Unavailable>(result.Reply);
        Assert.Equal("http-500", unavailable.Code);
        Assert.Equal(2, h.Lab.HitsFor(AiLabMode.Error500));
    }

    [Fact]
    public async Task Refusal_ThroughPipeline_TypedCarrier_ExactlyOneHit()
    {
        using var h = new Harness(AiRetryPolicy.WpfObservedPlaceholder); // refusal never retried even with retry on
        await h.SelectAndProbeAsync();
        h.Lab.Inject(AiLabMode.Refusal);

        var result = await h.Pipeline.RunInteractiveAsync(Request);

        var refused = Assert.IsType<AiReply.Refused>(result.Reply);
        Assert.Equal("content_filter", refused.Refusal.CategoryCode);
        Assert.Equal(AiModerationSource.Output, refused.Refusal.Source);
        Assert.Equal(1, h.Lab.HitsFor(AiLabMode.Refusal));
        Assert.Contains(h.Diagnostics.Records, r => r.Outcome == AiDiagnosticOutcome.Refused);
    }

    [Fact]
    public async Task Malformed_ThroughPipeline_NeverPartial_OneHit()
    {
        using var h = new Harness();
        await h.SelectAndProbeAsync();
        h.Lab.Inject(AiLabMode.Malformed);

        var result = await h.Pipeline.RunInteractiveAsync(Request);

        var unavailable = Assert.IsType<AiReply.Unavailable>(result.Reply);
        Assert.Equal(AiReplyCodes.MalformedOutput, unavailable.Code);
        Assert.Equal(1, h.Lab.HitsFor(AiLabMode.Malformed));
    }

    [Fact]
    public async Task Truncated_ThroughPipeline_PrefixNeverSurfaced_OneHit()
    {
        using var h = new Harness();
        await h.SelectAndProbeAsync();
        h.Lab.Inject(AiLabMode.Truncated);

        var result = await h.Pipeline.RunInteractiveAsync(Request);

        var unavailable = Assert.IsType<AiReply.Unavailable>(result.Reply);
        Assert.Equal(AiReplyCodes.MalformedOutput, unavailable.Code);
        Assert.Equal(1, h.Lab.HitsFor(AiLabMode.Truncated));
    }

    [Fact]
    public async Task SlowOk_LateCompletion_ExactlyOneStaleDiscard_ZeroApplied()
    {
        // The REAL transport (socket, lab, 1.5s-late body) with a test-side token-swallowing
        // decorator — the SP-019 detached-transport shape: a late arrival dies at the seam.
        using var h = new Harness(uncooperative: true);
        await h.SelectAndProbeAsync();
        h.Lab.Inject(AiLabMode.SlowOk);
        var discardsBefore = h.Registry.DiscardedStaleCompletions;

        var operation = h.Pipeline.RunInteractiveAsync(Request);
        // The probe's GET /api/version already counts as a lab hit — wait for the PROVIDER's
        // send seam instead: the socket write has genuinely begun (request in flight).
        await WaitForAsync(() => h.Provider.SendAttempts >= 1, "the real request reaching the wire");

        // Switch while the REAL network operation is in flight; the token is swallowed by
        // the decorator, so the late body genuinely arrives — and must be discarded.
        h.Pipeline.SelectProvider(null);
        var result = await operation;

        Assert.IsType<OperationOutcome.Cancelled>(result.Outcome);
        Assert.Null(result.Reply); // zero applied
        Assert.Equal(discardsBefore + 1, h.Registry.DiscardedStaleCompletions); // exactly 1 stale discard
        var record = await WaitForRecordAsync(h.Lab, AiLabMode.SlowOk);
        Assert.Equal("completed", record.Outcome); // the late completion REALLY arrived (consult condition)
    }

    [Fact]
    public async Task Panic_Live_DuringRealInFlightOperation_TypedCancelled_BoundedDrain_ClientGone()
    {
        using var h = new Harness();
        await h.SelectAndProbeAsync();
        h.Lab.Inject(AiLabMode.HangStream);

        var operation = h.Pipeline.RunInteractiveAsync(Request);
        await WaitForAsync(() => h.Provider.BytesReadSoFar > 0, "a real in-flight network operation");

        var started = Environment.TickCount64;
        await h.Pipeline.PanicAsync(TimeSpan.FromSeconds(2));
        var result = await operation;
        var elapsed = Environment.TickCount64 - started;

        Assert.IsType<OperationOutcome.Cancelled>(result.Outcome);
        Assert.Null(result.Reply);
        Assert.True(elapsed < 2000 + 1500, $"panic drain must be bounded, took {elapsed}ms");
        var record = await WaitForRecordAsync(h.Lab, AiLabMode.HangStream);
        Assert.Equal("client-gone", record.Outcome);
    }

    [Fact]
    public async Task RemoteHost_InProduct_PreSocketRejection_BothLayers_ZeroSendAttempts()
    {
        using var h = new Harness(host: new Uri("http://192.168.1.50:11434/"));
        await h.SelectAndProbeAsync();

        // Probe layer: typed rejection with ZERO socket contact.
        var state = h.Capabilities.GetState(AiOperationPipeline.CapabilityName(AiProviderId.LocalOllama));
        var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);
        Assert.Equal(AiReplyCodes.EndpointNotAdmitted, unavailable.Reason.Code);

        // Pipeline layer: capability never Available → provider-unproven, pre-socket.
        var result = await h.Pipeline.RunInteractiveAsync(Request);
        var reply = Assert.IsType<AiReply.Unavailable>(result.Reply);
        Assert.Equal(AiReplyCodes.ProviderUnproven, reply.Code);
        Assert.Equal(0, h.Pipeline.SendAttempts);
        Assert.Equal(0, h.Provider.SendAttempts);

        // Provider layer (defense in depth): even invoked directly, no socket opens.
        var direct = await h.Provider.CompleteAsync(Request, CancellationToken.None);
        Assert.Equal(AiReplyCodes.EndpointNotAdmitted, Assert.IsType<AiReply.Unavailable>(direct).Code);
        Assert.Equal(0, h.Provider.SendAttempts);
    }

    [Fact]
    public async Task Offline_ReVerified_ZeroNetwork_LabBoundLoopbackOnly()
    {
        using var h = new Harness();
        // Provider registered but NEVER probed: selected-but-unproven → no traffic (contract §11).
        h.Pipeline.SelectProvider(AiProviderId.LocalOllama);

        var interactive = await h.Pipeline.RunInteractiveAsync(Request);
        var awareness = await h.Pipeline.RunAwarenessAsync(Request, awarenessConsent: true);

        Assert.Equal(AiReplyCodes.ProviderUnproven, Assert.IsType<AiReply.Unavailable>(interactive.Reply).Code);
        Assert.Equal(AiReplyCodes.ProviderUnproven, Assert.IsType<AiReply.Unavailable>(awareness.Reply).Code);
        Assert.Equal(0, h.Pipeline.SendAttempts);
        Assert.Equal(0, h.Provider.SendAttempts);
        Assert.Equal(0, h.Lab.HitCount); // not even the lab was touched
        Assert.True(h.Lab.Host.IsLoopback); // the lab binds 127.0.0.1 only — zero external network by construction
    }
}
