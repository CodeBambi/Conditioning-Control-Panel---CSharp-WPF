/* ============================================================================
 * ui/drops.js — the bubble-pop item economy, paced by HEAT.
 *
 * THE LOOP: pop bubbles -> build heat -> heat drops items -> throw them.
 *
 *   exec/bubbles.js   dispatches `gg-bubble-pop` {kind, worth, payload, size}
 *   ui/hud.js         hears it and hands the detail to this roller
 *   THIS FILE         adds heat, rolls the chance the heat bought, picks a
 *                     payload kind, credits the charge, arms the slot, and
 *                     spends a chunk of the heat back
 *   ui/arsenal.js     lights the sticker up and counts the stack
 *   ui/hud.js         paints the gauge off `roller.heat`
 *
 * WHY HEAT REPLACED THE FLAT ROLL (owner, 2026-08-04). The old economy was a
 * flat 12%-per-worth coin flip on every pop: invisible, memoryless and
 * unreadable — a player could pop thirty bubbles and see nothing, with no way
 * to tell whether they were unlucky or doing it wrong. Heat is the same economy
 * with a MEMORY and a FACE. Every pop banks progress you can see climbing, the
 * chance ramps as the gauge fills, and a drop spends the heat back down, so
 * drops pace themselves into a visible sawtooth instead of clustering.
 *
 * THE RATE IS CONSERVED, AND THAT IS WHY THE CURVE IS FREE. Over any long run
 * heat in must equal heat out: pops x (worth x HEAT_PER_WORTH) = drops x
 * HEAT_DROP_COST. So drops-per-pop settles at
 *
 *     HEAT_PER_WORTH / HEAT_DROP_COST  x  worth   =   7.5 / 58  =  12.9% x worth
 *
 * NO MATTER what shape the chance curve has (decay and the ceiling shave a
 * little off it). That is the old 12%/worth back, to the percentage point —
 * verified by simulation at several pop rates: 12.0-13.1% for a plain bubble,
 * ~28% for an effect bubble (old flat value 30%). The curve therefore only buys
 * FEEL, never rate: CHANCE_FLOOR/PEAK/CURVE decide how high the gauge rides and
 * how sharp the sawtooth is, and HEAT_PER_WORTH / HEAT_DROP_COST alone decide
 * how often you actually get something. Retune the shape freely; move that
 * ratio only when you mean to move the economy.
 *
 * WHY THE CHARGE STILL MATTERS. Charges are the WIRE truth: the receiving side
 * validates "sender charges >= cost" and rejects anything it does not believe,
 * so an armed sticker with no charge behind it would fire into a rejection. A
 * drop therefore credits `match.creditCharges(cost, 'bubble-drop')` FIRST and
 * only arms if the engine said yes (it refuses outside Live and clamps at the
 * cap). Charges earned the old way — by SURVIVING what they sent you — still
 * accrue, they just do not arm anything: they are headroom, not inventory.
 * A roll the engine or a full arsenal refuses does NOT spend heat: the player
 * got nothing, so the gauge keeps what it earned.
 *
 * RANDOMNESS. Plain Math.random on purpose. This is client-local economy, it
 * crosses no wire and it is not part of the deterministic engine RNG (core/rng
 * vectors must stay reproducible). The `random` option exists so the self-test
 * can drive it; `now` likewise, so decay is testable without a clock.
 *
 * Node-import-safe: no DOM and no engine access at import.
 * ==========================================================================*/

import { GoonMatchPhase } from '../core/contracts.js';
import { S } from './strings.js';

/* ---------------------------------------------------------------------------
 * TUNING — every number the owner may want to move lives HERE and nowhere else.
 * ------------------------------------------------------------------------ */
export const DROP_TUNING = Object.freeze({
  /** Full gauge. Pure scale: the gauge paints heat/HEAT_MAX. */
  HEAT_MAX: 100,
  /** Heat banked per unit of pop worth — HALF of the drop rate (see the header). */
  HEAT_PER_WORTH: 7.5,
  /** Heat a landed drop spends. The OTHER half of the drop rate. */
  HEAT_DROP_COST: 58,
  /**
   * Cooling while you are not popping. Deliberately gentle: at one pop a second
   * this is a ~7% tax on the gain, but it empties a full gauge in ~3 minutes of
   * standing still, so heat reads as something you are holding, not banking.
   */
  HEAT_DECAY_PER_SEC: 0.5,
  /** Drop chance at an empty gauge — small, never zero: a cold pop can still pay. */
  CHANCE_FLOOR: 0.02,
  /** Drop chance at a full gauge. Also the hard ceiling: a drop is never certain. */
  CHANCE_PEAK: 0.60,
  /**
   * Shape of the ramp between them: chance = FLOOR + (PEAK-FLOOR) x fill^CURVE.
   * >1 means the payoff hides in the TOP of the gauge, which is what keeps the
   * needle riding around half full (measured: ~50% average, spread across every
   * decile) instead of hovering just off empty. Rate is unaffected — see header.
   */
  HEAT_CURVE: 3.4,
  /**
   * Documentation of the other side of the seam: exec/bubbles.js stamps
   * POP_WORTH_EFFECT on the five effect kinds and 0 on anything an opponent's
   * BubbleSwarm minted. An effect bubble is worth 2.5 plain ones, in heat and
   * therefore in drops.
   */
  EFFECT_WORTH: 2.5,
  /** Below this, a pop is clutter: no heat, no roll (payload-minted bubbles). */
  MIN_WORTH: 0.01,
  /**
   * Rarity curve — WHICH item, not WHETHER. An item's pick weight is
   * 1 / cost^COST_BIAS, so cheap items are common and expensive ones are rare.
   * 1 = plain inverse cost; raise it to make the heavy hitters rarer still.
   */
  COST_BIAS: 1.6,
  /** Stacks handed out per successful drop. */
  STACK_PER_DROP: 1,
  /** Hard stop on banked stacks of ONE item, so a marathon cannot hoard 40. */
  MAX_STACK: 4,
});

const clamp01 = (n) => (n < 0 ? 0 : n > 1 ? 1 : n);

/** Heat a pop of this worth banks (0 for clutter and malformed detail). */
export function heatGainFor(worth) {
  const w = Number(worth);
  if (!(w > DROP_TUNING.MIN_WORTH)) return 0;
  return w * DROP_TUNING.HEAT_PER_WORTH;
}

/** Drop chance at a given heat level. Monotone, floored, ceilinged. */
export function dropChanceAt(heat) {
  const fill = clamp01((Number(heat) || 0) / DROP_TUNING.HEAT_MAX);
  const { CHANCE_FLOOR: lo, CHANCE_PEAK: hi } = DROP_TUNING;
  return lo + (hi - lo) * Math.pow(fill, DROP_TUNING.HEAT_CURVE);
}

/** Heat left after `ms` of not popping. */
export function decayHeat(heat, ms) {
  const h = Number(heat) || 0;
  const t = Number(ms);
  if (!(t > 0)) return Math.max(0, h);
  return Math.max(0, h - (DROP_TUNING.HEAT_DECAY_PER_SEC * t) / 1000);
}

/** Pick weight of one candidate: cheap items common, expensive rare. */
export function weightOf(cost) {
  const c = Math.max(1, Number(cost) || 1);
  return 1 / Math.pow(c, DROP_TUNING.COST_BIAS);
}

/**
 * Weighted pick over arsenal candidates.
 * @param {Array<{id:string, cost:number, armed?:number}>} candidates
 * @param {number} roll 0..1
 * @returns {object|null} the chosen candidate
 */
export function pickDrop(candidates, roll) {
  const pool = (candidates || []).filter((c) => c && (c.armed | 0) < DROP_TUNING.MAX_STACK);
  if (!pool.length) return null;
  let total = 0;
  for (const c of pool) total += weightOf(c.cost);
  if (!(total > 0)) return pool[0];
  let r = (Number(roll) || 0) * total;
  for (const c of pool) {
    r -= weightOf(c.cost);
    if (r < 0) return c;
  }
  return pool[pool.length - 1];
}

/**
 * @param {object}   o
 * @param {object}   o.match      GoonMatchService (creditCharges + phase)
 * @param {object}   o.arsenal    mountArsenal handle (droppable/armDrop)
 * @param {object}   [o.audio]
 * @param {Function} [o.onLog]
 * @param {Function} [o.random]   () => 0..1, defaults to Math.random
 * @param {Function} [o.now]      () => ms, defaults to Date.now (decay clock)
 * @param {Function} [o.toast]    (text) => void, optional
 */
export function createDropRoller({ match, arsenal, audio = null, onLog = null, random = null,
  now = null, toast = null } = {}) {
  // Resolved per call, never captured: `Math.random` read at construction time
  // would freeze the reference (and make the self-test's scripted RNG a no-op).
  const rnd = typeof random === 'function' ? random : () => Math.random();
  const clock = typeof now === 'function' ? now : () => Date.now();
  const stats = { pops: 0, clutter: 0, rolls: 0, drops: 0, refused: 0 };

  /* THE GAUGE'S STATE — two numbers, and decay is LAZY.
   * There is no timer here: `heat` is whatever was banked, aged by the wall
   * clock at the moment somebody asks. A roller nobody reads costs nothing, a
   * roller read at 5 Hz by the HUD paints a smooth cool-down, and a tab that
   * was backgrounded for a minute comes back correctly cold instead of owing
   * sixty ticks. */
  let heat = 0;
  let heatAt = clock();

  function current() {
    const t = clock();
    const aged = decayHeat(heat, t - heatAt);
    heat = aged;
    heatAt = t;
    return aged;
  }

  function setHeat(v) {
    heat = Math.max(0, Math.min(DROP_TUNING.HEAT_MAX, Number(v) || 0));
    heatAt = clock();
    return heat;
  }

  /** The roll has exactly two audible outcomes: a slot lit up ('gg-drop', the
   *  payoff ding of the whole loop) or the roll WON and could not be taken —
   *  no charge, or the arsenal already full ('gg-drop-dud', a small nothing).
   *  A miss stays silent: most rolls miss, and a sound for "nothing happened"
   *  would be the loudest thing in the match. */
  const sfx = (id) => {
    try { if (audio && typeof audio.sfx === 'function') audio.sfx(id); } catch (_e) { /* stub */ }
  };

  function isLive() {
    const p = match && match.phase;
    return p === GoonMatchPhase.Live || p === GoonMatchPhase.SuddenDeath;
  }

  /**
   * Credit the charge the item will cost to fire. Returns true when the arsenal
   * may arm. Until core/match.js publishes the seam this degrades to "yes" so
   * the loop is playable today; the moment it lands, the engine is authority.
   */
  function credit(cost) {
    if (!match || typeof match.creditCharges !== 'function') return true;
    try { return match.creditCharges(cost | 0, 'bubble-drop') === true; }
    catch (_e) { return false; }
  }

  /**
   * One pop. Safe to call with anything: a malformed detail is a no-op.
   * @param {{kind?:string, worth?:number, payload?:boolean, x?:number, y?:number}} detail
   * @returns {{dropped:boolean, id:string|null, reason:string, heat:number}}
   */
  function onPop(detail) {
    stats.pops++;
    const d = detail || {};
    const gain = heatGainFor(d.worth);
    if (gain <= 0) { stats.clutter++; return { dropped: false, id: null, reason: 'clutter', heat: current() }; }
    if (!isLive()) return { dropped: false, id: null, reason: 'phase', heat: current() };
    if (!arsenal || typeof arsenal.droppable !== 'function') {
      return { dropped: false, id: null, reason: 'no-arsenal', heat: current() };
    }

    // The pop's OWN heat counts toward its own roll: a pop always improves your
    // odds, immediately, which is the whole promise the gauge is making.
    const hot = setHeat(current() + gain);

    stats.rolls++;
    if (rnd() >= dropChanceAt(hot)) return { dropped: false, id: null, reason: 'miss', heat: hot };

    const pick = pickDrop(arsenal.droppable(), rnd());
    if (!pick) return { dropped: false, id: null, reason: 'no-candidate', heat: hot };
    if (!credit(pick.cost)) { stats.refused++; sfx('gg-drop-dud'); return { dropped: false, id: null, reason: 'no-charge', heat: hot }; }

    const from = (typeof d.x === 'number' && typeof d.y === 'number') ? { x: d.x, y: d.y } : null;
    const armed = arsenal.armDrop(pick.id, { count: DROP_TUNING.STACK_PER_DROP, from });
    if (!armed) { stats.refused++; sfx('gg-drop-dud'); return { dropped: false, id: null, reason: 'refused', heat: hot }; }

    // Only a drop the player actually GOT spends the gauge.
    const left = setHeat(hot - DROP_TUNING.HEAT_DROP_COST);
    stats.drops++;
    sfx('gg-drop');
    if (typeof toast === 'function') { try { toast(S.arsenal.dropToast(pick.id)); } catch (_e) { /* never load-bearing */ } }
    if (typeof onLog === 'function') { try { onLog({ t: 'bubble-drop', item: pick.id, kind: d.kind }); } catch (_e) { /* ignore */ } }
    return { dropped: true, id: pick.id, reason: 'drop', heat: left };
  }

  return {
    onPop,
    stats,
    /** Heat right now, decay applied. 0..HEAT_MAX. */
    get heat() { return current(); },
    /** The same thing the gauge paints: 0..1. */
    get heatFraction() { return clamp01(current() / DROP_TUNING.HEAT_MAX); },
    /** Chance the NEXT worth-1 pop lands a drop, for a tooltip or a driver. */
    get dropChance() { return dropChanceAt(current() + DROP_TUNING.HEAT_PER_WORTH); },
    /** Match restart / test hook. */
    resetHeat() { return setHeat(0); },
  };
}

export default createDropRoller;
