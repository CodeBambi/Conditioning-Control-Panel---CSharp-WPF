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
/// Hosts THE CAUCUS RACE - the no-lose kart run that lives as a sibling page next to the
/// DtRH game (<c>Resources/web/dtrh/race.html</c>). A stripped-down sibling of
/// <see cref="DtrhHostService"/>, the way <see cref="LoomHostService"/> is: one windowed,
/// input-receiving <see cref="ChaosWebViewHost"/> with the SAME virtual-host mappings the
/// descent registers (the race drives through the user's own media, so it needs ccp.assets,
/// ccp.art and the pack/mod hosts exactly as the descent does), speaking the race bridge
/// subset (Protocol v1, see dtrh/race/CONTRACT.md "Host protocol"):
///   page -> host: ready, heartbeat/pong, sfx, fire-payload, run-started, run-ended,
///                 boot-error, report-bug, fullscreen-set, exit, exit-done
///   host -> page: init, manifest, favorites, payout-result, pause, ping, exit-request
///
/// Nothing here is duplicated from the descent host: the manifest, the mod merge, the native
/// payloads and the Spark banking all go through the shared services. The one thing that is
/// deliberately different is the XP formula (see <see cref="OnRunEnded"/>): race scores run
/// about 5x a descent's because of the multiplier ladder, so the score is divided down before
/// it meets the same per-minute cap. No test mode, no legacy fallback: a boot-error simply
/// closes the window - there is no classic race to degrade to.
/// </summary>
internal static class CaucusHostService
{
    private const int Protocol = 1;
    private static ChaosWebViewHost? _host;
    private static DtrhMetaBridge? _meta;
    private static DispatcherTimer? _exitWatchdog;
    private static DispatcherTimer? _heartbeatWatch;
    private static DateTime _lastHeartbeatUtc;
    private static bool _pinged;
    private static bool _runActive;
    private static bool _exiting;
    private static bool _disposing;
    private static bool _videoHooked;

    /// <summary>True while the race window is open.</summary>
    public static bool IsActive => _host != null;

    /// <summary>Open the race window (idempotent - refocuses if already open).</summary>
    public static void Launch()
    {
        if (_host != null) { _host.FocusWeb(); return; }
        try
        {
            // EMI Desk: the ring learns from every open, not just its own cards.
            try { App.EmiDesk?.NoteOpen("race"); } catch { }

            // The race shares the descent's audio (bubble pops, stingers), which ships as the
            // lazy audio-web pack. Fire-and-forget: no-op once installed, or offline.
            try { _ = App.ReleaseContent?.RequestPackAsync(ReleaseContentService.PackAudioWeb); }
            catch (Exception ex) { App.Logger?.Debug("RaceHost: audio-web request failed: {E}", ex.Message); }

            _exiting = false;
            _runActive = false;
            _pinged = false;
            // Real banking, never the cloned test state: the race pays Sparks into the same
            // chaos_meta.json the descent banks into.
            _meta = new DtrhMetaBridge(testMode: false, msg => _host?.Post(msg));

            var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
            // Same rule as the descent host: WebView2 SKIPS a mapping whose folder is missing,
            // so the spirals library has to exist before the host registers it.
            try { Directory.CreateDirectory(DtrhLoomStore.SpiralsFolder); }
            catch (Exception ex) { App.Logger?.Debug("RaceHost: spirals dir create failed: {E}", ex.Message); }
            var mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
            {
                ("ccp.game", webRoot, CoreWebView2HostResourceAccessKind.Deny),
                // Allow (not DenyCors): the media goes up to WebGL, which needs CORS-clean
                // responses - the descent's M0 spike proved it for this exact folder.
                ("ccp.assets", App.EffectiveAssetsPath, CoreWebView2HostResourceAccessKind.Allow),
                ("ccp.art", Path.Combine(AppContext.BaseDirectory, "assets", "Chaos"), CoreWebView2HostResourceAccessKind.Allow),
                ("ccp.spirals", DtrhLoomStore.SpiralsFolder, CoreWebView2HostResourceAccessKind.Allow),
                ChaosWebViewHost.ContentMapping(),
            };
            // Creator mods: the mod's dtrh subfolder only, exactly as the descent maps it, so the
            // race can mix the mod's descent media through the same manifest merge.
            var modDtrh = DtrhModContent.ModDtrhRoot();
            if (modDtrh != null)
                mappings.Add(("ccp.mod", modDtrh, CoreWebView2HostResourceAccessKind.Allow));

            _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
            {
                StartUrl = "https://ccp.game/dtrh/race.html",
                PrimaryHost = "ccp.game",
                Mappings = mappings,
                // Own browser profile: the descent's WebView2 state stays untouched.
                UserDataFolderName = "browser_data_race",
                InputEnabled = true,
                // A normal titled window at launch; the page's fullscreen-set toggles the
                // borderless mode through the host (never the browser Fullscreen API, which
                // would take Esc away from the page).
                StartFullscreen = false,
                // Glued above MainWindow like the descent, so a bark or a closing video window
                // raising main can never bury the race.
                OwnedByMainWindow = true,
                WindowTitle = "The Caucus Race",
                LogTag = "Race",
                // The engine hum and the pop bed must start without a click.
                ExtraBrowserArguments = "--autoplay-policy=no-user-gesture-required",
                OnReady = OnPageReady,
                OnMessage = OnPageMessage,
                OnProcessFailed = _ => DisposeAll(),
            });
            _host.Show();
            // Windowed: the user can close it with the title-bar X. Tear down so the heartbeat
            // watchdog cannot read the resulting silence as a wedged page.
            if (_host.Window != null) _host.Window.Closed += (_, _) => DisposeAll();
            HookVideoEvents(true);
            StartHeartbeatWatch();
            _host.FocusWeb();
            App.Logger?.Information("CaucusHostService: launched");
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "CaucusHostService.Launch failed");
            DisposeAll();
        }
    }

    /// <summary>Graceful close: ask the page to wind down (it answers exit / exit-done),
    /// watchdog-force after 1200ms. Idempotent.</summary>
    public static void CloseActive()
    {
        try
        {
            if (_host == null) return;
            if (_host.IsReady && !_exiting)
            {
                _exiting = true;
                _host.Post(new { type = "exit-request" });
                ArmExitWatchdog();
            }
            else
            {
                DisposeAll();
            }
        }
        catch (Exception ex) { App.Logger?.Debug("CaucusHostService.CloseActive: {E}", ex.Message); DisposeAll(); }
    }

    // ============================ boot ============================

    private static void OnPageReady()
    {
        try
        {
            _lastHeartbeatUtc = DateTime.UtcNow;
            _pinged = false;
            // Keyboard focus does not land in the WebView2 child on a fresh launch until a
            // click - claim it now so the steering keys work from the first frame.
            _host?.FocusWeb();
            _host?.Post(new
            {
                type = "init",
                protocol = Protocol,
                settings = new
                {
                    masterVolume = SafeMasterVolume(),
                    // The app's own motion setting, capped by the OS animation switch (MotionFx
                    // owns that resolution). Reduced and Off both read as reduced motion on the
                    // page: it has no third state to offer.
                    reducedMotion = SafeReducedMotion(),
                },
                modId = SafeActiveModId(),
                // Creator mods: the mod's own DTRH content as ccp.mod URLs; null = no mod
                // content, the page runs on its shipped assets.
                modContent = DtrhModContent.BuildInitPayload(),
            });
            var m = DtrhAssetManifest.Build();
            DtrhModContent.MergeMedia(m);   // creator mods: mix/replace media, same as the descent
            _host?.Post(new
            {
                type = "manifest",
                images = m.Images.Select(e => new { name = e.Name, url = e.Url }),
                videos = m.Videos.Select(e => new { name = e.Name, url = e.Url }),
                skipped = m.Skipped,
                truncated = m.Truncated,
            });
            // The descent's cumulative engagement ranking, so the race can bias toward the media
            // the user actually likes. Best-effort: an empty store simply posts nothing.
            try
            {
                var favorites = DtrhAssetStatsStore.TopAssets(12);
                if (favorites.Count > 0) _host?.Post(new { type = "favorites", names = favorites });
            }
            catch (Exception ex) { App.Logger?.Debug("RaceHost favorites post failed: {E}", ex.Message); }
        }
        catch (Exception ex) { App.Logger?.Warning("CaucusHostService.OnPageReady: {E}", ex.Message); }
    }

    // ============================ page messages ============================

    private static void OnPageMessage(JObject o)
    {
        switch ((string?)o["type"])
        {
            case "heartbeat":
            case "pong":
                _lastHeartbeatUtc = DateTime.UtcNow;
                _pinged = false;
                break;
            case "sfx":
            {
                var name = (string?)o["name"];
                var scale = (float?)o["scale"] ?? 0.6f;
                if (!string.IsNullOrEmpty(name)) ChaosSfx.Play(name, scale);
                break;
            }
            case "fire-payload":
                FirePayload(o);
                break;
            case "run-started":
                _runActive = true;
                App.Logger?.Information("RaceHost: run started (seed={Seed})", (string?)o["seed"]);
                break;
            case "run-ended":
                OnRunEnded(o);
                break;
            case "boot-error":
                OnBootError((string?)o["message"] ?? (string?)o["msg"]);
                break;
            case "report-bug":   // the page's in-game bug button (no chrome to hang one on in fullscreen)
                OpenBugReport();
                break;
            case "fullscreen-set":   // page's Esc ladder / dock button: C# owns the borderless toggle
                ApplyHostFullscreen((bool?)o["on"] ?? false);
                break;
            case "exit":       // page-initiated: it winds itself down, then exit-done
                _exiting = true;
                ArmExitWatchdog();
                break;
            case "exit-done":
                DisposeAll();
                break;
        }
    }

    /// <summary>fire-payload {kind, strength, durationMult} -> the REAL desktop effects through
    /// the shared factory. Video and audio only: every visual effect is in-world on the page.</summary>
    private static void FirePayload(JObject o)
    {
        try
        {
            var kindStr = (string?)o["kind"];
            if (string.IsNullOrWhiteSpace(kindStr)) return;

            EffectPayload payload;
            if (string.Equals(kindStr, "video", StringComparison.OrdinalIgnoreCase))
                payload = EffectPayloadFactory.Build(EffectBubblePayloadKind.Video);
            else if (string.Equals(kindStr, "audio", StringComparison.OrdinalIgnoreCase))
                payload = EffectPayloadFactory.Build(EffectBubblePayloadKind.Audio);
            else
            {
                App.Logger?.Warning("RaceHost: payload kind '{K}' is in-world - ignored", kindStr);
                return;
            }

            payload.Strength = Math.Clamp((int?)o["strength"] ?? 60, 0, 100);
            payload.DurationMult = Math.Clamp((double?)o["durationMult"] ?? 1.0, 0.1, 10.0);
            payload.Fire();
            App.Logger?.Information("RaceHost: fired native payload {K} (strength {S})", payload.DisplayName, payload.Strength);
        }
        catch (Exception ex) { App.Logger?.Warning("RaceHost.FirePayload: {E}", ex.Message); }
    }

    /// <summary>run-ended -> XP payout + Spark banking, answered with payout-result.
    /// The descent's formula with the score divided by 5 first: the race's multiplier ladder
    /// makes its scores run about 5x a descent's, and the per-minute cap (250 XP a minute) is
    /// the same ceiling both games share, so the divide keeps a race lap worth a descent lap.</summary>
    private static void OnRunEnded(JObject o)
    {
        _runActive = false;
        try
        {
            double score = Math.Max(0, (double?)o["score"] ?? 0);
            double durationSec = Math.Max(1, (double?)o["durationSec"] ?? 60);
            int bestCombo = (int?)o["bestCombo"] ?? 0;
            int popped = (int?)o["popped"] ?? 0;
            int effects = (int?)o["effects"] ?? 0;

            double durMin = durationSec / 60.0;
            double capBase = 250.0 * durMin;
            int baseXp = (int)Math.Min(score / 5.0, capBase);
            double skillMult = App.SkillTree?.GetTotalXpMultiplier() ?? 1.0;
            int finalXp = (int)Math.Round(baseXp * skillMult);

            long previousBest = ChaosMeta.State.BestScore;

            int sparksEarned = 0;
            if (_meta != null)
            {
                // Score is scaled the same way for the Spark formula (it is sqrt-shaped, so an
                // unscaled race score would out-bank every descent).
                sparksEarned = _meta.AwardRun(new ChaosMeta.ChaosRunRewardInput(
                    RunDurationSec: durationSec,
                    DifficultyMult: 1.0,
                    SparkGainMult: 1.0,
                    Score: score / 5.0,
                    TrickleDrops: 0,
                    DripFeedMaxed: false,
                    BestCombo: bestCombo,
                    Defused: effects,
                    ElapsedSec: durationSec));
            }

            try { App.Progression?.AddXP(baseXp, XPSource.Chaos); }
            catch (Exception ex) { App.Logger?.Debug("RaceHost payout AddXP: {E}", ex.Message); }
            // Popped bubbles feed the GLOBAL bubble count and its sparkle-point milestones,
            // the same sink the descent and the native chaos mode credit.
            try { if (popped > 0) App.Achievements?.TrackBubblesPopped(popped); }
            catch (Exception ex) { App.Logger?.Debug("RaceHost bubble credit: {E}", ex.Message); }

            _host?.Post(new
            {
                type = "payout-result",
                baseXp,
                skillMult,
                finalXp,
                sparksEarned,
                previousBest,
                dryRun = false,
            });
            App.Logger?.Information(
                "RaceHost: run complete: score {Score:0} over {Dur:0}s ({Laps} laps) -> base {Base} x skill {Mult:0.0} = {Final} XP, {Sparks} sparks",
                score, durationSec, (int?)o["laps"] ?? 0, baseXp, skillMult, finalXp, sparksEarned);
        }
        catch (Exception ex) { App.Logger?.Warning("RaceHost.OnRunEnded: {E}", ex.Message); }
    }

    // ============================ window plumbing ============================

    /// <summary>Page-driven fullscreen: borderless-toggle our own window and echo the resulting
    /// state back so the page's dock button + Esc ladder stay in sync.</summary>
    private static void ApplyHostFullscreen(bool on)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        disp.BeginInvoke(() =>
        {
            try
            {
                _host?.SetFullscreen(on);
                _host?.Post(new { type = "fullscreen", on = _host.IsFullscreen });
            }
            catch (Exception ex) { App.Logger?.Debug("RaceHost.fullscreen: {E}", ex.Message); }
        });
    }

    /// <summary>Same modal the rest of the app uses, owned by the race window so it sits on
    /// top even in fullscreen.</summary>
    private static void OpenBugReport()
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null) return;
        disp.BeginInvoke(() =>
        {
            try
            {
                var dlg = new BugReportWindow();
                var owner = _host?.Window;
                if (owner != null && owner.IsLoaded) dlg.Owner = owner;
                dlg.ShowDialog();
            }
            catch (Exception ex) { App.Logger?.Warning("RaceHost.report-bug: {E}", ex.Message); }
        });
    }

    /// <summary>The page's boot failed (WebGL refused, engine import threw). No classic race to
    /// fall back to, so just close cleanly - the log line is the diagnosis.</summary>
    private static void OnBootError(string? msg)
    {
        App.Logger?.Warning("RaceHost: page boot-error: {Msg}", msg);
        var disp = Application.Current?.Dispatcher;
        if (disp == null) { DisposeAll(); return; }
        disp.BeginInvoke(DisposeAll);
    }

    /// <summary>A mandatory video (fired by fire-payload) covers the page: tell it to pause so
    /// the kart is not driving blind under the video window, and resume when it closes.</summary>
    private static void HookVideoEvents(bool on)
    {
        try
        {
            if (App.Video == null) return;
            if (on && !_videoHooked)
            {
                App.Video.VideoStarted += OnVideoStarted;
                App.Video.VideoEnded += OnVideoEnded;
                _videoHooked = true;
            }
            else if (!on && _videoHooked)
            {
                App.Video.VideoStarted -= OnVideoStarted;
                App.Video.VideoEnded -= OnVideoEnded;
                _videoHooked = false;
            }
        }
        catch { }
    }

    private static void OnVideoStarted(object? sender, EventArgs e) => PostPause(true);

    private static void OnVideoEnded(object? sender, EventArgs e)
    {
        PostPause(false);
        var disp = Application.Current?.Dispatcher;
        if (disp == null || _host == null) return;
        disp.BeginInvoke(() => _host?.FocusWeb());   // the video window had Win32 focus; reclaim keyboard
    }

    private static void PostPause(bool on)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || _host == null) return;
        disp.BeginInvoke(() => _host?.Post(new { type = "pause", on }));
    }

    // ============================ watchdogs ============================

    /// <summary>Simplified descent watchdog: a page silent past the limit gets one ping, and if
    /// it stays silent through the next tick the window is closed (no relaunch ladder - a race
    /// is short, the user just clicks the button again).</summary>
    private static void StartHeartbeatWatch()
    {
        StopHeartbeatWatch();
        _lastHeartbeatUtc = DateTime.UtcNow;
        _heartbeatWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _heartbeatWatch.Tick += (_, _) =>
        {
            // Guarded on IsReady: the page only starts beating after boot, so a still-loading
            // page cannot false-trip.
            if (_host == null || !_host.IsReady || _exiting) return;
            double silent = (DateTime.UtcNow - _lastHeartbeatUtc).TotalSeconds;
            double limit = _runActive ? 10 : 20;
            if (silent <= limit) return;
            if (!_pinged)
            {
                _pinged = true;
                try { _host.Post(new { type = "ping" }); } catch { }
                return;
            }
            App.Logger?.Warning("RaceHost: page heartbeat silent >{Limit}s and no pong - closing", limit);
            DisposeAll();
        };
        _heartbeatWatch.Start();
    }

    private static void StopHeartbeatWatch()
    {
        try { _heartbeatWatch?.Stop(); } catch { }
        _heartbeatWatch = null;
    }

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

    // ============================ teardown ============================

    /// <summary>The one funnel every exit reaches: graceful close, watchdogs, the window's own
    /// Closed event, process death. Idempotent - _host.Dispose() closes the window, which
    /// re-raises Closed back into here.</summary>
    private static void DisposeAll()
    {
        if (_disposing) return;
        _disposing = true;
        try
        {
            CancelExitWatchdog();
            StopHeartbeatWatch();
            HookVideoEvents(false);
            try { _meta?.FlushSave(); } catch { }
            _runActive = false;
            try { _host?.Dispose(); } catch { }
            _host = null;
            _meta = null;
            _exiting = false;
            App.Logger?.Information("CaucusHostService: closed");
        }
        finally { _disposing = false; }
    }

    // ============================ settings reads ============================

    private static int SafeMasterVolume()
    {
        try { return App.Settings?.Current?.MasterVolume ?? 100; }
        catch { return 100; }
    }

    private static bool SafeReducedMotion()
    {
        try { return MotionFx.Level != Models.MotionLevel.Full; }
        catch { return false; }
    }

    private static string SafeActiveModId()
    {
        try { return App.Mods?.ActiveModId ?? "builtin-sissyhypno"; }
        catch { return "builtin-sissyhypno"; }
    }
}
