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
    private static bool _relaunchedOnce;
    private static bool _testMode;
    private static bool _videoHooked;

    public static bool IsActive => _host != null;

    /// <summary>Launch the game window (idempotent). The page boots into The Fall on the
    /// active preset; the Warren hub becomes the boot target in M5.</summary>
    public static void Launch(bool testMode = false)
    {
        if (_host != null) { _host.FocusWeb(); return; }
        try
        {
            _exiting = false;
            _runActive = false;
            _testMode = testMode;
            _meta = new DtrhMetaBridge(testMode, msg => _host?.Post(msg));
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
                    // M4: the bundled Chaos art (bubble sprites, boon icons, announcer
                    // banners) - plain <img> loads, so DenyCors suffices.
                    ("ccp.art", Path.Combine(AppContext.BaseDirectory, "assets", "Chaos"), CoreWebView2HostResourceAccessKind.DenyCors),
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
            HookVideoEvents(true);
            StartHeartbeatWatch();
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
                runConfig = BuildRunConfig(),
                m2Test = _testMode,
            });
            if (_meta != null) _host?.Post(_meta.SnapshotMessage());
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

    // ============================ page messages ============================

    private static void OnPageMessage(JObject o)
    {
        switch ((string?)o["type"])
        {
            case "sfx":
            {
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
            case "meta-command":
                _meta?.Handle(o);
                break;
            case "run-started":
            {
                _runActive = true;
                var diff = (string?)o["difficulty"] ?? "Gentle";
                if (!_testMode)
                {
                    try { ChaosCrashSentinel.Mark($"mode=dtrh-web diff={diff}"); } catch { }
                    try { App.Bark?.NotifyChaosRunStarted(diff); } catch { }
                }
                App.Logger?.Information("DtrhHost: run started (diff={D}, mode={M})", diff, (string?)o["mode"]);
                break;
            }
            case "run-ended":
                OnRunEnded(o);
                break;
            case "bark":
                RouteBark(o);
                break;
            case "heartbeat":
                _lastHeartbeatUtc = DateTime.UtcNow;
                break;
            case "boot-error":
                App.Logger?.Warning("DtrhHost: page boot-error: {Msg} - closing", (string?)o["msg"]);
                CloseActive();
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

    /// <summary>fire-payload {kind, overlay?, strength?, durationMult?} -> the real desktop effect.
    /// The page decides WHEN; the native services own HOW (they already handle their own z-order,
    /// which lands payload windows above the game surface - M0-verified).</summary>
    private static void FirePayload(JObject o)
    {
        try
        {
            var kindStr = (string?)o["kind"];
            if (string.IsNullOrWhiteSpace(kindStr)) return;

            EffectPayload payload;
            if (string.Equals(kindStr, "overlay", StringComparison.OrdinalIgnoreCase))
            {
                // Explicit overlay flavor when given; factory picks randomly otherwise.
                var flavor = (string?)o["overlay"];
                payload = flavor is "pink_filter" or "spiral" or "braindrain"
                    ? new OverlayPayload(flavor)
                    : EffectPayloadFactory.Build(EffectBubblePayloadKind.Overlay);
            }
            else if (Enum.TryParse<EffectBubblePayloadKind>(kindStr, ignoreCase: true, out var kind))
            {
                payload = EffectPayloadFactory.Build(kind);
            }
            else
            {
                App.Logger?.Warning("DtrhHost: unknown payload kind '{K}' ignored", kindStr);
                return;
            }

            payload.Strength = Math.Clamp((int?)o["strength"] ?? 60, 0, 100);
            payload.DurationMult = Math.Clamp((double?)o["durationMult"] ?? 1.0, 0.1, 10.0);
            payload.Fire();
            App.Logger?.Information("DtrhHost: fired payload {K} (strength {S})", payload.DisplayName, payload.Strength);
        }
        catch (Exception ex) { App.Logger?.Warning("DtrhHost.FirePayload: {E}", ex.Message); }
    }

    /// <summary>run-ended -> XP payout (C#-owned formula, identical to the WPF EndRun) +
    /// meta banking via the shared AwardRunRewards, answered with payout-result.</summary>
    private static void OnRunEnded(JObject o)
    {
        _runActive = false;
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
                try { RevealService.Sync("run_end"); } catch { }
                try
                {
                    var nowRank = ChaosRanks.For(ChaosMeta.State.RunsCompleted);
                    if ((int)nowRank > ChaosMeta.State.LastRankSeen) rankUp = nowRank;
                }
                catch { }
                try { App.Bark?.NotifyChaosRunCompleted((int)finalXp, diff); } catch { }
                try { ChaosCrashSentinel.Clear(); } catch { }
            }

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
                default: App.Logger?.Debug("DtrhHost: unrouted bark event '{E}'", (string?)o["event"]); break;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("DtrhHost.RouteBark: {E}", ex.Message); }
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

    private static void OnVideoStarted(object? sender, EventArgs e)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || _host == null) return;
        disp.BeginInvoke(() => _host?.Post(new { type = "payload-state", kind = "video", on = true }));
    }

    private static void OnVideoEnded(object? sender, EventArgs e)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || _host == null) return;
        disp.BeginInvoke(() =>
        {
            _host?.Post(new { type = "payload-state", kind = "video", on = false });
            _host?.FocusWeb();   // the video window had Win32 focus; reclaim keyboard for the game
        });
    }

    // ============================ watchdogs / recovery ============================

    private static void StartHeartbeatWatch()
    {
        StopHeartbeatWatch();
        _lastHeartbeatUtc = DateTime.UtcNow;
        _heartbeatWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _heartbeatWatch.Tick += (_, _) =>
        {
            // Only a WEDGED live run warrants the recovery ladder; the hub idling with the
            // page not yet booted (no heartbeat source) must not trip it.
            if (!_runActive || _host == null || !_host.IsReady || _exiting) return;
            if ((DateTime.UtcNow - _lastHeartbeatUtc).TotalSeconds > 10)
            {
                App.Logger?.Warning("DtrhHost: page heartbeat silent >10s during a run - recovering");
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
    private static object BuildRunConfig()
    {
        try
        {
            var cfg = ChaosRunConfig.FromSettings();
            var state = new ChaosRunState(cfg);
            try { ChaosMeta.ApplyLifetimeBoons(state); }
            catch (Exception ex) { App.Logger?.Debug("DtrhHost loadout apply: {E}", ex.Message); }

            var s = App.Settings?.Current;
            var meta = ChaosMeta.State;

            // Equipped active-use skills (toys), in catalogue order, capped by sewn pockets -
            // mirrors ChaosModeService.BuildActiveToys.
            var toys = new List<object>();
            int pockets = ChaosMeta.SlotsFor(ChaosBoonCategory.Skill);
            string[] keys = { s?.ChaosAccessoryKey1 ?? "Q", s?.ChaosAccessoryKey2 ?? "E" };
            int slot = 0;
            foreach (var b in ChaosLifetimeBoons.All)
            {
                if (slot >= pockets) break;
                if (!b.IsActiveUse || !ChaosMeta.IsBoonActive(b.Id)) continue;
                if (!state.ToyPower.TryGetValue(b.Id, out var power)) continue;
                int lvl = ChaosMeta.BoonLevel(b.Id);
                toys.Add(new
                {
                    id = b.Id,
                    name = b.Name,
                    glyph = b.Glyph,
                    desc = b.Desc,
                    key = slot < keys.Length ? keys[slot] : "",
                    cooldownSec = b.UseCooldownSec,
                    power,
                    level = lvl,
                    maxed = lvl >= b.MaxLevel,
                });
                slot++;
            }

            // Everything equipped/trained, for duo/trio draft gating (RequiresAny/All).
            var equipment = new List<string>();
            foreach (var id in meta.ActiveLifetimeBoons ?? new HashSet<string>())
                if (ChaosMeta.IsBoonActive(id)) equipment.Add(id);
            foreach (var id in meta.PurchasedUpgrades ?? new HashSet<string>())
                if (ChaosMeta.IsUpgradeActive(id)) equipment.Add(id);

            // Intrusive Thoughts' phrase pool (the user's enabled bouncing-text lines).
            var thoughts = new List<string>();
            try
            {
                var pool = s?.BouncingTextPool;
                if (pool != null)
                    foreach (var kv in pool) if (kv.Value) thoughts.Add(kv.Key);
            }
            catch { }

            int rabbitsFootLvl = ChaosMeta.IsBoonActive("rabbits_foot") ? ChaosMeta.BoonLevel("rabbits_foot") : 0;
            var (gMin, gMax) = ChaosLifetimeBoons.GoldenPayRange(rabbitsFootLvl);

            return new
            {
                difficulty = cfg.Difficulty.ToString(),
                difficultyMult = cfg.DifficultyMult,
                durationSec = cfg.DurationSec,
                waveCount = cfg.WaveCount,
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
                hitboxScale = cfg.HitboxScale,
                magnetEnabled = cfg.MagnetEnabled,
                popupHeartEnabled = cfg.PopupHeartEnabled,
                pendulumSwing = cfg.PendulumSwing,
                rankIndex = (int)ChaosMeta.RankIndex,
                runsCompleted = meta.RunsCompleted,
                equipment,
                equippedStartBoon = meta.EquippedStartBoon,
                toys,
                toyKeys = keys,
                thoughtTexts = thoughts,

                // ---- M4: the applied loadout (ChaosMeta.ApplyLifetimeBoons snapshot) ----
                loadout = new
                {
                    shields = state.Shields,
                    startingShields = cfg.StartingShields,
                    collarSaves = state.CollarSaves,
                    fuseTimeMult = state.FuseTimeMult,
                    benignBaseline = state.BenignBaseline,
                    blindfoldActive = state.BlindfoldActive,
                    blindfoldPayMult = state.BlindfoldPayMult,
                    blindfoldOpacity = state.BlindfoldOpacity,
                    lastBreathWindowSec = state.LastBreathWindowSec,
                    lastBreathPayMult = state.LastBreathPayMult,
                    chanceDoubleOdds = state.ChanceDoubleOdds,
                    rerollsLeft = state.RerollsLeft,
                    sinExtraMult = state.SinExtraMult,
                    goldenChance = state.GoldenChance,
                    goldenPayRange = new[] { gMin, gMax },
                    dropPerPop = state.DropPerPop,
                    dripFeedCap = state.DropPerPop > 0 ? ChaosLifetimeBoons.DripFeedCap(state.DropPerPop) : 0,
                    shieldRegenPops = state.ShieldRegenPops,
                    showPopScores = state.ShowPopScores,
                    showWaveTimer = state.ShowWaveTimer,
                    rippleRechargeSec = state.RippleRechargeSec,
                    rippleRadiusPx = state.RippleRadiusPx,
                    rippleLifeMs = state.RippleLifeMs,
                    rabbitRateMult = state.RabbitRateMult,
                    intrusiveThoughtsSec = state.IntrusiveThoughtsSec,
                    slowMoBonusSec = state.SlowMoBonusSec,
                    bubbleScale = state.BubbleScale,
                    chainReactionReach = state.ChainReactionReach,
                    cursorPullStrength = state.CursorPullStrength,
                    spankerActive = state.SpankerActive,
                    spankGrowFactor = state.SpankGrowFactor,
                    magnetEnabled = state.MagnetEnabled,
                    hitboxScale = cfg.HitboxScale,
                    maxedBoons = state.MaxedBoons.ToList(),
                },

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
}
