/* ============================================================================
 * scene.js - the slot cabinet in three.js, ported from blender-scripting
 * slot/preview/{app,polish}.js. One WebGL context per createScene(), freed by
 * dispose(). Nodes are looked up by NAME only (nodes.js); framing comes from
 * cam_seat -> cam_target and live bounds, because the glb is still moving.
 *
 * Not ported from the preview (feel is lane F1): chimes, the jewellery trims
 * and landing frames (they were placed at hard-coded coordinates), the debug
 * face strip and the cabinet/reel view toggle.
 * ==========================================================================*/

import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { REQUIRED, OPTIONAL, FACE_MATERIAL } from './nodes.js';
import { drawSymbol } from './symbols.js';

export const FACES = { idle0_0: 3, hearts: 7, spirals: 8, melt: 9, jackpot: 10 };
const RISE_MS = 620, CAMERA_MS = 620, SINK_MS = 340, THUD_MS = 340, PAINT_MS = 100;
const LENS_DEG = 20;           // vertical field; a narrow screen backs the camera off to keep the width
const FIT_MARGIN = 1.2;         // breathing room around the reels + lever box
const PULL_MAX = 0.5, PULL_COMMIT = 0.55;
const PALETTES = {
  idle: [0xd90068, 0x006acb, 0xff9c00, 0x6220bd, 0x00a97c], spin: [0xff126e, 0x1567ff, 0xffb000, 0x00ccac],
  pull: [0x7920c4, 0xff197c], freeze: [0x0049a8, 0x00c7f2, 0x74f4ff], win: [0xe37100, 0xffd32b, 0xff4b24],
  jackpot: [0xffbd00, 0xff1677, 0x7139ef, 0x00bed0], melt: [0x351369, 0xa32d99, 0x6230a0],
  free: [0x006ca0, 0x00dda8, 0x8726e5],
};

const clamp = x => Math.min(1, Math.max(0, x)), ease = x => 1 - (1 - clamp(x)) ** 3;
function bezier(x, a, b, c, d) {
  let lo = 0, hi = 1, t = x;
  for (let i = 0; i < 14; i++) { t = (lo + hi) / 2; const v = 3 * (1 - t) ** 2 * t * a + 3 * (1 - t) * t * t * c + t ** 3; if (v < x) lo = t; else hi = t; }
  return 3 * (1 - t) ** 2 * t * b + 3 * (1 - t) * t * t * d + t ** 3;
}
const reveal = x => bezier(clamp(x), 0.2, 1.35, 0.35, 1), thud = x => bezier(clamp(x), 0.2, 1.5, 0.4, 1);
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
 * @param {{canvas:HTMLCanvasElement, reduced:boolean, hint?:HTMLElement,
 *          canPull:()=>boolean, onLever:()=>void, onFreeze:(i:number)=>void}} o
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
  scene.add(rig);
  const get = name => rig.getObjectByName(name) || null;
  const missing = REQUIRED.filter(n => !get(n));
  if (missing.length) return { missing, dispose };
  const absent = OPTIONAL.filter(n => !get(n));
  if (absent.length) console.warn(`[slot] glb lacks optional nodes, degrading: ${absent.join(', ')}`);

  const cabinet = get('cabinet'), lever = get('lever');
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

  // Framing: from cam_seat/cam_target and live bounds, at the rest pose.
  let poses = null;
  function frame(aspect) {
    const y = rig.position.y; rig.position.y = 0; rig.updateMatrixWorld(true);
    const seat = get('cam_seat').getWorldPosition(new THREE.Vector3()), target = get('cam_target').getWorldPosition(new THREE.Vector3());
    const dir = seat.sub(target); if (dir.lengthSq() < 1e-8) dir.set(0, 0, 1); dir.normalize();
    const playBox = new THREE.Box3();
    (glass ? [glass] : reels).forEach(n => playBox.expandByObject(n));
    playBox.expandByObject(lever);
    const whole = new THREE.Box3().setFromObject(cabinet);
    const fit = (box, d, aimAt) => {
      const right = new THREE.Vector3().crossVectors(new THREE.Vector3(0, 1, 0), d).normalize(), up = new THREE.Vector3().crossVectors(d, right);
      const c = box.getCenter(new THREE.Vector3());
      if (aimAt) c.addScaledVector(up, aimAt.clone().sub(c).dot(up));   // authored aim height, live width
      const tan = Math.tan(THREE.MathUtils.degToRad(LENS_DEG) / 2);
      let hw = 0, hh = 0, depth = 0;
      for (const x of [box.min.x, box.max.x]) for (const yy of [box.min.y, box.max.y]) for (const z of [box.min.z, box.max.z]) {
        const p = new THREE.Vector3(x, yy, z).sub(c);
        hw = Math.max(hw, Math.abs(p.dot(right))); hh = Math.max(hh, Math.abs(p.dot(up))); depth = Math.max(depth, p.dot(d));
      }
      const dist = Math.max(hh / tan, hw / (tan * aspect)) * FIT_MARGIN + depth;
      return { pos: c.clone().addScaledVector(d, dist), look: c };
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
  let celebrateAt = -Infinity, celebrateAmount = 0;
  const pulse = [-Infinity, -Infinity, -Infinity];
  const sparks = [];
  const spawn = get('payout_spawn');
  if (spawn) {
    const geo = new THREE.OctahedronGeometry(0.012), mat = new THREE.MeshBasicMaterial({ color: 0xffd7a6 });
    for (let i = 0; i < 7; i++) { const m = new THREE.Mesh(geo, mat); m.visible = false; scene.add(m); sparks.push(m); }
  }
  const emi = get('emi_topper'), emiRest = emi && { p: emi.position.clone(), s: emi.scale.clone(), r: emi.rotation.clone() };
  const tmp = new THREE.Vector3(), nextColor = new THREE.Color();

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

  function settleSpin() {
    if (!spin) return;
    const s = spin; spin = null;
    setStops(s.stops);
    lever.rotation.x = 0;
    s.resolve();
  }

  function update(t) {
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
    if (spin) {
      const s = spin, dt = t - s.start;
      lever.rotation.x = s.leverFrom ? s.leverFrom * (1 - ease(dt / THUD_MS)) : 0.4 * Math.sin(Math.PI * clamp(dt / 420));
      let all = true;
      for (let i = 0; i < 3; i++) {
        if (s.held === i) continue;
        const n = strips[i].length || 13, dur = 1050 + i * 230, q = clamp(dt / dur);
        const target = angle(s.stops[i], n) + Math.PI * 2 * (4 + i);
        let x = THREE.MathUtils.lerp(s.from[i], target, ease(q));
        if (q === 1) { const k = clamp((dt - dur) / THUD_MS); x = target + (1 - thud(k)) * 0.035; if (k < 1) all = false; } else all = false;
        reels[i].rotation.x = restX[i] + x;
      }
      if (all) settleSpin();
    } else if (pull) lever.rotation.x = PULL_MAX * pull.amount;
    else if (pullBack) { const q = clamp((t - pullBack.start) / 200); lever.rotation.x = pullBack.angle * (1 - ease(q)); if (q === 1) pullBack = null; }

    freezers.forEach((f, i) => {
      if (!f) return;
      if (f.material && 'emissiveIntensity' in f.material) f.material.emissiveIntensity = hold === i ? 1.8 : 0.1;
      f.position.y = f.userData.restY - (reduced ? 0 : Math.sin(clamp((t - pulse[i]) / 200) * Math.PI) * 0.012);
    });

    const m = spin ? 'spin' : mood !== 'idle' ? mood : hold !== null ? 'freeze' : pull ? 'pull' : 'idle';
    const colors = PALETTES[m], period = m === 'spin' ? 420 : m === 'jackpot' ? 550 : m === 'idle' ? 1900 : 950;
    const travel = reduced ? 0 : (t - moodAt) / period;
    bulbs.forEach((b, i) => {
      const p = i * 0.65 + travel, step = Math.floor(p), mix = p - step;
      b.material.color.setHex(colors[step % colors.length]).lerp(nextColor.setHex(colors[(step + 1) % colors.length]), mix * mix * (3 - 2 * mix));
      b.material.emissive.copy(b.material.color); b.material.emissiveIntensity = m === 'jackpot' ? 0.22 : 0.1;
    });
    if (emi) {
      emi.position.copy(emiRest.p); emi.scale.copy(emiRest.s); emi.rotation.copy(emiRest.r);
      const age = t - moodAt;
      if (!reduced && phase === 'play') {
        let lean = 0, hop = 0, squash = 1;
        if (spin) lean = 0.055 * Math.sin((t - spin.start) / 220);
        else if (pull) lean = -0.09 * pull.amount;
        else if ((m === 'win' || m === 'jackpot') && age < 800) { const q = age / 800; hop = Math.sin(q * Math.PI) * 0.025; squash = 1 - 0.07 * Math.sin(q * Math.PI * 2); lean = 0.06 * Math.sin(q * Math.PI * 2); }
        else if (m === 'melt') { squash = 0.91; lean = -0.07; }
        else if (m === 'free' && age < 900) lean = 0.08 * Math.sin(age / 900 * Math.PI * 2);
        emi.position.y += hop; emi.rotation.z += lean; emi.scale.y *= squash; emi.scale.x /= Math.sqrt(squash);
      }
    }
    const celebrating = !reduced && t - celebrateAt < 1000;
    if (screens.screen_jackpot) screens.screen_jackpot.mesh.material.emissiveIntensity = celebrating ? 1.3 : reduced ? 0.45 : 0.45 + 0.12 * Math.sin(t / 500);
    if (screens.screen_jackpot) {
      const base = screens.screen_jackpot.base || '';
      screen('screen_jackpot', celebrating ? Math.round(celebrateAmount * ease((t - celebrateAt) / 650)).toLocaleString('en-US') : base);
    }
    sparks.forEach((s, i) => {
      const age = t - celebrateAt - i * 55;
      s.visible = celebrating && age >= 0 && age < 570;
      if (!s.visible) return;
      const q = age / 570;
      spawn.getWorldPosition(s.position);
      s.position.x += (i - 3) * 0.07 * q; s.position.y += Math.sin(q * Math.PI) * 0.32; s.position.z += q * 0.2; s.rotation.set(q * 4, i, q * 7);
    });
    if (!reduced && t - lastPaint > PAINT_MS) paint(t);
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
      e.preventDefault(); pullBack = null; pull = { id: e.pointerId, y: e.clientY, amount: 0 };
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
    get phase() { return phase; },
    get spinning() { return !!spin; },
    resize, setStrips, setStops, setFace, dispose, cancelPull,
    screen(name, text) { if (screens[name]) screens[name].base = text; screen(name, text); },
    setLook(next) { look = { ...next, reduced, face: atlas && atlas.image }; paint(performance.now()); },
    setHold(col) { if (col !== hold && col !== null) pulse[col] = performance.now(); hold = col; },
    celebrate(amount) { celebrateAt = performance.now(); celebrateAmount = amount; },
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
    /** Spin to `stops`, the held column stays put. Resolves when the last reel has thudded. */
    spin(stops, held = null) {
      settleSpin();
      return new Promise(resolve => {
        spin = { start: performance.now(), stops, held, leverFrom: lever.rotation.x, resolve,
                 from: reels.map((r, i) => r.rotation.x - restX[i]) };
        pull = null; pullBack = null;
        if (reduced) settleSpin();
      });
    },
    settle: settleSpin,
  };
}
