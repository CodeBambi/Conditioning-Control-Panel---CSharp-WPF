using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Transfer;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Goon own-media transfer through the window (WPF GoonHostService.OnRecvVerb, GoonCacheBridge,
/// the ccp.cache virtual host): what a partner sent is validated before the page ever gets a url, the
/// url is served from transfer-cache only, and the session's files go with the window. Process-wide
/// stores, so alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class GoonTransferTests
{
    private static (GameWindow W, List<JObject> Posted) Open()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        GoonHostService.DetachWindow();
        var w = new GameWindow(GameWindow.Games["goon"]);
        var posted = new List<JObject>();
        w.Posted += json => { lock (posted) posted.Add(JObject.Parse(json)); };
        w.Show();
        return (w, posted);
    }

    private static List<JObject> Of(List<JObject> posted, string type)
    {
        lock (posted) return posted.Where(p => (string?)p["type"] == type).ToList();
    }

    private static JObject Recv(List<JObject> posted, string id) => Of(posted, "goon-recv-result").Last(r => (string?)r["id"] == id);

    private static async Task<JObject> WaitRecv(List<JObject> posted, string id, int count)
    {
        for (int i = 0; i < 400 && Of(posted, "goon-recv-result").Count(r => (string?)r["id"] == id) < count; i++) await Task.Delay(10);
        return Recv(posted, id);
    }

    /// <summary>A file that sniffs as WebP (RIFF....WEBP), unique per call.</summary>
    private static (byte[] Bytes, string Sha) Webp()
    {
        var b = new byte[96];
        RandomNumberGenerator.Fill(b);
        "RIFF"u8.CopyTo(b); "WEBP"u8.CopyTo(b.AsSpan(8));
        return (b, Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant());
    }

    private static string Frame(string type, object body)
    {
        var o = JObject.FromObject(body);
        o["type"] = type;
        return o.ToString(Newtonsoft.Json.Formatting.None);
    }

    [Fact]
    public async Task AReceivedFile_IsHashedAndSniffed_ThenServedFromTheCache_AndGoesWithTheWindow()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (w, posted) = Open();
            string? path = null;
            try
            {
                w.HandleMessage("{\"type\":\"ready\"}");
                Assert.Empty((JArray)Of(posted, "manifest").Single()["received"]!);   // ephemeral: empty at boot

                var (bytes, sha) = Webp();
                w.HandleMessage(Frame("goon-recv-begin", new { id = "j1", sha256 = sha, mime = "image/webp", bytes = bytes.Length }));
                Assert.True((bool)Recv(posted, "j1")["ok"]!);
                w.HandleMessage(Frame("goon-recv-chunk", new { id = "j1", seq = 0, b64 = Convert.ToBase64String(bytes) }));
                Assert.True((bool)Recv(posted, "j1")["ok"]!);
                w.HandleMessage(Frame("goon-recv-commit", new { id = "j1" }));
                var done = await WaitRecv(posted, "j1", 3);
                Assert.True((bool)done["ok"]!, (string?)done["error"]);
                Assert.Equal(bytes.Length, (long)done["bytes"]!);

                // The url is this head's loopback stand-in for https://ccp.cache/recv/<sha>.<ext>, and it resolves
                // to the file in transfer-cache/recv and nowhere else.
                var url = (string?)done["url"];
                Assert.Equal(WebAssetServer.Shared.CacheUrlBase + "recv/" + sha + ".webp", url);
                path = WebAssetServer.Shared.ResolveFile(new Uri(url!).AbsolutePath);
                Assert.Equal(Path.Combine(TransferInboxStore.Instance.RecvDir, sha + ".webp"), path);
                Assert.Equal(bytes, File.ReadAllBytes(path!));
            }
            finally { w.Close(); }
            // The window is the session: its received files are gone with it.
            Assert.False(File.Exists(path));
        });
    }

    [Fact]
    public async Task AReceivedFile_ThatLiesAboutItsHashOrType_OrIsTooBig_IsRefused()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (w, posted) = Open();
            try
            {
                w.HandleMessage("{\"type\":\"ready\"}");
                var (bytes, sha) = Webp();

                // A name that is not a sha, a type off the list, a size past the 64 MB cap.
                w.HandleMessage(Frame("goon-recv-begin", new { id = "a", sha256 = "../../evil", mime = "image/webp", bytes = 10 }));
                Assert.Equal("bad-name", (string?)Recv(posted, "a")["error"]);
                w.HandleMessage(Frame("goon-recv-begin", new { id = "b", sha256 = sha, mime = "application/x-msdownload", bytes = 10 }));
                Assert.Equal("bad-format", (string?)Recv(posted, "b")["error"]);
                w.HandleMessage(Frame("goon-recv-begin", new { id = "c", sha256 = sha, mime = "image/webp", bytes = TransferInboxStore.MaxRecvBytes + 1 }));
                Assert.Equal("too-big", (string?)Recv(posted, "c")["error"]);

                // The bytes do not hash to the name they came under.
                var wrong = new string('a', 64);
                w.HandleMessage(Frame("goon-recv-begin", new { id = "d", sha256 = wrong, mime = "image/webp", bytes = bytes.Length }));
                w.HandleMessage(Frame("goon-recv-chunk", new { id = "d", seq = 0, b64 = Convert.ToBase64String(bytes) }));
                w.HandleMessage(Frame("goon-recv-commit", new { id = "d" }));
                var mismatch = await WaitRecv(posted, "d", 3);
                Assert.False((bool)mismatch["ok"]!);
                Assert.Equal("hash-mismatch", (string?)mismatch["error"]);
                Assert.False(File.Exists(Path.Combine(TransferInboxStore.Instance.RecvDir, wrong + ".webp")));

                // Declared a video, the magic bytes say WebP: rejected, never relabelled.
                w.HandleMessage(Frame("goon-recv-begin", new { id = "e", sha256 = sha, mime = "video/mp4", bytes = bytes.Length }));
                w.HandleMessage(Frame("goon-recv-chunk", new { id = "e", seq = 0, b64 = Convert.ToBase64String(bytes) }));
                w.HandleMessage(Frame("goon-recv-commit", new { id = "e" }));
                var lied = await WaitRecv(posted, "e", 3);
                Assert.False((bool)lied["ok"]!);
                Assert.Equal("bad-format", (string?)lied["error"]);
                Assert.Empty(Directory.EnumerateFiles(TransferInboxStore.Instance.RecvDir, sha + ".*"));

                // A chunk for a job nobody opened.
                w.HandleMessage(Frame("goon-recv-chunk", new { id = "nobody", seq = 0, b64 = "AAAA" }));
                Assert.Equal("unknown-job", (string?)Recv(posted, "nobody")["error"]);
            }
            finally { w.Close(); }
        });
    }

    [Fact]
    public void TheCacheRoute_ServesOnlyHashNamedMediaInItsThreeFolders()
    {
        var store = TransferCacheStore.Instance;
        store.EnsureRoot();
        var sha = new string('b', 64);
        var art = Path.Combine(store.ArtDir, sha + ".webp");
        var exe = Path.Combine(store.RecvDir, sha + ".exe");
        var index = Path.Combine(store.Root, "probe_index.json");
        try
        {
            File.WriteAllBytes(art, new byte[] { 1 });
            File.WriteAllBytes(exe, new byte[] { 1 });
            File.WriteAllText(index, "{}");
            var server = WebAssetServer.Shared;
            Assert.Equal(art, server.ResolveFile("/ccp.cache/art/" + sha + ".webp"));
            Assert.Null(server.ResolveFile("/ccp.cache/recv/" + sha + ".exe"));          // not media
            Assert.Null(server.ResolveFile("/ccp.cache/probe_index.json"));               // the bookkeeping
            Assert.Null(server.ResolveFile("/ccp.cache/art/../probe_index.json"));
            Assert.Null(server.ResolveFile("/ccp.cache/tmp/" + sha + ".webp"));           // not one of the three folders
            Assert.Null(server.ResolveFile("/ccp.cache/art/sub/" + sha + ".webp"));       // never nested
            Assert.Null(server.ResolveFile("/ccp.cache/art/notahash.webp"));
        }
        finally { File.Delete(art); File.Delete(exe); File.Delete(index); }
    }

    [Fact]
    public async Task TheCacheFeed_AnswersThePagesHelloAndList_AndSendingIsThePatronBar()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (w, posted) = Open();
            try
            {
                w.HandleMessage("{\"type\":\"ready\"}");
                // The cap is WPF's patron bar, no longer forced off on this head.
                var caps = (JObject)Of(posted, "init").Single()["caps"]!;
                Assert.Equal(GoonHostService.TransferAllowed(), (bool)caps["mediaTransfer"]!);

                w.HandleMessage("{\"type\":\"cache-req\",\"op\":\"hello\",\"caps\":{\"videoEncoder\":false}}");
                w.HandleMessage("{\"type\":\"cache-req\",\"op\":\"list\"}");
                for (int i = 0; i < 300 && (Of(posted, "cache-list").Count == 0 || Of(posted, "cache-state").Count == 0); i++) await Task.Delay(10);
                Assert.NotEmpty(Of(posted, "cache-state"));
                var list = Of(posted, "cache-list").Last();
                Assert.True((bool)list["last"]!);
            }
            finally { w.Close(); }
        });
    }
}
