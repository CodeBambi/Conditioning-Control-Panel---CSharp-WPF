/* ============================================================================
 * race/menu.js - the main menu and the character stage of Racing Thoughts.
 *
 *   createMenu({ root, renderer, pixel, audio, settings, log }) ->
 *     { show(), hide(), onPick(cb), options, stage: { update(dt), render(), dispose() }, dispose() }
 *   onPick yields 'race' | 'track' | 'clear' | 'surface'; setTrack(state | null) drives the track plate
 *   (CHART.md: the host's track-progress and the chart that lands).
 *
 * Two halves. LEFT is a DOM column (menu.css, .rm-*): the title, the tagline,
 * four verbs (race / options / how to drive / surface), options opening in
 * place, a key card. RIGHT is the stage: a second THREE.Scene drawn through
 * the race's own renderer + pixelizer (no second canvas) by a menu camera
 * whose view offset parks EMI in the right part of the frame (the boot puts
 * `is-lobby` on .race-hud so the run's chrome stays out of it). She STANDS on a
 * saucer podium with the cup beside her (never in it on the stage). Her idle
 * clip is the one breath on screen (Law III); every 3..6 s a wave / hop /
 * drum plays on a 0.3 s crossfade and settles back; a peek fires when the
 * pointer crosses into her half (6 s gap). The face swaps with the clip:
 * idle ^_^ 0, wave :3 1, hop >_< 2, peek o_o 3, drum $_$ 4. Reduced motion:
 * idle only, no one-shots, no crossfade pops.
 *
 * Props: race/assets/props.glb (podium, kart_cup, kart_saucer, floor_tile)
 * dresses the stage when it resolves; otherwise the lathe cup from the old
 * rig, a lathe podium and a checker floor stand in. EMI is emi.glb, a clone
 * of the cached pack with its own glass material so the stage face and the
 * run's face never fight.
 *
 * Options persist under ONE localStorage key, `race.options`, not through
 * engine/settings.js: that store is the dive's typed `sf-settings` row with a
 * fixed DEFAULTS table and a purchase ladder, and the race must not widen it.
 *
 * Keyboard (arrows, enter, esc), pointer and the gamepad (polled every frame,
 * the way the splash polled pads) all drive it. Every press answers inside
 * 100 ms (Law VIII): THE BOUNCE on the button, THE GLOW on focus.
 * ==========================================================================*/

import * as THREE from 'three';
import { loadPack, preparePixel, toInstanceGeometry, flattenRig } from './gltf.js';
import { PIXEL_STEPS, PIXEL_DEFAULT, normalizeBlock } from './pixel.js';

export const OPTIONS_KEY = 'race.options';
export const PROPS_URL = '/dtrh/race/assets/props.glb';
/** One entry per character. A second character later is one more line. */
export const ROSTER = [
  { id: 'emi', name: 'E.M.I.', glb: '/dtrh/race/assets/emi.glb',
    clips: { idle: 'idle', wave: 'wave', hop: 'hop', peek: 'peek', drum: 'drum' },
    faces: { idle: 0, wave: 1, hop: 2, peek: 3, drum: 4 } },
];
const DEFAULTS = { pixel: PIXEL_DEFAULT, music: 0.8, sfx: 0.8, motion: 'system', seed: 'daily', seedValue: 7 };
const MOTIONS = ['system', 'on', 'off'], SEEDS = ['daily', 'random', 'custom'];
const FACE_N = 5, GLASS = 'EMI_glass', FADE = 0.3, ONE_SHOTS = ['wave', 'hop', 'drum'];
const BEAT_MIN = 3, BEAT_MAX = 6, PEEK_GAP = 6;
export const PODIUM_H = 0.22, CUP_SCALE = 1.3;
export const CUP_AT = Object.freeze({ x: -1.2, y: 0, z: 0.45 });
const CUP_PROFILE = [[0.001, 0], [0.28, 0], [0.34, 0.06], [0.44, 0.3], [0.52, 0.58], [0.55, 0.66], [0.5, 0.68], [0.47, 0.6], [0.4, 0.3], [0.3, 0.1], [0.001, 0.08]];
const PODIUM_PROFILE = [[0.001, 0], [1.25, 0], [1.32, 0.05], [1.2, 0.12], [1.02, 0.18], [0.98, PODIUM_H], [0.001, PODIUM_H]];
const PINK = 0xff69b4, PORCELAIN = 0xf6e7c8, HAZE = 0x1b1232;
const PAD = { a: 0, b: 1, up: 12, down: 13, left: 14, right: 15 };
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

// ---- options ------------------------------------------------------------------------------
export function loadOptions() {
  const o = { ...DEFAULTS };
  try {
    const raw = JSON.parse(localStorage.getItem(OPTIONS_KEY) || 'null');
    if (raw && typeof raw === 'object') {
      if (typeof raw.pixel === 'number') o.pixel = normalizeBlock(raw.pixel);
      for (const k of ['music', 'sfx']) if (typeof raw[k] === 'number' && isFinite(raw[k])) o[k] = clamp(raw[k], 0, 1);
      if (MOTIONS.includes(raw.motion)) o.motion = raw.motion;
      if (SEEDS.includes(raw.seed)) o.seed = raw.seed;
      if (typeof raw.seedValue === 'number' && isFinite(raw.seedValue)) o.seedValue = raw.seedValue >>> 0;
    }
  } catch (e) { /* fresh defaults */ }
  return o;
}
export function saveOptions(o) { try { localStorage.setItem(OPTIONS_KEY, JSON.stringify(o)); } catch (e) { /* private mode */ } }
/** The run seed the options ask for: a number, or null for "roll one" (random). Daily = one track for everyone that day. */
export function seedFromOptions(o, now = new Date()) {
  if (o.seed === 'custom') return (o.seedValue >>> 0) || 1;
  if (o.seed === 'random') return null;
  const day = now.getUTCFullYear() * 10000 + (now.getUTCMonth() + 1) * 100 + now.getUTCDate();
  return (Math.imul(day, 0x9e3779b1) ^ 0x5bd1e995) >>> 0;
}
export function wantsReducedMotion(o, system) { return o.motion === 'on' ? true : o.motion === 'off' ? false : !!system; }

// ---- textures the placeholders need ------------------------------------------------------------
function checkerTexture(a, b) {
  const c = document.createElement('canvas'); c.width = c.height = 2;
  const g = c.getContext('2d'); g.fillStyle = a; g.fillRect(0, 0, 2, 2); g.fillStyle = b; g.fillRect(0, 0, 1, 1); g.fillRect(1, 1, 1, 1);
  const t = new THREE.CanvasTexture(c); t.wrapS = t.wrapT = THREE.RepeatWrapping; t.repeat.set(15, 15);
  t.magFilter = t.minFilter = THREE.NearestFilter; t.generateMipmaps = false; t.colorSpace = THREE.SRGBColorSpace;
  return t;
}
function hazeTexture() {
  const c = document.createElement('canvas'); c.width = 64; c.height = 128;
  const g = c.getContext('2d'), grad = g.createLinearGradient(0, 0, 0, 128);
  // the plane spans y -5..13 behind the stage: the pink band sits just above the floor's horizon (canvas y 0.55 = plane y ~ 3)
  grad.addColorStop(0, '#120b24'); grad.addColorStop(0.42, '#2a1540'); grad.addColorStop(0.55, '#7a2f66'); grad.addColorStop(0.64, '#3a1c4e'); grad.addColorStop(1, '#1b1232');
  g.fillStyle = grad; g.fillRect(0, 0, 64, 128);
  const t = new THREE.CanvasTexture(c); t.wrapS = THREE.RepeatWrapping; t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

// ---- the stage -----------------------------------------------------------------------------------
export function createStage({ renderer, pixel, reducedMotion = false, log = null, character = ROSTER[0] } = {}) {
  const scene = new THREE.Scene();
  scene.background = new THREE.Color(HAZE);
  scene.fog = new THREE.FogExp2(HAZE, 0.07);
  const camera = new THREE.PerspectiveCamera(42, 1, 0.1, 80);
  camera.position.set(0, 1.0, 4.4); camera.lookAt(0, 0.6, 0);   // pulled back from the brief's (0, 0.9, 3.2): at block 3 she filled the frame
  const view = { w: 1, h: 1, frac: 0.5 };
  let t = 0, mode = 'menu', reduced = !!reducedMotion, disposed = false;
  const own = [];
  const keep = (x) => { own.push(x); return x; };
  const lathe = (pts, mat, seg = 40) => new THREE.Mesh(keep(new THREE.LatheGeometry(pts.map(([x, y]) => new THREE.Vector2(x, y)), seg)), mat);

  scene.add(new THREE.HemisphereLight(0xffe4f2, 0x24163a, 0.9));
  const key = new THREE.DirectionalLight(0xfff0d8, 1.6); key.position.set(2.5, 4, 3); scene.add(key);
  const rim = new THREE.DirectionalLight(PINK, 1.5); rim.position.set(-2, 2.2, -3); scene.add(rim);
  const porcelain = keep(new THREE.MeshStandardMaterial({ color: PORCELAIN, roughness: 0.4, metalness: 0.05 }));
  const glow = keep(new THREE.MeshStandardMaterial({ color: PINK, emissive: PINK, emissiveIntensity: 1.1, roughness: 0.4 }));

  // backdrop haze + checker floor (props.glb floor_tile replaces the floor)
  const haze = keep(hazeTexture());
  const back = new THREE.Mesh(keep(new THREE.PlaneGeometry(40, 18)), keep(new THREE.MeshBasicMaterial({ map: haze, fog: false, depthWrite: false })));
  back.position.set(0, 4, -9); scene.add(back);
  let floor = new THREE.Mesh(keep(new THREE.PlaneGeometry(30, 30)), keep(new THREE.MeshLambertMaterial({ map: keep(checkerTexture('#2b1f47', '#3a2a5e')) })));
  floor.rotation.x = -Math.PI / 2; scene.add(floor);
  // the saucer podium she stands on
  const podium = new THREE.Group(); scene.add(podium);
  podium.add(lathe(PODIUM_PROFILE, porcelain, 48));
  const pring = new THREE.Mesh(keep(new THREE.TorusGeometry(1.1, 0.03, 8, 64)), glow); pring.rotation.x = Math.PI / 2; pring.position.y = PODIUM_H + 0.005; podium.add(pring);
  // the cup BESIDE her: group keeps the place, body takes the squash and stretch
  const cup = new THREE.Group(); cup.position.set(CUP_AT.x, CUP_AT.y, CUP_AT.z); cup.scale.setScalar(CUP_SCALE); cup.rotation.y = -0.6; scene.add(cup);
  const cupBody = new THREE.Group(); cup.add(cupBody);
  const cupMat = keep(new THREE.MeshStandardMaterial({ color: PORCELAIN, roughness: 0.35, metalness: 0.05, side: THREE.DoubleSide }));
  const saucer = new THREE.Mesh(keep(new THREE.CylinderGeometry(0.95, 0.7, 0.09, 40)), porcelain); saucer.position.y = 0.05; cupBody.add(saucer);
  const cupMesh = lathe(CUP_PROFILE, cupMat); cupMesh.position.y = 0.09; cupBody.add(cupMesh);
  const rimT = new THREE.Mesh(keep(new THREE.TorusGeometry(0.53, 0.025, 8, 48)), glow); rimT.rotation.x = Math.PI / 2; rimT.position.y = 0.75; cupBody.add(rimT);
  const handleCurve = new THREE.CubicBezierCurve3(new THREE.Vector3(0.33, 0.24, 0), new THREE.Vector3(0.86, 0.14, 0), new THREE.Vector3(0.86, 0.74, 0), new THREE.Vector3(0.46, 0.63, 0));
  cupBody.add(new THREE.Mesh(keep(new THREE.TubeGeometry(handleCurve, 18, 0.048, 8, false)), cupMat));
  const tea = new THREE.Mesh(keep(new THREE.CircleGeometry(0.47, 32)), keep(new THREE.MeshStandardMaterial({ color: 0x8a4a6a, roughness: 0.2 }))); tea.rotation.x = -Math.PI / 2; tea.position.y = 0.66; cupBody.add(tea);
  const ripple = new THREE.Mesh(keep(new THREE.RingGeometry(0.1, 0.14, 32)), keep(new THREE.MeshBasicMaterial({ color: 0xffd6ea, transparent: true, opacity: 0 })));
  ripple.rotation.x = -Math.PI / 2; ripple.position.y = 0.67; cupBody.add(ripple);

  loadPack(PROPS_URL, { log }).then((pack) => {
    if (disposed) return;
    if (pack.byName('podium')) { podium.clear(); podium.add(pack.clone('podium')); }
    if (pack.byName('kart_cup')) { cupBody.clear(); cupBody.add(pack.clone('kart_cup')); const s = pack.clone('kart_saucer'); if (s) cupBody.add(s); cupBody.add(ripple); }
    const tileGeo = pack.byName('floor_tile') ? toInstanceGeometry(pack.byName('floor_tile')) : null;
    if (tileGeo) {
      scene.remove(floor);
      const n = 11, im = new THREE.InstancedMesh(keep(tileGeo), keep(new THREE.MeshLambertMaterial({ vertexColors: true })), n * n), m = new THREE.Matrix4();
      let i = 0;
      for (let x = 0; x < n; x++) for (let z = 0; z < n; z++) im.setMatrixAt(i++, m.makeTranslation((x - 5) * 2, 0, (z - 5) * 2));
      floor = im; scene.add(im);
    }
    pixel.retexture(scene);
    if (log) log('[menu] props.glb dressed the stage');
  }).catch(() => { if (log) log('[menu] props.glb absent, placeholders stand'); });

  // ---- EMI: the pack clone, her own glass, the mixer ----
  const emi = { root: new THREE.Group(), model: null, mixer: null, actions: {}, busy: null, face: -1, tex: [], ready: [],
    nextAt: BEAT_MIN + Math.random() * (BEAT_MAX - BEAT_MIN), lastPeek: -PEEK_GAP };
  emi.root.position.set(0, PODIUM_H, 0); scene.add(emi.root);
  loadPack(character.glb, { log }).then((pack) => {
    if (disposed) return;
    const model = pack.scene.clone(true);
    flattenRig(model, pack.animations);       // one draw per pivot and material, the face glass kept apart
    model.traverse((o) => {
      if (!o.isMesh) return;
      const fix = (m) => {
        if (!m) return m;
        if (m.name === 'outline') return keep(new THREE.MeshBasicMaterial({ color: 0x0a0814, side: THREE.FrontSide }));
        if (o.name !== GLASS) return m;
        const c = keep(m.clone());
        for (const k of ['map', 'emissiveMap']) if (c[k]) {
          const tx = keep(c[k].clone()); tx.wrapS = THREE.RepeatWrapping; tx.magFilter = tx.minFilter = THREE.NearestFilter; tx.generateMipmaps = false; tx.needsUpdate = true;
          c[k] = tx; emi.tex.push(tx);
        }
        return c;
      };
      o.material = Array.isArray(o.material) ? o.material.map(fix) : fix(o.material);
    });
    preparePixel(model, pixel);
    emi.root.add(model); emi.model = model;
    const mixer = new THREE.AnimationMixer(model); emi.mixer = mixer;
    for (const [k, name] of Object.entries(character.clips)) {
      const clip = THREE.AnimationClip.findByName(pack.animations, name);
      if (!clip) continue;
      const a = mixer.clipAction(clip); emi.actions[k] = a;
      if (k === 'idle') { a.setLoop(THREE.LoopRepeat, Infinity); a.play(); }
      else { a.setLoop(THREE.LoopOnce, 1); a.clampWhenFinished = true; }
    }
    mixer.addEventListener('finished', (e) => { if (e.action !== emi.actions.idle) settle(e.action); });
    face(character.faces.idle);
    for (const cb of emi.ready) { try { cb(model); } catch (err) { /* a listener never breaks the stage */ } }
    emi.ready.length = 0;
    if (log) log(`[menu] ${character.id} on stage: ${Object.keys(emi.actions).join(' ')}`);
  }).catch((e) => { if (log) log('[menu] emi.glb failed: ' + ((e && e.message) || e)); });

  function face(i) { i = clamp(i | 0, 0, FACE_N - 1); if (i === emi.face) return; emi.face = i; for (const tx of emi.tex) tx.offset.x = i / FACE_N; }
  function settle(a) {
    const idle = emi.actions.idle;
    if (idle) { idle.enabled = true; idle.setEffectiveTimeScale(1); idle.setEffectiveWeight(1); a.crossFadeTo(idle, reduced ? 0 : FADE, false); }
    emi.busy = null; face(character.faces.idle);
    emi.nextAt = t + BEAT_MIN + Math.random() * (BEAT_MAX - BEAT_MIN);
  }
  /** Play a one-shot over the idle (0.3 s crossfade, Law XI: a blend, never a cut). Returns false if the clip is missing. */
  function play(k, { fade = FADE, timeScale = 1 } = {}) {
    const a = emi.actions[k], idle = emi.actions.idle;
    if (!a || a === idle) return false;
    if (emi.busy && emi.actions[emi.busy] !== a) emi.actions[emi.busy].fadeOut(0.1);
    a.reset().setEffectiveTimeScale(timeScale).setEffectiveWeight(1).play();
    if (idle) idle.crossFadeTo(a, reduced ? 0 : fade, false);
    emi.busy = k;
    if (character.faces[k] != null) face(character.faces[k]);
    return true;
  }
  function peek() { if (reduced || mode !== 'menu' || emi.busy || t - emi.lastPeek < PEEK_GAP) return; emi.lastPeek = t; play('peek'); }
  function rippleTea() { ripple.scale.setScalar(1); ripple.material.opacity = 0.9; }

  function update(dt) {
    t += dt;
    if (emi.mixer) emi.mixer.update(dt);
    if (mode === 'menu' && !reduced && !emi.busy && t >= emi.nextAt) play(ONE_SHOTS[Math.floor(Math.random() * ONE_SHOTS.length)]);
    haze.offset.x += dt * 0.004;   // scenery drifts; EMI is the one breath (Law III)
    if (ripple.material.opacity > 0) { ripple.scale.addScalar(dt * 4); ripple.material.opacity = Math.max(0, ripple.material.opacity - dt * 1.6); }
  }
  function render() { pixel.render(scene, camera); }
  function resize(w, h) { view.w = Math.max(1, w | 0); view.h = Math.max(1, h | 0); camera.aspect = view.w / view.h; applyView(); }
  /** Where the visible centre of the stage sits across the canvas (0.5 = the middle; the menu column pushes it right). */
  function setViewFraction(f) { view.frac = clamp(+f || 0.5, 0.3, 0.8); applyView(); }
  function applyView() {
    const off = Math.round((view.frac - 0.5) * view.w);
    if (off) camera.setViewOffset(view.w, view.h, -off, 0, view.w, view.h); else camera.clearViewOffset();
    camera.updateProjectionMatrix();
  }
  function dispose() {
    if (disposed) return;
    disposed = true;
    if (emi.mixer) emi.mixer.stopAllAction();
    for (const x of own) { try { x.dispose(); } catch (e) { /* shared or gone */ } }
    scene.clear();
  }
  return {
    scene, camera, podium, cup: { group: cup, body: cupBody, ripple: rippleTea },
    emi: { root: emi.root, play, face, peek, model: () => emi.model, busy: () => emi.busy, ready(cb) { if (emi.model) cb(emi.model); else emi.ready.push(cb); } },
    update, render, resize, setViewFraction, setMode(m) { mode = m; }, setReduced(v) { reduced = !!v; }, dispose,
  };
}

// ---- the menu ------------------------------------------------------------------------------------
export function createMenu({ root, renderer, pixel, audio, settings = {}, log = null }) {
  const options = loadOptions();
  const systemReduced = !!(typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches);
  const reduced = () => wantsReducedMotion(options, settings.reducedMotion != null ? settings.reducedMotion : systemReduced);
  const stage = createStage({ renderer, pixel, reducedMotion: reduced(), log });
  const picks = [];
  const el = (tag, cls, parent, text) => { const d = document.createElement(tag); d.className = cls; if (text != null) d.textContent = text; parent.appendChild(d); return d; };
  const hit = (node, cls) => { node.classList.remove(cls); void node.offsetWidth; node.classList.add(cls); };
  const canLevels = !!(audio && typeof audio.setLevels === 'function');

  const layer = el('div', 'rm-root', root); layer.hidden = true; layer.setAttribute('role', 'dialog'); layer.setAttribute('aria-label', 'racing thoughts');
  const col = el('div', 'rm-col', layer);
  el('div', 'rm-title', col, 'racing thoughts');
  el('div', 'rm-tag', col, 'steer. pop. never stop.');
  const list = el('div', 'rm-list', col); list.setAttribute('role', 'menu');
  const optPanel = el('div', 'rm-panel rm-options', col); optPanel.hidden = true;
  const howPanel = el('div', 'rm-panel rm-how', col); howPanel.hidden = true;
  // ---- the track plate: what the host is doing with the file, then what it found ----
  const trackEl = el('div', 'rm-track', col); trackEl.hidden = true; trackEl.setAttribute('aria-live', 'polite');
  const trackName = el('div', 'rm-track-name', trackEl, '');
  const trackBar = el('div', 'rm-track-bar', trackEl); const trackFill = el('i', '', trackBar);
  const trackCap = el('div', 'rm-track-cap', trackEl, '');
  el('div', 'rm-foot', col, 'arrows move · enter picks · esc back · p pixels · m mute');
  const stageEl = el('div', 'rm-stage', layer);
  const plate = el('div', 'rm-plate', stageEl);
  const arrowL = el('button', 'rm-arrow', plate, '‹'), nameEl = el('div', 'rm-name', plate, ROSTER[0].name), arrowR = el('button', 'rm-arrow', plate, '›');
  for (const a of [arrowL, arrowR]) { a.type = 'button'; const off = ROSTER.length === 1; a.disabled = off; a.setAttribute('aria-disabled', String(off)); a.title = off ? 'one racer for now' : 'next racer'; }

  // ---- how to drive ----
  el('h3', 'rm-h', howPanel, 'how to drive');
  for (const [k, v] of [['arrows / wasd', 'steer, throttle, brake'], ['shift', 'drift (hold, let go for turbo)'], ['e', 'use the item'], ['p', 'pixel look'], ['m', 'mute'], ['esc', 'the brake'], ['pad', 'stick steers, rt goes, a drifts, x item, start brakes']]) {
    const row = el('div', 'rm-key', howPanel); el('kbd', '', row, k); el('span', '', row, v);
  }
  el('div', 'rm-hint', howPanel, 'nothing is lost. you cannot fail. esc or enter goes back.');

  // ---- options: each row is a value with left / right and a press ----
  const pct = (v) => `${Math.round(v * 100)}%`;
  const step = (k, d) => { options[k] = clamp(Math.round((options[k] + d) * 10) / 10, 0, 1); applyLevels(); };
  const cyc = (arr, v, d) => arr[(arr.indexOf(v) + d + arr.length) % arr.length];
  function applyLevels() { if (canLevels) { try { audio.setLevels({ music: options.music, sfx: options.sfx }); } catch (e) { /* audio gone */ } } }
  function setPixel(n) { pixel.setBlock(n); pixel.retexture(stage.scene); options.pixel = pixel.block; }
  const ROWS = [
    { id: 'pixel', label: 'pixel block', get: () => (pixel.block ? `${pixel.block} px` : 'off'), move: (d) => setPixel(cyc(PIXEL_STEPS, pixel.block, d)) },
    { id: 'music', label: 'music', get: () => pct(options.music), move: (d) => step('music', d * 0.1), dim: !canLevels, hint: 'wired in the audio pass' },
    { id: 'sfx', label: 'sfx', get: () => pct(options.sfx), move: (d) => step('sfx', d * 0.1), dim: !canLevels, hint: 'wired in the audio pass' },
    { id: 'motion', label: 'reduced motion', get: () => options.motion, move: (d) => { options.motion = cyc(MOTIONS, options.motion, d); stage.setReduced(reduced()); layer.dataset.motion = options.motion; }, hint: 'stage and intro now, the run on the next launch' },
    { id: 'seed', label: 'seed', get: () => (options.seed === 'custom' ? `custom ${options.seedValue}` : options.seed), move: (d) => { options.seed = cyc(SEEDS, options.seed, d); seedIn.hidden = options.seed !== 'custom'; }, press: () => { if (options.seed === 'custom') seedIn.focus(); } },
    { id: 'back', label: 'back', get: () => '', press: () => open('main') },
  ];
  el('h3', 'rm-h', optPanel, 'options');
  const rowEls = ROWS.map((r) => {
    const row = el('div', `rm-row${r.dim ? ' is-dim' : ''}`, optPanel); row.dataset.id = r.id; row.setAttribute('role', 'menuitem');
    el('span', 'rm-row-label', row, r.label); r.valEl = el('span', 'rm-row-val rh-num', row, r.get());
    if (r.hint) el('span', 'rm-row-hint', row, r.hint);
    row.addEventListener('click', () => { focusRow(ROWS.indexOf(r)); act('press'); });
    row.addEventListener('pointerenter', () => focusRow(ROWS.indexOf(r)));
    return row;
  });
  const seedIn = el('input', 'rm-seed', optPanel); seedIn.type = 'number'; seedIn.min = '1'; seedIn.step = '1'; seedIn.value = String(options.seedValue); seedIn.hidden = options.seed !== 'custom';
  seedIn.addEventListener('input', () => { const v = Number(seedIn.value); if (isFinite(v) && v > 0) { options.seedValue = v >>> 0; refresh(); } });
  seedIn.addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === 'Escape') { e.preventDefault(); seedIn.blur(); } e.stopPropagation(); });

  // ---- the verbs. `track` only under a host (the file dialog is its), `clear` only once a file is in ----
  const canTrack = !!settings.trackPick;
  const VERBS = [['race', 'race'], ['track', 'load a track'], ['clear', 'just the road'], ['options', 'options'], ['how', 'how to drive'], ['surface', 'surface']];
  const verbEls = VERBS.map(([id, label], i) => {
    const b = el('button', 'rm-btn', list, label); b.type = 'button'; b.dataset.id = id; b.setAttribute('role', 'menuitem');
    b.addEventListener('click', () => { idx.main = i; act('press'); });
    b.addEventListener('pointerenter', () => { idx.main = i; refresh(); });
    return b;
  });
  const verbEl = (id) => verbEls[VERBS.findIndex(([v]) => v === id)];
  verbEl('track').hidden = !canTrack; verbEl('clear').hidden = true;
  const stepVerb = (from, dir) => {   // the next visible verb in that direction, wrapping
    let i = from;
    for (let k = 0; k < VERBS.length; k++) { i = (i + dir + VERBS.length) % VERBS.length; if (!verbEls[i].hidden) return i; }
    return from;
  };
  const STAGE_CAP = { picking: 'pick a file', decode: 'reading the file', energy: 'feeling the pulse', words: 'listening for the words', cancelled: '', error: '' };
  let trackState = null;
  /**
   * setTrack(state | null): the plate and the verbs follow the host. state = { stage, pct, name,
   * durationSec, countable, partial, message }; stage 'ready' is a chart in hand (partial while the
   * words are still landing). Null clears the plate and the verbs read as the seeded run again.
   */
  function setTrack(state) {
    trackState = state && state.stage ? state : null;
    const st = trackState, ready = !!st && st.stage === 'ready';
    trackEl.hidden = !st || st.stage === 'cancelled';
    trackEl.classList.toggle('is-ready', ready); trackEl.classList.toggle('is-error', !!st && st.stage === 'error');
    trackEl.classList.toggle('is-busy', !!st && !ready && st.stage !== 'error');
    if (st) {
      trackName.textContent = st.name || (st.stage === 'picking' ? 'a track' : '');
      const pct = ready ? 1 : clamp(Number(st.pct) || 0, 0, 1);
      trackFill.style.width = `${Math.round(pct * 100)}%`;
      if (ready) {
        const mins = Math.max(1, Math.round((Number(st.durationSec) || 0) / 60));
        const n = Number(st.countable) || 0;
        trackCap.textContent = st.partial ? `${mins} min · still listening for the words` : `${mins} min · ${n ? `${n} to take` : 'nothing spoken, the pulse drives'}`;
      } else trackCap.textContent = st.stage === 'error' ? (st.message || 'the track would not load') : (STAGE_CAP[st.stage] || st.stage);
    }
    verbEl('race').textContent = ready ? 'race the track' : 'race';
    verbEl('track').textContent = ready ? 'another track' : 'load a track';
    verbEl('clear').hidden = !ready;
    if (verbEls[idx.main].hidden) idx.main = stepVerb(idx.main, 1);
    refresh();
  }

  // ---- navigation: one focus index per panel, every input path lands on act() ----
  let panel = 'main', shown = false, disposed = false;
  const idx = { main: 0, options: 0 };
  function open(p) { panel = p; list.hidden = p !== 'main'; optPanel.hidden = p !== 'options'; howPanel.hidden = p !== 'how'; if (p === 'options') idx.options = 0; refresh(); if (p !== 'options') seedIn.blur(); }
  function focusRow(i) { idx.options = clamp(i, 0, ROWS.length - 1); refresh(); }
  function refresh() {
    verbEls.forEach((b, i) => b.classList.toggle('is-focus', panel === 'main' && i === idx.main));
    ROWS.forEach((r, i) => { const v = r.get(); if (r.valEl.textContent !== v) r.valEl.textContent = v; rowEls[i].classList.toggle('is-focus', panel === 'options' && i === idx.options); });
  }
  function pick(id) { for (const cb of picks) { try { cb(id); } catch (e) { /* a listener never breaks the menu */ } } }
  function act(what) {
    if (!shown || disposed) return;
    if (panel === 'main') {
      if (what === 'up' || what === 'down') { idx.main = stepVerb(idx.main, what === 'up' ? -1 : 1); refresh(); return; }
      if (what !== 'press') return;
      const id = VERBS[idx.main][0];
      if (verbEls[idx.main].hidden) return;
      hit(verbEls[idx.main], 'is-hit');
      if (id === 'options' || id === 'how') open(id); else pick(id);
      return;
    }
    if (what === 'back') { open('main'); return; }
    if (panel === 'how') { if (what === 'press') open('main'); return; }
    const r = ROWS[idx.options];
    if (what === 'up' || what === 'down') { focusRow(idx.options + (what === 'up' ? -1 : 1)); return; }
    if ((what === 'left' || what === 'right') && r.move) { r.move(what === 'left' ? -1 : 1); hit(rowEls[idx.options], 'is-hit'); }
    else if (what === 'press') { if (r.press) r.press(); else if (r.move) r.move(1); hit(rowEls[idx.options], 'is-hit'); }
    saveOptions(options); refresh();
  }
  const KEYMAP = { ArrowUp: 'up', KeyW: 'up', ArrowDown: 'down', KeyS: 'down', ArrowLeft: 'left', KeyA: 'left', ArrowRight: 'right', KeyD: 'right', Enter: 'press', Space: 'press', Escape: 'back', Backspace: 'back' };
  function onKey(e) {
    if (!shown || e.repeat || e.altKey || e.ctrlKey || e.metaKey || document.activeElement === seedIn) return;
    const what = KEYMAP[e.code]; if (!what) return;
    e.preventDefault(); act(what);
  }
  const padWas = {};
  function pollPad(dt) {
    const list = navigator.getGamepads ? navigator.getGamepads() : null;
    let g = null; if (list) for (const p of list) if (p && p.connected) { g = p; break; }
    if (!g) return;
    const btn = (i) => !!(g.buttons[i] && g.buttons[i].pressed);
    const ax = g.axes || [], now = { up: btn(PAD.up) || ax[1] < -0.6, down: btn(PAD.down) || ax[1] > 0.6, left: btn(PAD.left) || ax[0] < -0.6, right: btn(PAD.right) || ax[0] > 0.6, press: btn(PAD.a), back: btn(PAD.b) };
    for (const k of Object.keys(now)) { if (now[k] && !padWas[k]) act(k); padWas[k] = now[k]; }
  }
  function onResize() {
    const w = root.clientWidth || window.innerWidth, h = root.clientHeight || window.innerHeight;
    stage.resize(w, h);
    stage.setViewFraction(shown ? ((col.offsetWidth || w * 0.38) / w + 1) / 2 + 0.03 : 0.5);   // a hair right of the visible centre: room for the cup
  }
  const onPointer = (e) => { if (shown && e.clientX > window.innerWidth * 0.5) stage.emi.peek(); };
  window.addEventListener('keydown', onKey);
  window.addEventListener('resize', onResize);
  stageEl.addEventListener('pointerenter', onPointer);
  layer.dataset.motion = options.motion;

  const menu = {
    options, stage: { update(dt) { if (shown) pollPad(dt); stage.update(dt); }, render: stage.render, dispose: stage.dispose, live: stage },
    onPick(cb) { if (typeof cb === 'function') picks.push(cb); },
    setTrack, get track() { return trackState; },
    show() { shown = true; layer.hidden = false; stage.setMode('menu'); open('main'); onResize(); hit(layer, 'is-in'); },
    hide() { shown = false; layer.hidden = true; stage.setViewFraction(0.5); },
    refresh,
    dispose() {
      if (disposed) return;
      disposed = true; shown = false;
      window.removeEventListener('keydown', onKey); window.removeEventListener('resize', onResize);
      stage.dispose(); layer.remove();
    },
  };
  onResize();
  return menu;
}

// self-check: node --check is the bar; loadOptions / seedFromOptions are pure given a localStorage stub.
