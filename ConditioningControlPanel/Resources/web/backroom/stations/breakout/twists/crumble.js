/* ============================================================================
 * stations/breakout/twists/crumble.js - the pure sim half of the twist.
 * Twist: Crumble (Pink Fog). Clay takes three hits and gains a crack per hit. A
 * clay brick with one hit left is precarious; popping one pops every adjacent
 * precarious clay brick, in sequence, and those chain on.
 * Nothing here touches the DOM, the clock or Math.random. See twists/CONTRACT.md.
 * ==========================================================================*/
import { BOARD_COLS } from '../doors.js';

export const id = 'crumble';

/** One link every 85 ms: the chain is a run of notes, never a single thud. */
export const CHAIN_STEP_S = 0.085;
/** Owner call: a shatter never cracks healthy clay beside it. Off, and it stays off. */
export const CRUMBLE_SHOCK = false;
/** Up and down before left and right: the wave reads as a vein lighting, and it is deterministic. */
export const N4 = [[-1, 0], [1, 0], [0, -1], [0, 1]];
/** The prime wave crosses the board in this long a step, and never dawdles longer than the cap. */
const PRIME_STEP_S = 0.035, PRIME_CAP_S = 0.55;

/** A clay brick with one hit left: it wobbles, it sheds dust, and it joins a chain. */
export const precarious = br => !!br && br.alive && !!br.clay && br.hp <= 1;

/** The twist's own state, per game. Timers die with the board, so this is rebuilt in `build`. */
function state(g) {
  if (!g.crumble) g.crumble = { queue: [], running: false, n: 0, t: 0 };
  return g.crumble;
}

/** The authored cell, guarded: `ctx.at` indexes a flat map, so col -1 would wrap onto the row above. */
function cell(ctx, row, col) {
  if (col < 0 || col >= BOARD_COLS || row < 0) return null;
  return ctx.at(row, col);
}

const centre = br => ({ x: br.x + br.w / 2, y: br.y + br.h / 2 });

/** Every precarious clay brick touching this one, in N4 order, that is not already in the queue. */
export function neighbours(ctx, br) {
  const out = [];
  for (const [dr, dc] of N4) {
    const n = cell(ctx, br.row + dr, br.col + dc);
    if (precarious(n) && !n.crumbleQueued) out.push(n);
  }
  return out;
}

/** One link: pop the head of the queue, let its own break feed the tail, and come back in 85 ms. */
function pump(g, ctx) {
  const st = state(g);
  let br = null;
  while (st.queue.length && !br) { const next = st.queue.shift(); if (next && next.alive) br = next; }
  if (!br) {
    st.running = false;
    if (st.n > 0) ctx.emit('clayChainEnd', { n: st.n });
    st.n = 0;
    return;
  }
  st.n++;
  const at = centre(br);
  ctx.emit('clayChain', { x: at.x, y: at.y, n: st.n });
  br.crumbleQueued = false;
  ctx.breakBrick(br, null);                       // this re-enters onBreak and feeds the tail
  ctx.schedule(CHAIN_STEP_S, pump);
}

/** Light the fuse under a clay brick that just died. */
function light(g, br, ctx) {
  const st = state(g);
  const next = neighbours(ctx, br);
  for (const n of next) { n.crumbleQueued = true; st.queue.push(n); }
  if (!st.running && st.queue.length) { st.running = true; ctx.schedule(CHAIN_STEP_S, pump); }
}

/**
 * Prime every living clay brick to precarious (the `P` key, and anything else
 * that wants the whole board wobbly). The hp flip is synchronous, so the board
 * is correct the instant this returns; only the `clayReady` ticks ripple, row by
 * row, so the ear hears the wave cross the wall. One `clayPrimed { n }` closes it.
 * The keys lane calls this, so the signature and the events are frozen.
 */
export function prime(g, ctx) {
  const moved = [];
  for (const br of g.bricks) {
    if (!br.alive || !br.clay || br.hp <= 1) continue;
    br.hp = 1;
    moved.push(br);
  }
  moved.sort((a, b) => (a.row - b.row) || (a.col - b.col));
  const canStagger = typeof ctx.schedule === 'function';
  moved.forEach((br, i) => {
    const at = centre(br);
    const wait = Math.min(PRIME_CAP_S, i * PRIME_STEP_S);
    if (canStagger && wait > 0) ctx.schedule(wait, () => ctx.emit('clayReady', { x: at.x, y: at.y }));
    else ctx.emit('clayReady', at);
  });
  if (moved.length) ctx.emit('clayPrimed', { n: moved.length });
  return moved.length;
}

/** Called once after the authored wall is built: the board is new, so nothing is queued or lit. */
export function build(g, ctx) {
  g.crumble = { queue: [], running: false, n: 0, t: 0 };
  for (const br of g.bricks) if (br.clay) { br.crumbleQueued = false; br.crumbleWob = 0; }
}

/** A hit that did not break it. Clay counts its cracks out loud, and says when it is ready. */
export function onHit(g, br, ball, ctx) {
  if (!br || !br.clay) return;
  const at = centre(br);
  ctx.emit('clayCrack', { x: at.x, y: at.y, hp: br.hp });
  if (br.hp <= 1) ctx.emit('clayReady', at);
}

/** A clay brick died: every precarious neighbour joins the run. */
export function onBreak(g, br, ball, ctx) {
  if (!br || !br.clay) return;
  br.crumbleQueued = false;
  if (CRUMBLE_SHOCK) {                            // owner call: off. Kept as the one place it would live.
    for (const [dr, dc] of N4) {
      const n = cell(ctx, br.row + dr, br.col + dc);
      if (n && n.alive && n.clay && n.hp > 1) { n.hp--; ctx.emit('clayCrack', { ...centre(n), hp: n.hp }); }
    }
  }
  light(g, br, ctx);
}

/**
 * The wobble. A precarious brick is nudged a pixel or two on its own phase, through the
 * push the renderer already honours, so the whole face moves and not a decal on top of it.
 * Reduced motion holds every brick still; the cracks and the pale face still say "ready".
 */
export function update(g, dt, ctx) {
  const st = state(g);
  const t = st.t = (st.t || 0) + (Number(dt) || 0);
  for (const br of g.bricks) {
    if (!br.clay || !br.push) continue;
    const had = br.crumbleWob || 0;
    if (had) { br.push.dx -= had * 0.9; br.push.dy -= had; }   // undo last frame's nudge, whoever recomputed push
    if (!precarious(br) || g.reduced) { br.crumbleWob = 0; continue; }
    const phase = ((br.row + 1) * 2.7 + (br.col + 1) * 1.31) % 6.283;
    const wob = Math.sin(t * 7.3 + phase) * 0.9 + Math.sin(t * 11.9 + phase * 2) * 0.35;
    br.push.dx += wob * 0.9; br.push.dy += wob;
    br.crumbleWob = wob;
  }
}

export default { id, build, onHit, onBreak, update, prime, precarious, neighbours, CHAIN_STEP_S, CRUMBLE_SHOCK };
