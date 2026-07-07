using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Host service for the DtRH browser game (feat/dtrh-web-port). Owns the fullscreen
/// input-receiving WebView2 window (via <see cref="ChaosWebViewHost"/>), the virtual-host
/// mappings (ccp.game -> Resources/web, ccp.assets -> the user's active preset), and the
/// bridge protocol (v1). M1 scope: boot The Fall engine on the active preset's media.
/// Payload firing / meta persistence / XP payout land in M2 (DtrhBridgeRouter/DtrhMetaBridge).
///
/// Deliberately NOT an evolution of ChaosTunnelService - opposite window semantics (the
/// tunnel is a passive backdrop under the WPF game; this IS the game surface). The two
/// coexist until the legacy WPF game retires.
/// </summary>
internal static class DtrhHostService
{
    private const int Protocol = 1;
    private static ChaosWebViewHost? _host;
    private static DispatcherTimer? _exitWatchdog;
    private static bool _exiting;

    public static bool IsActive => _host != null;

    /// <summary>Launch the game window (idempotent). The page boots into The Fall on the
    /// active preset; the Warren hub becomes the boot target in M5.</summary>
    public static void Launch()
    {
        if (_host != null) { _host.FocusWeb(); return; }
        try
        {
            _exiting = false;
            var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
            _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
            {
                StartUrl = "https://ccp.game/dtrh/index.html",
                PrimaryHost = "ccp.game",
                Mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
                {
                    ("ccp.game", webRoot, CoreWebView2HostResourceAccessKind.Deny),
                    // Allow (not DenyCors): the engine uploads this media to WebGL, which
                    // needs CORS-clean responses (verified in the M0 spike).
                    ("ccp.assets", App.EffectiveAssetsPath, CoreWebView2HostResourceAccessKind.Allow),
                },
                UserDataFolderName = "browser_data_dtrh",
                InputEnabled = true,
                LogTag = "DtrhHost",
                // The game's audio bed / drift voice must start without a click.
                ExtraBrowserArguments = "--autoplay-policy=no-user-gesture-required",
                OnReady = OnPageReady,
                OnMessage = OnPageMessage,
                OnProcessFailed = OnProcessFailed,
            });
            _host.Show();
            App.Logger?.Information("DtrhHostService: launched");
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "DtrhHostService.Launch failed");
            CloseActive();
        }
    }

    /// <summary>Graceful close: ask the page to wind down, watchdog-force after 1200ms.
    /// Also the panic-key path. Idempotent.</summary>
    public static void CloseActive()
    {
        try
        {
            if (_host == null) return;
            if (_host.IsReady && !_exiting)
            {
                _exiting = true;
                _host.Post(new { type = "end-run", reason = "host" });
                CancelExitWatchdog();
                _exitWatchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                _exitWatchdog.Tick += (_, _) => DisposeAll();
                _exitWatchdog.Start();
            }
            else
            {
                DisposeAll();
            }
        }
        catch (Exception ex) { App.Logger?.Debug("DtrhHostService.CloseActive: {E}", ex.Message); DisposeAll(); }
    }

    private static void OnPageReady()
    {
        try
        {
            _host?.Post(new
            {
                type = "init",
                protocol = Protocol,
                settings = new
                {
                    masterVolume = SafeMasterVolume(),
                },
            });
            var m = DtrhAssetManifest.Build();
            _host?.Post(new
            {
                type = "manifest",
                images = m.Images.Select(e => new { name = e.Name, url = e.Url }),
                videos = m.Videos.Select(e => new { name = e.Name, url = e.Url }),
                skipped = m.Skipped,
                truncated = m.Truncated,
            });
        }
        catch (Exception ex) { App.Logger?.Warning("DtrhHostService.OnPageReady: {E}", ex.Message); }
    }

    private static void OnPageMessage(JObject o)
    {
        switch ((string?)o["type"])
        {
            case "sfx":
                var name = (string?)o["name"];
                var scale = (float?)o["scale"] ?? 0.6f;
                if (!string.IsNullOrEmpty(name)) ChaosSfx.Play(name, scale);
                break;
            case "run-started":
                App.Logger?.Information("DtrhHost: page run started ({Mode})", (string?)o["mode"]);
                break;
            case "boot-error":
                App.Logger?.Warning("DtrhHost: page boot-error: {Msg} - closing", (string?)o["msg"]);
                CloseActive();
                break;
            case "exit":       // page-initiated (Esc held): it winds itself down, then exit-done
                _exiting = true;
                CancelExitWatchdog();
                _exitWatchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                _exitWatchdog.Tick += (_, _) => DisposeAll();
                _exitWatchdog.Start();
                break;
            case "exit-done":
                DisposeAll();
                break;
            case "pong":
                break; // watchdog plumbing arrives in M2
        }
    }

    private static void OnProcessFailed(CoreWebView2ProcessFailedKind kind)
    {
        // M1: no mid-run state to lose - tear down cleanly. The reload-and-resume
        // recovery ladder lands with the watchdog work in M2.
        App.Logger?.Warning("DtrhHost: WebView2 process failed ({Kind}) - closing", kind);
        DisposeAll();
    }

    private static void CancelExitWatchdog()
    {
        try { _exitWatchdog?.Stop(); } catch { }
        _exitWatchdog = null;
    }

    private static void DisposeAll()
    {
        CancelExitWatchdog();
        try { _host?.Dispose(); } catch { }
        _host = null;
        _exiting = false;
        App.Logger?.Information("DtrhHostService: closed");
    }

    private static int SafeMasterVolume()
    {
        try { return App.Settings?.Current?.MasterVolume ?? 100; }
        catch { return 100; }
    }
}
