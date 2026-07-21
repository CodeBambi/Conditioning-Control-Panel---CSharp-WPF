using System.Net;
using System.Text;
using System.Text.Json;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// The §4 loopback security contract (dtrh-admission.md, approved-by-decree): two GET-only
/// origins on 127.0.0.1, ephemeral ports (random 49152–65535 retry loop — HttpListener
/// cannot bind port 0). Page origin: /health, /dtrh/* overlay-first over the READ-ONLY
/// payload copy, /bridge/&lt;token&gt;/inbox long-poll (§3.3). Media origin: /media/* with
/// CORS scoped to the page origin (preserves the WPF ccp.game/ccp.assets cross-origin split
/// so taint checks stay meaningful), OPTIONS preflight, CORS-on-errors (SP-011 W18 lesson).
/// Range 206/416; MIME allowlist + nosniff + 415 deny-by-default (allowlist pinned by the
/// §4.4 extension sweep of the trust-anchored tree); traversal refusal 403.
/// Sensitive-logging ban (§4.8): route classes only — query strings are stripped (the
/// bridge token rides in the navigated URL query) and the token never appears in logs.
/// </summary>
public sealed class LoopbackServer : IDisposable
{
    /// <summary>The §4.4-pinned allowlist: exactly the 9 extensions in the trust-anchored
    /// tree (css 1, gif 6, html 2, js 74, json 1, mp3 1306, png 129, webm 1, webp 16).</summary>
    private static readonly Dictionary<string, string> Mime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".css"] = "text/css; charset=utf-8",
        [".gif"] = "image/gif",
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".mp3"] = "audio/mpeg",
        [".png"] = "image/png",
        [".webm"] = "video/webm",
        [".webp"] = "image/webp",
    };

    private readonly string _payloadRoot;   // output payload/dtrh (READ-ONLY at runtime)
    private readonly string _overlayRoot;   // output payload-overlay (product-owned)
    private readonly string _mediaRoot;     // output payload/dtrh/assets (READ-ONLY)
    private readonly Inbox _inbox;
    private readonly string _bridgeToken;
    private readonly TimeSpan _longPollTimeout;
    private readonly ILogSink _log;
    private HttpListener _page = null!;   // assigned by Start (BindWithRetry)
    private HttpListener _media = null!;  // assigned by Start (BindWithRetry)
    private readonly CancellationTokenSource _cts = new();
    private int _stopped;

    public int PagePort { get; private set; }
    public int MediaPort { get; private set; }
    public string PageOrigin => $"http://127.0.0.1:{PagePort}";
    public string MediaOrigin => $"http://127.0.0.1:{MediaPort}";

    public LoopbackServer(
        string payloadRoot, string overlayRoot, string mediaRoot,
        Inbox inbox, string bridgeToken, ILogSink log, TimeSpan? longPollTimeout = null)
    {
        _payloadRoot = Path.GetFullPath(payloadRoot);
        _overlayRoot = Path.GetFullPath(overlayRoot);
        _mediaRoot = Path.GetFullPath(mediaRoot);
        _inbox = inbox;
        _bridgeToken = bridgeToken;
        _log = log;
        _longPollTimeout = longPollTimeout ?? TimeSpan.FromSeconds(25);
    }

    public void Start()
    {
        (_page, PagePort) = BindWithRetry();
        (_media, MediaPort) = BindWithRetry();
        _log.Log("dtrh-loopback: page+media origins bound on 127.0.0.1 (ephemeral)");
        _ = Task.Run(() => AcceptLoop(_page, HandlePage));
        _ = Task.Run(() => AcceptLoop(_media, HandleMedia));
    }

    /// <summary>
    /// §4.1 ephemeral-port retry loop. Empirical (SP-023): a FAILED HttpListener.Start()
    /// disposes the instance (a retry on the same instance throws ObjectDisposedException),
    /// so every attempt uses a FRESH listener; the failed one is closed. Collisions are
    /// real: the 49152–65535 range IS the OS dynamic client range, so outbound connections
    /// (incl. this host's own long-polls, TIME_WAIT) collide with binds — the retry loop
    /// is the contract's answer and must actually work.
    /// </summary>
    private (HttpListener Listener, int Port) BindWithRetry()
    {
        Exception? first = null;
        Exception? last = null;
        for (var i = 0; i < 60; i++)
        {
            var port = Random.Shared.Next(49152, 65536);
            var listener = new HttpListener();
            try
            {
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                listener.Start();
                return (listener, port);
            }
            catch (Exception ex)
            {
                first ??= ex;
                last = ex;
                try { listener.Close(); } catch { /* best effort */ }
            }
        }

        throw new InvalidOperationException($"no free ephemeral port in 60 tries (first: {first?.GetType().Name}: {first?.Message}; last: {last?.GetType().Name}: {last?.Message})");
    }

    private async Task AcceptLoop(HttpListener listener, Func<HttpListenerContext, Task> handler)
    {
        while (listener.IsListening && !_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync().ConfigureAwait(false); }
            catch (Exception) when (!listener.IsListening || _cts.IsCancellationRequested) { return; }
            catch (Exception ex) { _log.Log($"dtrh-loopback: accept fault {ex.GetType().Name}"); continue; }

            _ = Task.Run(async () =>
            {
                try { await handler(ctx).ConfigureAwait(false); }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException)
                {
                    // Client went away mid-response (renderer stall/teardown race) — never a fault.
                }
                catch (Exception ex) { _log.Log($"dtrh-loopback: handler fault {ex.GetType().Name}: {ex.Message}"); }
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
            _log.Log("dtrh-loopback: GET /health -> 200");
            return;
        }

        // §3.3 inbox: /bridge/<token>/inbox?after=N — token required (404 otherwise),
        // JSON only, never payload bytes. The token is NEVER logged.
        if (path.StartsWith("/bridge/", StringComparison.Ordinal))
        {
            if (path == $"/bridge/{_bridgeToken}/inbox")
            {
                await HandleInbox(ctx);
                return;
            }

            // Fixed route class: a wrong-token path contains the presented token — never log it (§4.8).
            await Refuse(ctx, 404, "refused: no such route", "bridge:unknown");
            return;
        }

        if (!path.StartsWith("/dtrh/", StringComparison.Ordinal))
        {
            await Refuse(ctx, 404, "refused: no such route (only /dtrh/* is exposed)", RouteClass(path));
            return;
        }

        var rel = path["/dtrh/".Length..];
        if (rel.Length == 0) rel = "index.html";
        if (!TryResolve(_overlayRoot, rel, out var overlayFile) || !TryResolve(_payloadRoot, rel, out var payloadFile))
        {
            await Refuse(ctx, 403, "refused: path traversal", RouteClass(path));
            return;
        }

        string file, source;
        if (File.Exists(overlayFile)) { file = overlayFile; source = "overlay"; }
        else if (File.Exists(payloadFile)) { file = payloadFile; source = "payload"; }
        else { await Refuse(ctx, 404, "not found", RouteClass(path)); return; }

        await ServeFile(ctx, file, $"page:{source}", cors: false);
    }

    private async Task HandleInbox(HttpListenerContext ctx)
    {
        var afterText = ctx.Request.QueryString["after"];
        if (!long.TryParse(afterText, out var after) || after < 0)
        {
            await Refuse(ctx, 400, "refused: inbox requires ?after=N", "bridge:inbox");
            return;
        }

        IReadOnlyList<(long Seq, string Body)> messages;
        try
        {
            messages = await _inbox.PollAsync(after, _longPollTimeout, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // teardown released the poller; the aborted response is fine
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("messages");
            foreach (var (seq, body) in messages)
            {
                writer.WriteStartObject();
                writer.WriteNumber("seq", seq);
                writer.WritePropertyName("body");
                using (var doc = JsonDocument.Parse(body))
                {
                    doc.RootElement.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        var bytes = stream.ToArray();
        var res = ctx.Response;
        res.StatusCode = 200;
        res.ContentType = "application/json; charset=utf-8";
        res.Headers["X-Content-Type-Options"] = "nosniff";
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        _log.Log($"dtrh-loopback: GET bridge:inbox -> 200 ({messages.Count} message(s))");
    }

    // ---------- media origin ----------

    private async Task HandleMedia(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var path = req.Url?.AbsolutePath ?? "/";

        // CORS preflight (Range is NOT a safelisted request header — §4.1).
        if (req.HttpMethod == "OPTIONS")
        {
            var r = ctx.Response;
            r.StatusCode = 204;
            AddCors(r);
            r.Headers["Access-Control-Allow-Methods"] = "GET, OPTIONS";
            r.Headers["Access-Control-Allow-Headers"] = "range";
            r.Headers["Access-Control-Max-Age"] = "600";
            _log.Log("dtrh-loopback: OPTIONS media -> 204 preflight");
            return;
        }

        if (req.HttpMethod != "GET")
        {
            await Refuse(ctx, 405, "refused: GET-only origin", RouteClass(path), cors: true);
            return;
        }

        if (!path.StartsWith("/media/", StringComparison.Ordinal))
        {
            await Refuse(ctx, 404, "refused: no such route (only /media/* is exposed)", RouteClass(path), cors: true);
            return;
        }

        var rel = path["/media/".Length..];
        if (!TryResolve(_mediaRoot, rel, out var file))
        {
            await Refuse(ctx, 403, "refused: path traversal", RouteClass(path), cors: true);
            return;
        }

        if (!File.Exists(file))
        {
            await Refuse(ctx, 404, "not found", RouteClass(path), cors: true);
            return;
        }

        await ServeFile(ctx, file, "media", cors: true);
    }

    // ---------- shared ----------

    /// <summary>Decode, reject traversal/absolute weirdness, resolve under root. Root is never writable here.</summary>
    private static bool TryResolve(string root, string rel, out string full)
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
        var path = req.Url?.AbsolutePath ?? "/";
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
                res.Headers["X-Content-Type-Options"] = "nosniff";
                if (cors) AddCors(res);
                _log.Log($"dtrh-loopback: GET {RouteClass(path)} -> 416");
                return;
            }

            status = 206;
            res.Headers["Content-Range"] = $"bytes {start}-{end}/{len}";
        }

        // MIME allowlist, deny-by-default (§4.4): unknown extension -> 415.
        if (!Mime.TryGetValue(Path.GetExtension(file), out var contentType))
        {
            await Refuse(ctx, 415, "refused: extension outside the pinned allowlist", RouteClass(path), cors);
            return;
        }

        res.StatusCode = status;
        res.ContentType = contentType;
        res.Headers["X-Content-Type-Options"] = "nosniff";
        res.ContentLength64 = end - start + 1;
        if (cors) AddCors(res);

        await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Seek(start, SeekOrigin.Begin);
        var remaining = end - start + 1;
        var buf = new byte[64 * 1024];
        while (remaining > 0)
        {
            var n = await fs.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, remaining)), _cts.Token).ConfigureAwait(false);
            if (n <= 0) break;
            await res.OutputStream.WriteAsync(buf.AsMemory(0, n), _cts.Token).ConfigureAwait(false);
            remaining -= n;
        }

        _log.Log($"dtrh-loopback: GET {RouteClass(path)} -> {status} ({source})");
    }

    private void AddCors(HttpListenerResponse res)
    {
        res.Headers["Access-Control-Allow-Origin"] = PageOrigin;
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

    /// <summary>
    /// Route-class logging (§4.8 sensitive-logging ban): first two path segments only —
    /// never the full path, never a query string (the bridge token rides in the navigated
    /// URL's query), never the token route's path.
    /// </summary>
    private static string RouteClass(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length switch
        {
            0 => "/",
            1 => "/" + segments[0] + "/",
            _ => "/" + segments[0] + "/" + segments[1] + "/…",
        };
    }

    private async Task Refuse(HttpListenerContext ctx, int code, string msg, string routeClass, bool cors = false)
    {
        _log.Log($"dtrh-loopback: {ctx.Request.HttpMethod} {routeClass} -> {code}");
        if (cors) AddCors(ctx.Response);
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
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
        _inbox.ReleaseAll(); // hanging long-polls complete empty before the listeners stop
        try { _page.Stop(); } catch { /* best effort */ }
        try { _media.Stop(); } catch { /* best effort */ }
        try { _page.Close(); } catch { /* best effort */ }
        try { _media.Close(); } catch { /* best effort */ }
        _cts.Dispose();
    }
}
