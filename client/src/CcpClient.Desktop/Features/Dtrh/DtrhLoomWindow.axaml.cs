using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// SP-049: THE LOOM studio window — the v6.6.3 promotion (WPF <c>LoomHostService</c>
/// parity, Services/Chaos/LoomHostService.cs): a STRIPPED sibling of the game host. One
/// windowed web surface on <c>/dtrh/loom.html</c>, speaking only the loom bridge subset
/// (loom-save / loom-delete / loom-reveal / sfx / log / ready out of the page, loom-result
/// / loom-list in). NO exit protocol, NO meta/slots/bark/watchdog — a plain titled window
/// the user closes with X (LoomHostService.cs:78-83). The store + serving + list/result
/// machinery are b4-landed and shared through <see cref="DtrhLoomDispatch"/> (never
/// re-ported). Own WebView2 profile (LoomHostService.cs:64 — "the game's WebView2 state
/// stays untouched"; also keeps the b5 stale-profile-lock class single-surface).
/// </summary>
public partial class DtrhLoomWindow : Window
{
    private readonly ApplicationHost _host;
    private readonly DtrhParticipant _dtrh;
    private readonly string? _loomDrive;
    private NativeWebView? _web;        // created programmatically, embedded path only
    private NativeWebDialog? _dialog;
    private readonly CancellationTokenSource _closing = new();
    private DtrhLoomDispatch? _loomDispatch;
    private DtrhNativeEffects? _fx;
    private SoundFlowDtrhAudio? _audio;
    private bool _sentLoomList;

    /// <summary>Boot-matrix facts (headed harness + diagnostics). Content-free.</summary>
    public string Surface { get; private set; } = "pending";

    /// <summary>True once the page announced ready and the first loom-list was posted.</summary>
    public bool StudioLive => _sentLoomList;

    public DtrhLoomWindow(ApplicationHost host, string? loomDrive = null)
    {
        _host = host;
        _dtrh = host.Participants.OfType<DtrhParticipant>().Single();
        _loomDrive = loomDrive;
        InitializeComponent();
        Opened += (_, _) =>
        {
            InitLoom();
            Begin();
            ScheduleLoomDrive();
        };
        Closing += (_, _) =>
        {
            _closing.Cancel();
            try { _fx?.Teardown(); } catch { /* best-effort — teardown is idempotent */ }
            try { _fx?.Dispose(); } catch { /* best-effort */ }
            _fx = null;
            try { _audio?.Dispose(); } catch { /* best-effort */ }
            _audio = null;
            try { _dialog?.Close(); } catch { /* best effort */ }
            try { _dialog?.Dispose(); } catch { /* best effort */ }
            _dialog = null;
        };
    }

    /// <summary>The loom store + shared dispatch (b4-landed machinery; the sfx channel is
    /// the b3 native-effects path with a null video backend — the studio never fires
    /// video/freeze/whisper; boon_pick + UI blips are its whole audio surface).</summary>
    private void InitLoom()
    {
        var loom = new DtrhLoom(_dtrh.SpiralsRoot, _host.LogDiagnostic);
        // WPF LoomHostService.Launch :37-39: create the store folder BEFORE navigating —
        // the rack thumbnails serve from it (a missing folder would 404 every tile).
        try { Directory.CreateDirectory(_dtrh.SpiralsRoot); }
        catch (Exception ex) { _host.LogDiagnostic($"loom: spirals dir create failed ({ex.GetType().Name})"); }

        _loomDispatch = new DtrhLoomDispatch(loom, () => _dtrh.Server.MediaOrigin, SendToPage, _host.LogDiagnostic);

        _audio = new SoundFlowDtrhAudio(_host.LogDiagnostic);
        if (!_audio.TryInit(null, out var audioError))
        {
            _host.LogDiagnostic($"loom: audio backend init failed ({audioError}) — audio disabled this session");
        }

        _fx = new DtrhNativeEffects(_audio, new NullDtrhVideo(), new DtrhNativeEffectsOptions
        {
            SfxRoots = [Path.Combine(DtrhParticipant.MediaRoot, "bubbles", "sfx"),
                        Path.Combine(DtrhParticipant.OverlayRoot, "assets", "bubbles", "sfx")],
            WhisperRoots = [],
            VideoRoots = [],
            PresenceOnlyRoots = [],
            MasterVolume = 80, // the host window's literal (b2); the settings seam is a named limit.
        }, _host.LogDiagnostic);
        _fx.NotifySessionStart();
        _host.LogDiagnostic("loom: store + dispatch + sfx channel up (null video — the studio has no video surface)");
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
                $"capability {DtrhCapabilityProbes.EmbeddedCapability}: {DtrhHostWindow.DescribeState(embedded)}\n"
                + $"capability {DtrhCapabilityProbes.DialogCapability}: {DtrhHostWindow.DescribeState(dialog)}";
            SetStatus("loom: honest unsupported (no classic fallback)");
            _host.LogDiagnostic("loom: no admitted web surface available — unsupported surface shown");
        }
    }

    // ---------- Windows: embedded WebView2 ----------

    private void BeginEmbedded()
    {
        _web = new NativeWebView();
        _web.EnvironmentRequested += OnEnvironmentRequested;
        _web.AdapterCreated += (_, _) => _host.LogDiagnostic("loom: AdapterCreated");
        _web.AdapterDestroyed += (_, _) => _host.LogDiagnostic("loom: AdapterDestroyed");
        _web.WebMessageReceived += OnWebMessage;
        WebHost.Children.Add(_web);
        var url = _dtrh.PageUrl("loom.html");
        SetStatus("loom: navigating (embedded)");
        _host.LogDiagnostic("loom: navigating embedded surface (page loom.html)");
        _web.Source = new Uri(url);
    }

    private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs args)
    {
        args.EnableDevTools = false;
        if (args is Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs wv2)
        {
            // OWN profile (LoomHostService.cs:64 browser_data_loom parity — the game's
            // WebView2 state stays untouched; never share the b5-profiled surface).
            wv2.UserDataFolder = DtrhProfileLock.WebView2ProfileDir("loom");
            // The studio's sfx play off worker round-trips, not gestures.
            wv2.AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required";
            _host.LogDiagnostic("loom: WebView2 UserDataFolder set (own profile, loom-suffixed); autoplay-policy=no-user-gesture-required");
        }
        else if (args is Avalonia.Platform.GtkWebViewEnvironmentRequestedEventArgs gtk)
        {
            var dataRoot = DtrhProfileLock.DtrhDataRoot();
            gtk.BaseDataDirectory = Path.Combine(dataRoot, "gtk-data-loom");
            gtk.BaseCacheDirectory = Path.Combine(dataRoot, "gtk-cache-loom");
            _host.LogDiagnostic("loom: GTK WebKit base dirs set (loom-suffixed)");
        }
    }

    // ---------- Linux: NativeWebDialog (WebKitGTK toplevel) ----------

    private void BeginDialog()
    {
        var url = _dtrh.PageUrl("loom.html");
        _dialog = new NativeWebDialog { Title = "The Loom" };
        _dialog.EnvironmentRequested += OnEnvironmentRequested;
        _dialog.AdapterCreated += (_, _) => _host.LogDiagnostic("loom(dialog): AdapterCreated");
        _dialog.AdapterDestroyed += (_, _) => _host.LogDiagnostic("loom(dialog): AdapterDestroyed");
        _dialog.WebMessageReceived += OnWebMessage;
        _dialog.Closing += (_, _) =>
        {
            _host.LogDiagnostic("loom(dialog): closing");
            Dispatcher.UIThread.Post(() =>
            {
                if (IsVisible) Close();
            });
        };
        _dialog.Source = new Uri(url);
        SetStatus("loom: dialog surface shown (NativeWebDialog)");
        _host.LogDiagnostic("loom: showing NativeWebDialog surface (page loom.html)");
        _dialog.Show(this);
    }

    // ---------- the loom bridge subset ----------

    private void OnWebMessage(object? sender, WebMessageReceivedEventArgs e) => HandleWebMessageBody(e.Body ?? "");

    /// <summary>The same b2 tolerance discipline as the game host: every frame parses to
    /// a typed outcome — never silent, never crashes. Presence+shape logging only.</summary>
    private void HandleWebMessageBody(string body)
    {
        switch (DtrhProtocol.ParsePageMessage(body))
        {
            case DtrhProtocol.DtrhPageParseResult.Parsed parsed:
                DispatchPageMessage(parsed.Message);
                return;
            case DtrhProtocol.DtrhPageParseResult.UnknownType unknown:
                _host.LogDiagnostic($"loom: unknown page message type '{unknown.Type}' — tolerated (typed, not dropped)");
                return;
            case DtrhProtocol.DtrhPageParseResult.ForwardVersion forward:
                _host.LogDiagnostic(
                    $"loom: page message '{forward.Type}' declares protocol {forward.Protocol} > {DtrhProtocol.Version} — tolerated (forward-version, not acted on)");
                return;
            case DtrhProtocol.DtrhPageParseResult.Malformed malformed:
                _host.LogDiagnostic($"loom: malformed page message ({malformed.Reason}) — tolerated");
                return;
        }
    }

    private void DispatchPageMessage(DtrhProtocol.DtrhPageMessage message)
    {
        if (DtrhProtocol.Classify(message) is DtrhProtocol.DtrhDispatchClass.Deferred deferred)
        {
            _host.LogDiagnostic($"loom: '{message.GetType().Name}' deferred to slice {deferred.Slice} (typed, not dropped)");
            return;
        }

        switch (message)
        {
            case DtrhProtocol.DtrhPageMessage.Ready:
                // WPF LoomHostService OnReady=PostLoomList (:66): the studio consumes ONLY
                // the loom-list (loomBoot.js:17-22) — no init/manifest/meta on this host.
                _host.LogDiagnostic("loom: ready received — posting loom-list (stripped boot, LoomHostService parity)");
                Activate();
                _web?.Focus();
                _loomDispatch?.PostList();
                _sentLoomList = true;
                SetStatus("loom: studio live (loom-list posted)");
                return;
            case DtrhProtocol.DtrhPageMessage.Log log:
                _host.LogDiagnostic($"loom page log: {log.Msg}");
                return;
            case DtrhProtocol.DtrhPageMessage.Sfx sfx:
                if (_fx is null)
                {
                    _host.LogDiagnostic("loom: 'Sfx' arrived before effects init — logged, not acted on");
                    return;
                }

                _fx.PlaySfx(sfx.Name, sfx.Scale);
                return;
            case DtrhProtocol.DtrhPageMessage.LoomSave or DtrhProtocol.DtrhPageMessage.LoomDelete
                or DtrhProtocol.DtrhPageMessage.LoomReveal:
                if (_loomDispatch is null)
                {
                    _host.LogDiagnostic($"loom: '{message.GetType().Name}' arrived before loom init — logged, not acted on");
                    return;
                }

                _loomDispatch.TryHandle(message);
                return;
            default:
                // Everything else in the v1 vocabulary is the GAME's surface (meta, runs,
                // freeze, …) — the stripped host logs it typed, never acts (WPF's
                // LoomHostService switch has no arms for them either).
                _host.LogDiagnostic($"loom: '{message.GetType().Name}' is not part of the studio subset — logged, not acted on");
                return;
        }
    }

    /// <summary>Host→page: identical transports to the game host (embedded = synthetic
    /// MessageEvent dispatch, dialog = §3.3 inbox enqueue).</summary>
    public void SendToPage(object msg)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SendToPage(msg));
            return;
        }

        var json = DtrhProtocol.SerializeForPage(msg);
        if (Surface == "embedded")
        {
            _ = _web!.InvokeScript($"window.chrome.webview.dispatchEvent(new MessageEvent('message',{{data:{json}}}))")
                .ContinueWith(
                    t => _host.LogDiagnostic($"loom: host->page dispatch faulted: {t.Exception?.GetBaseException().Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
        }
        else
        {
            _dtrh.Inbox.Enqueue(json);
        }
    }

    // ---------- HARNESS-ONLY loom drive (--loom-drive) ----------

    /// <summary>HARNESS-ONLY headed evidence drive: <code>save:&lt;name&gt;@t;
    /// delete-first@t; reveal-first@t</code> (@t seconds, default spacing 6s). Each step
    /// runs ONE atomic script through the engine's own InvokeScript (the SP-011 W14
    /// precedent) — the gifenc encoder, the bridge messages, the store, the serving, and
    /// the rack re-render are ALL real; only the pointer is scripted (WSLg no-input
    /// class, honestly labeled). The studio rebuilds its DOM on every redraw, so each
    /// script is self-contained (element refs never cross evaluations).</summary>
    private void ScheduleLoomDrive()
    {
        if (string.IsNullOrWhiteSpace(_loomDrive)) return;
        _host.LogDiagnostic($"loom: loom-drive armed (HARNESS-ONLY — scripted pointer, real everything else): {_loomDrive}");
        var steps = _loomDrive.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var index = 0;
        foreach (var step in steps)
        {
            index++;
            var at = step.IndexOf('@');
            var seconds = at >= 0 && double.TryParse(step[(at + 1)..], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : index * 6.0;
            var bare = at >= 0 ? step[..at] : step;
            var script = LoomDriveScript(bare);
            if (script is null)
            {
                _host.LogDiagnostic($"loom: loom-drive step '{step}' unknown — skipped (harness)");
                continue;
            }

            _ = Task.Delay(TimeSpan.FromSeconds(seconds), _closing.Token).ContinueWith(
                t =>
                {
                    if (t.IsCanceled) return;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_web is null)
                        {
                            _host.LogDiagnostic($"loom: loom-drive '{bare}' skipped — no embedded surface (dialog path has no InvokeScript; typed)");
                            return;
                        }

                        _ = _web.InvokeScript(script)
                            .ContinueWith(
                                st => _host.LogDiagnostic(st.IsFaulted
                                    ? $"loom: loom-drive '{bare}' faulted: {st.Exception?.GetBaseException().Message}"
                                    : $"loom: loom-drive '{bare}' -> {st.Result}"),
                                TaskScheduler.Default);
                    });
                }, TaskScheduler.Default);
        }
    }

    private static string? LoomDriveScript(string step)
    {
        if (step.StartsWith("save:", StringComparison.Ordinal))
        {
            // Name the weave + SAVE, atomically: .value alone never fires the input
            // handler (loomStudio.js:678 — the module state is what startSave reads).
            var name = JsonSerializer.Serialize(step["save:".Length..]);
            return "(function(){var inp=document.querySelector('.wr-loom-name');"
                + "var btn=document.querySelector('.wr-loom-save button');"
                + "if(!inp||!btn)return 'loom-drive: controls not found';"
                + $"inp.value={name};inp.dispatchEvent(new Event('input',{{bubbles:true}}));"
                + "if(btn.disabled)return 'loom-drive: save disabled (encoding)';"
                + "btn.click();return 'loom-drive: save clicked';})()";
        }

        return step switch
        {
            // Two-click forget across the redraw: click 🗑, then (after the armed redraw)
            // click the armed 'forget?' — the second click sends the REAL loom-delete.
            "delete-first" =>
                "(function(){var del=document.querySelector('.wr-loom-rackrow .wr-row-right .wr-craft-chip:last-child');"
                + "if(!del)return 'loom-drive: no rack tile';del.click();"
                + "setTimeout(function(){var armed=document.querySelector('.wr-loom-rackrow .wr-row-right .wr-craft-chip.is-armed');"
                + "if(armed)armed.click();},400);return 'loom-drive: delete armed';})()",
            // 📂 on the first tile — the REAL loom-reveal through the real dispatch.
            "reveal-first" =>
                "(function(){var chips=document.querySelectorAll('.wr-loom-rackrow .wr-row-right .wr-craft-chip');"
                + "if(chips.length<2)return 'loom-drive: no rack tile';chips[1].click();return 'loom-drive: reveal clicked';})()",
            _ => null,
        };
    }

    private void SetStatus(string s) => Dispatcher.UIThread.Post(() => Status.Text = s);

    /// <summary>The studio has no video surface (no fire-payload/freeze/whisper on this
    /// host) — the effects engine still wants the seam. Every member is an honest no-op;
    /// TryPlay refuses (nothing here should ever call it).</summary>
    private sealed class NullDtrhVideo : IDtrhVideoBackend
    {
        public long FrameCount => 0;
        public double PositionSec => 0;
        public Avalonia.Media.Imaging.WriteableBitmap? CurrentFrame => null;

#pragma warning disable CS0067 // never raised — the null backend presents nothing
        public event EventHandler? FramePresented;
        public event EventHandler? PlaybackEnded;
        public event EventHandler? EncounteredError;
#pragma warning restore CS0067

        public bool TryPlay(string path) => false;
        public void SetPaused(bool paused) { }
        public void Stop() { }
    }
}
