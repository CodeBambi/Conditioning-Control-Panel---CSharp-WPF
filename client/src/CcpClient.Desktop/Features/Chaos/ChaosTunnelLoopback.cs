using System.Net;
using System.Text;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Chaos;

/// <summary>
/// The tunnel payload's §4-discipline loopback origin (SP-023 record Step 1 design item 5,
/// approved-by-decree contract class — the DTRH `LoopbackServer` is NOT reused: its
/// /dtrh/+/media/+/bridge/ route table and inbox are DTRH-shaped, and the intake-style
/// overlay borrow would expose the whole payload/ parent; pre-approach consult ruling 1).
/// ONE GET-only origin on 127.0.0.1 (the tunnel serves no media — no second origin, no CORS
/// split; WPF uses a single virtual host for the same reason, ChaosTunnelService.cs:43-46):
///
///   /health, /tunnel/* → output payload/tunnel, /vendor/* → output payload/vendor.
///
/// Traversal/MIME/refusal logic mirrors `Features/Dtrh/LoopbackServer.cs` (TryResolve
/// `:393-414`, deny-by-default 415 `:444-448`, nosniff, GET-only 405) — the cited lines are
/// the security-hardening sweep points; a shared-invariant test pins both servers.
/// Route-class logging is TIGHTER than the DTRH shape: ONE segment only (`/tunnel/`,
/// `/vendor/`) — presence/shape, never a filename, never a query string.
/// </summary>
public sealed class ChaosTunnelLoopback : IDisposable
{
    /// <summary>MIME allowlist DERIVED by sweeping both payload trees (the §4.4 extension-sweep
    /// discipline): Resources/web/tunnel = .html×1 + .js×8; Resources/web/vendor/three = .js×9.
    /// ChaosTunnelLoopbackTests walks the OUTPUT roots and asserts every file's extension is in
    /// this table — the pin fails loud when the trees drift. Unknown extension → 415.</summary>
    private static readonly Dictionary<string, string> Mime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
    };

    /// <summary>The swept-extension pin (see Mime — derived, never asserted). Exposed for
    /// the drift-guard test that re-sweeps the upstream trees.</summary>
    public static IReadOnlyDictionary<string, string> AllowedMime => Mime;

    private readonly string _tunnelRoot;
    private readonly string _vendorRoot;
    private readonly Action<string> _log;
    private HttpListener _listener = null!; // assigned by Start (BindWithRetry)
    private readonly CancellationTokenSource _cts = new();
    private int _stopped;

    public int Port { get; private set; }
    public string Origin => $"http://127.0.0.1:{Port}";

    public ChaosTunnelLoopback(string tunnelRoot, string vendorRoot, Action<string> log)
    {
        _tunnelRoot = Path.GetFullPath(tunnelRoot);
        _vendorRoot = Path.GetFullPath(vendorRoot);
        _log = log;
    }

    public void Start()
    {
        // §4.1 ephemeral-port retry loop (LoopbackServer.cs:123-148): a FAILED
        // HttpListener.Start() DISPOSES the instance (SP-023 surprise #1) — every attempt
        // uses a FRESH listener; the 49152–65535 range IS the OS dynamic client range, so
        // collisions are real and the retry loop must actually work.
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
                _listener = listener;
                Port = port;
                _log("chaos-tunnel-loopback: origin bound on 127.0.0.1 (ephemeral)");
                _ = Task.Run(() => AcceptLoop(listener));
                return;
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

    private async Task AcceptLoop(HttpListener listener)
    {
        while (listener.IsListening && !_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync().ConfigureAwait(false); }
            catch (Exception) when (!listener.IsListening || _cts.IsCancellationRequested) { return; }
            catch (Exception ex) { _log($"chaos-tunnel-loopback: accept fault {ex.GetType().Name}"); continue; }

            _ = Task.Run(async () =>
            {
                try { await Handle(ctx).ConfigureAwait(false); }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException)
                {
                    // Client went away mid-response (renderer stall/teardown race) — never a fault.
                }
                catch (Exception ex) { _log($"chaos-tunnel-loopback: handler fault {ex.GetType().Name}: {ex.Message}"); }
                finally { try { ctx.Response.OutputStream.Close(); } catch { /* best effort */ } }
            });
        }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var path = req.Url?.AbsolutePath ?? "/";
        if (req.HttpMethod != "GET")
        {
            await Refuse(ctx, 405, "refused: GET-only origin", RouteClass(path));
            return;
        }

        if (path == "/health")
        {
            await WriteText(ctx, 200, "ok", "text/plain");
            _log("chaos-tunnel-loopback: GET /health -> 200");
            return;
        }

        string root;
        string rel;
        if (path.StartsWith("/tunnel/", StringComparison.Ordinal))
        {
            root = _tunnelRoot;
            rel = path["/tunnel/".Length..];
        }
        else if (path.StartsWith("/vendor/", StringComparison.Ordinal))
        {
            root = _vendorRoot;
            rel = path["/vendor/".Length..];
        }
        else
        {
            await Refuse(ctx, 404, "refused: no such route (only /tunnel/* and /vendor/* are exposed)", RouteClass(path));
            return;
        }

        if (rel.Length == 0) rel = "index.html";
        if (!TryResolve(root, rel, out var file))
        {
            await Refuse(ctx, 403, "refused: path traversal", RouteClass(path));
            return;
        }

        if (!File.Exists(file))
        {
            await Refuse(ctx, 404, "not found", RouteClass(path));
            return;
        }

        // MIME allowlist, deny-by-default (§4.4): unknown extension -> 415.
        if (!Mime.TryGetValue(Path.GetExtension(file), out var contentType))
        {
            await Refuse(ctx, 415, "refused: extension outside the swept allowlist", RouteClass(path));
            return;
        }

        var bytes = await File.ReadAllBytesAsync(file, _cts.Token).ConfigureAwait(false);
        var res = ctx.Response;
        res.StatusCode = 200;
        res.ContentType = contentType;
        res.Headers["X-Content-Type-Options"] = "nosniff";
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes, _cts.Token).ConfigureAwait(false);
        _log($"chaos-tunnel-loopback: GET {RouteClass(path)} -> 200");
    }

    /// <summary>Decode, reject traversal/absolute weirdness, resolve under root
    /// (LoopbackServer.cs:393-414 — the mirrored refusal set). Root is never writable here.</summary>
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

    /// <summary>Route-class logging (§4.8), TIGHTER than the DTRH two-segment shape: the
    /// route ROOT only — never a filename, never a query string.</summary>
    private static string RouteClass(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? "/" : "/" + segments[0] + "/";
    }

    private async Task Refuse(HttpListenerContext ctx, int code, string msg, string routeClass)
    {
        _log($"chaos-tunnel-loopback: {ctx.Request.HttpMethod} {routeClass} -> {code}");
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
        try { _listener?.Stop(); } catch { /* best effort */ }
        try { _listener?.Close(); } catch { /* best effort */ }
        _cts.Dispose();
    }
}
