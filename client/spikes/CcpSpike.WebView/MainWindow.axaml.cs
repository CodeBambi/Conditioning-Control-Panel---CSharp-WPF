using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace CcpSpike.WebView;

public partial class MainWindow : Window
{
    private readonly SpikeConfig _config;
    private readonly LoopbackServer _server;
    private readonly SpikeLog _log;
    private string _transport = "unknown";
    private int _heartbeats;
    private bool _engineLive;
    private bool _sentBootMessages;
    private bool _teardownDone;

    public MainWindow()
    {
        _config = null!;
        _server = null!;
        _log = null!;
        InitializeComponent();
    } // designer only

    public MainWindow(SpikeConfig config, LoopbackServer server, SpikeLog log)
    {
        _config = config;
        _server = server;
        _log = log;
        InitializeComponent();

        Web.EnvironmentRequested += OnEnvironmentRequested;
        Web.AdapterCreated += (_, e) => _log.Log($"webview: AdapterCreated info='{SafeAdapterInfo()}'");
        Web.AdapterDestroyed += (_, _) => _log.Log("webview: AdapterDestroyed (renderer/host process failure surfaces here)");
        Web.NavigationStarted += (_, _) => _log.Log($"webview: NavigationStarted t={ElapsedMs()}ms");
        Web.NavigationCompleted += OnNavigationCompleted;
        Web.WebMessageReceived += OnWebMessage;
        Closing += (_, _) => Teardown();

        Opened += (_, _) => Begin();
    }

    private string SafeAdapterInfo()
    {
        try { return Web.AdapterInfo?.ToString() ?? "(null)"; }
        catch (Exception ex) { return $"(AdapterInfo threw {ex.GetType().Name})"; }
    }

    private long ElapsedMs() => (long)((Stopwatch.GetTimestamp() - _config.StartedTicks) / (double)Stopwatch.Frequency * 1000);

    private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs args)
    {
        // Backend identity evidence: WHICH platform args type arrived.
        _log.Log($"webview: EnvironmentRequested args={args.GetType().FullName}");
        args.EnableDevTools = false;
        if (args is WindowsWebView2EnvironmentRequestedEventArgs wv2)
        {
            wv2.UserDataFolder = Path.Combine(_config.ScratchDir, "wv2-profile");
            _log.Log($"webview: WebView2 UserDataFolder = {wv2.UserDataFolder}");
        }
        else if (args is LinuxWpeWebViewEnvironmentRequestedEventArgs wpe)
        {
            wpe.DataDirectory = Path.Combine(_config.ScratchDir, "wpe-data");
            wpe.CacheDirectory = Path.Combine(_config.ScratchDir, "wpe-cache");
            _log.Log("webview: WPE data/cache dirs set under scratch");
        }
    }

    private void Begin()
    {
        var url = _config.Page switch
        {
            "spike" => $"{_server.PageOrigin}/dtrh/spike.html",
            "probe" => $"{_server.PageOrigin}/dtrh/probe.html",
            _ => $"{_server.PageOrigin}/dtrh/index.html",
        };
        _log.Log($"spike: navigating {url} t={ElapsedMs()}ms (cold start)");
        SetStatus($"navigating {url}");
        Web.Source = new Uri(url);

        if (_config.AutoQuitSeconds > 0)
        {
            _ = Task.Delay(TimeSpan.FromSeconds(_config.AutoQuitSeconds)).ContinueWith(
                _ => Dispatcher.UIThread.Post(() => { _log.Log("spike: auto-quit"); Close(); }),
                TaskScheduler.Default);
        }
    }

    private async void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        _log.Log($"webview: NavigationCompleted t={ElapsedMs()}ms success={e.IsSuccess}");
        try
        {
            _transport = await DetectTransport();
            _log.Log($"transport: {_transport}");

            switch (_config.Page)
            {
                case "probe": await RunProbeSequence(); break;
                case "spike": await RunSpikePage(); break;
                default: await RunIndexPage(); break;
            }
        }
        catch (Exception ex)
        {
            _log.Log($"spike: post-navigation sequence faulted: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Transport check 1: which JS-side host objects exist in the page.</summary>
    private async Task<string> DetectTransport()
    {
        var wv2 = await EvalString("window.chrome && window.chrome.webview ? 'present' : 'absent'");
        var ica = await EvalString("typeof window.invokeCSharpAction");
        _log.Log($"transport check1: window.chrome.webview={wv2} invokeCSharpAction={ica}");
        return wv2 == "present" ? "webview2-native-object" : $"no-webview2-object(ica={ica})";
    }

    private async Task<string?> EvalString(string js)
    {
        var raw = await Web.InvokeScript(js);
        if (raw is null) return null;
        try { return JsonSerializer.Deserialize<string>(raw) ?? raw; } catch { return raw.Trim('"'); }
    }

    // ---------- probe page: ordered transport checks 2 and 3 ----------

    private async Task RunProbeSequence()
    {
        SetStatus("probe: running transport checks");
        await Task.Delay(1200); // let the probe module import bridge.js and send its messages
        var outText = await EvalString("document.getElementById('out') ? document.getElementById('out').textContent : '(no #out)'");
        _log.Log("probe #out after module run:\n" + outText);

        // Check 3: host -> page via synthetic MessageEvent dispatch on the unchanged bridge transport.
        if (_transport == "webview2-native-object")
        {
            await SendToPage(new { type = "probe-h2p", via = "synthetic-dispatch" });
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(250);
                var title = await EvalString("document.title");
                if (title == "PROBE-DONE") break;
            }

            var final = await EvalString("document.getElementById('out').textContent");
            _log.Log("probe #out final:\n" + final);
            _log.Log($"probe check3: synthetic host->page dispatch {(await EvalString("document.title") == "PROBE-DONE" ? "DELIVERED" : "NOT delivered")}");
        }
        else
        {
            _log.Log("probe check3: SKIPPED — window.chrome.webview absent; plan-B shim would be required (admit-row material)");
        }

        _log.Log("probe: sequence complete");
        SetStatus("probe: complete (see log)");
    }

    // ---------- spike page (granular matrix, M0 harness) ----------

    private async Task RunSpikePage()
    {
        if (_transport != "webview2-native-object")
        {
            _log.Log("spike.html: bridge object absent — spike-run cannot be delivered unchanged; named finding");
            SetStatus("spike: bridge absent (see log)");
            return;
        }

        SetStatus("spike.html: sending spike-run");
        await Task.Delay(500); // page module registers its listener at import; give it a beat
        await SendToPage(new
        {
            type = "spike-run",
            assets = new
            {
                video = $"{_server.MediaOrigin}/media/bubbles/spiral.webm",
                image = $"{_server.MediaOrigin}/media/bubbles/bubble.png",
            },
        });
        SetStatus("spike.html: spike-run sent; results stream to log");
    }

    // ---------- index page (the boot claim) ----------

    private async Task RunIndexPage()
    {
        SetStatus("index: waiting for page ready (or grace timeout)");
        // The boot contract: host queues init/manifest until 'ready'. If page->host is
        // silent (transport check 2 failed), we still attempt boot via timed send and
        // RECORD which path happened — that difference is itself the transport evidence.
        for (var i = 0; i < 40 && !_sentBootMessages; i++) await Task.Delay(250);
        if (!_sentBootMessages)
        {
            _log.Log("index: NO 'ready' received within 10s grace — page->host transport silent; sending init/manifest unsolicited (timed path)");
            await SendBootMessages("timed");
        }
    }

    private async Task SendBootMessages(string path)
    {
        if (_sentBootMessages) return;
        _sentBootMessages = true;
        _log.Log($"index: sending init+manifest ({path} path) t={ElapsedMs()}ms");
        // 'ready' can arrive BEFORE NavigationCompleted (WebView2 pumps messages during
        // load), i.e. before DetectTransport ran — detect on demand instead of gating.
        if (_transport == "unknown") _transport = await DetectTransport();
        if (_transport != "webview2-native-object")
        {
            _log.Log("index: host->page transport absent — boot cannot proceed unchanged; named finding");
            SetStatus("index: transport absent (see log)");
            return;
        }

        await SendToPage(new
        {
            type = "init",
            protocol = 1,
            settings = new { masterVolume = 0.8 },
            modId = "builtin-bambisleep",
            modContent = (object?)null,
            runSetup = new
            {
                difficulty = "Easy",
                durationSec = 180,
                waveCount = 5,
                motion = "Mixed",
                enabledVariants = (object?)null,
                effectIntensity = 0.85,
                colorFlashes = true,
                boonDraftEnabled = true,
                allowCurses = true,
                dartersEnabled = true,
                key1 = "Q",
                key2 = "E",
            },
            m2Test = false,
        });
        await SendToPage(BuildManifest());
        SetStatus("index: init+manifest sent; waiting for engine");
    }

    private object BuildManifest()
    {
        if (!_config.PopulateManifest)
            return new { type = "manifest", images = Array.Empty<object>(), videos = Array.Empty<object>(), skipped = 0, truncated = false };

        return new
        {
            type = "manifest",
            images = new[] { new { name = "bubble.png", url = $"{_server.MediaOrigin}/media/bubbles/bubble.png" } },
            videos = new[] { new { name = "spiral.webm", url = $"{_server.MediaOrigin}/media/bubbles/spiral.webm" } },
            skipped = 0,
            truncated = false,
        };
    }

    private async Task SendToPage(object msg)
    {
        var json = JsonSerializer.Serialize(msg);
        // Synthetic WebView2-shaped dispatch: bridge.js listens via
        // window.chrome.webview.addEventListener('message') — a synthetic MessageEvent on
        // that EventTarget reaches its handlers byte-unchanged (spike transport; the real
        // transport choice is the admit row's).
        await Web.InvokeScript($"window.chrome.webview.dispatchEvent(new MessageEvent('message',{{data:{json}}}))");
        _log.Log($"host->page: {json[..Math.Min(json.Length, 220)]}");
    }

    // ---------- page -> host ----------

    private void OnWebMessage(object? sender, WebMessageReceivedEventArgs e)
    {
        var body = e.Body ?? "";
        string type;
        try { type = JsonDocument.Parse(body).RootElement.GetProperty("type").GetString() ?? "?"; }
        catch { type = "(unparseable)"; }

        switch (type)
        {
            case "heartbeat":
                _heartbeats++;
                if (_heartbeats % 15 == 1) _log.Log($"page->host: heartbeat #{_heartbeats} t={ElapsedMs()}ms");
                return;
            case "log":
                var msg = JsonDocument.Parse(body).RootElement.GetProperty("msg").GetString();
                _log.Log($"page log: {msg}");
                if (msg?.Contains("engine live") == true)
                {
                    _engineLive = true;
                    _log.Log($"index: ENGINE LIVE t={ElapsedMs()}ms (cold time-to-engine)");
                    SetStatus("index: ENGINE LIVE");
                    _ = SampleFrames();
                }

                return;
            case "ready":
                _log.Log($"page->host: ready (protocol {(JsonDocument.Parse(body).RootElement.TryGetProperty("protocol", out var p) ? p.GetInt32() : -1)}) t={ElapsedMs()}ms");
                _ = SendBootMessages("ready-triggered");
                return;
            case "exit":
                _log.Log($"page->host: exit t={ElapsedMs()}ms — closing window");
                Close();
                return;
            default:
                _log.Log($"page->host: {body[..Math.Min(body.Length, 300)]}");
                return;
        }
    }

    private async Task SampleFrames()
    {
        await Task.Delay(500);
        await Web.InvokeScript("window.__fps=null;(()=>{let n=0;const t0=performance.now();const f=()=>{if(++n<90)requestAnimationFrame(f);else window.__fps=(n*1000/(performance.now()-t0)).toFixed(1)};requestAnimationFrame(f)})()");
        for (var i = 0; i < 40; i++)
        {
            await Task.Delay(250);
            var fps = await EvalString("window.__fps === null ? null : String(window.__fps)");
            if (fps is not null)
            {
                _log.Log($"frames: steady-state rAF average {fps} fps over 90 frames t={ElapsedMs()}ms");
                return;
            }
        }

        _log.Log("frames: rAF sampler produced no value in 10s (page main thread stalled?)");
    }

    // ---------- teardown ----------

    private void SetStatus(string s) => Dispatcher.UIThread.Post(() => Status.Text = s);

    private void Teardown()
    {
        if (_teardownDone) return; // idempotent (SP-003 discipline)
        _teardownDone = true;
        _log.Log($"spike: teardown t={ElapsedMs()}ms engineLive={_engineLive} heartbeats={_heartbeats}");
        _server.Dispose();
    }
}
