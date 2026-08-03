using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;

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
    private static double _ghostLeft, _ghostTop;      // the real window's parked-from position
    private static bool _ghostWasMaximized;           // it was maximized before we normalized it
    private static DateTime _lastGhostExitUtc = DateTime.MinValue;

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
                _meta?.Update((string?)o["id"], (long?)o["durationMs"], (int?)o["width"], (int?)o["height"]);
                break;
            }
            case "clip-viewed":
            {
                if (!AllowClipXp()) break;
                try { App.Progression?.AddXP(5, XPSource.Fyp); }
                catch (Exception ex) { App.Logger?.Debug("FypHost: clip XP failed: {E}", ex.Message); }
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
            }
        }
        catch (Exception ex) { App.Logger?.Debug("FypHost: settings-changed {Key} failed: {E}", key, ex.Message); }
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

            // Park it past the right edge of every monitor. Still SHOWN (DWM keeps compositing it,
            // so the thumbnail stays live); the browser flags keep Chromium from throttling it.
            win.Left = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth + 200;

            _ghost.Show();
            App.Logger?.Information(
                "FypHostService: ghost mode ON (mirror at {L}x{T} {W}x{H}, real window parked at {Park})",
                (int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height, (int)win.Left);
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
            if (win != null)
            {
                win.Left = _ghostLeft;
                win.Top = _ghostTop;
                if (_ghostWasMaximized) win.WindowState = WindowState.Maximized;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("FypHost.ExitGhost restore: {E}", ex.Message); }
        _ghostWasMaximized = false;
        _lastGhostExitUtc = DateTime.UtcNow;
        try { _host?.Post(new { type = "clickThrough", on = false }); }
        catch (Exception ex) { App.Logger?.Debug("FypHost.ExitGhost post: {E}", ex.Message); }
        App.Logger?.Information("FypHostService: ghost mode off");
    }

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
