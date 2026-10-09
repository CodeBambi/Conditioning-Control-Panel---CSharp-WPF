using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Core.Content.Tests;

/// <summary>
/// The network half of WPF ContentPackService on Core, against a fake HTTP handler (never the
/// live server): manifest + built-in fallback, the Patreon signed-URL install with Range resume and
/// the encrypt step, rate limits, the V2 external link, the local zip install and the preview picks.
/// </summary>
public sealed class ContentPackServiceTests : IDisposable
{
    private const string Base = "http://127.0.0.1:9";
    private const string Cdn = "https://cdn.test/pack.zip";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-packsvc-" + Guid.NewGuid().ToString("N"));
    private readonly AppSettings _settings = new();
    private readonly FakeHandler _h = new();

    public ContentPackServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private (ContentPackService Svc, ContentPackStore Store) New(string? baseUrl = Base, string? token = "pat",
        (string, string)? identity = null)
    {
        var store = new ContentPackStore(() => _root, () => _settings, () => { }, tempDir: () => Path.Combine(_root, ".temp"));
        var svc = new ContentPackService(store, new HttpClient(_h), () => baseUrl, () => _settings, () => { },
            () => token, () => identity, () => Path.Combine(_root, "previews"), _ => Task.CompletedTask);
        return (svc, store);
    }

    private static byte[] Zip(params (string Path, string Body)[] entries)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (p, b) in entries)
            {
                using var s = z.CreateEntry(p).Open();
                var bytes = Encoding.UTF8.GetBytes(b);
                s.Write(bytes, 0, bytes.Length);
            }
        return ms.ToArray();
    }

    private void ServeSignedUrl() => _h.On("POST", Base + "/pack/download-url",
        _ => Json(HttpStatusCode.OK, new { success = true, downloadUrl = Cdn, rateLimit = new { remaining = 4 } }));

    private static HttpResponseMessage Json(HttpStatusCode code, object body) =>
        new(code) { Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Manifest_FromServer_StampsInstalledState()
    {
        ContentPackStoreTests.WritePack(_root, "p1", "Pack One", ("a.png", "image"));
        _h.On("GET", Base + "/packs/manifest", _ => Json(HttpStatusCode.OK,
            new { version = "2", packs = new[] { new { id = "p1", name = "Pack One" }, new { id = "p2", name = "Two" } } }));
        var (svc, _) = New();

        var packs = await svc.GetAvailablePacksAsync();

        Assert.Equal(new[] { "p1", "p2" }, packs.Select(p => p.Id));
        Assert.True(packs[0].IsDownloaded);
        Assert.True(packs[0].IsActive);   // orphans register active, as on WPF
        Assert.False(packs[1].IsDownloaded);
    }

    [Fact]
    public async Task Manifest_Unreachable_FallsBackToBuiltIn()
    {
        _h.On("GET", Base + "/packs/manifest", _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var (svc, _) = New();
        var packs = await svc.GetAvailablePacksAsync();
        Assert.Equal(new[] { "basic-bimbo-starter", "enhanced-bimbodoll-video" }, packs.Select(p => p.Id));
    }

    [Fact]
    public async Task NoBaseUrl_OrOffline_SendsNothing()
    {
        var (svc, _) = New(baseUrl: null);
        Assert.Equal(2, (await svc.GetAvailablePacksAsync()).Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.InstallPackAsync(new ContentPack { Id = "x" }));

        _settings.OfflineMode = true;
        var (svc2, _) = New();
        Assert.Equal(2, (await svc2.GetAvailablePacksAsync()).Count);
        Assert.Null(await svc2.GetFullPackStatusAsync());
        Assert.Empty(_h.Requests);
    }

    [Fact]
    public async Task Install_SignedUrl_DownloadsEncryptsAndRegisters()
    {
        ServeSignedUrl();
        var zip = Zip(("Pack/images/a.png", "AAA"), ("Pack/images/b.txt", "skip"), ("Pack/videos/v.mp4", "VVV"));
        _h.On("GET", Cdn, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });
        var (svc, store) = New();
        var pack = new ContentPack { Id = "p9", Name = "Nine", SizeBytes = zip.Length };
        var completed = 0;
        svc.PackDownloadCompleted += (_, _) => completed++;

        await svc.InstallPackAsync(pack);

        var post = _h.Requests.First(r => r.Method == "POST");
        Assert.Equal("Bearer pat", post.Authorization);
        Assert.Contains("\"packId\":\"p9\"", post.Body);
        Assert.True(store.IsPackInstalled("p9"));
        Assert.Contains("p9", _settings.InstalledPackIds);
        Assert.Equal(new[] { "a.png" }, store.GetPackFiles("p9", "image").Select(f => f.OriginalName));
        Assert.Single(store.GetPackFiles("p9", "video"));
        using (var s = store.GetPackFileStream("p9", store.GetPackFiles("p9", "image")[0])!)
            Assert.Equal("AAA", Encoding.UTF8.GetString(s.ToArray()));
        Assert.True(pack.IsDownloaded);
        Assert.False(pack.IsDownloading);
        Assert.Equal(1, completed);
        Assert.Empty(Directory.GetFiles(store.PacksFolder, ".*_temp.zip"));
        Assert.Empty(Directory.GetDirectories(store.PacksFolder, ".*_extract"));
    }

    [Fact]
    public async Task Install_DroppedConnection_ResumesWithRange()
    {
        ServeSignedUrl();
        var zip = Zip(("images/a.png", new string('x', 4000)));
        var cut = zip.Length / 2;
        var calls = 0;
        _h.On("GET", Cdn, req =>
        {
            calls++;
            if (calls == 1)
            {
                var c = new StreamContent(new BreakingStream(zip, cut));
                c.Headers.ContentLength = zip.Length;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = c };
            }
            Assert.Equal(cut, req.Headers.Range!.Ranges.Single().From);
            var rest = new ByteArrayContent(zip.Skip(cut).ToArray());
            rest.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(cut, zip.Length - 1, zip.Length);
            return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = rest };
        });
        var (svc, store) = New();
        var statuses = new List<string>();
        svc.PackInstallStatus += (_, e) => statuses.Add(e.Status);

        await svc.InstallPackAsync(new ContentPack { Id = "r1", Name = "Resume" });

        Assert.Equal(2, calls);
        Assert.Contains(statuses, s => s.StartsWith("Connection lost at", StringComparison.Ordinal));
        Assert.True(store.IsPackInstalled("r1"));
    }

    [Fact]
    public async Task Install_RateLimited_RaisesEventWithResetTime()
    {
        _h.On("POST", Base + "/pack/download-url", _ => Json(HttpStatusCode.TooManyRequests,
            new { error = "rate", message = "Daily limit reached", resetTime = "2026-10-10T12:00:00Z" }));
        var (svc, store) = New();
        (string Msg, DateTime Reset)? seen = null;
        svc.RateLimitExceeded += (_, e) => seen = (e.Message, e.ResetTime);

        await Assert.ThrowsAsync<PackRateLimitException>(() => svc.InstallPackAsync(new ContentPack { Id = "p1" }));

        Assert.Equal("Daily limit reached", seen!.Value.Msg);
        Assert.Equal(new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc), seen.Value.Reset.ToUniversalTime());
        Assert.False(store.IsPackInstalled("p1"));
    }

    [Fact]
    public async Task Install_NoPatreonToken_AsksForSignInAndSendsNothing()
    {
        var (svc, _) = New(token: null);
        string? msg = null;
        svc.AuthenticationRequired += (_, m) => msg = m;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.InstallPackAsync(new ContentPack { Id = "p1" }));
        Assert.NotNull(msg);
        Assert.Empty(_h.Requests);
    }

    [Fact]
    public async Task Install_Server401_RaisesAuthRequired()
    {
        _h.On("POST", Base + "/pack/download-url", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var (svc, _) = New();
        var asked = 0;
        svc.AuthenticationRequired += (_, _) => asked++;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.InstallPackAsync(new ContentPack { Id = "p1" }));
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task ExternalLink_UsesV2Door()
    {
        _h.On("POST", Base + "/pack/download-url", _ => Json(HttpStatusCode.OK, new { downloadUrl = "https://mega.test/x" }));
        var (svc, _) = New(identity: ("u-1", "tok"));
        Assert.Equal("https://mega.test/x", await svc.GetExternalPackDownloadUrlAsync("ext"));
        var req = _h.Requests.Single();
        Assert.Equal("tok", req.AuthToken);
        Assert.Null(req.Authorization);
        Assert.Contains("\"unified_id\":\"u-1\"", req.Body);

        var (signedOut, _) = New(identity: null);
        string? msg = null;
        signedOut.AuthenticationRequired += (_, m) => msg = m;
        Assert.Null(await signedOut.GetExternalPackDownloadUrlAsync("ext"));
        Assert.NotNull(msg);
    }

    [Fact]
    public async Task PackStatus_ReadsServerShape()
    {
        _h.On("GET", Base + "/pack/status", _ => Json(HttpStatusCode.OK,
            new { userId = "u", dailyLimit = 5, packs = new Dictionary<string, object> { ["p1"] = new { canDownload = true, downloadsRemaining = 3 } } }));
        var (svc, _) = New();
        var status = await svc.GetFullPackStatusAsync();
        Assert.Equal(5, status!.DailyLimit);
        Assert.Equal(3, status.Packs!["p1"].DownloadsRemaining);
    }

    [Fact]
    public async Task LocalZip_Installs_AndKeepsUsersZip_RefusesNonPack()
    {
        var zipPath = Path.Combine(_root, "mine.zip");
        File.WriteAllBytes(zipPath, Zip(("images/a.jpg", "J")));
        var (svc, store) = New(baseUrl: null);
        await svc.InstallPackFromLocalZipAsync(new ContentPack { Id = "z1", Name = "Zip" }, zipPath);
        Assert.True(store.IsPackInstalled("z1"));
        Assert.True(File.Exists(zipPath));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.InstallPackFromLocalZipAsync(new ContentPack { Id = "z1" }, zipPath));

        var junk = Path.Combine(_root, "junk.zip");
        File.WriteAllBytes(junk, Zip(("readme.txt", "no")));
        var before = Directory.GetDirectories(store.PacksFolder).Length;
        await Assert.ThrowsAsync<InvalidDataException>(() => svc.InstallPackFromLocalZipAsync(new ContentPack { Id = "z2" }, junk));
        Assert.Equal(before, Directory.GetDirectories(store.PacksFolder).Length);
        Assert.Empty(_h.Requests);
    }

    [Fact]
    public void Previews_PickIsCachedPerPack()
    {
        var files = Enumerable.Range(0, 14).Select(i => ($"i{i}.png", "image")).ToArray();
        var (guid, _) = ContentPackStoreTests.WritePack(_root, "pv", "Previews", files);
        var (svc, _) = New();

        var first = svc.GetPackPreviewBytes("pv", 10).Select(Encoding.UTF8.GetString).ToList();
        var second = svc.GetPackPreviewBytes("pv", 10).Select(Encoding.UTF8.GetString).ToList();

        Assert.Equal(10, first.Count);
        Assert.Equal(first.OrderBy(x => x), second.OrderBy(x => x));
        Assert.True(File.Exists(Path.Combine(_root, ".packs", guid, ContentPackService.PreviewCacheFileName)));
        svc.ClearPreviewCache("pv");
        Assert.False(File.Exists(Path.Combine(_root, ".packs", guid, ContentPackService.PreviewCacheFileName)));
    }

    [Fact]
    public async Task UrlPreviews_CachedOnDisk_SecondReadSendsNothing()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        _h.On("GET", "https://img.test/abc123", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) });
        var (svc, _) = New();
        var urls = new[] { "https://img.test/abc123", "http://plain.test/no.png" };

        Assert.Single(await svc.GetPreviewBytesFromUrlsAsync("p1", urls));
        Assert.Single(await svc.GetPreviewBytesFromUrlsAsync("p1", urls));
        Assert.Single(_h.Requests);
        Assert.True(File.Exists(Path.Combine(_root, "previews", "p1", "abc123.png")));
    }

    // ---------------------------------------------------------------------------------------------

    private sealed record Seen(string Method, string Url, string? Authorization, string? AuthToken, string Body);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();
        public List<Seen> Requests { get; } = new();

        public void On(string method, string url, Func<HttpRequestMessage, HttpResponseMessage> reply) => _routes[method + " " + url] = reply;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new Seen(request.Method.Method, request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-Auth-Token", out var t) ? t.Single() : null, body));
            return _routes.TryGetValue(request.Method.Method + " " + request.RequestUri, out var r)
                ? r(request)
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    /// <summary>Hands out the first <c>cut</c> bytes, then drops the connection.</summary>
    private sealed class BreakingStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _cut;
        private int _pos;
        public BreakingStream(byte[] data, int cut) { _data = data; _cut = cut; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _cut) throw new IOException("connection reset");
            var n = Math.Min(count, _cut - _pos);
            Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
