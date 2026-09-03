/* ============================================================================
 * race/walls.js - The Caucus Race wall media: the player's own images plastered on
 * the upper tube wall, exactly like the descent's wall posters (engine/wallPosters.js
 * is reused as-is), plus painted room signage so the wall is never bare when the
 * library is empty.
 *
 * Adapter over createWallPosters, which was written for a one-way fall:
 *   depth   - it wants a monotonically increasing camDepth; the race laps a closed
 *             loop, so update() feeds it an unwrapped odometer (layout.frameAtDepth
 *             wraps internally, so unwrapped depths land on the right spot).
 *   angle   - it throws posters anywhere around the tube (ceiling to floor). A kart
 *             race has a road down there, so every freshly placed slot is re-aimed
 *             into the upper-wall band (18..73 deg either side of the ceiling: never
 *             over the road, never straight overhead) and re-oriented with the same
 *             maths as its own orient().
 *   density - per room through setRegion: loud rooms plaster the wall (region III),
 *             soft rooms scatter (region II), the Tea Garden keeps a bare wall.
 *
 * Signage: one merged quad mesh per room, textured from a single pixel canvas atlas
 * of room words in the room's colours. A sparse "base" set is always up (diegetic
 * placards); the "fill" set only draws while media.hasUserMedia() is false, via
 * geometry drawRange, so an empty library still gets a dressed wall. 8 meshes, only
 * the rooms in view visible (2..3 draw calls at a time).
 *
 * createWalls({ scene, layout, media, renderer, camera, rng }) -> { update(d, dt), setRoom(id), dispose }
 * ==========================================================================*/

import * as THREE from 'three';
import { createWallPosters } from '../engine/wallPosters.js';
import { RADIUS } from './consts.js';
import { ROOMS, roomById } from './rooms.js';
import { pixelTex } from './roomProps.js';

const BAND_LO = 0.32, BAND_HI = 1.27;   // radians from the ceiling: the upper-wall poster band
const SIGN_INSET = 0.16;                // signs sit just inside the wall, like the posters
const SIGN_W = 3.2, SIGN_H = 2.4;
const VIEW = 170;                       // metres: rooms farther than this hide their signs
const CELL_W = 64, CELL_H = 48, ATLAS_COLS = 4;

// Two placards per room. Short, printed, in-world; the words a wall would carry.
const WORDS = {
  teagarden: ['drink me', 'eat me'], toybox: ['play', 'bounce'], casino: ['PAYS', 'JACKPOT'],
  undertow: ['deeper', 'breathe'], mirrors: ['look', 'again'], chapel: ['kneel', 'amen'],
  greyward: ['quiet', 'ward 9'], coronation: ['crown', 'all hail'],
};
// signage spacing (metres) for the always-on base set and the empty-library fill set
const SIGN_GAP = { loud: [40, 9], soft: [60, 15], teagarden: [70, 36] };

function hex(c) { return '#' + c.toString(16).padStart(6, '0'); }

/** The placard atlas: a 4x4 grid of 64x48 cells, two per room, drawn once. */
function signAtlas() {
  const rows = Math.ceil(ROOMS.length * 2 / ATLAS_COLS);
  return pixelTex(CELL_W * ATLAS_COLS, CELL_H * rows, (c) => {
    c.imageSmoothingEnabled = false;
    ROOMS.forEach((r, ri) => (WORDS[r.id] || ['', '']).forEach((word, wi) => {
      const i = ri * 2 + wi, x = (i % ATLAS_COLS) * CELL_W, y = Math.floor(i / ATLAS_COLS) * CELL_H;
      c.fillStyle = hex(r.colors.edge); c.fillRect(x, y, CELL_W, CELL_H);            // frame
      c.fillStyle = hex(r.colors.banner); c.fillRect(x + 3, y + 3, CELL_W - 6, CELL_H - 6);
      c.fillStyle = hex(r.colors.fog); c.fillRect(x + 3, y + CELL_H - 9, CELL_W - 6, 6);   // a shadow band at the foot
      const size = word.length > 6 ? 13 : 17;
      c.font = `bold ${size}px "Courier New", monospace`; c.textAlign = 'center'; c.textBaseline = 'middle';
      c.fillStyle = hex(r.colors.fog); c.fillText(word, x + CELL_W / 2 + 1, y + CELL_H / 2 - 1);  // drop shadow
      c.fillStyle = '#ffffff'; c.fillText(word, x + CELL_W / 2, y + CELL_H / 2 - 2);
    }));
  }, { clamp: true });
}

export function createWalls({ scene, layout, media, renderer, camera, rng }) {
  const total = layout.totalDepth;
  const wrapDist = (a, b) => { const w = layout.wrap(a - b); return Math.min(w, total - w); };
  const _c = new THREE.Vector3(), _in = new THREE.Vector3(), _rt = new THREE.Vector3(), _up = new THREE.Vector3();
  const _m = new THREE.Matrix4();

  /** Wall point + axes at (depth, angle from ceiling): the poster's own placement maths. */
  function wallFrame(depth, angle, roll) {
    const fr = layout.frameAtDepth(depth);
    const ca = Math.cos(angle), sa = Math.sin(angle), R = RADIUS - SIGN_INSET;
    _c.copy(fr.pos).addScaledVector(fr.normal, ca * R).addScaledVector(fr.binormal, sa * R);
    _in.copy(fr.normal).multiplyScalar(-ca).addScaledVector(fr.binormal, -sa).normalize();
    _rt.copy(fr.tangent).normalize();
    _up.crossVectors(_in, _rt).normalize();
    _rt.crossVectors(_up, _in).normalize();
    if (_up.dot(fr.normal) < 0) { _up.negate(); _rt.negate(); }   // upright: words and faces read from the road
    if (roll) { _rt.applyAxisAngle(_in, roll); _up.applyAxisAngle(_in, roll); }
  }

  // ---- the posters (engine/wallPosters.js, adapted) --------------------------------------
  const posters = createWallPosters({ scene, layout, media, renderer, camera });
  const seen = new Map();          // slot -> depth we last aimed it at
  let odometer = 0, lastW = null;  // unwrapped camera depth for the one-way poster logic
  function aimNewSlots() {
    for (const mesh of posters.getPickables()) {
      const slot = mesh.userData && mesh.userData.slot;
      if (!slot || seen.get(slot) === slot.depth) continue;
      seen.set(slot, slot.depth);
      const side = rng() < 0.5 ? -1 : 1;
      slot.angle = side * (BAND_LO + rng() * (BAND_HI - BAND_LO));
      slot.roll = (rng() - 0.5) * 0.3;
      wallFrame(slot.depth, slot.angle, 0);
      mesh.position.copy(_c).addScaledVector(_in, RADIUS - SIGN_INSET - slot.wallR); // keep the slot's own wall radius
      mesh.quaternion.setFromRotationMatrix(_m.makeBasis(_rt, _up, _in));
      mesh.rotateZ(slot.roll);
    }
  }

  // ---- the signage ----------------------------------------------------------------------
  const atlas = signAtlas();
  const signMat = new THREE.MeshBasicMaterial({ map: atlas, color: atlas ? 0xffffff : 0x8a5a8a, side: THREE.DoubleSide, toneMapped: false });
  const signs = [];   // { mesh, d0, d1, baseCount }
  const rows = Math.ceil(ROOMS.length * 2 / ATLAS_COLS);   // atlas rows, for the cell uvs
  const chunksOf = (id) => layout.chunks.filter((c) => c.room === id && c.kind !== 'loop');
  const spans = [];
  for (const ch of layout.chunks) {
    if (ch.kind === 'loop') continue;
    const last = spans[spans.length - 1];
    if (last && last.id === ch.room) last.d1 = ch.d1; else spans.push({ id: ch.room, d0: ch.d0, d1: ch.d1 });
  }
  for (const span of spans) {
    const spec = roomById(span.id) || ROOMS[0];
    const gaps = SIGN_GAP[span.id === 'teagarden' ? 'teagarden' : spec.loud ? 'loud' : 'soft'];
    const ri = Math.max(0, ROOMS.indexOf(spec));
    const pos = [], uv = [], idx = [];
    let n = 0, baseCount = 0;
    const put = (d, side, wi) => {
      const angle = side * (BAND_LO + rng() * (BAND_HI - BAND_LO));
      wallFrame(d, angle, (rng() - 0.5) * 0.4);
      const cell = ri * 2 + wi, cx = (cell % ATLAS_COLS) / ATLAS_COLS, cy = 1 - (Math.floor(cell / ATLAS_COLS) + 1) / rows;
      const cw = 1 / ATLAS_COLS, chh = 1 / rows;
      const corner = (sx, sy) => { const p = _c.clone().addScaledVector(_rt, sx * SIGN_W / 2).addScaledVector(_up, sy * SIGN_H / 2); pos.push(p.x, p.y, p.z); };
      corner(-1, -1); uv.push(cx, cy); corner(1, -1); uv.push(cx + cw, cy);
      corner(1, 1); uv.push(cx + cw, cy + chh); corner(-1, 1); uv.push(cx, cy + chh);
      idx.push(n, n + 1, n + 2, n, n + 2, n + 3); n += 4;
    };
    const lay = (gap, margin) => {
      let side = rng() < 0.5 ? 1 : -1, wi = 0;
      for (const ch of chunksOf(span.id)) {
        for (let d = ch.d0 + margin + rng() * gap * 0.5; d < ch.d1 - margin; d += gap * (0.75 + rng() * 0.5)) { put(d, side, wi); side = -side; wi ^= 1; }
      }
    };
    lay(gaps[0], 6); baseCount = idx.length;
    lay(gaps[1], 4);
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
    g.setIndex(idx);
    const mesh = new THREE.Mesh(g, signMat);
    mesh.frustumCulled = false; mesh.renderOrder = -1; mesh.name = `race-signs-${span.id}`;
    scene.add(mesh);
    signs.push({ mesh, d0: span.d0, d1: span.d1, baseCount, all: idx.length });
  }

  // ---- density per room -------------------------------------------------------------------
  let room = null;
  function setRoom(id) {
    if (id === room) return;
    room = id;
    const spec = roomById(id);
    posters.setRegion(!spec || id === 'teagarden' ? 1 : spec.loud ? 3 : 2);
  }

  // ---- runtime ----------------------------------------------------------------------------
  function update(d, dt) {
    const w = layout.wrap(d);
    if (lastW == null) lastW = w;
    let delta = w - lastW;
    if (delta > total / 2) delta -= total; else if (delta < -total / 2) delta += total;
    odometer += delta; lastW = w;
    setRoom(layout.roomAtDepth(w));
    posters.update(camera, odometer, dt);
    aimNewSlots();
    const fill = !(media && typeof media.hasUserMedia === 'function' && media.hasUserMedia());
    for (const s of signs) {
      s.mesh.visible = wrapDist(w, s.d0) < VIEW || wrapDist(w, s.d1) < VIEW || (w >= s.d0 && w <= s.d1);
      s.mesh.geometry.setDrawRange(0, fill ? s.all : s.baseCount);
    }
  }
  function dispose() {
    posters.dispose();
    for (const s of signs) { scene.remove(s.mesh); s.mesh.geometry.dispose(); }
    signMat.dispose(); if (atlas) atlas.dispose();
    signs.length = 0; seen.clear();
  }
  return { update, setRoom, dispose, posters };
}
