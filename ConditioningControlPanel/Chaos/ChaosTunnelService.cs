using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using NAudio.Wave;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel;

/// <summary>
/// The endless three.js "rabbit hole" tunnel rendered UNDER the whole Chaos game.
///
/// Like <see cref="ChaosBackdropService"/> this is a single <b>non-topmost</b> fullscreen
/// window: every bubble/FX/video/HUD window is Topmost, so this sits deterministically below
/// all of them with zero z-order bookkeeping (and it is deliberately NOT added to
/// <c>RaiseGameLayerAboveVideo</c>). It hosts a WebView2 that loads a local three.js page
/// (Resources/web/tunnel) via a virtual https origin so ES modules + import maps resolve.
///
/// The window is OPAQUE (not a layered/transparent WPF window) because a WebView2 child HWND
/// does not paint reliably inside <c>AllowsTransparency=true</c>; the page fills the screen and
/// does all fade-in/out in-page over a black clear. It is NOT click-through (no WS_EX_TRANSPARENT)
/// so pointer clicks reach the page for power-up raycasting; WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW keep
/// it from stealing focus or showing in Alt-Tab.
///
/// Gated on <c>ChaosTunnelEnabled</c> only (NO Story gate) — default OFF. One-shot SFX route back
/// through <see cref="Services.Chaos.ChaosSfx"/> (master-volume + preferred-device aware); the
/// looping ambient bed is a small NAudio player owned here.
/// </summary>
internal static class ChaosTunnelService
{
    private const string VirtualHost = "ccp.tunnel";
    // The virtual host maps Resources/web, so the page is at /tunnel/index.html and its
    // "../vendor/three" import map resolves to /vendor/three under the same root.
    private const string StartUrl = "https://ccp.tunnel/tunnel/index.html";

    private static Window? _window;
    private static WebView2? _web;
    private static bool _ready;
    private static bool _runActive;
    private static bool _initStarted;
    private static readonly List<string> _pending = new();   // JSON queued until the page says 'ready'
    private static DispatcherTimer? _exitWatchdog;

    // ---- ambient bed (looping NAudio) ----
    private static WaveOutEvent? _ambOut;
    private static AudioFileReader? _ambReader;
    private static bool _ambPaused;

    private static bool Enabled => App.Settings?.Current?.ChaosTunnelEnabled == true;

    private static Dispatcher Ui => Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    /// <summary>Build the window + WebView and start loading the page WITHOUT starting a run.
    /// Called when the countdown starts so the ~2s WebView2/three.js warm-up happens under it
    /// instead of showing as a black screen after GO. The page idles behind its black curtain
    /// until <see cref="Show"/> posts run-start.</summary>
    public static void Preload()
    {
        if (!Enabled) return;
        try
        {
            // RunAgain lands here while the previous run's exit anim/watchdog is still going —
            // claim the warm window so neither the watchdog nor the in-flight exit-done kills it.
            CancelExitWatchdog();
            _runActive = true;
            if (_window == null) Build();
        }
        catch (Exception ex) { App.Logger?.Warning("ChaosTunnelService.Preload failed: {E}", ex.Message); }
    }

    /// <summary>Spawn the tunnel and kick off the falling intro. No-op (and closes any stray
    /// window) when the feature is off.</summary>
    public static void Show()
    {
        if (!Enabled) { CloseActive(); return; }
        try
        {
            if (_window == null) Build();
            _runActive = true;
            _lastStreak = -1;   // fresh run: the first SetStreak(0) must go through
            CancelExitWatchdog();
            if (_ready) StartAmbient();   // preloaded page: 'ready' already fired with no run active
            StartZGuard();
            // Queued until 'ready'; the page fades in from its curtain on receipt.
            PostToPage(new { type = "run-start" });
        }
        catch (Exception ex) { App.Logger?.Warning("ChaosTunnelService.Show failed: {E}", ex.Message); }
    }

    /// <summary>Nudge the zone scheduler bolder/faster as the run goes deeper.</summary>
    public static void SendZoneHint(int depth, double intensity)
    {
        if (_window == null) return;
        PostToPage(new { type = "zone-hint", depth, intensity });
    }

    public static void SetIntensity(double value)
    {
        if (_window == null) return;
        PostToPage(new { type = "intensity", value });
    }

    private static int _lastStreak = -1;

    /// <summary>Fall speed is tied to the pop streak: the page starts the descent near-stalled,
    /// accelerates as the combo climbs, and brakes hard when the streak halves/breaks.</summary>
    public static void SetStreak(int combo, double mult)
    {
        if (_window == null) return;
        if (combo == _lastStreak) return;
        _lastStreak = combo;
        PostToPage(new { type = "streak", combo, mult });
    }

    /// <summary>A fullscreen video covers the tunnel — tell the page to pause its render loop
    /// (0% tunnel GPU) and hush the ambient bed until the video ends.</summary>
    public static void SetVideoPlaying(bool on)
    {
        if (_window == null) return;
        PostToPage(new { type = "video-playing", on });
        PauseAmbient(on);
    }

    /// <summary>Spawn a clickable power-up (verification / future gameplay hook).</summary>
    public static void SpawnPowerup(string? id = null, double ahead = 90)
    {
        if (_window == null) return;
        PostToPage(new { type = "spawn-powerup", id, ahead });
    }

    /// <summary>Play the exit animation then tear the window down (idempotent).</summary>
    public static void CloseActive()
    {
        try
        {
            _runActive = false;
            StopAmbient();
            if (_window == null) { DisposeAll(); return; }
            if (_ready)
            {
                PostToPage(new { type = "run-end" });
                // Close on the page's 'exit-done', or force it after a short watchdog.
                CancelExitWatchdog();
                _exitWatchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                _exitWatchdog.Tick += (_, _) => { CancelExitWatchdog(); DisposeAll(); };
                _exitWatchdog.Start();
            }
            else
            {
                DisposeAll();
            }
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosTunnelService.CloseActive: {E}", ex.Message); DisposeAll(); }
    }

    // ============================ window + webview ============================

    private static void Build()
    {
        _ready = false;
        _initStarted = false;
        _pending.Clear();

        _web = new WebView2
        {
            DefaultBackgroundColor = System.Drawing.Color.Black,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        var grid = new Grid { Background = Brushes.Black };
        grid.Children.Add(_web);

        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = false,      // WebView2 does not paint in a layered window; stay opaque
            Background = Brushes.Black,
            Topmost = false,                 // sits under every topmost bubble/overlay/video window
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            Width = SystemParameters.PrimaryScreenWidth,
            Height = SystemParameters.PrimaryScreenHeight,
            Content = grid,
        };
        _window.SourceInitialized += (_, _) => ApplyExStyles(_window);
        _window.Closed += (_, _) => { /* teardown handled in DisposeAll */ };
        _window.Show();
        // Sink to the BOTTOM of the z-order: the game's windows can run NON-topmost (pin-on-top
        // off / Free Desktop), and this window is created after the HUD/sidebar — freshly shown
        // it would land above them and swallow the whole game UI. At the bottom, everything sits
        // over it; MainWindow is the one thing that must stay under it, and the z-guard owns that.
        SinkToBottom();

        _ = InitWebAsync();
        App.Logger?.Information("ChaosTunnelService window up (non-topmost, opaque, WebView2 tunnel)");
    }

    private static async Task InitWebAsync()
    {
        if (_initStarted || _web == null) return;
        _initStarted = true;
        try
        {
            var userDataFolder = Path.Combine(App.UserDataPath, "browser_data_chaos_tunnel");
            Directory.CreateDirectory(userDataFolder);

            // --disable-direct-composition-video-overlays: keep the WebGL swapchain composited
            // through DWM so it stays BELOW the topmost overlays (the app's established anti-MPO flag).
            // CalculateNativeWinOcclusion: the chaos game stacks layered windows over the tunnel;
            // Chromium's occlusion tracker can decide the page is covered and throttle rAF, which
            // shows as skipped frames — turn it off.
            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments =
                    "--disable-direct-composition-video-overlays --disable-features=CalculateNativeWinOcclusion"
            };
            var env = await CoreWebView2Environment
                .CreateAsync(browserExecutableFolder: null, userDataFolder: userDataFolder, options: options)
                .ConfigureAwait(true);
            await _web.EnsureCoreWebView2Async(env).ConfigureAwait(true);
            if (_web.CoreWebView2 == null) { App.Logger?.Warning("ChaosTunnelService: WebView2 core null"); return; }

            var core = _web.CoreWebView2;
            var settings = core.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsBuiltInErrorPageEnabled = false;
            settings.IsWebMessageEnabled = true;

            // Serve the bundled page from a virtual https origin so ESM + import maps resolve.
            var folder = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
            core.SetVirtualHostNameToFolderMapping(VirtualHost, folder, CoreWebView2HostResourceAccessKind.Deny);

            core.NavigationStarting += OnNavigationStarting;
            core.WebMessageReceived += OnWebMessageReceived;

            core.Navigate(StartUrl);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ChaosTunnelService.InitWebAsync failed: {E}", ex.Message);
        }
    }

    private static void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // Only ever our local page. Block anything else (defence in depth).
        if (string.IsNullOrEmpty(e.Uri) ||
            !e.Uri.StartsWith("https://" + VirtualHost + "/", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
        }
    }

    private static void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            if (string.IsNullOrEmpty(json)) return;
            var o = JObject.Parse(json);
            var type = (string?)o["type"];
            switch (type)
            {
                case "ready":
                    _ready = true;
                    FlushPending();
                    if (_runActive) StartAmbient();
                    break;
                case "sfx":
                    var name = (string?)o["name"];
                    var scale = (float?)o["scale"] ?? 0.6f;
                    if (!string.IsNullOrEmpty(name)) Services.Chaos.ChaosSfx.Play(name, scale);
                    break;
                case "powerup-click":
                    App.Logger?.Information("ChaosTunnel powerup-click: {Id}", (string?)o["id"]);
                    break;
                case "exit-done":
                    // A RunAgain inside the exit window re-arms _runActive — don't kill the
                    // window the new run is about to fade back in.
                    if (_runActive) { CancelExitWatchdog(); break; }
                    CancelExitWatchdog();
                    DisposeAll();
                    break;
                case "log":
                    App.Logger?.Debug("ChaosTunnel[page]: {Msg}", (string?)o["msg"]);
                    break;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosTunnel.OnWebMessageReceived: {E}", ex.Message); }
    }

    private static void PostToPage(object msg)
    {
        try
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(msg);
            if (_ready && _web?.CoreWebView2 != null)
                _web.CoreWebView2.PostWebMessageAsJson(json);
            else
                _pending.Add(json);
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosTunnel.PostToPage: {E}", ex.Message); }
    }

    private static void FlushPending()
    {
        if (_web?.CoreWebView2 == null) return;
        foreach (var json in _pending)
        {
            try { _web.CoreWebView2.PostWebMessageAsJson(json); } catch { }
        }
        _pending.Clear();
    }

    private static void CancelExitWatchdog()
    {
        try { _exitWatchdog?.Stop(); } catch { }
        _exitWatchdog = null;
    }

    private static void DisposeAll()
    {
        CancelExitWatchdog();
        StopZGuard();
        StopAmbient();
        try { if (_web?.CoreWebView2 != null) { _web.CoreWebView2.NavigationStarting -= OnNavigationStarting; _web.CoreWebView2.WebMessageReceived -= OnWebMessageReceived; } } catch { }
        try { _web?.Dispose(); } catch { }
        try { _window?.Close(); } catch { }
        _web = null; _window = null; _ready = false; _initStarted = false; _pending.Clear();
    }

    // ============================ ambient bed ============================

    private static void StartAmbient()
    {
        try
        {
            if (_ambOut != null) return;
            var path = Services.Chaos.ChaosSfx.ResolvePath("tunnel_ambient");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;   // silent until the asset ships
            _ambReader = new AudioFileReader(path) { Volume = AmbientVolume() };
            _ambOut = new WaveOutEvent();
            App.Audio?.ApplyPreferredDevice(_ambOut);
            _ambOut.Init(new LoopStream(_ambReader));
            _ambOut.Play();
            _ambPaused = false;
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosTunnel.StartAmbient: {E}", ex.Message); }
    }

    private static void PauseAmbient(bool pause)
    {
        try
        {
            if (_ambOut == null) return;
            if (pause && !_ambPaused) { _ambOut.Pause(); _ambPaused = true; }
            else if (!pause && _ambPaused) { _ambOut.Play(); _ambPaused = false; }
        }
        catch { }
    }

    private static void StopAmbient()
    {
        try { _ambOut?.Stop(); } catch { }
        try { _ambOut?.Dispose(); } catch { }
        try { _ambReader?.Dispose(); } catch { }
        _ambOut = null; _ambReader = null; _ambPaused = false;
    }

    private static float AmbientVolume()
    {
        try
        {
            float master = App.Settings.Current.MasterVolume / 100f;
            return Math.Clamp(master * 0.26f, 0f, 1f);   // a faint bed under the game, never a wall of sound
        }
        catch { return 0.2f; }
    }

    /// <summary>Loops a WaveStream forever (NAudio has no built-in looper).</summary>
    private sealed class LoopStream : WaveStream
    {
        private readonly WaveStream _src;
        public LoopStream(WaveStream src) { _src = src; }
        public override WaveFormat WaveFormat => _src.WaveFormat;
        public override long Length => _src.Length;
        public override long Position { get => _src.Position; set => _src.Position = value; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = _src.Read(buffer, offset + total, count - total);
                if (n == 0)
                {
                    if (_src.Position == 0) break;   // empty source — avoid a busy loop
                    _src.Position = 0;
                }
                total += n;
            }
            return total;
        }
    }

    // ============================ z-guard ============================
    // The tunnel is non-topmost by design (the topmost game windows stack above it), but
    // MainWindow is ALSO non-topmost — an attached avatar commenting on an effect re-activates
    // it and it rises over the tunnel. While a run is live, demote MainWindow back below the
    // tunnel when that happens. Targeted (touches only MainWindow) so a user-disabled
    // ChaosPinOnTop stack is never reshuffled.

    private static DispatcherTimer? _zGuard;
    private static HwndSource? _mainHook;

    private static void SinkToBottom()
    {
        try
        {
            var h = _window != null ? new WindowInteropHelper(_window).Handle : IntPtr.Zero;
            if (h == IntPtr.Zero) return;
            SetWindowPos(h, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch { }
    }

    private static void StartZGuard()
    {
        if (_zGuard == null)
        {
            _zGuard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _zGuard.Tick += (_, _) => EnforceBelowTunnel();
            _zGuard.Start();
            EnforceBelowTunnel();   // don't wait a tick — tuck MainWindow under right now
        }
        // Preventive hook: rewrite MainWindow raise-to-top REQUESTS in flight (no visible flash),
        // whatever their source — avatar pair-raise, activation, level-up/achievement chains.
        if (_mainHook == null)
        {
            try
            {
                var main = Application.Current?.MainWindow;
                var h = main != null ? new WindowInteropHelper(main).Handle : IntPtr.Zero;
                if (h != IntPtr.Zero)
                {
                    _mainHook = HwndSource.FromHwnd(h);
                    _mainHook?.AddHook(MainWndProc);
                }
            }
            catch (Exception ex) { App.Logger?.Debug("ChaosTunnel.StartZGuard hook: {E}", ex.Message); }
        }
    }

    private static void StopZGuard()
    {
        try { _zGuard?.Stop(); } catch { }
        _zGuard = null;
        try { _mainHook?.RemoveHook(MainWndProc); } catch { }
        _mainHook = null;
    }

    /// <summary>While the run is live, a MainWindow z-order raise to HWND_TOP is rewritten to land
    /// just below the tunnel — the raise never paints, so there is no flash to correct. Topmost
    /// raises (panic key's visibility pulse) are deliberately left alone.</summary>
    private static IntPtr MainWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_WINDOWPOSCHANGING = 0x0046;
        if (msg != WM_WINDOWPOSCHANGING || !_runActive || _window == null) return IntPtr.Zero;
        try
        {
            var tunH = new WindowInteropHelper(_window).Handle;
            if (tunH == IntPtr.Zero || !IsWindow(tunH)) return IntPtr.Zero;
            var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((wp.flags & SWP_NOZORDER) != 0) return IntPtr.Zero;
            if (wp.hwndInsertAfter == IntPtr.Zero)   // HWND_TOP
            {
                wp.hwndInsertAfter = tunH;
                Marshal.StructureToPtr(wp, lParam, false);
            }
        }
        catch { }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    private static void EnforceBelowTunnel()
    {
        try
        {
            if (!_runActive || _window == null) return;
            var main = Application.Current?.MainWindow;
            if (main == null || main == _window) return;
            var mainH = new WindowInteropHelper(main).Handle;
            var tunH = new WindowInteropHelper(_window).Handle;
            if (mainH == IntPtr.Zero || tunH == IntPtr.Zero) return;
            if (!IsWindowVisible(mainH) || IsIconic(mainH)) return;

            // Walk upward from the tunnel; if MainWindow sits anywhere above it, slot it back below.
            var h = tunH;
            for (int i = 0; i < 512; i++)
            {
                h = GetWindow(h, GW_HWNDPREV);
                if (h == IntPtr.Zero) return;
                if (h == mainH)
                {
                    App.Logger?.Debug("ChaosTunnel z-guard: MainWindow was above the tunnel — demoting");
                    SetWindowPos(mainH, tunH, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
                    return;
                }
            }
        }
        catch { }
    }

    private static readonly IntPtr HWND_BOTTOM = new(1);
    private const uint GW_HWNDPREV = 3;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);

    // Absorb clicks (NO WS_EX_TRANSPARENT) but never steal focus / show in Alt-Tab.
    private static void ApplyExStyles(Window w)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
