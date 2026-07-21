using System.Net;
using System.Text;

namespace CcpSpike.VideoHandoff;

/// <summary>
/// Loopback source lab (SP-011 LoopbackServer pattern): GET-only, 127.0.0.1, ephemeral port.
/// Every matrix row's fixture is served here. All request logging is redacted (presence+shape).
/// </summary>
public sealed class Lab : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _fixtures;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    public int Port { get; }
    public string CookieValue { get; } = Redact.NewSecret("cookie");
    public string HeaderValue { get; } = Redact.NewSecret("header");
    private readonly byte[] _sigKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    /// <summary>Gate-side observations (what credential the DECODER actually presented), thread-safe.</summary>
    public readonly System.Collections.Concurrent.ConcurrentQueue<string> GateObservations = new();

    public Lab(string fixturesDir)
    {
        _fixtures = fixturesDir;
        // HttpListener cannot bind port 0 (SP-011 pattern): retry random ephemeral ports.
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

    public string SignedUrl(string path, DateTimeOffset exp)
    {
        var expUnix = exp.ToUnixTimeSeconds();
        var sig = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(_sigKey, Encoding.ASCII.GetBytes($"{path}:{expUnix}"))).ToLowerInvariant();
        Redact.Register("sig", sig);
        return $"http://127.0.0.1:{Port}{path}?exp={expUnix}&sig={sig}";
    }

    public string Url(string path) => $"http://127.0.0.1:{Port}{path}";

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
        try
        {
            if (req.HttpMethod != "GET") { res.StatusCode = 405; return; }
            var path = req.Url!.AbsolutePath;

            // Redacted request log: SpikeLog.Line scrubs every registered secret value;
            // cookie/header/sig additionally shaped to presence+len at the source.
            var cookieNote = req.Headers["Cookie"] is { } c ? $" cookie=present(len={c.Length})" : "";
            var headerNote = req.Headers["X-Spike-Gate"] is { } h ? $" x-spike-gate=present(len={h.Length})" : "";
            var sigNote = req.QueryString["sig"] is { } s ? $" sig=present(len={s.Length})" : "";
            SpikeLog.Line("lab", $"GET {path}{cookieNote}{headerNote}{sigNote}");

            switch (path)
            {
                case "/health":
                    await Write(res, 200, "text/plain", "ok"u8.ToArray());
                    return;
                case "/media/clip.mp4":
                    await ServeFile(res, "clip.mp4", "video/mp4", req);
                    return;
                case "/media/clip.webm":
                    await ServeFile(res, "clip.webm", "video/webm", req);
                    return;
                case "/gated-cookie/clip.mp4":
                    GateObservations.Enqueue($"gated-cookie presented={(req.Headers["Cookie"] is null ? "absent" : req.Headers["Cookie"] == $"spike_gate={CookieValue}" ? "valid" : "invalid")}");
                    if (req.Headers["Cookie"] != $"spike_gate={CookieValue}") { await Write(res, 401, "text/plain", "cookie-required"u8.ToArray()); return; }
                    await ServeFile(res, "clip.mp4", "video/mp4", req);
                    return;
                case "/gated-header/clip.mp4":
                    GateObservations.Enqueue($"gated-header presented={(req.Headers["X-Spike-Gate"] is null ? "absent" : req.Headers["X-Spike-Gate"] == HeaderValue ? "valid" : "invalid")}");
                    if (req.Headers["X-Spike-Gate"] != HeaderValue) { await Write(res, 403, "text/plain", "header-required"u8.ToArray()); return; }
                    await ServeFile(res, "clip.mp4", "video/mp4", req);
                    return;
                case "/signed/clip.mp4":
                {
                    var expRaw = req.QueryString["exp"];
                    var sig = req.QueryString["sig"] ?? "";
                    if (!long.TryParse(expRaw, out var expUnix)) { await Write(res, 403, "text/plain", "bad-signature"u8.ToArray()); return; }
                    var expect = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(_sigKey, Encoding.ASCII.GetBytes($"/signed/clip.mp4:{expUnix}"))).ToLowerInvariant();
                    if (sig != expect) { await Write(res, 403, "text/plain", "bad-signature"u8.ToArray()); return; }
                    if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expUnix) { await Write(res, 403, "text/plain", "expired"u8.ToArray()); return; }
                    await ServeFile(res, "clip.mp4", "video/mp4", req);
                    return;
                }
                case "/relay/cookie/clip.mp4":
                    await Relay(res, "/gated-cookie/clip.mp4", injectCookie: true, injectHeader: false);
                    return;
                case "/relay/nocookie/clip.mp4":
                    await Relay(res, "/gated-cookie/clip.mp4", injectCookie: false, injectHeader: false);
                    return;
                case "/relay/header/clip.mp4":
                    await Relay(res, "/gated-header/clip.mp4", injectCookie: false, injectHeader: true);
                    return;
                case "/relay/noheader/clip.mp4":
                    await Relay(res, "/gated-header/clip.mp4", injectCookie: false, injectHeader: false);
                    return;
                case "/page/site.html":
                    await Write(res, 200, "text/html", Encoding.UTF8.GetBytes(
                        "<!doctype html><html><body><video id=\"v\" src=\"" + Url("/media/clip.mp4") + "\" controls></video></body></html>"));
                    return;
                case "/page/blob.html":
                    await Write(res, 200, "text/html", Encoding.UTF8.GetBytes(BlobPage
                        .Replace("MEDIA_URL", Url("/media/clip.webm"), StringComparison.Ordinal)
                        .Replace("MIME", "video/webm", StringComparison.Ordinal)
                        .Replace("CODECS", "vp8, vorbis", StringComparison.Ordinal)));
                    return;
                case "/page/drm.html":
                    await Write(res, 200, "text/html", Encoding.UTF8.GetBytes(DrmPage
                        .Replace("MEDIA_URL", Url("/media/clip.mp4"), StringComparison.Ordinal)));
                    return;
                default:
                    // Manifest trees: /hls-fmp4/*, /hls-ts/*, /dash/*
                    if (path.StartsWith("/hls-fmp4/", StringComparison.Ordinal) ||
                        path.StartsWith("/hls-ts/", StringComparison.Ordinal) ||
                        path.StartsWith("/dash/", StringComparison.Ordinal))
                    {
                        var name = path[(path.LastIndexOf('/') + 1)..];
                        if (name.Length == 0 || name.Contains("..", StringComparison.Ordinal)) { res.StatusCode = 403; return; }
                        var dir = path.TrimStart('/')[..path.LastIndexOf('/')];
                        var mime = name switch
                        {
                            _ when name.EndsWith(".m3u8", StringComparison.Ordinal) => "application/vnd.apple.mpegurl",
                            _ when name.EndsWith(".mpd", StringComparison.Ordinal) => "application/dash+xml",
                            _ when name.EndsWith(".ts", StringComparison.Ordinal) => "video/mp2t",
                            _ => "video/iso.segment",
                        };
                        await ServeFile(res, Path.Combine(dir, name), mime, req);
                        return;
                    }
                    res.StatusCode = 404;
                    return;
            }
        }
        finally
        {
            res.Close();
        }
    }

    /// <summary>
    /// Relay-with-header-injection (consult §1.5b: proxy-mediated auth strategy evidence,
    /// pending-owner). The decoder opens the loopback relay URL; the relay injects the secret
    /// upstream. Negative controls (/relay/nocookie, /relay/noheader) inject nothing and must
    /// propagate the upstream 401/403.
    /// </summary>
    private async Task Relay(HttpListenerResponse res, string upstreamPath, bool injectCookie, bool injectHeader)
    {
        using var http = new HttpClient();
        using var msg = new HttpRequestMessage(HttpMethod.Get, Url(upstreamPath));
        if (injectCookie) msg.Headers.TryAddWithoutValidation("Cookie", $"spike_gate={CookieValue}");
        if (injectHeader) msg.Headers.TryAddWithoutValidation("X-Spike-Gate", HeaderValue);
        using var up = await http.SendAsync(msg);
        SpikeLog.Line("lab", $"relay upstream={upstreamPath} inject-cookie={(injectCookie ? Redact.Shape("cookie", CookieValue) : "absent")} inject-header={(injectHeader ? Redact.Shape("header", HeaderValue) : "absent")} upstream-status={(int)up.StatusCode}");
        res.StatusCode = (int)up.StatusCode;
        res.ContentType = up.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        var body = await up.Content.ReadAsByteArrayAsync();
        res.ContentLength64 = body.Length;
        await res.OutputStream.WriteAsync(body);
    }

    private async Task ServeFile(HttpListenerResponse res, string relName, string mime, HttpListenerRequest req)
    {
        var full = Path.Combine(_fixtures, relName);
        if (!File.Exists(full)) { res.StatusCode = 404; return; }
        var bytes = await File.ReadAllBytesAsync(full);
        res.ContentType = mime;
        // Range support (video seek / progressive fetch; SP-011 contract point).
        var range = req.Headers["Range"];
        if (range is not null && range.StartsWith("bytes=", StringComparison.Ordinal))
        {
            var parts = range["bytes=".Length..].Split('-');
            if (long.TryParse(parts[0], out var start) && start < bytes.Length)
            {
                var end = parts.Length > 1 && long.TryParse(parts[1], out var e) ? Math.Min(e, bytes.Length - 1) : bytes.Length - 1;
                res.StatusCode = 206;
                res.AddHeader("Content-Range", $"bytes {start}-{end}/{bytes.Length}");
                res.ContentLength64 = end - start + 1;
                await res.OutputStream.WriteAsync(bytes.AsMemory((int)start, (int)(end - start + 1)));
                return;
            }
            res.StatusCode = 416;
            return;
        }
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
    }

    private static async Task Write(HttpListenerResponse res, int status, string mime, byte[] body)
    {
        res.StatusCode = status;
        res.ContentType = mime;
        res.ContentLength64 = body.Length;
        await res.OutputStream.WriteAsync(body);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }

    // blob:/MSE fixture page: fetches a media file, creates a blob: object URL for the video
    // element, and ALSO attempts a real MSE SourceBuffer append (outcome reported honestly).
    // Discovery must DETECT the blob: protocol on the resolved src (consult §1.5c trap 2).
    private const string BlobPage = """
        <!doctype html><html><body>
        <video id="v" controls></video>
        <script>
        (async () => {
          const v = document.getElementById('v');
          const log = (m) => { const d = document.createElement('div'); d.className = 'spike-log'; d.textContent = m; document.body.appendChild(d); };
          try {
            const buf = await fetch('MEDIA_URL').then(r => r.arrayBuffer());
            v.src = URL.createObjectURL(new Blob([buf], { type: 'MIME' }));
            log('blob-src-set protocol=' + new URL(v.src).protocol);
            // MSE attempt (secondary evidence; failure is an honest outcome, not a spike failure)
            try {
              const ms = new MediaSource();
              const v2 = document.createElement('video');
              v2.src = URL.createObjectURL(ms);
              ms.addEventListener('sourceopen', () => {
                try {
                  const sb = ms.addSourceBuffer('MIME; codecs="CODECS"');
                  sb.addEventListener('updateend', () => log('mse-append-ok'));
                  sb.addEventListener('error', () => log('mse-append-error'));
                  sb.appendBuffer(buf);
                } catch (e) { log('mse-unsupported ' + e.name); }
              });
            } catch (e) { log('mse-unavailable ' + e.name); }
          } catch (e) { log('blob-fetch-failed ' + e.name); }
        })();
        </script>
        </body></html>
        """;

    // Fake-DRM/EME-signaling page: really CALLS requestMediaKeySystemAccess (usage, not mere
    // API presence — consult §1.5c trap 3) and listens for the 'encrypted' event on a video.
    private const string DrmPage = """
        <!doctype html><html><body>
        <video id="v" src="MEDIA_URL" controls></video>
        <script>
        (async () => {
          const v = document.getElementById('v');
          const log = (m) => { const d = document.createElement('div'); d.className = 'spike-log'; d.textContent = m; document.body.appendChild(d); };
          v.addEventListener('encrypted', (e) => log('eme-encrypted-event initDataType=' + e.initDataType));
          try {
            const access = await navigator.requestMediaKeySystemAccess('org.w3.clearkey',
              [{ initDataTypes: ['cenc'], videoCapabilities: [{ contentType: 'video/mp4; codecs="avc1.42c00a"' }] }]);
            log('eme-keysystem-access-granted keySystem=' + access.keySystem);
          } catch (e) { log('eme-keysystem-access-denied ' + e.name); }
        })();
        </script>
        </body></html>
        """;
}
