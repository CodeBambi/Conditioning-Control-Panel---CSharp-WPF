using System.Net;
using System.Text;

namespace CcpSpike.WebView;

/// <summary>
/// SP-011 loopback: two GET-only origins on 127.0.0.1, ephemeral ports (retry loop —
/// HttpListener cannot bind port 0). Page origin serves the READ-ONLY payload tree
/// overlay-first; media origin serves the payload's assets dir with CORS scoped to the
/// page origin (preserves the WPF ccp.game/ccp.assets cross-origin split so taint checks
/// stay meaningful). HTTP Range on both. Path traversal refused. Every request logged.
/// </summary>
public sealed class LoopbackServer : IDisposable
{
    private static readonly Dictionary<string, string> Mime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon",
        [".webm"] = "video/webm",
        [".mp4"] = "video/mp4",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".ogg"] = "audio/ogg",
        [".wasm"] = "application/wasm",
        [".ttf"] = "font/ttf",
        [".woff2"] = "font/woff2",
        [".txt"] = "text/plain; charset=utf-8",
        [".map"] = "application/json; charset=utf-8",
    };

    private readonly string _payloadRoot;   // .../Resources/web/dtrh (READ-ONLY)
    private readonly string _overlayRoot;   // tracked overlay dir (overlay-first)
    private readonly string _mediaRoot;     // .../Resources/web/dtrh/assets (READ-ONLY)
    private readonly SpikeLog _log;
    private readonly HttpListener _page = new();
    private readonly HttpListener _media = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _pageLoop;
    private Task? _mediaLoop;
    private int _stopped;

    public int PagePort { get; private set; }
    public int MediaPort { get; private set; }
    public string PageOrigin => $"http://127.0.0.1:{PagePort}";
    public string MediaOrigin => $"http://127.0.0.1:{MediaPort}";

    /// <summary>Failure-injection case 2: when armed, the media origin refuses everything (403).</summary>
    public volatile bool MediaBlocked;

    public LoopbackServer(string payloadRoot, string overlayRoot, string mediaRoot, SpikeLog log)
    {
        _payloadRoot = Path.GetFullPath(payloadRoot);
        _overlayRoot = Path.GetFullPath(overlayRoot);
        _mediaRoot = Path.GetFullPath(mediaRoot);
        _log = log;
    }

    public void Start()
    {
        PagePort = BindWithRetry(_page);
        MediaPort = BindWithRetry(_media);
        _log.Log($"loopback: page origin {PageOrigin} (overlay-first {_overlayRoot} over READ-ONLY {_payloadRoot})");
        _log.Log($"loopback: media origin {MediaOrigin} (READ-ONLY {_mediaRoot}, CORS scoped to {PageOrigin})");
        _pageLoop = Task.Run(() => AcceptLoop(_page, HandlePage));
        _mediaLoop = Task.Run(() => AcceptLoop(_media, HandleMedia));
    }

    private int BindWithRetry(HttpListener l)
    {
        Exception? last = null;
        for (var i = 0; i < 60; i++)
        {
            var port = Random.Shared.Next(49152, 65536);
            try
            {
                l.Prefixes.Add($"http://127.0.0.1:{port}/");
                l.Start();
                return port;
            }
            catch (Exception ex) // HttpListenerException (in use) or anything transient
            {
                last = ex;
                _log.Log($"loopback: bind 127.0.0.1:{port} failed ({ex.GetType().Name}: {ex.Message}) — retrying");
                try { l.Prefixes.Clear(); } catch { /* best effort */ }
                try { if (l.IsListening) l.Stop(); } catch { /* best effort */ }
            }
        }

        throw new InvalidOperationException($"no free ephemeral port in 60 tries (last: {last?.Message})");
    }

    private async Task AcceptLoop(HttpListener l, Func<HttpListenerContext, Task> handler)
    {
        while (l.IsListening && !_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await l.GetContextAsync().ConfigureAwait(false); }
            catch (Exception) when (!l.IsListening || _cts.IsCancellationRequested) { return; }
            catch (Exception ex) { _log.Log($"loopback: accept fault {ex.GetType().Name}"); continue; }

            _ = Task.Run(async () =>
            {
                try { await handler(ctx).ConfigureAwait(false); }
                catch (Exception ex) { _log.Log($"loopback: handler fault {ex.GetType().Name}: {ex.Message}"); }
                finally { try { ctx.Response.OutputStream.Close(); } catch { /* best effort */ } }
            });
        }
    }

    // ---------- page origin ----------

    private async Task HandlePage(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var path = req.Url?.AbsolutePath ?? "/";
        if (req.HttpMethod != "GET")
        {
            await Refuse(ctx, 405, "refused: GET-only origin", path);
            return;
        }

        if (path == "/health")
        {
            await WriteText(ctx, 200, "ok", "text/plain");
            return;
        }

        if (!path.StartsWith("/dtrh/", StringComparison.Ordinal))
        {
            await Refuse(ctx, 404, "refused: no such route (only /dtrh/* is exposed)", path);
            return;
        }

        var rel = path["/dtrh/".Length..];
        if (rel.Length == 0) rel = "index.html";
        if (!TryResolve(_overlayRoot, rel, out var overlayFile) || !TryResolve(_payloadRoot, rel, out var payloadFile))
        {
            await Refuse(ctx, 403, "refused: path traversal", path);
            return;
        }

        string file, source;
        if (File.Exists(overlayFile)) { file = overlayFile; source = "overlay"; }
        else if (File.Exists(payloadFile)) { file = payloadFile; source = "payload"; }
        else { await Refuse(ctx, 404, "not found", path); return; }

        await ServeFile(ctx, file, source, cors: false);
    }

    // ---------- media origin ----------

    private async Task HandleMedia(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var path = req.Url?.AbsolutePath ?? "/";

        // CORS preflight (Range is NOT a safelisted request header — consult correction 1).
        if (req.HttpMethod == "OPTIONS")
        {
            var r = ctx.Response;
            r.StatusCode = 204;
            r.Headers["Access-Control-Allow-Origin"] = PageOrigin;
            r.Headers["Access-Control-Allow-Methods"] = "GET, OPTIONS";
            r.Headers["Access-Control-Allow-Headers"] = "range";
            r.Headers["Access-Control-Max-Age"] = "600";
            _log.Log($"loopback: OPTIONS {path} -> 204 preflight");
            return;
        }

        if (req.HttpMethod != "GET")
        {
            await Refuse(ctx, 405, "refused: GET-only origin", path, cors: true);
            return;
        }

        if (MediaBlocked)
        {
            // CORS on refusals too — a CORS-less 403 surfaces to fetch() as an opaque
            // TypeError, not a status (run G: spike.js run() aborted silently). Error
            // diagnosability is part of the loopback contract.
            await Refuse(ctx, 403, "refused: route blocked (fault injection)", path, cors: true);
            return;
        }

        if (!path.StartsWith("/media/", StringComparison.Ordinal))
        {
            await Refuse(ctx, 404, "refused: no such route (only /media/* is exposed)", path, cors: true);
            return;
        }

        var rel = path["/media/".Length..];
        if (!TryResolve(_mediaRoot, rel, out var file))
        {
            await Refuse(ctx, 403, "refused: path traversal", path, cors: true);
            return;
        }

        if (!File.Exists(file))
        {
            await Refuse(ctx, 404, "not found", path, cors: true);
            return;
        }

        await ServeFile(ctx, file, "media", cors: true);
    }

    // ---------- shared ----------

    /// <summary>Decode, reject traversal/absolute weirdness, resolve under root. Root is never writable here.</summary>
    private bool TryResolve(string root, string rel, out string full)
    {
        full = "";
        string decoded;
        try { decoded = Uri.UnescapeDataString(rel); }
        catch { return false; }

        if (decoded.IndexOf('\0') >= 0 || decoded.Contains("..") || decoded.Contains('\\') ||
            decoded.Contains(':') || decoded.StartsWith('/'))
        {
            return false;
        }

        var candidate = Path.GetFullPath(Path.Combine(root, decoded.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !string.Equals(candidate, root, StringComparison.Ordinal))
        {
            return false;
        }

        full = candidate;
        return true;
    }

    private async Task ServeFile(HttpListenerContext ctx, string file, string source, bool cors)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        var len = new FileInfo(file).Length;
        long start = 0, end = len - 1;
        var status = 200;
        var rangeHeader = req.Headers["Range"];

        if (!string.IsNullOrEmpty(rangeHeader))
        {
            if (!TryParseRange(rangeHeader, len, out start, out end))
            {
                res.StatusCode = 416;
                res.Headers["Content-Range"] = $"bytes */{len}";
                if (cors) AddCors(res);
                _log.Log($"loopback: GET {req.Url?.AbsolutePath} -> 416 (Range '{rangeHeader}')");
                return;
            }

            status = 206;
            res.Headers["Content-Range"] = $"bytes {start}-{end}/{len}";
        }

        res.StatusCode = status;
        res.ContentType = Mime.TryGetValue(Path.GetExtension(file), out var mt) ? mt : "application/octet-stream";
        res.ContentLength64 = end - start + 1;
        if (cors) AddCors(res);

        await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Seek(start, SeekOrigin.Begin);
        var remaining = end - start + 1;
        var buf = new byte[64 * 1024];
        while (remaining > 0)
        {
            var n = await fs.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, remaining))).ConfigureAwait(false);
            if (n <= 0) break;
            await res.OutputStream.WriteAsync(buf.AsMemory(0, n)).ConfigureAwait(false);
            remaining -= n;
        }

        _log.Log($"loopback: GET {req.Url?.AbsolutePath} -> {status} {end - start + 1}B ({source}:{Path.GetFileName(file)}{(status == 206 ? $" range {start}-{end}/{len}" : "")})");
    }

    private void AddCors(HttpListenerResponse res)
    {
        res.Headers["Access-Control-Allow-Origin"] = PageOrigin;
        // Consult correction 2: spike.js's evidence line reads Content-Range cross-origin.
        res.Headers["Access-Control-Expose-Headers"] = "Content-Range";
    }

    private static bool TryParseRange(string header, long len, out long start, out long end)
    {
        start = 0; end = len - 1;
        if (!header.StartsWith("bytes=", StringComparison.Ordinal)) return false;
        var parts = header["bytes=".Length..].Split('-', 2);
        try
        {
            if (parts[0].Length == 0) // suffix: bytes=-500
            {
                var n = long.Parse(parts[1]);
                if (n <= 0) return false;
                start = Math.Max(0, len - n);
            }
            else
            {
                start = long.Parse(parts[0]);
                if (parts.Length > 1 && parts[1].Length > 0) end = Math.Min(long.Parse(parts[1]), len - 1);
            }
        }
        catch { return false; }

        return start >= 0 && start <= end && start < len;
    }

    private async Task Refuse(HttpListenerContext ctx, int code, string msg, string path, bool cors = false)
    {
        _log.Log($"loopback: {ctx.Request.HttpMethod} {path} -> {code} {msg}");
        if (cors) AddCors(ctx.Response);
        await WriteText(ctx, code, msg, "text/plain");
    }

    private static async Task WriteText(HttpListenerContext ctx, int code, string text, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        ctx.Response.StatusCode = code;
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return; // idempotent teardown (SP-003 discipline)
        _cts.Cancel();
        try { _page.Stop(); } catch { /* best effort */ }
        try { _media.Stop(); } catch { /* best effort */ }
        try { _page.Close(); } catch { /* best effort */ }
        try { _media.Close(); } catch { /* best effort */ }
        _cts.Dispose();
    }
}
