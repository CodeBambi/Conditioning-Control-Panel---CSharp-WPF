/* ============================================================================
 * scene.js - the slot cabinet in three.js, ported from blender-scripting
 * slot/preview/{app,polish}.js. One WebGL context per createScene(), freed by
 * dispose(). Nodes are looked up by NAME only (nodes.js); framing comes from
 * cam_seat -> cam_target and live bounds, because the glb is still moving.
 *
 * The feel (lane F1) is decided in feel.js and played here: the lever's Law VIII
 * answer, THE THUD per reel, THE SHIVER, THE BREATH, THE MARQUEE heat on
 * marquee_glow, the payout_tray thud, and one cabinet celebration per win tier.
 *
 * Not ported from the preview: the jewellery trims (hard-coded coordinates), the
 * debug face strip and the view toggle. The payline frame IS here, but taken off
 * live bounds (playbook A6), never off the preview's coordinates.
 * ==========================================================================*/

import * as THREE from 'three';
import { createCoinShower } from '../../room/coin-shower.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { REQUIRED, OPTIONAL, FACE_MATERIAL } from './nodes.js';
import { drawSymbol } from './symbols.js';
import { PACE, reelStopMs, reelsMs, respinStopMs, respinMs } from './pace.js';
import { applyPalette } from './palette.js';
import { FEEL, ALMOST as FEEL_ALMOST, ATTRACT, bezier, breath, shiverPx, chaseMs, paylineGlow, paylinePulses, wiggleCells } from './feel.js';

export const FACES = { idle0_0: 3, hearts: 7, spirals: 8, melt: 9, jackpot: 10 };
const RISE_MS = 620, CAMERA_MS = 620, SINK_MS = 340, PAINT_MS = 100, { THUD_MS } = FEEL;
const LENS_DEG = 20;           // vertical field; a narrow screen backs the camera off to keep the width
const FIT_MARGIN = 1.04;        // breathing room around the marquee, reels and lever box
const PULL_MAX = 0.5, PULL_COMMIT = 0.55;
const LEAN = 0.22, BREATH_RAD = 0.03;   // Law VIII lean into a press; THE BREATH's reach at rest
const PALETTES = {
  idle: [0xd90068, 0x006acb, 0xff9c00, 0x6220bd, 0x00a97c], spin: [0xff126e, 0x1567ff, 0xffb000, 0x00ccac],
  pull: [0x7920c4, 0xff197c], freeze: [0x0049a8, 0x00c7f2, 0x74f4ff], win: [0xe37100, 0xffd32b, 0xff4b24],
  big: [0xff4b24, 0xffd32b, 0xff126e, 0xffb000], jackpot: [0xffbd00, 0xffe27a, 0xffa000, 0xfff1b8],
  melt: [0x351369, 0xa32d99, 0x6230a0], free: [0x006ca0, 0x00dda8, 0x8726e5],
  tease: [0x3d1550, 0x6a2178, 0x2a1040, 0x59206b], tease_gold: [0xffbd00, 0x8a5a00, 0xffe27a, 0xa06c00],
};
const PARTY_MS = [0, 700, 900, 1000, 1400];   // per tier: how long the cabinet celebrates (THE BREATH waits it out)
const TEASE_DIM = 0.62;   // A1: the cabinet drops a notch while reel 3 holds (never under Calm or reduced motion)

const clamp = x => Math.min(1, Math.max(0, x)), ease = x => 1 - (1 - clamp(x)) ** 3;
const TAU = Math.PI * 2, ATTRACT_SPEED = [1, 0.86, 1.13];   // A4: the three drums drift at slightly different speeds
const wrapRad = a => { const x = ((a % TAU) + TAU) % TAU; return x > Math.PI ? x - TAU : x; };   // ...so home is never half a turn away
/** 0..1 of a reel's travel at `dt`: spin up, blur at a steady speed, decelerate into the stop. */
function reelTravel(dt, dur, up = 180, down = Math.min(PACE.DECEL_MS, dur * 0.5)) {
  const v = 1 / (dur - up / 2 - (2 * down) / 3);
  if (dt <= up) return v * dt * dt / (2 * up);
  return dt <= dur - down ? v * (dt - up / 2) : v * (dur - down - up / 2) + (v * down / 3) * (1 - (1 - clamp((dt - dur + down) / down)) ** 3);
}
const reveal = x => bezier(FEEL.REVEAL_EASE, x), thud = x => bezier(FEEL.THUD_EASE, x);
const asset = p => new URL(p, import.meta.url).href;

function makeCanvas(w, h) { const c = document.createElement('canvas'); c.width = w; c.height = h; return c; }
function canvasTexture(c) {
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace; t.flipY = false; t.generateMipmaps = false;
  t.minFilter = t.magFilter = THREE.LinearFilter;
  return t;
}

function paintDisplay(ctx, w, h, marquee, text) {
  const g = ctx.createLinearGradient(0, 0, w, h);
  g.addColorStop(0, '#180d27'); g.addColorStop(0.5, '#432040'); g.addColorStop(1, '#180d27');
  ctx.fillStyle = g; ctx.fillRect(0, 0, w, h);
  ctx.lineWidth = 1.5; ctx.strokeStyle = '#bf85ac'; ctx.strokeRect(9, 9, w - 18, h - 18);
  ctx.strokeStyle = '#744366'; ctx.strokeRect(15, 15, w - 30, h - 30);
  ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
  ctx.font = `600 ${marquee ? 57 : 44}px Segoe UI, Arial, sans-serif`;
  ctx.shadowColor = '#ff75c8'; ctx.shadowBlur = marquee ? 12 : 4; ctx.fillStyle = '#ffe3f2';
  ctx.fillText(text, w / 2, h / 2, w * 0.8); ctx.shadowBlur = 0;
}

/**
 * @param {{canvas:HTMLCanvasElement, reduced:boolean, hint?:HTMLElement, palette?:object,
 *          canPull:()=>boolean, onLever:()=>void, onFreeze:(i:number)=>void, onReelStop?:(i:number)=>void}} o
 * @returns {Promise<{missing:string[], dispose:()=>void} | object>}
 */
export async function createScene(o) {
  const { canvas, reduced } = o;
  const renderer = new THREE.WebGLRenderer({ canvas, alpha: true, antialias: true, powerPreference: 'high-performance' });
  renderer.setPixelRatio(Math.min(devicePixelRatio, 1.5));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping; renderer.toneMappingExposure = 1.25;
  const scene = new THREE.Scene(), camera = new THREE.PerspectiveCamera(LENS_DEG, 16 / 9, 0.015, 60);
  scene.add(new THREE.HemisphereLight(0xe3bdf9, 0x40213e, 2));
  for (const [color, intensity, pos] of [[0xffd9ed, 3, [-3, 5, 4]], [0xb69cf4, 2, [3, 2, 2]], [0xff83c9, 3, [1, 4, -3]]]) {
    const l = new THREE.DirectionalLight(color, intensity); l.position.set(...pos); scene.add(l);
  }
  const owned = [];            // textures and materials made here, freed in dispose()
  let raf = 0, disposed = false, settle = null;
  function dispose() {
    if (disposed) return;
    disposed = true;
    cancelAnimationFrame(raf);
    if (settle) settle();         // a rise, sink or spin cut short by dispose still resolves its promise
    canvas.removeEventListener('pointerdown', onDown); canvas.removeEventListener('pointermove', onMove);
    canvas.removeEventListener('pointerup', onUp); canvas.removeEventListener('pointercancel', cancelPull);
    scene.traverse(n => {
      if (n.geometry) n.geometry.dispose();
      for (const m of [].concat(n.material || [])) { for (const k of Object.keys(m)) if (m[k] && m[k].isTexture) m[k].dispose(); m.dispose(); }
    });
    owned.forEach(x => x.dispose());
    renderer.dispose(); renderer.forceContextLoss();
  }

  let gltf, atlas = null;
  try {
    [gltf, atlas] = await Promise.all([
      new GLTFLoader().loadAsync(asset('./assets/slot.glb')),
      new THREE.TextureLoader().loadAsync(asset('./assets/emi-faces-slot.png')).catch(() => null),
    ]);
  } catch (e) { dispose(); throw e; }
  const rig = gltf.scene;
  const coinShower = createCoinShower(rig);
  let coinTick = performance.now();
  scene.add(rig);
  const recoloured = applyPalette(rig, o.palette);   // a room variant's cabinet colours, before any other swap
  owned.push(...recoloured);
  const get = name => rig.getObjectByName(name) || null;
  const missing = REQUIRED.filter(n => !get(n));
  if (missing.length) return { missing, dispose };
  const absent = OPTIONAL.filter(n => !get(n));
  if (absent.length) console.warn(`[slot] glb lacks optional nodes, degrading: ${absent.join(', ')}`);

  const cabinet = get('cabinet'), lever = get('lever'), rigRest = rig.position.clone();
  const reels = [1, 2, 3].map(i => get(`reel_${i}`)), restX = reels.map(r => r.rotation.x);
  const freezers = [1, 2, 3].map(i => get(`freeze_${i}`));
  freezers.forEach(f => f && f.material && (f.material = f.material.clone(), f.userData.restY = f.position.y));
  const bulbs = [];
  rig.traverse(n => { if (n.isMesh && /^lights_chase_\d+$/.test(n.name)) bulbs.push(n); });
  bulbs.sort((a, b) => a.name.localeCompare(b.name));
  bulbs.forEach((b, i) => {
    b.material = new THREE.MeshPhysicalMaterial({ color: PALETTES.idle[i % 5], emissive: PALETTES.idle[i % 5],
      emissiveIntensity: 0.1, roughness: 0.24, metalness: 0.08, clearcoat: 1, clearcoatRoughness: 0.14 });
  });
  const glass = get('reel_window');
  if (glass && glass.material) Object.assign(glass.material, { transparent: true, opacity: 0.035, depthWrite: false });
  // THE MARQUEE's heat lives on marquee_glow (its own clone: neon_pink is shared with the piping).
  const glowNode = get('marquee_glow'), glowMat = glowNode && glowNode.isMesh && glowNode.material && glowNode.material.emissive
    ? (glowNode.material = glowNode.material.clone()) : null;
  const glowRest = glowMat && { color: glowMat.emissive.clone(), intensity: glowMat.emissiveIntensity };
  if (glowMat) owned.push(glowMat);
  const tray = get('payout_tray'), trayRest = tray && tray.scale.clone();

  // EMI face: atlas cell with a half-texel inset (FACES.md), Nearest, no mips.
  let faceMesh = null, faceName = 'idle0_0';
  rig.traverse(n => { if (n.isMesh && n.material && n.material.name === FACE_MATERIAL) faceMesh = n; });
  if (atlas && faceMesh) {
    Object.assign(atlas, { flipY: false, colorSpace: THREE.SRGBColorSpace, minFilter: THREE.NearestFilter,
      magFilter: THREE.NearestFilter, generateMipmaps: false, wrapS: THREE.ClampToEdgeWrapping, wrapT: THREE.ClampToEdgeWrapping });
    atlas.repeat.set(151 / 1672, 136 / 137);
    faceMesh.material = new THREE.MeshBasicMaterial({ map: atlas });
  } else if (!faceMesh) console.warn(`[slot] no ${FACE_MATERIAL} material, EMI face stays as modelled`);
  owned.push(...(atlas ? [atlas] : []));

  const screens = {};
  for (const name of ['screen_jackpot', 'screen_status', 'marquee']) {
    const mesh = get(name);
    if (!mesh || !mesh.isMesh) continue;
    const c = makeCanvas(1024, 128), t = canvasTexture(c);
    mesh.material = new THREE.MeshStandardMaterial({ map: t, emissiveMap: t, emissive: 0xffffff, emissiveIntensity: 0.45, roughness: 0.6 });
    owned.push(t);
    screens[name] = { c, t, mesh, text: null };
  }

  // Reels: one canvas per drum, N cells along U, painted in strip order.
  let strips = [[], [], []], look = { reduced }, lastPaint = -Infinity;
  // A2 THE ALMOST: the off-by-one cell on reel 3 going gold, {r, j, at}. Set by almost(), cleared when it ends.
  let ghost = null;
  /** The gold on that cell: in over the tell, then ONE snap back. Reduced motion takes a single tint (Law VI). */
  function ghostAmt(t) {
    if (!ghost) return 0;
    const age = t - ghost.at, rise = FEEL_ALMOST.TELL_MS - FEEL_ALMOST.SNAP_MS;
    if (!(age >= 0) || age >= FEEL_ALMOST.TELL_MS) return 0;
    if (reduced) return age < FEEL_ALMOST.SNAP_MS ? 1 : 0;
    return age < rise ? ease(age / rise) : 1 - clamp((age - rise) / FEEL_ALMOST.SNAP_MS);
  }
  const reelCanvas = [], reelTex = [];
  const angle = (k, n) => ((k + 0.5) / n - 0.5) * Math.PI * 2;
  function paint(t) {
    for (let r = 0; r < 3; r++) {
      const n = strips[r].length || 1, c = reelCanvas[r], ctx = c.getContext('2d');
      ctx.clearRect(0, 0, c.width, c.height);
      for (let j = 0; j < strips[r].length; j++) {
        ctx.save(); ctx.translate((j + 0.5) * 256, 128); ctx.rotate(-Math.PI / 2); ctx.scale(1, -1);
        // One bad drawable (a broken or tainted GIF) paints the fallback tile, never the whole reel.
        try { ctx.save(); drawSymbol(ctx, strips[r][j], t, look); } catch { ctx.restore(); ctx.save(); drawSymbol(ctx, strips[r][j], t, { reduced: look.reduced, face: look.face }); }
        ctx.restore();
        const glaze = ctx.createLinearGradient(-128, 0, 128, 0);
        glaze.addColorStop(0, '#07040f99'); glaze.addColorStop(0.12, '#ffffff08'); glaze.addColorStop(0.5, '#ffffff00');
        glaze.addColorStop(0.88, '#ffffff08'); glaze.addColorStop(1, '#07040f99');
        ctx.fillStyle = glaze; ctx.fillRect(-128, -128, 256, 256);
        ctx.strokeStyle = '#e8bbd526'; ctx.lineWidth = 1; ctx.strokeRect(-116, -116, 232, 232);
        // A2: the cell one step off the payline ghosts gold. The reel window shows about half of each
        // neighbour (drum r 0.43, 13 cells, window 0.39 tall), so the tell reads without moving a stop.
        const gh = ghost && ghost.r === r && ghost.j === j ? ghostAmt(t) : 0;
        if (gh > 0) { ctx.fillStyle = `rgba(255,194,58,${(0.62 * gh).toFixed(3)})`; ctx.fillRect(-128, -128, 256, 256); }
        ctx.restore();
      }
      if (n) reelTex[r].needsUpdate = true;
    }
    lastPaint = t;
  }
  function setStrips(next) {
    strips = [0, 1, 2].map(r => (next && Array.isArray(next[r]) ? next[r] : []));
    for (let r = 0; r < 3; r++) {
      if (reelTex[r]) reelTex[r].dispose();
      reelCanvas[r] = makeCanvas(Math.max(1, strips[r].length) * 256, 256);
      reelTex[r] = canvasTexture(reelCanvas[r]);
      reels[r].material = new THREE.MeshBasicMaterial({ map: reelTex[r] });
    }
    owned.push(...reelTex);
    paint(performance.now());
  }
  setStrips([]);
  const stopsNow = [0, 0, 0];
  function setStops(stops) {
    stops.forEach((k, r) => { stopsNow[r] = k; reels[r].rotation.x = restX[r] + angle(k, strips[r].length || 13); });
  }

  // Framing: from cam_seat/cam_target and live bounds, at the rest pose. The marquee is in the play box
  // and EMI's face so the cabinet's name and her glance read above the reels at every aspect (in-room tidy, lane F1).
  let poses = null;
  function frame(aspect) {
    const y = rig.position.y; rig.position.y = 0; rig.updateMatrixWorld(true);
    const seat = get('cam_seat').getWorldPosition(new THREE.Vector3()), target = get('cam_target').getWorldPosition(new THREE.Vector3());
    const dir = seat.sub(target); if (dir.lengthSq() < 1e-8) dir.set(0, 0, 1); dir.normalize();
    const playBox = new THREE.Box3();
    (glass ? [glass] : reels).forEach(n => playBox.expandByObject(n));
    playBox.expandByObject(lever);
    for (const n of [get('marquee'), glowNode, faceMesh]) if (n) playBox.expandByObject(n);
    const whole = new THREE.Box3().setFromObject(cabinet);
    const fit = (box, d, aimAt) => {
      const right = new THREE.Vector3().crossVectors(new THREE.Vector3(0, 1, 0), d).normalize(), up = new THREE.Vector3().crossVectors(d, right);
      const c = box.getCenter(new THREE.Vector3());
      if (aimAt) c.addScaledVector(up, (aimAt.clone().sub(c).dot(up)) * 0.15);   // a nod toward the authored aim height, keep the box
      const tan = Math.tan(THREE.MathUtils.degToRad(LENS_DEG) / 2);
      let hw = 0, hh = 0, depth = 0;
      for (const x of [box.min.x, box.max.x]) for (const yy of [box.min.y, box.max.y]) for (const z of [box.min.z, box.max.z]) {
        const p = new THREE.Vector3(x, yy, z).sub(c);
        hw = Math.max(hw, Math.abs(p.dot(right))); hh = Math.max(hh, Math.abs(p.dot(up))); depth = Math.max(depth, p.dot(d));
      }
      const dist = Math.max(hh / tan, hw / (tan * aspect)) * FIT_MARGIN + depth;
      return { pos: c.clone().addScaledVector(d, dist), look: c, dist, right };
    };
    const side = new THREE.Vector3().crossVectors(new THREE.Vector3(0, 1, 0), dir).normalize();
    const quarter = dir.clone().addScaledVector(side, 0.75).add(new THREE.Vector3(0, 0.45, 0)).normalize();
    rig.position.y = y; rig.updateMatrixWorld(true);
    return { play: fit(playBox, dir, target), arrive: fit(whole, quarter), drop: (whole.max.y - whole.min.y) * 1.6 };
  }
  const aim = (pos, lookAt) => { camera.position.copy(pos); camera.lookAt(lookAt); };
  function resize() {
    const w = canvas.clientWidth || 1, h = canvas.clientHeight || 1;
    renderer.setSize(w, h, false); camera.aspect = w / h; camera.updateProjectionMatrix();
    poses = frame(camera.aspect);
    if (phase === 'play') aim(poses.play.pos, poses.play.look);
  }

  // Timeline state.
  let phase = 'hidden', tl = null, spin = null, pull = null, pullBack = null, hold = null, mood = 'idle', moodAt = 0;
  settle = () => { const t = tl, s = spin; tl = null; spin = null; if (t && t.done) t.done(); if (s && s.resolve) s.resolve(); };
  let revealAt = -Infinity, revealGain = 0, lean = null, party = null, shiverAt = -Infinity, trayAt = -Infinity, melted = false;
  let teasing = false;   // A1: reel 3 is alone and holding (the marquee's tease mood, the lights a notch down)
  // A4 attract and A5 the EMI land-wiggle: both live on the loop, so neither holds a timer of its own.
  let attract = null, attractOut = null;
  const wiggleAt = [-Infinity, -Infinity, -Infinity];
  const cellRad = i => TAU / (strips[i].length || 13);
  const restAngle = i => restX[i] + angle(stopsNow[i], strips[i].length || 13);
  const attractRad = (i, age) => (age / 1000) * ATTRACT.DRIFT_CELLS_PER_S * ATTRACT_SPEED[i] * cellRad(i);
  let heat = { from: 0, to: 0, at: -Infinity, gold: false };
  const pulse = [-Infinity, -Infinity, -Infinity], stopAt = [-Infinity, -Infinity, -Infinity];
  const sparks = [];
  const spawn = get('payout_spawn');
  if (spawn) {
    const geo = new THREE.OctahedronGeometry(0.012), mat = new THREE.MeshBasicMaterial({ color: 0xffd7a6 });
    for (let i = 0; i < 7; i++) { const m = new THREE.Mesh(geo, mat); m.visible = false; scene.add(m); sparks.push(m); }
  }
  const emi = get('emi_topper'), emiRest = emi && { p: emi.position.clone(), s: emi.scale.clone(), r: emi.rotation.clone() };
  const tmp = new THREE.Vector3(), nextColor = new THREE.Color(), GOLD = new THREE.Color(0xffc23a);

  function screen(name, text) {
    const s = screens[name];
    if (!s || s.text === text) return;
    s.text = text; paintDisplay(s.c.getContext('2d'), s.c.width, s.c.height, name === 'marquee', text); s.t.needsUpdate = true;
  }
  function setFace(name) {
    faceName = FACES[name] !== undefined ? name : 'idle0_0';
    if (atlas && faceMesh) atlas.offset.set((FACES[faceName] * 152 + 0.5) / 1672, 0.5 / 137);
    const m = { jackpot: 'jackpot', hearts: 'win', melt: 'melt', spirals: 'free' }[faceName] || 'idle';
    if (m !== mood) { mood = m; moodAt = performance.now(); }
  }
  /** THE GLOW on the marquee heat: in fast, out slow; reduced motion steps to the state. */
  function heatNow(t) {
    if (reduced) return heat.to;
    const q = clamp((t - heat.at) / (heat.to > heat.from ? FEEL.GLOW_IN_MS : FEEL.GLOW_OUT_MS));
    return heat.from + (heat.to - heat.from) * ease(q);
  }
  function heatTo(level, gold) { const t = performance.now(); heat = { from: heatNow(t), to: level, at: t, gold: !!gold }; }

  /* THE PAYLINE FRAME (playbook A6). The preview's landing frames sat at hard-coded coordinates and were not
   * ported; this one comes off live bounds instead, like the lever hint. The row is the reel window's own
   * spread, one drum cell tall (the chord of a 2PI/n cell on a drum of that radius), projected to client px,
   * and o.payline (a DOM frame) is moved onto it. No new material, no geometry, nothing in the glb. */
  let paylineAt = -Infinity, paylineHold = 0, paylinePulseN = 1;
  function paylineRect() {
    const box = new THREE.Box3();
    if (glass) box.expandByObject(glass); else reels.forEach(r => box.expandByObject(r));
    if (box.isEmpty()) return null;
    const drum = new THREE.Box3().setFromObject(reels[1]), n = strips[1].length || 13;
    const half = (drum.isEmpty() ? (box.max.y - box.min.y) : (drum.max.y - drum.min.y)) * Math.sin(Math.PI / n) / 2;
    const mid = (box.min.y + box.max.y) / 2, w = canvas.clientWidth || 1, h = canvas.clientHeight || 1, pts = [];
    for (const x of [box.min.x, box.max.x]) for (const y of [mid - half, mid + half]) for (const z of [box.min.z, box.max.z]) {
      const p = new THREE.Vector3(x, y, z).project(camera);
      pts.push([(p.x + 1) * w / 2, (1 - p.y) * h / 2]);
    }
    const left = Math.min(...pts.map(p => p[0])), right = Math.max(...pts.map(p => p[0]));
    const top = Math.min(...pts.map(p => p[1])), bottom = Math.max(...pts.map(p => p[1]));
    if (!(right > left && bottom > top)) return null;
    return { left, top, width: right - left, height: bottom - top };
  }

  /* B1 THE SPIRAL JAR (playbook Tier B, CONTRACT 10.16.A). There is no glb node for a jar and no model
   * request is allowed (section 9.6), so it is a DOM tube on live projected bounds, exactly the pattern the
   * payline frame uses: the payout_tray's middle (the cabinet's own box when the tray is absent), at the
   * CABINET's left edge in screen space, one reel_window tall. No new material, no geometry, nothing added
   * to the glb. Brake 9: the count is printed inside it, so the jar survives motion level 0. */
  const JAR_W = 0.17;          // of its own height: a narrow upright tube
  function screenBox(node) {
    if (!node) return null;
    const box = new THREE.Box3().setFromObject(node);
    if (box.isEmpty()) return null;
    const w = canvas.clientWidth || 1, h = canvas.clientHeight || 1, pts = [];
    for (const x of [box.min.x, box.max.x]) for (const y of [box.min.y, box.max.y]) for (const z of [box.min.z, box.max.z]) {
      const p = new THREE.Vector3(x, y, z).project(camera);
      pts.push([(p.x + 1) * w / 2, (1 - p.y) * h / 2]);
    }
    const left = Math.min(...pts.map(q => q[0])), right = Math.max(...pts.map(q => q[0]));
    const top = Math.min(...pts.map(q => q[1])), bottom = Math.max(...pts.map(q => q[1]));
    return right > left && bottom > top ? { left, top, width: right - left, height: bottom - top } : null;
  }
  function jarRect() {
    const cab = screenBox(cabinet);
    if (!cab) return null;
    const anchor = screenBox(tray) || cab, win = screenBox(glass);
    const height = Math.max(24, win ? win.height : cab.height * 0.3), width = Math.max(12, height * JAR_W);
    const w = canvas.clientWidth || 1, h = canvas.clientHeight || 1, m = 8;
    // The play camera frames the marquee and the reels, so payout_tray's projected middle sits BELOW the
    // viewport entirely at 16:9 (measured: y 930 of 720). Brake 9 says the count has to be readable, so the
    // tray is where the tube wants to stand and the screen is where it has to: held inside the canvas and
    // never lower than the reel window's own bottom, so it reads as a jar standing beside the reels.
    const floor = win ? win.top + win.height - height : h - height - m;
    const top = Math.min(anchor.top + anchor.height / 2 - height / 2, floor, Math.max(m, h - height - m));
    return { left: Math.min(Math.max(cab.left, m), Math.max(m, w - width - m)), top: Math.max(m, top), width, height };
  }

  function settleSpin() {
    if (!spin) return;
    const s = spin; spin = null;
    setStops(s.stops);
    lever.rotation.x = 0;
    s.resolve();
  }

  function update(t) {
    coinShower.update((t-coinTick)/1000,reduced); coinTick=t;
    if (tl) {
      const dt = t - tl.start;
      if (tl.kind === 'rise') {
        rig.position.y = -poses.drop * (1 - reveal(dt / RISE_MS));
        const k = ease((dt - RISE_MS) / CAMERA_MS);
        camera.position.lerpVectors(poses.arrive.pos, poses.play.pos, k);
        camera.lookAt(tmp.lerpVectors(poses.arrive.look, poses.play.look, k));
        if (dt >= RISE_MS + CAMERA_MS) { const done = tl.done; tl = null; phase = 'play'; rig.position.y = 0; aim(poses.play.pos, poses.play.look); done(); }
      } else if (tl.kind === 'sink') {
        rig.position.y = tl.fromY - (poses.drop + tl.fromY) * ease(dt / SINK_MS);
        if (dt >= SINK_MS) { const done = tl.done; tl = null; phase = 'hidden'; done(); }
      }
    }
    // A1 THE ANTICIPATION REEL: from reel 2's thud to reel 3's, with reel 3 alone and holding. The tape
    // already carries the outcome, so this window only delays it; nothing here can change what lands.
    // C1 (10.16.D): a solo re-spin has no reels 1 and 2 to wait for, so the whole travel is the tease window:
    // reel 3 alone, gold, for SPIN_MS and the full hold.
    teasing = !!spin && spin.teaseMs > 0 && spin.teaseDim && !reduced && (spin.solo
      ? t - spin.start < respinStopMs(PACE, spin.teaseMs)
      : t - spin.start >= reelStopMs(1) && t - spin.start < reelStopMs(2, PACE, spin.teaseMs));
    const partying = !!party && t < party.end, pr = partying ? party.r : null;
    if (spin) {
      const s = spin, dt = t - s.start;
      // The pull carries on from wherever the Law VIII lean (or the drag) left the lever.
      const wholeMs = s.solo ? respinMs(PACE, s.teaseMs) : reelsMs(PACE, s.teaseMs);
      lever.rotation.x = reduced ? (dt < wholeMs ? LEAN : 0) : Math.max(0.4 * Math.sin(Math.PI * clamp(dt / 420)), s.leverFrom * (1 - ease(dt / 420)));
      let all = true;
      for (let i = 0; i < 3; i++) {
        if (s.held === i || s.keep.includes(i)) continue;
        // C1: the re-spin is reel 3 alone, so it takes no stagger (pace.respinStopMs).
        const n = strips[i].length || 13, home = angle(s.stops[i], n);
        const dur = s.solo ? respinStopMs(PACE, s.teaseMs) : reelStopMs(i, PACE, s.teaseMs);
        const target = home + Math.PI * 2 * (6 + 2 * i);
        // Law VI: reduced motion takes the STATE. The reel rests, then is simply on its stop at its thud frame.
        let x = reduced ? s.from[i] : THREE.MathUtils.lerp(s.from[i], target, reelTravel(dt, dur));
        if (dt >= dur) {
          if (!s.stopped[i]) { s.stopped[i] = true; stopAt[i] = t; if (o.onReelStop) o.onReelStop(i); }   // THE THUD: cue on this frame
          const k = clamp((dt - dur) / THUD_MS);
          x = reduced ? home : target + (1 - thud(k)) * 0.035;
          if (k < 1) all = false;
        } else all = false;
        reels[i].rotation.x = restX[i] + x;
      }
      if (all && dt >= wholeMs) settleSpin();   // a held column never shortens the pace
    } else if (pull) lever.rotation.x = PULL_MAX * pull.amount;
    else if (pullBack) { const q = clamp((t - pullBack.start) / 200); lever.rotation.x = pullBack.angle * (1 - ease(q)); if (q === 1) pullBack = null; }
    else if (lean) lever.rotation.x = reduced ? LEAN : lean.from + (LEAN - lean.from) * ease((t - lean.start) / FEEL.LEAN_MS);   // Law VIII
    else lever.rotation.x = reduced || phase !== 'play' || partying ? 0 : BREATH_RAD * breath(t);   // THE BREATH, the only breather

    // A4 THE DRIFT while attracting: the drums roll slowly and the payline lands nothing (the stops never move).
    // Leaving it eases home over SETTLE_MS, never a thud: entering or leaving attract is not a party (Brake 1).
    if (!spin && (attract || attractOut)) {
      const q = attractOut ? clamp((t - attractOut.start) / ATTRACT.SETTLE_MS) : 0;
      for (let i = 0; i < 3; i++) reels[i].rotation.x = restAngle(i) + (attract ? attractRad(i, t - attract.start) : attractOut.from[i] * (1 - ease(q)));
      if (attractOut && q >= 1) attractOut = null;
    }
    // A5 THE EMI LAND-WIGGLE: a cell that landed EMI shrugs once after its own reel's thud and comes back to
    // the same stop. It rides after the thud, so no other reel waits on it (Law X). Reduced motion: nothing.
    if (!reduced) for (let i = 0; i < 3; i++) {
      const w = wiggleCells(t - wiggleAt[i]);
      if (w) reels[i].rotation.x = (spin ? reels[i].rotation.x : restAngle(i)) + w * cellRad(i);
    }

    // The payline reveal lifts (a win) or dims (nothing) for REVEAL_MS; THE THUD flashes each reel 2.2 -> 1.
    const rq = (t - revealAt) / PACE.REVEAL_MS, glow = rq < 0 || rq > 1 ? 1 : 1 + revealGain * (reduced ? 0.6 : Math.sin(Math.PI * rq));
    reels.forEach((r, i) => {
      const k = (t - stopAt[i]) / THUD_MS, flash = k < 0 || k >= 1 ? 1 : reduced ? 1.3 : 1 + 1.2 * (1 - ease(k));
      if (r.material && r.material.color) r.material.color.setScalar(glow * flash);
      // A2 under reduced motion: no travel and no repaint, the reel takes one gold tint and settles (Law VI).
      if (reduced && ghost && ghost.r === i && r.material && r.material.color) r.material.color.lerp(GOLD, 0.6 * ghostAmt(t));
    });

    freezers.forEach((f, i) => {
      if (!f) return;
      if (f.material && 'emissiveIntensity' in f.material) f.material.emissiveIntensity = hold === i ? 1.8 : 0.1;
      f.position.y = f.userData.restY - (reduced ? 0 : Math.sin(clamp((t - pulse[i]) / 200) * Math.PI) * 0.012);
    });

    // THE MARQUEE: the chase runs at the heat of the last win, the tier's own palette while it celebrates.
    const h = heatNow(t);
    const partyMood = pr && pr.chase ? (pr.gold ? 'jackpot' : pr.tier >= 3 ? 'big' : 'win') : null;
    const m = spin ? (teasing ? (spin.teaseGold ? 'tease_gold' : 'tease') : 'spin')
      : partyMood || (mood !== 'idle' ? mood : hold !== null ? 'freeze' : pull ? 'pull' : 'idle');
    const colors = PALETTES[m], period = m === 'spin' ? 420 : chaseMs(partyMood ? Math.max(h, pr.tier) : h);
    const travel = reduced ? 0 : (t - (partyMood ? party.start : moodAt)) / period;
    bulbs.forEach((b, i) => {
      const p = i * 0.65 + travel, step = Math.floor(p), mix = p - step;
      b.material.color.setHex(colors[step % colors.length]).lerp(nextColor.setHex(colors[(step + 1) % colors.length]), mix * mix * (3 - 2 * mix));
      b.material.emissive.copy(b.material.color); b.material.emissiveIntensity = (0.1 + 0.04 * h) * (teasing ? TEASE_DIM : 1);
    });
    // A4: one chase sweeps the bulbs every ~8 s while attracting. Brightness only, one pulse a bulb, no colour
    // change and nothing near the strobe floor.
    if (attract && !reduced) {
      const pass = ((t - attract.start) % ATTRACT.CHASE_MS) / ATTRACT.CHASE_PASS_MS;
      if (pass <= 1) bulbs.forEach((b, i) => {
        const d = Math.abs(pass * (bulbs.length + 8) - 4 - i);
        if (d < 4) b.material.emissiveIntensity += 0.9 * (1 - d / 4) ** 2;
      });
    }
    if (glowMat) {
      glowMat.emissive.copy(glowRest.color).lerp(GOLD, heat.gold ? clamp(h / 4) : 0);
      glowMat.emissiveIntensity = glowRest.intensity * (0.55 + 0.45 * h);
    }

    if (emi) {
      emi.position.copy(emiRest.p); emi.scale.copy(emiRest.s); emi.rotation.copy(emiRest.r);
      const age = t - moodAt, pa = partying ? t - party.start : Infinity;
      if (!reduced && phase === 'play') {
        let lean2 = 0, hop = 0, squash = 1, pop = 1, turn = 0;
        if (spin) lean2 = 0.055 * Math.sin((t - spin.start) / (melted ? 440 : 220));   // Brake 5: EMI slows while melted
        else if (pull) lean2 = -0.09 * pull.amount;
        else if (pr && pr.reveal && pa < FEEL.REVEAL_MS) { const q = pa / FEEL.REVEAL_MS; pop = 0.6 + 0.4 * reveal(q); turn = Math.PI * 2 * reveal(q); }   // THE REVEAL
        else if (pr && pr.jolt && pa < 600) { const q = pa / 600; hop = Math.sin(q * Math.PI) * 0.025; squash = 1 - 0.07 * Math.sin(q * Math.PI * 2); lean2 = 0.06 * Math.sin(q * Math.PI * 2); }
        else if (m === 'melt') { squash = 0.91; lean2 = -0.07; }
        else if (m === 'free' && age < 600) lean2 = 0.08 * Math.sin(age / 600 * Math.PI * 2);
        emi.position.y += hop; emi.rotation.z += lean2; emi.rotation.y += turn;
        emi.scale.multiplyScalar(pop); emi.scale.y *= squash; emi.scale.x /= Math.sqrt(squash);
      }
    }
    // screen_jackpot per tier: the jackpot counts up (THE REVEAL), big thuds 2.2 -> 1, bigger glows, then the base text.
    const sj = screens.screen_jackpot;
    if (sj) {
      const pa = partying ? t - party.start : Infinity;
      let lit = 0.45, text = sj.base || '';
      if (pr && pr.screen) {
        text = pr.tier === 4 && !reduced && pa < FEEL.REVEAL_MS ? Math.round(party.amount * ease(pa / FEEL.REVEAL_MS)).toLocaleString('en-US') : party.label || text;
        lit = reduced ? 1 : pr.tier >= 3 ? 0.45 * (1 + 1.2 * (1 - ease(pa / THUD_MS))) + 0.35 : 0.45 + 0.5 * (1 - ease(pa / FEEL.GLOW_OUT_MS));
      }
      sj.mesh.material.emissiveIntensity = lit;
      screen('screen_jackpot', text);
    }
    const payout=coinShower.debug();
    screen('screen_status',payout.active?'✦ '+payout.label+' ✦':screens.screen_status?.base||'');
    if(screens.screen_status) {const m=screens.screen_status.mesh.material;
      if(payout.active)m.emissive.setHSL(reduced ? .1 :(payout.age*.18)%1,.8,.6);else m.emissive.set(0xffffff);}
    sparks.forEach((s, i) => {
      const age = partying && pr.sparks && !reduced ? t - party.start - i * 55 : -1;
      s.visible = age >= 0 && age < 570;   // THE SPARKLE BURST, under 600 ms
      if (!s.visible) return;
      const q = age / 570;
      spawn.getWorldPosition(s.position);
      s.position.x += (i - 3) * 0.07 * q; s.position.y += Math.sin(q * Math.PI) * 0.32; s.position.z += q * 0.2; s.rotation.set(q * 4, i, q * 7);
    });
    if (tray) {   // payout_tray takes a small THE THUD when the bank starts or the spend lands
      const k = (t - trayAt) / THUD_MS;
      tray.scale.copy(trayRest);
      if (!reduced && k >= 0 && k < 1) tray.scale.y *= 1 + 0.12 * (1 - thud(k));
    }
    // THE SHIVER: the whole cabinet, +-4 px across the screen, no colour change. Reduced motion plays nothing.
    const px = reduced ? 0 : shiverPx(t - shiverAt);
    if (poses && phase === 'play') {
      const wpp = (2 * poses.play.dist * Math.tan(THREE.MathUtils.degToRad(LENS_DEG) / 2)) / (canvas.clientHeight || 1);
      rig.position.x = rigRest.x + poses.play.right.x * px * wpp; rig.position.z = rigRest.z + poses.play.right.z * px * wpp;
    }
    if (o.jar) {   // B1: the tube rides the cabinet's live bounds; the station paints the fill and the count
      const jr = phase === 'play' && o.jar.dataset.on != null ? jarRect() : null;
      o.jar.hidden = !jr;
      if (jr) {
        const js = o.jar.style;
        js.left = `${jr.left.toFixed(1)}px`; js.top = `${jr.top.toFixed(1)}px`;
        js.width = `${jr.width.toFixed(1)}px`; js.height = `${jr.height.toFixed(1)}px`;
      }
    }
    if (o.payline) {   // A6: the winning row is framed for the rollup, then THE GLOW goes out over 480 ms
      const lit = phase === 'play' ? paylineGlow(t - paylineAt, paylineHold, paylinePulseN) : 0;
      const rect = lit > 0 ? paylineRect() : null;
      o.payline.hidden = !rect;
      if (rect) {
        const s = o.payline.style;
        s.left = `${rect.left.toFixed(1)}px`; s.top = `${rect.top.toFixed(1)}px`;
        s.width = `${rect.width.toFixed(1)}px`; s.height = `${rect.height.toFixed(1)}px`;
        s.opacity = lit.toFixed(3);
      }
    }
    if (!reduced && (ghost || t - lastPaint > PAINT_MS)) paint(t);   // A2's ghost repaints every frame while it runs
    if (ghost && t - ghost.at >= FEEL_ALMOST.TELL_MS) ghost = null;
    if (o.hint) {
      o.hint.hidden = phase !== 'play' || !!spin;
      if (!o.hint.hidden) {
        const box = new THREE.Box3().setFromObject(lever), p = new THREE.Vector3((box.min.x + box.max.x) / 2, box.max.y, (box.min.z + box.max.z) / 2).project(camera);
        o.hint.style.left = `${(p.x + 1) * canvas.clientWidth / 2}px`; o.hint.style.top = `${(1 - p.y) * canvas.clientHeight / 2 - 12}px`;
      }
    }
    renderer.render(scene, camera);
  }
  // A throw in one frame must not stop the loop (a spin in flight would never land). A tainted canvas
  // (cross-origin media without CORS) stays tainted, so drop the dealt GIFs and rebuild the reel canvases.
  let frameFailed = false;
  const loop = t => {
    if (disposed) return;
    try { update(t); } catch (err) {
      if (look.gif) { console.warn('[slot] reel media failed to upload, using fallback art', err); look = { ...look, gif: null }; setStrips(strips); }
      else if (!frameFailed) console.error('[slot] frame failed', err);
      frameFailed = true;
    }
    raf = requestAnimationFrame(loop);
  };

  // Lever drag and freeze taps (preview gesture: release past 55% of the pull spins once).
  const ray = new THREE.Raycaster(), pointer = new THREE.Vector2();
  function leverRect() {
    const box = new THREE.Box3().setFromObject(lever), r = canvas.getBoundingClientRect(), pts = [];
    for (const x of [box.min.x, box.max.x]) for (const y of [box.min.y, box.max.y]) for (const z of [box.min.z, box.max.z]) {
      const p = new THREE.Vector3(x, y, z).project(camera); pts.push([r.left + (p.x + 1) * r.width / 2, r.top + (1 - p.y) * r.height / 2]);
    }
    return { left: Math.min(...pts.map(p => p[0])), right: Math.max(...pts.map(p => p[0])), top: Math.min(...pts.map(p => p[1])), bottom: Math.max(...pts.map(p => p[1])) };
  }
  function cancelPull() {
    if (!pull) return;
    pullBack = { start: performance.now(), angle: lever.rotation.x };
    if (canvas.hasPointerCapture(pull.id)) canvas.releasePointerCapture(pull.id);
    pull = null; canvas.style.cursor = '';
  }
  function onDown(e) {
    if (phase !== 'play' || spin || pull || !e.isPrimary || e.button !== 0 || !o.canPull()) return;
    const r = canvas.getBoundingClientRect();
    pointer.set((e.clientX - r.left) / r.width * 2 - 1, 1 - (e.clientY - r.top) / r.height * 2);
    ray.setFromCamera(pointer, camera);
    const targets = [lever, ...freezers.filter(Boolean)];
    let ob = ray.intersectObjects(targets, true)[0]?.object;
    while (ob && !targets.includes(ob)) ob = ob.parent;
    const b = leverRect(), pad = 18;
    if (ob === lever || (!ob && e.clientX >= b.left - pad && e.clientX <= b.right + pad && e.clientY >= b.top - pad && e.clientY <= b.bottom + pad)) {
      e.preventDefault(); pullBack = null; lean = null; pull = { id: e.pointerId, y: e.clientY, amount: 0 };
      canvas.setPointerCapture(e.pointerId); canvas.style.cursor = 'grabbing';
    } else if (ob) o.onFreeze(freezers.indexOf(ob));
  }
  function onMove(e) { if (pull && pull.id === e.pointerId) { e.preventDefault(); pull.amount = clamp((e.clientY - pull.y) / Math.min(120, canvas.clientHeight * 0.18)); } }
  function onUp(e) { if (!pull || pull.id !== e.pointerId) return; const commit = pull.amount >= PULL_COMMIT; cancelPull(); if (commit) o.onLever(); }
  canvas.addEventListener('pointerdown', onDown); canvas.addEventListener('pointermove', onMove);
  canvas.addEventListener('pointerup', onUp); canvas.addEventListener('pointercancel', cancelPull);

  resize();
  rig.position.y = -poses.drop;
  aim(poses.arrive.pos, poses.arrive.look);
  setFace('idle0_0');
  raf = requestAnimationFrame(loop);

  return {
    missing: [],
    faceImage: atlas && atlas.image,
    recoloured: recoloured.length > 0,
    get phase() { return phase; },
    get spinning() { return !!spin; },
    resize, setStrips, setStops, setFace, dispose, cancelPull,
    screen(name, text) { if (screens[name]) screens[name].base = text; screen(name, text); },
    setLook(next) { look = { ...next, reduced, face: atlas && atlas.image }; paint(performance.now()); },
    /** A freeze lit or cleared: the button dips either way (Law VIII). */
    setHold(col) { if (col !== hold) { const c = col !== null ? col : hold; if (c !== null) pulse[c] = performance.now(); } hold = col; },
    /** Law VIII: the lever leans into a press at once, before the tape or the server answers. */
    answer() { if (spin || pull || phase !== 'play') return; pullBack = null; lean = { start: performance.now(), from: lever.rotation.x }; },
    /** A press that was refused: the lean lets go. */
    letGo() { if (!lean) return; lean = null; pullBack = { start: performance.now(), angle: lever.rotation.x }; },
    setMelted(on) { melted = !!on; },
    /** One cabinet celebration per landed outcome (feel.recipe). Brake 2: a lesser party inside a running one merges. */
    celebrate(r, amount = 0, label = '') {
      coinShower.start(amount,r.tier,label);
      const t = performance.now();
      heatTo(Math.max(r.heat, party && t < party.end ? heat.to : 0), r.gold || (party && t < party.end && heat.gold));
      if (r.shiver) shiverAt = t;
      if (party && t < party.end && party.r.tier >= r.tier) return false;
      party = r.tier > 0 ? { r, amount, label, start: t, end: t + PARTY_MS[r.tier] } : null;
      return true;
    },
    /** THE BANK touches the tray: when a win starts paying out, or when a spend lands in it. */
    trayThud() { trayAt = performance.now(); },
    /** A6: frame the winning row for `ms` (THE BANK's rollup), then let THE GLOW out. It rides the landing
     *  beat the reveal already owns (Law X): tier 1 takes one soft pulse, reduced motion and a melted spin
     *  take a steady frame. `r` is feel.recipe's verdict for the outcome. */
    payline(ms, r) {
      paylineAt = performance.now();
      // Reduced motion has no rollup to follow (THE BANK settles at once), so the frame is steady and holds
      // the reveal's own beat instead of the whole count (Law VI: the STATE, not a longer version of it).
      paylineHold = reduced ? PACE.REVEAL_MS : Math.max(0, Number(ms) || 0);
      paylinePulseN = paylinePulses(r ? r.tier : 0, { reduced, melted: !!(r && r.melted) });
    },
    /** Law VI: a press or a new spin takes the frame straight to its settled end, never a faster pulse. */
    paylineOut() { if (paylineAt > -Infinity) paylineHold = Math.max(0, Math.min(paylineHold, performance.now() - paylineAt)); },
    /** Law VI: Back and suspend skip every ceremony to its settled state. */
    skip() { party = null; shiverAt = trayAt = -Infinity; stopAt.fill(-Infinity); revealAt = -Infinity; lean = null; ghost = null; teasing = false; heat = { ...heat, from: heat.to, at: -Infinity }; paylineAt = -Infinity; if (o.payline) o.payline.hidden = true; },
    /** A node's centre in client px (tokens fly from and to these), null when the glb lacks it. */
    project(name) {
      const n = get(name);
      if (!n) return null;
      const box = new THREE.Box3().setFromObject(n), p = (box.isEmpty() ? n.getWorldPosition(new THREE.Vector3()) : box.getCenter(new THREE.Vector3())).project(camera);
      const r = canvas.getBoundingClientRect();
      return { x: r.left + (p.x + 1) * r.width / 2, y: r.top + (1 - p.y) * r.height / 2 };
    },
    /** Light the landed payline for PACE.REVEAL_MS (a win lifts, nothing dims). The caller holds the pace. */
    reveal(win) { revealAt = performance.now(); revealGain = win ? 0.45 : -0.25; },
    /** Rise over the dimmed room, then ease to the seat. Resolves when interactive. */
    rise() {
      if (reduced) { rig.position.y = 0; phase = 'play'; aim(poses.play.pos, poses.play.look); return Promise.resolve(); }
      phase = 'rise';
      return new Promise(done => { tl = { kind: 'rise', start: performance.now(), done }; });
    },
    /** Slide down from wherever it is (Back at every phase). */
    sink() {
      cancelPull(); settleSpin();
      if (reduced || phase === 'hidden') { tl = null; phase = 'hidden'; return Promise.resolve(); }
      const prev = tl; phase = 'sink';
      return new Promise(done => { tl = { kind: 'sink', start: performance.now(), fromY: rig.position.y, done: () => { done(); if (prev && prev.done) prev.done(); } }; });
    },
    /** Spin to `stops`, the held column stays put. Resolves when the last reel has thudded, reelsMs() from
     *  now with or without reduced motion (the pace is economy, not animation).
     *  `tease` is A1 from feel.anticipation: `{holdMs, gold, dim}`. Reel 3 keeps its blur for holdMs longer
     *  and thuds late; reduced motion and Calm keep the hold, they only drop the light change. */
    spin(stops, held = null, tease = null) {
      coinShower.clear();
      settleSpin();
      ghost = null;
      wiggleAt.fill(-Infinity);
      // C1 (10.16.D): `keep` are reels that do not travel at all (the re-spin holds 1 and 2 as EMI), and
      // `solo` drops the stagger, because there is nothing before reel 3 to stagger behind.
      const keep = Array.isArray(tease && tease.keep) ? tease.keep.filter(i => [0, 1, 2].includes(i)) : [];
      return new Promise(resolve => {
        spin = { start: performance.now(), stops, held, keep, solo: keep.length > 0, leverFrom: lever.rotation.x,
                 resolve, stopped: [false, false, false],
                 teaseMs: Math.max(0, (tease && tease.holdMs) || 0), teaseGold: !!(tease && tease.gold),
                 teaseDim: tease ? tease.dim !== false : true,
                 from: reels.map((r, i) => r.rotation.x - restX[i]) };
        pull = null; pullBack = null; lean = null;
      });
    },
    /** A2 THE ALMOST, from feel.almost: the off-by-one cell the server's own strip put next to the line
     *  ghosts gold and snaps back, once. Nothing is weighted, nudged or re-drawn; this shows what landed. */
    almost(near) {
      if (!near || !(near.cell >= 0)) return;
      ghost = { r: near.reel === undefined ? 2 : near.reel, j: near.cell, at: performance.now() };
    },
    /** A4: the attract drift on or off (the station owns the idle timer and EMI's wink). `now` snaps back to
     *  the stops instead of easing home (Law VI: Back and suspend skip to the settled state). */
    attract(on, now = false) {
      if (on) {
        if (attract || attractOut || spin || reduced || phase !== 'play') return false;
        attract = { start: performance.now() };
        return true;
      }
      const was = !!attract, at = performance.now();
      if (attract && !now) attractOut = { start: at, from: [0, 1, 2].map(i => wrapRad(attractRad(i, at - attract.start))) };
      attract = null;
      if (now) { attractOut = null; setStops(stopsNow); }
      return was;
    },
    get attracting() { return !!attract; },
    /** A5: reel `i` wiggles `delay` ms from now (its own thud goes first). The loop owns it: no timer. */
    wiggle(i, delay = 0) { if (!reduced && i >= 0 && i < 3) wiggleAt[i] = performance.now() + Math.max(0, delay); },
    settle: settleSpin,
    /** For dev.html and CDP checks only. */
    debug() {
      const t = performance.now();
      return { lever: lever.rotation.x, heat: heatNow(t), gold: heat.gold, party: party && t < party.end ? party.r.party : null,
               tier: party && t < party.end ? party.r.tier : 0, face: faceName, shiverPx: reduced ? 0 : shiverPx(t - shiverAt),
               reelBrightness: reels.map(r => r.material && r.material.color ? r.material.color.r : 1), leaning: !!lean,
               tease: teasing, teaseMs: spin ? spin.teaseMs : 0, teaseGold: !!(spin && spin.teaseGold),
               almost: ghost ? { cell: ghost.j, reel: ghost.r, amt: Number(ghostAmt(t).toFixed(3)) } : null,
               tray: !!tray, glow: !!glowMat, emiScale: emi ? emi.scale.x / emiRest.s.x : null,
               payline: { lit: paylineGlow(t - paylineAt, paylineHold, paylinePulseN), hold: paylineHold, pulses: paylinePulseN, rect: paylineRect() },
               jar: jarRect(), solo: !!(spin && spin.solo), keep: spin ? spin.keep : [],
               attract: !!attract, drifting: !!(attract || attractOut), wiggling: wiggleAt.map(a => wiggleCells(t - a) !== 0) };
    },
  };
}
