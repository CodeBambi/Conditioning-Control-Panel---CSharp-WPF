/* ============================================================================
 * games/echo/sequence.js - ECHO's seeded sequence, decoy plan and tier dials.
 * PURE: no DOM, no clock, no ctx, no engine.
 *
 * WHAT THIS FILE IS. Echo is Simon: one growing stream per ATTEMPT, and the
 * sequence at length L is the first L items of that stream (a cleared round
 * APPENDS, it never reshuffles - that is what makes the melody feel like one
 * melody). The stream, the decoy plan and every dial are functions of the
 * class seed, so a retake replays the identical class (Law V).
 *
 * SEEDING (append-only, Law V):
 *   stream   makeRng(seed + '|ec-seq|'   + attempt)   - drawn once per attempt
 *   decoys   makeTaggedRoll(seed + '|ec-decoy|' + attempt) - tag-namespaced, so
 *            adding a new decoy roll never shifts an existing sequence
 * Neither ever calls Math.random.
 *
 * THE WARM START (contract ruling 5, the critic's top fix). Simon's structural
 * flaw is that every class restarts at 3 and the first 40 seconds are filler.
 * Echo opens at clamp(3 + floor(lifetime bestLen / 4), 3, 6) + a per-tier
 * bonus, capped at START_MAX. A veteran skips the trivial opening; a first
 * class still opens at 3.
 *
 * THE DECOY ECHO (contract ruling 2, SYNTHESIS #2 - the signature twist enters
 * at tier 2). A decoy is a pad that LIGHTS DURING PLAYBACK and is NOT part of
 * the sequence; the player must not echo it. It sits at a SEAM: `afterIndex`
 * real items have played before it, so the temptation is to press it as the
 * afterIndex-th press. Tier 2 telegraphs it (the sheet says so and the pad
 * wears a tell); tier 3+ does not. Decoy pads are drawn from the tier's own
 * alphabet - a pad that could never appear in a sequence would be a free tell.
 *
 * ONE FAIL IS NOT THE CLASS (contract ruling 6). After a fail the next
 * sequence deals at max(startLen, failedLen - FAIL_SETBACK) - a setback, never
 * a reset to 3 - and the class keeps dealing until the bell.
 *
 * THE PITCH LADDER. Six pads, ONE engine recipe, six pitches: a pentatonic
 * ladder spanning exactly one octave, PAD_SEMIS semitones from the recipe's
 * own pitch. The combo ratchet adds +1 semitone per streak link, cap 7 (the
 * Impulse Control shape). The ladder is chosen so that the TOP pad at the FULL
 * ratchet lands on 2.0 and the BOTTOM pad at zero ratchet on ~0.67 - inside
 * shell/audio.js's 0.5..2 clamp at both ends, so nothing in the ladder ever
 * silently flattens against the clamp.
 * ==========================================================================*/

import { makeRng, makeTaggedRoll } from '../../core/rng.js';

/* ----------------------------------------------------------------------------
 * THE PLAYTEST BLOCK - every number a playtest might touch, in one place.
 * -------------------------------------------------------------------------- */
export const PLAYTEST = Object.freeze({
  /** Pads on the ring. Always six; the TIER picks how many of them a sequence
   *  may draw from (all six stay live and pressable at every tier). */
  PADS: 6,
  ALPHABET: Object.freeze({ 1: 4, 2: 5, 3: 6, 4: 6 }),

  /** THE WARM START: clamp(BASE + floor(bestLen / DIV), MIN, MAX) + bonus. */
  WARM_BASE: 3,
  WARM_DIV: 4,
  WARM_MIN: 3,
  WARM_MAX: 6,
  WARM_TIER_BONUS: Object.freeze({ 1: 0, 2: 1, 3: 1, 4: 2 }),
  START_MAX: 8,
  /** The longest stream a class ever deals (a 105s class never reaches it). */
  MAX_LEN: 24,
  /** A fail costs this much length, never more (floor = the warm start). */
  FAIL_SETBACK: 2,

  /** PLAYBACK TEMPO: ms per step by tier, and the lit share of a step. */
  STEP_MS: Object.freeze({ 1: 620, 2: 560, 3: 500, 4: 440 }),
  LIT_RATIO: 0.62,
  /** The floor a lit pad never goes under, however tight the tempo runs. */
  LIT_MIN_MS: 150,
  /** ENCORE: the same sequence at half tempo (contract ruling 5). */
  ENCORE_TEMPO_MULT: 2,
  /** The interference beat: playback ends -> this gap -> the input turn. */
  SEAM_MS: 560,
  SEAM_MS_REDUCED: 360,

  /** THE DECOY PLAN. Chance is per candidate slot; MAX is the ceiling. */
  DECOY_CHANCE: Object.freeze({ 1: 0, 2: 0.6, 3: 0.75, 4: 0.9 }),
  DECOY_MAX: Object.freeze({ 1: 0, 2: 1, 3: 1, 4: 2 }),
  /** No decoy under this sequence length (there is no seam worth using). */
  DECOY_MIN_LEN: 4,
  /** Tiers at or below this TELEGRAPH the decoy; above it, they do not. */
  DECOY_TELEGRAPH_MAX_TIER: 2,

  /** SOFT INPUT TIMER (dossier: tier 3 soft, tier 4 strict). 0 = untimed. */
  INPUT_WINDOW_MS: Object.freeze({ 1: 0, 2: 0, 3: 4200, 4: 3200 }),

  /** audio_trigger level ceilings by tier (the engine clamps again). */
  AUDIO_CEIL: Object.freeze({ 1: 0.45, 2: 0.6, 3: 0.75, 4: 0.9 }),

  /** HEAT: the class's own ladder is the sequence length; the tier caps it. */
  HEAT_CAP: Object.freeze({ 1: 0.45, 2: 0.65, 3: 0.85, 4: 1 }),
  HEAT_FLOOR: 0.12,
  /** Sequence length that reaches the tier's heat cap. */
  HEAT_TOP: Object.freeze({ 1: 8, 2: 10, 3: 12, 4: 14 }),

  /** THE COMBO RATCHET: +1 semitone per clean press, capped. */
  RATCHET_CAP: 7,
  /** A miss at or past this fraction of the sequence is a NEAR MISS: the
   *  ratchet drops by NEAR_MISS_DROP instead of resetting (dossier). */
  NEAR_MISS_FRAC: 0.8,
  NEAR_MISS_DROP: 2,

  /** The pentatonic pad ladder, in semitones off the recipe's own pitch. */
  PAD_SEMIS: Object.freeze([-7, -5, -2, 0, 3, 5]),
  PITCH_MIN: 0.5,
  PITCH_MAX: 2,

  /** Class rhythm. */
  BELL_WARN_SEC: 15,
  BRIEF_MS: 1500,
  BRIEF_MS_REDUCED: 800,
  CLEAR_HOLD_MS: 950,
  CLEAR_HOLD_MS_REDUCED: 520,
  FAIL_HOLD_MS: 1250,
  FAIL_HOLD_MS_REDUCED: 700,
  /** THE REVEAL after a miss: the pad you needed holds lit, wearing the
   *  "this one" halo, long enough to actually be read (owner verdict B). */
  REVEAL_MS: 700,

  /** THE HAND-OFF (owner verdict A): the beat between the room's turn and
   *  yours. The ring sweeps bright, every pad pulses once, one chime. */
  HANDOFF_MS: 260,
  HANDOFF_MS_REDUCED: 160,
  /** How long each pad's hand-off pulse holds. */
  HANDOFF_PULSE_MS: 180,
  /** THE ROUND-CLEAR SWEEP: a ring flourish, one pad after another. */
  SWEEP_STEP_MS: 70,
  SWEEP_LIT_MS: 320,

  /** THE TRIGGER CLIP (owner verdict C): the player's own whisper audio for
   *  the phrase a pad wears, UNDER the pad's note - never instead of it and
   *  never louder. `LEVEL` is the request the engine then clamps; `MAX_MS`
   *  truncates a long clip so a fast sequence does not smear. */
  CLIP_LEVEL: 0.3,
  CLIP_MAX_MS: 1200,
  CLIP_FADE_MS: 180,
  /** The clip bus. `voice` is where the app's own whispers belong, so the
   *  player's voice slider owns them. */
  CLIP_BUS: 'voice',
  END_HOLD_MS: 2400,
  END_HOLD_MS_REDUCED: 1300,
  STALL_TICK_MS: 500,
  /** The trickster is told about a stall at this many ms of silence. */
  STALL_MS: 3500,

  /** ctx.audioAudible === false: the cue is mixed but inaudible, so the LIGHT
   *  has to carry the whole signal - pads light longer and brighter. */
  SILENT_LIT_MULT: 1.35,

  /** The streak chip appears from this many clean presses. */
  STREAK_VISIBLE: 2,
  /** The 10-segment shell meter fills to here. */
  STREAK_CAP: 10,
});

/* ----------------------------------------------------------------------------
 * SMALL PURE HELPERS
 * -------------------------------------------------------------------------- */
export function clamp01(v) { const n = Number(v); return !Number.isFinite(n) ? 0 : n < 0 ? 0 : n > 1 ? 1 : n; }
export function tierOf(gradeTier) { return Math.max(1, Math.min(4, Math.round(Number(gradeTier) || 1))); }

/** How many of the six pads a sequence at this tier may draw from. */
export function alphabetFor(gradeTier) {
  const n = PLAYTEST.ALPHABET[tierOf(gradeTier)] || PLAYTEST.PADS;
  return Math.max(2, Math.min(PLAYTEST.PADS, n));
}

/**
 * THE WARM START. `bestLen` is the player's LIFETIME longest cleared echo
 * (gameMeta.bestLen); a first class passes 0 and opens at 3.
 */
export function warmStartLen(bestLen, gradeTier) {
  const t = tierOf(gradeTier);
  const best = Math.max(0, Math.floor(Number(bestLen) || 0));
  const base = PLAYTEST.WARM_BASE + Math.floor(best / PLAYTEST.WARM_DIV);
  const clamped = Math.max(PLAYTEST.WARM_MIN, Math.min(PLAYTEST.WARM_MAX, base));
  const bonus = PLAYTEST.WARM_TIER_BONUS[t] || 0;
  return Math.max(PLAYTEST.WARM_MIN, Math.min(PLAYTEST.START_MAX, clamped + bonus));
}

/** The next sequence length after a fail: a setback, never a reset. */
export function nextLenAfterFail(failedLen, startLen) {
  const failed = Math.max(1, Math.floor(Number(failedLen) || 1));
  const floorLen = Math.max(PLAYTEST.WARM_MIN, Math.floor(Number(startLen) || PLAYTEST.WARM_MIN));
  return Math.max(floorLen, failed - PLAYTEST.FAIL_SETBACK);
}

/** ms per playback step. `tempoMult` is the encore's half tempo (2). */
export function stepMsFor(gradeTier, tempoMult) {
  const base = PLAYTEST.STEP_MS[tierOf(gradeTier)] || PLAYTEST.STEP_MS[1];
  const mult = Number.isFinite(Number(tempoMult)) && Number(tempoMult) > 0 ? Number(tempoMult) : 1;
  return Math.round(base * mult);
}

/** How long a pad stays lit inside a step of `stepMs`. */
export function litMsFor(stepMs, opts = {}) {
  const mult = opts.silent ? PLAYTEST.SILENT_LIT_MULT : 1;
  const raw = Math.round(Math.max(1, Number(stepMs) || 0) * PLAYTEST.LIT_RATIO * mult);
  return Math.max(PLAYTEST.LIT_MIN_MS, Math.min(Math.max(1, Number(stepMs) || 0), raw));
}

/** The soft input window (ms) for a tier. 0 = untimed. */
export function inputWindowMs(gradeTier, tempoMult) {
  const base = PLAYTEST.INPUT_WINDOW_MS[tierOf(gradeTier)] || 0;
  if (!base) return 0;
  const mult = Number.isFinite(Number(tempoMult)) && Number(tempoMult) > 0 ? Number(tempoMult) : 1;
  return Math.round(base * mult);
}

/** The tier's audio ceiling (the engine clamps again on top of this). */
export function audioCeilFor(gradeTier) {
  return PLAYTEST.AUDIO_CEIL[tierOf(gradeTier)] || PLAYTEST.AUDIO_CEIL[1];
}

/**
 * THE PAD PITCH. One recipe, six pitches, plus the combo ratchet.
 * @param {number} pad     0..5
 * @param {number} ratchet 0..RATCHET_CAP semitones
 */
export function pitchFor(pad, ratchet) {
  const i = Math.max(0, Math.min(PLAYTEST.PADS - 1, Math.floor(Number(pad) || 0)));
  const r = Math.max(0, Math.min(PLAYTEST.RATCHET_CAP, Math.floor(Number(ratchet) || 0)));
  const semis = PLAYTEST.PAD_SEMIS[i] + r;
  const p = Math.pow(2, semis / 12);
  return Math.max(PLAYTEST.PITCH_MIN, Math.min(PLAYTEST.PITCH_MAX, Number(p.toFixed(4))));
}

/** The ratchet after a miss: a NEAR miss only drops it, a plain miss resets. */
export function ratchetAfterMiss(ratchet, reachedIndex, len) {
  const total = Math.max(1, Math.floor(Number(len) || 1));
  const reached = Math.max(0, Math.floor(Number(reachedIndex) || 0));
  if (reached / total >= PLAYTEST.NEAR_MISS_FRAC) {
    return Math.max(0, (Math.floor(Number(ratchet) || 0)) - PLAYTEST.NEAR_MISS_DROP);
  }
  return 0;
}

/** Was a miss at `reachedIndex` of `len` a near miss? (the tease + the tick) */
export function isNearMiss(reachedIndex, len) {
  const total = Math.max(1, Math.floor(Number(len) || 1));
  return (Math.max(0, Math.floor(Number(reachedIndex) || 0)) / total) >= PLAYTEST.NEAR_MISS_FRAC;
}

/**
 * HEAT: the class's own ladder is the sequence length, and gradeTier caps it
 * (the DE / IC shape). A clean-press streak nudges it, never past the cap.
 */
export function heatFor(len, gradeTier, startLen, streakLinks) {
  const t = tierOf(gradeTier);
  const cap = PLAYTEST.HEAT_CAP[t] || 1;
  const top = PLAYTEST.HEAT_TOP[t] || 10;
  const from = Math.max(PLAYTEST.WARM_MIN, Math.floor(Number(startLen) || PLAYTEST.WARM_MIN));
  const cur = Math.max(from, Math.floor(Number(len) || from));
  const span = Math.max(1, top - from);
  const p = clamp01((cur - from) / span);
  const links = Math.max(0, Math.floor(Number(streakLinks) || 0));
  const nudge = Math.min(0.08, links * 0.01);
  const h = PLAYTEST.HEAT_FLOOR + (1 - PLAYTEST.HEAT_FLOOR) * p + nudge;
  return Math.min(cap, clamp01(h));
}

/* ----------------------------------------------------------------------------
 * THE STREAM
 * -------------------------------------------------------------------------- */
/**
 * One attempt's whole stream. The sequence at length L is `stream.slice(0, L)`,
 * so growth APPENDS and a retake replays the same melody.
 *
 * The one shaping rule: never three of the same pad in a row (that reads as a
 * rendering fault rather than as a sequence). The fix-up consumes the stream
 * deterministically, so it is still a pure function of the seed.
 *
 * @returns {{stream:number[], n:number, attempt:number}}
 */
export function buildAttempt({ seed, gradeTier = 1, attempt = 0, maxLen = PLAYTEST.MAX_LEN } = {}) {
  const n = alphabetFor(gradeTier);
  const len = Math.max(1, Math.min(256, Math.floor(Number(maxLen) || PLAYTEST.MAX_LEN)));
  const rng = makeRng(String(seed == null ? '' : seed) + '|ec-seq|' + Math.max(0, Math.floor(Number(attempt) || 0)));
  const stream = [];
  for (let i = 0; i < len; i++) {
    let p = Math.floor(rng() * n);
    if (p >= n) p = n - 1;
    if (p < 0) p = 0;
    if (i >= 2 && stream[i - 1] === p && stream[i - 2] === p) {
      p = (p + 1 + Math.floor(rng() * (n - 1))) % n;
    }
    stream.push(p);
  }
  return { stream, n, attempt: Math.max(0, Math.floor(Number(attempt) || 0)) };
}

/** The sequence dealt at length `len` for this attempt. */
export function sequenceFor({ seed, gradeTier = 1, attempt = 0, len = 3 } = {}) {
  const want = Math.max(1, Math.min(PLAYTEST.MAX_LEN, Math.floor(Number(len) || 1)));
  const { stream } = buildAttempt({ seed, gradeTier, attempt, maxLen: PLAYTEST.MAX_LEN });
  return stream.slice(0, want);
}

/* ----------------------------------------------------------------------------
 * THE DECOY PLAN
 * -------------------------------------------------------------------------- */
/**
 * The decoys for ONE sequence. Deterministic, tier-gated, at most one per seam.
 *
 * @returns {Array<{pad:number, afterIndex:number, telegraph:boolean}>} sorted
 *   by afterIndex. `afterIndex` = how many REAL items played before the decoy,
 *   so the trap is pressing `pad` when the input is at index afterIndex.
 */
export function decoysFor({ seed, gradeTier = 1, attempt = 0, len = 3 } = {}) {
  const t = tierOf(gradeTier);
  const max = PLAYTEST.DECOY_MAX[t] || 0;
  const want = Math.max(1, Math.min(PLAYTEST.MAX_LEN, Math.floor(Number(len) || 1)));
  if (max <= 0 || want < PLAYTEST.DECOY_MIN_LEN) return [];
  const chance = PLAYTEST.DECOY_CHANCE[t] || 0;
  const n = alphabetFor(t);
  const seq = sequenceFor({ seed, gradeTier: t, attempt, len: want });
  const roll = makeTaggedRoll(String(seed == null ? '' : seed) + '|ec-decoy|' + Math.max(0, Math.floor(Number(attempt) || 0)));
  const telegraph = t <= PLAYTEST.DECOY_TELEGRAPH_MAX_TIER;
  const out = [];
  const taken = new Set();
  for (let k = 0; k < max; k++) {
    if (roll('fire|' + want + '|' + k) >= chance) continue;
    /* The seam sits BETWEEN two real items: 1 .. len-1. */
    const slots = Math.max(1, want - 1);
    let afterIndex = 1 + Math.floor(roll('pos|' + want + '|' + k) * slots);
    if (afterIndex > want - 1) afterIndex = want - 1;
    if (taken.has(afterIndex)) continue;              // one decoy per seam
    /* The pad must be in the tier's own alphabet (a pad that could never be
     * part of a sequence would be a free tell), and it must not be either of
     * the two real items it sits between - that would be indistinguishable
     * from a legitimate repeat rather than a lie. */
    let pad = Math.floor(roll('pad|' + want + '|' + k) * n);
    if (pad >= n) pad = n - 1;
    const banned = new Set([seq[afterIndex - 1], seq[afterIndex]]);
    let guard = 0;
    while (banned.has(pad) && guard < n) { pad = (pad + 1) % n; guard += 1; }
    if (banned.has(pad)) continue;                    // a 2-pad alphabet has no room
    taken.add(afterIndex);
    out.push({ pad, afterIndex, telegraph });
  }
  out.sort((a, b) => a.afterIndex - b.afterIndex);
  return out;
}

/**
 * PLAYBACK: the flat step list the ring walks, real items and decoys woven
 * together in play order.
 * @returns {Array<{pad:number, decoy:boolean, index:number, telegraph:boolean}>}
 *   `index` is the REAL sequence index for a real step, and the seam's
 *   afterIndex for a decoy.
 */
export function playbackSteps(seq, decoys) {
  const items = Array.isArray(seq) ? seq : [];
  const plan = Array.isArray(decoys) ? decoys.slice().sort((a, b) => a.afterIndex - b.afterIndex) : [];
  const out = [];
  let d = 0;
  for (let i = 0; i < items.length; i++) {
    while (d < plan.length && plan[d].afterIndex === i) {
      out.push({ pad: plan[d].pad, decoy: true, index: i, telegraph: !!plan[d].telegraph });
      d += 1;
    }
    out.push({ pad: items[i], decoy: false, index: i, telegraph: false });
  }
  while (d < plan.length) {                            // a seam past the end (never dealt, kept safe)
    out.push({ pad: plan[d].pad, decoy: true, index: items.length, telegraph: !!plan[d].telegraph });
    d += 1;
  }
  return out;
}

/**
 * THE WHOLE ROUND, in one pure call - what the suite asserts against and what
 * index.js walks. Nothing here touches a clock or a node.
 */
export function buildRound({ seed, gradeTier = 1, attempt = 0, len = 3, encore = false } = {}) {
  const t = tierOf(gradeTier);
  const want = Math.max(1, Math.min(PLAYTEST.MAX_LEN, Math.floor(Number(len) || 1)));
  const seq = sequenceFor({ seed, gradeTier: t, attempt, len: want });
  const decoys = decoysFor({ seed, gradeTier: t, attempt, len: want });
  const tempoMult = encore ? PLAYTEST.ENCORE_TEMPO_MULT : 1;
  const stepMs = stepMsFor(t, tempoMult);
  return {
    seq,
    len: seq.length,
    decoys,
    steps: playbackSteps(seq, decoys),
    stepMs,
    litMs: litMsFor(stepMs),
    windowMs: inputWindowMs(t, tempoMult),
    encore: !!encore,
    tier: t,
    attempt: Math.max(0, Math.floor(Number(attempt) || 0)),
  };
}

export default {
  PLAYTEST, alphabetFor, warmStartLen, nextLenAfterFail, stepMsFor, litMsFor,
  inputWindowMs, audioCeilFor, pitchFor, ratchetAfterMiss, isNearMiss, heatFor,
  buildAttempt, sequenceFor, decoysFor, playbackSteps, buildRound, tierOf, clamp01,
};
