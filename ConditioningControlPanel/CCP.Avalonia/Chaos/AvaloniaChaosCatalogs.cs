using System;
using System.Collections.Generic;
using global::Avalonia.Media;
using ConditioningControlPanel.Core.Services.Chaos;

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
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "blank_eyes", Category = ChaosBoonCategory.Utility,
            Name = "Blank Eyes", Glyph = "👁",
            Desc = "every pop floats its true payout on screen, multipliers and coin flips included.",
            Flavor = "glaze over. let the numbers do the looking.",
            UnlockCost = 120, MaxLevel = 1, ValueLabel = "on",
            LevelValues = new[] { 1.0 },
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
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "pocket_watch", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Tempted,
            Name = "Pocket Watch", Glyph = "🕰",
            Desc = "the loop countdown hangs top right and the sidebar shows the descent clock. without it, time down here stays a mystery.",
            Flavor = "borrowed from the white rabbit. he knows where you live.",
            UnlockCost = 150, MaxLevel = 1, ValueLabel = "on",
            LevelValues = new[] { 1.0 },
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
        });
    }

    private static void SeedUpgrades()
    {
        void Add(ChaosUpgrade u)
        {
            if (ChaosUpgrades.All.Exists(x => x.Id == u.Id)) return;
            ChaosUpgrades.All.Add(u);
        }

        // Apply effects verbatim from the WPF catalogue (WPF ChaosUpgrades.cs:49-88);
        // ChaosMeta.ApplyTo walks these for every owned-and-active habit at run start.
        Add(new ChaosUpgrade { Id = "slow_fuses", Branch = ChaosBranch.Control, Name = "Slower Trance", Cost = 120, Glyph = "⏳",
            Desc = "live bubbles hold their trance 15% longer before they trigger.",
            Flavor = "a little more time to change your mind. you won't.",
            Apply = c => c.FuseTimeMult *= 1.15 });   // WPF ChaosUpgrades.cs:53
        Add(new ChaosUpgrade { Id = "silk_touch", Branch = ChaosBranch.Control, Name = "Silk Touch", Cost = 180, Glyph = "🪶",
            Desc = "bubble hitboxes grow 25%, and a near-miss on a live one still counts as a touch.",
            Flavor = "silk doesn't try. it just lands.",
            Apply = c => { c.HitboxScale = 1.25; c.MagnetEnabled = true; } });   // WPF ChaosUpgrades.cs:60
        Add(new ChaosUpgrade { Id = "popup_notification", Branch = ChaosBranch.Control, Name = "Pop-up Notification", Cost = 160, Glyph = "💖",
            Desc = "once per loop, 60% of the time, a heart drifts down mid-loop. catch it for +1 resistance and +10 focus.",
            Flavor = "you opted in. you always opt in.",
            Apply = c => c.PopupHeartEnabled = true });   // WPF ChaosUpgrades.cs:64
        Add(new ChaosUpgrade { Id = "pendulum_swing", Branch = ChaosBranch.Control, Name = "Pendulum", Cost = 220, Glyph = "🕰",
            Desc = "once per loop, at a random beat, the pendulum swings: 2.5 seconds of slow motion.",
            Flavor = "tick. tock. you looked.",
            Apply = c => c.PendulumSwing = true });   // WPF ChaosUpgrades.cs:71
        Add(new ChaosUpgrade { Id = "draft4", Branch = ChaosBranch.Depth, Name = "4-Mantra Draft", Cost = 200, Glyph = "🃏",
            Desc = "mantra drafts offer four choices instead of three.",
            Flavor = "more ways to say yes.",
            Apply = c => c.DraftChoices = 4 });   // WPF ChaosUpgrades.cs:86
        Add(new ChaosUpgrade { Id = "extreme_tier", Branch = ChaosBranch.Depth, Name = "Inescapable Tier", Cost = 350, Glyph = "🌀",
            Desc = "opens the inescapable difficulty in the descent setup.",
            Flavor = "the last door was never locked." });   // no-op Apply: flag stored at purchase time (WPF ChaosUpgrades.cs:88)
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
