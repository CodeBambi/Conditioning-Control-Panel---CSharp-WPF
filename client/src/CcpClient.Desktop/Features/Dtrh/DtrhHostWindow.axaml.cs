using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// The DTRH host shell (slice b1; dtrh-admission.md §3/§5). Surface selection is driven
/// by the PROBED capability states (SP-006 — never an OS guess): Windows embedded
/// WebView2 vs Linux NativeWebDialog vs honest unsupported. Boot contract (WPF
/// archaeology, DtrhHostService.cs:166-211 + ChaosWebViewHost.cs:301-305): the host
/// queues host→page messages until the page's `ready`, then flushes init + manifest in
/// order and claims keyboard focus (DtrhHostService.cs:169-172). Host→page: Windows =
/// synthetic MessageEvent dispatch on window.chrome.webview (SP-011 W4, byte-identical);
/// Linux = §3.3 inbox enqueue (retained delivery = the pre-ready queue, replay-equivalent).
/// Page→host: WebMessageReceived on both (FIRST GATE proven on the dialog path).
/// </summary>
public partial class DtrhHostWindow : Window
{
    private readonly ApplicationHost _host;
    private readonly DtrhParticipant _dtrh;
    private readonly string _page;
    private NativeWebView? _web;   // created programmatically, embedded path only (see axaml note)
    private NativeWebDialog? _dialog;
    private bool _sentBootMessages;
    private bool _engineLive;
    private int _heartbeats;
    private readonly CancellationTokenSource _closing = new();

    /// <summary>Boot-matrix facts (headed harness + diagnostics). Content-free.</summary>
    public bool EngineLive => _engineLive;

    public int Heartbeats => _heartbeats;

    public string Surface { get; private set; } = "pending";

    public DtrhHostWindow(ApplicationHost host, string page = "index.html")
    {
        _host = host;
        _dtrh = host.Participants.OfType<DtrhParticipant>().Single();
        _page = page;
        InitializeComponent();
        Opened += (_, _) => Begin();
        Closing += (_, _) =>
        {
            _closing.Cancel();
            TryCloseDialog();
        };
    }

    private void Begin()
    {
        var capabilities = _host.Capabilities;
        var embedded = capabilities?.GetState(DtrhCapabilityProbes.EmbeddedCapability);
        var dialog = capabilities?.GetState(DtrhCapabilityProbes.DialogCapability);

        if (embedded is CapabilityState.Available)
        {
            Surface = "embedded";
            BeginEmbedded();
        }
        else if (dialog is CapabilityState.Available)
        {
            Surface = "dialog";
            BeginDialog();
        }
        else
        {
            // Honest unsupported (admission §5: no classic fallback, never a silent substitute).
            Surface = "unsupported";
            UnsupportedPanel.IsVisible = true;
            UnsupportedDetail.Text =
                $"capability {DtrhCapabilityProbes.EmbeddedCapability}: {Describe(embedded)}\n"
                + $"capability {DtrhCapabilityProbes.DialogCapability}: {Describe(dialog)}";
            SetStatus("dtrh: honest unsupported (no classic fallback)");
            _host.LogDiagnostic("dtrh: no admitted web surface available — unsupported surface shown");
            _host.LogDiagnostic("dtrh: " + UnsupportedDetail.Text.Replace("\n", " | "));
        }
    }

    // ---------- Windows: embedded WebView2 ----------

    private void BeginEmbedded()
    {
        _web = new NativeWebView();
        _web.EnvironmentRequested += OnEnvironmentRequested;
        _web.AdapterCreated += (_, _) => _host.LogDiagnostic($"dtrh: AdapterCreated info='{SafeAdapterInfo()}'");
        _web.AdapterDestroyed += (_, _) => _host.LogDiagnostic("dtrh: AdapterDestroyed");
        _web.WebMessageReceived += OnWebMessage;
        _web.NavigationCompleted += OnNavigationCompleted;
        WebHost.Children.Add(_web);
        var url = _dtrh.PageUrl(_page);
        SetStatus("dtrh: navigating (embedded)");
        _host.LogDiagnostic($"dtrh: navigating embedded surface (page {_page})");
        _web.Source = new Uri(url);
    }

    private string SafeAdapterInfo()
    {
        try { return _web?.AdapterInfo?.ToString() ?? "(null)"; }
        catch (Exception ex) { return $"(AdapterInfo threw {ex.GetType().Name})"; }
    }

    private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs args)
    {
        args.EnableDevTools = false;
        if (args is Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs wv2)
        {
            var dataRoot = Path.Combine(
                Path.GetDirectoryName(CompositionRoot.DefaultSettingsPath())!, "dtrh");
            wv2.UserDataFolder = Path.Combine(dataRoot, "wv2-profile");
            // WPF parity (DtrhHostService.cs:119-120): the game's audio bed / drift voice
            // must start without a click; SP-011 W10 verified the flag end-to-end.
            wv2.AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required";
            _host.LogDiagnostic("dtrh: WebView2 UserDataFolder set; autoplay-policy=no-user-gesture-required");
        }
        else if (args is Avalonia.Platform.GtkWebViewEnvironmentRequestedEventArgs gtk)
        {
            var dataRoot = Path.Combine(
                Path.GetDirectoryName(CompositionRoot.DefaultSettingsPath())!, "dtrh");
            gtk.BaseDataDirectory = Path.Combine(dataRoot, "gtk-data");
            gtk.BaseCacheDirectory = Path.Combine(dataRoot, "gtk-cache");
            _host.LogDiagnostic("dtrh: GTK WebKit base dirs set");
        }
    }

    // ---------- Linux: NativeWebDialog (WebKitGTK toplevel) ----------

    private void BeginDialog()
    {
        var url = _dtrh.PageUrl(_page);
        _dialog = new NativeWebDialog { Title = "CCP — DTRH" };
        _dialog.EnvironmentRequested += OnEnvironmentRequested;
        _dialog.AdapterCreated += (_, _) => _host.LogDiagnostic("dtrh(dialog): AdapterCreated");
        _dialog.AdapterDestroyed += (_, _) => _host.LogDiagnostic("dtrh(dialog): AdapterDestroyed");
        _dialog.WebMessageReceived += OnWebMessage;
        _dialog.NavigationCompleted += OnNavigationCompleted;
        _dialog.Closing += (_, _) =>
        {
            _host.LogDiagnostic("dtrh(dialog): closing");
            Dispatcher.UIThread.Post(() =>
            {
                if (IsVisible) Close();
            });
        };
        _dialog.Source = new Uri(url);
        SetStatus("dtrh: dialog surface shown (NativeWebDialog)");
        _host.LogDiagnostic($"dtrh: showing NativeWebDialog surface (page {_page})");
        _dialog.Show(this);
    }

    private void TryCloseDialog()
    {
        try { _dialog?.Close(); } catch { /* best effort — teardown is idempotent */ }
        try { _dialog?.Dispose(); } catch { /* best effort */ }
        _dialog = null;
    }

    // ---------- probe page drive (boot-matrix transport checks) ----------

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        _host.LogDiagnostic($"dtrh: NavigationCompleted success={e.IsSuccess} (surface {Surface})");
        if (_page != "probe.html" || !e.IsSuccess)
        {
            return;
        }

        // Ported spike probe sequence (SP-011 W4/W6), driven identically on both surfaces:
        // probe-h2p AFTER the page's module registered its handler; probe-buffered BEFORE
        // the +4s late registration — bridge.js must buffer and replay it. Results mirror
        // back via bridge.log (page→host), so the no-InvokeScript dialog path evidences
        // both directions.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000, _closing.Token);
                // SendToPage marshals to the UI thread (WebView2 ExecuteScriptAsync is
                // apartment-bound; from a pool thread the call silently never lands).
                SendToPage(new { type = "probe-h2p", via = Surface == "embedded" ? "synthetic-dispatch" : "inbox" });
                SendToPage(new { type = "probe-buffered", via = "pre-handler-send" });
            }
            catch (OperationCanceledException) { /* window closed mid-probe */ }
        });
    }

    // ---------- boot contract ----------

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
                if (_heartbeats % 15 == 1) _host.LogDiagnostic($"dtrh: heartbeat #{_heartbeats}");
                return;
            case "log":
                var msg = JsonDocument.Parse(body).RootElement.GetProperty("msg").GetString();
                _host.LogDiagnostic($"dtrh page log: {msg}");
                if (msg?.Contains("engine live") == true)
                {
                    _engineLive = true;
                    _host.LogDiagnostic("dtrh: ENGINE LIVE");
                    SetStatus("dtrh: ENGINE LIVE");
                }

                return;
            case "ready":
                _host.LogDiagnostic("dtrh: ready received — flushing init+manifest");
                SendBootMessages();
                return;
            case "exit":
                _host.LogDiagnostic("dtrh: exit received — closing");
                Close();
                return;
            case "fullscreen-set":
                var fsOn = JsonDocument.Parse(body).RootElement.TryGetProperty("on", out var f) && f.GetBoolean();
                WindowState = fsOn ? WindowState.FullScreen : WindowState.Normal;
                _host.LogDiagnostic($"dtrh: fullscreen-set on={fsOn} -> WindowState={WindowState}");
                return;
            default:
                // Transport-probe messages surface verbatim (both-directions evidence).
                if (type.StartsWith("probe-", StringComparison.Ordinal))
                {
                    _host.LogDiagnostic($"dtrh: {body}");
                }

                return;
        }
    }

    /// <summary>
    /// The WPF boot contract (archaeology): init first, manifest second, in order, after
    /// ready. b1 sends ONLY these two (meta/loom-list/favorites are b2…b4).
    /// </summary>
    private void SendBootMessages()
    {
        if (_sentBootMessages) return;
        _sentBootMessages = true;

        // Focus claim at ready (DtrhHostService.cs:169-172): keyboard focus does not land
        // in the web child on a fresh launch until a click — claim it now so ESC works.
        Activate();
        _web?.Focus();
        if (_web is not null)
        {
            // Behavioral evidence of the claim (SP-011 W14 class): the page reports whether
            // keyboard focus actually reached the document.
            _ = _web.InvokeScript("document.hasFocus()")
                .ContinueWith(
                    t => _host.LogDiagnostic(t.IsFaulted
                        ? $"dtrh: focus check faulted: {t.Exception?.GetBaseException().Message}"
                        : $"dtrh: focus claimed at ready; document.hasFocus()={t.Result}"),
                    TaskScheduler.Default);
        }

        SendToPage(new
        {
            type = "init",
            protocol = 1,
            settings = new { masterVolume = 80 },
            modId = "builtin-sissyhypno",
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
        SendToPage(new
        {
            type = "manifest",
            images = new[] { new { name = "bubble.png", url = $"{_dtrh.Server.MediaOrigin}/media/bubbles/bubble.png" } },
            videos = new[] { new { name = "spiral.webm", url = $"{_dtrh.Server.MediaOrigin}/media/bubbles/spiral.webm" } },
            skipped = 0,
            truncated = false,
        });
        SetStatus("dtrh: init+manifest sent");
    }

    /// <summary>
    /// Host→page (admission §3.2): Windows embedded = synthetic MessageEvent dispatch on
    /// window.chrome.webview (SP-011 W4/W6 proven, byte-identical — never unified onto
    /// polling); Linux dialog = §3.3 inbox (retained seq delivery, replay-equivalent).
    /// </summary>
    public void SendToPage(object msg)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SendToPage(msg));
            return;
        }

        var json = JsonSerializer.Serialize(msg);
        if (Surface == "embedded")
        {
            _ = _web!.InvokeScript($"window.chrome.webview.dispatchEvent(new MessageEvent('message',{{data:{json}}}))")
                .ContinueWith(
                    t => _host.LogDiagnostic($"dtrh: host->page dispatch faulted: {t.Exception?.GetBaseException().Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
        }
        else
        {
            _dtrh.Inbox.Enqueue(json);
        }
    }

    private void SetStatus(string s) => Dispatcher.UIThread.Post(() => Status.Text = s);

    private static string Describe(CapabilityState? state) => state switch
    {
        null => "no capability registry",
        CapabilityState.Available available => $"Available — {available.Detail}",
        CapabilityState.Unavailable unavailable => $"Unavailable ({unavailable.Reason.Code}) — {unavailable.Reason.Detail}",
        CapabilityState.Degraded degraded => $"Degraded ({degraded.Reason.Code}) — {degraded.Reason.Detail}",
        CapabilityState.PermissionRequired permission => $"PermissionRequired ({permission.Reason.Code}) — {permission.Reason.Detail}",
        CapabilityState.DependencyMissing missing => $"DependencyMissing ({missing.Dependency}) — {missing.Reason.Detail}",
        CapabilityState.Faulted faulted => $"Faulted ({faulted.Reason.Code}) — {faulted.Reason.Detail}",
        _ => state.GetType().Name,
    };
}
