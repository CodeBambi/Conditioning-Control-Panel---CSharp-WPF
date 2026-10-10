using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.Quiz;
using Serilog;

using ConditioningControlPanel;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Serves Resources/web to this head's web views, standing in for WPF's WebView2
/// SetVirtualHostNameToFolderMapping("ccp.game", Resources\web). ONE resolver (<see cref="ResolveFile"/>)
/// decides what a page may load; two transports carry it, and which one a page gets decides its ORIGIN
/// (and so whether its localStorage / IndexedDB survives a restart):
///   - <see cref="VirtualHosts"/> (Windows, WebView2): the pages load <c>https://ccp.game/</c>,
///     <c>https://ccp.assets/</c>, ... exactly as under WPF (Platform/WebView2Hosts answers every
///     <c>https://ccp.*</c> request on the web view from <see cref="ResolveVirtual"/>). Same origin as WPF.
///   - loopback (Linux, and Windows if the hosts cannot be installed): Avalonia.Controls.WebView 12.1
///     cannot answer a custom scheme on WebKitGTK (and a custom scheme could not be https anyway) and
///     file:// breaks module/fetch loads, so plain HTTP on 127.0.0.1 only, with a per-run token. The
///     port is STABLE: chosen once per user-data folder and remembered (<see cref="StablePort"/>), so
///     the origin <c>http://127.0.0.1:&lt;port&gt;</c> is the same every run; only when another program
///     holds the port does one run fall back to a random one. The first URL carries ?ccp_t=token; the
///     answer sets an HttpOnly SameSite=Strict cookie so the page's own relative and root-relative
///     loads keep working. Anything without the token, with a foreign Host header (DNS rebinding), or
///     resolving outside the root is refused.
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
                return _shared ??= new WebAssetServer(Path.Combine(AppContext.BaseDirectory, "Resources", "web"),
                        StablePort.For(CorePaths.UserData), WantsVirtualHosts())
                    { AssetsRoot = () => CorePaths.EffectiveAssets, CacheRoot = () => Services.Transfer.TransferCacheStore.Instance.Root };
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

    readonly HttpListener _listener;
    readonly string _root;

    public int Port { get; }
    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>True when the loopback port is the remembered one (the page origin is the same as last run).</summary>
    public bool PortIsStable { get; }

    /// <param name="root">The page root (WPF's ccp.game folder).</param>
    /// <param name="preferredPort">The remembered loopback port (0 = any free one). Taken if free;
    /// when another program holds it this run gets a random port and <see cref="PortIsStable"/> is false.</param>
    /// <param name="virtualHosts">Hand out <c>https://ccp.*</c> urls (a WebView2 with WebView2Hosts installed).</param>
    public WebAssetServer(string root, int preferredPort = 0, bool virtualHosts = false)
    {
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        VirtualHosts = virtualHosts;
        if (preferredPort > 0 && TryListen(preferredPort, out var stable))
        {
            _listener = stable!;
            Port = preferredPort;
            PortIsStable = true;
        }
        else
        {
            if (preferredPort > 0) Log.Warning("WebAssetServer: port {Port} is taken; this run's pages get a temporary origin (their saved state is untouched and back next run)", preferredPort);
            // ponytail: probe-then-bind can lose the port to another process in between; retry if that ever shows up.
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
        }
        _ = Task.Run(Loop);
    }

    static bool TryListen(int port, out HttpListener? listener)
    {
        listener = new HttpListener();
        try
        {
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            return true;
        }
        catch (Exception)
        {
            try { listener.Close(); } catch { }
            listener = null;
            return false;
        }
    }

    // ---- the page origin ---------------------------------------------------------------------

    /// <summary>WPF's page host: <c>https://ccp.game/</c> is the page root.</summary>
    public const string GameHost = "ccp.game";

    /// <summary>
    /// True while this server hands out WPF's <c>https://ccp.*</c> urls. Only a web view with
    /// WebView2Hosts installed can load them; <c>WebHost</c> installs them, and calls
    /// <see cref="DemoteToLoopback"/> the first time that fails, after which every url is loopback.
    /// </summary>
    public bool VirtualHosts { get; private set; }

    /// <summary>Set once by Program.Main before the app starts. Off everywhere else (headless checks,
    /// tests), where no web view exists to carry the hosts and urls stay loopback.</summary>
    public static bool VirtualHostsEnabled { get; set; }

    /// <summary>The virtual hosts cannot be installed on this machine: loopback urls from here on.</summary>
    internal void DemoteToLoopback() => VirtualHosts = false;

    /// <summary>What <see cref="Shared"/> starts with: WebView2 on Windows, unless CCP_WEB_ORIGIN=loopback.
    /// Decided without building a web view; a failed install demotes it later.</summary>
    static bool WantsVirtualHosts()
    {
        if (!VirtualHostsEnabled || !OperatingSystem.IsWindows()) return false;
        try
        {
            if (string.Equals(Environment.GetEnvironmentVariable("CCP_WEB_ORIGIN")?.Trim(), "loopback", StringComparison.OrdinalIgnoreCase)) return false;
            var info = global::Avalonia.Platform.WebViewAdapterInfo.GetAdapterInfo(global::Avalonia.Platform.WebViewAdapterType.WebView2);
            return info.IsSupported && info.IsInstalled;
        }
        catch (Exception) { return false; }
    }

    /// <summary>The base url of a host: <c>https://ccp.assets/</c>, or its loopback route.</summary>
    string Base(string host) => VirtualHosts
        ? $"https://{host}/"
        : host == GameHost ? $"http://127.0.0.1:{Port}/" : $"http://127.0.0.1:{Port}/{host}/";

    /// <summary>The URL a web view navigates to for a page under the root, e.g. "intake/index.html".
    /// <paramref name="query"/> ("a=1&amp;b=2", no leading mark) rides along; on loopback the token leads.</summary>
    public string Url(string relativePath, string? query = null)
    {
        var page = Base(GameHost) + relativePath.TrimStart('/');
        if (VirtualHosts) return string.IsNullOrEmpty(query) ? page : page + "?" + query;
        return page + "?ccp_t=" + Token + (string.IsNullOrEmpty(query) ? "" : "&" + query);
    }

    /// <summary>True for a url on one of this server's virtual hosts (<c>https://ccp.game/...</c>).</summary>
    public bool IsVirtual(Uri? uri) =>
        uri is { IsAbsoluteUri: true } && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && IsHostName(uri.Host);

    bool IsHostName(string host) =>
        host is GameHost or "ccp.assets" or "ccp.cache" or "ccp.mod" || Hosts.ContainsKey(host);

    /// <summary>True for a url on this server's loopback listener.</summary>
    public bool IsLoopback(Uri? uri) =>
        uri is { IsAbsoluteUri: true } && uri.Scheme == Uri.UriSchemeHttp && uri.Host == "127.0.0.1" && uri.Port == Port;

    /// <summary>A virtual url as the loopback listener serves it (token added); anything else unchanged.</summary>
    public Uri ToLoopback(Uri uri)
    {
        if (!IsVirtual(uri)) return uri;
        var prefix = uri.Host == GameHost ? "" : uri.Host + "/";
        var query = uri.Query.Length > 1 ? "&" + uri.Query[1..] : "";
        return new Uri($"http://127.0.0.1:{Port}/{prefix}{uri.AbsolutePath.TrimStart('/')}?ccp_t={Token}{query}{uri.Fragment}");
    }

    /// <summary>The reverse of <see cref="ToLoopback"/> (token dropped); anything else unchanged.</summary>
    public Uri ToVirtual(Uri uri)
    {
        if (!IsLoopback(uri)) return uri;
        var path = uri.AbsolutePath.TrimStart('/');
        var host = GameHost;
        int cut = path.IndexOf('/');
        if (cut > 0 && path[..cut] != GameHost && IsHostName(path[..cut])) { host = path[..cut]; path = path[(cut + 1)..]; }
        var query = uri.Query.Length > 1
            ? string.Join('&', uri.Query[1..].Split('&').Where(p => !p.StartsWith("ccp_t=", StringComparison.Ordinal)))
            : "";
        return new Uri($"https://{host}/{path}{(query.Length > 0 ? "?" + query : "")}{uri.Fragment}");
    }

    /// <summary>
    /// The file a virtual-host request names, or null when it must be refused: the SAME rules as the
    /// loopback listener, because it is the same resolver. <paramref name="cors"/> is WPF's access
    /// kind (the media hosts are Allow, the page root is Deny).
    /// </summary>
    internal string? ResolveVirtual(Uri uri, out string host, out bool cors)
    {
        host = ""; cors = false;
        if (!IsVirtual(uri)) return null;
        host = uri.Host;
        cors = host != GameHost;
        var path = uri.AbsolutePath;
        if (host != GameHost) return ResolveFile("/" + host + path);
        // The page root never doubles as a second door to another host's route.
        var first = Uri.UnescapeDataString(path).TrimStart('/');
        int cut = first.IndexOf('/');
        if (cut > 0 && IsHostName(first[..cut])) return null;
        return ResolveFile(path);
    }

    /// <summary>The Content-Type a served file gets.</summary>
    internal static string ContentType(string path) => Types.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");

    /// <summary>
    /// The remembered loopback port of a user-data folder (<c>web-origin.port</c>): chosen once, outside
    /// the ranges the OS hands out for outgoing connections, so the loopback page origin is the same
    /// every run. Never rewritten once it exists: changing it would orphan every page's saved state.
    /// </summary>
    internal static class StablePort
    {
        internal const string FileName = "web-origin.port";
        internal const int Low = 20000, High = 32000;   // below Linux 32768 and Windows 49152 ephemeral ranges

        internal static int For(string userData)
        {
            try
            {
                var file = Path.Combine(userData, FileName);
                if (File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), out var saved) && saved is >= 1024 and <= 65535)
                    return saved;
                for (int i = 0; i < 40; i++)
                {
                    int port = RandomNumberGenerator.GetInt32(Low, High);
                    if (!IsFree(port)) continue;
                    Directory.CreateDirectory(userData);
                    File.WriteAllText(file, port.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    return port;
                }
            }
            catch (Exception ex) { Log.Warning("WebAssetServer: no stable port ({Error}); pages get a temporary origin this run", ex.Message); }
            return 0;
        }

        static bool IsFree(int port)
        {
            try
            {
                var probe = new TcpListener(IPAddress.Loopback, port);
                probe.Start();
                probe.Stop();
                return true;
            }
            catch (SocketException) { return false; }
        }
    }

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
        // A foreign Host header (DNS rebinding): the managed (Linux) listener 404s it against the
        // 127.0.0.1 prefix, but Windows' http.sys routes it here (CI answered 200), so refuse it alike.
        if (!string.Equals(req.UserHostName, $"127.0.0.1:{Port}", StringComparison.OrdinalIgnoreCase))
        {
            res.StatusCode = 404;
            return;
        }
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
        res.ContentType = ContentType(path);
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

    /// <summary>The assets folder's temp dir (WPF App.GetMediaTempPath): its top-level files are served.</summary>
    public const string TempFolder = ".temp";

    /// <summary>The URL a page loads a library file by (WPF DtrhAssetManifest.AssetUrl's https://ccp.assets/&lt;rel&gt;):
    /// same origin as the page, so the token cookie covers it; each path segment escaped.</summary>
    public string AssetUrl(string rel) =>
        Base(AssetsPrefix.TrimEnd('/')) + string.Join('/', rel.Replace(Path.DirectorySeparatorChar, '/').Split('/').Select(Uri.EscapeDataString));

    /// <summary>Root behind <see cref="AssetsPrefix"/>, read per request (the user can move the library);
    /// null = the prefix is not served.</summary>
    public Func<string?>? AssetsRoot { get; init; }

    /// <summary>More virtual hosts, each a flat folder of audio clips or gifs (WPF's <c>ccp.subaudio</c>,
    /// <c>ccp.modaudio</c>, <c>ccp.spirals</c> mappings): url prefix -> folder, read per request; null = not served.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, Func<string?>> Hosts { get; } = new(StringComparer.Ordinal);

    /// <summary>The URL of one file on a <see cref="Hosts"/> prefix (same origin as the page, so the token cookie covers it).</summary>
    public string HostUrl(string host, string fileName) => Base(host) + Uri.EscapeDataString(fileName);

    static string? ResolveHosted(string? folder, string name)
    {
        if (string.IsNullOrEmpty(folder) || name.Length == 0 || name.StartsWith('.') || name.IndexOfAny(new[] { '/', (char)92, (char)0 }) >= 0) return null;
        if (Path.GetExtension(name).ToLowerInvariant() is not (".mp3" or ".wav" or ".ogg" or ".gif")) return null;
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, name));
        return Inside(full, root) && File.Exists(full) ? full : null;
    }

    /// <summary>The file a URL path names, or null if it is missing or lies outside its root, symlinks followed.</summary>
    internal string? ResolveFile(string urlPath)
    {
        var rel = Uri.UnescapeDataString(urlPath).TrimStart('/');
        if (rel.StartsWith(CachePrefix, StringComparison.Ordinal)) return ResolveCacheFile(rel[CachePrefix.Length..]);
        if (rel.StartsWith(ModPrefix, StringComparison.Ordinal)) return ResolveModFile(rel[ModPrefix.Length..]);
        var root = _root;
        int hostCut = rel.IndexOf('/');
        if (hostCut > 0 && Hosts.TryGetValue(rel[..hostCut], out var hosted)) return ResolveHosted(hosted(), rel[(hostCut + 1)..]);
        bool asset = rel.StartsWith(AssetsPrefix, StringComparison.Ordinal);
        if (asset)
        {
            if (AssetsRoot?.Invoke() is not { Length: > 0 } assets) return null;
            root = Path.GetFullPath(assets).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            rel = rel[AssetsPrefix.Length..];
            // Media only, never the app's own dot-folders (.packs, ...), never the profile itself. The one
            // exception is a file directly in .temp: WPF maps ccp.assets over the whole assets folder, and the
            // Back Room's warm Scrolller clips (and pack decrypts) land there (AGENTS.md: a legal ccp.assets url).
            var segs = rel.Split('/', '\\');
            bool tempFile = segs.Length == 2 && segs[0] == TempFolder && segs[1].Length > 0 && !segs[1].StartsWith('.');
            if (!MediaTypeSniffer.MediaExtensions.Contains(Path.GetExtension(rel))
                || (!tempFile && segs.Any(seg => seg.StartsWith('.')))
                || HoldsUserData(root)) return null;
        }
        if (rel.Length == 0 || rel.EndsWith('/')) rel += "index.html";
        if (rel.Contains('\0')) return null;
        var full = Path.GetFullPath(Path.Combine(root, rel));
        if (!Inside(full, root) || !File.Exists(full) || !LinksStayInside(full, root)) return null;
        if (asset && !IntakeRun.IsAssetActive(IntakeRun.DisabledAssetSet(DisabledAssets()), root, full)) return null;
        return full;
    }

    // ---- ccp.mod (a creator mod's own DtRH content) ------------------------------------------
    /// <summary>URL prefix for the active mod's <c>resources/dtrh</c> folder, WPF's <c>https://ccp.mod/</c>
    /// virtual host (DtrhHostService / CaucusHostService map it at launch): same server, same token rule.</summary>
    public const string ModPrefix = "ccp.mod/";

    /// <summary>Root behind <see cref="ModPrefix"/>, read per request (the active mod can change); null = not served.</summary>
    public Func<string?>? ModRoot { get; set; }

    /// <summary>The page-side base for mod files (WPF "https://ccp.mod/").</summary>
    public string ModUrlBase => Base(ModPrefix.TrimEnd('/'));

    static readonly HashSet<string> ModExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".ogg", ".wav", ".png", ".jpg", ".jpeg", ".webp", ".gif", ".mp4", ".webm", ".m4v" };

    /// <summary>Media only (the kinds DtrhModContent hands out), no dot-files, never a link out of the folder.</summary>
    string? ResolveModFile(string rel)
    {
        if (ModRoot?.Invoke() is not { Length: > 0 } mod || rel.Length == 0 || rel.Contains('\0')) return null;
        if (!ModExtensions.Contains(Path.GetExtension(rel)) || rel.Split('/', (char)92).Any(seg => seg.StartsWith('.'))) return null;
        var root = Path.GetFullPath(mod).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, rel));
        return Inside(full, root) && File.Exists(full) && LinksStayInside(full, root) ? full : null;
    }

    // ---- ccp.cache (Goon own-media transfer) -------------------------------------------------
    /// <summary>URL prefix for the transfer cache, WPF's third virtual host <c>https://ccp.cache/</c>
    /// (GoonHostService maps it over {userdata}/transfer-cache): same server, same token rule.</summary>
    public const string CachePrefix = "ccp.cache/";

    /// <summary>Root behind <see cref="CachePrefix"/> (the folder that holds art/, prv/, recv/); null = not served.</summary>
    public Func<string?>? CacheRoot { get; init; }

    /// <summary>The page-side base for cache files (WPF "https://ccp.cache/").</summary>
    public string CacheUrlBase => Base(CachePrefix.TrimEnd('/'));

    /// <summary>Exactly <c>art|prv|recv / &lt;64 hex&gt;.&lt;ext&gt;</c> and nothing else: never the index files,
    /// never a nested path, never the .tmp sibling (it is outside the root), never a link out.</summary>
    string? ResolveCacheFile(string rel)
    {
        if (CacheRoot?.Invoke() is not { Length: > 0 } cache) return null;
        var segs = rel.Split('/');
        if (segs.Length != 2 || segs[0] is not ("art" or "prv" or "recv")) return null;
        var name = segs[1];
        int dot = name.IndexOf('.');
        if (dot != 64 || name.LastIndexOf('.') != dot || !name.AsSpan(0, 64).ToString().All(Uri.IsHexDigit)) return null;
        if (!MediaTypeSniffer.MediaExtensions.Contains(Path.GetExtension(name))) return null;
        var root = Path.GetFullPath(cache).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, segs[0], name));
        return Inside(full, root) && File.Exists(full) && LinksStayInside(full, root) ? full : null;
    }

    /// <summary>The user's unchecked assets (Settings DisabledAssetPaths), never served.</summary>
    public Func<IEnumerable<string>?> DisabledAssets { get; init; } = () => CoreSettings.Current.DisabledAssetPaths;

    int _userDataWarned;

    /// <summary>An assets root that is, or contains, the profile folder would expose settings and secrets.</summary>
    bool HoldsUserData(string root)
    {
        var data = Path.GetFullPath(CorePaths.UserData).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!FolderHolds(root, data, OperatingSystem.IsWindows())) return false;
        if (Interlocked.Exchange(ref _userDataWarned, 1) == 0)
            Log.Warning("WebAssetServer: refusing to serve assets from {Root}: it holds the profile folder", root);
        return true;
    }

    /// <summary>IB10: is <paramref name="folder"/> (ending in a separator) at or under <paramref name="root"/>
    /// (ending in a separator)? Windows paths differ in letter case and still name one folder, so the
    /// refusal there ignores case; elsewhere a path is exactly its letters.</summary>
    internal static bool FolderHolds(string root, string folder, bool ignoreCase) =>
        folder.StartsWith(root, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

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
