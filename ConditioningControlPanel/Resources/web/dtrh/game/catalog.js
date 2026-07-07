/* ============================================================================
 * catalog.js - the Warren's data spine, ported verbatim from the C# catalogues:
 * ChaosUpgrades (habits), ChaosLifetimeBoons (toys/accessories/charms),
 * ChaosHubWindow.Bench (her gold shop), ChaosRanks, ChaosLessons,
 * ChaosFirstTimes, the diary verb sheet + codex, and the how-to cards.
 *
 * DISPLAY + GATING DATA ONLY. No Apply lambdas live here - the loadout's
 * numeric effects stay pre-applied C#-side (ChaosMeta.ApplyLifetimeBoons)
 * and arrive in runConfig.loadout, so boon math can never drift (M4 design).
 *
 * metaView(meta) wraps a chaos_meta snapshot in the same query surface the
 * WPF hub uses (BoonLevel / IsBoonActive / SlotsFor / IsLessonBlocked / ...).
 * ==========================================================================*/

// ============================ ranks (ChaosRanks.cs) ============================

export const RANKS = {
  thresholds: [0, 3, 10, 25, 50, 100],
  names: ['Curious', 'Tempted', 'Slipping', 'Entranced', 'Devoted', 'Claimed'],
  lower: ['curious', 'tempted', 'slipping', 'entranced', 'devoted', 'claimed'],
  forRuns(runs) {
    for (let i = this.thresholds.length - 1; i >= 0; i--) {
      if (runs >= this.thresholds[i]) return i;
    }
    return 0;
  },
  name(i) { return this.names[Math.max(0, Math.min(5, i | 0))]; },
  lockedTip: 'she’ll sell this to someone deeper.',
  capstoneLockedTip: 'the last stitch is hers to give. she gives it to the devoted.',
  specifics(needIdx, runs) {
    const need = this.thresholds[Math.max(0, Math.min(5, needIdx | 0))];
    return `unlocks at ${this.name(needIdx)}: ${need} descents finished. you’ve finished ${runs}.`;
  },
  line(i) {
    return [
      '',
      'tempted. three times down. you can stop calling it curiosity.',
      'slipping. the climb out takes longer every time. you noticed. you came anyway.',
      'entranced. you don’t fall anymore. you arrive.',
      'devoted. the dollhouse keeps a room warm for you now. it always knew it would.',
      'claimed. it stopped counting your visits a long time ago. so did you.',
    ][Math.max(0, Math.min(5, i | 0))];
  },
};

export const GLYPHS = { drops: '✦', gold: '🪙', xp: '🕰' };

// rank indices, named (mirror of the ChaosRank enum)
export const RANK = { Curious: 0, Tempted: 1, Slipping: 2, Entranced: 3, Devoted: 4, Claimed: 5 };

// ============================ habits (ChaosUpgrades.cs) ============================

export const UPGRADES = [
  { id: 'slow_fuses', branch: 'Control', name: 'Slower Trance', glyph: '⏳', cost: 120,
    desc: 'live bubbles hold their trance 15% longer before they trigger.',
    flavor: 'a little more time to change your mind. you won’t.' },
  { id: 'silk_touch', branch: 'Control', name: 'Silk Touch', glyph: '🪶', cost: 180,
    desc: 'bubble hitboxes grow 25%, and a near-miss on a live one still counts as a touch.',
    flavor: 'silk doesn’t try. it just lands.' },
  { id: 'popup_notification', branch: 'Control', name: 'Pop-up Notification', glyph: '💖', cost: 160,
    desc: 'once per loop, 60% of the time, a heart drifts down mid-loop. catch it for +1 resistance and +10 focus. missing it costs nothing.',
    flavor: 'you opted in. you always opt in.' },
  { id: 'pendulum_swing', branch: 'Control', name: 'Pendulum', glyph: '🕰', cost: 220,
    desc: 'once per loop, at a random beat, the pendulum swings: 2.5 seconds of slow motion. the "Focus here..." mantra turns that swing into a x3 pay window.',
    flavor: 'tick. tock. you looked.' },
  { id: 'draft4', branch: 'Depth', name: '4-Mantra Draft', glyph: '🃏', cost: 200,
    desc: 'mantra drafts offer four choices instead of three.',
    flavor: 'more ways to say yes.' },
  { id: 'extreme_tier', branch: 'Depth', name: 'Inescapable Tier', glyph: '🌀', cost: 350,
    desc: 'opens the inescapable difficulty in the descent setup.',
    flavor: 'the last door was never locked.' },
];

export const upgradeById = (id) => UPGRADES.find((u) => u.id === id) || null;

export const BRANCH_LABEL = { Control: 'restraint', Greed: 'craving', Depth: 'depth' };
export const BRANCH_COLOR = { Control: '73,182,232', Greed: '232,180,67', Depth: '139,92,246' };

// ============================ lifetime boons (ChaosLifetimeBoons.cs) ============================
// value(v) renders the level value the way the WPF ValueLabel format string did.

const pct = (v, d = 1) => (v * 100).toFixed(d) + '%';

export const LIFETIME_BOONS = [
  // ---- Skills (active-use toys) ----
  { id: 'vibe_popping', cat: 'skill', rankFloor: RANK.Slipping, name: 'VibePopping', glyph: '🔸',
    desc: 'press for a 3/4/5/5s buzz by level. while it buzzes, hold left or right mouse and sweep: everything you brush over pops instantly, and live ones snap clean for full pay. 20s cooldown.',
    flavor: 'you don’t have to aim. just let the hand wander.',
    unlockCost: 400, upgradeCosts: [500, 800, 1200], levelValues: [3, 4, 5, 5],
    value: (v) => `${v.toFixed(0)}s buzz`,
    capstone: 'no need to hold. while it buzzes, hovering alone pops.',
    activeUse: true, cooldownSec: 20 },
  { id: 'freeze_trigger', cat: 'skill', rankFloor: RANK.Slipping, name: 'Freeze Trigger', glyph: '❄',
    desc: 'press to freeze the whole field for 3.5s, exactly like a caught freeze bubble. 1/2/3/3 uses per descent, and holds channeled while frozen spend no focus.',
    flavor: 'stillness on demand. she lends it, never gives it.',
    unlockCost: 500, upgradeCosts: [800, 1300, 1800], levelValues: [1, 2, 3, 3],
    value: (v) => `${v.toFixed(0)} uses`,
    capstone: 'each freeze also snaps every live bubble on screen.',
    activeUse: true, cooldownSec: 0 },
  { id: 'porn_dvd', cat: 'skill', rankFloor: RANK.Entranced, name: 'Porn DVD', glyph: '📀',
    desc: 'press play: a logo bounces across the screen for 10/15/20/20s by level, popping every treat it touches and snapping every live one. bigger and faster at higher levels. 60s cooldown.',
    flavor: 'it always finds the corner eventually. so will you.',
    unlockCost: 600, upgradeCosts: [900, 1400, 2000], levelValues: [10, 15, 20, 20],
    value: (v) => `${v.toFixed(0)}s playback`,
    capstone: 'two screens.',
    activeUse: true, cooldownSec: 60 },
  { id: 'snap_field', cat: 'skill', rankFloor: RANK.Entranced, name: 'Snap Field', glyph: '✋',
    desc: 'the panic button. every live bubble on screen snaps at once, each paying in full. cooldown 60/45/30s by level.',
    flavor: 'one clean breath and the whole room lets go.',
    unlockCost: 600, upgradeCosts: [800, 1200], levelValues: [60, 45, 30],
    value: (v) => `${v.toFixed(0)}s cooldown`,
    capstone: 'the snap clears EVERYTHING — every bubble on screen goes.',
    activeUse: true, cooldownSec: 60 },
  { id: 'rabbit_caller', cat: 'skill', rankFloor: RANK.Tempted, name: 'Rabbit Caller', glyph: '🐇',
    desc: 'press to arm the whistle, then click anywhere: 1/2/3 white rabbits by level arrive right where you pointed. 45s cooldown.',
    flavor: 'they were always waiting to be called.',
    unlockCost: 500, upgradeCosts: [700, 1100], levelValues: [1, 2, 3],
    value: (v) => `${v.toFixed(0)} rabbits`,
    capstone: 'each whistle also calls a storm — eight more rabbits over the next ten seconds.',
    activeUse: true, cooldownSec: 45 },
  { id: 'e_stim', cat: 'skill', rankFloor: RANK.Curious, name: 'E-Stim', glyph: '⚡',
    desc: 'press to charge your next 3/4/5 clicks by level. a charged pop arcs lightning into up to 3 bubbles within 600px, snapping any live ones. nothing in reach? the charge keeps. 30s cooldown.',
    flavor: 'the current knows exactly where you’re tender.',
    unlockCost: 600, upgradeCosts: [900, 1300], levelValues: [3, 4, 5],
    value: (v) => `${v.toFixed(0)} charged pops`,
    capstone: 'charged pops chain-react — the current leaps on through every bubble close enough, and onward.',
    activeUse: true, cooldownSec: 30 },

  // ---- Accessories (passives that shape the run) ----
  { id: 'surrender', cat: 'accessory', rankFloor: RANK.Slipping, name: 'Surrender', glyph: '🕯',
    desc: 'every sin you accept adds +0.05/+0.10/+0.15x run multiplier by level, on top of whatever the sin pays.',
    flavor: 'you stopped pretending you’d say no.',
    unlockCost: 150, upgradeCosts: [250, 450], levelValues: [0.05, 0.10, 0.15],
    value: (v) => `+${v.toFixed(2)}x per sin`,
    capstone: 'every draft offers a sin, saying yes restores +1 resistance, and the first sin you embrace loses its sting entirely.' },
  { id: 'chain_reaction', cat: 'accessory', rankFloor: RANK.Tempted, name: 'Poppers', glyph: '💨',
    desc: 'a popped bubble bursts outward and pops whatever it overlaps, rippling on through the cluster. burst reach x1.20/1.35/1.60/1.80/2.00 by level.',
    flavor: 'they dilate. everything opens a little wider.',
    unlockCost: 150, upgradeCosts: [120, 160, 220, 300], levelValues: [1.2, 1.35, 1.6, 1.8, 2.0],
    value: (v) => `${v.toFixed(2)}x reach` },
  { id: 'blindfold', cat: 'accessory', rankFloor: RANK.Entranced, name: 'Blindfold', glyph: '🙈',
    desc: 'bubbles dim to 40/32/25% visibility by level. in exchange every pop and snap pays x1.50/x1.75/x2.00. golden bubbles, hearts, rabbits and glass stay bright.',
    flavor: 'you don’t need to see them. you feel where they are.',
    unlockCost: 300, upgradeCosts: [450, 700], levelValues: [1.5, 1.75, 2.0],
    value: (v) => `x${v.toFixed(2)} payout`,
    capstone: 'a heartbeat tells you when one is about to go. listen.' },
  { id: 'last_breath', cat: 'accessory', rankFloor: RANK.Entranced, name: 'Last Breath', glyph: '⏱',
    desc: 'snap a live bubble with 0.4/0.6/0.8s of trance left by level and it pays x5/x10/x20. the fuse ring burns solid red through the final 0.8s, so watch for it.',
    flavor: 'the closer the edge, the sweeter she sings.',
    unlockCost: 250, upgradeCosts: [350, 550], levelValues: [5, 10, 20],
    value: (v) => `x${v.toFixed(0)} at the brink` },
  { id: 'taking_chances', cat: 'accessory', rankFloor: RANK.Entranced, name: 'Taking Chances', glyph: '🎲',
    desc: 'every pop flips a coin: x2 or x0.5 pay, with 50/55/60% odds on the double by level. also grants 1/2/3 mantra draft rerolls per descent.',
    flavor: 'heads she wins, tails you do. you keep forgetting which is which.',
    unlockCost: 250, upgradeCosts: [300, 500], levelValues: [1, 2, 3],
    value: (v) => `${v.toFixed(0)} rerolls` },
  { id: 'the_pull', cat: 'accessory', rankFloor: RANK.Slipping, name: 'The Pull', glyph: '🧭',
    desc: 'bubbles drift toward your cursor, pull strength 0.12/0.22/0.32/0.44/0.58 by level, and white rabbits fly straight at you instead of past you.',
    flavor: 'you’re not chasing them. be honest.',
    unlockCost: 200, upgradeCosts: [200, 300, 450, 650], levelValues: [0.12, 0.22, 0.32, 0.44, 0.58],
    value: (v) => `${v.toFixed(2)} pull` },
  { id: 'the_spanker', cat: 'accessory', rankFloor: RANK.Tempted, name: 'The Spanker', glyph: '🏓',
    desc: 'rabbits can’t be caught anymore. smack one and it turns, swells x1.20/1.45/1.70 by level, gains 18% speed per smack, and pops everything in its path. you give up the free slow-mo.',
    flavor: 'good rabbits get a pat. yours get the paddle.',
    unlockCost: 300, upgradeCosts: [450, 700], levelValues: [1.20, 1.45, 1.70],
    value: (v) => `x${v.toFixed(2)} swell`,
    capstone: 'the bouncing texts answer to you too — smack them to turn them.' },
  { id: 'intrusive_thoughts', cat: 'accessory', rankFloor: RANK.Slipping, name: 'Intrusive Thoughts', glyph: '💭',
    desc: 'every 5 seconds a stray thought races across the screen for 3/4/5s by level, popping whatever it touches.',
    flavor: 'they aren’t yours. they pop things anyway.',
    unlockCost: 250, upgradeCosts: [350, 550], levelValues: [3, 4, 5],
    value: (v) => `${v.toFixed(0)}s thoughts`,
    capstone: 'a thought that brushes a rabbit splits in two. and those split too. (max 8, +2s)' },

  // ---- Utility (charms; pockets are uncapped) ----
  { id: 'rabbits_foot', cat: 'utility', rankFloor: RANK.Slipping, name: 'Rabbit’s Foot', glyph: '🍀',
    desc: 'lucky golden bubbles surface on 1.0/1.5/2.0/2.0% of spawns by level (0.5% without) and pay 12-24/14-28/16-32/20-40 gold on the spot.',
    flavor: 'it wasn’t lucky for the rabbit.',
    unlockCost: 200, upgradeCosts: [350, 600, 900], levelValues: [0.010, 0.015, 0.020, 0.020],
    value: (v) => `${pct(v)} lucky`,
    capstone: 'the gold doubles — twenty to forty a bubble.' },
  { id: 'drip_feed', cat: 'utility', rankFloor: RANK.Entranced, name: 'Drip Feed', glyph: '💧',
    desc: 'every treat popped and every trance snapped banks +1/+2/+3/+4 drops by level — up to 60/90/120/150 a descent — collected when you surface.',
    flavor: 'drop by drop. that’s how anything fills.',
    unlockCost: 250, upgradeCosts: [400, 650, 1000], levelValues: [1, 2, 3, 4],
    value: (v) => `+${v.toFixed(0)} a pop`,
    capstone: 'the hole tips you 10% extra on everything gathered when you surface.' },
  { id: 'blank_eyes', cat: 'utility', rankFloor: RANK.Curious, name: 'Blank Eyes', glyph: '👁',
    desc: 'every pop floats its true payout on screen, multipliers and coin flips included.',
    flavor: 'glaze over. let the numbers do the looking.',
    unlockCost: 120, upgradeCosts: [], levelValues: [1],
    value: () => 'on' },
  { id: 'breast_enlargement', cat: 'utility', rankFloor: RANK.Curious, name: 'Breast Enlargement', glyph: '🎈',
    desc: 'every effect bubble runs +5/+10/+15/+25% bigger by level. pay is unchanged, they’re simply easier to touch.',
    flavor: 'fuller. rounder. harder to ignore.',
    unlockCost: 120, upgradeCosts: [180, 260, 380], levelValues: [5, 10, 15, 25],
    value: (v) => `+${v.toFixed(0)}% size` },
  { id: 'slow_recovery', cat: 'utility', rankFloor: RANK.Slipping, name: 'Slow Recovery', glyph: '♻',
    desc: 'every 60/50/40/30 pops by level knits back one point of resistance, up to where you started.',
    flavor: 'it grows back slow. everything down here does.',
    unlockCost: 200, upgradeCosts: [300, 450, 650], levelValues: [60, 50, 40, 30],
    value: (v) => `${v.toFixed(0)} pops a point` },
  { id: 'start_resistance', cat: 'utility', rankFloor: RANK.Curious, name: 'It would never work on me...', glyph: '♥',
    desc: 'you descend wearing +1/+2/+3 resistance by level. without it you start bare, at zero.',
    flavor: 'famous last words.',
    unlockCost: 100, upgradeCosts: [200, 350], levelValues: [1, 2, 3],
    value: (v) => `+${v.toFixed(0)} resistance` },
  { id: 'collar', cat: 'utility', rankFloor: RANK.Slipping, name: 'Collar', glyph: '📿',
    desc: 'when a trigger slips past your resistance, the collar holds your streak: 1/2/3 saves per descent. the effect itself still plays.',
    flavor: 'the streak was never yours to drop.',
    unlockCost: 200, upgradeCosts: [300, 450], levelValues: [1, 2, 3],
    value: (v) => `${v.toFixed(0)} saves` },
  { id: 'golden_touch', cat: 'utility', rankFloor: RANK.Tempted, name: 'Golden Touch', glyph: '✨',
    desc: 'your run multiplier starts at x1.10/x1.20/x1.30/x1.45 by level, and calm pops pay from a 45/50/55/60% baseline instead of 40%.',
    flavor: 'everything you touch comes back heavier.',
    unlockCost: 150, upgradeCosts: [250, 400, 600], levelValues: [1.1, 1.2, 1.3, 1.45],
    value: (v) => `x${v.toFixed(2)} baseline` },
  { id: 'slowburner', cat: 'utility', rankFloor: RANK.Tempted, name: 'Slowburner', glyph: '🐌',
    desc: 'live bubbles hold their trance 10/20/30/40% longer by level before they trigger.',
    flavor: 'no rush. she likes you slow.',
    unlockCost: 150, upgradeCosts: [250, 400, 600], levelValues: [10, 20, 30, 40],
    value: (v) => `${v.toFixed(0)}% slower`,
    capstone: 'snapping one in its final 1.5 seconds pays triple.' },
  { id: 'pocket_watch', cat: 'utility', rankFloor: RANK.Tempted, name: 'Pocket Watch', glyph: '🕰',
    desc: 'the loop countdown hangs top right and the sidebar shows the descent clock. without it, time down here stays a mystery.',
    flavor: 'borrowed from the white rabbit. he knows where you live.',
    unlockCost: 150, upgradeCosts: [], levelValues: [1],
    value: () => 'on' },
  { id: 'mood_ring', cat: 'utility', rankFloor: RANK.Tempted, name: 'Mood Ring', glyph: '💍', webOnly: true,
    desc: 'the sky down there changes every loop. level 1 forecasts the next weather on the HUD; level 2 makes every weather effect hit x1.5; level 3 lets you click the sky to reroll it, once per descent.',
    flavor: 'she always knows what you\'re in the mood for.',
    unlockCost: 200, upgradeCosts: [300, 450], levelValues: [1, 2, 3],
    value: (v) => v >= 3 ? 'forecast + x1.5 + reroll' : v >= 2 ? 'forecast + x1.5' : 'forecast',
    capstone: 'click the sky — she changes her mind, once per descent.' },
  { id: 'skipping_stone', cat: 'utility', rankFloor: RANK.Entranced, name: 'Skipping Stone', glyph: '🪨',
    desc: 'your ripple gathers in 13/11/9/8 seconds by level (15 bare-handed), and each level sends a wider, slower wave.',
    flavor: 'flat stone, still water. she taught you the wrist for it.',
    unlockCost: 220, upgradeCosts: [380, 650, 950], levelValues: [13, 11, 9, 8],
    value: (v) => `${v.toFixed(0)}s gather`,
    capstone: 'the stone skips — every cast sends three waves, a second apart.' },
];

export const boonDefById = (id) => LIFETIME_BOONS.find((b) => b.id === id) || null;
export const boonsInCat = (cat) => LIFETIME_BOONS.filter((b) => b.cat === cat);

// ============================ her bench (ChaosHubWindow.Bench.cs) ============================

export const BENCH_ITEMS = [
  { id: 'toy_pocket_1', glyph: '👝', label: 'first toy pocket',
    line: 'she sews you a pocket.', cost: 50, pocket: 'toy' },
  { id: 'accessory_pocket_1', glyph: '👝', label: 'first accessory pocket',
    line: 'she only has two hands. she found a third.', cost: 150, pocket: 'accessory' },
  { id: 'start_mantra', glyph: '◈', label: 'the starting mantra',
    line: 'fall in holding something.', cost: 200 },
  { id: 'diary', glyph: '📓', label: 'the diary',
    line: 'she keeps notes on what you meet down there.', cost: 150 },
  { id: 'stats_panel', glyph: '🕰', label: 'the stats panel',
    line: 'the numbers, if you want them.', cost: 100 },
  { id: 'toy_pocket_2', glyph: '👝', label: 'second toy pocket',
    line: 'she found room for one more.', cost: 2000,
    rankNeed: RANK.Devoted, revealGate: 'bench_toy_pocket_2', pocket: 'toy' },
  { id: 'accessory_pocket_2', glyph: '👝', label: 'second accessory pocket',
    line: 'a fourth hand. don’t ask.', cost: 2500,
    rankNeed: RANK.Devoted, revealGate: 'bench_acc_pocket_2', pocket: 'accessory' },
];

export const BENCH_RESERVED = [
  'the clocks', 'descent ledger', 'payout eyes', 'the fine print',
  'fall right in', 'held breath', 'soft landing', 'no countdown',
  'dollhouse wallpapers', 'recap frames', 'a chattier companion', 'the pact',
];
export const BENCH_CLAIMED_RESERVED = ['daily descent', 'leaderboard', 'prestige'];

export const WALL_TIP = 'there’s a wall here that isn’t quite a wall.';
export const DEEPER_TIP = 'she’ll sell this to someone deeper.';
export const BOTTOM_TIP = 'the bottom is not where you think it is.';

// ============================ lessons (ChaosLessons.cs) ============================

export const LESSONS = [
  { id: 'vibe_popping', text: 'pop 10 treats inside 5 seconds', target: 10, highWater: true,
    detail: 'pop 10 treats inside one rolling 5-second window. flash and whisper treats count (specials don’t). the bar keeps your best burst, so it never goes down.' },
  { id: 'freeze_trigger', text: 'catch 15 freeze pickups', target: 15,
    detail: 'catch 15 freeze pickups (the ❄ bubble), lifetime. click it before it drifts off the screen.' },
  { id: 'porn_dvd', text: 'endure 10 videos to the end', target: 10,
    detail: 'sit through 10 video payloads to their natural end (or the 15 second cap). a tape cut short by waking up doesn’t count.' },
  { id: 'snap_field', text: 'defuse 5 threats inside a single loop', target: 5, highWater: true,
    detail: 'snap 5 trances inside one loop. your own holds, toys, chains and zones all count. the bar keeps your best loop.' },
  { id: 'rabbit_caller', text: 'catch 25 rabbits', target: 25,
    detail: 'catch 25 white rabbits, lifetime. with the spanker on, getting your hands on one — the first smack — counts too.' },
  { id: 'chain_reaction', text: 'land 50 interlaced pops', target: 50,
    detail: '50 interlaced pops, lifetime: pop a bubble while another burst from the last 0.4 seconds still glows within reach (about 170px). a tight cluster pays several at once.' },
  { id: 'blindfold', text: 'defuse 10 threats while the screen is busy', target: 10,
    detail: 'snap 10 trances while the screen is busy with an effect: a fresh flash or whisper, an overlay, a video, gif rain. lifetime.' },
  { id: 'last_breath', text: 'start 10 defuses with under a second left', target: 10,
    detail: 'complete 10 holds that STARTED with 0.8 seconds or less left on the trance. starting late is the skill; finishing the hold is the proof.' },
  { id: 'taking_chances', text: 'pop 8 prisms', target: 8,
    detail: 'pop 8 mimic prisms (the swirling ❂), lifetime. yes, the effect sealed inside fires every time.' },
  { id: 'the_pull', text: 'pop 15 bubbles without moving', target: 15,
    detail: 'pop 15 treats with your cursor at rest (under ~3px of drift in the last half second). park the hand. let them come to you.' },
  { id: 'intrusive_thoughts', text: 'pop 50 whispering treats', target: 50,
    detail: 'pop 50 whisper treats (the ♥ subliminal bubble), lifetime.' },
  { id: 'surrender', text: 'accept 5 sins', target: 5,
    detail: 'accept 5 sins at draft tables, lifetime. the first free taste counts too.' },
  { id: 'slow_fuses', text: 'spend a minute holding on', target: 60,
    detail: 'hold defuse channels for 60 seconds in total, lifetime. the clock only runs while your button is down on a live bubble; a full hold is about 1 second.' },
  { id: 'silk_touch', text: 'finish a loop without a single detonation', target: 1,
    detail: 'finish one whole loop with zero detonations. a touched tease dirties it too. the run’s final loop counts when the clock runs out.' },
  { id: 'popup_notification', text: 'end 3 descents with resistance still held', target: 3,
    detail: 'finish 3 descents to the buzzer with at least 1 resistance still held. waking up early doesn’t count.' },
  { id: 'draft4', text: 'take 15 mantras', target: 15,
    detail: 'take 15 cards at draft tables, lifetime. mantras and sins both count; skips don’t.' },
  { id: 'extreme_tier', text: 'finish 10 relentless descents', target: 10,
    detail: 'finish 10 descents on Relentless or harder, full course to the buzzer. (the deeper door also wants the Devoted rank.)' },
];

export const lessonById = (id) => LESSONS.find((l) => l.id === id) || null;
/** Ids that never get a lesson (scripted first purchases). */
export const LESSONLESS = new Set(['e_stim', 'the_spanker']);
/** Lessons waived while the sins toggle is off (unprovable, not failed). */
export const CURSE_BOUND = new Set(['taking_chances', 'surrender']);

// ============================ first-times (ChaosFirstTimes) ============================

export const FIRST_TIMES = {
  first_taste: { amount: 5, label: 'first taste' },
  first_snap: { amount: 10, label: 'first snap' },
  first_whisper: { amount: 10, label: 'first whisper' },
  first_yes: { amount: 15, label: 'first yes' },
  first_play: { amount: 15, label: 'first play' },
};

// ============================ diary (verbs + codex) ============================

export const DIARY_VERBS = [
  { glyph: '✋', name: 'hold to snap', desc: 'press and HOLD a live (ringed) bubble about a second to defuse it — costs 30 focus. a quick click, or letting go early, TRIGGERS it instead.' },
  { glyph: '○', name: 'click the treats', desc: 'a tap pops a treat: its payload plays, the streak climbs, and +10 focus flows back (+15 from heavies and rabbits).' },
  { glyph: '🌊', name: 'right-click · the ripple', desc: 'casts a wave from your cursor (near the bubbles): treats pop fully paid, trances snap clean, rabbits get flung. one charge, gathered back over time — READY on the sidebar means it’s in your hand.' },
  { glyph: '◌', name: 'focus', desc: 'the defuse fuel: max 100, you fall in with 50, no regen on its own. when the bar runs red you can’t afford a hold — farm treats before touching a live one (pressing one anyway triggers it in your grip).' },
  { glyph: '🔥', name: 'lust', desc: 'the orange bar. climbs while you perform and pays up to x2 at full burn; an unblocked trigger cools it to zero.' },
  { glyph: '💨', name: 'never let treats rot', desc: 'a treat that fades unpopped HALVES your streak. chase the rewards too, not just the threats.' },
  { glyph: '🐇', name: 'catch the white rabbit', desc: 'everything slows to a crawl for six seconds. with the Spanker worn, you smack it into the field instead.' },
  { glyph: '❄', name: 'the pickups', desc: 'freeze ❄ holds the whole field 3.5 seconds (still poppable, and snaps cost no focus) · the lucky bubble 🍀 pays gold on the spot.' },
  { glyph: '⏸', name: 'your panic key', desc: 'one press holds the field mid-fall; pressing it again wakes you up to the recap.' },
];

/** Codex rows in WPF diary order: the weighted pool first, then the specials. */
export const DIARY_CODEX = [
  { codex: 'bubble:flash', name: 'Flash', glyph: '●', tint: '255,208,232',
    desc: 'A benign treat. Pop it for a quick flash burst and a little score. Ignore it and it fades in seconds — and your streak halves.' },
  { codex: 'bubble:subliminal', name: 'Subliminal', glyph: '●', tint: '176,128,255',
    desc: 'A benign treat. Pop it to flash a subliminal from the active pool. Ignore it and it fades in seconds — and your streak halves.' },
  { codex: 'bubble:pink', name: 'Pink Filter', glyph: '●', tint: '255,61,165',
    desc: 'Live. Snap it before the trance runs out, or a pink filter settles over the screen.' },
  { codex: 'bubble:spiral', name: 'Spiral', glyph: '●', tint: '64,208,192',
    desc: 'Live. Roams and bounces. Snap it or it drops a spiral overlay.' },
  { codex: 'bubble:braindrain', name: 'BrainDrain', glyph: '●', tint: '64,96,192',
    desc: 'Live and large. Slow but heavy. Triggers into a creeping mind-mist.' },
  { codex: 'bubble:bambifreeze', name: 'Freeze', glyph: '●', tint: '138,230,255',
    desc: 'A good pickup. Catch it to freeze the whole field. Bubbles hold in place and trances pause for a few seconds.' },
  { codex: 'bubble:video', name: 'Video', glyph: '●', tint: '224,64,77',
    desc: 'Live and rare. A long trance, but it opens a mandatory video if it goes off.' },
  { codex: 'bubble:htlink', name: 'Gif Rain', glyph: '●', tint: '255,200,61',
    desc: 'Live and rare. Snap it or it triggers a rain of gifs sliding down the screen.' },
  { codex: 'bubble:darter', name: 'White Rabbit', glyph: '✧', tint: '255,77,196',
    desc: 'A white rabbit. Fast, bouncing, always late. Catch it for points and a micro flash. Harmless if it gets away.' },
  { codex: 'bubble:golden', name: 'Lucky Bubble', glyph: '🍀', tint: '255,215,0',
    desc: 'A lucky bubble. Rare, quick, gone before you know it. Pop it for real gold, banked on the spot. Let it fade and your streak halves.' },
  { codex: 'bubble:echo', name: 'The Echo', glyph: '◌', tint: '201,196,232',
    desc: 'Live, and not quite singular. Trigger it — by timeout, a tap, or letting go — and it splits into two smaller, faster ones. Hold it all the way down and there’s only ever the one.' },
  { codex: 'bubble:chaperone', name: 'The Chaperone', glyph: '💞', tint: '156,232,255',
    desc: 'Live, but spoken for. While its little escort circles, nothing touches it. Pop the escort first — then it’s alone, and yours to hold.' },
  { codex: 'bubble:tease', name: 'The Tease', glyph: '✖', tint: '179,14,46',
    desc: 'It wants your hand. Don’t. Touch it once and it fires — and your streak halves. Let it leave unanswered and it pays you for the restraint. Toys slide right off it.' },
  { codex: 'bubble:bound', name: 'The Bound', glyph: '⛓', tint: '255,105,180',
    desc: 'Two of them, one thread. Each costs half a hold — but the second must come down quick, or the one left waiting turns furious: half the trance, half again the speed.' },
  { codex: 'bubble:brittle', name: 'The Brittle', glyph: '◇', tint: '217,239,255',
    desc: 'Thin glass with something live sealed inside. Your cursor brushing it is enough — it shatters, and whatever it held fires. Toys slide around it; a frozen field is safe to cross. Steer wide.' },
  // ---- the weather (Wave 2): one sky per loop, worn by the tunnel itself ----
  { codex: 'weather:stillness', name: 'Stillness', glyph: '🕯', tint: '255,214,236',
    desc: 'Weather. The deep holds its breath — trances burn 10% slower under it.' },
  { codex: 'weather:perfume', name: 'Her Perfume', glyph: '🌸', tint: '255,143,206',
    desc: 'Weather. The fog turns sweet and pink — lust climbs 25% faster and every pop pays +10%.' },
  { codex: 'weather:static', name: 'Static', glyph: '⚡', tint: '217,160,255',
    desc: 'Weather. Stray current cracks through the tube — every few seconds a bolt pops a treat FOR you. One bolt in ten bites a live fuse down to half instead.' },
  { codex: 'weather:foolsgold', name: 'Fool\'s Gold', glyph: '✨', tint: '255,215,0',
    desc: 'Weather. Everything glitters — lucky bubbles drift in noticeably more often.' },
  { codex: 'weather:overstim', name: 'Overstim', glyph: '💉', tint: '255,47,158',
    desc: 'Weather. Too bright, too fast — the fall runs 15% hotter and bubbles surface 10% faster.' },
];

// ============================ run setup (pool + presets) ============================

export const POOL_VARIANTS = [
  { id: 'flash', name: 'Flash' },
  { id: 'subliminal', name: 'Subliminal' },
  { id: 'pink', name: 'Pink Filter' },
  { id: 'spiral', name: 'Spiral' },
  { id: 'braindrain', name: 'BrainDrain' },
  { id: 'bambifreeze', name: 'Freeze' },
  { id: 'video', name: 'Video', revealGate: 'variant_video' },
  { id: 'htlink', name: 'Gif Rain', revealGate: 'variant_htlink' },
];

export const POOL_PRESETS = [
  { name: 'Balanced', ids: POOL_VARIANTS.map((v) => v.id) },
  { name: 'Tease', ids: ['flash', 'subliminal', 'pink', 'spiral', 'bambifreeze'] },
  { name: 'Flash-only', ids: ['flash', 'subliminal'] },
];

export const DIFF_PILLS = [
  { id: 'Easy', label: 'Gentle', tip: 'x1.0 pay. the calmest fall: baseline spawn pace, the longest trances, and the strange bubbles roll half as often.' },
  { id: 'Medium', label: 'Teasing', revealGate: 'pill_teasing', tip: 'x1.3 on every payout. bubbles surface ~30% faster and the field holds ~14% more of them at once.' },
  { id: 'Hard', label: 'Relentless', revealGate: 'pill_relentless', tip: 'x1.7 on every payout. ~70% faster spawns, ~30% more on screen, shorter trances — and the Bound hunts here on any rank.' },
  { id: 'Extreme', label: 'Inescapable', extremeGate: true, tip: 'x2.2 on every payout. spawns at more than double pace, ~48% more on screen. the deepest the hole goes.' },
];

// ============================ how to play (ChaosHubWindow._howToCards) ============================

export const HOWTO_CARDS = [
  { title: 'What the Rabbit Hole is', image: 'howto_1', lines: [
    { body: 'Bubbles drift up the screen carrying flashes, videos and overlays. Pop the good ones, snap the dangerous ones before they go off, and ride it deeper. One descent is about **five minutes** — survive the waves, take what she offers, climb out a little more hers.' },
  ] },
  { title: 'What you do', image: 'howto_2', lines: [
    { emoji: '🫧', color: '255,159,208', lead: 'Left-click', body: 'pop the treats — the soft pink bubbles. One click builds your streak and refills your focus.' },
    { emoji: '◉', color: '255,210,40', lead: 'Press & hold', body: 'the glowing bubbles are live. Keep pressing until they snap — let one finish and it goes off (a flash or video fires).' },
    { emoji: '🌊', color: '122,224,255', lead: 'Right-click', body: 'the ripple. A wave near the bubbles pops treats, snaps live ones and scatters rabbits. Strong, but slow to gather again.' },
    { emoji: '🐇', color: '255,105,180', lead: 'The rabbits', body: 'chase them for little bonuses. Everything else down there is yours to find out.' },
  ] },
  { title: 'The two bars', image: 'howto_3', lines: [
    { lead: 'FOCUS', body: 'your nerve. Snapping live bubbles spends it; popping treats refills it. Run dry and you can’t snap — so keep feeding.' },
    { lead: 'HEAT', body: 'the burn. It climbs every time something triggers. Let it run high and the descent gets harder to resist.' },
  ] },
  { title: 'A descent', image: 'howto_4', lines: [
    { body: 'Four waves, then it ends. Between waves she offers you a **mantra** — pick one and it bends the rules for that run only. Finish the whole descent for the full reward; slip out early and you forfeit it.' },
  ] },
  { title: 'What you keep', image: 'howto_5', lines: [
    { body: 'Every descent earns **XP** toward your normal level, plus **Sparks** (gold) you carry back out.' },
    { body: 'Spend Sparks in **the dollhouse** — accessories at the table by the door, charms, active toys you trigger mid-descent, and the seamstress’s bench for permanent upgrades.' },
    { body: 'The more descents you finish, the higher your **RANK** — curious, tempted, slipping, entranced, devoted… — and the more of the Rabbit Hole opens up to you.' },
  ] },
];

// ============================ metaView (the ChaosMeta facade over a snapshot) ============================

/**
 * Wrap a chaos_meta snapshot (camelCase, from the bridge) in the WPF hub's
 * query surface. Pure reads - all writes go over meta-commands.
 */
export function metaView(meta) {
  const m = meta || {};
  const asSet = (a) => (a instanceof Set ? a : new Set(a || []));
  const purchased = asSet(m.purchasedUpgrades);
  const disabled = asSet(m.disabledUpgrades);
  const active = asSet(m.activeLifetimeBoons);
  const levels = m.lifetimeBoonLevels || {};
  const bench = asSet(m.benchPurchases);
  const lessonsDone = asSet(m.lessonsComplete);
  const lessonProg = m.lessonProgress || {};
  const discovered = asSet(m.discoveredCodexIds);
  const pending = asSet(m.pendingReveals);
  const seenReveals = asSet(m.seenReveals);
  const firstTimes = asSet(m.firstTimesAwarded);
  const runs = m.runsCompleted | 0;
  const rankIndex = RANKS.forRuns(runs);
  const MAX_POCKETS = 2;

  const v = {
    raw: m,
    sparks: m.sparks | 0,
    gold: m.gold | 0,
    runs,
    rankIndex,
    rankName: RANKS.name(rankIndex),
    extremeUnlocked: !!m.extremeUnlocked,
    giftGiven: !!m.giftGiven,
    equippedStartBoon: m.equippedStartBoon || null,
    toyPockets: Math.min(m.toyPockets | 0, MAX_POCKETS),
    accessoryPockets: Math.min(m.accessoryPockets | 0, MAX_POCKETS),
    bench, discovered, pending, seenReveals, firstTimes, lessonsDone,
    bestScore: m.bestScore || 0,
    bestCombo: m.bestCombo || 0,
    totalDefused: m.totalDefused || 0,
    totalRunSeconds: m.totalRunSeconds || 0,
    totalChannelSeconds: m.totalChannelSeconds || 0,
    seenIntroGuide: !!m.seenIntroGuide,
    seenDollhouse: !!m.seenDollhouse,

    atLeast: (r) => rankIndex >= r,
    isOwned: (id) => purchased.has(id),
    isUpgradeActive: (id) => purchased.has(id) && !disabled.has(id),
    canAffordUpgrade(id) {
      const u = upgradeById(id);
      return !!u && !purchased.has(id) && v.sparks >= u.cost;
    },
    isPurchaseRankLocked: (id) => id === 'extreme_tier' && !purchased.has(id) && rankIndex < RANK.Devoted,

    boonLevel: (id) => levels[id] | 0,
    isBoonUnlocked: (id) => (levels[id] | 0) >= 1,
    isBoonActive: (id) => active.has(id) && (levels[id] | 0) >= 1,
    slotsFor: (cat) => (cat === 'utility' ? Infinity
      : cat === 'skill' ? Math.min(m.toyPockets | 0, MAX_POCKETS)
      : Math.min(m.accessoryPockets | 0, MAX_POCKETS)),
    equippedCountIn: (cat) => boonsInCat(cat).filter((b) => v.isBoonActive(b.id)).length,
    hasFreePocket: (cat) => v.equippedCountIn(cat) < v.slotsFor(cat),
    unlockCostOf(id) {
      const b = boonDefById(id);
      return !b || v.isBoonUnlocked(id) ? null : b.unlockCost;
    },
    nextUpgradeCostOf(id) {
      const b = boonDefById(id);
      if (!b) return null;
      const lvl = v.boonLevel(id);
      if (lvl < 1 || lvl >= b.levelValues.length) return null;
      return b.upgradeCosts[lvl - 1] ?? null;
    },
    canAffordUnlock(id) { const c = v.unlockCostOf(id); return c != null && v.sparks >= c; },
    canAffordDeepen(id) { const c = v.nextUpgradeCostOf(id); return c != null && v.sparks >= c; },
    isBoonRankLocked(id) {
      const b = boonDefById(id);
      return !!b && !v.isBoonUnlocked(id) && rankIndex < b.rankFloor;
    },
    isAccessoryScriptLocked(id) {
      const b = boonDefById(id);
      return !!b && b.cat === 'accessory' && b.id !== 'the_spanker' && !v.isBoonUnlocked('the_spanker');
    },
    isCapstoneRankLocked(id) {
      const b = boonDefById(id);
      if (!b) return false;
      const lvl = v.boonLevel(id);
      return lvl >= 1 && lvl < b.levelValues.length && lvl + 1 >= b.levelValues.length
        && rankIndex < RANK.Devoted;
    },
    isMaxed(id) {
      const b = boonDefById(id);
      return !!b && v.boonLevel(id) >= b.levelValues.length;
    },

    lessonProgress: (id) => lessonProg[id] | 0,
    isLessonComplete: (id) => !lessonById(id) || LESSONLESS.has(id) || lessonsDone.has(id),
    /** allowCurses = the CURRENT sins toggle (WPF reads the live setting). */
    isLessonBlocked: (id, allowCurses = true) =>
      !!lessonById(id) && !LESSONLESS.has(id) && !v.isLessonComplete(id)
      && !(CURSE_BOUND.has(id) && !allowCurses),

    isDiscovered: (codexId) => discovered.has(codexId),
    /** Happy path: run 2+ shows full shelves; before that only the starter trio. */
    onShelfNow: (id) => runs >= 2 || ['start_resistance', 'blank_eyes', 'slow_fuses'].includes(id),
  };
  return v;
}
