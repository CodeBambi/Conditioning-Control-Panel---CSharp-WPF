using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Page wave x1 (P18): the game media manifest's remote tail, WPF DtrhAssetManifest.AppendRemote.
/// Remote entries appear only with the source off "local" AND consent, wear the "online&lt;pct&gt;:" marker,
/// and never trigger a fetch from a test.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class GameMediaManifestRemoteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-manifest-" + Guid.NewGuid().ToString("N"));
    private int _refills;

    public GameMediaManifestRemoteTests()
    {
        Directory.CreateDirectory(_dir);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long stale = now - (long)TimeSpan.FromDays(4).TotalSeconds;
        File.WriteAllText(Path.Combine(_dir, "cache.json"),
            "{\"entries\":[" +
            "{\"id\":\"scrolller/EroticHypnosis/111\",\"url\":\"https://cdn.example/a/111.webm?x=1\",\"image\":false,\"at\":" + now + "}," +
            "{\"id\":\"scrolller/EroticHypnosis/222\",\"url\":\"https://cdn.example/a/222.webp\",\"image\":true,\"at\":" + now + "}," +
            "{\"id\":\"scrolller/Old/333\",\"url\":\"https://cdn.example/a/333.webp\",\"image\":true,\"at\":" + stale + "}]}");
        GameMediaManifest.RemoteCachePathOverride = Path.Combine(_dir, "cache.json");
        GameMediaManifest.RefillOverride = () => _refills++;
        GameMediaManifest.ResetRemoteCacheForTest();
    }

    public void Dispose()
    {
        GameMediaManifest.RemoteCachePathOverride = null;
        GameMediaManifest.RefillOverride = null;
        GameMediaManifest.ResetRemoteCacheForTest();
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void LocalSource_OrNoConsent_AddsNothing_AndNeverFetches()
    {
        var m = new GameMediaManifest.Manifest();
        Assert.Equal(0, GameMediaManifest.AppendRemote(m, null));
        Assert.Equal(0, GameMediaManifest.AppendRemote(m, new AppSettings { MediaSource = "local", RemoteMediaConsented = true }));
        Assert.Equal(0, GameMediaManifest.AppendRemote(m, new AppSettings { MediaSource = "online", RemoteMediaConsented = false }));
        Assert.Empty(m.Images);
        Assert.Empty(m.Videos);
        Assert.Equal(0, _refills);
    }

    [Fact]
    public void Online_WithConsent_AppendsTheFreshCache_WithTheShareMarker()
    {
        var m = new GameMediaManifest.Manifest();
        var s = new AppSettings { MediaSource = "online", RemoteMediaConsented = true };
        Assert.Equal(2, GameMediaManifest.AppendRemote(m, s));   // the 4-day-old entry is past the TTL
        Assert.Equal("online100:EroticHypnosis-111.webm", m.Videos.Single().Name);
        Assert.Equal("https://cdn.example/a/111.webm?x=1", m.Videos.Single().Url);
        Assert.Equal("online100:EroticHypnosis-222.webp", m.Images.Single().Name);
        Assert.Equal(1, _refills);   // under the low-water mark: one top-up asked for the next launch
    }

    [Fact]
    public void Mixed_CarriesTheClampedShare()
    {
        var m = new GameMediaManifest.Manifest();
        var s = new AppSettings { MediaSource = "mixed", RemoteMediaConsented = true, RemoteMediaRatio = 30 };
        Assert.Equal(2, GameMediaManifest.AppendRemote(m, s));
        Assert.StartsWith("online30:", m.Images.Single().Name);
    }

    [Fact]
    public void CountKinds_SplitsStillsFromClips()
    {
        var cache = new[]
        {
            new GameMediaManifest.RemoteEntry("a/b/1", "https://x/1.webp", true, 0),
            new GameMediaManifest.RemoteEntry("a/b/2", "https://x/2.mp4", false, 0),
            new GameMediaManifest.RemoteEntry("a/b/3", "https://x/3.mp4", false, 0),
        };
        Assert.Equal((1, 2), GameMediaManifest.CountKinds(cache));
        Assert.Equal((0, 0), GameMediaManifest.CountKinds(null));
        Assert.Equal("b-2.mp4", GameMediaManifest.NameFor(cache[1]));
    }
}
