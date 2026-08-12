using System.Net;
using CcpClient.Desktop.Features.Chaos;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Manifest;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-061: the tunnel loopback origin's §4-discipline contract — GET-only (405), both
/// payload routes (200 + correct MIME), 415 deny-by-default (negative control), traversal
/// refusal (403), route/missing-file 404s, nosniff, and the tightened route-class logging
/// (one segment — never a filename, never a query string). Plus the consult's pins: the
/// MIME allowlist is DERIVED from the swept upstream trees (drift guard), the manifest
/// entries two-direction-validate against the upstream trees, and the §4 shared invariants
/// hold on BOTH this server and the DTRH LoopbackServer it mirrors.
/// </summary>
public sealed class ChaosTunnelLoopbackTests : IDisposable
{
    private readonly string _root;
    private readonly CollectingLog _log = new();
    private readonly ChaosTunnelLoopback _server;

    public ChaosTunnelLoopbackTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ccp-sp061-loopback-" + Guid.NewGuid().ToString("N"));
        var tunnel = Path.Combine(_root, "tunnel");
        var vendor = Path.Combine(_root, "vendor", "three");
        Directory.CreateDirectory(tunnel);
        Directory.CreateDirectory(vendor);
        File.WriteAllText(Path.Combine(tunnel, "index.html"), "<html>tunnel</html>");
        File.WriteAllText(Path.Combine(tunnel, "main.js"), "// main");
        File.WriteAllText(Path.Combine(vendor, "three.module.min.js"), "// three");
        File.WriteAllText(Path.Combine(tunnel, "sneaky.png"), "not a real png"); // negative control

        _server = new ChaosTunnelLoopback(tunnel, Path.Combine(_root, "vendor"), _log.Log);
        _server.Start();
        LoopbackListenerRegistry.Register(nameof(ChaosTunnelLoopbackTests), _server.Port, _server.Origin);
    }

    public void Dispose()
    {
        _server.Dispose();
        LoopbackListenerRegistry.Unregister(_server.Port); // only after a successful dispose (registry discipline)
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class CollectingLog : CcpClient.Desktop.Lifecycle.ILogSink
    {
        private readonly object _gate = new();
        private readonly List<string> _lines = [];

        public void Log(string message)
        {
            lock (_gate) { _lines.Add(message); }
        }

        public string All { get { lock (_gate) { return string.Join('\n', _lines); } } }
    }

    [Fact]
    public async Task Health_Returns200()
    {
        using var client = new HttpClient();
        var response = await client.GetAsync(_server.Origin + "/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TunnelRoute_ServesIndexWithHtmlMime()
    {
        using var client = new HttpClient();
        var response = await client.GetAsync(_server.Origin + "/tunnel/index.html", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/html", response.Content.Headers.ContentType?.ToString(), StringComparison.Ordinal);
        Assert.Equal("nosniff", string.Join(",", response.Headers.GetValues("X-Content-Type-Options")));
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("tunnel", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VendorRoute_ServesJsWithJavascriptMime()
    {
        using var client = new HttpClient();
        // The exact resolution the page's import map performs (../vendor/three from /tunnel/).
        var response = await client.GetAsync(_server.Origin + "/vendor/three/three.module.min.js", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/javascript", response.Content.Headers.ContentType?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetOnly_PostIs405()
    {
        using var client = new HttpClient();
        var response = await client.PostAsync(_server.Origin + "/tunnel/index.html", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task DenyByDefault_DisallowedExtensionIs415_NegativeControl()
    {
        using var client = new HttpClient();
        var response = await client.GetAsync(_server.Origin + "/tunnel/sneaky.png", TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode)415, response.StatusCode);
    }

    [Theory]
    [InlineData("/tunnel/%2e%2e%2fsecret.js")]   // encoded traversal (encoded slash survives Uri normalization)
    [InlineData("/vendor/%2e%2e%2ftunnel%2findex.html")] // cross-root traversal attempt
    [InlineData("/tunnel/a%5cb.js")]             // backslash
    [InlineData("/tunnel/a:b.js")]               // colon
    // NOTE: a bare "%2e%2e/" segment (no encoded slash) never reaches the server —
    // System.Uri unescapes %2e to '.' and dot-segment-removes it client-side, so that
    // shape answers 404 (route refusal) instead. Still refused, never served.
    public async Task Traversal_IsRefused403(string path)
    {
        using var client = new HttpClient();
        var response = await client.GetAsync(_server.Origin + path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_AndMissingFile_Are404()
    {
        using var client = new HttpClient();
        var unknown = await client.GetAsync(_server.Origin + "/dtrh/index.html", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var missing = await client.GetAsync(_server.Origin + "/tunnel/absent.js", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Logging_RouteClassesOnly_NeverFilenameOrQuery()
    {
        using var client = new HttpClient();
        await client.GetAsync(_server.Origin + "/tunnel/index.html?probe=shh-secret", TestContext.Current.CancellationToken);
        await client.GetAsync(_server.Origin + "/vendor/three/three.module.min.js", TestContext.Current.CancellationToken);
        var logs = _log.All;
        Assert.Contains("/tunnel/", logs, StringComparison.Ordinal);
        Assert.Contains("/vendor/", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("index.html", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("three.module.min.js", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("shh-secret", logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SharedInvariants_HoldOnTheMirroredDtrhServerToo()
    {
        // The consult's pin: the §4 invariants this server mirrors from LoopbackServer
        // (GET-only, traversal refusal, deny-by-default 415) hold on BOTH implementations —
        // a security-hardening sweep finding one must find the other.
        var dtrhRoot = Path.Combine(_root, "dtrh-fixture");
        var overlay = Path.Combine(_root, "dtrh-overlay");
        var media = Path.Combine(dtrhRoot, "assets");
        Directory.CreateDirectory(media);
        Directory.CreateDirectory(overlay);
        File.WriteAllText(Path.Combine(dtrhRoot, "weird.bin"), "binary");
        var inbox = new Inbox();
        var dtrh = new LoopbackServer(dtrhRoot, overlay, media, inbox, "token", _log,
            longPollTimeout: TimeSpan.FromMilliseconds(200));
        dtrh.Start();
        LoopbackListenerRegistry.RegisterLoopbackServer(nameof(ChaosTunnelLoopbackTests), dtrh);
        try
        {
            using var client = new HttpClient();
            Assert.Equal(HttpStatusCode.MethodNotAllowed,
                (await client.PostAsync(dtrh.PageOrigin + "/dtrh/index.html", null, TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.GetAsync(dtrh.PageOrigin + "/dtrh/%2e%2e%2fsecret.js", TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal((HttpStatusCode)415,
                (await client.GetAsync(dtrh.PageOrigin + "/dtrh/weird.bin", TestContext.Current.CancellationToken)).StatusCode);
        }
        finally
        {
            dtrh.Dispose();
            LoopbackListenerRegistry.UnregisterLoopbackServer(dtrh);
        }
    }

    // ---------- derived pins (never asserted from memory) ----------

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "client", "CcpClient.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("repo root not found — the guard refuses to skip (FindRepoRoot precedent)");
    }

    [Fact]
    public void MimeAllowlist_IsDerivedFromTheSweptUpstreamTrees()
    {
        // The §4.4 extension-sweep discipline: walk the REAL upstream trees (the exact
        // bytes the linked globs copy) and prove the swept extension set IS the server's
        // allowlist — a tree that gains a new extension fails here until the pin moves.
        var webRoot = Path.Combine(FindRepoRoot(), "ConditioningControlPanel", "Resources", "web");
        var swept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tree in new[] { "tunnel", "vendor" })
        {
            var treeRoot = Path.Combine(webRoot, tree);
            Assert.True(Directory.Exists(treeRoot), $"upstream tree missing: {treeRoot} — the sweep never skips");
            foreach (var file in Directory.EnumerateFiles(treeRoot, "*", SearchOption.AllDirectories))
            {
                swept.Add(Path.GetExtension(file));
            }
        }

        Assert.Equal(
            new HashSet<string>([".html", ".js"], StringComparer.OrdinalIgnoreCase),
            swept);
        Assert.Equal(swept.Count, ChaosTunnelLoopback.AllowedMime.Count);
        foreach (var ext in swept)
        {
            Assert.True(ChaosTunnelLoopback.AllowedMime.ContainsKey(ext),
                $"swept extension '{ext}' is not in the server's allowlist — the pin is stale");
        }
    }

    [Fact]
    public void Manifest_TunnelEntries_TwoDirectionAgainstUpstreamTrees()
    {
        // Forward: every manifest tunnel/vendor entry maps to a REAL upstream file.
        // Sweep: every upstream file has a manifest entry. (The output-side direction is
        // --verify-assets' copied sweep — SP-009's two-direction rule.)
        var repoRoot = FindRepoRoot();
        var manifestPath = Path.Combine(repoRoot, "client", "src", "CcpClient.Desktop", "Assets", "assets.manifest.json");
        Assert.True(AssetManifest.TryParse(File.ReadAllText(manifestPath), out var entries, out var errors),
            "manifest must parse: " + string.Join("; ", errors));

        var tunnelEntries = entries!
            .Where(e => e.Path.StartsWith("payload/tunnel/", StringComparison.Ordinal)
                || e.Path.StartsWith("payload/vendor/", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(18, tunnelEntries.Length);
        Assert.All(tunnelEntries, e =>
        {
            Assert.Equal(AssetSource.Copied, e.Source);
            Assert.True(e.Required);
            Assert.Equal("full", e.Trust);
            Assert.Equal("none", e.OverridePolicy);
        });

        var webRoot = Path.Combine(repoRoot, "ConditioningControlPanel", "Resources", "web");
        var upstreamFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in new[] { "tunnel", "vendor" })
        {
            var treeRoot = Path.Combine(webRoot, tree);
            foreach (var file in Directory.EnumerateFiles(treeRoot, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(webRoot, file).Replace(Path.DirectorySeparatorChar, '/');
                upstreamFiles.Add(rel);
                var entry = tunnelEntries.SingleOrDefault(e =>
                    string.Equals(e.Path, "payload/" + rel, StringComparison.Ordinal));
                Assert.True(entry is not null, $"upstream file '{rel}' has no manifest entry");
                Assert.StartsWith("tunnel.", entry!.Id, StringComparison.Ordinal);
            }
        }

        foreach (var e in tunnelEntries)
        {
            Assert.Contains(e.Path["payload/".Length..], upstreamFiles);
        }
    }
}
