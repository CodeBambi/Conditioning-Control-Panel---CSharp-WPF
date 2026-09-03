/* ============================================================================
 * race/rooms.js - The Caucus Race rooms and road furniture.
 *
 * Implements CONTRACT.md section "race/rooms.js": ROOMS (the eight room specs),
 * rollRoomOrder(seed) (Tea Garden first, then the rest dealt loud/soft/loud so the
 * contrast rule holds) and createRoomDresser({ scene, layout, rooms }) which builds
 * everything that sits on or around the road: the vertex-coloured road ribbon with
 * its edge lines, porcelain ramp wedges with pink lips and the gold dotted air line,
 * cyan boost pads, spinning sugar-cube item boxes, and one instanced prop set per
 * room around the tube walls. update(d) culls rooms out of sight and animates what
 * is near the kart; applyRoom(fx, roomId, fadeSec) hands the room's biome style to
 * fx.applyRegionGrade; dispose() tears it all down.
 *
 * Every position goes through layout.toWorld(d, x, h) and every orientation through
 * layout.frameAtDepth(d). Draw calls: 7 for the furniture + 12 for the props (19).
 * The dresser adds its own hemisphere + directional light so the Lambert props read
 * without run.js having to know about them.
 * ==========================================================================*/

import * as THREE from 'three';
import { RADIUS, ROAD_DROP, ROAD_HALF_W, ROOM_IDS, makeRng } from './consts.js';
import { biomeById } from '../game/biomes.js';

// ---- the eight rooms -------------------------------------------------------------
// colors: road = ribbon tint, edge = kerb lines, prop = wall props, fog = the room's haze
// (informational; the biome palette is what fx.js grades), banner = the MARQUEE plate.
// bubbleBias multiplies bubbles.js kind weights; ambient names a fieldFx particle field.
export const ROOMS = [
  { id: 'teagarden', name: 'The Tea Garden', tagline: 'nothing here fights you', biome: 'mirrorlake',
    colors: { road: 0x1e3a30, edge: 0xf6e7c8, prop: 0xffb6d9, fog: 0x12261f, banner: 0x5fa98a },
    propKind: 'teacup', loud: false,
    bubbleBias: { treat: 1.4, golden: 0.8, lucky: 1.2, flash: 0.5, subliminal: 0.5, video: 0 },
    ambient: { kind: 'petals', colors: [[255, 182, 217], [191, 235, 216], [246, 231, 200]] } },
  { id: 'toybox', name: 'The Toybox', tagline: 'the floor bounces. so do you', biome: 'toybox',
    colors: { road: 0x1f1b52, edge: 0xffd23f, prop: 0xffd23f, fog: 0x14103a, banner: 0x6c63d8 },
    propKind: 'block', loud: true, propAlt: [0xff4d6d, 0x3a86ff],
    bubbleBias: { treat: 1.2, prism: 1.5, flash: 1.3, glitch: 0.6 },
    ambient: { kind: 'confetti', colors: [[255, 77, 109], [255, 210, 63], [58, 134, 255]] } },
  { id: 'casino', name: "The Fool's Casino", tagline: 'the wheel always pays. eventually', biome: 'casino',
    colors: { road: 0x3a0716, edge: 0xf2c14e, prop: 0xf2c14e, fog: 0x0b0508, banner: 0xa3122e },
    propKind: 'chip', loud: true, propAlt: [0xa3122e],
    bubbleBias: { golden: 2.0, lucky: 2.0, glitch: 1.3, treat: 0.9 },
    ambient: { kind: 'coins', colors: [[242, 193, 78], [255, 240, 160]] } },
  { id: 'undertow', name: 'The Undertow', tagline: 'the lane drifts. let it', biome: 'undertow',
    colors: { road: 0x0a2540, edge: 0x7fe7f0, prop: 0x1fa9b5, fog: 0x06202a, banner: 0x2a8fa8 },
    propKind: 'kelp', loud: false,
    bubbleBias: { treat: 1.0, spiral: 1.4, braindrain: 1.3, freeze: 1.2 },
    ambient: { kind: 'bubbles', colors: [[127, 231, 240], [159, 200, 255]] } },
  { id: 'mirrors', name: 'The Hall of Mirrors', tagline: 'the picture flips. your hand does not', biome: 'mirrors',
    colors: { road: 0x2b2b33, edge: 0x5be7d8, prop: 0xdde3f0, fog: 0x1a1e2c, banner: 0x9aa3c8 },
    propKind: 'mirror', loud: false,
    bubbleBias: { prism: 1.6, glitch: 1.5, spiral: 1.2, flash: 1.2 },
    ambient: { kind: 'glints', colors: [[221, 227, 240], [91, 231, 216]] } },
  { id: 'chapel', name: 'The Pink Chapel', tagline: 'the spiral pins itself here', biome: 'chapel',
    colors: { road: 0x4a1030, edge: 0xf2c14e, prop: 0xffffff, fog: 0x2a0820, banner: 0xe23c9c },
    propKind: 'candle', loud: true,
    bubbleBias: { subliminal: 1.8, spiral: 1.5, pink: 1.4 },
    ambient: { kind: 'motes', colors: [[255, 214, 150], [255, 105, 180]] } },
  { id: 'greyward', name: 'The Grey Ward', tagline: 'the only pink left is the treats', biome: 'greyward',
    colors: { road: 0x3c4043, edge: 0x9aa0a6, prop: 0x9aa0a6, fog: 0x2b2b33, banner: 0xff69b4 },
    propKind: 'cot', loud: false,
    bubbleBias: { treat: 0.8, pink: 1.6, braindrain: 1.4, freeze: 1.2 },
    ambient: { kind: 'ash', colors: [[154, 160, 166], [221, 227, 240]] } },
  { id: 'coronation', name: 'The Coronation', tagline: 'the run remembers. so will you', biome: 'coronation',
    colors: { road: 0x4a1030, edge: 0xf2c14e, prop: 0xf2c14e, fog: 0x3a0716, banner: 0x7a0f2b },
    propKind: 'crown', loud: true,
    bubbleBias: { golden: 1.5, video: 1.6, gifrain: 1.4, pink: 1.2 },
    ambient: { kind: 'goldleaf', colors: [[242, 193, 78], [255, 105, 180]] } },
];
const ROOM_BY_ID = Object.fromEntries(ROOMS.map((r) => [r.id, r]));
export const roomById = (id) => ROOM_BY_ID[id] || null;

/** Tea Garden first; the other seven dealt by the seed, loud and soft rooms alternating. */
export function rollRoomOrder(seed) {
  const rng = makeRng(seed | 0);
  const shuffle = (arr) => { for (let i = arr.length - 1; i > 0; i--) { const j = Math.floor(rng() * (i + 1)); [arr[i], arr[j]] = [arr[j], arr[i]]; } return arr; };
  const loud = shuffle(ROOMS.filter((r) => r.loud).map((r) => r.id));
  const soft = shuffle(ROOMS.filter((r) => !r.loud && r.id !== 'teagarden').map((r) => r.id));
  const out = ['teagarden'];
  while (loud.length || soft.length) { if (loud.length) out.push(loud.shift()); if (soft.length) out.push(soft.shift()); }
  return out;
}

// ---- prop shape language per room ------------------------------------------------
// orient: tumble = any rotation, stand = local +y points into the tube, face = local +z does.
// follow: reuse the previous shape's placements (a flame sits on its candle).
function propShapes(kind) {
  switch (kind) {
    case 'teacup': return [
      { geo: new THREE.CylinderGeometry(0.36, 0.26, 0.4, 14, 1, true), n: 10, orient: 'tumble', side: THREE.DoubleSide },
      { geo: new THREE.CapsuleGeometry(0.08, 0.5, 3, 8), n: 8, orient: 'tumble', squash: 0.35, color: 0xf6e7c8 }];
    case 'block': return [{ geo: new THREE.BoxGeometry(0.7, 0.7, 0.7), n: 16, orient: 'tumble' }];
    case 'chip': return [
      { geo: new THREE.CylinderGeometry(0.4, 0.4, 0.1, 18), n: 12, orient: 'tumble' },
      { geo: new THREE.BoxGeometry(0.5, 0.5, 0.5), n: 6, orient: 'tumble', color: 0xffffff }];
    case 'kelp': return [{ geo: new THREE.PlaneGeometry(0.3, 1.8).translate(0, 0.9, 0), n: 16, orient: 'stand', side: THREE.DoubleSide, sway: true }];
    case 'mirror': return [{ geo: new THREE.PlaneGeometry(1.1, 1.6), n: 12, orient: 'face', side: THREE.DoubleSide, shiny: true }];
    case 'candle': return [
      { geo: new THREE.CylinderGeometry(0.09, 0.11, 0.9, 8).translate(0, 0.45, 0), n: 14, orient: 'stand' },
      { geo: new THREE.SphereGeometry(0.09, 8, 6).translate(0, 1.0, 0), n: 14, orient: 'stand', color: 0xf2c14e, glow: true, follow: true }];
    case 'cot': return [{ geo: new THREE.BoxGeometry(0.9, 0.18, 1.9), n: 10, orient: 'stand' }];
    case 'crown': return [
      { geo: new THREE.TorusGeometry(0.34, 0.09, 8, 14), n: 10, orient: 'tumble' },
      { geo: new THREE.CylinderGeometry(0.22, 0.26, 2.4, 10).translate(0, 1.2, 0), n: 8, orient: 'stand', color: 0x7a0f2b }];
    default: return [{ geo: new THREE.SphereGeometry(0.4, 10, 8), n: 10, orient: 'tumble' }];
  }
}

/** A porcelain wedge: flat road-wide triangle rising to `hgt` at the lip (local +z = forward). */
function wedgeGeometry(halfW, len, hgt) {
  const a = [-halfW, 0, -len], b = [halfW, 0, -len], c = [halfW, hgt, 0], d = [-halfW, hgt, 0];
  const c0 = [halfW, 0, 0], d0 = [-halfW, 0, 0];
  const tri = (...v) => v.flat();
  const pos = new Float32Array([
    ...tri(a, b, c), ...tri(a, c, d),          // the slope
    ...tri(d, c, c0), ...tri(d, c0, d0),      // the lip face
    ...tri(b, c0, c), ...tri(a, d, d0),        // the sides
  ]);
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  g.computeVertexNormals();
  return g;
}

function canvasTex(w, h, draw) {
  if (typeof document === 'undefined') return null;
  const c = document.createElement('canvas'); c.width = w; c.height = h;
  draw(c.getContext('2d'), w, h);
  const t = new THREE.CanvasTexture(c);
  t.wrapS = t.wrapT = THREE.RepeatWrapping; t.anisotropy = 4;
  return t;
}

// ---- the dresser -----------------------------------------------------------------
export function createRoomDresser({ scene, layout, rooms = ROOMS }) {
  const specs = rooms.map((r) => (typeof r === 'string' ? roomById(r) : r)).filter(Boolean);
  const specOf = (id) => specs.find((s) => s.id === id) || roomById(id) || ROOMS[0];
  const group = new THREE.Group();
  group.name = 'race-room-dresser';
  const geos = [], mats = [], texes = [];
  const track = (g) => { geos.push(g); return g; };
  const mat = (m) => { mats.push(m); return m; };
  const rng = makeRng((layout.seed | 0) ^ 0x5eed);
  const total = layout.totalDepth;
  const wrapDist = (a, b) => { const w = layout.wrap(a - b); return Math.min(w, total - w); };

  // room depth spans (a room is one contiguous run of chunks)
  const spans = [];
  for (const ch of layout.chunks) {
    const last = spans[spans.length - 1];
    if (last && last.id === ch.room) last.d1 = ch.d1;
    else spans.push({ id: ch.room, d0: ch.d0, d1: ch.d1 });
  }
  const loopChunk = layout.chunks.find((c) => c.kind === 'loop');
  const inLoop = (d) => loopChunk && d >= loopChunk.d0 - 4 && d <= loopChunk.d1 + 4;
  const _c = new THREE.Color(), _c2 = new THREE.Color();
  const _p = new THREE.Vector3(), _q = new THREE.Vector3(), _m = new THREE.Matrix4(), _r = new THREE.Matrix4();
  const _s = new THREE.Vector3(), _e = new THREE.Euler();
  const roomColorAt = (d, key, out) => {   // crossfade into the next room over its last 6%
    const w = layout.wrap(d);
    const i = spans.findIndex((s) => w >= s.d0 && w < s.d1);
    const s = spans[i < 0 ? 0 : i], next = spans[(i + 1) % spans.length];
    out.setHex(specOf(s.id).colors[key]);
    const u = (w - s.d0) / (s.d1 - s.d0);
    if (u > 0.94) out.lerp(_c2.setHex(specOf(next.id).colors[key]), (u - 0.94) / 0.06);
    if (inLoop(w)) out.lerp(_c2.setHex(0xf2c14e), 0.35);
    return out;
  };

  // instance matrix on the road: basis (right, up, tangent), optional yaw about up, uniform scale
  function roadMatrix(d, x, h, yaw, scale, out) {
    const f = layout.frameAtDepth(d);
    out.makeBasis(f.right, f.up, f.tangent);
    if (yaw) out.multiply(_r.makeRotationY(yaw));
    if (scale !== 1) out.scale(_s.setScalar(scale));
    return out.setPosition(layout.toWorld(d, x, h, _p));
  }

  // ---- road ribbon + edge lines ---------------------------------------------------
  {
    const STEP = 1.0, n = Math.ceil(total / STEP);
    const pos = new Float32Array((n + 1) * 6), col = new Float32Array((n + 1) * 6), uv = new Float32Array((n + 1) * 4);
    const idx = [];
    const epos = new Float32Array(n * 12), ecol = new Float32Array(n * 12);
    for (let i = 0; i <= n; i++) {
      const d = Math.min(i * STEP, total);
      layout.toWorld(d, -ROAD_HALF_W, 0.02, _p).toArray(pos, i * 6);
      layout.toWorld(d, ROAD_HALF_W, 0.02, _q).toArray(pos, i * 6 + 3);
      roomColorAt(d, 'road', _c); _c.toArray(col, i * 6); _c.toArray(col, i * 6 + 3);
      uv[i * 4] = d / 6; uv[i * 4 + 1] = 0; uv[i * 4 + 2] = d / 6; uv[i * 4 + 3] = 1;
      if (i < n) { const k = i * 2; idx.push(k, k + 1, k + 2, k + 1, k + 3, k + 2); }
      if (i < n) {   // edge line segments: this sample to the next, both sides
        const d2 = Math.min((i + 1) * STEP, total);
        roomColorAt(d, 'edge', _c);
        for (let side = 0; side < 2; side++) {
          const x = (side ? 1 : -1) * (ROAD_HALF_W + 0.06), o = i * 12 + side * 6;
          layout.toWorld(d, x, 0.05, _p).toArray(epos, o);
          layout.toWorld(d2, x, 0.05, _q).toArray(epos, o + 3);
          _c.toArray(ecol, o); _c.toArray(ecol, o + 3);
        }
      }
    }
    const g = track(new THREE.BufferGeometry());
    g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    g.setAttribute('color', new THREE.BufferAttribute(col, 3));
    g.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
    g.setIndex(idx);
    const roadTex = canvasTex(256, 128, (c, w, h) => {
      c.fillStyle = '#d9d9d9'; c.fillRect(0, 0, w, h);
      for (let x = 0; x < w; x += 32) {   // kerb ticks, alternating
        const lit = ((x / 32) % 2) === 0;
        c.fillStyle = lit ? '#ffffff' : '#9a9a9a'; c.fillRect(x, 0, 32, 9);
        c.fillStyle = lit ? '#9a9a9a' : '#ffffff'; c.fillRect(x, h - 9, 32, 9);
      }
      c.fillStyle = '#ffffff'; for (let x = 0; x < w; x += 64) c.fillRect(x, h / 2 - 1, 30, 2);
    });
    if (roadTex) texes.push(roadTex);
    const road = new THREE.Mesh(g, mat(new THREE.MeshBasicMaterial({ map: roadTex, vertexColors: true, side: THREE.DoubleSide })));
    road.name = 'race-road'; road.frustumCulled = false; group.add(road);
    const eg = track(new THREE.BufferGeometry());
    eg.setAttribute('position', new THREE.BufferAttribute(epos, 3));
    eg.setAttribute('color', new THREE.BufferAttribute(ecol, 3));
    const edges = new THREE.LineSegments(eg, mat(new THREE.LineBasicMaterial({ vertexColors: true })));
    edges.name = 'race-road-edges'; edges.frustumCulled = false; group.add(edges);
  }

  // ---- ramps: wedge + pink lip + gold air line; boost pads; sugar cubes -----------
  const feats = layout.chunks.flatMap((c) => c.features);
  const ramps = feats.filter((f) => f.type === 'ramp');
  const pads = feats.filter((f) => f.type === 'boost');
  const cubes = feats.filter((f) => f.type === 'itembox');
  const porcelain = mat(new THREE.MeshLambertMaterial({ color: 0xf6e7c8 }));
  const pinkGlow = mat(new THREE.MeshLambertMaterial({ color: 0xff69b4, emissive: 0xff69b4, emissiveIntensity: 0.9 }));
  const gold = mat(new THREE.MeshLambertMaterial({ color: 0xf2c14e, emissive: 0xf2c14e, emissiveIntensity: 0.6 }));
  const AIR_DOTS = 6;
  const wedges = new THREE.InstancedMesh(track(wedgeGeometry(ROAD_HALF_W, 4, 0.8)), porcelain, Math.max(1, ramps.length));
  const lips = new THREE.InstancedMesh(track(new THREE.BoxGeometry(ROAD_HALF_W * 2 + 0.1, 0.08, 0.14)), pinkGlow, Math.max(1, ramps.length));
  const airDots = new THREE.InstancedMesh(track(new THREE.SphereGeometry(0.22, 12, 10)), gold, Math.max(1, ramps.length * AIR_DOTS));
  ramps.forEach((r, i) => {
    wedges.setMatrixAt(i, roadMatrix(r.d, 0, 0.01, 0, 1, _m));
    lips.setMatrixAt(i, roadMatrix(r.d, 0, 0.82, 0, 1, _m));
    for (let k = 0; k < AIR_DOTS; k++) {
      const prog = (k + 1) / (AIR_DOTS + 1);
      airDots.setMatrixAt(i * AIR_DOTS + k, roadMatrix(r.d + prog * r.airLen, 0, 0.5 + r.height * Math.sin(Math.PI * prog), 0, 1, _m));
    }
  });
  wedges.count = ramps.length; lips.count = ramps.length; airDots.count = ramps.length * AIR_DOTS;
  const padTex = canvasTex(64, 64, (c, w, h) => {
    c.fillStyle = '#0e2e33'; c.fillRect(0, 0, w, h);
    c.strokeStyle = '#5be7d8'; c.lineWidth = 7; c.lineCap = 'round';
    for (let y = -16; y < h + 16; y += 22) { c.beginPath(); c.moveTo(6, y + 14); c.lineTo(w / 2, y); c.lineTo(w - 6, y + 14); c.stroke(); }
  });
  if (padTex) { padTex.repeat.set(1, 2); texes.push(padTex); }
  const padMat = mat(new THREE.MeshBasicMaterial({ map: padTex, color: padTex ? 0xffffff : 0x5be7d8, transparent: true, opacity: 0.95 }));
  const padGeo = track(new THREE.PlaneGeometry(ROAD_HALF_W * 1.5, 2.6).rotateX(-Math.PI / 2));
  const padMesh = new THREE.InstancedMesh(padGeo, padMat, Math.max(1, pads.length));
  pads.forEach((p, i) => padMesh.setMatrixAt(i, roadMatrix(p.d, p.x, 0.03, 0, 1, _m)));
  padMesh.count = pads.length;
  const cubeMat = mat(new THREE.MeshLambertMaterial({ color: 0xffffff, emissive: 0xf2c14e, emissiveIntensity: 0.4 }));
  const cubeMesh = new THREE.InstancedMesh(track(new THREE.BoxGeometry(0.55, 0.55, 0.55)), cubeMat, Math.max(1, cubes.length));
  cubes.forEach((c, i) => cubeMesh.setMatrixAt(i, roadMatrix(c.d, c.x, 0.7, 0, 1, _m)));
  cubeMesh.count = cubes.length;
  for (const m of [wedges, lips, airDots, padMesh, cubeMesh]) { m.frustumCulled = false; m.instanceMatrix.needsUpdate = true; group.add(m); }

  // ---- per-room wall props ----------------------------------------------------------
  const roomSets = [];   // { id, d0, d1, meshes:[{mesh, sway?, base:[...], phase:[...], d:[...]}] }
  for (const span of spans) {
    const spec = specOf(span.id);
    const chunksHere = layout.chunks.filter((c) => c.room === span.id && c.kind !== 'loop' && c.kind !== 'gate');
    const set = { id: span.id, d0: span.d0, d1: span.d1, meshes: [] };
    let prev = null;
    for (const shape of propShapes(spec.propKind)) {
      track(shape.geo);
      const material = mat(shape.shiny
        ? new THREE.MeshStandardMaterial({ color: shape.color || spec.colors.prop, metalness: 0.9, roughness: 0.15, side: shape.side || THREE.FrontSide })
        : new THREE.MeshLambertMaterial({ color: shape.color || spec.colors.prop, side: shape.side || THREE.FrontSide,
          emissive: shape.glow ? (shape.color || spec.colors.prop) : 0x000000, emissiveIntensity: shape.glow ? 1.0 : 0 }));
      const count = shape.n * Math.max(1, chunksHere.length);
      const mesh = new THREE.InstancedMesh(shape.geo, material, count);
      const entry = { mesh, sway: !!shape.sway, base: [], phase: [], d: [], glow: !!shape.glow, mat: material };
      let i = 0;
      if (shape.follow && prev) {
        for (; i < prev.base.length; i++) { mesh.setMatrixAt(i, prev.base[i]); entry.base.push(prev.base[i]); entry.phase.push(prev.phase[i]); entry.d.push(prev.d[i]); }
      } else for (const ch of chunksHere) for (let k = 0; k < shape.n; k++, i++) {
        const d = ch.d0 + 2 + rng() * (ch.d1 - ch.d0 - 4);
        const phi = (rng() < 0.5 ? -1 : 1) * (0.12 + rng() * 0.58) * Math.PI;  // from the ceiling down to just above the kerb
        const rad = RADIUS - 0.35 - rng() * 0.3;
        const x = rad * Math.sin(phi), h = ROAD_DROP + rad * Math.cos(phi);
        const f = layout.frameAtDepth(d);
        layout.toWorld(d, x, h, _p);
        const inward = layout.toWorld(d, 0, ROAD_DROP, _q).sub(_p).normalize();
        const scale = 0.7 + rng() * 0.8;
        if (shape.orient === 'tumble') {
          _e.set(rng() * 6.28, rng() * 6.28, rng() * 6.28);
          _m.makeRotationFromEuler(_e);
        } else {
          const side = new THREE.Vector3().crossVectors(inward, f.tangent).normalize();
          if (shape.orient === 'stand') _m.makeBasis(side, inward, new THREE.Vector3().crossVectors(side, inward));
          else _m.makeBasis(side, new THREE.Vector3().crossVectors(inward, side), inward);
          _m.multiply(_r.makeRotationY(rng() * 6.28 * (shape.orient === 'stand' ? 1 : 0.08)));
        }
        _s.set(scale, scale, shape.squash ? scale * shape.squash : scale);
        _m.scale(_s).setPosition(_p);
        mesh.setMatrixAt(i, _m);
        entry.base.push(_m.clone()); entry.phase.push(rng() * 6.28); entry.d.push(d);
        if (spec.propAlt && !shape.color) mesh.setColorAt(i, _c.setHex(k % 3 === 0 ? spec.colors.prop : spec.propAlt[k % spec.propAlt.length]));
      }
      mesh.count = i;
      mesh.instanceMatrix.needsUpdate = true;
      if (mesh.instanceColor) mesh.instanceColor.needsUpdate = true;
      mesh.frustumCulled = false;
      mesh.name = 'race-props-' + span.id;
      group.add(mesh);
      set.meshes.push(entry);
      prev = entry;
    }
    roomSets.push(set);
  }

  // ---- light so the Lambert props read (the tunnel shader ignores lights) ---------
  const hemi = new THREE.HemisphereLight(0xffd6ee, 0x1a1a2e, 1.1);
  const sun = new THREE.DirectionalLight(0xf6e7c8, 0.6); sun.position.set(0.3, 1, 0.2);
  group.add(hemi, sun);
  scene.add(group);

  // ---- runtime ----------------------------------------------------------------------
  const VIEW = 160;          // metres: rooms farther than this from the kart are hidden
  const ANIM = 90;           // metres: props within this range animate
  const now = () => (typeof performance !== 'undefined' ? performance.now() : Date.now()) / 1000;
  const t0 = now();
  const _sw = new THREE.Matrix4();
  function update(d) {
    const t = now() - t0;
    for (const set of roomSets) {
      const w = layout.wrap(d);
      const near = (w >= set.d0 - VIEW && w <= set.d1 + VIEW) || wrapDist(w, set.d0) < VIEW || wrapDist(w, set.d1) < VIEW;
      for (const entry of set.meshes) {
        entry.mesh.visible = near;
        if (!near) continue;
        if (entry.glow) entry.mat.emissiveIntensity = 0.85 + 0.15 * Math.sin(t * 9 + set.d0);
        if (!entry.sway) continue;
        let dirty = false;
        for (let i = 0; i < entry.base.length; i++) {
          if (wrapDist(entry.d[i], w) > ANIM) continue;
          _sw.makeRotationZ(0.22 * Math.sin(t * 1.3 + entry.phase[i]));
          entry.mesh.setMatrixAt(i, _m.copy(entry.base[i]).multiply(_sw));
          dirty = true;
        }
        if (dirty) entry.mesh.instanceMatrix.needsUpdate = true;
      }
    }
    let dirty = false;
    cubes.forEach((c, i) => {
      if (wrapDist(c.d, d) > ANIM) return;
      cubeMesh.setMatrixAt(i, roadMatrix(c.d, c.x, 0.7 + 0.08 * Math.sin(t * 2 + i), t * 1.1 + i, 1, _m));
      dirty = true;
    });
    if (dirty) cubeMesh.instanceMatrix.needsUpdate = true;
  }

  /** Hand the room's biome style to fx.applyRegionGrade. Returns the room spec. */
  function applyRoom(fx, roomId, fadeSec = 3.2) {
    const spec = specOf(roomId);
    const biome = biomeById(spec.biome);
    if (fx && typeof fx.applyRegionGrade === 'function') fx.applyRegionGrade(biome ? biome.style : null, fadeSec);
    return spec;
  }

  function dispose() {
    scene.remove(group);
    for (const g of geos) g.dispose();
    for (const m of mats) m.dispose();
    for (const t of texes) t.dispose();
    for (const set of roomSets) for (const e of set.meshes) e.mesh.dispose();
    for (const m of [wedges, lips, airDots, padMesh, cubeMesh]) m.dispose();
    roomSets.length = 0;
  }

  return { update, applyRoom, dispose, group, spans, rooms: specs };
}
