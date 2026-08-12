using System.Net;
using System.Text.Json;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-026 slice b4: THE LOOM (save/delete/list/result + GIF validation + §4 serving) and
/// user-media serving (manifest enumeration + /umedia/ route). Loom semantics ported from
/// DtrhLoomStore.cs; serving stays inside the §4 loopback contract (GET-only, MIME
/// allowlist deny-by-default, traversal refusal, localhost, CORS-on-errors). Media
/// logging is presence+shape ONLY (packet framing c — no filenames/slugs in logs).
/// </summary>
public sealed class DtrhLoomTests : IDisposable
{
    private readonly string _root;
    private readonly string _spirals;
    private readonly List<string> _log = [];

    public DtrhLoomTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ccp-sp026-loom-" + Guid.NewGuid().ToString("N"));
        _spirals = Path.Combine(_root, "Spirals");
        Directory.CreateDirectory(_spirals);
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    private DtrhLoom NewLoom() => new(_spirals, _log.Add);

    /// <summary>A minimal valid GIF89a (magic + filler + 0x3B trailer, ≥16 bytes).</summary>
    private static byte[] TinyGif(int size = 64)
    {
        var b = new byte[size];
        b[0] = (byte)'G'; b[1] = (byte)'I'; b[2] = (byte)'F'; b[3] = (byte)'8'; b[4] = (byte)'9'; b[5] = (byte)'a';
        b[^1] = 0x3B;
        return b;
    }

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes);

    // ---------- slug discipline ----------

    [Theory]
    [InlineData("Dream Spiral", "dream-spiral")]
    [InlineData("  Padded  ", "padded")]
    [InlineData("under_score-ok", "under_score-ok")]
    [InlineData("CaPs MiX", "caps-mix")]
    [InlineData("weird!@#$chars", "weird-chars")]
    [InlineData("---", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("a-very-long-spiral-name-that-exceeds-the-limit", "a-very-long-spiral-name-")]
    public void Slugify_WpfDiscipline(string? input, string? expected) =>
        Assert.Equal(expected, DtrhLoom.Slugify(input));

    // ---------- save/list/delete lifecycle ----------

    [Fact]
    public void Save_List_Delete_Lifecycle_WithSidecar()
    {
        var loom = NewLoom();
        var save = loom.Save("Dream", B64(TinyGif()), JsonDocument.Parse("{\"arms\":4}").RootElement, overwrite: false);
        Assert.True(save.Ok);
        Assert.Equal("dream", save.Slug);
        Assert.True(File.Exists(Path.Combine(_spirals, "loom_dream.gif")));
        Assert.True(File.Exists(Path.Combine(_spirals, "loom_dream.json")));
        Assert.False(File.Exists(Path.Combine(_spirals, "loom_dream.gif.tmp"))); // temp moved over

        var list = loom.List();
        Assert.Single(list);
        Assert.Equal("dream", list[0].Slug);
        Assert.Contains("\"arms\"", list[0].ParamsJson!);

        // Media-logging rule: the log carries bytes/status, NEVER the slug.
        Assert.Contains(_log, l => l.Contains("saved spiral (64 bytes)"));
        Assert.DoesNotContain(_log, l => l.Contains("dream"));

        var del = loom.Delete("dream");
        Assert.True(del.Ok);
        Assert.Empty(loom.List());
        Assert.False(File.Exists(Path.Combine(_spirals, "loom_dream.gif")));
        Assert.False(File.Exists(Path.Combine(_spirals, "loom_dream.json"))); // sidecar best-effort removed
    }

    [Fact]
    public void Save_ExistsRequiresOverwrite()
    {
        var loom = NewLoom();
        Assert.True(loom.Save("dream", B64(TinyGif()), null, overwrite: false).Ok);
        var again = loom.Save("dream", B64(TinyGif(32)), null, overwrite: false);
        Assert.False(again.Ok);
        Assert.Equal("exists", again.Error); // the page arms overwrite on this code (loomStudio.js:113)
        Assert.Equal(64, new FileInfo(Path.Combine(_spirals, "loom_dream.gif")).Length); // untouched
        var over = loom.Save("dream", B64(TinyGif(32)), null, overwrite: true);
        Assert.True(over.Ok);
        Assert.Equal(32, new FileInfo(Path.Combine(_spirals, "loom_dream.gif")).Length);
    }

    [Fact]
    public void Save_ErrorCodes_BadName_TooBig_BadGif_IoFailed()
    {
        var loom = NewLoom();
        Assert.Equal("bad-name", loom.Save("!!!", B64(TinyGif()), null, false).Error);
        Assert.Equal("bad-name", loom.Save(null, B64(TinyGif()), null, false).Error);
        Assert.Equal("too-big", loom.Save("x", "", null, false).Error); // empty payload
        Assert.Equal("bad-gif", loom.Save("x", "!!!not-base64!!!", null, false).Error);
        Assert.Equal("bad-gif", loom.Save("x", B64(new byte[64]), null, false).Error); // no magic
        var shortGif = TinyGif(16);
        shortGif[4] = (byte)'6'; // GIF86a — not 87a/89a
        Assert.Equal("bad-gif", loom.Save("x", B64(shortGif), null, false).Error);
        var noTrailer = TinyGif(64);
        noTrailer[^1] = 0x00;
        Assert.Equal("bad-gif", loom.Save("x", B64(noTrailer), null, false).Error);
        // Delete on a missing file → io-failed (WPF :118).
        Assert.Equal("io-failed", loom.Delete("ghost").Error);
        // Delete with a traversal-shaped slug → bad-name (regex refuses).
        Assert.Equal("bad-name", loom.Delete("../escape").Error);
        Assert.Equal("bad-name", loom.Delete("a/b").Error);
    }

    [Fact]
    public void Save_CapReached_At12()
    {
        var loom = NewLoom();
        for (var i = 0; i < DtrhLoom.MaxSpirals; i++)
        {
            Assert.True(loom.Save($"spiral-{i}", B64(TinyGif(32)), null, false).Ok);
        }

        var over = loom.Save("one-too-many", B64(TinyGif(32)), null, false);
        Assert.False(over.Ok);
        Assert.Equal("cap-reached", over.Error);
        // …but an overwrite of an EXISTING slug is not a new entry — allowed at cap.
        Assert.True(loom.Save("spiral-0", B64(TinyGif(48)), null, true).Ok);
    }

    // ---------- serving through the §4 loopback ----------

    private sealed class ServerHarness : IDisposable
    {
        public LoopbackServer Server { get; }
        public CollectingLog Log { get; } = new();

        public ServerHarness(string spiralsRoot, string userMediaRoot)
        {
            var payload = Path.Combine(Path.GetTempPath(), "ccp-sp026-lb-" + Guid.NewGuid().ToString("N"));
            var media = Path.Combine(payload, "assets");
            Directory.CreateDirectory(media);
            File.WriteAllText(Path.Combine(payload, "index.html"), "<html/>");
            Server = new LoopbackServer(payload, payload, media, new Inbox(), "tok", Log,
                TimeSpan.FromMilliseconds(100), spiralsRoot: spiralsRoot, userMediaRoot: userMediaRoot);
            Server.Start();
            LoopbackListenerRegistry.RegisterLoopbackServer(nameof(ServerHarness), Server); // SP-059 T-15 self-check coverage
        }

        public void Dispose()
        {
            Server.Dispose();
            LoopbackListenerRegistry.UnregisterLoopbackServer(Server); // unregister only after the best-effort dispose
        }
    }

    private sealed class CollectingLog : ILogSink
    {
        private readonly object _gate = new();
        private readonly List<string> _lines = [];
        public void Log(string message) { lock (_gate) _lines.Add(message); }
        public string All { get { lock (_gate) return string.Join('\n', _lines); } }
    }

    [Fact]
    public async Task Spirals_ServedThroughLoopback_GifMime_Cors_Nosniff()
    {
        File.WriteAllBytes(Path.Combine(_spirals, "loom_dream.gif"), TinyGif(128));
        var userMedia = Path.Combine(_root, "assets");
        using var h = new ServerHarness(_spirals, userMedia);
        using var client = new HttpClient();
        var response = await client.GetAsync(h.Server.MediaOrigin + "/spirals/loom_dream.gif", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/gif", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", string.Join(',', response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(h.Server.PageOrigin, string.Join(',', response.Headers.GetValues("Access-Control-Allow-Origin")));
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(128, body.Length);
    }

    [Fact]
    public async Task Spirals_Traversal_AndMethod_Refused()
    {
        var userMedia = Path.Combine(_root, "assets");
        using var h = new ServerHarness(_spirals, userMedia);
        using var client = new HttpClient();
        var traversal = await client.GetAsync(h.Server.MediaOrigin + "/spirals/..%2Fsecret.gif", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, traversal.StatusCode);
        var backslash = await client.GetAsync(h.Server.MediaOrigin + "/spirals/a%5Cb.gif", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, backslash.StatusCode);
        var missing = await client.GetAsync(h.Server.MediaOrigin + "/spirals/loom_ghost.gif", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        // CORS-on-errors (§4.5): refusals on the media origin carry CORS headers.
        Assert.True(traversal.Headers.Contains("Access-Control-Allow-Origin"));
        var post = await client.PostAsync(h.Server.MediaOrigin + "/spirals/loom_dream.gif", new StringContent("x"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
    }

    [Fact]
    public async Task UserMedia_Served_WithRouteScopedMime_Range_AndTraversalRefusal()
    {
        var userMedia = Path.Combine(_root, "assets");
        var images = Path.Combine(userMedia, "images");
        var videos = Path.Combine(userMedia, "videos");
        Directory.CreateDirectory(images);
        Directory.CreateDirectory(videos);
        File.WriteAllBytes(Path.Combine(images, "photo.jpg"), Enumerable.Range(0, 500).Select(i => (byte)i).ToArray());
        File.WriteAllBytes(Path.Combine(videos, "clip.mp4"), Enumerable.Range(0, 2000).Select(i => (byte)(i % 253)).ToArray());
        File.WriteAllText(Path.Combine(images, "notes.txt"), "not media");
        using var h = new ServerHarness(_spirals, userMedia);
        using var client = new HttpClient();

        var jpg = await client.GetAsync(h.Server.MediaOrigin + "/umedia/images/photo.jpg", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, jpg.StatusCode);
        Assert.Equal("image/jpeg", jpg.Content.Headers.ContentType?.MediaType);

        // Range on a video (§4.3 — required by seek).
        using var rangeReq = new HttpRequestMessage(HttpMethod.Get, h.Server.MediaOrigin + "/umedia/videos/clip.mp4");
        rangeReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 99);
        var ranged = await client.SendAsync(rangeReq, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.PartialContent, ranged.StatusCode);
        Assert.Equal("bytes 0-99/2000", ranged.Content.Headers.ContentRange?.ToString());
        Assert.Equal("video/mp4", ranged.Content.Headers.ContentType?.MediaType);
        using var badRangeReq = new HttpRequestMessage(HttpMethod.Get, h.Server.MediaOrigin + "/umedia/videos/clip.mp4");
        badRangeReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(99999, 100000);
        var badRange = await client.SendAsync(badRangeReq, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, badRange.StatusCode);

        // Deny-by-default 415 outside the route's table (§4.4 posture).
        var txt = await client.GetAsync(h.Server.MediaOrigin + "/umedia/images/notes.txt", TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode)415, txt.StatusCode);

        // Traversal classes (§4.6): encoded dots, drive-colon, leading-slash escape.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(h.Server.MediaOrigin + "/umedia/..%2F..%2Fetc", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(h.Server.MediaOrigin + "/umedia/C%3A%5CWindows%5Cx.mp4", TestContext.Current.CancellationToken)).StatusCode);

        // Media-logging rule: route classes only — no user filename ever logged.
        Assert.DoesNotContain("photo.jpg", h.Log.All);
        Assert.DoesNotContain("clip.mp4", h.Log.All);
    }

    // ---------- user-media manifest ----------

    [Fact]
    public void Manifest_Enumerates_UserMedia_WithCaps_Skips_AndCountsOnlyLog()
    {
        var userMedia = Path.Combine(_root, "assets");
        var images = Path.Combine(userMedia, "images");
        var videos = Path.Combine(userMedia, "videos");
        var hidden = Path.Combine(images, ".hidden");
        Directory.CreateDirectory(images);
        Directory.CreateDirectory(videos);
        Directory.CreateDirectory(hidden);
        File.WriteAllBytes(Path.Combine(images, "a.jpg"), [1]);
        File.WriteAllBytes(Path.Combine(images, "b.png"), [1]);
        File.WriteAllBytes(Path.Combine(images, "c.bmp"), [1]);     // media-like, undecodable → skipped
        File.WriteAllBytes(Path.Combine(images, "notes.txt"), [1]); // junk → silently ignored
        File.WriteAllBytes(Path.Combine(hidden, "d.jpg"), [1]);     // dot-dir → skipped silently
        File.WriteAllBytes(Path.Combine(videos, "v.mp4"), [1]);
        File.WriteAllBytes(Path.Combine(videos, "w.wmv"), [1]);     // media-like → skipped
        var huge = Path.Combine(images, "huge.png");
        using (var fs = File.Create(huge)) fs.SetLength(51L * 1024 * 1024); // over the 50MB cap → skipped

        var log = new List<string>();
        var m = DtrhUserMedia.Build(userMedia, "http://127.0.0.1:50001", log.Add);

        Assert.Equal(2, m.Images.Count);
        Assert.Single(m.Videos);
        Assert.Equal(3, m.Skipped); // bmp + wmv + oversized png
        Assert.False(m.Truncated);
        var entry = Assert.Single(m.Images, e => e.Name == "a.jpg");
        Assert.Equal("http://127.0.0.1:50001/umedia/images/a.jpg", entry.Url);
        Assert.Equal("http://127.0.0.1:50001/umedia/videos/v.mp4", m.Videos[0].Url);
        // Counts-only logging (packet framing c): counts in, names/paths OUT.
        Assert.Single(log);
        Assert.Contains("2 image(s), 1 video(s), 3 skipped", log[0]);
        Assert.DoesNotContain("a.jpg", log[0]);
        Assert.DoesNotContain("huge", log[0]);
    }

    [Fact]
    public void Manifest_EmptyPool_IsEmpty_NotFallback()
    {
        var log = new List<string>();
        var m = DtrhUserMedia.Build(Path.Combine(_root, "does-not-exist"), "http://127.0.0.1:50001", log.Add);
        Assert.Empty(m.Images);
        Assert.Empty(m.Videos);
        Assert.Equal(0, m.Skipped);
    }
}
