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
/// bridge protocol (v1):
///   - fire-payload -> the REAL desktop conditioning effects (EffectPayloadFactory)
///   - meta-command -> <see cref="DtrhMetaBridge"/> (chaos_meta.json stays C#-owned)
///   - run-started/run-ended -> crash sentinel, barks, XP payout + payout-result reply
///   - payload-state (video) -> page pauses while a mandatory video covers it; focus returns after
///   - heartbeat watchdog + ProcessFailed relaunch-once recovery
///
/// Deliberately NOT an evolution of ChaosTunnelService - opposite window semantics (the
/// tunnel is a passive backdrop under the WPF game; this IS the game surface). The two
/// coexist until the legacy WPF game retires.
/// </summary>
internal static class DtrhHostService
{
    private const int Protocol = 1;
    private static ChaosWebViewHost? _host;
    private static DtrhMetaBridge? _meta;
    private static DispatcherTimer? _exitWatchdog;
    private static DispatcherTimer? _heartbeatWatch;
    private static DateTime _lastHeartbeatUtc;
    private static bool _exiting;
    private static bool _runActive;
    private static bool _minimizedMainWindow;   // we tucked the main window to the tray for this session
    private static bool _relaunchedOnce;
    private static bool _testMode;
    private static bool _videoHooked;
    private static bool _vnSpeaking;   // a VN tutorial beat owns the mix: skip native stingers/barks
    private static bool _worldFrozen;  // an in-world Freeze bubble is holding native video + voice
    private static bool _diveMuted;    // the dive's in-page master mute is holding native video silent

    // ---- per-run native engagement metrics (local-only session telemetry) ----
    // Durations only the host can measure (video watch / voiceover / native audio
    // subliminals) accumulate here between run-started and run-ended, then merge into
    // the page's sessionStats snapshot and sum into DtrhSessionStatsStore at OnRunEnded.
    private static double _runVideoWatchSec;
    private static int _runVideosShown;
    private static int _runVideosSkipped;
    private static int _runVoicelines;
    private static double _runVoiceoverSec;
    private static int _runSubliminalsHeard;

    public static bool IsActive => _host != null;

    /// <summary>A DtRH descent is currently running (between run-started and run-ended). Used to
    /// cheaply gate optional per-run telemetry off the global bark/audio paths.</summary>
    public static bool IsRunActive => _runActive;

    /// <summary>The page reported boot-error (WebGL/GPU init failed) this app session.
    /// The Lab entry points check this and route to the classic WPF game instead, so a
    /// machine that can't run the browser game isn't stuck clicking into a black flash.</summary>
    public static bool BootFailedThisSession { get; private set; }

    /// <summary>Launch the game window (idempotent). The page boots into The Fall on the
    /// active preset; the Warren hub becomes the boot target in M5.</summary>
    public static void Launch(bool testMode = false)
    {
        if (_host != null) { _host.FocusWeb(); return; }
        try
        {
            _exiting = false;
            _runActive = false;
            _worldFrozen = false;
            _testMode = testMode;
            _meta = new DtrhMetaBridge(testMode, msg => _host?.Post(msg));
            var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
            // THE LOOM (crafting Part 2): saved spirals live in the CCP Spirals library
            // folder, exposed to the page as ccp.spirals. A missing folder would make
            // WebView2 SKIP the mapping entirely, so create it BEFORE Launch.
            try { Directory.CreateDirectory(DtrhLoomStore.SpiralsFolder); }
            catch (Exception ex) { App.Logger?.Debug("DtrhHost: spirals dir create failed: {E}", ex.Message); }
            var mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
            {
                ("ccp.game", webRoot, CoreWebView2HostResourceAccessKind.Deny),
                // Allow (not DenyCors): the engine uploads this media to WebGL, which
                // needs CORS-clean responses (verified in the M0 spike).
                ("ccp.assets", App.EffectiveAssetsPath, CoreWebView2HostResourceAccessKind.Allow),
                // M4: the bundled Chaos art (bubble sprites, boon icons, announcer
                // banners). Allow (not DenyCors): the in-tube boon draft
                // (engine/boonPick.js) uploads the boon icons to WebGL, which needs
                // CORS-clean responses; plain <img> consumers are unaffected.
                ("ccp.art", Path.Combine(AppContext.BaseDirectory, "assets", "Chaos"), CoreWebView2HostResourceAccessKind.Allow),
                // THE LOOM: the saved-spiral GIFs (thumbnails + in-run overlay pool).
                ("ccp.spirals", DtrhLoomStore.SpiralsFolder, CoreWebView2HostResourceAccessKind.Allow),
            };
            // Creator mods: an installed mod's resources/dtrh folder (voice clips,
            // descent media, portrait, drone). Mapping ONLY the dtrh subfolder keeps
            // the rest of the mod's resources off the page. Launch-time snapshot:
            // switching the active mod requires a DTRH restart (ActivateMod closes us).
            var modDtrh = DtrhModContent.ModDtrhRoot();
            if (modDtrh != null)
                mappings.Add(("ccp.mod", modDtrh, CoreWebView2HostResourceAccessKind.Allow));
            _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
            {
                StartUrl = "https://ccp.game/dtrh/index.html",
                PrimaryHost = "ccp.game",
                Mappings = mappings,
                UserDataFolderName = "browser_data_dtrh",
                InputEnabled = true,
                // Launch in a normal titled window (Alt-Tab / minimize / move); the dock's [ ]
                // button toggles borderless fullscreen. All effects are in-world now, so nothing
                // needs the old topmost fullscreen surface.
                StartFullscreen = false,
                WindowTitle = "Down the Rabbit Hole",
                LogTag = "DtrhHost",
                // The game's audio bed / drift voice must start without a click.
                ExtraBrowserArguments = "--autoplay-policy=no-user-gesture-required",
                OnReady = OnPageReady,
                OnMessage = OnPageMessage,
                OnProcessFailed = OnProcessFailed,
            });
            _host.Show();
            HookVideoEvents(true);
            StartHeartbeatWatch();
            Haptics.DtrhHapticDirector.OnLaunch(testMode);
            // Tuck the main CCP window into the tray while the descent owns the screen;
            // DisposeAll restores it on exit. The game window keeps focus.
            try
            {
                if (Application.Current?.MainWindow is MainWindow mw)
                {
                    mw.MinimizeToTrayForChaos();
                    _minimizedMainWindow = true;
                    _host.FocusWeb();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("DtrhHost: minimize main window failed: {E}", ex.Message); }
            App.Logger?.Information("DtrhHostService: launched{T}", testMode ? " (M2 TEST MODE)" : "");
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
                ArmExitWatchdog();
            }
            else
            {
                DisposeAll();
            }
        }
        catch (Exception ex) { App.Logger?.Debug("DtrhHostService.CloseActive: {E}", ex.Message); DisposeAll(); }
    }

    // ============================ boot ============================

    private static void OnPageReady()
    {
        try
        {
            _lastHeartbeatUtc = DateTime.UtcNow;
            // Keyboard focus does not land in the WebView2 child on a fresh launch until a
            // click - claim it now so Esc (pause / hold-to-exit) works from the first frame.
            _host?.FocusWeb();
            _host?.Post(new
            {
                type = "init",
                protocol = Protocol,
                settings = new { masterVolume = SafeMasterVolume() },
                // Active persona (builtin-bambisleep / builtin-sissyhypno / builtin-locked):
                // the page's VN portrait picks the matching portrait set + tint.
                modId = SafeActiveModId(),
                // Creator mods: the mod's own DTRH content (drift pools, portrait,
                // tint, drone) as ccp.mod URLs; null = no mod content, page runs
                // on its shipped assets exactly as before.
                modContent = DtrhModContent.BuildInitPayload(),
                // M5: the page boots into the Warren hub; a run's config is dealt
                // per-descent by request-run. init carries the SAVED run setup so
                // the hub's Descent tab opens on the user's own choices.
                runSetup = BuildRunSetup(),
                m2Test = _testMode,
            });
            if (_meta != null) _host?.Post(_meta.SnapshotMessage());
            var m = DtrhAssetManifest.Build();
            DtrhModContent.MergeMedia(m);   // creator mods: mix/replace descent media
            _host?.Post(new
            {
                type = "manifest",
                images = m.Images.Select(e => new { name = e.Name, url = e.Url }),
                videos = m.Videos.Select(e => new { name = e.Name, url = e.Url }),
                skipped = m.Skipped,
                truncated = m.Truncated,
            });
            PostLoomList();   // THE LOOM: seed the page's saved-spiral pool
            // THE BIOMES (S3 read-back): the cumulative engagement ranking, so the
            // page can bias toward the media the user actually likes (the exact
            // future feature DtrhAssetStatsStore was built to serve).
            try
            {
                var favorites = DtrhAssetStatsStore.TopAssets(12);
                if (favorites.Count > 0) _host?.Post(new { type = "favorites", names = favorites });
            }
            catch (Exception ex) { App.Logger?.Debug("DtrhHostService favorites post failed: {E}", ex.Message); }
        }
        catch (Exception ex) { App.Logger?.Warning("DtrhHostService.OnPageReady: {E}", ex.Message); }
    }

    // ============================ page messages ============================

    private static void OnPageMessage(JObject o)
    {
        switch ((string?)o["type"])
        {
            case "vn-speaking":
                _vnSpeaking = (bool?)o["on"] ?? false;
                break;
            case "sfx":
            {
                if (_vnSpeaking) break;   // VN owns the mix - stingers stay silent while she speaks
                var name = (string?)o["name"];
                var scale = (float?)o["scale"] ?? 0.6f;
                // Two cues live behind fallback-resolving helpers (no dedicated asset yet).
                if (name == "wave_clear") ChaosSfx.PlayWaveClear();
                else if (name == "ripple_cast") ChaosSfx.PlayRippleCast();
                else if (!string.IsNullOrEmpty(name)) ChaosSfx.Play(name, scale);
                break;
            }
            case "fire-payload":
                FirePayload(o);
                break;
            case "freeze-state":
                ApplyWorldFreeze((bool?)o["on"] ?? false);
                break;
            case "mute-state":
                ApplyDiveMute((bool?)o["on"] ?? false);
                break;
            case "meta-command":
                _meta?.Handle(o);
                break;
            case "request-run":
                OnRequestRun(o);
                break;
            case "run-started":
            {
                _runActive = true;
                _vnSpeaking = false;   // never carry a stale duck into a run
                ApplyWorldFreeze(false);   // a stale freeze from a crashed prior run must not bleed into this descent's dedup state
                ResetRunMetrics();     // fresh native engagement counters for this descent
                var diff = (string?)o["difficulty"] ?? "Gentle";
                if (!_testMode)
                {
                    try { ChaosCrashSentinel.Mark($"mode=dtrh-web diff={diff}"); } catch { }
                    try { App.Bark?.NotifyChaosRunStarted(diff); } catch { }
                }
                try { Haptics.DtrhHapticDirector.OnRunStarted(); } catch { }
                App.Logger?.Information("DtrhHost: run started (diff={D}, mode={M})", diff, (string?)o["mode"]);
                break;
            }
            case "run-ended":
                OnRunEnded(o);
                break;
            case "bark":
                // Haptics tap FIRST: the moment happened whether or not a voice line plays
                // over it (the vn-speaking gate below only guards the audio mix).
                try { Haptics.DtrhHapticDirector.OnGameEvent(o); } catch { }
                if (_vnSpeaking) break;   // don't let a bark talk over her VN line
                RouteBark(o);
                break;
            case "haptic-state":
                // page's ~2s depth/melt feed for the haptic director's ambient floor
                try { Haptics.DtrhHapticDirector.OnHapticState(o); } catch { }
                break;
            case "heartbeat":
                _lastHeartbeatUtc = DateTime.UtcNow;
                break;
            case "asset-stats":
                // per-asset engagement delta (weighted attention + paddle interactions);
                // summed into dtrh_asset_stats.json for future media-selection features
                try { DtrhAssetStatsStore.Merge(o); } catch { }
                break;
            case "loom-save":
            {
                // THE LOOM (crafting Part 2): the pane wove a GIF; store validates + writes
                // into the CCP Spirals library, then the page gets the verdict + fresh list.
                var (ok, slug, error) = DtrhLoomStore.Save(
                    (string?)o["name"], (string?)o["gifBase64"], o["params"], (bool?)o["overwrite"] ?? false);
                _host?.Post(new { type = "loom-result", op = "save", ok, slug, error });
                if (ok) PostLoomList();
                break;
            }
            case "loom-delete":
            {
                var slug = (string?)o["slug"];
                var (ok, error) = DtrhLoomStore.Delete(slug);
                _host?.Post(new { type = "loom-result", op = "delete", ok, slug, error });
                if (ok) PostLoomList();
                break;
            }
            case "loom-reveal":
            {
                // 📂 on a rack tile: show the saved gif in Explorer (the game runs
                // fullscreen, so Explorer opens behind it - still there on alt-tab).
                var path = DtrhLoomStore.GifPathFor((string?)o["slug"]);
                if (path != null)
                {
                    try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); }
                    catch (Exception ex) { App.Logger?.Debug("DtrhHost: reveal failed: {E}", ex.Message); }
                }
                break;
            }
            case "boot-error":
                OnBootError((string?)o["msg"]);
                break;
            case "report-bug":   // the hub dock's 🐛 button (no title bar to hang one on)
                OpenBugReport();
                break;
            case "fullscreen-set":   // page's Esc ladder / dock button: C# owns the borderless toggle
                ApplyHostFullscreen((bool?)o["on"] ?? false);
                break;
            case "exit":       // page-initiated (Esc held): it winds itself down, then exit-done
                _exiting = true;
                ArmExitWatchdog();
                break;
            case "exit-done":
                DisposeAll();
                break;
            case "pong":
                _lastHeartbeatUtc = DateTime.UtcNow;
                break;
        }
    }

    /// <summary>THE LOOM: post the saved-spiral library (page-ready + after save/delete).
    /// Params sidecars ride along so the pane can re-edit a kept spiral.</summary>
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
                    @params = TryParseJson(s.ParamsJson),
                }),
            });
        }
        catch (Exception ex) { App.Logger?.Debug("DtrhHost.PostLoomList: {E}", ex.Message); }
    }

    private static JObject? TryParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JObject.Parse(json); } catch { return null; }
    }

    /// <summary>Page-driven fullscreen: the game asks C# to borderless-toggle its own
    /// window (we do NOT use the browser Fullscreen API, which would hijack Esc). Echo the
    /// resulting window state back so the page's dock button + Esc ladder stay in sync.</summary>
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
            catch (Exception ex) { App.Logger?.Debug("DtrhHost.fullscreen: {E}", ex.Message); }
        });
    }

    /// <summary>The hub's dock 🐛 button: this window has a native (buttonless) title bar,
    /// so the bug affordance lives in-game and posts here. Opens the same modal dialog the
    /// rest of the app uses (MainWindow's title-bar 🐛), owned by the DtRH window so it sits
    /// on top even in fullscreen.</summary>
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
            catch (Exception ex) { App.Logger?.Warning("DtrhHost.report-bug: {E}", ex.Message); }
        });
    }

    /// <summary>request-run {setup?} -> persist the hub's Descent-tab choices into AppSettings
    /// (the same fields ChaosHubWindow.SaveToSettings writes, so WPF and web share one setup),
    /// then deal a fresh run config: the scripted first-descent classroom when RunsCompleted == 0
    /// (ChaosHappyPath.BuildFirstRunConfig - naked, gentle, treats only), else FromSettings with
    /// the loadout pre-applied. Answered with {type:'run-config'}.</summary>
    private static void OnRequestRun(JObject o)
    {
        try
        {
            if (o["setup"] is JObject setup) PersistRunSetup(setup);
            bool force = !_testMode && ChaosMeta.State.ForceScriptedRun;
            bool scripted = !_testMode && (ChaosMeta.State.RunsCompleted == 0 || force);
            var cfg = scripted ? ChaosHappyPath.BuildFirstRunConfig() : ChaosRunConfig.FromSettings();
            if (force)
            {
                // One-shot spent at DEAL time: the recap's "fall again" re-requests a config,
                // and a run-end clear has too many exit paths (watchdog, crash) - any missed
                // one would deal a second classroom. Persist + rebroadcast so JS agrees.
                ChaosMeta.State.ForceScriptedRun = false;
                ChaosMeta.Save();
                try { _meta?.Rebroadcast(); } catch { }
            }
            _host?.Post(new { type = "run-config", runConfig = BuildRunConfig(cfg) });
            App.Logger?.Information("DtrhHost: dealt run config (diff={D}, scripted={S})", cfg.Difficulty, scripted);
        }
        catch (Exception ex) { App.Logger?.Warning("DtrhHost.OnRequestRun: {E}", ex.Message); }
    }

    /// <summary>The web-relevant subset of ChaosHubWindow.SaveToSettings. Absent fields are
    /// left untouched; the reveal clamps stay C#-side (FromSettings), so a stale page can
    /// never write itself past a gate.</summary>
    private static void PersistRunSetup(JObject setup)
    {
        var s = App.Settings?.Current;
        if (s == null) return;
        if (setup["difficulty"] != null) s.ChaosDifficulty = (string?)setup["difficulty"] ?? s.ChaosDifficulty;
        if (setup["durationSec"] != null)
        {
            // The Hourglass unlock lifts the ceiling to 2h; without it the preset ceiling holds.
            int durMax = ChaosMeta.IsOwned("custom_duration") ? 7200 : 1200;
            s.ChaosRunDurationSec = Math.Clamp((int?)setup["durationSec"] ?? 960, 60, durMax);
        }
        if (setup["waveCount"] != null) s.ChaosWaveCount = Math.Clamp((int?)setup["waveCount"] ?? 5, 1, 12);
        // The Bottomless Fall: the endless toggle only sticks for owners (a stale page can't
        // arm it without the unlock); FromSettings re-checks ownership at deal time too.
        if (setup["endless"] != null) s.ChaosEndless = ((bool?)setup["endless"] ?? false) && ChaosMeta.IsOwned("endless_mode");
        if (setup["motion"] != null) s.ChaosMotionMode = (string?)setup["motion"] ?? s.ChaosMotionMode;
        if (setup["enabledVariants"] != null)
        {
            s.ChaosEnabledVariants = setup["enabledVariants"]!.Type == JTokenType.Null
                ? null
                : setup["enabledVariants"]!.ToObject<List<string>>();
        }
        if (setup["effectIntensity"] != null) s.ChaosEffectIntensity = Math.Clamp((double?)setup["effectIntensity"] ?? 0.85, 0.2, 1.5);
        if (setup["colorFlashes"] != null) s.ChaosColorFlashesEnabled = (bool?)setup["colorFlashes"] ?? true;
        if (setup["boonDraftEnabled"] != null) s.ChaosBoonDraftEnabled = (bool?)setup["boonDraftEnabled"] ?? true;
        if (setup["allowCurses"] != null) s.ChaosAllowCurses = (bool?)setup["allowCurses"] ?? true;
        if (setup["dartersEnabled"] != null) s.ChaosDartersEnabled = (bool?)setup["dartersEnabled"] ?? true;
        if (setup["key1"] != null) s.ChaosAccessoryKey1 = (string?)setup["key1"] ?? s.ChaosAccessoryKey1;
        if (setup["key2"] != null) s.ChaosAccessoryKey2 = (string?)setup["key2"] ?? s.ChaosAccessoryKey2;
    }

    /// <summary>The saved Descent-tab choices for the hub's setup UI (raw settings, NOT the
    /// clamped run values - the hub applies its own reveal gating visually, and FromSettings
    /// clamps again at deal time).</summary>
    private static object BuildRunSetup()
    {
        try
        {
            var s = App.Settings?.Current;
            return new
            {
                difficulty = s?.ChaosDifficulty ?? "Easy",
                durationSec = s?.ChaosRunDurationSec ?? 180,
                endless = s?.ChaosEndless ?? false,
                waveCount = s?.ChaosWaveCount ?? 5,
                motion = s?.ChaosMotionMode ?? "Mixed",
                enabledVariants = s?.ChaosEnabledVariants,
                effectIntensity = s?.ChaosEffectIntensity ?? 0.85,
                colorFlashes = s?.ChaosColorFlashesEnabled ?? true,
                boonDraftEnabled = s?.ChaosBoonDraftEnabled ?? true,
                allowCurses = s?.ChaosAllowCurses ?? true,
                dartersEnabled = s?.ChaosDartersEnabled ?? true,
                key1 = s?.ChaosAccessoryKey1 ?? "Q",
                key2 = s?.ChaosAccessoryKey2 ?? "E",
            };
        }
        catch { return new { difficulty = "Easy", durationSec = 180, waveCount = 5 }; }
    }

    /// <summary>fire-payload {kind, strength?, durationMult?} -> a native effect.
    /// Since the 2026-07 hard cutover the browser game renders every VISUAL effect in-world
    /// (dtrh/game/payloadFx.js) - no more native WPF layered windows over the game surface.
    /// Only the two non-visual / native-by-necessity kinds still ride this bridge:
    ///   - video: a mandatory covering video window (in-world port is separate future work)
    ///   - audio: a one-shot whisper on the native audio path
    /// Any other kind arriving here means a page/host version mismatch - log and ignore.</summary>
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
            {
                payload = EffectPayloadFactory.Build(EffectBubblePayloadKind.Audio);
                if (_runActive) _runSubliminalsHeard++;   // native whisper = a subliminal heard
            }
            else
            {
                // Visual effects are in-world now; the page should never send them here.
                App.Logger?.Warning("DtrhHost: payload kind '{K}' is in-world since the cutover - ignored", kindStr);
                return;
            }

            payload.Strength = Math.Clamp((int?)o["strength"] ?? 60, 0, 100);
            payload.DurationMult = Math.Clamp((double?)o["durationMult"] ?? 1.0, 0.1, 10.0);
            payload.Fire();
            App.Logger?.Information("DtrhHost: fired native payload {K} (strength {S})", payload.DisplayName, payload.Strength);
        }
        catch (Exception ex) { App.Logger?.Warning("DtrhHost.FirePayload: {E}", ex.Message); }
    }

    /// <summary>run-ended -> XP payout (C#-owned formula, identical to the WPF EndRun) +
    /// meta banking via the shared AwardRunRewards, answered with payout-result.</summary>
    private static void OnRunEnded(JObject o)
    {
        _runActive = false;
        try { Haptics.DtrhHapticDirector.OnRunEnded(); } catch { }
        ApplyWorldFreeze(false);   // a run ending mid-freeze must resume native video + voice, not wedge them through the hub
        try
        {
            double score = (double?)o["score"] ?? 0;
            double durationSec = Math.Max(1, (double?)o["durationSec"] ?? 60);
            double elapsedSec = Math.Clamp((double?)o["elapsedSec"] ?? durationSec, 0, durationSec * 2);
            double diffMult = Math.Clamp((double?)o["difficultyMult"] ?? 1.0, 0.5, 5.0);
            double sparkGainMult = Math.Clamp((double?)o["sparkGainMult"] ?? 1.0, 0.5, 5.0);
            string diff = (string?)o["difficulty"] ?? "Gentle";

            double durMin = durationSec / 60.0;
            double capBase = 250.0 * durMin * diffMult;
            double baseXp = Math.Min(score, capBase);
            double skillMult = App.SkillTree?.GetTotalXpMultiplier() ?? 1.0;
            double finalXp = baseXp * skillMult;

            long previousBest = _meta?.TestMode == true ? 0 : ChaosMeta.State.BestScore;

            int sparksEarned = 0;
            if (_meta != null)
            {
                sparksEarned = _meta.AwardRun(new ChaosMeta.ChaosRunRewardInput(
                    RunDurationSec: durationSec,
                    DifficultyMult: diffMult,
                    SparkGainMult: sparkGainMult,
                    Score: score,
                    TrickleDrops: (double?)o["trickleDrops"] ?? 0,
                    DripFeedMaxed: (bool?)o["dripFeedMaxed"] ?? false,
                    BestCombo: (int?)o["bestCombo"] ?? 0,
                    Defused: (int?)o["defused"] ?? 0,
                    ElapsedSec: elapsedSec));
            }

            ChaosRank? rankUp = null;
            if (!_testMode)
            {
                try { App.Progression?.AddXP(baseXp, XPSource.Chaos); }
                catch (Exception ex) { App.Logger?.Debug("DtrhHost payout AddXP: {E}", ex.Message); }
                // Restore V1 behavior: a run's popped bubbles feed the GLOBAL bubble
                // count and its per-100 sparkle-point milestones. The web port had been
                // recording bubblesPopped only into the local stats store; this credits
                // it to the same sink the native chaos mode uses. Additive to score XP.
                try
                {
                    int bubblesPopped = (int?)(o["sessionStats"]?["bubblesPopped"]) ?? 0;
                    if (bubblesPopped > 0) App.Achievements?.TrackBubblesPopped(bubblesPopped);
                }
                catch (Exception ex) { App.Logger?.Debug("DtrhHost bubble credit: {E}", ex.Message); }
                try { RevealService.Sync("run_end"); } catch { }
                try
                {
                    var nowRank = ChaosRanks.For(ChaosMeta.State.RunsCompleted);
                    if ((int)nowRank > ChaosMeta.State.LastRankSeen) rankUp = nowRank;
                }
                catch { }
                try { App.Bark?.NotifyChaosRunCompleted((int)finalXp, diff); } catch { }
                try { ChaosCrashSentinel.Clear(); } catch { }
                // RevealService.Sync mutated pendingReveals BEHIND the bridge - push a fresh
                // snapshot so the Warren's flash pass sees the new pendings on return.
                try { _meta?.Rebroadcast(); } catch { }
            }

            // Local-only session telemetry: fold the host-measured natives + payout into the
            // page's sessionStats snapshot and sum it into the cumulative store. Best-effort,
            // never sent to the server. (No recap UI reads this yet - that ships with the
            // unlock-gated recap card later; we just accumulate the data now.)
            try
            {
                var js = o["sessionStats"] as JObject ?? new JObject();
                js["videoWatchSec"]    = _runVideoWatchSec;
                js["videosShown"]      = _runVideosShown;
                js["videosSkipped"]    = _runVideosSkipped;
                js["voicelinesHeard"]  = _runVoicelines;
                js["voiceoverSec"]     = _runVoiceoverSec;
                js["subliminalsHeard"] = _runSubliminalsHeard;
                js["sparksEarned"]     = sparksEarned;
                js["xpEarned"]         = finalXp;
                var life = DtrhSessionStatsStore.Record(js, diff);
                App.Logger?.Information(
                    "DtrhHost: session metrics recorded (run #{Runs}): {Bubbles} bubbles, {Boons} boons, {VidSec:0}s video, {VoxSec:0}s voice",
                    life.Runs, life.BubblesPopped, life.BoonsReceived, life.VideoWatchSec, life.VoiceoverSec);
            }
            catch (Exception ex) { App.Logger?.Debug("DtrhHost session-metrics record: {E}", ex.Message); }

            _host?.Post(new
            {
                type = "payout-result",
                baseXp,
                skillMult,
                finalXp,
                sparksEarned,
                previousBest,
                rankUp = rankUp?.ToString(),
                dryRun = _testMode,
            });
            App.Logger?.Information(
                "DtrhHost: web run complete: base {Base:0} x skill {Mult:0.0} = {Final:0} XP, {Sparks} sparks{T}",
                baseXp, skillMult, finalXp, sparksEarned, _testMode ? " (TEST, no XP credited)" : "");
        }
        catch (Exception ex) { App.Logger?.Warning("DtrhHost.OnRunEnded: {E}", ex.Message); }
    }

    /// <summary>bark {event, ...} -> the matching App.Bark chaos hook (voice stays native).
    /// The FULL M4 surface: the page mirrors every WPF Notify* call site; BarkService's
    /// own cooldown/weighting logic decides what actually speaks.</summary>
    private static void RouteBark(JObject o)
    {
        if (_testMode) return;
        try
        {
            var bark = App.Bark;
            if (bark == null) return;
            string S(string k, string d = "") => (string?)o[k] ?? d;
            int I(string k) => (int?)o[k] ?? 0;
            double D(string k) => (double?)o[k] ?? 0;
            switch ((string?)o["event"])
            {
                case "ending-soon": bark.NotifyChaosEndingSoon(); break;
                case "wave-cleared": bark.NotifyChaosWaveCleared(I("wave")); break;
                case "wave-escalated": bark.NotifyChaosWaveEscalated(I("wave")); break;
                case "act-changed": bark.NotifyChaosActChanged(I("act"), I("wave")); break;
                case "benign-popped": bark.NotifyChaosBenignPopped(S("variant"), S("payload"), I("combo")); break;
                case "defused": bark.NotifyChaosBubbleDefused(I("combo"), S("variant"), S("difficulty")); break;
                case "detonated": bark.NotifyChaosBubbleDetonated(S("variant"), D("strength"), D("runDetonations"), I("combo"), S("difficulty")); break;
                case "detonated-absorbed": bark.NotifyChaosBubbleDetonatedAbsorbed(S("variant"), D("strength"), D("runDetonations"), I("combo"), S("difficulty"), I("shields")); break;
                case "darter-caught": bark.NotifyChaosDarterCaught(D("points"), I("combo"), (bool?)o["quick"] ?? false); break;
                case "freeze-caught": bark.NotifyChaosFreezeCaught(D("points"), I("combo")); break;
                case "combo-milestone": bark.NotifyChaosComboMilestone(I("combo"), S("difficulty")); break;
                case "combo-big": bark.NotifyChaosComboBig(I("combo"), D("threshold")); break;
                case "boon-picked": bark.NotifyChaosBoonPicked(S("name")); break;
                case "curse-picked": bark.NotifyChaosCursePicked(S("name"), S("rarity"), D("mult")); break;
                case "boon-skipped": bark.NotifyChaosBoonSkipped(I("shields")); break;
                case "draft-autopick": bark.NotifyChaosDraftAutopick(); break;
                case "focus-low": bark.NotifyChaosFocusLow(); break;
                case "defuse-first": bark.NotifyChaosDefuseFirst(); break;
                case "defuse-nofocus": bark.NotifyChaosDefuseNoFocus(); break;
                case "defuse-release": bark.NotifyChaosDefuseRelease(); break;
                case "click-detonate": bark.NotifyChaosClickDetonate(); break;
                case "tease-debut": bark.NotifyChaosTeaseDebut(); break;
                case "tease-clicked": bark.NotifyChaosTeaseClicked(); break;
                case "tease-denied": bark.NotifyChaosTeaseDenied(I("count")); break;
                case "tease-denied-streak": bark.NotifyChaosTeaseDeniedStreak(I("count")); break;
                case "gold-first": bark.NotifyChaosGoldFirst(); break;
                // ---- M5: Warren hub + lessons + happy path ----
                case "dollhouse-first-open": bark.NotifyChaosDollhouseFirstOpen(); break;
                case "reveal-flash": bark.NotifyChaosRevealFlash(S("id")); break;
                case "lesson-complete": bark.NotifyChaosLessonComplete(S("id")); break;
                case "duo-demo": bark.NotifyChaosDuoDemo(); break;
                // ---- Wave 2: tunnel pickups (reuse the rabbit-catch voice) ----
                case "rabbit-caught": bark.NotifyChaosDarterCaught(I("gold"), 0, true); break;
                // ---- Crafting: THE BOUDOIR (reuse the first-time voice) ----
                case "crafted": bark.NotifyChaosFirstTime(S("id")); break;
                default: App.Logger?.Debug("DtrhHost: unrouted bark event '{E}'", (string?)o["event"]); break;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("DtrhHost.RouteBark: {E}", ex.Message); }
    }

    // ============================ world freeze ============================

    /// <summary>freeze-state {on} - the in-world Freeze bubble stops the field, so stop the
    /// REAL world with it: pause any covering video and the currently-playing spoken voiceline
    /// for the freeze window, resuming both when it lifts. Idempotent (dedup on _worldFrozen);
    /// also force-resumed on run end / teardown so a clip can never wedge paused.</summary>
    /// <summary>mute-state {on} - the dive's in-page master mute is a WEB switch that can
    /// only silence page media; a mandatory native video that covers the page never heard it
    /// and faded its audio in anyway. Mirror the mute onto the native VideoService so one
    /// control silences everything. Idempotent; force-released on run end / teardown so a
    /// muted descent can never leave every video silent afterward.</summary>
    private static void ApplyDiveMute(bool on)
    {
        if (on == _diveMuted) return;
        _diveMuted = on;
        var disp = Application.Current?.Dispatcher;
        if (disp == null) return;
        disp.BeginInvoke(() =>
        {
            try { App.Video?.SetExternalMute(on); }
            catch (Exception ex) { App.Logger?.Debug("DtrhHost.ApplyDiveMute: {E}", ex.Message); }
        });
    }

    private static void ApplyWorldFreeze(bool on)
    {
        if (on == _worldFrozen) return;
        _worldFrozen = on;
        try { Haptics.DtrhHapticDirector.OnWorldFreeze(on); } catch { }
        var disp = Application.Current?.Dispatcher;
        if (disp == null) return;
        disp.BeginInvoke(() =>
        {
            try
            {
                if (on)
                {
                    App.Video?.PausePrimary();
                    App.AvatarWindow?.PauseSpokenAudio();
                }
                else
                {
                    App.Video?.PlayPrimary();
                    App.AvatarWindow?.ResumeSpokenAudio();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("DtrhHost.ApplyWorldFreeze: {E}", ex.Message); }
        });
    }

    // ============================ native payload state ============================

    /// <summary>A mandatory video fully covers the game - tell the page (pause/duck) and
    /// give it Win32 focus back when the video closes (video clicks steal activation).</summary>
    private static void HookVideoEvents(bool on)
    {
        try
        {
            if (App.Video == null) return;
            if (on && !_videoHooked)
            {
                App.Video.VideoStarted += OnVideoStarted;
                App.Video.VideoEnded += OnVideoEnded;
                App.Video.VideoWatchCredited += OnVideoWatchCredited;
                _videoHooked = true;
            }
            else if (!on && _videoHooked)
            {
                App.Video.VideoStarted -= OnVideoStarted;
                App.Video.VideoEnded -= OnVideoEnded;
                App.Video.VideoWatchCredited -= OnVideoWatchCredited;
                _videoHooked = false;
            }
        }
        catch { }
    }

    private static void OnVideoStarted(object? sender, EventArgs e)
    {
        if (_runActive) _runVideosShown++;   // session telemetry: a video was shown this run
        try { Haptics.DtrhHapticDirector.OnVideoCovering(true); } catch { }
        var disp = Application.Current?.Dispatcher;
        if (disp == null || _host == null) return;
        disp.BeginInvoke(() => _host?.Post(new { type = "payload-state", kind = "video", on = true }));
    }

    /// <summary>The video path finished crediting its watched time (fires from VideoService's
    /// interruption-safe credit finalizer). Accumulate the real watched seconds and, when the
    /// user bailed well before the end, count it as a skip. Local-only session telemetry.</summary>
    private static void OnVideoWatchCredited(object? sender, VideoWatchInfoEventArgs e)
    {
        if (!_runActive) return;
        _runVideoWatchSec += Math.Max(0, e.WatchedSec);
        if (e.DurationSec > 0 && e.WatchedSec / e.DurationSec < 0.90) _runVideosSkipped++;
    }

    /// <summary>Zero the per-run native engagement counters (called on run-started).</summary>
    private static void ResetRunMetrics()
    {
        _runVideoWatchSec = 0;
        _runVideosShown = 0;
        _runVideosSkipped = 0;
        _runVoicelines = 0;
        _runVoiceoverSec = 0;
        _runSubliminalsHeard = 0;
    }

    /// <summary>A companion voiceline played its audio (called from BarkService.Speak with the
    /// clip's duration in seconds). No-ops unless a DtRH run is active, so instrumenting the
    /// global bark path costs nothing off the game. Local-only session telemetry.</summary>
    public static void NoteVoicelineHeard(double sec)
    {
        if (!_runActive) return;
        _runVoicelines++;
        if (sec > 0) _runVoiceoverSec += sec;
    }

    private static void OnVideoEnded(object? sender, EventArgs e)
    {
        try { Haptics.DtrhHapticDirector.OnVideoCovering(false); } catch { }
        var disp = Application.Current?.Dispatcher;
        if (disp == null || _host == null) return;
        disp.BeginInvoke(() =>
        {
            _host?.Post(new { type = "payload-state", kind = "video", on = false });
            _host?.FocusWeb();   // the video window had Win32 focus; reclaim keyboard for the game
        });
    }

    /// <summary>The page's 3D boot failed (a genuine WebGL/GPU failure - the page only
    /// reports boot-error after the engine import or renderer creation threw). Fall back
    /// to the classic WPF game for the rest of the session so the user's click still
    /// lands in a game.</summary>
    private static void OnBootError(string? msg)
    {
        App.Logger?.Warning("DtrhHost: page boot-error: {Msg} - falling back to the classic game this session", msg);
        BootFailedThisSession = true;
        bool wasTest = _testMode;
        var disp = Application.Current?.Dispatcher;
        if (disp == null) { DisposeAll(); return; }
        disp.BeginInvoke(() =>
        {
            DisposeAll();
            if (!wasTest) LaunchLegacyFallback();
        });
    }

    /// <summary>Mirror of the Lab card's legacy branch (MainWindow.Lab.cs) for the
    /// boot-error path: scripted first run when fresh, else the WPF hub.</summary>
    private static void LaunchLegacyFallback()
    {
        try
        {
            if (App.Chaos == null || App.Chaos.IsRunning) return;
            if (ChaosMeta.State.RunsCompleted == 0)
            {
                App.Chaos.StartRun(ChaosHappyPath.BuildFirstRunConfig());
                return;
            }
            if (ChaosHubWindow.Current != null) { ChaosHubWindow.Current.Activate(); return; }
            var hub = new ChaosHubWindow();
            if (Application.Current?.MainWindow is Window mw && mw.IsLoaded) hub.Owner = mw;
            hub.Show();
        }
        catch (Exception ex) { App.Logger?.Error(ex, "DtrhHost: legacy fallback failed"); }
    }

    // ============================ watchdogs / recovery ============================

    private static void StartHeartbeatWatch()
    {
        StopHeartbeatWatch();
        _lastHeartbeatUtc = DateTime.UtcNow;
        _heartbeatWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _heartbeatWatch.Tick += (_, _) =>
        {
            // Guarded on IsReady: the page only starts beating (rAF, ~2s) after boot, so a
            // still-loading page can't false-trip. A wedged live RUN gets the fast ladder;
            // the idling Warren gets a lazier one - but it must exist, because a locked
            // page main thread also kills the JS Esc-hold exit and would otherwise trap
            // the user under a fullscreen topmost window (panic key aside).
            if (_host == null || !_host.IsReady || _exiting) return;
            double silent = (DateTime.UtcNow - _lastHeartbeatUtc).TotalSeconds;
            double limit = _runActive ? 10 : 20;
            if (silent > limit)
            {
                App.Logger?.Warning("DtrhHost: page heartbeat silent >{Limit}s ({Where}) - recovering",
                    limit, _runActive ? "mid-run" : "hub");
                Recover("heartbeat-silent");
            }
        };
        _heartbeatWatch.Start();
    }

    private static void StopHeartbeatWatch()
    {
        try { _heartbeatWatch?.Stop(); } catch { }
        _heartbeatWatch = null;
    }

    private static void OnProcessFailed(CoreWebView2ProcessFailedKind kind)
    {
        Recover($"process-failed:{kind}");
    }

    /// <summary>Recovery ladder: relaunch once per session (mid-run state is lost - the
    /// sentinel records the abnormal end); a second failure gives up cleanly.</summary>
    private static void Recover(string reason)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null) { DisposeAll(); return; }
        disp.BeginInvoke(() =>
        {
            bool retry = !_relaunchedOnce;
            bool wasTest = _testMode;
            App.Logger?.Warning("DtrhHost: recovery ({Reason}) - {Action}", reason, retry ? "relaunching once" : "giving up");
            DisposeAll();
            if (retry)
            {
                _relaunchedOnce = true;
                Launch(wasTest);
            }
        });
    }

    // ============================ teardown ============================

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

    private static void DisposeAll()
    {
        CancelExitWatchdog();
        StopHeartbeatWatch();
        HookVideoEvents(false);
        // Never leave the toy running after the window dies (crash, watchdog, clean exit alike).
        try { Haptics.DtrhHapticDirector.OnClosed(); } catch { }
        // Never leave a video or voiceline wedged paused if the window dies mid-freeze.
        if (_worldFrozen) { _worldFrozen = false; try { App.Video?.PlayPrimary(); } catch { } try { App.AvatarWindow?.ResumeSpokenAudio(); } catch { } }
        // ...and never leave every video silent because the dive ended while muted.
        if (_diveMuted) { _diveMuted = false; try { App.Video?.SetExternalMute(false); } catch { } }
        try { _meta?.FlushSave(); } catch { }
        if (_runActive && !_testMode)
        {
            // The window died mid-run (crash/force close): leave the sentinel armed only for
            // genuine process death; a deliberate close is a clean end.
            try { ChaosCrashSentinel.Clear(); } catch { }
        }
        _runActive = false;
        try { _host?.Dispose(); } catch { }
        _host = null;
        _meta = null;
        _exiting = false;
        // Bring the main CCP window back from the tray if we minimized it at launch.
        if (_minimizedMainWindow)
        {
            _minimizedMainWindow = false;
            try { (Application.Current?.MainWindow as MainWindow)?.ShowFromTray(); } catch { }
        }
        App.Logger?.Information("DtrhHostService: closed");
    }

    /// <summary>
    /// The run knobs the page's game brain needs, snapshotted from the SAME
    /// <see cref="ChaosRunConfig.FromSettings"/> the WPF game uses - so rank clamps
    /// (difficulty/variants) and owned-upgrade multipliers (fuse/base/spark/spawn-rate)
    /// carry over and the two games score identically at identical settings.
    ///
    /// M4: the LOADOUT is pre-applied here exactly like the WPF BeginRun - a
    /// <see cref="ChaosRunState"/> is built and <see cref="ChaosMeta.ApplyLifetimeBoons"/>
    /// runs over it, then the resulting knob values are snapshotted. The page never
    /// re-implements a boon's Apply lambda, so the math can never drift.
    /// </summary>
    private static object BuildRunConfig(ChaosRunConfig cfg)
    {
        try
        {
            // Grab-in-the-tube rework: the run no longer ships a pre-applied loadout. Every
            // accessory/charm/toy is discovered + grabbed in the fall and applied there (JS
            // game/boonPassives.js), so this only ships each item's current LEVEL + the
            // consumable-slot count. Habits stay always-on and arrive as cfg.* knobs below.
            var s = App.Settings?.Current;
            var meta = ChaosMeta.State;

            // Intrusive Thoughts' phrase pool (the user's enabled bouncing-text lines).
            var thoughts = new List<string>();
            try
            {
                var pool = s?.BouncingTextPool;
                if (pool != null)
                    foreach (var kv in pool) if (kv.Value) thoughts.Add(kv.Key);
            }
            catch { }

            return new
            {
                difficulty = cfg.Difficulty.ToString(),
                difficultyMult = cfg.DifficultyMult,
                durationSec = cfg.DurationSec,
                waveCount = cfg.WaveCount,
                endless = cfg.Endless,   // The Bottomless Fall: no clock; regions loop + deepen until wake
                effectIntensity = cfg.EffectIntensity,
                enabledVariants = cfg.EnabledVariants,
                motionOverride = cfg.MotionOverride?.ToString(),
                fuseTimeMult = cfg.FuseTimeMult,
                baseMult = cfg.BaseMult,   // golden_touch writes Config.BaseMult during Apply
                sparkGainMult = cfg.SparkGainMult,
                spawnRateMult = cfg.SpawnRateMult,
                colorFlashes = cfg.ColorFlashesEnabled,
                screenShake = cfg.ScreenShakeEnabled,

                // ---- M4: run-shape knobs ----
                boonDraftEnabled = cfg.BoonDraftEnabled,
                allowCurses = cfg.AllowCurses,
                dartersEnabled = cfg.DartersEnabled,
                draftChoices = cfg.DraftChoices,
                draftAutoResumeSec = cfg.DraftAutoResumeSec,
                sinChance = cfg.SinChance,
                scriptedFirstRun = cfg.ScriptedFirstRun,
                hitboxScale = cfg.HitboxScale,
                magnetEnabled = cfg.MagnetEnabled,
                popupHeartEnabled = cfg.PopupHeartEnabled,
                pendulumSwing = cfg.PendulumSwing,
                // The persistent habits (Warren upgrades) that actually shape this run,
                // surfaced in the left-rail HUD (the drafted modifiers already ride the
                // top ribbon). extreme_tier only unlocks a difficulty tier - no in-run
                // effect - so it's filtered out.
                ownedHabitIds = meta.PurchasedUpgrades
                    .Where(id => ChaosMeta.IsUpgradeActive(id)
                                 && id != "extreme_tier"
                                 && id != "custom_duration"   // setup-shape unlocks, no in-run
                                 && id != "endless_mode"      // effect -> keep them off the HUD rail
                                 && ChaosUpgrades.ById(id) != null)
                    .ToList(),
                rankIndex = (int)ChaosMeta.RankIndex,
                runsCompleted = meta.RunsCompleted,
                equippedStartBoon = meta.EquippedStartBoon,
                thoughtTexts = thoughts,

                // ---- grab-in-the-tube rework: per-item level + consumable-slot count.
                // A grab applies the item at its current dollhouse level (min 1). ----
                levels = meta.LifetimeBoonLevels ?? new Dictionary<string, int>(),
                consumableSlots = meta.ConsumableSlots,
                // Seeds the in-run first-discovery ledger so lesson cards fire once EVER,
                // not once per app-session (the JS `discovered` set is otherwise empty on boot).
                discoveredCodexIds = (meta.DiscoveredCodexIds ?? new HashSet<string>()).ToList(),

                // ---- crafting Part 2: the run reads its crafted kit from cfg, not live
                // meta, so a mid-run craft never retro-applies to the fall in progress ----
                craftedItems = meta.CraftedItems ?? new Dictionary<string, int>(),
                pinnedBoonId = meta.PinnedBoon,
                denialArmed = meta.DenialArmed,

                // ---- M4: seen-once flags (debuts + teaches), mirrored back one-way via set-flag ----
                flags = new
                {
                    seenDefuseTutorial = meta.SeenDefuseTutorial,
                    seenFocusTip = meta.SeenFocusTip,
                    seenHeatTeach = meta.SeenHeatTeach,
                    seenRippleTeach = meta.SeenRippleTeach,
                    seenEcho = meta.SeenEcho,
                    seenChaperone = meta.SeenChaperone,
                    seenTease = meta.SeenTease,
                    seenBound = meta.SeenBound,
                    seenBrittle = meta.SeenBrittle,
                    seenGoldFirst = meta.SeenGoldFirst,
                    seenBarkDefuseFirst = meta.SeenBarkDefuseFirst,
                    seenBarkDefuseNoFocus = meta.SeenBarkDefuseNoFocus,
                    seenBarkDefuseRelease = meta.SeenBarkDefuseRelease,
                    seenBarkClickDetonate = meta.SeenBarkClickDetonate,
                    // M5: happy-path beats (run 2 debut + the run-4 rigged first sin + duo demo)
                    seenBraindrain = meta.SeenBraindrain,
                    seenFirstSin = meta.SeenFirstSin,
                    seenDuoDemo = meta.SeenDuoDemo,
                },
            };
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("DtrhHost.BuildRunConfig: {E}", ex.Message);
            return new { difficulty = "Easy", difficultyMult = 1.0, durationSec = 180, waveCount = 5, effectIntensity = 0.85 };
        }
    }

    private static int SafeMasterVolume()
    {
        try { return App.Settings?.Current?.MasterVolume ?? 100; }
        catch { return 100; }
    }

    /// <summary>Active persona/mod id for the page's VN portrait. Defaults to the
    /// Sissy set when no mod is resolvable (that's the only baked portrait set today).</summary>
    private static string SafeActiveModId()
    {
        try { return App.Mods?.ActiveModId ?? "builtin-sissyhypno"; }
        catch { return "builtin-sissyhypno"; }
    }
}
