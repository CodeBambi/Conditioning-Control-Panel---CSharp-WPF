using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The page ORIGIN: WPF's <c>https://ccp.*</c> virtual hosts on WebView2, a stable loopback port
/// elsewhere, and one resolver (one set of rules) behind both.
/// </summary>
public sealed class WebOriginTests
{
    sealed record Site(string Dir, string Web, string Assets, string Audio);

    static Site MakeSite()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-origin-").FullName;
        var web = Path.Combine(dir, "web");
        var assets = Path.Combine(dir, "assets");
        var audio = Path.Combine(dir, "audio");
        Directory.CreateDirectory(Path.Combine(web, "sub"));
        Directory.CreateDirectory(Path.Combine(assets, ".packs"));
        Directory.CreateDirectory(audio);
        File.WriteAllText(Path.Combine(web, "sub", "index.html"), "<p>index</p>");
        File.WriteAllText(Path.Combine(web, "data.json"), "{\"ok\":true}");
        File.WriteAllText(Path.Combine(web, "w.js"), "postMessage('worker-ok');");
        File.WriteAllText(Path.Combine(dir, "secret.txt"), "outside");
        File.WriteAllBytes(Path.Combine(assets, "pic.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        File.WriteAllText(Path.Combine(assets, "notes.txt"), "not media");
        File.WriteAllBytes(Path.Combine(assets, ".packs", "hidden.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(audio, "clip.mp3"), new byte[] { 1, 2, 3 });
        File.WriteAllText(Path.Combine(audio, "clip.txt"), "no");
        return new Site(dir, web, assets, audio);
    }

    static WebAssetServer Server(Site site, bool virtualHosts, Func<string?>? subAudio = null)
    {
        var s = new WebAssetServer(site.Web, 0, virtualHosts) { AssetsRoot = () => site.Assets, DisabledAssets = () => null };
        s.Hosts["ccp.subaudio"] = subAudio ?? (() => site.Audio);
        return s;
    }

    [Fact]
    public void VirtualHosts_HandOutWpfsUrls()
    {
        using var s = Server(MakeSite(), virtualHosts: true);
        Assert.Equal("https://ccp.game/arcademy/index.html", s.Url("arcademy/index.html"));
        Assert.Equal("https://ccp.game/goon/index.html?mode=duel", s.Url("/goon/index.html", "mode=duel"));
        Assert.Equal("https://ccp.assets/my%20pics/a.png", s.AssetUrl(Path.Combine("my pics", "a.png")));
        Assert.Equal("https://ccp.subaudio/good%20girl.mp3", s.HostUrl("ccp.subaudio", "good girl.mp3"));
        Assert.Equal("https://ccp.mod/", s.ModUrlBase);
        Assert.Equal("https://ccp.cache/", s.CacheUrlBase);
    }

    [Fact]
    public void Loopback_KeepsTheTokenFirst_AndCarriesTheQuery()
    {
        using var s = Server(MakeSite(), virtualHosts: false);
        Assert.Equal($"http://127.0.0.1:{s.Port}/goon/index.html?ccp_t={s.Token}&mode=duel", s.Url("goon/index.html", "mode=duel"));
        Assert.Equal($"http://127.0.0.1:{s.Port}/ccp.assets/a.png", s.AssetUrl("a.png"));
    }

    [Fact]
    public void AVirtualUrl_HasALoopbackTwin_AndBack()
    {
        using var s = Server(MakeSite(), virtualHosts: true);
        var page = new Uri("https://ccp.game/goon/index.html?mode=duel#top");
        var twin = s.ToLoopback(page);
        Assert.Equal($"http://127.0.0.1:{s.Port}/goon/index.html?ccp_t={s.Token}&mode=duel#top", twin.AbsoluteUri);
        Assert.Equal(page, s.ToVirtual(twin));
        var asset = new Uri("https://ccp.assets/my%20pics/a.png");
        Assert.Equal($"http://127.0.0.1:{s.Port}/ccp.assets/my%20pics/a.png?ccp_t={s.Token}", s.ToLoopback(asset).AbsoluteUri);
        Assert.Equal(asset, s.ToVirtual(s.ToLoopback(asset)));
        // Not ours: untouched both ways.
        var site = new Uri("https://hypnotube.com/x");
        Assert.Same(site, s.ToLoopback(site));
        Assert.Same(site, s.ToVirtual(site));
        Assert.False(s.IsVirtual(new Uri("http://ccp.game/x")));
        Assert.False(s.IsVirtual(new Uri("https://ccp.game:8443/x")));
        Assert.False(s.IsVirtual(new Uri("https://ccp.evil.example/x")));
    }

    [Fact]
    public void TheVirtualHosts_KeepEveryRuleOfTheLoopbackServer()
    {
        var site = MakeSite();
        using var s = Server(site, virtualHosts: true);
        string? R(string url) => s.ResolveVirtual(new Uri(url), out _, out _);

        Assert.Equal(Path.Combine(site.Web, "data.json"), R("https://ccp.game/data.json"));
        Assert.Equal(Path.Combine(site.Web, "sub", "index.html"), R("https://ccp.game/sub/"));
        Assert.Null(R("https://ccp.game/missing.js"));
        // No traversal out of a root, plain or escaped.
        Assert.Null(R("https://ccp.game/../secret.txt"));
        Assert.Null(R("https://ccp.game/sub/%2e%2e/%2e%2e/secret.txt"));
        Assert.Null(R("https://ccp.game/sub/..%5c..%5csecret.txt"));
        // The page root is not a second door to a media host.
        Assert.Null(R("https://ccp.game/ccp.assets/pic.png"));
        // Media only on the media hosts, never a dot-folder.
        Assert.Equal(Path.Combine(site.Assets, "pic.png"), R("https://ccp.assets/pic.png"));
        Assert.Null(R("https://ccp.assets/notes.txt"));
        Assert.Null(R("https://ccp.assets/.packs/hidden.png"));
        Assert.Equal(Path.Combine(site.Audio, "clip.mp3"), R("https://ccp.subaudio/clip.mp3"));
        Assert.Null(R("https://ccp.subaudio/clip.txt"));
        // Only https, only our names.
        Assert.Null(R("http://ccp.game/data.json"));
        Assert.Null(R("https://ccp.other/data.json"));
        Assert.Null(R("https://ccp.mod/x.png"));   // no mod root set

        s.ResolveVirtual(new Uri("https://ccp.assets/pic.png"), out var host, out var cors);
        Assert.Equal("ccp.assets", host);
        Assert.True(cors);   // WPF maps the media hosts Allow
        s.ResolveVirtual(new Uri("https://ccp.game/data.json"), out _, out cors);
        Assert.False(cors);  // and the page root Deny
    }

    [Fact]
    public void ARefusedAudioFolder_IsNotServed()
    {
        var site = MakeSite();
        bool allowed = false;   // CCP Default / Locked: the Bambi sub_audio folder is refused
        using var s = Server(site, virtualHosts: true, subAudio: () => allowed ? site.Audio : null);
        Assert.Null(s.ResolveVirtual(new Uri("https://ccp.subaudio/clip.mp3"), out _, out _));
        allowed = true;         // a mod switch moves it, read per request
        Assert.NotNull(s.ResolveVirtual(new Uri("https://ccp.subaudio/clip.mp3"), out _, out _));
    }

    [Fact]
    public void ARangeHeader_NamesTheBytesToSend()
    {
        Assert.Equal((2L, 5L), WebView2Hosts.ParseRange("bytes=2-5", 100));
        Assert.Equal((10L, 99L), WebView2Hosts.ParseRange("bytes=10-", 100));
        Assert.Equal((90L, 99L), WebView2Hosts.ParseRange("bytes=-10", 100));
        Assert.Equal((0L, 99L), WebView2Hosts.ParseRange("bytes=0-5000", 100));
        Assert.Equal((0L, 15L), WebView2Hosts.ParseRange("bytes=0-", 100, chunk: 16));   // one piece at a time
        // No range, or one this gate does not split: the whole file goes.
        Assert.Null(WebView2Hosts.ParseRange(null, 100));
        Assert.Null(WebView2Hosts.ParseRange("bytes=0-1,5-6", 100));
        Assert.Null(WebView2Hosts.ParseRange("bytes=200-", 100));
        Assert.Null(WebView2Hosts.ParseRange("bytes=5-2", 100));
        Assert.Null(WebView2Hosts.ParseRange("items=0-1", 100));
    }

    [Fact]
    public void TheLoopbackPort_IsChosenOncePerUserDataFolder()
    {
        var site = MakeSite();
        int port = WebAssetServer.StablePort.For(site.Dir);
        Assert.InRange(port, WebAssetServer.StablePort.Low, WebAssetServer.StablePort.High);
        Assert.Equal(port, WebAssetServer.StablePort.For(site.Dir));
        Assert.Equal(port.ToString(), File.ReadAllText(Path.Combine(site.Dir, WebAssetServer.StablePort.FileName)));

        using var first = new WebAssetServer(site.Web, port);
        Assert.True(first.PortIsStable);
        Assert.Equal(port, first.Port);
        // Another program holds the port: this run still serves, on a temporary origin, and the
        // remembered port is left alone for the next run.
        using var second = new WebAssetServer(site.Web, port);
        Assert.False(second.PortIsStable);
        Assert.NotEqual(port, second.Port);
        Assert.Equal(port, WebAssetServer.StablePort.For(site.Dir));
    }

    [Fact]
    public async Task AWebHostWithoutTheHosts_LoadsTheLoopbackTwin_AndTheCallerStillSeesItsUrl() =>
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            using var s = Server(MakeSite(), virtualHosts: true);
            var page = new Uri(s.Url("sub/index.html", "a=1"));
            Assert.Equal("https://ccp.game/sub/index.html?a=1", page.AbsoluteUri);
            var web = new WebHost { AppServer = s };
            // No engine yet: an app page is held, never handed over early.
            Assert.Equal(WebHost.AppHostState.Waiting, web.AppHosts);
            Assert.Null(web.EngineUrl(page));
            // A site is never held or rewritten.
            var site = new Uri("https://hypnotube.com/");
            Assert.Same(site, web.EngineUrl(site));

            web.OnAdapterCreated(IntPtr.Zero);   // WebKitGTK, or a WebView2 the hosts could not be put on
            Assert.Equal(WebHost.AppHostState.Loopback, web.AppHosts);
            Assert.False(s.VirtualHosts);        // demoted: every later url is loopback
            Assert.StartsWith($"http://127.0.0.1:{s.Port}/", s.Url("x.html"));
            var engine = web.EngineUrl(page)!;
            Assert.Equal($"http://127.0.0.1:{s.Port}/sub/index.html?ccp_t={s.Token}&a=1", engine.AbsoluteUri);

            // What the engine reports reads as the url the caller asked for (its same-origin guard holds).
            Uri? seen = null;
            web.NavigationCompleted += u => seen = u;
            web.OnNavigationCompleted(engine);
            Assert.Equal(page, seen);
            Assert.Equal(page, web.CurrentUrl);
            return Task.CompletedTask;
        });

    [Fact]
    public async Task AWebHostWithTheHosts_LoadsWpfsUrlAsIs() =>
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            using var s = Server(MakeSite(), virtualHosts: true);
            var page = new Uri(s.Url("sub/index.html"));
            int installs = 0;
            var web = new WebHost { AppServer = s, InstallHosts = (_, _) => { installs++; return new Noop(); } };
            web.OnAdapterCreated(new IntPtr(1));
            Assert.Equal(1, installs);
            Assert.Equal(WebHost.AppHostState.Installed, web.AppHosts);
            Assert.True(s.VirtualHosts);
            Assert.Same(page, web.EngineUrl(page));
            return Task.CompletedTask;
        });

    sealed class Noop : IDisposable { public void Dispose() { } }

    // ---- the real thing: a windowless WebView2 with the hosts installed ----------------------

    const string Probe = @"<!doctype html><script>
(async () => {
  const r = { origin: location.origin, secure: window.isSecureContext };
  const st = (u) => fetch(u).then(x => x.status, () => -1);
  try { r.before = localStorage.getItem('k'); localStorage.setItem('k', 'kept'); } catch (e) { r.before = 'ERR ' + e; }
  r.data = await st('/data.json');
  r.range = await fetch('/data.json', { headers: { Range: 'bytes=2-5' } }).then(async x => x.status + ':' + await x.text(), () => 'ERR');
  r.index = await fetch('/sub/').then(x => x.text(), () => 'ERR');
  r.missing = await st('/missing.js');
  r.asset = await st('https://ccp.assets/pic.png');
  r.assetText = await st('https://ccp.assets/notes.txt');
  r.assetDot = await st('https://ccp.assets/.packs/hidden.png');
  r.audio = await st('https://ccp.subaudio/clip.mp3');
  r.audioText = await st('https://ccp.subaudio/clip.txt');
  r.alias = await st('/ccp.assets/pic.png');
  r.stranger = await st('https://ccp.other/x.png');
  r.worker = await new Promise(res => { try { const w = new Worker('/w.js'); w.onmessage = e => res(e.data); w.onerror = () => res('ERR'); } catch (e) { res('THROW'); } });
  window.chrome.webview.postMessage(JSON.stringify(r));
})();
</script>";

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    delegate int CreateEnvironment([MarshalAs(UnmanagedType.LPWStr)] string? browserFolder, [MarshalAs(UnmanagedType.LPWStr)] string userDataFolder, IntPtr options, IntPtr handler);

    [StructLayout(LayoutKind.Sequential)]
    struct Msg { public IntPtr Hwnd; public uint Message; public IntPtr W, L; public uint Time; public int X, Y; public uint Extra; }
    [DllImport("user32.dll")] static extern bool PeekMessageW(out Msg msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessageW(ref Msg msg);

    static string? FindLoader()
    {
        var arch = RuntimeInformation.ProcessArchitecture switch { Architecture.X64 => "win-x64", Architecture.Arm64 => "win-arm64", Architecture.X86 => "win-x86", _ => null };
        if (arch == null) return null;
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrEmpty(packages)) packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var dir = Path.Combine(packages, "microsoft.web.webview2");
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateDirectories(dir).Select(v => Path.Combine(v, "runtimes", arch, "native", "WebView2Loader.dll")).LastOrDefault(File.Exists);
    }

    /// <summary>One browser session over <paramref name="profile"/>: loads the probe page on
    /// <c>https://ccp.game</c> and returns what it posted. Null result + reason = could not start.</summary>
    static (JObject? Result, string? Skip, int Refused, int Served) RunSession(string loader, string profile, WebAssetServer server)
    {
        JObject? result = null; string? skip = null; int refused = 0, served = 0;
        var thread = new Thread(() =>
        {
            IntPtr env = IntPtr.Zero, controller = IntPtr.Zero, core = IntPtr.Zero;
            WebView2Hosts? hosts = null;
            string? posted = null;
            var message = new WebView2Hosts.Callback(new Guid("57213f19-00e6-49fa-8e07-898ea01ecbd2"), (_, args) =>
            {
                WebView2Hosts.Fn<WebView2Hosts.GetPtr>(args, WebView2Hosts.MessageArgs_TryGetString)(args, out var p);
                posted = WebView2Hosts.TakeString(p) ?? "";
                return 0;
            });
            var controllerDone = new WebView2Hosts.Callback(new Guid("6c4819f3-c9b7-4260-8127-c9f5bde7f68c"), (hr, created) =>
            {
                if (unchecked((int)hr.ToInt64()) < 0 || created == IntPtr.Zero) { skip = "WebView2 controller failed 0x" + unchecked((int)hr.ToInt64()).ToString("X8"); return 0; }
                controller = created; Marshal.AddRef(created);
                WebView2Hosts.Check(WebView2Hosts.Fn<WebView2Hosts.GetPtr>(created, WebView2Hosts.Controller_GetCoreWebView2)(created, out core), "get_CoreWebView2");
                hosts = WebView2Hosts.TryInstall(core, server);
                if (hosts == null) { posted = "{\"install\":false}"; return 0; }
                WebView2Hosts.Check(WebView2Hosts.Fn<WebView2Hosts.AddHandler>(core, WebView2Hosts.Core_AddWebMessageReceived)(core, message.Pointer, out _), "add_WebMessageReceived");
                WebView2Hosts.Check(WebView2Hosts.Fn<WebView2Hosts.StrArg>(core, WebView2Hosts.Core_Navigate)(core, server.Url("probe.html")), "Navigate");
                return 0;
            });
            var environmentDone = new WebView2Hosts.Callback(new Guid("4e8a3389-c9d8-4bd2-b6b5-124fee6cc14d"), (hr, created) =>
            {
                if (unchecked((int)hr.ToInt64()) < 0 || created == IntPtr.Zero) { skip = "WebView2 environment failed 0x" + unchecked((int)hr.ToInt64()).ToString("X8"); return 0; }
                env = created; Marshal.AddRef(created);
                int chr = WebView2Hosts.Fn<WebView2Hosts.CreateController>(created, WebView2Hosts.Env_CreateController)(created, new IntPtr(-3) /* HWND_MESSAGE */, controllerDone.Pointer);
                if (chr < 0) skip = "CreateCoreWebView2Controller failed 0x" + chr.ToString("X8");
                return 0;
            });
            try
            {
                var lib = NativeLibrary.Load(loader);
                var create = Marshal.GetDelegateForFunctionPointer<CreateEnvironment>(NativeLibrary.GetExport(lib, "CreateCoreWebView2EnvironmentWithOptions"));
                int hr = create(null, profile, IntPtr.Zero, environmentDone.Pointer);
                if (hr < 0) { skip = "no WebView2 runtime (0x" + hr.ToString("X8") + ")"; return; }
                var until = DateTime.UtcNow.AddSeconds(60);
                while (posted == null && skip == null && DateTime.UtcNow < until)
                {
                    while (PeekMessageW(out var m, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref m); DispatchMessageW(ref m); }
                    Thread.Sleep(5);
                }
                if (posted == null && skip == null) skip = "WebView2 did not answer in 60 s";
                if (posted != null) result = JObject.Parse(posted);
                if (hosts != null) { refused = hosts.Refused; served = hosts.Served; skip ??= hosts.LastError; }
            }
            catch (Exception ex) { skip = "WebView2 unavailable: " + ex.Message; }
            finally
            {
                try
                {
                    hosts?.Dispose();
                    if (controller != IntPtr.Zero) { WebView2Hosts.Fn<WebView2Hosts.NoArgs>(controller, WebView2Hosts.Controller_Close)(controller); Marshal.Release(controller); }
                    if (core != IntPtr.Zero) Marshal.Release(core);
                    if (env != IntPtr.Zero) Marshal.Release(env);
                    var drain = DateTime.UtcNow.AddMilliseconds(500);
                    while (DateTime.UtcNow < drain) { while (PeekMessageW(out var m, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref m); DispatchMessageW(ref m); } Thread.Sleep(5); }
                }
                catch (Exception) { }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(90))) skip ??= "WebView2 session hung";
        return (result, skip, refused, served);
    }

    /// <summary>
    /// End to end on a real (windowless) WebView2: the page's origin is WPF's, its storage is still
    /// there in the next session, an allowed file loads, and a file that exists on disk but the
    /// rules refuse does not. (This test is also what showed that a folder MAPPING cannot be gated:
    /// with SetVirtualHostNameToFolderMapping in place the refused files below all came back 200.)
    /// </summary>
    [Fact]
    public void OnWebView2_ThePageHasWpfsOrigin_KeepsItsStorage_AndTheGateHolds()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("WebView2 is Windows only; Linux pages use the stable loopback origin.");
        var loader = FindLoader();
        if (loader == null) Assert.Skip("WebView2Loader.dll is not in the NuGet cache (the port itself does not need it; this test does).");

        var site = MakeSite();
        File.WriteAllText(Path.Combine(site.Web, "probe.html"), Probe);
        using var server = Server(site, virtualHosts: true);
        var profile = Path.Combine(site.Dir, "profile");

        var first = RunSession(loader!, profile, server);
        if (first.Result == null) Assert.Skip(first.Skip ?? "no WebView2 session");
        var r = first.Result!;
        Assert.NotEqual(false, (bool?)r["install"]);
        Assert.Equal("https://ccp.game", (string?)r["origin"]);
        Assert.True((bool)r["secure"]!);
        Assert.Equal(JTokenType.Null, r["before"]!.Type);
        Assert.Equal(200, (int)r["data"]!);
        Assert.Equal("206:ok\":", (string?)r["range"]);              // a seek gets its bytes
        Assert.Equal("<p>index</p>", (string?)r["index"]);
        Assert.Equal(404, (int)r["missing"]!);
        Assert.Equal(200, (int)r["asset"]!);                         // cross-origin media: WPF's Allow
        Assert.NotEqual(200, (int)r["assetText"]!);                  // on disk, in the assets folder, refused by the rules
        Assert.NotEqual(200, (int)r["assetDot"]!);
        Assert.Equal(200, (int)r["audio"]!);
        Assert.NotEqual(200, (int)r["audioText"]!);
        Assert.Equal(404, (int)r["alias"]!);
        Assert.NotEqual(200, (int)r["stranger"]!);                   // a look-alike host never loads
        Assert.Equal("worker-ok", (string?)r["worker"]);             // a dedicated worker's script is served too
        Assert.True(first.Refused >= 6, $"refused {first.Refused}");
        Assert.True(first.Served >= 7, $"served {first.Served}");    // probe.html, data.json x2, sub/, pic.png, clip.mp3, w.js
        Assert.Null(first.Skip);                                     // the gate never failed to answer

        // A restart: same profile folder, same origin, the page's storage is still there.
        var second = RunSession(loader!, profile, server);
        if (second.Result == null) Assert.Skip(second.Skip ?? "no second WebView2 session");
        Assert.Equal("https://ccp.game", (string?)second.Result!["origin"]);
        Assert.Equal("kept", (string?)second.Result!["before"]);
    }
}
