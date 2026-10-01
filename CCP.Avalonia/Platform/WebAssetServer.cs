using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
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
    const string TokenName = "ccp_t";

    static readonly Lazy<WebAssetServer> _shared = new(() =>
        new WebAssetServer(Path.Combine(AppContext.BaseDirectory, "Resources", "web")));

    /// <summary>The app-wide server over Resources/web, started on first use.</summary>
    public static WebAssetServer Shared => _shared.Value;

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
        $"http://127.0.0.1:{Port}/{relativePath.TrimStart('/')}?{TokenName}={Token}";

    async Task Loop()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch (Exception) { return; } // listener stopped
            try { Serve(ctx); }
            catch (Exception ex) { Log.Debug(ex, "WebAssetServer: request failed"); }
            finally { try { ctx.Response.Close(); } catch { } }
        }
    }

    void Serve(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        bool queryToken = req.QueryString[TokenName] == Token;
        bool cookieToken = req.Cookies[TokenName]?.Value == Token;
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
            res.Headers.Add("Set-Cookie", $"{TokenName}={Token}; Path=/; HttpOnly; SameSite=Strict");
        res.ContentType = Types.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");
        res.Headers.Add("Cache-Control", "no-cache");
        // ponytail: whole-file responses, no Range; add 206 handling if a page needs to seek long media.
        using var file = File.OpenRead(path);
        res.ContentLength64 = file.Length;
        if (req.HttpMethod == "GET") file.CopyTo(res.OutputStream);
    }

    /// <summary>The file a URL path names, or null if it is missing or lies outside the root.</summary>
    internal string? ResolveFile(string urlPath)
    {
        var rel = Uri.UnescapeDataString(urlPath).TrimStart('/');
        if (rel.Length == 0 || rel.EndsWith('/')) rel += "index.html";
        if (rel.Contains('\0')) return null;
        var full = Path.GetFullPath(Path.Combine(_root, rel));
        return full.StartsWith(_root, StringComparison.Ordinal) && File.Exists(full) ? full : null;
    }

    public void Dispose() => _listener.Close();
}
