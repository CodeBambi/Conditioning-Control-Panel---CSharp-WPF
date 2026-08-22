/* ============================================================================
 * games/deja-vu/script.js - Deja Vu's PURE side.
 *
 * Everything here is a pure function of (seed, tier, settings). No DOM, no
 * engine, no clock. That is deliberate: the dossier's determinism clause says
 * "board layout, pair assignment, swap script and drift schedule all derive from
 * the UTC date seed, so the day's board is globally identical and times are
 * comparable", and a retake must replay the IDENTICAL script (SYNTHESIS,
 * per-game rulings). Keeping the whole script in pure functions is what makes
 * that testable headless and impossible to drift by accident.
 *
 *   dialsFor(tier, opts)            tier -> every difficulty dial
 *   buildLayout(seed, dials)        deal order + pair-per-cell assignment
 *   buildSwapSchedule(seed, dials)  the glitch_swap script (settled windows)
 *   buildDriftSchedule(seed, dials) the row_drift script (tier 4)
 *   compositeFor(inputs)            the game's inputs to the SHARED rubric
 *   flavorXpFor(tracked)            the capped flavor bonus (SYNTHESIS #4)
 *
 * EFFECTS ARE THE DIFFICULTY (GROUND-RULES §6): tier 2 raises effects on the
 * SAME 4x3 grid as tier 1, and only tiers 3/4 grow the board. The one grid the
 * player may choose (boardSizes) is a per-game setting whose below-par values
 * cap the class at A - the shell computes that cap, never this file.
 * ==========================================================================*/

import { makeRng, makeTaggedRoll, shuffled } from '../../core/rng.js';

/** Test seam ONLY: the scratch harness scales every duration so a 90s class
 *  runs in ~2s. Production never touches it (1 = real time). */
let timeScale = 1;
export function setTimeScale(f) {
  const v = Number(f);
  timeScale = Number.isFinite(v) && v > 0 ? Math.min(1, v) : 1;
  return timeScale;
}
export function getTimeScale() { return timeScale; }
/** Scale a duration. Always >= 0, never fractional-ms surprises. */
export function scaled(ms) {
  const v = Number(ms);
  if (!Number.isFinite(v) || v <= 0) return 0;
  return Math.max(1, Math.round(v * timeScale));
}

/* ----------------------------------------------------------------------------
 * TIMINGS (dossier core loop) - one table, scaled at use.
 * -------------------------------------------------------------------------- */
export const TIMING = Object.freeze({
  dealStaggerMs: 120,       // seeded card-toss cascade
  previewDownMs: 240,       // the single flip-down wave
  poisonLeadMs: 400,        // sub_flash at preview end -400ms
  flipMs: 220,              // 3D Y-flip
  flipReducedMs: 160,       // reduced motion: crossfade
  judgeMs: 250,             // both loops playing, before the verdict
  mismatchHoldMs: 900,      // x DejaVuPeekHold (dv_peek_hold)
  matchLockMs: 520,         // pulse + wax stamp
  tellMs: 600,              // swap telegraph BEFORE the swap
  swapMs: 500,              // the glitch transition itself
  driftTellMs: 520,
  cramRevealMs: 800,        // ghost reveal (the shared peek verb's window)
  settleMs: 140,            // pause before a settled-board mutation window
  endgameDrumMs: 620,
  ceremonyMs: 420,          // stamp before the class hands over to the report card
});

/* ----------------------------------------------------------------------------
 * TIER DIALS - effects first, grid second.
 * -------------------------------------------------------------------------- */
const TIER_TABLE = Object.freeze({
  1: {
    pairs: 6, previewMs: 5000, heat: 0.18, swapBudget: 0, adjacentOnly: true,
    drift: 0, subFlash: false, wash: false, bubbles: 0, crt: 0,
    burstPressure: false, plainFloor: 0.80, parPerPair: 7.0, ambient: 0.25,
  },
  2: {
    // SAME 4x3 grid as tier 1 - a pure effect raise (dossier) - plus the
    // taste-of-the-twist: exactly ONE telegraphed adjacent-only swap
    // (SYNTHESIS amendment 2, budget 1/class).
    pairs: 6, previewMs: 4000, heat: 0.38, swapBudget: 1, adjacentOnly: true,
    drift: 0, subFlash: true, wash: true, bubbles: 0, crt: 0.25,
    burstPressure: false, plainFloor: 0.65, parPerPair: 7.0, ambient: 0.4,
  },
  3: {
    pairs: 8, previewMs: 3000, heat: 0.60, swapBudget: 2, adjacentOnly: true,
    drift: 0, subFlash: true, wash: true, bubbles: 3, crt: 0.5,
    burstPressure: false, plainFloor: 0.45, parPerPair: 6.5, ambient: 0.55,
  },
  4: {
    pairs: 10, previewMs: 2500, heat: 0.85, swapBudget: 6, adjacentOnly: false,
    drift: 2, subFlash: true, wash: true, bubbles: 5, crt: 0.8,
    burstPressure: true, plainFloor: 0.30, parPerPair: 6.0, ambient: 0.7,
  },
});

/** Grid shape for a pair count. 6 -> 4x3, 8 -> 4x4, 10 -> 5x4 (dossier). */
export function gridFor(pairs) {
  const p = Math.max(2, Math.round(Number(pairs) || 6));
  const cells = p * 2;
  const table = { 12: [4, 3], 16: [4, 4], 20: [5, 4] };
  if (table[cells]) return { cols: table[cells][0], rows: table[cells][1] };
  // Any other pair count still yields a sane rectangle (never crash on a
  // hand-edited setting): widest factor <= 5.
  for (let cols = 5; cols >= 2; cols--) if (cells % cols === 0) return { cols, rows: cells / cols };
  return { cols: cells, rows: 1 };
}

export const BOARD_SIZES = Object.freeze([6, 8, 10]);
export const BOARD_PAR = Object.freeze({ 1: 6, 2: 6, 3: 8, 4: 10 });

/**
 * Every dial for one class.
 * @param {number} tier 1..4
 * @param {Object=} opts {pairs} - the player's board-size choice (may be below
 *        par; the SHELL prices that as an A-cap, we just honour the board).
 */
export function dialsFor(tier, opts = {}) {
  const t = Math.max(1, Math.min(4, Math.round(Number(tier) || 1)));
  const base = TIER_TABLE[t];
  const chosen = Number(opts.pairs);
  const pairs = Number.isFinite(chosen) && chosen >= 2 ? Math.round(chosen) : base.pairs;
  const grid = gridFor(pairs);
  return Object.assign({}, base, {
    tier: t,
    pairs,
    cols: grid.cols,
    rows: grid.rows,
    cells: grid.cols * grid.rows,
    parSec: Math.round(pairs * base.parPerPair),
    belowTierPar: pairs < (BOARD_PAR[t] || base.pairs),
  });
}

/* ----------------------------------------------------------------------------
 * LAYOUT
 * -------------------------------------------------------------------------- */
/**
 * @returns {{cols,rows,pairs,slots:number[],dealOrder:number[]}}
 *   slots[cellIndex] = pairId. Deterministic for a seed: two boots of the same
 *   seed deal the same board (tested).
 */
export function buildLayout(seed, dials) {
  const d = dials || dialsFor(1);
  const ids = [];
  for (let p = 0; p < d.pairs; p++) { ids.push(p); ids.push(p); }
  while (ids.length < d.cells) ids.push(-1);          // never under-fill a grid
  const slots = shuffled(ids.slice(0, d.cells), makeRng(seed + '|dv|layout'));
  const order = [];
  for (let i = 0; i < d.cells; i++) order.push(i);
  const dealOrder = shuffled(order, makeRng(seed + '|dv|deal'));
  return { cols: d.cols, rows: d.rows, pairs: d.pairs, slots, dealOrder };
}

/** Orthogonal neighbours of a cell index on a cols x rows grid. */
export function neighborsOf(index, cols, rows) {
  const i = index | 0;
  const x = i % cols;
  const y = Math.floor(i / cols);
  const out = [];
  if (x > 0) out.push(i - 1);
  if (x < cols - 1) out.push(i + 1);
  if (y > 0) out.push(i - cols);
  if (y < rows - 1) out.push(i + cols);
  return out;
}

/** True when two cells share an edge (the "adjacent-only" law of early swaps). */
export function isAdjacent(a, b, cols, rows) {
  return neighborsOf(a, cols, rows).indexOf(b | 0) >= 0;
}

/* ----------------------------------------------------------------------------
 * THE SWAP SCRIPT
 *
 * Swaps fire ONLY on a settled board (no unmatched tile face-up), so the script
 * is indexed by SETTLED WINDOW, not by wall clock: "nothing you are looking at
 * ever moves" (dossier). Each entry carries a CANDIDATE LIST because locked
 * pairs are exempt - at fire time the runtime takes the first candidate whose
 * cells are both still unmatched, which keeps the choice deterministic without
 * the schedule having to predict how the player plays.
 * -------------------------------------------------------------------------- */
const FIRST_WINDOW = 2;      // one clean look before the board ever lies
const MIN_GAP = 2;           // anti-clump spacing on the seeded rng

export function buildSwapSchedule(seed, dials) {
  const d = dials || dialsFor(1);
  const out = [];
  if (!d.swapBudget) return out;
  const roll = makeTaggedRoll(seed + '|dv|swap');
  const rng = makeRng(seed + '|dv|swapcells');
  let window = FIRST_WINDOW;
  for (let n = 0; n < d.swapBudget; n++) {
    // spacing: MIN_GAP plus 0..2 seeded slack, so a class never machine-guns
    window += n === 0 ? Math.floor(roll('lead') * 2) : MIN_GAP + Math.floor(roll('gap') * 3);
    const adjacentOnly = d.adjacentOnly || n === 0;   // the FIRST swap is always gentle
    out.push({
      index: n,
      window,
      adjacentOnly,
      candidates: swapCandidates(rng, d, adjacentOnly),
    });
  }
  return out;
}

/** 4 deterministic candidate cell-pairs, best first. */
function swapCandidates(rng, d, adjacentOnly) {
  const list = [];
  const seen = new Set();
  let guard = 0;
  while (list.length < 4 && guard++ < 60) {
    const a = Math.floor(rng() * d.cells);
    let b;
    if (adjacentOnly) {
      const ns = neighborsOf(a, d.cols, d.rows);
      if (!ns.length) continue;
      b = ns[Math.floor(rng() * ns.length)];
    } else {
      b = Math.floor(rng() * d.cells);
      if (b === a) continue;
    }
    const key = Math.min(a, b) + ':' + Math.max(a, b);
    if (seen.has(key)) continue;
    seen.add(key);
    list.push([a, b]);
  }
  return list;
}

/* ----------------------------------------------------------------------------
 * THE DRIFT SCRIPT (tier 4, single axis in v1 - dual axis stays undesigned)
 * -------------------------------------------------------------------------- */
export function buildDriftSchedule(seed, dials, swaps) {
  const d = dials || dialsFor(1);
  const out = [];
  if (!d.drift) return out;
  const roll = makeTaggedRoll(seed + '|dv|drift');
  const taken = new Set((swaps || []).map((s) => s.window));
  let window = FIRST_WINDOW + 2;
  for (let n = 0; n < d.drift; n++) {
    window += MIN_GAP + 1 + Math.floor(roll('gap') * 3);
    // one mutation per settled window: never a swap AND a drift in the same one
    while (taken.has(window)) window += 1;
    taken.add(window);
    const axis = roll('axis') < 0.5 ? 'row' : 'col';
    const lines = axis === 'row' ? d.rows : d.cols;
    out.push({ index: n, window, axis, line: Math.floor(roll('line') * lines) });
  }
  return out;
}

/** Cell indices of one row/column, in slide order. */
export function lineCells(axis, line, cols, rows) {
  const out = [];
  if (axis === 'row') {
    for (let x = 0; x < cols; x++) out.push(line * cols + x);
  } else {
    for (let y = 0; y < rows; y++) out.push(y * cols + line);
  }
  return out;
}

/* ----------------------------------------------------------------------------
 * PLAIN-SHARE RAMP (DTRH): effect-free settled windows ease .80 -> .30, so the
 * board stays mostly quiet even at the top and each mutation lands.
 * -------------------------------------------------------------------------- */
export function plainShareFor(dials, progress01) {
  const d = dials || dialsFor(1);
  const p = Math.max(0, Math.min(1, Number(progress01) || 0));
  // the floor is the tier's; early in the class it sits a little higher
  return Math.max(0, Math.min(1, d.plainFloor + (1 - d.plainFloor) * 0.25 * (1 - p)));
}

/** Per-phase sawtooth (DTRH): each tier starts higher and breathes inside it. */
export function heatFor(dials, progress01) {
  const d = dials || dialsFor(1);
  const p = Math.max(0, Math.min(1, Number(progress01) || 0));
  const breathe = 0.10 * Math.sin(p * Math.PI * 3);
  return Math.max(0, Math.min(1, d.heat + 0.08 * p + breathe));
}

/* ----------------------------------------------------------------------------
 * MATCHED-LOOP POLICY (per-game setting, ceiling rule)
 *
 * 'auto' = freeze at high tiers (SYNTHESIS: protects the engine's distraction
 * budget). The setting may only ever choose the CALMER option where the tier
 * would allow playing - it can never force loops back on (GROUND-RULES §5:
 * global/quality ceilings win, a per-game knob may use less, never more).
 * -------------------------------------------------------------------------- */
export function matchedLoopPolicy(setting, tier, posterOnly) {
  const t = Math.max(1, Math.min(4, Math.round(Number(tier) || 1)));
  const autoFreeze = posterOnly || t >= 3;
  const want = String(setting || 'auto');
  if (want === 'freeze') return { play: false, reason: 'setting' };
  if (want === 'keep-playing') {
    if (autoFreeze) return { play: false, reason: 'ceiling' };   // refused upward
    return { play: true, reason: 'setting' };
  }
  return { play: !autoFreeze, reason: 'auto' };
}

/* ----------------------------------------------------------------------------
 * GRADING INPUTS (the shared rubric owns the letters - core/grades.js)
 * -------------------------------------------------------------------------- */
export const COMPOSITE_WEIGHTS = Object.freeze({
  accuracy: 0.46,     // pairs / attempts
  time: 0.30,         // par / elapsed
  combo: 0.16,        // max combo against the shared cap of 8
  trackedEach: 0.03,  // "tracked through the static", max 3 instances
});
export const TRACKED_CAP = 3;
export const COMBO_CAP = 8;

/**
 * @param {Object} i {tier, pairs, matched, attempts, elapsedSec, maxCombo,
 *                    tracked, timeout, parSec}
 * @returns {{composite:number, accuracy:number, timeScore:number,
 *            comboScore:number, trackedBonus:number, timeout:boolean}}
 */
export function compositeFor(i) {
  const src = i || {};
  const pairs = Math.max(1, Math.round(Number(src.pairs) || 1));
  const matched = Math.max(0, Math.min(pairs, Math.round(Number(src.matched) || 0)));
  const attempts = Math.max(matched, Math.round(Number(src.attempts) || 0));
  const parSec = Math.max(1, Number(src.parSec) || pairs * 7);
  const elapsed = Math.max(0.001, Number(src.elapsedSec) || 0);
  const timeout = !!src.timeout;

  const accuracy = attempts > 0 ? matched / attempts : 0;
  // an unfinished board can never score full accuracy: unmatched pairs count as
  // outstanding attempts, so bailing at 1/10 pairs does not read as 100%
  const completion = matched / pairs;
  const timeScore = Math.max(0, Math.min(1, parSec / elapsed));
  const comboScore = Math.max(0, Math.min(1, (Number(src.maxCombo) || 0) / COMBO_CAP));
  const trackedBonus = Math.min(TRACKED_CAP, Math.max(0, Math.round(Number(src.tracked) || 0)))
    * COMPOSITE_WEIGHTS.trackedEach;

  let composite = (COMPOSITE_WEIGHTS.accuracy * accuracy * completion)
    + (COMPOSITE_WEIGHTS.time * timeScore * completion)
    + (COMPOSITE_WEIGHTS.combo * comboScore)
    + trackedBonus;
  composite = Math.max(0, Math.min(1, composite));
  // Dossier: the 90s bell without a clear is an automatic C. The rubric's C band
  // is "below B", so the timeout clamps under the B threshold rather than
  // inventing a letter here (grades.js owns letters).
  if (timeout) composite = Math.min(composite, 0.49);
  return { composite, accuracy, completion, timeScore, comboScore, trackedBonus, timeout };
}

/** +3 XP per tracked-through-a-swap match, capped 3/class (=9, under the 15 cap). */
export function flavorXpFor(tracked) {
  const n = Math.min(TRACKED_CAP, Math.max(0, Math.round(Number(tracked) || 0)));
  return n * 3;
}

/* ----------------------------------------------------------------------------
 * VARIABLE-RATIO REWARD (Intake canon, seeded per class)
 * baseChance .30 at low heat -> .60 at high heat; streak cap 8 x .03; jackpot
 * roll .85. Local because the shell's per-class engine handle does not expose
 * engine.rewardRoll() (reported in the build summary).
 * -------------------------------------------------------------------------- */
export function createReward(seed) {
  const roll = makeTaggedRoll(seed + '|dv|vr');
  let n = 0;
  return function rollReward({ heat = 0, streak = 0, force = false } = {}) {
    n += 1;
    const base = 0.30 + 0.30 * Math.max(0, Math.min(1, heat));
    const chance = Math.max(0, Math.min(1, base + Math.min(COMBO_CAP, streak) * 0.03));
    const r = roll('fire');
    const fire = !!force || r < chance;
    const j = roll('jack');
    const jackpot = fire && j >= 0.85;
    const nearMiss = !fire && r < chance + 0.08;
    return { fire, jackpot, nearMiss, chance, n };
  };
}

export default { dialsFor, buildLayout, buildSwapSchedule, buildDriftSchedule, compositeFor };
