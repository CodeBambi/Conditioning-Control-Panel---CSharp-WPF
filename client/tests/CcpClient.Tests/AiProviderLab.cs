using System.Collections.Concurrent;
using System.Net;
using System.Text;

// T-15 leaked-listener self-check: after ALL collections finish, any undisposed lab fails the run LOUD.
[assembly: Xunit.AssemblyFixture(typeof(CcpClient.Tests.AiLabLeakSelfCheck))]

namespace CcpClient.Tests;

/// <summary>Deterministic failure-injection modes for the fake Ollama loopback endpoint (SP-019 shapes, Ollama-native protocol).</summary>
public enum AiLabMode
{
    /// <summary>200 with a valid native api/chat reply ({message:{role,content}}).</summary>
    Ok,

    /// <summary>Accept and NEVER respond; the request ends only when the client goes away (cancellation/timeout instrument).</summary>
    Timeout,

    /// <summary>429 with Retry-After: 1 (LAB CONSTRUCT — proves the client's shape handling; never a claim that a real Ollama emits 429).</summary>
    Rate429,

    /// <summary>500 with a short body.</summary>
    Error500,

    /// <summary>404 on the chat path (other-4xx no-retry proof).</summary>
    NotFound404,

    /// <summary>200 with a deterministic provider-refusal shape {"refusal":{"category":"content_filter"}} (LAB CONSTRUCT — the typed-carrier mechanism proof, never a real-Ollama wire claim).</summary>
    Refusal,

    /// <summary>200 with syntactically invalid JSON.</summary>
    Malformed,

    /// <summary>200 with a valid-JSON prefix cut mid-document (truncated stream; reply text partially present).</summary>
    Truncated,

    /// <summary>200 headers + partial body flushed, then stall (mid-stream cancellation instrument).</summary>
    HangStream,

    /// <summary>200 with the valid reply after a delay (late-completion instrument for the live stale-discard proof).</summary>
    SlowOk,
}

/// <summary>Per-request lab-side record (what the SERVER saw — the falsifiable side of every client claim). Never carries payload text.</summary>
public sealed record AiLabRequestRecord(
    int Seq,
    string Path,
    AiLabMode Mode,
    int BodyBytes,
    string Outcome); // completed | client-gone | released-after-disconnect

/// <summary>
/// Fake Ollama loopback endpoint (SP-035 slice c2; the SP-019 AiLab SHAPES re-implemented
/// fresh against the Ollama-native protocol — the spike stays quarantined, no spike code is
/// imported). POST /api/chat + GET /api/version only, 127.0.0.1, ephemeral port, zero
/// external network. A mode queue injects timeout/429/500/refusal/malformed/truncated/
/// mid-stream-hang ON DEMAND — shapes a live model cannot produce on cue. The lab logs
/// nothing and records no payload content (per-run reply text is a registered audit secret).
/// </summary>
public sealed class AiProviderLab : IDisposable
{
    /// <summary>Live-instance registry (T-15 self-check): port → prefix for every undisposed lab. A leaked entry at assembly teardown = a leaked listener holding a loopback port; the assembly fixture fails the run LOUD with the port/prefix named.</summary>
    private static readonly ConcurrentDictionary<int, string> LivePrefixes = new();

    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly ConcurrentQueue<AiLabMode> _modes = new();
    private readonly ConcurrentQueue<AiLabRequestRecord> _records = new();
    private int _seq;

    /// <summary>The per-run reply payload served in Ok/SlowOk modes. A registered audit secret: never committed, never logged.</summary>
    public string OkReplyText { get; } = "lab-reply-" + Guid.NewGuid().ToString("N")[..12];

    public int Port { get; }

    public AiProviderLab()
    {
        // SP-023 rule HONORED (T-15 root cause): a FAILED Start() DISPOSES the instance, so
        // every bind attempt uses a FRESH HttpListener. The pre-hardening loop reused one
        // instance; a port collision (zombie test host or ephemeral churn) → HttpListenerException
        // → instance disposed → retry threw ObjectDisposedException, which the HttpListenerException-
        // only catch did not handle — the ODE escaped the constructor and failed the test.
        for (var attempt = 0; ; attempt++)
        {
            var port = Random.Shared.Next(49152, 65535);
            var candidate = new HttpListener();
            try
            {
                candidate.Prefixes.Add($"http://127.0.0.1:{port}/");
                candidate.Start();
                _listener = candidate;
                Port = port;
                break;
            }
            catch (HttpListenerException ex)
            {
                try { candidate.Close(); } catch { }
                if (attempt >= 25)
                {
                    throw new InvalidOperationException(
                        $"AiProviderLab: {attempt + 1} loopback bind attempts failed (last prefix http://127.0.0.1:{port}/) — " +
                        "likely leaked dotnet test hosts holding loopback ports (T-15 zombie class): enumerate and kill stray dotnet.exe test hosts.",
                        ex);
                }
            }
        }

        LivePrefixes[Port] = $"http://127.0.0.1:{Port}/";
        _loop = Task.Run(ServeLoop);
    }

    /// <summary>The loopback host URI the provider under test is configured with.</summary>
    public Uri Host => new($"http://127.0.0.1:{Port}/");

    /// <summary>Control surface: queue the exact response modes for the next N requests (one dequeued per request; default when empty = Ok).</summary>
    public void Inject(params AiLabMode[] modes)
    {
        foreach (var mode in modes)
        {
            _modes.Enqueue(mode);
        }
    }

    public IReadOnlyList<AiLabRequestRecord> Records => _records.ToArray();

    public int HitCount => _records.Count;

    /// <summary>Hits recorded for one mode (retry-storm proofs are server-side hit counts, not client-side hope).</summary>
    public int HitsFor(AiLabMode mode) => _records.Count(r => r.Mode == mode);

    public string OkBody() =>
        $$"""{"model":"lab-model","message":{"role":"assistant","content":"{{OkReplyText}}"},"done":true}""";

    private async Task ServeLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().WaitAsync(_cts.Token);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; } // harness teardown (listener closed mid-await) — never a product failure

            _ = Task.Run(() => Handle(ctx));
        }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        var seq = Interlocked.Increment(ref _seq);
        var mode = _modes.TryDequeue(out var m) ? m : AiLabMode.Ok;
        HttpListenerResponse? res = null;
        try
        {
            // ctx.Request/ctx.Response access stays INSIDE the try: on a torn-down listener
            // these throw ObjectDisposedException — harness teardown, never a product failure.
            var req = ctx.Request;
            res = ctx.Response;
            var bodyBytes = 0;
            using (var ms = new MemoryStream())
            {
                await req.InputStream.CopyToAsync(ms);
                bodyBytes = (int)ms.Length;
            }

            var path = req.Url!.AbsolutePath;

            // The SP-006 probe endpoint (SP-019 item-8 URL shape).
            if (req.HttpMethod == "GET" && path == "/api/version")
            {
                await Write(res, 200, """{"version":"lab"}""");
                _records.Enqueue(new AiLabRequestRecord(seq, path, mode, bodyBytes, "completed"));
                return;
            }

            if (req.HttpMethod != "POST" || path != "/api/chat")
            {
                res.StatusCode = 404;
                res.Close();
                _records.Enqueue(new AiLabRequestRecord(seq, path, mode, bodyBytes, "completed"));
                return;
            }

            switch (mode)
            {
                case AiLabMode.Ok:
                    await Write(res, 200, OkBody());
                    _records.Enqueue(new AiLabRequestRecord(seq, path, mode, bodyBytes, "completed"));
                    break;

                case AiLabMode.Rate429:
                    res.Headers["Retry-After"] = "1";
                    await Write(res, 429, """{"error":"rate_limit"}""");
                    _records.Enqueue(new AiLabRequestRecord(seq, path, mode, bodyBytes, "completed"));
                    break;

                case AiLabMode.Error500:
                    await Write(res, 500, """{"error":"server"}""");
                    _records.Enqueue(new AiLabRequestRecord(seq, path, mode, bodyBytes, "completed"));
                    break;

                case AiLabMode.NotFound404:
                    await Write(res, 404, """{"error":"not found"}""");
                    _records.Enqueue(new AiLabRequestRecord(seq, path, mode, bodyBytes, "completed"));
                    break;

                case AiLabMode.Refusal:
                    await Write(res, 200, """{"refusal":{"category":"content_filter"}}""");
                    _records.Enqueue(new AiLabRequestRecord(seq, path, mode, bodyBytes, "completed"));
                    break;

                case AiLabMode.Malformed:
                    await Write(res, 200, "this is not json at all {{{");
                    _records.Enqueue(new AiLabRequestRecord(seq, path, mode, bodyBytes, "completed"));
                    break;

                case AiLabMode.Truncated:
                    // A valid-reply PREFIX cut mid-document: the reply text is partially present.
                    await Write(res, 200, OkBody()[..(OkBody().Length / 2)]);
                    _records.Enqueue(new AiLabRequestRecord(seq, path, mode, bodyBytes, "completed"));
                    break;

                case AiLabMode.Timeout:
                    _records.Enqueue(await HoldUntilClientGone(res, seq, path, mode, bodyBytes));
                    break;

                case AiLabMode.HangStream:
                    _records.Enqueue(await PartialThenStall(res, seq, path, mode, bodyBytes));
                    break;

                case AiLabMode.SlowOk:
                    await Task.Delay(1500);
                    try
                    {
                        await Write(res, 200, OkBody());
                        _records.Enqueue(new AiLabRequestRecord(seq, path, mode, bodyBytes, "completed"));
                    }
                    catch
                    {
                        _records.Enqueue(new AiLabRequestRecord(seq, path, mode, bodyBytes, "client-gone"));
                    }

                    break;
            }
        }
        catch (ObjectDisposedException)
        {
            // Harness teardown raced an in-flight request — classified as harness, never product.
        }
        catch
        {
            try { res?.Abort(); } catch { }
        }
    }

    /// <summary>Never respond; complete the record when the client goes away or after a bounded lab-side hold.</summary>
    private static async Task<AiLabRequestRecord> HoldUntilClientGone(HttpListenerResponse res, int seq, string path, AiLabMode mode, int bodyBytes)
    {
        for (var i = 0; i < 300; i++)
        {
            await Task.Delay(100);
            try
            {
                res.OutputStream.WriteByte(0); // never flushed alone; probes the connection
                await res.OutputStream.FlushAsync(); // a dead client faults here
            }
            catch
            {
                return new AiLabRequestRecord(seq, path, mode, bodyBytes, "client-gone");
            }
        }

        try { res.Abort(); } catch { }
        return new AiLabRequestRecord(seq, path, mode, bodyBytes, "released-after-disconnect");
    }

    /// <summary>Send 200 headers + a partial body, flush, then stall until the client goes away (mid-stream cancellation instrument).</summary>
    private static async Task<AiLabRequestRecord> PartialThenStall(HttpListenerResponse res, int seq, string path, AiLabMode mode, int bodyBytes)
    {
        res.StatusCode = 200;
        res.ContentType = "application/json";
        res.SendChunked = true;
        var partial = Encoding.UTF8.GetBytes("""{"model":"lab-model","message":{"role":"assistant","content":"partial-""");
        try
        {
            await res.OutputStream.WriteAsync(partial);
            await res.OutputStream.FlushAsync();
        }
        catch
        {
            return new AiLabRequestRecord(seq, path, mode, bodyBytes, "client-gone");
        }

        for (var i = 0; i < 300; i++)
        {
            await Task.Delay(100);
            try
            {
                res.OutputStream.WriteByte(0);
                await res.OutputStream.FlushAsync(); // a dead client faults here
            }
            catch
            {
                return new AiLabRequestRecord(seq, path, mode, bodyBytes, "client-gone");
            }
        }

        try { res.Abort(); } catch { }
        return new AiLabRequestRecord(seq, path, mode, bodyBytes, "released-after-disconnect");
    }

    private static async Task Write(HttpListenerResponse res, int status, string body)
    {
        res.StatusCode = status;
        res.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(body);
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Close(); } catch { } // aborts in-flight requests; their handlers fault into their own catches (abandon-by-abort)
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
        LivePrefixes.TryRemove(Port, out _);
    }

    /// <summary>The leaked-listener self-check (T-15): called by the assembly fixture at test-assembly teardown. Throws LOUD naming every leaked port/prefix. Never called from this class's own Dispose — a Dispose throw during test-failure unwinding would mask the real failure.</summary>
    public static void AssertNoLeakedListeners()
    {
        if (!LivePrefixes.IsEmpty)
        {
            throw new InvalidOperationException(
                "AiProviderLab leaked listener(s) holding loopback port(s): " +
                string.Join(", ", LivePrefixes.Select(kv => $"{kv.Value} (port {kv.Key})")));
        }
    }
}

/// <summary>Assembly-teardown self-check (T-15): runs AFTER all collections (no parallel-lab race), adds zero test cases. Any lab still registered leaked its listener.</summary>
public sealed class AiLabLeakSelfCheck : IDisposable
{
    public void Dispose() => AiProviderLab.AssertNoLeakedListeners();
}
