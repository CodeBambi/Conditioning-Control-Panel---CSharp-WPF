using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Remix;

/// <summary>What one build produced: the gif on disk plus the numbers the spike is judged on.</summary>
public sealed record JackpotRemixResult(
    string FilePath, long Bytes, int Frames, int Width, int Height, string Code, string Layout, long SeedUsed,
    JackpotRemixTimings Timings);

/// <summary>Page-side phases in ms, plus the host's wall time from BuildAsync to the file on disk.</summary>
public sealed record JackpotRemixTimings(double FetchMs, double DecodeMs, double RollMs, double EncodeMs, double PageTotalMs, double HostTotalMs);

/// <summary>
/// Hidden WebView2 host that runs the vendored Remix Room engine (Resources/web/remix/jackpot.html)
/// and turns eight of the user's gifs into one composite gif for the Jackpot Remix flash.
///
/// Modelled on <see cref="Chaos.DtrhHostService"/>'s host: the same virtual hosts (ccp.remix for the
/// page, ccp.assets for the user's library so gifs load by url with no copy) and the same
/// queue-until-ready message bridge, but the window is a 2x2 tool window parked off screen with
/// WS_EX_NOACTIVATE, never activated and never in Alt-Tab. It is created lazily on the UI thread
/// by the first build, owned by the main window when there is one (so it closes with it and never
/// holds OnLastWindowClose open), and disposed on app exit.
///
/// One build at a time. A WebView2 process failure fails the running build with null, tears the
/// host down and logs once; the next build recreates it. A second failure in the same app session
/// switches the builder off for good: a flash that never arrives is better than a browser that
/// keeps crashing behind the user's back.
///
/// Does NOT roll the 1-in-100, show anything or pay XP. That is J2, after the spike is reviewed.
/// </summary>
internal sealed class JackpotRemixBuilder : IDisposable
{
    public sealed class Options
    {
        /// <summary>Folder holding <c>remix/jackpot.html</c> (the app's Resources/web).</summary>
        public required string WebRoot { get; init; }
        /// <summary>The user's media library; gifs must live under it.</summary>
        public required string AssetsRoot { get; init; }
        /// <summary>WebView2 user data folder. Keep the browser argument string constant per folder.</summary>
        public required string UserDataFolder { get; init; }
        /// <summary>Where finished gifs go; bounded to <see cref="JackpotRemixPlan.KeepFiles"/>.</summary>
        public required string OutputFolder { get; init; }
        /// <summary>The dispatcher the window lives on. Null = Application.Current's.</summary>
        public Dispatcher? Dispatcher { get; init; }
        /// <summary>Loop length in seconds (the room offers 3, 5, 8). 3 keeps 45 frames at 480x270
        /// inside FlashService's 60-frame / 30 MB budget, so every frame the page drew gets shown.</summary>
        public double Seconds { get; init; } = 3;
        /// <summary>Output scale over the engine's 480x270 (1 = native, 2 = 960x540). Strips decode at
        /// the scaled size, so memory grows with the square of this.</summary>
        public double Scale { get; init; } = 1;
        public TimeSpan BuildTimeout { get; init; } = TimeSpan.FromSeconds(120);
        /// <summary>Per-source byte cap; the plan's default. The spike harness lifts it to measure
        /// what an uncapped build costs. Nothing in the app should.</summary>
        public long MaxSourceBytes { get; init; } = JackpotRemixPlan.MaxSourceBytes;
        /// <summary>Drop the browser after every build (default). A jackpot is one build per hundred
        /// flashes, and a cold start costs about 2.5 s against a resident browser tree the spike
        /// measured at a gigabyte and more. The spike harness keeps a warm host to measure it.</summary>
        public bool ReleaseHostAfterBuild { get; init; } = true;
        /// <summary>Measurement only: walk seeds up from the given one until the roll lands on this
        /// layout id (engine/layout.js LAYOUTS), so a dear layout can be timed on purpose.</summary>
        public string? WantLayout { get; init; }
        public string LogTag { get; init; } = "JackpotRemix";
    }

    private const string PageHost = "ccp.remix";
    private const string BrowserArgs =
        "--disable-direct-composition-video-overlays "
        + "--disable-features=CalculateNativeWinOcclusion "
        + "--disable-background-timer-throttling --disable-renderer-backgrounding";

    private readonly Options _opts;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly List<string> _pending = new();
    private Dispatcher? _dispatcher;
    private Window? _window;
    private WebView2? _web;
    private Task? _init;
    private bool _ready;
    private bool _disposed;
    private int _failures;
    private int _seq;
    private TaskCompletionSource<JObject?>? _inFlight;
    private string? _inFlightId;

    public JackpotRemixBuilder(Options opts) => _opts = opts;

    /// <summary>The browser process id once the page is up (for the spike's memory sampling).</summary>
    public uint BrowserProcessId { get; private set; }

    /// <summary>Two failures and the builder stays off for the session.</summary>
    public bool Disabled => _failures >= 2;

    /* ------------------------------------------------------------- default --*/

    private static JackpotRemixBuilder? _default;
    private static readonly object DefaultGate = new();

    /// <summary>The app-wide instance, wired to the app's folders. Created on first use.</summary>
    public static JackpotRemixBuilder Default
    {
        get
        {
            lock (DefaultGate)
            {
                return _default ??= new JackpotRemixBuilder(new Options
                {
                    WebRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web"),
                    AssetsRoot = App.EffectiveAssetsPath,
                    UserDataFolder = Path.Combine(App.UserDataPath, "browser_data_remix"),
                    OutputFolder = Path.Combine(App.UserDataPath, "cache", "remix"),
                });
            }
        }
    }

    /// <summary>App exit: drop the browser process if it was ever started. Safe when it never was.</summary>
    public static void DisposeDefault()
    {
        JackpotRemixBuilder? d;
        lock (DefaultGate) { d = _default; _default = null; }
        d?.Dispose();
    }

    /* --------------------------------------------------------------- build --*/

    /// <summary>
    /// Build one composite from <paramref name="gifPaths"/> (under the assets root; over-cap and
    /// missing files are skipped, fewer than eight cycle). Null when there is nothing to build,
    /// the host is unavailable, the page failed, or the token cancelled. Callable from any thread.
    /// </summary>
    public async Task<JackpotRemixResult?> BuildAsync(IReadOnlyList<string> gifPaths, int seed, CancellationToken ct)
    {
        if (_disposed || Disabled) return null;
        var usable = JackpotRemixPlan.FilterBySize(gifPaths ?? Array.Empty<string>(), SizeOf, _opts.MaxSourceBytes);
        var media = new JArray();
        foreach (var p in JackpotRemixPlan.Cycle(usable))
        {
            var url = JackpotRemixPlan.ToAssetUrl(_opts.AssetsRoot, p);
            if (url == null) continue;
            media.Add(new JObject { ["url"] = url, ["name"] = Path.GetFileName(p) });
        }
        if (media.Count == 0) return null;

        var sw = Stopwatch.StartNew();
        await _oneAtATime.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var disp = _dispatcher ?? _opts.Dispatcher ?? Application.Current?.Dispatcher;
            if (disp == null) { App.Logger?.Warning("{Tag}: no dispatcher, no host", _opts.LogTag); return null; }
            _dispatcher = disp;

            var id = "b" + Interlocked.Increment(ref _seq);
            var tcs = new TaskCompletionSource<JObject?>(TaskCreationOptions.RunContinuationsAsynchronously);
            await disp.InvokeAsync(() =>
            {
                _inFlight = tcs; _inFlightId = id;
                EnsureHost();
                Post(new JObject
                {
                    ["type"] = "build", ["id"] = id, ["media"] = media, ["seed"] = seed,
                    ["seconds"] = _opts.Seconds, ["scale"] = _opts.Scale, ["wantLayout"] = _opts.WantLayout,
                });
            });

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_opts.BuildTimeout);
            using var reg = timeout.Token.Register(() =>
            {
                try { disp.BeginInvoke(new Action(() => Post(new JObject { ["type"] = "cancel", ["id"] = id }))); } catch { }
                tcs.TrySetResult(null);
            });

            var reply = await tcs.Task.ConfigureAwait(false);
            if (ct.IsCancellationRequested) return null;
            if (reply == null)
            {
                if (timeout.IsCancellationRequested)
                    App.Logger?.Warning("{Tag}: build timed out after {S}s", _opts.LogTag, _opts.BuildTimeout.TotalSeconds);
                return null;
            }
            if (!(bool?)reply["ok"] ?? true)
            {
                App.Logger?.Warning("{Tag}: page failed the build: {E}", _opts.LogTag, (string?)reply["error"]);
                return null;
            }
            return WriteResult(reply, sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            App.Logger?.Warning("{Tag}: build failed: {E}", _opts.LogTag, ex.Message);
            return null;
        }
        finally
        {
            _inFlight = null; _inFlightId = null;
            if (_opts.ReleaseHostAfterBuild && !_disposed && _dispatcher != null)
            {
                try { await _dispatcher.InvokeAsync(() => Teardown("build done")); } catch (Exception ex) { Diag.Swallowed(ex); }
            }
            _oneAtATime.Release();
        }
    }

    private JackpotRemixResult? WriteResult(JObject reply, double hostMs)
    {
        var b64 = (string?)reply["base64"];
        if (string.IsNullOrEmpty(b64)) return null;
        var bytes = Convert.FromBase64String(b64);
        var code = (string?)reply["code"] ?? "0";
        Directory.CreateDirectory(_opts.OutputFolder);
        var path = Path.Combine(_opts.OutputFolder, JackpotRemixPlan.OutputName(code, DateTime.UtcNow));
        File.WriteAllBytes(path, bytes);
        JackpotRemixPlan.Bound(_opts.OutputFolder);
        var t = reply["timings"] as JObject;
        double Ms(string k) => (double?)t?[k] ?? 0;
        var timings = new JackpotRemixTimings(Ms("fetch"), Ms("decode"), Ms("roll"), Ms("encode"), Ms("total"), hostMs);
        App.Logger?.Information("{Tag}: built {Code} {W}x{H} {Frames}f {KB} KB in {Ms} ms (decode {D}, encode {E})",
            _opts.LogTag, code, (int?)reply["w"], (int?)reply["h"], (int?)reply["frames"], bytes.Length / 1024,
            (int)hostMs, (int)timings.DecodeMs, (int)timings.EncodeMs);
        return new JackpotRemixResult(path, bytes.Length, (int?)reply["frames"] ?? 0, (int?)reply["w"] ?? 0,
            (int?)reply["h"] ?? 0, code, (string?)reply["layout"] ?? "", (long?)reply["seedUsed"] ?? 0, timings);
    }

    private static long SizeOf(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : -1; } catch { return -1; }
    }

    /* ---------------------------------------------------------------- host --*/

    // UI thread only.
    private void EnsureHost()
    {
        if (_window != null || _disposed) return;
        _web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Black };
        _window = new Window
        {
            Title = "Jackpot Remix",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = false,     // WebView2 does not paint in a layered window
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            Topmost = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000, Top = -32000,    // off every screen; visible to Windows, never to the user
            Width = 2, Height = 2,
            Content = _web,
        };
        _window.SourceInitialized += (_, _) => ApplyPassiveExStyles(_window);
        try
        {
            var main = Application.Current?.MainWindow;
            if (main != null && main != _window && PresentationSource.FromVisual(main) != null) _window.Owner = main;
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
        _window.Closed += (_, _) => { if (!_disposed) Teardown("window closed"); };
        _window.Show();
        _init = InitWebAsync();
    }

    private async Task InitWebAsync()
    {
        try
        {
            Directory.CreateDirectory(_opts.UserDataFolder);
            var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = BrowserArgs };
            var env = await CoreWebView2Environment.CreateAsync(null, _opts.UserDataFolder, options).ConfigureAwait(true);
            if (_web == null || _disposed) return;
            await _web.EnsureCoreWebView2Async(env).ConfigureAwait(true);
            if (_web?.CoreWebView2 == null || _disposed) return;
            var core = _web.CoreWebView2;
            var s = core.Settings;
            s.AreDevToolsEnabled = false;
            s.AreDefaultContextMenusEnabled = false;
            s.IsStatusBarEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsZoomControlEnabled = false;
            s.IsBuiltInErrorPageEnabled = false;
            s.IsWebMessageEnabled = true;
            core.SetVirtualHostNameToFolderMapping(PageHost, _opts.WebRoot, CoreWebView2HostResourceAccessKind.Deny);
            if (Directory.Exists(_opts.AssetsRoot))
                core.SetVirtualHostNameToFolderMapping(JackpotRemixPlan.AssetsHost, _opts.AssetsRoot, CoreWebView2HostResourceAccessKind.Allow);
            else
                App.Logger?.Warning("{Tag}: assets root missing: {Dir}", _opts.LogTag, _opts.AssetsRoot);
            core.NavigationStarting += OnNavigationStarting;
            core.WebMessageReceived += OnWebMessageReceived;
            core.ProcessFailed += OnProcessFailed;
            BrowserProcessId = core.BrowserProcessId;
            core.Navigate("https://" + PageHost + "/remix/jackpot.html");
        }
        catch (Exception ex)
        {
            // No runtime (an old machine without the Evergreen WebView2), a broken profile, anything:
            // the build in flight gets null and the failure counts toward the switch-off.
            App.Logger?.Warning("{Tag}: WebView2 init failed: {E}", _opts.LogTag, ex.Message);
            Fail("init failed");
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u)
            && string.Equals(u.Host, PageHost, StringComparison.OrdinalIgnoreCase)) return;
        e.Cancel = true;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var o = JObject.Parse(e.WebMessageAsJson);
            switch ((string?)o["type"])
            {
                case "ready":
                    _ready = true;
                    foreach (var json in _pending) { try { _web?.CoreWebView2?.PostWebMessageAsJson(json); } catch (Exception ex) { Diag.Swallowed(ex); } }
                    _pending.Clear();
                    break;
                case "log":
                    App.Logger?.Debug("{Tag}[page]: {Msg}", _opts.LogTag, (string?)o["msg"]);
                    break;
                case "built":
                    if ((string?)o["id"] == _inFlightId) _inFlight?.TrySetResult(o);
                    break;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("{Tag}: bad page message: {E}", _opts.LogTag, ex.Message); }
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        App.Logger?.Warning("{Tag}: WebView2 process failed ({Kind})", _opts.LogTag, e.ProcessFailedKind);
        Fail("process failed");
    }

    // UI thread. Fails the build in flight, drops the host, counts the failure; logs the switch-off once.
    private void Fail(string why)
    {
        _failures++;
        _inFlight?.TrySetResult(null);
        Teardown(why);
        if (Disabled) App.Logger?.Warning("{Tag}: off for this session after a second failure ({Why})", _opts.LogTag, why);
    }

    private void Post(JObject msg)
    {
        var json = msg.ToString(Newtonsoft.Json.Formatting.None);
        if (_ready && _web?.CoreWebView2 != null)
        {
            try { _web.CoreWebView2.PostWebMessageAsJson(json); return; } catch (Exception ex) { Diag.Swallowed(ex); }
        }
        _pending.Add(json);
    }

    private void Teardown(string why)
    {
        App.Logger?.Debug("{Tag}: host down ({Why})", _opts.LogTag, why);
        _ready = false;
        _pending.Clear();
        BrowserProcessId = 0;
        try
        {
            if (_web?.CoreWebView2 != null)
            {
                _web.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                _web.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                _web.CoreWebView2.ProcessFailed -= OnProcessFailed;
            }
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
        try { _web?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
        var w = _window;
        _web = null; _window = null; _init = null;
        try { w?.Close(); } catch (Exception ex) { Diag.Swallowed(ex); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _inFlight?.TrySetResult(null);
        var disp = _dispatcher;
        if (disp == null || disp.CheckAccess()) Teardown("dispose");
        else
        {
            try { disp.Invoke(() => Teardown("dispose"), DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(5)); }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }
    }

    /* ----------------------------------------------------------- win32 -----*/

    private static void ApplyPassiveExStyles(Window w)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
