using CcpClient.Desktop.Ai;

namespace CcpSpike.AiProvider;

/// <summary>
/// Step-4 provider-behavior matrix against the loopback lab. Every row names the typed
/// outcome the SP-016 contract requires and asserts the falsifiable side (lab hit counts,
/// partial-body bytes, send-attempt counters, elapsed bounds). Rows:
///   cancellation (mid-stream, generation invalidated, no late result applied),
///   timeout (typed, bounded, token-NOT-cancelled disambiguation),
///   429 (typed rate outcome, exactly 2 hits = initial+1 bounded retry, Retry-After honored),
///   500 (typed error, exactly 2 hits), refusal (typed, exactly 1 hit — no retry),
///   malformed + truncated (typed parse outcome, exactly 1 hit, never a partial apply),
///   stale-generation LIVE discard (dual-transport: a real late completion arrives and dies
///     at the application seam),
///   remote-host rejection (ThirdPartyCloud unroutable IP, RemoteHostOllama-shaped host,
///     nonexistent DNS name, localhost-trailing-dot near-miss — all: policy code, zero send
///     attempts, instant),
///   Ollama session fact (probe; absent → named limit), cloud (named limit, no credentials).
/// </summary>
public static class Matrix
{
    public static async Task<int> RunAsync()
    {
        var failures = new List<string>();
        void Check(bool ok, string name)
        {
            SpikeLog.Line("matrix", $"{(ok ? "PASS" : "FAIL")} {name}");
            if (!ok) failures.Add(name);
        }

        var unobserved = 0;
        TaskScheduler.UnobservedTaskException += (_, e) => { unobserved++; e.SetObserved(); };

        using var lab = new AiLab();
        var apiKey = Redact.NewSecret("apikey");

        // ---- 1. Cancellation: mid-stream cancel → client stops, generation invalidated, no late result ----
        {
            var client = new SpikeAiClient(lab.Endpoint, apiKey, TimeSpan.FromSeconds(30));
            var gen = client.AdvanceGeneration();
            lab.Inject(LabMode.HangStream);
            using var cts = new CancellationTokenSource();
            var task = client.RequestAsync("matrix-cancel-prompt", gen, cts.Token);
            // Wait for a TRUE mid-stream position (headers + partial body received).
            var waited = 0;
            while (client.BytesReadSoFar == 0 && waited < 5000) { await Task.Delay(50); waited += 50; }
            Check(client.BytesReadSoFar > 0, $"cancel-midstream: partial body received before cancel ({client.BytesReadSoFar}B > 0 — true mid-stream)");
            var t0 = Environment.TickCount64;
            cts.Cancel();
            var result = await task;
            var elapsed = Environment.TickCount64 - t0;
            Check(result.Kind == SpikeOutcomeKind.Cancelled, "cancel-midstream: typed Cancelled");
            Check(result.PartialBodyBytes > 0, $"cancel-midstream: result carries mid-stream position ({result.PartialBodyBytes}B)");
            Check(elapsed < 3000, $"cancel-midstream: client stops promptly ({elapsed}ms < 3000ms, no hang)");
            // Generation invalidation: the owner advances; the cancelled result is terminal.
            client.AdvanceGeneration();
            Check(!client.ApplyResult(result) && client.AppliedResults == 0,
                "cancel-midstream: stale-generation result discarded at the application seam, nothing applied");
            // The cancelled transport is dead: the lab must observe client-gone (no late result CAN arrive).
            var rec = await WaitRecord(lab, LabMode.HangStream, 15000);
            Check(rec is not null && rec.Outcome == "client-gone",
                $"cancel-midstream: lab observed client-gone (cancelled transport cannot deliver a late result; outcome={rec?.Outcome ?? "none"})");
        }

        // ---- 2. Timeout: bounded wait → typed timeout; token NOT cancelled (disambiguation) ----
        {
            var client = new SpikeAiClient(lab.Endpoint, apiKey, TimeSpan.FromMilliseconds(800));
            var gen = client.AdvanceGeneration();
            lab.Inject(LabMode.Timeout);
            using var cts = new CancellationTokenSource();
            var t0 = Environment.TickCount64;
            var result = await client.RequestAsync("matrix-timeout-prompt", gen, cts.Token);
            var elapsed = Environment.TickCount64 - t0;
            Check(result.Kind == SpikeOutcomeKind.Timeout, "timeout: typed Timeout outcome (not Cancelled — token source disambiguated)");
            Check(!cts.IsCancellationRequested, "timeout: external token NOT cancelled (timeout is a failure classifier, never the cancellation mechanism)");
            Check(elapsed >= 700 && elapsed < 5000, $"timeout: bounded wait ({elapsed}ms ≈ 800ms bound, no hang)");
            // The lab-held request must observe the client leaving (synchronizes before hit-count rows).
            var trec = await WaitRecord(lab, LabMode.Timeout, 15000);
            Check(trec is not null && trec.Outcome == "client-gone",
                $"timeout: lab observed client-gone after the bounded wait (outcome={trec?.Outcome ?? "none"})");
        }

        // ---- 3. 429: typed rate outcome + bounded backoff, no retry-storm ----
        {
            var client = new SpikeAiClient(lab.Endpoint, apiKey, TimeSpan.FromSeconds(10));
            var gen = client.AdvanceGeneration();
            lab.Inject(LabMode.Rate429, LabMode.Rate429);
            var hitsBefore = lab.Records.Count(r => r.Mode == LabMode.Rate429);
            var t0 = Environment.TickCount64;
            var result = await client.RequestAsync("matrix-429-prompt", gen, CancellationToken.None);
            var elapsed = Environment.TickCount64 - t0;
            Check(result.Kind == SpikeOutcomeKind.RateLimited, "429: typed rate outcome");
            Check(result.Reply is AiReply.Unavailable u && u.Code == AiReplyCodes.QuotaExhausted, "429: typed Unavailable(quota-exhausted)");
            Check(lab.Records.Count(r => r.Mode == LabMode.Rate429) - hitsBefore == 2, $"429: EXACTLY 2 lab hits (initial + 1 bounded retry — no retry-storm; saw {lab.Records.Count(r => r.Mode == LabMode.Rate429) - hitsBefore})");
            Check(elapsed >= 900, $"429: Retry-After backoff honored ({elapsed}ms ≥ ~1s)");
        }

        // ---- 4. 500: typed error outcome, bounded retry ----
        {
            var client = new SpikeAiClient(lab.Endpoint, apiKey, TimeSpan.FromSeconds(10));
            var gen = client.AdvanceGeneration();
            lab.Inject(LabMode.Error500, LabMode.Error500);
            var hitsBefore = lab.Records.Count(r => r.Mode == LabMode.Error500);
            var result = await client.RequestAsync("matrix-500-prompt", gen, CancellationToken.None);
            Check(result.Kind == SpikeOutcomeKind.ServerError, "500: typed error outcome");
            Check(lab.Records.Count(r => r.Mode == LabMode.Error500) - hitsBefore == 2, $"500: EXACTLY 2 lab hits (bounded retry; saw {lab.Records.Count(r => r.Mode == LabMode.Error500) - hitsBefore})");
        }

        // ---- 5. Refusal: typed refusal, no retry ----
        {
            var client = new SpikeAiClient(lab.Endpoint, apiKey, TimeSpan.FromSeconds(10));
            var gen = client.AdvanceGeneration();
            lab.Inject(LabMode.Refusal);
            var hitsBefore = lab.Records.Count(r => r.Mode == LabMode.Refusal);
            var result = await client.RequestAsync("matrix-refusal-prompt", gen, CancellationToken.None);
            Check(result.Kind == SpikeOutcomeKind.Refused, "refusal: typed Refused outcome");
            Check(result.Reply is AiReply.Refused r && r.Refusal.CategoryCode == "content_filter" && r.Refusal.Source == AiModerationSource.Output,
                "refusal: typed refusal carrier (category + output source, deterministic shape — no string-sniffing)");
            Check(lab.Records.Count(r => r.Mode == LabMode.Refusal) - hitsBefore == 1, $"refusal: EXACTLY 1 lab hit (no retry on refusal; saw {lab.Records.Count(r => r.Mode == LabMode.Refusal) - hitsBefore})");
        }

        // ---- 6+7. Malformed + truncated: typed parse outcome, never a partial apply ----
        foreach (var (mode, name) in new[] { (LabMode.Malformed, "malformed"), (LabMode.Truncated, "truncated") })
        {
            var client = new SpikeAiClient(lab.Endpoint, apiKey, TimeSpan.FromSeconds(10));
            var gen = client.AdvanceGeneration();
            lab.Inject(mode);
            var hitsBefore = lab.Records.Count(r => r.Mode == mode);
            var result = await client.RequestAsync($"matrix-{name}-prompt", gen, CancellationToken.None);
            Check(result.Kind == SpikeOutcomeKind.MalformedOutput, $"{name}: typed parse outcome");
            Check(result.Reply is AiReply.Unavailable u && u.Code == AiReplyCodes.MalformedOutput,
                $"{name}: Unavailable(malformed-output) — no reply text surfaced, no partial apply");
            Check(lab.Records.Count(r => r.Mode == mode) - hitsBefore == 1, $"{name}: EXACTLY 1 lab hit (no retry on parse failure; saw {lab.Records.Count(r => r.Mode == mode) - hitsBefore})");
            Check(result.Diagnostic.CommandCount == 0 && result.Diagnostic.Outcome == AiDiagnosticOutcome.Unavailable,
                $"{name}: content-free diagnostic records typed outcome, zero commands");
        }

        // ---- 8. Stale-generation LIVE discard (dual-transport): a REAL late completion arrives and dies at the seam ----
        {
            var client = new SpikeAiClient(lab.Endpoint, apiKey, TimeSpan.FromSeconds(10));
            var gen = client.AdvanceGeneration();
            lab.Inject(LabMode.SlowOk);
            var detached = client.RequestDetachedAsync("matrix-stale-prompt", gen); // ignores any external token
            client.AdvanceGeneration(); // owner switch/cancel-restart: gen now stale
            var late = await detached;    // the late completion REALLY arrives (lab SlowOk completes)
            Check(late.Kind == SpikeOutcomeKind.Completed, "stale: late completion genuinely arrived (Completed after generation advance)");
            var applied = client.ApplyResult(late);
            Check(!applied && client.StaleDiscards == 1 && client.AppliedResults == 0,
                "stale: LIVE discard at the application seam (exactly 1 STALE-DISCARD, zero applied)");
        }

        // ---- 9. Remote-host rejection: policy test, no real remote ----
        {
            // ThirdPartyCloud: unroutable TEST-NET-1 (192.0.2.1) — policy must fire BEFORE the (impossible) connect.
            var c1 = new SpikeAiClient(new Uri("http://192.0.2.1:11434/"), apiKey, TimeSpan.FromSeconds(5));
            Check(c1.EndpointClass == AiEndpointClass.ThirdPartyCloud, "remote: 192.0.2.1 classified ThirdPartyCloud");
            var t0 = Environment.TickCount64;
            var r1 = await c1.RequestAsync("matrix-remote-1", c1.AdvanceGeneration(), CancellationToken.None);
            Check(r1.Kind == SpikeOutcomeKind.PolicyRejected && c1.SendAttempts == 0 && Environment.TickCount64 - t0 < 1000,
                $"remote: ThirdPartyCloud rejected before socket (sendAttempts={c1.SendAttempts}, {Environment.TickCount64 - t0}ms)");

            // RemoteHostOllama: a non-loopback Ollama-shaped host is REMOTE (contract §6 rule 1).
            Check(AiEndpointClassifier.ClassifyOllamaHost(new Uri("http://192.168.1.50:11434/")) == AiEndpointClass.RemoteHostOllama,
                "remote: non-loopback Ollama host classified RemoteHostOllama (rejected assumption: 'local AI = local-only data')");
            var c2 = new SpikeAiClient(new Uri("http://192.168.1.50:11434/"), apiKey, TimeSpan.FromSeconds(5));
            var r2 = await c2.RequestAsync("matrix-remote-2", c2.AdvanceGeneration(), CancellationToken.None);
            Check(r2.Kind == SpikeOutcomeKind.PolicyRejected && c2.SendAttempts == 0,
                "remote: RemoteHostOllama-shaped host rejected before socket");

            // Nonexistent DNS name: policy fires before ANY DNS lookup (instant, policy code — never a DNS error).
            var c3 = new SpikeAiClient(new Uri("http://nonexistent.invalid:11434/"), apiKey, TimeSpan.FromSeconds(5));
            var t3 = Environment.TickCount64;
            var r3 = await c3.RequestAsync("matrix-remote-3", c3.AdvanceGeneration(), CancellationToken.None);
            Check(r3.Kind == SpikeOutcomeKind.PolicyRejected && c3.SendAttempts == 0 && Environment.TickCount64 - t3 < 1000,
                $"remote: nonexistent-DNS host rejected pre-DNS ({Environment.TickCount64 - t3}ms, policy code — classification is config-pure)");

            // Near-miss: "localhost." (trailing dot) resolves to loopback but Uri.IsLoopback is literal-only.
            var c4 = new SpikeAiClient(new Uri("http://localhost.:11434/"), apiKey, TimeSpan.FromSeconds(5));
            var r4 = await c4.RequestAsync("matrix-remote-4", c4.AdvanceGeneration(), CancellationToken.None);
            Check(r4.Kind == SpikeOutcomeKind.PolicyRejected && c4.SendAttempts == 0,
                "remote: 'localhost.' near-miss rejected (literal-only loopback classification — no DNS probe, contract §6 rule 2)");
        }

        // ---- 10. Ollama session fact ----
        {
            var present = false;
            try
            {
                using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var resp = await probe.GetStringAsync("http://localhost:11434/api/version");
                present = true;
                SpikeLog.Line("matrix", $"ollama: PRESENT version-shape len={resp.Length} — session fact");
            }
            catch
            {
                SpikeLog.Line("matrix", "ollama: ABSENT (localhost:11434 probe failed) — named limit, no real-Ollama round-trip this session");
            }
            Check(true, $"ollama: session fact recorded (present={present})");
            if (present)
            {
                // Bonus: one real round-trip through the strict path if a model is reachable.
                SpikeLog.Line("matrix", "ollama: present — bonus round-trip deferred to model availability (no model list assumed)");
            }
        }

        // ---- 11. Cloud / approved-endpoint: named limit ----
        SpikeLog.Line("matrix", "cloud: NAMED LIMIT — no credentials exist on this box (never invented); cloud/approved-endpoint paths not exercisable this session");
        Check(true, "cloud: named limit recorded (no credentials)");

        // ---- hygiene: no unobserved task exceptions from cancelled rows ----
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Check(unobserved == 0, $"hygiene: zero unobserved task exceptions ({unobserved})");

        SpikeLog.Line("matrix", failures.Count == 0 ? "MATRIX GREEN" : $"MATRIX FAILED: {failures.Count} — {string.Join(", ", failures)}");
        Console.WriteLine(failures.Count == 0 ? "MATRIX: all pass" : $"MATRIX: {failures.Count} FAILURES — {string.Join(", ", failures)}");
        return failures.Count == 0 ? 0 : 1;
    }

    private static async Task<LabRequestRecord?> WaitRecord(AiLab lab, LabMode mode, int timeoutMs)
    {
        var waited = 0;
        while (waited < timeoutMs)
        {
            var rec = lab.Records.LastOrDefault(r => r.Mode == mode);
            if (rec is not null) return rec;
            await Task.Delay(100);
            waited += 100;
        }
        return null;
    }
}
