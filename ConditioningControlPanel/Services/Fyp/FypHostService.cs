using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Linq;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Services.Fyp.Online;

namespace ConditioningControlPanel.Services.Fyp;

/// <summary>
/// Hosts the For You feed (premium): a TikTok-style endless feed of random 20-40s clip
/// windows cut from the user's active videos and GIFs, ported from the mobile reel and
/// rendered in a WebView2 window at <c>Resources/web/fyp/index.html</c>. Modeled on
/// <see cref="Chaos.LoomHostService"/>: one windowed <see cref="ChaosWebViewHost"/>,
/// singleton, torn down on window close or process failure.
///
/// The page owns the feed algorithm and its retention stats; this class owns persistence
/// (fyp_stats.json / fyp_meta.json), the asset manifest, settings round-trip, and XP.
/// While <see cref="IsActive"/>, video-class features stand down (VideoService trigger,
/// BubbleCount, Autonomy WebVideo) — flashes/subliminals/lock cards still fire over the feed.
/// </summary>
internal static class FypHostService
{
    private static ChaosWebViewHost? _host;
    private static FypMetaStore? _meta;

    // Passive-XP rate cap: the page reports honest dwell, but cap awards anyway so a
    // misbehaving page can't fountain XP. 12 clip-views/minute is faster than the feed
    // can actually produce distinct 20-40s slices.
    private const int MaxClipXpPerMinute = 12;
    private static readonly Queue<DateTime> _clipXpTimes = new();

    private static string StatsFilePath => Path.Combine(App.UserDataPath, "fyp_stats.json");

    // ---- ghost-mode state (see the "window mechanics" region at the bottom) ----
    // Session-only: ghost mode ALWAYS starts off, so a relaunch can never come up as an
    // un-clickable ghost the user has no way to grab.
    private static bool _ghosted;
    private static FypGhostOverlay? _ghost;
    private static double _ghostLeft, _ghostTop;      // the real window's parked-from position (DIPs, log/fallback)
    private static bool _ghostWasMaximized;           // it was maximized before we normalized it
    private static DateTime _lastGhostExitUtc = DateTime.MinValue;
    private static int _restoreX, _restoreY, _restoreW, _restoreH;   // pre-park window rect (physical px)
    private static int _parkX, _parkY, _parkW, _parkH;               // parked window rect (physical px)
    private static EventHandler? _ghostStateGuard;    // heals a minimize of the parked window
    private static HwndSource? _ghostHookSource;      // carries the SC_MINIMIZE veto while ghosted
    private static HwndSourceHook? _ghostHook;        // strong ref: AddHook holds only weakly

    /// <summary>True while the feed is a see-through DWM mirror and the real window is parked
    /// off-screen (mouse passes straight through to whatever is behind it).</summary>
    public static bool IsGhosted => _ghosted;

    /// <summary>True just after ghost mode ended. The panic ladder uses this to swallow the
    /// reflexive double-tap: rung 1 drops the ghost, and a second press inside this window
    /// must not immediately take the whole feed down.</summary>
    public static bool RecentlyUnghosted => (DateTime.UtcNow - _lastGhostExitUtc).TotalMilliseconds < 1500;

    /// <summary>True while the For You window is open.</summary>
    public static bool IsActive => _host != null;

    /// <summary>Open the feed window (idempotent - refocuses if already open).</summary>
    public static void Launch()
    {
        if (_host != null) { _host.FocusWeb(); return; }
        try
        {
            var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
            // A missing folder makes WebView2 SKIP the mapping entirely (host rule),
            // so make sure the assets root exists before Show().
            try { Directory.CreateDirectory(App.EffectiveAssetsPath); }
            catch (Exception ex) { App.Logger?.Debug("FypHost: assets dir create failed: {E}", ex.Message); }

            _meta ??= new FypMetaStore();

            _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
            {
                StartUrl = "https://ccp.game/fyp/index.html",
                PrimaryHost = "ccp.game",
                Mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
                {
                    ("ccp.game", webRoot, CoreWebView2HostResourceAccessKind.Deny),
                    ("ccp.assets", App.EffectiveAssetsPath, CoreWebView2HostResourceAccessKind.Allow),
                },
                // Own browser profile; keeps DtRH/Loom state untouched.
                UserDataFolderName = "browser_data_fyp",
                InputEnabled = true,
                StartFullscreen = false,
                OwnedByMainWindow = true,
                WindowTitle = "For You",
                LogTag = "FypHost",
                // An autoplaying feed: media must start without a user gesture. The two
                // occlusion flags are what make GHOST MODE work: the real window is parked off
                // the virtual desktop while the DWM mirror shows it, and Chromium would
                // otherwise call it occluded/backgrounded, stop rendering, and freeze the
                // mirror to a still frame.
                ExtraBrowserArguments = "--autoplay-policy=no-user-gesture-required "
                                        + "--disable-features=CalculateNativeWinOcclusion "
                                        + "--disable-backgrounding-occluded-windows",
                OnReady = PostInit,
                OnMessage = OnPageMessage,
                OnProcessFailed = _ => Close(),
            });
            _host.Show();
            if (_host.Window != null)
                _host.Window.Closed += (_, _) => Close();
            App.Logger?.Information("FypHostService: launched");
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "FypHostService.Launch failed");
            Close();
        }
    }

    /// <summary>Tear the window down (idempotent; also the ProcessFailed and panic path).</summary>
    public static void Close()
    {
        // Before _host goes: drop the webcam subscriptions and put the camera back the way we
        // found it. Nothing in here posts to the page, so ordering only matters for the stray
        // event that could otherwise land between here and Dispose().
        DisableEyeControl();
        // Un-ghost BEFORE the host goes: the mirror window must not outlive its thumbnail source,
        // and ExitGhost needs _host to put the real window back where it came from.
        ExitGhost();
        var h = _host;
        _host = null;
        try { _meta?.Save(); } catch { }
        try { FypOnlineCoordinator.SaveAll(); } catch { }
        try { h?.Dispose(); }
        catch (Exception ex) { App.Logger?.Debug("FypHost.Close: {E}", ex.Message); }
    }

    /// <summary>One init payload on page-ready: assets + settings + persisted stats.</summary>
    private static void PostInit()
    {
        try
        {
            var s = App.Settings?.Current;
            _meta ??= new FypMetaStore();
            _host?.Post(new
            {
                type = "init",
                assets = FypAssetManifest.Build(_meta),
                settings = new
                {
                    layout = s?.FypLayout ?? "duo",
                    includeGifs = s?.FypIncludeGifs ?? true,
                    mosaicAutoChange = s?.FypMosaicAutoChange ?? true,
                    mosaicChangeSec = s?.FypMosaicChangeSec ?? 10,
                    autoAdvance = s?.FypAutoAdvance ?? false,
                    muted = s?.FypMuted ?? false,
                    windowOpacity = s?.FypWindowOpacity ?? 1.0,
                    audioGlow = s?.FypAudioGlow ?? true,
                    eyeControl = s?.FypEyeControl ?? false,
                    eyeGaze = s?.FypEyeGaze ?? false,
                    // Effective, not raw: the app-wide media source (Assets tab) reaches the
                    // feed too — see EffectiveFeedSource. The page's popover still edits the
                    // feed's own FypSource underneath.
                    source = EffectiveFeedSource(),
                    onlineRatio = EffectiveOnlineRatio(),
                    // Either consent card (the feed's or the app-wide one) counts — asking the
                    // same question twice in different words reads as the app forgetting.
                    onlineConsented = s?.HasRemoteMediaConsent ?? false,
                },
                // The niche catalog rides along so the page can render chips (and knows
                // each niche's subreddits — deselecting one prunes its clips client-side).
                online = new
                {
                    niches = FypOnlineCoordinator.Catalog.Select(n => new
                    {
                        id = n.Id,
                        label = n.Label,
                        subs = n.Subs,
                        selected = s?.FypOnlineNiches?.Contains(n.Id) ?? false,
                    }),
                    customSubs = s?.FypOnlineCustomSubs ?? new List<string>(),
                },
                stats = LoadStats(),
            });
            // Opacity is applied host-side, but only once ghost mode engages (there is nothing to
            // make translucent before that); ghost mode itself is deliberately NOT restored
            // (session-only) — the page's toggle comes up off.
            // Persisted eye control re-arms the camera on every launch (with the same consent
            // gate as the toggle) — the page has already rendered its toggle "on" from the
            // payload above, and eyeStatus corrects it if the camera refuses.
            if (s?.FypEyeControl == true) EnableEyeControl();
        }
        catch (Exception ex) { App.Logger?.Warning("FypHost.PostInit failed: {E}", ex.Message); }
    }

    private static void OnPageMessage(JObject o)
    {
        switch ((string?)o["type"])
        {
            case "stats-save":
            {
                // The page owns the stats shape; we just persist the blob verbatim.
                if (o["stats"] is JObject stats) SaveStats(stats);
                break;
            }
            case "asset-meta":
            {
                // Remote pools churn forever — never let their probes grow fyp_meta.json.
                var id = (string?)o["id"];
                if (!IsRemoteId(id))
                    _meta?.Update(id, (long?)o["durationMs"], (int?)o["width"], (int?)o["height"]);
                break;
            }
            case "media-error":
            {
                // Warning so it lands in user bug reports — the "black tiles" class of report
                // (#562) was undiagnosable with the page swallowing errors client-side.
                // MediaError codes: 2 = network/fetch, 3 = decode failed, 4 = format not
                // supported (HEVC in Chromium reads as 3 or 4; LibVLC plays the same file).
                var id = (string?)o["id"];
                App.Logger?.Warning("[FYP] media-error for {Id}: code={Code} {Message}",
                    id, (int?)o["code"] ?? 0, (string?)o["message"] ?? "");
                // Fail-strikes are for library files (a remote entry is one-shot anyway;
                // the page's own error swap already pages past it).
                if (!IsRemoteId(id)) _meta?.RecordFailure(id);
                break;
            }
            case "clip-viewed":
            {
                // Remote clips feed the channel-taste EWMA before the XP cap gets a say.
                var segId = (string?)o["segId"];
                if (IsRemoteId(segId))
                {
                    try { FypOnlineCoordinator.Fyp.RecordDwell(segId!, (long?)o["dwellMs"] ?? 0); }
                    catch (Exception ex) { App.Logger?.Debug("FypHost: dwell record failed: {E}", ex.Message); }
                }
                if (!AllowClipXp()) break;
                try { App.Progression?.AddXP(5, XPSource.Fyp); }
                catch (Exception ex) { App.Logger?.Debug("FypHost: clip XP failed: {E}", ex.Message); }
                break;
            }
            case "need-remote":
            {
                ServeRemoteBatch();
                break;
            }
            case "attention-hit":
            {
                try { App.Progression?.AddXP(15, XPSource.Fyp); }
                catch (Exception ex) { App.Logger?.Debug("FypHost: attention XP failed: {E}", ex.Message); }
                break;
            }
            case "settings-changed":
            {
                ApplySetting((string?)o["key"], o["value"]);
                break;
            }
            case "calibrate":
            {
                _host?.Window?.Dispatcher.BeginInvoke(new Action(RunGazeCalibration));
                break;
            }
            case "close":
            {
                _host?.Window?.Dispatcher.BeginInvoke(Close);
                break;
            }
        }
    }

    private static bool AllowClipXp()
    {
        var now = DateTime.UtcNow;
        while (_clipXpTimes.Count > 0 && (now - _clipXpTimes.Peek()).TotalSeconds > 60)
            _clipXpTimes.Dequeue();
        if (_clipXpTimes.Count >= MaxClipXpPerMinute) return false;
        _clipXpTimes.Enqueue(now);
        return true;
    }

    private static void ApplySetting(string? key, JToken? value)
    {
        if (key == null || value == null) return;
        // Session-only and settings-free, so it is handled before the settings null-guard.
        // ("clickThrough" is the on-the-wire key name the page has always used; host-side the
        // feature is ghost mode. Only the user-facing strings changed.)
        if (key == "clickThrough")
        {
            try
            {
                if ((bool?)value ?? false) EnterGhost();
                else ExitGhost();
            }
            catch (Exception ex) { App.Logger?.Debug("FypHost: clickThrough failed: {E}", ex.Message); }
            return;
        }

        var s = App.Settings?.Current;
        if (s == null) return;
        try
        {
            switch (key)
            {
                case "layout": s.FypLayout = (string?)value ?? "duo"; break;
                case "includeGifs": s.FypIncludeGifs = (bool?)value ?? true; break;
                case "mosaicAutoChange": s.FypMosaicAutoChange = (bool?)value ?? true; break;
                case "mosaicChangeSec": s.FypMosaicChangeSec = (int?)value ?? 10; break;
                case "autoAdvance": s.FypAutoAdvance = (bool?)value ?? false; break;
                case "muted": s.FypMuted = (bool?)value ?? false; break;
                // Persist, and push straight onto the ghost mirror when one is up — the mirror
                // is a plain window, so its constant alpha IS the opacity (see below).
                case "windowOpacity":
                {
                    double v = (double?)value ?? 1.0;
                    s.FypWindowOpacity = v;
                    _ghost?.SetOpacity(v);
                    break;
                }
                case "audioGlow": s.FypAudioGlow = (bool?)value ?? true; break;   // page-side visual: persist only
                case "eyeControl":
                {
                    bool on = (bool?)value ?? false;
                    s.FypEyeControl = on;
                    if (on) EnableEyeControl();
                    else { DisableEyeControl(); PostEyeStatus(null); }
                    break;
                }
                case "eyeGaze":
                {
                    s.FypEyeGaze = (bool?)value ?? false;
                    SyncGazeSubscription();
                    PostEyeStatus(null);
                    break;
                }
                case "source":
                {
                    var src = (string?)value ?? "library";
                    // The page shows the consent card before ever posting a non-library
                    // source; belt and braces here so a stale page can't skip it.
                    // HasRemoteMediaConsent, not the raw FYP flag: a user who accepted the
                    // app-wide remote-media card has already agreed to exactly this.
                    if (src != "library" && !s.HasRemoteMediaConsent) src = "library";
                    s.FypSource = src;
                    break;
                }
                case "onlineRatio": s.FypOnlineRatio = (int?)value ?? 30; break;
                case "onlineConsented":
                {
                    // One-way: the page only ever sends true (accepting the card).
                    if ((bool?)value == true && !s.FypOnlineConsented)
                    {
                        s.FypOnlineConsented = true;
                        App.Logger?.Information("FypHost: online-content consent accepted");
                    }
                    break;
                }
                case "onlineNiches":
                {
                    if (value is JArray arr)
                    {
                        var known = new HashSet<string>(FypOnlineCoordinator.Catalog.Select(n => n.Id));
                        s.FypOnlineNiches = arr.Select(t => (string?)t)
                            .Where(id => id != null && known.Contains(id)).Select(id => id!)
                            .Distinct().ToList();
                        FypOnlineCoordinator.Fyp.ResetChannels();
                    }
                    break;
                }
                case "onlineCustomSubs":
                {
                    if (value is JArray arr)
                    {
                        s.FypOnlineCustomSubs = arr.Select(t => FypOnlineCoordinator.SanitizeSub((string?)t))
                            .Where(x => x != null).Select(x => x!)
                            .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
                        FypOnlineCoordinator.Fyp.ResetChannels();
                    }
                    break;
                }
            }
        }
        catch (Exception ex) { App.Logger?.Debug("FypHost: settings-changed {Key} failed: {E}", key, ex.Message); }
    }

    // ============================= online feed (Scrolller) =============================
    //
    // The page asks for content ({type:'need-remote'}) whenever its remote buffer runs
    // low; one batch (~a page from one channel) comes back as {type:'assets-append'}.
    // Fetches run on the thread pool and the reply hops back to the UI thread — the
    // WebView2 Post is thread-affine. Single-flight: a second need-remote while one is
    // in the air is dropped (the page re-asks after every append until it is satisfied).
    //
    // BRIGHT LINE: these fetches go straight from this machine to scrolller's API/CDN.
    // Nothing here may ever involve CC Labs servers — see planning/fyp-online/DESIGN.md.

    private static int _remoteFetchInFlight;   // 0/1 via Interlocked

    private static bool IsRemoteId(string? id) =>
        id != null && id.StartsWith("scrolller/", StringComparison.Ordinal);

    /// <summary>
    /// The feed's effective content source: "library", "online" or "mixed".
    ///
    /// The FYP predates the app-wide media source and kept its own picker
    /// (FypSource/FypOnlineConsented) — which made it the ONE remote surface the app-wide
    /// gate (<c>MediaSource != "local" &amp;&amp; HasRemoteMediaConsent</c>) never reached. A user
    /// who flipped the Assets tab to Online got remote flashes, videos, intake and DTRH,
    /// and a stubbornly local feed, because FypSource was still "library" and
    /// <see cref="ServeRemoteBatch"/> refused to fetch (found live 2026-08-12: the flashes
    /// consumer pulled 30 entries while the feed pulled zero in the same session).
    ///
    /// Web parity: the web app has exactly one source of remote content. Here the feed's
    /// own picker wins whenever it has left "library"; otherwise the app-wide source
    /// carries over, gated on the same consent property every other surface reads.
    /// </summary>
    private static string EffectiveFeedSource()
    {
        var s = App.Settings?.Current;
        if (s == null) return "library";
        if (s.FypSource != "library") return s.FypSource;
        if (s.MediaSource != "local" && s.HasRemoteMediaConsent) return s.MediaSource;
        return "library";
    }

    /// <summary>The mixed-mode remote share the effective source implies: the feed's own
    /// ratio when its picker is active, the app-wide ratio when the source carried over.</summary>
    private static int EffectiveOnlineRatio()
    {
        var s = App.Settings?.Current;
        if (s == null) return 30;
        return s.FypSource != "library" ? s.FypOnlineRatio : s.RemoteMediaRatio;
    }

    private static async void ServeRemoteBatch()
    {
        var s = App.Settings?.Current;
        if (s == null || !s.HasRemoteMediaConsent || EffectiveFeedSource() == "library")
        {
            // The page only asks when it believes remote is on, so a refusal here is the
            // interesting half of a "my feed is empty" report — say why, quietly.
            App.Logger?.Debug("FypHost: need-remote refused (source {Fyp}/{App}, consent {Consent})",
                s?.FypSource, s?.MediaSource, s?.HasRemoteMediaConsent);
            return;
        }
        if (System.Threading.Interlocked.CompareExchange(ref _remoteFetchInFlight, 1, 0) != 0) return;
        try
        {
            var (entries, error) = await FypOnlineCoordinator.Fyp
                .FetchBatchAsync(System.Threading.CancellationToken.None).ConfigureAwait(false);

            var win = _host?.Window;
            if (win == null) return;   // feed closed while fetching
            await win.Dispatcher.InvokeAsync(() =>
            {
                if (_host == null) return;
                if (entries.Count > 0)
                    _host.Post(new { type = "assets-append", assets = entries });
                // Status goes out either way so the page can clear its loading state or
                // show "offline" instead of an eternal spinner.
                _host.Post(new { type = "online-status", ok = error == null, error, added = entries.Count });
            });
        }
        catch (Exception ex) { App.Logger?.Warning("FypHost: remote batch failed: {E}", ex.Message); }
        finally { System.Threading.Interlocked.Exchange(ref _remoteFetchInFlight, 0); }
    }

    private static JObject? LoadStats()
    {
        try
        {
            if (File.Exists(StatsFilePath))
                return JObject.Parse(File.ReadAllText(StatsFilePath));
        }
        catch (Exception ex) { App.Logger?.Warning("FypHost: stats load failed: {E}", ex.Message); }
        return null;
    }

    private static void SaveStats(JObject stats)
    {
        try
        {
            Directory.CreateDirectory(App.UserDataPath);
            File.WriteAllText(StatsFilePath, stats.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception ex) { App.Logger?.Warning("FypHost: stats save failed: {E}", ex.Message); }
    }

    // ================== window mechanics: GHOST MODE (opacity + click-through) ==================
    //
    // The point is "park the feed over the desktop and keep working": a faint, unclickable pane of
    // the feed running behind whatever the user is actually doing.
    //
    // THE MECHANISM IS A DWM LIVE-THUMBNAIL MIRROR (the OnTopReplica technique), not a style dance
    // on the feed window. Ghosting moves the REAL WebView2 window fully off the virtual desktop
    // (shown, never minimized — a minimized source freezes the thumbnail) and shows a
    // FypGhostOverlay at its former rectangle: an empty WPF window carrying a live DWM thumbnail
    // of the real one. Audio keeps playing, blink/gaze keep driving the real page, and the mirror
    // simply shows the result — see-through and input-transparent for real.
    //
    // Why not just style the feed window (this was shipped once and reproduced live 2026-08-03):
    //   - WPF Window.Opacity: composition-tree property; the WebView2 child HWND ignores it.
    //   - AllowsTransparency=true: per-pixel UpdateLayeredWindow path; WebView2 does not paint at
    //     all (see the class comment on ChaosWebViewHost).
    //   - WS_EX_LAYERED + SetLayeredWindowAttributes(LWA_ALPHA): the moment the constant-alpha
    //     path ENGAGES, the WebView2 content goes solid BLACK and stays black — DWM's layered
    //     redirection never sees the Chromium child processes' cross-process DirectComposition
    //     visuals ("clicking the opacity brings up a black screen").
    //   - WS_EX_TRANSPARENT alone gave real click-through, but paired with a page-side dim overlay
    //     it only ever made the feed DARKER — you still could not see the desktop behind it.
    // NOTHING may ever call SetLayeredWindowAttributes on the FEED window. On the MIRROR window
    // SLWA is used with LWA_COLORKEY only — pixel-measured on this machine: LWA_ALPHA multiplies
    // the DWM thumbnail together with the surface (no alpha value works), while a color-keyed
    // surface drops out of composition and lets DWM_TNP_OPACITY blend the mirror against the
    // desktop. See FypGhostOverlay for the styles, the DWM structs and the DPI math.

    /// <summary>Park the real feed window off-screen and put the see-through DWM mirror in its
    /// place. Idempotent; no-op before the window exists.</summary>
    private static void EnterGhost()
    {
        if (_ghosted) return;
        var win = _host?.Window;
        if (win == null) return;
        if (!win.Dispatcher.CheckAccess())
        {
            // WebView2 raises WebMessageReceived on the thread that created it, so ApplySetting is
            // already on the UI thread in practice - this is belt and braces, not a hot path.
            try { win.Dispatcher.BeginInvoke(new Action(EnterGhost)); } catch { }
            return;
        }
        try
        {
            var hwnd = new WindowInteropHelper(win).Handle;
            if (hwnd == IntPtr.Zero) return;

            // A maximized window has no meaningful Left/Top to mirror or restore, so normalize
            // first (remembering to put it back) and read the restored rect.
            _ghostWasMaximized = win.WindowState == WindowState.Maximized;
            if (win.WindowState != WindowState.Normal) win.WindowState = WindowState.Normal;

            _ghostLeft = win.Left;
            _ghostTop = win.Top;
            double w = win.ActualWidth > 0 ? win.ActualWidth : win.Width;
            double h = win.ActualHeight > 0 ? win.ActualHeight : win.Height;
            if (!(w > 0) || !(h > 0))
            {
                // Nothing to mirror yet, and nothing has moved — put the state back and bail.
                if (_ghostWasMaximized) win.WindowState = WindowState.Maximized;
                _ghostWasMaximized = false;
                return;
            }
            var bounds = new Rect(_ghostLeft, _ghostTop, w, h);

            // From here on ExitGhost is the only correct way out, so arm it BEFORE the window
            // moves: a throw below must never leave the real window parked off-screen.
            _ghosted = true;

            // The overlay ctor captures the feed's home monitor from its HWND, so it must run
            // BEFORE the park moves the window off every monitor.
            _ghost = new FypGhostOverlay(hwnd, bounds, App.Settings?.Current?.FypWindowOpacity ?? 1.0,
                onGear: GhostGearClicked, onMute: GhostMuteClicked,
                isMuted: () => App.Settings?.Current?.FypMuted ?? false);

            // Park it past the right edge of every monitor, RESIZED so the client area is
            // exactly the home monitor. The mirror letterboxes at the source's aspect, and a
            // windowed source (screen-wide but short of the taskbar) is wider-aspect than the
            // monitor — that letterbox was the bare band across the top of the ghost (its twin
            // sat invisibly over the taskbar). Sized 1:1 there is nothing to letterbox.
            // One native call, physical px end to end: atomic (no flash of a monitor-sized
            // window at the old spot) and an exact round-trip for ExitGhost regardless of what
            // DPI the parking spot resolves to. Still SHOWN (DWM keeps compositing it, so the
            // thumbnail stays live); the browser flags keep Chromium from throttling it.
            GetWindowRect(hwnd, out var wr);
            GetClientRect(hwnd, out var cr);
            _restoreX = wr.Left; _restoreY = wr.Top;
            _restoreW = wr.Right - wr.Left; _restoreH = wr.Bottom - wr.Top;
            int chromeW = _restoreW - cr.Right;    // window minus client: borders + title bar
            int chromeH = _restoreH - cr.Bottom;
            var scr = System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
            var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            _parkX = vs.Right + 200;
            _parkY = scr.Y;
            _parkW = scr.Width + Math.Max(0, chromeW);
            _parkH = scr.Height + Math.Max(0, chromeH);
            SetWindowPos(hwnd, IntPtr.Zero, _parkX, _parkY, _parkW, _parkH,
                SWP_NOZORDER | SWP_NOACTIVATE);

            // Sever the MainWindow owner link for the duration. Off-screen, the z-order glue
            // buys nothing — and it is the one path Show Desktop / Win+D still reaches the
            // parked window through: minimizing the OWNER makes USER32 hide its owned windows,
            // a hidden source stops being composed (WindowState/IsIconic never change, so no
            // state event fires), and the mirror freezes on its last frame. A probe of this
            // exact park + mirror + browser-flags configuration WITHOUT an owner link sailed
            // through Show Desktop live (2026-08-04).
            _host?.SuspendMainWindowGlue(true);

            // Show Desktop / Win+D also minimizes every restorable top-level window — the
            // parked source included. Veto SC_MINIMIZE while ghosted (the window is off-screen,
            // minimizing it means nothing), veto the owner-cascade hide, and if a minimize or a
            // hide lands anyway, heal it — restore, re-park, re-register the thumbnail.
            _ghostHookSource = HwndSource.FromHwnd(hwnd);
            _ghostHook = GhostWndProc;
            _ghostHookSource?.AddHook(_ghostHook);
            _ghostStateGuard = OnGhostWindowStateChanged;
            win.StateChanged += _ghostStateGuard;
            StartGhostDiagnostics(hwnd);
            // That tick is no longer only diagnostics — it carries the topmost re-assert, the one
            // thing that keeps the mirror from being buried for the rest of the session. Its own
            // catch swallows a construction failure, so a ghost could go live with no watchdog at
            // all. Treat a missing timer as a failed EnterGhost and let the handler below unwind
            // it, rather than parking the real window off-screen behind a mirror nothing maintains.
            if (_ghostDiagTimer == null)
                throw new InvalidOperationException("ghost watchdog timer failed to start");

            _ghost.Show();
            GetWindowRect(hwnd, out var applied);
            App.Logger?.Information(
                "FypHostService: ghost mode ON (mirror on {Mon}, park requested {X}x{Y} {W}x{H}px, applied {AW}x{AH}px)",
                scr, _parkX, _parkY, _parkW, _parkH,
                applied.Right - applied.Left, applied.Bottom - applied.Top);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("FypHost.EnterGhost failed: {E}", ex.Message);
            ExitGhost();   // never leave the real window parked off-screen with no mirror
        }
    }

    /// <summary>The ghost gear button: give the window back and open the options popover in one
    /// click, so tweaking settings from ghost mode is one step instead of Esc-then-gear.</summary>
    private static void GhostGearClicked()
    {
        try
        {
            ExitGhost();
            _host?.Window?.Activate();
            _host?.Post(new { type = "openOptions" });
        }
        catch (Exception ex) { App.Logger?.Debug("FypHost: ghost gear failed: {E}", ex.Message); }
    }

    /// <summary>The ghost speaker button: flip mute WITHOUT leaving ghost mode — the parked
    /// window keeps playing audio, so this is the one control worth reaching in ghost.</summary>
    private static void GhostMuteClicked()
    {
        try
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            s.FypMuted = !s.FypMuted;
            _host?.Post(new { type = "setMuted", on = s.FypMuted });
            _ghost?.RefreshMuteGlyph();
        }
        catch (Exception ex) { App.Logger?.Debug("FypHost: ghost mute failed: {E}", ex.Message); }
    }

    /// <summary>Close the mirror, bring the real window back to where it was, and tell the page to
    /// un-check its toggle. The panic ladder's first rung; idempotent and safe when off.</summary>
    public static void ExitGhost()
    {
        if (!_ghosted) return;
        var win = _host?.Window;
        if (win != null && !win.Dispatcher.CheckAccess())
        {
            try { win.Dispatcher.BeginInvoke(new Action(ExitGhost)); } catch { }
            return;
        }
        _ghosted = false;
        var g = _ghost;
        _ghost = null;
        try { g?.Close(); }
        catch (Exception ex) { App.Logger?.Debug("FypHost.ExitGhost close: {E}", ex.Message); }
        try
        {
            StopGhostDiagnostics();
            if (win != null && _ghostStateGuard != null) win.StateChanged -= _ghostStateGuard;
            _ghostStateGuard = null;
            if (_ghostHookSource != null && _ghostHook != null) _ghostHookSource.RemoveHook(_ghostHook);
            _ghostHookSource = null;
            _ghostHook = null;
        }
        catch (Exception ex) { App.Logger?.Debug("FypHost.ExitGhost unhook: {E}", ex.Message); }
        try
        {
            if (win != null)
            {
                // A minimize can still have slipped through (the veto arms mid-Show-Desktop, a
                // race loses): un-minimize FIRST or the rect restore below lands on a window
                // nobody can see. Windows' own restore rect may be the parked spot by now —
                // the native SetWindowPos right after puts the real geometry back regardless.
                if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
                var hwnd = new WindowInteropHelper(win).Handle;
                if (hwnd != IntPtr.Zero && _restoreW > 0 && _restoreH > 0)
                    SetWindowPos(hwnd, IntPtr.Zero, _restoreX, _restoreY, _restoreW, _restoreH,
                        SWP_NOZORDER | SWP_NOACTIVATE);
                else { win.Left = _ghostLeft; win.Top = _ghostTop; }
                if (_ghostWasMaximized) win.WindowState = WindowState.Maximized;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("FypHost.ExitGhost restore: {E}", ex.Message); }
        // Window is back on-screen: give it its owner link (and its z-order slot) back.
        try { _host?.SuspendMainWindowGlue(false); }
        catch (Exception ex) { App.Logger?.Debug("FypHost.ExitGhost glue resume: {E}", ex.Message); }
        _ghostWasMaximized = false;
        _lastGhostExitUtc = DateTime.UtcNow;
        try { _host?.Post(new { type = "clickThrough", on = false }); }
        catch (Exception ex) { App.Logger?.Debug("FypHost.ExitGhost post: {E}", ex.Message); }
        App.Logger?.Information("FypHostService: ghost mode off");
    }

    /// <summary>While ghosted, refuse everything that would stop the parked window from being
    /// composed (a source DWM stops composing = a mirror frozen on its last frame):
    /// SC_MINIMIZE (Show Desktop, Win+D, Win+M), and any native HIDE — the owner-minimize
    /// cascade hides owned windows entirely inside USER32, so neither WindowState nor IsIconic
    /// ever changes and only this message ever tells us. Vetoing is primary; a hide that lands
    /// through a path DefWindowProc never sees is healed right after. Never swallows anything
    /// when not ghosted.</summary>
    private static IntPtr GhostWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_ghosted) return IntPtr.Zero;
        if (msg == WM_SYSCOMMAND && ((long)wParam & 0xFFF0) == SC_MINIMIZE)
        {
            App.Logger?.Information("FypHostService: ghost vetoed SC_MINIMIZE");
            handled = true;
        }
        else if (msg == WM_SHOWWINDOW && wParam == IntPtr.Zero)
        {
            long why = (long)lParam;
            App.Logger?.Information("FypHostService: ghost saw WM_SHOWWINDOW hide (lParam={Why})", why);
            if (why != 0)
            {
                // A cascade hide (SW_PARENTCLOSING et al.): DefWindowProc is what would hide
                // us — refuse it, and re-check visibility once the storm passes.
                handled = true;
            }
            try { _host?.Window?.Dispatcher.BeginInvoke(new Action(HealGhostVisibility)); } catch { }
        }
        return IntPtr.Zero;
    }

    /// <summary>If the parked window ended up natively hidden while ghosted (WPF still believes
    /// it is Visible — the hide happened inside USER32), show it again without activation and
    /// re-register the thumbnail so the mirror comes back live.</summary>
    private static void HealGhostVisibility()
    {
        try
        {
            if (!_ghosted) return;
            var win = _host?.Window;
            if (win == null) return;
            var hwnd = new WindowInteropHelper(win).Handle;
            if (hwnd == IntPtr.Zero) return;
            if (!IsWindowVisible(hwnd))
            {
                ShowWindow(hwnd, SW_SHOWNA);
                App.Logger?.Information("FypHostService: parked window was natively hidden — re-shown");
            }
            _ghost?.RefreshThumbnail();
        }
        catch (Exception ex) { App.Logger?.Debug("FypHost: ghost visibility heal failed: {E}", ex.Message); }
    }

    /// <summary>The belt to the veto's braces: a minimize that reached the parked window some
    /// other way is healed after the fact — back to Normal, back to the parking spot (restore-
    /// from-minimize goes to Windows' saved rect, which can be anywhere by now), and a FRESH
    /// thumbnail registration, because DWM does not reliably resume the old one.</summary>
    private static void OnGhostWindowStateChanged(object? sender, EventArgs e)
    {
        if (!_ghosted) return;
        var win = _host?.Window;
        if (win == null || win.WindowState != WindowState.Minimized) return;
        // Deferred: this event is raised off the window's own WM_SIZE — let it finish first.
        win.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (!_ghosted) return;
                var w = _host?.Window;
                if (w == null || w.WindowState != WindowState.Minimized) return;
                w.WindowState = WindowState.Normal;
                var hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd != IntPtr.Zero)
                    SetWindowPos(hwnd, IntPtr.Zero, _parkX, _parkY, _parkW, _parkH,
                        SWP_NOZORDER | SWP_NOACTIVATE);
                _ghost?.RefreshThumbnail();
                App.Logger?.Information("FypHostService: parked window was minimized (Show Desktop?) — healed");
            }
            catch (Exception ex) { App.Logger?.Debug("FypHost: ghost minimize heal failed: {E}", ex.Message); }
        }));
    }

    // While ghosted, log the parked window's real native state every few seconds. This is the
    // instrumentation that turns the next "it froze" report into a diagnosis instead of a
    // guess: visible/iconic/cloaked and the applied rect, straight from USER32/DWM — the exact
    // three ways a window can silently stop being composed.
    private static System.Windows.Threading.DispatcherTimer? _ghostDiagTimer;

    private static void StartGhostDiagnostics(IntPtr hwnd)
    {
        try
        {
            StopGhostDiagnostics();
            _ghostDiagTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5),
            };
            _ghostDiagTimer.Tick += (_, _) =>
            {
                try
                {
                    if (!_ghosted) { StopGhostDiagnostics(); return; }
                    DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int));
                    GetWindowRect(hwnd, out var r);
                    App.Logger?.Information(
                        "FypGhost diag: visible={Vis} iconic={Icon} cloaked={Cloak} rect=({X},{Y} {W}x{H})",
                        IsWindowVisible(hwnd), IsIconic(hwnd), cloaked,
                        r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                }
                catch (Exception ex) { App.Logger?.Debug("FypGhost diag failed: {E}", ex.Message); }
                ReassertGhostTopmost();
            };
            _ghostDiagTimer.Start();
        }
        catch (Exception ex) { App.Logger?.Debug("FypHost.StartGhostDiagnostics: {E}", ex.Message); }
    }

    /// <summary>Topmost is a z-order BAND, not a promise: the mirror and its two buttons are
    /// created TopMost once, and anything shown topmost afterwards lands above them. Nothing on
    /// the ghost path ever puts them back (every SetWindowPos here passes SWP_NOZORDER, and the
    /// owner link that used to carry z-order is deliberately severed while parked), so one
    /// full-screen intruder buried the feed for the rest of the session. Re-assert on the tick
    /// that already runs while ghosted — biggest window first, so the mirror lands under its own
    /// buttons rather than over them.</summary>
    private static void ReassertGhostTopmost()
    {
        try
        {
            if (!_ghosted || _ghost == null) return;
            List<System.Windows.Forms.Form>? ghostForms = null;
            foreach (System.Windows.Forms.Form f in System.Windows.Forms.Application.OpenForms)
            {
                if (!f.IsHandleCreated || !f.TopMost) continue;
                if (!string.Equals(f.Text, GhostFormTitle, StringComparison.Ordinal)) continue;
                (ghostForms ??= new List<System.Windows.Forms.Form>()).Add(f);
            }
            if (ghostForms == null) return;
            ghostForms.Sort((a, b) => ((long)b.Width * b.Height).CompareTo((long)a.Width * a.Height));
            foreach (var f in ghostForms)
                SetWindowPos(f.Handle, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch (Exception ex) { App.Logger?.Debug("FypGhost topmost re-assert failed: {E}", ex.Message); }
    }

    private static void StopGhostDiagnostics()
    {
        try { _ghostDiagTimer?.Stop(); } catch { }
        _ghostDiagTimer = null;
    }

    // ------------------------------- native (ghost mode) -------------------------------

    private const int WM_SYSCOMMAND = 0x0112;
    private const int WM_SHOWWINDOW = 0x0018;
    private const int SC_MINIMIZE = 0xF020;
    private const int SW_SHOWNA = 8;
    private const int DWMWA_CLOAKED = 14;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    /// <summary>Window text every <see cref="FypGhostOverlay"/> form carries — the only handle
    /// this class has on the mirror and its buttons (the overlay owns them privately).</summary>
    private const string GhostFormTitle = "For You";

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    // ============================= webcam eye control =============================
    //
    // Blink = swap one tile on the active page (the tile being LOOKED at when gaze mode is on
    // and calibrated, otherwise a random one). Eyes held shut for 2s = change the whole page.
    // The gestures themselves live in the page (main.js); this end owns the camera lifecycle,
    // the coordinate transform and the status protocol.
    //
    // Lifecycle rules, copied from the Deeper player's pre-play prompt:
    //   * consent is gated on WebcamTrackingService.IsConsentCurrent() and re-prompted through
    //     the same WebcamConsentDialog every other call site uses;
    //   * Start() is NEVER called on the UI thread (it opens the camera and builds three ONNX
    //     sessions synchronously) — always await StartAsync();
    //   * we only stop the camera we started ourselves: a webcam that was already tracking for
    //     the Lab / Deeper / blink trainer is left running when the feed closes.

    private static bool _eyeSubscribed;      // OnBlink + OnEyesClosedLong attached
    private static bool _gazeSubscribed;     // OnGazeMove attached (needs eyeGaze + calibration)
    private static bool _fypStartedWebcam;   // WE turned the camera on -> WE turn it off
    private static bool _eyeEnabling;        // an enable sequence is in flight (re-entrancy guard)
    private static DateTime _lastGazePostAt = DateTime.MinValue;

    // OnGazeMove fires per processed frame (~30Hz). The page only needs to know which TILE is
    // being looked at, so coalesce to 10Hz and spare the WebView2 message pump.
    private const int GazePostIntervalMs = 100;

    /// <summary>Consent → camera → subscriptions, reporting each step to the page. Fire and
    /// forget: OnPageMessage must never block on a camera open.</summary>
    private static async void EnableEyeControl()
    {
        if (_eyeEnabling) return;
        _eyeEnabling = true;
        try
        {
            // Hop off the WebView2 message callback (and off OnReady) before anything
            // modal or long-running: a consent dialog pumped from inside a web-message
            // handler re-enters the browser's own message loop.
            await System.Threading.Tasks.Task.Yield();

            var svc = App.Webcam;
            if (svc == null) { FailEyeControl("no-camera"); return; }

            if (!WebcamTrackingService.IsConsentCurrent())
            {
                var dlg = new WebcamConsentDialog();
                var owner = _host?.Window;
                if (owner != null && owner.IsLoaded) dlg.Owner = owner;
                var ok = dlg.ShowDialog();
                if (ok != true || !dlg.ConsentGiven) { FailEyeControl("consent"); return; }
            }

            if (!svc.IsRunning)
            {
                PostEyeStatus("starting");   // the global loading splash rides OnStartupProgress
                // NEVER Start() on the UI thread: it opens the camera and builds three ONNX
                // sessions synchronously and can block for tens of seconds.
                bool started = await svc.StartAsync();
                if (!started)
                {
                    FailEyeControl(svc.State == WebcamTrackingState.Error ? "error" : "no-camera");
                    return;
                }
                _fypStartedWebcam = true;
            }

            // The window may have closed, or the user may have flipped the toggle back off,
            // while the camera was opening. Undo rather than leave an orphan camera running.
            if (_host == null || App.Settings?.Current?.FypEyeControl != true)
            {
                DisableEyeControl();
                return;
            }

            svc.OnBlink += OnEyeBlink;
            svc.OnEyesClosedLong += OnEyeClosedLong;
            _eyeSubscribed = true;
            SyncGazeSubscription();
            PostEyeStatus(null);
            App.Logger?.Information("FypHost: eye control ON (gaze {Gaze})",
                _gazeSubscribed ? "on" : "off");
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("FypHost: eye control enable failed: {E}", ex.Message);
            FailEyeControl("error");
        }
        finally { _eyeEnabling = false; }
    }

    /// <summary>Enable failed: flip the persisted setting back off, tear down whatever got
    /// half-built, and tell the page why so its toggle resets with a reason.</summary>
    private static void FailEyeControl(string reason)
    {
        try { if (App.Settings?.Current is { } s) s.FypEyeControl = false; } catch { }
        DisableEyeControl();
        PostEyeStatus(reason);
    }

    /// <summary>Unsubscribe everything and hand the camera back. Safe to call repeatedly and
    /// when nothing was ever enabled.</summary>
    private static void DisableEyeControl()
    {
        var svc = App.Webcam;
        try
        {
            if (svc != null)
            {
                if (_eyeSubscribed)
                {
                    svc.OnBlink -= OnEyeBlink;
                    svc.OnEyesClosedLong -= OnEyeClosedLong;
                }
                if (_gazeSubscribed) svc.OnGazeMove -= OnEyeGaze;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("FypHost: eye unsubscribe: {E}", ex.Message); }
        _eyeSubscribed = false;
        _gazeSubscribed = false;

        try
        {
            if (_fypStartedWebcam && svc?.IsRunning == true) svc.Stop();
        }
        catch (Exception ex) { App.Logger?.Debug("FypHost: webcam stop: {E}", ex.Message); }
        _fypStartedWebcam = false;
    }

    /// <summary>Gaze costs a message every 100ms, so it is only wired when it can actually
    /// resolve a tile: eye control on, gaze mode on, AND a calibration loaded (uncalibrated,
    /// OnGazeMove never fires at all — the page falls back to a random tile).</summary>
    private static void SyncGazeSubscription()
    {
        var svc = App.Webcam;
        var s = App.Settings?.Current;
        bool want = _eyeSubscribed
                    && svc != null
                    && s?.FypEyeControl == true
                    && s.FypEyeGaze
                    && svc.Calibration != null;
        if (want == _gazeSubscribed) return;
        try
        {
            if (svc == null) { _gazeSubscribed = false; return; }
            if (want) svc.OnGazeMove += OnEyeGaze;
            else svc.OnGazeMove -= OnEyeGaze;
            _gazeSubscribed = want;
        }
        catch (Exception ex) { App.Logger?.Debug("FypHost: gaze subscribe: {E}", ex.Message); }
    }

    /// <summary>Whatever just happened, tell the page what is actually true so its toggles and
    /// status line render reality. reason ∈ null|"consent"|"starting"|"no-camera"|"error".</summary>
    private static void PostEyeStatus(string? reason)
    {
        try
        {
            var s = App.Settings?.Current;
            var svc = App.Webcam;
            _host?.Post(new
            {
                type = "eyeStatus",
                enabled = s?.FypEyeControl ?? false,
                gaze = s?.FypEyeGaze ?? false,
                running = svc?.IsRunning == true,
                calibrated = svc?.Calibration != null,
                reason,
            });
        }
        catch (Exception ex) { App.Logger?.Debug("FypHost: eyeStatus post: {E}", ex.Message); }
    }

    /// <summary>The page's Calibrate button: run the native gaze-calibration dialog over the
    /// feed, then re-wire gaze and tell the page the new state. UI thread only.</summary>
    private static void RunGazeCalibration()
    {
        try
        {
            if (WebcamCalibrationWindow.IsShowing) return;
            if (App.Webcam?.IsRunning != true)
            {
                // The page disables the button without a running camera; this is belt and braces.
                PostEyeStatus(null);
                return;
            }
            ExitGhost();   // a parked window cannot host a modal dot dance
            WebcamCalibrationWindow.ShowDialogWithRecalibrate(_host?.Window);
            SyncGazeSubscription();
            PostEyeStatus(null);
        }
        catch (Exception ex) { App.Logger?.Warning("FypHost: gaze calibration failed: {E}", ex.Message); }
    }

    // All three handlers already arrive on the UI thread (the service Dispatch()es every event),
    // which is also the thread that owns the WebView2 — Post() is safe from here.
    private static void OnEyeBlink()
    {
        try { _host?.Post(new { type = "blink" }); }
        catch (Exception ex) { App.Logger?.Debug("FypHost: blink post: {E}", ex.Message); }
    }

    private static void OnEyeClosedLong()
    {
        try { _host?.Post(new { type = "eyesClosed" }); }
        catch (Exception ex) { App.Logger?.Debug("FypHost: eyesClosed post: {E}", ex.Message); }
    }

    /// <summary>
    /// Gaze point → normalized WebView client coordinates.
    ///
    /// OnGazeMove emits DIPs local to the CALIBRATED monitor's top-left, at THAT monitor's DPI
    /// (the calibration window was borderless-maximized on it). So: back to physical desktop
    /// pixels through the calibration's own DpiScale/origin, then into the WebView's client
    /// space through the WebView's own DPI — the two can differ on a mixed-DPI desktop, which
    /// is exactly why the conversion cannot be skipped in either direction.
    ///
    /// The page gets 0..1 plus an <c>inside</c> flag: the point is clamped so it is always a
    /// usable number, and the flag lets the page ignore gaze that is off the window entirely
    /// instead of pinning it to an edge tile.
    /// </summary>
    private static void OnEyeGaze(Point p)
    {
        try
        {
            var now = DateTime.UtcNow;
            if ((now - _lastGazePostAt).TotalMilliseconds < GazePostIntervalMs) return;

            var web = _host?.WebView;
            if (web == null || !web.IsLoaded || web.ActualWidth <= 0 || web.ActualHeight <= 0) return;

            var bounds = App.Webcam?.Calibration?.MonitorBounds;
            double calDpi = bounds is { DpiScale: > 0.25 and < 8.0 } ? bounds.DpiScale : 1.0;
            double originX = bounds?.DeviceName != null ? bounds.X : 0;
            double originY = bounds?.DeviceName != null ? bounds.Y : 0;
            double physX = p.X * calDpi + originX;
            double physY = p.Y * calDpi + originY;

            var dpi = VisualTreeHelper.GetDpi(web);
            double sx = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
            double sy = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;
            var webOriginPx = web.PointToScreen(new Point(0, 0));   // physical px

            double nx = ((physX - webOriginPx.X) / sx) / web.ActualWidth;
            double ny = ((physY - webOriginPx.Y) / sy) / web.ActualHeight;
            bool inside = nx >= 0 && nx <= 1 && ny >= 0 && ny <= 1;

            _lastGazePostAt = now;
            _host?.Post(new
            {
                type = "gaze",
                x = Math.Clamp(nx, 0, 1),
                y = Math.Clamp(ny, 0, 1),
                inside,
            });
        }
        catch (Exception ex) { App.Logger?.Debug("FypHost: gaze post: {E}", ex.Message); }
    }

}
