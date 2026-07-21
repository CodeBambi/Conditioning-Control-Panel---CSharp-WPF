using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace CcpSpike.AiProvider;

/// <summary>Deterministic failure-injection modes for the fake OpenAI-compatible endpoint.</summary>
public enum LabMode
{
    /// <summary>200 with a valid strict-envelope JSON reply.</summary>
    Ok,

    /// <summary>Accept and NEVER respond; the request ends only when the client goes away (cancellation/timeout instrument).</summary>
    Timeout,

    /// <summary>429 with Retry-After: 1.</summary>
    Rate429,

    /// <summary>500 with a short body.</summary>
    Error500,

    /// <summary>200 with a deterministic provider-refusal shape: {"refusal":{"category":"content_filter"}}.</summary>
    Refusal,

    /// <summary>200 with syntactically invalid JSON.</summary>
    Malformed,

    /// <summary>200 with a valid-JSON prefix cut mid-document (truncated stream).</summary>
    Truncated,

    /// <summary>200 headers + partial body flushed, then stall (mid-stream cancellation instrument).</summary>
    HangStream,
}

/// <summary>Per-request lab-side record (what the SERVER saw — the falsifiable side of every client claim).</summary>
public sealed record LabRequestRecord(
    int Seq,
    string Path,
    LabMode Mode,
    int BodyBytes,
    string? AuthShape,       // "present(len=N)" or null — never the value
    string Outcome);         // completed | client-gone | released-after-disconnect

/// <summary>
/// Fake OpenAI-compatible loopback endpoint (SP-018 Lab pattern): POST /v1/chat/completions
/// only, 127.0.0.1, ephemeral port. A deterministic failure-injection control surface
/// (in-proc mode queue) injects timeout/429/500/refusal/malformed/truncated/mid-stream-hang
/// ON DEMAND — shapes a live model cannot produce on cue. Request bodies and auth values
/// are never logged (presence+len only).
/// </summary>
public sealed class AiLab : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly ConcurrentQueue<LabMode> _modes = new();
    private readonly ConcurrentQueue<LabRequestRecord> _records = new();
    private int _seq;

    /// <summary>The valid-envelope reply payload the lab serves in Ok mode. Registered as a secret: logs carry shape only.</summary>
    public string OkReplyText { get; } = "lab-reply-" + Guid.NewGuid().ToString("N")[..12];

    public int Port { get; }

    public AiLab()
    {
        Redact.Register("reply", OkReplyText);
        for (var attempt = 0; ; attempt++)
        {
            var port = Random.Shared.Next(49152, 65535);
            try
            {
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                _listener.Start();
                Port = port;
                break;
            }
            catch (HttpListenerException) when (attempt < 25) { _listener.Prefixes.Clear(); }
        }
        _loop = Task.Run(ServeLoop);
    }

    public Uri Endpoint => new($"http://127.0.0.1:{Port}/v1/");

    /// <summary>Control surface: queue the exact response modes for the next N requests (one dequeued per request; default when empty = Ok).</summary>
    public void Inject(params LabMode[] modes)
    {
        foreach (var m in modes) _modes.Enqueue(m);
    }

    public IReadOnlyList<LabRequestRecord> Records => _records.ToArray();
    public int HitCount => _records.Count;

    public string OkBody() =>
        $"{{\"reply\":\"{OkReplyText}\",\"commands\":[{{\"command\":\"bubbles\",\"data\":{{\"on\":true,\"frequency\":5}}}}]}}";

    private async Task ServeLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().WaitAsync(_cts.Token); }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        var seq = Interlocked.Increment(ref _seq);
        var mode = _modes.TryDequeue(out var m) ? m : LabMode.Ok;
        try
        {
            var bodyBytes = 0;
            using (var ms = new MemoryStream())
            {
                await req.InputStream.CopyToAsync(ms);
                bodyBytes = (int)ms.Length;
            }
            var auth = req.Headers["Authorization"] is { } a ? $"present(len={a.Length})" : null;

            if (req.HttpMethod != "POST" || req.Url!.AbsolutePath != "/v1/chat/completions")
            {
                res.StatusCode = 404;
                res.Close();
                _records.Enqueue(new LabRequestRecord(seq, req.Url!.AbsolutePath, mode, bodyBytes, auth, "completed"));
                SpikeLog.Line("lab", $"#{seq} 404 {req.Url!.AbsolutePath} auth={(auth ?? "absent")} body={bodyBytes}B");
                return;
            }

            SpikeLog.Line("lab", $"#{seq} POST /v1/chat/completions mode={mode} auth={(auth ?? "absent")} body={bodyBytes}B");
            switch (mode)
            {
                case LabMode.Ok:
                    await Write(res, 200, OkBody());
                    _records.Enqueue(new LabRequestRecord(seq, "/v1/chat/completions", mode, bodyBytes, auth, "completed"));
                    break;

                case LabMode.Rate429:
                    res.Headers["Retry-After"] = "1";
                    await Write(res, 429, "{\"error\":{\"type\":\"rate_limit\"}}");
                    _records.Enqueue(new LabRequestRecord(seq, "/v1/chat/completions", mode, bodyBytes, auth, "completed"));
                    break;

                case LabMode.Error500:
                    await Write(res, 500, "{\"error\":{\"type\":\"server\"}}");
                    _records.Enqueue(new LabRequestRecord(seq, "/v1/chat/completions", mode, bodyBytes, auth, "completed"));
                    break;

                case LabMode.Refusal:
                    await Write(res, 200, "{\"refusal\":{\"category\":\"content_filter\"}}");
                    _records.Enqueue(new LabRequestRecord(seq, "/v1/chat/completions", mode, bodyBytes, auth, "completed"));
                    break;

                case LabMode.Malformed:
                    await Write(res, 200, "this is not json at all {{{");
                    _records.Enqueue(new LabRequestRecord(seq, "/v1/chat/completions", mode, bodyBytes, auth, "completed"));
                    break;

                case LabMode.Truncated:
                    // A valid-envelope PREFIX cut mid-document: the reply text is partially present.
                    await Write(res, 200, OkBody()[..(OkBody().Length / 2)]);
                    _records.Enqueue(new LabRequestRecord(seq, "/v1/chat/completions", mode, bodyBytes, auth, "completed"));
                    break;

                case LabMode.Timeout:
                    // Hold forever; the request ends when the client disconnects (cancel/timeout).
                    _records.Enqueue(await HoldUntilClientGone(req, res, seq, mode, bodyBytes, auth));
                    break;

                case LabMode.HangStream:
                    _records.Enqueue(await PartialThenStall(req, res, seq, mode, bodyBytes, auth));
                    break;
            }
        }
        catch (Exception ex)
        {
            SpikeLog.Line("lab", $"#{seq} handler fault {ex.GetType().Name}");
            try { res.Abort(); } catch { }
        }
    }

    /// <summary>Never respond; complete the record when the client goes away or after a bounded lab-side hold.</summary>
    private async Task<LabRequestRecord> HoldUntilClientGone(HttpListenerRequest req, HttpListenerResponse res, int seq, LabMode mode, int bodyBytes, string? auth)
    {
        // Poll the client connection by attempting a zero-byte write cycle; a disconnected
        // client faults the response stream. Bounded at 30s so the lab never wedges.
        for (var i = 0; i < 300; i++)
        {
            await Task.Delay(100);
            try
            {
                res.OutputStream.WriteByte(0); // never flushed; probes the connection
            }
            catch
            {
                SpikeLog.Line("lab", $"#{seq} held request ended: client-gone after ~{(i + 1) * 100}ms");
                return new LabRequestRecord(seq, "/v1/chat/completions", mode, bodyBytes, auth, "client-gone");
            }
        }
        try { res.Abort(); } catch { }
        return new LabRequestRecord(seq, "/v1/chat/completions", mode, bodyBytes, auth, "released-after-disconnect");
    }

    /// <summary>Send 200 headers + a partial body, flush, then stall until the client goes away (mid-stream cancellation instrument).</summary>
    private async Task<LabRequestRecord> PartialThenStall(HttpListenerRequest req, HttpListenerResponse res, int seq, LabMode mode, int bodyBytes, string? auth)
    {
        res.StatusCode = 200;
        res.ContentType = "application/json";
        res.SendChunked = true;
        var partial = Encoding.UTF8.GetBytes("{\"reply\":\"partial-");
        try
        {
            await res.OutputStream.WriteAsync(partial);
            await res.OutputStream.FlushAsync();
            SpikeLog.Line("lab", $"#{seq} hang-stream: headers+{partial.Length}B flushed, stalling");
        }
        catch
        {
            return new LabRequestRecord(seq, "/v1/chat/completions", mode, bodyBytes, auth, "client-gone");
        }
        for (var i = 0; i < 300; i++)
        {
            await Task.Delay(100);
            try { await res.OutputStream.FlushAsync(); }
            catch
            {
                SpikeLog.Line("lab", $"#{seq} hang-stream ended: client-gone after ~{(i + 1) * 100}ms");
                return new LabRequestRecord(seq, "/v1/chat/completions", mode, bodyBytes, auth, "client-gone");
            }
        }
        try { res.Abort(); } catch { }
        return new LabRequestRecord(seq, "/v1/chat/completions", mode, bodyBytes, auth, "released-after-disconnect");
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
        try { _listener.Stop(); } catch { }
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
    }
}
