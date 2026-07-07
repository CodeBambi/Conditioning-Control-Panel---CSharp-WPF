using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>The three lifetime-boon shelves shown as Hub tabs (we lean on the tropes).</summary>
public enum ChaosBoonCategory { Skill, Accessory, Utility }

/// <summary>
/// A permanent, leveled, toggleable meta-progression boon — distinct from the in-run
/// drafted boons (<see cref="ChaosBoonPool"/>, ephemeral), the single
/// <c>EquippedStartBoon</c>, and the always-on <see cref="ChaosUpgrade"/>s. Unlocked +
/// upgraded with Sparks, switched on/off in the Hub, and applied to the live
/// <see cref="ChaosRunState"/> at run start when active. Data-driven: the shelf grows by
/// adding records, not code paths. Art (if present) resolves via
/// <c>ChaosArt.Resolve("boons", Id)</c>; until then the <see cref="Glyph"/> is the placeholder.
/// </summary>
public sealed class ChaosLifetimeBoon
{
    public string Id = "";
    public ChaosBoonCategory Category;
    /// <summary>Lifetime-descent rank floor before this habit's Unlock (level 1) is buyable.
    /// Curious = available from the first visit; deeper ranks stagger the catalogue so it
    /// can't all be bought at once. Lessons + Spark cost stack ON TOP of this. (2026-06-12)</summary>
    public ChaosRank RankFloor = ChaosRank.Curious;
    public string Name = "";
    public string Desc = "";
    /// <summary>One flavorful line, rendered grey italic under the mechanics text.</summary>
    public string Flavor = "";
    public string Glyph = "◈";                       // placeholder icon char until real art ships
    public int UnlockCost;                            // Sparks for level 1
    public int[] UpgradeCosts = Array.Empty<int>();   // Sparks for levels 2..MaxLevel (len = MaxLevel - 1)
    public double[] LevelValues = Array.Empty<double>(); // the boon's value at each level ([level - 1])
    public string ValueLabel = "{0}";                 // how to render the level value, e.g. "{0:0.00}x reach"
    /// <summary>Max-rank bonus effect blurb shown on the Hub card (dim until maxed). Empty = no capstone.</summary>
    public string CapstoneDesc = "";
    /// <summary>Active-use skill: fires on its keybind / HUD button mid-descent instead of acting passively.</summary>
    public bool IsActiveUse;
    /// <summary>Seconds between uses for active-use skills. 0 = charge-based (LevelValues are uses per descent).</summary>
    public double UseCooldownSec;
    /// <summary>Wave 2: implemented only in the browser game (its mechanic lives in JS).
    /// The legacy WPF hub filters these out so it never sells an item it can't honor;
    /// the JS Warren ignores the flag. Dies with the legacy hub (task: legacy retirement).</summary>
    public bool WebOnly;
    public Action<ChaosRunState, double> Apply = (_, __) => { };

    /// <summary>Highest level this boon can reach (length of <see cref="LevelValues"/>).</summary>
    public int MaxLevel => LevelValues.Length;

    /// <summary>The value at a 1-indexed <paramref name="level"/>, clamped into range.</summary>
    public double ValueAt(int level) =>
        LevelValues.Length == 0 ? 0 : LevelValues[Math.Clamp(level, 1, LevelValues.Length) - 1];
}

/// <summary>The v1 lifetime-boon catalogue. Costs/levels live here for easy balancing.</summary>
public static class ChaosLifetimeBoons
{
    public static readonly IReadOnlyList<ChaosLifetimeBoon> All = new List<ChaosLifetimeBoon>
    {
        // ---- Skills (active-use: you press them mid-descent) ----
        new()
        {
            Id = "vibe_popping", Category = ChaosBoonCategory.Skill, RankFloor = ChaosRank.Slipping, Name = "VibePopping", Glyph = "🔸",
            Desc = "press for a 3/4/5/5s buzz by level. while it buzzes, hold left or right mouse and sweep: everything you brush over pops instantly, and live ones snap clean for full pay. 20s cooldown.",
            Flavor = "you don't have to aim. just let the hand wander.",
            UnlockCost = 400,                                     // grind doubled 2026-06-12: actives unlocked too fast
            UpgradeCosts = new[] { 500, 800, 1200 },              // levels 2..4
            LevelValues  = new[] { 3.0, 4, 5, 5 },                // buzz duration (seconds)
            ValueLabel = "{0:0}s buzz",
            CapstoneDesc = "no need to hold. while it buzzes, hovering alone pops.",
            IsActiveUse = true, UseCooldownSec = 20,
            Apply = (s, v) => s.ToyPower["vibe_popping"] = v,
        },
        new()
        {
            Id = "freeze_trigger", Category = ChaosBoonCategory.Skill, RankFloor = ChaosRank.Slipping, Name = "Freeze Trigger", Glyph = "❄",
            Desc = "press to freeze the whole field for 3.5s, exactly like a caught freeze bubble. 1/2/3/3 uses per descent, and holds channeled while frozen spend no focus.",
            Flavor = "stillness on demand. she lends it, never gives it.",
            UnlockCost = 500,                                     // grind doubled 2026-06-12
            UpgradeCosts = new[] { 800, 1300, 1800 },             // levels 2..4
            LevelValues  = new[] { 1.0, 2, 3, 3 },                // uses per descent
            ValueLabel = "{0:0} uses",
            CapstoneDesc = "each freeze also snaps every live bubble on screen.",
            IsActiveUse = true, UseCooldownSec = 0,               // charge-based
            Apply = (s, v) => s.ToyPower["freeze_trigger"] = v,
        },
        new()
        {
            Id = "porn_dvd", Category = ChaosBoonCategory.Skill, RankFloor = ChaosRank.Entranced, Name = "Porn DVD", Glyph = "📀",
            Desc = "press play: a logo bounces across the screen for 10/15/20/20s by level, popping every treat it touches and snapping every live one. bigger and faster at higher levels. 60s cooldown.",
            Flavor = "it always finds the corner eventually. so will you.",
            UnlockCost = 600,                                     // grind doubled 2026-06-12
            UpgradeCosts = new[] { 900, 1400, 2000 },             // levels 2..4
            LevelValues  = new[] { 10.0, 15, 20, 20 },            // flight time (seconds); speed+size also scale with rank
            ValueLabel = "{0:0}s playback",
            CapstoneDesc = "two screens.",
            IsActiveUse = true, UseCooldownSec = 60,
            Apply = (s, v) => s.ToyPower["porn_dvd"] = v,
        },
        new()
        {
            Id = "snap_field", Category = ChaosBoonCategory.Skill, RankFloor = ChaosRank.Entranced, Name = "Snap Field", Glyph = "✋",
            Desc = "the panic button. every live bubble on screen snaps at once, each paying in full. cooldown 60/45/30s by level.",
            Flavor = "one clean breath and the whole room lets go.",
            UnlockCost = 600,                                     // grind doubled 2026-06-12
            UpgradeCosts = new[] { 800, 1200 },                   // levels 2..3
            LevelValues  = new[] { 60.0, 45, 30 },                // cooldown seconds by level
            ValueLabel = "{0:0}s cooldown",
            CapstoneDesc = "the snap clears EVERYTHING — every bubble on screen goes.",
            IsActiveUse = true, UseCooldownSec = 60,              // real cooldown comes from the level value
            Apply = (s, v) => s.ToyPower["snap_field"] = v,
        },
        new()
        {
            Id = "rabbit_caller", Category = ChaosBoonCategory.Skill, RankFloor = ChaosRank.Tempted, Name = "Rabbit Caller", Glyph = "🐇",
            Desc = "press to arm the whistle, then click anywhere: 1/2/3 white rabbits by level arrive right where you pointed. 45s cooldown.",
            Flavor = "they were always waiting to be called.",
            UnlockCost = 500,                                     // grind doubled 2026-06-12
            UpgradeCosts = new[] { 700, 1100 },                   // levels 2..3
            LevelValues  = new[] { 1.0, 2, 3 },                   // rabbits per whistle
            ValueLabel = "{0:0} rabbits",
            CapstoneDesc = "each whistle also calls a storm — eight more rabbits over the next ten seconds.",
            IsActiveUse = true, UseCooldownSec = 45,
            Apply = (s, v) => s.ToyPower["rabbit_caller"] = v,
        },
        new()
        {
            Id = "e_stim", Category = ChaosBoonCategory.Skill, Name = "E-Stim", Glyph = "⚡",
            Desc = "press to charge your next 3/4/5 clicks by level. a charged pop arcs lightning into up to 3 bubbles within 600px, snapping any live ones. nothing in reach? the charge keeps. 30s cooldown.",
            Flavor = "the current knows exactly where you're tender.",
            UnlockCost = 600,                                     // grind doubled 2026-06-12
            UpgradeCosts = new[] { 900, 1300 },                   // levels 2..3
            LevelValues  = new[] { 3.0, 4, 5 },                   // charged clicks per press
            ValueLabel = "{0:0} charged pops",
            CapstoneDesc = "charged pops chain-react — the current leaps on through every bubble close enough, and onward.",
            IsActiveUse = true, UseCooldownSec = 30,
            Apply = (s, v) => s.ToyPower["e_stim"] = v,
        },

        // ---- Accessories (passives that shape the run) ----
        // breast_enlargement moved to Utility 2026-06-10 (it reads as a trained habit, not a
        // pocketed accessory) — same id, levels carry over; Utility pockets are uncapped.
        new()
        {
            Id = "surrender", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Slipping, Name = "Surrender", Glyph = "🕯",
            Desc = "every sin you accept adds +0.05/+0.10/+0.15x run multiplier by level, on top of whatever the sin pays.",
            Flavor = "you stopped pretending you'd say no.",
            UnlockCost = 150,
            UpgradeCosts = new[] { 250, 450 },                    // levels 2..3
            LevelValues  = new[] { 0.05, 0.10, 0.15 },            // extra BoonMult per accepted sin
            ValueLabel = "+{0:0.00}x per sin",
            CapstoneDesc = "every draft offers a sin, saying yes restores +1 resistance, and the first sin you embrace loses its sting entirely.",
            Apply = (s, v) => s.SinExtraMult = v,
        },
        // muscle_memory + magic_wand retired 2026-06-10 (pure stat passives, both duplicated by
        // habits — Slow Recovery / Soft Focus). ChaosMeta.Init refunds owners; never reuse the ids.
        // tunnel_vision + collar + pendulum moved to HABITS 2026-06-10 (they read as passives, not
        // accessories). Boon levels are scrubbed at load (no refund — never released); the same id
        // strings live on as ChaosUpgrade habits, which is fine: different stores.

        new()
        {
            // Display renamed Inflatable Plug → Poppers (2026-06-10); id is save-persisted, never change it.
            Id = "chain_reaction", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Tempted, Name = "Poppers", Glyph = "💨",
            Desc = "a popped bubble bursts outward and pops whatever it overlaps, rippling on through the cluster. burst reach x1.20/1.35/1.60/1.80/2.00 by level.",
            Flavor = "they dilate. everything opens a little wider.",
            UnlockCost = 150,
            UpgradeCosts = new[] { 120, 160, 220, 300 },          // levels 2..5
            LevelValues  = new[] { 1.2, 1.35, 1.6, 1.8, 2.0 },    // burst reach multiplier per level
            ValueLabel = "{0:0.00}x reach",
            Apply = (s, v) => s.ChainReactionReach = v,
        },
        new()
        {
            Id = "blindfold", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Entranced, Name = "Blindfold", Glyph = "🙈",
            Desc = "bubbles dim to 40/32/25% visibility by level. in exchange every pop and snap pays x1.50/x1.75/x2.00. golden bubbles, hearts, rabbits and glass stay bright.",
            Flavor = "you don't need to see them. you feel where they are.",
            UnlockCost = 300,
            UpgradeCosts = new[] { 450, 700 },                    // levels 2..3
            LevelValues  = new[] { 1.5, 1.75, 2.0 },              // payout multiplier
            ValueLabel = "x{0:0.00} payout",
            CapstoneDesc = "a heartbeat tells you when one is about to go. listen.",
            Apply = (s, v) =>
            {
                s.BlindfoldPayMult = v;
                s.BlindfoldActive = true;
                // The whisper deepens with the level: x1.5 → 40%, x1.75 → 32%, x2.0 → 25%.
                s.BlindfoldOpacity = v >= 2.0 ? 0.25 : v >= 1.75 ? 0.32 : 0.40;
            },
        },
        new()
        {
            Id = "last_breath", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Entranced, Name = "Last Breath", Glyph = "⏱",
            Desc = "snap a live bubble with 0.4/0.6/0.8s of trance left by level and it pays x5/x10/x20. the fuse ring burns solid red through the final 0.8s, so watch for it.",
            Flavor = "the closer the edge, the sweeter she sings.",
            UnlockCost = 250,
            UpgradeCosts = new[] { 350, 550 },                    // levels 2..3
            LevelValues  = new[] { 5.0, 10, 20 },                 // payout mult; the brink window widens with it (0.4/0.6/0.8s)
            ValueLabel = "x{0:0} at the brink",
            Apply = (s, v) =>
            {
                s.LastBreathPayMult = v;
                s.LastBreathWindowSec = v >= 20 ? 0.8 : v >= 10 ? 0.6 : 0.4;
            },
        },
        new()
        {
            Id = "taking_chances", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Entranced, Name = "Taking Chances", Glyph = "🎲",
            Desc = "every pop flips a coin: x2 or x0.5 pay, with 50/55/60% odds on the double by level. also grants 1/2/3 mantra draft rerolls per descent.",
            Flavor = "heads she wins, tails you do. you keep forgetting which is which.",
            UnlockCost = 250,
            UpgradeCosts = new[] { 300, 500 },                    // levels 2..3
            LevelValues  = new[] { 1.0, 2, 3 },                   // rerolls per descent; the coin also tilts: 50/50 → 45/55 → 40/60
            ValueLabel = "{0:0} rerolls",
            Apply = (s, v) =>
            {
                s.RerollsLeft = (int)v;
                s.ChanceDoubleOdds = 0.50 + 0.05 * (Math.Clamp(v, 1, 3) - 1);   // P(double pay)
            },
        },
        new()
        {
            Id = "the_pull", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Slipping, Name = "The Pull", Glyph = "🧭",
            Desc = "bubbles drift toward your cursor, pull strength 0.12/0.22/0.32/0.44/0.58 by level, and white rabbits fly straight at you instead of past you.",
            Flavor = "you're not chasing them. be honest.",
            UnlockCost = 200,
            UpgradeCosts = new[] { 200, 300, 450, 650 },          // levels 2..5
            LevelValues  = new[] { 0.12, 0.22, 0.32, 0.44, 0.58 },// drift bias (DIPs/frame); rabbits home at all levels
            ValueLabel = "{0:0.00} pull",
            Apply = (s, v) => s.CursorPullStrength = v,
        },
        new()
        {
            Id = "the_spanker", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Tempted, Name = "The Spanker", Glyph = "🏓",
            Desc = "rabbits can't be caught anymore. smack one and it turns, swells x1.20/1.45/1.70 by level, gains 18% speed per smack, and pops everything in its path. you give up the free slow-mo.",
            Flavor = "good rabbits get a pat. yours get the paddle.",
            UnlockCost = 300,
            UpgradeCosts = new[] { 450, 700 },                    // levels 2..3
            LevelValues  = new[] { 1.20, 1.45, 1.70 },            // one-time swell on the first smack
            ValueLabel = "x{0:0.00} swell",
            CapstoneDesc = "the bouncing texts answer to you too — smack them to turn them.",
            Apply = (s, v) => { s.SpankerActive = true; s.SpankGrowFactor = v; },
        },
        new()
        {
            Id = "intrusive_thoughts", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Slipping, Name = "Intrusive Thoughts", Glyph = "💭",
            Desc = "every 5 seconds a stray thought races across the screen for 3/4/5s by level, popping whatever it touches.",
            Flavor = "they aren't yours. they pop things anyway.",
            UnlockCost = 250,
            UpgradeCosts = new[] { 350, 550 },                    // levels 2..3
            LevelValues  = new[] { 3.0, 4, 5 },                   // seconds each thought lives (one spawns every 5s)
            ValueLabel = "{0:0}s thoughts",
            CapstoneDesc = "a thought that brushes a rabbit splits in two. and those split too. (max 8, +2s)",
            Apply = (s, v) => s.IntrusiveThoughtsSec = v,
        },

        // ---- Utility (charms — quiet, always-on trinkets; pockets are uncapped) ----
        new()
        {
            Id = "rabbits_foot", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Slipping, Name = "Rabbit's Foot", Glyph = "🍀",
            Desc = "lucky golden bubbles surface on 1.0/1.5/2.0/2.0% of spawns by level (0.5% without) and pay 12-24/14-28/16-32/20-40 gold on the spot.",
            Flavor = "it wasn't lucky for the rabbit.",
            UnlockCost = 200,
            UpgradeCosts = new[] { 350, 600, 900 },               // levels 2..4
            LevelValues  = new[] { 0.010, 0.015, 0.020, 0.020 },  // golden-bubble chance per spawn (base 0.5% unworn);
                                                                  // the gold scales by level too (see GoldenPayRange)
            ValueLabel = "{0:0.0%} lucky",
            CapstoneDesc = "the gold doubles — twenty to forty a bubble.",
            Apply = (s, v) => s.GoldenChance = v,
        },
        new()
        {
            Id = "drip_feed", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Entranced, Name = "Drip Feed", Glyph = "💧",
            Desc = "every treat popped and every trance snapped banks +1/+2/+3/+4 drops by level — up to 60/90/120/150 a descent — collected when you surface.",
            Flavor = "drop by drop. that's how anything fills.",
            UnlockCost = 250,
            UpgradeCosts = new[] { 400, 650, 1000 },              // levels 2..4
            // 2026-06-12 economy rework: 5/10/15/20 uncapped out-earned the entire score
            // formula (~550✦/run at L1) and replaced the economy. Now a bad-run FLOOR:
            // small per-pop trickle, capped per descent (see DripFeedCap).
            LevelValues  = new[] { 1.0, 2, 3, 4 },                // drops banked per pop (defuses + treats)
            ValueLabel = "+{0:0} a pop",
            CapstoneDesc = "the hole tips you 10% extra on everything gathered when you surface.",
            Apply = (s, v) => s.DropPerPop = (int)v,
        },
        new()
        {
            Id = "blank_eyes", Category = ChaosBoonCategory.Utility, Name = "Blank Eyes", Glyph = "👁",
            Desc = "every pop floats its true payout on screen, multipliers and coin flips included.",
            Flavor = "glaze over. let the numbers do the looking.",
            UnlockCost = 120,
            LevelValues  = new[] { 1.0 },                          // single-rank QoL toggle
            ValueLabel = "on",
            Apply = (s, _) => s.ShowPopScores = true,
        },
        new()
        {
            Id = "breast_enlargement", Category = ChaosBoonCategory.Utility, Name = "Breast Enlargement", Glyph = "🎈",
            Desc = "every effect bubble runs +5/+10/+15/+25% bigger by level. pay is unchanged, they're simply easier to touch.",
            Flavor = "fuller. rounder. harder to ignore.",
            UnlockCost = 120,
            UpgradeCosts = new[] { 180, 260, 380 },               // levels 2..4
            LevelValues  = new[] { 5.0, 10, 15, 25 },             // % size on every variant bubble
            ValueLabel = "+{0:0}% size",
            Apply = (s, v) => s.BubbleScale = 1.0 + v / 100.0,
        },
        new()
        {
            // Replaces the single-rank shield_recharge habit (retired 2026-06-10): regen is now
            // EARNED — pops, not seconds. The threshold tightens per level.
            Id = "slow_recovery", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Slipping, Name = "Slow Recovery", Glyph = "♻",
            Desc = "every 60/50/40/30 pops by level knits back one point of resistance, up to where you started.",
            Flavor = "it grows back slow. everything down here does.",
            UnlockCost = 200,
            UpgradeCosts = new[] { 300, 450, 650 },               // levels 2..4
            LevelValues  = new[] { 60.0, 50, 40, 30 },            // pops per regrown resistance point
            ValueLabel = "{0:0} pops a point",
            Apply = (s, v) => s.ShieldRegenPops = (int)v,
        },
        new()
        {
            // Replaces the +1 Start Resistance habit (retired 2026-06-10). Base resistance is
            // now ZERO — this charm is the only way to descend wearing any.
            Id = "start_resistance", Category = ChaosBoonCategory.Utility, Name = "It would never work on me...", Glyph = "♥",
            Desc = "you descend wearing +1/+2/+3 resistance by level. without it you start bare, at zero.",
            Flavor = "famous last words.",
            UnlockCost = 100,
            UpgradeCosts = new[] { 200, 350 },                    // levels 2..3
            LevelValues  = new[] { 1.0, 2, 3 },                   // resistance points at run start
            ValueLabel = "+{0:0} resistance",
            // Shields land before BeginRun captures StartShields (the regen cap), and the config
            // bump keeps the HUD's hollow-heart row sized to what you descended with.
            Apply = (s, v) => { s.Shields += (int)v; s.Config.StartingShields += (int)v; },
        },
        new()
        {
            // Reborn from the single-rank Collar habit (retired 2026-06-10): saves now level.
            Id = "collar", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Slipping, Name = "Collar", Glyph = "📿",
            Desc = "when a trigger slips past your resistance, the collar holds your streak: 1/2/3 saves per descent. the effect itself still plays.",
            Flavor = "the streak was never yours to drop.",
            UnlockCost = 200,
            UpgradeCosts = new[] { 300, 450 },                    // levels 2..3
            LevelValues  = new[] { 1.0, 2, 3 },                   // streak saves per descent
            ValueLabel = "{0:0} saves",
            Apply = (s, v) => s.CollarSaves = (int)v,
        },
        new()
        {
            // Deeper Pull (base_mult) + Golden Touch merged 2026-06-10: one charm, both halves —
            // the score baseline deepens AND calm pops carry their bonus, scaling together.
            Id = "golden_touch", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Tempted, Name = "Golden Touch", Glyph = "✨",
            Desc = "your run multiplier starts at x1.10/x1.20/x1.30/x1.45 by level, and calm pops pay from a 45/50/55/60% baseline instead of 40%.",
            Flavor = "everything you touch comes back heavier.",
            UnlockCost = 150,
            UpgradeCosts = new[] { 250, 400, 600 },               // levels 2..4
            LevelValues  = new[] { 1.1, 1.2, 1.3, 1.45 },         // BaseMult; calm-pop baseline scales with it
            ValueLabel = "x{0:0.00} baseline",
            Apply = (s, v) =>
            {
                s.Config.BaseMult = v;   // state.BaseMult reads through to Config — safe post-ctor
                // The calm-pop (benign) baseline climbs with the level: 0.45 → 0.50 → 0.55 → 0.60 (unworn 0.40).
                s.BenignBaseline = v >= 1.45 ? 0.60 : v >= 1.3 ? 0.55 : v >= 1.2 ? 0.50 : 0.45;
            },
        },
        new()
        {
            // Reborn from Take More (retired 2026-06-10) — instead of softening the sting,
            // it stretches the trance so you can beat it.
            Id = "slowburner", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Tempted, Name = "Slowburner", Glyph = "🐌",
            Desc = "live bubbles hold their trance 10/20/30/40% longer by level before they trigger.",
            Flavor = "no rush. she likes you slow.",
            UnlockCost = 150,
            UpgradeCosts = new[] { 250, 400, 600 },               // levels 2..4
            LevelValues  = new[] { 10.0, 20, 30, 40 },            // % slower fuse burn
            ValueLabel = "{0:0}% slower",
            CapstoneDesc = "snapping one in its final 1.5 seconds pays triple.",
            Apply = (s, v) => s.FuseTimeMult *= 1.0 + v / 100.0,
        },
        new()
        {
            // Wave 2 (browser game): the per-loop weather system's companion charm.
            Id = "mood_ring", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Tempted, Name = "Mood Ring", Glyph = "💍",
            Desc = "the sky down there changes every loop. level 1 forecasts the next weather on the HUD; level 2 makes every weather effect hit x1.5; level 3 lets you click the sky to reroll it, once per descent.",
            Flavor = "she always knows what you're in the mood for.",
            UnlockCost = 200,
            UpgradeCosts = new[] { 300, 450 },                    // levels 2..3
            LevelValues  = new[] { 1.0, 2, 3 },                   // feature tier
            ValueLabel = "tier {0:0}",
            CapstoneDesc = "click the sky — she changes her mind, once per descent.",
            WebOnly = true,
            Apply = (s, v) => s.MoodRingLevel = (int)v,
        },
        new()
        {
            Id = "pocket_watch", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Tempted, Name = "Pocket Watch", Glyph = "🕰",
            Desc = "the loop countdown hangs top right and the sidebar shows the descent clock. without it, time down here stays a mystery.",
            Flavor = "borrowed from the white rabbit. he knows where you live.",
            UnlockCost = 150,
            LevelValues  = new[] { 1.0 },                          // single-rank QoL toggle
            ValueLabel = "on",
            Apply = (s, _) => s.ShowWaveTimer = true,
        },
        new()
        {
            // The Ripple (right-click) is base kit — everyone gets one charge on a 15s gather.
            // This charm tunes the VERB: shorter gathers, wider/slower waves, and the capstone
            // skips the stone (three waves a second apart per cast).
            Id = "skipping_stone", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Entranced, Name = "Skipping Stone", Glyph = "🪨",
            Desc = "your ripple gathers in 13/11/9/8 seconds by level (15 bare-handed), and each level sends a wider, slower wave.",
            Flavor = "flat stone, still water. she taught you the wrist for it.",
            UnlockCost = 220,
            UpgradeCosts = new[] { 380, 650, 950 },               // levels 2..4
            LevelValues  = new[] { 13.0, 11, 9, 8 },              // recharge seconds
            ValueLabel = "{0:0}s gather",
            CapstoneDesc = "the stone skips — every cast sends three waves, a second apart.",
            Apply = (s, v) =>
            {
                s.RippleRechargeSec = v;
                int lvl = ChaosMeta.BoonLevel("skipping_stone");
                s.RippleRadiusPx = ChaosTuning.RIPPLE_RADIUS_PX + lvl * ChaosTuning.RIPPLE_RADIUS_PER_LVL_PX;
                s.RippleLifeMs = ChaosTuning.RIPPLE_LIFE_MS + lvl * ChaosTuning.RIPPLE_LIFE_PER_LVL_MS;
            },
        },
    };

    /// <summary>Drip Feed's per-descent trickle ceiling by per-pop value (== level, 1..4):
    /// 60/90/120/150✦. The cap bounds the Relapse-loop doubling too — the trickle is a
    /// floor-raiser, never a second economy.</summary>
    public static int DripFeedCap(int dropPerPop) => 30 + 30 * Math.Clamp(dropPerPop, 1, 4);

    /// <summary>Gold paid by a lucky golden bubble at a Rabbit's Foot level (0 = unworn).
    /// Scales per level; the capstone is the doubled base range.</summary>
    public static (int Min, int Max) GoldenPayRange(int level) => level switch
    {
        <= 0 => (10, 20),
        1    => (12, 24),
        2    => (14, 28),
        3    => (16, 32),
        _    => (20, 40),   // level 4: the gold doubles
    };

    public static ChaosLifetimeBoon? ById(string id) => All.FirstOrDefault(b => b.Id == id);

    public static IEnumerable<ChaosLifetimeBoon> InCategory(ChaosBoonCategory cat) =>
        All.Where(b => b.Category == cat);

    // The legacy WPF hub's view: WebOnly items (their mechanics live in the browser
    // game's JS) are hidden so the old hub never sells something it can't honor.
    public static IEnumerable<ChaosLifetimeBoon> LegacyAll => All.Where(b => !b.WebOnly);

    public static IEnumerable<ChaosLifetimeBoon> LegacyInCategory(ChaosBoonCategory cat) =>
        InCategory(cat).Where(b => !b.WebOnly);
}
