/* ============================================================================
 * ui/drops.js — the bubble-pop item economy.
 *
 * THE LOOP: pop bubbles -> earn drops -> throw items at the opponent.
 *
 *   exec/bubbles.js   dispatches `gg-bubble-pop` {kind, worth, payload, size}
 *   ui/hud.js         hears it and hands the detail to this roller
 *   THIS FILE         rolls the chance, picks a payload kind, credits the
 *                     charge on the engine, and arms the slot
 *   ui/arsenal.js     lights the sticker up and counts the stack
 *
 * WHY THE CHARGE STILL MATTERS. Charges are the WIRE truth: the receiving side
 * validates "sender charges >= cost" and rejects anything it does not believe,
 * so an armed sticker with no charge behind it would fire into a rejection. A
 * drop therefore credits `match.creditCharges(cost, 'bubble-drop')` FIRST and
 * only arms if the engine said yes (it refuses outside Live and clamps at the
 * cap). Charges earned the old way — by SURVIVING what they sent you — still
 * accrue, they just do not arm anything: they are headroom, not inventory.
 *
 * RANDOMNESS. Plain Math.random on purpose. This is client-local economy, it
 * crosses no wire and it is not part of the deterministic engine RNG (core/rng
 * vectors must stay reproducible). The `random` option exists so the self-test
 * can drive it.
 *
 * Node-import-safe: no DOM and no engine access at import.
 * ==========================================================================*/

import { GoonMatchPhase } from '../core/contracts.js';
import { S } from './strings.js';

/* ---------------------------------------------------------------------------
 * TUNING — every number the owner may want to move lives HERE and nowhere else.
 * ------------------------------------------------------------------------ */
export const DROP_TUNING = Object.freeze({
  /** Drop chance of a worth-1 pop, i.e. a plain bubble. */
  CHANCE_PER_WORTH: 0.12,
  /** Ceiling, so a future high-worth bubble can never become a guaranteed drop. */
  CHANCE_MAX: 0.60,
  /**
   * Documentation of the other side of the seam: exec/bubbles.js stamps
   * POP_WORTH_EFFECT on the five effect kinds and 0 on anything an opponent's
   * BubbleSwarm minted. 2.5 x 0.12 = 30% for an effect bubble, 0% for clutter.
   */
  EFFECT_WORTH: 2.5,
  /** Below this, a pop is clutter and never rolls (payload-minted bubbles). */
  MIN_WORTH: 0.01,
  /**
   * Rarity curve. An item's pick weight is 1 / cost^COST_BIAS, so cheap items
   * are common and expensive ones are rare. 1 = plain inverse cost; raise it to
   * make the heavy hitters rarer still.
   */
  COST_BIAS: 1.6,
  /** Stacks handed out per successful drop. */
  STACK_PER_DROP: 1,
  /** Hard stop on banked stacks of ONE item, so a marathon cannot hoard 40. */
  MAX_STACK: 4,
});

/** Chance a pop of this worth drops something (0 for clutter). */
export function dropChanceFor(worth) {
  const w = Number(worth);
  if (!(w > DROP_TUNING.MIN_WORTH)) return 0;
  return Math.min(DROP_TUNING.CHANCE_MAX, DROP_TUNING.CHANCE_PER_WORTH * w);
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
 * @param {Function} [o.toast]    (text) => void, optional
 */
export function createDropRoller({ match, arsenal, audio = null, onLog = null, random = null, toast = null } = {}) {
  // Resolved per call, never captured: `Math.random` read at construction time
  // would freeze the reference (and make the self-test's scripted RNG a no-op).
  const rnd = typeof random === 'function' ? random : () => Math.random();
  const stats = { pops: 0, clutter: 0, rolls: 0, drops: 0, refused: 0 };

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
   * @returns {{dropped:boolean, id:string|null, reason:string}}
   */
  function onPop(detail) {
    stats.pops++;
    const d = detail || {};
    const chance = dropChanceFor(d.worth);
    if (chance <= 0) { stats.clutter++; return { dropped: false, id: null, reason: 'clutter' }; }
    if (!isLive()) return { dropped: false, id: null, reason: 'phase' };
    if (!arsenal || typeof arsenal.droppable !== 'function') return { dropped: false, id: null, reason: 'no-arsenal' };

    stats.rolls++;
    if (rnd() >= chance) return { dropped: false, id: null, reason: 'miss' };

    const pick = pickDrop(arsenal.droppable(), rnd());
    if (!pick) return { dropped: false, id: null, reason: 'no-candidate' };
    if (!credit(pick.cost)) { stats.refused++; return { dropped: false, id: null, reason: 'no-charge' }; }

    const from = (typeof d.x === 'number' && typeof d.y === 'number') ? { x: d.x, y: d.y } : null;
    const armed = arsenal.armDrop(pick.id, { count: DROP_TUNING.STACK_PER_DROP, from });
    if (!armed) { stats.refused++; return { dropped: false, id: null, reason: 'refused' }; }

    stats.drops++;
    try { if (audio && typeof audio.sfx === 'function') audio.sfx('gg-drop'); } catch (_e) { /* stub */ }
    if (typeof toast === 'function') { try { toast(S.arsenal.dropToast(pick.id)); } catch (_e) { /* never load-bearing */ } }
    if (typeof onLog === 'function') { try { onLog({ t: 'bubble-drop', item: pick.id, kind: d.kind }); } catch (_e) { /* ignore */ } }
    return { dropped: true, id: pick.id, reason: 'drop' };
  }

  return { onPop, stats };
}

export default createDropRoller;
