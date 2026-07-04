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

/// <summary>The rank spine — the single source of truth for depth ranks. Order and
/// ordinals mirror WPF <c>ChaosRanks.cs</c> exactly (Entranced before Devoted; no Lost).</summary>
public enum ChaosRank { Curious = 0, Tempted = 1, Slipping = 2, Entranced = 3, Devoted = 4, Claimed = 5 }
public enum ChaosBranch { Control, Greed, Depth }
public enum ChaosRarity { Common, Uncommon, Rare }
public enum ChaosSpeaker { Madam, Rabbit, Hatter, Doll, Enemy }

// ---- narrative layer enums (mirror WPF ChaosNarrativeModels) ----
public enum ChaosBand { Ambient = 0, Reactive = 1, Story = 2 }
public enum ChaosLineMode { Once, Pooled }
public enum ChaosRegister { Hub, DescentHigh, DescentLow }
public enum ChaosDepthMatch { Min, Exact, Range }
public enum ChaosConversationMode { Once, Repeatable }

public enum ChaosDifficulty { Easy, Medium, Hard, Extreme }

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

public static class BenchIds
{
    public const string ToyPocket1 = "toy_pocket_1";
    public const string AccPocket1 = "acc_pocket_1";
    public const string StartMantra = "start_mantra";
    public const string Diary = "diary";
    public const string StatsPanel = "stats_panel";
    public const string ToyPocket2 = "toy_pocket_2";
    public const string AccPocket2 = "acc_pocket_2";
}

public static class ChaosGlyphs
{
    public const string Xp = "🕰";
    public const string Drops = "✦";
    public const string Gold = "🪙";
}

#endregion

#region models

public sealed class ChaosBoon : IChaosDraftCard
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public string? Flavor { get; set; }
    public bool IsCurse { get; set; }
    public ChaosRarity Rarity { get; set; } = ChaosRarity.Common;
    public string[]? RequiresAny { get; set; }
    public string[]? RequiresAll { get; set; }

    /// <summary>Run-multiplier bonus added when this boon is taken (WPF parity).</summary>
    public double RunMultBonus { get; set; }

    /// <summary>Effect applied when this boon is drafted or equipped as a start boon.</summary>
    public Action<ChaosRunState>? Apply { get; set; }

    /// <summary>Curse-only: the sin's upside when Surrender's capstone waives the drawback.</summary>
    public Action<ChaosRunState>? ApplyShielded { get; set; }

    /// <summary>One-shot boons: can only be taken once per run.</summary>
    public bool Unique { get; set; }
}

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

    /// <summary>Effect applied when this lifetime boon is active at run start; the double is the level value.</summary>
    public Action<ChaosRunState, double>? Apply { get; set; }

    /// <summary>Capstone-only alternate apply when Surrender shields a sin drawback.</summary>
    public Action<ChaosRunState, double>? ApplyShielded { get; set; }
}

public sealed class ChaosConversationLine
{
    public string Text { get; set; } = "";
    public string? AudioKey { get; set; }
    public bool Emphasis { get; set; }
}

public sealed class ChaosConversation
{
    public string Id { get; set; } = "";
    public string Trigger { get; set; } = "";
    public ChaosSpeaker Speaker { get; set; } = ChaosSpeaker.Madam;
    public string? Title { get; set; }
    public ChaosRegister Register { get; set; } = ChaosRegister.Hub;
    public ChaosConversationMode Mode { get; set; } = ChaosConversationMode.Once;
    public ChaosLineGate? Gates { get; set; }
    public string PortraitId { get; set; } = "madam";
    public bool PortraitOnLeft { get; set; }
    public List<ChaosConversationLine> Lines { get; set; } = new();
}

public sealed class ChaosLineGate
{
    public int RankMin { get; set; }
    public ChaosDepthMatch? DepthMatch { get; set; }
    public int DepthA { get; set; }
    public int DepthB { get; set; }
    public bool? FirstTime { get; set; }
    public string? ItemOwned { get; set; }
    public string? SinId { get; set; }
    public string? RunStatKey { get; set; }
    public double RunStatMin { get; set; }
}

public sealed class ChaosNarrativeCue
{
    public string Id { get; set; } = "";
    public string Trigger { get; set; } = "";
    public ChaosSpeaker Speaker { get; set; } = ChaosSpeaker.Madam;
    public ChaosBand Band { get; set; } = ChaosBand.Reactive;
    public ChaosLineMode Mode { get; set; } = ChaosLineMode.Pooled;
    public ChaosRegister Register { get; set; } = ChaosRegister.DescentHigh;
    public ChaosLineGate? Gates { get; set; }
    public int CooldownMs { get; set; }
    public int Weight { get; set; } = 1;
    public string Text { get; set; } = "";
    public string? AudioKey { get; set; }
}

public sealed class ChaosToyState : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string Glyph { get; set; } = "◈";
    public string KeyLabel { get; set; } = "Q";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public string? CapstoneDesc { get; set; }
    public string? Flavor { get; set; }
    public double CooldownSec { get; set; }

    private int _chargesLeft = -1;
    public int ChargesLeft
    {
        get => _chargesLeft;
        set { _chargesLeft = value; Refresh(); }
    }

    private double _cooldownRemainingSec;
    public double CooldownRemainingSec
    {
        get => _cooldownRemainingSec;
        set { _cooldownRemainingSec = Math.Max(0, value); Refresh(); }
    }

    private bool _isEffectActive;
    public bool IsEffectActive
    {
        get => _isEffectActive;
        set { if (_isEffectActive == value) return; _isEffectActive = value; OnChanged(nameof(IsEffectActive)); Refresh(); }
    }

    public bool IsReady => CooldownRemainingSec <= 0 && ChargesLeft != 0;
    public string StatusText => ChargesLeft >= 0
        ? (ChargesLeft == 0 ? "spent" : $"{ChargesLeft} left")
        : CooldownRemainingSec > 0 ? $"{Math.Ceiling(CooldownRemainingSec):0}s" : "ready";
    public string ButtonLabel => $"{Glyph} {Name} · {KeyLabel}";

    private void Refresh()
    {
        OnChanged(nameof(ChargesLeft));
        OnChanged(nameof(CooldownRemainingSec));
        OnChanged(nameof(IsReady));
        OnChanged(nameof(StatusText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public void RaiseChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void OnChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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

public sealed class ChaosRunState : INotifyPropertyChanged
{
    public int Shields { get; set; }
    public bool FocusLow { get; set; }
    public int Combo { get; set; }
    public double BoonMult { get; set; } = 1.0;
    public double Heat { get; set; }

    // ---- multiplier stack (WPF ChaosModels.cs:524-535): chaos-local, computed live;
    //      skill/pink-rush applied once at payout ----
    /// <summary>Base of the stack — Golden Touch writes <c>Config.BaseMult</c> at boon-apply time (WPF ChaosModels.cs:524).</summary>
    public double BaseMult => Config.BaseMult;
    /// <summary>Streak multiplier: +0.08 per combo step, capped at x6 (WPF ChaosModels.cs:525).</summary>
    public double ComboMult => Math.Min(1.0 + Combo * 0.08, 6.0);
    /// <summary>Per-difficulty payout scalar, read from the run config (WPF ChaosModels.cs:526).</summary>
    public double DifficultyMult => Config.DifficultyMult;
    /// <summary>Up to x2 at full heat (WPF ChaosModels.cs:527).</summary>
    public double HeatMult => 1.0 + Heat;
    /// <summary>The full chaos-local multiplier stack (WPF ChaosModels.cs:530).</summary>
    public double TotalMult => BaseMult * ComboMult * DifficultyMult * HeatMult * BoonMult * UrgeMult;
    /// <summary>Skill-tree multiplier (incl. Pink Rush) — informational; applied once at payout (WPF ChaosModels.cs:535).</summary>
    public double SkillMult => App.Services?.GetService<ISkillTreeService>()?.GetTotalXpMultiplier() ?? 1.0;
    public bool RippleReady { get; set; }
    public string ClockText { get; set; } = "0:00";
    public string ScoreText { get; set; } = "0";
    public string TotalMultText { get; set; } = "x1.0";
    public string FocusText { get; set; } = "50 / 100";
    public double Focus { get; set; } = 50;
    public double FocusMax { get; set; } = 100;
    public string RippleText { get; set; } = "READY";
    public string ShieldText { get; set; } = "0 ♥";

    // ---- hold-to-defuse channel state ----
    public bool IsChanneling { get; set; }
    public DateTime ChannelStartTime { get; set; }
    public string? ChannelTargetBubbleId { get; set; }
    public double ChannelHeldSec { get; set; }
    public string ChannelText { get; set; } = "";

    /// <summary>Run completion 0..1, computed from the clock (WPF ChaosModels.cs:485).</summary>
    public double RunProgress => Math.Clamp(ElapsedSec / Math.Max(1.0, RunDurationSec), 0, 1);
    /// <summary>0..1 escalation curve used to scale spawn rate, fuse, strength, live-share (WPF ChaosModels.cs:487).</summary>
    public double RunIntensity => RunProgress;
    public string RunTimeText { get; set; } = "0:00";
    public string ActWaveText { get; set; } = "I · 1";
    public double RunDurationSec { get; set; } = 180;
    public int WaveCount { get; set; } = 5;
    public double ElapsedSec { get; set; }

    /// <summary>Relapse sin: bolt one more loop onto the end of the run. That loop pays
    /// double drops and gold (<see cref="RelapseLoopActive"/> read at every gold/drop bank).
    /// Verbatim WPF ChaosModels.cs:494-502.</summary>
    public void ExtendOneLoop()
    {
        int waveLen = (int)Math.Round((double)RunDurationSec / Math.Max(1, WaveCount));
        WaveCount += 1;
        RunDurationSec += waveLen;
        RelapseLoopActive = true;
    }
    public int ActIndex { get; set; } = 1;
    public int WaveIndex { get; set; } = 1;
    public int BestCombo { get; set; }
    public int Defused { get; set; }
    public int Detonated { get; set; }
    public int EffectsFired { get; set; }
    public double Score { get; set; }
    public ChaosRunConfig Config { get; set; } = new();

    public List<ChaosSidebarBoon> ActiveSidebarToys { get; set; } = new();
    public List<ChaosSidebarBoon> ActiveSidebarAccessories { get; set; } = new();
    public List<ChaosSidebarBoon> RunPickTiles { get; set; } = new();
    public List<ChaosSidebarBoon> RunModifiers { get; set; } = new();
    public List<ChaosBoon> ActiveBoons { get; set; } = new();
    public List<ChaosBoon> ActiveCurses { get; set; } = new();
    public List<ChaosToyState> ActiveToys { get; set; } = new();
    public List<string> RecentEvents { get; set; } = new();

    #region boon / curse tuning knobs

    public double FuseTimeMult { get; set; } = 1.0;
    public bool MagnetEnabled { get; set; }
    public double DefuseInvulnMs { get; set; }
    public bool AllLiveNextWave { get; set; }
    public double ChainReactionReach { get; set; } = 1.0;
    public double LuckBonus { get; set; }
    public HashSet<string> MaxedBoons { get; set; } = new();
    public Dictionary<string, double> ToyPower { get; set; } = new();
    public int CollarSaves { get; set; }
    public double SlowMoBonusSec { get; set; }
    public double BlindfoldPayMult { get; set; } = 1.0;
    public bool BlindfoldActive { get; set; }
    public double BlindfoldOpacity { get; set; } = 1.0;
    public double SinExtraMult { get; set; }
    public bool SurrenderShieldUsed { get; set; }
    public double RabbitRateMult { get; set; } = 1.0;
    public double LastBreathWindowSec { get; set; }
    public double LastBreathPayMult { get; set; } = 1.0;
    public double ChanceDoubleOdds { get; set; }
    public int RerollsLeft { get; set; }
    public double CursorPullStrength { get; set; }
    public bool SpankerActive { get; set; }
    public double SpankGrowFactor { get; set; } = 1.0;
    public double IntrusiveThoughtsSec { get; set; }
    public double GoldenChance { get; set; } = 0.005;
    public int DropPerPop { get; set; }
    public long TrickleDrops { get; set; }
    public bool ShowPopScores { get; set; }
    public bool ShowWaveTimer { get; set; }
    public int ShieldRegenPops { get; set; }
    public double BenignBaseline { get; set; } = 0.4;
    public double BubbleScale { get; set; } = 1.0;
    public int StartShields { get; set; }
    public double RippleRechargeSec { get; set; } = 15.0;
    public double RippleRadiusPx { get; set; } = 400.0;
    public double RippleLifeMs { get; set; } = 1200.0;

    // ---- run-boon (mantra / sin) flags ----
    public bool GoldDiggerEnabled { get; set; }
    public bool WelcomeShowerEnabled { get; set; }
    public int HeavyDropEvery { get; set; }
    public double GgRabbitChance { get; set; }
    public bool RippleEnabled { get; set; }
    public bool AftermathEnabled { get; set; }
    public int EStimChargeMult { get; set; } = 1;
    public double AfterglowSec { get; set; }
    public int DvdSplitBounces { get; set; }
    public double RabbitTrailSec { get; set; }
    public bool UnleashedEnabled { get; set; }
    public bool ElectrifiedRabbits { get; set; }
    public double EStimShockwaveChance { get; set; }
    public double PendulumPayMult { get; set; } = 1.0;
    public double DetonationDurationMult { get; set; } = 1.0;
    public bool LastSecondGoldEnabled { get; set; }
    public double PrismChance { get; set; }
    public bool PrismTreatOnly { get; set; }
    public double CamGirlFlee { get; set; }
    public double CamGirlTipChance { get; set; }
    public double UrgeMult { get; set; } = 1.0;
    public bool ActivesDisabled { get; set; }
    public bool RelapseLoopArmed { get; set; }
    public bool RelapseLoopActive { get; set; }

    // ---- generic runtime bonuses for the minimal common-effect boons ----
    public double ComboMultBonus { get; set; }

    #endregion

    public event PropertyChangedEventHandler? PropertyChanged;
    public void RaiseChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void PushEvent(string text)
    {
        RecentEvents.Add(text);
        if (RecentEvents.Count > 40) RecentEvents.RemoveAt(0);
    }

    /// <summary>Applies a drafted boon or start-boon effect to this run state, pushes the
    /// HUD-strip pick tile and the feed line (verbatim WPF ChaosModels.cs:598-620).</summary>
    public void ApplyBoon(ChaosBoon boon, bool shieldDrawback = false)
    {
        if (boon == null) return;
        if (shieldDrawback) boon.ApplyShielded?.Invoke(this);
        else boon.Apply?.Invoke(this);
        BoonMult += boon.RunMultBonus;
        (boon.IsCurse ? ActiveCurses : ActiveBoons).Add(boon);
        RunPickTiles.Add(new ChaosSidebarBoon
        {
            Id = boon.Id,   // carry the id so the ribbon tile colors by payload family (WPF ChaosModels.cs:610)
            Icon = ChaosArt.Resolve("boons", boon.Id),
            Glyph = boon.IsCurse ? "☠" : "◈",
            Name = boon.Name,
            Desc = boon.Desc,
            Flavor = boon.Flavor ?? "",
            IsCurse = boon.IsCurse,
        });
        PushEvent($"{(boon.IsCurse ? "☠" : "◈")} {boon.Name}");
    }
}

public sealed class BubblePreset
{
    public string Name { get; set; } = "";
    public List<string> VariantIds { get; set; } = new();
}

#endregion

#region static service stubs

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
    public static void ApplyLifetimeBoons(ChaosRunState run) => Service?.ApplyLifetimeBoons(run);
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

public static class ChaosBoonPool
{
    public static List<ChaosBoon> All { get; } = new();

    private static readonly Random _rng = new();   // WPF ChaosModels.cs:400

    /// <summary>
    /// Deal a draft from the seeded pool — thin head-side wrapper over the Core dealer
    /// (<see cref="ChaosDraftPool"/>): the WPF <c>ChaosBoonPool.Draft</c> surface verbatim
    /// (WPF ChaosModels.cs:404-431) with the head's ChaosMeta reads plugged into ReqMet.
    /// </summary>
    public static List<ChaosBoon> Draft(bool allowCurses = true, int choices = 3, bool guaranteeCurse = false,
                                        IReadOnlyCollection<string>? takenIds = null, double sinChance = 0.5)
        => ChaosDraftPool.Draft(All, ReqMet, _rng, allowCurses, choices, guaranteeCurse, takenIds, sinChance);

    /// <summary>A requirement id may name a lifetime boon OR a trained habit/upgrade
    /// (WPF ChaosModels.cs:407-409 verbatim).</summary>
    private static bool ReqMet(string id) => ChaosMeta.IsBoonActive(id) || ChaosMeta.IsUpgradeActive(id);
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

public static class ChaosArt
{
    public static IImage? Resolve(string kind, string id) => AvaloniaChaosArt.Resolve(kind, id);
    public static IImage? ResolveBanner() => ResolveThemeFile("banner.png");
    public static IImage? ResolveMenu() => AvaloniaChaosArt.ResolveMenu();
    public static IImage? ResolveMenuFrame(int n) => AvaloniaChaosArt.ResolveMenuFrame(n);
    public static IImage? ResolveRecap() => ResolveThemeFile("recap.png");
    public static IImage? TryLoad(string? path) => AvaloniaChaosArt.TryLoad(path);
    public static string? PathFor(string kind, string id) => AvaloniaChaosArt.PathFor(kind, id);
    public static string? FilePath(string fileName) => AvaloniaChaosArt.FilePath(fileName);
    public static string? MenuFramePath(int n) => AvaloniaChaosArt.MenuFramePath(n);

    private static IImage? ResolveThemeFile(string fileName)
    {
        var modService = App.Services?.GetService<global::ConditioningControlPanel.IModService>();
        var modPath = modService?.ActiveMod?.InstalledPath;
        if (!string.IsNullOrEmpty(modPath))
        {
            var modFile = Path.Combine(modPath, "assets", "Chaos", fileName);
            var modImg = AvaloniaChaosArt.TryLoad(modFile);
            if (modImg != null) return modImg;
        }
        foreach (var root in new[] { AvaloniaChaosEnv.EffectiveAssetsPath, AppContext.BaseDirectory })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var path = Path.Combine(root, "assets", "Chaos", fileName);
            var img = AvaloniaChaosArt.TryLoad(path);
            if (img != null) return img;
        }
        return null;
    }
}

public static class ChaosTips
{
    /// <summary>
    /// Attaches a styled tooltip to a Chaos UI element. Falls back gracefully when inputs are empty.
    /// </summary>
    public static void Attach(Control element, string title, string? desc, string? extra = null,
                              Color? accent = null, string? flavor = null)
    {
        if (element == null) return;

        var panel = new StackPanel { Spacing = 4, MaxWidth = 320 };

        IBrush titleBrush = accent.HasValue ? new SolidColorBrush(accent.Value) : Brushes.White;
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.Bold,
            Foreground = titleBrush,
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrWhiteSpace(desc))
        {
            panel.Children.Add(new TextBlock
            {
                Text = desc,
                Foreground = Brushes.LightGray,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
            });
        }

        if (!string.IsNullOrWhiteSpace(extra))
        {
            panel.Children.Add(new TextBlock
            {
                Text = extra,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
            });
        }

        if (!string.IsNullOrWhiteSpace(flavor))
        {
            panel.Children.Add(new TextBlock
            {
                Text = flavor,
                Foreground = Brushes.HotPink,
                FontStyle = FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        ToolTip.SetTip(element, panel);
    }
}

public static class ChaosNarrator
{
    private const int MIN_DWELL_MS = 2400;
    private const int MAX_DWELL_MS = 7000;

    /// <summary>True while the Madam is approximately still speaking.</summary>
    public static bool IsPlaying { get; private set; }

    /// <summary>
    /// Speak a single cue: show the on-screen subtitle, mark the narrator busy, and
    /// play the matching narrator clip if one exists.
    /// </summary>
    public static void Speak(ChaosNarrativeCue cue, bool interrupt)
    {
        try
        {
            if (cue == null) return;
            int durMs = EstimateDurationMs(cue.Text);
            IsPlaying = true;
            (AvaloniaChaosApp.Chaos as global::ConditioningControlPanel.Avalonia.Services.AvaloniaChaosService)
                ?.AnnounceChaosNarrator(cue.Text, (int)cue.Band, interrupt, durMs);
            PlayCardLine(cue.AudioKey, cue.Text);
            _ = ResetAfterAsync(durMs + 220);
        }
        catch (Exception ex)
        {
            LogDebug("ChaosNarrator.Speak failed: {E}", ex.Message);
        }
    }

    /// <summary>Play one conversation-card line's narrator clip, if the file exists.</summary>
    public static void PlayCardLine(string? audioKey, string text)
    {
        if (string.IsNullOrWhiteSpace(audioKey)) return;
        var path = ResolveNarratorPath(audioKey);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            var player = App.Services?.GetService<global::ConditioningControlPanel.Core.Platform.IAudioPlayer>();
            if (player == null) return;
            player.SetVolume(0.75f);
            _ = player.PlayAsync(path);
        }
        catch (Exception ex)
        {
            LogDebug("ChaosNarrator.PlayCardLine failed: {E}", ex.Message);
        }
    }

    private static string? ResolveNarratorPath(string audioKey)
    {
        var relative = $"assets/Chaos/narrator/{audioKey}.mp3";
        var modService = App.Services?.GetService<global::ConditioningControlPanel.IModService>();
        var modPath = modService?.ActiveMod?.InstalledPath;
        if (!string.IsNullOrEmpty(modPath))
        {
            var modOverride = Path.Combine(modPath, relative);
            if (File.Exists(modOverride)) return modOverride;
        }
        foreach (var root in new[] { AvaloniaChaosEnv.EffectiveAssetsPath, AppContext.BaseDirectory })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var candidate = Path.Combine(root, relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Release the card's speaking hold.</summary>
    public static void EndCard() => Reset();

    /// <summary>Force-stop any speaking state (run teardown).</summary>
    public static void Reset() => IsPlaying = false;

    private static async System.Threading.Tasks.Task ResetAfterAsync(int delayMs)
    {
        try
        {
            await System.Threading.Tasks.Task.Delay(delayMs);
            IsPlaying = false;
        }
        catch { }
    }

    private static int EstimateDurationMs(string text)
    {
        int est = 1200 + (text?.Length ?? 0) * 55;
        return Math.Clamp(est, MIN_DWELL_MS, MAX_DWELL_MS);
    }

    private static void LogDebug(string message, params object?[] args)
    {
        try { App.Services?.GetRequiredService<ILogger<object>>().LogDebug(message, args); } catch { }
    }
}

/// <summary>
/// Static in-run/hub dispatch for the narrative layer. Mirrors the WPF
/// <c>ChaosNarrativeHooks</c> shape but with Avalonia-specific display wiring.
/// </summary>
public static class ChaosNarrativeHooks
{
    private static bool _active;
    private static bool _sawBareDeto;
    private static bool _sawFirstPop;
    private static bool _sawFirstDefuse;
    private static bool _sawFirstDetonation;

    public static void Init() => ChaosNarrativeDirector.Init();

    public static void OnRunStarted(ChaosNarrativeContext ctx)
    {
        _active = true;
        _sawBareDeto = false;
        _sawFirstPop = false;
        _sawFirstDefuse = false;
        _sawFirstDetonation = false;
        ctx.Trigger = "run_start";
        if (ChaosHappyPath.IsScripting) return;
        ChaosNarrativeDirector.Fire(ctx);
    }

    public static void OnRunEnded(ChaosNarrativeContext ctx, double score, bool ranFullCourse)
    {
        _active = false;
        _sawBareDeto = false;
        _sawFirstPop = false;
        _sawFirstDefuse = false;
        _sawFirstDetonation = false;
        ChaosNarrator.Reset();
    }

    /// <summary>Hub-side moment (no live run). Picks and displays a conversation standalone.</summary>
    public static ChaosOverlayWindow? OnHubMoment(string moment, ChaosNarrativeContext ctx)
    {
        try
        {
            ctx.Trigger = moment;
            ctx.Depth = 0;
            var convo = ChaosNarrativeDirector.Pick(ctx, moment);
            if (convo == null) return null;
            return ShowStandaloneConversation(convo);
        }
        catch (Exception ex)
        {
            LogDebug("ChaosNarrativeHooks.OnHubMoment: {E}", ex.Message);
            return null;
        }
    }

    public static void OnWaveStart(int waveIndex, ChaosNarrativeContext ctx)
    {
        if (!_active || ChaosHappyPath.IsScripting) return;
        ctx.Trigger = "zone_border";
        ctx.Depth = waveIndex;
        ChaosNarrativeDirector.Fire(ctx);
    }

    public static void OnBoonDraft(int waveIndex, ChaosNarrativeContext ctx)
    {
        if (!_active) return;
        // Reserved hook: no default narrator line while the draft UI is open.
    }

    public static void OnFirstPop(ChaosNarrativeContext ctx)
    {
        if (!_active || _sawFirstPop || ChaosHappyPath.IsScripting) return;
        _sawFirstPop = true;
        ctx.Trigger = "first_pop";
        ChaosNarrativeDirector.Fire(ctx);
    }

    public static void OnFirstDefuse(ChaosNarrativeContext ctx)
    {
        if (!_active || _sawFirstDefuse || ChaosHappyPath.IsScripting) return;
        _sawFirstDefuse = true;
        ctx.Trigger = "first_defuse";
        ChaosNarrativeDirector.Fire(ctx);
    }

    public static void OnFirstDetonation(ChaosNarrativeContext ctx)
    {
        if (!_active || _sawFirstDetonation || ChaosHappyPath.IsScripting) return;
        _sawFirstDetonation = true;
        ctx.Trigger = "first_bare_deto";
        ChaosNarrativeDirector.Fire(ctx);
    }

    public static void OnBrinkDefuse(ChaosNarrativeContext ctx)
    {
        if (!_active || ChaosHappyPath.IsScripting) return;
        ctx.Trigger = "brink_defuse";
        ChaosNarrativeDirector.Fire(ctx);
    }

    public static void OnSinAccepted(string sinId, ChaosNarrativeContext ctx)
    {
        if (!_active || ChaosHappyPath.IsScripting) return;
        ctx.Trigger = "sin_accepted";
        ctx.SinId = sinId;
        ChaosNarrativeDirector.Fire(ctx);
    }

    /// <summary>True the first time a bare detonation lands in this run (false thereafter).</summary>
    public static bool TryFirstBareDeto()
    {
        if (_sawBareDeto) return false;
        _sawBareDeto = true;
        return true;
    }

    private static ChaosOverlayWindow? ShowStandaloneConversation(ChaosConversation convo)
    {
        try
        {
            var win = new ChaosOverlayWindow();
            var bg = ChaosArt.Resolve("hub", "backdrop") ?? ChaosArt.Resolve("backdrops", "depth1");
            win.Show();
            win.ShowConversation(convo, bg, onComplete: () => { try { win.Close(); } catch { } });
            return win;
        }
        catch (Exception ex)
        {
            LogDebug("ChaosNarrativeHooks.ShowStandaloneConversation: {E}", ex.Message);
            return null;
        }
    }

    private static void LogDebug(string message, params object?[] args)
    {
        try { App.Services?.GetRequiredService<ILogger<object>>().LogDebug(message, args); } catch { }
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
