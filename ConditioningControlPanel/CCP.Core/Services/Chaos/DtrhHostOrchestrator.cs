using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// Portable orchestrator for the DTRH web game (the Avalonia head's WebView game window). A faithful
/// port of the WPF <c>DtrhHostService</c> (DtrhHostService.cs, 914L): it owns the inbound page-message
/// router, run/session lifecycle, XP banking, outbound message factories with a queue-until-page-ready
/// buffer, and the heartbeat/exit watchdogs. Everything head-native (real effects, native video/avatar
/// freeze, SFX, barks, reveal-sync, run-config construction, tray restore) is routed through
/// <see cref="IDtrhNativeEffects"/>; the browser surface is the <see cref="IBrowserHost"/> seam. Wired
/// by the head (S2c-2): construct, then attach to <c>IBrowserHost.WebMessageReceived</c> and the
/// head video events.
/// </summary>
public sealed class DtrhHostOrchestrator : IDisposable
{
    private const int Protocol = 1;   // WPF DtrhHostService.cs:30

    private readonly IBrowserHost _browser;
    private readonly IDtrhNativeEffects _fx;
    private readonly IChaosMetaStore _store;
    private readonly DtrhMetaBridge _meta;
    private readonly DtrhAssetManifest _manifest;
    private readonly DtrhAssetStatsStore _assetStats;
    private readonly DtrhSessionStatsStore _sessionStats;
    private readonly IProgressionService? _progression;
    private readonly ISkillTreeService? _skillTree;
    private readonly IAchievementService? _achievements;   // DtRH web-run bubble credit (WPF DtrhHostService.cs:469-474)
    private readonly IQuestService? _quests;               // DtRH web-run bubble credit (v6.3.1 parity)
    private readonly ChaosCrashSentinel? _sentinel;
    private readonly ISettingsService? _settings;
    private readonly ILogger<DtrhHostOrchestrator> _log;
    private readonly bool _testMode;

    // Outbound queue-until-page-ready (WPF: ChaosWebViewHost._pending/IsReady, moved into the orchestrator).
    private readonly List<string> _pending = new();
    private readonly object _pendingLock = new();
    private bool _isReady;

    // Run / session state.
    private bool _runActive;
    private bool _vnSpeaking;
    private bool _worldFrozen;
    private bool _exiting;
    private bool _disposed;
    private DateTime _lastHeartbeatUtc = DateTime.UtcNow;

    // Per-run session counters (host-authoritative; overwrite the page's sessionStats at run-end).
    private double _runVideoWatchSec;
    private int _runVideosShown;
    private int _runVideosSkipped;
    private int _runVoicelines;
    private double _runVoiceoverSec;
    private int _runSubliminalsHeard;

    // Watchdogs (System.Threading.Timer — Core has no DispatcherTimer; seam impls marshal to UI).
    private Timer? _heartbeatTimer;
    private Timer? _exitTimer;

    /// <summary>Raised when the orchestrator has fully torn down (timers stopped, meta saved, freeze
    /// resumed, main window restored). The head disposes the game window + browser in response.</summary>
    public event Action? Closed;

    /// <summary>Raised when a watchdog gives up on the current page (heartbeat silence / process fail).
    /// The head may relaunch a fresh orchestrator once. Fires immediately before <see cref="Closed"/>.</summary>
    public event Action<string>? RecoverRequested;

    public DtrhHostOrchestrator(
        IBrowserHost browser,
        IDtrhNativeEffects effects,
        IChaosMetaStore store,
        DtrhAssetManifest manifest,
        DtrhAssetStatsStore assetStats,
        DtrhSessionStatsStore sessionStats,
        IAppEnvironment environment,
        ILogger<DtrhHostOrchestrator> logger,
        ILogger<DtrhMetaBridge> bridgeLogger,
        IProgressionService? progression = null,
        ISkillTreeService? skillTree = null,
        IAchievementService? achievements = null,
        IQuestService? quests = null,
        ChaosCrashSentinel? sentinel = null,
        ISettingsService? settings = null,
        IBarkService? bark = null,
        bool testMode = false)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _fx = effects ?? throw new ArgumentNullException(nameof(effects));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _assetStats = assetStats ?? throw new ArgumentNullException(nameof(assetStats));
        _sessionStats = sessionStats ?? throw new ArgumentNullException(nameof(sessionStats));
        _log = logger ?? throw new ArgumentNullException(nameof(logger));
        _progression = progression;
        _skillTree = skillTree;
        _achievements = achievements;
        _quests = quests;
        _sentinel = sentinel;
        _settings = settings;
        _testMode = testMode;

        _meta = new DtrhMetaBridge(store, environment, bridgeLogger, Post, testMode, bark);
        _browser.WebMessageReceived += OnBrowserMessage;
    }

    /// <summary>Test/diagnostic access to the meta bridge this orchestrator drives.</summary>
    public DtrhMetaBridge Meta => _meta;

    // ---- inbound ---------------------------------------------------------------------------------

    private void OnBrowserMessage(object? sender, string json) => HandleMessage(json);

    /// <summary>Entry point for every inbound page message (raw JSON). Intercepts the transport-level
    /// <c>ready</c>/<c>log</c> types, then routes the rest through the switch. WPF <c>OnPageMessage</c>
    /// DtrhHostService.cs:204-278 (WPF handled ready/log one layer up in ChaosWebViewHost).</summary>
    public void HandleMessage(string json)
    {
        if (_disposed || string.IsNullOrWhiteSpace(json)) return;
        JObject o;
        try { o = JObject.Parse(json); }
        catch { return; }
        var type = (string?)o["type"];
        if (type == "ready") { OnPageReady(); return; }
        if (type == "log") { _log.LogDebug("dtrh page: {Msg}", (string?)o["msg"]); return; }
        try { Route(type, o); }
        catch (Exception ex) { _log.LogWarning(ex, "DtrhHostOrchestrator: message '{Type}' failed", type); }
    }

    private void Route(string? type, JObject o)
    {
        switch (type)
        {
            case "vn-speaking":
                _vnSpeaking = (bool?)o["on"] ?? false;                 // :208-210
                break;
            case "sfx":                                                // :211-221
                if (_vnSpeaking) break;
                var name = (string?)o["name"];
                if (!string.IsNullOrEmpty(name))
                    _fx.PlaySfx(name, (float?)o["scale"] ?? 0.6f);
                break;
            case "fire-payload":                                       // :222-224
                FirePayload(o);
                break;
            case "freeze-state":                                       // :225-227
                ApplyWorldFreeze((bool?)o["on"] ?? false);
                break;
            case "meta-command":                                       // :228-230
                _meta.Handle(o);
                break;
            case "request-run":                                        // :231-233
                OnRequestRun(o);
                break;
            case "run-started":                                        // :234-248
                _runActive = true;
                _vnSpeaking = false;
                ApplyWorldFreeze(false);                               // :237 stale-freeze force-resume
                ResetRunMetrics();
                var diff = (string?)o["difficulty"] ?? "Gentle";
                if (!_testMode)
                {
                    _sentinel?.Mark($"mode=dtrh-web diff={diff}");     // :242
                    _fx.NotifyRunStarted(diff);                        // :243
                }
                break;
            case "run-ended":                                          // :249-251
                OnRunEnded(o);
                break;
            case "bark":                                               // :252-255
                if (_vnSpeaking || _testMode) break;
                _fx.RouteBark(o.ToString(Formatting.None));
                break;
            case "heartbeat":                                          // :256-258
            case "pong":                                               // :274-276
                _lastHeartbeatUtc = DateTime.UtcNow;
                break;
            case "asset-stats":                                        // :259-263
                try { _assetStats.Merge(o); } catch (Exception ex) { _log.LogDebug(ex, "asset-stats merge failed"); }
                break;
            case "boot-error":                                         // :264-266 — web-only ruling: NO WPF fallback
                _log.LogError("dtrh boot-error (WebGL/GPU init failed): {Msg}", (string?)o["msg"]);
                DisposeAll();
                break;
            case "fullscreen-set":                                     // :270-272 dock [ ] button + Esc ladder
                ApplyHostFullscreen((bool?)o["on"] ?? false);
                break;
            case "exit":                                               // :267-270
                _exiting = true;
                ArmExitWatchdog();
                break;
            case "exit-done":                                          // :271-273
                DisposeAll();
                break;
            // no default — unknown types are silently dropped (WPF parity)
        }
    }

    // ---- run lifecycle ---------------------------------------------------------------------------

    private void OnRequestRun(JObject o)   // WPF OnRequestRun DtrhHostService.cs:285-307
    {
        try
        {
            if (o["setup"] is JObject setup) PersistRunSetup(setup);
            bool force = !_testMode && _store.State.ForceScriptedRun;
            bool scripted = !_testMode && (_store.State.RunsCompleted == 0 || force);
            string cfgJson = _fx.BuildRunConfigJson(scripted);
            // ForceScriptedRun is spent HERE at deal time, not at run-end (WPF :294-301): run-end has too
            // many exit paths (watchdog, crash) that could miss the clear and deal a second classroom.
            if (force)
            {
                _store.State.ForceScriptedRun = false;
                _store.Save();
                _meta.Rebroadcast();
            }
            JToken cfg;
            try { cfg = JToken.Parse(string.IsNullOrWhiteSpace(cfgJson) ? "{}" : cfgJson); }
            catch { cfg = new JObject(); }
            Post(new { type = "run-config", runConfig = cfg });
            _log.LogInformation("DtrhHostOrchestrator: dealt run (scripted={Scripted})", scripted);
        }
        catch (Exception ex) { _log.LogWarning(ex, "DtrhHostOrchestrator: request-run failed"); }
    }

    private void OnRunEnded(JObject o)   // WPF OnRunEnded DtrhHostService.cs:400-496
    {
        try
        {
            _runActive = false;                                        // :402
            ApplyWorldFreeze(false);                                   // :403 resume a run that ended mid-freeze

            // XP (verbatim :406-417). All inputs come from the message except skillMult (progression).
            double score = (double?)o["score"] ?? 0;
            double durationSec = Math.Max(1, (double?)o["durationSec"] ?? 60);
            double elapsedSec = Math.Clamp((double?)o["elapsedSec"] ?? durationSec, 0, durationSec * 2);
            double diffMult = Math.Clamp((double?)o["difficultyMult"] ?? 1.0, 0.5, 5.0);
            double sparkGainMult = Math.Clamp((double?)o["sparkGainMult"] ?? 1.0, 0.5, 5.0);
            string diff = (string?)o["difficulty"] ?? "Gentle";
            double durMin = durationSec / 60.0;
            double capBase = 250.0 * durMin * diffMult;
            double baseXp = Math.Min(score, capBase);
            double skillMult = _skillTree?.GetTotalXpMultiplier() ?? 1.0;
            double finalXp = baseXp * skillMult;

            long previousBest = _meta.TestMode ? 0 : _store.State.BestScore;   // :419

            // (a) meta banking / AwardRun — banks+saves BEFORE the payout reply (:421-435).
            int sparksEarned = _meta.AwardRun(new ChaosRunRewardInput(
                RunDurationSec: durationSec,
                DifficultyMult: diffMult,
                SparkGainMult: sparkGainMult,
                Score: score,
                TrickleDrops: (double?)o["trickleDrops"] ?? 0,
                DripFeedMaxed: (bool?)o["dripFeedMaxed"] ?? false,
                BestCombo: (int?)o["bestCombo"] ?? 0,
                Defused: (int?)o["defused"] ?? 0,
                ElapsedSec: elapsedSec));

            ChaosRank? rankUp = null;
            if (!_testMode)   // non-test side effects, exact order (:437-453)
            {
                _progression?.AddXP((int)baseXp, XPSource.Chaos);     // :439 banks baseXp, NOT finalXp
                // Restore V1 behavior: a run's popped bubbles feed the GLOBAL bubble count and its
                // per-100 sparkle-point milestones. The web port records bubblesPopped only into the
                // local stats store; this credits it to the same sinks the native chaos mode uses.
                // Additive to score XP. (WPF DtrhHostService.cs:461-474)
                try
                {
                    int bubblesPopped = (int?)(o["sessionStats"]?["bubblesPopped"]) ?? 0;
                    if (bubblesPopped > 0)
                    {
                        _achievements?.TrackBubblesPopped(bubblesPopped);
                        _quests?.TrackBubblesPopped(bubblesPopped);
                    }
                }
                catch (Exception ex) { _log.LogDebug("DtrhHost bubble credit: {E}", ex.Message); }
                _fx.SyncReveals("run_end");                           // :441 BEFORE Rebroadcast
                var nowRank = ChaosRankThresholds.For(_store.State.RunsCompleted);   // :443
                if ((int)nowRank > _store.State.LastRankSeen) rankUp = nowRank;
                _fx.NotifyRunCompleted((int)finalXp, diff);          // :448
                _sentinel?.Clear();                                  // :449
                _meta.Rebroadcast();                                 // :452 AFTER SyncReveals
            }

            // Session telemetry (:457-475): overwrite page stats with host-authoritative counters.
            var js = o["sessionStats"] as JObject ?? new JObject();
            js["videoWatchSec"] = _runVideoWatchSec;
            js["videosShown"] = _runVideosShown;
            js["videosSkipped"] = _runVideosSkipped;
            js["voicelinesHeard"] = _runVoicelines;
            js["voiceoverSec"] = _runVoiceoverSec;
            js["subliminalsHeard"] = _runSubliminalsHeard;
            js["sparksEarned"] = sparksEarned;
            js["xpEarned"] = finalXp;
            try { _sessionStats.Record(js, diff); } catch (Exception ex) { _log.LogDebug(ex, "session-stats record failed"); }

            // (b) payout reply LAST (:477-488), after Rebroadcast.
            Post(new
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
        }
        catch (Exception ex) { _log.LogWarning(ex, "DtrhHostOrchestrator: run-ended failed"); }
    }

    private void PersistRunSetup(JObject setup)   // WPF PersistRunSetup DtrhHostService.cs:311-334
    {
        var s = _settings?.Current;
        if (s == null) return;
        if (setup["difficulty"] is { } d) s.ChaosDifficulty = (string?)d ?? s.ChaosDifficulty;
        if (setup["durationSec"] is { } dur) s.ChaosRunDurationSec = Math.Clamp((int?)dur ?? 960, 60, 1200);
        if (setup["waveCount"] is { } wc) s.ChaosWaveCount = Math.Clamp((int?)wc ?? 5, 1, 12);
        if (setup["motion"] is { } m) s.ChaosMotionMode = (string?)m ?? s.ChaosMotionMode;
        if (setup["enabledVariants"] is { } ev)
            s.ChaosEnabledVariants = ev.Type == JTokenType.Null ? null : ev.ToObject<List<string>>();
        if (setup["effectIntensity"] is { } ei) s.ChaosEffectIntensity = Math.Clamp((double?)ei ?? 0.85, 0.2, 1.5);
        if (setup["colorFlashes"] is { } cf) s.ChaosColorFlashesEnabled = (bool?)cf ?? true;
        if (setup["boonDraftEnabled"] is { } bd) s.ChaosBoonDraftEnabled = (bool?)bd ?? true;
        if (setup["allowCurses"] is { } ac) s.ChaosAllowCurses = (bool?)ac ?? true;
        if (setup["dartersEnabled"] is { } de) s.ChaosDartersEnabled = (bool?)de ?? true;
        if (setup["key1"] is { } k1) s.ChaosAccessoryKey1 = (string?)k1 ?? s.ChaosAccessoryKey1;
        if (setup["key2"] is { } k2) s.ChaosAccessoryKey2 = (string?)k2 ?? s.ChaosAccessoryKey2;
    }

    private void FirePayload(JObject o)   // WPF FirePayload DtrhHostService.cs:368-391
    {
        try
        {
            var kind = (string?)o["kind"];
            if (string.IsNullOrWhiteSpace(kind)) return;
            bool isVideo = string.Equals(kind, "video", StringComparison.OrdinalIgnoreCase);
            bool isAudio = string.Equals(kind, "audio", StringComparison.OrdinalIgnoreCase);
            if (!isVideo && !isAudio)
            {
                _log.LogWarning("DtrhHostOrchestrator: unknown fire-payload kind '{Kind}'", kind);
                return;
            }
            int strength = Math.Clamp((int?)o["strength"] ?? 60, 0, 100);           // :389
            double durationMult = Math.Clamp((double?)o["durationMult"] ?? 1.0, 0.1, 10.0);   // :390
            if (isAudio && _runActive) _runSubliminalsHeard++;                      // :382
            _fx.FirePayload(kind!, strength, durationMult);
        }
        catch (Exception ex) { _log.LogWarning(ex, "DtrhHostOrchestrator: fire-payload failed"); }
    }

    /// <summary>Page-driven fullscreen: the game asks the host to borderless-toggle its own window —
    /// the page deliberately does NOT use the browser HTML5 Fullscreen API, which would hijack Esc
    /// away from the game's Esc ladder. The head applies the window state via the seam; then the
    /// state is echoed back so the page's dock button + Esc ladder stay in sync. WPF
    /// <c>ApplyHostFullscreen</c> DtrhHostService.cs:286-302. The echo value matches WPF :298
    /// byte-for-byte: WPF read <c>_host.IsFullscreen</c> immediately after <c>SetFullscreen(on)</c>,
    /// which had just synchronously set it to the same value (ChaosWebViewHost.cs:154), so the
    /// requested state IS the resulting state.</summary>
    private void ApplyHostFullscreen(bool on)
    {
        _fx.SetHostFullscreen(on);                // seam throw skips the echo (WPF :295-300 try scope)
        Post(new { type = "fullscreen", on });    // :298 {"type":"fullscreen","on":...}
    }

    private void ApplyWorldFreeze(bool on)   // WPF ApplyWorldFreeze DtrhHostService.cs:555-579
    {
        if (on == _worldFrozen) return;   // dedup
        _worldFrozen = on;
        _fx.SetWorldFrozen(on);
    }

    private void ResetRunMetrics()
    {
        _runVideoWatchSec = 0;
        _runVideosShown = 0;
        _runVideosSkipped = 0;
        _runVoicelines = 0;
        _runVoiceoverSec = 0;
        _runSubliminalsHeard = 0;
    }

    // ---- head-driven inputs (wired to the head video/voice services in S2c-2) --------------------

    /// <summary>Native video started: mark it shown and tell the page the world is playing a video.</summary>
    public void OnVideoStarted()
    {
        if (_runActive) _runVideosShown++;
        Post(new { type = "payload-state", kind = "video", on = true });   // :614
    }

    /// <summary>Native video ended: tell the page and reclaim browser focus.</summary>
    public void OnVideoEnded()
    {
        Post(new { type = "payload-state", kind = "video", on = false });  // :653
        _fx.ReclaimBrowserFocus();                                         // :654
    }

    /// <summary>Native video credited <paramref name="sec"/> watched seconds this run.</summary>
    public void OnVideoWatchCredited(double sec)
    {
        if (_runActive && sec > 0) _runVideoWatchSec += sec;
    }

    /// <summary>Native video was skipped by the user this run.</summary>
    public void OnVideoSkipped()
    {
        if (_runActive) _runVideosSkipped++;
    }

    /// <summary>A voiceline was heard this run (called into the orchestrator by the head bark service).
    /// WPF NoteVoicelineHeard DtrhHostService.cs:639-644.</summary>
    public void NoteVoicelineHeard(double sec)
    {
        if (!_runActive) return;
        _runVoicelines++;
        if (sec > 0) _runVoiceoverSec += sec;
    }

    // ---- outbound / ready --------------------------------------------------------------------------

    /// <summary>Serialize and post a message to the page, queuing until the page reports <c>ready</c>
    /// (WPF ChaosWebViewHost.Post DtrhHostService.cs transport :174-185).</summary>
    public void Post(object msg)
    {
        string json;
        try { json = JsonConvert.SerializeObject(msg); }
        catch (Exception ex) { _log.LogDebug(ex, "DtrhHostOrchestrator: serialize failed"); return; }
        lock (_pendingLock)
        {
            if (!_isReady) { _pending.Add(json); return; }
        }
        SafePost(json);
    }

    private void SafePost(string json)
    {
        try { _browser.PostWebMessageAsJson(json); }
        catch (Exception ex) { _log.LogDebug(ex, "DtrhHostOrchestrator: post failed"); }
    }

    private void OnPageReady()   // WPF OnPageReady DtrhHostService.cs:155-193 + transport flush
    {
        List<string> flush;
        lock (_pendingLock)
        {
            _isReady = true;
            flush = new List<string>(_pending);
            _pending.Clear();
        }
        foreach (var j in flush) SafePost(j);

        // Claim keyboard focus for the browser surface at first ready: on a fresh launch focus does not
        // land in the WebView child until a click, so hold-to-exit (Esc) would be dead from frame one
        // (WPF DtrhHostService.cs:161-163 _host.FocusWeb()).
        _fx.ReclaimBrowserFocus();

        _lastHeartbeatUtc = DateTime.UtcNow;
        StartHeartbeatWatch();

        Post(new
        {
            type = "init",
            protocol = Protocol,
            settings = new { masterVolume = SafeMasterVolume() },
            modId = _fx.ActiveModId(),
            runSetup = BuildRunSetup(),
            m2Test = _testMode,
        });
        Post(_meta.SnapshotMessage());
        try
        {
            var m = _manifest.Build();
            Post(new
            {
                type = "manifest",
                images = m.Images.Select(e => new { name = e.Name, url = e.Url }),
                videos = m.Videos.Select(e => new { name = e.Name, url = e.Url }),
                skipped = m.Skipped,
                truncated = m.Truncated,
            });
        }
        catch (Exception ex) { _log.LogDebug(ex, "manifest build failed"); }
        try
        {
            var favorites = _assetStats.TopAssets(12);
            if (favorites.Count > 0) Post(new { type = "favorites", names = favorites });
        }
        catch (Exception ex) { _log.LogDebug(ex, "favorites build failed"); }
    }

    private int SafeMasterVolume() => _settings?.Current?.MasterVolume ?? 100;   // :901-905

    private object BuildRunSetup()   // WPF BuildRunSetup DtrhHostService.cs:337-363
    {
        try
        {
            var s = _settings?.Current;
            if (s == null) return new { difficulty = "Easy", durationSec = 180, waveCount = 5 };
            return new
            {
                difficulty = s.ChaosDifficulty ?? "Easy",
                durationSec = s.ChaosRunDurationSec,
                waveCount = s.ChaosWaveCount,
                motion = s.ChaosMotionMode ?? "Mixed",
                enabledVariants = s.ChaosEnabledVariants,
                effectIntensity = s.ChaosEffectIntensity,
                colorFlashes = s.ChaosColorFlashesEnabled,
                boonDraftEnabled = s.ChaosBoonDraftEnabled,
                allowCurses = s.ChaosAllowCurses,
                dartersEnabled = s.ChaosDartersEnabled,
                key1 = s.ChaosAccessoryKey1 ?? "Q",
                key2 = s.ChaosAccessoryKey2 ?? "E",
            };
        }
        catch { return new { difficulty = "Easy", durationSec = 180, waveCount = 5 }; }
    }

    // ---- watchdogs / teardown --------------------------------------------------------------------

    private void StartHeartbeatWatch()   // WPF StartHeartbeatWatch DtrhHostService.cs:697-726
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = new Timer(_ => HeartbeatTick(), null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private void HeartbeatTick()
    {
        try
        {
            if (_disposed || !_isReady || _exiting) return;
            double silent = (DateTime.UtcNow - _lastHeartbeatUtc).TotalSeconds;
            double limit = _runActive ? 10 : 20;   // :711 10s mid-run, 20s in the hub
            if (silent > limit)
            {
                _log.LogWarning("DtrhHostOrchestrator: heartbeat silent {Silent:F0}s (limit {Limit}) — recovering", silent, limit);
                Recover("heartbeat-silent");
            }
        }
        catch (Exception ex) { _log.LogDebug(ex, "heartbeat tick failed"); }
    }

    /// <summary>Watchdog give-up: tears down and asks the head to relaunch (once). WPF Recover
    /// DtrhHostService.cs:735-752 relaunched in-process; the port hands that decision to the head.</summary>
    public void Recover(string reason)
    {
        _log.LogWarning("DtrhHostOrchestrator: recover ({Reason})", reason);
        var handler = RecoverRequested;
        DisposeAll();
        handler?.Invoke(reason);
    }

    private void ArmExitWatchdog()   // WPF ArmExitWatchdog DtrhHostService.cs:755-761
    {
        _exitTimer?.Dispose();
        _exitTimer = new Timer(_ => DisposeAll(), null,
            TimeSpan.FromMilliseconds(1200), Timeout.InfiniteTimeSpan);
    }

    /// <summary>Ask the page to wind down gracefully, then force teardown via the exit watchdog. WPF
    /// CloseActive DtrhHostService.cs:143-145.</summary>
    public void RequestClose()
    {
        if (_disposed) return;
        Post(new { type = "end-run", reason = "host" });
        _exiting = true;
        ArmExitWatchdog();
    }

    private void DisposeAll()   // WPF DisposeAll DtrhHostService.cs:769-795
    {
        if (_disposed) return;
        _disposed = true;
        try { _exitTimer?.Dispose(); } catch { }
        _exitTimer = null;
        try { _heartbeatTimer?.Dispose(); } catch { }
        _heartbeatTimer = null;

        // World-freeze force-resume on teardown (:775).
        if (_worldFrozen)
        {
            _worldFrozen = false;
            try { _fx.SetWorldFrozen(false); } catch { }
        }
        // FlushSave on EVERY teardown path (:776).
        try { _meta.FlushSave(); } catch { }
        // Clear the crash sentinel on deliberate close (mid-run death leaves it armed) (:777-782).
        if (_runActive && !_testMode) { try { _sentinel?.Clear(); } catch { } }
        _runActive = false;
        _exiting = false;

        try { _browser.WebMessageReceived -= OnBrowserMessage; } catch { }
        try { _fx.RestoreMainWindow(); } catch { }   // :792 ShowFromTray
        _log.LogInformation("DtrhHostOrchestrator: closed");
        Closed?.Invoke();
    }

    public void Dispose() => DisposeAll();
}
