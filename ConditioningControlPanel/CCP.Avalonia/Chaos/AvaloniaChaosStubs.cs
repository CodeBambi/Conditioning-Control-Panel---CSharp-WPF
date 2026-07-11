using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Chaos;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Chaos;

#region legacy enums / identifiers

// ChaosRank enum relocated to CCP.Core/Services/Chaos/ChaosMetaPrimitives.cs (web-port S2a-2)
// so the portable DTRH meta bridge shares one source of truth; consumed here via the
// `using ConditioningControlPanel.Core.Services.Chaos;` above.
public enum ChaosBranch { Control, Greed, Depth }
public enum ChaosDifficulty { Easy, Medium, Hard, Extreme }

// Relocated from the deleted native-run AvaloniaChaosUnlockCards.cs (native DTRH strip
// 2026-07-11): the lifetime-boon pocket categories are META data the kept dollhouse economy
// (ChaosMetaService pockets/slots) still keys on (WPF Services/Chaos/ChaosLifetimeBoons.cs:8).
public enum ChaosBoonCategory { Skill, Accessory, Utility }
public static class RevealIds
{
    public const string Dollhouse          = "dollhouse";            // the hub itself (first descent done)
    public const string TabLookingGlass    = "tab_looking_glass";    // Slipping
    public const string SectionToys        = "section_toys";          // first toy pocket owned
    public const string SectionAccessories = "section_accessories";   // first accessory pocket owned
    public const string HerCorner          = "her_corner";            // bench stub in the Toybox (run 2+, until Looking Glass reveals)
    public const string PillTeasing        = "pill_teasing";          // Tempted
    public const string PillRelentless     = "pill_relentless";       // Entranced
    public const string PillInescapable    = "pill_inescapable";      // extreme_tier owned
    public const string StartPicker        = "start_picker";          // bench: the starting mantra
    public const string Diary              = "diary";                 // bench: the Diary
    public const string StatsPanel         = "stats_panel";           // bench: the stats panel
    public const string DraftSkip          = "draft_skip";            // run 3+
    public const string BenchToyPocket2    = "bench_toy_pocket_2";    // Devoted
    public const string BenchAccPocket2    = "bench_acc_pocket_2";    // Devoted
    public const string VariantVideo       = "variant_video";         // Entranced (run whitelist clamp)
    public const string VariantHtlink      = "variant_htlink";        // Entranced (run whitelist clamp)
    public const string Capstones          = "capstones";             // Devoted (final levels purchasable)
    public const string ExtremeTierRow     = "extreme_tier_buyable";  // Devoted (buyability; lesson stacks on top)
}

// BenchIds relocated to CCP.Core/Services/Chaos/ChaosMetaPrimitives.cs (web-port S2a-2).
#endregion

#region models

public sealed class ChaosUpgrade
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public string? Flavor { get; set; }
    public ChaosBranch Branch { get; set; } = ChaosBranch.Depth;
    public int Cost { get; set; }
    public string Glyph { get; set; } = "◈";
    public string? IconPath { get; set; }

    /// <summary>Mutates a freshly-built <see cref="ChaosRunConfig"/> at run start — owning
    /// the upgrade shapes every run (WPF ChaosUpgrades.cs:27 / effects :49-88).</summary>
    public Action<ChaosRunConfig> Apply { get; set; } = _ => { };
}

public sealed class ChaosLifetimeBoon
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public string? Flavor { get; set; }
    public string Glyph { get; set; } = "◈";
    public ChaosBoonCategory Category { get; set; } = ChaosBoonCategory.Skill;
    public int UnlockCost { get; set; }
    public int MaxLevel { get; set; } = 1;
    public int[] UpgradeCosts { get; set; } = Array.Empty<int>();
    public double[] LevelValues { get; set; } = Array.Empty<double>();
    public string ValueLabel { get; set; } = "{0}";
    public double ValueAt(int level) =>
        LevelValues.Length == 0 ? 0 : LevelValues[Math.Clamp(level, 1, LevelValues.Length) - 1];

    public bool IsActiveUse { get; set; }
    public double UseCooldownSec { get; set; }
    public ChaosRank RankFloor { get; set; } = ChaosRank.Curious;
    public string? CapstoneDesc { get; set; }

}

public sealed class ChaosRunConfig
{
    public ChaosPlayMode PlayMode { get; set; } = ChaosPlayMode.Story;
    public string Difficulty { get; set; } = "Easy";
    public string MotionMode { get; set; } = "Mixed";
    public int RunDurationSec { get; set; } = 180;
    public int WaveCount { get; set; } = 5;
    /// <summary>Enabled variant ids. Null = all enabled (WPF ChaosModels.cs:146-147).</summary>
    public List<string>? EnabledVariants { get; set; }
    public bool BoonDraftEnabled { get; set; } = true;
    public bool AllowCurses { get; set; } = true;
    public bool DartersEnabled { get; set; } = true;
    public double DifficultyMult { get; set; } = 1.0;
    public double SparkGainMult { get; set; } = 1.0;
    public double BaseMult { get; set; } = 1.0;
    public int StartingShields { get; set; } = 0;
    public double StartingFocus { get; set; } = 50;

    // ---- happy-path / WPF parity knobs (kept additive; defaults are safe defaults) ----
    public bool ScriptedFirstRun { get; set; }
    public double SpawnRateMult { get; set; } = 1.0;
    public double SinChance { get; set; } = 0.5;
    public double EffectIntensity { get; set; } = 1.0;
    /// <summary>Untouched-draft auto-SKIP timeout (WPF ChaosModeService.cs:94 DraftAutoResumeSecDefault = 15).</summary>
    public int DraftAutoResumeSec { get; set; } = 15;
    public bool AmbientMode { get; set; }
    public bool MagnetEnabled { get; set; }
    public double FuseTimeMult { get; set; } = 1.0;
    /// <summary>Default false — ONLY the popup_notification upgrade enables it (WPF ChaosModels.cs:174 / ChaosUpgrades.cs:64).</summary>
    public bool PopupHeartEnabled { get; set; }
    public bool PendulumSwing { get; set; }
    public double HitboxScale { get; set; } = 1.0;
    public int DraftChoices { get; set; } = 3;
    public bool ScreenShakeEnabled { get; set; } = true;
    public bool ColorFlashesEnabled { get; set; } = true;
    public double ShakeIntensity { get; set; } = 1.0;
    public ChaosMotion? MotionOverride { get; set; }

    /// <summary>Faithful port of WPF ChaosRunConfig.FromSettings (WPF ChaosModels.cs:189-231):
    /// sin-slot ramp first, the null-settings early path still applies owned upgrades, then
    /// the reveal clamps (difficulty pills, video/htlink variants), the numeric clamps, and
    /// ChaosMeta.ApplyTo at the end so every fresh run config carries owned upgrades.</summary>
    public static ChaosRunConfig FromSettings()
    {
        var cfg = new ChaosRunConfig();
        cfg.SinChance = ChaosRunRules.DefaultSinChance(ChaosMeta.State.RunsCompleted);   // WPF ChaosModels.cs:192
        var s = App.Services?.GetService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>()?.Current;
        if (s == null) { ChaosMeta.ApplyTo(cfg); return cfg; }                            // WPF ChaosModels.cs:193

        // Story mode is globally locked off until content ships; NarrativeModeEnabled is ignored.
        cfg.PlayMode = (AvaloniaChaosMode.StoryModeEnabled && s.NarrativeModeEnabled)
            ? ChaosPlayMode.Story
            : ChaosPlayMode.FreeDesktop;
        var saved = Enum.TryParse<ChaosDifficulty>(s.ChaosDifficulty, out var d) ? d : ChaosDifficulty.Easy;   // WPF ChaosModels.cs:194
        cfg.Difficulty = ClampDifficulty(saved).ToString();
        cfg.DifficultyMult = ChaosRunRules.DifficultyMultFor(cfg.Difficulty);            // WPF ChaosModels.cs:267-274
        cfg.RunDurationSec = ChaosRunRules.ClampDurationSec(s.ChaosRunDurationSec);      // WPF ChaosModels.cs:196
        cfg.WaveCount = ChaosRunRules.ClampWaveCount(s.ChaosWaveCount);                  // WPF ChaosModels.cs:197
        cfg.MotionMode = s.ChaosMotionMode;
        // "Mixed" (or anything unrecognised) parses to null = per-variant default motion (WPF ChaosModels.cs:198).
        cfg.MotionOverride = Enum.TryParse<ChaosMotion>(s.ChaosMotionMode, out var m) ? m : (ChaosMotion?)null;
        cfg.EnabledVariants = ClampVariants(s.ChaosEnabledVariants);                     // null = all (WPF ChaosModels.cs:199)
        cfg.ScreenShakeEnabled = s.ChaosScreenShakeEnabled;
        cfg.ColorFlashesEnabled = s.ChaosColorFlashesEnabled;
        cfg.ShakeIntensity = ChaosRunRules.ClampShakeIntensity(s.ChaosShakeIntensity);   // WPF ChaosModels.cs:200
        cfg.EffectIntensity = ChaosRunRules.ClampEffectIntensity(s.ChaosEffectIntensity); // WPF ChaosModels.cs:201
        cfg.BoonDraftEnabled = s.ChaosBoonDraftEnabled;
        cfg.AllowCurses = s.ChaosAllowCurses;
        cfg.DartersEnabled = s.ChaosDartersEnabled;
        ChaosMeta.ApplyTo(cfg);   // owned permanent upgrades shape every run (WPF ChaosModels.cs:210)
        return cfg;
    }

    /// <summary>
    /// Rank clamp for the run's difficulty (WPF ChaosModels.cs:225-243): if the SAVED pill is
    /// still locked, the run falls back to the highest unlocked one. The saved setting is never
    /// written — unlocking restores the user's own choice untouched. Gentle is always open;
    /// Teasing needs the PillTeasing reveal, Relentless PillRelentless, Inescapable PillInescapable.
    /// </summary>
    private static ChaosDifficulty ClampDifficulty(ChaosDifficulty saved)
    {
        static bool Unlocked(ChaosDifficulty d) => d switch
        {
            ChaosDifficulty.Extreme => RevealService.IsUnlocked(RevealIds.PillInescapable),
            ChaosDifficulty.Hard    => RevealService.IsUnlocked(RevealIds.PillRelentless),
            ChaosDifficulty.Medium  => RevealService.IsUnlocked(RevealIds.PillTeasing),
            _                       => true,
        };
        var d = saved;
        while (d > ChaosDifficulty.Easy && !Unlocked(d)) d--;
        return d;
    }

    /// <summary>
    /// Rank clamp for the run's bubble pool (WPF ChaosModels.cs:245-260): the <c>video</c> /
    /// <c>htlink</c> variants only enter a run once their reveals unlock. Returns the saved
    /// list untouched when both are open (may stay null = all); otherwise a NEW narrowed list
    /// — the saved setting is never mutated.
    /// </summary>
    private static List<string>? ClampVariants(List<string>? saved)
    {
        bool videoOk = RevealService.IsUnlocked(RevealIds.VariantVideo);
        bool htOk = RevealService.IsUnlocked(RevealIds.VariantHtlink);
        if (videoOk && htOk) return saved;
        var list = new List<string>(saved ?? ChaosSpawnCatalog.AllIds());
        if (!videoOk) list.Remove("video");
        if (!htOk) list.Remove("htlink");
        return list;
    }
}
public sealed class BubblePreset
{
    public string Name { get; set; } = "";
    public List<string> VariantIds { get; set; } = new();
}

#endregion

#region static facades over DI services

public static class ChaosMeta
{
    private static IChaosMetaService? Service => App.Services?.GetService<IChaosMetaService>();

    public static ChaosMetaState State
    {
        get => Service?.State ?? new ChaosMetaState();
        set
        {
            var svc = Service;
            if (svc != null) svc.State = value;
        }
    }

    public static string Rank => Service?.Rank ?? ChaosRanks.Name(ChaosRank.Curious);
    public static ChaosRank CurrentRank => Service?.CurrentRank ?? ChaosRank.Curious;
    public static int RankIndex => Service?.RankIndex ?? 0;

    /// <summary>One-time "first fall" bonus on the very first completed descent
    /// (WPF ChaosUpgrades.cs:106 FIRST_FALL_BONUS = 25; named on the recap card).</summary>
    public const int FIRST_FALL_BONUS = 25;

    public static void Init(IAppEnvironment env) => Service?.Init(env);
    public static void Save() => Service?.Save();
    public static bool AtLeast(ChaosRank rank) => Service?.AtLeast(rank) ?? false;
    public static void AddGold(int amount) => Service?.AddGold(amount);
    public static bool TrySpendGold(int amount) => Service?.TrySpendGold(amount) ?? false;
    public static void EquipStartBoon(string? boonId) => Service?.EquipStartBoon(boonId);
    /// <summary>Apply every owned-and-switched-on upgrade's effect to a freshly-built run config
    /// (WPF ChaosUpgrades.cs:312-318).</summary>
    public static void ApplyTo(ChaosRunConfig config) => Service?.ApplyTo(config);
    public static void MarkDiscovered(string codexId) => Service?.MarkDiscovered(codexId);
    public static bool IsDiscovered(string codexId) => Service?.IsDiscovered(codexId) ?? false;
    public static bool IsOwned(string id) => Service?.IsOwned(id) ?? false;
    public static bool IsUpgradeActive(string id) => Service?.IsUpgradeActive(id) ?? false;
    public static void SetUpgradeActive(string id, bool active) => Service?.SetUpgradeActive(id, active);
    public static bool CanAfford(string id) => Service?.CanAfford(id) ?? false;
    public static bool CanAffordUnlock(string id) => Service?.CanAffordUnlock(id) ?? false;
    public static bool CanAffordUpgrade(string id) => Service?.CanAffordUpgrade(id) ?? false;
    public static bool IsPurchaseRankLocked(string id) => Service?.IsPurchaseRankLocked(id) ?? false;
    public static bool IsBoonRankLocked(string id) => Service?.IsBoonRankLocked(id) ?? false;
    public static bool IsAccessoryScriptLocked(string id) => Service?.IsAccessoryScriptLocked(id) ?? false;
    public static bool IsBoonUnlocked(string id) => Service?.IsBoonUnlocked(id) ?? false;
    public static bool IsBoonActive(string id) => Service?.IsBoonActive(id) ?? false;
    public static void SetBoonActive(string id, bool active) => Service?.SetBoonActive(id, active);
    public static int BoonLevel(string id) => Service?.BoonLevel(id) ?? 0;
    public static bool TryUnlockBoon(string id) => Service?.TryUnlockBoon(id) ?? false;
    public static bool TryUpgradeBoon(string id) => Service?.TryUpgradeBoon(id) ?? false;
    public static bool TryPurchase(string id) => Service?.TryPurchase(id) ?? false;
    public static bool HasFreePocket(ChaosBoonCategory cat) => Service?.HasFreePocket(cat) ?? false;
    public const int MAX_POCKETS_PER_CATEGORY = 2;
    public static int SlotsFor(ChaosBoonCategory cat) => Service?.SlotsFor(cat) ?? 0;
    public static int EquippedCountIn(ChaosBoonCategory cat) => Service?.EquippedCountIn(cat) ?? 0;
    public static (string Name, bool Affordable, string? LessonId, int Cost)? NextGoal() => Service?.NextGoal();
    public static void DebugResetState() => Service?.DebugResetState();
}
public static class RevealService
{
    private static IRevealService? Service => App.Services?.GetService<IRevealService>();

    public static event Action<string>? Pending
    {
        add
        {
            var svc = Service;
            if (svc != null) svc.Pending += value;
        }
        remove
        {
            var svc = Service;
            if (svc != null) svc.Pending -= value;
        }
    }

    public static bool IsUnlocked(string id) => Service?.IsUnlocked(id) ?? true;
    public static bool IsPending(string id) => Service?.IsPending(id) ?? false;
    public static bool IsSeen(string id) => Service?.IsSeen(id) ?? false;
    public static bool Clamp(string id, bool userSetting) => Service?.Clamp(id, userSetting) ?? false;
    public static void Sync(string reason) => Service?.Sync(reason);
    public static IReadOnlyList<string> PendingIds() => Service?.PendingIds() ?? new List<string>();
    public static void MarkSeen(string id) => Service?.MarkSeen(id);
}
public static class ChaosRanks
{
    // ---- thresholds (lifetime completed descents) — WPF parity (ChaosRanks.cs:22) ----
    public static int[] Thresholds { get; } = { 0, 3, 10, 25, 50, 100 };

    /// <summary>[LOCKED] generic tooltip for anything visible but above the player's rank. WPF parity.</summary>
    public static string RankLockedTip => "she'll sell this to someone deeper.";

    /// <summary>[LOCKED] tooltip for a capstone (final) boon level before Devoted. WPF parity.</summary>
    public static string CapstoneLockedTip => "the last stitch is hers to give. she gives it to the devoted.";

    public static ChaosRank For(int runsCompleted)
    {
        var r = ChaosRank.Curious;
        for (int i = Thresholds.Length - 1; i >= 0; i--)
            if (runsCompleted >= Thresholds[i]) { r = (ChaosRank)i; break; }
        return r;
    }

    /// <summary>Lowercase rank word — the recap card renders this bare and huge (WPF parity).</summary>
    public static string NameLower(ChaosRank rank) => rank switch
    {
        ChaosRank.Tempted   => "tempted",
        ChaosRank.Slipping  => "slipping",
        ChaosRank.Entranced => "entranced",
        ChaosRank.Devoted   => "devoted",
        ChaosRank.Claimed   => "claimed",
        _                   => "curious",
    };

    /// <summary>Capitalized rank word for the dollhouse top bar (WPF parity).</summary>
    public static string Name(ChaosRank rank) => rank switch
    {
        ChaosRank.Tempted   => "Tempted",
        ChaosRank.Slipping  => "Slipping",
        ChaosRank.Entranced => "Entranced",
        ChaosRank.Devoted   => "Devoted",
        ChaosRank.Claimed   => "Claimed",
        _                   => "Curious",
    };

    /// <summary>[LOCKED] one line under the bare rank word on the rank card. WPF parity (ChaosRanks.cs:74).</summary>
    public static string Line(ChaosRank rank) => rank switch
    {
        ChaosRank.Tempted   => "tempted. three times down. you can stop calling it curiosity.",
        ChaosRank.Slipping  => "slipping. the climb out takes longer every time. you noticed. you came anyway.",
        ChaosRank.Entranced => "entranced. you don't fall anymore. you arrive.",
        ChaosRank.Devoted   => "devoted. the dollhouse keeps a room warm for you now. it always knew it would.",
        ChaosRank.Claimed   => "claimed. it stopped counting your visits a long time ago. so did you.",
        _                   => "",
    };

    /// <summary>Hover specifics for a rank gate: exact rank, exact descent count, live progress. WPF parity.</summary>
    public static string RankSpecifics(ChaosRank needed)
    {
        int need = Thresholds[Math.Clamp((int)needed, 0, Thresholds.Length - 1)];
        int have = ChaosMeta.State.RunsCompleted;
        return $"unlocks at {Name(needed)}: {need} descents finished. you've finished {have}.";
    }
}
public static class ChaosUpgrades
{
    public static List<ChaosUpgrade> All { get; } = new();
    public static ChaosUpgrade? ById(string id) => All.FirstOrDefault(x => x.Id == id);
}
public static class ChaosLifetimeBoons
{
    public static List<ChaosLifetimeBoon> All { get; } = new();
    public static IEnumerable<ChaosLifetimeBoon> InCategory(ChaosBoonCategory cat) => All.Where(b => b.Category == cat);
    public static ChaosLifetimeBoon? ById(string id) => All.FirstOrDefault(x => x.Id == id);

    /// <summary>Drip Feed's per-descent trickle ceiling by per-pop value (== level, 1..4):
    /// 60/90/120/150✦. The cap bounds the Relapse-loop doubling too — the trickle is a
    /// floor-raiser, never a second economy (verbatim WPF ChaosLifetimeBoons.cs:412).</summary>
    public static int DripFeedCap(int dropPerPop) => 30 + 30 * Math.Clamp(dropPerPop, 1, 4);

    /// <summary>Gold paid by a lucky golden bubble at a Rabbit's Foot level (0 = unworn).
    /// Scales per level; the capstone is the doubled base range
    /// (verbatim WPF ChaosLifetimeBoons.cs:416-424).</summary>
    public static (int Min, int Max) GoldenPayRange(int level) => level switch
    {
        <= 0 => (10, 20),
        1    => (12, 24),
        2    => (14, 28),
        3    => (16, 32),
        _    => (20, 40),   // level 4: the gold doubles
    };
}
public static class ChaosBubbleVariants
{
    public sealed class Variant
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public Color Tint { get; set; }
        public bool IsLive { get; set; }
    }

    public static List<Variant> All { get; } = new();
    public static string DescriptionFor(string id) => "";
    public static List<BubblePreset> Presets { get; } = new();

    /// <summary>Build a variant spec from its catalog definition.</summary>
    public static ChaosBubbleSpec Build(Variant variant, double intensity, double fuseTimeMult = 1.0,
        ChaosMotion? motionOverride = null, double effectIntensity = 1.0, double sizeScale = 1.0,
        double sideDriftChance = 0.0)
    {
        var rng = Random.Shared;
        double size = 80 + rng.NextDouble() * 80;
        bool isLive = variant.IsLive;
        int fuseMs = isLive ? (int)(4000 * fuseTimeMult * (1.1 - intensity * 0.2)) : 0;
        var motion = motionOverride ?? variant.Id switch
        {
            "flash" => ChaosMotion.FloatUp,
            "subliminal" => ChaosMotion.RainDown,
            "braindrain" => ChaosMotion.RoamBounce,
            "pink" => ChaosMotion.RoamBounce,
            "spiral" => ChaosMotion.RoamBounce,
            _ => rng.Next(3) switch { 0 => ChaosMotion.FloatUp, 1 => ChaosMotion.RainDown, _ => ChaosMotion.RoamBounce }
        };
        var tint = variant.Tint;
        return new ChaosBubbleSpec
        {
            VariantId = variant.Id,
            PayloadKind = variant.Id,
            SizePx = size * sizeScale,
            IsLive = isLive,
            FuseMs = Math.Max(500, fuseMs),
            Motion = motion,
            SpeedMult = 1.0 + rng.NextDouble() * 0.5,
            EffectIntensity = effectIntensity,
            SideDriftChance = sideDriftChance,
            TintR = tint.R, TintG = tint.G, TintB = tint.B,
        };
    }

    /// <summary>Build a lucky golden income bubble spec.</summary>
    public static ChaosBubbleSpec BuildGolden()
    {
        var rng = Random.Shared;
        return new ChaosBubbleSpec
        {
            VariantId = "golden",
            PayloadKind = "golden",
            IsGolden = true,
            SizePx = 110 + rng.NextDouble() * 30,
            Motion = rng.NextDouble() < 0.5 ? ChaosMotion.FloatUp : ChaosMotion.RainDown,
            SpeedMult = 2.8,
            TintR = 0xFF, TintG = 0xD7, TintB = 0x00,
        };
    }

    /// <summary>Builds a white-rabbit darter bubble spec for the Rabbit Caller active toy.</summary>
    public static ChaosBubbleSpec BuildDarter(double intensity = 1.0, bool spotlight = false,
        double? atPxX = null, double? atPxY = null)
    {
        var rng = Random.Shared;
        return new ChaosBubbleSpec
        {
            VariantId = "darter",
            PayloadKind = "darter",
            IsDarter = true,
            SizePx = 70 + rng.Next(40),
            Motion = ChaosMotion.RoamBounce,
            SpeedMult = 1.0,
            DarterSpeed = 360 * intensity,
            DarterMaxBounces = 3,
            TelegraphMs = 500,
            LifetimeMs = 6000,
            Spotlight = spotlight,
            SpawnAtPxX = atPxX,
            SpawnAtPxY = atPxY,
            TintR = 0xFF, TintG = 0xFF, TintB = 0xFF,
        };
    }

    /// <summary>Build one Echo split-child at the parent's pop point: a NORMAL live from the light
    /// trio (pink/spiral/braindrain), smaller, faster, with a short fuse. Children carry no IsEcho
    /// flag, so they never re-split. Mirrors the WPF ChaosBubbleVariants.BuildEchoChild behaviour.</summary>
    public static ChaosBubbleSpec BuildEchoChild(double parentVisualSizePx, double atPxX, double atPxY,
        double effectIntensity = 1.0)
    {
        var rng = Random.Shared;
        // Rows 2..4 in the seeded catalog are pink / spiral / braindrain (the light live trio).
        var v = All[2 + rng.Next(Math.Min(3, Math.Max(1, All.Count - 2)))];
        double size = Math.Max(60, parentVisualSizePx * ChaosTuning.EchoChildScale);
        int fuse = ChaosTuning.EchoChildFuseMinMs
                   + rng.Next(Math.Max(1, ChaosTuning.EchoChildFuseMaxMs - ChaosTuning.EchoChildFuseMinMs));
        return new ChaosBubbleSpec
        {
            SpawnAtPxX = atPxX,
            SpawnAtPxY = atPxY,
            VariantId = v.Id,
            PayloadKind = v.Id,
            SizePx = size,
            IsLive = true,
            FuseMs = fuse,
            Motion = ChaosMotion.RoamBounce,
            SpeedMult = ChaosTuning.EchoChildSpeedMult,
            TintR = v.Tint.R, TintG = v.Tint.G, TintB = v.Tint.B,
            EffectIntensity = effectIntensity,
        };
    }
}

#endregion

#region typed app-service facades

public static class AvaloniaChaosApp
{
    public static IChaosService? Chaos => App.Services?.GetService<IChaosService>();
    public static IAvatarWindowService? Avatar => App.Services?.GetService<IAvatarWindowService>();
    public static IBarkService? Bark => App.Services?.GetService<IBarkService>();
    public static IVideoInfo? Video => App.Services?.GetService<IVideoInfo>();
    public static Window? MainWindowRef => App.Services?.GetService<IMainWindowService>()?.MainWindow as Window;
}

#endregion
