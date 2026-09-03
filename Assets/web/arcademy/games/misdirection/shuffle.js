/* ============================================================================
 * games/misdirection/shuffle.js - MISDIRECTION's seeded shuffle plan. PURE.
 *
 * No DOM, no engine, no clock, no ctx. Everything here is a pure function of
 * (seed, gradeTier, round index, ride depth, remedial). That is what makes a
 * retake replay the identical table and what makes the TRACKABILITY INVARIANT
 * assertable headless.
 *
 * ---------------------------------------------------------------------------
 * THE TRACKABILITY INVARIANT (the critic's top fix; the contract closes it as
 * LAW, and `verifyRound()` below is the machine-readable form the suite runs
 * over 200 seeds x 4 tiers):
 *
 *   1. THE CHAIN IS THE TRUTH. A round's swap chain is dealt from the seed
 *      BEFORE any effect is chosen, and `simulate()` replays it to the final
 *      slot. The plan is reconstructable in principle: nothing the player does
 *      moves the target, and nothing the decks do is allowed to.
 *   2. AT MOST ONE HIDDEN LINK. Of the swaps that actually MOVE THE TARGET, at
 *      most ONE may be occluded (a glitch swap or a blackout beat). Every other
 *      occlusion in the round lands on a swap the target is not part of - the
 *      house looks like it blinded you three times; it only ever hid one link.
 *   3. A TELL ALWAYS SURVIVES. Every occluded swap carries a `tell` naming the
 *      two slots involved, emitted as a peripheral mark AND an audio cue
 *      regardless of the blackout. Every occlusion carries one, so the tell can
 *      never be used to spot which occlusion was the real one.
 *   4. DECOYS NEVER LIFT THE TARGET. A decoy reveal is scheduled on a slot the
 *      target is not under at that instant, so the bait is always a lie about
 *      an empty shell and never a truthful accident.
 *
 * ---------------------------------------------------------------------------
 * DIALS FIRST, DIFFICULTY SECOND (GROUND-RULES §6). `rideDepth` raises the
 * EFFECT tier by one notch (decoy count, occlusion count, wash alpha) and
 * never the shell count, the swap rate or the preview time - the dossier's
 * "effects first" rule, made structural: `effectTier` and `classicTier` are
 * two different numbers and only the second one touches the classic dials.
 *
 * REDUCED MOTION caps the swap RATE and stretches the shuffle window to keep
 * the SAME chain length - the dossier's "difficulty is preserved via longer
 * swap chains instead of speed", so grading stays fair.
 *
 * SEEDING (Law V): `seed + '|md-chain|' + index` deals the truth (start slot +
 * chain), `seed + '|md-load|' + index` deals the presentation (which swaps are
 * occluded, where the decoys lift). Draw order is append-only inside each.
 * ==========================================================================*/

import { makeRng, makeTaggedRoll } from '../../core/rng.js';

/** Every number a playtest might touch. One place, one edit. */
export const PLAYTEST = Object.freeze({
  /* ---- classic dials (gradeTier ONLY - a ride never moves these) ------- */
  /** Shells on the arc. 3 -> 3 -> 4 -> 5 (dossier). */
  SHELLS: Object.freeze({ 1: 3, 2: 3, 3: 4, 4: 5 }),
  /** The reveal window, grade-scaled 1.6s -> 0.7s (dossier). */
  PREVIEW_MS: Object.freeze({ 1: 1600, 2: 1300, 3: 1000, 4: 700 }),
  /** Nominal shuffle window; the chain length is rate x window. */
  SHUFFLE_MS: Object.freeze({ 1: 2400, 2: 3000, 3: 3800, 4: 4600 }),
  /** Swaps per second. */
  SWAP_RATE: Object.freeze({ 1: 0.9, 2: 1.3, 3: 1.7, 4: 2.2 }),
  /** The pick window. HONEST and never bent - only the drawn ring may lie. */
  PICK_MS: 4000,

  /* ---- effect dials (raised one notch by ANY ride) --------------------- */
  /** Decoy reveals per shuffle. */
  DECOYS: Object.freeze({ 1: 0, 2: 1, 3: 2, 4: 3 }),
  /** Unanimated glitch swaps per shuffle. */
  GLITCHES: Object.freeze({ 1: 0, 2: 0, 3: 1, 4: 2 }),
  /** Blackout beats (wash rides to its cap) per shuffle. */
  BLACKOUTS: Object.freeze({ 1: 0, 2: 0, 3: 0, 4: 1 }),
  /** A decoy reveal shows a convincing FAKE target from this effect tier up. */
  FAKE_TARGET_TIER: 4,
  /** row_drift slides the whole arc during the pick window from this tier up. */
  ROW_DRIFT_TIER: 4,
  /** flash_burst contaminates the last glance at pick-open from this tier up. */
  PICK_BURST_TIER: 3,
  /** Base wash alpha by effect tier, and what one ride notch adds. */
  WASH_ALPHA: Object.freeze({ 1: 0.18, 2: 0.32, 3: 0.48, 4: 0.62 }),
  WASH_PER_RIDE: 0.06,
  /** How long a blackout beat holds the wash at its cap. */
  BLACKOUT_MS: 300,
  /** How long an occluded swap's peripheral tell stays on the two shells. */
  TELL_MS: 420,
  /** Bubble decoys drifting over the arc, by effect tier. */
  BUBBLES: Object.freeze({ 1: 0, 2: 4, 3: 6, 4: 8 }),

  /* ---- the pot ---------------------------------------------------------- */
  /** Ride cap (Intake's STREAK_CAP posture). At the cap a win force-banks. */
  RIDE_CAP: 5,
  /** A clean win pays this; each ride doubles the live pot. */
  POT_BASE: 1,

  /* ---- the remedial round (the comeback hook) -------------------------- */
  /** Consecutive misses that arm it. */
  REMEDIAL_AFTER: 2,
  /** Extended preview, and it cannot fire twice in a row (anti-clump). */
  REMEDIAL_PREVIEW_MULT: 1.8,

  /* ---- reduced motion --------------------------------------------------- */
  /** Swap rate ceiling; the window stretches to keep the chain length. */
  REDUCED_RATE_CAP: 1.1,

  /* ---- heat + audio ----------------------------------------------------- */
  HEAT_CAP: Object.freeze({ 1: 0.45, 2: 0.65, 3: 0.85, 4: 1 }),
  HEAT_FLOOR: 0.14,
  /** Streak that reaches the top of the game's own ladder. */
  HEAT_STREAK_FULL: 8,
  /** How the ladder splits between streak and ride depth. */
  HEAT_STREAK_SHARE: 0.6,
  AUDIO_CEIL: Object.freeze({ 1: 0.45, 2: 0.6, 3: 0.75, 4: 0.9 }),

  /* ---- beats ------------------------------------------------------------ */
  BRIEF_MS: 1700,
  BRIEF_MS_REDUCED: 900,
  SETTLE_MS: 380,
  RESOLVE_MS: 1200,
  RESOLVE_MS_REDUCED: 700,
  STAKE_MS: 4000,
  AUTO_STAKE_MS: 900,
  BELL_WARN_SEC: 20,
  END_HOLD_MS: 2600,
  END_HOLD_MS_REDUCED: 1400,
  /** sub_flash cadence between rounds, by effect tier (0 = off). */
  SUB_FLASH_MS: Object.freeze({ 1: 0, 2: 9000, 3: 7000, 4: 5200 }),
  /** The trickster's `stalled(ms)` tick. */
  STALL_TICK_MS: 500,
});

export function clamp01(v) { const n = Number(v); return !Number.isFinite(n) ? 0 : n < 0 ? 0 : n > 1 ? 1 : n; }
export function tierOf(gradeTier) { return Math.max(1, Math.min(4, Math.round(Number(gradeTier) || 1))); }

/** The per-game stake setting, normalised. */
export function stakeModeFrom(v) {
  const s = String(v == null ? '' : v).trim().toLowerCase();
  return (s === 'bank' || s === 'ride') ? s : 'ask';
}

/** The per-game skin setting, normalised (the value lands on data-skin). */
export function skinFrom(v) {
  const s = String(v == null ? '' : v).trim().toLowerCase();
  return (s === 'minimal' || s === 'contrast') ? s : 'themed';
}

/**
 * Every dial for ONE round.
 *
 * `classicTier` is the grade tier and nothing else touches it. `effectTier` is
 * the grade tier plus one notch whenever the player is riding - that is the
 * whole of "riding raises the distraction dials before anything classical".
 *
 * @param {Object} o
 *   gradeTier   1..4
 *   rideDepth   0..RIDE_CAP - how deep the live pot is ridden
 *   remedial    true = the comeback round (clean shuffle, longer look)
 *   reduced     reduced motion / motionLevel 0
 */
export function dialsFor(o = {}) {
  const classicTier = tierOf(o.gradeTier);
  const rideDepth = Math.max(0, Math.min(PLAYTEST.RIDE_CAP, Math.round(Number(o.rideDepth) || 0)));
  const remedial = !!o.remedial;
  const reduced = !!o.reduced;
  const effectTier = remedial ? 1 : Math.min(4, classicTier + (rideDepth > 0 ? 1 : 0));

  const shells = PLAYTEST.SHELLS[classicTier];
  const nominalRate = PLAYTEST.SWAP_RATE[classicTier];
  const nominalMs = PLAYTEST.SHUFFLE_MS[classicTier];
  /* THE CHAIN LENGTH is a classic dial, and reduced motion must not lower it. */
  const chainLen = Math.max(1, Math.round(nominalRate * (nominalMs / 1000)));
  const swapRate = reduced ? Math.min(nominalRate, PLAYTEST.REDUCED_RATE_CAP) : nominalRate;
  const shuffleMs = Math.round((chainLen / swapRate) * 1000);

  const decoys = remedial ? 0 : PLAYTEST.DECOYS[effectTier];
  const glitches = remedial ? 0 : PLAYTEST.GLITCHES[effectTier];
  const blackouts = remedial ? 0 : PLAYTEST.BLACKOUTS[effectTier];

  return Object.freeze({
    classicTier,
    effectTier,
    rideDepth,
    remedial,
    reduced,
    shells,
    chainLen,
    swapRate,
    shuffleMs,
    previewMs: Math.round(PLAYTEST.PREVIEW_MS[classicTier] * (remedial ? PLAYTEST.REMEDIAL_PREVIEW_MULT : 1)),
    pickMs: PLAYTEST.PICK_MS,
    decoys,
    glitches,
    blackouts,
    fakeTargets: !remedial && effectTier >= PLAYTEST.FAKE_TARGET_TIER,
    rowDrift: !remedial && effectTier >= PLAYTEST.ROW_DRIFT_TIER,
    pickBurst: !remedial && effectTier >= PLAYTEST.PICK_BURST_TIER,
    bubbles: remedial ? 0 : PLAYTEST.BUBBLES[effectTier],
    washAlpha: clamp01(PLAYTEST.WASH_ALPHA[effectTier] + rideDepth * PLAYTEST.WASH_PER_RIDE),
    blackoutMs: PLAYTEST.BLACKOUT_MS,
    tellMs: PLAYTEST.TELL_MS,
    heatCap: PLAYTEST.HEAT_CAP[classicTier],
    audioCeil: PLAYTEST.AUDIO_CEIL[classicTier],
    subFlashMs: remedial ? 0 : PLAYTEST.SUB_FLASH_MS[effectTier],
    /* The distraction LOAD of this round, and the load a clean round of this
     * grade tier would carry. `heavy` is what the rubric weighs at 1.5x. */
    load: loadOf(decoys, glitches, blackouts),
    baselineLoad: loadOf(PLAYTEST.DECOYS[classicTier], PLAYTEST.GLITCHES[classicTier], PLAYTEST.BLACKOUTS[classicTier]),
  });
}

/** One scalar for "how blinded was this shuffle". Pure bookkeeping. */
export function loadOf(decoys, glitches, blackouts) {
  return (Number(decoys) || 0) * 0.5 + (Number(glitches) || 0) * 1 + (Number(blackouts) || 0) * 1.5;
}

/**
 * THE CHAIN - the truth, dealt from the seed and the round index ONLY.
 *
 * Two shells trade slots per link. The chain never repeats the SAME unordered
 * pair twice in a row (a visible A<->B<->A is a wasted link and reads as a
 * stutter), and a link always touches two different slots.
 *
 * @returns {{startSlot:number, swaps:Array<{a:number,b:number}>}}
 */
export function buildChain(seed, dials) {
  const d = dials || dialsFor({ gradeTier: 1 });
  const n = d.shells;
  const rng = makeRng(String(seed));
  const startSlot = Math.min(n - 1, Math.floor(rng() * n));
  const swaps = [];
  let lastKey = '';
  let guard = 0;
  while (swaps.length < d.chainLen && guard++ < d.chainLen * 40 + 80) {
    const a = Math.min(n - 1, Math.floor(rng() * n));
    let b = Math.min(n - 1, Math.floor(rng() * n));
    if (b === a) b = (a + 1) % n;
    const key = Math.min(a, b) + ':' + Math.max(a, b);
    if (key === lastKey) continue;
    lastKey = key;
    swaps.push({ a: Math.min(a, b), b: Math.max(a, b) });
  }
  /* A degenerate guard exit still returns a legal chain (never an empty one). */
  while (swaps.length < 1) swaps.push({ a: 0, b: Math.min(1, n - 1) });
  return { startSlot, swaps };
}

/**
 * Replay a chain. PURE and the suite's oracle: `simulate(plan).finalSlot` must
 * equal `plan.finalSlot`, and `movesTarget` on every link must match.
 *
 * @returns {{finalSlot:number, order:number[], path:number[], moved:boolean[]}}
 *   order[slot] = shell id sitting in that slot at the end.
 */
export function simulate(plan) {
  const n = Math.max(2, Math.round(Number(plan && plan.shells) || 3));
  const order = [];
  for (let i = 0; i < n; i++) order.push(i);
  let slot = Math.max(0, Math.min(n - 1, Math.round(Number(plan && plan.startSlot) || 0)));
  const path = [slot];
  const moved = [];
  const swaps = (plan && Array.isArray(plan.swaps)) ? plan.swaps : [];
  for (const s of swaps) {
    const a = Math.max(0, Math.min(n - 1, Math.round(Number(s.a) || 0)));
    const b = Math.max(0, Math.min(n - 1, Math.round(Number(s.b) || 0)));
    const tmp = order[a]; order[a] = order[b]; order[b] = tmp;
    const hit = (slot === a || slot === b);
    if (hit) slot = (slot === a) ? b : a;
    moved.push(hit);
    path.push(slot);
  }
  return { finalSlot: slot, order, path, moved };
}

/**
 * THE ROUND PLAN.
 *
 * Order of operations is the invariant's whole enforcement:
 *   1. deal the chain from `seed|md-chain|index` (truth; ride depth cannot
 *      touch it, so the ledger is a pure function of the seed);
 *   2. simulate it to learn which links move the target;
 *   3. choose occlusions from `seed|md-load|index` under the ONE-LINK rule:
 *      at most one target-moving link may be hidden, everything else must
 *      land on a link the target is not in - and if there are not enough of
 *      those, the occlusion count DROPS rather than the invariant;
 *   4. every occluded link gets a truthful tell;
 *   5. decoy reveals are placed on slots the target is not under.
 *
 * @param {Object} o {seed, gradeTier, index, rideDepth, remedial, reduced}
 */
export function buildRound(o = {}) {
  const index = Math.max(0, Math.round(Number(o.index) || 0));
  const seed = String(o.seed == null ? '' : o.seed);
  const dials = dialsFor(o);
  const n = dials.shells;

  /* ---- 1. the truth --------------------------------------------------- */
  const chain = buildChain(seed + '|md-chain|' + index, dials);
  const swaps = chain.swaps.map((s, i) => ({
    index: i,
    a: s.a,
    b: s.b,
    /** When the link lands inside the shuffle window (ms from shuffle start). */
    at: Math.round(((i + 1) / (chain.swaps.length + 1)) * dials.shuffleMs),
    glitch: false,
    blackout: false,
    occluded: false,
    movesTarget: false,
    tell: null,
  }));

  /* ---- 2. which links move her ---------------------------------------- */
  const sim = simulate({ shells: n, startSlot: chain.startSlot, swaps });
  for (let i = 0; i < swaps.length; i++) swaps[i].movesTarget = !!sim.moved[i];

  /* ---- 3. the occlusions, under the ONE-LINK rule ---------------------- */
  const roll = makeTaggedRoll(seed + '|md-load|' + index);
  const carriers = [];                    // links that MOVE her
  const empties = [];                     // links she is not in
  for (const s of swaps) (s.movesTarget ? carriers : empties).push(s);

  const wantHidden = dials.glitches + dials.blackouts;
  const picked = [];
  /* The ONE link: only ever one, and only when the round is allowed any
   * occlusion at all. Deliberately dealt FIRST so the seeded choice of WHICH
   * link is hidden never depends on how many decoy occlusions fit. */
  if (wantHidden > 0 && carriers.length > 0) {
    picked.push(carriers[Math.floor(roll('link') * carriers.length) % carriers.length]);
  }
  /* Everything else must land on a link she is not in. The pool is walked in a
   * seeded order and consumed without repeats; running out lowers the count. */
  const pool = empties.slice();
  for (let k = pool.length - 1; k > 0; k--) {
    const j = Math.floor(roll('shuffle') * (k + 1)) % (k + 1);
    const tmp = pool[k]; pool[k] = pool[j]; pool[j] = tmp;
  }
  while (picked.length < wantHidden && pool.length) picked.push(pool.shift());

  /* Blackouts are the loudest, so they take the LATE links (a blackout on the
   * first link of a five-link chain is wasted on a shell nobody is tracking
   * yet). Sort the chosen set by position and hand the tail the blackouts. */
  picked.sort((x, y) => x.index - y.index);
  const blackoutCount = Math.min(dials.blackouts, picked.length);
  for (let i = 0; i < picked.length; i++) {
    const s = picked[i];
    s.occluded = true;
    const isBlackout = i >= picked.length - blackoutCount;
    s.blackout = isBlackout;
    s.glitch = !isBlackout;
    /* 3-4. THE TELL. Truthful, on both slots, and every occlusion carries one
     * so the tell can never single out the link that mattered. */
    s.tell = {
      slots: [s.a, s.b],
      /* which side of the table it comes from - the audio cue's pitch hint */
      side: (s.a + s.b) / 2 < (n - 1) / 2 ? 'left' : 'right',
      ms: dials.tellMs,
    };
  }

  /* ---- 5. decoy reveals, never on her --------------------------------- */
  const decoys = [];
  const decoySpacing = dials.shuffleMs / (dials.decoys + 1);
  for (let d = 0; d < dials.decoys; d++) {
    /* Position the lift between links so it reads as a beat of its own; the
     * seeded jitter keeps a lift off the exact frame of a slide. */
    const at = Math.max(120, Math.min(dials.shuffleMs - 120, Math.round(
      (d + 1) * decoySpacing + (roll('when') - 0.5) * decoySpacing * 0.4,
    )));
    /* Where is she at that instant? The link index she has passed so far. */
    let passed = 0;
    for (const s of swaps) { if (s.at <= at) passed += 1; }
    const hereSlot = sim.path[Math.min(passed, sim.path.length - 1)];
    const choices = [];
    for (let i = 0; i < n; i++) if (i !== hereSlot) choices.push(i);
    const slot = choices[Math.floor(roll('decoy') * choices.length) % choices.length];
    decoys.push({
      index: d,
      at,
      slot,
      /* At the top effect tier the bait is a convincing FAKE target. */
      fake: dials.fakeTargets && roll('fake') < 0.6,
    });
  }

  const hiddenLinks = swaps.filter((s) => s.occluded && s.movesTarget).length;

  return Object.freeze({
    index,
    seed,
    shells: n,
    dials,
    startSlot: chain.startSlot,
    swaps: Object.freeze(swaps.map((s) => Object.freeze(s))),
    decoys: Object.freeze(decoys.map((d) => Object.freeze(d))),
    finalSlot: sim.finalSlot,
    path: Object.freeze(sim.path.slice()),
    /** Occluded links that actually hid her. LAW: never more than 1. */
    hiddenLinks,
    /** True when the round hid a link at all - the sGate's "eyes open" test. */
    blind: hiddenLinks > 0,
    /** True when the round out-loaded a clean round of this grade tier. */
    heavy: dials.load > dials.baselineLoad + 1e-9,
    remedial: dials.remedial,
    rideDepth: dials.rideDepth,
    /** The whole round, end to end, in ms (the clock is the shell's). */
    totalMs: dials.previewMs + PLAYTEST.SETTLE_MS + dials.shuffleMs + dials.pickMs,
  });
}

/**
 * THE INVARIANT, as an assertion. Returns a list of violations; an empty list
 * is a legal round. The node suite runs this over 200 seeds x 4 tiers x every
 * ride depth, and index.js runs it in `ctx.dev` builds only.
 *
 * @returns {string[]}
 */
export function verifyRound(plan) {
  const bad = [];
  if (!plan || !Array.isArray(plan.swaps)) return ['no plan'];
  const n = plan.shells;
  if (!(n >= 2 && n <= 5)) bad.push('shell count out of range: ' + n);
  if (!plan.swaps.length) bad.push('empty chain');

  /* 1. reconstructable */
  const sim = simulate(plan);
  if (sim.finalSlot !== plan.finalSlot) bad.push('finalSlot ' + plan.finalSlot + ' != simulated ' + sim.finalSlot);

  let hidden = 0;
  let lastKey = '';
  for (let i = 0; i < plan.swaps.length; i++) {
    const s = plan.swaps[i];
    if (s.a === s.b) bad.push('link ' + i + ' swaps a slot with itself');
    if (s.a < 0 || s.b < 0 || s.a >= n || s.b >= n) bad.push('link ' + i + ' is off the arc');
    if (!!s.movesTarget !== !!sim.moved[i]) bad.push('link ' + i + ' movesTarget disagrees with the simulation');
    const key = Math.min(s.a, s.b) + ':' + Math.max(s.a, s.b);
    if (key === lastKey) bad.push('link ' + i + ' repeats the previous pair');
    lastKey = key;
    /* 2. at most one hidden LINK */
    if (s.occluded && s.movesTarget) hidden += 1;
    /* 3. a tell always survives */
    if (s.occluded && !(s.tell && Array.isArray(s.tell.slots) && s.tell.slots.length === 2)) {
      bad.push('link ' + i + ' is occluded with no tell');
    }
    if (!s.occluded && s.tell) bad.push('link ' + i + ' carries a tell it did not earn');
    if (s.occluded && !(s.glitch || s.blackout)) bad.push('link ' + i + ' is occluded by nothing');
  }
  if (hidden > 1) bad.push('TRACKABILITY: ' + hidden + ' hidden links (max 1)');
  if (hidden !== plan.hiddenLinks) bad.push('hiddenLinks ' + plan.hiddenLinks + ' != counted ' + hidden);

  /* 4. decoys never lift her */
  for (const d of (plan.decoys || [])) {
    let passed = 0;
    for (const s of plan.swaps) { if (s.at <= d.at) passed += 1; }
    const here = sim.path[Math.min(passed, sim.path.length - 1)];
    if (d.slot === here) bad.push('decoy ' + d.index + ' lifts the true shell');
    if (d.slot < 0 || d.slot >= n) bad.push('decoy ' + d.index + ' is off the arc');
  }

  /* the honest window is never bent */
  if (plan.dials.pickMs !== PLAYTEST.PICK_MS) bad.push('the pick window was bent');
  return bad;
}

/**
 * THE POT. Pure, and upward-only by construction: `banked` never falls.
 *
 * @param {Object} pot {live, rideDepth, banked, deepestBanked}
 * @param {string} action 'win' | 'bank' | 'ride' | 'bust' | 'double'
 * @returns {Object} a NEW pot state plus `event` ('forceBank' at the ride cap)
 */
export function potAfter(pot, action) {
  const p = {
    live: Math.max(0, Number(pot && pot.live) || 0),
    rideDepth: Math.max(0, Math.min(PLAYTEST.RIDE_CAP, Math.round(Number(pot && pot.rideDepth) || 0))),
    banked: Math.max(0, Number(pot && pot.banked) || 0),
    deepestBanked: Math.max(0, Math.round(Number(pot && pot.deepestBanked) || 0)),
    event: '',
  };
  switch (String(action)) {
    case 'win':
      /* A win pays the base times two per ride already taken. */
      p.live = PLAYTEST.POT_BASE * Math.pow(2, p.rideDepth);
      /* THE CAP: five deep force-banks with the jackpot ceremony. */
      if (p.rideDepth >= PLAYTEST.RIDE_CAP) {
        p.banked += p.live;
        p.deepestBanked = Math.max(p.deepestBanked, p.rideDepth);
        p.live = 0;
        p.rideDepth = 0;
        p.event = 'forceBank';
      }
      break;
    case 'double':
      /* The scholarship round: the jackpot ceremony doubles the live pot free. */
      p.live = p.live * 2;
      break;
    case 'bank':
      p.banked += p.live;
      p.deepestBanked = Math.max(p.deepestBanked, p.rideDepth);
      p.live = 0;
      p.rideDepth = 0;
      break;
    case 'ride':
      p.rideDepth = Math.min(PLAYTEST.RIDE_CAP, p.rideDepth + 1);
      break;
    case 'bust':
      /* Only the STAKED pot burns. The bank is loss-proof by design. */
      p.live = 0;
      p.rideDepth = 0;
      break;
    default:
      break;
  }
  return p;
}

/**
 * THE HEAT LADDER. The class's own ladder is streak + ride depth; the grade
 * tier only ever caps it (the DE/IC pattern).
 */
export function heatFor(streak, rideDepth, gradeTier) {
  const cap = PLAYTEST.HEAT_CAP[tierOf(gradeTier)];
  const s = clamp01((Number(streak) || 0) / PLAYTEST.HEAT_STREAK_FULL);
  const r = clamp01((Number(rideDepth) || 0) / PLAYTEST.RIDE_CAP);
  const line = clamp01(s * PLAYTEST.HEAT_STREAK_SHARE + r * (1 - PLAYTEST.HEAT_STREAK_SHARE));
  return clamp01(cap * (PLAYTEST.HEAT_FLOOR + (1 - PLAYTEST.HEAT_FLOOR) * line));
}

export default { buildRound, simulate, verifyRound, dialsFor, potAfter, heatFor, PLAYTEST };
