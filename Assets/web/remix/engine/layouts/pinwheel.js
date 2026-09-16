/* ============================================================================
 * layouts/pinwheel.js - Pinwheel.
 *
 * One gif on every spoke, turning around a hub that breathes. The most
 * hypnotic of the set and the one that needs the least media.
 *
 * The loop: the wheel turns by exactly one spoke per loop and every spoke
 * carries the same gif, so spoke k ends the loop standing where spoke k + 1
 * started it and the cut has nothing to show. The hub breathes once over the
 * same loop, so it is back at its own size on the last frame.
 *
 * There are always at least three spokes, more when there are more gifs, and
 * a spoke tile is sized off the gap between neighbours so the wheel never
 * overlaps itself. The gif on the spokes is the first one and the hub is the
 * second if there is one, which is every tile this layout has a place for:
 * past the second there is no part of the wheel left to put one on, which is
 * what `maxTiles` says.
 *
 * Spokes and hub float over the ground rather than sitting in the mosaic, so
 * they carry a plate and skip the gutters, the way the dealt cards do.
 *
 * From the seed: where the wheel starts and which way it turns.
 * ==========================================================================*/

import { V, VW, VH, rngFor } from './util.js';

const NAME = 'Pinwheel';
const SPOKES = 3;        // fewest spokes, however few gifs there are
const RAD = 0.28;        // wheel radius, as a share of the short side
const RAD_PER = 0.0125;  // and what each spoke past nothing adds to it
const RAD_MAX = 0.38;    // but never past this, or a spoke leaves the canvas
const TILE = 0.36;       // widest a spoke tile gets, share of the short side
const FIT = 0.9;         // share of the room between neighbours it may take
const AR = 1.25;         // spoke tile shape
const HUB = 0.3;         // hub size, share of the short side
const PUFF = 0.05;       // how far the hub breathes either way

export function rectsAt(cfg, frame) {
  const n = Math.max(1, cfg.n | 0);
  const F = Math.max(1, cfg.frames | 0);
  const u = frame / F;
  const rng = rngFor(cfg, 'pin');
  const start = rng.next() * Math.PI * 2;
  const dir = rng.next() < 0.5 ? 1 : -1;
  const land = cfg.orientation !== 'portrait';

  const spokes = Math.max(SPOKES, n);
  const rad = VH * Math.min(RAD_MAX, RAD + RAD_PER * spokes);
  // the straight line between two neighbouring spoke centres, which is all
  // the width a tile can take without touching the one next to it
  const chord = 2 * rad * Math.sin(Math.PI / spokes);
  const tw = Math.min(VH * TILE, chord * FIT);
  const th = tw / AR;
  const out = [];

  for (let k = 0; k < spokes; k++) {
    const a = start + dir * 2 * Math.PI * (k + u) / spokes;
    const cx = VW / 2 + Math.cos(a) * rad;
    const cy = VH / 2 + Math.sin(a) * rad;
    // the tile stands square to the hub; portrait transposes the wheel, which
    // mirrors it about the diagonal, so the lean has to be mirrored with it
    out.push(V(cfg, cx - tw / 2, cy - th / 2, tw, th,
      { g: 0, z: 1, rot: land ? a + Math.PI / 2 : -a, plate: true, gap: false }));
  }

  const hs = VH * HUB * (1 + PUFF * Math.sin(2 * Math.PI * u));
  out.push(V(cfg, VW / 2 - hs / 2, VH / 2 - hs / 2, hs, hs,
    { g: n >= 2 ? 1 : 0, z: 2, plate: true, gap: false }));
  return out;
}

export const pinwheel = {
  backdrop: true,   // sparse on the canvas; a soft copy of the first gif fills the ground
  id: 'pinwheel',
  name: NAME,
  minTiles: 1,
  maxTiles: 2,
  cost: 'dear',
  rectsAt,
};

export default pinwheel;
