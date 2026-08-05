using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Ai;

/// <summary>
/// Bounded-retry policy for the loopback Ollama provider (admission §9.2 #6 — the VALUES
/// are owner-pending; this is the typed placeholder SHAPE). WPF-observed shape:
/// <c>OpenAiCompatibleService.cs:425-427</c> — exactly one retry, 429/5xx only, fixed
/// 1200ms; SP-019 refined the delay to Retry-After honored clamped ≤2s (spike limit 8:
/// placeholder, not owner-approved). DEFAULT IS OFF (conservative posture, SP-035
/// pre-approach consult; the c6 none-admitted-default precedent): no retry happens unless
/// a policy is explicitly enabled.
/// </summary>
public sealed record AiRetryPolicy(bool Enabled, int MaxRetries, TimeSpan MaxDelay)
{
    /// <summary>No retries (the default — conservative posture; §9.2 #6 owner-pending).</summary>
    public static readonly AiRetryPolicy Off = new(false, 0, TimeSpan.Zero);

    /// <summary>
    /// The WPF-observed placeholder shape (§9.2 #6, NOT owner-approved values): exactly one
    /// bounded retry on 429/5xx, Retry-After honored clamped to ≤2s (SP-019 items 3-4:
    /// exactly-2-hits, no retry-storm).
    /// </summary>
    public static readonly AiRetryPolicy WpfObservedPlaceholder = new(true, 1, TimeSpan.FromSeconds(2));
}

/// <summary>Options for <see cref="LoopbackOllamaProvider"/>. Values are placeholders pending the §9.2 ledger where noted.</summary>
public sealed record LoopbackOllamaProviderOptions
{
    /// <summary>Ollama host (WPF default when blank: <c>http://localhost:11434/</c> — LocalAiService.cs:515-517).</summary>
    public Uri Host { get; init; } = new("http://localhost:11434/");

    /// <summary>Model name (WPF default: <c>qwen3.5:latest</c> — LocalAiService.cs:403).</summary>
    public string Model { get; init; } = "qwen3.5:latest";

    /// <summary>
    /// Per-request timeout — a FAILURE CLASSIFIER, never the cancellation mechanism
    /// (contract §2 rule 1). Default 5 min = the WPF-observed LOCAL value
    /// (LocalAiService.cs:341,412 — cold model-load accommodation); placeholder, not
    /// owner-approved. Tests override to sub-second bounds.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Probe timeout — short and DISTINCT from <see cref="RequestTimeout"/> so a long request bound never wedges startup probing.</summary>
    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Bounded-retry placeholder (typed; §9.2 #6). Default <see cref="AiRetryPolicy.Off"/>.</summary>
    public AiRetryPolicy Retry { get; init; } = AiRetryPolicy.Off;
}

/// <summary>
/// The first REAL provider (admission §8 slice c2): a loopback Ollama host speaking the
/// WPF-observed NATIVE protocol — <c>POST {host}api/chat</c>, payload
/// <c>{model, messages, stream:false, think:false}</c> (LocalAiService.cs:374-390, :23-24),
/// with the request's history turns (if any) rendered oldest-first before the final user
/// message (SP-047 memory→prompt assembly), reply extracted from <c>message.content</c>.
/// Semantics:
/// - Cancellation: the caller's token is the ONLY cancellation mechanism. The body is read
///   as a stream (<see cref="HttpCompletionOption.ResponseHeadersRead"/>) so a mid-stream
///   cancel has a true partial-body position (<see cref="BytesReadSoFar"/>); OCE from the
///   external token propagates (the pipeline types it Cancelled).
/// - Timeout: linked-CTS <c>CancelAfter</c> with <c>HttpClient.Timeout = Infinite</c>;
///   disambiguation by token ORIGIN (external-cancelled vs timeout-fired), never
///   exception-type guessing (SP-019 item 2). Timeout → <see cref="AiReply.Unavailable"/>
///   (<c>timeout</c>); the external token is never cancelled by a timeout.
/// - Retry: <see cref="AiRetryPolicy"/> placeholder — 429/5xx only, Retry-After honored
///   clamped to ≤<see cref="AiRetryPolicy.MaxDelay"/>; never on refusal/parse/other-4xx.
/// - Refusal: deterministic shape discrimination (a top-level <c>refusal</c> object with a
///   string <c>category</c>) → typed <see cref="AiReply.Refused"/> carrier, exactly one
///   attempt, NO string-sniffing (contract §7 rule 3).
/// - Malformed/truncated: the full document parses BEFORE any text is extracted; any parse
///   or shape failure → <see cref="AiReply.Unavailable"/> (<c>malformed-output</c>) — never
///   a partial apply, a truncated prefix is never surfaced (SP-019 item 6).
/// - Remote hosts: a non-loopback host is rejected BEFORE any socket opens — both in the
///   probe (probing a remote host would itself be undeclared remote traffic) and in
///   <see cref="CompleteAsync"/> (<see cref="SendAttempts"/> stays 0; the pipeline's
///   admission policy is the first layer, this is defense in depth).
/// - Diagnostics: NONE. This type emits no log lines and no text; outcomes ride the typed
///   <see cref="AiReply"/> and the pipeline's content-free <see cref="AiDiagnosticRecord"/>.
///
/// Lifetime/instrument notes (pre-completion consult): the <see cref="HttpClient"/> is an
/// app-lifetime singleton by design (reuse is the documented .NET pattern; no per-operation
/// sockets are leaked). <see cref="SendAttempts"/> and <see cref="BytesReadSoFar"/> are
/// SINGLE-OPERATION test instruments — not concurrency-safe across simultaneous operations;
/// the pipeline's own gateway counter is the product-grade offline proof.
/// </summary>
public sealed class LoopbackOllamaProvider : IAiProvider
{
    private readonly LoopbackOllamaProviderOptions _options;
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan }; // per-request linked CTS owns the bound
    private int _sendAttempts;

    public LoopbackOllamaProvider(LoopbackOllamaProviderOptions? options = null)
    {
        _options = options ?? new LoopbackOllamaProviderOptions();
        Descriptor = new AiProviderDescriptor(
            AiProviderId.LocalOllama, AiEndpointClassifier.ClassifyOllamaHost(_options.Host));
    }

    public AiProviderDescriptor Descriptor { get; }

    /// <summary>Provider-side send-seam instrument: incremented IMMEDIATELY before each socket write. Pre-socket rejections leave this at 0 (SP-019 item 7 discipline, in-product).</summary>
    public int SendAttempts => Volatile.Read(ref _sendAttempts);

    /// <summary>Body bytes read so far by the in-flight request (true mid-stream position proof; content-free).</summary>
    public int BytesReadSoFar { get; private set; }

    /// <summary>
    /// The SP-006 probe — the ONLY availability authority. Classification is config-pure
    /// and runs FIRST: a non-loopback host yields typed Unavailable with ZERO socket
    /// contact. A loopback host is probed with <c>GET {host}api/version</c> (the SP-019
    /// item-8 probe URL) under a short bound; Available iff HTTP 200, with the Detail
    /// honestly scoped (service reachable; MODEL PRESENCE UNPROVEN — no /api/tags, no pull).
    /// </summary>
    public Func<CancellationToken, Task<CapabilityState>>? Probe => ProbeCoreAsync;

    public async Task<AiReply> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Pre-socket rejection (defense in depth behind the pipeline's admission policy):
        // a non-loopback host NEVER gets a socket from this provider.
        if (Descriptor.EndpointClass != AiEndpointClass.Loopback)
        {
            return new AiReply.Unavailable(AiReplyCodes.EndpointNotAdmitted);
        }

        BytesReadSoFar = 0;

        var maxAttempts = 1 + (_options.Retry.Enabled ? Math.Max(0, _options.Retry.MaxRetries) : 0);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // The timeout bound is PER ATTEMPT (the WPF-observed shape: HttpClient.Timeout
            // governs each SendAsync; the retry backoff is never counted against it).
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(_options.RequestTimeout);
            try
            {
                using var msg = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.Host, "api/chat"))
                {
                    Content = new StringContent(BuildPayload(request), Encoding.UTF8, "application/json"),
                };

                Interlocked.Increment(ref _sendAttempts);
                using var resp = await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
                var status = (int)resp.StatusCode;

                if (status == 429 || status >= 500)
                {
                    if (attempt < maxAttempts)
                    {
                        // Bounded retry (placeholder shape): Retry-After honored, clamped
                        // to ≤ MaxDelay; WPF's fixed 1200ms when no header is present
                        // (OpenAiCompatibleService.cs:425-427 + SP-019 refinement).
                        var delay = TimeSpan.FromMilliseconds(1200);
                        if (resp.Headers.RetryAfter?.Delta is { } retryAfter)
                        {
                            delay = retryAfter;
                        }

                        if (delay > _options.Retry.MaxDelay)
                        {
                            delay = _options.Retry.MaxDelay;
                        }

                        // Backoff observes ONLY the external token: cancellation still
                        // unblocks instantly; the per-attempt timeout does not apply here.
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    return new AiReply.Unavailable(status == 429 ? AiReplyCodes.QuotaExhausted : $"http-{status}");
                }

                if (status != 200)
                {
                    return new AiReply.Unavailable($"http-{status}");
                }

                var body = await ReadBodyAsync(resp, linked.Token).ConfigureAwait(false);
                return Classify(body);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // external cancellation — the ONLY cancellation mechanism (contract §2 rule 1)
            }
            catch (OperationCanceledException)
            {
                // The linked CTS fired without external cancellation = per-request TIMEOUT
                // (a failure classifier; the external token is NOT cancelled).
                return new AiReply.Unavailable(AiReplyCodes.Timeout);
            }
            catch (HttpRequestException)
            {
                return new AiReply.Unavailable(AiReplyCodes.Offline);
            }
        }

        throw new InvalidOperationException("unreachable: the retry loop always returns");
    }

    private async Task<CapabilityState> ProbeCoreAsync(CancellationToken cancellationToken)
    {
        if (Descriptor.EndpointClass != AiEndpointClass.Loopback)
        {
            // Pre-socket: probing a remote host would itself be undeclared remote traffic.
            return new CapabilityState.Unavailable(new CapabilityReason(
                AiReplyCodes.EndpointNotAdmitted,
                $"non-loopback Ollama host classified {Descriptor.EndpointClass}; rejected before any socket"));
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(_options.ProbeTimeout);
        try
        {
            using var resp = await _http.GetAsync(new Uri(_options.Host, "api/version"), linked.Token).ConfigureAwait(false);
            return (int)resp.StatusCode == 200
                ? new CapabilityState.Available("ollama-shaped HTTP service reachable at loopback (api/version 200); model presence unproven")
                : new CapabilityState.Unavailable(new CapabilityReason(
                    CapabilityReasonCodes.HostUnreachable, $"api/version returned HTTP {(int)resp.StatusCode}"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // startup/teardown cancellation — the probe runner types this honestly
        }
        catch (OperationCanceledException)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                CapabilityReasonCodes.HostUnreachable, "api/version probe timed out (bounded)"));
        }
        catch (HttpRequestException)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                CapabilityReasonCodes.HostUnreachable, "api/version connection failed"));
        }
    }

    private string BuildPayload(AiRequest request)
    {
        // History turns render OLDEST FIRST before the final user message (the WPF
        // outgoing-list order, LocalAiService.cs:531-548, minus system/enrichment — the
        // greenfield request shape carries neither). Null/empty history ⇒ the pre-SP-047
        // single-message payload, byte-identical.
        var history = request.History;
        var messages = history is { Count: > 0 }
            ? history.Select(t => new { role = t.Role == AiMemoryRole.Assistant ? "assistant" : "user", content = t.Text })
                .Append(new { role = "user", content = request.Prompt }).ToArray()
            : [new { role = "user", content = request.Prompt }];
        return JsonSerializer.Serialize(new
        {
            model = _options.Model,
            messages,
            stream = false,
            think = false,
        });
    }

    private async Task<string> ReadBodyAsync(HttpResponseMessage resp, CancellationToken cancellationToken)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            ms.Write(buffer, 0, read);
            BytesReadSoFar += read;
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Shape discrimination by explicit fields, never string-sniffing: the deterministic
    /// refusal shape first, then the native <c>message.content</c> extraction. The whole
    /// document must parse — malformed or truncated output is typed Unavailable, never a
    /// partial apply.
    /// </summary>
    private AiReply Classify(string body)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
        }
        catch (JsonException)
        {
            return new AiReply.Unavailable(AiReplyCodes.MalformedOutput);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new AiReply.Unavailable(AiReplyCodes.MalformedOutput);
            }

            if (root.TryGetProperty("refusal", out var refusal) && refusal.ValueKind == JsonValueKind.Object)
            {
                var category = refusal.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String
                    ? c.GetString()!
                    : "unknown";
                return new AiReply.Refused(new AiModerationRefusal(category, AiModerationSource.Output));
            }

            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object &&
                message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                return new AiReply.Generated(content.GetString()!, Descriptor.EndpointClass);
            }

            return new AiReply.Unavailable(AiReplyCodes.MalformedOutput);
        }
    }
}
