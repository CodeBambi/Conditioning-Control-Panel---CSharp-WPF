/* ============================================================================
 * race/smoke/slope-check.mjs - nothing is hung inside a slope or under a flight.
 *
 *   node race/smoke/slope-check.mjs      (from Resources/web/dtrh; 0 on pass, 1 with a count)
 *
 * THE BUG THIS HOLDS SHUT. The owner, driving it: "some bubbles get placed under the
 * slopes and we can't get to them". Track space measures h from the road PLANE, and every
 * climb, dip and the Big Wheel are baked into the spine, so on open road the plane IS the
 * surface. A ramp is not: rooms.js stands a solid RAMP_LEN x RAMP_H wedge on the asphalt in
 * front of the lip and the kart flies an arc off its crest. A bubble hung at a flat LANE_H
 * through those metres is inside the wedge or a long way under the flight.
 *
 * So spine.js now answers where the kart IS - surfaceH / airLineAt / rideH, THE REACHABLE
 * LINE - and bubbles.js measures every placement off it. This walks the REAL placement code
 * (the real spine, the real createBubbleField, the real seedChunk / rain / spawnAhead and the
 * two cue doors) over every chunk of several seeded tracks and counts what is out of reach,
 * under the old flat rule and under the new one.
 *
 * It holds four things:
 *   1. nothing is BELOW the reachable line at its own depth (nothing inside a wedge, nothing
 *      buried in the road)
 *   2. everything a run can put down is inside the pop box of that line: a bubble the kart
 *      cannot meet is a bubble that should not have been placed
 *   3. the flat rule it replaces really was leaving bubbles behind (else this smoke is testing
 *      nothing) and the new rule leaves none
 *   4. the air line is takeable across the whole pace band - the gentle opening at OPEN_PACE
 *      through a boosted lap - because a line only one speed can reach is the same bug wearing
 *      a different height
 *
 * three resolves off the local vendor copy the way race/smoke/gltf-smoke.mjs does it, and the
 * DOM the texture loader reaches for is stubbed: no browser, no network, no renderer.
 * ==========================================================================*/

import { register } from 'node:module';
import { pathToFileURL } from 'node:url';
import path from 'node:path';

const VENDOR = pathToFileURL(path.resolve(import.meta.dirname, '../../vendor/three/') + path.sep).href;
register('data:text/javascript,' + encodeURIComponent(`
  export function resolve(spec, ctx, next) {
    if (spec === 'three') return { url: ${JSON.stringify(VENDOR)} + 'three.module.min.js', shortCircuit: true };
    if (spec.startsWith('three/addons/')) return { url: ${JSON.stringify(VENDOR)} + 'addons/' + spec.slice('three/addons/'.length), shortCircuit: true };
    return next(spec, ctx);
  }`), import.meta.url);

// the TextureLoader reaches for an <img> the moment the field is built, and the face cache for a
// <canvas>: both are handed something inert, so every sprite keeps its fallback dot and nothing draws.
const el = () => ({ addEventListener() {}, removeEventListener() {}, style: {}, width: 0, height: 0, getContext: () => null, set src(v) {}, get src() { return ''; } });
globalThis.document = { createElement: el, createElementNS: el, documentElement: {} };

const THREE = await import('three');
const { createSpine } = await import('../spine.js');
const { createBubbleField } = await import('../bubbles.js');
const { rollRoomOrder } = await import('../rooms.js');
const { LANE_H, POP_HIT_H, POP_HIT_D, CEILING_H, RAMP_H, RAMP_LEN, GRAVITY, KART_BASE_SPEED, OPEN_PACE, KART_MAX_SPEED } = await import('../consts.js');

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const SEEDS = [1, 7, 42, 1234, 90210];
/** A hair of slack: the placement rounds through wrap() and the line is sampled at the same d. */
const EPS = 1e-6;

/** The height the OLD flat rule gave this bubble at this depth: lane / spawn / a landed rain all sat
 *  at LANE_H over the road plane, and the air line hung on a fixed parabola over the ramp's window. */
function flatH(s, layout) {
  if (s.placement !== 'air') return LANE_H;
  const a = layout.airLineAt(s.d);
  const u = a ? a.u : 0.5;
  return 2 + 3 * (4 * u * (1 - u));
}
/** Where the OLD kart was at this depth: h = 0 all the way along the ground (it drove THROUGH the
 *  wedge) and a flight launched off the road plane rather than off the crest. This is the line the
 *  owner was actually driving when the bubbles turned out to be unreachable. */
function oldRideH(d, layout) {
  const a = layout.airLineAt(d);
  if (!a) return 0;
  const t = (d - a.ramp.d) / KART_BASE_SPEED;
  return Math.max(0, a.ramp.vh * t - 0.5 * GRAVITY * t * t);
}
/** Half a bubble sprite, near enough: a lane bubble whose centre is this close to the wedge is
 *  drawing inside it, which is the "under the slope" the owner could see. */
const BUBBLE_R = 0.5;
/** The resting height of a placed bubble: rain is measured where it LANDS, not mid-fall. */
const restH = (s) => (s.placement === 'rain' ? s.ride + LANE_H : s.baseH);

/** Every bubble one seeded track puts on the road, through the real field. The pool holds 160, and
 *  a whole track lays far more than that, so each chunk gets its own field: the point is to walk
 *  EVERY chunk of every seed, not to watch one pool recycle. */
function lay(seed) {
  const layout = createSpine({ seed, roomOrder: rollRoomOrder(seed) });
  const scene = new THREE.Scene();
  const newField = () => createBubbleField({ scene, layout, media: null,
    getIntensity: () => 0.8, getRoom: () => null, getElapsed: () => 999 });
  const ramps = layout.chunks.flatMap((c) => c.features || []).filter((f) => f.type === 'ramp');
  const slots = [];
  const drain = (f) => { slots.push(...f.slots()); f.dispose(); };
  for (const c of layout.chunks) { const f = newField(); f.seedChunk(c); drain(f); }
  // the two cue doors, aimed straight at the slopes: a lone spawn and a word row on the wedge, at
  // the lip, and through the flight, plus the drip and the rain. This is what a run does to a ramp.
  for (const r of ramps) {
    const f = newField();
    for (const off of [-RAMP_LEN, -RAMP_LEN / 2, -0.2, 0, 2, 6, 11]) {
      f.spawnAt({ kindId: 'treat', placement: 'lane', d: r.d + off, x: 0, h: LANE_H, eventId: 'cue' });
      f.spawnRow({ kindId: 'treat', placement: 'lane', d: r.d + off + 0.5, xs: [-1.6, 0, 1.6], h: LANE_H, eventId: 'row' });
    }
    f.rain(r.d - 20, 6);
    f.spawnAhead(r.d - 45, 6);
    drain(f);
  }
  return { layout, slots, ramps };
}

// ---- 1 + 2 + 3: the road, chunk by chunk, seed by seed --------------------------------
let after = { buried: 0, unreachable: 0 }, before = { buried: 0, unreachable: 0 }, total = 0, onRamp = 0;
const byPlacement = new Map();   // placement -> [placed, on a slope, buried before, unreachable before]
for (const seed of SEEDS) {
  const { layout, slots } = lay(seed);
  const s0 = { below: 0, unreachable: 0, n: slots.length };
  for (const s of slots) {
    total++;
    const ride = layout.rideH(s.d), surf = layout.surfaceH(s.d);
    const row = byPlacement.get(s.placement) || [0, 0, 0, 0];
    row[0]++;
    if (ride > EPS) { onRamp++; row[1]++; }
    const h = restH(s);
    if (h < ride - EPS) { after.unreachable++; s0.below++; }
    else if (Math.abs(h - ride) > POP_HIT_H) { after.unreachable++; s0.unreachable++; }
    if (h < surf + BUBBLE_R) after.buried++;
    // the same bubble under the rule this replaces, held against the kart THAT rule was driving
    const f = flatH(s, layout);
    if (f < surf + BUBBLE_R) { before.buried++; row[2]++; }
    if (Math.abs(f - oldRideH(s.d, layout)) > POP_HIT_H) { before.unreachable++; row[3]++; }
    byPlacement.set(s.placement, row);
  }
  ok(s0.below === 0 && s0.unreachable === 0,
    `seed ${seed}: ${s0.n} bubbles, ${s0.below} below the line, ${s0.unreachable} out of the pop box`);
}
console.log(`  ..  ${total} bubbles over ${SEEDS.length} seeds, ${onRamp} of them on a slope or a flight`);
console.log(`  ..  the flat rule: ${before.buried} drawn inside a slope, ${before.unreachable} out of the kart's reach`);
console.log(`  ..  riding the line: ${after.buried} inside a slope, ${after.unreachable} out of reach`);
for (const [p, [n, ramp, bur, lost]] of [...byPlacement].sort()) console.log(`  ..    ${p}: ${n} placed, ${ramp} on a slope, of those ${bur} were buried and ${lost} unreachable before`);
ok(after.buried === 0, 'nothing is drawn inside a slope');
ok(after.unreachable === 0, 'everything placed is inside the pop box of the reachable line');
ok(before.unreachable > 0 && before.buried > 0, 'the flat rule it replaces really did strand bubbles (this smoke tests something)');

// ---- 4: the air line, across the pace band --------------------------------------------
// The line is dressed to the cruise. A slow lap lands early and a boosted one flies further, so
// what matters is that the FIRST stretch - the metres the line actually covers - is crossed at
// about the same height whatever the throttle did.
const PACES = [OPEN_PACE, 0.85, 1, 1.15, KART_MAX_SPEED / KART_BASE_SPEED];
/** kart.js launch(): vh scales with speed, capped, and the flight starts on the wedge crest. */
function kartH(atD, ramp, speed) {
  const scale = Math.min(1.3, Math.max(0.7, speed / KART_BASE_SPEED));
  const vh = Math.sqrt(2 * GRAVITY * ramp.height * scale);
  const t = (atD - ramp.d) / speed;
  return Math.max(0, RAMP_H + vh * t - 0.5 * GRAVITY * t * t);
}
let airN = 0, airMiss = 0, worst = 0;
for (const seed of SEEDS) {
  const { slots, ramps } = lay(seed);
  for (const s of slots) {
    if (s.placement !== 'air') continue;
    const r = ramps.find((f) => { const d = s.d - f.d; return d > -1 && d < f.airLen; });
    if (!r) continue;
    airN++;
    for (const p of PACES) {
      const gap = Math.abs(restH(s) - kartH(s.d, r, p * KART_BASE_SPEED) - LANE_H);
      worst = Math.max(worst, gap);
      if (gap > POP_HIT_H) { airMiss++; break; }
    }
  }
}
// and the same measure on the air line it replaces: five bubbles spread across the WHOLE window on
// a fixed 2 + 3*4u(1-u) parabola. Its window was a random 22..30 m that had nothing to do with how
// far the kart actually flew, so it is measured at both ends of that range and in the middle.
let oldN = 0, oldMiss = 0;
for (const len of [22, 26, 30]) {
  let n = 0, miss = 0;
  for (const seed of SEEDS) {
    const { ramps } = lay(seed);
    for (const r of ramps) for (let i = 0; i < 5; i++) {
      const u = (i + 0.5) / 5, d = r.d + len * u, h = 2 + 3 * (4 * u * (1 - u));
      const t = (d - r.d) / KART_BASE_SPEED;
      n++;
      if (Math.abs(h - Math.max(0, r.vh * t - 0.5 * GRAVITY * t * t)) > POP_HIT_H) miss++;   // the OLD kart: it left the road, not the crest
    }
  }
  console.log(`  ..  the air line it replaces, over a ${len} m window: ${miss} of ${n} out of the kart's reach at the cruise`);
  oldN += n; oldMiss += miss;
}
console.log(`  ..  ${airN} air-line bubbles, worst gap to the kart over the pace band ${worst.toFixed(2)} m (pop box ${POP_HIT_H})`);
ok(oldMiss > oldN / 5, `the old air line really was hung over the top of the jump (${oldMiss} of ${oldN})`);
ok(airN > 0, 'the ramps carry an air line at all');
ok(airMiss === 0, `every air-line bubble is takeable at every pace (${airMiss} were not)`);

// ---- the line itself: the wedge, the lip, the landing ---------------------------------
{
  const { layout, ramps } = lay(SEEDS[0]);
  const r = ramps[0];
  ok(Math.abs(layout.surfaceH(r.d) - RAMP_H) < 1e-6, 'the lip is the crest of the wedge');
  ok(Math.abs(layout.surfaceH(r.d - RAMP_LEN) - 0) < 1e-6 && layout.surfaceH(r.d - RAMP_LEN / 2) > 0.3, 'the wedge slopes up over RAMP_LEN');
  ok(layout.surfaceH(r.d - RAMP_LEN - 2) === 0 && layout.surfaceH(r.d + 8) === 0, 'open road is flat road');
  ok(Math.abs(layout.surfaceSlope(r.d - 1) - RAMP_H / RAMP_LEN) < 1e-9 && layout.surfaceSlope(r.d + 8) === 0, 'the gradient is the wedge and nothing else');
  const air = layout.airLineAt(r.d + r.airLen * 0.5);
  ok(!!air && air.h > r.height * 0.7, 'the flight arcs over the road between the lip and the landing');
  ok(layout.airLineAt(r.d + r.airLen + 1) === null && layout.rideH(r.d + r.airLen + 1) === 0, 'past the landing the line is the road again');
  ok(Math.abs(layout.rideH(r.d + r.airLen - 0.05)) < 0.2, 'the arc comes back down onto the road exactly where the air line ends');
  ok(r.airLen > POP_HIT_D * 2 && r.airLen < 40, `the air line is the flight's own length (${r.airLen.toFixed(1)} m)`);
  ok(CEILING_H > layout.rideH(r.d + 4) + LANE_H, 'the rain still has room to fall over a flight');
}

console.log(fails ? `slope-check: ${fails} FAILED` : 'slope-check: all good');
process.exit(fails ? 1 : 0);
