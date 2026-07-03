using System;
using System.Collections.Generic;
using global::Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Seeds the Avalonia Chaos stub catalogues (lifetime boons, upgrades, draftable mantras,
/// bubble variants) so the Hub shelves and the run-time draft UI are populated. This is a
/// stand-in until the WPF catalogue classes are moved into CCP.Core and shared.
/// </summary>
public static class AvaloniaChaosCatalogs
{
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        SeedLifetimeBoons();
        SeedUpgrades();
        SeedBoonPool();
        SeedBubbleVariants();
    }

    private static void SeedLifetimeBoons()
    {
        void Add(ChaosLifetimeBoon b)
        {
            if (ChaosLifetimeBoons.All.Exists(x => x.Id == b.Id)) return;
            ChaosLifetimeBoons.All.Add(b);
        }

        // ---- Toys (active-use skills) ----
        Add(new ChaosLifetimeBoon
        {
            Id = "vibe_popping", Category = ChaosBoonCategory.Skill, RankFloor = ChaosRank.Slipping,
            Name = "VibePopping", Glyph = "🔸",
            Desc = "press for a 3/4/5/5s buzz by level. while it buzzes, hold left or right mouse and sweep: everything you brush over pops instantly, and live ones snap clean for full pay. 20s cooldown.",
            Flavor = "you don't have to aim. just let the hand wander.",
            UnlockCost = 400, MaxLevel = 4, ValueLabel = "{0:0}s buzz",
            UpgradeCosts = new[] { 500, 800, 1200 },
            LevelValues = new[] { 3.0, 4, 5, 5 },
            CapstoneDesc = "no need to hold. while it buzzes, hovering alone pops.",
            IsActiveUse = true, UseCooldownSec = 20,
            Apply = (s, v) => s.ToyPower["vibe_popping"] = v,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "freeze_trigger", Category = ChaosBoonCategory.Skill, RankFloor = ChaosRank.Slipping,
            Name = "Freeze Trigger", Glyph = "❄",
            Desc = "press to freeze the whole field for 3.5s, exactly like a caught freeze bubble. 1/2/3/3 uses per descent, and holds channeled while frozen spend no focus.",
            Flavor = "stillness on demand. she lends it, never gives it.",
            UnlockCost = 500, MaxLevel = 4, ValueLabel = "{0:0} uses",
            UpgradeCosts = new[] { 800, 1300, 1800 },
            LevelValues = new[] { 1.0, 2, 3, 3 },
            CapstoneDesc = "each freeze also snaps every live bubble on screen.",
            IsActiveUse = true, UseCooldownSec = 0,
            Apply = (s, v) => s.ToyPower["freeze_trigger"] = v,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "porn_dvd", Category = ChaosBoonCategory.Skill, RankFloor = ChaosRank.Entranced,
            Name = "Porn DVD", Glyph = "📀",
            Desc = "press play: a logo bounces across the screen for 10/15/20/20s by level, popping every treat it touches and snapping every live one. bigger and faster at higher levels. 60s cooldown.",
            Flavor = "it always finds the corner eventually. so will you.",
            UnlockCost = 600, MaxLevel = 4, ValueLabel = "{0:0}s playback",
            UpgradeCosts = new[] { 900, 1400, 2000 },
            LevelValues = new[] { 10.0, 15, 20, 20 },
            CapstoneDesc = "two screens.",
            IsActiveUse = true, UseCooldownSec = 60,
            Apply = (s, v) => s.ToyPower["porn_dvd"] = v,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "snap_field", Category = ChaosBoonCategory.Skill, RankFloor = ChaosRank.Entranced,
            Name = "Snap Field", Glyph = "✋",
            Desc = "the panic button. every live bubble on screen snaps at once, each paying in full. cooldown 60/45/30s by level.",
            Flavor = "one clean breath and the whole room lets go.",
            UnlockCost = 600, MaxLevel = 3, ValueLabel = "{0:0}s cooldown",
            UpgradeCosts = new[] { 800, 1200 },
            LevelValues = new[] { 60.0, 45, 30 },
            CapstoneDesc = "the snap clears EVERYTHING — every bubble on screen goes.",
            IsActiveUse = true, UseCooldownSec = 60,
            Apply = (s, v) => s.ToyPower["snap_field"] = v,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "rabbit_caller", Category = ChaosBoonCategory.Skill, RankFloor = ChaosRank.Tempted,
            Name = "Rabbit Caller", Glyph = "🐇",
            Desc = "press to arm the whistle, then click anywhere: 1/2/3 white rabbits by level arrive right where you pointed. 45s cooldown.",
            Flavor = "they were always waiting to be called.",
            UnlockCost = 500, MaxLevel = 3, ValueLabel = "{0:0} rabbits",
            UpgradeCosts = new[] { 700, 1100 },
            LevelValues = new[] { 1.0, 2, 3 },
            CapstoneDesc = "each whistle also calls a storm — eight more rabbits over the next ten seconds.",
            IsActiveUse = true, UseCooldownSec = 45,
            Apply = (s, v) => s.ToyPower["rabbit_caller"] = v,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "e_stim", Category = ChaosBoonCategory.Skill,
            Name = "E-Stim", Glyph = "⚡",
            Desc = "press to charge your next 3/4/5 clicks by level. a charged pop arcs lightning into up to 3 bubbles within 600px, snapping any live ones. nothing in reach? the charge keeps. 30s cooldown.",
            Flavor = "the current knows exactly where you're tender.",
            UnlockCost = 600, MaxLevel = 3, ValueLabel = "{0:0} charged pops",
            UpgradeCosts = new[] { 900, 1300 },
            LevelValues = new[] { 3.0, 4, 5 },
            CapstoneDesc = "charged pops chain-react — the current leaps on through every bubble close enough, and onward.",
            IsActiveUse = true, UseCooldownSec = 30,
            Apply = (s, v) => s.ToyPower["e_stim"] = v,
        });

        // ---- Accessories (passives) ----
        Add(new ChaosLifetimeBoon
        {
            Id = "surrender", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Slipping,
            Name = "Surrender", Glyph = "🕯",
            Desc = "every sin you accept adds +0.05/+0.10/+0.15x run multiplier by level, on top of whatever the sin pays.",
            Flavor = "you stopped pretending you'd say no.",
            UnlockCost = 150, MaxLevel = 3, ValueLabel = "+{0:0.00}x per sin",
            UpgradeCosts = new[] { 250, 450 },
            LevelValues = new[] { 0.05, 0.10, 0.15 },
            CapstoneDesc = "every draft offers a sin, saying yes restores +1 resistance, and the first sin you embrace loses its sting entirely.",
            Apply = (s, v) => s.SinExtraMult = v,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "chain_reaction", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Tempted,
            Name = "Poppers", Glyph = "💨",
            Desc = "a popped bubble bursts outward and pops whatever it overlaps, rippling on through the cluster. burst reach x1.20/1.35/1.60/1.80/2.00 by level.",
            Flavor = "they dilate. everything opens a little wider.",
            UnlockCost = 150, MaxLevel = 5, ValueLabel = "{0:0.00}x reach",
            UpgradeCosts = new[] { 120, 160, 220, 300 },
            LevelValues = new[] { 1.2, 1.35, 1.6, 1.8, 2.0 },
            Apply = (s, v) => s.ChainReactionReach = v,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "blindfold", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Entranced,
            Name = "Blindfold", Glyph = "🙈",
            Desc = "bubbles dim to 40/32/25% visibility by level. in exchange every pop and snap pays x1.50/x1.75/x2.00.",
            Flavor = "you don't need to see them. you feel where they are.",
            UnlockCost = 300, MaxLevel = 3, ValueLabel = "x{0:0.00} payout",
            UpgradeCosts = new[] { 450, 700 },
            LevelValues = new[] { 1.5, 1.75, 2.0 },
            CapstoneDesc = "a heartbeat tells you when one is about to go. listen.",
            Apply = (s, v) =>
            {
                s.BlindfoldPayMult = v;
                s.BlindfoldActive = true;
                s.BlindfoldOpacity = v >= 2.0 ? 0.25 : v >= 1.75 ? 0.32 : 0.40;
            },
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "last_breath", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Entranced,
            Name = "Last Breath", Glyph = "⏱",
            Desc = "snap a live bubble with 0.4/0.6/0.8s of trance left by level and it pays x5/x10/x20.",
            Flavor = "the closer the edge, the sweeter she sings.",
            UnlockCost = 250, MaxLevel = 3, ValueLabel = "x{0:0} at the brink",
            UpgradeCosts = new[] { 350, 550 },
            LevelValues = new[] { 5.0, 10, 20 },
            Apply = (s, v) =>
            {
                s.LastBreathPayMult = v;
                s.LastBreathWindowSec = v >= 20 ? 0.8 : v >= 10 ? 0.6 : 0.4;
            },
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "taking_chances", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Entranced,
            Name = "Taking Chances", Glyph = "🎲",
            Desc = "every pop flips a coin: x2 or x0.5 pay, with 50/55/60% odds on the double by level. also grants 1/2/3 mantra draft rerolls per descent.",
            Flavor = "heads she wins, tails you do. you keep forgetting which is which.",
            UnlockCost = 250, MaxLevel = 3, ValueLabel = "{0:0} rerolls",
            UpgradeCosts = new[] { 300, 500 },
            LevelValues = new[] { 1.0, 2, 3 },
            Apply = (s, v) =>
            {
                s.RerollsLeft = (int)v;
                s.ChanceDoubleOdds = 0.50 + 0.05 * (Math.Clamp(v, 1, 3) - 1);
            },
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "the_pull", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Slipping,
            Name = "The Pull", Glyph = "🧭",
            Desc = "bubbles drift toward your cursor, pull strength 0.12/0.22/0.32/0.44/0.58 by level, and white rabbits fly straight at you instead of past you.",
            Flavor = "you're not chasing them. be honest.",
            UnlockCost = 200, MaxLevel = 5, ValueLabel = "{0:0.00} pull",
            UpgradeCosts = new[] { 200, 300, 450, 650 },
            LevelValues = new[] { 0.12, 0.22, 0.32, 0.44, 0.58 },
            Apply = (s, v) => s.CursorPullStrength = v,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "the_spanker", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Tempted,
            Name = "The Spanker", Glyph = "🏓",
            Desc = "rabbits can't be caught anymore. smack one and it turns, swells x1.20/1.45/1.70 by level, gains 18% speed per smack, and pops everything in its path.",
            Flavor = "good rabbits get a pat. yours get the paddle.",
            UnlockCost = 300, MaxLevel = 3, ValueLabel = "x{0:0.00} swell",
            UpgradeCosts = new[] { 450, 700 },
            LevelValues = new[] { 1.20, 1.45, 1.70 },
            CapstoneDesc = "the bouncing texts answer to you too — smack them to turn them.",
            Apply = (s, v) => { s.SpankerActive = true; s.SpankGrowFactor = v; },
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "intrusive_thoughts", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Slipping,
            Name = "Intrusive Thoughts", Glyph = "💭",
            Desc = "every 5 seconds a stray thought races across the screen for 3/4/5s by level, popping whatever it touches.",
            Flavor = "they aren't yours. they pop things anyway.",
            UnlockCost = 250, MaxLevel = 3, ValueLabel = "{0:0}s thoughts",
            UpgradeCosts = new[] { 350, 550 },
            LevelValues = new[] { 3.0, 4, 5 },
            CapstoneDesc = "a thought that brushes a rabbit splits in two. and those split too. (max 8, +2s)",
            Apply = (s, v) => s.IntrusiveThoughtsSec = v,
        });

        // ---- Utility (charms) ----
        Add(new ChaosLifetimeBoon
        {
            Id = "rabbits_foot", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Slipping,
            Name = "Rabbit's Foot", Glyph = "🍀",
            Desc = "lucky golden bubbles surface on 1.0/1.5/2.0/2.0% of spawns by level and pay 12-24/14-28/16-32/20-40 gold on the spot.",
            Flavor = "it wasn't lucky for the rabbit.",
            UnlockCost = 200, MaxLevel = 4, ValueLabel = "{0:0.0%} lucky",
            UpgradeCosts = new[] { 350, 600, 900 },
            LevelValues = new[] { 0.010, 0.015, 0.020, 0.020 },
            CapstoneDesc = "the gold doubles — twenty to forty a bubble.",
            Apply = (s, v) => s.GoldenChance = v,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "drip_feed", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Entranced,
            Name = "Drip Feed", Glyph = "💧",
            Desc = "every treat popped and every trance snapped banks +1/+2/+3/+4 drops by level — up to 60/90/120/150 a descent — collected when you surface.",
            Flavor = "drop by drop. that's how anything fills.",
            UnlockCost = 250, MaxLevel = 4, ValueLabel = "+{0:0} a pop",
            UpgradeCosts = new[] { 400, 650, 1000 },
            LevelValues = new[] { 1.0, 2, 3, 4 },
            CapstoneDesc = "the hole tips you 10% extra on everything gathered when you surface.",
            Apply = (s, v) => s.DropPerPop = (int)v,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "blank_eyes", Category = ChaosBoonCategory.Utility,
            Name = "Blank Eyes", Glyph = "👁",
            Desc = "every pop floats its true payout on screen, multipliers and coin flips included.",
            Flavor = "glaze over. let the numbers do the looking.",
            UnlockCost = 120, MaxLevel = 1, ValueLabel = "on",
            LevelValues = new[] { 1.0 },
            Apply = (s, v) => s.ShowPopScores = true,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "breast_enlargement", Category = ChaosBoonCategory.Utility,
            Name = "Breast Enlargement", Glyph = "🎈",
            Desc = "every effect bubble runs +5/+10/+15/+25% bigger by level. pay is unchanged, they're simply easier to touch.",
            Flavor = "fuller. rounder. harder to ignore.",
            UnlockCost = 120, MaxLevel = 4, ValueLabel = "+{0:0}% size",
            UpgradeCosts = new[] { 180, 260, 380 },
            LevelValues = new[] { 5.0, 10, 15, 25 },
            Apply = (s, v) => s.BubbleScale = 1.0 + v / 100.0,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "slow_recovery", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Slipping,
            Name = "Slow Recovery", Glyph = "♻",
            Desc = "every 60/50/40/30 pops by level knits back one point of resistance, up to where you started.",
            Flavor = "it grows back slow. everything down here does.",
            UnlockCost = 200, MaxLevel = 4, ValueLabel = "{0:0} pops a point",
            UpgradeCosts = new[] { 300, 450, 650 },
            LevelValues = new[] { 60.0, 50, 40, 30 },
            Apply = (s, v) => s.ShieldRegenPops = (int)v,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "start_resistance", Category = ChaosBoonCategory.Utility,
            Name = "It would never work on me...", Glyph = "♥",
            Desc = "you descend wearing +1/+2/+3 resistance by level. without it you start bare, at zero.",
            Flavor = "famous last words.",
            UnlockCost = 100, MaxLevel = 3, ValueLabel = "+{0:0} resistance",
            UpgradeCosts = new[] { 200, 350 },
            LevelValues = new[] { 1.0, 2, 3 },
            Apply = (s, v) => { s.Shields += (int)v; s.Config.StartingShields += (int)v; },
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "collar", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Slipping,
            Name = "Collar", Glyph = "📿",
            Desc = "when a trigger slips past your resistance, the collar holds your streak: 1/2/3 saves per descent.",
            Flavor = "the streak was never yours to drop.",
            UnlockCost = 200, MaxLevel = 3, ValueLabel = "{0:0} saves",
            UpgradeCosts = new[] { 300, 450 },
            LevelValues = new[] { 1.0, 2, 3 },
            Apply = (s, v) => s.CollarSaves = (int)v,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "golden_touch", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Tempted,
            Name = "Golden Touch", Glyph = "✨",
            Desc = "your run multiplier starts at x1.10/x1.20/x1.30/x1.45 by level, and calm pops pay from a 45/50/55/60% baseline instead of 40%.",
            Flavor = "everything you touch comes back heavier.",
            UnlockCost = 150, MaxLevel = 4, ValueLabel = "x{0:0.00} baseline",
            UpgradeCosts = new[] { 250, 400, 600 },
            LevelValues = new[] { 1.1, 1.2, 1.3, 1.45 },
            Apply = (s, v) =>
            {
                s.Config.BaseMult = v;
                s.BenignBaseline = v >= 1.45 ? 0.60 : v >= 1.3 ? 0.55 : v >= 1.2 ? 0.50 : 0.45;
            },
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "slowburner", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Tempted,
            Name = "Slowburner", Glyph = "🐌",
            Desc = "live bubbles hold their trance 10/20/30/40% longer by level before they trigger.",
            Flavor = "no rush. she likes you slow.",
            UnlockCost = 150, MaxLevel = 4, ValueLabel = "{0:0}% slower",
            UpgradeCosts = new[] { 250, 400, 600 },
            LevelValues = new[] { 10.0, 20, 30, 40 },
            CapstoneDesc = "snapping one in its final 1.5 seconds pays triple.",
            Apply = (s, v) => s.FuseTimeMult *= 1.0 + v / 100.0,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "pocket_watch", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Tempted,
            Name = "Pocket Watch", Glyph = "🕰",
            Desc = "the loop countdown hangs top right and the sidebar shows the descent clock. without it, time down here stays a mystery.",
            Flavor = "borrowed from the white rabbit. he knows where you live.",
            UnlockCost = 150, MaxLevel = 1, ValueLabel = "on",
            LevelValues = new[] { 1.0 },
            Apply = (s, v) => s.ShowWaveTimer = true,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "skipping_stone", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Entranced,
            Name = "Skipping Stone", Glyph = "🪨",
            Desc = "your ripple gathers in 13/11/9/8 seconds by level (15 bare-handed), and each level sends a wider, slower wave.",
            Flavor = "flat stone, still water. she taught you the wrist for it.",
            UnlockCost = 220, MaxLevel = 4, ValueLabel = "{0:0}s gather",
            UpgradeCosts = new[] { 380, 650, 950 },
            LevelValues = new[] { 13.0, 11, 9, 8 },
            CapstoneDesc = "the stone skips — every cast sends three waves, a second apart.",
            Apply = (s, v) =>
            {
                s.RippleRechargeSec = v;
                int lvl = ChaosMeta.BoonLevel("skipping_stone");
                s.RippleRadiusPx = ChaosTuning.RIPPLE_RADIUS_PX + lvl * ChaosTuning.RIPPLE_RADIUS_PER_LVL_PX;
                s.RippleLifeMs = ChaosTuning.RIPPLE_LIFE_MS + lvl * ChaosTuning.RIPPLE_LIFE_PER_LVL_MS;
            },
        });
    }

    private static void SeedUpgrades()
    {
        void Add(ChaosUpgrade u)
        {
            if (ChaosUpgrades.All.Exists(x => x.Id == u.Id)) return;
            ChaosUpgrades.All.Add(u);
        }

        Add(new ChaosUpgrade { Id = "slow_fuses", Branch = ChaosBranch.Control, Name = "Slower Trance", Cost = 120, Glyph = "⏳",
            Desc = "live bubbles hold their trance 15% longer before they trigger.",
            Flavor = "a little more time to change your mind. you won't." });
        Add(new ChaosUpgrade { Id = "silk_touch", Branch = ChaosBranch.Control, Name = "Silk Touch", Cost = 180, Glyph = "🪶",
            Desc = "bubble hitboxes grow 25%, and a near-miss on a live one still counts as a touch.",
            Flavor = "silk doesn't try. it just lands." });
        Add(new ChaosUpgrade { Id = "popup_notification", Branch = ChaosBranch.Control, Name = "Pop-up Notification", Cost = 160, Glyph = "💖",
            Desc = "once per loop, 60% of the time, a heart drifts down mid-loop. catch it for +1 resistance and +10 focus.",
            Flavor = "you opted in. you always opt in." });
        Add(new ChaosUpgrade { Id = "pendulum_swing", Branch = ChaosBranch.Control, Name = "Pendulum", Cost = 220, Glyph = "🕰",
            Desc = "once per loop, at a random beat, the pendulum swings: 2.5 seconds of slow motion.",
            Flavor = "tick. tock. you looked." });
        Add(new ChaosUpgrade { Id = "draft4", Branch = ChaosBranch.Depth, Name = "4-Mantra Draft", Cost = 200, Glyph = "🃏",
            Desc = "mantra drafts offer four choices instead of three.",
            Flavor = "more ways to say yes." });
        Add(new ChaosUpgrade { Id = "extreme_tier", Branch = ChaosBranch.Depth, Name = "Inescapable Tier", Cost = 350, Glyph = "🌀",
            Desc = "opens the inescapable difficulty in the descent setup.",
            Flavor = "the last door was never locked." });
    }

    private static void SeedBoonPool()
    {
        void Add(ChaosBoon b)
        {
            if (ChaosBoonPool.All.Exists(x => x.Id == b.Id)) return;
            ChaosBoonPool.All.Add(b);
        }

        // ---- WPF parity: v1 draftable mantras / sins ----
        Add(new ChaosBoon
        {
            Id = "defuse_chain", Name = "Snap Chain",
            Desc = "for 0.9s after every snap, a trigger can't break your streak, feed your lust, or spend your resistance. +0.10x run multiplier.",
            Rarity = ChaosRarity.Uncommon, RunMultBonus = 0.10,
            Apply = s => s.DefuseInvulnMs = 900,
            Flavor = "one clean snap buys a heartbeat of grace."
        });
        Add(new ChaosBoon
        {
            Id = "golden_touch", Name = "Golden Touch",
            Desc = "+0.15x run multiplier, immediately.",
            Rarity = ChaosRarity.Uncommon, RunMultBonus = 0.15,
            Apply = _ => { /* RunMultBonus is the effect */ },
            Flavor = "nothing down here is actually free."
        });
        Add(new ChaosBoon
        {
            Id = "extra_shield", Name = "Left Brain",
            Desc = "+2 resistance, right now.",
            Rarity = ChaosRarity.Common,
            Apply = s => s.Shields += 2,
            Flavor = "the part still arguing. give it something to hold."
        });
        Add(new ChaosBoon
        {
            Id = "gold_digger", Name = "Gold Digger",
            Desc = "golden bubbles burst into 3 falling droplets worth 3-7 gold each. catch them before they slip off screen.",
            Rarity = ChaosRarity.Uncommon, Unique = true,
            Apply = s => s.GoldDiggerEnabled = true,
            Flavor = "she loves you for your wallet. you knew."
        });
        Add(new ChaosBoon
        {
            Id = "welcome_shower", Name = "Welcome Shower",
            Desc = "every loop opens with 6 treats raining from the top of the screen.",
            Rarity = ChaosRarity.Common, Unique = true,
            Apply = s => s.WelcomeShowerEnabled = true,
            Flavor = "a warm welcome, every time you go back under."
        });
        Add(new ChaosBoon
        {
            Id = "heavy_drop", Name = "Heavy Drop",
            Desc = "every 10th bubble is a giant: x1.55 size, drifts at half speed, lives 9s, pays x3.",
            Rarity = ChaosRarity.Common, Unique = true,
            Apply = s => s.HeavyDropEvery = 10,
            Flavor = "some things are worth waiting under."
        });
        Add(new ChaosBoon
        {
            Id = "gg_rabbits", Name = "GG make more GG",
            Desc = "15% of popped treats burst into 3 wild rabbits that mow down everything in their path.",
            Rarity = ChaosRarity.Rare, Unique = true,
            Apply = s => s.GgRabbitChance = 0.15,
            Flavor = "good girls multiply."
        });
        Add(new ChaosBoon
        {
            Id = "size_queen", Name = "Size Queen",
            Desc = "every snap sends out an expanding ring, 430px wide, that pops every treat it touches.",
            Rarity = ChaosRarity.Uncommon, Unique = true,
            Apply = s => s.RippleEnabled = true,
            Flavor = "bigger is a love language."
        });
        Add(new ChaosBoon
        {
            Id = "aftermath", Name = "Aftermath",
            Desc = "snap a live bubble in its final 1.5s and the spot crackles for 2s: a 170px zone where anything drifting through pops itself.",
            Rarity = ChaosRarity.Uncommon, Unique = true,
            Apply = s => s.AftermathEnabled = true,
            Flavor = "you can still feel it after. that's the point."
        });
        Add(new ChaosBoon
        {
            Id = "focus_here", Name = "Focus here...",
            Desc = "the pendulum swings once per loop. pops during its 2.5s slow swing pay x3.",
            Rarity = ChaosRarity.Uncommon, Unique = true,
            RequiresAny = new[] { "pendulum_swing" },
            Apply = s => s.PendulumPayMult = 3.0,
            Flavor = "watch the watch. everything else can wait."
        });
        Add(new ChaosBoon
        {
            Id = "overload", Name = "Overload",
            Desc = "the e-stim runs double charges per press: 6/8/10 charged pops by toy level.",
            Rarity = ChaosRarity.Rare, Unique = true,
            RequiresAny = new[] { "e_stim" },
            Apply = s => s.EStimChargeMult = 2,
            Flavor = "more than the dial was built for."
        });
        Add(new ChaosBoon
        {
            Id = "afterglow", Name = "Afterglow",
            Desc = "when the buzz ends it lingers 2.5s more, still popping whatever you hover.",
            Rarity = ChaosRarity.Rare, Unique = true,
            RequiresAny = new[] { "vibe_popping" },
            Apply = s => s.AfterglowSec = 2.5,
            Flavor = "it never really stops. you just stop noticing."
        });
        Add(new ChaosBoon
        {
            Id = "casting_couch", Name = "Casting Couch",
            Desc = "the logo splits on its first two bounces: one becomes two, then four.",
            Rarity = ChaosRarity.Rare, Unique = true,
            RequiresAny = new[] { "porn_dvd" },
            Apply = s => s.DvdSplitBounces = 2,
            Flavor = "everyone starts somewhere."
        });
        Add(new ChaosBoon
        {
            Id = "tail_plug", Name = "Tail-Plug",
            Desc = "every rabbit drags a sparkling trail for 2s that pops anything within 46px of it.",
            Rarity = ChaosRarity.Rare, Unique = true,
            RequiresAny = new[] { "rabbit_caller", "the_pull", "the_spanker" },
            Apply = s => s.RabbitTrailSec = 2.0,
            Flavor = "you'll know exactly where they've been."
        });
        Add(new ChaosBoon
        {
            Id = "unleashed", Name = "Unleashed",
            Desc = "each time the collar saves your streak, a golden shockwave snaps every live bubble on screen for full pay.",
            Rarity = ChaosRarity.Rare, Unique = true,
            RequiresAny = new[] { "collar" },
            Apply = s => s.UnleashedEnabled = true,
            Flavor = "held tight, then let go all at once."
        });
        Add(new ChaosBoon
        {
            Id = "electrified_rabbits", Name = "Electrified Rabbits",
            Desc = "every bubble a spanked rabbit mows down discharges, arcing lightning into up to 3 bubbles within 620px.",
            Rarity = ChaosRarity.Rare, Unique = true,
            RequiresAll = new[] { "the_spanker", "e_stim" },
            Apply = s => s.ElectrifiedRabbits = true,
            Flavor = "you wired the paddle. of course you did."
        });
        Add(new ChaosBoon
        {
            Id = "body_buzz", Name = "Body Buzz",
            Desc = "1 treat pop in 8 fires a 440px shockwave, arcing lightning into up to 8 bubbles caught inside it.",
            Rarity = ChaosRarity.Rare, Unique = true,
            RequiresAll = new[] { "chain_reaction", "e_stim" },
            Apply = s => s.EStimShockwaveChance = 0.125,
            Flavor = "it hums under your skin between pops."
        });

        // ---- sins ----
        Add(new ChaosBoon
        {
            Id = "hair_trigger", Name = "Hair Trigger",
            Desc = "every trance burns 25% faster. in exchange, +0.40x run multiplier.",
            Rarity = ChaosRarity.Rare, IsCurse = true, Unique = true, RunMultBonus = 0.40,
            Apply = s => s.FuseTimeMult *= 0.75,
            Flavor = "everything goes off early. including you."
        });
        Add(new ChaosBoon
        {
            Id = "playing_fire", Name = "Playing with fire",
            Desc = "trigger effects last 50% longer. in exchange, snapping a live bubble in its final second tips 5-9 gold, plus +0.15x run multiplier.",
            Rarity = ChaosRarity.Rare, IsCurse = true, Unique = true, RunMultBonus = 0.15,
            Apply = s => { s.DetonationDurationMult = 1.5; s.LastSecondGoldEnabled = true; },
            ApplyShielded = s => s.LastSecondGoldEnabled = true,
            Flavor = "warm hands were always the price."
        });
        Add(new ChaosBoon
        {
            Id = "bright_colors", Name = "Look at the bright colors...",
            Desc = "5% of spawns are prism bubbles wearing another bubble's look. popping one pays x10 and fires the copied effect at full strength.",
            Rarity = ChaosRarity.Rare, IsCurse = true, Unique = true,
            Apply = s => s.PrismChance = 0.05,
            ApplyShielded = s => { s.PrismChance = 0.05; s.PrismTreatOnly = true; },
            Flavor = "so pretty you forget to ask what's inside."
        });
        Add(new ChaosBoon
        {
            Id = "cam_girl", Name = "Cam Girl",
            Desc = "bubbles flee your cursor, stronger than any pull. in exchange, 25% of pops tip 2-4 gold, plus +0.40x run multiplier.",
            Rarity = ChaosRarity.Rare, IsCurse = true, Unique = true, RunMultBonus = 0.40,
            Apply = s => { s.CamGirlFlee = 1.6; s.CamGirlTipChance = 0.25; },
            ApplyShielded = s => s.CamGirlTipChance = 0.25,
            Flavor = "look, don't touch. tips appreciated."
        });
        Add(new ChaosBoon
        {
            Id = "the_urge", Name = "The urge",
            Desc = "the rest of the descent pays x3 on everything. your toys are off-limits.",
            Rarity = ChaosRarity.Rare, IsCurse = true, Unique = true,
            Apply = s => { s.UrgeMult = 3.0; s.ActivesDisabled = true; },
            ApplyShielded = s => s.UrgeMult = 2.0,
            Flavor = "bare hands. that's the deal."
        });
        Add(new ChaosBoon
        {
            Id = "double_or_nothing", Name = "Relapse",
            Desc = "60% chance the descent runs one loop longer than promised, and that loop pays double gold and double drops.",
            Rarity = ChaosRarity.Rare, IsCurse = true, Unique = true,
            Apply = s => s.RelapseLoopArmed = Random.Shared.NextDouble() < 0.6,
            ApplyShielded = s => s.RelapseLoopArmed = true,
            Flavor = "one more. it's always just one more."
        });
        // WPF parity (ChaosModels.cs:313-392): the draft pool is EXACTLY these 23 mantras/sins.
        // The seven non-WPF "common-effect" boons (focus_crystal, combo_rush, cold_snap,
        // starter_purse, unlock_spiral, puritan_oath, forbidden_tome) were removed 2026 to match.
    }

    private static void SeedBubbleVariants()
    {
        void Add(ChaosBubbleVariants.Variant v)
        {
            if (ChaosBubbleVariants.All.Exists(x => x.Id == v.Id)) return;
            ChaosBubbleVariants.All.Add(v);
        }

        Add(new ChaosBubbleVariants.Variant { Id = "flash", Name = "Flash", Tint = Color.FromRgb(0xFF, 0xD7, 0x00) });
        Add(new ChaosBubbleVariants.Variant { Id = "subliminal", Name = "Subliminal", Tint = Color.FromRgb(0x9C, 0x5C, 0xFF) });
        Add(new ChaosBubbleVariants.Variant { Id = "pink", Name = "Pink Filter", Tint = Color.FromRgb(0xFF, 0x4D, 0xC4), IsLive = true });
        Add(new ChaosBubbleVariants.Variant { Id = "spiral", Name = "Spiral", Tint = Color.FromRgb(0x7A, 0xE0, 0xFF), IsLive = true });
        Add(new ChaosBubbleVariants.Variant { Id = "braindrain", Name = "BrainDrain", Tint = Color.FromRgb(0xFF, 0x69, 0xB4), IsLive = true });
        Add(new ChaosBubbleVariants.Variant { Id = "bambifreeze", Name = "Bambi Freeze", Tint = Color.FromRgb(0xAA, 0xE8, 0xFF) });
        Add(new ChaosBubbleVariants.Variant { Id = "video", Name = "Video", Tint = Color.FromRgb(0xFF, 0x8A, 0x14) });
        Add(new ChaosBubbleVariants.Variant { Id = "htlink", Name = "Gif Rain", Tint = Color.FromRgb(0xFF, 0xA0, 0x70) });

        ChaosBubbleVariants.Presets.Add(new BubblePreset { Name = "Balanced", VariantIds = new() { "flash", "pink", "spiral", "bambifreeze" } });
        ChaosBubbleVariants.Presets.Add(new BubblePreset { Name = "Tease", VariantIds = new() { "subliminal", "braindrain", "pink", "video" } });
        ChaosBubbleVariants.Presets.Add(new BubblePreset { Name = "Flash-only", VariantIds = new() { "flash", "htlink" } });
    }
}
