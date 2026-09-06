using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Models.Race;
using ConditioningControlPanel.Services.Race;
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

    // ---- track charts (CHART.md, PR c6) ----
    private static TrackPlayer? _player;
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

    /// <summary>True while the race window is open.</summary>
    public static bool IsActive => _host != null;

    /// <summary>Open the race window (idempotent - refocuses if already open).</summary>
    /// <param name="devTrackPath">The `--race-track` dev arg's file, or null in a normal launch.</param>
    public static void Launch(string? devTrackPath = null)
    {
        if (_host != null) { _host.FocusWeb(); return; }
        try
        {
            _devTrackPath = devTrackPath;
            _devTrackLog = !string.IsNullOrEmpty(devTrackPath);
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
            if (_devTrackLog) ArmDevTrackDrive();
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
    private static void DisposeAll()
    {
        if (_disposing) return;
        _disposing = true;
        try
        {
            CancelExitWatchdog();
            StopHeartbeatWatch();
            HookVideoEvents(false);
            StopTrack();
            try { _player?.Dispose(); } catch { }
            _player = null;
            _devTrackPath = null;
            _devTrackLog = false;
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
    private static void PostChart(TrackChart chart, bool partial)
    {
        int events = chart.Events?.Count ?? 0;
        PostTrack(new { type = "track-chart", chart, partial },
            "{ type: track-chart, partial: " + (partial ? "true" : "false") + ", events: " + events + " }");
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
            StartTrackClock();
            App.Logger?.Information("RaceHost: track loaded {Name} ({Dur:0.0}s)", _trackName, _player.DurationSec);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("RaceHost: track load failed: {E}", ex.Message);
            PostTrack(new { type = "track-error", message = ex.Message });
        }
    }

    /// <summary>The whole analysis, off the UI thread. Every call into the decoder, the analyzer,
    /// the cache and the word spotter sits inside this one try: a file NAudio hates, a missing
    /// Vosk model or a half-written cache entry becomes a track-error, never a crash.</summary>
    private static void AnalyzeTrack(string path, string name, int gen, CancellationToken ct, CancellationTokenSource cts)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            PostProgress("decode", 0, name, force: true);

            string hash = TrackDecoder.HashFile(path);
            var cached = TrackChartCache.TryLoad(hash);
            // A cached chart is only worth reusing if its word pass is as good as the one we could
            // run now: a "none" chart is charted again once a model has appeared.
            if (cached != null && (cached.Analysis?.Words == "vosk-v1" || !TrackWordSpotter.ModelAvailable))
            {
                App.Logger?.Information("RaceHost: chart cache hit for {Name}", name);
                PostChart(cached, partial: false);
                return;
            }

            var pcm = TrackDecoder.Decode(path, new Progress<double>(v => PostProgress("decode", v, name)), ct);
            ct.ThrowIfCancellationRequested();

            var chart = TrackAnalyzer.Energy(pcm, new Progress<double>(v => PostProgress("energy", v, name)), ct);
            ct.ThrowIfCancellationRequested();
            chart.Analysis.Partial = true;
            PostChart(chart, partial: true);

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

    /// <summary>track-play: the run started, so the file starts from its own zero.</summary>
    private static void TrackPlay()
    {
        if (_player == null) return;
        _player.RefreshVolume();
        _player.Play();
        StartTrackClock();
        PostClock();
    }

    /// <summary>track-pause {on}: the Brake, a host pause and a video pop all land here.</summary>
    private static void TrackPause(bool on)
    {
        if (_player == null) return;
        if (on) _player.Pause(); else _player.Resume();
        PostClock();
    }

    /// <summary>End of run, exit or teardown: the audio stops, the clock stops and any analysis
    /// still grinding away is dropped.</summary>
    private static void StopTrack()
    {
        StopTrackClock();
        CancelAnalysis(postCancelled: false);
        try { _player?.Stop(); }
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
        var p = _player;
        if (p == null) { StopTrackClock(); return; }
        PostTrack(new
        {
            type = "track-clock",
            t = Math.Round(p.PositionSec, 3),
            playing = p.IsPlaying,
            durationSec = Math.Round(p.DurationSec, 3),
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
