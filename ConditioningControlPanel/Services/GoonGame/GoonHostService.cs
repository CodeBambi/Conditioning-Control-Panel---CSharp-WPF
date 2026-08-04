using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    ///                  cache-state · cache-list · cache-progress · encode-request ·
    ///                  cache-put-result         (transfer cache — <see cref="GoonCacheBridge"/>)
    ///                  goon-recv-result { id, ok, url, bytes, error }
    ///                  discord { avatarState, avatarDataUri, dmShared, richPresence,
    ///                            seenSharePrompt }
    ///                  peer-card { name, avatarDataUri, reason, dm, ver }
    ///                                           (Discord sharing — docs/GOON_DISCORD_CONTRACT.md §4)
    ///   Page -&gt; Host:  ready · log              (consumed inside ChaosWebViewHost)
    ///                  heartbeat { t, paint, vis } · pong · boot-error · fullscreen-set
    ///                  exit · exit-done
    ///                  toy-pattern · toy-stop   (STUBS — haptics v2 is not merged)
    ///                  match-result             (STUB — XP wiring comes later)
    ///                  net-post { id, path, body }
    ///                  cache-req { op } · cache-put · encode-done
    ///                                           (transfer cache — <see cref="GoonCacheBridge"/>)
    ///                  goon-recv-begin/chunk/commit/abort/drop
    ///                                           (received inbox — <see cref="TransferInboxStore"/>)
    ///                  discord-prefs · peer-card-req · discord-open-dm ·
    ///                  discord-link-request · rp-state · last-opponent-clear
    ///                                           (Discord sharing — docs/GOON_DISCORD_CONTRACT.md §4)
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

        /// <summary>Seconds of "beats arriving, frame counter frozen, page says it is visible"
        /// before the page is treated as visually frozen. Ten seconds is far past any legitimate
        /// hitch (a 4K shader resize, a video decode stutter, a GC pause) and far short of how long
        /// the owner sat in front of a dead picture on 2026-08-04.</summary>
        private const double PaintStallSeconds = 10;

        private static ChaosWebViewHost? _host;
        private static DispatcherTimer? _heartbeatWatch;
        private static DispatcherTimer? _exitWatchdog;
        private static DateTime _lastHeartbeatUtc;
        /// <summary>Last frame count the page stamped on a heartbeat; null = it has never sent one
        /// (no rAF on this host), which switches the paint-stall rule OFF rather than tripping it.</summary>
        private static long? _lastPaint;
        /// <summary>When <see cref="_lastPaint"/> last MOVED — or when the page last told us it was
        /// not visible, which is a legitimate reason to stop painting and must not accumulate.</summary>
        private static DateTime _lastPaintMoveUtc;
        /// <summary>One paint-stall recovery per watch. The tick runs every 5s and
        /// <see cref="Recover"/> is dispatched asynchronously, so without this the same stall would
        /// queue several relaunches before the first one lands.</summary>
        private static bool _paintStallHandled;
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

                // BEFORE the mappings list: AddIfPresent silently DROPS a mapping whose folder is
                // missing, and a cold install has no transfer-cache yet - without this the
                // ccp.cache vhost would be dead for the whole session (trap 10).
                try
                {
                    Transfer.TransferCacheStore.Instance.EnsureRoot();
                    Transfer.TransferCompressionService.Instance.Initialize();   // idempotent
                }
                catch (Exception ex) { App.Logger?.Warning("GoonHostService: transfer cache init: {E}", ex.Message); }

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
                // ONE vhost for the whole transfer cache: art/ + prv/ (the user's own compressed
                // copies) and recv/ (what a partner sent). The .part staging area is a SIBLING of
                // this root on purpose, so a half-written file is never page-reachable.
                AddIfPresent(mappings, "ccp.cache", Transfer.TransferCacheStore.Instance.Root);

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
                // A reloaded page counts its own frames from zero: forget the old page's stamp
                // rather than compare across two documents.
                _lastPaint = null;
                _lastPaintMoveUtc = DateTime.UtcNow;
                _paintStallHandled = false;
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
                        spiral = true,            // in-page spiral veil; exec/ owns the renderer
                        camera = false,           // no webcam bridge into the page in v1
                        video = true,
                        mediaTransfer = TransferAllowed(),
                    },
                    consent = new
                    {
                        liveDurationSec = consent.LiveDurationSec,
                        toyCap = consent.ToyCap,
                        payloadMinGapMs = consent.PayloadMinGapMs,
                    },
                    fullscreen = _host?.IsFullscreen == true,
                    // Discord sharing state (contract §4). Built from the CACHE only — init is
                    // posted synchronously and an avatar may never sit on a boot path. If the
                    // cached own-avatar is stale/absent, KickOwnAvatarRefresh below fetches it and
                    // the page gets a `discord` echo when it lands; the page's box is reserved from
                    // first paint either way, so nothing shifts.
                    discord = BuildDiscordBlock(includeLastOpponent: true),
                });
                // ...and top the own avatar up off-thread. No-op unless the user is linked AND
                // sharing, so a player who shares nothing never touches the Discord CDN.
                KickOwnAvatarRefresh();

                // The user's own images/videos, as ccp.assets URLs. Reuses the DtRH manifest
                // builder verbatim (asset-tree deselection, size caps, sampling) — one enumerator
                // for every web core.
                var m = DtrhAssetManifest.Build();
                // `received` rides the EXISTING manifest frame rather than a new boot milestone:
                // boot.js already gates settle() on gotManifest, so the received-artifact set is
                // primed before any screen mounts - which is what lets the very first offer of a
                // match answer decline:'have' instead of re-transferring (spec 6.4).
                var received = SafeReceivedList();
                _host?.Post(new
                {
                    type = "manifest",
                    images = m.Images.Select(e => new { name = e.Name, url = e.Url }),
                    videos = m.Videos.Select(e => new { name = e.Name, url = e.Url }),
                    skipped = m.Skipped,
                    truncated = m.Truncated,
                    received,
                });

                // The compression cache's own feed. Attached AFTER the manifest so the page has its
                // pool before the first cache-state lands, and pruned here because "page ready" is
                // one of exactly two moments when nothing can be mid-transfer.
                GoonCacheBridge.Attach(_host);
                try { TransferInboxStore.Instance.PruneSafe(); }
                catch (Exception ex) { App.Logger?.Debug("GoonHostService: inbox prune: {E}", ex.Message); }

                // ...and tell the page which window it is actually painted in. Its affordances read
                // the echoed state, never the requested one.
                if (_host != null) _host.Post(new { type = "fullscreen", on = _host.IsFullscreen });
                App.Logger?.Information(
                    "GoonHostService: sent init + manifest ({I} images, {V} videos, {R} received)",
                    m.Images.Count, m.Videos.Count, received.Count);
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
        //
        // WHY THE STORAGE VERBS ARE ADMISSIBLE (cache-req/cache-put/encode-done, goon-recv-*).
        // They are file-scoped, they fail closed, and they NEVER touch the user's originals — the
        // compression queue only ever READS App.EffectiveAssetsPath and only ever WRITES inside
        // transfer-cache/ (+ its sibling tmp). Critically, none of them can NAME a file: the host
        // mints every filename from a job id it dispatched (lane B) or from a sha256 it computed
        // itself over the bytes on disk (received inbox), so no page- or peer-supplied string
        // reaches Path.Combine and traversal is not a thing that can be attempted. They touch no
        // session, overlay, panic or input surface, and the worst a hostile page can achieve is
        // filling a capped, user-deletable cache folder.
        //
        // WHY THE DISCORD-SHARING VERBS ARE ADMISSIBLE (contract §4). None of them takes the
        // screen, the input stack or the session; all of them fail closed; and the two that carry
        // a real secret keep it OUT of the page entirely.
        //   * discord-prefs  — writes five booleans the user owns (GoonShareAvatar /
        //     GoonShareDiscordDm / GoonRichPresence / GoonSeenSharePrompt). Each one only ever
        //     REDUCES or restores what the local user exposes about themselves; none reveals
        //     anything TO the page, none touches another user's data, and there is no value of
        //     them that starts a session, an overlay or a network identity. The page must read the
        //     ECHO, so a write it isn't allowed to make simply doesn't come back.
        //   * peer-card-req  — the host, not the page, chooses the URL: the path is the compile-time
        //     constant PeerCardPath, so this cannot be steered into the net-post proxy's general
        //     shape. It is fire-and-forget with a 3 s budget and one retry, so it can never gate the
        //     lobby, the countdown or Live. The response's snowflake is stored in a private static
        //     field and DELIBERATELY stripped from the `peer-card` frame — the page sees a boolean.
        //   * discord-open-dm — the page supplies a two-value ENUM ("peer"|"last"), never an id.
        //     The host resolves the snowflake from its own store, re-validates it as ≤20 digits,
        //     and shell-opens a fixed discord.com/users/ URL. A hostile page's whole reachable
        //     surface is "open the DM the user already has, or nothing".
        //   * discord-link-request — restores main and calls ShowTab("discord"). It MUST NOT start
        //     OAuth: an auto-started login is a credential prompt the page could summon at will, so
        //     the verb is deliberately navigation-only. BANNED for this bridge, permanently:
        //     StartOAuthFlowAsync, Logout, token reads, and any /discord/* call.
        //   * rp-state — an ENUM (lobby|live|recap|off) mapped to FIXED presence strings. It is
        //     dropped outright unless GoonRichPresence is on, so a page cannot publish anything
        //     about the user who did not ask for it, and it can never carry free text or a name.
        //   * last-opponent-clear — deletes local state and one cached file. Strictly destructive
        //     of the app's own data, so there is nothing to abuse.
        // =====================================================================================
        private static void OnPageMessage(JObject o)
        {
            switch ((string?)o["type"])
            {
                case "heartbeat":
                case "pong":
                    _lastHeartbeatUtc = DateTime.UtcNow;
                    NotePaintStamp(o);
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
                    // The last-opponent record is written HERE, by the host, from the peer card it
                    // already fetched — no page-supplied data and no extra verb (contract §4).
                    WriteLastOpponentRecord();
                    break;
                case "net-post":
                    OnNetPost(o);
                    break;
                case "cache-req":        // assets screen: state/list/queue control
                case "cache-put":        // lane-B (page WebCodecs) artifact bytes coming back
                case "encode-done":      // ...and the commit for them
                    GoonCacheBridge.OnMessage(o);
                    break;
                case "goon-recv-begin":
                case "goon-recv-chunk":
                case "goon-recv-commit":
                case "goon-recv-abort":
                case "goon-recv-drop":
                    OnRecvVerb(o);
                    break;
                // ---- Discord sharing (contract §4; admissibility in the banner above) ----
                case "discord-prefs":
                    OnDiscordPrefs(o);
                    break;
                case "peer-card-req":
                    OnPeerCardRequest(o);
                    break;
                case "discord-open-dm":
                    OnDiscordOpenDm(o);
                    break;
                case "discord-link-request":
                    OnDiscordLinkRequest();
                    break;
                case "rp-state":
                    OnRichPresenceState(o);
                    break;
                case "last-opponent-clear":
                    OnLastOpponentClear();
                    break;
            }
        }

        // ============================ received-artifact inbox ============================

        /// <summary>
        /// <c>goon-recv-begin/chunk/commit/abort/drop</c> — the page's disk backend for artifacts a
        /// duel partner sent (spec §6.2). Every one of them replies with the single
        /// <c>goon-recv-result { id, ok, url, bytes, error }</c> shape, which
        /// <c>exec/receivedStore.js</c> registers and correlates by <c>id</c>.
        ///
        /// The page's sha256 is a CLAIM until commit, where the host hashes the file it actually
        /// wrote and compares; the page's mime is a CLAIM until commit, where the magic bytes decide
        /// the extension and a disagreement is a rejection rather than a relabel. Commit runs the
        /// hash on a worker (up to 24 MB) so a duel's frame budget is not spent on it.
        /// Error vocabulary: bad-name | too-big | bad-format | hash-mismatch | cap-reached |
        /// io-failed | bad-seq | unknown-job.
        /// </summary>
        private static void OnRecvVerb(JObject o)
        {
            var type = (string?)o["type"] ?? "";
            var id = (string?)o["id"] ?? "";
            var store = TransferInboxStore.Instance;
            try
            {
                switch (type)
                {
                    case "goon-recv-begin":
                    {
                        var err = store.Begin(id, (string?)o["sha256"], (string?)o["mime"],
                            (long?)o["bytes"] ?? 0);
                        ReplyRecv(id, err == null, null, 0, err);
                        break;
                    }
                    case "goon-recv-chunk":
                    {
                        var err = store.AppendChunk(id, (int?)o["seq"] ?? -1, (string?)o["b64"]);
                        ReplyRecv(id, err == null, null, 0, err);
                        break;
                    }
                    case "goon-recv-commit":
                    {
                        // Off the UI thread: this is a full SHA-256 over up to 24 MB.
                        _ = Task.Run(() =>
                        {
                            var r = store.Commit(id);
                            ReplyRecv(id, r.Ok, r.Url, r.Ok ? SafeFileLength(r.Sha, r.Ext) : 0, r.Error);
                        });
                        break;
                    }
                    case "goon-recv-abort":
                        store.Abort(id);
                        ReplyRecv(id, true, null, 0, null);
                        break;
                    case "goon-recv-drop":
                    {
                        var sha = (string?)o["sha256"];
                        var ok = store.Drop(sha);
                        ReplyRecv(id.Length > 0 ? id : sha ?? "", ok, null, 0, ok ? null : "bad-name");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("GoonHostService.{Type}: {E}", type, ex.Message);
                ReplyRecv(id, false, null, 0, "io-failed");
            }
        }

        private static long SafeFileLength(string sha, string ext)
        {
            try
            {
                var p = Path.Combine(TransferInboxStore.Instance.RecvDir, sha + "." + ext);
                return File.Exists(p) ? new FileInfo(p).Length : 0;
            }
            catch { return 0; }
        }

        /// <summary>Post the inbox reply on the UI thread (WebView2 is thread-affine), mirroring
        /// <see cref="ReplyNetPost"/>.</summary>
        private static void ReplyRecv(string id, bool ok, string? url, long bytes, string? error)
        {
            try
            {
                var disp = Application.Current?.Dispatcher;
                if (disp == null || disp.HasShutdownStarted) return;
                disp.BeginInvoke(() =>
                {
                    try { _host?.Post(new { type = "goon-recv-result", id, ok, url, bytes, error }); }
                    catch (Exception ex) { App.Logger?.Debug("GoonHostService.ReplyRecv: {E}", ex.Message); }
                });
            }
            catch (Exception ex) { App.Logger?.Debug("GoonHostService.ReplyRecv dispatch: {E}", ex.Message); }
        }

        /// <summary>What this machine already holds, for the manifest frame. Never throws — a dead
        /// inbox must cost a re-transfer, not the whole boot.</summary>
        private static IReadOnlyList<object> SafeReceivedList()
        {
            try { return TransferInboxStore.Instance.ListForManifest(); }
            catch (Exception ex)
            {
                App.Logger?.Warning("GoonHostService: received list failed: {E}", ex.Message);
                return Array.Empty<object>();
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

        /// <summary>May the page draft/render Brain Drain? ALWAYS, as of 2026-08-03 (owner call).
        ///
        /// GG DIVERGES from <c>OverlayService.BrainDrainWithheld</c> ON PURPOSE. That flag gates
        /// <c>OverlayService.StartBrainDrainBlur</c> — the NATIVE, desktop-wide blur/melt overlay
        /// that is withheld from the app's pickers while it is reworked. The duel's drain is a
        /// different thing entirely: an in-page veil drawn by <c>exec/brainDrain.js</c> inside the
        /// WebView, scoped to the duel window, ending with the match. Withholding the native
        /// overlay says nothing about the page's veil, so the element is advertised unconditionally
        /// and the drain is a real part of the duel. (Kept as a method rather than an inlined
        /// <c>true</c> so the gate has one place to come back to if that ever changes.)</summary>
        private static bool BrainDrainAllowed() => true;

        /// <summary>May the page send its OWN media to the opponent? PREMIUM ONLY.
        ///
        /// Unlike <see cref="BrainDrainAllowed"/> (unconditionally true because the duel's drain is
        /// an in-page veil), this is a real entitlement gate: transferring the user's own library to
        /// another machine is the supporter-facing half of the feature. RECEIVING is NOT gated, so a
        /// free player still sees a supporter's media — which is what makes the feature worth
        /// buying. The page checks this with <c>=== true</c>, so a host that predates the flag
        /// defaults the SEND capability OFF rather than on.</summary>
        private static bool TransferAllowed()
        {
            try { return App.Patreon?.HasPremiumAccess == true; }
            catch { return false; }
        }

        // ============================ Discord sharing (contract §4/§5) ============================

        /// <summary>The peer-card endpoint. A COMPILE-TIME CONSTANT on purpose: unlike net-post,
        /// the page never names this URL, so there is no path to whitelist and nothing to steer.
        /// It happens to sit under <see cref="AllowedPathPrefix"/> as well, which is the point —
        /// this is the duel's own surface, not a general proxy.</summary>
        private const string PeerCardPath = "/v2/goon/peercard";

        /// <summary>The current match peer's Discord snowflake, or null when they did not share it.
        /// PRIVACY BOUNDARY: this field, the last-opponent record and the shell-opened URL are the
        /// only three places it ever exists. It is never posted to the page (the `peer-card` frame
        /// carries a BOOLEAN), never logged, and never written anywhere else.</summary>
        private static string? _peerDmId;
        private static string? _peerName;
        private static bool _peerAvatarCached;
        private static bool _peerCardFetched;
        private static int _peerCardInFlight;   // Interlocked: one fetch at a time, no lock on the UI thread

        // ---------------------------------------------------------------- init/echo payloads

        /// <summary>The `discord` block for init (and, minus lastOpponent, for the echo). Reads the
        /// avatar from the DISK CACHE only — see the call site in <see cref="OnPageReady"/>.</summary>
        private static JObject BuildDiscordBlock(bool includeLastOpponent)
        {
            var block = new JObject
            {
                ["avatarState"] = "unlinked",
                ["avatarDataUri"] = JValue.CreateNull(),
                ["dmShared"] = false,
                ["richPresence"] = false,
                ["seenSharePrompt"] = false,
            };
            try
            {
                var s = App.Settings?.Current;
                var d = App.Discord;
                var linked = d != null && d.IsAuthenticated && !string.IsNullOrEmpty(d.UserId);
                var shareAvatar = s?.GoonShareAvatar == true;
                // Three states, not two: "off" (linked, chose not to share) and "unlinked" (nothing
                // to share) read completely differently in the lobby, and only one of them has a
                // useful call to action.
                block["avatarState"] = !linked ? "unlinked" : (shareAvatar ? "shared" : "off");
                block["dmShared"] = s?.GoonShareDiscordDm == true;
                block["richPresence"] = s?.GoonRichPresence == true;
                block["seenSharePrompt"] = s?.GoonSeenSharePrompt == true;
                if (linked && shareAvatar)
                {
                    var uri = GoonAvatarCache.ReadOwnDataUriIfFresh(d?.Avatar);
                    if (uri != null) block["avatarDataUri"] = uri;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("GoonHostService.BuildDiscordBlock: {E}", ex.Message); }

            if (includeLastOpponent) block["lastOpponent"] = BuildLastOpponentBlock();
            return block;
        }

        /// <summary>`{ name, avatarDataUri, dm, ts }` or null. The stored record's `dmId` is
        /// FLATTENED to a boolean here — the page is never told the snowflake exists as a value,
        /// only that a Message button is possible.</summary>
        private static JToken BuildLastOpponentBlock()
        {
            try
            {
                var raw = App.Settings?.Current?.GoonLastOpponentJson;
                if (string.IsNullOrWhiteSpace(raw)) return JValue.CreateNull();
                var rec = JObject.Parse(raw!);
                var name = (string?)rec["name"];
                if (string.IsNullOrWhiteSpace(name)) return JValue.CreateNull();

                string? uri = null;
                // Only the ONE bare filename this cache ever writes is accepted; a record naming
                // anything else is treated as having no picture rather than being followed.
                if ((string?)rec["avatarFile"] == GoonAvatarCache.LastOpponentFile)
                    uri = GoonAvatarCache.ReadDataUri(GoonAvatarCache.LastOpponentFile);

                return new JObject
                {
                    ["name"] = name,
                    ["avatarDataUri"] = uri == null ? JValue.CreateNull() : (JToken)uri,
                    ["dm"] = !string.IsNullOrEmpty((string?)rec["dmId"]),
                    ["ts"] = (long?)rec["ts"] ?? 0L,
                };
            }
            catch (Exception ex)
            {
                // A corrupt record is a missing record: never a boot failure.
                App.Logger?.Debug("GoonHostService.BuildLastOpponentBlock: {E}", ex.Message);
                return JValue.CreateNull();
            }
        }

        /// <summary>Echo the current sharing state. Every page affordance reads THIS, never the
        /// request it sent — the same rule the fullscreen toggle follows.</summary>
        private static void PostDiscordEcho()
        {
            try
            {
                var block = BuildDiscordBlock(includeLastOpponent: false);
                block["type"] = "discord";
                _host?.Post(block);
            }
            catch (Exception ex) { App.Logger?.Debug("GoonHostService.PostDiscordEcho: {E}", ex.Message); }
        }

        /// <summary>Top up the cached own-avatar off-thread and echo when it lands. No-op unless the
        /// user is linked AND sharing: a player who shares nothing never touches the CDN.</summary>
        private static void KickOwnAvatarRefresh()
        {
            try
            {
                if (App.Settings?.Current?.GoonShareAvatar != true) return;
                if (App.Discord?.IsAuthenticated != true) return;
                _ = Task.Run(async () =>
                {
                    var uri = await GoonAvatarCache.RefreshOwnAvatarAsync().ConfigureAwait(false);
                    if (uri == null) return;
                    var disp = Application.Current?.Dispatcher;
                    if (disp == null || disp.HasShutdownStarted) return;
                    disp.BeginInvoke(() => { try { PostDiscordEcho(); } catch { } });
                });
            }
            catch (Exception ex) { App.Logger?.Debug("GoonHostService.KickOwnAvatarRefresh: {E}", ex.Message); }
        }

        // ---------------------------------------------------------------- discord-prefs

        /// <summary><c>discord-prefs { shareAvatar?, shareDm?, richPresence?, seenSharePrompt? }</c>
        /// — every field OPTIONAL, so the page can move one toggle without restating the others.
        /// Writes, pushes the profile sync on change (the two shared flags only), then echoes.</summary>
        private static void OnDiscordPrefs(JObject o)
        {
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return;

                var sharedChanged = false;
                var rpTurnedOff = false;

                var a = (bool?)o["shareAvatar"];
                if (a.HasValue && s.GoonShareAvatar != a.Value) { s.GoonShareAvatar = a.Value; sharedChanged = true; }

                var dm = (bool?)o["shareDm"];
                if (dm.HasValue && s.GoonShareDiscordDm != dm.Value) { s.GoonShareDiscordDm = dm.Value; sharedChanged = true; }

                var rp = (bool?)o["richPresence"];
                if (rp.HasValue && s.GoonRichPresence != rp.Value)
                {
                    s.GoonRichPresence = rp.Value;
                    rpTurnedOff = !rp.Value;
                }

                var seen = (bool?)o["seenSharePrompt"];
                if (seen.HasValue && s.GoonSeenSharePrompt != seen.Value) s.GoonSeenSharePrompt = seen.Value;

                App.Settings?.Save();

                // Turning the flag off mid-session must retract the presence NOW. rp-state is
                // dropped while the flag is off, so the page's own "off" would never arrive.
                if (rpTurnedOff)
                {
                    try { App.DiscordRpc?.SetGoonActivity("off"); }
                    catch (Exception ex) { App.Logger?.Debug("GoonHostService: rp retract: {E}", ex.Message); }
                }

                if (sharedChanged)
                {
                    // Push-on-change (contract §2, RemoteControl precedent): the server's room
                    // snapshot is taken at invite/join, so a flag flipped in the lobby has to reach
                    // it before the next match rather than at the next scheduled sync. The service's
                    // own 30 s cooldown may defer it; the flags ride the normal sync body too, so a
                    // deferred push costs latency, never correctness.
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var svc = App.ProfileSync;
                            if (svc != null) await svc.SyncProfileAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex) { App.Logger?.Debug("GoonHostService: prefs sync push: {E}", ex.Message); }
                    });
                    if (s.GoonShareAvatar) KickOwnAvatarRefresh();
                }

                PostDiscordEcho();
            }
            catch (Exception ex) { App.Logger?.Warning("GoonHostService.discord-prefs: {E}", ex.Message); }
        }

        // ---------------------------------------------------------------- peer-card-req

        /// <summary>
        /// <c>peer-card-req</c> — the HOST fetches the peer card itself (contract §4). Fire-and-forget
        /// with a 3 s budget and exactly one retry: this may never gate the lobby, the countdown or
        /// Live, so a failure posts a <c>peer-card</c> with <c>reason:"error"</c> and the page falls
        /// back to its initial-letter tile.
        ///
        /// DEVIATION (reported, not renamed): the contract writes the payload as <c>{}</c>, but
        /// /v2/goon/peercard is roomAuth(requireJoined=true) and only the PAGE holds the room
        /// credentials — the host never joined a room. The optional <c>code</c>/<c>token</c>/<c>role</c>
        /// fields below are therefore read when present, which is a superset of <c>{}</c> and leaves
        /// the frozen verb name and response shape untouched. They are room credentials the page
        /// obtained for itself, they cannot name a path (see <see cref="PeerCardPath"/>), and the
        /// snowflake still never travels back to the page.
        /// </summary>
        private static void OnPeerCardRequest(JObject o)
        {
            // One in flight at a time. A duplicated request during a countdown must cost nothing.
            if (Interlocked.CompareExchange(ref _peerCardInFlight, 1, 0) != 0)
            {
                App.Logger?.Debug("GoonHostService: peer-card-req ignored (already in flight)");
                return;
            }

            // A NEW request means a NEW peer: drop the previous one's card first, or a failed fetch
            // for match 2 would let match 1's opponent be written as match 2's last-opponent record.
            ResetPeerCardState();

            var code = SafeShort((string?)o["code"], 32);
            var token = SafeShort((string?)o["token"], 128);
            var role = SafeShort((string?)o["role"], 16);

            _ = Task.Run(async () =>
            {
                try
                {
                    var json = await PostPeerCardAsync(code, token, role).ConfigureAwait(false);
                    if (json == null)
                    {
                        PostPeerCard(null, null, "error", false, null);
                        return;
                    }

                    var name = (string?)json["name"];
                    var reason = (string?)json["avatar_reason"] ?? "error";
                    var ver = (string?)json["ver"];
                    var dmId = (string?)json["dm_id"];

                    _peerName = string.IsNullOrWhiteSpace(name) ? null : name!.Trim();
                    // Re-validate what the server sent before it can ever reach a shell command.
                    _peerDmId = IsSnowflake(dmId) ? dmId : null;
                    _peerAvatarCached = false;

                    string? uri = null;
                    var bytes = GoonAvatarCache.DecodeDataUri((string?)json["avatar"]);
                    if (bytes != null && GoonAvatarCache.Write(GoonAvatarCache.PeerFile, bytes))
                    {
                        _peerAvatarCached = true;
                        uri = GoonAvatarCache.ReadDataUri(GoonAvatarCache.PeerFile);
                    }
                    _peerCardFetched = true;

                    // Never the snowflake, never the CDN URL — a boolean and a reason code.
                    App.Logger?.Information(
                        "GoonHostService: peer card fetched (avatar={A}, reason={R}, dm={D})",
                        uri != null, reason, _peerDmId != null);
                    PostPeerCard(_peerName, uri, reason, _peerDmId != null, ver);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("GoonHostService.peer-card-req: {E}", ex.Message);
                    PostPeerCard(null, null, "error", false, null);
                }
                finally { Interlocked.Exchange(ref _peerCardInFlight, 0); }
            });
        }

        /// <summary>POST the peer card with a 3 s budget and one retry. Returns null on anything
        /// that isn't a parseable 2xx body — the caller's answer to that is a tile, not an error
        /// dialog.</summary>
        private static async Task<JObject?> PostPeerCardAsync(string code, string token, string role)
        {
            var body = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                unified_id = App.UnifiedUserId ?? "",
                code,
                token,
                role,
            });

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    // The shared client's 40 s timeout is for the relay long-poll; an avatar gets 3 s.
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    using var request = new HttpRequestMessage(HttpMethod.Post, ProxyBaseUrl + PeerCardPath)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json")
                    };
                    var auth = SafeAuthToken();
                    if (!string.IsNullOrEmpty(auth)) request.Headers.Add("X-Auth-Token", auth);
                    request.Headers.Add("X-Client-Version", UpdateService.AppVersion);

                    using var response = await Http.SendAsync(request, cts.Token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        // 403/429 are ANSWERS ("not joined", "rate limited"), not transport faults:
                        // retrying them just spends the 6/min gate for nothing.
                        App.Logger?.Debug("GoonHostService: peercard HTTP {S}", (int)response.StatusCode);
                        return null;
                    }
                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return JObject.Parse(text);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("GoonHostService: peercard attempt {N}/2 failed: {E}", attempt, ex.Message);
                }
            }
            return null;
        }

        /// <summary>Post the `peer-card` frame on the UI thread. The snowflake is NOT a parameter
        /// here — <paramref name="dm"/> is the whole of what the page learns.</summary>
        private static void PostPeerCard(string? name, string? avatarDataUri, string reason, bool dm, string? ver)
        {
            try
            {
                var disp = Application.Current?.Dispatcher;
                if (disp == null || disp.HasShutdownStarted) return;
                disp.BeginInvoke(() =>
                {
                    try
                    {
                        _host?.Post(new
                        {
                            type = "peer-card",
                            name = name ?? "",
                            avatarDataUri,
                            reason,
                            dm,
                            ver,
                        });
                    }
                    catch (Exception ex) { App.Logger?.Debug("GoonHostService.PostPeerCard: {E}", ex.Message); }
                });
            }
            catch (Exception ex) { App.Logger?.Debug("GoonHostService.PostPeerCard dispatch: {E}", ex.Message); }
        }

        // ---------------------------------------------------------------- last-opponent record

        /// <summary>Write `{ name, dmId, avatarFile, ts }` for the peer this match, overwriting the
        /// previous record (most recent only, contract §4). ONLY the fields the peer actually
        /// shared: no dm flag means no dmId, no avatar means no avatarFile.</summary>
        private static void WriteLastOpponentRecord()
        {
            try
            {
                if (!_peerCardFetched || string.IsNullOrWhiteSpace(_peerName)) return;
                var s = App.Settings?.Current;
                if (s == null) return;

                string? file = null;
                if (_peerAvatarCached) file = GoonAvatarCache.PromotePeerToLastOpponent();
                // A peer who shared no picture must not inherit the PREVIOUS opponent's one.
                if (file == null) GoonAvatarCache.Delete(GoonAvatarCache.LastOpponentFile);

                var rec = new JObject
                {
                    ["name"] = _peerName,
                    ["dmId"] = _peerDmId == null ? JValue.CreateNull() : (JToken)_peerDmId,
                    ["avatarFile"] = file == null ? JValue.CreateNull() : (JToken)file,   // bare name, never a path
                    ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                };
                s.GoonLastOpponentJson = rec.ToString(Newtonsoft.Json.Formatting.None);
                App.Settings?.Save();
                App.Logger?.Information("GoonHostService: last-opponent record written (avatar={A}, dm={D})",
                    file != null, _peerDmId != null);
            }
            catch (Exception ex) { App.Logger?.Warning("GoonHostService.WriteLastOpponentRecord: {E}", ex.Message); }
        }

        /// <summary><c>last-opponent-clear</c> — wipe the record and its cached picture.</summary>
        private static void OnLastOpponentClear()
        {
            try
            {
                var s = App.Settings?.Current;
                if (s != null && !string.IsNullOrEmpty(s.GoonLastOpponentJson))
                {
                    s.GoonLastOpponentJson = "";
                    App.Settings?.Save();
                }
                GoonAvatarCache.Delete(GoonAvatarCache.LastOpponentFile);
                App.Logger?.Information("GoonHostService: last-opponent record cleared");
            }
            catch (Exception ex) { App.Logger?.Warning("GoonHostService.last-opponent-clear: {E}", ex.Message); }
        }

        /// <summary>The stored opponent's snowflake, or null. Read straight off disk each time so a
        /// cleared record can never be opened from a stale in-memory copy.</summary>
        private static string? ReadLastOpponentDmId()
        {
            try
            {
                var raw = App.Settings?.Current?.GoonLastOpponentJson;
                if (string.IsNullOrWhiteSpace(raw)) return null;
                var id = (string?)JObject.Parse(raw!)["dmId"];
                return IsSnowflake(id) ? id : null;
            }
            catch { return null; }
        }

        // ---------------------------------------------------------------- open DM / link request

        /// <summary><c>discord-open-dm { which: "peer"|"last" }</c> — resolve the snowflake from the
        /// HOST's own store, un-fullscreen first (a browser opened under a borderless fullscreen
        /// window is invisible), then shell-open the fixed profile URL.</summary>
        private static void OnDiscordOpenDm(JObject o)
        {
            var which = (string?)o["which"] ?? "";
            string? id = which switch
            {
                "peer" => _peerDmId,
                "last" => ReadLastOpponentDmId(),
                _ => null,                       // enum only — a page-supplied id is not a case
            };
            if (!IsSnowflake(id))
            {
                App.Logger?.Debug("GoonHostService: discord-open-dm '{W}' - nothing to open", which);
                return;
            }

            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    // FIRST, synchronously on this thread — ApplyHostFullscreen would queue the
                    // toggle behind the shell open and the browser would land underneath.
                    UnFullscreenForShellOpen();
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://discord.com/users/" + id,
                        UseShellExecute = true,
                    });
                    // The id is never logged: this URL identifies a real person.
                    App.Logger?.Information("GoonHostService: opened Discord DM ({W})", which);
                }
                catch (Exception ex) { App.Logger?.Warning("GoonHostService: discord-open-dm failed: {E}", ex.Message); }
            });
        }

        /// <summary><c>discord-link-request</c> — hand the user back the control panel on the Discord
        /// tab and STOP. It deliberately does not call StartOAuthFlowAsync: a login the page can
        /// summon is a credential prompt the page can summon (see the banned-verbs note in the
        /// safety rail). The user presses "Connect" themselves, in the app, where they can see it.</summary>
        private static void OnDiscordLinkRequest()
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    UnFullscreenForShellOpen();
                    var main = App.MainWindowRef;
                    if (main == null) return;
                    if (main.WindowState == WindowState.Minimized)
                    {
                        main.WindowState = _mainStateBeforeDuck == WindowState.Maximized
                            ? WindowState.Maximized
                            : WindowState.Normal;
                    }
                    // The duck debt is paid here: we handed the window back, so DisposeAll must not
                    // "restore" a window the user may since have minimized on purpose.
                    _duckedMainWindow = false;
                    main.Show();
                    main.Activate();
                    main.ShowTab("discord");
                    App.Logger?.Information("GoonHostService: discord-link-request - focused Discord tab");
                }
                catch (Exception ex) { App.Logger?.Warning("GoonHostService.discord-link-request: {E}", ex.Message); }
            });
        }

        /// <summary>Drop out of borderless fullscreen so another window can be seen, WITHOUT
        /// rewriting <c>GoonFullscreen</c> — the user did not choose windowed, we did, and their
        /// remembered mode has to survive opening a DM.</summary>
        private static void UnFullscreenForShellOpen()
        {
            try
            {
                if (_host == null || !_host.IsFullscreen) return;
                _host.SetFullscreen(false);
                _host.Post(new { type = "fullscreen", on = _host.IsFullscreen });   // page reads the echo
            }
            catch (Exception ex) { App.Logger?.Debug("GoonHostService.UnFullscreenForShellOpen: {E}", ex.Message); }
        }

        // ---------------------------------------------------------------- rich presence

        /// <summary><c>rp-state { s }</c> — enum-validated and dropped entirely unless
        /// <c>GoonRichPresence</c> is on, so a duel can never move the app's generic presence for a
        /// user who did not ask for it (contract §1).</summary>
        private static void OnRichPresenceState(JObject o)
        {
            var s = (string?)o["s"] ?? "";
            if (s != "lobby" && s != "live" && s != "recap" && s != "off")
            {
                App.Logger?.Debug("GoonHostService: rp-state rejected (not in the enum)");
                return;
            }
            if (App.Settings?.Current?.GoonRichPresence != true) return;

            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try { App.DiscordRpc?.SetGoonActivity(s); }
                catch (Exception ex) { App.Logger?.Debug("GoonHostService.rp-state: {E}", ex.Message); }
            });
        }

        // ---------------------------------------------------------------- small validators

        /// <summary>A Discord snowflake is digits and nothing else. Applied to the SERVER's value on
        /// arrival and again on the way to <see cref="Process.Start"/>, because that string is about
        /// to become part of a shell-executed URL.</summary>
        private static bool IsSnowflake(string? id)
        {
            if (string.IsNullOrEmpty(id) || id!.Length > 20) return false;
            foreach (var c in id) if (c < '0' || c > '9') return false;
            return true;
        }

        /// <summary>Trim + hard-cap a page-supplied string before it rides an outgoing request body.</summary>
        private static string SafeShort(string? v, int max)
        {
            if (string.IsNullOrWhiteSpace(v)) return "";
            var t = v!.Trim();
            return t.Length > max ? t.Substring(0, max) : t;
        }

        /// <summary>Forget everything about the match peer. Called from the single teardown funnel:
        /// the snowflake must not outlive the window it was fetched for, and the next duel must not
        /// inherit the previous opponent's cached picture.</summary>
        private static void ResetPeerCardState()
        {
            _peerDmId = null;
            _peerName = null;
            _peerAvatarCached = false;
            _peerCardFetched = false;
            try { GoonAvatarCache.Delete(GoonAvatarCache.PeerFile); } catch { }
        }

        // ============================ watchdogs / recovery ============================

        /// <summary>Fold a heartbeat's paint/visibility stamp into the paint-stall clock.
        ///
        /// TWO WAYS TO NOT BE STALLED, and both have to reset the clock. The obvious one is the
        /// frame counter moving. The other is the page saying it is not visible: a minimized,
        /// occluded or alt-tabbed window stops getting frames BY DESIGN, and counting that as a
        /// freeze would relaunch the duel every time the player looked at something else. The page
        /// reports what it knows (document.visibilityState) and the decision is made here.</summary>
        private static void NotePaintStamp(JObject o)
        {
            try
            {
                var now = DateTime.UtcNow;
                var vis = (string?)o["vis"];
                if (!string.IsNullOrEmpty(vis) && !string.Equals(vis, "visible", StringComparison.Ordinal))
                {
                    _lastPaintMoveUtc = now;
                    return;
                }
                var paint = (long?)o["paint"];
                if (paint == null) return;   // no counter on this host -> the rule stays off entirely
                if (_lastPaint == null || paint.Value != _lastPaint.Value)
                {
                    _lastPaint = paint.Value;
                    _lastPaintMoveUtc = now;
                }
            }
            catch { /* a malformed stamp is not worth a log line every 2s */ }
        }

        private static void StartHeartbeatWatch()
        {
            StopHeartbeatWatch();
            _lastHeartbeatUtc = DateTime.UtcNow;
            _lastPaint = null;
            _lastPaintMoveUtc = DateTime.UtcNow;
            _paintStallHandled = false;
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
                // SECOND TRIGGER (2026-08-04): the beats keep coming and the PICTURE is dead.
                // A silent heartbeat only ever described a wedged main thread; the freeze the
                // owner actually hit was a compositor/GPU stall with live script — the app was
                // healthy, this watchdog never fired, and WebView2 never even wrote a dump,
                // because nothing crashed. The page now stamps a frame counter on every beat
                // (boot.js), so "alive but not painting" is a fact we can read, and it gets the
                // SAME single relaunch a dead page does.
                if (!_paintStallHandled && _lastPaint != null)
                {
                    var frozen = (DateTime.UtcNow - _lastPaintMoveUtc).TotalSeconds;
                    if (frozen > PaintStallSeconds)
                    {
                        _paintStallHandled = true;
                        App.Logger?.Warning(
                            "GoonHostService: paint stall detected (js alive, {Sec:F0}s no frames) - recovering",
                            frozen);
                        Recover("paint-stall");
                        return;
                    }
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
                // Unbind the cache feed BEFORE the host goes, so a worker-thread Changed can't post
                // into a disposed WebView2. The ResumeAfterMatch is a safety net, not bookkeeping:
                // the page pauses the queue at Countdown and resumes at Recap, and a window closed
                // mid-match would otherwise leave the queue paused for the rest of the app session.
                try { GoonCacheBridge.Detach(); } catch { }
                try { Transfer.TransferCompressionService.Instance.ResumeAfterMatch(); } catch { }
                try { TransferInboxStore.Instance.PruneSafe(); } catch { }
                // Last word to the page before the window goes: a live match should get a chance
                // to post its own abandon rather than just vanishing from the opponent's side.
                try { _host?.Post(new { type = "end-run", reason = "dispose" }); } catch { }
                // The duel's rich presence dies with the duel WHATEVER killed it — a window closed
                // mid-match would otherwise leave "In a duel" published forever (and, when GG owned
                // the connection, an RPC pipe open). No-op when GG never set it.
                try { App.DiscordRpc?.SetGoonActivity("off"); } catch { }
                try { ResetPeerCardState(); } catch { }
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
