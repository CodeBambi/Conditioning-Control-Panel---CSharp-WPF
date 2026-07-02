using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Media;
using global::Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Chaos;
using ConditioningControlPanel.Avalonia.Desktop.Windows.Platform;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Settings;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows.Services.Chaos;

/// <summary>
/// Avalonia port of the WPF ChaosTunnelService. Hosts the bundled three.js "rabbit hole" tunnel page in
/// a dedicated opaque overlay window via a dedicated <see cref="WebView2BrowserHost"/> (so it does not
/// disturb the dashboard browser) and drives it with JSON messages over the IBrowserHost seam
/// (<see cref="IBrowserHost.PostWebMessageAsJson"/> / <see cref="IBrowserHost.WebMessageReceived"/>).
///
/// The window sits at the BOTTOM of the z-order (the non-topmost game windows stack above it) and
/// plays a faint looping ambient bed through a dedicated LibVLC player. Anti-MPO CoreWebView2 flags
/// keep the WebGL swapchain composited through DWM so it stays below the topmost overlays.
/// </summary>
public sealed class ChaosTunnelService : IChaosTunnelService, IDisposable
{
    private const string VirtualHost = "ccp.tunnel";
    private const string StartUrl = "https://" + VirtualHost + "/tunnel/index.html";
    private const string AntiMpoArgs =
        "--disable-direct-composition-video-overlays --disable-features=CalculateNativeWinOcclusion";

    private readonly ISettingsService _settings;
    private readonly ILibVlcProvider _vlc;
    private readonly ILogger<ChaosTunnelService>? _logger;

    private WebView2BrowserHost? _host;
    private Window? _window;
    private MediaPlayer? _ambient;
    private DispatcherTimer? _zGuard;
    private DispatcherTimer? _exitWatchdog;
    private readonly List<string> _pending = new();

    private bool _ready;
    private bool _runActive;
    private bool _disposed;

    public ChaosTunnelService(ISettingsService settings, ILibVlcProvider vlc, ILogger<ChaosTunnelService>? logger = null)
    {
        _settings = settings;
        _vlc = vlc;
        _logger = logger;
    }

    private bool Enabled => _settings.Current?.ChaosTunnelEnabled == true;
    private int LastStreak = -1;

    public void Preload()
    {
        if (!Enabled) return;
        try
        {
            CancelExitWatchdog();
            _runActive = true;
            if (_window == null) Build();
        }
        catch (Exception ex) { _logger?.LogWarning("ChaosTunnelService.Preload failed: {E}", ex.Message); }
    }

    public void Show()
    {
        if (!Enabled) { CloseActive(); return; }
        try
        {
            if (_window == null) Build();
            _runActive = true;
            LastStreak = -1;            // fresh run: the first SetStreak(0) must go through
            CancelExitWatchdog();
            if (_ready) StartAmbient(); // preloaded page: 'ready' already fired with no run active
            StartZGuard();
            PostToPage(new { type = "run-start" }); // queued until 'ready'; the page fades in on receipt
        }
        catch (Exception ex) { _logger?.LogWarning("ChaosTunnelService.Show failed: {E}", ex.Message); }
    }

    public void SendZoneHint(int depth, double intensity)
    {
        if (_window == null) return;
        PostToPage(new { type = "zone-hint", depth, intensity });
    }

    public void SetIntensity(double value)
    {
        if (_window == null) return;
        PostToPage(new { type = "intensity", value });
    }

    public void SetStreak(int combo, double mult)
    {
        if (_window == null) return;
        if (combo == LastStreak) return;
        LastStreak = combo;
        PostToPage(new { type = "streak", combo, mult });
    }

    public void SetVideoPlaying(bool on)
    {
        if (_window == null) return;
        PostToPage(new { type = "video-playing", on });
        try { if (_ambient != null) _ambient.Mute = on; } catch { }
    }

    public void SpawnPowerup(string? id = null, double ahead = 90)
    {
        if (_window == null) return;
        PostToPage(new { type = "spawn-powerup", id, ahead });
    }

    public void CloseActive()
    {
        try
        {
            _runActive = false;
            StopAmbient();
            StopZGuard();
            if (_window == null) { DisposeAll(); return; }
            if (_ready)
            {
                PostToPage(new { type = "run-end" });
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
        catch (Exception ex) { _logger?.LogDebug("ChaosTunnelService.CloseActive: {E}", ex.Message); DisposeAll(); }
    }

    private void Build()
    {
        _ready = false;
        _pending.Clear();

        _host = new WebView2BrowserHost { AdditionalBrowserArguments = AntiMpoArgs };
        _host.WebMessageReceived += OnWebMessageReceived;
        _host.SetVirtualHostToFolder(VirtualHost, Path.Combine(AppContext.BaseDirectory, "Resources", "web"));

        _window = new Window
        {
            // Borderless via the stable client-area extension (Window.SystemDecorations was renamed/obsoleted in Avalonia v12).
            ExtendClientAreaToDecorationsHint = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = false,                 // sits under every topmost bubble/overlay/video window
            Focusable = false,
            Background = Brushes.Black,
            Width = 1920,
            Height = 1080,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint(0, 0),
            Content = _host.CreateBrowserControl(),
        };
        _window.Opened += (_, _) =>
        {
            // Span the primary screen (DIPs). The tunnel is a full-bleed background.
            try
            {
                var scr = _window.Screens?.Primary;
                if (scr != null)
                {
                    _window.Width = scr.Bounds.Width / scr.Scaling;
                    _window.Height = scr.Bounds.Height / scr.Scaling;
                }
            }
            catch { }
            SinkToBottom();
        };
        _window.Show();
        SinkToBottom();
        _ = _host.NavigateAsync(new Uri(StartUrl));
        _logger?.LogInformation("ChaosTunnelService window up (non-topmost, opaque, WebView2 tunnel)");
    }

    private void OnWebMessageReceived(object? sender, string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => HandleMessage(json));
            return;
        }
        HandleMessage(json);
    }

    private void HandleMessage(string json)
    {
        try
        {
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
                    if (!string.IsNullOrEmpty(name)) try { AvaloniaChaosSfx.Play(name, scale); } catch { }
                    break;
                case "powerup-click":
                    _logger?.LogInformation("ChaosTunnel powerup-click: {Id}", (string?)o["id"]);
                    break;
                case "exit-done":
                    // A RunAgain inside the exit window re-arms _runActive — don't kill the window.
                    if (_runActive) { CancelExitWatchdog(); break; }
                    CancelExitWatchdog();
                    DisposeAll();
                    break;
                case "log":
                    _logger?.LogDebug("ChaosTunnel[page]: {Msg}", (string?)o["msg"]);
                    break;
            }
        }
        catch (Exception ex) { _logger?.LogDebug("ChaosTunnel.HandleMessage: {E}", ex.Message); }
    }

    private void PostToPage(object msg)
    {
        try
        {
            var json = JsonConvert.SerializeObject(msg);
            if (_ready && _host != null)
                _host.PostWebMessageAsJson(json);
            else
                _pending.Add(json);
        }
        catch (Exception ex) { _logger?.LogDebug("ChaosTunnel.PostToPage: {E}", ex.Message); }
    }

    private void FlushPending()
    {
        if (_host == null) return;
        foreach (var json in _pending)
        {
            try { _host.PostWebMessageAsJson(json); } catch { }
        }
        _pending.Clear();
    }

    private void CancelExitWatchdog()
    {
        try { _exitWatchdog?.Stop(); } catch { }
        _exitWatchdog = null;
    }

    private void StartAmbient()
    {
        try
        {
            if (_ambient != null) return;
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", "chaos", "tunnel_ambient.mp3");
            if (!File.Exists(path)) return; // silent until the asset ships
            var lib = _vlc.Value;
            _ambient = new MediaPlayer(lib) { Volume = (int)(AmbientVolume() * 100), Mute = false };
            var media = new Media(lib, path);
            media.AddOption("--input-repeat=-1"); // loop forever
            _ambient.Play(media);
        }
        catch (Exception ex) { _logger?.LogDebug("ChaosTunnel.StartAmbient: {E}", ex.Message); }
    }

    private void StopAmbient()
    {
        try { _ambient?.Stop(); _ambient?.Dispose(); } catch { }
        _ambient = null;
    }

    private double AmbientVolume()
    {
        try
        {
            double master = (_settings.Current?.MasterVolume ?? 80) / 100.0;
            return Math.Clamp(master * 0.26, 0.0, 1.0); // a faint bed under the game, never a wall of sound
        }
        catch { return 0.2; }
    }

    // --- z-order: keep the tunnel at the BOTTOM so the non-topmost game windows stack above it ---

    private void StartZGuard()
    {
        if (_zGuard == null)
        {
            _zGuard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _zGuard.Tick += (_, _) => SinkToBottom();
        }
        _zGuard.Start();
    }

    private void StopZGuard()
    {
        try { _zGuard?.Stop(); } catch { }
    }

    private void SinkToBottom()
    {
        try
        {
            var h = _window?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (h == IntPtr.Zero) return;
            SetWindowPos(h, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch { }
    }

    private void DisposeAll()
    {
        CancelExitWatchdog();
        StopZGuard();
        StopAmbient();
        try { _host?.Dispose(); } catch { }
        try { _window?.Close(); } catch { }
        _host = null;
        _window = null;
        _ready = false;
        _pending.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeAll();
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint flags);

    private static readonly IntPtr HWND_BOTTOM = new(1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
}
