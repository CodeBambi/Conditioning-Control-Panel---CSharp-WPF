/* ============================================================================
 * race/roomProps.js - The Caucus Race diegetic props. Everything is grounded:
 * shoulder props stand on a chunky step block beside the kerb, wall props sit
 * flush on the tube wall at shoulder height and above. Nothing floats mid-tube.
 *
 * Look: pixel / voxel. Every prop is a handful of flat-coloured boxes merged into
 * one geometry (voxel()), drawn as ONE InstancedMesh per prop kind per room, lit by
 * the dresser's hemisphere + sun so the faces shade like blocks. The few "screen"
 * props (neon sign, porthole, stained glass, fluorescent tube) are unlit quads with
 * a tiny nearest-filtered canvas.
 *
 * Frames (never computed by hand, always through layout):
 *   shoulder: origin toWorld(d, side*SH_X, 0) at the kerb's outer edge; local +x points
 *             away from the road, +y is road up, +z runs down the tube. The tube wall
 *             meets the road plane at x=3.15, so a plinth block bridges kerb and wall
 *             and the prop stands on top of it, inside the tube (wall x at h=0.6 is 3.9).
 *   wall:     origin on the wall at `angle` from the ceiling (radians, signed);
 *             local +z points into the tube, +y toward the ceiling along the wall,
 *             +x reads left-to-right for someone looking at it from the road.
 *
 * Draw calls: per room 2..3 (wall kind, shoulder kind, optional extra) plus two
 * shared meshes (step blocks, risers) = 21 for the eight rooms. Rooms out of view
 * are hidden; animation touches only instances within ANIM metres of the kart.
 *
 * createRoomProps({ scene, group, layout, spans, specOf, rng }) -> { update(d, t), dispose }
 * ==========================================================================*/

import * as THREE from 'three';
import { RADIUS, ROAD_DROP } from './consts.js';

const SH_X = 3.5;                                               // shoulder origin: the kerb's outer edge, road level
const SH_H = 0;
const STEP = { w: 1.05, h: 0.6, l: 0.9, in: 0.45 };             // plinth beside the kerb: overlaps it by `in`, runs
                                                                // out into the wall (the tube hides the rest)
const WALL_INSET = 0.06;
const ANIM = 90;                                                // metres: instances nearer than this animate
const VIEW = 160;                                               // metres: rooms farther than this are hidden
const TAU = Math.PI * 2;

/** Canvas -> chunky pixel texture: nearest filtering, no mipmaps (big texels are the look). */
export function pixelTex(w, h, draw, opts = {}) {
  if (typeof document === 'undefined') return null;
  const c = document.createElement('canvas'); c.width = w; c.height = h;
  const ctx = c.getContext('2d');
  ctx.imageSmoothingEnabled = false;
  draw(ctx, w, h);
  const t = new THREE.CanvasTexture(c);
  t.wrapS = t.wrapT = opts.clamp ? THREE.ClampToEdgeWrapping : THREE.RepeatWrapping;
  t.magFilter = THREE.NearestFilter; t.minFilter = THREE.NearestFilter;
  t.generateMipmaps = false; t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

// ---- the voxel kit -------------------------------------------------------------------
// parts: [{ s:[w,h,d], p:[x,y,z], c:hex, r?:[rx,ry,rz] }] -> one indexed geometry with
// flat per-part vertex colours. Boxes only: that is the whole point of the look.
const _col = new THREE.Color(), _eul = new THREE.Euler();
function voxel(parts) {
  const pos = [], nor = [], uv = [], col = [], idx = [];
  let base = 0;
  for (const part of parts) {
    const g = new THREE.BoxGeometry(part.s[0], part.s[1], part.s[2]);
    if (part.r) g.applyMatrix4(new THREE.Matrix4().makeRotationFromEuler(_eul.set(part.r[0], part.r[1], part.r[2])));
    g.translate(part.p[0], part.p[1], part.p[2]);
    const p = g.attributes.position.array, n = g.attributes.normal.array, u = g.attributes.uv.array;
    _col.setHex(part.c);
    for (let i = 0; i < p.length; i++) pos.push(p[i]);
    for (let i = 0; i < n.length; i++) nor.push(n[i]);
    for (let i = 0; i < u.length; i++) uv.push(u[i]);
    for (let i = 0; i < p.length / 3; i++) col.push(_col.r, _col.g, _col.b);
    const ix = g.index.array;
    for (let i = 0; i < ix.length; i++) idx.push(ix[i] + base);
    base += p.length / 3;
    g.dispose();
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('normal', new THREE.Float32BufferAttribute(nor, 3));
  g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
  g.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
  g.setIndex(idx);
  return g;
}
const box = (s, p, c, r) => ({ s, p, c, r });

// ---- prop vignettes per room ------------------------------------------------------------
// Each room: wall (flush, mid band), shoulder (on a step block), optional extra (on the
// shoulder too), and where the risers (steam, bubbles, sparks) come from.
// anim: bounce | bob | tumble | sway | flicker | glint | none. screen: unlit canvas quad.
const CREAM = 0xf6e7c8, PINK = 0xffb6d9, GOLD = 0xf2c14e, WHITE = 0xffffff;
function teacupPair() {
  const cup = (x, c) => [
    box([0.5, 0.05, 0.5], [x, 0.065, 0.25], CREAM), box([0.36, 0.32, 0.36], [x, 0.25, 0.25], c),
    box([0.08, 0.16, 0.06], [x + 0.22, 0.25, 0.25], c), box([0.3, 0.02, 0.3], [x, 0.42, 0.25], 0x8a5a3a),
  ];
  return [box([1.4, 0.08, 0.5], [0, 0, 0.25], 0xc9b79a), box([0.06, 0.2, 0.4], [-0.6, -0.14, 0.2], 0xa8967a),
    box([0.06, 0.2, 0.4], [0.6, -0.14, 0.2], 0xa8967a), ...cup(-0.4, PINK), ...cup(0.4, 0xbfebd8)];
}
const ROOM_PROPS = {
  teagarden: {
    wall: { parts: teacupPair(), every: 13, risers: [[-0.4, 0.5, 0.25], [0.4, 0.5, 0.25]], riserColor: WHITE },
    shoulder: { every: 22, gateOnly: false, parts: [
      box([0.5, 0.42, 0.5], [0.3, 0.21, 0.1], PINK), box([0.3, 0.08, 0.3], [0.3, 0.46, 0.1], CREAM), box([0.1, 0.1, 0.1], [0.3, 0.55, 0.1], CREAM),
      box([0.12, 0.12, 0.22], [0.3, 0.3, 0.44], PINK), box([0.08, 0.24, 0.06], [0.3, 0.26, -0.2], PINK),
      box([0.16, 0.16, 0.16], [0.16, 0.08, -0.32], WHITE), box([0.16, 0.16, 0.16], [0.42, 0.08, -0.34], WHITE), box([0.16, 0.16, 0.16], [0.29, 0.24, -0.33], WHITE)] },
  },
  toybox: {
    wall: { every: 14, parts: [box([1.3, 0.08, 0.5], [0, 0, 0.25], 0x8a5a3a), box([0.34, 0.34, 0.34], [-0.42, 0.21, 0.25], 0xff4d6d),
      box([0.34, 0.34, 0.34], [0, 0.21, 0.25], 0xffd23f), box([0.34, 0.34, 0.34], [0.42, 0.21, 0.25], 0x3a86ff), box([0.26, 0.26, 0.26], [0, 0.51, 0.25], PINK)] },
    shoulder: { every: 16, anim: 'bounce', parts: [box([0.4, 0.4, 0.4], [0.3, 0.2, 0], 0xff4d6d), box([0.36, 0.36, 0.36], [0.32, 0.58, 0.04], 0xffd23f), box([0.3, 0.3, 0.3], [0.28, 0.91, -0.02], 0x3a86ff)] },
    extra: { every: 34, anim: 'bob', parts: [box([0.5, 0.5, 0.5], [0.3, 0.25, 0], 0x3a86ff), box([0.5, 0.06, 0.5], [0.3, 0.6, -0.24], 0xffd23f, [-0.9, 0, 0]),
      box([0.22, 0.05, 0.22], [0.3, 0.62, 0], 0xffd23f), box([0.18, 0.05, 0.18], [0.3, 0.76, 0], 0xffd23f), box([0.22, 0.05, 0.22], [0.3, 0.9, 0], 0xffd23f), box([0.28, 0.28, 0.28], [0.3, 1.1, 0], PINK)] },
  },
  casino: {
    wall: { every: 12, screen: 'neon', size: [1.6, 0.8] },
    shoulder: { every: 14, risers: [[0.3, 0.5, 0]], riserColor: GOLD, parts: (() => {
      const out = []; const stack = (x, z, n, cols) => { for (let i = 0; i < n; i++) out.push(box([0.34, 0.06, 0.34], [x, 0.03 + i * 0.065, z], cols[i % cols.length])); };
      stack(0.2, -0.2, 6, [0xa3122e, WHITE]); stack(0.42, 0.18, 4, [GOLD, 0x1a1a2e]); stack(0.16, 0.24, 3, [WHITE, 0xa3122e]); return out; })() },
    extra: { every: 30, anim: 'tumble', parts: [box([0.36, 0.36, 0.36], [0.3, 0.4, 0], WHITE),
      box([0.07, 0.07, 0.02], [0.3, 0.4, 0.18], 0x1a1a2e), box([0.07, 0.07, 0.02], [0.2, 0.5, 0.18], 0x1a1a2e), box([0.07, 0.07, 0.02], [0.4, 0.3, 0.18], 0x1a1a2e),
      box([0.02, 0.07, 0.07], [0.48, 0.4, 0], 0x1a1a2e), box([0.07, 0.02, 0.07], [0.3, 0.58, 0.08], 0x1a1a2e), box([0.07, 0.02, 0.07], [0.3, 0.58, -0.08], 0x1a1a2e)] },
  },
  undertow: {
    wall: { every: 13, screen: 'porthole', size: [1.2, 1.2] },
    shoulder: { every: 9, anim: 'sway', risers: [[0.3, 2.3, 0]], riserColor: 0x7fe7f0, parts: [
      box([0.3, 0.5, 0.14], [0.3, 0.25, 0], 0x1fa9b5), box([0.26, 0.5, 0.12], [0.3, 0.75, 0.05], 0x27b8a0), box([0.2, 0.5, 0.1], [0.3, 1.25, 0], 0x1fa9b5),
      box([0.14, 0.4, 0.08], [0.3, 1.7, -0.04], 0x5be7d8), box([0.16, 0.3, 0.06], [0.14, 0.9, 0.1], 0x27b8a0), box([0.16, 0.3, 0.06], [0.46, 1.3, -0.1], 0x27b8a0)] },
  },
  mirrors: {
    wall: { every: 10, anim: 'glint', parts: [box([1.2, 1.7, 0.08], [0, 0, 0.04], 0x4a4f66), box([1.04, 1.54, 0.06], [0, 0, 0.08], 0xdde3f0)] },
    shoulder: { every: 18, parts: [box([0.32, 0.6, 0.05], [0.24, 0.3, 0.1], 0xdde3f0, [0, 0.4, 0.15]), box([0.26, 0.44, 0.05], [0.42, 0.22, -0.2], 0xc9d3ea, [0, -0.7, -0.1]),
      box([0.2, 0.3, 0.05], [0.16, 0.15, -0.3], 0x5be7d8, [0, 1.2, 0.25])] },
  },
  chapel: {
    wall: { every: 12, screen: 'glass', size: [1.2, 1.6] },
    shoulder: { every: 10, anim: 'flicker', risers: [[0.3, 0.9, 0]], riserColor: 0xffd696, parts: [
      box([0.14, 0.5, 0.14], [0.18, 0.25, -0.2], WHITE), box([0.14, 0.7, 0.14], [0.36, 0.35, 0.05], WHITE), box([0.14, 0.6, 0.14], [0.2, 0.3, 0.26], WHITE),
      box([0.1, 0.14, 0.1], [0.18, 0.57, -0.2], GOLD), box([0.1, 0.14, 0.1], [0.36, 0.77, 0.05], GOLD), box([0.1, 0.14, 0.1], [0.2, 0.67, 0.26], GOLD),
      box([0.5, 0.06, 0.7], [0.3, 0.03, 0], 0xe23c9c)] },
  },
  greyward: {
    wall: { every: 11, screen: 'fluoro', size: [1.6, 0.4] },
    shoulder: { every: 15, parts: [box([0.54, 0.08, 0.9], [0.3, 0.22, 0], 0x6d7278), box([0.5, 0.14, 0.9], [0.3, 0.33, 0], 0xdde3f0), box([0.2, 0.08, 0.3], [0.3, 0.44, -0.28], WHITE),
      box([0.06, 0.18, 0.06], [0.08, 0.09, -0.4], 0x6d7278), box([0.06, 0.18, 0.06], [0.52, 0.09, -0.4], 0x6d7278), box([0.06, 0.18, 0.06], [0.08, 0.09, 0.4], 0x6d7278), box([0.06, 0.18, 0.06], [0.52, 0.09, 0.4], 0x6d7278),
      box([0.05, 1.4, 0.05], [0.3, 0.7, 0.6], 0x9aa0a6), box([0.3, 0.04, 0.04], [0.3, 1.4, 0.6], 0x9aa0a6), box([0.16, 0.24, 0.08], [0.42, 1.26, 0.6], 0xbfd7e0)] },
  },
  coronation: {
    wall: { every: 12, parts: [box([0.9, 2.2, 0.06], [0, -0.2, 0.03], 0x7a0f2b), box([1.2, 0.08, 0.08], [0, 0.95, 0.06], GOLD), box([0.9, 0.12, 0.02], [0, 0.3, 0.07], GOLD),
      box([0.14, 0.14, 0.04], [-0.2, -0.4, 0.07], GOLD), box([0.14, 0.14, 0.04], [0, -0.4, 0.07], GOLD), box([0.14, 0.14, 0.04], [0.2, -0.4, 0.07], GOLD), box([0.5, 0.1, 0.04], [0, -0.55, 0.07], GOLD)] },
    shoulder: { every: 14, parts: [box([0.56, 0.12, 0.56], [0.3, 0.06, 0], CREAM), box([0.44, 2.0, 0.44], [0.3, 1.0, 0], 0xe9dcc0), box([0.6, 0.16, 0.6], [0.3, 2.08, 0], GOLD)] },
    extra: { every: 14, anim: 'spin', onShoulderTop: 2.16, risers: [[0.3, 0.5, 0]], riserColor: PINK, parts: [
      box([0.5, 0.1, 0.06], [0.3, 0.05, 0.22], GOLD), box([0.5, 0.1, 0.06], [0.3, 0.05, -0.22], GOLD), box([0.06, 0.1, 0.5], [0.08, 0.05, 0], GOLD), box([0.06, 0.1, 0.5], [0.52, 0.05, 0], GOLD),
      box([0.08, 0.16, 0.08], [0.08, 0.16, 0.22], GOLD), box([0.08, 0.16, 0.08], [0.52, 0.16, 0.22], GOLD), box([0.08, 0.16, 0.08], [0.08, 0.16, -0.22], GOLD), box([0.08, 0.16, 0.08], [0.52, 0.16, -0.22], GOLD),
      box([0.1, 0.1, 0.1], [0.3, 0.22, 0.22], 0xe23c9c)] },
  },
};

// ---- the screens (unlit pixel quads) ----------------------------------------------------
function screenTex(kind, rng) {
  switch (kind) {
    case 'neon': return pixelTex(48, 24, (c, w, h) => {
      c.fillStyle = '#2a050f'; c.fillRect(0, 0, w, h);
      c.fillStyle = '#f2c14e'; c.fillRect(0, 0, w, 1); c.fillRect(0, h - 1, w, 1); c.fillRect(0, 0, 1, h); c.fillRect(w - 1, 0, 1, h);
      c.font = 'bold 15px sans-serif'; c.textAlign = 'center'; c.textBaseline = 'middle';
      c.fillStyle = '#ff69b4'; c.fillText('PAYS', w / 2, h / 2 + 1);
    }, { clamp: true });
    case 'porthole': return pixelTex(32, 32, (c, w, h) => {
      c.fillStyle = '#0a1a22'; c.fillRect(0, 0, w, h);
      c.fillStyle = '#9aa0a6'; c.beginPath(); c.arc(16, 16, 15, 0, TAU); c.fill();
      c.fillStyle = '#0c3a5a'; c.beginPath(); c.arc(16, 16, 12, 0, TAU); c.fill();
      c.fillStyle = '#7fe7f0'; c.fillRect(8, 13, 2, 2); c.fillRect(19, 9, 2, 2); c.fillRect(22, 18, 2, 2);
      c.fillStyle = '#ffb6d9'; c.fillRect(11, 18, 5, 3); c.fillRect(16, 19, 2, 1); c.fillRect(9, 17, 2, 5);
    }, { clamp: true });
    case 'glass': return pixelTex(24, 32, (c, w, h) => {
      c.fillStyle = '#2a0820'; c.fillRect(0, 0, w, h);
      const pal = ['#e23c9c', '#f2c14e', '#f6e7c8', '#ffb6d9', '#bfebd8'];
      for (let y = 1; y < h - 1; y += 5) for (let x = 1; x < w - 1; x += 4) { c.fillStyle = pal[Math.floor(rng() * pal.length)]; c.fillRect(x, y, 3, 4); }
      c.fillStyle = '#fff6ea'; c.beginPath(); c.arc(12, 8, 5, 0, TAU); c.fill();
      c.fillStyle = '#e23c9c'; c.beginPath(); c.arc(12, 8, 3, 0, TAU); c.fill();
    }, { clamp: true });
    case 'fluoro': return pixelTex(32, 8, (c, w, h) => {
      c.fillStyle = '#2b2b33'; c.fillRect(0, 0, w, h);
      c.fillStyle = '#ffffff'; c.fillRect(2, 3, w - 4, 2);
      c.fillStyle = '#dde3f0'; c.fillRect(2, 2, w - 4, 1); c.fillRect(2, 5, w - 4, 1);
    }, { clamp: true });
    default: return null;
  }
}

// ---- the builder -------------------------------------------------------------------------
export function createRoomProps({ scene, group, layout, spans, specOf, rng }) {
  const total = layout.totalDepth;
  const wrapDist = (a, b) => { const w = layout.wrap(a - b); return Math.min(w, total - w); };
  const loopChunk = layout.chunks.find((c) => c.kind === 'loop');
  const geos = [], mats = [], texes = [], meshes = [];
  const lambert = new THREE.MeshLambertMaterial({ vertexColors: true }); mats.push(lambert);
  const _p = new THREE.Vector3(), _q = new THREE.Vector3(), _m = new THREE.Matrix4(), _r = new THREE.Matrix4(), _t = new THREE.Matrix4();
  const _x = new THREE.Vector3(), _y = new THREE.Vector3(), _z = new THREE.Vector3(), _s = new THREE.Vector3(), _c = new THREE.Color();

  /** Shoulder frame: origin where the wall meets lateral +-SH_X, +x away from the road. */
  function shoulderMatrix(d, side, out, lift = 0) {
    const f = layout.frameAtDepth(d);
    _x.copy(f.right).multiplyScalar(side); _y.copy(f.up); _z.copy(f.tangent).multiplyScalar(side);
    out.makeBasis(_x, _y, _z);
    return out.setPosition(layout.toWorld(d, side * SH_X, SH_H + lift, _p));
  }
  /** Wall frame: origin on the wall at `angle` from the ceiling, +z into the tube, +y toward the ceiling. */
  function wallMatrix(d, angle, out) {
    const f = layout.frameAtDepth(d);
    const ca = Math.cos(angle), sa = Math.sin(angle), R = RADIUS - WALL_INSET;
    layout.toWorld(d, sa * R, ROAD_DROP + ca * R, _p);
    _z.copy(f.up).multiplyScalar(-ca).addScaledVector(f.right, -sa).normalize();                   // inward
    _y.copy(f.up).multiplyScalar(Math.abs(sa)).addScaledVector(f.right, -Math.sign(angle) * ca).normalize();
    _x.crossVectors(_y, _z).normalize();
    return out.makeBasis(_x, _y, _z).setPosition(_p);
  }

  // shared step blocks (one draw call) and risers (one draw call)
  const stepGeo = voxel([box([STEP.w, STEP.h, STEP.l], [STEP.w / 2 - STEP.in, STEP.h / 2, 0], WHITE)]); geos.push(stepGeo);
  const riserGeo = voxel([box([0.18, 0.18, 0.18], [0, 0, 0], WHITE)]); geos.push(riserGeo);
  const stepList = [], riserList = [];                     // { m: Matrix4, color }  /  { base: Matrix4, phase, d, color }

  const roomSets = [];   // { d0, d1, meshes: [{ mesh, base:[], phase:[], d:[], anim, mat, screen }] }
  for (const span of spans) {
    const spec = specOf(span.id), def = ROOM_PROPS[span.id] || ROOM_PROPS.teagarden;
    const chunks = layout.chunks.filter((c) => c.room === span.id && c.kind !== 'loop');
    const set = { id: span.id, d0: span.d0, d1: span.d1, meshes: [] };
    _c.setHex(spec.colors.prop).multiplyScalar(0.55);
    const stepColor = _c.getHex();

    // placements along the room's chunks, alternating sides, jittered
    const slots = (every, margin = 3) => {
      const out = [];
      let side = rng() < 0.5 ? 1 : -1;
      for (const ch of chunks) {
        for (let d = ch.d0 + margin + rng() * every * 0.5; d < ch.d1 - margin; d += every * (0.8 + rng() * 0.4)) {
          out.push({ d, side }); side = -side;
        }
      }
      return out;
    };
    const shoulderSlots = slots(def.shoulder.every);

    const build = (kind, cfg, place) => {
      let geo, material;
      if (cfg.screen) {
        const tex = screenTex(cfg.screen, rng); if (tex) texes.push(tex);
        geo = new THREE.PlaneGeometry(cfg.size[0], cfg.size[1]);
        material = new THREE.MeshBasicMaterial({ map: tex, color: tex ? 0xffffff : spec.colors.prop });
        mats.push(material);
      } else {
        // wall props get a mounting plate behind them so they read as fixed to the wall
        // even where the room's grade paints the tube near-black (Tea Garden)
        const plate = kind === 'wall' ? [box([1.7, 1.1, 0.06], [0, 0.4, -0.03], stepColor)] : [];
        geo = voxel([...plate, ...cfg.parts]); material = lambert;
      }
      geos.push(geo);
      const list = place();
      const mesh = new THREE.InstancedMesh(geo, material, Math.max(1, list.length));
      const entry = { mesh, base: [], phase: [], d: [], anim: cfg.anim || 'none', mat: material, screen: cfg.screen || null };
      list.forEach((it, i) => {
        mesh.setMatrixAt(i, it.m);
        entry.base.push(it.m.clone()); entry.phase.push(rng() * TAU); entry.d.push(it.d);
        if (cfg.risers) for (const off of cfg.risers) riserList.push({ base: it.m.clone().multiply(_t.makeTranslation(off[0], off[1], off[2])), phase: rng() * TAU, d: it.d, color: cfg.riserColor || WHITE });
      });
      mesh.count = list.length; mesh.instanceMatrix.needsUpdate = true; mesh.frustumCulled = false;
      mesh.name = `race-prop-${span.id}-${kind}`;
      group.add(mesh); meshes.push(mesh); set.meshes.push(entry);
    };
    build('wall', def.wall, () => slots(def.wall.every, 4).map((s) => {
      const angle = s.side * (1.55 + rng() * 0.32);     // 89..108 deg from the ceiling: the mid band above the shoulder
      return { d: s.d, m: wallMatrix(s.d, angle, new THREE.Matrix4()) };
    }));
    build('shoulder', def.shoulder, () => shoulderSlots.map((s) => {
      const m = shoulderMatrix(s.d, s.side, new THREE.Matrix4(), STEP.h);
      stepList.push({ m: shoulderMatrix(s.d, s.side, new THREE.Matrix4()), color: stepColor });
      return { d: s.d, m };
    }));
    if (def.extra) build('extra', def.extra, () => {
      if (def.extra.onShoulderTop != null) return shoulderSlots.map((s) => ({ d: s.d, m: shoulderMatrix(s.d, s.side, new THREE.Matrix4(), STEP.h + def.extra.onShoulderTop) }));
      return slots(def.extra.every, 6).map((s) => {
        stepList.push({ m: shoulderMatrix(s.d, s.side, new THREE.Matrix4()), color: stepColor });
        return { d: s.d, m: shoulderMatrix(s.d, s.side, new THREE.Matrix4(), STEP.h) };
      });
    });
    roomSets.push(set);
  }

  const steps = new THREE.InstancedMesh(stepGeo, lambert, Math.max(1, stepList.length));
  stepList.forEach((s, i) => { steps.setMatrixAt(i, s.m); steps.setColorAt(i, _c.setHex(s.color)); });
  steps.count = stepList.length; steps.frustumCulled = false; steps.name = 'race-prop-steps';
  const risers = new THREE.InstancedMesh(riserGeo, lambert, Math.max(1, riserList.length));
  riserList.forEach((r, i) => { risers.setMatrixAt(i, r.base); risers.setColorAt(i, _c.setHex(r.color)); });
  risers.count = riserList.length; risers.frustumCulled = false; risers.name = 'race-prop-risers';
  for (const m of [steps, risers]) { m.instanceMatrix.needsUpdate = true; if (m.instanceColor) m.instanceColor.needsUpdate = true; group.add(m); meshes.push(m); }

  // ---- runtime ------------------------------------------------------------------------------
  const _sc = new THREE.Vector3();
  function update(d, t) {
    const w = layout.wrap(d);
    for (const set of roomSets) {
      const near = wrapDist(w, set.d0) < VIEW || wrapDist(w, set.d1) < VIEW || (w >= set.d0 && w <= set.d1);
      for (const e of set.meshes) {
        e.mesh.visible = near;
        if (!near) continue;
        if (e.screen === 'neon') e.mat.color.setScalar(Math.sin(t * 31) > 0.92 ? 0.55 : 1);
        else if (e.screen === 'fluoro') e.mat.color.setScalar(Math.sin(t * 17 + set.d0) * Math.sin(t * 5.3) > 0.85 ? 0.35 : 1);
        else if (e.screen === 'glass') e.mat.color.setScalar(0.85 + 0.15 * Math.sin(t * 1.1 + set.d0));
        if (e.anim === 'none') continue;
        let dirty = false, dirtyColor = false;
        for (let i = 0; i < e.base.length; i++) {
          if (wrapDist(e.d[i], w) > ANIM) continue;
          const ph = e.phase[i];
          switch (e.anim) {
            case 'bounce': _m.copy(e.base[i]).multiply(_t.makeTranslation(0, Math.abs(Math.sin(t * 3.2 + ph)) * 0.18, 0)); break;
            case 'bob': _m.copy(e.base[i]).multiply(_t.makeTranslation(0.3, 0.06 + 0.06 * Math.sin(t * 2.4 + ph), 0)).multiply(_r.makeRotationY(t * 1.2 + ph)).multiply(_t.makeTranslation(-0.3, 0, 0)); break;
            case 'spin': _m.copy(e.base[i]).multiply(_t.makeTranslation(0.3, 0, 0)).multiply(_r.makeRotationY(t * 0.9 + ph)).multiply(_t.makeTranslation(-0.3, 0, 0)); break;
            case 'tumble': _m.copy(e.base[i]).multiply(_t.makeTranslation(0.3, 0.4, 0)).multiply(_r.makeRotationFromEuler(_eul.set(t * 1.7 + ph, t * 1.1, ph))).multiply(_t.makeTranslation(-0.3, -0.4, 0)); break;
            case 'sway': _m.copy(e.base[i]).multiply(_t.makeTranslation(0.3, 0, 0)).multiply(_r.makeRotationFromEuler(_eul.set(0.16 * Math.sin(t * 1.1 + ph), 0, 0.2 * Math.sin(t * 1.4 + ph)))).multiply(_t.makeTranslation(-0.3, 0, 0)); break;
            case 'flicker': e.mesh.setColorAt(i, _c.setScalar(0.85 + 0.25 * Math.abs(Math.sin(t * 13 + ph) * Math.sin(t * 5 + ph * 2)))); dirtyColor = true; continue;
            case 'glint': { const k = ((t * 0.35 + ph / TAU) % 1); const g = k < 0.12 ? 1 + 2.4 * Math.sin(k / 0.12 * Math.PI) : 1; e.mesh.setColorAt(i, _c.setScalar(g)); dirtyColor = true; continue; }
            default: continue;
          }
          e.mesh.setMatrixAt(i, _m); dirty = true;
        }
        if (dirty) e.mesh.instanceMatrix.needsUpdate = true;
        if (dirtyColor && e.mesh.instanceColor) e.mesh.instanceColor.needsUpdate = true;
      }
    }
    let dirty = false;
    for (let i = 0; i < riserList.length; i++) {
      const r = riserList[i];
      if (wrapDist(r.d, w) > ANIM) continue;
      const k = (t * 0.35 + r.phase / TAU) % 1;
      _m.copy(r.base).multiply(_t.makeTranslation(0.08 * Math.sin(t * 2 + r.phase), k * 1.1, 0)).scale(_sc.setScalar(1 - 0.75 * k));
      risers.setMatrixAt(i, _m); dirty = true;
    }
    if (dirty) risers.instanceMatrix.needsUpdate = true;
  }

  function dispose() {
    for (const m of meshes) { group.remove(m); m.dispose(); }
    for (const g of geos) g.dispose();
    for (const m of mats) m.dispose();
    for (const t of texes) t.dispose();
    roomSets.length = 0; riserList.length = 0; stepList.length = 0;
  }

  return { update, dispose, meshes };
}

// self-check: node --check is the bar; the kit only touches the DOM inside pixelTex.
