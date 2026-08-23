/* ============================================================================
 * games/instant-recall/vigil.js - THE DAY'S VIGIL SCRIPT. PURE.
 *
 * One class = one continuous 120s montage with unpredictable freeze-and-quiz
 * stops. Everything about the SHOW is dealt here, off the class seed, before a
 * single frame renders: when it stops, whether a bell warns you, what the stage
 * is doing, how thick the stream gets, which trigger channels are live and on
 * what cadence, which template each question instantiates from, and where the
 * decoy plants sit. Law V: seed -> plan -> events, never live rng. A retake
 * replays the identical vigil.
 *
 * NOTHING HERE TOUCHES THE DOM, THE ENGINE OR THE CLOCK. index.js walks this
 * plan; montage.js renders it; grade.js scores what actually happened. The one
 * thing this file may NOT decide is the ANSWER to a question - that comes from
 * the ledger tail at stop time, because the ledger is the truth (Law I) and a
 * pre-dealt answer would be a lie waiting for a dropped frame.
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
 *   1  rows only; 2 trigger channels at low dials; 2 stops, 1 question, 6s,
 *      EVERY stop belled.
 *   2  rows + mosaic; 3 channels; wash tints enter; stings enter; 3 stops,
 *      1 question, 6s, exactly ONE unannounced (SYNTHESIS #2 taste of the twist).
 *   3  swirl enters; 4 channels near caps; gif_rain over flash_burst; ALL stops
 *      unannounced; DECOY PLANTS unlock; LAST_TWO enters; 4 stops, 5s.
 *   4  every dial at the global ceiling; crt/chroma dressing; 3-4 stops, 2
 *      questions each, 4s, MODE spice at <= 10% of the class's question weight.
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
  /** MODE is tier-4 spice: at most ONE per class, dealt on this chance, and
   *  down-weighted so its realised share of the class's question weight can
   *  never exceed the dossier's 10% ceiling (the suite asserts the realised
   *  share over 300 seeds). */
  MODE_CHANCE: 0.55,
  MODE_WEIGHT: 0.5,
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

/** The three stage layouts, in the order they unlock. */
export const LAYOUTS = Object.freeze(['rows', 'mosaic', 'swirl']);

/** Layout pool by tier (reduced motion drops swirl - dossier Portability). */
export function layoutPool(tier, reduced) {
  const k = tierOf(tier);
  const all = k >= 3 ? ['rows', 'mosaic', 'swirl'] : k >= 2 ? ['rows', 'mosaic'] : ['rows'];
  return reduced ? all.filter((x) => x !== 'swirl') : all;
}

/**
 * TRIGGER CHANNELS - the emission channels ARE the game.
 * These are the channels that write ledger entries a question can be asked
 * about. `DRESSING` is the per-tier atmosphere that also writes (it is a real
 * effect and LAST_EFFECT may name it) but is held rather than pulsed.
 */
export const TRIGGER_CHANNELS = Object.freeze({
  1: Object.freeze(['sub_flash', 'bubble_field']),
  2: Object.freeze(['sub_flash', 'bubble_field', 'glitch_swap']),
  3: Object.freeze(['sub_flash', 'bubble_field', 'glitch_swap', 'flash_burst']),
  4: Object.freeze(['sub_flash', 'bubble_field', 'glitch_swap', 'flash_burst', 'gif_burst']),
});
/*
 * Tier 4's dressing degrades the montage's legibility on purpose: the wash
 * tints and the rain from tier 3, plus `crt` scanline/chroma. (`crt` was
 * briefly absent from this class's pinned `effectsConsumed` - an owner ruling
 * added it, so the dossier's tier-4 line is honoured as written. The
 * `ambient_field` grain stays a STREAK beat rather than a dressing channel:
 * index.js lays it over the resumed stream at a 2-stop run, so the golden
 * grain means something rather than just being on.)
 */
export const DRESSING = Object.freeze({
  1: Object.freeze([]),
  2: Object.freeze(['wash']),
  3: Object.freeze(['wash', 'gif_rain']),
  4: Object.freeze(['wash', 'gif_rain', 'crt']),
});
/** Stings (audio_trigger) enter at tier 2 - LAST_STING is gated on them. */
export const STING_FROM_TIER = 2;
/** The sting vocabulary: shell/audio.js SOUNDS names with an `ir_sting_*` row. */
export const STINGS = Object.freeze(['blip', 'sting', 'pop', 'bump', 'glitch']);

/** Every channel LAST_EFFECT may name. Every one of them is in this class's
 *  manifest AND actually reachable at some tier - an option that can never fire
 *  is not a distractor, it is a freebie. (`row_drift` is the rows layout's own;
 *  `ambient_field` is the streak grain; `crt` is tier 4's dressing.) */
export const EFFECT_VOCAB = Object.freeze([
  'bubble_field', 'wash', 'glitch_swap', 'gif_rain', 'flash_burst', 'gif_burst',
  'ambient_field', 'crt', 'row_drift',
]);

/** Per-channel cadence band: base (cold) -> min (at full density). */
export const CADENCE = Object.freeze({
  sub_flash: Object.freeze({ base: 2600, min: 360 }),
  bubble_field: Object.freeze({ base: 9000, min: 3200 }),
  glitch_swap: Object.freeze({ base: 6000, min: 1800 }),
  flash_burst: Object.freeze({ base: 11000, min: 4200 }),
  gif_burst: Object.freeze({ base: 13000, min: 5200 }),
  audio_trigger: Object.freeze({ base: 8000, min: 3000 }),
  wash: Object.freeze({ base: 12000, min: 5200 }),
  gif_rain: Object.freeze({ base: 16000, min: 9000 }),
  ambient_field: Object.freeze({ base: 15000, min: 8000 }),
  crt: Object.freeze({ base: 18000, min: 11000 }),
});

/** Variant pools per channel (no-repeat-last is the engine's; the ORDER is ours). */
const VARIANTS = Object.freeze({
  sub_flash: Object.freeze(['whisper', 'centre', 'scatter', 'stamp']),
  bubble_field: Object.freeze(['drift', 'rise', 'swarm']),
  glitch_swap: Object.freeze(['crossfade', 'rgbsplit', 'vhsroll', 'datamosh']),
  flash_burst: Object.freeze(['single', 'scatter', 'double']),
  gif_burst: Object.freeze(['single', 'scatter', 'double']),
  wash: Object.freeze(['pink', 'sublim', 'drain']),
  gif_rain: Object.freeze(['light', 'steady', 'downpour']),
  ambient_field: Object.freeze(['motes', 'specks', 'ash', 'glints']),
  crt: Object.freeze(['scanline', 'chroma', 'bloom']),
  audio_trigger: STINGS,
});

/** Question templates, by the tier that unlocks them. */
export const TEMPLATES = Object.freeze(['LAST_WORD', 'LAST_EFFECT', 'LAST_STING', 'LAST_TWO', 'MODE']);
export const TEMPLATE_FROM_TIER = Object.freeze({
  LAST_WORD: 1, LAST_EFFECT: 1, LAST_STING: 2, LAST_TWO: 3, MODE: 4,
});
/** The fallback walk when the dealt template cannot be instantiated from the
 *  ledger tail (empty word pool, no stings, inaudible audio, single layout).
 *  Fixed ORDER, so a fallback is as deterministic as the deal it replaces. */
export const FALLBACK_ORDER = Object.freeze(['LAST_WORD', 'LAST_EFFECT', 'LAST_STING', 'LAST_TWO', 'MODE']);

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

/** Heat-scaled cadence for a channel at a density band, with a seeded jitter. */
export function cadenceMs(kind, band, jitter) {
  const c = CADENCE[kind];
  if (!c) return Infinity;
  const b = clamp01(band);
  const base = lerp(c.base, c.min, b);
  const j = Number.isFinite(jitter) ? jitter : 0;
  return Math.max(120, Math.round(base * (0.78 + 0.44 * j)));
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
 *   reduced        reduced motion (drops swirl and every plant)
 * @returns {Object} the plan
 */
export function buildVigil(o = {}) {
  const seed = String(o.seed == null ? 'instant_recall' : o.seed);
  const tier = tierOf(o.gradeTier);
  const reduced = !!o.reduced;
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
  const pool = TEMPLATES.filter((k) => TEMPLATE_FROM_TIER[k] <= tier && k !== 'MODE');
  const totalQ = count * qPer;
  const wantMode = tier === 4 && roll('mode') < PLAYTEST.MODE_CHANCE;
  /* MODE never takes the very last question of the class - the vigil's exit
   * beat is about what FIRED, not about the furniture. */
  const modeSlot = wantMode && totalQ > 1
    ? Math.floor(roll('mode-slot') * (totalQ - 1))
    : -1;

  const stops = [];
  let dealt = 0;
  let prevTemplate = '';
  let plants = 0;
  let plantedLast = false;
  for (let n = 0; n < count; n++) {
    const questions = [];
    for (let q = 0; q < qPer; q++) {
      let template;
      if (dealt === modeSlot) {
        template = 'MODE';
      } else {
        /* no-repeat-last across the whole class where the pool allows it */
        const avail = pool.length > 1 ? pool.filter((k) => k !== prevTemplate) : pool.slice();
        template = pickFrom(avail, roll('tmpl')) || pool[0];
      }
      const weight = (6000 / windowMs) * (template === 'MODE' ? PLAYTEST.MODE_WEIGHT : 1);
      questions.push({ i: dealt, template, weight, windowMs });
      if (template !== 'MODE') prevTemplate = template;
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
      const channel = (tier >= 4 && roll('plant-ch') < 0.42) ? 'audio_trigger' : 'sub_flash';
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

  /* ---- layout sequence: one at the open, one at every resume ----------- */
  const lpool = layoutPool(tier, reduced);
  const layouts = [];
  let prevLayout = '';
  for (let i = 0; i <= count; i++) {
    let kind;
    if (i === 0) {
      kind = 'rows';                      // the vigil always opens on rows
    } else {
      const avail = lpool.length > 1 ? lpool.filter((k) => k !== prevLayout) : lpool.slice();
      kind = pickFrom(avail, roll('layout')) || lpool[0];
    }
    layouts.push({ index: i, kind });
    prevLayout = kind;
  }

  /* ---- emission schedule: per channel, a seeded jitter + variant ring --- */
  const channels = [];
  const kinds = TRIGGER_CHANNELS[tier].slice();
  if (tier >= STING_FROM_TIER) kinds.push('audio_trigger');
  for (const kind of kinds) {
    channels.push(makeChannel(kind, roll, false));
  }
  const dressing = [];
  for (const kind of DRESSING[tier]) dressing.push(makeChannel(kind, roll, true));

  const plan = {
    seed,
    tier,
    reduced,
    budgetMs,
    windowMs,
    qPerStop: qPer,
    stopCount: count,
    segments: count + 1,
    stops,
    layouts,
    layoutPool: lpool,
    channels,
    dressing,
    stings: STINGS.slice(),
    templates: pool.concat(wantMode ? ['MODE'] : []),
    modeSlot,
    plantCount: plants,
    densityMult,
    densityCeil,
    densityFloor,
    rampMs: PLAYTEST.RAMP_MS,
    heatCap: PLAYTEST.HEAT_CAP[tier],
    audioCeil: PLAYTEST.AUDIO_CEIL[tier],
    totalQuestions: totalQ,
  };
  return plan;
}

/** One channel's dealt ring: 8 jitters + 8 variants, consumed round-robin. */
function makeChannel(kind, roll, held) {
  const jitter = [];
  for (let i = 0; i < 8; i++) jitter.push(roll('jit-' + kind));
  const pool = VARIANTS[kind] || [''];
  const variants = [];
  let prev = '';
  for (let i = 0; i < 8; i++) {
    const avail = pool.length > 1 ? pool.filter((v) => v !== prev) : pool.slice();
    const v = pickFrom(avail, roll('var-' + kind)) || pool[0];
    variants.push(v);
    prev = v;
  }
  return { kind, jitter, variants, held: !!held };
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
 *   effects     distinct effect names available as distractors
 *   hasEffect   the tail carries at least one effect
 *   layouts     how many layouts this class can name
 *   hasLayout   the stage layout at the freeze is known
 */
export function canAsk(template, avail) {
  const a = avail || {};
  switch (template) {
    case 'LAST_WORD': return !!a.hasWord && (a.words | 0) >= 3;
    case 'LAST_TWO': return !!a.hasTwoWords && (a.words | 0) >= 2;
    case 'LAST_EFFECT': return !!a.hasEffect && (a.effects | 0) >= 3;
    case 'LAST_STING': return !!a.hasSting && !!a.audible && (a.stings | 0) >= 3;
    case 'MODE': return !!a.hasLayout && (a.layouts | 0) >= 3;
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
  }
  for (let i = 1; i < p.stops.length; i++) {
    if (p.stops[i].plant && p.stops[i - 1].plant) bad.push('plants clumped at ' + i);
  }

  /* templates */
  let modes = 0;
  for (const s of p.stops) {
    for (const q of s.questions) {
      if (TEMPLATES.indexOf(q.template) < 0) bad.push('unknown template ' + q.template);
      if (TEMPLATE_FROM_TIER[q.template] > tier) bad.push(q.template + ' above tier ' + tier);
      if (q.template === 'MODE') modes += 1;
    }
  }
  if (modes > 1) bad.push('more than one MODE question');
  if (modes && tier !== 4) bad.push('MODE below tier 4');

  /* layouts */
  if (p.layouts.length !== p.stopCount + 1) bad.push('layout count');
  if (p.layouts[0].kind !== 'rows') bad.push('vigil must open on rows');
  for (let i = 1; i < p.layouts.length; i++) {
    if (p.layoutPool.length > 1 && p.layouts[i].kind === p.layouts[i - 1].kind) bad.push('layout repeated at ' + i);
    if (p.layoutPool.indexOf(p.layouts[i].kind) < 0) bad.push('layout outside the tier pool at ' + i);
  }
  if (p.reduced && p.layouts.some((l) => l.kind === 'swirl')) bad.push('swirl under reduced motion');

  return bad;
}

/** The realised MODE share of a plan's question weight (the <=10% ruling). */
export function modeWeightShare(p) {
  let total = 0;
  let mode = 0;
  for (const s of p.stops) {
    for (const q of s.questions) { total += q.weight; if (q.template === 'MODE') mode += q.weight; }
  }
  return total > 0 ? mode / total : 0;
}

export default { buildVigil, densityAt, heatFor, cadenceMs, resolveTemplate, canAsk, assertPlan, PLAYTEST };
