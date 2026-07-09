/* ============================================================================
 * boons.js - the in-run mantra/sin pool + the draft dealer, ported verbatim
 * from ChaosBoonPool (ChaosModels.cs). Each card's apply(st) mutates the live
 * run-state knobs in chaosRun.js; applyShielded is the Surrender capstone's
 * sting-free half. Duo/trio cards gate on the equipped loadout (runConfig
 * carries the equipment id list - equipment can't change mid-run).
 * ==========================================================================*/

export const BOONS = [
  { id: 'defuse_chain', name: 'Snap Chain', rarity: 'Uncommon', curse: false, mult: 0.10,
    desc: 'for 0.9s after every snap, a trigger can\'t break your streak, feed your lust, or spend your resistance. +0.10x run multiplier.',
    flavor: 'one clean snap buys a heartbeat of grace.',
    apply: (s) => { s.defuseInvulnMs = 900; } },
  { id: 'golden_touch', name: 'Golden Touch', rarity: 'Uncommon', curse: false, mult: 0.15,
    desc: '+0.15x run multiplier, immediately.',
    flavor: 'nothing down here is actually free.',
    apply: () => {} },
  { id: 'extra_shield', name: 'Left Brain', rarity: 'Common', curse: false, mult: 0.0,
    desc: '+2 resistance, right now.',
    flavor: 'the part still arguing. give it something to hold.',
    apply: (s) => { s.shields += 2; } },

  // ---- the visible pool: entities, field FX, behaviors ----
  { id: 'gold_digger', name: 'Gold Digger', rarity: 'Uncommon', curse: false, mult: 0.0, unique: true,
    desc: 'golden bubbles burst into 3 falling droplets worth 3-7 gold each. catch them before they slip off screen. a missed droplet costs nothing.',
    flavor: 'she loves you for your wallet. you knew.',
    apply: (s) => { s.goldDigger = true; } },
  { id: 'welcome_shower', name: 'Welcome Shower', rarity: 'Common', curse: false, mult: 0.0, unique: true,
    desc: 'every loop opens with 6 treats raining from the top of the screen.',
    flavor: 'a warm welcome, every time you go back under.',
    apply: (s) => { s.welcomeShower = true; } },
  { id: 'heavy_drop', name: 'Heavy Drop', rarity: 'Common', curse: false, mult: 0.0, unique: true,
    desc: 'every 10th bubble is a giant: x1.55 size, drifts at half speed, lives 9s, pays x3.',
    flavor: 'some things are worth waiting under.',
    apply: (s) => { s.heavyDropEvery = 10; } },
  { id: 'gg_rabbits', name: 'GG make more GG', rarity: 'Rare', curse: false, mult: 0.0, unique: true, fxTheme: 'rabbit',
    desc: '15% of popped treats burst into 3 wild rabbits that mow down everything in their path. they can\'t be caught, only smacked along.',
    flavor: 'good girls multiply.',
    apply: (s) => { s.ggRabbitChance = 0.15; } },
  { id: 'size_queen', name: 'Size Queen', rarity: 'Uncommon', curse: false, mult: 0.0, unique: true,
    desc: 'every snap sends out an expanding ring, 430px wide, that pops every treat it touches.',
    flavor: 'bigger is a love language.',
    apply: (s) => { s.snapRing = true; } },
  { id: 'aftermath', name: 'Aftermath', rarity: 'Uncommon', curse: false, mult: 0.0, unique: true,
    desc: 'snap a live bubble in its final 1.5s and the spot crackles for 2s: a 170px zone where anything drifting through pops itself.',
    flavor: 'you can still feel it after. that\'s the point.',
    apply: (s) => { s.aftermath = true; } },
  { id: 'focus_here', name: 'Focus here...', rarity: 'Uncommon', curse: false, mult: 0.0, unique: true, fxTheme: 'rabbit',
    requiresAny: ['pendulum_swing'],
    desc: 'the pendulum swings once per loop. pops during its 2.5s slow swing pay x3.',
    flavor: 'watch the watch. everything else can wait.',
    apply: (s) => { s.pendulumPayMult = 3.0; } },
  { id: 'storm_chaser', name: 'Storm Chaser', rarity: 'Uncommon', curse: false, mult: 0.0, unique: true, fxTheme: 'electric',
    desc: 'the weather locks to STATIC for the rest of the descent, and every detonation arcs lightning into up to 4 bubbles within 440px.',
    flavor: 'you taste the metal before it hits.',
    apply: (s) => { s.stormChaser = true; } },

  // ---- synergy duos: only drafted when the partner is equipped (gold frame) ----
  { id: 'overload', name: 'Overload', rarity: 'Rare', curse: false, mult: 0.0, unique: true, fxTheme: 'electric',
    requiresAny: ['e_stim'],
    desc: 'the e-stim runs double charges per press: 6/8/10 charged pops by toy level.',
    flavor: 'more than the dial was built for.',
    apply: (s) => { s.estimChargeMult = 2; } },
  { id: 'afterglow', name: 'Afterglow', rarity: 'Rare', curse: false, mult: 0.0, unique: true,
    requiresAny: ['vibe_popping'],
    desc: 'when the buzz ends it lingers 2.5s more, still popping whatever you hover.',
    flavor: 'it never really stops. you just stop noticing.',
    apply: (s) => { s.afterglowSec = 2.5; } },
  { id: 'casting_couch', name: 'Casting Couch', rarity: 'Rare', curse: false, mult: 0.0, unique: true,
    requiresAny: ['porn_dvd'],
    desc: 'the logo splits on its first two bounces: one becomes two, then four.',
    flavor: 'everyone starts somewhere.',
    apply: (s) => { s.dvdSplitBounces = 2; } },
  { id: 'tail_plug', name: 'Tail-Plug', rarity: 'Rare', curse: false, mult: 0.0, unique: true, fxTheme: 'rabbit',
    requiresAny: ['rabbit_caller', 'the_pull', 'the_spanker'],
    desc: 'every rabbit drags a sparkling trail for 2s that pops anything within 46px of it.',
    flavor: 'you\'ll know exactly where they\'ve been.',
    apply: (s) => { s.rabbitTrailSec = 2.0; } },
  { id: 'unleashed', name: 'Unleashed', rarity: 'Rare', curse: false, mult: 0.0, unique: true,
    requiresAny: ['collar'],
    desc: 'each time the collar saves your streak, a golden shockwave snaps every live bubble on screen for full pay.',
    flavor: 'held tight, then let go all at once.',
    apply: (s) => { s.unleashed = true; } },
  { id: 'electrified_rabbits', name: 'Electrified Rabbits', rarity: 'Rare', curse: false, mult: 0.0, unique: true, fxTheme: 'electric',
    requiresAll: ['the_spanker', 'e_stim'],
    desc: 'every bubble a spanked rabbit mows down discharges, arcing lightning into up to 3 bubbles within 620px.',
    flavor: 'you wired the paddle. of course you did.',
    apply: (s) => { s.electrifiedRabbits = true; } },
  { id: 'body_buzz', name: 'Body Buzz', rarity: 'Rare', curse: false, mult: 0.0, unique: true, fxTheme: 'electric',
    requiresAll: ['chain_reaction', 'e_stim'],
    desc: '1 treat pop in 8 fires a 440px shockwave, arcing lightning into up to 8 bubbles caught inside it.',
    flavor: 'it hums under your skin between pops.',
    apply: (s) => { s.estimShockwaveChance = 0.125; } },
  { id: 'live_wire', name: 'Live Wire', rarity: 'Rare', curse: false, mult: 0.0, unique: true, fxTheme: 'electric',
    requiresAll: ['e_stim', 'the_wand'],
    desc: 'the wand\'s trail runs current: every bubble it takes discharges, arcing lightning onward into up to 3 bubbles within 400px.',
    flavor: 'you drew it. it\'s live.',
    apply: (s) => { s.liveWire = true; } },
  { id: 'hopscotch', name: 'Hopscotch', rarity: 'Rare', curse: false, mult: 0.0, unique: true, fxTheme: 'rabbit',
    requiresAll: ['the_spanker', 'the_wand'],
    desc: 'rabbits ricochet off the wand\'s trail instead of crossing it - and every bounce counts as a fresh smack: faster, bigger, mowing down everything in their path.',
    flavor: 'chalk lines on the floor. they know this game.',
    apply: (s) => { s.wandBounce = true; } },
  { id: 'superconductor', name: 'Superconductor', rarity: 'Rare', curse: false, mult: 0.0, unique: true, fxTheme: 'electric',
    requiresAll: ['freeze_trigger', 'e_stim'],
    desc: 'a frozen field has zero resistance: while a freeze holds, your pop discharges into the nearest bubbles and the current CHAINS onward - one crawling bolt-cascade across the whole screen.',
    flavor: 'cold wire carries everything.',
    apply: (s) => { s.superconductor = true; } },
  { id: 'autoplay', name: 'Autoplay', rarity: 'Rare', curse: false, mult: 0.0, unique: true,
    requiresAny: ['porn_dvd'],
    desc: 'when the logo hits a CORNER, the whole screen pays: a shockwave sweeps out from the corner taking everything it touches, and treats rain in after it.',
    flavor: 'everyone claps. even the hole.',
    apply: (s) => { s.autoplay = true; } },
  { id: 'trust_exercise', name: 'Trust Exercise', rarity: 'Rare', curse: false, mult: 0.0, unique: true,
    requiresAny: ['blindfold'],
    desc: 'every pop rings out a sonar pulse that lights the dimmed bubbles back up for a heartbeat as it passes over them.',
    flavor: 'you don\'t need eyes. you need rhythm.',
    apply: (s) => { s.trustExercise = true; } },
  { id: 'milking_machine', name: 'Milking Machine', rarity: 'Rare', curse: false, mult: 0.0, unique: true,
    requiresAll: ['the_pump', 'the_wand'],
    desc: 'while the pump\'s suction runs your drawn ink refuses to fade - and when the pump lets go, the whole drawing DETONATES, popping everything along its length.',
    flavor: 'draw first. then squeeze.',
    apply: (s) => { s.milkingMachine = true; } },
  { id: 'skinny_dipping', name: 'Skinny Dipping', rarity: 'Rare', curse: false, mult: 0.0, unique: true, fxTheme: 'electric',
    requiresAll: ['skipping_stone', 'e_stim'],
    desc: 'the ripple runs charged: every treat the wave takes arcs lightning onward into up to 2 bubbles within 420px.',
    flavor: 'you knew the water was live when you slid in.',
    apply: (s) => { s.chargedRipple = true; } },
  { id: 'one_track_mind', name: 'One-Track Mind', rarity: 'Rare', curse: false, mult: 0.0, unique: true,
    requiresAll: ['intrusive_thoughts', 'the_pull'],
    desc: 'intrusive thoughts stop racing past - they ORBIT your cursor for their lifetime, a popping halo mowing down whatever the pull drags in.',
    flavor: 'now it\'s the only thought you have.',
    apply: (s) => { s.thoughtOrbit = true; } },
  { id: 'weather_girl', name: 'Weather Girl', rarity: 'Rare', curse: false, mult: 0.0, unique: true,
    requiresAny: ['mood_ring'],
    desc: 'every new sky makes an ENTRANCE: static opens with a bolt barrage, perfume with hearts, fool\'s gold with lucky bubbles, stillness with slowed time, overstim with a treat shower.',
    flavor: 'she reads the forecast on your skin.',
    apply: (s) => { s.weatherGirl = true; } },
  { id: 'midas_ricochet', name: 'Midas Ricochet', rarity: 'Rare', curse: false, mult: 0.0, unique: true,
    requiresAny: ['rabbits_foot'],
    desc: 'a popped lucky bubble GILDS every treat within 340px for 2.5s - each gilded pop tips 2-4 gold on the spot.',
    flavor: 'the touch spreads.',
    apply: (s) => { s.midas = true; } },
  { id: 'loaded_dice', name: 'Loaded Dice', rarity: 'Rare', curse: false, mult: 0.0, unique: true,
    requiresAny: ['taking_chances'],
    desc: 'three DOUBLED coin-flips in a row hit the jackpot: 8-12 gold droplets rain from the top of the screen.',
    flavor: 'the house cheats. tonight, so do you.',
    apply: (s) => { s.loadedDice = true; } },
  { id: 'echo_chamber', name: 'Echo Chamber', rarity: 'Rare', curse: false, mult: 0.0, unique: true,
    requiresAny: ['sticky_fingers'],
    desc: 'a third of the treats your held card takes ECHO - two more bubbles bloom right where they popped. keep sweeping.',
    flavor: 'say it again. and again.',
    apply: (s) => { s.echoChamber = true; } },

  // ---- sins - risk/reward: visible drawbacks, visible sweetness ----
  { id: 'hair_trigger', name: 'Hair Trigger', rarity: 'Rare', curse: true, mult: 0.40, unique: true,
    desc: 'every trance burns 25% faster. in exchange, +0.40x run multiplier.',
    flavor: 'everything goes off early. including you.',
    apply: (s) => { s.fuseTimeMult *= 0.75; } },
  { id: 'playing_fire', name: 'Playing with fire', rarity: 'Rare', curse: true, mult: 0.15, unique: true,
    desc: 'trigger effects last 50% longer. in exchange, snapping a live bubble in its final second tips 5-9 gold, plus +0.15x run multiplier.',
    flavor: 'warm hands were always the price.',
    apply: (s) => { s.detonationDurationMult = 1.5; s.lastSecondGold = true; },
    applyShielded: (s) => { s.lastSecondGold = true; } },
  { id: 'bright_colors', name: 'Look at the bright colors...', rarity: 'Rare', curse: true, mult: 0.0, unique: true,
    desc: '5% of spawns are prism bubbles wearing another bubble\'s look. popping one pays x10 and fires the copied effect at full strength.',
    flavor: 'so pretty you forget to ask what\'s inside.',
    apply: (s) => { s.prismChance = 0.05; },
    applyShielded: (s) => { s.prismChance = 0.05; s.prismTreatOnly = true; } },
  { id: 'cam_girl', name: 'Cam Girl', rarity: 'Rare', curse: true, mult: 0.40, unique: true,
    desc: 'bubbles flee your cursor, stronger than any pull. in exchange, 25% of pops tip 2-4 gold, plus +0.40x run multiplier.',
    flavor: 'look, don\'t touch. tips appreciated.',
    apply: (s) => { s.camGirlFlee = 1.6; s.camGirlTipChance = 0.25; },
    applyShielded: (s) => { s.camGirlTipChance = 0.25; } },
  { id: 'the_urge', name: 'The urge', rarity: 'Rare', curse: true, mult: 0.0, unique: true,
    desc: 'the rest of the descent pays x3 on everything. your toys are off-limits.',
    flavor: 'bare hands. that\'s the deal.',
    apply: (s) => { s.urgeMult = 3.0; s.activesDisabled = true; },
    applyShielded: (s) => { s.urgeMult = 2.0; } },
  { id: 'double_or_nothing', name: 'Relapse', rarity: 'Rare', curse: true, mult: 0.0, unique: true,
    desc: '60% chance the descent runs one loop longer than promised, and that loop pays double gold and double drops.',
    flavor: 'one more. it\'s always just one more.',
    apply: (s) => { s.relapseArmed = Math.random() < 0.6; },
    applyShielded: (s) => { s.relapseArmed = true; } },   // shielded: the extra loop is certain

  // ---- Wave 2 sins: the tunnel itself joins the bargain ----
  { id: 'freefall', name: 'Freefall', rarity: 'Rare', curse: true, mult: 0.40, unique: true,
    desc: 'the fall runs at DOUBLE speed and bubbles come 25% faster. in exchange, +0.40x run multiplier.',
    flavor: 'stop bracing. it\'s happening.',
    apply: (s) => { s.freefallActive = true; s.freefallCadence = true; },
    applyShielded: (s) => { s.freefallActive = true; } },   // shielded: the speed stays, the cadence doesn't
  { id: 'private_show', name: 'Private Show', rarity: 'Rare', curse: true, mult: 0.25, unique: true,
    needsVideo: true,
    desc: 'once, mid-descent, one of your videos takes the stage at FULL volume for 15s while the bubbles keep coming. keep your streak alive through the show and it DOUBLES; +0.25x run multiplier.',
    flavor: 'eyes on her. hands busy.',
    apply: (s) => { s.privateShowPending = true; s.privateShowAt = 0.5 + Math.random() * 0.2; },
    applyShielded: (s) => { s.privateShowPending = true; s.privateShowAt = 0.5 + Math.random() * 0.2; s.privateShowPayMult = 3.0; } },
  { id: 'spun', name: 'Spun', rarity: 'Rare', curse: true, mult: 0.35, unique: true,
    desc: 'the whole world rolls, slowly, for the rest of the descent. in exchange, +0.35x run multiplier.',
    flavor: 'which way is up, dolly?',
    apply: (s) => { s.spunRollRate = 9; },
    applyShielded: (s) => { s.spunRollRate = 4.5; } },
];

export const boonById = (id) => BOONS.find((b) => b.id === id) || null;

// Visual identity key for the 3D pick (engine/boonPick.js): sins read from the
// `curse` flag; a handful of boons carry an explicit `fxTheme` (electric / rabbit);
// synergy duos/trios get a gold accent; everything else themes by rarity.
const RARITY_THEME = { Common: 'common', Uncommon: 'uncommon', Rare: 'rare' };
export function boonTheme(b) {
  if (!b) return 'common';
  if (b.curse) return 'sin';
  if (b.fxTheme) return b.fxTheme;                 // explicit look wins over the duo frame
  if (b.requiresAny || b.requiresAll) return 'duo';
  return RARITY_THEME[b.rarity] || 'common';
}

/**
 * How much a lifetime item (toy/accessory/charm id) matters to the duo pool
 * RIGHT NOW, given what's already equipped this run:
 *   2 = grabbing it UNLOCKS at least one still-available duo/trio draft
 *       (a requiresAny hit, or the last missing piece of a requiresAll),
 *   1 = it progresses a requiresAll set that would still be missing more,
 *   0 = no synergy card cares about it.
 * The drop/doorway offer picker uses this to bait the partner items in early.
 */
export function duoPartnerScore(id, equipment = [], takenIds = null) {
  if (equipment.includes(id)) return 0;
  let score = 0;
  for (const b of BOONS) {
    if (b.curse || (!b.requiresAny && !b.requiresAll)) continue;
    if (b.unique && takenIds && takenIds.has(b.id)) continue;   // already drafted this run
    if (b.requiresAny) {
      if (!b.requiresAny.includes(id)) continue;
      if (b.requiresAny.some((x) => equipment.includes(x))) continue;   // already unlocked
      return 2;
    }
    if (!b.requiresAll.includes(id)) continue;
    const missing = b.requiresAll.filter((x) => !equipment.includes(x));
    if (missing.length === 1) return 2;         // this grab completes the set
    score = 1;                                  // first piece of a pair - still worth nudging
  }
  return score;
}

// When the loadout has EARNED a synergy card, it should actually show up in the
// hand - not drown in the ~20-card commons pool. Chance to pull one eligible
// duo/trio to the front of the shuffled deck (guaranteeing it a seat).
const DUO_FAVOR = 0.75;

/**
 * Deal a draft (C# ChaosBoonPool.Draft): mostly mantras + a dedicated sin slot
 * (sinChance roll, or guaranteed by the Surrender capstone). Duo/trio cards
 * need their partner equipped; unique cards already taken sit the run out.
 */
export function draft({ allowCurses = true, choices = 3, guaranteeCurse = false,
  takenIds = null, sinChance = 0.5, equipment = [], hasVideo = false } = {}) {
  choices = Math.min(4, Math.max(2, choices));
  const has = (id) => equipment.includes(id);
  const draftable = (b) =>
    (!b.requiresAny || b.requiresAny.some(has))
    && (!b.requiresAll || b.requiresAll.every(has))
    && (!b.needsVideo || hasVideo)   // Private Show needs a video in the preset
    && !(b.unique && takenIds && takenIds.has(b.id));
  const shuffle = (arr) => arr.map((v) => [Math.random(), v]).sort((a, z) => a[0] - z[0]).map((p) => p[1]);
  const boons = shuffle(BOONS.filter((b) => !b.curse && draftable(b)));
  const curses = shuffle(BOONS.filter((b) => b.curse && draftable(b)));

  // Duo favoring (2026-07): the shuffle is uniform, so the first duo it left in
  // the deck is a uniformly-random pick among the eligible ones - promote it.
  const duoIdx = boons.findIndex((b) => b.requiresAny || b.requiresAll);
  if (duoIdx > 0 && Math.random() < DUO_FAVOR) {
    boons.unshift(boons.splice(duoIdx, 1)[0]);
  }

  const out = [];
  const includeCurse = allowCurses && (guaranteeCurse || Math.random() < sinChance) && curses.length > 0;
  const boonCount = includeCurse ? choices - 1 : choices;
  out.push(...boons.slice(0, Math.min(boonCount, boons.length)));
  if (includeCurse) out.push(curses[0]);
  for (const b of boons.slice(boonCount)) {
    if (out.length >= choices) break;
    out.push(b);
  }
  return out.slice(0, choices);
}
