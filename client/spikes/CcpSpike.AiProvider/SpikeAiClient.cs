using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CcpClient.Desktop.Ai;

namespace CcpSpike.AiProvider;

/// <summary>Terminal classification of a spike provider operation (mirrors the contract's OperationOutcome mapping).</summary>
public enum SpikeOutcomeKind
{
    Completed,
    Cancelled,
    PolicyRejected,   // non-loopback endpoint refused BEFORE any socket (the remote-host policy test)
    Timeout,
    RateLimited,
    ServerError,
    Refused,
    MalformedOutput,
    TransportError,
}

/// <summary>One spike provider operation result: typed outcome + content-free diagnostics. Never carries payload text.</summary>
public sealed record SpikeResult(
    SpikeOutcomeKind Kind,
    AiReply? Reply,
    int Generation,
    long DurationMs,
    int LabHits,
    int PartialBodyBytes,
    AiDiagnosticRecord Diagnostic);

/// <summary>
/// Cancellable spike provider client against the fake OpenAI-compatible loopback lab.
/// Implements the SP-016 contract mechanics under test:
///  - endpoint admission policy (spike-local, allow-list values pending-owner): ONLY
///    AiEndpointClass.Loopback is admitted; anything else is rejected BEFORE any socket
///    opens (proven by <see cref="SendAttempts"/> staying 0).
///  - SP-004 generation discipline: the caller advances <see cref="CurrentGeneration"/>;
///    a completion applied for a stale generation is discarded at the application seam
///    (<see cref="ApplyResult"/>) with an explicit stale-discard record.
///  - typed outcomes per contract §1/§11; per-request timeout is a failure classifier,
///    never the cancellation mechanism (token-cancelled vs timeout-fired disambiguated
///    via the linked-CTS source, cross-checked against TaskCanceledException.InnerException).
///  - bounded retry: exactly ONE retry on 429/5xx (the WPF-observed policy shape,
///    OpenAiCompatibleService.cs:425-427), Retry-After honored clamped to ≤2s; NEVER a
///    retry on parse/refusal/other-4xx. Lab hit counts prove no retry-storm.
///  - content-free AiDiagnosticRecord per operation (contract §12).
/// </summary>
public sealed class SpikeAiClient
{
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(2);

    private readonly HttpClient _http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan }; // per-request linked CTS owns the timeout
    private readonly string _apiKey;
    private readonly TimeSpan _requestTimeout;
    private readonly AiEnvelopePolicy _policy;

    public Uri Endpoint { get; }
    public AiEndpointClass EndpointClass { get; }

    /// <summary>Send-seam instrumentation: incremented IMMEDIATELY before HttpClient.SendAsync. Policy-rejected operations must leave this at 0.</summary>
    public int SendAttempts { get; private set; }

    /// <summary>The current application generation (SP-004). Advanced by the harness on cancel/switch.</summary>
    public int CurrentGeneration { get; private set; }

    /// <summary>Results actually APPLIED at the application seam (must be 0 for cancelled/stale operations).</summary>
    public int AppliedResults { get; private set; }

    /// <summary>Stale completions discarded at the application seam.</summary>
    public int StaleDiscards { get; private set; }

    public SpikeAiClient(Uri endpoint, string apiKey, TimeSpan requestTimeout, AiEnvelopePolicy? policy = null)
    {
        Endpoint = endpoint;
        EndpointClass = AiEndpointClassifier.ClassifyProviderEndpoint(endpoint);
        _apiKey = apiKey;
        _requestTimeout = requestTimeout;
        _policy = policy ?? AiEnvelopePolicy.PermitAll;
        Redact.Register("apikey", apiKey);
    }

    public int AdvanceGeneration() => ++CurrentGeneration;

    /// <summary>
    /// One provider operation. Returns a typed result; never throws for provider/parse
    /// failures. Cancellation of <paramref name="ct"/> is the ONLY cancellation mechanism.
    /// </summary>
    public async Task<SpikeResult> RequestAsync(string userText, int generation, CancellationToken ct)
    {
        var started = Environment.TickCount64;
        var labHitsBefore = SendAttempts;
        Redact.Register("prompt", userText);

        // Endpoint admission policy: classification is config-pure (contract §6 rule 2).
        // Non-loopback is rejected BEFORE any socket opens — SendAttempts must stay 0.
        if (EndpointClass != AiEndpointClass.Loopback)
        {
            var d0 = Diagnostic(AiDiagnosticOutcome.Unavailable, "endpoint-policy-rejected", generation, started, 0, []);
            SpikeLog.Line("client", $"gen={generation} POLICY-REJECTED class={EndpointClass} (no socket; sendAttempts={SendAttempts})");
            return new SpikeResult(SpikeOutcomeKind.PolicyRejected,
                new AiReply.Unavailable("endpoint-policy-rejected"), generation, Elapsed(started), 0, 0, d0);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(_requestTimeout);

        const int maxAttempts = 2; // initial + exactly one bounded retry (429/5xx only)
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var msg = new HttpRequestMessage(HttpMethod.Post, new Uri(Endpoint, "chat/completions"));
                msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                msg.Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        model = "spike-model",
                        messages = new[] { new { role = "user", content = userText } },
                        stream = false,
                    }),
                    Encoding.UTF8, "application/json");

                SendAttempts++;
                using var resp = await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, linked.Token);
                var status = (int)resp.StatusCode;

                if (status == 429 || status >= 500)
                {
                    var kind = status == 429 ? SpikeOutcomeKind.RateLimited : SpikeOutcomeKind.ServerError;
                    var code = status == 429 ? AiReplyCodes.QuotaExhausted : $"http-{status}";
                    if (attempt < maxAttempts)
                    {
                        var delay = MaxRetryAfter;
                        if (resp.Headers.RetryAfter?.Delta is { } ra && ra < delay) delay = ra;
                        SpikeLog.Line("client", $"gen={generation} attempt={attempt} status={status} backoff={delay.TotalMilliseconds:F0}ms (bounded retry 1/1)");
                        await Task.Delay(delay, linked.Token);
                        continue;
                    }
                    var d1 = Diagnostic(AiDiagnosticOutcome.Unavailable, code, generation, started, 0, []);
                    SpikeLog.Line("client", $"gen={generation} {kind} after retry cap (attempts={attempt})");
                    return new SpikeResult(kind, new AiReply.Unavailable(code), generation, Elapsed(started), SendAttempts - labHitsBefore, 0, d1);
                }

                if (status != 200)
                {
                    var d2 = Diagnostic(AiDiagnosticOutcome.Unavailable, $"http-{status}", generation, started, 0, []);
                    return new SpikeResult(SpikeOutcomeKind.TransportError, new AiReply.Unavailable($"http-{status}"), generation, Elapsed(started), SendAttempts - labHitsBefore, 0, d2);
                }

                // Read the body as a stream: mid-stream cancellation must observe the token,
                // and partial-body bytes prove a TRUE mid-stream position on the hang row.
                var body = await ReadBody(resp, linked.Token);
                return Complete(body.text, body.bytes, generation, started, labHitsBefore);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // External cancellation — the ONLY cancellation mechanism (contract §2 rule 1).
                var d3 = Diagnostic(AiDiagnosticOutcome.Cancelled, null, generation, started, 0, []);
                SpikeLog.Line("client", $"gen={generation} CANCELLED by token (attempt in-flight)");
                return new SpikeResult(SpikeOutcomeKind.Cancelled, null, generation, Elapsed(started), SendAttempts - labHitsBefore, 0, d3);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // Linked CTS fired without external cancellation = per-request TIMEOUT
                // (a failure classifier, never the cancellation mechanism).
                var d4 = Diagnostic(AiDiagnosticOutcome.Unavailable, AiReplyCodes.Timeout, generation, started, 0, []);
                SpikeLog.Line("client", $"gen={generation} TIMEOUT (bounded wait {_requestTimeout.TotalMilliseconds:F0}ms, no hang)");
                return new SpikeResult(SpikeOutcomeKind.Timeout, new AiReply.Unavailable(AiReplyCodes.Timeout), generation, Elapsed(started), SendAttempts - labHitsBefore, 0, d4);
            }
            catch (HttpRequestException)
            {
                var d5 = Diagnostic(AiDiagnosticOutcome.Unavailable, AiReplyCodes.Offline, generation, started, 0, []);
                return new SpikeResult(SpikeOutcomeKind.TransportError, new AiReply.Unavailable(AiReplyCodes.Offline), generation, Elapsed(started), SendAttempts - labHitsBefore, 0, d5);
            }
        }
        throw new InvalidOperationException("unreachable: retry loop always returns");
    }

    /// <summary>
    /// Detached completion path (the dual-transport stale proof): a request that IGNORES the
    /// caller's token and completes after the generation may have advanced. The application
    /// seam — never the transport — is where stale results die (SP-004 §3 rule 2).
    /// </summary>
    public Task<SpikeResult> RequestDetachedAsync(string userText, int generation) =>
        RequestAsync(userText, generation, CancellationToken.None);

    /// <summary>
    /// The application seam (SP-004 §3 rule 2): a completion for a stale generation is
    /// discarded with an explicit record; only current-generation results apply.
    /// </summary>
    public bool ApplyResult(SpikeResult result)
    {
        if (result.Generation != CurrentGeneration)
        {
            StaleDiscards++;
            SpikeLog.Line("client", $"STALE-DISCARD gen={result.Generation} current={CurrentGeneration} kind={result.Kind} — late result NOT applied");
            return false;
        }
        AppliedResults++;
        SpikeLog.Line("client", $"applied gen={result.Generation} kind={result.Kind}");
        return true;
    }

    private SpikeResult Complete(string body, int partialBytes, int generation, long started, int labHitsBefore)
    {
        // Shape discrimination by explicit fields, never string-sniffing: provider-refusal
        // shape first, then the strict envelope validator (SP-016 real code path).
        try
        {
            using var doc = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("refusal", out var r))
            {
                var category = r.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String
                    ? c.GetString()! : "unknown";
                var refusal = new AiModerationRefusal(category, AiModerationSource.Output);
                var dr = Diagnostic(AiDiagnosticOutcome.Refused, category, generation, started, 0, []);
                SpikeLog.Line("client", $"gen={generation} REFUSED category={category} (typed refusal, deterministic shape)");
                return new SpikeResult(SpikeOutcomeKind.Refused, new AiReply.Refused(refusal), generation, Elapsed(started), SendAttempts - labHitsBefore, partialBytes, dr);
            }
        }
        catch (JsonException)
        {
            var dm = Diagnostic(AiDiagnosticOutcome.Unavailable, AiReplyCodes.MalformedOutput, generation, started, 0, []);
            SpikeLog.Line("client", $"gen={generation} MALFORMED output — typed parse outcome, no partial apply");
            return new SpikeResult(SpikeOutcomeKind.MalformedOutput, new AiReply.Unavailable(AiReplyCodes.MalformedOutput), generation, Elapsed(started), SendAttempts - labHitsBefore, partialBytes, dm);
        }

        // Strict envelope path: SP-016's real validator. Rejected output = typed outcome,
        // NEVER a partial apply (no repair, no salvage — contract §8 rule 2).
        var result = AiEnvelopeValidator.Validate(body, _policy);
        if (!result.Accepted)
        {
            var dp = Diagnostic(AiDiagnosticOutcome.Unavailable, AiReplyCodes.MalformedOutput, generation, started,
                result.Verdicts.Count, result.Verdicts.Select(AiDiagnosticCodes.VerdictCode).ToArray());
            SpikeLog.Line("client", $"gen={generation} envelope-rejected code={result.EnvelopeRejectionCode} — no partial apply");
            return new SpikeResult(SpikeOutcomeKind.MalformedOutput, new AiReply.Unavailable(AiReplyCodes.MalformedOutput), generation, Elapsed(started), SendAttempts - labHitsBefore, partialBytes, dp);
        }

        var dg = Diagnostic(AiDiagnosticOutcome.Completed, null, generation, started,
            result.Verdicts.Count, result.Verdicts.Select(AiDiagnosticCodes.VerdictCode).ToArray());
        SpikeLog.Line("client", $"gen={generation} COMPLETED reply=present(len={(result.Reply?.Length ?? 0)}) commands={result.Plan?.Commands.Count ?? 0}");
        return new SpikeResult(SpikeOutcomeKind.Completed,
            new AiReply.Generated(result.Reply ?? "", AiEndpointClass.Loopback), generation, Elapsed(started), SendAttempts - labHitsBefore, partialBytes, dg);
    }

    private static async Task<(string text, int bytes)> ReadBody(HttpResponseMessage resp, CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[4096];
        int n;
        while ((n = await stream.ReadAsync(buf, ct)) > 0)
            ms.Write(buf, 0, n);
        return (Encoding.UTF8.GetString(ms.ToArray()), (int)ms.Length);
    }

    private AiDiagnosticRecord Diagnostic(AiDiagnosticOutcome outcome, string? code, int generation, long started, int commandCount, string[] verdictCodes) =>
        new(AiOperationClass.Interactive, EndpointClass, outcome, code, generation, Elapsed(started), commandCount, verdictCodes);

    private static long Elapsed(long started) => Environment.TickCount64 - started;
}
