using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CcpClient.Desktop.Features.Goon;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-132 — RUNG 1: <b>the origin SERVES</b>.
///
/// <para>SP-130 proved the payload SHIPS: 184 files land in the build output, byte-identical to the
/// upstream tree. It never asked the origin for one of them. Its own record names the gap
/// (§7.4 item 2, "No test issues an HTTP request against the running goon <c>LoopbackServer</c>"),
/// and the board row filed at the wave-65 land repeats it. <b>These facts close it: a real socket,
/// a real GET at the bound origin, and an assertion on the BYTES that come back.</b></para>
///
/// <para><b>The rung this reaches, and the two above it.</b> Serving bytes is not loading a page and
/// loading a page is not playing a duel. Nothing in this file involves a browser, a monitor or the
/// real-desktop lease, and nothing here is evidence that the page BOOTED — that claim needs a
/// headed run and lives in <c>spine-tasks/SP-132-goon-evidence/record.md</c>, classed separately.
/// A duel is a HUMAN gate and is claimed nowhere.</para>
///
/// <para><b>Why the assertions are on content and not on status codes.</b> A 200 proves a route
/// answered; it does not prove the route answered with the file. Every retrieval below is compared
/// to the bytes on disk, or to a magic number, or to a string the file really contains.</para>
/// </summary>
public sealed class GoonServingTests : IDisposable
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];

    /// <summary>The four files the reused §4 server refuses by extension (D258). Named here as
    /// the EXPECTED set; the test derives the observed set from the wire and compares.</summary>
    private static readonly string[] DeniedByExtension =
    [
        "manifest.webmanifest",
        "vendor/fflate/LICENSE",
        "vendor/mp4-muxer/LICENSE",
        "vendor/mp4-muxer/mp4-muxer.mjs",
    ];

    private readonly string _dataDirectory;
    private readonly GoonParticipant _participant;
    private readonly HttpClient _client = new();

    public GoonServingTests()
    {
        // An explicit data directory: never CompositionRoot.DefaultSettingsPath(), so this fixture
        // cannot touch the real profile (SP-057 discipline) — the shared user-media root the
        // participant derives from it is <dataDir>/assets and is empty here by construction.
        _dataDirectory = Path.Combine(Path.GetTempPath(), "ccp-sp132-goon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDirectory);
        _participant = new GoonParticipant(new SilentLog(), _dataDirectory);
        var probe = _participant.Start();
        Assert.Equal(GoonServingRoots.GoonPayloadState.Present, probe.State);
        // SP-059 T-15: both origins registered while bound, unregistered only after a clean dispose.
        LoopbackListenerRegistry.RegisterLoopbackServer(nameof(GoonServingTests), _participant.Server);
    }

    public void Dispose()
    {
        var server = _participant.Server;
        try { _participant.Dispose(); LoopbackListenerRegistry.UnregisterLoopbackServer(server); } catch { /* best-effort */ }
        _client.Dispose();
        try { Directory.Delete(_dataDirectory, recursive: true); } catch { /* best-effort */ }
    }

    private sealed class SilentLog : ILogSink
    {
        public void Log(string message) { }
    }

    // ==================================================================================
    // helpers — every request goes through the real socket at the participant's own origin
    // ==================================================================================

    private static string PayloadRoot => GoonServingRoots.PayloadRoot;

    private string Origin => _participant.Server.PageOrigin;

    private async Task<(int Status, byte[] Bytes, MediaTypeHeaderValue? ContentType, string? Nosniff)> GetUrl(string url)
    {
        using var response = await _client.GetAsync(url, TestContext.Current.CancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        response.Headers.TryGetValues("X-Content-Type-Options", out var nosniff);
        return ((int)response.StatusCode, bytes, response.Content.Headers.ContentType, nosniff?.FirstOrDefault());
    }

    private Task<(int Status, byte[] Bytes, MediaTypeHeaderValue? ContentType, string? Nosniff)> Get(string path) =>
        GetUrl(Origin + path);

    /// <summary>The route the page uses for a payload-relative path (the reused server's hardcoded
    /// <c>/dtrh/</c> class — not a DTRH page: overlay-first resolves it against the goon tree).</summary>
    private static string Route(string relative) => "/dtrh/" + relative.Replace('\\', '/');

    private static byte[] OnDisk(string relative) =>
        File.ReadAllBytes(Path.Combine(PayloadRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    /// <summary>Every file in the served tree, as forward-slashed relative paths.</summary>
    private static string[] ServedRelatives() =>
        [.. Directory.EnumerateFiles(PayloadRoot, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(PayloadRoot, f).Replace('\\', '/'))
            .OrderBy(r => r, StringComparer.Ordinal)];

    private async Task<byte[]> BytesOf(string path) => (await Get(path)).Bytes;

    /// <summary>Which of <paramref name="relatives"/> are NOT under <paramref name="root"/> —
    /// returned as a set so a failing fact NAMES the files rather than reporting a bare false.</summary>
    private static string[] MissingFrom(string root, IEnumerable<string> relatives) =>
        [.. relatives.Where(r => !File.Exists(Path.Combine(root, r.Replace('/', Path.DirectorySeparatorChar))))];

    /// <summary>Which of <paramref name="relatives"/> ARE under <paramref name="root"/>.</summary>
    private static string[] PresentIn(string root, IEnumerable<string> relatives) =>
        [.. relatives.Where(r => File.Exists(Path.Combine(root, r.Replace('/', Path.DirectorySeparatorChar))))];

    // ==================================================================================
    // 1. THE DOCUMENT
    // ==================================================================================

    /// <summary>
    /// The page document itself, retrieved at the origin and compared BYTE FOR BYTE with the file
    /// the glob copied. The content assertion on top of that is the one line that makes the
    /// document a boot path rather than a blob: <c>index.html:96</c>'s module script tag.
    /// </summary>
    [Fact]
    public async Task TheDocument_IsRetrievedAtTheOrigin_WithItsRealBytes()
    {
        var (status, bytes, contentType, nosniff) = await Get(Route("index.html"));

        Assert.Equal(200, status);
        Assert.Equal(OnDisk("index.html"), bytes);
        Assert.Equal("text/html", contentType?.MediaType);
        Assert.Equal("utf-8", contentType?.CharSet);
        Assert.Equal("nosniff", nosniff);
        // Content, not length: this is the tag that makes boot.js run at all.
        Assert.Contains("<script type=\"module\" src=\"./boot.js\"></script>", Text(bytes), StringComparison.Ordinal);
    }

    /// <summary>
    /// All three files <see cref="GoonServingRoots.RequiredFiles"/> calls the boot path, each
    /// byte-equal at the wire. <c>boot.js</c> is ~130 KB, so this also proves the response BODY
    /// really streams through <c>ServeFile</c>'s 64 KB loop rather than a header answering alone.
    /// </summary>
    [Fact]
    public async Task EveryRequiredBootFile_IsRetrievable_ByteForByte()
    {
        foreach (var required in GoonServingRoots.RequiredFiles)
        {
            var (status, bytes, contentType, _) = await Get(Route(required));

            Assert.Equal(200, status);
            Assert.Equal(OnDisk(required), bytes);
            Assert.Equal(
                required.EndsWith(".html", StringComparison.Ordinal) ? "text/html" : "text/javascript",
                contentType?.MediaType);
        }

        // The streaming half, stated as a number so a truncating change is loud: boot.js is the
        // largest file on the boot path and it arrives whole.
        var boot = await Get(Route("boot.js"));
        Assert.Equal(200, boot.Status);
        Assert.True(boot.Bytes.Length > 100_000, $"boot.js came back {boot.Bytes.Length} bytes — the body did not stream");
        Assert.Equal(OnDisk("boot.js").Length, boot.Bytes.Length);
        // bridge.js is boot.js's transport import — no bridge, no handshake (bridge.js:106).
        Assert.Contains("export function announceReady()", Text(await BytesOf(Route("bridge.js"))), StringComparison.Ordinal);
    }

    // ==================================================================================
    // 2. ASSETS THAT ARE NOT THE DOCUMENT
    // ==================================================================================

    /// <summary>
    /// The packet's explicit requirement — at least one asset that is not <c>index.html</c>. Four,
    /// spanning three MIME rows of the §4 allowlist, each asserted on CONTENT: the two stylesheets
    /// <c>index.html</c> links, the title screen's logo (checked by its PNG magic number, so a
    /// zero-length or HTML-error body cannot pass), and a JSON file.
    /// </summary>
    [Fact]
    public async Task AssetsBeyondTheDocument_AreServed_WithTheRightTypeAndTheRightBytes()
    {
        var css = await Get(Route("goon.css"));
        Assert.Equal(200, css.Status);
        Assert.Equal("text/css", css.ContentType?.MediaType);
        Assert.Equal(OnDisk("goon.css"), css.Bytes);
        // goon.css:56-58 — the body gradient the headed capture photographs.
        Assert.Contains("radial-gradient(120% 120% at 50% 0%, #2a1030 0%", Text(css.Bytes), StringComparison.Ordinal);

        var screens = await Get(Route("ui/screens.css"));
        Assert.Equal(200, screens.Status);
        Assert.Equal("text/css", screens.ContentType?.MediaType);
        Assert.Equal(OnDisk("ui/screens.css"), screens.Bytes);

        // The title screen's brand image (ui/screens/title.js:31). A magic-number assertion: an
        // error page or an empty body would pass a length check and fails this one.
        var logo = await Get(Route("assets/goon_game_logo.png"));
        Assert.Equal(200, logo.Status);
        Assert.Equal("image/png", logo.ContentType?.MediaType);
        Assert.Equal(OnDisk("assets/goon_game_logo.png"), logo.Bytes);
        Assert.True(logo.Bytes.Length > 8, "the logo came back with no body");
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, logo.Bytes.Take(8).ToArray());

        var json = await Get(Route("package.json"));
        Assert.Equal(200, json.Status);
        Assert.Equal("application/json", json.ContentType?.MediaType);
        Assert.Equal(OnDisk("package.json"), json.Bytes);
    }

    /// <summary>
    /// Range 206/416 on the goon origin. The stated reason the §4 class was reused UNMODIFIED
    /// rather than re-derived is that the page plays the user's own VIDEOS and this class already
    /// ships Range; that reasoning is worth something only if Range really works at THIS origin.
    /// </summary>
    [Fact]
    public async Task RangeRequests_AreHonoured_OnTheGoonOrigin()
    {
        var whole = OnDisk("goon.css");

        using var partial = new HttpRequestMessage(HttpMethod.Get, Origin + Route("goon.css"));
        partial.Headers.Range = new RangeHeaderValue(0, 63);
        using var partialResponse = await _client.SendAsync(partial, TestContext.Current.CancellationToken);
        var partialBytes = await partialResponse.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(206, (int)partialResponse.StatusCode);
        Assert.Equal(whole.Take(64).ToArray(), partialBytes);

        using var unsatisfiable = new HttpRequestMessage(HttpMethod.Get, Origin + Route("goon.css"));
        unsatisfiable.Headers.Range = new RangeHeaderValue(whole.Length + 4096, null);
        using var unsatisfiableResponse = await _client.SendAsync(unsatisfiable, TestContext.Current.CancellationToken);
        Assert.Equal(416, (int)unsatisfiableResponse.StatusCode);
    }

    // ==================================================================================
    // 3. THE URL THE HOST REALLY NAVIGATES TO
    // ==================================================================================

    /// <summary>
    /// <b>Not a reconstructed path — the exact string <see cref="GoonParticipant.PageUrl"/> hands
    /// to the WebView.</b> Bridge-token query and all. A route prefix that stopped resolving, or a
    /// query the server choked on, would leave the headed host navigating to a 404, and this is
    /// the only fact that would notice without a browser.
    /// </summary>
    [Fact]
    public async Task ThePageUrlTheHostNavigatesTo_Serves()
    {
        var url = _participant.PageUrl();
        Assert.StartsWith(Origin + "/dtrh/index.html?bridge=", url, StringComparison.Ordinal);

        var (status, bytes, contentType, _) = await GetUrl(url);
        Assert.Equal(200, status);
        Assert.Equal("text/html", contentType?.MediaType);
        Assert.Equal(OnDisk("index.html"), bytes);
    }

    // ==================================================================================
    // 4. THE REFUSAL — the files the server DENIES, observed at the wire
    // ==================================================================================

    /// <summary>
    /// D258's four denied files, refused at the wire — and each asserted to EXIST on disk first,
    /// so a 415 is a refusal by the MIME allowlist (<c>LoopbackServer.cs:446-450</c>) and not a
    /// 404 in disguise. The refusal body is asserted too: the server names the rule that refused.
    /// </summary>
    [Fact]
    public async Task TheFourDeniedFiles_Answer415_AndTheyReallyExist()
    {
        // Top-level, before the loop: the set is the D258 four, and every one of them is really in
        // the served tree — so a 415 below is the MIME allowlist refusing a file that is there, and
        // not a 404 wearing a different number.
        Assert.Equal(4, DeniedByExtension.Length);
        Assert.Empty(MissingFrom(PayloadRoot, DeniedByExtension));

        foreach (var denied in DeniedByExtension)
        {
            var (status, bytes, _, nosniff) = await Get(Route(denied));
            Assert.Equal(415, status);
            Assert.Equal("refused: extension outside the pinned allowlist", Text(bytes));
            Assert.Equal("nosniff", nosniff);
        }
    }

    /// <summary>
    /// <b>THE ALLOWLIST, DERIVED FROM BEHAVIOUR RATHER THAN RESTATED.</b> The board row filed at
    /// the wave-65 land names the defect: <c>GoonPracticeTests</c> duplicates the server's nine
    /// extensions as a test literal, so a change on the SERVER side would not red it. This fact
    /// restates nothing. It sweeps the served tree, groups it by extension class, asks the running
    /// server for ONE representative of each, and derives the refused set from the status codes
    /// that come back. Add an extension to <c>LoopbackServer</c>'s table, or a new file type
    /// upstream, and the observed partition moves — so this reds.
    ///
    /// <para>Note the shape of the derivation: the refused set is a set of EXTENSION CLASSES —
    /// three of them, <c>.webmanifest</c>, <c>.mjs</c> and the EMPTY STRING — and the empty string
    /// covers TWO files, which is why the comparison to the four names is made over files and not
    /// over classes.</para>
    /// </summary>
    [Fact]
    public async Task TheServedTrees415Set_IsDerivedFromTheWire_NotRestated()
    {
        var relatives = ServedRelatives();
        Assert.Equal(GoonServingRoots.FileCountAtBaseline, relatives.Length);

        var byExtension = relatives
            .GroupBy(r => Path.GetExtension(r), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.True(byExtension.Length >= 4, $"only {byExtension.Length} extension classes in the served tree");

        var refusedClasses = new List<string>();
        var servedClasses = new List<string>();
        foreach (var group in byExtension)
        {
            var representative = group.OrderBy(r => r, StringComparer.Ordinal).First();
            var (status, bytes, _, _) = await Get(Route(representative));
            switch (status)
            {
                case 200:
                    // A served class is served WITH ITS BYTES — otherwise "served" would mean
                    // "answered", and a 200 with an empty body would pass.
                    Assert.Equal(OnDisk(representative), bytes);
                    servedClasses.Add(group.Key);
                    break;
                case 415:
                    Assert.Equal("refused: extension outside the pinned allowlist", Text(bytes));
                    refusedClasses.Add(group.Key);
                    break;
                default:
                    Assert.Fail($"{representative} answered {status} — neither served nor refused by the allowlist");
                    break;
            }
        }

        // The three refused CLASSES, observed at the wire.
        Assert.Equal(
            new[] { "", ".mjs", ".webmanifest" },
            refusedClasses.Order(StringComparer.Ordinal).ToArray());
        // The six served ones. The goon tree carries no .gif/.webm/.webp, so six of the server's
        // nine allowlist rows are exercised through this tree and three are unreachable by it —
        // stated because "the allowlist works" would be a wider claim than this evidence supports.
        Assert.Equal(
            new[] { ".css", ".html", ".js", ".json", ".mp3", ".png" },
            servedClasses.Order(StringComparer.Ordinal).ToArray());

        // And the FILES those refused classes cover are exactly the four D258 names.
        var refusedSet = refusedClasses.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            DeniedByExtension.Order(StringComparer.Ordinal).ToArray(),
            relatives.Where(r => refusedSet.Contains(Path.GetExtension(r))).Order(StringComparer.Ordinal).ToArray());
    }

    // ==================================================================================
    // 4b. THE BORROW — the twelve assets the page hotlinks out of the DTRH tree
    // ==================================================================================

    /// <summary>
    /// The six audio cues the page hotlinks by ABSOLUTE path. <c>ui/audio.js:112</c> declares
    /// <c>DTRH_SFX_DIR = '/dtrh/assets/bubbles/sfx/'</c> and the registry resolves through it at
    /// <c>:203</c>, <c>:219</c>, <c>:221</c>, <c>:225</c>, <c>:230</c> and <c>:235</c>.
    /// </summary>
    private static readonly string[] BorrowedAudio =
        ["Pop.mp3", "Pop2.mp3", "Pop3.mp3", "chime1.mp3", "chime2.mp3", "GG.mp3"];

    /// <summary>The bubble sprite (<c>exec/fx.css:271</c>) and the five effect icons
    /// (<c>:304-308</c>). <c>golden.png</c> and <c>prism.png</c> are in the DTRH tree but are NOT
    /// referenced by this page, so they are not in the borrowed set.</summary>
    private static readonly string[] BorrowedImages =
    [
        "assets/bubbles/bubble.png",
        "assets/bubbles/effects/flash.png",
        "assets/bubbles/effects/spiral.png",
        "assets/bubbles/effects/glitch.png",
        "assets/bubbles/effects/braindrain.png",
        "assets/bubbles/effects/pinkfilter.png",
    ];

    /// <summary>
    /// <b>THE BORROW, proved at the wire — and it exists because a real page load found it broken
    /// (D266).</b> SP-130 served the goon tree as BOTH roots so that nothing else was reachable on
    /// this origin. The first run that ever loaded the page found the cost: the page hotlinks
    /// twelve assets out of the DTRH tree by absolute <c>/dtrh/</c> path, which upstream is a
    /// sibling folder under the same WebView2 virtual host, and all twelve answered 404. The page
    /// reported it itself — <c>warn: [GG audio] asset failed: …Pop2.mp3 (HTTP 404)</c>.
    ///
    /// <para>Every one is asserted with BYTES, and each is checked to be absent from the goon tree
    /// first — otherwise a 200 would prove nothing about the fallback, only that something
    /// answered.</para>
    /// </summary>
    [Fact]
    public async Task TheTwelveHotlinkedAssets_FallThroughToTheDtrhTree()
    {
        var borrowed = BorrowedAudio.Select(f => "assets/bubbles/sfx/" + f).Concat(BorrowedImages).ToArray();

        // Top-level, before the loop, and the two halves that make the loop mean anything:
        // NONE of the twelve is in the goon tree (so a 200 cannot come through the overlay), and
        // EVERY one is in the dtrh tree (so the borrow has a source to fall through to).
        Assert.Equal(12, borrowed.Length);
        Assert.Empty(PresentIn(PayloadRoot, borrowed));
        Assert.Empty(MissingFrom(Desktop.Features.Dtrh.DtrhParticipant.PayloadRoot, borrowed));

        foreach (var relative in borrowed)
        {
            var fallbackSource = Path.Combine(
                Desktop.Features.Dtrh.DtrhParticipant.PayloadRoot,
                relative.Replace('/', Path.DirectorySeparatorChar));

            var (status, bytes, contentType, _) = await Get(Route(relative));
            Assert.Equal(200, status);
            Assert.Equal(File.ReadAllBytes(fallbackSource), bytes);
            Assert.Equal(
                relative.EndsWith(".mp3", StringComparison.Ordinal) ? "audio/mpeg" : "image/png",
                contentType?.MediaType);
        }
    }

    /// <summary>
    /// The OTHER half of overlay-first, and the half a borrow can silently break: the goon tree
    /// still SHADOWS. Both trees carry <c>index.html</c>, <c>boot.js</c> and <c>bridge.js</c> at
    /// the same route path, and if the fallback ever won, the goon origin would serve the DTRH
    /// game — a far worse failure than a missing pop sound, and one that would still answer 200.
    /// </summary>
    [Fact]
    public async Task TheGoonTreeStillShadows_TheBorrowedFallback()
    {
        var collisions = PresentIn(Desktop.Features.Dtrh.DtrhParticipant.PayloadRoot, ServedRelatives());

        // Non-vacuity: if the two trees shared no path at all, "the overlay wins" would be untested.
        Assert.Contains("index.html", collisions);
        Assert.True(collisions.Length >= 3, $"only {collisions.Length} colliding path(s) between the two trees");

        foreach (var relative in collisions)
        {
            var (status, bytes, _, _) = await Get(Route(relative));
            Assert.Equal(200, status);
            Assert.Equal(OnDisk(relative), bytes);
        }

        // Named directly, because it is the one that would be catastrophic rather than merely wrong.
        var index = await Get(Route("index.html"));
        Assert.Contains("<title>Goon Game</title>", Text(index.Bytes), StringComparison.Ordinal);
    }

    // ==================================================================================
    // 5. §4 DISCIPLINE ON THIS ORIGIN — it is a THIRD server, separately constructed
    // ==================================================================================

    /// <summary>
    /// The goon origin is its own <see cref="Desktop.Features.Dtrh.LoopbackServer"/> instance with
    /// its own ports and its own token (<see cref="GoonParticipant"/>). DTRH's and intake's
    /// contract facts say nothing about it, so the §4 refusals are re-asserted HERE.
    /// </summary>
    [Fact]
    public async Task Section4_Discipline_HoldsOnTheGoonOrigin()
    {
        // GET-only.
        using (var post = await _client.PostAsync(
            Origin + Route("index.html"), new StringContent("x"), TestContext.Current.CancellationToken))
        {
            Assert.Equal(405, (int)post.StatusCode);
        }

        // Traversal refusal — the escaped form the resolver has to catch.
        Assert.Equal(403, (await Get("/dtrh/..%2Fsecret.txt")).Status);
        // Absent path inside the route class.
        Assert.Equal(404, (await Get(Route("no/such/module.js"))).Status);
        // Outside the exposed route class entirely. There is no /goon/ route: the reused server's
        // prefix is /dtrh/, and that is the ported fact rather than a typo.
        Assert.Equal(404, (await Get("/goon/index.html")).Status);
        // Health lives.
        var health = await Get("/health");
        Assert.Equal(200, health.Status);
        Assert.Equal("ok", Text(health.Bytes));
    }

    /// <summary>
    /// The §3.3 bridge inbox is present on this origin because the §4 class requires one, and the
    /// goon page never uses it (its bridge speaks <c>window.chrome.webview</c> only —
    /// <c>bridge.js:45-47</c>). "Unused" and "unguarded" are different facts: the route is
    /// token-guarded, and a wrong token is refused with a FIXED route class so the token that was
    /// presented can never reach a log (§4.8).
    /// </summary>
    [Fact]
    public async Task TheBridgeInbox_IsTokenGuarded_AndServesJson()
    {
        var wrong = await Get("/bridge/not-the-token/inbox?after=0");
        Assert.Equal(404, wrong.Status);
        Assert.Equal("refused: no such route", Text(wrong.Bytes));

        // Enqueue FIRST: an empty inbox is a long poll by contract, and a test that polled an
        // empty one would be waiting on a clock instead of asserting a route.
        _participant.Inbox.Enqueue("{\"type\":\"sp132-probe\"}");

        var ok = await Get($"/bridge/{_participant.BridgeToken}/inbox?after=0");
        Assert.Equal(200, ok.Status);
        Assert.Equal("application/json", ok.ContentType?.MediaType);
        using var document = JsonDocument.Parse(ok.Bytes);
        var messages = document.RootElement.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("sp132-probe", messages[0].GetProperty("body").GetProperty("type").GetString());

        // The route requires ?after=N — a missing cursor is a typed 400, never a silent replay.
        Assert.Equal(400, (await Get($"/bridge/{_participant.BridgeToken}/inbox")).Status);
    }

    /// <summary>
    /// The user-media origin the <c>manifest</c> frame's URLs point into is bound on this
    /// participant too, and it is a SECOND port. The pool is empty here by construction (a fresh
    /// data directory), so what this pins is that the origin exists and refuses an absent name
    /// rather than faulting — <c>GoonHostWindow</c> builds the manifest against
    /// <c>Server.MediaOrigin</c>, and an unbound one would fail at the handshake.
    /// </summary>
    [Fact]
    public async Task TheUserMediaOrigin_IsBound_AndRefusesAnAbsentName()
    {
        Assert.NotEqual(_participant.Server.PagePort, _participant.Server.MediaPort);
        Assert.StartsWith("http://127.0.0.1:", _participant.Server.MediaOrigin, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(_dataDirectory, "assets"), _participant.UserMediaRoot);

        var (status, _, _, _) = await GetUrl(_participant.Server.MediaOrigin + "/umedia/nothing-here.png");
        Assert.Equal(404, status);
    }

    // ==================================================================================
    // 6. THE HEADED HARNESS — the things that rot silently between headed runs
    // ==================================================================================
    //
    // These are NOT the headed evidence. The evidence is a real capture of the running host on a
    // real Windows desktop, checked by CcpVerify, with the wrong-state demonstrations recorded in
    // spine-tasks/SP-132-goon-evidence/record.md. A headless assembly cannot photograph anything
    // and no fact here claims to. What these pin is the shape SP-122 established: a check that
    // cannot be captured, a check demoted out of its evidence class, a tolerance widened past the
    // colour it exists to reject, or a pixel read that no longer waits for the state.

    private static string ManifestPath() =>
        Path.Combine(FindRepoRoot(), "client", "tools", "verify", "checks.json");

    private static string CaptureScript() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "client", "tools", "verify", "capture.ps1"));

    /// <summary>The capture script with comment lines removed — it EXPLAINS several of the things
    /// a naive search would then report as defects (the <c>RackPresentationTests</c> precedent).</summary>
    private static string CaptureScriptCode() =>
        string.Join('\n', CaptureScript().Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

    private static CcpVerify.ManifestCheck[] GoonChecks() =>
        [.. CcpVerify.CheckManifest.Load(ManifestPath())
            .Where(c => c.Surface.StartsWith("goon-", StringComparison.Ordinal))];

    /// <summary>
    /// The goon surface is on the manifest, and every check on it claims the class a headless frame
    /// can NEVER discharge. A goon check quietly demoted to <c>draw-verified</c> would let a
    /// headless run claim the page rendered, which is the exact substitution the evidence classes
    /// exist to stop — and it is the substitution this packet is most at risk of making.
    /// </summary>
    [Fact]
    public void EveryGoonCheck_ClaimsPresentationVerified()
    {
        var checks = GoonChecks();
        Assert.NotEmpty(checks);
        foreach (var check in checks)
        {
            Assert.Equal(CcpVerify.CheckManifest.EvidencePresentation, check.EvidenceClass);
        }
    }

    /// <summary>
    /// BOTH of <c>capture.ps1</c>'s ValidateSets are parsed, not just one. The <c>$statesFor</c>
    /// table is already guarded (<c>RackPresentationTests</c>), but the <c>-State</c> PARAMETER's
    /// own set is guarded by nothing: a state present in the table and absent from the parameter
    /// fails at PowerShell parameter binding, which is the worst place to fail — before the
    /// script's own refusals, with a message about a set rather than about a surface. Both sides
    /// are read from the script on disk; neither is restated here.
    /// </summary>
    [Fact]
    public void EveryGoonSurfaceAndState_IsOneTheCaptureScriptAcceptsAndCanDrive()
    {
        var script = CaptureScriptCode();

        var surfaceSet = System.Text.RegularExpressions.Regex.Match(
            script, @"ValidateSet\((?<set>[^)]*)\)\]\s*\[string\]\$Surface");
        Assert.True(surfaceSet.Success, "capture.ps1 no longer declares a ValidateSet for -Surface");
        var acceptedSurfaces = Names(surfaceSet.Groups["set"].Value);

        var stateSet = System.Text.RegularExpressions.Regex.Match(
            script, @"ValidateSet\((?<set>[^)]*)\)\]\s*\[string\]\$State");
        Assert.True(stateSet.Success, "capture.ps1 no longer declares a ValidateSet for -State");
        var acceptedStates = Names(stateSet.Groups["set"].Value);

        var start = script.IndexOf("$statesFor = @{", StringComparison.Ordinal);
        Assert.InRange(start, 0, script.Length);
        var end = script.IndexOf("\n}", start, StringComparison.Ordinal);
        Assert.InRange(end, start, script.Length);
        var drivable = new HashSet<string>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match row in System.Text.RegularExpressions.Regex.Matches(
            script[start..end], @"'(?<surface>[a-z-]+)'\s*=\s*@\((?<states>[^)]*)\)"))
        {
            foreach (System.Text.RegularExpressions.Match state in
                System.Text.RegularExpressions.Regex.Matches(row.Groups["states"].Value, "'(?<s>[a-z-]+)'"))
            {
                drivable.Add($"{row.Groups["surface"].Value}/{state.Groups["s"].Value}");
            }
        }

        var checks = GoonChecks();
        Assert.NotEmpty(checks);
        foreach (var check in checks)
        {
            Assert.True(acceptedSurfaces.Contains(check.Surface),
                $"checks.json names surface '{check.Surface}' but capture.ps1's -Surface accepts only "
                + $"[{string.Join(", ", acceptedSurfaces.Order(StringComparer.Ordinal))}]");
            Assert.True(acceptedStates.Contains(check.State),
                $"checks.json names state '{check.State}' but capture.ps1's -State accepts only "
                + $"[{string.Join(", ", acceptedStates.Order(StringComparer.Ordinal))}] — that run would fail at "
                + "parameter binding, before any refusal this script writes");
            Assert.Contains($"{check.Surface}/{check.State}", drivable);
        }
    }

    private static HashSet<string> Names(string validateSetBody) =>
        [.. System.Text.RegularExpressions.Regex.Matches(validateSetBody, "'(?<s>[^']+)'")
            .Select(m => m.Groups["s"].Value)];

    private static int PerChannel((byte R, byte G, byte B) a, (int R, int G, int B) b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    /// <summary>
    /// THE TOLERANCE RULE, MECHANICAL, part one: the boot LOADER. The captured rect has one other
    /// state this build can put there — a flat <c>#0d0716</c> fill (<c>goon.css:225-229</c>) that
    /// covers everything until <c>settle()</c> runs, which is what a page that loads but never
    /// completes the handshake looks like. If a goon check's tolerance ever reaches the per-channel
    /// distance to it, the check passes on a capture of a page that never booted: precisely the
    /// outcome this packet exists to prevent.
    /// </summary>
    [Fact]
    public void NoGoonCheckAcceptsTheColourOfTheLoaderItMustReject()
    {
        var checks = GoonChecks();
        Assert.NotEmpty(checks);

        foreach (var check in checks)
        {
            var expected = CcpVerify.CheckManifest.ParseColor(check.ExpectedColor, $"check '{check.Name}':");
            var separation = PerChannel(expected, (0x0D, 0x07, 0x16));
            Assert.True(check.Tolerance < separation,
                $"'{check.Name}' has tolerance {check.Tolerance} against #0D0716 (the boot loader's flat fill) at "
                + $"per-channel distance {separation} from its own {check.ExpectedColor} — at this tolerance a "
                + "capture of a page that NEVER BOOTED would pass the check that exists to say it did");
        }
    }

    /// <summary>
    /// THE TOLERANCE RULE, MECHANICAL, part two — <b>and this one is here because the rule above
    /// was not enough and a real capture proved it.</b>
    ///
    /// <para>The first draft pinned <c>goon-page-backdrop</c> at tolerance 8. It cleared the loader
    /// rule comfortably (distance 14) and it PASSED a real capture of the DASHBOARD at 1.0000,
    /// because <c>dashboard-background</c> is <c>#141018</c> and sits only 7 away per channel. A
    /// check that accepts a photograph of a different window is the "cannot fail on a wrong
    /// capture" defect, and it would have shipped.</para>
    ///
    /// <para>So the rule is now general and is derived from the manifest rather than from a list
    /// somebody remembered to update: <b>no goon check may accept the colour that any check of a
    /// DIFFERENT surface+state declares.</b> Same-state siblings are excluded deliberately — two
    /// regions of one screen may well be similar colours, and that is not a confusion between
    /// states. Adding any surface to this manifest automatically widens what this guard defends
    /// against.</para>
    /// </summary>
    [Fact]
    public void NoGoonCheckAcceptsTheColourOfAnotherDeclaredState()
    {
        var all = CcpVerify.CheckManifest.Load(ManifestPath());
        var goon = GoonChecks();
        Assert.NotEmpty(goon);

        var others = all
            .Where(c => !c.Surface.StartsWith("goon-", StringComparison.Ordinal))
            .ToArray();
        Assert.True(others.Length >= 3,
            $"only {others.Length} non-goon check(s) in the manifest — this guard would be nearly vacuous");

        foreach (var check in goon)
        {
            var expected = CcpVerify.CheckManifest.ParseColor(check.ExpectedColor, $"check '{check.Name}':");
            foreach (var other in others)
            {
                var otherColour = CcpVerify.CheckManifest.ParseColor(other.ExpectedColor, $"check '{other.Name}':");
                var separation = PerChannel(expected, (otherColour.R, otherColour.G, otherColour.B));
                Assert.True(check.Tolerance < separation,
                    $"'{check.Name}' ({check.ExpectedColor}, tolerance {check.Tolerance}) accepts "
                    + $"'{other.Name}' ({other.ExpectedColor}) — they are {separation} apart per channel, so a "
                    + $"capture of {other.Surface}/{other.State} would PASS a check that exists to say the goon "
                    + "page rendered. Measured, not hypothetical: this is the exact pair that shipped at "
                    + "tolerance 8 in this packet's first draft and scored 1.0000 on a real dashboard capture");
            }
        }
    }

    /// <summary>
    /// The state confirmation happens BEFORE the pixel read, in the script that actually runs, and
    /// the wait for it is a POLL TO A DEADLINE rather than a single read — a slow-but-healthy boot
    /// must not be indistinguishable from a boot that never completes. Index-order over the real
    /// source, the technique <c>HarnessEntryPointGuardTests</c> uses on <c>Program.cs</c>.
    ///
    /// <para><b>Honest limit, stated rather than implied.</b> This is LEXICAL. It proves the
    /// assertion has not been deleted or moved below the capture; it cannot prove the assertion
    /// still bites. What proves that is the wrong-state demonstrations in the packet record.</para>
    /// </summary>
    [Fact]
    public void TheGoonCapture_ConfirmsTheHandshakeBeforeItReadsAnyPixel()
    {
        var script = CaptureScriptCode();

        // 'GoonProbe' is the AutomationId the window publishes; needling THAT rather than the
        // probe line's own prefix binds the harness to the product element it reads, and the
        // prefix appears only in prose, which this comment-stripped view does not contain.
        var probeRead = script.IndexOf("'GoonProbe'", StringComparison.Ordinal);
        var surfaceAssert = script.IndexOf("surface=(?<s>", StringComparison.Ordinal);
        var navAssert = script.IndexOf("nav=failed", StringComparison.Ordinal);
        var readyAssert = script.IndexOf("ready=true", StringComparison.Ordinal);
        var screenAssert = script.IndexOf("screen=title", StringComparison.Ordinal);
        var modalAssert = script.IndexOf("modal=open", StringComparison.Ordinal);
        var pixelRead = script.IndexOf("CopyFromScreen", StringComparison.Ordinal);

        Assert.True(probeRead >= 0, "capture.ps1 no longer reads the goon window's GoonProbe element");
        Assert.True(surfaceAssert >= 0, "capture.ps1 no longer checks which surface the host selected");
        Assert.True(navAssert >= 0, "capture.ps1 no longer refuses on a FAILED navigation — it would photograph an error page");
        Assert.True(readyAssert >= 0, "capture.ps1 no longer asserts the page reported ready=true");
        Assert.True(screenAssert >= 0, "capture.ps1 no longer asserts the page reached screen=title");
        Assert.True(modalAssert >= 0, "capture.ps1 no longer asserts the captured first-run card is actually open");
        Assert.True(pixelRead >= 0, "capture.ps1 no longer reads the screen at all");

        var confirmations = new[]
        {
            ("GoonProbe read", probeRead), ("surface", surfaceAssert), ("nav", navAssert),
            ("ready", readyAssert), ("screen", screenAssert), ("modal", modalAssert),
        };
        foreach (var (name, at) in confirmations)
        {
            Assert.True(at < pixelRead,
                $"the goon '{name}' confirmation sits AFTER CopyFromScreen (at {at} vs {pixelRead}) — a pixel read "
                + "that happens first can photograph a loader, an error page or the wallpaper and report it as "
                + "the page having rendered");
        }

        // The wait is bounded and POLLED. A single read cannot tell a slow-but-healthy boot from
        // one that never completes, and a check that fails honestly-passing runs gets disabled.
        Assert.Contains("$goonDeadline.Elapsed.TotalSeconds", script, StringComparison.Ordinal);
    }

    // ==================================================================================
    // helpers
    // ==================================================================================

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine([dir.FullName, .. RepoAnchorParts])))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"repo root not found walking up from {AppContext.BaseDirectory} "
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the goon serving guard refuses to skip");
    }
}
