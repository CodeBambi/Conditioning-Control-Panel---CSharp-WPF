/* ============================================================================
 * race/spine.js - Racing Thoughts track spine.
 *
 * Implements CONTRACT.md section "race/spine.js": createSpine({ seed, roomOrder })
 * returns a closed CatmullRom layout built from the chunk grammar (straight, bendL,
 * bendR, sCurve, climb, dip, ramp, chicane, loop, gate), parallel-transported
 * frames cached every 0.5 m, the track-space (d, x, h) -> world mapping, and the
 * ordered chunk + feature lists. The object is also a drop-in argument for
 * engine/tunnel.js createTunnel(layout) (it reads layout.spine) and carries the
 * buildLoopLayout compat fields fx.js reads (pointAt(t), frameAt(t)).
 *
 * Shape: the chunks are laid around one big horizontal ring (the closure is free),
 * each chunk bending the ring with a bump that returns to zero at its ends, so the
 * spine is smooth everywhere. THE BIG WHEEL is an 18 m vertical circle spliced in at
 * one ring angle with a 16 m lateral sidestep (more than 2*RADIUS) that bleeds back
 * to the ring over the next ~120 m, so the tube never crosses itself. Frames are
 * parallel-transported (the world inverts through the wheel) and re-levelled toward
 * world up wherever the tangent is not vertical, so the road never stays banked.
 *
 * Contract notes: pointAt(t) / frameAt(t) take the normalized 0..1 parameter exactly
 * like buildLoopLayout (fx.js calls layout.frameAt(Math.random())) and the frames also
 * carry normal/binormal aliases for up/right. Each room's first chunk is its gate; the
 * Tea Garden gate sits at d = 0 and the start straight follows it.
 * ==========================================================================*/

import * as THREE from 'three';
import { RADIUS, ROAD_DROP, ROOM_IDS, makeRng } from './consts.js';

const FRAME_STEP = 0.5;                 // metres between cached frames
const CP_STEP = 6;                      // control-point spacing along the ring, metres
const LOOP_R = 18;                      // Big Wheel radius (> RADIUS: the road has headroom)
const LOOP_SIDESTEP = 2 * RADIUS + 5;   // 16 m: the exit runs beside the entry, never through it
const LOOP_FOOTPRINT = 12;              // ring metres the loop advances (entry -> exit)
const LOOP_CPS = 24;                    // control points around the wheel (+1 closing point)
const SIDESTEP_BLEED = 120;             // metres after the loop to ease the sidestep back to 0
const RING_WAVE = 5;                    // gentle global elevation swell (integer harmonic closes)
const RELEVEL_TAU = 10;                 // metres: how fast the frame rolls back level after the wheel
const RELEVEL_MAX = 0.045;              // rad per metre roll-rate cap while re-levelling
const WORLD_UP = new THREE.Vector3(0, 1, 0);

// Chunk recipes: footprint length range, lateral bump amplitude, elevation bump amplitude.
// `lat` shapes: bump = one hump (bend), sine = in then out (S), double = two quick alternations.
// Every shape is windowed by sin^4 so its curvature is zero at the chunk ends: neighbouring
// chunks never stack their bends, and the amplitudes below keep every bend radius above ~18 m.
const RECIPES = {
  straight: { len: [44, 64] },
  bendL:    { len: [80, 96], lat: 'bump', latAmp: [6, 8],  latSign: 1 },
  bendR:    { len: [80, 96], lat: 'bump', latAmp: [6, 8],  latSign: -1 },
  sCurve:   { len: [84, 104], lat: 'sine', latAmp: [4, 5.5] },
  chicane:  { len: [80, 96], lat: 'double', latAmp: [1.2, 1.6] },
  climb:    { len: [70, 86], elevAmp: [4.5, 6] },
  dip:      { len: [70, 86], elevAmp: [-6, -4.5] },
  ramp:     { len: [60, 64], landingDip: -1.8 },
  gate:     { len: [18, 18] },
  loop:     { len: [LOOP_FOOTPRINT, LOOP_FOOTPRINT] },
};

// Per-room chunk pools (the room's signature shapes from the pitch, weighted by repetition).
const ROOM_POOLS = {
  teagarden:  ['straight', 'bendL', 'bendR', 'sCurve', 'dip', 'straight'],
  toybox:     ['ramp', 'climb', 'dip', 'bendL', 'bendR', 'chicane'],
  casino:     ['straight', 'bendR', 'bendL', 'sCurve', 'ramp', 'straight'],
  undertow:   ['sCurve', 'sCurve', 'chicane', 'dip', 'bendL', 'climb'],
  mirrors:    ['sCurve', 'chicane', 'bendL', 'bendR', 'climb', 'chicane'],
  chapel:     ['climb', 'dip', 'straight', 'bendL', 'sCurve', 'dip'],
  greyward:   ['straight', 'straight', 'bendL', 'bendR', 'dip', 'straight'],
  coronation: ['ramp', 'climb', 'bendR', 'sCurve', 'straight', 'climb'],
};
const BOOST_ODDS = { casino: 0.75, toybox: 0.45, coronation: 0.45 };

const bump = (u) => Math.sin(Math.PI * u) ** 4;                    // 0 -> 1 -> 0, flat ends
const smooth = (u) => u <= 0 ? 0 : u >= 1 ? 1 : u * u * (3 - 2 * u);
const pick = (rng, arr) => arr[Math.floor(rng() * arr.length) % arr.length];
const range = (rng, [a, b]) => a + (b - a) * rng();

/** Lay out the chunk plan: one gate per room, the Tea Garden start straight, at least one
 * ramp per room, exactly one loop in a room at index >= 2 (the casino when it qualifies). */
function planChunks(rng, roomOrder) {
  const rooms = roomOrder && roomOrder.length ? roomOrder.slice() : ROOM_IDS.slice();
  const loopCandidates = rooms.map((r, i) => i).filter((i) => i >= 2);
  const casinoIdx = rooms.indexOf('casino');
  const loopRoom = casinoIdx >= 2 ? casinoIdx : pick(rng, loopCandidates);
  const plan = [];
  rooms.forEach((room, ri) => {
    const pool = ROOM_POOLS[room] || ROOM_POOLS.teagarden;
    const hasLoop = ri === loopRoom;
    let n = 4 + Math.floor(rng() * 4);                    // 4..7 chunks incl. the gate
    if (hasLoop) n = Math.max(n, 5);
    const kinds = new Array(n).fill(null);
    kinds[0] = 'gate';
    if (ri === 0) kinds[1] = 'start';                     // the Tea Garden start straight
    const free = () => kinds.map((k, i) => (k ? -1 : i)).filter((i) => i >= 0);
    if (hasLoop) kinds[pick(rng, free().filter((i) => i <= n - 3))] = 'loop';
    kinds[pick(rng, free())] = 'ramp';
    for (const i of free()) {
      let k = pick(rng, pool);
      if (k === 'ramp' && kinds.includes('ramp') && rng() < 0.6) k = 'straight';
      if (k === kinds[i - 1] && k !== 'straight') k = pick(rng, pool); // avoid stutters
      kinds[i] = k;
    }
    kinds.forEach((kind, ci) => {
      const isStart = kind === 'start';
      const recipe = RECIPES[isStart ? 'straight' : kind];
      const c = { kind: isStart ? 'straight' : kind, room, roomIndex: ri, indexInRoom: ci,
        len: isStart ? 96 : range(rng, recipe.len), latAmp: 0, elevAmp: 0, lat: recipe.lat || null,
        landingDip: recipe.landingDip || 0 };
      if (recipe.latAmp) c.latAmp = range(rng, recipe.latAmp) * (recipe.latSign || (rng() < 0.5 ? -1 : 1));
      if (recipe.elevAmp) c.elevAmp = range(rng, recipe.elevAmp);
      plan.push(c);
    });
  });
  return plan;
}

/** Turn the plan into control points on the ring (+ the spliced wheel). Fills c.cp0/c.cp1. */
function buildControlPoints(plan) {
  const footprint = plan.reduce((s, c) => s + c.len, 0);
  const ringR = footprint / (2 * Math.PI);
  const pts = [];
  let theta = 0, sidestepFrom = -1;     // ring metres where the sidestep bleed started
  const ringPoint = (m, radial, elev) => {   // m = metres along the ring
    const a = m / ringR;
    const r = ringR + radial;
    return new THREE.Vector3(Math.cos(a) * r, RING_WAVE * Math.sin(2 * a) + elev, Math.sin(a) * r);
  };
  const carry = (m) => (sidestepFrom < 0 ? 0
    : -LOOP_SIDESTEP * (1 - smooth((m - sidestepFrom) / SIDESTEP_BLEED)));
  for (const c of plan) {
    c.cp0 = pts.length;
    if (c.kind === 'loop') {
      // local frame at the wheel's foot: T along the ring, n = world up, b = right (inward)
      const P0 = ringPoint(theta, carry(theta), 0);
      const T = ringPoint(theta + 0.5, 0, 0).sub(ringPoint(theta - 0.5, 0, 0)).normalize();
      const n = WORLD_UP.clone().addScaledVector(T, -T.dot(WORLD_UP)).normalize();
      const b = new THREE.Vector3().crossVectors(T, n).normalize();
      for (let k = 0; k <= LOOP_CPS; k++) {
        const th = (k / LOOP_CPS) * Math.PI * 2;
        pts.push(P0.clone().addScaledVector(T, LOOP_R * Math.sin(th))
          .addScaledVector(n, LOOP_R * (1 - Math.cos(th)))
          .addScaledVector(b, LOOP_SIDESTEP * (k / LOOP_CPS)));
      }
      theta += c.len;
      sidestepFrom = theta;
      c.cp1 = pts.length;                // exclusive; the next chunk's first point closes the wheel
      continue;
    }
    const n = Math.max(2, Math.round(c.len / CP_STEP));
    for (let k = 0; k < n; k++) {
      const u = k / n, m = theta + u * c.len;
      let lat = 0, elev = 0;
      if (c.lat === 'bump') lat = c.latAmp * bump(u);
      else if (c.lat === 'sine') lat = c.latAmp * Math.sin(Math.PI * 2 * u) * bump(u);
      else if (c.lat === 'double') lat = c.latAmp * Math.sin(Math.PI * 4 * u) * bump(u);
      if (c.elevAmp) elev = c.elevAmp * bump(u);
      if (c.landingDip && u > 0.35) elev = c.landingDip * bump((u - 0.35) / 0.65);
      pts.push(ringPoint(m, lat + carry(m), elev));
    }
    theta += c.len;
    c.cp1 = pts.length;
  }
  return pts;
}

export function createSpine({ seed = 1, roomOrder } = {}) {
  const rng = makeRng(seed | 0);
  const plan = planChunks(rng, roomOrder);
  const pts = buildControlPoints(plan);
  const spine = new THREE.CatmullRomCurve3(pts, true, 'centripetal', 0.5);
  spine.arcLengthDivisions = pts.length * 16;
  const lengths = spine.getLengths(spine.arcLengthDivisions);
  const totalDepth = lengths[lengths.length - 1];
  const wrap = (d) => ((d % totalDepth) + totalDepth) % totalDepth;
  const cpDepth = (i) => lengths[Math.round((i / pts.length) * spine.arcLengthDivisions)];

  // ---- chunks + features (depths are read off the finished curve) ----------------
  const chunks = plan.map((c, i) => {
    const d0 = cpDepth(c.cp0), d1 = i === plan.length - 1 ? totalDepth : cpDepth(c.cp1);
    const len = d1 - d0, features = [];
    const boostOdds = BOOST_ODDS[c.room] || 0.35;
    if (c.kind === 'gate') features.push({ type: 'gate', d: d0 + len * 0.5, room: c.room });
    else if (c.kind === 'loop') features.push({ type: 'loop', d0, d1 });
    else if (c.kind === 'ramp') {
      features.push({ type: 'ramp', d: d0 + 14, airLen: range(rng, [22, 30]), height: range(rng, [3, 4]) });
    } else {
      if (rng() < boostOdds) features.push({ type: 'boost', d: d0 + len * 0.5, x: range(rng, [-1.2, 1.2]) });
      if (rng() < 0.5) features.push({ type: 'itembox', d: d0 + len * (rng() < 0.5 ? 0.28 : 0.76), x: range(rng, [-1.8, 1.8]) });
    }
    return { id: i, kind: c.kind, d0, d1, room: c.room, features };
  });
  // a boost pad on the run-up to the wheel, and one sugar cube per room at least
  chunks.forEach((ch, i) => {
    if (ch.kind !== 'loop' || i === 0) return;
    const prev = chunks[i - 1];
    prev.features.push({ type: 'boost', d: prev.d1 - 12, x: 0 });
  });
  for (const room of new Set(chunks.map((c) => c.room))) {
    const mine = chunks.filter((c) => c.room === room);
    if (mine.some((c) => c.features.some((f) => f.type === 'itembox'))) continue;
    const host = mine.find((c) => c.kind !== 'gate' && c.kind !== 'loop' && c.kind !== 'ramp') || mine[1] || mine[0];
    host.features.push({ type: 'itembox', d: host.d0 + (host.d1 - host.d0) * 0.6, x: 0 });
  }
  chunks.forEach((c) => c.features.sort((a, b) => (a.d ?? a.d0) - (b.d ?? b.d0)));
  const allFeatures = chunks.flatMap((c) => c.features);
  const ramps = allFeatures.filter((f) => f.type === 'ramp');

  // ---- frames, cached every FRAME_STEP metres -------------------------------------
  // Parallel transport carries `up` through the wheel so the world inverts at the top.
  // A helical loop leaves the transported frame rolled on exit (about 1 rad for a 16 m
  // sidestep), so wherever the tangent is not vertical the frame also eases back toward
  // the nearest of +/- world up: a no-op on the ring, and a short settle after the wheel.
  const N = Math.ceil(totalDepth / FRAME_STEP);
  const P = new Float32Array(N * 3), Tn = new Float32Array(N * 3), Up = new Float32Array(N * 3);
  {
    const tans = [], ups = [], axis = new THREE.Vector3(), ref = new THREE.Vector3();
    const relax = 1 - Math.exp(-FRAME_STEP / RELEVEL_TAU), cap = RELEVEL_MAX * FRAME_STEP;
    for (let i = 0; i < N; i++) {
      const u = (i * FRAME_STEP) / totalDepth;
      const p = spine.getPointAt(u), t = spine.getTangentAt(u).normalize();
      tans.push(t); P[i * 3] = p.x; P[i * 3 + 1] = p.y; P[i * 3 + 2] = p.z;
    }
    ups[0] = WORLD_UP.clone().addScaledVector(tans[0], -tans[0].dot(WORLD_UP)).normalize();
    for (let i = 1; i < N; i++) {
      const up = ups[i - 1].clone(), t = tans[i];
      axis.crossVectors(tans[i - 1], t);
      if (axis.lengthSq() > 1e-12) {
        const ang = Math.acos(THREE.MathUtils.clamp(tans[i - 1].dot(t), -1, 1));
        up.applyAxisAngle(axis.normalize(), ang);
      }
      up.addScaledVector(t, -up.dot(t)).normalize();
      const gain = (1 - Math.abs(t.y)) ** 2;
      ref.copy(WORLD_UP).addScaledVector(t, -t.y);
      if (gain > 1e-4 && ref.lengthSq() > 1e-6) {
        ref.normalize();
        if (up.dot(ref) < 0) ref.negate();
        let roll = Math.atan2(axis.crossVectors(up, ref).dot(t), up.dot(ref));
        roll = THREE.MathUtils.clamp(roll * relax, -cap, cap) * gain;
        up.applyAxisAngle(t, roll);
      }
      ups[i] = up;
    }
    // closure: transport one more step back onto sample 0 and spread the residual twist
    const upEnd = ups[N - 1].clone();
    axis.crossVectors(tans[N - 1], tans[0]);
    if (axis.lengthSq() > 1e-12) upEnd.applyAxisAngle(axis.normalize(), Math.acos(THREE.MathUtils.clamp(tans[N - 1].dot(tans[0]), -1, 1)));
    upEnd.addScaledVector(tans[0], -upEnd.dot(tans[0])).normalize();
    let twist = Math.acos(THREE.MathUtils.clamp(upEnd.dot(ups[0]), -1, 1));
    if (tans[0].dot(axis.crossVectors(upEnd, ups[0])) < 0) twist = -twist;
    for (let i = 0; i < N; i++) {
      const up = ups[i].applyAxisAngle(tans[i], (twist * i) / N);
      Tn[i * 3] = tans[i].x; Tn[i * 3 + 1] = tans[i].y; Tn[i * 3 + 2] = tans[i].z;
      Up[i * 3] = up.x; Up[i * 3 + 1] = up.y; Up[i * 3 + 2] = up.z;
    }
  }
  const _p = new THREE.Vector3(), _t = new THREE.Vector3(), _u = new THREE.Vector3(), _r = new THREE.Vector3();
  const _a = new THREE.Vector3(), _b = new THREE.Vector3();
  const lerp3 = (arr, i0, i1, k, out) => {
    _a.fromArray(arr, i0 * 3); _b.fromArray(arr, i1 * 3);
    return out.copy(_a).lerp(_b, k);
  };
  /** Fill the scratch frame at depth d (no allocation). */
  function frameInto(d) {
    const f = wrap(d) / FRAME_STEP, i0 = Math.floor(f) % N, i1 = (i0 + 1) % N, k = f - Math.floor(f);
    lerp3(P, i0, i1, k, _p);
    lerp3(Tn, i0, i1, k, _t).normalize();
    lerp3(Up, i0, i1, k, _u);
    _u.addScaledVector(_t, -_u.dot(_t)).normalize();
    _r.crossVectors(_t, _u).normalize();
  }
  function frameAtDepth(d) {
    frameInto(d);
    const pos = _p.clone(), tangent = _t.clone(), up = _u.clone(), right = _r.clone();
    return { pos, tangent, up, right, normal: up, binormal: right };
  }
  function toWorld(d, x, h, out = new THREE.Vector3()) {
    frameInto(d);
    return out.copy(_p).addScaledVector(_u, h - ROAD_DROP).addScaledVector(_r, x);
  }

  // ---- queries ------------------------------------------------------------------
  function chunkAtDepth(d) {
    const w = wrap(d);
    let lo = 0, hi = chunks.length - 1;
    while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (chunks[mid].d0 <= w) lo = mid; else hi = mid - 1; }
    return chunks[lo];
  }
  const roomAtDepth = (d) => chunkAtDepth(d).room;
  function featuresBetween(d0, d1) {
    const a = wrap(d0), b = wrap(d1);
    const inRange = (fd) => (a <= b ? fd >= a && fd < b : fd >= a || fd < b);
    const key = (f) => (f.d != null ? f.d : f.d0);
    return allFeatures.filter((f) => inRange(key(f)))
      .sort((f, g) => wrap(key(f) - a) - wrap(key(g) - a));
  }
  function rampAt(d) {
    const w = wrap(d);
    for (const r of ramps) if (w >= r.d && w <= r.d + r.airLen) return r;
    return null;
  }

  // ---- tunnel.js / fx.js compat (normalized t in 0..1, as buildLoopLayout) ---------
  const wrap01 = (t) => ((t % 1) + 1) % 1;
  const pointAt = (t, out) => spine.getPointAt(wrap01(t), out);
  const frameAt = (t) => frameAtDepth(wrap01(t) * totalDepth);

  return {
    RADIUS, totalDepth, loopDepth: totalDepth, spine, pointAt, frameAt,
    frameAtDepth, toWorld, wrap, chunks, featuresBetween, roomAtDepth, rampAt,
    seed, roomOrder: [...new Set(chunks.map((c) => c.room))],
  };
}
