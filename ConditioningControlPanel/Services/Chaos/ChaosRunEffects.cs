using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The run-side half of the Chaos meta catalogue. The catalogues and every purchase rule live
/// in Core (<see cref="ChaosUpgrades"/>, <see cref="ChaosLifetimeBoons"/>, <see cref="ChaosMeta"/>);
/// the effects each id has on a run stay here because <see cref="ChaosRunConfig"/> and
/// <see cref="ChaosRunState"/> are head-side (the run engine). Same lambdas, keyed by id.
/// </summary>
public static class ChaosRunEffects
{
    /// <summary>Habit (ChaosUpgrade) effects on a freshly built run config. Every catalogue id
    /// is listed (ChaosRunEffectsCoverageTests), the setup-only unlocks as explicit no-ops.</summary>
    public static readonly IReadOnlyDictionary<string, Action<ChaosRunConfig>> Habits = new Dictionary<string, Action<ChaosRunConfig>>
    {
        ["slow_fuses"] = c => c.FuseTimeMult *= 1.15,
        ["silk_touch"] = c => { c.HitboxScale = 1.25; c.MagnetEnabled = true; },
        ["popup_notification"] = c => c.PopupHeartEnabled = true,
        ["pendulum_swing"] = c => c.PendulumSwing = true,
        ["draft4"] = c => c.DraftChoices = 4,
        ["extreme_tier"] = _ => { },      // flag stored at purchase time
        ["custom_duration"] = _ => { },   // opens the length dial in setup; no in-run effect
        ["endless_mode"] = _ => { },      // per-run toggle in setup; no always-on effect
    };

    /// <summary>Lifetime-boon effects on the live run state, at the boon's level value.</summary>
    public static readonly IReadOnlyDictionary<string, Action<ChaosRunState, double>> Boons = new Dictionary<string, Action<ChaosRunState, double>>
    {
        ["vibe_popping"] = (s, v) => s.ToyPower["vibe_popping"] = v,
        ["freeze_trigger"] = (s, v) => s.ToyPower["freeze_trigger"] = v,
        ["porn_dvd"] = (s, v) => s.ToyPower["porn_dvd"] = v,
        ["snap_field"] = (s, v) => s.ToyPower["snap_field"] = v,
        ["rabbit_caller"] = (s, v) => s.ToyPower["rabbit_caller"] = v,
        ["e_stim"] = (s, v) => s.ToyPower["e_stim"] = v,
        ["the_wand"] = (s, v) => s.ToyPower["the_wand"] = v,
        ["the_pump"] = (s, v) => s.ToyPower["the_pump"] = v,
        ["sticky_fingers"] = (s, v) => s.StickyFingersLevel = ChaosMeta.BoonLevel("sticky_fingers"),
        ["surrender"] = (s, v) => s.SinExtraMult = v,
        ["chain_reaction"] = (s, v) => s.ChainReactionReach = v,
        ["blindfold"] = (s, v) =>
        {
            s.BlindfoldPayMult = v;
            s.BlindfoldActive = true;
            // The whisper deepens with the level: x1.5 → 40%, x1.75 → 32%, x2.0 → 25%.
            s.BlindfoldOpacity = v >= 2.0 ? 0.25 : v >= 1.75 ? 0.32 : 0.40;
        },
        ["last_breath"] = (s, v) =>
        {
            s.LastBreathPayMult = v;
            s.LastBreathWindowSec = v >= 20 ? 0.8 : v >= 10 ? 0.6 : 0.4;
        },
        ["taking_chances"] = (s, v) =>
        {
            s.RerollsLeft = (int)v;
            s.ChanceDoubleOdds = 0.50 + 0.05 * (Math.Clamp(v, 1, 3) - 1);   // P(double pay)
        },
        ["the_pull"] = (s, v) => s.CursorPullStrength = v,
        ["the_spanker"] = (s, v) => { s.SpankerActive = true; s.SpankGrowFactor = v; },
        ["intrusive_thoughts"] = (s, v) => s.IntrusiveThoughtsSec = v,
        ["rabbits_foot"] = (s, v) => s.GoldenChance = v,
        ["drip_feed"] = (s, v) => s.DropPerPop = (int)v,
        ["blank_eyes"] = (s, _) => s.ShowPopScores = true,
        ["breast_enlargement"] = (s, v) => s.BubbleScale = 1.0 + v / 100.0,
        ["slow_recovery"] = (s, v) => s.ShieldRegenPops = (int)v,
        ["start_resistance"] = (s, v) => { s.Shields += (int)v; s.Config.StartingShields += (int)v; },
        ["collar"] = (s, v) => s.CollarSaves = (int)v,
        ["golden_touch"] = (s, v) =>
        {
            s.Config.BaseMult = v;   // state.BaseMult reads through to Config — safe post-ctor
            // The calm-pop (benign) baseline climbs with the level: 0.45 → 0.50 → 0.55 → 0.60 (unworn 0.40).
            s.BenignBaseline = v >= 1.45 ? 0.60 : v >= 1.3 ? 0.55 : v >= 1.2 ? 0.50 : 0.45;
        },
        ["slowburner"] = (s, v) => s.FuseTimeMult *= 1.0 + v / 100.0,
        ["mood_ring"] = (s, v) => s.MoodRingLevel = (int)v,
        ["pocket_watch"] = (s, _) => s.ShowWaveTimer = true,
        ["skipping_stone"] = (s, v) =>
        {
            s.RippleRechargeSec = v;
            int lvl = ChaosMeta.BoonLevel("skipping_stone");
            s.RippleRadiusPx = ChaosTuning.RIPPLE_RADIUS_PX + lvl * ChaosTuning.RIPPLE_RADIUS_PER_LVL_PX;
            s.RippleLifeMs = ChaosTuning.RIPPLE_LIFE_MS + lvl * ChaosTuning.RIPPLE_LIFE_PER_LVL_MS;
        },
    };

    /// <summary>Apply every owned-and-switched-on upgrade's effect to a freshly-built run config.</summary>
    public static void ApplyTo(ChaosRunConfig config)
    {
        if (config == null) return;
        foreach (var id in ChaosMeta.State.PurchasedUpgrades)
            if (ChaosMeta.IsUpgradeActive(id) && Habits.TryGetValue(id, out var apply))
                apply(config);
    }

    /// <summary>Apply every active+unlocked lifetime boon (at its current level) to the run state.</summary>
    public static void ApplyLifetimeBoons(ChaosRunState run)
    {
        if (run == null || ChaosMeta.State.ActiveLifetimeBoons == null) return;
        foreach (var id in ChaosMeta.State.ActiveLifetimeBoons)
        {
            int lvl = ChaosMeta.BoonLevel(id);
            var b = ChaosLifetimeBoons.ById(id);
            if (b != null && lvl >= 1)
            {
                if (Boons.TryGetValue(b.Id, out var apply)) apply(run, b.ValueAt(lvl));
                if (lvl >= b.MaxLevel) run.MaxedBoons.Add(b.Id);   // capstone effects key off this
            }
        }
    }

    /// <summary>
    /// Bank Sparks + update lifetime stats at the end of a completed run, then persist.
    /// Formula (2026-06-12 economy rework, Hades pacing — full collection ≈ 100 descents):
    /// <c>round((1.5·√score + 35·diff·min(1, durMin/3)) * SparkGainMult)</c>.
    /// The square root compresses the late-game multiplier explosion (×10-20 score over a
    /// fresh save → ×3-4 drops) and self-normalizes duration (double-length run ≈ ×1.4).
    /// Difficulty is NOT re-applied to the score part — DifficultyMult already multiplies
    /// every pop inside TotalMult, so the old <c>score/100·diff</c> double-dipped
    /// (Inescapable paid ~×4.8 Gentle). The completion bonus keeps the linear diff scalar
    /// and scales down below 3 minutes so 60-second runs can't farm the flat floor.
    /// Returns the Sparks banked so the recap card can show the haul.
    /// </summary>
    public static int AwardRunRewards(ChaosRunState run)
    {
        if (run == null) return 0;
        return ChaosMeta.AwardRunRewards(new ChaosMeta.ChaosRunRewardInput(
            RunDurationSec: run.RunDurationSec,
            DifficultyMult: run.Config.DifficultyMult,
            SparkGainMult: run.Config.SparkGainMult,
            Score: run.Score,
            TrickleDrops: run.TrickleDrops,
            DripFeedMaxed: run.MaxedBoons.Contains("drip_feed"),
            BestCombo: run.BestCombo,
            Defused: run.Defused,
            ElapsedSec: run.ElapsedSec));
    }
}
