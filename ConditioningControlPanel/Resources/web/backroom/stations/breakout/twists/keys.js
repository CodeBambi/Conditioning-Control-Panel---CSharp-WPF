/* ============================================================================
 * stations/breakout/twists/keys.js - the pure sim half of the twist.
 * Twist: Keys and Gates (Lock and Key).
 *
 * Two loot boxes stand sealed behind gate steel: a gold box (`M`) and a cyan
 * one (`W`). The ball cannot get in. Lower on the wall sits a key in each trim,
 * and a third, cracked key (`P`) that primes the wax seal instead.
 *
 * Break a key and its colour lets go: `keyTurn` first (the lock turning), then
 * `gateOpen` with how many plates are moving, then those plates come down one
 * after another in a ripple that runs OUTWARD FROM THE KEY, so the eye follows
 * the turn to the box it opened. A gate the ball hits while it is still locked
 * rattles in its frame, which is the whole tutorial.
 *
 * Pure sim: no DOM, no clock, no Math.random. Everything visible lives in
 * `g.keysTwist` (beams, boxes, the seal) and is drawn by keys-render.js.
 * See twists/CONTRACT.md.
 * ==========================================================================*/

import { prime } from './crumble.js';

export const id = 'keys';

/** The beat between the key turning and the first plate letting go. */
export const GATE_LEAD_S = 0.16;
/** One plate after another, outward from the key. Fast: a whole box is under a fifth of a second. */
export const GATE_STEP_S = 0.045;
/** How long the beam from the key to what it opened stays up. */
export const BEAM_LIFE_S = 0.45;
/** The glass over a loot box melting away once its gates are moving. */
export const SEAL_FADE_S = 0.35;
/** A locked gate rattling in its frame after a ball hits it. */
export const LOCK_SHAKE_S = 0.26;

const centreX = br => br.x + br.w / 2;
const centreY = br => br.y + br.h / 2;
const beam = (x1, y1, x2, y2, gate) => ({ x1, y1, x2, y2, gate, life: BEAM_LIFE_S, max: BEAM_LIFE_S });
const mean = (list, of) => list.reduce((sum, br) => sum + of(br), 0) / list.length;

/** The twist's own state, made once per board by `build`. Null on any other board. */
function state(g) { return g && g.keysTwist ? g.keysTwist : null; }

/**
 * The order the plates let go in: nearest the key first, and a dead-stable
 * tiebreak (row then column) so the same board always ripples the same way.
 */
export function gateOrder(gates, kx, ky) {
  return gates
    .map(br => ({ br, d: Math.hypot(centreX(br) - kx, centreY(br) - ky) }))
    .sort((a, b) => (a.d - b.d) || (a.br.row - b.br.row) || (a.br.col - b.br.col))
    .map(e => e.br);
}

/**
 * A loot box: the rectangle the gate plates of one trim enclose, and the prize
 * bricks standing inside it. Measured once, at build, from the plates' own
 * cells, so it survives every plate coming down.
 */
export function boxesFrom(bricks, at) {
  const byGate = new Map();
  for (const br of bricks) if (br.gate) {
    if (!byGate.has(br.gate)) byGate.set(br.gate, []);
    byGate.get(br.gate).push(br);
  }
  const boxes = [];
  for (const [gate, plates] of byGate) {
    const r0 = Math.min(...plates.map(b => b.row)), r1 = Math.max(...plates.map(b => b.row));
    const c0 = Math.min(...plates.map(b => b.col)), c1 = Math.max(...plates.map(b => b.col));
    const inside = [];
    for (let r = r0; r <= r1; r++) for (let c = c0; c <= c1; c++) {
      const br = typeof at === 'function' ? at(r, c) : null;
      if (br && br.gate !== gate) inside.push(br);
    }
    if (!inside.length) continue;                 // a wall of plates with nothing behind it is not a box
    const x0 = Math.min(...inside.map(b => b.x)), y0 = Math.min(...inside.map(b => b.y));
    const x1 = Math.max(...inside.map(b => b.x + b.w)), y1 = Math.max(...inside.map(b => b.y + b.h));
    const loot = inside.find(b => b.powerup) || null;
    boxes.push({ gate, x: x0, y: y0, w: x1 - x0, h: y1 - y0, seal: 1, opening: false, prize: loot ? loot.powerup : null });
  }
  return boxes;
}

/** Called once after the authored wall is built. */
export function build(g, ctx) {
  g.keysTwist = { t: 0, beams: [], boxes: boxesFrom(g.bricks, ctx && ctx.at), opened: {} };
}

/** A ball on a locked plate: it rattles, and that is the only lesson the board teaches. */
export function onHit(g, br, ball, ctx) {
  if (!br || !br.gate) return;
  br.lockT = LOCK_SHAKE_S;
}

/** A key broke. Turn the lock, then let its colour go. */
export function onBreak(g, br, ball, ctx) {
  if (!br || !br.key) return;
  const st = state(g);
  const kx = centreX(br), ky = centreY(br), gate = br.key;
  ctx.emit('keyTurn', { x: kx, y: ky, gate });

  if (gate === 'clay') {
    const clay = g.bricks.filter(b => b.alive && b.clay);
    if (clay.length && st) st.beams.push(beam(kx, ky, mean(clay, centreX), mean(clay, centreY), 'clay'));
    prime(g, ctx);                                // the crumble lane owns the wax seal, not this one
    return;
  }

  const gates = gateOrder(g.bricks.filter(b => b.alive && b.gate === gate), kx, ky);
  if (!gates.length) return;                      // the lock was already open: the turn is all there is
  const bx = mean(gates, centreX), by = mean(gates, centreY);
  if (st) {
    st.opened[gate] = true;
    st.beams.push(beam(kx, ky, bx, by, gate));
    for (const box of st.boxes) if (box.gate === gate) box.opening = true;
  }
  ctx.emit('gateOpen', { gate, n: gates.length, x: bx, y: by });
  gates.forEach((plate, i) => {
    plate.gateOpening = i;                        // the plates ahead of the ripple brighten, in order
    ctx.schedule(GATE_LEAD_S + i * GATE_STEP_S, () => ctx.breakBrick(plate, null));
  });
}

/** Beams fade, seals melt, a rattled plate settles. Nothing here decides anything. */
export function update(g, dt, ctx) {
  const st = state(g);
  if (!st) return;
  st.t += dt;
  if (st.beams.length) {
    for (const b of st.beams) b.life -= dt;
    st.beams = st.beams.filter(b => b.life > 0);
  }
  for (const box of st.boxes) if (box.opening && box.seal > 0) box.seal = Math.max(0, box.seal - dt / SEAL_FADE_S);
  for (const br of g.bricks) if (br.lockT > 0) br.lockT = Math.max(0, br.lockT - dt);
}

/** The board is going: nothing of this one leaks into the next. */
export function wallCleared(g) { if (g) g.keysTwist = null; }

export default { id, build, onHit, onBreak, update, wallCleared };
