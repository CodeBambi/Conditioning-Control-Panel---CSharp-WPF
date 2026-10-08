using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Core CatalogueLookup (WPF CatalogueLookupService before the move) against a fake handler: no network.</summary>
public sealed class CatalogueLookupTests : IDisposable
{
    private const string HtUrl = "https://hypnotube.com/video/123";
    private readonly string _lib = Path.Combine(Path.GetTempPath(), "ccp-lookup-" + Guid.NewGuid());

    public void Dispose() { if (Directory.Exists(_lib)) Directory.Delete(_lib, true); }

    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Seen = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => new(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen.Add(r);
            return Task.FromResult(Respond(r));
        }
    }

    private static HttpResponseMessage Body(string s) => new(HttpStatusCode.OK) { Content = new StringContent(s) };

    private CatalogueLookup Lookup(Fake f, Func<string, bool>? opener = null)
    {
        var l = new CatalogueLookup(() => _lib, "9.9.9", null, f);
        if (opener != null) l.SetOpener(opener);
        return l;
    }

    private static CatalogueEntry Entry(string fileUrl, string title = "My Mix") =>
        new("e1", title, "", "me", null, new List<string>(), null, 0, HtUrl, null, fileUrl);

    [Fact]
    public async Task LookupSkipsMalformedEntriesAndKeepsTheRest()
    {
        var f = new Fake { Respond = _ => Body("{\"enhancements\":[{\"id\":\"a\",\"view_count\":\"lots\"},{\"id\":\"b\",\"title\":\"B\",\"file_url\":\"https://x/b\"}]}") };
        var ok = Assert.IsType<LookupResult.Success>(await Lookup(f).LookupForUrlAsync(HtUrl, CancellationToken.None));
        var e = Assert.Single(ok.Entries);
        Assert.Equal("b", e.Id);
        Assert.Equal(HtUrl, e.HtUrl);
        Assert.Equal("https://app.cclabs.app/api/enhancements/by-ht-url?url=" + Uri.EscapeDataString(HtUrl), f.Seen[0].RequestUri!.ToString());
        Assert.Equal("9.9.9", string.Join("", f.Seen[0].Headers.GetValues("X-Client-Version")));
    }

    [Fact]
    public async Task NonHtUrlMakesNoRequest()
    {
        var f = new Fake();
        Assert.IsType<LookupResult.InvalidUrl>(await Lookup(f).LookupForUrlAsync("https://example.com/video/1", CancellationToken.None));
        Assert.Empty(f.Seen);
    }

    [Fact]
    public async Task EmptyFileUrlIsRejectedWithoutARequest()
    {
        var f = new Fake();
        Assert.IsType<DownloadResult.NetworkError>(await Lookup(f).DownloadAndOpenAsync(Entry(" "), CancellationToken.None));
        Assert.Empty(f.Seen);
    }

    [Fact]
    public async Task NonJsonDownloadIsRejectedAndNothingIsSaved()
    {
        var f = new Fake { Respond = _ => Body("<html>oops</html>") };
        Assert.IsType<DownloadResult.InvalidFile>(await Lookup(f).DownloadAndOpenAsync(Entry("https://x/e1"), CancellationToken.None));
        Assert.False(Directory.Exists(_lib));
    }

    [Fact]
    public async Task DownloadSavesIntoTheLibraryWithACollisionSuffixAndOpensIt()
    {
        var f = new Fake { Respond = _ => Body("{\"v\":1}") };
        var opened = new List<string>();
        var l = Lookup(f, p => { opened.Add(p); return true; });

        Assert.Equal("My Mix.ccpenh.json", Assert.IsType<DownloadResult.Success>(await l.DownloadAndOpenAsync(Entry("https://x/e1"), CancellationToken.None)).LocalFilename);
        Assert.Equal("My Mix (2).ccpenh.json", Assert.IsType<DownloadResult.Success>(await l.DownloadAndOpenAsync(Entry("https://x/e1"), CancellationToken.None)).LocalFilename);
        Assert.Equal("{\"v\":1}", File.ReadAllText(Path.Combine(_lib, "My Mix.ccpenh.json")));
        Assert.Equal(new[] { Path.Combine(_lib, "My Mix.ccpenh.json"), Path.Combine(_lib, "My Mix (2).ccpenh.json") }, opened);

        var failing = Lookup(f, _ => false);
        Assert.IsType<DownloadResult.OpenError>(await failing.DownloadAndOpenAsync(Entry("https://x/e1"), CancellationToken.None));
    }

    [Fact]
    public async Task ClosingAnOldHostKeepsTheNewOpenerAndClosingTheNewHostReleasesIt()
    {
        var f = new Fake { Respond = _ => Body("{\"v\":1}") };
        var opened = 0;
        Func<string, bool> oldHost = _ => throw new InvalidOperationException("old host called");
        Func<string, bool> newHost = _ => { opened++; return true; };
        var lookup = Lookup(f, oldHost);
        lookup.SetOpener(newHost);
        lookup.ClearOpener(oldHost);
        Assert.IsType<DownloadResult.Success>(await lookup.DownloadAndOpenAsync(Entry("https://x/e1"), CancellationToken.None));
        Assert.Equal(1, opened);
        lookup.ClearOpener(newHost);
        Assert.IsType<DownloadResult.Success>(await lookup.DownloadAndOpenAsync(Entry("https://x/e1"), CancellationToken.None));
        Assert.Equal(1, opened);
    }

    /// <summary>The sandbox rule (CatalogueClient.ResolveBaseUrl): a CCP_USERDATA_DIR sandbox with no loopback
    /// override never sends, lookup or download; a loopback override is where both go, and a non-loopback
    /// bundle URL is refused under it.</summary>
    [Fact]
    public async Task SandboxWithoutAnOverrideSendsNothingAndALoopbackOverrideIsWhereItGoes()
    {
        var f = new Fake { Respond = _ => Body("{\"enhancements\":[]}") };
        var bare = new CatalogueLookup(() => _lib, "9.9.9", null, f, overrideUrl: null, sandboxed: true);
        Assert.IsType<LookupResult.NetworkError>(await bare.LookupForUrlAsync(HtUrl, CancellationToken.None));
        Assert.IsType<DownloadResult.NetworkError>(await bare.DownloadAndOpenAsync(Entry("http://127.0.0.1:9/e1"), CancellationToken.None));
        Assert.Empty(f.Seen);
        // A non-loopback override is not honoured either.
        var remote = new CatalogueLookup(() => _lib, "9.9.9", null, f, "https://evil.example", sandboxed: true);
        Assert.IsType<LookupResult.NetworkError>(await remote.LookupForUrlAsync(HtUrl, CancellationToken.None));
        Assert.Empty(f.Seen);

        var loop = new CatalogueLookup(() => _lib, "9.9.9", null, f, "http://127.0.0.1:4555", sandboxed: true);
        Assert.IsType<LookupResult.None>(await loop.LookupForUrlAsync(HtUrl, CancellationToken.None));
        Assert.StartsWith("http://127.0.0.1:4555/api/enhancements/by-ht-url?url=", Assert.Single(f.Seen).RequestUri!.ToString());
        Assert.IsType<DownloadResult.NetworkError>(await loop.DownloadAndOpenAsync(Entry("https://app.cclabs.app/e1"), CancellationToken.None));
        Assert.Single(f.Seen);
    }
}
