using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Avalonia.Views.Games.Dtrh;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The Down the Rabbit Hole host (WPF Services/Chaos/DtrhHostService.cs, the run + meta half):
    /// meta-command through the ported <see cref="DtrhMetaBridge"/> (the Warren save on ChaosMeta),
    /// request-run (saved setup persisted, run-config dealt, the scripted first run), run-started /
    /// run-progress / run-ended with DtrhRunPayoutRule (abandoned runs pro rata, a minute to count) and
    /// payout-result (XP capped by score, sparks, previous best, rank up), and a window closed mid-run
    /// banks its last snapshot (DtrhRunCloseRule). Bodies copied from WPF 7.1.5 :480-744, :1133-1243.
    /// ponytail: sfx, barks, haptics, the Loom, asset stats, world freeze / dive mute, session metrics,
    /// reveals and the crash sentinel are not wired yet.
    /// </summary>
    internal sealed partial class GameWindow
    {
        private DtrhMetaBridge? _meta;
        private bool _runActive;
        private JObject? _lastRunProgress;
        private DateTime _runStartedUtc = DateTime.MinValue;
        private const bool _testMode = false;

        private double? HostElapsedSec => _runStartedUtc == DateTime.MinValue ? null : (DateTime.UtcNow - _runStartedUtc).TotalSeconds;

        /// <summary>WPF ChaosHappyPath.BuildFirstRunConfig: the scripted first descent.</summary>
        internal static ChaosRunConfig BuildFirstRunConfig() => new()
        {
            ScriptedFirstRun = true,
            Difficulty = ChaosDifficulty.Easy,
            DurationSec = 180,
            WaveCount = 5,
            EnabledVariants = new List<string> { "flash", "subliminal" },
            BoonDraftEnabled = false,
            AllowCurses = false,
            DartersEnabled = false,
            SpawnRateMult = 0.6,
            SinChance = 0.0,
        };

        private bool HandleDtrh(JObject o)
        {
            switch ((string?)o["type"])
            {
                case "meta-command":
                    _meta ??= new DtrhMetaBridge(false, Post);
                    _meta.Handle(o);
                    return true;
                case "request-run":
                    OnRequestRun(o);
                    return true;
                case "run-progress":
                    if (_runActive) _lastRunProgress = o;
                    return true;
                case "run-started":
                    _runActive = true;
                    InRun = true;
                    _lastRunProgress = null;
                    _runStartedUtc = DateTime.UtcNow;
                    Log.Information("[Game] dtrh: run started (diff={D}, mode={M})", (string?)o["difficulty"] ?? "Gentle", (string?)o["mode"]);
                    return true;
                case "run-ended":
                    InRun = false;
                    OnRunEnded(o);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>WPF OnPageReady's DtRH extras: the Warren snapshot after init (init carries runSetup).</summary>
        private void PostDtrhReady()
        {
            _meta ??= new DtrhMetaBridge(false, Post);
            Post(_meta.SnapshotMessage());
        }

        private void CloseDtrh()
        {
            if (Spec.Id != "dtrh") return;
            BankDtrhRunOnTeardown("closed");
            try { _meta?.FlushSave(); } catch (Exception ex) { Log.Debug("[Game] dtrh meta flush: {E}", ex.Message); }
        }

        /// <summary>WPF DisposeAll: a window that dies mid-run banks its last snapshot as an abandoned run.</summary>
        private void BankDtrhRunOnTeardown(string why)
        {
            if (!DtrhRunCloseRule.ShouldBookOnClose(_runActive, _lastRunProgress != null)) return;
            Log.Information("[Game] dtrh: {Why} mid-run - banking the last run snapshot", why);
            try
            {
                var snapshot = _lastRunProgress!;
                snapshot["abandoned"] = true;
                OnRunEnded(snapshot, fromTeardown: true);
            }
            catch (Exception ex) { Log.Debug("[Game] dtrh bank: {E}", ex.Message); }
        }

        private void OnRequestRun(JObject o)
        {
            try
            {
                if (o["setup"] is JObject setup) PersistRunSetup(setup);
                bool force = !_testMode && ChaosMeta.State.ForceScriptedRun;
                bool scripted = !_testMode && (ChaosMeta.State.RunsCompleted == 0 || force);
                var cfg = scripted ? BuildFirstRunConfig() : ChaosRunConfig.FromSettings();
                if (force)
                {
                    // One-shot spent at DEAL time: the recap's "fall again" re-requests a config,
                    // and a run-end clear has too many exit paths (watchdog, crash) - any missed
                    // one would deal a second classroom. Persist + rebroadcast so JS agrees.
                    ChaosMeta.State.ForceScriptedRun = false;
                    ChaosMeta.Save();
                    try { _meta?.Rebroadcast(); } catch (Exception ex) { _ = ex; }
                }
                Post(new { type = "run-config", runConfig = BuildRunConfig(cfg) });
                Log.Information("DtrhHost: dealt run config (diff={D}, scripted={S})", cfg.Difficulty, scripted);
            }
            catch (Exception ex) { Log.Warning("DtrhHost.OnRequestRun: {E}", ex.Message); }
        }

        /// <summary>The web-relevant subset of ChaosHubWindow.SaveToSettings. Absent fields are
        /// left untouched; the reveal clamps stay C#-side (FromSettings), so a stale page can
        /// never write itself past a gate.</summary>
        private void PersistRunSetup(JObject setup)
        {
            var s = CoreSettings.Current;
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

        private object BuildRunSetup()
        {
            try
            {
                var s = CoreSettings.Current;
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

        private void OnRunEnded(JObject o, bool fromTeardown = false)
        {
            // The host is the authority on whether a descent is still open, because it now has TWO
            // producers: the page's run-ended and the teardown's synthetic one. A run-ended already
            // queued on the dispatcher can be pumped after DisposeAll banked, and paying it again
            // would double the Sparks and the run counter.
            bool wasActive = _runActive;
            var hostElapsed = HostElapsedSec;   // read before the claim clears the stamp
            _runActive = false;
            _lastRunProgress = null;   // banked: nothing left for the teardown to pay
            _runStartedUtc = DateTime.MinValue;
            
               // a run ending mid-freeze must resume native video + voice, not wedge them through the hub
            if (!DtrhRunCloseRule.ShouldPayBooking(wasActive))
            {
                Log.Debug("DtrhHost: run-ended for a descent that is already banked - ignored");
                return;
            }
            try
            {
                double score = (double?)o["score"] ?? 0;
                double configuredSec = Math.Max(1, (double?)o["durationSec"] ?? 60);
                double elapsedSec = Math.Clamp((double?)o["elapsedSec"] ?? configuredSec, 0, configuredSec * 2);
                double diffMult = Math.Clamp((double?)o["difficultyMult"] ?? 1.0, 0.5, 5.0);
                double sparkGainMult = Math.Clamp((double?)o["sparkGainMult"] ?? 1.0, 0.5, 5.0);
                string diff = (string?)o["difficulty"] ?? "Gentle";

                // A descent that was LEFT is paid for the seconds it really fell, and only counts as
                // a run once it lasted a minute. Both because AwardRunRewards scales its Spark floor
                // off the CONFIGURED length: an endless run is dealt 720s, so without this a
                // hold-Escape one second in mints the fully maxed floor, repeatably. The seconds
                // themselves come from the page, so they are bounded by the host's own clock.
                bool abandoned = (bool?)o["abandoned"] ?? false;
                var payout = DtrhRunPayoutRule.For(abandoned, configuredSec, elapsedSec, hostElapsed);
                double durationSec = Math.Max(1, payout.PaidDurationSec);

                double durMin = durationSec / 60.0;
                double capBase = 250.0 * durMin * diffMult;
                double baseXp = Math.Min(score, capBase);
                double skillMult = ChaosRunState.SkillMultProvider?.Invoke() ?? 1.0;
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
                        ElapsedSec: elapsedSec),
                        payout.CountsAsRun);
                }

                ChaosRank? rankUp = null;
                if (!_testMode)
                {
                    try { CoreProgression.AddXP(baseXp, "Chaos"); }
                    catch (Exception ex) { Log.Debug("DtrhHost payout AddXP: {E}", ex.Message); }
                    // Restore V1 behavior: a run's popped bubbles feed the GLOBAL bubble
                    // count and its per-100 sparkle-point milestones. The web port had been
                    // recording bubblesPopped only into the local stats store; this credits
                    // it to the same sink the native chaos mode uses. Additive to score XP.
                    try
                    {
                        int bubblesPopped = (int?)(o["sessionStats"]?["bubblesPopped"]) ?? 0;
                        if (bubblesPopped > 0) global::ConditioningControlPanel.Avalonia.App.Achievements?.TrackBubblesPopped(bubblesPopped);
                    }
                    catch (Exception ex) { Log.Debug("DtrhHost bubble credit: {E}", ex.Message); }
                    /* ponytail: reveals */
                    try
                    {
                        var nowRank = ChaosRanks.For(ChaosMeta.State.RunsCompleted);
                        if ((int)nowRank > ChaosMeta.State.LastRankSeen) rankUp = nowRank;
                    }
                    catch (Exception ex) { _ = ex; }
                    // Not on the teardown path: the window is closing, so a "nice descent" line has
                    // nowhere to land and arrives over whatever the player went back to. The reveal
                    // sync above is bookkeeping and still runs.
                    if (!fromTeardown)
                    {
                        /* ponytail: barks (B1) */
                    }
                    // NOT on the teardown path: ChaosCrashSentinel.Recover("process-failed") and
                    // ("heartbeat-silent") both land in DisposeAll, and clearing the sentinel from
                    // there would report a genuine WebView2 crash as a clean run next launch.
                    if (!fromTeardown) {  }
                    // RevealService.Sync mutated pendingReveals BEHIND the bridge - push a fresh
                    // snapshot so the Warren's flash pass sees the new pendings on return.
                    try { _meta?.Rebroadcast(); } catch (Exception ex) { _ = ex; }
                }

                // ponytail: local session telemetry (DtrhSessionStatsStore) is not ported.

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
                Log.Information(
                    "DtrhHost: web run complete: base {Base:0} x skill {Mult:0.0} = {Final:0} XP, {Sparks} sparks{T}",
                    baseXp, skillMult, finalXp, sparksEarned, _testMode ? " (TEST, no XP credited)" : "");
            }
            catch (Exception ex) { Log.Warning("DtrhHost.OnRunEnded: {E}", ex.Message); }
        }

        private object BuildRunConfig(ChaosRunConfig cfg)
        {
            try
            {
                // Grab-in-the-tube rework: the run no longer ships a pre-applied loadout. Every
                // accessory/charm/toy is discovered + grabbed in the fall and applied there (JS
                // game/boonPassives.js), so this only ships each item's current LEVEL + the
                // consumable-slot count. Habits stay always-on and arrive as cfg.* knobs below.
                var s = CoreSettings.Current;
                var meta = ChaosMeta.State;

                // Intrusive Thoughts' phrase pool (the user's enabled bouncing-text lines).
                var thoughts = new List<string>();
                try
                {
                    var pool = s?.BouncingTextPool;
                    if (pool != null)
                        foreach (var kv in pool) if (kv.Value) thoughts.Add(kv.Key);
                }
                catch (Exception ex) { _ = ex; }

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
                    shakeIntensity = cfg.ShakeIntensity,   // 0..1 amplitude for the page's shake (game/screenShake.js)

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
                Log.Debug("DtrhHost.BuildRunConfig: {E}", ex.Message);
                return new { difficulty = "Easy", difficultyMult = 1.0, durationSec = 180, waveCount = 5, effectIntensity = 0.85 };
            }
        }
    }
}
