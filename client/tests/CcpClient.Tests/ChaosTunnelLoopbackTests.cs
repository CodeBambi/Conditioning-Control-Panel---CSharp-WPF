using System.Net;
using CcpClient.Desktop.Features.Chaos;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Manifest;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The tunnel loopback origin's §4-discipline contract — GET-only (405), both
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
    public async Task RouteClassLine_IsAlreadyInTheSink_WhenTheResponseBecomesObservable()
    {
        // The behavioural half. The historical red above was a MISSING route class:
        // the server appended the line only AFTER writing the body, so a client could observe
        // a whole response before the sink mentioned it. Under a saturated pool that gap is
        // where the flake lived (reproduced: 1 red in 382 natural round trips, failing on
        // Assert.Contains("/vendor/", ...) — the SECOND request's class).
        //
        // This fact removes the scheduling luck instead of tolerating it. The body is far
        // larger than any send buffer, so the server's write genuinely cannot complete until
        // the client drains it — and this client deliberately never drains: it stops at the
        // headers. Fixed tree: the line is appended before the first write, so it is in the
        // sink the moment headers are observable (deterministic green). Unfixed tree: headers
        // flush on that first write and the line waits on a body nobody is reading
        // (deterministic red, measured at 1, 2, 4 and 8 MB alike). No wait decides either
        // outcome; needing one would be the tell that the ordering was tolerated, not fixed.
        var big = Path.Combine(_root, "tunnel", "big.js"); // allowlisted extension -> 200
        await File.WriteAllBytesAsync(big, new byte[4 * 1024 * 1024], TestContext.Current.CancellationToken);

        using var client = new HttpClient();
        using var response = await client.GetAsync(
            _server.Origin + "/tunnel/big.js",
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);
        var logs = _log.All;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/tunnel/ -> 200", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("big.js", logs, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryResponseEmittingPath_RecordsItsRouteClassBeforeItWrites()
    {
        // The structural half, and the honest reason it exists: the behavioural fact
        // above can only pin a path whose body is big enough to block a write, and /health is
        // a two-byte "ok" that never can be. The only alternative for /health is a product
        // seam the packet forbids, so this guard carries the invariant across every
        // response-emitting path — including the one no round trip can reach — and reports
        // file:line. It is LEXICAL and therefore strictly weaker than the fact above.
        var source = ServerSourcePath();
        var lines = File.ReadAllLines(source);

        var health = Ordering(lines, source, "/health",
            "chaos-tunnel-loopback: GET /health -> 200",
            "await WriteText(ctx, 200, \"ok\"");
        var payload = Ordering(lines, source, "payload 200",
            "chaos-tunnel-loopback: GET {RouteClass(path)} -> 200",
            "await res.OutputStream.WriteAsync(bytes");
        var refusals = Ordering(lines, source, "refusals",
            "chaos-tunnel-loopback: {ctx.Request.HttpMethod} {routeClass} -> {code}",
            "await WriteText(ctx, code, msg");

        Assert.True(health.LogLine < health.WriteLine, health.Failure);
        Assert.True(payload.LogLine < payload.WriteLine, payload.Failure);
        Assert.True(refusals.LogLine < refusals.WriteLine, refusals.Failure);

        // Completeness, because three hard-coded anchors are blind to the likelier regression:
        // a FOURTH response-emitting path added later that logs after it writes would pass the
        // checks above simply by never being looked at. So the SET of response writes is pinned
        // too — every site that puts bytes on a response must be one of the three paths checked
        // above or the shared text writer they both delegate to.
        var writerDeclaration = SoleMatch(
            lines, "private static async Task WriteText(", source, "shared writer", "declaration");
        var writerBody = SoleMatch(
            lines, "await ctx.Response.OutputStream.WriteAsync(bytes)", source, "shared writer", "response write");
        int[] pinned = [health.WriteLine, payload.WriteLine, refusals.WriteLine, writerBody];

        var unpinned = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = i + 1;
            if (line == writerDeclaration || pinned.Contains(line))
            {
                continue;
            }

            if (lines[i].Contains("WriteText(", StringComparison.Ordinal) ||
                lines[i].Contains("OutputStream.WriteAsync(", StringComparison.Ordinal))
            {
                unpinned.Add($"{source}:{line}: {lines[i].Trim()}");
            }
        }

        Assert.True(unpinned.Count == 0,
            "a response is emitted by a path this guard does not check, so its route-class ordering "
            + "is unpinned. Every response-emitting path must record its route class before it writes "
            + "— anchor the new path here rather than leaving it unchecked:"
            + Environment.NewLine + string.Join(Environment.NewLine, unpinned));
    }

    [Fact]
    public void ThePrivacySink_NeverReceivesAnExceptionMessage()
    {
        // The handler-fault line interpolated ex.Message verbatim into the SAME
        // sink the route-class discipline governs — a sink forbidden even a bare filename.
        // Verified rather than recalled: UnauthorizedAccessException is NOT an IOException
        // (chain: UnauthorizedAccessException -> SystemException -> Exception), so the
        // client-went-away filter does not catch it, and its message is "Access to the path
        // '<full path>' is denied."; File.ReadAllBytesAsync on an ACL-denied payload file
        // reaches it. Narrowing to the type-only shape the accept-fault line already uses
        // deletes that vector, and nothing outside the server parses these strings.
        //
        // This pin is LEXICAL, and that is the honest bound: driving a non-filtered fault
        // through a real round trip needs either a product seam (forbidden here) or an
        // ACL fixture (platform-gated, which this fact must not be).
        var source = ServerSourcePath();
        var lines = File.ReadAllLines(source);

        // Each call is read to its closing parenthesis however many lines it spans, so an
        // interpolation broken across lines cannot slip past a single-line scan.
        var sinkCalls = SinkCalls(lines, source);
        var offenders = sinkCalls
            .Where(call => call.Text.Contains(".Message", StringComparison.Ordinal))
            .Select(call => $"{source}:{call.Line}: {call.Text}")
            .ToList();

        Assert.True(sinkCalls.Count >= 4,
            $"{source} — only {sinkCalls.Count} _log( call sites found; this guard anchors on them, so "
            + "it refuses to pass vacuously if the sink is renamed or the calls move.");
        Assert.True(offenders.Count == 0,
            "an exception message is interpolated into the route-class privacy sink; exception "
            + "messages carry filesystem paths, so log the TYPE only, as the accept-fault line "
            + "does:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static string ServerSourcePath() => Path.Combine(
        FindRepoRoot(), "client", "src", "CcpClient.Desktop", "Features", "Chaos", "ChaosTunnelLoopback.cs");

    /// <summary>Locate one response-emitting path's route-class record and its response write.
    /// An anchor that matches zero lines, or more than one, fails here rather than quietly
    /// making the ordering comparison vacuous.</summary>
    private static (int LogLine, int WriteLine, string Failure) Ordering(
        string[] lines, string source, string path, string logAnchor, string writeAnchor)
    {
        var logLine = SoleMatch(lines, logAnchor, source, path, "route-class record");
        var writeLine = SoleMatch(lines, writeAnchor, source, path, "response write");
        return (logLine, writeLine,
            $"{source}:{writeLine} — the {path} path writes its response at :{writeLine} but only "
            + $"records the route class at :{logLine}. Every route-class line must be emitted "
            + "before any byte of the corresponding response can leave the process; "
            + "Refuse is the reference shape.");
    }

    /// <summary>1-based line of the single line containing <paramref name="anchor"/>.</summary>
    private static int SoleMatch(string[] lines, string anchor, string source, string path, string what)
    {
        var hits = new List<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(anchor, StringComparison.Ordinal))
            {
                hits.Add(i + 1);
            }
        }

        Assert.True(hits.Count == 1,
            $"{source} — the {path} path's {what} anchor \"{anchor}\" matched {hits.Count} lines "
            + "(expected exactly 1). Re-anchor the guard on the moved code rather than relaxing it; "
            + "this guard refuses to skip.");
        return hits[0];
    }

    /// <summary>Every `_log(...)` call in the server source as (first line, whole-call text).
    /// The call is read to the parenthesis that closes it, across as many lines as it spans,
    /// so a multi-line interpolation is not invisible to a per-line scan. Parentheses inside
    /// string literals are not counted, and a call that does not close inside the span cap
    /// fails loud rather than being scanned half-read.</summary>
    private static List<(int Line, string Text)> SinkCalls(string[] lines, string source)
    {
        const string open = "_log(";
        const int spanCap = 40;

        var calls = new List<(int Line, string Text)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var at = lines[i].IndexOf(open, StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }

            var parts = new List<string>();
            var depth = 0;
            var closedAt = -1;
            for (var j = i; j < lines.Length && j - i < spanCap && closedAt < 0; j++)
            {
                var line = lines[j];
                var from = j == i ? at + open.Length - 1 : 0; // the '(' of _log(
                var inString = false;
                for (var k = from; k < line.Length; k++)
                {
                    var c = line[k];
                    if (inString)
                    {
                        if (c == '\\') k++;
                        else if (c == '"') inString = false;
                        continue;
                    }

                    if (c == '"') inString = true;
                    else if (c == '(') depth++;
                    else if (c == ')' && --depth == 0) { closedAt = j; break; }
                }

                parts.Add(line[from..].Trim());
            }

            Assert.True(closedAt >= 0,
                $"{source}:{i + 1} — this _log( call does not close within {spanCap} lines, so the "
                + "guard cannot read the whole call. It fails here rather than scanning part of one.");
            calls.Add((i + 1, string.Concat(parts)));
            i = closedAt;
        }

        return calls;
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
            longPollTimeout: TestWait.InjectedBudget); // no poll is issued here; the §4 invariants must not turn on a budget
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
        // --verify-assets' copied sweep — the manifest's two-direction rule.)
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
