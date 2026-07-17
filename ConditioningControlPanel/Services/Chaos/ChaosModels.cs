using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// One boon tile in the HUD sidebar: art if present, else a glyph. Used both for
/// equipped lifetime boons (Level ≥ 1, pink, level badge) and for mantras/sins
/// drafted during the run (Level 0, green/red, no badge). Hovering a tile shows
/// the themed card (title + desc + capstone line).
/// </summary>
public sealed class ChaosSidebarBoon
{
    /// <summary>Lifetime-boon id for pocket tiles (click-to-unequip pre-run). Empty for run picks/modifiers.</summary>
    public string Id { get; init; } = "";
    public ImageSource? Icon { get; init; }
    public string Glyph { get; init; } = "◈";
    public string Name { get; init; } = "";
    public int Level { get; init; }
    public string Desc { get; init; } = "";
    public string Flavor { get; init; } = "";
    /// <summary>Capstone line for the hover card (gold). Empty = hidden.</summary>
    public string Extra { get; init; } = "";
    public bool IsCurse { get; init; }
    /// <summary>Owned always-on upgrade (the MODIFIERS list) — purple tile.</summary>
    public bool IsModifier { get; init; }
    /// <summary>An unfilled pocket slot (dim "+" tile shown during the pre-run loadout glance).</summary>
    public bool IsEmptySlot { get; init; }
    public bool HasIcon => Icon != null;
    public bool ShowGlyph => Icon == null;
    public string LevelText => $"L{Level}";
    /// <summary>One-line loadout entry for the expanded HUD's POCKETS list.</summary>
    public string PocketText => $"{Glyph} {Name} · L{Level}";

    // ---- hover card + tile accents ----
    public string TipTitle => Level > 0 ? $"{Name} · L{Level}" : Name;
    public Visibility LevelBadgeVisibility => Level > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DescVisibility => string.IsNullOrEmpty(Desc) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FlavorVisibility => string.IsNullOrEmpty(Flavor) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ExtraVisibility => string.IsNullOrEmpty(Extra) ? Visibility.Collapsed : Visibility.Visible;
    public Brush AccentBrush
    {
        get
        {
            var fallback = IsEmptySlot ? EmptyAccent : IsModifier ? ModAccent : IsCurse ? CurseAccent : Level > 0 ? PocketAccent : BoonAccent;
            // Payload-based color language: a mapped boon shows its family color; everything else
            // (empty slots, unmapped mechanics) keeps the category fallback above.
            return IsEmptySlot ? fallback : ConditioningControlPanel.ChaosBoonColors.BrushForOrDefault(Id, fallback);
        }
    }
    public Brush TileBackBrush => IsEmptySlot ? Brushes.Transparent : IsModifier ? ModBack : IsCurse ? CurseBack : Level > 0 ? PocketBack : BoonBack;
    public double TileOpacity => IsEmptySlot ? 0.55 : 1.0;

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static readonly Brush EmptyAccent = Frozen(Color.FromArgb(0x60, 0xB8, 0xB8, 0xD0));
    private static readonly Brush PocketAccent = Frozen(Color.FromRgb(0xFF, 0x69, 0xB4));
    private static readonly Brush BoonAccent = Frozen(Color.FromRgb(0x9C, 0xE8, 0xA0));
    private static readonly Brush CurseAccent = Frozen(Color.FromRgb(0xFF, 0x8A, 0x8A));
    private static readonly Brush ModAccent = Frozen(Color.FromRgb(0x8B, 0x5C, 0xF6));
    private static readonly Brush PocketBack = Frozen(Color.FromArgb(0x33, 0xFF, 0x69, 0xB4));
    private static readonly Brush BoonBack = Frozen(Color.FromArgb(0x2E, 0x9C, 0xE8, 0xA0));
    private static readonly Brush CurseBack = Frozen(Color.FromArgb(0x2E, 0xFF, 0x8A, 0x8A));
    private static readonly Brush ModBack = Frozen(Color.FromArgb(0x2E, 0x8B, 0x5C, 0xF6));
}

/// <summary>
/// One equipped active-use skill (toy) during a run: HUD button + keybind state.
/// Cooldown-based toys show "ready"/"{n}s"; charge-based toys show "{n} left".
/// </summary>
public sealed class ChaosToyState : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Glyph { get; init; } = "◈";
    public string Desc { get; init; } = "";
    public string Flavor { get; init; } = "";
    public string CapstoneDesc { get; init; } = "";
    /// <summary>Key that fires this toy ("Q"). Shown on the HUD button.</summary>
    public string KeyLabel { get; init; } = "";
    public double CooldownSec { get; init; }          // 0 = charge-based

    private int _chargesLeft = -1;                    // -1 = cooldown-based
    public int ChargesLeft { get => _chargesLeft; set { _chargesLeft = value; Refresh(); } }

    private double _cooldownRemainingSec;
    public double CooldownRemainingSec { get => _cooldownRemainingSec; set { _cooldownRemainingSec = Math.Max(0, value); Refresh(); } }

    private bool _isEffectActive;
    /// <summary>True while this toy's temporary effect is running (buzz window, freeze hold,
    /// DVD flight). Drives the hero button's "glowing" state.</summary>
    public bool IsEffectActive { get => _isEffectActive; set { if (_isEffectActive == value) return; _isEffectActive = value; OnChanged(nameof(IsEffectActive)); Refresh(); } }

    public bool IsReady => CooldownRemainingSec <= 0 && (ChargesLeft != 0);
    public string StatusText =>
        ChargesLeft >= 0 ? (ChargesLeft == 0 ? "spent" : $"{ChargesLeft} left")
        : CooldownRemainingSec > 0 ? $"{Math.Ceiling(CooldownRemainingSec):0}s" : "ready";
    public string ButtonLabel => $"{Glyph} {Name} · {KeyLabel}";

    private void Refresh()
    {
        OnChanged(nameof(ChargesLeft)); OnChanged(nameof(CooldownRemainingSec));
        OnChanged(nameof(IsReady)); OnChanged(nameof(StatusText));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public enum ChaosDifficulty { Easy, Medium, Hard, Extreme }

public enum ChaosRarity { Common, Uncommon, Rare }

/// <summary>
/// Which flavour of the Rabbit Hole a run is: the immersive <see cref="Story"/> descent (backdrop,
/// Madam narrative + story cards, run locked on top, floating avatar hidden) or <see cref="FreeDesktop"/>
/// free-play (no backdrop/narrative, the run does NOT pin itself above other apps so you keep using
/// your PC, and the companion avatar floats over the desktop). Chosen per-run at the hub; the in-run
/// subsystems read <see cref="ChaosModeService.ActiveMode"/> rather than mutating saved settings.
/// </summary>
public enum ChaosPlayMode { Story, FreeDesktop }

/// <summary>Knobs that drive a single Chaos run. Built from the Lab card + AppSettings.</summary>
public sealed class ChaosRunConfig
{
    public ChaosDifficulty Difficulty { get; set; } = ChaosDifficulty.Easy;
    public int DurationSec { get; set; } = 180;
    public int WaveCount { get; set; } = 5;
    /// <summary>The Bottomless Fall unlock: this descent ignores the clock — regions loop and
    /// deepen, and it ends only when the player wakes (hold ESC). Gated on owning endless_mode.</summary>
    public bool Endless { get; set; } = false;

    /// <summary>Story (immersive, default) vs Free Desktop (free-play over the real desktop). Drives the
    /// backdrop, narrative, avatar visibility and z-order at run start. See <see cref="ChaosPlayMode"/>.</summary>
    public ChaosPlayMode PlayMode { get; set; } = ChaosPlayMode.Story;

    // ---- setup-window config ----
    /// <summary>Base resistance is ZERO (2026-06-10): the "It would never work on me..." charm
    /// is the only way to descend wearing any (its Apply bumps this for the HUD heart row).</summary>
    public int StartingShields { get; set; } = 0;
    /// <summary>Null = Mixed (per-variant default motion); otherwise force this motion.</summary>
    public ChaosMotion? MotionOverride { get; set; }
    /// <summary>Enabled variant ids. Null = all enabled.</summary>
    public List<string>? EnabledVariants { get; set; }
    public bool ScreenShakeEnabled { get; set; } = true;
    public bool ColorFlashesEnabled { get; set; } = true;
    public double ShakeIntensity { get; set; } = 0.8;
    public double EffectIntensity { get; set; } = 0.85;
    public bool BoonDraftEnabled { get; set; } = true;
    public bool AllowCurses { get; set; } = true;
    /// <summary>Darters (bouncing-flash catch targets) spawn during the run when true.</summary>
    public bool DartersEnabled { get; set; } = true;
    /// <summary>If a boon draft is left untouched this many seconds, auto-take the SKIP (+1 shield) and
    /// resume so an unattended run never freezes forever. 0 disables (wait indefinitely).</summary>
    public int DraftAutoResumeSec { get; set; } = ChaosModeService.DraftAutoResumeSecDefault;
    /// <summary>Opt-in ambient mode (OFF by default): remap intrusive detonations (video / HT link) to a
    /// lighter payload (bouncing text / gif cascade) so a background run is never yanked fullscreen.</summary>
    public bool AmbientMode { get; set; } = false;

    // ---- meta-progression knobs (set by ChaosMeta.ApplyTo from owned upgrades) ----
    // Every field has a neutral default so an unmodified run is byte-for-byte unchanged.
    /// <summary>Multiplies live-bubble fuse time; seeds <see cref="ChaosRunState.FuseTimeMult"/>.</summary>
    public double FuseTimeMult { get; set; } = 1.0;
    /// <summary>Near-click defuse (silk_touch habit); seeds <see cref="ChaosRunState.MagnetEnabled"/>.</summary>
    public bool MagnetEnabled { get; set; } = false;
    /// <summary>Base of the multiplier stack (Golden Touch charm writes it at boon-apply time).</summary>
    public double BaseMult { get; set; } = 1.0;
    /// <summary>Scales banked Sparks at run end (spark_gain).</summary>
    public double SparkGainMult { get; set; } = 1.0;
    /// <summary>Boon-draft option count (draft4 → 4). Default 3 = current behavior.</summary>
    public int DraftChoices { get; set; } = 3;
    /// <summary>Hit-test scale (silk_touch).</summary>
    public double HitboxScale { get; set; } = 1.0;
    /// <summary>Pop-up Notification habit: once per loop, sometimes, a heart drifts down (+1 resistance on catch).</summary>
    public bool PopupHeartEnabled { get; set; } = false;
    /// <summary>Pendulum habit: once per loop, at a random beat, 2.5s of slow motion.</summary>
    public bool PendulumSwing { get; set; } = false;
    /// <summary>Scales the spawn-tick rate (1.0 = current behavior). The scripted first descent
    /// runs gentler (~0.6) so the teach beats land in quiet air.</summary>
    public double SpawnRateMult { get; set; } = 1.0;
    /// <summary>Chance the dedicated sin slot rolls in a draft (was a 0.5 literal). The happy
    /// path debuts sins at 0.25 on run 3 and ramps to 0.5 by Slipping.</summary>
    public double SinChance { get; set; } = 0.5;
    /// <summary>The scripted very-first descent (RunsCompleted == 0): a code-built naked run —
    /// behavioral bubbles, the start boon and the BeginRun defuse teach all stand down so
    /// <see cref="ChaosHappyPath"/> can run its beats. Default false = every other run unchanged.</summary>
    public bool ScriptedFirstRun { get; set; } = false;

    public static ChaosRunConfig FromSettings()
    {
        var s = App.Settings?.Current;
        var cfg = new ChaosRunConfig();
        cfg.SinChance = DefaultSinChance(ChaosMeta.State.RunsCompleted);
        if (s == null) { ChaosMeta.ApplyTo(cfg); return cfg; }
        var saved = Enum.TryParse<ChaosDifficulty>(s.ChaosDifficulty, out var d) ? d : ChaosDifficulty.Easy;
        cfg.Difficulty = ClampDifficulty(saved);
        // The Hourglass unlock widens the ceiling from 20 min to 2 hours; without it the old
        // preset ceiling still holds (a stale page can never write itself a longer fall).
        int durMax = ChaosMeta.IsOwned("custom_duration") ? 7200 : 1200;
        cfg.DurationSec = Math.Clamp(s.ChaosRunDurationSec, 60, durMax);
        cfg.WaveCount = Math.Clamp(s.ChaosWaveCount, 1, 12);
        cfg.Endless = s.ChaosEndless && ChaosMeta.IsOwned("endless_mode");
        cfg.MotionOverride = Enum.TryParse<ChaosMotion>(s.ChaosMotionMode, out var m) ? m : (ChaosMotion?)null;
        cfg.EnabledVariants = ClampVariants(s.ChaosEnabledVariants);   // null = all
        cfg.ScreenShakeEnabled = s.ChaosScreenShakeEnabled;
        cfg.ColorFlashesEnabled = s.ChaosColorFlashesEnabled;
        cfg.ShakeIntensity = Math.Clamp(s.ChaosShakeIntensity, 0.0, 1.0);
        cfg.EffectIntensity = Math.Clamp(s.ChaosEffectIntensity, 0.2, 1.5);
        cfg.BoonDraftEnabled = s.ChaosBoonDraftEnabled;
        cfg.AllowCurses = s.ChaosAllowCurses;
        cfg.DartersEnabled = s.ChaosDartersEnabled;
        ChaosMeta.ApplyTo(cfg);   // owned permanent upgrades shape every run
        return cfg;
    }

    // ---- sin-slot ramp (tunable): sins debut on the 3rd descent at 25%, reaching the
    // full 50% slot by Slipping. The AllowCurses user toggle still clamps on top. ----
    private const int    SIN_DEBUT_RUNS  = 2;     // runs completed before sins deal at all
    private const int    SIN_FULL_RUNS   = 10;    // runs completed when the ramp tops out (Slipping)
    private const double SIN_CHANCE_DEBUT = 0.25;
    private const double SIN_CHANCE_FULL  = 0.5;

    /// <summary>Happy-path default for <see cref="SinChance"/> by lifetime completed descents:
    /// 0 before the debut, then a linear 0.25 → 0.5 ramp between runs 2 and 10, then 0.5.</summary>
    public static double DefaultSinChance(int runsCompleted)
    {
        if (runsCompleted < SIN_DEBUT_RUNS) return 0.0;
        if (runsCompleted >= SIN_FULL_RUNS) return SIN_CHANCE_FULL;
        double t = (runsCompleted - SIN_DEBUT_RUNS) / (double)(SIN_FULL_RUNS - SIN_DEBUT_RUNS);
        return SIN_CHANCE_DEBUT + (SIN_CHANCE_FULL - SIN_CHANCE_DEBUT) * t;
    }

    /// <summary>
    /// Rank clamp for the run's difficulty: if the SAVED pill is still locked, the run falls
    /// back to the highest unlocked one. The saved setting is never written — unlocking
    /// restores the user's own choice untouched. Gentle is always open; Teasing needs Tempted,
    /// Relentless needs Entranced, Inescapable keeps its extreme_tier-ownership gate.
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
    /// Rank clamp for the run's bubble pool: narrows out <c>video</c> / <c>htlink</c> while
    /// their reveals are locked. Since 2026-07 both reveals are open from the first run
    /// (depth-gating moved in-run: the web spawner only deals them in chambers III-IV), so
    /// this passes the saved list through untouched — kept as the backstop should the
    /// predicates ever gate again. The saved setting is never mutated.
    /// </summary>
    private static List<string>? ClampVariants(List<string>? saved)
    {
        bool videoOk = RevealService.IsUnlocked(RevealIds.VariantVideo);
        bool htOk = RevealService.IsUnlocked(RevealIds.VariantHtlink);
        if (videoOk && htOk) return saved;
        var list = new List<string>(saved ?? ChaosBubbleVariants.AllIds());
        if (!videoOk) list.Remove("video");
        if (!htOk) list.Remove("htlink");
        return list;
    }

    /// <summary>Per-difficulty payout/intensity scalar baked into the multiplier stack.</summary>
    public double DifficultyMult => Difficulty switch
    {
        ChaosDifficulty.Easy => 1.0,
        ChaosDifficulty.Medium => 1.3,
        ChaosDifficulty.Hard => 1.7,
        ChaosDifficulty.Extreme => 2.2,
        _ => 1.0
    };
}

/// <summary>
/// A drafted boon (or curse). Pure data + an <see cref="Apply"/> mutator that
/// flips knobs on the live <see cref="ChaosRunState"/>. Data-driven so the pool
/// grows by adding records, not code paths.
/// </summary>
public sealed record ChaosBoon(
    string Id,
    string Name,
    string Desc,
    ChaosRarity Rarity,
    bool IsCurse,
    double RunMultBonus,
    Action<ChaosRunState> Apply)
{
    /// <summary>Curse-only: the sin's sweet half, applied INSTEAD of <see cref="Apply"/> when
    /// Surrender's capstone waives the first sin's drawback. Null = the whole Apply is the
    /// drawback, so a shielded pick skips it entirely (RunMultBonus still lands).</summary>
    public Action<ChaosRunState>? ApplyShielded { get; init; }

    /// <summary>One flavorful line, rendered grey italic under the mechanics text.</summary>
    public string Flavor { get; init; } = "";

    /// <summary>One-shot flag boons: once taken this run, never offered again.</summary>
    public bool Unique { get; init; }

    /// <summary>Duo gating: this card only enters the draft when at least one of these
    /// lifetime boons (skill/accessory/charm ids) is equipped. Null = always draftable.
    /// Duo cards render with a gold frame at the table.</summary>
    public string[]? RequiresAny { get; init; }

    /// <summary>Trio gating: ALL of these lifetime boons must be equipped (e.g. Spanker + E-Stim).
    /// Combines with <see cref="RequiresAny"/>; gold frame like any duo card.</summary>
    public string[]? RequiresAll { get; init; }
}

/// <summary>The v1 boon/curse pool.</summary>
public static class ChaosBoonPool
{
    public static readonly List<ChaosBoon> All = new()
    {
        // (Slow Trance + Mesmer Pull benched 2026-06-11: their effects live on as the
        //  Slowburner/Silk Touch habits. Left Brain stays — a real decision at 0 base resistance.)
        new("defuse_chain", "Snap Chain", "for 0.9s after every snap, a trigger can't break your streak, feed your lust, or spend your resistance. +0.10x run multiplier.",
            ChaosRarity.Uncommon, false, 0.10, s => s.DefuseInvulnMs = 900) { Flavor = "one clean snap buys a heartbeat of grace." },
        new("golden_touch", "Golden Touch", "+0.15x run multiplier, immediately.",
            ChaosRarity.Uncommon, false, 0.15, s => { /* RunMultBonus folds into BoonMult */ }) { Flavor = "nothing down here is actually free." },
        new("extra_shield", "Left Brain", "+2 resistance, right now.",
            ChaosRarity.Common, false, 0.0, s => s.Shields += 2) { Flavor = "the part still arguing. give it something to hold." },

        // ---- the visible pool (2026-06-11): entities, field FX, behaviors — not multipliers ----
        new("gold_digger", "Gold Digger", "golden bubbles burst into 3 falling droplets worth 3-7 gold each. catch them before they slip off screen. a missed droplet costs nothing.",
            ChaosRarity.Uncommon, false, 0.0, s => s.GoldDiggerEnabled = true) { Unique = true, Flavor = "she loves you for your wallet. you knew." },
        new("welcome_shower", "Welcome Shower", "every loop opens with 6 treats raining from the top of the screen.",
            ChaosRarity.Common, false, 0.0, s => s.WelcomeShowerEnabled = true) { Unique = true, Flavor = "a warm welcome, every time you go back under." },
        new("heavy_drop", "Heavy Drop", "every 10th bubble is a giant: x1.55 size, drifts at half speed, lives 9s, pays x3.",
            ChaosRarity.Common, false, 0.0, s => s.HeavyDropEvery = 10) { Unique = true, Flavor = "some things are worth waiting under." },
        new("gg_rabbits", "GG make more GG", "15% of popped treats burst into 3 wild rabbits that mow down everything in their path. they can't be caught, only smacked along.",
            ChaosRarity.Rare, false, 0.0, s => s.GgRabbitChance = 0.15) { Unique = true, Flavor = "good girls multiply." },
        new("size_queen", "Size Queen", "every snap sends out an expanding ring, 430px wide, that pops every treat it touches.",
            ChaosRarity.Uncommon, false, 0.0, s => s.RippleEnabled = true) { Unique = true, Flavor = "bigger is a love language." },
        new("aftermath", "Aftermath", "snap a live bubble in its final 1.5s and the spot crackles for 2s: a 170px zone where anything drifting through pops itself.",
            ChaosRarity.Uncommon, false, 0.0, s => s.AftermathEnabled = true) { Unique = true, Flavor = "you can still feel it after. that's the point." },
        new("focus_here", "Focus here...", "the pendulum swings once per loop. pops during its 2.5s slow swing pay x3.",
            ChaosRarity.Uncommon, false, 0.0, s => s.PendulumPayMult = 3.0)
            { Unique = true, RequiresAny = new[] { "pendulum_swing" }, Flavor = "watch the watch. everything else can wait." },

        // ---- synergy duos: only drafted when the partner is equipped (gold frame) ----
        new("overload", "Overload", "the e-stim runs double charges per press: 6/8/10 charged pops by toy level.",
            ChaosRarity.Rare, false, 0.0, s => s.EStimChargeMult = 2)
            { Unique = true, RequiresAny = new[] { "e_stim" }, Flavor = "more than the dial was built for." },
        new("afterglow", "Afterglow", "when the buzz ends it lingers 2.5s more, still popping whatever you hover.",
            ChaosRarity.Rare, false, 0.0, s => s.AfterglowSec = 2.5)
            { Unique = true, RequiresAny = new[] { "vibe_popping" }, Flavor = "it never really stops. you just stop noticing." },
        new("casting_couch", "Casting Couch", "the logo splits on its first two bounces: one becomes two, then four.",
            ChaosRarity.Rare, false, 0.0, s => s.DvdSplitBounces = 2)
            { Unique = true, RequiresAny = new[] { "porn_dvd" }, Flavor = "everyone starts somewhere." },
        new("tail_plug", "Tail-Plug", "every rabbit drags a sparkling trail for 2s that pops anything within 46px of it.",
            ChaosRarity.Rare, false, 0.0, s => s.RabbitTrailSec = 2.0)
            { Unique = true, RequiresAny = new[] { "rabbit_caller", "the_pull", "the_spanker" }, Flavor = "you'll know exactly where they've been." },
        new("unleashed", "Unleashed", "each time the collar saves your streak, a golden shockwave snaps every live bubble on screen for full pay.",
            ChaosRarity.Rare, false, 0.0, s => s.UnleashedEnabled = true)
            { Unique = true, RequiresAny = new[] { "collar" }, Flavor = "held tight, then let go all at once." },
        new("electrified_rabbits", "Electrified Rabbits", "every bubble a spanked rabbit mows down discharges, arcing lightning into up to 3 bubbles within 620px.",
            ChaosRarity.Rare, false, 0.0, s => s.ElectrifiedRabbits = true)
            { Unique = true, RequiresAll = new[] { "the_spanker", "e_stim" }, Flavor = "you wired the paddle. of course you did." },
        new("body_buzz", "Body Buzz", "1 treat pop in 8 fires a 440px shockwave, arcing lightning into up to 8 bubbles caught inside it.",
            ChaosRarity.Rare, false, 0.0, s => s.EStimShockwaveChance = 0.125)
            { Unique = true, RequiresAll = new[] { "chain_reaction", "e_stim" }, Flavor = "it hums under your skin between pops." },

        // ---- sins — risk/reward: visible drawbacks, visible sweetness ----
        new("hair_trigger", "Hair Trigger", "every trance burns 25% faster. in exchange, +0.40x run multiplier.",
            ChaosRarity.Rare, true, 0.40, s => s.FuseTimeMult *= 0.75) { Unique = true, Flavor = "everything goes off early. including you." },
        new("playing_fire", "Playing with fire", "trigger effects last 50% longer. in exchange, snapping a live bubble in its final second tips 5-9 gold, plus +0.15x run multiplier.",
            ChaosRarity.Rare, true, 0.15, s => { s.DetonationDurationMult = 1.5; s.LastSecondGoldEnabled = true; })
            { Unique = true, ApplyShielded = s => s.LastSecondGoldEnabled = true, Flavor = "warm hands were always the price." },
        // Shielded: the prisms still come and still pay x10 — they just only ever wear a TREAT's
        // look. Apply IS this sin's upside, so a null handler here made the Surrender capstone
        // hand out a literally blank card (and silently bricked the taking_chances grind).
        new("bright_colors", "Look at the bright colors...", "5% of spawns are prism bubbles wearing another bubble's look. popping one pays x10 and fires the copied effect at full strength.",
            ChaosRarity.Rare, true, 0.0, s => s.PrismChance = 0.05)
            { Unique = true, ApplyShielded = s => { s.PrismChance = 0.05; s.PrismTreatOnly = true; }, Flavor = "so pretty you forget to ask what's inside." },
        new("cam_girl", "Cam Girl", "bubbles flee your cursor, stronger than any pull. in exchange, 25% of pops tip 2-4 gold, plus +0.40x run multiplier.",
            ChaosRarity.Rare, true, 0.40, s => { s.CamGirlFlee = 1.6; s.CamGirlTipChance = 0.25; })
            { Unique = true, ApplyShielded = s => s.CamGirlTipChance = 0.25, Flavor = "look, don't touch. tips appreciated." },
        new("the_urge", "The urge", "the rest of the descent pays x3 on everything. your toys are off-limits.",
            ChaosRarity.Rare, true, 0.0, s => { s.UrgeMult = 3.0; s.ActivesDisabled = true; })
            // Shielded: toys stay usable, but the high softens to x2 — a waived drawback that
            // kept the full x3 was a free permanent multiplier dwarfing every other card.
            { Unique = true, ApplyShielded = s => s.UrgeMult = 2.0, Flavor = "bare hands. that's the deal." },
        new("double_or_nothing", "Relapse", "60% chance the descent runs one loop longer than promised, and that loop pays double gold and double drops.",
            ChaosRarity.Rare, true, 0.0, s => s.RelapseLoopArmed = Random.Shared.NextDouble() < 0.6)
            { Unique = true, ApplyShielded = s => s.RelapseLoopArmed = true, Flavor = "one more. it's always just one more." },   // shielded: the extra loop is certain
    };

    private static readonly Random _rng = new();

    /// <summary>
    /// Draft <paramref name="choices"/> options (default 3): mostly boons + occasionally a
    /// curse (unless curses are disabled). <paramref name="choices"/> comes from
    /// <c>ChaosRunConfig.DraftChoices</c> (the draft4 upgrade raises it to 4).
    /// Duo cards (<see cref="ChaosBoon.RequiresAny"/>/<see cref="ChaosBoon.RequiresAll"/>) deal
    /// as soon as their partner is equipped — no rank gate on top (the old Entranced wall meant
    /// the duo-demo rig teased cards at purchase, then revoked them for ~10 runs);
    /// <see cref="ChaosBoon.Unique"/> cards already taken (<paramref name="takenIds"/>)
    /// sit the rest of the run out. <paramref name="sinChance"/> is the dedicated sin-slot
    /// roll (<c>ChaosRunConfig.SinChance</c> — the happy path ramps it in over the early runs).
    /// </summary>
    public static List<ChaosBoon> Draft(bool allowCurses = true, int choices = 3, bool guaranteeCurse = false,
                                        IReadOnlyCollection<string>? takenIds = null, double sinChance = 0.5)
    {
        choices = Math.Clamp(choices, 2, 4);
        // A requirement id may name a lifetime boon (skill/accessory/charm) OR a trained habit
        // (e.g. Focus here → the pendulum_swing habit).
        static bool ReqMet(string id) => ChaosMeta.IsBoonActive(id) || ChaosMeta.IsUpgradeActive(id);
        bool Draftable(ChaosBoon b) =>
            (b.RequiresAny == null || b.RequiresAny.Any(ReqMet))
            && (b.RequiresAll == null || b.RequiresAll.All(ReqMet))
            && !(b.Unique && takenIds != null && takenIds.Contains(b.Id));
        var boons = All.Where(b => !b.IsCurse && Draftable(b)).OrderBy(_ => _rng.Next()).ToList();
        var curses = All.Where(b => b.IsCurse && Draftable(b)).OrderBy(_ => _rng.Next()).ToList();

        var draft = new List<ChaosBoon>();
        bool includeCurse = allowCurses && (guaranteeCurse || _rng.NextDouble() < sinChance) && curses.Count > 0;
        int boonCount = includeCurse ? choices - 1 : choices;

        draft.AddRange(boons.Take(Math.Min(boonCount, boons.Count)));
        if (includeCurse) draft.Add(curses[0]);

        // Top up from boons if the pool ran short.
        foreach (var b in boons.Skip(boonCount))
        {
            if (draft.Count >= choices) break;
            draft.Add(b);
        }
        return draft.Take(choices).ToList();
    }
}

/// <summary>Live, bindable state for one Chaos run. The HUD binds directly to this.</summary>
public sealed class ChaosRunState : INotifyPropertyChanged
{
    public ChaosRunConfig Config { get; }

    public ChaosRunState(ChaosRunConfig config)
    {
        Config = config;
        WaveCount = config.WaveCount;
        RunDurationSec = config.DurationSec;
        Shields = config.StartingShields;
        FuseTimeMult = config.FuseTimeMult;     // seed from owned upgrades; boons multiply at runtime
        MagnetEnabled = config.MagnetEnabled;
    }

    // ---- timing / waves ----
    private double _elapsedSec;
    public double ElapsedSec { get => _elapsedSec; set { _elapsedSec = value; OnChanged(); OnChanged(nameof(RunTimeText)); OnChanged(nameof(ClockText)); OnChanged(nameof(RunProgress)); OnChanged(nameof(RunIntensity)); } }
    public int RunDurationSec { get; private set; }
    public double RunProgress => RunDurationSec <= 0 ? 0 : Math.Clamp(ElapsedSec / RunDurationSec, 0, 1);
    /// <summary>0..1 escalation curve used to scale spawn rate, fuse, strength, live-share.</summary>
    public double RunIntensity => Math.Clamp(RunProgress, 0, 1);
    public string RunTimeText => $"{(int)ElapsedSec / 60:00}:{(int)ElapsedSec % 60:00} / {RunDurationSec / 60:00}:{RunDurationSec % 60:00}";
    /// <summary>Compact remaining/elapsed clock for the collapsed HUD strip.</summary>
    public string ClockText => $"{(int)ElapsedSec / 60:00}:{(int)ElapsedSec % 60:00}";

    private int _actIndex = 1;
    public int ActIndex { get => _actIndex; set { _actIndex = value; OnChanged(); OnChanged(nameof(ActWaveText)); } }
    private int _waveIndex = 1;
    public int WaveIndex { get => _waveIndex; set { _waveIndex = value; OnChanged(); OnChanged(nameof(ActWaveText)); } }
    public int WaveCount { get; private set; }
    public string ActWaveText => $"DEPTH {ToRoman(ActIndex)} · LOOP {WaveIndex}/{WaveCount}";

    /// <summary>Relapse sin: bolt one more loop onto the end of the run. That loop pays
    /// double drops and gold (<see cref="RelapseLoopActive"/> read at every gold/drop bank).</summary>
    public void ExtendOneLoop()
    {
        int waveLen = (int)Math.Round((double)RunDurationSec / Math.Max(1, WaveCount));
        WaveCount += 1;
        RunDurationSec += waveLen;
        RelapseLoopActive = true;
        OnChanged(nameof(WaveCount)); OnChanged(nameof(ActWaveText));
        OnChanged(nameof(RunDurationSec)); OnChanged(nameof(RunTimeText));
        OnChanged(nameof(RunProgress)); OnChanged(nameof(RunIntensity));
    }

    private double _waveProgress;
    public double WaveProgress { get => _waveProgress; set { _waveProgress = Math.Clamp(value, 0, 1); OnChanged(); } }

    // ---- score / combo / heat ----
    private double _score;
    public double Score { get => _score; set { _score = value; OnChanged(); OnChanged(nameof(ScoreText)); } }
    public string ScoreText => $"{(int)Score:N0}";

    private int _combo;
    public int Combo { get => _combo; set { _combo = Math.Max(0, value); if (_combo > BestCombo) BestCombo = _combo; OnChanged(); OnChanged(nameof(ComboMult)); OnChanged(nameof(TotalMult)); OnChanged(nameof(TotalMultText)); } }
    public int BestCombo { get; private set; }

    private double _heat;
    /// <summary>0..1 chaos-local heat; ramps while clean, resets on detonation.</summary>
    public double Heat { get => _heat; set { _heat = Math.Clamp(value, 0, 1); OnChanged(); OnChanged(nameof(HeatMult)); OnChanged(nameof(TotalMult)); OnChanged(nameof(TotalMultText)); } }

    private int _shields;
    public int Shields { get => _shields; set { _shields = Math.Max(0, value); OnChanged(); OnChanged(nameof(ShieldText)); } }

    // ---- focus (the defuse fuel: treat pops generate it, hold-channels spend it) ----
    private double _focus = ChaosTuning.FOCUS_START;
    public double FocusMax => ChaosTuning.FOCUS_MAX;
    public double Focus
    {
        get => _focus;
        set { _focus = Math.Clamp(value, 0, FocusMax); OnChanged(); OnChanged(nameof(FocusText)); OnChanged(nameof(FocusLow)); }
    }
    public string FocusText => $"{(int)Focus}";
    /// <summary>Below the price of a defuse — the HUD bar dims and pulses ("don't touch lives").</summary>
    public bool FocusLow => Focus < ChaosTuning.DEFUSE_COST;

    // ---- the Ripple (right-click verb): one charge on a slow recharge ----
    private double _rippleCooldown;
    /// <summary>Seconds until the next ripple is gathered; 0 = ready. Counted down in RunTick.</summary>
    public double RippleCooldown
    {
        get => _rippleCooldown;
        set { _rippleCooldown = Math.Max(0, value); OnChanged(); OnChanged(nameof(RippleReady)); OnChanged(nameof(RippleText)); }
    }
    public bool RippleReady => RippleCooldown <= 0;
    public string RippleText => RippleReady ? "READY" : $"{Math.Ceiling(RippleCooldown):0}s";
    public string ShieldText => string.Concat(Enumerable.Repeat("♥", Shields)) + string.Concat(Enumerable.Repeat("♡", Math.Max(0, Config.StartingShields - Shields)));

    // ---- multiplier stack (chaos-local; skill/pink-rush applied once at payout) ----
    public double BaseMult => Config.BaseMult;
    public double ComboMult => Math.Min(1.0 + Combo * 0.08, 6.0);
    public double DifficultyMult => Config.DifficultyMult;
    public double HeatMult => 1.0 + Heat * 1.0; // up to x2 at full heat
    private double _boonMult = 1.0;
    public double BoonMult { get => _boonMult; set { _boonMult = value; OnChanged(); OnChanged(nameof(TotalMult)); OnChanged(nameof(TotalMultText)); } }
    public double TotalMult => BaseMult * ComboMult * DifficultyMult * HeatMult * BoonMult * UrgeMult;
    public string TotalMultText => $"x{TotalMult:0.0}";

    /// <summary>Skill-tree multiplier (incl. Pink Rush) — informational; applied once at payout.</summary>
    public double SkillMult => App.SkillTree?.GetTotalXpMultiplier() ?? 1.0;
    public string SkillMultText => $"x{SkillMult:0.0}";

    // ---- counters ----
    private int _spawned; public int Spawned { get => _spawned; set { _spawned = value; OnChanged(); } }
    private int _defused; public int Defused { get => _defused; set { _defused = value; OnChanged(); } }
    private int _detonated; public int Detonated { get => _detonated; set { _detonated = value; OnChanged(); } }
    private int _effectsFired; public int EffectsFired { get => _effectsFired; set { _effectsFired = value; OnChanged(); } }

    // ---- boons / curses / feed ----
    public ObservableCollection<ChaosBoon> ActiveBoons { get; } = new();
    public ObservableCollection<ChaosBoon> ActiveCurses { get; } = new();
    public ObservableCollection<string> RecentEvents { get; } = new();

    /// <summary>Equipped toys (active-use Skills) shown as icons in the HUD strip — max 2. Filled at run start.</summary>
    public ObservableCollection<ChaosSidebarBoon> ActiveSidebarToys { get; } = new();

    /// <summary>Equipped Accessories shown as icons in the HUD strip — max 2, in their own group under the toys. Filled at run start.</summary>
    public ObservableCollection<ChaosSidebarBoon> ActiveSidebarAccessories { get; } = new();

    /// <summary>Mantras/sins taken this run, in draft order — the Hades-style tile column in the HUD strip.</summary>
    public ObservableCollection<ChaosSidebarBoon> RunPickTiles { get; } = new();

    /// <summary>Passive run modifiers (owned always-on upgrades) shown as purple tiles in the expanded HUD. Filled at run start.</summary>
    public ObservableCollection<ChaosSidebarBoon> RunModifiers { get; } = new();

    public void PushEvent(string text)
    {
        RecentEvents.Insert(0, text);
        while (RecentEvents.Count > 6) RecentEvents.RemoveAt(RecentEvents.Count - 1);
    }

    public void ApplyBoon(ChaosBoon boon, bool shieldDrawback = false)
    {
        if (shieldDrawback) boon.ApplyShielded?.Invoke(this);
        else boon.Apply(this);
        BoonMult += boon.RunMultBonus;
        (boon.IsCurse ? ActiveCurses : ActiveBoons).Add(boon);
        RunPickTiles.Add(new ChaosSidebarBoon
        {
            Id = boon.Id,   // carry the id so the ribbon tile colors by payload family
            Icon = ChaosArt.Resolve("boons", boon.Id),
            Glyph = boon.IsCurse ? "☠" : "◈",
            Name = boon.Name,
            Desc = boon.Desc,
            Flavor = boon.Flavor,
            IsCurse = boon.IsCurse,
        });
        PushEvent($"{(boon.IsCurse ? "☠" : "◈")} {boon.Name}");
    }

    // ---- run-tuning knobs flipped by boons/curses; spawner/fuse logic reads these ----
    public double FuseTimeMult = 1.0;
    public bool MagnetEnabled;
    public double DefuseInvulnMs;
    // (NextWavePayoutMult / DoubleOrNothingArmed / DoubleOrNothingActive deleted 2026-06-12:
    //  the original Double-or-Nothing wager never shipped — nothing ever armed it — and its
    //  "double_or_nothing" id was reused by Relapse, which runs on RelapseLoopArmed/Active.)
    public bool AllLiveNextWave;
    /// <summary>Chain Reaction lifetime boon: a pop's burst pops neighbours within this box-multiple. 1.0 = off.</summary>
    public double ChainReactionReach = 1.0;
    public double LuckBonus;

    // ---- lifetime-boon knobs (set by ChaosMeta.ApplyLifetimeBoons at run start) ----
    /// <summary>Ids of ACTIVE lifetime boons at max level — runtime checks these for capstone effects.</summary>
    public HashSet<string> MaxedBoons { get; } = new();
    /// <summary>Per-toy level value for equipped active-use skills (id → LevelValue). Filled by Apply lambdas.</summary>
    public Dictionary<string, double> ToyPower { get; } = new();
    /// <summary>Equipped active-use skills as HUD button/keybind state. Built at run start.</summary>
    public ObservableCollection<ChaosToyState> ActiveToys { get; } = new();
    /// <summary>Collar: streak saves left this descent (a detonation that would break the combo is held).</summary>
    public int CollarSaves;
    /// <summary>Pendulum: seconds added to every slow-mo window.</summary>
    public double SlowMoBonusSec;
    /// <summary>Blindfold: payout multiplier while active (bubbles render translucent).</summary>
    public double BlindfoldPayMult = 1.0;
    public bool BlindfoldActive;
    /// <summary>Blindfold: opacity plain bubbles render at while it's worn — a whisper, and
    /// fainter the deeper the blindfold (0.40/0.32/0.25 by level). Pickups stay fully visible.</summary>
    public double BlindfoldOpacity = 1.0;
    /// <summary>Surrender: extra BoonMult granted per accepted sin.</summary>
    public double SinExtraMult;
    /// <summary>Surrender capstone: the one-per-descent first-sin drawback waiver has been spent.</summary>
    public bool SurrenderShieldUsed;
    /// <summary>White-rabbit spawn-rate multiplier (Tunnel Vision habit cut 2026-06-10; kept
    /// neutral at 1.0 for future boons to scale).</summary>
    public double RabbitRateMult = 1.0;
    /// <summary>Last Breath: defusing with this little fuse left pays <see cref="LastBreathPayMult"/>. 0 = off.</summary>
    public double LastBreathWindowSec;
    public double LastBreathPayMult = 1.0;
    /// <summary>Taking Chances: P(a pop pays x2) — the rest pay x0.5. 0 = coin off.</summary>
    public double ChanceDoubleOdds;
    /// <summary>Taking Chances: boon-draft rerolls left this descent.</summary>
    public int RerollsLeft;
    /// <summary>The Pull: per-frame drift bias toward the cursor (DIPs/frame). 0 = off. Rabbits home whenever &gt;0.</summary>
    public double CursorPullStrength;
    /// <summary>The Spanker: rabbits are smacked into allies instead of caught (no slow-mo from rabbits).</summary>
    public bool SpankerActive;
    /// <summary>The Spanker: growth factor applied to a rabbit each smack (total growth capped at x3).</summary>
    public double SpankGrowFactor = 1.0;
    /// <summary>Intrusive Thoughts: seconds each auto-spawned bouncing text lives (one every 5s). 0 = off.</summary>
    public double IntrusiveThoughtsSec;
    /// <summary>Chance per ordinary bubble spawn that a lucky golden bubble also surfaces
    /// (pays real gold — Sparks — on the spot). Base 0.5%; Rabbit's Foot raises it.</summary>
    public double GoldenChance = 0.005;
    /// <summary>Drip Feed: drops banked per pop (treats + defuses). 0 = off.</summary>
    public int DropPerPop;
    /// <summary>Drip Feed: drops gathered this run, banked at the end alongside the score award.</summary>
    public long TrickleDrops;
    /// <summary>Blank Eyes: float each pop's actual payout next to its pop word.</summary>
    public bool ShowPopScores;
    /// <summary>Pocket Watch: hang the wave countdown at the top of the screen.</summary>
    public bool ShowWaveTimer;
    /// <summary>Slow Recovery: pops needed to knit one resistance point back (0 = off).</summary>
    public int ShieldRegenPops;
    /// <summary>Benign-pop scoring baseline (0.4 unworn; the Golden Touch charm raises it to 0.45–0.60).</summary>
    public double BenignBaseline = 0.4;
    /// <summary>Breast Enlargement: multiplier on every variant bubble's rendered size (1.0 = unworn).
    /// Applies on top of the global 25% field shrink; never touches Strength/scoring.</summary>
    public double BubbleScale = 1.0;
    /// <summary>Resistance at run start (after boons) — the regen cap.</summary>
    public int StartShields;
    /// <summary>Ripple recharge seconds (the Skipping Stone charm shortens this: 13/11/9/8).</summary>
    public double RippleRechargeSec = ChaosTuning.RIPPLE_RECHARGE_SEC;
    /// <summary>Ripple wavefront reach in physical px (Skipping Stone widens it per level).</summary>
    public double RippleRadiusPx = ChaosTuning.RIPPLE_RADIUS_PX;
    /// <summary>Ripple expansion time in ms (Skipping Stone slows it per level — wider AND longer).</summary>
    public double RippleLifeMs = ChaosTuning.RIPPLE_LIFE_MS;
    /// <summary>Mood Ring (Wave 2, browser game only): 0 = unworn; 1 forecast the next
    /// weather, 2 weather effects x1.5, 3 one weather reroll per descent. The WPF game
    /// has no weather system — the knob just rides the loadout snapshot to JS.</summary>
    public int MoodRingLevel;
    /// <summary>Sticky Fingers (Wave 2, browser game only): the held tunnel card pops the
    /// treats it sweeps at a premium. 0 = unworn; the level tiers the pay (JS-side).</summary>
    public int StickyFingersLevel;

    // ---- run-boon (mantra/sin) knobs — the 2026-06-11 visible pool ----
    /// <summary>Gold Digger: golden bubbles burst into 3 falling gold droplets on pop.</summary>
    public bool GoldDiggerEnabled;
    /// <summary>Welcome Shower: every loop's GO! dumps a quick shower of treats.</summary>
    public bool WelcomeShowerEnabled;
    /// <summary>Heavy Drop: every Nth ordinary spawn is a giant slow triple-pay treat. 0 = off.</summary>
    public int HeavyDropEvery;
    /// <summary>GG make more GG: chance a popped treat births 3 uncatchable sweeper rabbits. 0 = off.</summary>
    public double GgRabbitChance;
    /// <summary>Size Queen: snapping a live bubble emits an expanding treat-popping ring.</summary>
    public bool RippleEnabled;
    /// <summary>Aftermath: a last-1.5s snap leaves 2s of crackling residue that pops what drifts through.</summary>
    public bool AftermathEnabled;
    /// <summary>Overload: multiplier on E-Stim charges per press (1 = unboosted).</summary>
    public int EStimChargeMult = 1;
    /// <summary>Afterglow: seconds the vibe trail lingers (and keeps popping) after the buzz. 0 = off.</summary>
    public double AfterglowSec;
    /// <summary>Casting Couch: DVD logos split on the bounce this many times (2 → two, then four). 0 = off.</summary>
    public int DvdSplitBounces;
    /// <summary>Tail-Plug: seconds of treat-popping sparkle trail each rabbit drags. 0 = off.</summary>
    public double RabbitTrailSec;
    /// <summary>Unleashed: a collar save also snaps every live bubble on screen.</summary>
    public bool UnleashedEnabled;
    /// <summary>Electrified Rabbits (Spanker + E-Stim): every bubble a spanked rabbit smacks
    /// discharges free E-Stim arcs into its neighbours.</summary>
    public bool ElectrifiedRabbits;
    /// <summary>Body Buzz (Poppers + E-Stim): chance a benign pop emits an electric shockwave
    /// that strikes every bubble in its ring. 0 = off.</summary>
    public double EStimShockwaveChance;
    /// <summary>"Focus here...": pay multiplier while the pendulum's slow swing holds. 1 = off.</summary>
    public double PendulumPayMult = 1.0;
    /// <summary>Playing with fire: detonation payload durations scale by this. 1 = off.</summary>
    public double DetonationDurationMult = 1.0;
    /// <summary>Playing with fire: snapping a bomb in its final second pays gold.</summary>
    public bool LastSecondGoldEnabled;
    /// <summary>"Look at the bright colors...": chance per spawn tick a mimic prism drifts in. 0 = off.</summary>
    public double PrismChance;
    /// <summary>Shielded bright_colors: prisms only ever mimic TREAT looks (no live payload sealed inside).</summary>
    public bool PrismTreatOnly;
    /// <summary>Cam Girl: per-frame drift bias AWAY from the cursor (tug-of-war vs The Pull). 0 = off.</summary>
    public double CamGirlFlee;
    /// <summary>Cam Girl: chance any pop tips gold. 0 = off.</summary>
    public double CamGirlTipChance;
    /// <summary>The urge: whole-stack pay multiplier for the rest of the run. 1 = off.</summary>
    public double UrgeMult = 1.0;
    /// <summary>The urge: active skills (toys) refuse to fire.</summary>
    public bool ActivesDisabled;
    /// <summary>Relapse: the bonus loop will fire when the run would end.</summary>
    public bool RelapseLoopArmed;
    /// <summary>Relapse: the bonus loop is running — gold and drops bank double.</summary>
    public bool RelapseLoopActive;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string ToRoman(int n) => n switch
    {
        <= 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V",
        6 => "VI", 7 => "VII", 8 => "VIII", 9 => "IX", _ => n.ToString()
    };
}
