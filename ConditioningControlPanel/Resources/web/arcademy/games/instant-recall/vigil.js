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
 * THE CADENCE (owner ruling 2026-08-23, "about 5 rounds per minute").
 * A round IS a stop and every stop asks exactly ONE question, at every tier.
 * A 120s class deals 9-11 stops - 4.5 to 5.5 a minute - and the TIER decides
 * what is asked and how long you get, never how often you are asked.
 *
 * THE GAP IS DERIVED, NOT TASTED. `MIN_GAP_MS[tier]` is exactly
 *     windowMs + VERDICT_MS + DEAL_BEAT_MS + FRESH_MS + SCHEDULE_SLOP_MS
 * so that the WORST case freeze (the player never answers and the window times
 * out) still leaves FRESH_MS of live montage before the next one - a question
 * must always have something new to be about, and re-asking the same tail entry
 * twice would be the one unforgivable bug in a recall class. `derivedMinGap()`
 * recomputes it and `assertPlan()` fails if the table ever drifts off it.
 *   tier 1/2  6000 + 1100 + 240 + 4000 + 400 = 11740ms
 *   tier 3    5000 + ...                     = 10740ms
 *   tier 4    4000 + ...                     =  9740ms
 * Budget check, 120s: at the MEAN commit (half a window) a tier-1 class spends
 * ~43s frozen and ~77s on the wall (64% montage); tier 4 ~37s / ~83s (69%). At
 * the degenerate worst case - every single question blanked - the split
 * inverts to ~39% montage, which is the "answered nothing" run and is graded
 * as one. That is the price of five rounds a minute against a 6s window; the
 * only lever left would be a shorter window, and the window is the tier's
 * contract.
 *
 * TIER LADDER (dossier; dials first, classic difficulty second)
 *   1  4 effects in the pool at low dials; 9-10 stops, 6s, EVERY stop belled.
 *   2  6 effects (the whisper and the pink filter enter); 9-10 stops, 6s,
 *      exactly ONE unannounced (SYNTHESIS #2 taste of the twist).
 *   3  8 effects (the corner GIF and the cascade enter); ALL stops unannounced;
 *      DECOY PLANTS unlock; LAST_TWO enters; 10-11 stops, 5s.
 *   4  all 10 (the fullscreen GIF and Brain Drain enter), every dial at the
 *      global ceiling; 10-11 stops, 4s.
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
  /** The verdict + truth replay after a commit, before the vigil resumes.
   *  HALVED for the five-a-minute cadence (was 2200 / 1100): at ten stops a
   *  class spends this beat ten times, and 2.2s of ribbon between every
   *  question read as the class pausing to admire itself. The ribbon itself is
   *  unchanged - only how long it is held. */
  VERDICT_MS: 1100,
  VERDICT_MS_REDUCED: 700,
  /** The beat between the freeze landing and the first question rendering. It
   *  is part of the freeze, so the gap derivation has to pay for it. */
  DEAL_BEAT_MS: 240,
  /** The bell warning at the end of the class (HUD goes gold). */
  BELL_WARN_SEC: 20,

  /* --- the stop schedule ---------------------------------------------- */
  /** THE FINAL-STOP GUARANTEE: the last stop lands inside this tail window. */
  FINAL_WINDOW_MS: 15000,
  /** ...and no later than (budget - window - this), so it always resolves. */
  RESOLVE_PAD_MS: 1200,
  /** No stop before the vigil has had time to be a vigil. Shorter than it was
   *  (16/14/12/11s): at five stops a minute the opening cannot be a sixth of
   *  the class, and the slack it used to hold is redistributed as jitter. */
  OPEN_MS: Object.freeze({ 1: 6000, 2: 6000, 3: 5500, 4: 5000 }),
  /** THE FRESH-MONTAGE FLOOR. However long the freeze actually ran, this much
   *  live wall always follows the resume before the next stop can land. */
  FRESH_MS: 4000,
  /** Timer granularity + the clock tick, paid into every gap so the floor holds
   *  against a 200ms clock instead of only against arithmetic. */
  SCHEDULE_SLOP_MS: 400,
  /** Minimum gap between two stops. DERIVED - see derivedMinGap(); the table is
   *  the flat form the dealer and the suite read, and assertPlan() fails if the
   *  two ever disagree. */
  MIN_GAP_MS: Object.freeze({ 1: 11740, 2: 11740, 3: 10740, 4: 9740 }),
  /** The answer window, by tier (contract: 6s / 6s / 5s / 4s). */
  WINDOW_MS: Object.freeze({ 1: 6000, 2: 6000, 3: 5000, 4: 4000 }),
  /** Stops per class, seeded from the tier's band. Every band sits inside
   *  9-11 per 120s (4.5-5.5 a minute); tiers 1-2 top out one lower purely
   *  because a 6s window costs 2s more clock per stop than a 4s one. */
  STOPS_BAND: Object.freeze({
    1: Object.freeze([9, 10]),
    2: Object.freeze([9, 10]),
    3: Object.freeze([10, 11]),
    4: Object.freeze([10, 11]),
  }),
  /** The rate the whole schedule exists to hit, stops per minute (asserted). */
  STOPS_PER_MIN: Object.freeze({ lo: 4.4, hi: 5.6 }),
  /** Questions per stop. ONE, at every tier: a round is a stop, and tier 4's
   *  old double question would have cost a second window + verdict on every
   *  one of ten stops. Tier still moves the window and the template pool. */
  Q_PER_STOP: Object.freeze({ 1: 1, 2: 1, 3: 1, 4: 1 }),
  /** The dealer's first two dues after a resume are pulled in to these, so a
   *  resumed wall always writes at least TWO fresh ledger entries inside
   *  FRESH_MS however cold the band is (the second half of "something new to
   *  ask about"). Both sit clear of MIN_SEPARATION_MS. */
  RESUME_LEAD_MS: Object.freeze([700, 2400]),

  /* --- question templates --------------------------------------------- */
  /** How many recently-dealt templates the next draw refuses, when the tier's
   *  pool is wide enough to afford it. The ban NARROWS rather than dying
   *  (the timetable's relaxation ladder, web CLAUDE.md trap 5):
   *  `min(TEMPLATE_NO_REPEAT, max(1, pool.length - 2))`, so a 2-template tier
   *  alternates, a 3- or 4-template tier always keeps two live choices, and no
   *  template can EVER land twice in a row - let alone four times. */
  TEMPLATE_NO_REPEAT: 2,
  /** Distractors are never drawn from the last N ledger entries of the family:
   *  a distractor that also just happened would make the answer ambiguous. */
  DISTRACTOR_EXCLUDE: 5,

  /* --- decoy plants (tier 3+, SetPiece-gated) -------------------------- */
  /* Caps up, chance down, for the same ~1 plant per 3 stops the 4-stop class
   * had: with ten stops the old cap bound after the third one and every plant
   * clustered in the first half of the class. */
  PLANT_CAP: Object.freeze({ 1: 0, 2: 0, 3: 3, 4: 4 }),
  PLANT_CHANCE: Object.freeze({ 1: 0, 2: 0, 3: 0.34, 4: 0.44 }),
  /** Where in the answer window the plant fires (fraction of the window). */
  PLANT_AT: Object.freeze([0.34, 0.46, 0.38, 0.52]),

  /* --- density + heat --------------------------------------------------- */
  /** `ir_density` scales the CEILING (never the floor, never the stop schedule). */
  DENSITY_MULT: Object.freeze({ calm: 0.72, standard: 1, dense: 1.28 }),
  DENSITY_CEIL: Object.freeze({ 1: 0.45, 2: 0.62, 3: 0.82, 4: 1 }),
  DENSITY_FLOOR: Object.freeze({ 1: 0.12, 2: 0.16, 3: 0.20, 4: 0.26 }),
  /** How long a segment takes to climb from its floor to its ceiling. A
   *  SEGMENT IS NOW THE FRESH MONTAGE BETWEEN TWO STOPS - 4s at the worst-case
   *  floor, ~7s at the mean commit - not the 25-45s stretch a 2-4 stop class
   *  had, so 26000 left tiers 3-4 climbing to barely 40% of their band and the
   *  ceiling was unreachable. At 8000 a segment is ~50% up at the floor and
   *  saturated by the mean commit, which is what keeps the sawtooth a sawtooth
   *  instead of one slow ramp. RELAX_BAND is unchanged: a resume still drops
   *  exactly ONE band and the segment's own floor still ratchets, so it never
   *  falls back to the class floor. */
  RAMP_MS: 8000,
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

/**
 * THE GAP, DERIVED. A stop's freeze is at worst
 *   DEAL_BEAT_MS + windowMs (a blanked question) + VERDICT_MS (the ribbon),
 * so the next stop may not be scheduled inside that plus FRESH_MS of live wall
 * plus the clock's own granularity. This is the ONE number the whole cadence
 * rests on: shorten the verdict and every tier gets denser for free.
 */
export function derivedMinGap(tier) {
  const k = tierOf(tier);
  return PLAYTEST.WINDOW_MS[k] + PLAYTEST.VERDICT_MS + PLAYTEST.DEAL_BEAT_MS
    + PLAYTEST.FRESH_MS + PLAYTEST.SCHEDULE_SLOP_MS;
}
/** The flat table the dealer reads, falling back to the derivation. */
export function minGapFor(tier) {
  const k = tierOf(tier);
  const t = PLAYTEST.MIN_GAP_MS[k];
  return Number.isFinite(t) ? t : derivedMinGap(k);
}
/** The opening silence, clamped so it can never eat the final window. */
export function openFor(tier, budgetMs) {
  const k = tierOf(tier);
  const b = Math.max(20000, Math.round(Number(budgetMs) || 120000));
  return Math.min(PLAYTEST.OPEN_MS[k],
    Math.max(0, b - PLAYTEST.FINAL_WINDOW_MS - minGapFor(k)));
}
/**
 * How many stops this tier can legally fit in this budget: the opening, then
 * one min gap per stop after the first, with the last one still resolving
 * before the bell. The dealt count is clamped to it, so a shorter harness
 * budget thins the class instead of dealing an illegal schedule.
 */
export function maxStopsFor(tier, budgetMs) {
  const k = tierOf(tier);
  const b = Math.max(20000, Math.round(Number(budgetMs) || 120000));
  const gap = minGapFor(k);
  const hi = Math.max(0, b - PLAYTEST.WINDOW_MS[k] - PLAYTEST.RESOLVE_PAD_MS);
  const open = openFor(k, b);
  return Math.max(1, 1 + Math.floor((hi - open) / gap));
}
/** Stops per minute, the number the owner actually asked for. */
export function stopsPerMinute(count, budgetMs) {
  const b = Math.max(1, Number(budgetMs) || 120000);
  return (Math.max(0, Number(count) || 0) * 60000) / b;
}
/**
 * THE TEMPLATE BAN WINDOW. Never wider than the pool can pay for: a 2-template
 * tier alternates (ban 1), a 3- or 4-template tier always keeps TWO live
 * choices, and nothing ever bans the whole pool. Same narrowing discipline as
 * the timetable's no-repeat relaxation (web CLAUDE.md trap 5).
 */
export function templateBan(poolSize) {
  const n = Math.max(1, Math.round(Number(poolSize) || 1));
  if (n < 2) return 0;
  return Math.min(PLAYTEST.TEMPLATE_NO_REPEAT, Math.max(1, n - 2));
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
 * THE FRESH-TAIL SEED. PURE. Every pool key's first due after a resume (or at
 * the top of the class): a FRACTION of its own cadence, so the wall does not go
 * quiet for a whole period every time a stop resolves - and then the two
 * earliest are pulled in to `RESUME_LEAD_MS`.
 *
 * That pull is the other half of the min-gap promise. The gap guarantees
 * FRESH_MS of live wall before the next freeze; this guarantees the wall SAID
 * something in it. Without it a cold tier-1 band (subliminal at ~3s, first due
 * a fraction of that) could carry a single entry through the whole fresh
 * window and the next question would be about the tail the last one already
 * asked about - the one thing a recall class may never do.
 *
 * `nextEmission`'s 700ms rule still arbitrates afterwards, so pulling two dues
 * together can crowd the queue but can never stack two starts.
 *
 * @param {string[]} keys     the tier's pool, in pool order
 * @param {number} nowMs      the class clock
 * @param {number} band       the density band 0..1
 * @param {function} jitterOf key -> that key's next ring jitter 0..1
 * @returns {Object} {poolKey: dueAtMs}
 */
export function seedDues(keys, nowMs, band, jitterOf) {
  const now = Number.isFinite(nowMs) ? nowMs : 0;
  const due = {};
  const order = [];
  for (const key of (keys || [])) {
    const j = jitterOf ? jitterOf(key) : 0.5;
    due[key] = now + Math.round(cadenceMs(key, band, j) * (0.25 + 0.75 * j));
    order.push(key);
  }
  order.sort((a, b) => due[a] - due[b]);
  const leads = PLAYTEST.RESUME_LEAD_MS;
  for (let k = 0; k < leads.length && k < order.length; k++) {
    const cap = now + leads[k];
    if (due[order[k]] > cap) due[order[k]] = cap;
  }
  return due;
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
  const band = PLAYTEST.STOPS_BAND[tier];
  const fit = maxStopsFor(tier, budgetMs);
  const count = Math.max(1, Math.min(fit, pickFrom(band, roll('stop-count')) || band[0]));

  const densityMult = densityMultFor(o.density);
  const densityCeil = clamp01(PLAYTEST.DENSITY_CEIL[tier] * densityMult);
  const densityFloor = Math.min(densityCeil, PLAYTEST.DENSITY_FLOOR[tier]);

  /* ---- stop times: min gap + a seeded share of the slack ----------------
   * At two stops a class the old segmentation had whole minutes of room and
   * could jitter inside a segment. At ten it does not: the gaps ARE the class.
   * So the schedule is built the other way round - every gap is exactly
   * `minGap`, and whatever the budget has left over is shared out across the
   * (count) slots as seeded weights. The floor is therefore structural rather
   * than a check-and-shove, and what jitter there is is real jitter rather
   * than the residue of a clamp. */
  const minGap = minGapFor(tier);
  const open = openFor(tier, budgetMs);
  const earlier = Math.max(0, count - 1);
  const finalHi = Math.max(0, budgetMs - windowMs - PLAYTEST.RESOLVE_PAD_MS);
  /* the final stop may not land before the earlier ones can legally fit; the
   * count was already clamped to `fit`, so this never climbs past finalHi. */
  const finalLo = Math.min(finalHi,
    Math.max(Math.max(0, budgetMs - PLAYTEST.FINAL_WINDOW_MS), open + earlier * minGap));
  const finalAt = Math.round(finalLo + (finalHi - finalLo) * roll('stop-final'));
  const times = [];
  if (earlier > 0) {
    const slack = Math.max(0, (finalAt - minGap) - open - (earlier - 1) * minGap);
    const w = [];
    let wSum = 0;
    for (let i = 0; i < earlier; i++) { const x = 0.25 + roll('stop-slack'); w.push(x); wSum += x; }
    let at = open;
    for (let i = 0; i < earlier; i++) {
      if (i > 0) at += minGap;
      at += Math.round(slack * (w[i] / wSum));
      /* rounding can only ever push a share a millisecond or two long; the cap
       * is monotone (cap_i = cap_(i-1) + minGap) so clamping to it can never
       * break the gap behind it. */
      at = Math.min(at, finalAt - (earlier - i) * minGap);
      times.push(Math.round(at));
    }
  }
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
  const ban = templateBan(pool.length);
  const recent = [];
  let dealt = 0;
  let plants = 0;
  let plantedLast = false;
  for (let n = 0; n < count; n++) {
    const questions = [];
    for (let q = 0; q < qPer; q++) {
      /* NO-REPEAT, NARROWING. Ten draws out of a 2-4 template pool used to run
       * on no-repeat-LAST alone, which is fine for four stops and reads as a
       * coin flip over ten. The ban walks 2 -> 1 -> 0 with the pool's width so
       * it can never starve, and at every tier at least one template is always
       * refused: the same question can never land twice in a row. */
      let avail = [];
      for (let w = Math.min(ban, recent.length); w >= 0; w--) {
        const no = recent.slice(0, w);
        avail = pool.filter((k) => no.indexOf(k) < 0);
        if (avail.length) break;
      }
      if (!avail.length) avail = pool.slice();
      const template = pickFrom(avail, roll('tmpl')) || pool[0];
      questions.push({ i: dealt, template, weight: 6000 / windowMs, windowMs });
      recent.unshift(template);
      if (recent.length > PLAYTEST.TEMPLATE_NO_REPEAT) recent.pop();
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
    templateBan: ban,
    minGapMs: minGap,
    openMs: open,
    stopsPerMin: stopsPerMinute(count, budgetMs),
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
  /* THE CADENCE. The band is the deal; the fit clamp is the only thing allowed
   * to take a class below it, and only on a budget shorter than 120s. */
  const band = PLAYTEST.STOPS_BAND[tier];
  const fit = maxStopsFor(tier, p.budgetMs);
  if (band.indexOf(p.stopCount) < 0 && p.stopCount !== Math.min(fit, band[0])) {
    bad.push('tier ' + tier + ' stop count out of band: ' + p.stopCount);
  }
  if (p.stopCount > fit) bad.push('tier ' + tier + ' dealt more stops than the budget fits');
  const rate = stopsPerMinute(p.stopCount, p.budgetMs);
  if (p.budgetMs >= 100000 && (rate < PLAYTEST.STOPS_PER_MIN.lo || rate > PLAYTEST.STOPS_PER_MIN.hi)) {
    bad.push('stops per minute out of band: ' + rate.toFixed(2));
  }
  /* THE GAP TABLE MAY NOT DRIFT OFF ITS DERIVATION. */
  if (PLAYTEST.MIN_GAP_MS[tier] !== derivedMinGap(tier)) {
    bad.push('MIN_GAP_MS[' + tier + '] is not window + verdict + deal + fresh + slop');
  }

  let prev = -Infinity;
  for (const s of p.stops) {
    if (!(s.atMs > prev)) bad.push('stops out of order at ' + s.n);
    if (s.n > 0 && s.atMs - prev < minGapFor(tier)) bad.push('min gap broken at ' + s.n);
    prev = s.atMs;
    if (s.questions.length !== PLAYTEST.Q_PER_STOP[tier]) bad.push('question count at ' + s.n);
    if (s.windowMs !== PLAYTEST.WINDOW_MS[tier]) bad.push('window at ' + s.n);
  }
  if (p.stops[0].atMs < openFor(tier, p.budgetMs)) bad.push('first stop inside the opening');

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

  /* templates: every tier must be able to ask at least TWO different things,
   * and the draw may never repeat itself back to back (which is also what
   * makes "four in a row" unreachable by construction). */
  const asked = new Set();
  const seq = [];
  for (const s of p.stops) {
    for (const q of s.questions) {
      if (TEMPLATES.indexOf(q.template) < 0) bad.push('unknown template ' + q.template);
      if (TEMPLATE_FROM_TIER[q.template] > tier) bad.push(q.template + ' above tier ' + tier);
      asked.add(q.template);
      seq.push(q.template);
    }
  }
  if (p.templates.length < 2) bad.push('tier ' + tier + ' has fewer than 2 templates');
  const ban = templateBan(p.templates.length);
  for (let i = 1; i < seq.length; i++) {
    const back = seq.slice(Math.max(0, i - ban), i);
    if (back.indexOf(seq[i]) >= 0 && p.templates.length > ban) {
      bad.push('template ' + seq[i] + ' repeated inside the ban window at ' + i);
    }
  }
  if (seq.length >= 6 && asked.size < Math.min(2, p.templates.length)) {
    bad.push('a whole class asked one template');
  }

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
  assertPlan, poolFor, derivedMinGap, minGapFor, openFor, maxStopsFor, stopsPerMinute,
  templateBan, seedDues, PLAYTEST, EFFECT_POOL, POOL_KEYS, MIN_SEPARATION_MS,
};
