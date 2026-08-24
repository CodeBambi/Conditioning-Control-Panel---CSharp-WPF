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
 *   dialsFor(tier, opts)            tier -> every difficulty dial (BOARD 1)
 *   dialsForBoard(tier, cleared, o) THE LADDER: board N's dials (see ESCALATION)
 *   buildLayout(seed, dials)        deal order + pair-per-cell assignment
 *   buildSwapSchedule(seed, dials)  the glitch_swap script (settled windows)
 *   buildDriftSchedule(seed, dials) the row_drift script (tier 4)
 *   boardCostSec(dials)             preview + deal + a typical clear + ceremony
 *   expectedClears(dials, budget)   the S gate, in boards (see THE ARITHMETIC)
 *   compositeFor(inputs)            the game's inputs to the SHARED rubric
 *   flavorXpFor(tracked)            the capped flavor bonus (SYNTHESIS #4)
 *
 * THE CLASS IS MANY BOARDS (owner ruling, class-length wave 2026-08-24). Clear a
 * board and the machine deals a fresh one; the 300s bell is the NORMAL end of
 * the class, not a punishment. Every board re-derives from the CLASS seed plus
 * '|b<N>', so the whole night is still one pure function of (seed, tier,
 * settings) and a retake replays every board identically.
 *
 * EFFECTS ARE THE DIFFICULTY (GROUND-RULES §6): tier 2 raises effects on the
 * SAME 4x3 grid as tier 1, and only tiers 3/4 grow the board. The one grid the
 * player may choose (boardSizes) is a per-game setting whose below-par values
 * cap the class at A - the shell computes that cap, never this file. THE BOARD
 * NEVER GROWS ACROSS BOARDS either: escalation moves the EFFECT dials and the
 * preview, never the pair count, so the player's chosen grid is the grid all
 * night and the grade's per-board arithmetic stays honest.
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
  /* THE LAST CALL (this constant was dead until the class-length wave - it is
   * wired now rather than deleted). In the final `lastCallMs` of the class the
   * room beats a three-note drum, one note every `endgameDrumMs`, so the bell
   * is HEARD coming instead of arriving. Audio + one hint line only: no CSS, so
   * it survives reduced motion and a 0-capped bgIntensity intact. */
  endgameDrumMs: 620,
  lastCallMs: 10000,
  ceremonyMs: 420,          // stamp before the class hands over to the report card
  /* The clear celebration between boards. Deliberately short - the beat is a
   * breath, not a cutscene, because the bell is spending real seconds on it. */
  boardClearMs: 700,
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
 * THE ESCALATION LADDER - one gentle notch per CLEARED board.
 *
 * A 300s class is many boards, so a board 6 that plays exactly like board 1
 * would be a treadmill. Every clear bumps the EFFECT dials one notch; the pair
 * count never moves (see the header). Two rules hold it honest:
 *
 *   THE CAP. Nothing may ever exceed the dials of ONE TIER ABOVE the player's
 *   own (plus a single extra swap at the very top, which is the only place the
 *   ladder has nowhere left to climb). A tier-1 player who clears six boards is
 *   playing a hard tier 1, never a tier 3.
 *   THE STOP. The ladder stops climbing at `maxBump` cleared boards. Board 5
 *   and board 9 are the same room, so a long clean run is rewarded with pace,
 *   not with an unwinnable board at minute five.
 *
 * The ladder is PURE (cleared -> dials); nothing here is random, so the seeded
 * schedules built on top of these dials still replay exactly on a retake.
 * -------------------------------------------------------------------------- */
export const ESCALATION = Object.freeze({
  maxBump: 4,             // the ladder stops climbing after 4 cleared boards
  swapPerBoard: 0.5,      // +1 swap every 2 clears (ceil), capped below
  swapOverCeil: 1,        // the cap: (tier+1)'s budget, plus this one extra
  driftFromBump: 3,       // a drift joins from the 3rd clear - and only at t>=3
  loosenFromBump: 4,      // adjacent-only relaxes this late, and only if t+1 does
  effectsFromBump: 2,     // sub_flash / wash / bubbles / crt adopt (t+1)'s values
  previewStepMs: 250,     // the memorize beat shrinks a notch a board...
  previewFloorMs: 2500,   // ...never below tier 4's own floor
  heatPerBoard: 0.04,     // the engine's heat scalar creeps
  heatCeil: 0.95,
});

/**
 * Board N's dials.  `cleared` is how many boards the player has ALREADY cleared
 * this class (0 for the first board, which is exactly `dialsFor`).
 *
 * WORKED EXAMPLES (the comment is the spec):
 *   tier 1, cleared 1 -> swapBudget 1 (a first taste of the twist), preview
 *                        4750ms, effects still off (effectsFromBump is 2)
 *   tier 1, cleared 2 -> swapBudget 1, sub_flash + wash + crt 0.25 on (tier 2's
 *                        own values), preview 4500ms
 *   tier 1, cleared 4+-> swapBudget 2 = tier 2's budget (1) + swapOverCeil (1).
 *                        That is the ceiling; board 9 is board 5.
 *   tier 4, cleared 1+-> swapBudget 7 = tier 4's 6 + swapOverCeil. Drift 3 from
 *                        the 3rd clear. Preview already at the floor.
 *
 * @param {number} tier 1..4
 * @param {number} cleared boards already cleared this class
 * @param {Object=} opts {pairs} - the player's board-size choice, board 1's and
 *        every later board's alike (escalation never touches the grid).
 */
export function dialsForBoard(tier, cleared, opts = {}) {
  const base = dialsFor(tier, opts);
  const done = Math.max(0, Math.round(Number(cleared) || 0));
  const bump = Math.min(ESCALATION.maxBump, done);
  if (bump <= 0) return Object.assign({}, base, { board: 1, bump: 0, deckTier: base.tier });

  const t = base.tier;
  const ceilTier = Math.min(4, t + 1);          // THE CAP, in one number
  const ceil = TIER_TABLE[ceilTier];
  const on = bump >= ESCALATION.effectsFromBump;   // the effect dials unlock together

  const swapBudget = Math.min(
    ceil.swapBudget + ESCALATION.swapOverCeil,
    base.swapBudget + Math.ceil(bump * ESCALATION.swapPerBoard),
  );
  // Drift is a tier-3/4 verb. Handing a tier-1 player a sliding row because they
  // played well is a different game, not a harder one.
  const drift = t >= 3
    ? Math.min(ceil.drift + (t === 4 ? 1 : 0), base.drift + (bump >= ESCALATION.driftFromBump ? 1 : 0))
    : base.drift;

  const out = Object.assign({}, base, {
    board: done + 1,
    bump,
    swapBudget,
    drift,
    previewMs: Math.max(
      ESCALATION.previewFloorMs,
      base.previewMs - bump * ESCALATION.previewStepMs,
    ),
    heat: Math.min(ESCALATION.heatCeil, Math.max(base.heat, base.heat + ESCALATION.heatPerBoard * bump)),
    subFlash: base.subFlash || (on && ceil.subFlash),
    wash: base.wash || (on && ceil.wash),
    bubbles: on ? Math.max(base.bubbles, ceil.bubbles) : base.bubbles,
    crt: on ? Math.max(base.crt, ceil.crt) : base.crt,
    burstPressure: base.burstPressure || (on && ceil.burstPressure),
    ambient: on ? Math.max(base.ambient, ceil.ambient) : base.ambient,
    plainFloor: on ? Math.min(base.plainFloor, ceil.plainFloor) : base.plainFloor,
    // adjacent-only is the gentlest law there is, so it relaxes LAST and only
    // where the tier above would already have relaxed it (t>=3).
    adjacentOnly: base.adjacentOnly
      && !(bump >= ESCALATION.loosenFromBump && ceil.adjacentOnly === false),
    /* THE DECKS RIDE THE SAME CAP. trickster.js gates the fake shuffle, the
     * re-deal and the lie on a TIER, so a ladder that never moved that number
     * would leave the House cards frozen at board 1 while the honest dials
     * climbed. It moves exactly one notch and never further. */
    deckTier: on ? ceilTier : t,
  });
  return out;
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
 *
 * THE CLASS IS BOARDS NOW, so the composite is boards-cleared + accuracy +
 * combo + mutation survival. There is no time term any more and NO timeout
 * clamp: the bell is how a 300s class ends, and clamping it to an automatic C
 * would have graded every single class C. What replaces it is a per-tier
 * EXPECTATION - how many boards a good player clears in the budget - so the
 * bell is scored against what the tier could reasonably have done with it.
 *
 * -------- THE ARITHMETIC (300s budget; the work is shown) -------------------
 * A board costs: preview + the deal cascade + a typical clear + the ceremony.
 *   CLEAR_SEC_PER_PAIR is anchored on the two MEASURED clears we have (tier 1
 *   ~30s for 6 pairs = 5.0s/pair; tier 4 ~73s for 10 pairs = 7.3s/pair) and the
 *   middle two are interpolated.
 *
 *   tier 1: 5.00 preview + 12 x .12 deal + 6 x 5.0 clear + .42 = 36.9s
 *   tier 2: 4.00 preview + 12 x .12 deal + 6 x 5.6 clear + .42 = 39.4s
 *   tier 3: 3.00 preview + 16 x .12 deal + 8 x 6.8 clear + .42 = 59.8s
 *   tier 4: 2.50 preview + 20 x .12 deal + 10 x 7.3 clear + .42 = 78.3s
 *
 * Raw capacity in 300s is 300/cost = 8.1 / 7.6 / 5.0 / 3.8 boards. Nobody plays
 * a whole class at their own typical pace with the ladder climbing under them,
 * so the S gate is that capacity x S_PACE (0.82), rounded:
 *
 *   tier 1: 8.13 x .82 = 6.67 -> 7 boards
 *   tier 2: 7.61 x .82 = 6.24 -> 6 boards
 *   tier 3: 5.02 x .82 = 4.11 -> 4 boards
 *   tier 4: 3.83 x .82 = 3.14 -> 3 boards
 *
 * `boardCostSec` reads the DIALS, not the tier, so a player who chose a 6-pair
 * board at tier 4 is expected to clear MORE of them - which is what stops the
 * board-size setting being a way to farm clears (the below-par A-cap is the
 * shell's separate, unchanged answer to the same setting).
 *
 * -------- THE GATES ---------------------------------------------------------
 * composite = .58 clears + .30 accuracy + .12 combo (+ .03 per tracked, max 3)
 * against grades.js's S .92 / A .75 / B .50.
 *
 *   S at tier 1: 7/7 boards (.58) + accuracy at or above par (.30) + a combo of
 *                8 (.12) = 1.00. Miss one board and 6/7 (.497) needs both the
 *                full combo and the tracked bonus to reach .92 - which is the
 *                point: an S is a clean night, not a lucky one.
 *   A at tier 4: 2/3 boards (.387) + accuracy .40 against par .46 (.261) +
 *                combo 6 (.09) = .738, and two tracked-through-the-static
 *                matches (+.06) carry it to .798 = A.
 *   B          : roughly "half the expected boards at a competent accuracy" -
 *                4/7 at tier 1 with accuracy .42 lands .579.
 *
 * ACCURACY IS SCORED AGAINST PAR, not against 1.0. Concentration accuracy has a
 * ceiling a human cannot reach (a 6-pair board is 6 attempts only if you
 * memorised all twelve faces in five seconds), so a raw pairs/attempts term
 * would have made the accuracy weight unearnable and quietly turned the whole
 * grade into a clears counter.
 *
 * PARTIAL PROGRESS ON THE LIVE BOARD COUNTS, linearly: the bell falling on 5 of
 * 6 pairs is worth .833 of a board. Linear is deliberate - it is the same
 * fraction of the same work, and any curve would only be a second opinion about
 * how hard the last pair is.
 * -------------------------------------------------------------------------- */

/** Anchored on the two measured clears; the middle two are interpolated. */
export const CLEAR_SEC_PER_PAIR = Object.freeze({ 1: 5.0, 2: 5.6, 3: 6.8, 4: 7.3 });
/** The S gate is this fraction of raw capacity (the ladder is climbing). */
export const S_PACE = 0.82;
/** Achievable pairs/attempts for a strong player at each tier. Bigger boards
 *  and more mutations mean a lower reachable accuracy, not a worse player. */
export const PAR_ACCURACY = Object.freeze({ 1: 0.62, 2: 0.58, 3: 0.52, 4: 0.46 });

/** Seconds one whole board costs: preview + deal cascade + clear + ceremony. */
export function boardCostSec(dials) {
  const d = dials || dialsFor(1);
  const preview = Math.max(0, Number(d.previewMs) || 0) / 1000;
  const deal = (Number(d.cells) || 12) * (TIMING.dealStaggerMs / 1000);
  const perPair = CLEAR_SEC_PER_PAIR[d.tier] || CLEAR_SEC_PER_PAIR[1];
  const clear = (Number(d.pairs) || 6) * perPair;
  return preview + deal + clear + (TIMING.ceremonyMs / 1000);
}

/**
 * Boards a good player clears in `budgetSec` - the S gate, in boards.
 * ALWAYS pass BOARD 1's dials: the ladder makes later boards dearer, and that
 * is exactly what S_PACE is already paying for.
 */
export function expectedClears(dials, budgetSec) {
  const budget = Math.max(1, Number(budgetSec) || 300);
  const cost = Math.max(1, boardCostSec(dials));
  return Math.max(1, Math.round((budget / cost) * S_PACE));
}

export const COMPOSITE_WEIGHTS = Object.freeze({
  clears: 0.58,       // boards cleared (+ the live board's fraction) vs expectation
  accuracy: 0.30,     // class-wide pairs / attempts, against the tier's par
  combo: 0.12,        // best combo against the shared cap of 8
  trackedEach: 0.03,  // "tracked through the static", max 3 instances (a bonus)
});
export const TRACKED_CAP = 3;
export const COMBO_CAP = 8;

/**
 * @param {Object} i {tier, boardsCleared, livePairs, liveMatched, matched,
 *                    attempts, maxCombo, tracked, expectedClears, timeout}
 *   `matched` / `attempts` are the CLASS totals (every board summed); the live
 *   board's own two numbers ride `liveMatched` / `livePairs` so the partial can
 *   be priced without double-counting.
 * @returns {{composite:number, clears:number, clearScore:number,
 *            accuracy:number, accuracyScore:number, comboScore:number,
 *            trackedBonus:number, expected:number, timeout:boolean}}
 */
export function compositeFor(i) {
  const src = i || {};
  const tier = Math.max(1, Math.min(4, Math.round(Number(src.tier) || 1)));
  const expected = Math.max(1, Math.round(Number(src.expectedClears) || 1));

  const cleared = Math.max(0, Math.round(Number(src.boardsCleared) || 0));
  const livePairs = Math.max(0, Math.round(Number(src.livePairs) || 0));
  const liveMatched = Math.max(0, Math.min(livePairs, Math.round(Number(src.liveMatched) || 0)));
  const partial = livePairs > 0 ? liveMatched / livePairs : 0;
  const clears = cleared + partial;
  const clearScore = Math.max(0, Math.min(1, clears / expected));

  const matched = Math.max(0, Math.round(Number(src.matched) || 0));
  const attempts = Math.max(matched, Math.round(Number(src.attempts) || 0));
  const accuracy = attempts > 0 ? matched / attempts : 0;
  const par = PAR_ACCURACY[tier] || PAR_ACCURACY[1];
  const accuracyScore = Math.max(0, Math.min(1, accuracy / par));

  const comboScore = Math.max(0, Math.min(1, (Number(src.maxCombo) || 0) / COMBO_CAP));
  const trackedBonus = Math.min(TRACKED_CAP, Math.max(0, Math.round(Number(src.tracked) || 0)))
    * COMPOSITE_WEIGHTS.trackedEach;

  let composite = (COMPOSITE_WEIGHTS.clears * clearScore)
    + (COMPOSITE_WEIGHTS.accuracy * accuracyScore)
    + (COMPOSITE_WEIGHTS.combo * comboScore)
    + trackedBonus;
  composite = Math.max(0, Math.min(1, composite));
  /* NO TIMEOUT CLAMP. The bell is the class's normal end (owner ruling, the
   * class-length wave): it is reported for the share payload and the log, and
   * it changes NOTHING about the letter. The old `Math.min(composite, 0.49)`
   * lived here and would now grade an S run C. */
  return {
    composite, clears, clearScore, expected, partial,
    accuracy, accuracyScore, comboScore, trackedBonus,
    timeout: !!src.timeout,
  };
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

export default {
  dialsFor, dialsForBoard, buildLayout, buildSwapSchedule, buildDriftSchedule,
  boardCostSec, expectedClears, compositeFor,
};
