using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Capabilities;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Provider-level unit tests for the first REAL provider (SP-035 slice c2;
/// ai-companion-admission.md §8 c2). Every row runs against the in-process deterministic
/// loopback lab (real sockets on 127.0.0.1, zero external network): request round-trip,
/// timeout classification (bounded, token-NOT-cancelled disambiguation), bounded retry
/// (placeholder shape; 429 honors Retry-After; 500 bounded; refusal exactly 1 attempt;
/// retry DEFAULT is off), malformed/truncated never partial, remote-host pre-socket
/// rejection with SendAttempts==0, probe semantics (loopback up/down, remote zero-socket),
/// and true mid-stream cancellation with a partial-body position.
/// </summary>
public class LoopbackOllamaProviderTests
{
    private static readonly AiRequest Request = new("lab-prompt");

    private static LoopbackOllamaProvider Provider(AiProviderLab lab, AiRetryPolicy? retry = null) =>
        new(new LoopbackOllamaProviderOptions
        {
            Host = lab.Host,
            RequestTimeout = TestWait.InjectedBudget, // SP-063: never decides an outcome
            ProbeTimeout = TestWait.InjectedBudget,
            Retry = retry ?? AiRetryPolicy.Off,
        });

    [Fact]
    public async Task Ok_RoundTrip_Generated_WithLoopbackProvenance()
    {
        using var lab = new AiProviderLab();
        var provider = Provider(lab);
        lab.Inject(AiLabMode.Ok);

        var reply = await provider.CompleteAsync(Request, CancellationToken.None);

        var generated = Assert.IsType<AiReply.Generated>(reply);
        Assert.Equal(lab.OkReplyText, generated.Text);
        Assert.Equal(AiEndpointClass.Loopback, generated.Provenance);
        Assert.Equal(1, provider.SendAttempts);
        Assert.Equal(1, lab.HitCount);
    }

    [Fact]
    public async Task Timeout_Classifier_Bounded_ExternalTokenNotCancelled()
    {
        using var lab = new AiProviderLab();
        var provider = new LoopbackOllamaProvider(new LoopbackOllamaProviderOptions
        {
            Host = lab.Host,
            RequestTimeout = TimeSpan.FromMilliseconds(800), // wallclock-allow: the budget's elapsing IS the subject — timeout classification must fire, bounded, without cancelling the external token
        });
        lab.Inject(AiLabMode.Timeout);
        using var cts = new CancellationTokenSource();

        var started = TestWait.MonotonicNow();
        var reply = await provider.CompleteAsync(Request, cts.Token);
        var elapsed = TestWait.MonotonicNow() - started;

        var unavailable = Assert.IsType<AiReply.Unavailable>(reply);
        Assert.Equal(AiReplyCodes.Timeout, unavailable.Code);
        Assert.True(elapsed < 800 + 2500, $"timeout classification must be bounded, took {elapsed}ms");
        Assert.False(cts.IsCancellationRequested); // timeout is a CLASSIFIER, never the cancellation mechanism
        Assert.Equal(1, provider.SendAttempts);
    }

    [Fact]
    public async Task Rate429_RetryEnabled_ExactlyTwoHits_RetryAfterHonored()
    {
        using var lab = new AiProviderLab();
        var provider = Provider(lab, AiRetryPolicy.WpfObservedPlaceholder);
        lab.Inject(AiLabMode.Rate429, AiLabMode.Rate429);

        var started = TestWait.MonotonicNow();
        var reply = await provider.CompleteAsync(Request, CancellationToken.None);
        var elapsed = TestWait.MonotonicNow() - started;

        var unavailable = Assert.IsType<AiReply.Unavailable>(reply);
        Assert.Equal(AiReplyCodes.QuotaExhausted, unavailable.Code);
        Assert.Equal(2, lab.HitsFor(AiLabMode.Rate429)); // exactly one bounded retry — no storm
        Assert.True(elapsed >= 900, $"Retry-After: 1 must be honored, gap was {elapsed}ms");
        Assert.Equal(2, provider.SendAttempts);
    }

    [Fact]
    public async Task Rate429_RetryDefaultOff_ExactlyOneHit()
    {
        using var lab = new AiProviderLab();
        var provider = Provider(lab); // default: AiRetryPolicy.Off (conservative posture)
        lab.Inject(AiLabMode.Rate429);

        var reply = await provider.CompleteAsync(Request, CancellationToken.None);

        var unavailable = Assert.IsType<AiReply.Unavailable>(reply);
        Assert.Equal(AiReplyCodes.QuotaExhausted, unavailable.Code);
        Assert.Equal(1, lab.HitsFor(AiLabMode.Rate429));
        Assert.Equal(1, provider.SendAttempts);
    }

    [Fact]
    public async Task Error500_RetryEnabled_Bounded_TwoHits()
    {
        using var lab = new AiProviderLab();
        var provider = Provider(lab, AiRetryPolicy.WpfObservedPlaceholder);
        lab.Inject(AiLabMode.Error500, AiLabMode.Error500);

        var reply = await provider.CompleteAsync(Request, CancellationToken.None);

        var unavailable = Assert.IsType<AiReply.Unavailable>(reply);
        Assert.Equal("http-500", unavailable.Code);
        Assert.Equal(2, lab.HitsFor(AiLabMode.Error500));
    }

    [Fact]
    public async Task Other4xx_NeverRetried_ExactlyOneHit()
    {
        using var lab = new AiProviderLab();
        var provider = Provider(lab, AiRetryPolicy.WpfObservedPlaceholder); // retry on; other-4xx still not retried
        lab.Inject(AiLabMode.NotFound404);

        var reply = await provider.CompleteAsync(Request, CancellationToken.None);

        var unavailable = Assert.IsType<AiReply.Unavailable>(reply);
        Assert.Equal("http-404", unavailable.Code);
        Assert.Equal(1, lab.HitsFor(AiLabMode.NotFound404));
        Assert.Equal(1, provider.SendAttempts);
    }

    [Fact]
    public async Task Refusal_TypedCarrier_ExactlyOneAttempt_NeverRetried()
    {
        using var lab = new AiProviderLab();
        var provider = Provider(lab, AiRetryPolicy.WpfObservedPlaceholder); // retry on, refusal still not retried
        lab.Inject(AiLabMode.Refusal);

        var reply = await provider.CompleteAsync(Request, CancellationToken.None);

        var refused = Assert.IsType<AiReply.Refused>(reply);
        Assert.Equal("content_filter", refused.Refusal.CategoryCode);
        Assert.Equal(AiModerationSource.Output, refused.Refusal.Source);
        Assert.Equal(1, lab.HitsFor(AiLabMode.Refusal));
        Assert.Equal(1, provider.SendAttempts);
    }

    [Fact]
    public async Task Malformed_Garbage_NeverPartial_TypedUnavailable()
    {
        using var lab = new AiProviderLab();
        var provider = Provider(lab);
        lab.Inject(AiLabMode.Malformed);

        var reply = await provider.CompleteAsync(Request, CancellationToken.None);

        var unavailable = Assert.IsType<AiReply.Unavailable>(reply);
        Assert.Equal(AiReplyCodes.MalformedOutput, unavailable.Code);
        Assert.Equal(1, lab.HitsFor(AiLabMode.Malformed));
    }

    [Fact]
    public async Task Truncated_PrefixCut_NeverSurfaced_TypedUnavailable()
    {
        using var lab = new AiProviderLab();
        var provider = Provider(lab);
        lab.Inject(AiLabMode.Truncated);

        var reply = await provider.CompleteAsync(Request, CancellationToken.None);

        // The valid prefix carries a partial reply text — it must NEVER be surfaced.
        var unavailable = Assert.IsType<AiReply.Unavailable>(reply);
        Assert.Equal(AiReplyCodes.MalformedOutput, unavailable.Code);
        Assert.Equal(1, lab.HitsFor(AiLabMode.Truncated));
    }

    [Fact]
    public async Task RemoteHost_RejectedPreSocket_ZeroSendAttempts()
    {
        var provider = new LoopbackOllamaProvider(new LoopbackOllamaProviderOptions
        {
            Host = new Uri("http://192.168.1.50:11434/"),
        });
        Assert.Equal(AiEndpointClass.RemoteHostOllama, provider.Descriptor.EndpointClass);

        var started = TestWait.MonotonicNow();
        var reply = await provider.CompleteAsync(Request, CancellationToken.None);
        var elapsed = TestWait.MonotonicNow() - started;

        var unavailable = Assert.IsType<AiReply.Unavailable>(reply);
        Assert.Equal(AiReplyCodes.EndpointNotAdmitted, unavailable.Code);
        Assert.Equal(0, provider.SendAttempts); // rejection BEFORE any socket
        Assert.True(elapsed < 1000, $"pre-socket rejection must not touch the network, took {elapsed}ms");
    }

    [Fact]
    public async Task Probe_LoopbackUp_Available_HonestlyScopedDetail()
    {
        using var lab = new AiProviderLab();
        var provider = Provider(lab);

        var state = await provider.Probe!(CancellationToken.None);

        var available = Assert.IsType<CapabilityState.Available>(state);
        Assert.Contains("model presence unproven", available.Detail); // never overclaim model existence
    }

    [Fact]
    public async Task Probe_LoopbackDown_TypedUnavailable_HostUnreachable()
    {
        var provider = new LoopbackOllamaProvider(new LoopbackOllamaProviderOptions
        {
            Host = new Uri("http://127.0.0.1:1/"), // loopback, connection refused fast
        });

        var state = await provider.Probe!(CancellationToken.None);

        var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);
        Assert.Equal(CapabilityReasonCodes.HostUnreachable, unavailable.Reason.Code);
    }

    [Fact]
    public async Task Probe_RemoteHost_ZeroSocket_TypedRejection()
    {
        var provider = new LoopbackOllamaProvider(new LoopbackOllamaProviderOptions
        {
            Host = new Uri("http://192.168.1.50:11434/"),
        });

        var started = TestWait.MonotonicNow();
        var state = await provider.Probe!(CancellationToken.None);
        var elapsed = TestWait.MonotonicNow() - started;

        // Classification runs BEFORE any socket: probing a remote host would itself be
        // undeclared remote traffic. The typed code only the pre-socket branch produces.
        var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);
        Assert.Equal(AiReplyCodes.EndpointNotAdmitted, unavailable.Reason.Code);
        Assert.True(elapsed < 1000, $"remote probe must not touch the network, took {elapsed}ms");
    }

    [Fact]
    public async Task MidStreamCancel_TruePartialPosition_TokenIsTheMechanism()
    {
        using var lab = new AiProviderLab();
        var provider = Provider(lab); // the injected budget would also fire eventually — cancel must win first
        lab.Inject(AiLabMode.HangStream);
        using var cts = new CancellationTokenSource();

        var task = provider.CompleteAsync(Request, cts.Token);
        // Class 2 (SP-059): first bytes over a REAL loopback socket — the tolerant window
        // with the loud classifier via the single approved helper.
        await TestWait.Until(
            () => provider.BytesReadSoFar > 0,
            "a TRUE mid-stream position (partial body) before cancel",
            () => $"bytes={provider.BytesReadSoFar} sends={provider.SendAttempts} labHits={lab.HitCount}",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(provider.BytesReadSoFar > 0, "a TRUE mid-stream position (partial body) must be observed before cancel");
        var started = TestWait.MonotonicNow();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        var elapsed = TestWait.MonotonicNow() - started;
        Assert.True(elapsed < 2000, $"token cancellation must be fast (no hang), took {elapsed}ms");
        Assert.True(cts.IsCancellationRequested);
    }
}
