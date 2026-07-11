using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia head implementation of the Core <see cref="IDtrhNativeEffects"/> seam — the single
/// callback surface the portable <c>DtrhHostOrchestrator</c> uses for everything it cannot do inside
/// CCP.Core: real desktop conditioning payloads, chaos SFX, voice barks, reveal-sync, building the
/// head <c>ChaosRunConfig</c>, and restoring the main window. Mirrors the WPF <c>DtrhHostService</c>
/// native-call surface (DtrhHostService.cs:6 inventory). Constructed and DI-registered by S2c-2c;
/// window-coupled callbacks (<c>reclaimFocus</c>/<c>restoreMainWindow</c>) are wired by S2c-2b when
/// the game window exists. All members are fault-isolated: a head effect that throws logs and
/// returns, never breaking the orchestrator's run loop.
/// </summary>
public sealed class DtrhNativeEffects : IDtrhNativeEffects
{
    private readonly IBarkService? _bark;
    private readonly IModService? _mods;
    private readonly ISettingsService? _settings;
    private readonly IRevealService? _reveal;
    private readonly IChaosMetaService? _meta;
    private readonly ILogger<DtrhNativeEffects>? _log;
    private readonly Action? _reclaimFocus;
    private readonly Action? _restoreMainWindow;

    /// <summary>Construct the native-effects bridge. Every dependency is nullable so unit/2b wiring
    /// can supply only the surface it exercises; S2c-2c resolves the real singletons from DI.</summary>
    public DtrhNativeEffects(
        IBarkService? bark = null,
        IModService? mods = null,
        ISettingsService? settings = null,
        IRevealService? reveal = null,
        IChaosMetaService? meta = null,
        ILogger<DtrhNativeEffects>? log = null,
        Action? reclaimFocus = null,
        Action? restoreMainWindow = null)
    {
        _bark = bark;
        _mods = mods;
        _settings = settings;
        _reveal = reveal;
        _meta = meta;
        _log = log;
        _reclaimFocus = reclaimFocus;
        _restoreMainWindow = restoreMainWindow;
    }

    /// <inheritdoc/>
    public void PlaySfx(string name, float scale)
    {
        try { AvaloniaChaosSfx.Play(name, scale); }  // WPF DtrhHostService.cs:217-220
        catch (Exception ex) { _log?.LogDebug(ex, "DtrhNativeEffects.PlaySfx({Name}, {Scale}) failed", name, scale); }
    }

    /// <inheritdoc/>
    public void FirePayload(string kind, int strength, double durationMult)
    {
        try
        {
            var payload = AvaloniaEffectPayloadFactory.ForVariant(kind);  // WPF DtrhHostService.cs:368-391
            if (payload is null)
            {
                _log?.LogWarning("payload kind {Kind} has no factory mapping - ignored", kind);
                return;
            }
            payload.Strength = strength;
            // durationMult is NON-CONSUMED by VideoPayload/AudioPayload (the only two kinds the orchestrator sends):
            // VideoPayload arms a fixed 15s random segment + TriggerVideo, AudioPayload flashes a subliminal; neither
            // reads GlobalDurationMult or Strength. We set Strength (harmless/future-proof) and Fire(); we deliberately
            // do NOT mutate the global static (advisor ruling 2026-07-11 — narrow concurrent-ambient race).
            payload.Fire();
        }
        catch (Exception ex) { _log?.LogDebug(ex, "DtrhNativeEffects.FirePayload({Kind}, {Strength}) failed", kind, strength); }
    }

    /// <inheritdoc/>
    public void SetWorldFrozen(bool frozen)
    {
        // S2c-2a INTERIM: the world-freeze seam (IVideoService/avatar pause-resume) is not yet built
        // (board row S2c-3, a state-mutating JUDGMENT slice). Post-2026-cutover the game fires native
        // video/audio only rarely, so a native payload continuing under a game modal is a narrow,
        // tracked degradation, not a crash.
        try { _log?.LogDebug("DtrhNativeEffects.SetWorldFrozen({Frozen}) - interim no-op pending S2c-3 world-freeze seam", frozen); }
        catch { /* logging must never throw */ }
    }

    /// <inheritdoc/>
    public void ReclaimBrowserFocus()  // WPF DtrhHostService.cs:654 (_host.FocusWeb)
    {
        try { _reclaimFocus?.Invoke(); }
        catch (Exception ex) { _log?.LogDebug(ex, "DtrhNativeEffects.ReclaimBrowserFocus failed"); }
    }

    /// <inheritdoc/>
    public void NotifyRunStarted(string difficulty)  // WPF DtrhHostService.cs:243
    {
        try { _bark?.NotifyChaosRunStarted(difficulty); }
        catch (Exception ex) { _log?.LogDebug(ex, "DtrhNativeEffects.NotifyRunStarted({Difficulty}) failed", difficulty); }
    }

    /// <inheritdoc/>
    public void NotifyRunCompleted(int finalXp, string difficulty)  // WPF DtrhHostService.cs:448
    {
        try { _bark?.NotifyChaosRunCompleted(finalXp, difficulty); }
        catch (Exception ex) { _log?.LogDebug(ex, "DtrhNativeEffects.NotifyRunCompleted({Xp}, {Difficulty}) failed", finalXp, difficulty); }
    }

    /// <inheritdoc/>
    public void SyncReveals(string reason)  // WPF DtrhHostService.cs:441 (RevealService.Sync("run_end"))
    {
        try { _reveal?.Sync(reason); }
        catch (Exception ex) { _log?.LogDebug(ex, "DtrhNativeEffects.SyncReveals({Reason}) failed", reason); }
    }

    /// <inheritdoc/>
    public string ActiveModId()  // WPF DtrhHostService.cs:911 (App.Mods.ActiveModId)
        => _mods?.ActiveMod?.Id ?? "builtin-sissyhypno";

    /// <inheritdoc/>
    public void RestoreMainWindow()  // WPF DtrhHostService.cs:792 (ShowFromTray)
    {
        try { _restoreMainWindow?.Invoke(); }
        catch (Exception ex) { _log?.LogDebug(ex, "DtrhNativeEffects.RestoreMainWindow failed"); }
    }

    /// <inheritdoc/>
    public void RouteBark(string barkJson)  // WPF DtrhHostService.cs:498-548
    {
        try
        {
            var o = JObject.Parse(barkJson);
            var bark = _bark;
            if (bark is null) return;
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
                case "dollhouse-first-open": bark.NotifyChaosDollhouseFirstOpen(); break;
                case "reveal-flash": bark.NotifyChaosRevealFlash(S("id")); break;
                case "lesson-complete": bark.NotifyChaosLessonComplete(S("id")); break;
                case "duo-demo": bark.NotifyChaosDuoDemo(); break;
                case "rabbit-caught": bark.NotifyChaosDarterCaught(I("gold"), 0, true); break;
                default: _log?.LogDebug("DtrhNativeEffects: unrouted bark event {Event}", (string?)o["event"]); break;
            }
        }
        catch (Exception ex) { _log?.LogDebug(ex, "DtrhNativeEffects.RouteBark failed"); }
    }

    /// <inheritdoc/>
    public string BuildRunConfigJson(bool scripted)  // WPF DtrhHostService.cs:808-891
    {
        try
        {
            var cfg = scripted ? ChaosHappyPath.BuildFirstRunConfig() : ChaosRunConfig.FromSettings();
            var s = _settings?.Current;
            var meta = _meta?.State ?? new ChaosMetaState();
            int rankIndex = _meta?.RankIndex ?? 0;
            var thoughts = new List<string>();
            try { var pool = s?.BouncingTextPool; if (pool != null) foreach (var kv in pool) if (kv.Value) thoughts.Add(kv.Key); } catch { }
            var obj = new
            {
                difficulty = cfg.Difficulty.ToString(),
                difficultyMult = cfg.DifficultyMult,
                durationSec = cfg.RunDurationSec,
                waveCount = cfg.WaveCount,
                effectIntensity = cfg.EffectIntensity,
                enabledVariants = cfg.EnabledVariants,
                motionOverride = cfg.MotionOverride?.ToString(),
                fuseTimeMult = cfg.FuseTimeMult,
                baseMult = cfg.BaseMult,
                sparkGainMult = cfg.SparkGainMult,
                spawnRateMult = cfg.SpawnRateMult,
                colorFlashes = cfg.ColorFlashesEnabled,
                screenShake = cfg.ScreenShakeEnabled,
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
                rankIndex = rankIndex,
                runsCompleted = meta.RunsCompleted,
                equippedStartBoon = meta.EquippedStartBoon,
                thoughtTexts = thoughts,
                levels = meta.LifetimeBoonLevels ?? new Dictionary<string, int>(),
                consumableSlots = meta.ConsumableSlots,
                discoveredCodexIds = (meta.DiscoveredCodexIds ?? new HashSet<string>()).ToList(),
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
                    seenBraindrain = meta.SeenBraindrain,
                    seenFirstSin = meta.SeenFirstSin,
                    seenDuoDemo = meta.SeenDuoDemo,
                },
            };
            return JsonConvert.SerializeObject(obj);
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "DtrhNativeEffects.BuildRunConfigJson failed - returning fallback");
            return JsonConvert.SerializeObject(new { difficulty = "Easy", difficultyMult = 1.0, durationSec = 180, waveCount = 5, effectIntensity = 0.85 });
        }
    }
}
