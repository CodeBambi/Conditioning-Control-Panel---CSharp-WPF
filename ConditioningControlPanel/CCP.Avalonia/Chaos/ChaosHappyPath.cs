using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Avalonia.Services;
using ConditioningControlPanel.Core.Services.Chaos;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of the WPF happy-path scripted-run director. It observes the live run and
/// fires the first-descent teach beats in order: run 1 is fully scripted, runs 2+ get light
/// debuts, and runs 4+ get the rigged first-sin / duo demos. All state is run-scoped and
/// cleared by <see cref="OnRunEnded"/>; every beat swallows its own exceptions.
/// </summary>
public static class ChaosHappyPath
{
    // ============================ tunables (one block) ============================

    private const double R1_SPAWN_RATE_MULT = 0.6;
    private const double R1_THREAT_AT_PROGRESS = 0.30;
    private const double R1_DRAFT_AT_PROGRESS = 0.55;
    private const double R1_DRAFT_FALLBACK_PROGRESS = 0.70;
    private const double R1_DARTER_AT_PROGRESS = 0.88;
    private const double R1_THREAT_FUSE_MULT = 3.0;
    private const int R1_STREAK_TEACH_COMBO = 3;

    private const double R2_BRAINDRAIN_AT_PROGRESS = 0.25;
    private const double R2_GOLDEN_AT_PROGRESS = 0.50;

    private static readonly string[] R1_DRAFT_POOL =
        { "extra_shield", "defuse_chain", "golden_touch", "welcome_shower", "heavy_drop" };

    // ============================ run-scoped state ============================

    private static ChaosRunState? _state;
    private static AvaloniaChaosService? _svc;
    private static int _runsAtStart;
    private static bool _scripted;

    private static bool _streakTaught;
    private static bool _threatSpawned;
    private static bool _threatDefuseSeen;
    private static bool _variantsJoined;
    private static bool _draftFired;
    private static bool _darterSpawned;

    private static bool _braindrainDebuted;
    private static bool _goldenSpawned;

    private static int _draftsThisRun;
    private static string? _shieldRigBoonId;
    private static bool _firstSinDraftPending;

    private const int SCRIPTED_DESCENTS = 4;

    public static bool IsScripting => _state != null && _runsAtStart < SCRIPTED_DESCENTS;

    /// <summary>The concrete chaos service that owns the announcer queue (WS2: the announcer
    /// is a compositor layer driven through AvaloniaChaosService.AnnounceChaos). Beats fire
    /// mid-run, when <c>_svc</c> is set by OnRunStarted; the DI fallback covers any beat
    /// raised outside that window.</summary>
    private static AvaloniaChaosService? AnnSvc =>
        _svc ?? AvaloniaChaosApp.Chaos as AvaloniaChaosService;

    // ============================ entry points ============================

    /// <summary>The forced naked config for the very first descent (RunsCompleted == 0).</summary>
    public static ChaosRunConfig BuildFirstRunConfig() => new()
    {
        ScriptedFirstRun = true,
        Difficulty = "Easy",
        RunDurationSec = 180,
        WaveCount = 5,
        EnabledVariants = new List<string> { "flash", "subliminal" },
        BoonDraftEnabled = false,
        AllowCurses = false,
        DartersEnabled = false,
        SpawnRateMult = R1_SPAWN_RATE_MULT,
        SinChance = 0.0,
        EffectIntensity = 0.85,
        DraftAutoResumeSec = 12,
        AmbientMode = false,
        MagnetEnabled = false,
        FuseTimeMult = 1.0,
        HitboxScale = 1.0,
        DraftChoices = 3,
        ScreenShakeEnabled = true,
        ColorFlashesEnabled = true,
        ShakeIntensity = 0.8,
        StartingShields = 0,
        StartingFocus = 50,
        BaseMult = 1.0,
        SparkGainMult = 1.0,
    };

    public static void OnRunStarted(ChaosRunState state, AvaloniaChaosService svc)
    {
        _state = state;
        _svc = svc;
        _runsAtStart = ChaosMeta.State.RunsCompleted;
        _scripted = state.Config.ScriptedFirstRun;
        _streakTaught = _threatSpawned = _threatDefuseSeen = _variantsJoined = false;
        _draftFired = _darterSpawned = false;
        _braindrainDebuted = _goldenSpawned = false;
        _draftsThisRun = 0;
        _shieldRigBoonId = null;
        _firstSinDraftPending = false;
    }

    public static void OnRunEnded()
    {
        _state = null;
        _svc = null;
        _scripted = false;
    }

    public static void Tick(double dt)
    {
        var s = _state;
        if (s == null) return;
        try
        {
            if (_scripted) TickFirstRun(s);
            else if (_runsAtStart == 1) TickSecondRun(s);
        }
        catch (Exception ex) { LogDebug("ChaosHappyPath.Tick: {E}", ex.Message); }
    }

    // ============================ hub / lifecycle beats ============================

    public static void OnDollhouseFirstOpen()
    {
        if (ChaosMeta.State.SeenDollhouse) return;
        try
        {
            ChaosMeta.State.SeenDollhouse = true;
            ChaosMeta.Save();
            AvaloniaChaosApp.Bark?.NotifyChaosDollhouseFirstOpen();
        }
        catch (Exception ex) { LogDebug("ChaosHappyPath.OnDollhouseFirstOpen: {E}", ex.Message); }
    }

    public static void OnFirstDescentStarted(ChaosRunState state)
    {
        if (ChaosMeta.State.RunsCompleted != 0) return;
        try
        {
            state.PushEvent("🐇 the first fall begins");
        }
        catch (Exception ex) { LogDebug("ChaosHappyPath.OnFirstDescentStarted: {E}", ex.Message); }
    }

    public static void OnRunResultsShown(ChaosRunState state, double baseXp, double skillMult, double finalXp, long previousBest, int sparks)
    {
        try
        {
            RevealService.Sync("run_complete");
            if (ChaosMeta.State.RunsCompleted == 1)
            {
                state.PushEvent("you came back up. the dollhouse is open now.");
            }
        }
        catch (Exception ex) { LogDebug("ChaosHappyPath.OnRunResultsShown: {E}", ex.Message); }
    }

    public static void OnGoldFirstSeen()
    {
        if (ChaosMeta.State.SeenGoldFirst) return;
        try
        {
            ChaosMeta.State.SeenGoldFirst = true;
            ChaosMeta.Save();
            _state?.PushEvent($"{ChaosGlyphs.Gold} gold. she takes it at her bench.");
            AvaloniaChaosApp.Bark?.NotifyChaosGoldFirst();
        }
        catch (Exception ex) { LogDebug("ChaosHappyPath.OnGoldFirstSeen: {E}", ex.Message); }
    }

    public static void OnFirstSinOffered(List<ChaosBoon> options, ChaosRunState state)
    {
        try
        {
            if (_firstSinDraftPending || ChaosMeta.State.SeenFirstSin || _runsAtStart < 3 || !state.Config.AllowCurses)
                return;
            if (options.Count == 0) return;
            var don = ChaosBoonPool.All.FirstOrDefault(b => b.Id == "double_or_nothing");
            if (don == null) return;
            if (!options.Any(o => o.Id == don.Id))
            {
                int idx = options.FindIndex(o => o.IsCurse);
                if (idx < 0) idx = options.Count - 1;
                options[idx] = don;
            }
            _shieldRigBoonId = don.Id;
            _firstSinDraftPending = true;
            AnnSvc?.AnnounceChaos("☠ a sin is on the table. the first taste is free. this once.",
                ChaosAnnounceKind.Temptation, holdMs: AvaloniaChaosService.TeachHoldMs);
        }
        catch (Exception ex) { LogDebug("ChaosHappyPath.OnFirstSinOffered: {E}", ex.Message); }
    }

    public static void OnSinAccepted()
    {
        OnDraftResolved();
    }

    public static void OnSkipDebutAvailable()
    {
        if (ChaosMeta.State.SeenSkipDebut) return;
        try
        {
            ChaosMeta.State.SeenSkipDebut = true;
            ChaosMeta.Save();
            AnnSvc?.AnnounceChaos("you're allowed to refuse now.", ChaosAnnounceKind.Willpower);
        }
        catch (Exception ex) { LogDebug("ChaosHappyPath.OnSkipDebutAvailable: {E}", ex.Message); }
    }

    public static void OnDuoDemoAvailable(List<ChaosBoon> options, ChaosRunState state)
    {
        try
        {
            if (ChaosMeta.State.SeenDuoDemo) return;
            if (!ChaosMeta.IsBoonActive("the_spanker") || !ChaosMeta.IsBoonActive("e_stim"))
                return;
            if (options.Count == 0) return;
            var duo = ChaosBoonPool.All.FirstOrDefault(b => b.Id == "electrified_rabbits");
            if (duo == null) return;
            if (!options.Any(o => o.Id == duo.Id))
                options[options.Count - 1] = duo;
            ChaosMeta.State.SeenDuoDemo = true;
            ChaosMeta.Save();
            AnnSvc?.AnnounceChaos("a gold card. your toys learned to work together.", ChaosAnnounceKind.Item,
                holdMs: AvaloniaChaosService.TeachHoldMs);
            try { AvaloniaChaosApp.Bark?.NotifyChaosDuoDemo(); } catch { }
        }
        catch (Exception ex) { LogDebug("ChaosHappyPath.OnDuoDemoAvailable: {E}", ex.Message); }
    }

    // ============================ run 1: the full script ============================

    private static void TickFirstRun(ChaosRunState s)
    {
        if (!_streakTaught && s.Combo >= R1_STREAK_TEACH_COMBO)
        {
            _streakTaught = true;
            AnnSvc?.AnnounceChaos("pops in a row build a streak. it pays more.", ChaosAnnounceKind.Streak,
                holdMs: AvaloniaChaosService.TeachHoldMs);
            s.PushEvent("🔥 a streak. keep it alive.");
        }

        if (!_threatSpawned && s.RunProgress >= R1_THREAT_AT_PROGRESS)
        {
            _threatSpawned = true;
            SpawnScriptedThreat(s);
        }

        if (!_draftFired && s.RunProgress >= R1_DRAFT_AT_PROGRESS
            && (_threatDefuseSeen || s.RunProgress >= R1_DRAFT_FALLBACK_PROGRESS))
        {
            _draftFired = FireScriptedDraft();
        }

        if (!_darterSpawned && s.RunProgress >= R1_DARTER_AT_PROGRESS)
        {
            _darterSpawned = true;
            try
            {
                AnnSvc?.AnnounceChaos("🐇 a white rabbit. catch it.", ChaosAnnounceKind.Item,
                    holdMs: AvaloniaChaosService.TeachHoldMs);
                ChaosMeta.MarkDiscovered("bubble:darter");
                global::ConditioningControlPanel.CoreApp.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildDarter(s.RunIntensity, spotlight: false));
            }
            catch (Exception ex) { LogDebug("HappyPath darter: {E}", ex.Message); }
        }
    }

    private static void SpawnScriptedThreat(ChaosRunState s)
    {
        try
        {
            if (!ChaosMeta.State.SeenDefuseTutorial)
            {
                ChaosMeta.State.SeenDefuseTutorial = true;
                ChaosMeta.Save();
                AnnSvc?.AnnounceChaos("press and HOLD a live one to snap it", ChaosAnnounceKind.Willpower,
                    holdMs: AvaloniaChaosService.TeachHoldMs);
                s.PushEvent("✋ hold to snap. let go and it triggers.");
            }
            var pink = ChaosBubbleVariants.All.FirstOrDefault(v => v.Id == "pink");
            if (pink == null) return;
            var spec = ChaosBubbleVariants.Build(pink, s.RunIntensity,
                s.FuseTimeMult * R1_THREAT_FUSE_MULT, null, s.Config.EffectIntensity, s.BubbleScale);
            ChaosMeta.MarkDiscovered("bubble:" + spec.VariantId);
            global::ConditioningControlPanel.CoreApp.Bubbles?.SpawnChaosBubble(spec);
            s.PushEvent("◉ one live one. take your time with it.");
        }
        catch (Exception ex) { LogDebug("HappyPath threat: {E}", ex.Message); }
    }

    public static void OnDefuseCompleted()
    {
        var s = _state;
        if (s == null || !_scripted) return;
        try
        {
            if (!_threatSpawned || _variantsJoined) { _threatDefuseSeen |= _threatSpawned; return; }
            _threatDefuseSeen = true;
            _variantsJoined = true;
            var enabled = s.Config.EnabledVariants;
            if (enabled != null)
            {
                if (!enabled.Contains("pink")) enabled.Add("pink");
                if (!enabled.Contains("spiral")) enabled.Add("spiral");
            }
            AnnSvc?.AnnounceChaos("pink and spiral drift in. hold them down too.", ChaosAnnounceKind.Item,
                holdMs: AvaloniaChaosService.TeachHoldMs);
            s.PushEvent("◉ more of them live now.");
        }
        catch (Exception ex) { LogDebug("HappyPath defuse beat: {E}", ex.Message); }
    }

    private static bool FireScriptedDraft()
    {
        try
        {
            var pool = R1_DRAFT_POOL
                .Select(id => ChaosBoonPool.All.FirstOrDefault(b => b.Id == id && !b.IsCurse))
                .Where(b => b != null)
                .Cast<ChaosBoon>()
                .OrderBy(_ => Random.Shared.Next())
                .Take(3)
                .ToList();
            if (pool.Count == 0) return true;
            return _svc?.TriggerScriptedDraft(pool) ?? true;
        }
        catch (Exception ex) { LogDebug("HappyPath draft: {E}", ex.Message); return true; }
    }

    // ============================ run 2: light debuts ============================

    private static void TickSecondRun(ChaosRunState s)
    {
        if (!_braindrainDebuted && s.RunProgress >= R2_BRAINDRAIN_AT_PROGRESS)
        {
            _braindrainDebuted = true;
            if (!ChaosMeta.State.SeenBraindrain && VariantAllowed(s, "braindrain"))
            {
                try
                {
                    ChaosMeta.State.SeenBraindrain = true;
                    ChaosMeta.Save();
                    var v = ChaosBubbleVariants.All.FirstOrDefault(x => x.Id == "braindrain");
                    if (v != null)
                    {
                        AnnSvc?.AnnounceChaos("◍ BRAINDRAIN. bigger. heavier. hold it down.", ChaosAnnounceKind.Item,
                            holdMs: AvaloniaChaosService.TeachHoldMs);
                        s.PushEvent("◍ something heavy sinks in beside you");
                        var spec = ChaosBubbleVariants.Build(v, s.RunIntensity,
                            s.FuseTimeMult * ChaosTuning.DEBUT_FUSE_MULT, null,
                            s.Config.EffectIntensity, s.BubbleScale);
                        ChaosMeta.MarkDiscovered("bubble:braindrain");
                        global::ConditioningControlPanel.CoreApp.Bubbles?.SpawnChaosBubble(spec);
                    }
                }
                catch (Exception ex) { LogDebug("HappyPath braindrain debut: {E}", ex.Message); }
            }
        }

        if (!_goldenSpawned && s.RunProgress >= R2_GOLDEN_AT_PROGRESS)
        {
            _goldenSpawned = true;
            try
            {
                ChaosMeta.MarkDiscovered("bubble:golden");
                global::ConditioningControlPanel.CoreApp.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildGolden());
                global::ConditioningControlPanel.CoreApp.Bubbles?.PlayChime(0.30f);
            }
            catch (Exception ex) { LogDebug("HappyPath golden: {E}", ex.Message); }
        }
    }

    private static bool VariantAllowed(ChaosRunState s, string id) =>
        s.Config.EnabledVariants == null || s.Config.EnabledVariants.Contains(id);

    // ============================ draft rigging (runs 4 / 5+) ============================

    public static void RigDraft(List<ChaosBoon> options, ChaosRunState state)
    {
        try
        {
            _draftsThisRun++;
            if (_draftsThisRun != 1 || options.Count == 0) return;

            OnFirstSinOffered(options, state);
            if (_firstSinDraftPending) return;

            OnDuoDemoAvailable(options, state);
        }
        catch (Exception ex) { LogDebug("ChaosHappyPath.RigDraft: {E}", ex.Message); }
    }

    public static bool ShouldShieldSin(string boonId)
    {
        if (_shieldRigBoonId == null || boonId != _shieldRigBoonId) return false;
        _shieldRigBoonId = null;
        return true;
    }

    public static void OnDraftResolved()
    {
        if (!_firstSinDraftPending) return;
        _firstSinDraftPending = false;
        try
        {
            ChaosMeta.State.SeenFirstSin = true;
            ChaosMeta.Save();
        }
        catch (Exception ex) { LogDebug("ChaosHappyPath.OnDraftResolved: {E}", ex.Message); }
    }

    private static void LogDebug(string message, params object?[] args)
    {
        try { App.Services?.GetRequiredService<ILogger<object>>().LogDebug(message, args); } catch { }
    }
}
