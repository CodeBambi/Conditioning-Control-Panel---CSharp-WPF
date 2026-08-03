using System;
using System.Collections.Generic;
using System.IO;
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
                },
                stats = LoadStats(),
            });
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
        var s = App.Settings?.Current;
        if (s == null || key == null || value == null) return;
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
}
