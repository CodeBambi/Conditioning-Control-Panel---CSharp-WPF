/* ============================================================================
 * stations/breakout/twists/node.js - the pure sim half of the twist.
 *
 * Twist: Node (The Hive). Wires run across the wall to a core. A wire brick with
 * a living 4-neighbour path to a living core is POWERED: dark armoured, three
 * hits, a glow. Snip the path and everything past the cut goes dark and pale and
 * takes one hit. Kill the core and the whole net blows in a wave along the wires.
 *
 * Pure sim: no DOM, no clock, no Math.random. The look lives in node-render.js.
 * The node lane owns this file. See twists/CONTRACT.md.
 * ==========================================================================*/

export const id = 'node';

/** Up, down, left, right. The current never reaches a diagonal and never wraps a row. */
const N4 = [[-1, 0], [1, 0], [0, -1], [0, 1]];

/** A powered wire wears the game's three-hit armoured plate. A cut one is one plain hit. */
export const POWERED_HP = 3;
/** The wave that runs the net when the core dies: a lead-in, then one step per wire distance. */
export const POP_LEAD_S = 0.12;
export const POP_STEP_S = 0.06;

export const isWire = br => !!br && br.alive === true && !!br.wire;
export const isCore = br => !!br && br.alive === true && !!br.core;
/** Anything the current runs through. */
export const isNet = br => isWire(br) || isCore(br);

const centreX = br => br.x + br.w / 2;
const centreY = br => br.y + br.h / 2;
const fresh = () => ({ down: false, cuts: 0, blown: 0 });
/** Per-board state on the game, never module-level, so two games never share a net. */
const stateOf = g => (g.node || (g.node = fresh()));

/**
 * Breadth-first from every living core through living wire bricks, 4-neighbour.
 * Writes `netDepth` on every living net brick (0 at a core, -1 for no path) and
 * pulls the power out of any wire that just lost its path to one.
 *
 * Returns the wires that went dark THIS call, in board order.
 */
export function computePower(g, ctx) {
  const net = g.bricks.filter(isNet);
  for (const br of net) br.netDepth = -1;
  const queue = net.filter(isCore);
  for (const core of queue) core.netDepth = 0;
  for (let i = 0; i < queue.length; i++) {
    const br = queue[i];
    for (const step of N4) {
      const near = ctx.at(br.row + step[0], br.col + step[1]);
      if (isWire(near) && near.netDepth < 0) { near.netDepth = br.netDepth + 1; queue.push(near); }
    }
  }
  const cut = [];
  for (const br of net) {
    if (!br.wire || br.netDepth >= 0 || !br.powered) continue;
    br.powered = false;
    // One plain hit, and the game's armoured plate comes off with the current.
    br.strength = 0; br.hp = 1;
    cut.push(br);
  }
  return cut;
}

/**
 * Split a freshly dark set into its 4-neighbour groups, so a cut that orphans two
 * arms at once reads as two failures and not one. Board order in, board order out.
 */
export function darkGroups(cut, ctx) {
  const left = new Set(cut);
  const groups = [];
  for (const seed of cut) {
    if (!left.has(seed)) continue;
    left.delete(seed);
    const group = [seed];
    for (let i = 0; i < group.length; i++) {
      const br = group[i];
      for (const step of N4) {
        const near = ctx.at(br.row + step[0], br.col + step[1]);
        if (near && left.has(near)) { left.delete(near); group.push(near); }
      }
    }
    groups.push(group);
  }
  return groups;
}

/** The middle of a group: where the lights going out reads from. */
export function groupCentre(group) {
  let x = 0, y = 0;
  for (const br of group) { x += centreX(br); y += centreY(br); }
  return { x: x / group.length, y: y / group.length };
}

/** Recompute, then say so: one `nodeCut` per newly dark group, never one per brick. */
export function repower(g, ctx, options) {
  const silent = !!(options && options.silent);
  const cut = computePower(g, ctx);
  if (!cut.length || silent) return cut;
  const state = stateOf(g);
  for (const group of darkGroups(cut, ctx)) {
    const at = groupCentre(group);
    state.cuts++;
    ctx.emit('nodeCut', { x: at.x, y: at.y, n: group.length });
  }
  return cut;
}

/**
 * SEAM (fixed in the scaffold after this lane reported it): `ctx.breakBrick` is a
 * true kill now, armour and all, so a three-hit powered wire comes down in one
 * call instead of being chipped. This wrapper is kept only as the one place the
 * blast talks to the seam.
 */
function forceBreak(br, ctx) {
  if (!br || !br.alive) return;
  ctx.breakBrick(br, null);
}

/**
 * The core is gone. The net does not go quietly dark, it blows: one `coreDown`
 * carrying the whole count, then one pop per wire on the sim clock, ordered by
 * how far down the wire it sits, so the failure crawls outward from the hole.
 */
export function blowNet(g, br, ctx) {
  const state = stateOf(g);
  state.down = true;
  const doomed = g.bricks.filter(b => isWire(b) && b.powered)
    .sort((a, b) => (a.netDepth - b.netDepth) || (a.row - b.row) || (a.col - b.col));
  state.blown = doomed.length;
  ctx.emit('coreDown', { x: centreX(br), y: centreY(br), n: doomed.length });
  for (const wire of doomed) {
    const depth = Math.max(0, wire.netDepth | 0);
    ctx.schedule(POP_LEAD_S + depth * POP_STEP_S, () => {
      if (!wire.alive) return;
      ctx.emit('nodeZap', { x: centreX(wire), y: centreY(wire), depth });
      forceBreak(wire, ctx);
    });
  }
  return doomed;
}

/** Once, after the authored wall is built: light the net, quietly. */
export function build(g, ctx) {
  g.node = fresh();
  repower(g, ctx, { silent: true });
}

/** A brick broke. Only the net cares, and only while a core still lives. */
export function onBreak(g, br, ball, ctx) {
  if (!br || (!br.wire && !br.core)) return;
  const state = stateOf(g);
  if (state.down) return;                       // the wave is already running: there is nothing left to cut
  if (br.core && !g.bricks.some(isCore)) { blowNet(g, br, ctx); return; }
  repower(g, ctx);
}

/** The board is done: the next one builds its own net. */
export function wallCleared(g) { g.node = fresh(); }

export default {
  id, build, onBreak, wallCleared,
  computePower, repower, blowNet, darkGroups, groupCentre, isWire, isCore, isNet,
  POWERED_HP, POP_LEAD_S, POP_STEP_S,
};
