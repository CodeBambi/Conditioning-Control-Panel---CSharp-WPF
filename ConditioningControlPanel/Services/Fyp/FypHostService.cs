using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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

    // ---- desktop-parking state (see the "window mechanics" region at the bottom) ----
    // Session-only: click-through ALWAYS starts off, so a relaunch can never come up as an
    // un-clickable ghost the user has no way to grab.
    private static bool _clickThrough;
    // WS_EX_LAYERED is applied lazily and then left on (at alpha 255 when fully opaque) — flipping
    // the bit back off re-stamps the frame for no gain.
    private static bool _layered;

    /// <summary>True while the window is passing clicks through to whatever is behind it.</summary>
    public static bool IsClickThrough => _clickThrough;

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
                // An autoplaying feed: media must start without a user gesture.
                ExtraBrowserArguments = "--autoplay-policy=no-user-gesture-required",
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
        var h = _host;
        _host = null;
        // Session-only window state dies with the window: the next Launch() starts clickable and
        // re-applies the persisted opacity from scratch.
        _clickThrough = false;
        _layered = false;
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
            // Restore the parked opacity now the window is up. Click-through is deliberately NOT
            // restored (session-only) — the page's toggle comes up off to match.
            if ((s?.FypWindowOpacity ?? 1.0) < 1.0) ApplyWindowState();
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
        if (key == "clickThrough")
        {
            try
            {
                bool on = (bool?)value ?? false;
                if (on == _clickThrough) return;
                _clickThrough = on;
                ApplyWindowState();
                App.Logger?.Information("FypHost: click-through {State}", on ? "ON" : "off");
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
                // The setter clamps to 0.30-1.0; read it back so the live window and the stored
                // value can never disagree about what a rogue payload asked for.
                case "windowOpacity": s.FypWindowOpacity = (double?)value ?? 1.0; ApplyWindowState(); break;
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

    // ======================= window mechanics: opacity + click-through =======================
    //
    // The point of both is "park the feed over the desktop and keep working": a faint, unclickable
    // pane of the feed running behind whatever the user is actually doing.
    //
    // OPACITY — WPF's Window.Opacity is useless here. It is a composition-tree property, and the
    // WebView2 surface is a CHILD HWND that DWM composites separately (the classic airspace
    // problem): the WPF chrome would fade and the video would stay solid. AllowsTransparency=true
    // is worse still — that is the per-pixel UpdateLayeredWindow path, in which a WebView2 child
    // does not paint at all (see the class comment on ChaosWebViewHost). The one technique that
    // does work is the OTHER layered-window mode: WS_EX_LAYERED + SetLayeredWindowAttributes with
    // LWA_ALPHA, which since Windows 8 makes DWM apply a constant alpha to the whole window
    // hierarchy, child HWNDs included, with no bearing on how anything paints.
    //
    // CLICK-THROUGH — WS_EX_TRANSPARENT on the top-level window (which also requires WS_EX_LAYERED
    // to actually pass input through, not merely skip hit-testing). That alone is not enough here:
    // WebView2 parents its own child HWNDs (Chrome_WidgetWin_*, "Intermediate D3D Window") under
    // ours, and a child still answers WM_NCHITTEST for its own rectangle — which is exactly the
    // rectangle the video fills. So the flag is stamped on every descendant too, and cleared off
    // every descendant when click-through goes away.

    /// <summary>Push the current opacity/click-through state onto the live window. Safe to call
    /// from any thread and before the window exists (then it is simply a no-op).</summary>
    private static void ApplyWindowState()
    {
        var win = _host?.Window;
        if (win == null) return;
        if (!win.Dispatcher.CheckAccess())
        {
            // WebView2 raises WebMessageReceived on the thread that created it, so ApplySetting is
            // already on the UI thread in practice - this is belt and braces, not a hot path.
            try { win.Dispatcher.BeginInvoke(new Action(ApplyWindowState)); } catch { }
            return;
        }
        try
        {
            var hwnd = new WindowInteropHelper(win).Handle;
            if (hwnd == IntPtr.Zero) return;

            double opacity = Math.Clamp(App.Settings?.Current?.FypWindowOpacity ?? 1.0, 0.30, 1.0);
            // Never touch the frame of a window that has only ever been fully opaque and clickable:
            // the layered path is opt-in, so an untouched feed stays exactly as it was before.
            if (!_layered && opacity >= 1.0 && !_clickThrough) return;

            long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            long want = ex | WS_EX_LAYERED;
            if (_clickThrough) want |= WS_EX_TRANSPARENT;
            else want &= ~WS_EX_TRANSPARENT;
            if (want != ex) SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(want));
            _layered = true;

            SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(opacity * 255.0), LWA_ALPHA);
            ApplyChildClickThrough(hwnd, _clickThrough);
        }
        catch (Exception ex) { App.Logger?.Debug("FypHost.ApplyWindowState: {E}", ex.Message); }
    }

    /// <summary>Stamp (or strip) WS_EX_TRANSPARENT on every descendant HWND of the host window.
    /// WebView2's own child windows hit-test independently of ours.</summary>
    private static void ApplyChildClickThrough(IntPtr parent, bool on)
    {
        try { EnumChildWindows(parent, _childStyler, on ? new IntPtr(1) : IntPtr.Zero); }
        catch (Exception ex) { App.Logger?.Debug("FypHost.ApplyChildClickThrough: {E}", ex.Message); }
    }

    // Held in a static field so the GC cannot collect the delegate under the native callback.
    private static readonly EnumWindowsProc _childStyler = (child, lParam) =>
    {
        try
        {
            long ex = GetWindowLongPtr(child, GWL_EXSTYLE).ToInt64();
            long want = lParam != IntPtr.Zero ? (ex | WS_EX_TRANSPARENT) : (ex & ~WS_EX_TRANSPARENT);
            if (want != ex) SetWindowLongPtr(child, GWL_EXSTYLE, new IntPtr(want));
        }
        catch { }
        return true;   // keep enumerating
    };

    /// <summary>Give the window back its clicks and tell the page to un-check its toggle.
    /// The panic ladder's first rung; also safe to call when click-through is already off.</summary>
    public static void DisableClickThrough()
    {
        if (!_clickThrough) return;
        _clickThrough = false;
        ApplyWindowState();
        try { _host?.Post(new { type = "clickThrough", on = false }); }
        catch (Exception ex) { App.Logger?.Debug("FypHost.DisableClickThrough post: {E}", ex.Message); }
        App.Logger?.Information("FypHostService: click-through disabled");
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

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_LAYERED = 0x00080000L;
    private const long WS_EX_TRANSPARENT = 0x00000020L;
    private const uint LWA_ALPHA = 0x2;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);
    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
}
