namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// The run-config deal (b4): request-run answers with the run knobs the page's game brain
/// needs, ported from WPF ChaosRunConfig (ChaosModels.cs:128-283) + BuildRunConfig
/// (DtrhHostService.cs:930-1040). The setup comes from the slot INDEX document (WPF
/// AppSettings parity — app-global), the meta from the active slot document.
///
/// Named limits (no greenfield subsystem exists; recorded on the board row):
/// - the reveal-driven difficulty clamp (ChaosModels.cs:239-258) needs RevealService —
///   the saved difficulty passes through; the hub gates pills visually (WPF's own comment,
///   DtrhHostService.cs:447-450), and FromSetup still clamps the run values.
/// - thoughtTexts: WPF reads the bouncing-text settings pool — none exists; an empty list
///   is sent and the page falls back to its default (chaosRun.js: applyRunConfig).
/// - screenShake: no greenfield setting; the WPF default (true) is sent.
/// </summary>
public static class DtrhRunConfig
{
    /// <summary>Per-difficulty payout/intensity scalar (ChaosModels.cs:276-283).</summary>
    public static double DifficultyMult(string difficulty) => difficulty switch
    {
        "Medium" => 1.3,
        "Hard" => 1.7,
        "Extreme" => 2.2,
        _ => 1.0, // Easy + anything unparseable (Enum.TryParse fallback, ChaosModels.cs:196)
    };

    // ---- sin-slot ramp (ChaosModels.cs:220-237): sins debut on the 3rd descent at 25%,
    // topping out at 50% by Slipping; the AllowCurses toggle clamps on top page-side. ----
    private const int SinDebutRuns = 2;
    private const int SinFullRuns = 10;
    private const double SinChanceDebut = 0.25;
    private const double SinChanceFull = 0.5;

    /// <summary>Happy-path default sin chance by lifetime completed descents
    /// (ChaosModels.cs:233-237). WPF NEVER reads a saved value — the ramp is authoritative.</summary>
    public static double DefaultSinChance(int runsCompleted)
    {
        if (runsCompleted < SinDebutRuns) return 0.0;
        if (runsCompleted >= SinFullRuns) return SinChanceFull;
        var t = (runsCompleted - SinDebutRuns) / (double)(SinFullRuns - SinDebutRuns);
        return SinChanceDebut + (SinChanceFull - SinChanceDebut) * t;
    }

    /// <summary>The run knob set (ChaosRunConfig fields the web run-config message ships).</summary>
    public sealed record Values
    {
        public string Difficulty { get; set; } = "Easy";
        public double DifficultyMult { get; set; } = 1.0;
        public int DurationSec { get; set; } = 180;
        /// <summary>The Bottomless Fall unlock: this descent ignores the clock — regions loop
        /// and deepen until the player wakes. Gated on owning endless_mode (ChaosModels.cs:134-135).</summary>
        public bool Endless { get; set; }
        public int WaveCount { get; set; } = 5;
        public string? MotionOverride { get; set; }
        public List<string>? EnabledVariants { get; set; }
        public double EffectIntensity { get; set; } = 0.85;
        public bool ColorFlashes { get; set; } = true;
        public bool ScreenShake { get; set; } = true;
        public bool BoonDraftEnabled { get; set; } = true;
        public bool AllowCurses { get; set; } = true;
        public bool DartersEnabled { get; set; } = true;
        public double FuseTimeMult { get; set; } = 1.0;
        public double BaseMult { get; set; } = 1.0;
        public double SparkGainMult { get; set; } = 1.0;
        public double SpawnRateMult { get; set; } = 1.0;
        public int DraftChoices { get; set; } = 3;
        public int DraftAutoResumeSec { get; set; } = 15; // ChaosModeService.DraftAutoResumeSecDefault
        public double SinChance { get; set; }
        public bool ScriptedFirstRun { get; set; }
        public double HitboxScale { get; set; } = 1.0;
        public bool MagnetEnabled { get; set; }
        public bool PopupHeartEnabled { get; set; }
        public bool PendulumSwing { get; set; }
    }

    /// <summary>One permanent upgrade's run-config effect (ChaosUpgrades.cs:38-93 — the
    /// six-row catalog, verbatim).</summary>
    private static readonly Dictionary<string, Action<Values>> UpgradeEffects = new()
    {
        ["slow_fuses"] = c => c.FuseTimeMult *= 1.15,
        ["silk_touch"] = c => { c.HitboxScale = 1.25; c.MagnetEnabled = true; },
        ["popup_notification"] = c => c.PopupHeartEnabled = true,
        ["pendulum_swing"] = c => c.PendulumSwing = true,
        ["draft4"] = c => c.DraftChoices = 4,
        ["extreme_tier"] = _ => { }, // unlock flag only — no in-run effect (ChaosUpgrades.cs:85-91)
    };

    /// <summary>The catalog's id set (ownedHabitIds filters to catalog-resolved ids,
    /// DtrhHostService.cs:998-1002).</summary>
    public static bool IsCatalogUpgrade(string id) => UpgradeEffects.ContainsKey(id);

    /// <summary>ChaosRunConfig.FromSettings (ChaosModels.cs:190-215) + ChaosMeta.ApplyTo
    /// (ChaosUpgrades.cs:360-366): saved setup, clamped, then every owned-and-active
    /// upgrade shapes the run. SinChance is the happy-path ramp (never a saved value).</summary>
    public static Values FromSetup(DtrhSlotIndex setup, DtrhSlotDocument meta)
    {
        // The Hourglass unlock widens the ceiling from 20 min to 2 hours; without it the old
        // preset ceiling still holds — a stale page can never write itself a longer fall
        // (ChaosModels.cs:203-204; ownership = IsOwned, ChaosUpgrades.cs:323).
        var durMax = meta.PurchasedUpgrades.Contains("custom_duration") ? 7200 : 1200;
        var cfg = new Values
        {
            Difficulty = IsKnownDifficulty(setup.Difficulty) ? setup.Difficulty : "Easy",
            DurationSec = Math.Clamp(setup.DurationSec, 60, durMax),
            // Deal-time ownership re-check (ChaosModels.cs:206).
            Endless = setup.Endless && meta.PurchasedUpgrades.Contains("endless_mode"),
            WaveCount = Math.Clamp(setup.WaveCount, 1, 12),
            MotionOverride = setup.Motion is "FloatUp" or "RainDown" or "RoamBounce" or "SideDrift"
                ? setup.Motion
                : null, // "Mixed" (and anything stale) = per-variant default (ChaosModels.cs:199)
            EnabledVariants = setup.EnabledVariants, // ClampVariants: passthrough since 2026-07 (ChaosModels.cs:260-274)
            EffectIntensity = Math.Clamp(setup.EffectIntensity, 0.2, 1.5),
            ColorFlashes = setup.ColorFlashes,
            BoonDraftEnabled = setup.BoonDraftEnabled,
            AllowCurses = setup.AllowCurses,
            DartersEnabled = setup.DartersEnabled,
            SinChance = DefaultSinChance(meta.RunsCompleted),
        };
        cfg.DifficultyMult = DifficultyMult(cfg.Difficulty);
        ApplyTo(cfg, meta);
        return cfg;
    }

    /// <summary>The scripted very-first descent (ChaosHappyPath.BuildFirstRunConfig,
    /// ChaosHappyPath.cs:77-89): treats only, Gentle air, drafts/curses/darters off,
    /// gentler spawn clock. Built raw — owned upgrades stay OUT of the classroom.</summary>
    public static Values FirstRun() => new()
    {
        ScriptedFirstRun = true,
        Difficulty = "Easy",
        DifficultyMult = 1.0,
        DurationSec = 180,
        WaveCount = 5,
        EnabledVariants = ["flash", "subliminal"],
        BoonDraftEnabled = false,
        AllowCurses = false,
        DartersEnabled = false,
        SpawnRateMult = 0.6, // R1_SPAWN_RATE_MULT (ChaosHappyPath.cs:20)
        SinChance = 0.0,
    };

    /// <summary>ChaosMeta.ApplyTo (ChaosUpgrades.cs:360-366): foreach purchased where
    /// active (not in DisabledUpgrades) → apply.</summary>
    public static void ApplyTo(Values cfg, DtrhSlotDocument meta)
    {
        foreach (var id in meta.PurchasedUpgrades)
        {
            if (!meta.DisabledUpgrades.Contains(id) && UpgradeEffects.TryGetValue(id, out var apply))
            {
                apply(cfg);
            }
        }
    }

    private static bool IsKnownDifficulty(string difficulty) =>
        difficulty is "Easy" or "Medium" or "Hard" or "Extreme";

    /// <summary>BuildRunConfig (DtrhHostService.cs:930-1040): the full run-config message
    /// payload — cfg knobs + meta-derived loadout/flags. thoughtTexts is empty (no
    /// bouncing-text pool in the greenfield — the page falls back to its default).</summary>
    public static object BuildRunConfigPayload(Values cfg, DtrhSlotDocument meta, int rankIndex)
    {
        return new
        {
            difficulty = cfg.Difficulty,
            difficultyMult = cfg.DifficultyMult,
            durationSec = cfg.DurationSec,
            waveCount = cfg.WaveCount,
            endless = cfg.Endless,   // The Bottomless Fall: no clock; regions loop + deepen until wake (DtrhHostService.cs:1043)
            effectIntensity = cfg.EffectIntensity,
            enabledVariants = cfg.EnabledVariants,
            motionOverride = cfg.MotionOverride,
            fuseTimeMult = cfg.FuseTimeMult,
            baseMult = cfg.BaseMult,
            sparkGainMult = cfg.SparkGainMult,
            spawnRateMult = cfg.SpawnRateMult,
            colorFlashes = cfg.ColorFlashes,
            screenShake = cfg.ScreenShake,
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
            ownedHabitIds = meta.PurchasedUpgrades
                .Where(id => !meta.DisabledUpgrades.Contains(id)
                             && id != "extreme_tier"
                             && id != "custom_duration"   // setup-shape unlocks, no in-run
                             && id != "endless_mode"      // effect -> keep them off the HUD rail (DtrhHostService.cs:1073-1074)
                             && IsCatalogUpgrade(id))
                .ToList(),
            rankIndex,
            runsCompleted = meta.RunsCompleted,
            equippedStartBoon = meta.EquippedStartBoon,
            thoughtTexts = Array.Empty<string>(),
            levels = meta.LifetimeBoonLevels,
            consumableSlots = meta.ConsumableSlots,
            discoveredCodexIds = meta.DiscoveredCodexIds.ToList(),
            craftedItems = meta.CraftedItems,
            pinnedBoonId = meta.PinnedBoon,
            denialArmed = meta.DenialArmed,
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
    }
}
