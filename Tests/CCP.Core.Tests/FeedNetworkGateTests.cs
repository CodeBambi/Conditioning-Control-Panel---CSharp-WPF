using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard.Showcase;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>hunt3 IC9: community prompts and showcase clips make no request in Offline mode, and none
/// on the real transport from a CCP_USERDATA_DIR sandbox, as Chaster and the catalogue already do.</summary>
[Collection(SessionStatics.Name)]   // swaps ShowcaseCache.OfflineMode
public sealed class FeedNetworkGateTests
{
    [Theory]
    [InlineData(false, true, false, false)]   // a real install, online: goes out
    [InlineData(true, true, false, true)]     // Offline mode
    [InlineData(false, true, true, true)]     // sandbox, real transport: refused
    [InlineData(false, false, true, false)]   // sandbox, a test's own transport: not the network
    [InlineData(true, false, true, true)]     // Offline mode refuses any transport
    public void TheRule(bool offline, bool realTransport, bool sandboxed, bool blocked) =>
        Assert.Equal(blocked, SandboxNet.FeedBlocked(offline, realTransport, sandboxed));

    private sealed class Counting : HttpMessageHandler
    {
        public int Asked;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Asked);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task TheShowcaseMakesNoRequestInOfflineMode()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-k27-showcase-" + Guid.NewGuid().ToString("N"));
        var prev = ShowcaseCache.OfflineMode;
        var handler = new Counting();
        try
        {
            var cache = new ShowcaseCache(root, new Uri("https://example.invalid/showcase.json"), new HttpClient(handler));
            ShowcaseCache.OfflineMode = () => true;
            Assert.True(cache.NetworkOff);
            Assert.Null(await cache.FetchManifestAsync());
            Assert.Equal(0, handler.Asked);

            ShowcaseCache.OfflineMode = () => false;
            Assert.False(cache.NetworkOff);                   // an injected transport is not the sandbox's business
            Assert.Null(await cache.FetchManifestAsync());    // 404 from the fake
            Assert.Equal(1, handler.Asked);
        }
        finally
        {
            ShowcaseCache.OfflineMode = prev;
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }
}
