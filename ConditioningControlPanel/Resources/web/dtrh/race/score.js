/* ============================================================================
 * race/score.js - The Caucus Race ledger: score, combo, the multiplier ladder,
 * THE BANK at the Tea Garden, near-miss ALMOSTs and the jackpot ladder.
 * Implements CONTRACT.md section `race/score.js`.
 *
 * No renderer, no DOM: pure state plus an event stream a HUD reacts to.
 * Nothing here ever subtracts: a miss or a timeout only lets the combo go
 * (mult back to x1), the score never moves down (no-lose contract).
 * ==========================================================================*/

import { MULT_LADDER, COMBO_HOLD_SEC } from './consts.js';
import { KIND_BY_ID } from './bubbleKinds.js';

const BEST_KEY = 'race.best';
const NEAR_MISS_POINTS = 25;
const JACKPOT_COMBOS = [25, 50, 100];           // combo rungs that fire a major jackpot on their own
const JACKPOT_BONUS = { minor: 100, major: 250, royal: 1000 };

const ladderMult = (combo) => { let m = 1; for (const [at, mult] of MULT_LADDER) if (combo >= at) m = mult; return m; };
const ladderStep = (combo) => MULT_LADDER.some(([at]) => at === combo && at > 0);
function loadBest() { try { const v = Number(localStorage.getItem(BEST_KEY)); return isFinite(v) && v > 0 ? v : 0; } catch (e) { return 0; } }
function saveBest(v) { try { localStorage.setItem(BEST_KEY, String(v)); } catch (e) { /* private mode */ } }

export function createScore() {
  const state = { score: 0, combo: 0, mult: 1, bank: 0, banked: 0, best: loadBest(),
    popped: 0, treats: 0, effects: 0, nearMisses: 0, bestCombo: 0 };
  let hold = 0;          // seconds of combo left before it lets go
  let freezeSec = 0;     // pocket_watch: the hold timer stands still
  let boost = 1, boostSec = 0;   // lucky_star: a temporary multiplier on top of the ladder
  const cbs = [];
  const emit = (ev) => { for (const cb of cbs) { try { cb(ev); } catch (e) { /* listener bug, not ours */ } } };

  function touchBest() {
    const total = state.banked + state.score;
    if (total > state.best) { state.best = total; saveBest(total); }
  }
  /** Recompute the effective multiplier; emits `mult` when it changes. */
  function setMult() {
    const to = ladderMult(state.combo) * boost;
    if (to === state.mult) return false;
    const from = state.mult; state.mult = to;
    emit({ type: 'mult', from, to, combo: state.combo });
    return true;
  }
  function release(reason) {
    const lost = state.combo;
    state.combo = 0; hold = 0;
    emit({ type: 'combo', combo: 0, step: false, lost, reason });
    setMult();
    return lost;
  }

  function pop(points, kindId) {
    const k = KIND_BY_ID[kindId];
    state.combo++; hold = COMBO_HOLD_SEC;
    if (state.combo > state.bestCombo) state.bestCombo = state.combo;
    const step = setMult() || ladderStep(state.combo);
    const gain = Math.round((Number(points) || 0) * state.mult);
    state.score += gain; state.popped++;
    if (k && k.kind === 'effect') state.effects++; else state.treats++;
    emit({ type: 'pop', kindId, points, gain, combo: state.combo, mult: state.mult, score: state.score });
    emit({ type: 'combo', combo: state.combo, step, mult: state.mult, hold });
    if (JACKPOT_COMBOS.includes(state.combo)) jackpot('major');
    return gain;
  }
  /** A treat slipped behind the kart: the streak gets a shiver, the score does not move. */
  function miss() {
    const from = state.mult;
    const lost = state.combo > 0 ? release('miss') : 0;
    emit({ type: 'miss', comboLost: lost, multFrom: from, mult: state.mult });
  }
  function nearMiss() {
    const gain = Math.round(NEAR_MISS_POINTS * state.mult);
    state.score += gain; state.nearMisses++;
    emit({ type: 'almost', gain, nearMisses: state.nearMisses, score: state.score });
    return gain;
  }
  /** THE BANK: the run's score leaves the road and lands in `banked` (Tea Garden gate). */
  function bank() {
    const amount = state.score;
    state.banked += amount; state.bank = amount; state.score = 0;
    touchBest();
    emit({ type: 'bank', amount, banked: state.banked, combo: state.combo, best: state.best });
    return amount;
  }
  function jackpot(tier = 'minor') {
    const gain = Math.round((JACKPOT_BONUS[tier] || JACKPOT_BONUS.minor) * state.mult);
    state.score += gain;
    emit({ type: 'jackpot', tier, gain, combo: state.combo, mult: state.mult, score: state.score });
    return gain;
  }
  function tick(dt) {
    if (boostSec > 0) { boostSec -= dt; if (boostSec <= 0) { boost = 1; boostSec = 0; setMult(); } }
    if (state.combo <= 0) return;
    if (freezeSec > 0) { freezeSec -= dt; return; }
    hold -= dt;
    if (hold <= 0) release('timeout');
  }
  function reset() {
    touchBest();
    Object.assign(state, { score: 0, combo: 0, mult: 1, bank: 0, banked: 0, popped: 0, treats: 0, effects: 0, nearMisses: 0, bestCombo: 0 });
    hold = 0; freezeSec = 0; boost = 1; boostSec = 0;
  }

  return {
    state, pop, miss, nearMiss, bank, jackpot, tick, reset,
    onEvent(cb) { if (typeof cb === 'function') cbs.push(cb); },
    // items.js hooks (additive to the contract): pocket_watch + lucky_star
    freezeCombo(sec) { freezeSec = Math.max(freezeSec, Number(sec) || 0); },
    boostMult(mult, sec) { boost = Math.max(1, Number(mult) || 1); boostSec = Number(sec) || 0; setMult(); },
    /** 0..1 fraction of the combo hold still left, for a HUD ring. */
    holdLeft() { return state.combo > 0 ? Math.max(0, hold / COMBO_HOLD_SEC) : 0; },
  };
}

// self-check (node only): `RACE_SELFCHECK=1 node --input-type=module -e "import './score.js'"`
if (typeof process !== 'undefined' && process.env && process.env.RACE_SELFCHECK) {
  const s = createScore(); const ev = [];
  s.onEvent((e) => ev.push(e.type));
  for (let i = 0; i < 5; i++) s.pop(10, 'treat');
  console.assert(s.state.combo === 5 && s.state.mult === 2 && s.state.score === 60, 'ladder x2 at 5');
  s.tick(COMBO_HOLD_SEC + 0.01);
  console.assert(s.state.combo === 0 && s.state.mult === 1, 'combo lets go');
  s.pop(15, 'flash'); console.assert(s.state.effects === 1 && s.state.treats === 5, 'kind tally');
  const b = s.bank(); console.assert(b === 75 && s.state.score === 0 && s.state.banked === 75, 'bank');
  console.assert(ev.includes('mult') && ev.includes('bank') && ev.includes('combo'), 'events');
  console.log('score.js self-check ok', s.state);
}
