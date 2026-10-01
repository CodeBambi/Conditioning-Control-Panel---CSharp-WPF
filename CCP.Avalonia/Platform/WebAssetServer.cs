using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Serves Resources/web to this head's web views, standing in for WPF's WebView2
/// SetVirtualHostNameToFolderMapping("ccp.game", Resources\web). Avalonia.Controls.WebView 12.1 cannot
/// answer a custom scheme on WebKitGTK and file:// breaks module/fetch loads, so: plain HTTP on 127.0.0.1
/// only, a random port, and a per-run token. The first URL carries ?ccp_t=token; the answer sets an
/// HttpOnly SameSite=Strict cookie so the page's own relative and root-relative loads keep working.
/// Anything without the token, with a foreign Host header (DNS rebinding), or resolving outside the
/// root is refused.
/// </summary>
public sealed class WebAssetServer : IDisposable
{
    static readonly object _sharedLock = new();
    static WebAssetServer? _shared;

    /// <summary>The app-wide server over Resources/web, started on first use; a failed start is retried next time.</summary>
    public static WebAssetServer Shared
    {
        get
        {
            lock (_sharedLock)
                return _shared ??= new WebAssetServer(Path.Combine(AppContext.BaseDirectory, "Resources", "web"))
                    { AssetsRoot = () => ConditioningControlPanel.CorePaths.EffectiveAssets };
        }
    }

    readonly SemaphoreSlim _inFlight = new(16);

    static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8", [".htm"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8", [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8", [".json"] = "application/json; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8", [".svg"] = "image/svg+xml", [".png"] = "image/png",
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".webp"] = "image/webp",
        [".ico"] = "image/x-icon", [".mp3"] = "audio/mpeg", [".ogg"] = "audio/ogg", [".wav"] = "audio/wav",
        [".m4a"] = "audio/mp4", [".mp4"] = "video/mp4", [".webm"] = "video/webm", [".woff"] = "font/woff",
        [".woff2"] = "font/woff2", [".ttf"] = "font/ttf", [".wasm"] = "application/wasm",
    };

    readonly HttpListener _listener = new();
    readonly string _root;

    public int Port { get; }
    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    public WebAssetServer(string root)
    {
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        // ponytail: probe-then-bind can lose the port to another process in between; retry if that ever shows up.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        Port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _ = Task.Run(Loop);
    }

    /// <summary>The URL a web view navigates to for a page under the root, e.g. "intake/index.html".</summary>
    public string Url(string relativePath) =>
        $"http://127.0.0.1:{Port}/{relativePath.TrimStart('/')}?ccp_t={Token}";

    /// <summary>Per-port so two runs on 127.0.0.1 (cookies ignore the port) never overwrite each other.</summary>
    string CookieName => $"ccp_t_{Port}";

    bool IsToken(string? candidate) =>
        candidate != null && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidate), Encoding.UTF8.GetBytes(Token));

    async Task Loop()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch (Exception) { return; } // listener stopped
            await _inFlight.WaitAsync().ConfigureAwait(false);
            _ = Task.Run(() =>
            {
                try { Serve(ctx); }
                catch (Exception ex) { Log.Debug(ex, "WebAssetServer: request failed"); }
                finally { try { ctx.Response.Close(); } catch { } _inFlight.Release(); }
            });
        }
    }

    void Serve(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        bool queryToken = IsToken(req.QueryString["ccp_t"]);
        bool cookieToken = IsToken(req.Cookies[CookieName]?.Value);
        // A foreign Host header (DNS rebinding) never gets here: HttpListener 404s it against the 127.0.0.1 prefix.
        if (!IPAddress.IsLoopback(req.RemoteEndPoint.Address)
            || !(queryToken || cookieToken)
            || (req.HttpMethod != "GET" && req.HttpMethod != "HEAD"))
        {
            res.StatusCode = 403;
            return;
        }

        var path = ResolveFile(req.Url!.AbsolutePath);
        if (path == null) { res.StatusCode = 404; return; }

        if (queryToken)
            res.Headers.Add("Set-Cookie", $"{CookieName}={Token}; Path=/; HttpOnly; SameSite=Strict");
        res.ContentType = Types.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");
        res.Headers.Add("Cache-Control", "no-cache");
        res.Headers.Add("X-Content-Type-Options", "nosniff");
        res.Headers.Add("Referrer-Policy", "no-referrer");
        // ponytail: whole-file responses, no Range; add 206 handling if a page needs to seek long media.
        using var file = File.OpenRead(path);
        res.ContentLength64 = file.Length;
        if (req.HttpMethod == "GET") file.CopyTo(res.OutputStream);
    }

    /// <summary>URL prefix for the user's asset library, WPF's second virtual host <c>https://ccp.assets/</c>
    /// (IntakeHostService maps it over App.EffectiveAssetsPath): same server, same token rule.</summary>
    public const string AssetsPrefix = "ccp.assets/";

    /// <summary>Root behind <see cref="AssetsPrefix"/>, read per request (the user can move the library);
    /// null = the prefix is not served.</summary>
    public Func<string?>? AssetsRoot { get; init; }

    /// <summary>The file a URL path names, or null if it is missing or lies outside its root, symlinks followed.</summary>
    internal string? ResolveFile(string urlPath)
    {
        var rel = Uri.UnescapeDataString(urlPath).TrimStart('/');
        var root = _root;
        if (rel.StartsWith(AssetsPrefix, StringComparison.Ordinal))
        {
            if (AssetsRoot?.Invoke() is not { Length: > 0 } assets) return null;
            root = Path.GetFullPath(assets).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            rel = rel[AssetsPrefix.Length..];
        }
        if (rel.Length == 0 || rel.EndsWith('/')) rel += "index.html";
        if (rel.Contains('\0')) return null;
        var full = Path.GetFullPath(Path.Combine(root, rel));
        return Inside(full, root) && File.Exists(full) && LinksStayInside(full, root) ? full : null;
    }

    static bool Inside(string full, string root) => full.StartsWith(root, StringComparison.Ordinal);

    /// <summary>Every link on the way from the root down to the file must land inside the root.</summary>
    static bool LinksStayInside(string full, string root)
    {
        for (var p = full; p.Length > root.Length; p = Path.GetDirectoryName(p)!)
        {
            FileSystemInfo info = Directory.Exists(p) ? new DirectoryInfo(p) : new FileInfo(p);
            if (info.LinkTarget != null && info.ResolveLinkTarget(returnFinalTarget: true) is { } target
                && !Inside(Path.GetFullPath(target.FullName), root)
                && Path.GetFullPath(target.FullName) + Path.DirectorySeparatorChar != root)
                return false;
        }
        return true;
    }

    public void Dispose() => _listener.Close();
}
