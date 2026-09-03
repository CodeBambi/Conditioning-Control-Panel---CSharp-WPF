/* ============================================================================
 * race/score.js - The Caucus Race ledger: score, combo, the multiplier ladder,
 * THE BANK at the Tea Garden, near-miss ALMOSTs and the jackpot ladder.
 * Implements CONTRACT.md section `race/score.js`.
 *
 * No renderer, no DOM: pure state plus an event stream a HUD reacts to.
 * Nothing here ever subtracts: a miss or a timeout only lets the combo go
 * (mult back to x1), the score never moves down (no-lose contract).
 *
 * Pass three additions (all additive):
 *   pop(points, kindId, { inverted })  upside down (THE BIG WHEEL) pops count double; the `pop`
 *                                      event carries the base `gain` plus `bonus`, and a note says so
 *   setInverted(on)                    kart.js `inverted` events land here; leaving the wheel with
 *                                      FULL_CIRCLE_POPS or more inverted pops pays the "full circle"
 *   trick(points, name)                a ramp trick: points * mult, emits `trick`
 *   drainNotes()                       the HUD lines onScore cannot phrase itself ([{text, kind, mood, sfx}])
 * ==========================================================================*/

import { MULT_LADDER, COMBO_HOLD_SEC } from './consts.js';
import { KIND_BY_ID } from './bubbleKinds.js';

const BEST_KEY = 'race.best';
const NEAR_MISS_POINTS = 25;
const JACKPOT_COMBOS = [25, 50, 100];           // combo rungs that fire a major jackpot on their own
const JACKPOT_BONUS = { minor: 100, major: 250, royal: 1000 };
const FULL_CIRCLE_POPS = 3, FULL_CIRCLE_BONUS = 300;

const ladderMult = (combo) => { let m = 1; for (const [at, mult] of MULT_LADDER) if (combo >= at) m = mult; return m; };
const ladderStep = (combo) => MULT_LADDER.some(([at]) => at === combo && at > 0);
function loadBest() { try { const v = Number(localStorage.getItem(BEST_KEY)); return isFinite(v) && v > 0 ? v : 0; } catch (e) { return 0; } }
function saveBest(v) { try { localStorage.setItem(BEST_KEY, String(v)); } catch (e) { /* private mode */ } }

export function createScore() {
  const state = { score: 0, combo: 0, mult: 1, bank: 0, banked: 0, best: loadBest(),
    popped: 0, treats: 0, effects: 0, nearMisses: 0, bestCombo: 0,
    inverted: false, invertedPops: 0, tricks: 0, trickPoints: 0 };
  const notes = [];
  const note = (text, kind = 'pop', mood = null, sfx = null) => { notes.push({ text, kind, mood, sfx }); };
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

  function pop(points, kindId, opts) {
    const k = KIND_BY_ID[kindId];
    const inverted = opts && 'inverted' in opts ? !!opts.inverted : state.inverted;
    state.combo++; hold = COMBO_HOLD_SEC;
    if (state.combo > state.bestCombo) state.bestCombo = state.combo;
    const step = setMult() || ladderStep(state.combo);
    const gain = Math.round((Number(points) || 0) * state.mult);
    const bonus = inverted ? gain : 0;                     // upside down: the pop counts twice
    state.score += gain + bonus; state.popped++;
    if (k && k.kind === 'effect') state.effects++; else state.treats++;
    if (inverted) { state.invertedPops++; if (bonus > 0) note(`upside down +${bonus}`, 'pop'); }
    emit({ type: 'pop', kindId, points, gain, bonus, inverted, combo: state.combo, mult: state.mult, score: state.score });
    emit({ type: 'combo', combo: state.combo, step, mult: state.mult, hold });
    if (JACKPOT_COMBOS.includes(state.combo)) jackpot('major');
    return gain + bonus;
  }
  /** kart.js says the road rolled past 120 degrees (on) or back (off). Off pays the full circle. */
  function setInverted(on) {
    on = !!on;
    if (on === state.inverted) return;
    state.inverted = on;
    if (on) { state.invertedPops = 0; return; }
    if (state.invertedPops >= FULL_CIRCLE_POPS) {
      const gain = Math.round(FULL_CIRCLE_BONUS * state.mult);
      state.score += gain;
      note(`full circle +${gain}`, 'jackpot', 'jackpot', 'golden_pop');
      emit({ type: 'fullCircle', gain, pops: state.invertedPops, score: state.score });
    }
  }
  /** A ramp trick landed in the ledger: points * mult, never a combo step (the pops own the ladder). */
  function trick(points, name) {
    const gain = Math.round((Number(points) || 0) * state.mult);
    state.score += gain; state.tricks++; state.trickPoints += gain;
    emit({ type: 'trick', name, gain, mult: state.mult, score: state.score });
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
    Object.assign(state, { score: 0, combo: 0, mult: 1, bank: 0, banked: 0, popped: 0, treats: 0, effects: 0, nearMisses: 0, bestCombo: 0,
      inverted: false, invertedPops: 0, tricks: 0, trickPoints: 0 });
    hold = 0; freezeSec = 0; boost = 1; boostSec = 0; notes.length = 0;
  }

  return {
    state, pop, miss, nearMiss, bank, jackpot, tick, reset, setInverted, trick,
    onEvent(cb) { if (typeof cb === 'function') cbs.push(cb); },
    /** Pending HUD notes (upside down, full circle, ...), oldest first; the queue empties. */
    drainNotes() { return notes.splice(0, notes.length); },
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
  // ladder [[0,1],[3,2],[8,3],[15,4],[25,6],[40,8]]: pops 1-2 at x1, 3-7 at x2, 8+ at x3
  for (let i = 0; i < 5; i++) s.pop(10, 'treat');
  console.assert(s.state.combo === 5 && s.state.mult === 2 && s.state.score === 80, 'ladder x2 at 3');
  for (let i = 0; i < 3; i++) s.pop(10, 'treat');
  console.assert(s.state.combo === 8 && s.state.mult === 3 && s.state.score === 80 + 20 + 20 + 30, 'ladder x3 at 8');
  s.tick(COMBO_HOLD_SEC + 0.01);
  console.assert(s.state.combo === 0 && s.state.mult === 1, 'combo lets go');
  s.pop(15, 'flash'); console.assert(s.state.effects === 1 && s.state.treats === 8, 'kind tally');
  const b = s.bank(); console.assert(b === 165 && s.state.score === 0 && s.state.banked === 165, 'bank');
  console.assert(ev.includes('mult') && ev.includes('bank') && ev.includes('combo'), 'events');
  // upside down: pops double, three of them in one wheel pass pay the full circle on the way out
  s.setInverted(true); const g = s.pop(10, 'treat'); console.assert(g === 20 && s.state.score === 20, 'inverted pop doubles');
  s.pop(10, 'treat'); s.pop(10, 'treat'); s.setInverted(false);
  console.assert(s.state.score === 20 + 40 + 40 + 300 * 2 && ev.includes('fullCircle'), 'full circle');
  console.assert(s.drainNotes().map((n) => n.text).join('|') === 'upside down +20|upside down +40|upside down +40|full circle +600' && s.drainNotes().length === 0, 'notes drain once');
  console.assert(s.pop(10, 'treat', { inverted: true }) === 40 && s.pop(10, 'treat') === 20, 'opts.inverted overrides');
  console.assert(s.trick(150, 'spin left') === 300 && s.state.tricks === 1, 'trick pays points * mult');
  console.log('score.js self-check ok', s.state);
}
