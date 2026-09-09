using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Services.Chaos;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.PieceByPiece;

/// <summary>
/// Host for PIECE BY PIECE, the 3D chess board that lives at
/// <c>Resources/web/piecebypiece/index.html</c>. A stripped-down sibling of
/// <see cref="CaucusHostService"/> in exactly the way that host is a stripped-down sibling of
/// <see cref="DtrhHostService"/>: ONE windowed, input-receiving <see cref="ChaosWebViewHost"/>,
/// the two virtual hosts the page actually needs (<c>ccp.game</c> for its own files,
/// <c>ccp.assets</c> for the player's media) plus the content-pack mirror, and a four-message
/// protocol.
///
/// <para>Protocol (the effects ramp codes against these shapes; do not widen them casually):
/// <list type="bullet">
/// <item>host -&gt; page, once after boot: <c>{ type: 'pbp:settings', videoHoldSec, reducedMotion }</c></item>
/// <item>page -&gt; host: <c>{ type: 'pbp:media-request', kinds: ['image','gif','video'], count }</c></item>
/// <item>host -&gt; page: <c>{ type: 'pbp:media', images: [url...], gifs: [...], videos: [...] }</c></item>
/// <item>page -&gt; host: <c>{ type: 'pbp:exit' }</c> - close the window</item>
/// </list>
/// <c>heartbeat</c>/<c>pong</c> and <c>boot-error</c> are the shell conventions every other host
/// speaks, and are handled here too. Both watchdogs are guarded on the page having reported
/// <c>ready</c>, so a shell that speaks none of this cannot be closed by a silence it was never
/// going to break.</para>
///
/// <para>ONLINE PLAY adds three more, and they are a set - one that hands the page an identity,
/// and a request/reply pair that carries HTTP for it:
/// <list type="bullet">
/// <item>host -&gt; page, once after boot:
///   <c>{ type: 'pbp:identity', unifiedId, displayName, appVersion, online,
///        net: { serverBase, authToken, viaHost } }</c></item>
/// <item>page -&gt; host: <c>{ type: 'pbp:net', id, method, path, body }</c></item>
/// <item>host -&gt; page: <c>{ type: 'pbp:net-result', id, status, body }</c></item>
/// </list>
/// Mirrors <c>GoonHostService</c>'s <c>net-post</c> pair exactly, for its reasons: the token is
/// attached HERE rather than in the page, and only <c>/v2/pbp/*</c> is ever forwarded. See
/// <see cref="OnNetRequest"/> for why that whitelist is load-bearing rather than tidy.</para>
///
/// <para><b>Nothing here is authoritative over anything.</b> A chess game pays out no XP, keeps no
/// Sparks and fires no desktop payloads, so there is no state to flush and no verdict to protect:
/// every exit path lands on <see cref="Close"/>, which just tears the window down. That is also
/// why <see cref="Close"/> disposes DIRECTLY rather than posting a wind-down and waiting on a
/// 1200ms <see cref="DispatcherTimer"/> the way the descent does - that timer can never tick from
/// inside <c>App.OnExit</c>, and here it would be guarding nothing.</para>
/// </summary>
internal static class PieceByPieceHostService
{
    /// <summary>Display name for the tier gate, the window title and log lines.</summary>
    public const string ProductName = "Piece by Piece";

    /// <summary>Progress-aware boot deadline: 45s since the last sign of life from the page
    /// (launch, or any message at all). The Arcademy's pattern and for the Arcademy's reason - it
    /// covers a page that never runs a line of script, where nothing page-side is alive to
    /// report <c>boot-error</c> itself.</summary>
    private static readonly TimeSpan BootDeadline = TimeSpan.FromSeconds(45);

    /// <summary>Hard ceiling on one <c>pbp:media-request</c>. The page asks again if it wants
    /// more; an unbounded count from a hand-edited page would walk a whole library into one JSON
    /// frame.</summary>
    private const int MaxMediaPerKind = 64;

    /// <summary>Fallback hold for a video tile when the player's mandatory-video duration floor is
    /// unset (0 = "no limit", the shipped default). Inside the 10..30 clamp on purpose.</summary>
    private const int DefaultVideoHoldSec = 15;

    private static readonly Random Rng = new();

    /// <summary>Where <c>/v2/pbp/*</c> lives. The same proxy every other online surface uses.</summary>
    private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

    /// <summary>
    /// The ONLY path prefix <see cref="OnNetRequest"/> will forward.
    ///
    /// <para>A WHITELIST, NOT A CONVENIENCE. Without it this handler is an open HTTP proxy that
    /// signs whatever the page asks for with the user's auth token - and this page loads content
    /// from <c>ccp.assets</c>, so "whatever the page asks for" is not a purely hypothetical
    /// concern. Anything outside the chess game's own routes fails closed as
    /// <c>status:0, body:"forbidden_path"</c>, which is the shape a transport failure already
    /// produces, so the page needs no special case for it.</para>
    /// </summary>
    private const string AllowedPathPrefix = "/v2/pbp/";

    /// <summary>
    /// One client for the app session; a per-request <see cref="HttpClient"/> exhausts sockets.
    /// 30s covers the events long poll, which the server caps at 8s and Vercel kills at 10s -
    /// generous enough that a timeout here always means something is genuinely wrong, and short
    /// enough that the page's own 45s deadline is never the thing that fires first.
    /// </summary>
    private static readonly HttpClient Http = BuildHttpClient();

    private static HttpClient BuildHttpClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        try { c.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}"); }
        catch (Exception ex) { Diag.Swallowed(ex); }
        return c;
    }

    private static ChaosWebViewHost? _host;
    private static DispatcherTimer? _bootWatch;
    private static DispatcherTimer? _heartbeatWatch;
    private static DateTime _lastHeartbeatUtc;
    private static DateTime _lastProgressUtc;
    private static bool _pinged;
    private static bool _settingsPosted;   // pbp:settings goes out exactly once per boot
    private static bool _identityPosted;   // and so does pbp:identity
    private static bool _disposing;        // reentrancy: Dispose closes the window -> Closed -> Close()

    /// <summary>True while the board is open.</summary>
    public static bool IsActive => _host != null;

    /// <summary>The page's LAST boot attempt failed (it reported <c>boot-error</c>, or the host's
    /// own progress deadline fired). A LAST-attempt flag, not a session tombstone: most of what
    /// sets it is transient - a cold WebView2 runtime, a machine under load, a stalled driver - so
    /// <see cref="OnPageReady"/> clears it and an entry point may offer a retry rather than
    /// refusing outright.</summary>
    public static bool BootFailedThisSession { get; private set; }

    // ============================ launch / close ============================

    /// <summary>
    /// Open the board. Idempotent: a live window is only ever re-focused, never replaced, so a
    /// double click cannot stack two boards. The tier bar applies to a FRESH launch only, through
    /// <see cref="TierGate"/>, which fails closed while <c>App.Patreon</c> is null and raises the
    /// standard refusal with its "See tiers" action.
    /// </summary>
    public static void Launch()
    {
        if (_host != null) { _host.FocusWeb(); return; }

        // The same door the Arcademy and the descent ask for: a card's lockband is decoration, and
        // the one code path that actually opens the window has to be the one that can say no.
        if (!TierGate.DemandLab(ProductName)) return;

        try
        {
            _settingsPosted = false;
            _identityPosted = false;
            _pinged = false;
            _lastProgressUtc = DateTime.UtcNow;

            var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
            var mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
            {
                // The page, its board modules and the shared vendored three.js build. Deny: the
                // page never needs to be read cross-origin.
                ("ccp.game", webRoot, CoreWebView2HostResourceAccessKind.Deny),
                // The player's own media. Allow (not DenyCors) for the reason every other host
                // maps it that way: the effects layer may draw a tile into a canvas or upload it
                // as a texture, and a Deny origin taints both.
                ("ccp.assets", App.EffectiveAssetsPath, CoreWebView2HostResourceAccessKind.Allow),
                // Downloaded audio packs mirror the ccp.game tree under their own origin.
                ChaosWebViewHost.ContentMapping(),
            };

            _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
            {
                StartUrl = "https://ccp.game/piecebypiece/index.html",
                PrimaryHost = "ccp.game",
                Mappings = mappings,
                // Own browser profile: nothing this page does can disturb the descent's or the
                // Arcademy's WebView2 state.
                UserDataFolderName = "browser_data_piecebypiece",
                InputEnabled = true,
                StartFullscreen = false,
                // Native ownership rather than Topmost: plenty of things raise MainWindow (a bark,
                // a video window closing, a tray restore) and would otherwise bury the board,
                // while Topmost would float it over every OTHER application too.
                OwnedByMainWindow = true,
                WindowTitle = ProductName,
                LogTag = "PieceByPiece",
                // The effects ramp has stingers; none of them gets a click first.
                ExtraBrowserArguments = "--autoplay-policy=no-user-gesture-required",
                OnReady = OnPageReady,
                OnMessage = OnPageMessage,
                OnProcessFailed = OnProcessFailed,
            });

            _host.Show();
            // Windowed: the user can close it with the title-bar X. Tear down cleanly so the
            // heartbeat watchdog cannot misread the resulting silence as a wedged page.
            if (_host.Window != null) _host.Window.Closed += (_, _) => Close();

            StartHeartbeatWatch();
            ArmBootDeadline();
            // Keyboard focus does not land in the WebView2 child until a click; claim it now so
            // the board's own keys work from the first frame.
            _host.FocusWeb();

            App.Logger?.Information("PieceByPieceHostService: launched");
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "PieceByPieceHostService.Launch failed");
            Close();
        }
    }

    /// <summary>
    /// The one funnel every exit reaches: the page's <c>pbp:exit</c>, the title-bar X, a boot
    /// error, a dead render process, the heartbeat watchdog and app shutdown. Idempotent -
    /// <c>_host.Dispose()</c> closes the window, which re-raises <c>Closed</c> back into here.
    /// Safe to call from <c>App.OnExit</c>: nothing in it waits on a dispatcher timer.
    /// </summary>
    public static void Close()
    {
        if (_disposing) return;
        _disposing = true;
        try
        {
            CancelBootDeadline();
            StopHeartbeatWatch();
            _settingsPosted = false;
            _identityPosted = false;
            _pinged = false;
            bool had = _host != null;
            try { _host?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _host = null;
            if (had) App.Logger?.Information("PieceByPieceHostService: closed");
        }
        catch (Exception ex) { App.Logger?.Debug("PieceByPieceHostService.Close: {E}", ex.Message); }
        finally { _disposing = false; }
    }

    // ============================ boot ============================

    private static void OnPageReady()
    {
        try
        {
            CancelBootDeadline();
            BootFailedThisSession = false;   // a boot that succeeds clears the last attempt's flag
            _lastHeartbeatUtc = DateTime.UtcNow;
            _lastProgressUtc = DateTime.UtcNow;
            _pinged = false;
            _host?.FocusWeb();
            PostSettings();
            PostIdentity();
        }
        catch (Exception ex) { App.Logger?.Warning("PieceByPieceHostService.OnPageReady: {E}", ex.Message); }
    }

    /// <summary>
    /// Who is playing, and how the page reaches the server. Sent once per boot, right behind the
    /// settings frame.
    ///
    /// <para>THE TOKEN IS THE CCP AUTH TOKEN (DPAPI-backed <c>SecureAuthTokenStore</c> behind
    /// <c>AppSettings.AuthToken</c>), NOT the Patreon bearer - <c>/v2/pbp/*</c> authenticates the
    /// unified account, exactly as the Goon Game's <c>/v2/goon/*</c> does. It rides in the frame
    /// so a future direct-fetch build has it the moment CORS for the <c>ccp.game</c> origin is
    /// deployed; until then <c>viaHost:true</c> means the page never actually uses it and this
    /// host attaches the header itself, per call, against the path whitelist.</para>
    ///
    /// <para>An EMPTY token is a normal state, not a failure: it means the user has no cloud
    /// session. <c>online:false</c> says so plainly, and the page's lobby answers null and lets
    /// the front door open on its offline mock rather than 401ing at every screen.</para>
    /// </summary>
    private static void PostIdentity()
    {
        if (_identityPosted) return;
        _identityPosted = true;
        try
        {
            var uid = App.UnifiedUserId ?? string.Empty;
            var token = SafeAuthToken();
            _host?.Post(new
            {
                type = "pbp:identity",
                unifiedId = uid,
                displayName = SafeDisplayName(),
                appVersion = UpdateService.AppVersion,
                // Both halves are needed: an account with no token cannot authenticate, and a
                // token with no account has nothing to name in a request body.
                online = !string.IsNullOrEmpty(uid) && !string.IsNullOrEmpty(token),
                net = new
                {
                    serverBase = ProxyBaseUrl,
                    authToken = token,
                    viaHost = true,
                },
            });
        }
        catch (Exception ex) { App.Logger?.Debug("PieceByPiece: identity post failed: {E}", ex.Message); }
    }

    /// <summary>The one host -&gt; page settings frame, sent once per boot.</summary>
    private static void PostSettings()
    {
        if (_settingsPosted) return;
        _settingsPosted = true;
        try
        {
            _host?.Post(new
            {
                type = "pbp:settings",
                videoHoldSec = SafeVideoHoldSec(),
                reducedMotion = SafeReducedMotion(),
            });
        }
        catch (Exception ex) { App.Logger?.Debug("PieceByPiece: settings post failed: {E}", ex.Message); }
    }

    // ============================ page messages ============================

    private static void OnPageMessage(JObject o)
    {
        // Every message is a sign of life, whatever it says: the boot deadline is PROGRESS-aware,
        // not wall-clock, so a page that is slow but talking is not a failed boot.
        _lastProgressUtc = DateTime.UtcNow;
        try
        {
            switch ((string?)o["type"])
            {
                case "heartbeat":
                case "pong":
                    _lastHeartbeatUtc = DateTime.UtcNow;
                    _pinged = false;
                    break;

                case "pbp:media-request":
                    OnMediaRequest(o);
                    break;

                case "pbp:net":
                    OnNetRequest(o);
                    break;

                case "pbp:exit":
                    // Page-initiated (Esc with no drag in flight). Nothing to wind down, so this
                    // IS the close - marshalled, because tearing a window down is UI-thread work.
                    RunOnUi(Close);
                    break;

                case "boot-error":
                    OnBootError((string?)o["message"] ?? (string?)o["msg"]);
                    break;
            }
        }
        catch (Exception ex)
        {
            // Crash-safe by construction: one malformed frame must never take the window with it.
            App.Logger?.Warning("PieceByPiece: message handler threw ({Type}): {E}",
                (string?)o["type"], ex.Message);
        }
    }

    /// <summary>
    /// Answer <c>pbp:media-request</c> from the player's own library. Any list may come back
    /// empty and every kind is independent - a fresh install has no media at all, and the page
    /// must survive that.
    /// </summary>
    private static void OnMediaRequest(JObject o)
    {
        int count = Math.Clamp((int?)o["count"] ?? 24, 1, MaxMediaPerKind);
        var kinds = ReadKinds(o["kinds"]);
        var (images, gifs, videos) = SampleMedia(kinds, count);
        _host?.Post(new { type = "pbp:media", images, gifs, videos });
        App.Logger?.Debug("PieceByPiece: media reply {I} images, {G} gifs, {V} videos",
            images.Length, gifs.Length, videos.Length);
    }

    /// <summary>The requested kinds, defaulting to all three when the page leaves the field off or
    /// sends something that is not a list of strings.</summary>
    private static HashSet<string> ReadKinds(JToken? token)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (token is JArray arr)
        {
            foreach (var t in arr)
            {
                var s = (string?)t;
                if (!string.IsNullOrWhiteSpace(s)) set.Add(s!.Trim());
            }
        }
        if (set.Count == 0) { set.Add("image"); set.Add("gif"); set.Add("video"); }
        return set;
    }

    /// <summary>
    /// Up to <paramref name="count"/> ccp.assets URLs per kind, sampled uniformly.
    ///
    /// <para>The pool is <see cref="DtrhAssetManifest.EnumerateActive"/> - deliberately NOT a
    /// second scanner. That one enumeration is the single authority on what "the player's active
    /// pool" means (browser-decodable extensions only, the Assets tree's <c>DisabledAssetPaths</c>
    /// unchecks honoured, the 50MB/500MB size caps, depth 8, dot-directories skipped), and a copy
    /// of that filter here would be free to drift from the one the descent and the transfer
    /// compression planner already share. It is LOCAL DISK ONLY by design, which is also why no
    /// remote CDN entry can reach this page.</para>
    ///
    /// <para>Reservoir sampling, one reservoir per kind: an even draw across a library of any size
    /// without materialising it and without walking it twice. GIFs are split OUT of images - the
    /// manifest counts a .gif as an image, but a board tile that loops and one that does not are
    /// different things to whatever is placing them.</para>
    /// </summary>
    private static (string[] Images, string[] Gifs, string[] Videos) SampleMedia(
        HashSet<string> kinds, int count)
    {
        var images = new List<string>(count);
        var gifs = new List<string>(count);
        var videos = new List<string>(count);
        bool wantImages = kinds.Contains("image");
        bool wantGifs = kinds.Contains("gif");
        bool wantVideos = kinds.Contains("video");
        int seenImages = 0, seenGifs = 0, seenVideos = 0;

        try
        {
            foreach (var (_, rel, _, isImage) in DtrhAssetManifest.EnumerateActive())
            {
                bool isGif = rel.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
                if (!isImage)
                {
                    if (wantVideos) Offer(videos, ref seenVideos, count, rel);
                }
                else if (isGif)
                {
                    if (wantGifs) Offer(gifs, ref seenGifs, count, rel);
                }
                else if (wantImages)
                {
                    Offer(images, ref seenImages, count, rel);
                }
            }
        }
        catch (Exception ex)
        {
            // Whatever was gathered before the walk fell over is still worth sending.
            App.Logger?.Debug("PieceByPiece: media scan failed: {E}", ex.Message);
        }

        return (images.ToArray(), gifs.ToArray(), videos.ToArray());
    }

    /// <summary>One step of a reservoir: fill to <paramref name="count"/>, then replace with
    /// probability count/seen, so every file in the pool has the same chance of landing.</summary>
    private static void Offer(List<string> reservoir, ref int seen, int count, string rel)
    {
        seen++;
        var url = DtrhAssetManifest.AssetUrl(rel);
        if (reservoir.Count < count) { reservoir.Add(url); return; }
        int j = Rng.Next(seen);
        if (j < count) reservoir[j] = url;
    }

    // ============================ the net lane ============================

    /// <summary>
    /// <c>pbp:net { id, method, path, body }</c> - one HTTP call to
    /// <see cref="ProxyBaseUrl"/> + path, answered with
    /// <c>pbp:net-result { id, status, body }</c>.
    ///
    /// <para>THE PAGE NEVER HOLDS THE TOKEN IN PRACTICE. This is why: every online call comes
    /// back through here, the header is attached on this side, and the page's copy of the token
    /// (which the identity frame does carry, for a future direct-fetch build) is never used while
    /// <c>viaHost</c> is true. The whitelist on <see cref="AllowedPathPrefix"/> is what keeps that
    /// from being an open, authenticated proxy for anything else the page might be talked into
    /// asking for.</para>
    ///
    /// <para>A failure of any kind answers <c>status:0</c>, which the page reads as "no answer at
    /// all" - the same shape as being offline. It never throws back at the page, and it never
    /// leaves a call unanswered: an id with no reply would sit in the page's pending map until
    /// its own 45s deadline swept it.</para>
    /// </summary>
    private static void OnNetRequest(JObject o)
    {
        var id = (string?)o["id"] ?? "";
        var path = (string?)o["path"] ?? "";
        var method = ((string?)o["method"] ?? "GET").Trim().ToUpperInvariant();
        var body = o["body"]?.Type == JTokenType.String
            ? (string?)o["body"] ?? ""
            : (o["body"] is null || o["body"]!.Type == JTokenType.Null
                ? ""
                : o["body"]!.ToString(Newtonsoft.Json.Formatting.None));

        if (!path.StartsWith(AllowedPathPrefix, StringComparison.Ordinal))
        {
            App.Logger?.Warning("PieceByPiece: net REJECTED for path '{Path}'", path);
            ReplyNet(id, 0, "forbidden_path");
            return;
        }
        if (method != "GET" && method != "POST")
        {
            // The whole surface is GET and POST. Anything else is a page that has been
            // tampered with, not a route this build has not caught up with yet.
            App.Logger?.Warning("PieceByPiece: net REJECTED for method '{Method}'", method);
            ReplyNet(id, 0, "forbidden_method");
            return;
        }

        _ = Task.Run(async () =>
        {
            int status = 0;
            string responseBody = "";
            try
            {
                var verb = method == "POST" ? HttpMethod.Post : HttpMethod.Get;
                using var request = new HttpRequestMessage(verb, ProxyBaseUrl + path);
                if (method == "POST")
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }
                var token = SafeAuthToken();
                if (!string.IsNullOrEmpty(token)) request.Headers.Add("X-Auth-Token", token);
                request.Headers.Add("X-Client-Version", UpdateService.AppVersion);

                using var response = await Http.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
                status = (int)response.StatusCode;
                responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Timeout, DNS, offline, a cancelled long poll. status 0 is the page's
                // "never got an answer", which every call site there already branches on.
                status = 0;
                responseBody = "";
                App.Logger?.Debug("PieceByPiece: net {Method} {Path} failed: {E}", method, path, ex.Message);
            }
            ReplyNet(id, status, responseBody);
        });
    }

    /// <summary>Post the reply back on the UI thread - WebView2 is thread-affine.</summary>
    private static void ReplyNet(string id, int status, string body)
    {
        RunOnUi(() =>
        {
            try { _host?.Post(new { type = "pbp:net-result", id, status, body }); }
            catch (Exception ex) { App.Logger?.Debug("PieceByPiece.ReplyNet: {E}", ex.Message); }
        });
    }

    /// <summary>The CCP auth token - DPAPI-backed <c>SecureAuthTokenStore</c> behind
    /// <c>AppSettings.AuthToken</c>, NOT the Patreon bearer. Empty when the user has no cloud
    /// session, which the page reads as "online play is not available" rather than as a
    /// failure.</summary>
    private static string SafeAuthToken()
    {
        try { return App.Settings?.Current?.AuthToken ?? string.Empty; }
        catch { return string.Empty; }
    }

    /// <summary>The same name every other online surface presents, so one player is one identity
    /// whichever door of the app they came through.</summary>
    private static string SafeDisplayName()
    {
        try
        {
            var name = App.Settings?.Current?.UserDisplayName;
            return string.IsNullOrWhiteSpace(name) ? "Player" : name!;
        }
        catch { return "Player"; }
    }

    // ============================ window plumbing ============================

    /// <summary>The page's boot failed (WebGL refused, a module import threw). There is nothing to
    /// degrade to, so close cleanly and latch the flag - the log line is the diagnosis.</summary>
    private static void OnBootError(string? msg)
    {
        BootFailedThisSession = true;
        App.Logger?.Warning("PieceByPiece: page boot-error: {Msg}", msg);
        RunOnUi(Close);
    }

    private static void OnProcessFailed(CoreWebView2ProcessFailedKind kind)
    {
        App.Logger?.Warning("PieceByPiece: browser process failed ({Kind}) - closing", kind);
        RunOnUi(Close);
    }

    /// <summary>Marshal onto the UI thread, and do nothing at all once the dispatcher is going
    /// down - a BeginInvoke into a shutting-down dispatcher is the classic late-callback crash.</summary>
    private static void RunOnUi(Action act)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null) { try { act(); } catch (Exception ex) { Diag.Swallowed(ex); } return; }
        if (disp.HasShutdownStarted) return;
        disp.BeginInvoke(act);
    }

    // ============================ watchdogs ============================

    /// <summary>The page arms its own deadline and reports <c>boot-error</c> first in the normal
    /// case; this one covers a page that never runs a line of script (a missing module, a blocked
    /// navigation), where nothing page-side is alive to complain.</summary>
    private static void ArmBootDeadline()
    {
        CancelBootDeadline();
        _bootWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _bootWatch.Tick += (_, _) =>
        {
            if (_host == null || _host.IsReady) { CancelBootDeadline(); return; }
            if (DateTime.UtcNow - _lastProgressUtc < BootDeadline) return;
            CancelBootDeadline();
            OnBootError($"boot deadline: no progress for {BootDeadline.TotalSeconds:0}s");
        };
        _bootWatch.Start();
    }

    private static void CancelBootDeadline()
    {
        try { _bootWatch?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _bootWatch = null;
    }

    /// <summary>The race host's simplified ladder: a page silent past the limit gets one ping, and
    /// if it stays silent through the next tick the window closes. No relaunch ladder - a chess
    /// game is not a run worth preserving, and the user just clicks the button again.</summary>
    private static void StartHeartbeatWatch()
    {
        StopHeartbeatWatch();
        _lastHeartbeatUtc = DateTime.UtcNow;
        _heartbeatWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _heartbeatWatch.Tick += (_, _) =>
        {
            // Guarded on IsReady: a still-loading page cannot false-trip, and a shell that never
            // sends a heartbeat never reports ready either, so this stays asleep rather than
            // closing a window that is working fine.
            if (_host == null || !_host.IsReady) return;
            if ((DateTime.UtcNow - _lastHeartbeatUtc).TotalSeconds <= 20) return;
            if (!_pinged)
            {
                _pinged = true;
                try { _host.Post(new { type = "ping" }); } catch (Exception ex) { Diag.Swallowed(ex); }
                return;
            }
            App.Logger?.Warning("PieceByPiece: page heartbeat silent >20s and no pong - closing");
            Close();
        };
        _heartbeatWatch.Start();
    }

    private static void StopHeartbeatWatch()
    {
        try { _heartbeatWatch?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _heartbeatWatch = null;
    }

    // ============================ settings reads ============================

    /// <summary>
    /// How long a video tile is held on the board, in seconds.
    ///
    /// <para>Mapped onto <c>VideoMinDurationSeconds</c> - the player's own mandatory-video
    /// duration floor - rather than inventing a new setting, so someone who has pinned their
    /// session to short clips gets short tiles here too. That filter ships as 0 ("no limit"), and
    /// 0 is not a hold, so an unset filter falls back to <see cref="DefaultVideoHoldSec"/>. The
    /// 10..30 clamp is the page's contract either way.</para>
    /// </summary>
    private static int SafeVideoHoldSec()
    {
        try
        {
            int configured = App.Settings?.Current?.VideoMinDurationSeconds ?? 0;
            return Math.Clamp(configured > 0 ? configured : DefaultVideoHoldSec, 10, 30);
        }
        catch { return DefaultVideoHoldSec; }
    }

    /// <summary>The app's motion setting, capped by the OS animation switch (MotionFx owns that
    /// resolution). Reduced and Off both read as reduced motion on the page: it has no third
    /// state to offer.</summary>
    private static bool SafeReducedMotion()
    {
        try { return MotionFx.Level != Models.MotionLevel.Full; }
        catch { return false; }
    }
}
