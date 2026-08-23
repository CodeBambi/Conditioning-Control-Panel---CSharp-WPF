/* ============================================================================
 * games/instant-recall/vigil.js - THE DAY'S VIGIL SCRIPT. PURE.
 *
 * One class = one continuous 120s WALL of the player's own media with
 * unpredictable freeze-and-quiz stops. Everything about the SHOW is dealt here,
 * off the class seed, before a single frame renders: when it stops, whether a
 * bell warns you, how thick the stream gets, which CCP EFFECTS are in tonight's
 * pool and on what cadence, which template each question instantiates from, and
 * where the decoy plants sit. Law V: seed -> plan -> events, never live rng. A
 * retake replays the identical vigil.
 *
 * NOTHING HERE TOUCHES THE DOM, THE ENGINE OR THE CLOCK. index.js walks this
 * plan; montage.js renders the wall; grade.js scores what actually happened. The
 * one thing this file may NOT decide is the ANSWER to a question - that comes
 * from the ledger tail at stop time, because the ledger is the truth (Law I) and
 * a pre-dealt answer would be a lie waiting for a dropped frame.
 *
 * ---------------------------------------------------------------------------
 * THE EFFECT POOL (owner ruling 2026-08-23, "the mosaic rework").
 * A quiz may only ever ask about effects a CCP user already knows BY NAME from
 * the app's own tabs. Scan lines, row drift, glitch swaps and ambient grain are
 * NOT triggers - they were furniture wearing a trigger's clothes, and they are
 * gone from the ledger entirely. What is left is the classics, ten of them, and
 * the ledger channel IS the pool key (never the engine kind), so two pool
 * entries that ride the same primitive - the corner GIF and the fullscreen GIF
 * both ride gif_burst, three washes ride wash - stay distinct answers.
 *
 * THE 700ms RULE. Two effects may never START inside `MIN_SEPARATION_MS` of one
 * another: "what just happened" has to have exactly one honest answer. It is
 * enforced by the dealer (`nextEmission`), not by hope.
 *
 * ---------------------------------------------------------------------------
 * THE FINAL-STOP GUARANTEE (contract ruling 1, and the class's signature exit)
 * The last stop ALWAYS lands inside the final 15 seconds, and always early
 * enough inside them that its answer window plus the verdict resolve before the
 * bell - so the vigil ends on a quiz, never on a fade-out, and never on a
 * truncated question. `assertPlan()` re-checks it and the node suite runs it
 * over 300 seeds x 4 tiers.
 *
 * ---------------------------------------------------------------------------
 * TIER LADDER (dossier; dials first, classic difficulty second)
 *   1  4 effects in the pool at low dials; 2 stops, 1 question, 6s, EVERY stop
 *      belled.
 *   2  6 effects (the whisper and the pink filter enter); 3 stops, 1 question,
 *      6s, exactly ONE unannounced (SYNTHESIS #2 taste of the twist).
 *   3  8 effects (the corner GIF and the cascade enter); ALL stops unannounced;
 *      DECOY PLANTS unlock; LAST_TWO enters; 4 stops, 5s.
 *   4  all 10 (the fullscreen GIF and Brain Drain enter), every dial at the
 *      global ceiling; 3-4 stops, 2 questions each, 4s.
 * ==========================================================================*/

import { makeTaggedRoll } from '../../core/rng.js';

/* ----------------------------------------------------------------------------
 * PLAYTEST BLOCK - every tunable this game has, in one place.
 * Nothing here is an absolute effect strength (the engine owns those, and the
 * CEILING RULE means a game spends a clamped channel, never raises one). What
 * lives here is PACE: milliseconds, counts, bands and weights.
 * -------------------------------------------------------------------------- */
export const PLAYTEST = Object.freeze({
  /* --- ceremony timings ---------------------------------------------- */
  BRIEF_MS: 2600,
  BRIEF_MS_REDUCED: 1400,
  END_HOLD_MS: 2600,
  END_HOLD_MS_REDUCED: 1200,
  /** Tier 1 / announced stops: the warning bell's lead. */
  BELL_LEAD_MS: 1500,
  /** The verdict + truth replay after a commit, before the vigil resumes. */
  VERDICT_MS: 2200,
  VERDICT_MS_REDUCED: 1100,
  /** The bell warning at the end of the class (HUD goes gold). */
  BELL_WARN_SEC: 20,

  /* --- the stop schedule ---------------------------------------------- */
  /** THE FINAL-STOP GUARANTEE: the last stop lands inside this tail window. */
  FINAL_WINDOW_MS: 15000,
  /** ...and no later than (budget - window - this), so it always resolves. */
  RESOLVE_PAD_MS: 1200,
  /** No stop before the vigil has had time to be a vigil. */
  OPEN_MS: Object.freeze({ 1: 16000, 2: 14000, 3: 12000, 4: 11000 }),
  /** Minimum gap between two stops (the schedule is jittered inside segments,
   *  so this is a floor the segmentation already satisfies - asserted anyway). */
  MIN_GAP_MS: Object.freeze({ 1: 22000, 2: 18000, 3: 14000, 4: 12000 }),
  /** The answer window, by tier (contract: 6s / 6s / 5s / 4s). */
  WINDOW_MS: Object.freeze({ 1: 6000, 2: 6000, 3: 5000, 4: 4000 }),
  /** Stops per class. Tier 4 is dealt 3 or 4 (dossier "3-4 stops"). */
  STOPS: Object.freeze({ 1: 2, 2: 3, 3: 4, 4: 0 }),
  STOPS_T4: Object.freeze([3, 4]),
  /** Questions per stop. */
  Q_PER_STOP: Object.freeze({ 1: 1, 2: 1, 3: 1, 4: 2 }),

  /* --- question templates --------------------------------------------- */
  /** Distractors are never drawn from the last N ledger entries of the family:
   *  a distractor that also just happened would make the answer ambiguous. */
  DISTRACTOR_EXCLUDE: 5,

  /* --- decoy plants (tier 3+, SetPiece-gated) -------------------------- */
  PLANT_CAP: Object.freeze({ 1: 0, 2: 0, 3: 2, 4: 3 }),
  PLANT_CHANCE: Object.freeze({ 1: 0, 2: 0, 3: 0.55, 4: 0.75 }),
  /** Where in the answer window the plant fires (fraction of the window). */
  PLANT_AT: Object.freeze([0.34, 0.46, 0.38, 0.52]),

  /* --- density + heat --------------------------------------------------- */
  /** `ir_density` scales the CEILING (never the floor, never the stop schedule). */
  DENSITY_MULT: Object.freeze({ calm: 0.72, standard: 1, dense: 1.28 }),
  DENSITY_CEIL: Object.freeze({ 1: 0.45, 2: 0.62, 3: 0.82, 4: 1 }),
  DENSITY_FLOOR: Object.freeze({ 1: 0.12, 2: 0.16, 3: 0.20, 4: 0.26 }),
  /** How long a segment takes to climb from its floor to its ceiling. */
  RAMP_MS: 26000,
  /** Partial relax after a stop - one band, sawtooth, never back to the floor. */
  RELAX_BAND: 0.28,
  /** Comeback hook: a missed stop eases the NEXT interval's ceiling (session
   *  overlay only, never persisted - the SetSessionFlashRamp posture). */
  MISS_EASE: 0.85,
  /** The heat ladder's ceiling per grade tier (DE/IC pattern). */
  HEAT_CAP: Object.freeze({ 1: 0.45, 2: 0.65, 3: 0.85, 4: 1 }),
  /** Streak's contribution to heat, and progress's. */
  HEAT_FROM_PROGRESS: 0.55,
  HEAT_FROM_STREAK: 0.18,
  HEAT_STREAK_CAP: 3,
  /** Audio ceiling by tier - every cue's `level` is min()'d against it. */
  AUDIO_CEIL: Object.freeze({ 1: 0.32, 2: 0.38, 3: 0.44, 4: 0.50 }),
  /** Chime pitch ratchet per stop streak link, capped like IC's (+7 semitones). */
  PITCH_STEP: 0.06,
  PITCH_CAP: 7,

  /* --- the escape guard (a frozen quiz card is never a trap) ------------ */
  ESCAPE_TAPS: 6,
  ESCAPE_MS: 5000,

  /* --- misc ------------------------------------------------------------- */
  STALL_TICK_MS: 1000,
  CLOCK_TICK_MS: 200,
});

/* ============================================================================
 * THE EFFECT POOL - the ONLY things a question may be about.
 *
 * key      the ledger channel AND the `ir_fx_<key>` lexicon row (the CCP name)
 * kind     the engine primitive it spends
 * variant  the engine variant, where the primitive has one that matters
 * tier     the grade tier that unlocks it
 * held     true when the primitive is a sustain that this class PULSES (a short
 *          burst with a deadline) rather than holds - the ledger entry is
 *          written once, at the pulse's start
 * ==========================================================================*/
export const EFFECT_POOL = Object.freeze([
  Object.freeze({ key: 'flash', kind: 'flash_burst', variant: '', tier: 1, held: false }),
  Object.freeze({ key: 'subliminal', kind: 'sub_flash', variant: '', tier: 1, held: false }),
  Object.freeze({ key: 'bubbles', kind: 'bubble_field', variant: '', tier: 1, held: true }),
  Object.freeze({ key: 'spiral', kind: 'wash', variant: 'spiral', tier: 1, held: true }),
  Object.freeze({ key: 'whisper', kind: 'audio_trigger', variant: '', tier: 2, held: false }),
  Object.freeze({ key: 'pink', kind: 'wash', variant: 'pink', tier: 2, held: true }),
  Object.freeze({ key: 'corner_gif', kind: 'gif_burst', variant: '', tier: 3, held: false }),
  Object.freeze({ key: 'cascade', kind: 'gif_rain', variant: '', tier: 3, held: true }),
  Object.freeze({ key: 'fullscreen_gif', kind: 'gif_burst', variant: '', tier: 4, held: false }),
  Object.freeze({ key: 'brain_drain', kind: 'wash', variant: 'drain', tier: 4, held: true }),
]);

/** Every pool key, in unlock order. A LAST_EFFECT option is always one of these. */
export const POOL_KEYS = Object.freeze(EFFECT_POOL.map((e) => e.key));
/** key -> the pool row. */
export const POOL_BY_KEY = Object.freeze(EFFECT_POOL.reduce((m, e) => { m[e.key] = e; return m; }, {}));
/** How many effects the pool holds at each tier (the 4 / 6 / 8 / 10 ladder). */
export const POOL_SIZE = Object.freeze({ 1: 4, 2: 6, 3: 8, 4: 10 });

/** The pool at a tier. `audible` false drops the whisper: an option the player
 *  cannot hear is not a distractor, it is a coin flip. */
export function poolFor(tier, audible) {
  const k = tierOf(tier);
  return EFFECT_POOL
    .filter((e) => e.tier <= k)
    .filter((e) => (e.kind === 'audio_trigger' ? audible !== false : true))
    .map((e) => e.key);
}

/** Stings (audio_trigger) enter at tier 2 - LAST_STING is gated on them. */
export const STING_FROM_TIER = 2;
/** The sting vocabulary: shell/audio.js SOUNDS names with an `ir_sting_*` row. */
export const STINGS = Object.freeze(['blip', 'sting', 'pop', 'bump', 'glitch']);

/**
 * TWO EFFECTS MAY NEVER START INSIDE THIS WINDOW. The question is always "what
 * JUST happened", so an overlapping pair would have two honest answers and the
 * option list only has room for one. The dealer pushes the later one out.
 */
export const MIN_SEPARATION_MS = 700;

/** Per-pool-key cadence band: base (cold) -> min (at full density). */
export const CADENCE = Object.freeze({
  flash: Object.freeze({ base: 9000, min: 3400 }),
  subliminal: Object.freeze({ base: 3200, min: 1000 }),
  bubbles: Object.freeze({ base: 11000, min: 5000 }),
  spiral: Object.freeze({ base: 13000, min: 6000 }),
  whisper: Object.freeze({ base: 8000, min: 3000 }),
  pink: Object.freeze({ base: 12000, min: 5600 }),
  corner_gif: Object.freeze({ base: 12000, min: 5200 }),
  cascade: Object.freeze({ base: 16000, min: 8000 }),
  fullscreen_gif: Object.freeze({ base: 20000, min: 11000 }),
  brain_drain: Object.freeze({ base: 15000, min: 7000 }),
});

/** How long a PULSED sustain is held before it is stopped / fades. */
export const PULSE_MS = Object.freeze({
  bubbles: 3400,
  cascade: 3400,
  spiral: 2400,
  pink: 2400,
  brain_drain: 2600,
  corner_gif: 2000,
  fullscreen_gif: 1200,
});

/** Variant pools per pool key (no-repeat-last is the engine's; the ORDER is ours). */
const VARIANTS = Object.freeze({
  flash: Object.freeze(['single', 'double', 'scatter']),
  subliminal: Object.freeze(['whisper', 'centre', 'scatter', 'stamp']),
  bubbles: Object.freeze(['drift', 'rise', 'swarm']),
  spiral: Object.freeze(['spiral']),
  whisper: STINGS,
  pink: Object.freeze(['pink']),
  corner_gif: Object.freeze(['tl', 'tr', 'bl', 'br']),
  cascade: Object.freeze(['light', 'steady', 'downpour']),
  fullscreen_gif: Object.freeze(['full']),
  brain_drain: Object.freeze(['drain']),
});

/** Question templates, by the tier that unlocks them. */
export const TEMPLATES = Object.freeze(['LAST_WORD', 'LAST_EFFECT', 'LAST_STING', 'LAST_TWO']);
export const TEMPLATE_FROM_TIER = Object.freeze({
  LAST_WORD: 1, LAST_EFFECT: 1, LAST_STING: 2, LAST_TWO: 3,
});
/** The fallback walk when the dealt template cannot be instantiated from the
 *  ledger tail (empty word pool, no stings, inaudible audio). Fixed ORDER, so a
 *  fallback is as deterministic as the deal it replaces. */
export const FALLBACK_ORDER = Object.freeze(['LAST_EFFECT', 'LAST_WORD', 'LAST_STING', 'LAST_TWO']);

/* ----------------------------------------------------------------------------
 * SMALL PURE HELPERS
 * -------------------------------------------------------------------------- */
export function clamp01(v) { const n = Number(v); return !Number.isFinite(n) ? 0 : n < 0 ? 0 : n > 1 ? 1 : n; }
export function tierOf(t) { return Math.max(1, Math.min(4, Math.round(Number(t) || 1))); }
function lerp(a, b, f) { return a + (b - a) * f; }
function pickFrom(list, r) {
  if (!list || !list.length) return undefined;
  const f = r < 0 ? 0 : r > 0.999999 ? 0.999999 : r;
  return list[Math.floor(f * list.length)];
}

/** The density multiplier for the `ir_density` setting (unknown -> standard). */
export function densityMultFor(value) {
  const k = String(value == null ? 'standard' : value).trim().toLowerCase();
  const m = PLAYTEST.DENSITY_MULT[k];
  return Number.isFinite(m) ? m : PLAYTEST.DENSITY_MULT.standard;
}

/**
 * THE DENSITY SAWTOOTH. Between stops the band climbs continuously toward the
 * segment's ceiling; each stop knocks it down ONE band (never to the floor) and
 * raises the segment's own floor, so the class ratchets. A missed stop eases
 * the next segment's ceiling by MISS_EASE - the comeback hook, session-only.
 *
 * PURE: index.js hands it the elapsed time and the segment bookkeeping, so the
 * suite can walk the whole curve without a clock.
 *
 * @param {Object} p        the plan (needs densityFloor / densityCeil / rampMs)
 * @param {number} sinceMs  ms since this segment started (a resume, or t=0)
 * @param {number} seg      how many stops have already resolved (0-based segment)
 * @param {boolean} eased   the previous stop was missed -> eased ceiling
 * @returns {number} 0..1 density band
 */
export function densityAt(p, sinceMs, seg, eased) {
  if (!p) return 0;
  const segs = Math.max(1, Number(p.segments) || 1);
  const i = Math.max(0, Math.min(segs - 1, Math.round(Number(seg) || 0)));
  const span = Math.max(0, segs - 1);
  /* the segment's own floor ratchets up across the class (sawtooth teeth rise) */
  const floor = clamp01(lerp(p.densityFloor, Math.max(p.densityFloor, p.densityCeil - PLAYTEST.RELAX_BAND),
    span ? i / span : 1));
  let ceil = p.densityCeil;
  if (eased) ceil *= PLAYTEST.MISS_EASE;
  ceil = clamp01(Math.max(floor, ceil));
  const f = clamp01(Math.max(0, Number(sinceMs) || 0) / Math.max(1, p.rampMs));
  return clamp01(lerp(floor, ceil, f));
}

/** Heat from the class's own ladder (progress + stop streak), capped by tier. */
export function heatFor(p, progress01, streak) {
  if (!p) return 0;
  const s = Math.min(PLAYTEST.HEAT_STREAK_CAP, Math.max(0, Math.round(Number(streak) || 0)));
  const h = PLAYTEST.HEAT_FROM_PROGRESS * clamp01(progress01)
    + PLAYTEST.HEAT_FROM_STREAK * (s / PLAYTEST.HEAT_STREAK_CAP);
  return clamp01(Math.min(p.heatCap, h));
}

/** Heat-scaled cadence for a pool key at a density band, with a seeded jitter. */
export function cadenceMs(key, band, jitter) {
  const c = CADENCE[key];
  if (!c) return Infinity;
  const b = clamp01(band);
  const base = lerp(c.base, c.min, b);
  const j = Number.isFinite(jitter) ? jitter : 0;
  return Math.max(MIN_SEPARATION_MS, Math.round(base * (0.78 + 0.44 * j)));
}

/**
 * THE DEALER. PURE. Given every pool key's next due time (class-clock ms), the
 * moment the LAST emission started and "now", answer which key fires next and
 * exactly when.
 *
 * THE 700ms RULE lives here: the winner is never scheduled inside
 * MIN_SEPARATION_MS of the previous emission - it is pushed out, never dropped,
 * so a busy band thins into a queue instead of a pile-up. Ties resolve on the
 * insertion order of `due`, which is the pool's own order, so the answer is the
 * same on every replay of the same seed.
 *
 * @param {Object} due        {poolKey: dueAtMs}
 * @param {number} lastEmitAt class-clock ms of the previous emission (or -Infinity)
 * @param {number} nowMs      the class clock
 * @returns {{key:string, atMs:number, waitMs:number}|null}
 */
export function nextEmission(due, lastEmitAt, nowMs) {
  if (!due) return null;
  const now = Number.isFinite(nowMs) ? nowMs : 0;
  const last = Number.isFinite(lastEmitAt) ? lastEmitAt : -Infinity;
  let bestKey = null;
  let bestAt = Infinity;
  for (const key of Object.keys(due)) {
    const at = Number(due[key]);
    if (!Number.isFinite(at)) continue;
    if (at < bestAt) { bestAt = at; bestKey = key; }
  }
  if (bestKey == null) return null;
  const floor = Math.max(now, last + MIN_SEPARATION_MS);
  const atMs = Math.max(bestAt, floor);
  return { key: bestKey, atMs, waitMs: Math.max(0, atMs - now) };
}

/* ----------------------------------------------------------------------------
 * THE PLAN
 * -------------------------------------------------------------------------- */

/**
 * Deal the day's vigil.
 *
 * @param {Object} o
 *   seed           the class seed (UTC-dated; unchanged on a retake)
 *   gradeTier      1..4
 *   timeBudgetSec  the real budget (120)
 *   density        the `ir_density` setting value
 *   reduced        reduced motion (drops every plant)
 *   audible        ctx.audioAudible (false drops the whisper from the pool)
 * @returns {Object} the plan
 */
export function buildVigil(o = {}) {
  const seed = String(o.seed == null ? 'instant_recall' : o.seed);
  const tier = tierOf(o.gradeTier);
  const reduced = !!o.reduced;
  const audible = o.audible !== false;
  const budgetMs = Math.max(20000, Math.round((Number(o.timeBudgetSec) || 120) * 1000));
  const roll = makeTaggedRoll(seed + '|ir');

  const windowMs = PLAYTEST.WINDOW_MS[tier];
  const qPer = PLAYTEST.Q_PER_STOP[tier];
  const count = tier === 4
    ? pickFrom(PLAYTEST.STOPS_T4, roll('stop-count'))
    : PLAYTEST.STOPS[tier];

  const densityMult = densityMultFor(o.density);
  const densityCeil = clamp01(PLAYTEST.DENSITY_CEIL[tier] * densityMult);
  const densityFloor = Math.min(densityCeil, PLAYTEST.DENSITY_FLOOR[tier]);

  /* ---- stop times: segmented, jittered, final-stop guaranteed ---------- */
  const finalLo = Math.max(0, budgetMs - PLAYTEST.FINAL_WINDOW_MS);
  const finalHi = Math.max(finalLo, budgetMs - windowMs - PLAYTEST.RESOLVE_PAD_MS);
  const times = [];
  const open = Math.min(PLAYTEST.OPEN_MS[tier], Math.max(0, finalLo - 1000));
  const minGap = PLAYTEST.MIN_GAP_MS[tier];
  const earlier = Math.max(0, count - 1);
  if (earlier > 0) {
    const spanLo = open;
    const spanHi = Math.max(spanLo, finalLo - minGap);
    const segLen = (spanHi - spanLo) / earlier;
    let last = -Infinity;
    for (let i = 0; i < earlier; i++) {
      const segStart = spanLo + segLen * i;
      /* inside the middle 70% of the segment: never flush against a neighbour */
      let at = Math.round(segStart + segLen * (0.15 + 0.70 * roll('stop-at')));
      if (at - last < minGap) at = Math.round(last + minGap);
      at = Math.min(at, Math.round(spanHi));
      times.push(at);
      last = at;
    }
  }
  const finalAt = Math.round(finalLo + (finalHi - finalLo) * roll('stop-final'));
  times.push(finalAt);

  /* ---- the bell: tier 1 all, tier 2 exactly one silent, tier 3+ none --- */
  const announced = times.map(() => tier === 1);
  if (tier === 2) {
    for (let i = 0; i < announced.length; i++) announced[i] = true;
    /* never the FIRST stop: the ritual is taught once, then broken */
    const idx = 1 + Math.floor(roll('nobell') * Math.max(1, announced.length - 1));
    announced[Math.min(announced.length - 1, idx)] = false;
  }

  /* ---- template sequence ---------------------------------------------- */
  const pool = TEMPLATES.filter((k) => TEMPLATE_FROM_TIER[k] <= tier);
  const totalQ = count * qPer;

  const stops = [];
  let dealt = 0;
  let prevTemplate = '';
  let plants = 0;
  let plantedLast = false;
  for (let n = 0; n < count; n++) {
    const questions = [];
    for (let q = 0; q < qPer; q++) {
      /* no-repeat-last across the whole class where the pool allows it */
      const avail = pool.length > 1 ? pool.filter((k) => k !== prevTemplate) : pool.slice();
      const template = pickFrom(avail, roll('tmpl')) || pool[0];
      questions.push({ i: dealt, template, weight: 6000 / windowMs, windowMs });
      prevTemplate = template;
      dealt += 1;
    }

    /* ---- decoy plants: tier 3+, never the first stop, never two in a row,
     * capped per class, disabled entirely under reduced motion (and then
     * scored 1.0 so accessibility never costs a grade). */
    let plant = null;
    const cap = PLAYTEST.PLANT_CAP[tier];
    const chance = PLAYTEST.PLANT_CHANCE[tier];
    const gate = !reduced && n > 0 && plants < cap && !plantedLast;
    const r = roll('plant');
    if (gate && r < chance) {
      const channel = (tier >= 4 && audible && roll('plant-ch') < 0.42) ? 'whisper' : 'subliminal';
      const at = Math.round(windowMs * PLAYTEST.PLANT_AT[plants % PLAYTEST.PLANT_AT.length]);
      plant = { channel, atMs: at, stop: n };
      plants += 1;
      plantedLast = true;
    } else {
      plantedLast = false;
    }

    stops.push({
      n,
      atMs: times[n],
      announced: announced[n],
      windowMs,
      questions,
      plant,
      final: n === count - 1,
    });
  }

  /* ---- the effect pool + each key's dealt ring ------------------------- */
  const poolKeys = poolFor(tier, audible);
  const channels = [];
  for (const key of poolKeys) channels.push(makeChannel(key, roll));

  const plan = {
    seed,
    tier,
    reduced,
    audible,
    budgetMs,
    windowMs,
    qPerStop: qPer,
    stopCount: count,
    segments: count + 1,
    stops,
    pool: poolKeys,
    channels,
    stings: STINGS.slice(),
    templates: pool.slice(),
    plantCount: plants,
    densityMult,
    densityCeil,
    densityFloor,
    rampMs: PLAYTEST.RAMP_MS,
    heatCap: PLAYTEST.HEAT_CAP[tier],
    audioCeil: PLAYTEST.AUDIO_CEIL[tier],
    totalQuestions: totalQ,
    minSeparationMs: MIN_SEPARATION_MS,
  };
  return plan;
}

/** One pool key's dealt ring: 8 jitters + 8 variants, consumed round-robin. */
function makeChannel(key, roll) {
  const jitter = [];
  for (let i = 0; i < 8; i++) jitter.push(roll('jit-' + key));
  const pool = VARIANTS[key] || [''];
  const variants = [];
  let prev = '';
  for (let i = 0; i < 8; i++) {
    const avail = pool.length > 1 ? pool.filter((v) => v !== prev) : pool.slice();
    const v = pickFrom(avail, roll('var-' + key)) || pool[0];
    variants.push(v);
    prev = v;
  }
  const row = POOL_BY_KEY[key];
  return { key, kind: row ? row.kind : '', held: !!(row && row.held), jitter, variants };
}

/* ----------------------------------------------------------------------------
 * TEMPLATE RESOLUTION (pure) - the ledger tail decides what CAN be asked.
 * -------------------------------------------------------------------------- */

/**
 * Can this template be instantiated right now?
 *
 * @param {string} template
 * @param {Object} avail
 *   words       distinct words available as distractors (outside the excluded tail)
 *   hasWord     the tail carries at least one word
 *   hasTwoWords the tail carries at least two words
 *   stings      distinct stings available as distractors
 *   hasSting    the tail carries at least one sting
 *   audible     ctx.audioAudible
 *   effects     distinct REACHABLE pool keys available as distractors
 *   hasEffect   the tail carries at least one pool emission
 */
export function canAsk(template, avail) {
  const a = avail || {};
  switch (template) {
    case 'LAST_WORD': return !!a.hasWord && (a.words | 0) >= 3;
    case 'LAST_TWO': return !!a.hasTwoWords && (a.words | 0) >= 2;
    case 'LAST_EFFECT': return !!a.hasEffect && (a.effects | 0) >= 3;
    case 'LAST_STING': return !!a.hasSting && !!a.audible && (a.stings | 0) >= 3;
    default: return false;
  }
}

/** The dealt template, or the first template down the fixed fallback walk that
 *  the tail can actually answer. Null when the tail can answer nothing at all
 *  (the stop then resumes uncounted - a question is never invented). */
export function resolveTemplate(want, avail, tier) {
  const k = tierOf(tier);
  /* the tier gate outranks the deal: a plan can only ever deal a template its
   * tier unlocks, but this function is also the seam a harness and a future
   * caller reach for, and a LAST_TWO at tier 2 must fall through like any other
   * template the tail cannot serve. */
  if (TEMPLATE_FROM_TIER[want] <= k && canAsk(want, avail)) return want;
  for (const alt of FALLBACK_ORDER) {
    if (alt === want) continue;
    if (TEMPLATE_FROM_TIER[alt] > k) continue;
    if (canAsk(alt, avail)) return alt;
  }
  return null;
}

/* ----------------------------------------------------------------------------
 * THE ASSERTIONS (the suite calls this; index.js logs its failures)
 * -------------------------------------------------------------------------- */
/** @returns {string[]} the list of broken invariants (empty = the plan is legal). */
export function assertPlan(p) {
  const bad = [];
  if (!p || !Array.isArray(p.stops) || !p.stops.length) return ['no stops'];
  const tier = p.tier;
  if (p.stops.length !== p.stopCount) bad.push('stopCount mismatch');
  const wantCount = tier === 4 ? null : PLAYTEST.STOPS[tier];
  if (wantCount != null && p.stopCount !== wantCount) bad.push('tier ' + tier + ' must deal ' + wantCount + ' stops');
  if (tier === 4 && PLAYTEST.STOPS_T4.indexOf(p.stopCount) < 0) bad.push('tier 4 stop count out of band');

  let prev = -Infinity;
  for (const s of p.stops) {
    if (!(s.atMs > prev)) bad.push('stops out of order at ' + s.n);
    if (s.n > 0 && s.atMs - prev < PLAYTEST.MIN_GAP_MS[tier]) bad.push('min gap broken at ' + s.n);
    prev = s.atMs;
    if (s.questions.length !== PLAYTEST.Q_PER_STOP[tier]) bad.push('question count at ' + s.n);
    if (s.windowMs !== PLAYTEST.WINDOW_MS[tier]) bad.push('window at ' + s.n);
  }

  /* THE FINAL-STOP GUARANTEE */
  const last = p.stops[p.stops.length - 1];
  if (last.atMs < p.budgetMs - PLAYTEST.FINAL_WINDOW_MS) bad.push('final stop outside the last 15s');
  if (last.atMs > p.budgetMs - last.windowMs - PLAYTEST.RESOLVE_PAD_MS) bad.push('final stop cannot resolve before the bell');
  if (!last.final) bad.push('final stop not flagged');

  /* the bell ladder */
  const silent = p.stops.filter((s) => !s.announced).length;
  if (tier === 1 && silent !== 0) bad.push('tier 1 must bell every stop');
  if (tier === 2 && silent !== 1) bad.push('tier 2 must drop exactly one bell');
  if (tier >= 3 && silent !== p.stops.length) bad.push('tier 3+ must bell nothing');
  if (tier === 2 && !p.stops[0].announced) bad.push('tier 2 first stop must bell');

  /* plants */
  const planted = p.stops.filter((s) => !!s.plant);
  if (p.reduced && planted.length) bad.push('reduced motion must plant nothing');
  if (tier < 3 && planted.length) bad.push('plants before tier 3');
  if (planted.length > PLAYTEST.PLANT_CAP[tier]) bad.push('plant cap exceeded');
  for (const s of planted) {
    if (s.n === 0) bad.push('plant on the first stop');
    if (s.plant.atMs >= s.windowMs) bad.push('plant outside the answer window');
    if (s.plant.channel !== 'subliminal' && s.plant.channel !== 'whisper') bad.push('plant on a channel that is not a pool key');
    if (p.pool.indexOf(s.plant.channel) < 0) bad.push('plant on a key outside the tier pool');
  }
  for (let i = 1; i < p.stops.length; i++) {
    if (p.stops[i].plant && p.stops[i - 1].plant) bad.push('plants clumped at ' + i);
  }

  /* templates: every tier must be able to ask at least TWO different things */
  const asked = new Set();
  for (const s of p.stops) {
    for (const q of s.questions) {
      if (TEMPLATES.indexOf(q.template) < 0) bad.push('unknown template ' + q.template);
      if (TEMPLATE_FROM_TIER[q.template] > tier) bad.push(q.template + ' above tier ' + tier);
      asked.add(q.template);
    }
  }
  if (p.templates.length < 2) bad.push('tier ' + tier + ' has fewer than 2 templates');

  /* THE POOL: the 4 / 6 / 8 / 10 ladder, minus an inaudible whisper */
  const wantPool = POOL_SIZE[tier] - ((!p.audible && tier >= STING_FROM_TIER) ? 1 : 0);
  if (p.pool.length !== wantPool) bad.push('tier ' + tier + ' pool is ' + p.pool.length + ', want ' + wantPool);
  for (const k of p.pool) {
    if (POOL_KEYS.indexOf(k) < 0) bad.push('pool key outside the vocabulary: ' + k);
    if (!CADENCE[k]) bad.push('pool key with no cadence band: ' + k);
    if (POOL_BY_KEY[k].tier > tier) bad.push(k + ' above tier ' + tier);
  }
  if (p.channels.length !== p.pool.length) bad.push('channel ring count');
  if (!p.audible && p.pool.indexOf('whisper') >= 0) bad.push('inaudible class kept the whisper');

  return bad;
}

export default {
  buildVigil, densityAt, heatFor, cadenceMs, nextEmission, resolveTemplate, canAsk,
  assertPlan, poolFor, PLAYTEST, EFFECT_POOL, POOL_KEYS, MIN_SEPARATION_MS,
};
