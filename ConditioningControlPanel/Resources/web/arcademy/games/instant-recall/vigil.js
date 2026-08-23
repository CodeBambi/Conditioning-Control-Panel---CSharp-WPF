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
 * ---------------------------------------------------------------------------
 * THE VARIETY REWORK (owner ruling 2026-08-23, *"seems to ask me only about the
 * subliminals that played"*). TEN question families, dealt as PERMUTATION
 * ROUNDS rather than a weighted ban: round r is a seeded permutation of the
 * tier's surviving pool, the stops walk round 0 then round 1..., and a round
 * whose first entry equals the last one dealt swaps its first two. So every
 * family lands floor(n/k) or ceil(n/k) times, nothing lands twice in a row, and
 * a tier that unlocks ten families asks all ten in eleven stops - coverage by
 * construction instead of by luck.
 *
 * The pool is DROPPED at plan time for material the class does not have
 * (`templateDrops`): no words, no clips, no spiral set, no wall. A family the
 * plan never deals is honest; a family the plan deals and then falls out of at
 * every stop is the bug this replaced.
 *
 * THE DETERMINISM STATEMENT. The PLAN is seeded (Law V). The WALL's contents
 * are the provider's and the frame governor's (Math.random, by design), so a
 * WALL_* question is DOM-TRUTH read from the freeze snapshot, with the CHOICE
 * among candidates seeded off `|ir-quiz`. A retake replays the same families at
 * the same stops - not the same faces.
 *
 * ---------------------------------------------------------------------------
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
  /** THE PERMUTATION ROUNDS superseded the weighted-ban draw (THE VARIETY
   *  REWORK, 2026-08-23). The deal walks seeded permutations of the tier's
   *  surviving pool, so coverage is structural rather than statistical; the ban
   *  is 1 purely so nothing can land twice across a round boundary and the
   *  assertion helpers keep a number to read. */
  TEMPLATE_NO_REPEAT: 1,
  /** THE TAIL ALLOWANCE. `DISTRACTOR_EXCLUDE` is gone: every LAST_* question
   *  asks for the LAST one, which the 700ms rule makes unique, so an option
   *  that flashed EARLIER is not ambiguous - it is the classic recency-error
   *  decoy, and `isNearMiss()` already captions it. This is how many of a
   *  question's three decoys MAY come out of the same tail. */
  TAIL_DISTRACTORS: Object.freeze({ 1: 0, 2: 1, 3: 1, 4: 2 }),
  /** THE QUIET. No emission may START inside this much of a stop, per channel,
   *  so the last ledger entry is always fully PERCEIVED before the freeze (a
   *  sub word's plateau, a whisper clip's first seconds, a wash's fade-in). */
  PRE_STOP_QUIET_MS: Object.freeze({ default: 600, whisper: 2600, spiral: 1200 }),
  /** THE CUE. A dealt family must be LIKELY instantiable at its stop or the
   *  plan's variety is theatre: the stop's own channel is pulled this far in
   *  at the resume. The answer is still whatever the engine does (Law I). */
  CUE_LEAD_MS: 1200,

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

  /* --- the sub-fade fix (owner: "the text on the sub is pretty faded") ---
   * NOT an intensity raise - the ceiling rule forbids that. The engine's
   * additive `holdMs` stretches the blip's PLATEAU (`ae-sub-blip` holds full
   * alpha over 22%-70% of the duration), and the plate + outline in style.js
   * buy the contrast. Absent the engine option, the word simply holds its own
   * duration, exactly as today. */
  SUB_HOLD_MS: Object.freeze({ 1: 1100, 2: 1000, 3: 900, 4: 800 }),

  /* --- the spiral, as a QUESTION --------------------------------------- */
  /** The wash alpha band a quizzed spiral rides (was a flat 0.10 + 0.22*band).
   *  Still under the engine's own 0.25..0.70 wash ceiling: a spiral a player is
   *  asked to name has to register. */
  SPIRAL_ALPHA: Object.freeze([0.24, 0.52]),
  /** How stale the last spiral may be and still be askable. */
  SPIRAL_RECENT_MS: 25000,
  /** ...and how faint. Below this it was weather, not an event. */
  SPIRAL_MIN_ALPHA: 0.16,
  /** How many of the class's spiral SET are in the emission ring, by tier.
   *  The whole set at every tier (lead ruling): a 2-entry ring walked with
   *  no-repeat-last makes "which one did you just see" always "the other one".
   *  spirals.js clamps this to the set's own length. */
  SPIRAL_RING: Object.freeze({ 1: 4, 2: 4, 3: 4, 4: 4 }),

  /* --- the whisper, as a real whisper ---------------------------------- */
  /** A trigger clip is truncated (with the shell's own fade) at this. */
  WHISPER_CLIP_MAX_MS: 2400,
  /** A PLANTED clip is shorter still - it is a lie, not a lesson. */
  WHISPER_PLANT_MAX_MS: 1600,

  /* --- the wall's planted duplicate (WALL_TWICE) ------------------------ */
  /** The plant must have LANDED this long before the stop it serves. */
  DUP_LEAD_MS: 2500,
  /** ...and the two tiles hold this much past the freeze. */
  DUP_HOLD_PAD_MS: 2000,

  /* --- question weight -------------------------------------------------- */
  /** A card with fewer real choices is worth less. WALL_SEEN is a coin flip
   *  with a preview attached, so it weighs half. */
  OPTION_WEIGHT: Object.freeze({ 2: 0.5, 3: 0.75, 4: 1 }),
  /** Extra window a MEDIA card gets (a preview takes longer to read than a
   *  word). Ships at 0 and is paid for inside `derivedMinGap()`, so raising it
   *  is legal without touching the gap table by hand. */
  PREVIEW_WINDOW_BONUS_MS: 0,

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
  /* min 1000 -> 1400: a held sub word (SUB_HOLD_MS, up to 1100ms) plus its
   * release must clear before the next one starts, or two words overlap and
   * "the LAST word" stops having one answer. assertPlan re-checks the margin. */
  subliminal: Object.freeze({ base: 3200, min: 1400 }),
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

/**
 * QUESTION TEMPLATES, by the tier that unlocks them. TEN families as of THE
 * VARIETY REWORK (owner, 2026-08-23: *"seems to ask me only about the
 * subliminals that played"*) - the dealt variety was always real, it was the
 * RESOLUTION that collapsed, because LAST_EFFECT's old recency exclusion left
 * fewer than three distractors most of the time and every fallback walk landed
 * on LAST_WORD.
 *   LAST_WORD / LAST_EFFECT / WALL_PICK / SPIRAL          tier 1
 *   LAST_STING or HEARD / WALL_SEEN / WALL_TWICE          tier 2
 *   LAST_TWO / WALL_GONE                                  tier 3
 */
export const TEMPLATES = Object.freeze([
  'LAST_WORD', 'LAST_EFFECT', 'WALL_PICK', 'SPIRAL',
  'LAST_STING', 'HEARD', 'WALL_SEEN', 'WALL_TWICE',
  'LAST_TWO', 'WALL_GONE',
]);
export const TEMPLATE_FROM_TIER = Object.freeze({
  LAST_WORD: 1, LAST_EFFECT: 1, WALL_PICK: 1, SPIRAL: 1,
  LAST_STING: 2, HEARD: 2, WALL_SEEN: 2, WALL_TWICE: 2,
  LAST_TWO: 3, WALL_GONE: 3,
});
/** The families whose OPTIONS are media previews rather than text. */
export const MEDIA_TEMPLATES = Object.freeze(['SPIRAL', 'WALL_PICK', 'WALL_TWICE', 'WALL_GONE', 'WALL_SEEN']);
/** THE CUE. Which pool key a dealt family needs material from, if any. CORE
 *  hands this to `seedDues` at the resume before that stop. */
export const CUE_KEY = Object.freeze({
  LAST_WORD: 'subliminal', LAST_TWO: 'subliminal', SPIRAL: 'spiral',
  HEARD: 'whisper', LAST_STING: 'whisper',
});
/** The fallback walk's TIEBREAK when the dealt template cannot be instantiated
 *  from the ledger tail. It no longer decides anything by itself - the walk is
 *  history-aware now (`resolveTemplate`) and this only breaks a tie between two
 *  families that have been asked the same number of times. */
export const FALLBACK_ORDER = Object.freeze([
  'LAST_EFFECT', 'WALL_PICK', 'LAST_WORD', 'SPIRAL', 'HEARD', 'LAST_STING',
  'WALL_TWICE', 'WALL_GONE', 'LAST_TWO', 'WALL_SEEN',
]);
/** How long a channel must be quiet before a stop (THE QUIET). */
export function quietFor(key) {
  const v = PLAYTEST.PRE_STOP_QUIET_MS[key];
  return Number.isFinite(v) ? v : PLAYTEST.PRE_STOP_QUIET_MS.default;
}
/** What one question is worth for its option count (2 -> half a question). */
export function optionWeight(n) {
  const v = PLAYTEST.OPTION_WEIGHT[Math.max(0, Math.round(Number(n) || 0))];
  return Number.isFinite(v) ? v : 1;
}

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
  /* the media families' extra window is paid for HERE, so flipping
   * PREVIEW_WINDOW_BONUS_MS off zero can never quietly break the fresh floor. */
  return PLAYTEST.WINDOW_MS[k] + PLAYTEST.PREVIEW_WINDOW_BONUS_MS
    + PLAYTEST.VERDICT_MS + PLAYTEST.DEAL_BEAT_MS
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
/** A per-120s band entry, scaled to this class's budget and then held inside
 *  STOPS_PER_MIN (on any budget the rate is asserted on): 9 x 1.5 rounds to 14
 *  and 11 x 1.5 to 17, and 17 over 180s is 5.67 a minute, so the rate window
 *  is the last word. A 120s class is untouched by the clamp. Never < 1. */
export function scaleStops(perStandard, budgetMs) {
  const b = Math.max(20000, Math.round(Number(budgetMs) || 120000));
  let n = Math.round((Number(perStandard) || 1) * (b / 120000));
  if (b >= 100000) {
    const lo = Math.ceil(PLAYTEST.STOPS_PER_MIN.lo * b / 60000 - 1e-9);
    const hi = Math.floor(PLAYTEST.STOPS_PER_MIN.hi * b / 60000 + 1e-9);
    n = Math.max(lo, Math.min(hi, n));
  }
  return Math.max(1, n);
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
 * THE CUE (optional 5th argument, THE VARIETY REWORK). When `wantKey` is in
 * `keys` its due is pulled in to `now + CUE_LEAD_MS` BEFORE the two-earliest
 * pull, so it normally becomes one of the 700 / 2400 leads and the family the
 * next stop DEALT has material to be about. It never decides the answer - the
 * ledger still does (Law I) - it only raises the odds the question can be
 * asked at all. Omit it and this function is byte-identical to what it was.
 *
 * @param {function} jitterOf key -> that key's next ring jitter 0..1
 * @param {string} [wantKey]  the pool key the next stop's family wants
 * @returns {Object} {poolKey: dueAtMs}
 */
export function seedDues(keys, nowMs, band, jitterOf, wantKey) {
  const now = Number.isFinite(nowMs) ? nowMs : 0;
  const due = {};
  const order = [];
  for (const key of (keys || [])) {
    const j = jitterOf ? jitterOf(key) : 0.5;
    due[key] = now + Math.round(cadenceMs(key, band, j) * (0.25 + 0.75 * j));
    order.push(key);
  }
  if (wantKey != null && due[wantKey] != null) {
    const cue = now + PLAYTEST.CUE_LEAD_MS;
    if (due[wantKey] > cue) due[wantKey] = cue;
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
 * THE QUIET (optional 4th argument, THE VARIETY REWORK). When `stopAtMs` is a
 * finite number the keys are walked in due order and the FIRST one that can
 * legally fire at least `quietFor(key)` before the stop wins; a key that
 * cannot is skipped, not shoved (the next stop's `seedDealer()` re-seeds every
 * due anyway), and a walk that finds nobody answers null - the wall simply
 * holds its breath into the freeze. That is what makes "the LAST thing" always
 * something the player actually PERCEIVED, instead of a 50ms-old ghost.
 * Omit the argument and this function is byte-identical to what it was.
 *
 * @param {Object} due        {poolKey: dueAtMs}
 * @param {number} lastEmitAt class-clock ms of the previous emission (or -Infinity)
 * @param {number} nowMs      the class clock
 * @param {number} [stopAtMs] the next stop's class-clock time
 * @returns {{key:string, atMs:number, waitMs:number}|null}
 */
export function nextEmission(due, lastEmitAt, nowMs, stopAtMs) {
  if (!due) return null;
  const now = Number.isFinite(nowMs) ? nowMs : 0;
  const last = Number.isFinite(lastEmitAt) ? lastEmitAt : -Infinity;
  const floor = Math.max(now, last + MIN_SEPARATION_MS);
  if (Number.isFinite(stopAtMs)) {
    const keys = [];
    for (const key of Object.keys(due)) if (Number.isFinite(Number(due[key]))) keys.push(key);
    /* ties resolve on Object.keys order (the pool's own), same as the plain
     * path: Array#sort is stable, so an equal pair keeps its insertion order. */
    keys.sort((a, b) => Number(due[a]) - Number(due[b]));
    for (const key of keys) {
      const atMs = Math.max(Number(due[key]), floor);
      if (atMs <= stopAtMs - quietFor(key)) return { key, atMs, waitMs: Math.max(0, atMs - now) };
    }
    return null;
  }
  let bestKey = null;
  let bestAt = Infinity;
  for (const key of Object.keys(due)) {
    const at = Number(due[key]);
    if (!Number.isFinite(at)) continue;
    if (at < bestAt) { bestAt = at; bestKey = key; }
  }
  if (bestKey == null) return null;
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
 *   wordCount      ctx.words.length      (< 4 drops LAST_WORD / LAST_TWO / HEARD)
 *   clipCount      ctx.triggers with audio (0 drops HEARD; > 0 drops LAST_STING)
 *   spiralCount    ctx.spiralPool.length (< 4 drops SPIRAL)
 *   wallOk         montage.snapshot is a function (false drops every WALL_*)
 * @returns {Object} the plan
 */
export function buildVigil(o = {}) {
  const seed = String(o.seed == null ? 'instant_recall' : o.seed);
  const tier = tierOf(o.gradeTier);
  const reduced = !!o.reduced;
  const audible = o.audible !== false;
  const budgetMs = Math.max(20000, Math.round((Number(o.timeBudgetSec) || 120) * 1000));
  const roll = makeTaggedRoll(seed + '|ir');
  /* the four material inputs. A caller that names none of them (the pure
   * harness) gets the conservative read: words are plentiful, there are no
   * clips and no spiral pool, and the wall is present. */
  const wordCount = Number.isFinite(Number(o.wordCount)) ? Math.max(0, Math.round(Number(o.wordCount))) : Infinity;
  const clipCount = Math.max(0, Math.round(Number(o.clipCount) || 0));
  const spiralCount = Math.max(0, Math.round(Number(o.spiralCount) || 0));
  const wallOk = o.wallOk !== false;

  const windowMs = PLAYTEST.WINDOW_MS[tier];
  const qPer = PLAYTEST.Q_PER_STOP[tier];
  const band = PLAYTEST.STOPS_BAND[tier];
  const fit = maxStopsFor(tier, budgetMs);
  /* THE BAND IS A RATE, NOT A COUNT. The table is written per 120s; the shell
   * hands a MEATY class 180s (and a retake whatever the timetable says), and
   * "five rounds a minute" has to hold on every one of them - a 10-stop deal
   * over 180s is 3.3/min, which the field log caught on 2026-08-23. The pick
   * is scaled AFTER the roll, so a 120s plan is byte-identical to before. */
  const count = Math.max(1, Math.min(fit,
    scaleStops(pickFrom(band, roll('stop-count')) || band[0], budgetMs)));

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

  /* ---- template sequence: PLAN-TIME DROPS, then PERMUTATION ROUNDS ------ */
  const drops = templateDrops(tier, { audible, wordCount, clipCount, spiralCount, wallOk });
  let pool = TEMPLATES.filter((k) => TEMPLATE_FROM_TIER[k] <= tier && !drops[k]);
  /* THE DEGENERATE FLOOR. A harness with no words, no spiral pool and no wall
   * can starve the whole table; the class still has to be able to ask TWO
   * things, and LAST_WORD's may-be-empty contract already resumes uncounted at
   * stop time when the tail cannot serve it. */
  let degenerate = false;
  if (pool.length < 2) { pool = ['LAST_EFFECT', 'LAST_WORD']; degenerate = true; }
  const totalQ = count * qPer;

  /* THE ROUNDS. A weighted ban over ten draws out of a four-template pool is a
   * coin flip wearing a rule; a walk of seeded PERMUTATIONS is coverage by
   * construction. Every family lands floor(n/k) or ceil(n/k) times, no family
   * lands twice in a row (a permutation has no internal repeat, and the round
   * boundary swaps its first two entries when it would), and a tier that deals
   * ten families in eleven stops asks every single one of them. */
  const rounds = [];
  const seq = [];
  while (seq.length < totalQ) {
    const perm = pool.slice();
    for (let i = perm.length - 1; i > 0; i--) {
      const j = Math.min(i, Math.floor(roll('tmpl') * (i + 1)));
      const tmp = perm[i]; perm[i] = perm[j]; perm[j] = tmp;
    }
    if (seq.length && perm.length >= 2 && perm[0] === seq[seq.length - 1]) {
      const tmp = perm[0]; perm[0] = perm[1]; perm[1] = tmp;
    }
    rounds.push(perm.slice());
    for (const k of perm) { if (seq.length < totalQ) seq.push(k); }
  }

  const stops = [];
  const ban = templateBan(pool.length);
  let dealt = 0;
  let plants = 0;
  let plantedLast = false;
  for (let n = 0; n < count; n++) {
    const questions = [];
    for (let q = 0; q < qPer; q++) {
      const template = seq[dealt] || pool[0];
      questions.push({ i: dealt, template, weight: 6000 / windowMs, windowMs });
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
  const spiralRing = PLAYTEST.SPIRAL_RING[tier];
  const clipRing = Math.max(1, Math.min(8, clipCount));
  const channels = [];
  for (const key of poolKeys) channels.push(makeChannel(key, roll, { spiralRing, clipRing }));

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
    templateDrops: drops,
    degenerate,
    rounds,
    wordCount: Number.isFinite(wordCount) ? wordCount : null,
    clipCount,
    spiralCount,
    wallOk,
    spiralRing,
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

/**
 * WHICH FAMILIES TONIGHT'S MATERIAL CANNOT SERVE. Pure; the value is the
 * REASON, so the class log can say why a family never appeared instead of the
 * player wondering. A family the plan never deals is honest; a family the plan
 * deals and then always falls back out of is the bug this replaces.
 */
export function templateDrops(tier, o = {}) {
  const drops = {};
  const audible = o.audible !== false;
  const words = Number.isFinite(Number(o.wordCount)) ? Number(o.wordCount) : Infinity;
  const clips = Math.max(0, Math.round(Number(o.clipCount) || 0));
  const spirals = Math.max(0, Math.round(Number(o.spiralCount) || 0));
  const wallOk = o.wallOk !== false;
  if (words < 4) {
    drops.LAST_WORD = 'words<4';
    drops.LAST_TWO = 'words<4';
    drops.HEARD = 'words<4';
  }
  if (!audible) {
    drops.LAST_STING = 'inaudible';
    drops.HEARD = 'inaudible';
  } else {
    /* THE ONE-OF-TWO AUDIO FAMILY. A trigger CLIP is the content, so when the
     * mix carries clips the class asks what was SAID; with no clips it falls
     * back to the synthesised stings and asks which one played. Never both. */
    if (clips > 0) drops.LAST_STING = 'clips>0';
    else if (!drops.HEARD) drops.HEARD = 'clips=0';
  }
  if (spirals < 4) drops.SPIRAL = 'spirals<4';
  if (!wallOk) {
    drops.WALL_PICK = 'no wall';
    drops.WALL_SEEN = 'no wall';
    drops.WALL_TWICE = 'no wall';
    drops.WALL_GONE = 'no wall';
  }
  return drops;
}

/** A no-repeat-last walk over 0..size-1, eight long (the channel's own ring). */
function indexWalk(roll, tag, size) {
  const n = Math.max(1, Math.round(Number(size) || 1));
  const out = [];
  let prev = -1;
  for (let i = 0; i < 8; i++) {
    if (n < 2) { out.push(0); continue; }
    const avail = [];
    for (let v = 0; v < n; v++) if (v !== prev) avail.push(v);
    const v = pickFrom(avail, roll(tag));
    out.push(v);
    prev = v;
  }
  return out;
}

/** One pool key's dealt ring: 8 jitters + 8 variants, consumed round-robin.
 *  The spiral and the whisper carry an extra ring - WHICH spiral of the class's
 *  set, and WHICH trigger clip - because both are now question material and a
 *  ring the seed does not own could not replay. */
function makeChannel(key, roll, o) {
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
  const ch = { key, kind: row ? row.kind : '', held: !!(row && row.held), jitter, variants };
  const opt = o || {};
  if (key === 'spiral') ch.spiralIdx = indexWalk(roll, 'spiral-idx', opt.spiralRing);
  if (key === 'whisper') ch.clipIdx = indexWalk(roll, 'clip-idx', opt.clipRing);
  return ch;
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
 *   poolSize    tonight's pool width (LAST_EFFECT needs four names to offer)
 *   phrases     distinct whispered / trigger phrases available as decoys
 *   hasPhrase   the tail carries at least one whispered PHRASE (a clip)
 *   spiralSet   how many spirals the class's own set holds
 *   hasSpiral   the tail carries a recent, bright enough spiral WITH a url
 *   painted     wall tiles painted and not mid-swap (the freeze snapshot)
 *   dups        urls worn by two or more of those tiles
 *   singles     urls worn by exactly one
 *   unseen      candidate urls the wall has NOT worn since the last resume
 *   seenCoin    WALL_SEEN's seeded yes/no (drawn once per availability read)
 */
export function canAsk(template, avail) {
  const a = avail || {};
  switch (template) {
    case 'LAST_WORD': return !!a.hasWord && (a.words | 0) >= 3;
    case 'LAST_TWO': return !!a.hasTwoWords && (a.words | 0) >= 2;
    /* NO RECENCY EXCLUSION. "The LAST effect" is unique by the 700ms rule, so
     * an effect that fired EARLIER is the recency-error decoy, not an
     * ambiguity - and excluding it was what starved this family at tier 1. */
    case 'LAST_EFFECT': return !!a.hasEffect && (a.poolSize | 0) >= 4;
    case 'LAST_STING': return !!a.hasSting && !!a.audible && (a.stings | 0) >= 3;
    case 'HEARD': return !!a.hasPhrase && !!a.audible && (a.phrases | 0) >= 3;
    case 'SPIRAL': return !!a.hasSpiral && (a.spiralSet | 0) >= 4;
    case 'WALL_PICK': return (a.painted | 0) >= 1 && (a.unseen | 0) >= 3;
    case 'WALL_SEEN': return (a.painted | 0) >= 1 && (!!a.seenCoin || (a.unseen | 0) >= 1);
    case 'WALL_TWICE': return (a.dups | 0) === 1 && (a.singles | 0) >= 3;
    case 'WALL_GONE': return (a.painted | 0) >= 3 && (a.unseen | 0) >= 1;
    default: return false;
  }
}

/**
 * The dealt template, or - when the tail cannot serve it - the family the class
 * has asked LEAST. That history awareness is the whole point: the old walk was
 * a fixed order, so a starved family fell to the same replacement every time
 * and a class that dealt ten different things asked one of them nine times.
 *
 * 1. the dealt family, if its tier allows it and the tail can serve it
 * 2. else every other askable family this tier unlocks, minus the one the LAST
 *    question resolved to (re-admitted if that empties the list)
 * 3. the lowest count in `history`; ties -> asked longest ago (never = -1
 *    wins); ties -> FALLBACK_ORDER position
 * 4. nothing askable -> null (the stop resumes uncounted; a question is never
 *    invented - the may-be-empty contract)
 *
 * @param {string[]} [history] the RESOLVED families asked so far, in order
 */
export function resolveTemplate(want, avail, tier, history) {
  const k = tierOf(tier);
  const hist = Array.isArray(history) ? history : [];
  /* the tier gate outranks the deal: a plan can only ever deal a template its
   * tier unlocks, but this function is also the seam a harness and a future
   * caller reach for, and a LAST_TWO at tier 2 must fall through like any other
   * template the tail cannot serve. */
  if (TEMPLATE_FROM_TIER[want] <= k && canAsk(want, avail)) return want;
  const allowed = (avail && Array.isArray(avail.templates)) ? avail.templates : null;
  const banned = hist.length ? hist[hist.length - 1] : null;
  const base = TEMPLATES.filter((alt) => alt !== want
    && TEMPLATE_FROM_TIER[alt] <= k
    && (!allowed || allowed.indexOf(alt) >= 0)
    && canAsk(alt, avail));
  let cands = base.filter((alt) => alt !== banned);
  if (!cands.length) cands = base;
  if (!cands.length) return null;
  const countOf = (key) => hist.reduce((n, h) => (h === key ? n + 1 : n), 0);
  const lastAt = (key) => hist.lastIndexOf(key);
  let best = null;
  for (const alt of cands) {
    if (!best) { best = alt; continue; }
    const dc = countOf(alt) - countOf(best);
    if (dc < 0) { best = alt; continue; }
    if (dc > 0) continue;
    const dl = lastAt(alt) - lastAt(best);
    if (dl < 0) { best = alt; continue; }
    if (dl > 0) continue;
    if (FALLBACK_ORDER.indexOf(alt) < FALLBACK_ORDER.indexOf(best)) best = alt;
  }
  return best;
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
  const band = PLAYTEST.STOPS_BAND[tier].map((n) => scaleStops(n, p.budgetMs));
  const fit = maxStopsFor(tier, p.budgetMs);
  const bandLo = Math.min(fit, band[0]);
  const bandHi = Math.min(fit, band[band.length - 1]);
  if (p.stopCount < bandLo || p.stopCount > bandHi) {
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

  /* TEMPLATES: the permutation rounds make coverage structural, so the
   * assertions are about the ROUNDS now, not about a ban window. */
  const asked = new Set();
  const seq = [];
  for (const s of p.stops) {
    for (const q of s.questions) {
      if (TEMPLATES.indexOf(q.template) < 0) bad.push('unknown template ' + q.template);
      if (TEMPLATE_FROM_TIER[q.template] > tier) bad.push(q.template + ' above tier ' + tier);
      if (p.templates.indexOf(q.template) < 0) bad.push(q.template + ' dealt outside the surviving pool');
      asked.add(q.template);
      seq.push(q.template);
    }
  }
  if (p.templates.length < 2) bad.push('tier ' + tier + ' has fewer than 2 templates');
  for (let i = 1; i < seq.length; i++) {
    if (seq[i] === seq[i - 1]) bad.push('template ' + seq[i] + ' dealt twice in a row at ' + i);
  }
  /* every family gets floor(n/k) or ceil(n/k) of the class - the rounds' whole
   * reason for existing. A pool forced to the degenerate floor is exempt from
   * the count (it is not the tier's pool). */
  const k2 = p.templates.length;
  const lo = Math.floor(seq.length / k2);
  const hi = Math.ceil(seq.length / k2);
  const counts = {};
  for (const key of p.templates) counts[key] = 0;
  for (const key of seq) counts[key] = (counts[key] || 0) + 1;
  for (const key of p.templates) {
    if (counts[key] < lo || counts[key] > hi) {
      bad.push('template ' + key + ' dealt ' + counts[key] + ' times, want ' + lo + '-' + hi);
    }
  }
  if (seq.length >= 6 && asked.size < Math.min(2, p.templates.length)) {
    bad.push('a whole class asked one template');
  }

  /* THE DROPS ARE HONEST. A family whose material does not exist is never
   * dealt - it is not "dealt and quietly replaced", which is the bug the
   * variety rework existed to kill. The DEGENERATE FLOOR is exempt on purpose:
   * a harness with nothing at all still has to be able to ask two things, and
   * LAST_WORD's may-be-empty contract resumes it uncounted at stop time. */
  const has = (key) => !p.degenerate && p.templates.indexOf(key) >= 0;
  if (!p.audible && (has('LAST_STING') || has('HEARD'))) bad.push('an inaudible class kept an audio family');
  if (p.audible && p.clipCount > 0 && has('LAST_STING')) bad.push('LAST_STING dealt while the mix carries clips');
  if (p.audible && p.clipCount === 0 && has('HEARD')) bad.push('HEARD dealt with no clip to hear');
  if ((p.spiralCount | 0) < 4 && has('SPIRAL')) bad.push('SPIRAL dealt with fewer than four spirals');
  if (!p.wallOk && (has('WALL_PICK') || has('WALL_SEEN') || has('WALL_TWICE') || has('WALL_GONE'))) {
    bad.push('a WALL family dealt with no wall');
  }
  if (p.wordCount != null && p.wordCount < 4
    && (has('LAST_WORD') || has('LAST_TWO') || has('HEARD'))) {
    bad.push('a word family dealt on fewer than four words');
  }

  /* the spiral ring walks without repeating itself, and never off its end */
  const spiralCh = p.channels.find((c) => c.key === 'spiral');
  if (spiralCh && Array.isArray(spiralCh.spiralIdx)) {
    const ring = PLAYTEST.SPIRAL_RING[tier];
    for (let i = 0; i < spiralCh.spiralIdx.length; i++) {
      if (!(spiralCh.spiralIdx[i] >= 0 && spiralCh.spiralIdx[i] < ring)) bad.push('spiralIdx off the ring at ' + i);
      if (i > 0 && ring > 1 && spiralCh.spiralIdx[i] === spiralCh.spiralIdx[i - 1]) {
        bad.push('spiralIdx repeats itself at ' + i);
      }
    }
  } else if (p.pool.indexOf('spiral') >= 0) {
    bad.push('the spiral channel deals no ring');
  }
  /* and a held sub word always clears before the next one can start */
  if (CADENCE.subliminal.min < PLAYTEST.SUB_HOLD_MS[4] + 300) {
    bad.push('the subliminal cadence floor cannot hold a held word');
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
  templateBan, seedDues, quietFor, optionWeight, templateDrops,
  PLAYTEST, EFFECT_POOL, POOL_KEYS, MIN_SEPARATION_MS,
  TEMPLATES, TEMPLATE_FROM_TIER, MEDIA_TEMPLATES, CUE_KEY, FALLBACK_ORDER,
};
