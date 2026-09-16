/* ============================================================================
 * auto.js - the roll. "Load your gifs and press the button."
 *
 * autoCompose(state, seed) -> patch. Pure and deterministic from the seed,
 * the tile count, the media aspects, the canvas length and the orientation:
 * same code, same media, same composition. The patch is applied by the
 * project's roll() as one undo step: { layout: { mode, stageMs }, loop,
 * blocks }. Blocks come only from the retained effects (tint, spiral, focus,
 * glitch). Never a drain. Never a caption: that is the Caption step's job.
 *
 * Layouts come from the registry in layout.js, so a new layout joins the roll
 * by taking a row there. The weights table below is the whole tuning surface.
 * ==========================================================================*/

import { rngFrom } from './rng.js';
import { createBlock, clampBlock, defaultParams } from './blocks.js';
import { LAYOUTS, clampCount } from './layout.js';
import { framesForSeconds } from './clock.js';

/* --------------------------------------------------------------- weights -*/

export const AUTO_WEIGHTS = {
  // layout weight by tile count band. A layout the tables know but a band
  // leaves out does not roll in that band; a registered layout no band has
  // heard of (a new one) rolls at `layoutDefault` wherever it has enough tiles
  layouts: [
    { maxTiles: 1, table: { mirror: 5, flat: 2, tunnel: 4, pinwheel: 4 } },
    { maxTiles: 3, table: { grow: 4, spread: 4, stack: 3, flat: 2, mirror: 2, slide: 2, shuffle: 1, swallow: 3, cover: 2, ripple: 1, tunnel: 2, deal: 2, carousel: 2, hop: 1, kenburns: 1, pinwheel: 1 } },
    { maxTiles: 8, table: { grow: 4, slide: 4, shuffle: 3, stack: 3, spread: 3, flat: 1, mirror: 1, ripple: 3, swallow: 2, cover: 2, deal: 2, carousel: 2, hop: 3, kenburns: 3 } },
  ],
  layoutDefault: 2,
  // a dear layout is a lot of rects a frame, which costs bytes in the gif.
  // With Discord-safe on it rolls half as often
  dearPenalty: 0.5,
  // aspect nudge: media shaped like the canvas fill it flat; the other shape grows in
  aspectNudge: 1,
  // stage length by tile count band: shorter with more gifs
  stageMs: [
    { maxTiles: 3, options: [600, 900] },
    { maxTiles: 5, options: [400, 600] },
    { maxTiles: 8, options: [400] },
  ],
  // how many effect blocks
  blockCount: { 1: 3, 2: 4, 3: 2 },
  // which effects, picked without repeats (so never two spirals)
  effects: { tint: 4, glitch: 3, spiral: 2, focus: 2 },
  tintMode: { creep: 3, wash: 1 },
  tintColour: { pink: 3, lavender: 2, gold: 1 },
  glitchMode: { ghost: 3, double: 2 },
  focusMode: { ring: 3, dark: 1 },
  spiralMode: { over: 3, through: 1 },
  // block spans as a fraction of the loop
  span: {
    creep: [0.35, 0.6],
    wash: [0.25, 0.4],
    spiral: [0.3, 0.5],
    glitch: [0.3, 0.5],
    focus: [0.4, 0.6],
  },
  // knobs roll in the middle band, never the extremes
  strength: [35, 70],
  // budget: covered frames at most this share of the loop, and one clean second
  coverage: 0.7,
  cleanSeconds: 1,
  cleanAtHead: 0.65,
  loop: { clean: 40, snap: 30, seamless: 30 },
  // snap only when a tint ends within this many frames of the loop end
  snapWindow: 3,
  rerolls: 3,
};

/* --------------------------------------------------------------- helpers -*/

function weighted(r, table) {
  const keys = Object.keys(table).filter((k) => table[k] > 0);
  let total = 0;
  for (const k of keys) total += table[k];
  let x = r.next() * total;
  for (const k of keys) { x -= table[k]; if (x < 0) return k; }
  return keys[keys.length - 1];
}

function band(list, n) {
  for (const b of list) if (n <= b.maxTiles) return b;
  return list[list.length - 1];
}

/** Frames covered by at least one block. */
export function coverageOf(blocks, frames) {
  const on = new Uint8Array(Math.max(1, frames));
  for (const b of blocks) for (let i = Math.max(0, b.start); i < Math.min(frames, b.end); i++) on[i] = 1;
  let c = 0;
  for (let i = 0; i < on.length; i++) c += on[i];
  return c / on.length;
}

/** The longest run of frames with nothing over the gifs. */
export function longestClean(blocks, frames) {
  const on = new Uint8Array(Math.max(1, frames));
  for (const b of blocks) for (let i = Math.max(0, b.start); i < Math.min(frames, b.end); i++) on[i] = 1;
  let best = 0, run = 0;
  for (let i = 0; i < on.length; i++) { run = on[i] ? 0 : run + 1; if (run > best) best = run; }
  return best;
}

/** True when the patch clears the bar. */
export function passesGate(patch, frames, fps) {
  const W = AUTO_WEIGHTS;
  const blocks = patch.blocks || [];
  if (blocks.length < 1 || blocks.length > 3) return false;
  for (const b of blocks) {
    if (b.start < 0 || b.end > frames || b.end - b.start < 1) return false;
    if (b.effect === 'drain' || b.effect === 'caption') return false;
  }
  if (blocks.filter((b) => b.effect === 'spiral').length > 1) return false;
  if (coverageOf(blocks, frames) > W.coverage) return false;
  if (longestClean(blocks, frames) < Math.round(W.cleanSeconds * fps)) return false;
  return true;
}

/* --------------------------------------------------------------- compose -*/

/**
 * One candidate from one seed. The gate runs in autoCompose, which retries
 * with seed + 1 a few times before settling for the last candidate.
 */
function candidate(state, seed) {
  const W = AUTO_WEIGHTS;
  const frames = state.frames || framesForSeconds(5);
  const fps = state.fps || 15;
  const n = clampCount(Math.max(1, (state.tiles || []).length));
  const orientation = state.orientation || 'landscape';
  const r = rngFrom(seed, 'auto:' + n);

  // layout: the registry filtered by tile count, weighted by the band table
  const table = {};
  const row = band(W.layouts, n).table;
  const known = new Set(W.layouts.flatMap((b) => Object.keys(b.table)));
  for (const l of LAYOUTS) {
    if (n < (l.minTiles || 1)) continue;
    if (row[l.id] != null) table[l.id] = row[l.id];
    else if (!known.has(l.id)) table[l.id] = W.layoutDefault;
  }
  // aspect nudge: media that match the canvas shape sit well flat, the other shape grows in
  const wide = orientation === 'landscape';
  const aspects = (state.media || []).map((m) => (m && m.w && m.h) ? m.w / m.h : null).filter(Boolean);
  if (aspects.length) {
    const match = aspects.filter((a) => (a > 1) === wide).length;
    const key = match * 2 >= aspects.length ? 'flat' : 'grow';
    if (table[key] != null) table[key] += W.aspectNudge;
  }
  // Discord-safe wants a smaller gif, so the expensive layouts roll less
  if (state.discordSafe) {
    for (const l of LAYOUTS) {
      if (l.cost === 'dear' && table[l.id] != null) table[l.id] *= W.dearPenalty;
    }
  }
  const mode = weighted(r, table);
  const stageMs = r.pick(band(W.stageMs, n).options);

  // the clean second: at the head or the tail of the loop; blocks stay off it
  const clean = Math.round(W.cleanSeconds * fps);
  const atHead = r.chance(W.cleanAtHead);
  const lo = atHead ? clean : 0;
  const hi = atHead ? frames : frames - clean;
  const room = Math.max(1, hi - lo);

  // effects, without repeats
  const count = Math.min(Number(weighted(r, W.blockCount)), room > 4 ? 3 : 1);
  const pool = Object.assign({}, W.effects);
  if (n < 2) delete pool.focus; // the path crosses tiles, one tile has nothing to cross
  const picks = [];
  while (picks.length < count && Object.keys(pool).length) {
    const e = weighted(r, pool);
    picks.push(e);
    delete pool[e];
  }

  const blocks = [];
  for (const effect of picks) {
    let modeName, params = defaultParams(effect), spanKey = effect;
    if (effect === 'tint') {
      modeName = weighted(r, W.tintMode);
      spanKey = modeName;
      params.colour = weighted(r, W.tintColour);
      params.corner = r.pick(['tl', 'tr', 'bl', 'br']);
      params.intensity = r.int(W.strength[0], W.strength[1]);
    } else if (effect === 'spiral') {
      modeName = weighted(r, W.spiralMode);
      params.strength = r.int(W.strength[0], W.strength[1]);
      params.speed = r.int(W.strength[0], W.strength[1]);
      params.style = r.int(0, 2);
      params.dir = r.chance(0.5) ? 1 : -1;
    } else if (effect === 'glitch') {
      modeName = weighted(r, W.glitchMode);
      if (modeName === 'ghost' && n < 2) modeName = 'double'; // nothing to borrow from
      params.strength = r.int(W.strength[0], W.strength[1]);
      params.pick = r.int(0, 96);
    } else {
      modeName = weighted(r, W.focusMode);
      params.radius = r.int(W.strength[0], W.strength[1]);
      const y1 = r.range(0.28, 0.72), y2 = r.range(0.28, 0.72);
      params.path = r.chance(0.5)
        ? [{ x: 0.12, y: y1 }, { x: 0.5, y: (y1 + y2) / 2 }, { x: 0.88, y: y2 }]
        : [{ x: 0.88, y: y1 }, { x: 0.5, y: (y1 + y2) / 2 }, { x: 0.12, y: y2 }];
    }
    const [sLo, sHi] = W.span[spanKey] || W.span.spiral;
    const len = Math.max(2, Math.min(room, Math.round(frames * r.range(sLo, sHi))));
    // a creep is anchored to the end of its room, the house look; the rest land anywhere in it
    const start = spanKey === 'creep' ? hi - len : lo + r.int(0, room - len);
    blocks.push(createBlock({ effect, mode: modeName, target: 'canvas', start, end: start + len, params }, frames));
  }

  // budget: trim the longest block from its start until the union fits
  for (let guard = 0; guard < 8 && coverageOf(blocks, frames) > W.coverage; guard++) {
    let longest = blocks[0];
    for (const b of blocks) if (b.end - b.start > longest.end - longest.start) longest = b;
    const cut = Math.max(1, Math.round((longest.end - longest.start) * 0.15));
    if (longest.end - longest.start - cut < 2) break;
    longest.start += cut;
  }

  // loop marker: snap only when a tint ends against the loop end
  const loopTable = Object.assign({}, W.loop);
  const tintAtEnd = blocks.some((b) => b.effect === 'tint' && frames - b.end <= W.snapWindow);
  if (!tintAtEnd) delete loopTable.snap;
  const loop = weighted(r, loopTable);

  return {
    layout: { mode, stageMs },
    loop,
    blocks: blocks.map((b) => clampBlock(b, frames)),
  };
}

/**
 * The roll. Returns a patch for the project to apply. Pure: `state` is not
 * touched. Rerolls internally with seed + 1 up to AUTO_WEIGHTS.rerolls times
 * when a candidate fails the gate, then returns the last candidate.
 */
export function autoCompose(state, seed) {
  const frames = (state && state.frames) || framesForSeconds(5);
  const fps = (state && state.fps) || 15;
  let patch = null;
  for (let i = 0; i <= AUTO_WEIGHTS.rerolls; i++) {
    patch = candidate(state || {}, ((seed >>> 0) + i) >>> 0);
    if (passesGate(patch, frames, fps)) break;
  }
  return patch;
}
