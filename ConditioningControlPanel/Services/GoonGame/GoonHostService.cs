using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// Host service for the Goon Game browser client (Resources/web/goon). Modeled on
    /// <see cref="ConditioningControlPanel.Services.Quiz.IntakeHostService"/>, NOT on
    /// <see cref="ConditioningControlPanel.Services.Chaos.DtrhHostService"/>: this is a windowed
    /// duel surface, so there is no tray tuck and no meta bridge — main is ducked with a plain
    /// minimize and given back through the single <see cref="DisposeAll"/> funnel.
    ///
    /// The C# engine under Services/GoonGame stays the reference implementation; the page is a
    /// second client of the same server (/v2/goon/*) and of the same deterministic specs
    /// (see <see cref="GoonVectorDumper"/> for the parity vectors the page's tests consume).
    ///
    ///   Host -&gt; Page:  init { protocol, identity, net, caps, consent, fullscreen }
    ///                  manifest { images, videos, skipped, truncated }
    ///                  fullscreen { on }        (always the REAL window state)
    ///                  ping                     (heartbeat watchdog prod)
    ///                  end-run { reason }       (host-initiated wind-down)
    ///                  net-post-result { id, status, body }
    ///   Page -&gt; Host:  ready · log              (consumed inside ChaosWebViewHost)
    ///                  heartbeat · pong · boot-error · fullscreen-set
    ///                  exit · exit-done
    ///                  toy-pattern · toy-stop   (STUBS — haptics v2 is not merged)
    ///                  match-result             (STUB — XP wiring comes later)
    ///                  net-post { id, path, body }
    /// </summary>
    internal static class GoonHostService
    {
        public const string ProductName = "Goon Game";

        private const int Protocol = 1;

        /// <summary>Server base for the host-proxied bridge. Same origin the C# signaling client
        /// talks to (<see cref="GoonSignalingClient"/>), so both clients hit one deployment.</summary>
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

        /// <summary>The ONLY path prefix the page may proxy through the app. See
        /// <see cref="OnNetPost"/> — without this the page would be a general-purpose HTTP
        /// client wearing the app's auth token.</summary>
        private const string AllowedPathPrefix = "/v2/goon/";

        private static ChaosWebViewHost? _host;
        private static DispatcherTimer? _heartbeatWatch;
        private static DispatcherTimer? _exitWatchdog;
        private static DateTime _lastHeartbeatUtc;
        private static bool _exiting;
        private static bool _relaunchedOnce;
        private static bool _disposing;          // reentrancy guard (Dispose closes the window -> Closed -> DisposeAll)
        private static bool _recoveryWindowed;   // this relaunch is a recovery: ignore the remembered fullscreen
        private static bool _duckPreference = true;
        private static bool _duckedMainWindow;   // WE minimized main at launch, so WE owe a restore
        private static WindowState _mainStateBeforeDuck = WindowState.Normal;

        /// <summary>One client for the whole app session — a per-request HttpClient exhausts
        /// sockets, and the header/timeout shape here must match
        /// <c>GoonSignalingClient.DefaultPostAsync</c> (40 s: the relay long-polls ~20 s).</summary>
        private static readonly HttpClient Http = BuildHttpClient();

        public static bool IsActive => _host != null;

        /// <summary>The page reported boot-error this app session (a genuine load/init failure).
        /// A caller can check this to route back to the C# cockpit instead.</summary>
        public static bool BootFailedThisSession { get; private set; }

        private static HttpClient BuildHttpClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
            try
            {
                c.DefaultRequestHeaders.UserAgent.ParseAdd(
                    $"ConditioningControlPanel/{UpdateService.AppVersion}");
            }
            catch { }
            return c;
        }

        /// <summary>Launch the Goon Game window (idempotent). A running instance is re-focused.</summary>
        public static void Launch(bool duckMainWindow = true)
        {
            if (_host != null) { _host.FocusWeb(); return; }
            try
            {
                _exiting = false;
                _duckPreference = duckMainWindow;

                var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
                var mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
                {
                    // The page + anything it shares with the other web cores lives on this one
                    // origin (Deny = same-origin only, matching the intake/DtRH hosts).
                    ("ccp.game", webRoot, CoreWebView2HostResourceAccessKind.Deny),
                };
                // A mapping whose folder does not exist is silently DROPPED by WebView2, which
                // reads later as "the page's art 404s for no reason". Add the optional roots only
                // when they're really there, and say so in the log when they are not.
                AddIfPresent(mappings, "ccp.assets", App.EffectiveAssetsPath);
                AddIfPresent(mappings, "ccp.art", Path.Combine(AppContext.BaseDirectory, "assets", "Chaos"));

                _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
                {
                    StartUrl = "https://ccp.game/goon/index.html",
                    PrimaryHost = "ccp.game",
                    Mappings = mappings,
                    UserDataFolderName = "browser_data_goon",
                    InputEnabled = true,
                    // Window mode is REMEMBERED (AppSettings.GoonFullscreen) so the window is BUILT
                    // in the mode the player left it in. A recovery relaunch always comes back
                    // windowed: if the page wedged once it may wedge again, and a titled window is
                    // the state every ordinary Windows exit still works from.
                    StartFullscreen = !_recoveryWindowed && App.Settings?.Current?.GoonFullscreen == true,
                    OwnedByMainWindow = true,
                    WindowTitle = ProductName,
                    LogTag = "GoonHost",
                    // --autoplay-policy: the duel's audio bed must start without a click.
                    // --disable-background-timer-throttling / --disable-backgrounding-occluded-windows:
                    //   a match is a LIVE shared clock. Chromium throttles timers in a backgrounded
                    //   or occluded window, which would stall the page's tick/heartbeat while the
                    //   opponent's match keeps running — a desync produced by nothing but Alt-Tab.
                    ExtraBrowserArguments =
                        "--autoplay-policy=no-user-gesture-required "
                        + "--disable-background-timer-throttling "
                        + "--disable-backgrounding-occluded-windows",
                    OnReady = OnPageReady,
                    OnMessage = OnPageMessage,
                    OnProcessFailed = OnProcessFailed,
                });
                _recoveryWindowed = false;   // consumed by the Options above; the next launch is normal again
                _host.Show();
                // Windowed surface: the user closes it via the title-bar X. Tear down cleanly so
                // IsActive resets and the heartbeat watchdog can't relaunch a window they shut.
                if (_host.Window != null) _host.Window.Closed += (_, _) => DisposeAll();
                StartHeartbeatWatch();
                if (duckMainWindow) DuckMainWindow();
                App.Logger?.Information("GoonHostService: launched");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "GoonHostService.Launch failed");
                DisposeAll();
            }
        }

        private static void AddIfPresent(
            List<(string, string, CoreWebView2HostResourceAccessKind)> mappings, string host, string folder)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                {
                    // Allow (not DenyCors): the page uploads this media to canvas/WebGL, which
                    // needs CORS-clean responses.
                    mappings.Add((host, folder, CoreWebView2HostResourceAccessKind.Allow));
                    return;
                }
                App.Logger?.Debug("GoonHostService: {Host} mapping skipped (no folder at {Folder})", host, folder);
            }
            catch (Exception ex) { App.Logger?.Debug("GoonHostService.AddIfPresent({Host}): {E}", host, ex.Message); }
        }

        /// <summary>Graceful close: ask the page to wind down, watchdog-force after 1200 ms. Idempotent.</summary>
        public static void CloseActive()
        {
            try
            {
                if (_host == null) return;
                if (_host.IsReady && !_exiting)
                {
                    _exiting = true;
                    _host.Post(new { type = "end-run", reason = "host" });
                    ArmExitWatchdog();
                }
                else
                {
                    DisposeAll();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("GoonHostService.CloseActive: {E}", ex.Message); DisposeAll(); }
        }

        // ============================ ducking the control panel ============================

        /// <summary>Get the control panel out of the way, exactly as the intake host does: a plain
        /// MINIMIZE, never DtRH's tray tuck (this is a windowed surface; making the app's taskbar
        /// button vanish would read as a crash). No-op when main is already minimized or hidden —
        /// the user's own last word on the window stands and we take no restore debt we didn't earn.</summary>
        private static void DuckMainWindow()
        {
            try
            {
                var main = (Window?)App.MainWindowRef ?? Application.Current?.MainWindow;
                if (main == null || !main.IsVisible || main.WindowState == WindowState.Minimized) return;
                _mainStateBeforeDuck = main.WindowState;
                main.WindowState = WindowState.Minimized;
                _duckedMainWindow = true;
                // Minimizing main hands activation to whatever is next in the z-order; take it
                // back so the duel keeps keyboard focus from the first frame.
                _host?.FocusWeb();
                App.Logger?.Information("GoonHostService: ducked MainWindow (was {S})", _mainStateBeforeDuck);
            }
            catch (Exception ex) { App.Logger?.Debug("GoonHostService.DuckMainWindow: {E}", ex.Message); }
        }

        /// <summary>Undo <see cref="DuckMainWindow"/> — called from <see cref="DisposeAll"/>, the
        /// single funnel every close path runs through, because a tool that minimizes the app and
        /// leaves it minimized is a worse bug than the one the duck fixes.</summary>
        private static void RestoreMainWindow()
        {
            if (!_duckedMainWindow) return;
            _duckedMainWindow = false;
            try
            {
                var disp = Application.Current?.Dispatcher;
                if (disp == null || disp.HasShutdownStarted) return;   // app is going away regardless
                var main = (Window?)App.MainWindowRef ?? Application.Current?.MainWindow;
                if (main == null || !main.IsVisible) return;
                if (main.WindowState != WindowState.Minimized) return;
                main.WindowState = _mainStateBeforeDuck == WindowState.Maximized
                    ? WindowState.Maximized
                    : WindowState.Normal;
                try { main.Activate(); } catch { }
            }
            catch (Exception ex) { App.Logger?.Debug("GoonHostService.RestoreMainWindow: {E}", ex.Message); }
        }

        // ============================ boot ============================

        private static void OnPageReady()
        {
            try
            {
                _lastHeartbeatUtc = DateTime.UtcNow;
                _host?.FocusWeb();

                var consent = new ConsentSheetMsg();   // the engine's own defaults, never a fork
                _host?.Post(new
                {
                    type = "init",
                    protocol = Protocol,
                    identity = new
                    {
                        unifiedId = App.UnifiedUserId ?? "",
                        displayName = SafeDisplayName(),
                        appVersion = UpdateService.AppVersion,
                    },
                    net = new
                    {
                        serverBase = ProxyBaseUrl,
                        // The CCP auth token (X-Auth-Token), NOT the Patreon bearer — /v2/goon/*
                        // authenticates the unified account. Sent so the page can move to direct
                        // fetch once CORS for the ccp.game origin is deployed; until then every
                        // call comes back through net-post and the host attaches the header itself.
                        authToken = SafeAuthToken(),
                        viaHost = true,
                    },
                    caps = new
                    {
                        haptics = false,          // haptics v2 is not merged; toy-* are stubs below
                        brainDrain = BrainDrainAllowed(),
                        camera = false,           // no webcam bridge into the page in v1
                        video = true,
                    },
                    consent = new
                    {
                        liveDurationSec = consent.LiveDurationSec,
                        toyCap = consent.ToyCap,
                        payloadMinGapMs = consent.PayloadMinGapMs,
                    },
                    fullscreen = _host?.IsFullscreen == true,
                });

                // The user's own images/videos, as ccp.assets URLs. Reuses the DtRH manifest
                // builder verbatim (asset-tree deselection, size caps, sampling) — one enumerator
                // for every web core.
                var m = DtrhAssetManifest.Build();
                _host?.Post(new
                {
                    type = "manifest",
                    images = m.Images.Select(e => new { name = e.Name, url = e.Url }),
                    videos = m.Videos.Select(e => new { name = e.Name, url = e.Url }),
                    skipped = m.Skipped,
                    truncated = m.Truncated,
                });

                // ...and tell the page which window it is actually painted in. Its affordances read
                // the echoed state, never the requested one.
                if (_host != null) _host.Post(new { type = "fullscreen", on = _host.IsFullscreen });
                App.Logger?.Information("GoonHostService: sent init + manifest ({I} images, {V} videos)",
                    m.Images.Count, m.Videos.Count);
            }
            catch (Exception ex) { App.Logger?.Warning("GoonHostService.OnPageReady: {E}", ex.Message); }
        }

        // ============================ page messages ============================

        // =====================================================================================
        // SAFETY RAIL — READ BEFORE ADDING A CASE.
        // No message from this page may ever reach a panic verb, strict lockdown, the tray, a
        // session start/stop, wallpaper, autonomy, mind-wipe or the hypnotube. That ban is the
        // Goon Game's design constraint (GoonPayloadKind is the WHOLE set of things a duel may
        // dispatch, and Esc must stay mapped to Mercy for the length of a match). The switch below
        // is deliberately tiny: bridge, window mode, teardown, and two logged stubs. Anything that
        // takes the screen, the input stack or the user's session away from them belongs nowhere
        // near a surface the opponent can influence.
        // =====================================================================================
        private static void OnPageMessage(JObject o)
        {
            switch ((string?)o["type"])
            {
                case "heartbeat":
                case "pong":
                    _lastHeartbeatUtc = DateTime.UtcNow;
                    break;
                case "boot-error":
                    OnBootError((string?)o["msg"]);
                    break;
                case "fullscreen-set":   // pause menu / F11: C# owns the borderless toggle
                    ApplyHostFullscreen((bool?)o["on"] ?? false);
                    break;
                case "exit":             // page-initiated wind-down (its own exit affordance)
                    _exiting = true;
                    ArmExitWatchdog();
                    break;
                case "exit-done":
                    DisposeAll();
                    break;
                case "toy-pattern":
                case "toy-stop":
                    // STUB. The haptics v2 multi-toy director is not merged yet, so a toy cue is
                    // logged and dropped. Deliberately a no-op rather than a call into the v1
                    // haptics path: the consent sheet's toyCap is enforced by the mixer that
                    // doesn't exist here, and an uncapped buzz is exactly the thing the sheet is
                    // for. Wire this to the director when the overhaul lands.
                    App.Logger?.Debug("GoonHostService: {Type} (haptics stub, no-op)", (string?)o["type"]);
                    break;
                case "match-result":
                    // STUB. XP / progression wiring comes with the client ledger; log it so a
                    // play-test can see the duel actually ended and with what.
                    App.Logger?.Information("GoonHostService: match-result (stub, not scored yet): {R}",
                        o["result"]?.ToString(Newtonsoft.Json.Formatting.None));
                    break;
                case "net-post":
                    OnNetPost(o);
                    break;
            }
        }

        // ============================ window mode ============================

        /// <summary>Page-driven fullscreen, mirroring the intake host: the page asks C# to
        /// borderless-toggle its OWN window instead of calling the browser Fullscreen API, whose
        /// first Escape a page cannot preventDefault — and Escape is Mercy for the whole match.
        /// The REAL resulting state is echoed back and persisted.</summary>
        private static void ApplyHostFullscreen(bool on)
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    if (_host == null) return;
                    _host.SetFullscreen(on);
                    _host.Post(new { type = "fullscreen", on = _host.IsFullscreen });
                    var settings = App.Settings?.Current;
                    if (settings != null && settings.GoonFullscreen != _host.IsFullscreen)
                    {
                        settings.GoonFullscreen = _host.IsFullscreen;
                        App.Settings?.Save();
                    }
                }
                catch (Exception ex) { App.Logger?.Debug("GoonHostService.fullscreen: {E}", ex.Message); }
            });
        }

        // ============================ host-proxied HTTP bridge ============================

        /// <summary>
        /// <c>net-post { id, path, body }</c> — POST to <see cref="ProxyBaseUrl"/> + path on the
        /// page's behalf, because CORS for the ccp.game origin isn't deployed yet. Mirrors
        /// <c>GoonSignalingClient.DefaultPostAsync</c>: X-Auth-Token per request (the token can be
        /// refreshed mid-session, so it is never baked into default headers), X-Client-Version,
        /// JSON content type, 40 s timeout.
        ///
        /// PATH WHITELIST, NOT A CONVENIENCE: without it this handler is an open HTTP proxy that
        /// signs whatever the page asks with the user's auth token. Only <c>/v2/goon/*</c> — the
        /// duel's own endpoints — is ever forwarded; anything else fails closed as
        /// <c>status:0, body:"forbidden_path"</c>, the same shape a transport failure produces, so
        /// the page needs no special case for it.
        /// </summary>
        private static void OnNetPost(JObject o)
        {
            var id = (string?)o["id"] ?? "";
            var path = (string?)o["path"] ?? "";
            var body = o["body"]?.Type == JTokenType.String
                ? (string?)o["body"] ?? ""
                : o["body"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "";

            if (!path.StartsWith(AllowedPathPrefix, StringComparison.Ordinal))
            {
                App.Logger?.Warning("GoonHostService: net-post REJECTED for path '{Path}'", path);
                ReplyNetPost(id, 0, "forbidden_path");
                return;
            }

            _ = Task.Run(async () =>
            {
                int status = 0;
                string responseBody = "";
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, ProxyBaseUrl + path)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json")
                    };
                    var token = SafeAuthToken();
                    if (!string.IsNullOrEmpty(token)) request.Headers.Add("X-Auth-Token", token);
                    request.Headers.Add("X-Client-Version", UpdateService.AppVersion);

                    using var response = await Http.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
                    status = (int)response.StatusCode;
                    responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // status 0 = transport failure (timeout, DNS, offline). The page treats it the
                    // same way the C# client treats a null result: retry or surface "offline".
                    status = 0;
                    responseBody = "";
                    App.Logger?.Warning("GoonHostService: net-post {Path} failed: {E}", path, ex.Message);
                }
                ReplyNetPost(id, status, responseBody);
            });
        }

        /// <summary>Post the bridge reply back on the UI thread (WebView2 is thread-affine).</summary>
        private static void ReplyNetPost(string id, int status, string body)
        {
            try
            {
                var disp = Application.Current?.Dispatcher;
                if (disp == null || disp.HasShutdownStarted) return;
                disp.BeginInvoke(() =>
                {
                    try { _host?.Post(new { type = "net-post-result", id, status, body }); }
                    catch (Exception ex) { App.Logger?.Debug("GoonHostService.ReplyNetPost: {E}", ex.Message); }
                });
            }
            catch (Exception ex) { App.Logger?.Debug("GoonHostService.ReplyNetPost dispatch: {E}", ex.Message); }
        }

        // ============================ init payload helpers ============================

        /// <summary>The CCP auth token the goon signaling already sends (DPAPI-backed
        /// <c>SecureAuthTokenStore</c> behind <c>AppSettings.AuthToken</c>) — NOT the Patreon
        /// bearer. Empty when the user has no cloud session; the page then shows the
        /// sign-in-required state instead of failing every call.</summary>
        private static string SafeAuthToken()
        {
            try { return App.Settings?.Current?.AuthToken ?? string.Empty; }
            catch { return string.Empty; }
        }

        /// <summary>The same name the C# duel puts in its HelloMsg
        /// (<c>GoonMatchService</c>: <c>AppSettings.UserDisplayName</c>), so the opponent sees one
        /// identity regardless of which client the player used.</summary>
        private static string SafeDisplayName()
        {
            try
            {
                var name = App.Settings?.Current?.UserDisplayName;
                return string.IsNullOrWhiteSpace(name) ? "Player" : name!;
            }
            catch { return "Player"; }
        }

        /// <summary>May the page draft/render Brain Drain? Gated on the one flag that gates
        /// <c>OverlayService.StartBrainDrainBlur</c> — <c>OverlayService.BrainDrainWithheld</c>,
        /// the same flag that hides the effect from the Deeper pickers while it is reworked.
        /// While it is true this returns false and the page must leave the element out of its
        /// draft pool (rather than drafting an element that would silently never fire).</summary>
        private static bool BrainDrainAllowed()
        {
            try { return !OverlayService.BrainDrainWithheld; }
            catch { return false; }
        }

        // ============================ watchdogs / recovery ============================

        private static void StartHeartbeatWatch()
        {
            StopHeartbeatWatch();
            _lastHeartbeatUtc = DateTime.UtcNow;
            _heartbeatWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _heartbeatWatch.Tick += (_, _) =>
            {
                // Only after the page is live (it beats via rAF once booted) so a still-loading
                // page can't false-trip.
                if (_host == null || !_host.IsReady || _exiting) return;
                var silent = (DateTime.UtcNow - _lastHeartbeatUtc).TotalSeconds;
                if (silent > 20)
                {
                    App.Logger?.Warning("GoonHostService: page heartbeat silent >20s - recovering");
                    Recover("heartbeat-silent");
                    return;
                }
                // Prod a quiet-but-alive page before writing it off: a pong resets the clock and
                // costs one message, whereas a false recovery costs a live match.
                if (silent > 8)
                {
                    try { _host.Post(new { type = "ping" }); }
                    catch (Exception ex) { App.Logger?.Debug("GoonHostService: ping failed: {E}", ex.Message); }
                }
            };
            _heartbeatWatch.Start();
        }

        private static void StopHeartbeatWatch()
        {
            try { _heartbeatWatch?.Stop(); } catch { }
            _heartbeatWatch = null;
        }

        private static void OnProcessFailed(CoreWebView2ProcessFailedKind kind) => Recover($"process-failed:{kind}");

        /// <summary>Relaunch once per session; a second failure gives up cleanly.</summary>
        private static void Recover(string reason)
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null) { DisposeAll(); return; }
            disp.BeginInvoke(() =>
            {
                var retry = !_relaunchedOnce;
                App.Logger?.Warning("GoonHostService: recovery ({Reason}) - {Action}",
                    reason, retry ? "relaunching once" : "giving up");
                DisposeAll();
                if (retry)
                {
                    _relaunchedOnce = true;
                    // Come back WINDOWED regardless of the remembered mode: the page just wedged
                    // or died, and a titled window still has a close button if it does it again.
                    _recoveryWindowed = true;
                    Launch(_duckPreference);
                }
            });
        }

        private static void OnBootError(string? msg)
        {
            App.Logger?.Warning("GoonHostService: page boot-error: {Msg}", msg);
            BootFailedThisSession = true;
            var disp = Application.Current?.Dispatcher;
            if (disp == null) { DisposeAll(); return; }
            disp.BeginInvoke(() =>
            {
                DisposeAll();
                try
                {
                    MessageBox.Show(
                        $"{ProductName} could not start on this machine.\n\n{msg}",
                        ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch { }
            });
        }

        // ============================ teardown ============================

        private static void ArmExitWatchdog()
        {
            CancelExitWatchdog();
            _exitWatchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _exitWatchdog.Tick += (_, _) => DisposeAll();
            _exitWatchdog.Start();
        }

        private static void CancelExitWatchdog()
        {
            try { _exitWatchdog?.Stop(); } catch { }
            _exitWatchdog = null;
        }

        private static void DisposeAll()
        {
            if (_disposing) return;   // _host.Dispose() closes the window, re-raising Closed -> here
            _disposing = true;
            try
            {
                CancelExitWatchdog();
                StopHeartbeatWatch();
                // Last word to the page before the window goes: a live match should get a chance
                // to post its own abandon rather than just vanishing from the opponent's side.
                try { _host?.Post(new { type = "end-run", reason = "dispose" }); } catch { }
                try { _host?.Dispose(); } catch { }
                _host = null;
                _exiting = false;
                RestoreMainWindow();
                App.Logger?.Information("GoonHostService: closed");
            }
            finally { _disposing = false; }
        }
    }
}
