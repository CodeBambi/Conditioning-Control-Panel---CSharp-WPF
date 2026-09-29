using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CCP.Core.Content.Tests;

/// <summary>Sandboxes the profile before CorePaths (immutable per process) can resolve.</summary>
internal static class Profile
{
    internal static readonly string Root = Path.Combine(Path.GetTempPath(), "ccp-content-tests-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", Root);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(Root, true); } catch { } };
    }

    internal static readonly Lazy<SettingsService> Settings = new(() => new SettingsService());
}

/// <summary>Counts requests and refuses (and records) any that is not for a loopback host.</summary>
internal sealed class LoopbackOnlyHandler : DelegatingHandler
{
    public int Count;
    public readonly ConcurrentQueue<Uri> Violations = new();
    public LoopbackOnlyHandler() : base(new SocketsHttpHandler()) { }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri is not { IsLoopback: true })
        {
            Violations.Enqueue(request.RequestUri!);
            throw new InvalidOperationException("non-loopback request: " + request.RequestUri);
        }
        Interlocked.Increment(ref Count);
        return base.SendAsync(request, ct);
    }
}

/// <summary>HttpListener on 127.0.0.1 standing in for the GitHub release assets.</summary>
internal sealed class FakeServer : IDisposable
{
    private readonly HttpListener _listener = new();
    public readonly string Prefix;
    public readonly ConcurrentQueue<(string Path, string? Range)> Requests = new();
    public Func<HttpListenerContext, Task> Handle = c => Send(c, Array.Empty<byte>(), 404);

    public FakeServer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Prefix = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(Prefix);
        _listener.Start();
        _ = Task.Run(Loop);
    }

    private async Task Loop()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext c;
            try { c = await _listener.GetContextAsync(); } catch { return; }
            Requests.Enqueue((c.Request.Url!.AbsolutePath, c.Request.Headers["Range"]));
            try { await Handle(c); } catch { try { c.Response.Abort(); } catch { } }
        }
    }

    public static async Task Send(HttpListenerContext c, byte[] body, int status = 200)
    {
        c.Response.StatusCode = status;
        c.Response.ContentLength64 = body.Length;
        await c.Response.OutputStream.WriteAsync(body);
        c.Response.Close();
    }

    public int PackGets => Requests.Count(r => r.Path.EndsWith(".zip", StringComparison.Ordinal));

    public void Dispose() { try { _listener.Stop(); _listener.Close(); } catch { } }
}

public sealed class ReleaseContentServiceTests : IDisposable
{
    private const string PackId = "mod-bambi";
    private const string ZipName = "mod-bambi.zip";
    private const string PayloadRel = "Resources/sounds/test/clip.mp3";

    private readonly FakeServer _server = new();
    private readonly LoopbackOnlyHandler _handler = new();
    private readonly SettingsService _settings = Profile.Settings.Value;
    private readonly ReleaseContentService _svc;
    private readonly byte[] _zip = BuildZip();
    private static string ContentDir => Path.Combine(CorePaths.UserData, "content");

    public ReleaseContentServiceTests()
    {
        CoreSettings.ServiceProvider = () => _settings;
        CoreReleaseContent.AppVersionProvider = () => "6.6.3";   // cycle v6.6.0, previous v6.5.0
        _settings.Current.InstalledContentPacks.Clear();
        _settings.Current.OfflineMode = false;
        _settings.Current.PendingModActivationId = "";
        if (Directory.Exists(ContentDir)) Directory.Delete(ContentDir, true);
        _svc = new ReleaseContentService(_server.Prefix + "{0}/", _handler);
    }

    public void Dispose()
    {
        _svc.Dispose();
        _server.Dispose();
        Assert.Empty(_handler.Violations);
    }

    private static byte[] BuildZip()
    {
        var payload = new byte[64 * 1024];
        new Random(42).NextBytes(payload);   // incompressible, so the zip is big enough to cut mid-way
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var s = zip.CreateEntry(PayloadRel, CompressionLevel.NoCompression).Open();
            s.Write(payload);
        }
        return ms.ToArray();
    }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private string Manifest(long? size = null, string targetRoot = "") =>
        "{\"packs\":[{\"id\":\"" + PackId + "\",\"file\":\"" + ZipName + "\",\"sizeBytes\":" + (size ?? _zip.Length) +
        ",\"sha256\":\"" + Sha(_zip) + "\",\"contentVersion\":3,\"targetRoot\":\"" + targetRoot + "\"}]}";

    /// <summary>Serves the manifest on <paramref name="tag"/> and hands zip requests to <paramref name="zip"/>.</summary>
    private void Serve(Func<HttpListenerContext, Task> zip, string tag = "v6.6.0", string? manifest = null)
    {
        var m = System.Text.Encoding.UTF8.GetBytes(manifest ?? Manifest());
        _server.Handle = c =>
        {
            var path = c.Request.Url!.AbsolutePath;
            if (path == $"/{tag}/content-manifest.json") return FakeServer.Send(c, m);
            if (path == $"/{tag}/{ZipName}") return zip(c);
            return FakeServer.Send(c, Array.Empty<byte>(), 404);
        };
    }

    private Task ServeZip(HttpListenerContext c) => FakeServer.Send(c, _zip);

    private async Task Ranged(HttpListenerContext c)
    {
        var range = c.Request.Headers["Range"];
        if (range == null) { await ServeZip(c); return; }
        var from = int.Parse(range["bytes=".Length..].TrimEnd('-'));
        c.Response.AddHeader("Content-Range", $"bytes {from}-{_zip.Length - 1}/{_zip.Length}");
        await FakeServer.Send(c, _zip[from..], 206);
    }

    /// <summary>Headers promise the whole zip, the body stops at <paramref name="n"/> bytes.</summary>
    private async Task Drop(HttpListenerContext c, int n)
    {
        c.Response.ContentLength64 = _zip.Length;
        await c.Response.OutputStream.WriteAsync(_zip.AsMemory(0, n));
        await c.Response.OutputStream.FlushAsync();
        await Task.Delay(300);
        c.Response.Abort();
    }

    private void AssertInstalled()
    {
        Assert.True(File.Exists(Path.Combine(ContentDir, PayloadRel)));
        var stamp = _svc.GetStamp(PackId);
        Assert.NotNull(stamp);
        Assert.Equal(3, stamp!.ContentVersion);
        Assert.Equal(Sha(_zip), stamp.Sha256);
    }

    private void AssertNothingInstalled()
    {
        Assert.Null(_svc.GetStamp(PackId));
        Assert.False(File.Exists(Path.Combine(ContentDir, PayloadRel)));
        Assert.False(File.Exists(Path.Combine(ContentDir, ".downloads", ZipName + ".partial")));
    }

    [Fact]
    public async Task HappyPath_DownloadsVerifiesInstallsStampsAndActivatesTheChosenMod()
    {
        Serve(ServeZip);
        _settings.Current.ActiveModId = BuiltInMods.CCPDefaultId;
        var mods = new ModService();
        mods.Initialize(BuiltInMods.CCPDefaultId);
        mods.AttachReleaseContent();

        var installed = new List<string>();
        _svc.PackInstalled += (_, id) => installed.Add(id);
        string? activated = null;
        // What the head's activator does (WPF: PendingModActivation.OnModAvailabilityChanged).
        mods.ModAvailabilityChanged += (_, id) =>
        {
            var pending = PendingModChoice.Pending;
            if (!PendingModChoice.Matches(pending, id)) return;
            if (!PendingModChoice.ShouldActivate(pending, mods.ActiveModId, PendingModChoice.IsContentAvailable(pending!, _svc))) return;
            mods.ActivateMod(pending!);
            PendingModChoice.Clear("activated");
            activated = pending;
        };

        PendingModChoice.Record(BuiltInMods.BambiSleepId, mods.ActiveModId);
        Assert.Equal(BuiltInMods.BambiSleepId, PendingModChoice.Pending);
        Assert.False(PendingModChoice.IsContentAvailable(BuiltInMods.BambiSleepId, _svc));

        Assert.True(await _svc.RequestPackAsync(PackId));

        AssertInstalled();
        Assert.Equal(new[] { PackId }, installed);
        Assert.Equal(BuiltInMods.BambiSleepId, activated);
        Assert.Equal(BuiltInMods.BambiSleepId, mods.ActiveModId);
        Assert.Null(PendingModChoice.Pending);
        Assert.Equal(1, _server.PackGets);
    }

    [Fact]
    public async Task ManifestMissingOnTheCurrentCycle_FallsBackToThePreviousOne()
    {
        Serve(ServeZip, tag: "v6.5.0");
        Assert.True(await _svc.RequestPackAsync(PackId));
        AssertInstalled();
        Assert.Contains(_server.Requests, r => r.Path == "/v6.6.0/content-manifest.json");
        Assert.Contains(_server.Requests, r => r.Path == "/v6.5.0/" + ZipName);
    }

    [Fact]
    public async Task ConnectionDroppedAtN_ResumesWithARangeRequest()
    {
        const int n = 20_000;
        var calls = 0;
        Serve(c => Interlocked.Increment(ref calls) == 1 ? Drop(c, n) : Ranged(c));
        Assert.True(await _svc.RequestPackAsync(PackId));
        AssertInstalled();
        var zips = _server.Requests.Where(r => r.Path.EndsWith(".zip")).ToList();
        Assert.Equal(2, zips.Count);
        Assert.Null(zips[0].Range);
        Assert.Equal($"bytes={n}-", zips[1].Range);
    }

    [Fact]
    public async Task RangeIgnoredByTheServer_RestartsFromZero()
    {
        var calls = 0;
        Serve(c => Interlocked.Increment(ref calls) == 1 ? Drop(c, 20_000) : ServeZip(c));
        Assert.True(await _svc.RequestPackAsync(PackId));
        AssertInstalled();
        // Appending the full 200 body onto the partial would fail the hash and cost a third GET.
        Assert.Equal(2, _server.PackGets);
        Assert.Equal("bytes=20000-", _server.Requests.Where(r => r.Path.EndsWith(".zip")).Last().Range);
    }

    [Fact]
    public async Task BadHashOnce_RedownloadsCleanlyAndSucceeds()
    {
        var calls = 0;
        var corrupt = _zip.ToArray();
        corrupt[^10] ^= 0xFF;
        Serve(c => FakeServer.Send(c, Interlocked.Increment(ref calls) == 1 ? corrupt : _zip));
        Assert.True(await _svc.RequestPackAsync(PackId));
        AssertInstalled();
        Assert.Equal(2, _server.PackGets);
        Assert.All(_server.Requests.Where(r => r.Path.EndsWith(".zip")), r => Assert.Null(r.Range));
    }

    [Fact]
    public async Task BadHashTwice_FailsAndLeavesNothingBehind()
    {
        var corrupt = _zip.ToArray();
        corrupt[^10] ^= 0xFF;
        Serve(c => FakeServer.Send(c, corrupt));
        Assert.False(await _svc.RequestPackAsync(PackId));
        AssertNothingInstalled();
        Assert.Equal(2, _server.PackGets);
    }

    [Fact]
    public async Task Offline_MakesNoRequest_AndIsNotCached()
    {
        Serve(ServeZip);
        _settings.Current.OfflineMode = true;
        Assert.False(await _svc.RequestPackAsync(PackId));
        Assert.Equal(0, _handler.Count);

        _settings.Current.OfflineMode = false;
        Assert.True(await _svc.RequestPackAsync(PackId));
        AssertInstalled();
    }

    [Fact]
    public async Task OversizedPack_IsNeverFetched()
    {
        Serve(ServeZip, manifest: Manifest(size: long.MaxValue / 2));
        Assert.False(await _svc.RequestPackAsync(PackId));
        Assert.Equal(0, _server.PackGets);
        AssertNothingInstalled();
    }

    [Fact]
    public async Task EscapingTargetRoot_IsRefused()
    {
        Serve(ServeZip, manifest: Manifest(targetRoot: "../x"));
        Assert.False(await _svc.RequestPackAsync(PackId));
        AssertNothingInstalled();
        Assert.False(Directory.Exists(Path.Combine(CorePaths.UserData, "x")));
    }

    [Theory]
    [InlineData("http://127.0.0.1:9/", true)]
    [InlineData("http://localhost:9", true)]
    [InlineData("http://[::1]:9/", true)]
    [InlineData("http://127.0.0.1:9/base/?q=1#f", true)]
    [InlineData("http://evil@127.0.0.1:9/", false)]
    [InlineData("http://127.0.0.1@evil.com/", false)]
    [InlineData("http://localhost.evil.com/", false)]
    [InlineData("https://evil.example/", false)]
    [InlineData("file:///tmp/", false)]
    [InlineData(null, false)]
    public void ContentBaseUrlOverride_IsHonouredOnlyForLoopback(string? url, bool honoured)
    {
        var format = ReleaseContentService.ResolveBaseUrlFormat(url);
        Assert.Equal(honoured, !format.StartsWith("https://github.com/", StringComparison.Ordinal));
        if (honoured) Assert.Equal(new Uri(url!).GetLeftPart(UriPartial.Path).TrimEnd('/') + "/{0}/", format);
    }

    [Fact]
    public void FreeSpace_IsMeasuredOnTheLongestMountHoldingTheContentRoot()
    {
        var mounts = new[] { "/", "/home", "/home/u/.local/share/other", "/ho" };
        Assert.Equal("/home", ReleaseContentService.MountFor("/home/u/.local/share/CCP/content", mounts));
        Assert.Equal("/", ReleaseContentService.MountFor("/var/x", mounts));
        Assert.Equal("/home", ReleaseContentService.MountFor("/home", mounts));
    }
}
