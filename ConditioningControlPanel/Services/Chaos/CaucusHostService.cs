using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Models.Race;
using ConditioningControlPanel.Services.Race;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Hosts RACING THOUGHTS (called The Caucus Race until 2026-09-06; the class keeps its name):
/// the no-lose kart run that lives as a sibling page next to the DtRH game
/// (<c>Resources/web/dtrh/race.html</c>). A stripped-down sibling of
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
    // THE LOOM: one DtrhLoomStore.Changed subscription per open page, dropped on teardown.
    private static bool _loomHooked;
    private static bool _disposing;
    private static bool _videoHooked;

    // ---- track charts (CHART.md, PR c6) ----
    private static TrackPlayer? _player;
    /// <summary>Whatever is making the sound right now: the local player, or the audio element the
    /// player is driving in the BambiCloud window. Null with no track loaded.</summary>
    private static ITrackClock? _clock;
    private static DispatcherTimer? _trackClock;
    private static CancellationTokenSource? _analysisCts;
    private static string _trackName = "";
    /// <summary>Throttle for track-progress: at most five posts a second, whatever the pass does.</summary>
    private static DateTime _lastProgressUtc = DateTime.MinValue;
    /// <summary>Bumped per pick so a superseded worker knows to keep quiet.</summary>
    private static int _analysisGen;
    /// <summary>Set by the `--race-track` dev arg: the file to drive the track handlers against.</summary>
    private static string? _devTrackPath;
    /// <summary>While the dev arg is active every track-* post is logged as JSON.</summary>
    private static bool _devTrackLog;
    /// <summary>Set by the `--race-cloud` dev arg: open the BambiCloud window once the page is up,
    /// so the cloud path can be exercised without driving the menu by hand.</summary>
    private static bool _devOpenCloud;
    /// <summary>The dev arg's Brake drive runs once, on the first cloud track of the session.</summary>
    private static bool _devCloudDriven;

    // ---- bambicloud (lane D1) ----
    /// <summary>The on-demand browser frame, built the first time the page asks for it.</summary>
    private static RaceCloudWindow? _cloud;
    /// <summary>The cloud clock, kept across laps: one window, one audio element, one clock.</summary>
    private static CloudTrackClock? _cloudClock;
    /// <summary>True between "the next track started over there" and the run-ended that answers our
    /// track-ended. The next lap already owns the clock, so that stop must not take it away.</summary>
    private static bool _cloudSwapping;

    /// <summary>True while the race window is open.</summary>
    public static bool IsActive => _host != null;

    /// <summary>Open the race window (idempotent - refocuses if already open).</summary>
    /// <param name="devTrackPath">The `--race-track` dev arg's file, or null in a normal launch.</param>
    /// <param name="openCloud">The `--race-cloud` dev arg: open the BambiCloud window on its own.</param>
    public static void Launch(string? devTrackPath = null, bool openCloud = false)
    {
        if (_host != null) { _host.FocusWeb(); if (openCloud) OpenCloudWindow(); return; }
        try
        {
            _devTrackPath = devTrackPath;
            // Both dev args log every track-* post as JSON: that log IS the verification, and the
            // cloud path has no other way to show the page what it was sent.
            _devTrackLog = !string.IsNullOrEmpty(devTrackPath) || openCloud;
            _devOpenCloud = openCloud;
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
                WindowTitle = "Racing Thoughts",
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
            if (_devTrackLog) ArmDevTrackDrive();
            if (_devOpenCloud) DevAfter(3, () => OpenCloudWindow());
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
                    // The menu's `levels` panel. The desktop owns the browser window a level
                    // opens in, so only the desktop host offers it; a host without one simply
                    // leaves the key off.
                    cloud = true,
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
            // THE LOOM: the player's own woven spirals. The race draws them live off the
            // params sidecar (raceBoot `loom-list` -> engine/loomSpirals.js), so a pop can be
            // one of theirs instead of a shipped gif. Subscribe once so a spiral woven in the
            // Boudoir mid-session reaches a race that is already open.
            PostLoomList();
            if (!_loomHooked) { DtrhLoomStore.Changed += OnLoomChanged; _loomHooked = true; }
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
                SeasonRecapService.TrackFeature(SeasonFeatureKeys.Race);
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
            // ---- track charts: the page drives the file the run is charted from ----
            case "track-pick":
                PickTrack();
                break;
            case "track-play":
                TrackPlay();
                break;
            case "track-pause":
                TrackPause((bool?)o["on"] ?? false);
                break;
            case "track-stop":
                StopTrack();
                break;
            case "track-cancel":
                CancelAnalysis(postCancelled: true);
                break;
            case "cloud-open":
                // `url` is optional: the levels panel names the track's own page, and a page
                // that does not send one just gets the site's front door.
                OpenCloudWindow((string?)o["url"]);
                break;
            case "exit":       // page-initiated: it winds itself down, then exit-done
                _exiting = true;
                StopTrack();
                ArmExitWatchdog();
                break;
            case "exit-done":
                DisposeAll();
                break;
        }
    }

    /// <summary>fire-payload {kind, strength, durationMult} -> the REAL desktop effects through
    /// the shared factory. Audio only now: every visual effect is in-world on the page, and the video
    /// kind is dark at both ends (the page never spawns one, this refuses it if one ever asks).</summary>
    private static void FirePayload(JObject o)
    {
        try
        {
            var kindStr = (string?)o["kind"];
            if (string.IsNullOrWhiteSpace(kindStr)) return;

            // Video bubbles are dark (race/bubbleKinds.js spawn:false, 2026-09-06): a mandatory video
            // has no business interrupting a lap, so refuse the message even if a page still asks.
            if (string.Equals(kindStr, "video", StringComparison.OrdinalIgnoreCase))
            {
                App.Logger?.Information("RaceHost: video payload refused, video bubbles are dark");
                return;
            }

            EffectPayload payload;
            if (string.Equals(kindStr, "audio", StringComparison.OrdinalIgnoreCase))
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
        // The file is the clock, so the end of the run is the end of the audio either way.
        StopTrack();
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
    /// <summary>THE LOOM: post the saved-spiral library to the race page, the same frame
    /// DtrhHostService posts to the descent (slug + ccp.spirals url + the params sidecar). The
    /// page weaves an entry that has params and falls back to the gif for one that does not.</summary>
    private static void PostLoomList()
    {
        try
        {
            _host?.Post(new
            {
                type = "loom-list",
                spirals = DtrhLoomStore.List().Select(s => new
                {
                    slug = s.Slug,
                    url = $"https://ccp.spirals/loom_{s.Slug}.gif",
                    @params = TryParseLoomParams(s.ParamsJson),
                }),
            });
        }
        catch (Exception ex) { App.Logger?.Debug("RaceHost.PostLoomList: {E}", ex.Message); }
    }

    private static JObject? TryParseLoomParams(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JObject.Parse(json); } catch { return null; }
    }

    /// <summary>A save or delete in the Boudoir: re-post, on the UI thread, only while a race
    /// page is actually up.</summary>
    private static void OnLoomChanged()
    {
        try
        {
            if (_host == null) return;
            var d = Application.Current?.Dispatcher;
            if (d == null || d.HasShutdownStarted) return;
            d.BeginInvoke(new Action(() => { if (_host != null) PostLoomList(); }));
        }
        catch (Exception ex) { App.Logger?.Debug("RaceHost.OnLoomChanged: {E}", ex.Message); }
    }

    private static void DisposeAll()
    {
        if (_disposing) return;
        _disposing = true;
        try
        {
            CancelExitWatchdog();
            StopHeartbeatWatch();
            HookVideoEvents(false);
            _cloudSwapping = false;
            _devCloudDriven = false;
            StopTrack();
            try { _cloud?.Dispose(); } catch { }
            _cloud = null;
            _cloudClock = null;
            _clock = null;
            try { _player?.Dispose(); } catch { }
            _player = null;
            _devTrackPath = null;
            _devTrackLog = false;
            try { _meta?.FlushSave(); } catch { }
            _runActive = false;
            if (_loomHooked) { try { DtrhLoomStore.Changed -= OnLoomChanged; } catch { } _loomHooked = false; }
            try { _host?.Dispose(); } catch { }
            _host = null;
            _meta = null;
            _exiting = false;
            App.Logger?.Information("CaucusHostService: closed");
        }
        finally { _disposing = false; }
    }

    // ============================ track charts ============================
    //
    // CHART.md "Host protocol additions (PR c6)". The page asks for a file, the host charts it on
    // a worker and answers with progress, one or two charts, a 250 ms clock and an ended note.
    // Nothing about the audio ever leaves the machine: the chart carries timestamps and labels.

    /// <summary>Every host to page track message goes through here: logged under the dev arg,
    /// then marshalled onto the UI thread because the analysis runs on a worker.</summary>
    private static void PostTrack(object msg, string? logAs = null)
    {
        if (_devTrackLog)
        {
            try
            {
                App.Logger?.Information("RaceHost track post: {Msg}",
                    logAs ?? Newtonsoft.Json.JsonConvert.SerializeObject(msg));
            }
            catch { }
        }
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        if (disp.CheckAccess()) { try { _host?.Post(msg); } catch { } }
        else disp.BeginInvoke(() => { try { _host?.Post(msg); } catch { } });
    }

    /// <summary>track-progress, throttled to five posts a second so a fast pass cannot flood the
    /// bridge. A forced post (a stage change, a cancel) always goes.</summary>
    private static void PostProgress(string stage, double pct, string name, bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && (now - _lastProgressUtc).TotalMilliseconds < 200) return;
        _lastProgressUtc = now;
        PostTrack(new { type = "track-progress", stage, pct = Math.Clamp(pct, 0, 1), name });
    }

    /// <summary>track-chart. The chart itself goes out whole; the dev log only gets a summary,
    /// since a full chart is thousands of lines of numbers.</summary>
    /// <param name="authored">A person wrote this one. The plate says so, and the page knows not to
    /// expect a fuller chart behind it: an authored chart is never partial and never replaced.</param>
    private static void PostChart(TrackChart chart, bool partial, bool authored = false)
    {
        int events = chart.Events?.Count ?? 0;
        PostTrack(new { type = "track-chart", chart, partial, authored },
            "{ type: track-chart, partial: " + (partial ? "true" : "false")
                + (authored ? ", authored: true" : "") + ", events: " + events + " }");
    }

    /// <summary>track-pick: the file dialog on the UI thread. A cancelled dialog is not an error,
    /// it is a cancelled progress post.</summary>
    private static void PickTrack()
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        disp.BeginInvoke(() =>
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Load a track",
                    Filter = "Audio|*.mp3;*.wav;*.m4a;*.wma;*.flac;*.ogg|All files|*.*",
                    CheckFileExists = true,
                };
                var owner = _host?.Window;
                bool? ok = owner != null && owner.IsLoaded ? dlg.ShowDialog(owner) : dlg.ShowDialog();
                if (ok != true) { PostProgress("cancelled", 0, "", force: true); return; }
                BeginTrack(dlg.FileName);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("RaceHost.track-pick: {E}", ex.Message);
                PostTrack(new { type = "track-error", message = ex.Message });
            }
        });
    }

    /// <summary>A pick landed: load it into the player right away so track-play can start it,
    /// then chart it on a worker.</summary>
    private static void BeginTrack(string path)
    {
        CancelAnalysis(postCancelled: false);
        _trackName = Path.GetFileName(path);
        _lastProgressUtc = DateTime.MinValue;
        LoadTrackFile(path);

        var cts = new CancellationTokenSource();
        _analysisCts = cts;
        int gen = ++_analysisGen;
        string name = _trackName;
        var ct = cts.Token;
        _ = Task.Run(() => AnalyzeTrack(path, name, gen, ct, cts));
    }

    private static void LoadTrackFile(string path)
    {
        try
        {
            if (_player == null)
            {
                _player = new TrackPlayer();
                _player.Ended += OnTrackEnded;
            }
            _player.Stop();
            _player.Load(path);
            _clock = new LocalTrackClock(_player);
            StartTrackClock();
            App.Logger?.Information("RaceHost: track loaded {Name} ({Dur:0.0}s)", _trackName, _player.DurationSec);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("RaceHost: track load failed: {E}", ex.Message);
            PostTrack(new { type = "track-error", message = ex.Message });
        }
    }

    /// <summary>
    /// Doors (a), (b) and (c) of the lookup: an authored chart the player wrote, an authored chart
    /// we ship, or a generated one already in the cache. Answers true when one of them posted a
    /// chart, in which case NOTHING is analysed: an authored chart is used exactly as it was
    /// written, and a cache hit was that analysis already.
    ///
    /// A cached chart is only worth reusing if its word pass is as good as the one we could run now,
    /// so a "none" chart is charted again once a Vosk model has appeared. An authored chart never is:
    /// there is no better pass than the person who wrote it.
    /// </summary>
    private static bool TryChartWithoutAnalysis(string hash, string? cloudId, string name, string? displayName)
    {
        var authored = AuthoredCharts.Find(hash, cloudId, out string door);
        if (authored != null)
        {
            if (string.IsNullOrWhiteSpace(authored.Source.Name) && !string.IsNullOrWhiteSpace(displayName))
                authored.Source.Name = displayName!;
            App.Logger?.Information("RaceHost: chart for {Name} via {Door}", name, door);
            PostChart(authored, partial: false, authored: true);
            return true;
        }

        var cached = TrackChartCache.TryLoad(hash);
        if (cached == null) return false;
        if (!AuthoredCharts.IsAuthored(cached) && cached.Analysis?.Words != "vosk-v1" && TrackWordSpotter.ModelAvailable)
            return false;
        App.Logger?.Information("RaceHost: chart for {Name} via {Door}", name, AuthoredCharts.DoorCache);
        PostChart(cached, partial: false, authored: AuthoredCharts.IsAuthored(cached));
        return true;
    }

    /// <summary>The whole analysis, off the UI thread. Every call into the decoder, the analyzer,
    /// the cache and the word spotter sits inside this one try: a file NAudio hates, a missing
    /// Vosk model or a half-written cache entry becomes a track-error, never a crash.</summary>
    /// <param name="displayName">The name to chart under, when the file's own is not worth having.
    /// A cloud track lands in a temp file called a GUID, and its title is the better name for both
    /// the plate and the cache: pick the same audio up locally later and the chart still reads as
    /// the track, not as a download nobody kept.</param>
    /// <param name="cloudId">The stable name of the file on the CDN, when the track came from
    /// there. An authored chart may be keyed by it, and it survives the file being renamed.</param>
    private static void AnalyzeTrack(string path, string name, int gen, CancellationToken ct, CancellationTokenSource cts,
        string? displayName = null, string? cloudId = null)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            PostProgress("decode", 0, name, force: true);

            string hash = TrackDecoder.HashFile(path);
            if (TryChartWithoutAnalysis(hash, cloudId, name, displayName)) return;
            App.Logger?.Information("RaceHost: chart for {Name} via {Door}", name, AuthoredCharts.DoorGenerated);

            var pcm = TrackDecoder.Decode(path, new Progress<double>(v => PostProgress("decode", v, name)), ct);
            ct.ThrowIfCancellationRequested();

            var chart = TrackAnalyzer.Energy(pcm, new Progress<double>(v => PostProgress("energy", v, name)), ct);
            ct.ThrowIfCancellationRequested();
            chart.Analysis.Partial = true;
            if (displayName != null) chart.Source.Name = displayName;
            PostChart(chart, partial: true);
            App.Logger?.Information("RaceHost: partial chart for {Name}: {Events} events", name, chart.Events?.Count ?? 0);

            // The word pass is the slow one, which is why the page already has a playable chart.
            var lexicon = TrackLexicon.Build();
            var words = TrackWordSpotter.Spot(pcm, lexicon, new Progress<double>(v => PostProgress("words", v, name)), ct);
            ct.ThrowIfCancellationRequested();
            TrackChartWords.Apply(chart, words, lexicon);
            chart.Analysis.Partial = false;
            TrackChartCache.Save(chart);
            PostChart(chart, partial: false);
            App.Logger?.Information("RaceHost: charted {Name}: {Events} events", name, chart.Events?.Count ?? 0);
        }
        catch (OperationCanceledException)
        {
            // A newer pick superseded this one: that pick owns the plate now, so say nothing.
            if (_analysisGen == gen) PostProgress("cancelled", 0, "", force: true);
            App.Logger?.Information("RaceHost: track analysis cancelled for {Name}", name);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("RaceHost: track analysis failed for {Name}: {E}", name, ex.Message);
            PostTrack(new { type = "track-error", message = ex.Message });
        }
        finally
        {
            if (ReferenceEquals(_analysisCts, cts)) _analysisCts = null;
            try { cts.Dispose(); } catch { }
        }
    }

    /// <summary>Drop an analysis in flight. With nothing running there is no worker to answer, so
    /// an explicit track-cancel is answered here.</summary>
    private static void CancelAnalysis(bool postCancelled)
    {
        var cts = _analysisCts;
        _analysisCts = null;
        if (cts == null)
        {
            if (postCancelled) PostProgress("cancelled", 0, "", force: true);
            return;
        }
        try { cts.Cancel(); }
        catch (Exception ex) { App.Logger?.Debug("RaceHost.CancelAnalysis: {E}", ex.Message); }
    }

    /// <summary>track-play: the run started, so a local file starts from its own zero. A cloud
    /// element is already running (that is what started the run) and is left alone.</summary>
    private static void TrackPlay()
    {
        var c = _clock;
        if (c == null) return;
        c.Start();
        StartTrackClock();
        PostClock();
    }

    /// <summary>track-pause {on}: the Brake, a host pause and a video pop all land here. On the
    /// cloud source this is the frame that pauses their player, which is the other half of the
    /// bargain their own pause button makes when it pauses the race.</summary>
    private static void TrackPause(bool on)
    {
        var c = _clock;
        if (c == null) return;
        c.SetPaused(on);
        PostClock();
    }

    /// <summary>End of run, exit or teardown: the audio stops, the clock stops and any analysis
    /// still grinding away is dropped. A cloud lap turnover is the one stop that passes straight
    /// through: the next track already owns the clock and its chart is already on the way.</summary>
    private static void StopTrack()
    {
        bool swapping = _cloudSwapping;
        _cloudSwapping = false;
        if (swapping) return;
        StopTrackClock();
        CancelAnalysis(postCancelled: false);
        try { _clock?.Stop(); }
        catch (Exception ex) { App.Logger?.Debug("RaceHost.StopTrack: {E}", ex.Message); }
    }

    private static void OnTrackEnded()
    {
        StopTrackClock();
        PostTrack(new { type = "track-ended" });
        App.Logger?.Information("RaceHost: track ended");
    }

    /// <summary>The 250 ms clock the page integrates between. UI thread only.</summary>
    private static void StartTrackClock()
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        if (_trackClock == null)
        {
            _trackClock = new DispatcherTimer(DispatcherPriority.Normal, disp)
            {
                Interval = TimeSpan.FromMilliseconds(250),
            };
            _trackClock.Tick += (_, _) => PostClock();
        }
        _trackClock.Start();
    }

    private static void StopTrackClock()
    {
        try { _trackClock?.Stop(); } catch { }
    }

    private static void PostClock()
    {
        var c = _clock;
        if (c == null) { StopTrackClock(); return; }
        PostTrack(new
        {
            type = "track-clock",
            t = Math.Round(c.PositionSec, 3),
            playing = c.IsPlaying,
            durationSec = Math.Round(c.DurationSec, 3),
        });
    }

    /// <summary>The dev arg's drive: pick, play, pause at 5 s, resume at 8 s, stop at 12 s, with
    /// every post logged. Posts queue inside the host until the page handshakes, so this exercises
    /// the C# side on its own.</summary>
    private static void ArmDevTrackDrive()
    {
        var path = _devTrackPath;
        if (string.IsNullOrEmpty(path)) return;
        App.Logger?.Information("RaceHost dev: --race-track {Path}", path);
        DevAfter(2, () => { BeginTrack(path); TrackPlay(); });
        DevAfter(7, () => TrackPause(true));
        DevAfter(10, () => TrackPause(false));
        DevAfter(14, StopTrack);
    }

    /// <summary>The `--race-cloud` arg's other half: once a cloud track is running, hold the Brake
    /// at 10 s and let it go at 16 s. That is the only way to watch C# pause their own player,
    /// because the race window will not take a synthetic key press.</summary>
    private static void ArmDevCloudDrive()
    {
        if (!_devOpenCloud || _devCloudDriven) return;
        _devCloudDriven = true;
        App.Logger?.Information("RaceHost dev: --race-cloud brake at 10s, released at 16s");
        DevAfter(10, () => { App.Logger?.Information("RaceHost dev: brake on"); TrackPause(true); });
        DevAfter(16, () => { App.Logger?.Information("RaceHost dev: brake off"); TrackPause(false); });
    }

    private static void DevAfter(double sec, Action act)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(sec) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            try { act(); }
            catch (Exception ex) { App.Logger?.Warning("RaceHost dev step: {E}", ex.Message); }
        };
        t.Start();
    }

    // ============================ bambicloud (lane D1) ============================
    //
    // The player signs in over there and drives their own playlist. We open a plain browser frame
    // on the site, watch the audio element their page is already playing, and follow it: their
    // pause pauses the race, the Brake pauses them, the next track is the next lap.
    //
    // READ-ONLY GUEST. Nothing here calls their API, logs anybody in, or reads anything of theirs
    // beyond the audio element's own state and the tab title. See RaceCloudWindow for the window
    // and for the watcher, which is the only code of ours that runs inside their page.

    /// <summary>The one message the player ever sees when the site is not answering. Plain, and it
    /// points at the path that always works.</summary>
    private const string CloudDownMessage = "bambicloud is not answering, load a file instead";

    /// <summary>cloud-open: a level tapped on the menu. Opens the window on that track's page, or
    /// brings it back if the player closed it (closing hides it, so their playlist survives). A
    /// null or off-site url falls back to the site's front door.</summary>
    private static void OpenCloudWindow(string? url = null)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        disp.BeginInvoke(() =>
        {
            try
            {
                if (_cloud == null)
                {
                    _cloud = new RaceCloudWindow();
                    _cloud.Message += OnCloudMessage;
                    _cloud.Hidden += OnCloudHidden;
                }
                _cloud.ShowOrFocus(RaceCloudWindow.IsSiteUri(url) ? url : null);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("RaceHost.cloud-open: {E}", ex.Message);
                PostTrack(new { type = "track-error", message = CloudDownMessage });
            }
        });
    }

    /// <summary>The window was closed (hidden) with no cloud track in hand: take the menu's
    /// "opening" plate back down so it is not left waiting for something that is not coming.</summary>
    private static void OnCloudHidden()
    {
        if (_clock is CloudTrackClock) return;
        PostProgress("cancelled", 0, "", force: true);
    }

    /// <summary>Every cloud-* frame the watcher posts, on the UI thread.</summary>
    private static void OnCloudMessage(JObject o)
    {
        try
        {
            switch ((string?)o["type"])
            {
                case "cloud-track":
                    OnCloudTrack(o);
                    break;
                case "cloud-clock":
                    if (_cloudClock == null || !ReferenceEquals(_clock, _cloudClock)) break;
                    _cloudClock.Update((double?)o["t"] ?? 0, (bool?)o["playing"] ?? false, (double?)o["durationSec"] ?? 0);
                    break;
                case "cloud-play":
                    OnCloudPlay();
                    break;
                case "cloud-pause":
                    if (_cloudClock == null || !ReferenceEquals(_clock, _cloudClock)) break;
                    _cloudClock.Update(_cloudClock.PositionSec, false, _cloudClock.DurationSec);
                    PostClock();   // the run reads playing:false and holds where it is
                    break;
                case "cloud-ended":
                    OnCloudEnded();
                    break;
                case "cloud-failed":
                    PostTrack(new { type = "track-error", message = CloudDownMessage });
                    break;
            }
        }
        catch (Exception ex) { App.Logger?.Warning("RaceHost.OnCloudMessage: {E}", ex.Message); }
    }

    /// <summary>A new source started playing over there. It becomes the clock straight away, so the
    /// run follows the voice from the first second; the chart catches up behind it.</summary>
    private static void OnCloudTrack(JObject o)
    {
        string src = (string?)o["src"] ?? "";
        string title = ((string?)o["title"] ?? "").Trim();
        double dur = (double?)o["durationSec"] ?? 0;
        if (string.IsNullOrWhiteSpace(src)) return;

        // A new track while a run is live is the next lap: end this one the way the file running
        // out does. The run-ended that answers must not take the new clock away, hence the flag.
        bool live = _runActive && _clock is CloudTrackClock;
        CancelAnalysis(postCancelled: false);
        try { _player?.Stop(); } catch (Exception ex) { App.Logger?.Debug("RaceHost.cloud stop local: {E}", ex.Message); }

        _trackName = string.IsNullOrWhiteSpace(title) ? "bambicloud" : title;
        _lastProgressUtc = DateTime.MinValue;
        _cloudClock ??= new CloudTrackClock(SetCloudPaused);
        _cloudClock.Update(0, true, dur);
        _clock = _cloudClock;
        if (live)
        {
            _cloudSwapping = true;
            PostTrack(new { type = "track-ended" });
        }
        StartTrackClock();
        PostClock();
        App.Logger?.Information("RaceHost: cloud track {Name} ({Dur:0.0}s){Lap}",
            _trackName, dur, live ? ", next lap" : "");
        ArmDevCloudDrive();
        BeginCloudChart(src);
    }

    /// <summary>Their player started. If the race is still sitting on the menu, this is what starts
    /// the run: the audio is the clock, so the run begins when the audio does. A play pressed over
    /// there hands the focus back to the race, so the player is not left steering a browser while
    /// the run starts behind it. The BambiCloud window stays open (the audio lives in it), just under.</summary>
    private static void OnCloudPlay()
    {
        if (_cloudClock == null || !ReferenceEquals(_clock, _cloudClock)) return;
        _cloudClock.Update(_cloudClock.PositionSec, true, _cloudClock.DurationSec);
        if (!_runActive) PostTrack(new { type = "cloud-run" });
        StartTrackClock();
        PostClock();
        if (_cloud?.IsFocused == true) _host?.FocusWeb();
    }

    /// <summary>Their element ran out. Same ending a local file gets: the page winds the lap up and
    /// the next cloud-track starts the next one.</summary>
    private static void OnCloudEnded()
    {
        if (!ReferenceEquals(_clock, _cloudClock) || _cloudClock == null) return;
        _cloudClock.Stop();
        StopTrackClock();
        PostTrack(new { type = "track-ended" });
        App.Logger?.Information("RaceHost: cloud track ended");
    }

    /// <summary>The Brake's half of the bargain: cloud-set-paused into their page.</summary>
    private static void SetCloudPaused(bool on)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        if (disp.CheckAccess()) _cloud?.PostToPage(new { type = "cloud-set-paused", on });
        else disp.BeginInvoke(() => _cloud?.PostToPage(new { type = "cloud-set-paused", on }));
    }

    // ---- charting what they are playing ----
    //
    // The chart comes from the audio itself, which means the desktop pulls the file down and runs
    // the SAME analysis a picked file gets: cache hit by hash first (so a track charted once, from
    // anywhere, is instant), then the energy pass, then the word pass. The page runs the plain
    // seeded road until the partial chart lands and swaps in mid-run, exactly as CHART.md says.
    //
    // The file lives under race/cloud/ for as long as the analysis takes and is deleted after,
    // cancelled or not: audio never stays on this machine, charts hold timestamps and labels.

    /// <summary>Where a cloud track waits while it is being charted. Emptied as it goes.</summary>
    private static string CloudTempRoot => Path.Combine(App.UserDataPath, "race", "cloud");

    /// <summary>One client for the whole session. The desktop talks to the site directly: nothing
    /// of theirs is ever proxied through anything of ours.</summary>
    private static readonly HttpClient CloudHttp = new() { Timeout = TimeSpan.FromMinutes(15) };

    /// <summary>Pull the track down and chart it. Supersedes any analysis already running, the same
    /// way a second file pick does.</summary>
    private static void BeginCloudChart(string src)
    {
        SweepCloudTemp();
        var cts = new CancellationTokenSource();
        _analysisCts = cts;
        int gen = ++_analysisGen;
        string name = _trackName;
        PostProgress("fetching", 0, name, force: true);
        _ = Task.Run(() => ChartCloudTrackAsync(src, name, gen, cts.Token, cts));
    }

    private static async Task ChartCloudTrackAsync(string src, string name, int gen, CancellationToken ct, CancellationTokenSource cts)
    {
        string? temp = null;
        try
        {
            // The doors before the download. (a) and (b) by cloudId need only the url, and all three
            // of (a), (b) and (c) answer off the hash, which is a length and a megabyte. An authored
            // or already charted track therefore costs no download at all.
            string cloudId = AuthoredCharts.CloudIdFrom(src);
            if (TryChartWithoutAnalysis("", cloudId, name, name)) return;

            string? hash = await ProbeCloudHashAsync(src, name, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (hash != null && TryChartWithoutAnalysis(hash, cloudId, name, name)) return;

            temp = await DownloadCloudTrackAsync(src, name, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            // From here it is the ordinary path, cache and all - the hash is computed off these
            // bytes by TrackDecoder.HashFile, so it matches a local chart of the same file.
            AnalyzeTrack(temp, name, gen, ct, cts, displayName: name, cloudId: cloudId);
        }
        catch (OperationCanceledException)
        {
            if (_analysisGen == gen) PostProgress("cancelled", 0, "", force: true);
            App.Logger?.Information("RaceHost: cloud chart cancelled for {Name}", name);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("RaceHost: cloud chart failed for {Name}: {E}", name, ex.Message);
            // Fail loud, and leave the seeded road running: the run is already following the clock.
            if (_analysisGen == gen) PostTrack(new { type = "track-error", message = CloudDownMessage });
        }
        finally
        {
            DeleteCloudTemp(temp);
            if (ReferenceEquals(_analysisCts, cts)) _analysisCts = null;
            try { cts.Dispose(); } catch (Exception ex) { App.Logger?.Debug("RaceHost.cloud cts: {E}", ex.Message); }
        }
    }

    /// <summary>
    /// The CHART.md hash of a track without downloading it: a HEAD for the length and a Range for
    /// the first 1 MiB. <see cref="TrackDecoder.HashBytes"/> is the same recipe HashFile uses over a
    /// file, so the number matches the one a local copy of the same audio would give.
    ///
    /// Null means "ask the ordinary way": no length, no ranges, a short read, anything. The caller
    /// falls back to the full download and hashes the file, which is exactly what it did before this
    /// existed. ONE retry and no more, the same bargain the download makes.
    /// </summary>
    private static async Task<string?> ProbeCloudHashAsync(string src, string name, CancellationToken ct)
    {
        if (!RaceCloudWindow.IsSiteUri(src)) return null;
        try
        {
            long? length = await CloudLengthAsync(src, ct).ConfigureAwait(false);
            if (length is not > 0) return null;

            int want = (int)Math.Min(TrackDecoder.HashHead, length.Value);
            using var req = new HttpRequestMessage(HttpMethod.Get, src);
            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, want - 1);
            using var resp = await CloudHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            // A 200 here is the CDN ignoring the range and offering the whole file. Walk away: the
            // download path is about to ask for it properly, with progress.
            if (resp.StatusCode != System.Net.HttpStatusCode.PartialContent) return null;

            var head = new byte[want];
            using (var body = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            {
                int filled = 0, got;
                while (filled < want && (got = await body.ReadAsync(head.AsMemory(filled, want - filled), ct).ConfigureAwait(false)) > 0)
                    filled += got;
                if (filled != want) return null;
            }

            string hash = TrackDecoder.HashBytes(length.Value, head);
            App.Logger?.Information("RaceHost: cloud hash for {Name} off {Bytes} bytes of {Total}: {Hash}", name, want, length.Value, hash);
            return hash;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            App.Logger?.Information("RaceHost: cloud hash probe failed for {Name} ({E}), downloading instead", name, ex.Message);
            return null;
        }
    }

    /// <summary>The file's length: a HEAD, or the total out of a one-byte range's Content-Range for
    /// a CDN that will not answer HEAD. Null when neither says.</summary>
    private static async Task<long?> CloudLengthAsync(string src, CancellationToken ct)
    {
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, src);
            using var resp = await CloudHttp.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode && resp.Content.Headers.ContentLength is > 0)
                return resp.Content.Headers.ContentLength;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { App.Logger?.Debug("RaceHost.cloud head: {E}", ex.Message); }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, src);
            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using var resp = await CloudHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            return resp.Content.Headers.ContentRange?.Length;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { App.Logger?.Debug("RaceHost.cloud range probe: {E}", ex.Message); }
        return null;
    }

    /// <summary>Stream the audio to a temp file, reporting bytes as track-progress. ONE retry and no
    /// more: a site that answered wrong twice is a site to stop asking.</summary>
    private static async Task<string> DownloadCloudTrackAsync(string src, string name, CancellationToken ct)
    {
        // Their own addresses only. A player's page could carry any src at all, and this is the one
        // place a url off that page turns into a request from the desktop.
        if (!RaceCloudWindow.IsSiteUri(src))
            throw new InvalidOperationException("the audio is not an address we may fetch");

        Directory.CreateDirectory(CloudTempRoot);
        string path = Path.Combine(CloudTempRoot, "cloud-" + Guid.NewGuid().ToString("N") + CloudExtension(src));
        Exception? last = null;
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var resp = await CloudHttp.GetAsync(src, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                long? total = resp.Content.Headers.ContentLength;
                using var body = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                {
                    var buffer = new byte[1 << 16];
                    long got = 0;
                    int read;
                    while ((read = await body.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        got += read;
                        if (total is > 0) PostProgress("fetching", (double)got / total.Value, name);
                    }
                    await file.FlushAsync(ct).ConfigureAwait(false);
                    App.Logger?.Information("RaceHost: cloud audio down for {Name} ({Bytes} bytes)", name, got);
                }
                return path;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                last = ex;
                DeleteCloudTemp(path);
                App.Logger?.Information("RaceHost: cloud download attempt {N} failed: {E}", attempt, ex.Message);
            }
        }
        throw last ?? new IOException("the audio would not come down");
    }

    /// <summary>The url's own extension when it is one the decoder knows, else mp3.</summary>
    private static string CloudExtension(string src)
    {
        try
        {
            var ext = Path.GetExtension(new Uri(src).AbsolutePath).ToLowerInvariant();
            return ext is ".mp3" or ".m4a" or ".wav" or ".ogg" or ".flac" or ".wma" ? ext : ".mp3";
        }
        catch { return ".mp3"; }
    }

    private static void DeleteCloudTemp(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { App.Logger?.Debug("RaceHost.cloud temp delete: {E}", ex.Message); }
    }

    /// <summary>A crash mid-chart is the one way a download outlives its analysis. Anything left in
    /// the folder from a previous session goes before the next one starts.</summary>
    private static void SweepCloudTemp()
    {
        try
        {
            if (!Directory.Exists(CloudTempRoot)) return;
            var cutoff = DateTime.UtcNow.AddHours(-6);
            foreach (var file in Directory.GetFiles(CloudTempRoot, "cloud-*"))
            {
                try { if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file); }
                catch (Exception ex) { App.Logger?.Debug("RaceHost.cloud sweep file: {E}", ex.Message); }
            }
        }
        catch (Exception ex) { App.Logger?.Debug("RaceHost.cloud sweep: {E}", ex.Message); }
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
