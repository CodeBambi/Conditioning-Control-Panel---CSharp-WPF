/* ============================================================================
 * race/rooms.js - The Caucus Race rooms and road furniture.
 *
 * Implements CONTRACT.md section "race/rooms.js": ROOMS (the eight room specs),
 * rollRoomOrder(seed) (Tea Garden first, then the rest dealt loud/soft/loud so the
 * contrast rule holds) and createRoomDresser({ scene, layout, rooms }) which builds
 * everything that sits on or around the road: the pixel-tiled road ribbon with its
 * chequered kerbs and centre dash (one draw call, room-tinted through an alpha mask),
 * tiled ramp wedges with a pink lip and gold air-line cubes, lane-wide boost pads
 * with running chevrons, bobbing sugar-cube item boxes, and the diegetic props from
 * race/roomProps.js. update(d) culls rooms out of sight and animates what is near
 * the kart; applyRoom(fx, roomId, fadeSec) hands the room's biome style to
 * fx.applyRegionGrade; breakItemBox(feature) smashes a crossed sugar cube (pooled
 * shards, a white flash, regrowth after RESPAWN_SEC); dispose() tears it all down.
 *
 * Every position goes through layout.toWorld(d, x, h) and every orientation through
 * layout.frameAtDepth(d). Draw calls: 6 for the furniture + 21 for the props (27).
 * The dresser adds its own hemisphere + directional light so the Lambert props read
 * without run.js having to know about them.
 * ==========================================================================*/

import * as THREE from 'three';
import { makeRng, CAM_BACK } from './consts.js';
import { biomeById } from '../game/biomes.js';
import { createRoomProps, pixelTex } from './roomProps.js';

// ---- the eight rooms -------------------------------------------------------------
// colors: road = ribbon tint, edge = kerb lines, prop = wall props, fog = the room's haze
// (informational; the biome palette is what fx.js grades), banner = the MARQUEE plate.
// bubbleBias multiplies bubbles.js kind weights; ambient names a fieldFx particle field.
export const ROOMS = [
  { id: 'teagarden', name: 'The Tea Garden', tagline: 'nothing here fights you', biome: 'mirrorlake',
    colors: { road: 0x2f6e50, edge: 0xf6e7c8, prop: 0xffb6d9, fog: 0x12261f, banner: 0x5fa98a },
    propKind: 'teacup', loud: false,
    bubbleBias: { treat: 1.4, golden: 0.8, lucky: 1.2, flash: 0.5, subliminal: 0.5, video: 0 },
    ambient: { kind: 'petals', colors: [[255, 182, 217], [191, 235, 216], [246, 231, 200]] } },
  { id: 'toybox', name: 'The Toybox', tagline: 'the floor bounces. so do you', biome: 'toybox',
    colors: { road: 0x33307f, edge: 0xffd23f, prop: 0xffd23f, fog: 0x14103a, banner: 0x6c63d8 },
    propKind: 'block', loud: true, propAlt: [0xff4d6d, 0x3a86ff],
    bubbleBias: { treat: 1.2, prism: 1.5, flash: 1.3, glitch: 0.6 },
    ambient: { kind: 'confetti', colors: [[255, 77, 109], [255, 210, 63], [58, 134, 255]] } },
  { id: 'casino', name: "The Fool's Casino", tagline: 'the wheel always pays. eventually', biome: 'casino',
    colors: { road: 0x5c1128, edge: 0xf2c14e, prop: 0xf2c14e, fog: 0x0b0508, banner: 0xa3122e },
    propKind: 'chip', loud: true, propAlt: [0xa3122e],
    bubbleBias: { golden: 2.0, lucky: 2.0, glitch: 1.3, treat: 0.9 },
    ambient: { kind: 'coins', colors: [[242, 193, 78], [255, 240, 160]] } },
  { id: 'undertow', name: 'The Undertow', tagline: 'the lane drifts. let it', biome: 'undertow',
    colors: { road: 0x15446c, edge: 0x7fe7f0, prop: 0x1fa9b5, fog: 0x06202a, banner: 0x2a8fa8 },
    propKind: 'kelp', loud: false,
    bubbleBias: { treat: 1.0, spiral: 1.4, braindrain: 1.3, freeze: 1.2 },
    ambient: { kind: 'bubbles', colors: [[127, 231, 240], [159, 200, 255]] } },
  { id: 'mirrors', name: 'The Hall of Mirrors', tagline: 'the picture flips. your hand does not', biome: 'mirrors',
    colors: { road: 0x44454f, edge: 0x5be7d8, prop: 0xdde3f0, fog: 0x1a1e2c, banner: 0x9aa3c8 },
    propKind: 'mirror', loud: false,
    bubbleBias: { prism: 1.6, glitch: 1.5, spiral: 1.2, flash: 1.2 },
    ambient: { kind: 'glints', colors: [[221, 227, 240], [91, 231, 216]] } },
  { id: 'chapel', name: 'The Pink Chapel', tagline: 'the spiral pins itself here', biome: 'chapel',
    colors: { road: 0x6c1c4c, edge: 0xf2c14e, prop: 0xffffff, fog: 0x2a0820, banner: 0xe23c9c },
    propKind: 'candle', loud: true,
    bubbleBias: { subliminal: 1.8, spiral: 1.5, pink: 1.4 },
    ambient: { kind: 'motes', colors: [[255, 214, 150], [255, 105, 180]] } },
  { id: 'greyward', name: 'The Grey Ward', tagline: 'the only pink left is the treats', biome: 'greyward',
    colors: { road: 0x555a5e, edge: 0x9aa0a6, prop: 0x9aa0a6, fog: 0x2b2b33, banner: 0xff69b4 },
    propKind: 'cot', loud: false,
    bubbleBias: { treat: 0.8, pink: 1.6, braindrain: 1.4, freeze: 1.2 },
    ambient: { kind: 'ash', colors: [[154, 160, 166], [221, 227, 240]] } },
  { id: 'coronation', name: 'The Coronation', tagline: 'the run remembers. so will you', biome: 'coronation',
    colors: { road: 0x661838, edge: 0xf2c14e, prop: 0xf2c14e, fog: 0x3a0716, banner: 0x7a0f2b },
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

/** A road-wide wedge rising to `hgt` at the lip (local +z = forward), uv'd for the tile texture. */
function wedgeGeometry(halfW, len, hgt) {
  const a = [-halfW, 0, -len], b = [halfW, 0, -len], c = [halfW, hgt, 0], d = [-halfW, hgt, 0];
  const c0 = [halfW, 0, 0], d0 = [-halfW, 0, 0];
  const tri = (...v) => v.flat();
  const pos = new Float32Array([
    ...tri(a, b, c), ...tri(a, c, d),          // the slope
    ...tri(d, c, c0), ...tri(d, c0, d0),      // the lip face
    ...tri(b, c0, c), ...tri(a, d, d0),        // the sides
  ]);
  const uv = new Float32Array(pos.length / 3 * 2);
  for (let i = 0; i < pos.length / 3; i++) { uv[i * 2] = (pos[i * 3] / (halfW * 2)) + 0.5; uv[i * 2 + 1] = pos[i * 3 + 2] / 2; }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  g.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
  g.computeVertexNormals();
  return g;
}

// ---- the road tile sheet ---------------------------------------------------------
// 16 texels per metre. One sheet spans the whole road profile across (kerb, face, road,
// face, kerb = 7 m = 112 px) and 2 m along (32 px), repeating with depth. The ALPHA channel
// is a tint mask, not transparency: 0.5 = multiply by the vertex colour (room tint),
// 1.0 = keep the texel's own colour (white chequers, the cream centre dash).
const TEXEL = 16, ROAD_W_PX = 112, ROAD_L_PX = 32, KERB_PX = 10, KERB_H = 0.16;
const KERB_OUT = ROAD_W_PX / TEXEL / 2;            // 3.5 m: outer edge of the kerb top
const KERB_IN = KERB_OUT - KERB_PX / TEXEL;        // 2.875 m: the kerb face
function roadSheet(rng) {
  return pixelTex(ROAD_W_PX, ROAD_L_PX, (c, w, h) => {
    // every put() clears first: stacked half-alpha fills would composite to 0.75 and lose the mask
    const put = (x, y, pw, ph, l, a = 0.5) => { c.clearRect(x, y, pw, ph); c.fillStyle = `rgba(${l},${l},${l},${a})`; c.fillRect(x, y, pw, ph); };
    put(0, 0, w, h, 215);
    for (let y = 0; y < h; y += TEXEL) for (let x = w / 2 - TEXEL * 3; x < w; x += TEXEL) {   // 1 m tiles, two tones
      put(x, y, TEXEL, TEXEL, (((x / TEXEL) + (y / TEXEL)) & 1) ? 232 : 208);
    }
    for (let x = w / 2 - TEXEL * 3; x < w; x += TEXEL) put(x, 0, 1, h, 150);                  // 1 px grout grid
    for (let y = 0; y < h; y += TEXEL) put(0, y, w, 1, 150);
    for (let i = 0; i < 26; i++) {                                                            // worn specks
      put(KERB_PX + 2 + Math.floor(rng() * (w - KERB_PX * 2 - 6)), Math.floor(rng() * h), 2, 2, rng() < 0.5 ? 175 : 245);
    }
    for (let y = 0; y < h; y += 8) {                                                          // kerb chequers, 0.5 m
      const lit = (y / 8) & 1;
      put(0, y, KERB_PX, 8, 255, lit ? 1 : 0.5); put(w - KERB_PX, y, KERB_PX, 8, 255, lit ? 1 : 0.5);
      put(KERB_PX, y, 1, 8, lit ? 70 : 255); put(w - KERB_PX - 1, y, 1, 8, lit ? 70 : 255);    // the kerb face column
    }
    c.clearRect(w / 2 - 2, 0, 4, TEXEL); c.fillStyle = 'rgba(246,231,200,1)'; c.fillRect(w / 2 - 2, 0, 4, TEXEL);   // centre dash 1 m on, 1 m off
  });
}
/** MeshBasicMaterial whose vertex colour tints only the texels flagged by the sheet's alpha. */
function tintMaskMaterial(map, extra = {}) {
  const m = new THREE.MeshBasicMaterial({ map, vertexColors: true, side: THREE.DoubleSide, ...extra });
  m.onBeforeCompile = (sh) => {
    sh.fragmentShader = sh.fragmentShader
      .replace('#include <map_fragment>', 'vec4 tx = texture2D(map, vMapUv); diffuseColor.rgb *= tx.rgb * mix(vColor, vec3(1.0), step(0.75, tx.a));')
      .replace('#include <color_fragment>', '');
  };
  return m;
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
  const _p = new THREE.Vector3(), _m = new THREE.Matrix4(), _r = new THREE.Matrix4();
  const _s = new THREE.Vector3();
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

  // ---- road ribbon: kerb top, kerb face, road, face, kerb; one draw call ---------------
  // Columns are (x, h, u, colourKey). Zero-width columns split the profile so the vertical
  // kerb faces get their own texel column and a hard colour edge.
  const COLS = [
    [-KERB_OUT, KERB_H, 0, 'edge'], [-KERB_IN, KERB_H, KERB_PX / ROAD_W_PX, 'edge'],
    [-KERB_IN, KERB_H, (KERB_PX + 0.5) / ROAD_W_PX, 'edge'], [-KERB_IN, 0, (KERB_PX + 0.5) / ROAD_W_PX, 'edge'],
    [-KERB_IN, 0, (KERB_PX + 1) / ROAD_W_PX, 'road'], [KERB_IN, 0, 1 - (KERB_PX + 1) / ROAD_W_PX, 'road'],
    [KERB_IN, 0, 1 - (KERB_PX + 0.5) / ROAD_W_PX, 'edge'], [KERB_IN, KERB_H, 1 - (KERB_PX + 0.5) / ROAD_W_PX, 'edge'],
    [KERB_IN, KERB_H, 1 - KERB_PX / ROAD_W_PX, 'edge'], [KERB_OUT, KERB_H, 1, 'edge'],
  ];
  const QUADS = [[0, 1], [2, 3], [4, 5], [6, 7], [8, 9]];
  const road = (() => {
    const STEP = 1.0, n = Math.ceil(total / STEP), C = COLS.length;
    const pos = new Float32Array((n + 1) * C * 3), col = new Float32Array((n + 1) * C * 3), uv = new Float32Array((n + 1) * C * 2);
    const idx = [];
    for (let i = 0; i <= n; i++) {
      const d = Math.min(i * STEP, total);
      roomColorAt(d, 'road', _c); roomColorAt(d, 'edge', _c2);
      for (let k = 0; k < C; k++) {
        const [x, h, u, key] = COLS[k], o = i * C + k;
        layout.toWorld(d, x, h, _p).toArray(pos, o * 3);
        (key === 'road' ? _c : _c2).toArray(col, o * 3);
        uv[o * 2] = u; uv[o * 2 + 1] = d / (ROAD_L_PX / TEXEL);
      }
      if (i < n) for (const [a, b] of QUADS) {
        const p0 = i * C + a, p1 = i * C + b, p2 = p0 + C, p3 = p1 + C;
        idx.push(p0, p1, p2, p1, p3, p2);
      }
    }
    const g = track(new THREE.BufferGeometry());
    g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    g.setAttribute('color', new THREE.BufferAttribute(col, 3));
    g.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
    g.setIndex(idx);
    const sheet = roadSheet(makeRng(0x0ad));
    if (sheet) texes.push(sheet);
    const mesh = new THREE.Mesh(g, mat(sheet ? tintMaskMaterial(sheet) : new THREE.MeshBasicMaterial({ vertexColors: true, side: THREE.DoubleSide })));
    mesh.name = 'race-road'; mesh.frustumCulled = false; group.add(mesh);
    return mesh;
  })();

  // ---- ramps: tiled wedge + pink lip + gold air cubes; boost pads; sugar cubes ---------
  const feats = layout.chunks.flatMap((c) => c.features);
  const ramps = feats.filter((f) => f.type === 'ramp');
  const pads = feats.filter((f) => f.type === 'boost');
  const cubes = feats.filter((f) => f.type === 'itembox');
  const wedgeTex = pixelTex(32, 32, (c, w, h) => {
    c.fillStyle = '#d8d8d8'; c.fillRect(0, 0, w, h);
    for (let y = 0; y < h; y += 8) for (let x = 0; x < w; x += 8) { c.fillStyle = (((x + y) / 8) & 1) ? '#e8e8e8' : '#cfcfcf'; c.fillRect(x, y, 8, 8); }
    c.fillStyle = '#8a8a8a'; for (let x = 0; x < w; x += 8) c.fillRect(x, 0, 1, h); for (let y = 0; y < h; y += 8) c.fillRect(0, y, w, 1);
  });
  if (wedgeTex) texes.push(wedgeTex);
  const wedgeMat = mat(new THREE.MeshLambertMaterial({ map: wedgeTex, color: wedgeTex ? 0xffffff : 0xf6e7c8 }));
  const pinkGlow = mat(new THREE.MeshLambertMaterial({ color: 0xff69b4, emissive: 0xff69b4, emissiveIntensity: 0.9 }));
  const gold = mat(new THREE.MeshLambertMaterial({ color: 0xf2c14e, emissive: 0xf2c14e, emissiveIntensity: 0.6 }));
  // the air line is marked by PAIRS of gold cubes either side of the arc. A dot hides while it would
  // sit between the camera seat and the cup (from DOT_BEHIND to DOT_NEAR of the kart) and fades back
  // in over DOT_NEAR..DOT_FAR ahead, so the low chase camera never has gold in its face mid-jump.
  const AIR_DOTS = 10, AIR_X = 2.4;
  const DOT_NEAR = 6, DOT_FAR = 11, DOT_BEHIND = -(CAM_BACK + 2.5);
  const airBase = [], airD = [];   // per dot: its resting matrix and wrapped depth
  const wedges = new THREE.InstancedMesh(track(wedgeGeometry(KERB_OUT, 4, 0.8)), wedgeMat, Math.max(1, ramps.length));
  const lips = new THREE.InstancedMesh(track(new THREE.BoxGeometry(KERB_OUT * 2 + 0.1, 0.2, 0.3)), pinkGlow, Math.max(1, ramps.length));
  const airDots = new THREE.InstancedMesh(track(new THREE.BoxGeometry(0.3, 0.3, 0.3)), gold, Math.max(1, ramps.length * AIR_DOTS));
  ramps.forEach((r, i) => {
    wedges.setMatrixAt(i, roadMatrix(r.d, 0, 0.01, 0, 1, _m));
    wedges.setColorAt(i, roomColorAt(r.d, 'road', _c).lerp(_c2.set(0xffffff), 0.45));
    lips.setMatrixAt(i, roadMatrix(r.d, 0, 0.85, 0, 1, _m));
    for (let k = 0; k < AIR_DOTS; k++) {
      const prog = (Math.floor(k / 2) + 1) / (AIR_DOTS / 2 + 1), side = k & 1 ? AIR_X : -AIR_X;
      const dd = r.d + prog * r.airLen;
      airDots.setMatrixAt(i * AIR_DOTS + k, roadMatrix(dd, side, 0.5 + r.height * Math.sin(Math.PI * prog), prog * 2, 1, _m));
      airBase.push(_m.clone()); airD.push(layout.wrap(dd));
    }
  });
  wedges.count = ramps.length; lips.count = ramps.length; airDots.count = ramps.length * AIR_DOTS;
  const airK = new Float32Array(airBase.length).fill(1);   // each dot's current scale (1 = resting)
  // boost pad: full lane width, 3 m long, chevrons that run forward (texture offset), cyan glow
  const PAD_W = 2.4, PAD_L = 3.0;
  const padTex = pixelTex(24, 30, (c, w, h) => {
    c.fillStyle = '#0b2a30'; c.fillRect(0, 0, w, h);
    c.fillStyle = '#5be7d8';
    for (let y0 = -10; y0 < h + 10; y0 += 10) {         // chevrons pointing to canvas-bottom (= forward)
      for (let x = 0; x < w / 2; x++) { const y = y0 + Math.floor(x * 0.55); c.fillRect(x, y, 1, 3); c.fillRect(w - 1 - x, y, 1, 3); }
    }
    c.fillStyle = '#9ff7ee'; c.fillRect(0, 0, 1, h); c.fillRect(w - 1, 0, 1, h);
  });
  if (padTex) texes.push(padTex);
  const padMat = mat(new THREE.MeshBasicMaterial({ map: padTex, color: padTex ? 0xffffff : 0x5be7d8 }));
  const padGeo = track(new THREE.PlaneGeometry(PAD_W, PAD_L).rotateX(-Math.PI / 2));
  const padMesh = new THREE.InstancedMesh(padGeo, padMat, Math.max(1, pads.length));
  pads.forEach((p, i) => padMesh.setMatrixAt(i, roadMatrix(p.d, p.x, 0.03, 0, 1, _m)));
  padMesh.count = pads.length;
  // sugar cube: 1.2 m, pixel "?" on every face, bobbing + turning, gold glow
  const cubeTex = pixelTex(16, 16, (c, w, h) => {
    c.fillStyle = '#fff6ea'; c.fillRect(0, 0, w, h);
    c.fillStyle = '#e9d9c4'; c.fillRect(0, 0, w, 1); c.fillRect(0, 0, 1, h); c.fillRect(0, h - 1, w, 1); c.fillRect(w - 1, 0, 1, h);
    c.fillStyle = '#e23c9c';
    const Q = ['.####.', '##..##', '....##', '...##.', '..##..', '......', '..##..', '..##..'];
    Q.forEach((row, y) => [...row].forEach((ch, x) => { if (ch === '#') c.fillRect(5 + x, 3 + y, 1, 1); }));
  });
  if (cubeTex) texes.push(cubeTex);
  const cubeMat = mat(new THREE.MeshLambertMaterial({ map: cubeTex, color: 0xffffff, emissive: 0xf2c14e, emissiveIntensity: 0.25 }));
  const CUBE = 1.2;
  const cubeMesh = new THREE.InstancedMesh(track(new THREE.BoxGeometry(CUBE, CUBE, CUBE)), cubeMat, Math.max(1, cubes.length));
  cubes.forEach((c, i) => cubeMesh.setMatrixAt(i, roadMatrix(c.d, c.x, CUBE * 0.75, 0, 1, _m)));
  cubeMesh.count = cubes.length;
  // a crossed cube BREAKS: it hides, throws SHARDS_PER pieces of itself (pooled, world space,
  // gravity along the local up) with a white flash on its spot, and grows back after RESPAWN_SEC.
  const cubeIndex = new Map(cubes.map((c, i) => [c, i]));
  const cubeBrokenAt = new Float64Array(Math.max(1, cubes.length)).fill(-1);   // -1 = intact
  const RESPAWN_SEC = 8, REGROW_SEC = 0.5, SHARD_TTL = 0.65, SHARDS_PER = 8, SHARD_N = 48, FLASH_N = 4, FLASH_SEC = 0.22;
  const shards = new THREE.InstancedMesh(track(new THREE.BoxGeometry(CUBE * 0.24, CUBE * 0.24, CUBE * 0.24)), cubeMat, SHARD_N);
  const shard = []; for (let i = 0; i < SHARD_N; i++) shard.push({ life: 0, age: 0, spin: 0, p: new THREE.Vector3(), v: new THREE.Vector3(), g: new THREE.Vector3(), axis: new THREE.Vector3(0, 1, 0) });
  let shardCursor = 0, shardsLive = 0;
  const flashMat = mat(new THREE.MeshBasicMaterial({ color: 0xffffff }));
  const flashes = new THREE.InstancedMesh(track(new THREE.BoxGeometry(CUBE, CUBE, CUBE)), flashMat, FLASH_N);
  const flash = []; for (let i = 0; i < FLASH_N; i++) flash.push({ life: 0, m: new THREE.Matrix4() });
  let flashCursor = 0, flashesLive = 0;
  const _q = new THREE.Quaternion(), _up = new THREE.Vector3(), _hide = new THREE.Vector3(0, -999, 0);
  const hideAll = (mesh, n) => { for (let i = 0; i < n; i++) mesh.setMatrixAt(i, _m.compose(_hide, _q.identity(), _s.setScalar(0.0001))); };
  hideAll(shards, SHARD_N); hideAll(flashes, FLASH_N);
  let lastT = -1;
  /** Break the cube for feature f (or its index). Returns true when it was intact, false when
   *  it was already broken (the run brain hands out an item only on true). */
  function breakItemBox(f) {
    const i = typeof f === 'number' ? f : cubeIndex.get(f);
    if (i == null || cubeBrokenAt[i] >= 0) return false;
    const c = cubes[i];
    cubeBrokenAt[i] = now() - t0;
    cubeMesh.setMatrixAt(i, roadMatrix(c.d, c.x, CUBE * 0.75, 0, 0.0001, _m)); cubeMesh.instanceMatrix.needsUpdate = true;
    const fr = layout.frameAtDepth(c.d); _up.copy(fr.up);
    layout.toWorld(c.d, c.x, CUBE * 0.75, _p);
    for (let k = 0; k < SHARDS_PER; k++) {
      const s = shard[shardCursor]; shardCursor = (shardCursor + 1) % SHARD_N;
      if (s.life <= 0) shardsLive++;
      s.life = SHARD_TTL; s.age = 0;
      s.p.copy(_p).addScaledVector(fr.right, (rng() - 0.5) * CUBE * 0.6).addScaledVector(_up, (rng() - 0.5) * CUBE * 0.6).addScaledVector(fr.tangent, (rng() - 0.5) * CUBE * 0.6);
      s.v.copy(fr.right).multiplyScalar((rng() - 0.5) * 7).addScaledVector(_up, 2.5 + rng() * 4).addScaledVector(fr.tangent, 1 + rng() * 5);
      s.g.copy(_up).multiplyScalar(-14);
      s.axis.set(rng() - 0.5, rng() - 0.5, rng() - 0.5).normalize(); s.spin = (rng() - 0.5) * 24;
    }
    const fl = flash[flashCursor]; flashCursor = (flashCursor + 1) % FLASH_N;
    if (fl.life <= 0) flashesLive++;
    fl.life = FLASH_SEC; roadMatrix(c.d, c.x, CUBE * 0.75, 0, 1, fl.m);
    return true;
  }
  function updateBreaks(t) {
    const dt = lastT < 0 ? 0 : Math.min(0.05, t - lastT); lastT = t;
    if (shardsLive > 0) {                   // shards fly, spin and shrink out
      let live = 0;
      for (let i = 0; i < SHARD_N; i++) {
        const s = shard[i];
        if (s.life <= 0) continue;
        s.life -= dt; s.age += dt;
        if (s.life <= 0) { shards.setMatrixAt(i, _m.compose(_hide, _q.identity(), _s.setScalar(0.0001))); continue; }
        live++;
        s.v.addScaledVector(s.g, dt); s.p.addScaledVector(s.v, dt);
        shards.setMatrixAt(i, _m.compose(s.p, _q.setFromAxisAngle(s.axis, s.spin * s.age), _s.setScalar(0.4 + 0.6 * (s.life / SHARD_TTL))));
      }
      shardsLive = live; shards.instanceMatrix.needsUpdate = true;
    }
    if (flashesLive > 0) {                  // the flash: a white cube on the spot that swells and vanishes
      let live = 0;
      for (let i = 0; i < FLASH_N; i++) {
        const fl = flash[i];
        if (fl.life <= 0) continue;
        fl.life -= dt;
        if (fl.life <= 0) { flashes.setMatrixAt(i, _m.compose(_hide, _q.identity(), _s.setScalar(0.0001))); continue; }
        live++;
        flashes.setMatrixAt(i, _m.copy(fl.m).scale(_s.setScalar(1.15 + 0.9 * (1 - fl.life / FLASH_SEC))));
      }
      flashesLive = live; flashes.instanceMatrix.needsUpdate = true;
    }
  }
  for (const m of [wedges, lips, airDots, padMesh, cubeMesh, shards, flashes]) { m.frustumCulled = false; m.instanceMatrix.needsUpdate = true; group.add(m); }
  if (wedges.instanceColor) wedges.instanceColor.needsUpdate = true;

  // ---- diegetic props: grounded, voxel, animated near the kart (race/roomProps.js) ------
  const props = createRoomProps({ scene, group, layout, spans, specOf, rng });

  // ---- light so the Lambert props read (the tunnel shader ignores lights) ---------
  const hemi = new THREE.HemisphereLight(0xffd6ee, 0x1a1a2e, 1.1);
  const sun = new THREE.DirectionalLight(0xf6e7c8, 0.6); sun.position.set(0.3, 1, 0.2);
  group.add(hemi, sun);
  scene.add(group);

  // ---- runtime ----------------------------------------------------------------------
  const ANIM = 90;           // metres: furniture within this range animates
  const now = () => (typeof performance !== 'undefined' ? performance.now() : Date.now()) / 1000;
  const t0 = now();
  function update(d) {
    const t = now() - t0;
    props.update(d, t);
    let dirty = false;
    for (let i = 0; i < cubes.length; i++) {
      const c = cubes[i], since = cubeBrokenAt[i] >= 0 ? t - cubeBrokenAt[i] : -1;
      if (since >= 0 && since < RESPAWN_SEC) continue;                       // hidden, matrix already written
      let scale = 1;
      if (since >= 0) {                                                       // growing back
        const u = Math.min(1, (since - RESPAWN_SEC) / REGROW_SEC);
        scale = u < 1 ? Math.max(0.0001, 1.12 * Math.sin(u * Math.PI * 0.5)) : 1;
        if (u >= 1) cubeBrokenAt[i] = -1;
      } else if (wrapDist(c.d, d) > ANIM) continue;
      cubeMesh.setMatrixAt(i, roadMatrix(c.d, c.x, CUBE * 0.75 + 0.18 * Math.sin(t * 2 + i), t * 1.1 + i, scale, _m));
      dirty = true;
    }
    if (dirty) cubeMesh.instanceMatrix.needsUpdate = true;
    updateBreaks(t);
    // air-line dots: gone while they would sit between the seat and the cup, back over DOT_NEAR..DOT_FAR
    let adirty = false;
    for (let i = 0; i < airD.length; i++) {
      const rel = layout.wrap(airD[i] - d + total / 2) - total / 2;   // signed, kart-relative metres
      let k = 1;
      if (rel > DOT_BEHIND && rel < DOT_FAR) k = rel < DOT_NEAR ? 0 : (rel - DOT_NEAR) / (DOT_FAR - DOT_NEAR);
      if (k === airK[i]) continue;
      airK[i] = k;
      airDots.setMatrixAt(i, _m.copy(airBase[i]).scale(_s.setScalar(Math.max(k, 0.0001))));
      adirty = true;
    }
    if (adirty) airDots.instanceMatrix.needsUpdate = true;
    cubeMat.emissiveIntensity = 0.25 + 0.2 * (0.5 + 0.5 * Math.sin(t * 3));
    if (padTex) padTex.offset.y = (t * 1.6) % 1;          // the chevrons run forward
    padMat.color.setScalar(0.85 + 0.15 * Math.sin(t * 6));
    pinkGlow.emissiveIntensity = 0.7 + 0.3 * Math.sin(t * 4);
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
    props.dispose();
    for (const m of [wedges, lips, airDots, padMesh, cubeMesh, shards, flashes]) m.dispose();
  }

  return { update, applyRoom, breakItemBox, dispose, group, spans, rooms: specs };
}
