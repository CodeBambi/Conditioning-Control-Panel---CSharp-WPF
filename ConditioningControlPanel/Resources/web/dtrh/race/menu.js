/* ============================================================================
 * race/menu.js - the main menu and the character stage of Racing Thoughts.
 *
 *   createMenu({ root, renderer, pixel, audio, settings, log }) ->
 *     { show(), hide(), onPick(cb), options, stage: { update(dt), render(), dispose() }, dispose() }
 *   onPick yields 'race' | 'track' | 'clear' | 'story' | 'surface'; setTrack(state | null) drives the
 *   track plate (CHART.md: the host's track-progress and the chart that lands). refreshView() parks
 *   the stage where the column would park it even while the menu is hidden (race/cards.js borrows it).
 *
 * Two halves. LEFT is a DOM column (menu.css, .rm-*): the title, the tagline,
 * the verbs (race / load a track / just the road / options / how to drive /
 * the story / surface), options and the key card opening in place. `the story`
 * replays the four introduction cards of race/cards.js. RIGHT is the stage: a second THREE.Scene drawn through
 * the race's own renderer + pixelizer (no second canvas) by a menu camera
 * whose view offset parks EMI in the right part of the frame (the boot puts
 * `is-lobby` on .race-hud so the run's chrome stays out of it). She STANDS on a
 * saucer podium with the cup beside her (never in it on the stage). Her idle
 * clip is the one breath on screen (Law III); every 3..6 s a wave / hop /
 * drum plays on a 0.3 s crossfade and settles back; a peek fires when the
 * pointer crosses into her half (6 s gap). The face swaps with the clip:
 * idle ^_^ 0, wave :3 1, hop >_< 2, peek o_o 3, drum $_$ 4, and race/menuFlashes.js
 * borrows starry 5 and spiral 6 for the idle show it runs on the same stage:
 * every 8..13 s a flash pops in the air around her and she turns to look at
 * it, over the idle clip and never instead of it. Reduced motion: idle only,
 * no one-shots, no flashes, no crossfade pops.
 *
 * Screenshot aid: `?face=N` pins the stage face at atlas frame N, so one frame
 * can be shot on its own while the clips keep running. `?panel=howto` (and
 * `?panel=options`) opens the menu on that panel, the way `?card=N` opens a card.
 *
 * Props: race/assets/props.glb (podium, kart_cup, kart_saucer, floor_tile)
 * dresses the stage when it resolves; otherwise the lathe cup from the old
 * rig, a lathe podium and a checker floor stand in. EMI is emi.glb, a clone
 * of the cached pack with its own glass material so the stage face and the
 * run's face never fight.
 *
 * The BACKDROP is geometry, never an asset: a 32 m sky dome painted with a
 * canvas gradient (plum overhead, a pink haze band on the eye line, the mist
 * below), three oversized lathe cups sat far back as flat-shaded silhouettes
 * and a dozen additive bubbles drifting up behind the stage. The scene fog is
 * that same mist, so the checker dies in the haze instead of ending on a line.
 * Reduced motion holds the bubbles still.
 *
 * Options persist under ONE localStorage key, `race.options`, not through
 * engine/settings.js: that store is the dive's typed `sf-settings` row with a
 * fixed DEFAULTS table and a purchase ladder, and the race must not widen it.
 *
 * Keyboard (arrows, enter, esc), pointer and the gamepad (polled every frame,
 * the way the splash polled pads) all drive it. Every press answers inside
 * 100 ms (Law VIII): THE BOUNCE on the button, THE GLOW on focus.
 *
 * A FINGER CAN REACH EVERYTHING. There is no key card and no pad on a phone, so
 * `how to drive` opens on a THUMB card (drag the left side, double tap to jump,
 * hold the right side to drift, the use button, pause and sound) with the keys
 * and the pad kept below it, and
 * every path a key takes has a target under it: `how to drive` carries its own
 * back row and a tap anywhere off the panel closes it, and each value row wears
 * a real left and right button around the number, so music and sfx go DOWN by
 * touch as well as up (a whole-row press still steps up the way the pad does).
 * menu.css sizes all three to 44 px under `@media (pointer: coarse)`.
 * ==========================================================================*/

import * as THREE from 'three';
import { wantsTouch } from './touch.js';
import { loadPack, preparePixel, toInstanceGeometry, flattenRig, setFace, FACES } from './gltf.js';
import { PIXEL_STEPS, PIXEL_DEFAULT, normalizeBlock } from './pixel.js';
import { createMenuFlashes } from './menuFlashes.js';
import { vFovForAspect, bindViewportResize } from './viewport.js';

export const OPTIONS_KEY = 'race.options';
export const PROPS_URL = '/dtrh/race/assets/props.glb';
/** One entry per character. A second character later is one more line. */
export const ROSTER = [
  { id: 'emi', name: 'E.M.I.', glb: '/dtrh/race/assets/emi.glb',
    clips: { idle: 'idle', wave: 'wave', hop: 'hop', peek: 'peek', drum: 'drum' },
    faces: { idle: 0, wave: 1, hop: 2, peek: 3, drum: 4, starry: 5, spiral: 6 } },
];
const DEFAULTS = { pixel: PIXEL_DEFAULT, music: 0.8, sfx: 0.8, motion: 'system', seed: 'daily', seedValue: 7 };
const MOTIONS = ['system', 'on', 'off'], SEEDS = ['daily', 'random', 'custom'];
const GLASS = 'EMI_glass', FADE = 0.3, ONE_SHOTS = ['wave', 'hop', 'drum'];
const BEAT_MIN = 3, BEAT_MAX = 6, PEEK_GAP = 6;
// Stage metres, all of them read off props.glb so the placeholders and the real props agree.
// `podium` is a saucer: a foot of radius 0.75 on the floor flaring to a rim of radius 1.10, whose
// TOP PLATE is y 0.31 (the raised lip around it tops out at 0.35). PODIUM_H is that plate, the
// height her soles stand at; the lathe fallback below is cut to the same silhouette, so she never
// sinks into one podium and floats on the other.
export const PODIUM_H = 0.31, CUP_SCALE = 1.3;
// `kart_saucer` is 0.98 wide before CUP_SCALE, so the dish alone is wider than the podium plate: the
// cup can only stand clear on the FLOOR, never half over the rim. Widest dish (r 1.27 at y 0.13)
// against the podium's flare at that height (r 0.93) wants 2.20 between the two centres; CUP_AT is
// 2.35 out, back and to her left so the menu column never swallows it.
export const CUP_AT = Object.freeze({ x: -2.05, y: 0, z: -1.15 });
const CUP_PROFILE = [[0.001, 0], [0.28, 0], [0.34, 0.06], [0.44, 0.3], [0.52, 0.58], [0.55, 0.66], [0.5, 0.68], [0.47, 0.6], [0.4, 0.3], [0.3, 0.1], [0.001, 0.08]];
const PODIUM_PROFILE = [[0.001, 0], [0.75, 0], [1.05, PODIUM_H - 0.05], [1.1, PODIUM_H], [1.1, PODIUM_H + 0.04], [0.98, PODIUM_H + 0.04], [0.98, PODIUM_H], [0.001, PODIUM_H]];
const PINK = 0xff69b4, PORCELAIN = 0xf6e7c8, HAZE = 0x1b1232;
// the backdrop. MIST is both the fog and the bottom of the sky, so the floor's edge has nowhere to show.
const MIST = 0x3a1c4e, FOG_NEAR = 9, FOG_FAR = 21, SKY_R = 44, FAR_PLUM = 0x4a2464, FLOOR_W = 46, FLOOR_N = 21, BUBBLE_N = 12;
// far shapes: [x, z, scale, yaw]. The same cup profile, blown up and sat on the floor behind the gantry.
const FAR_CUPS = [[-15, -28, 6, 0.5], [14, -33, 7, -0.8], [-6.5, -37, 6.4, 2.1]];
const PAD = { a: 0, b: 1, up: 12, down: 13, left: 14, right: 15 };
// the stage camera at 16:9, its own cap, and the one-column breakpoint menu.css already keeps
const STAGE_FOV = 42, STAGE_MAX_VFOV = 86, ONE_COL_Q = '(max-width: 720px)';
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
/** Screenshot aid: `?face=N` pins the stage face so one atlas frame can be shot on its own.
 *  -1 (no param, an empty one, or anything unparseable) means the clips drive the face. */
const FACE_PIN = (() => {
  try {
    const v = new URLSearchParams(location.search).get('face');
    if (v === null || v === '' || !Number.isFinite(+v)) return -1;
    return clamp(+v | 0, 0, FACES.length - 1);
  } catch (e) { return -1; }              // no location (a node import), no pin
})();
/** Screenshot aid: `?panel=howto | options` opens the menu on that panel instead of the verbs. */
const PANEL_PIN = (() => {
  try {
    const v = (new URLSearchParams(location.search).get('panel') || '').toLowerCase();
    return v === 'howto' || v === 'how' ? 'how' : v === 'options' ? 'options' : null;
  } catch (e) { return null; }           // no location (a node import), no pin
})();

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
function checkerTexture(a, b, repeat = 15) {
  const c = document.createElement('canvas'); c.width = c.height = 2;
  const g = c.getContext('2d'); g.fillStyle = a; g.fillRect(0, 0, 2, 2); g.fillStyle = b; g.fillRect(0, 0, 1, 1); g.fillRect(1, 1, 1, 1);
  const t = new THREE.CanvasTexture(c); t.wrapS = t.wrapT = THREE.RepeatWrapping; t.repeat.set(repeat, repeat);
  t.magFilter = t.minFilter = THREE.NearestFilter; t.generateMipmaps = false; t.colorSpace = THREE.SRGBColorSpace;
  return t;
}
function skyTexture() {
  const c = document.createElement('canvas'); c.width = 128; c.height = 256;
  const g = c.getContext('2d'), grad = g.createLinearGradient(0, 0, 0, 256);
  // the dome is a sphere: the top of the canvas is the zenith, the middle is the eye line, the bottom is the mist
  grad.addColorStop(0, '#120b24'); grad.addColorStop(0.38, '#2c1544'); grad.addColorStop(0.452, '#5a2660');
  grad.addColorStop(0.477, '#a04a86'); grad.addColorStop(0.495, '#6b2c63'); grad.addColorStop(0.5, '#3a1c4e'); grad.addColorStop(1, '#3a1c4e');
  g.fillStyle = grad; g.fillRect(0, 0, 128, 256);
  g.globalCompositeOperation = 'lighter';
  for (const [x, r, a] of [[18, 20, 0.40], [64, 13, 0.24], [106, 24, 0.34]]) {   // pink glows on the band, so the drift reads
    const b = g.createRadialGradient(x, 122, 0, x, 122, r);
    b.addColorStop(0, `rgba(255,105,180,${a})`); b.addColorStop(1, 'rgba(255,105,180,0)');
    g.fillStyle = b; g.fillRect(x - r, 122 - r, r * 2, r * 2);
  }
  // below the eye line the dome is flat mist and nothing else: that is the colour the fog leaves the floor
  g.globalCompositeOperation = 'source-over'; g.fillStyle = '#3a1c4e'; g.fillRect(0, 128, 128, 128);
  const t = new THREE.CanvasTexture(c); t.wrapS = THREE.RepeatWrapping; t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

// ---- the stage -----------------------------------------------------------------------------------
export function createStage({ renderer, pixel, reducedMotion = false, log = null, character = ROSTER[0] } = {}) {
  const scene = new THREE.Scene();
  scene.background = new THREE.Color(HAZE);
  scene.fog = new THREE.Fog(MIST, FOG_NEAR, FOG_FAR);
  // STAGE_FOV is the vertical fov at 16:9 only. race/viewport.js re-solves it per aspect so the
  // stage keeps the same WIDTH of room at every window shape; a portrait phone opened it to about
  // 112 unclamped, which put the camera inside her head, so the stage takes a tighter cap than the
  // road does: a character reads wrong long before a tunnel does.
  const camera = new THREE.PerspectiveCamera(STAGE_FOV, 1, 0.1, 80);
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

  // ---- the backdrop: one dome, three far shapes, one instanced field of bubbles ----
  const sky = keep(skyTexture());
  const dome = new THREE.Mesh(keep(new THREE.SphereGeometry(SKY_R, 20, 14)), keep(new THREE.MeshBasicMaterial({ map: sky, side: THREE.BackSide, fog: false })));
  scene.add(dome);
  const farMat = keep(new THREE.MeshLambertMaterial({ color: FAR_PLUM, flatShading: true, fog: false }));
  const farGeo = keep(new THREE.LatheGeometry(CUP_PROFILE.map(([x, y]) => new THREE.Vector2(x, y)), 12));
  for (const [fx, fz, fs, fr] of FAR_CUPS) {   // one geometry, one material: they read as shapes, never as detail
    const m = new THREE.Mesh(farGeo, farMat);
    m.position.set(fx, 0, fz); m.scale.setScalar(fs); m.rotation.y = fr; scene.add(m);
  }
  const bubMat = keep(new THREE.MeshBasicMaterial({ color: PINK, transparent: true, opacity: 0.22, blending: THREE.AdditiveBlending, depthWrite: false, fog: false }));
  const bubbles = keep(new THREE.InstancedMesh(keep(new THREE.SphereGeometry(0.34, 6, 4)), bubMat, BUBBLE_N));
  bubbles.frustumCulled = false; scene.add(bubbles);
  const bub = [], _bm = new THREE.Matrix4(), _bs = new THREE.Vector3();
  for (let i = 0; i < BUBBLE_N; i++) bub.push({ x: -13 + ((i * 5.3) % 26), y: 0.5 + ((i * 1.9) % 4.6), z: -8 - ((i * 3.7) % 15), s: 0.55 + ((i * 7) % 5) * 0.3, ph: i * 1.9, rise: 0.14 + ((i * 3) % 4) * 0.05 });
  let bubDirty = true;
  /** The bubbles rise and sway behind the stage. Reduced motion holds them where they are (Law III). */
  function driftBubbles(dt) {
    if (reduced && !bubDirty) return;
    for (let i = 0; i < BUBBLE_N; i++) {
      const b = bub[i];
      if (!reduced) { b.y += dt * b.rise; if (b.y > 6.4) b.y = 0.4; }
      _bm.makeTranslation(b.x + (reduced ? 0 : Math.sin(t * 0.45 + b.ph) * 0.55), b.y, b.z);
      bubbles.setMatrixAt(i, _bm.scale(_bs.setScalar(b.s)));
    }
    bubbles.instanceMatrix.needsUpdate = true; bubDirty = false;
  }
  driftBubbles(0);
  // the checker floor (props.glb floor_tile replaces it): wide enough that the fog eats it before its edge
  let floor = new THREE.Mesh(keep(new THREE.PlaneGeometry(FLOOR_W, FLOOR_W)), keep(new THREE.MeshLambertMaterial({ map: keep(checkerTexture('#2b1f47', '#3a2a5e', FLOOR_W / 2)) })));
  floor.rotation.x = -Math.PI / 2; scene.add(floor);
  // the saucer podium she stands on
  const podium = new THREE.Group(); scene.add(podium);
  podium.add(lathe(PODIUM_PROFILE, porcelain, 48));
  const pring = new THREE.Mesh(keep(new THREE.TorusGeometry(1.04, 0.03, 8, 64)), glow); pring.rotation.x = Math.PI / 2; pring.position.y = PODIUM_H + 0.045; podium.add(pring);
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
      const n = FLOOR_N, half = (n - 1) / 2, im = new THREE.InstancedMesh(keep(tileGeo), keep(new THREE.MeshLambertMaterial({ vertexColors: true })), n * n), m = new THREE.Matrix4();
      let i = 0;
      for (let x = 0; x < n; x++) for (let z = 0; z < n; z++) im.setMatrixAt(i++, m.makeTranslation((x - half) * 2, 0, (z - half) * 2));
      floor = im; scene.add(im);
    }
    pixel.retexture(scene);
    if (log) log('[menu] props.glb dressed the stage');
  }).catch(() => { if (log) log('[menu] props.glb absent, placeholders stand'); });

  // ---- EMI: the pack clone, her own glass, the mixer ----
  const emi = { root: new THREE.Group(), model: null, mixer: null, actions: {}, busy: null, face: -1, ready: [],
    nextAt: BEAT_MIN + Math.random() * (BEAT_MAX - BEAT_MIN), lastPeek: -PEEK_GAP };
  emi.root.position.set(0, PODIUM_H, 0); scene.add(emi.root);
  // the idle show: flashes pop around her and she looks at them (race/menuFlashes.js). A flash beat
  // pushes the one-shot beat back so a wave never lands mid-glance; `ctx` is one object, reused.
  const flashCtx = { t: 0, mode: 'menu', reduced: false, busy: null };
  const flashes = createMenuFlashes({
    scene, emiRoot: emi.root, baseY: PODIUM_H, faces: character.faces, setFace: (i) => face(i), log,
    holdBeat: (sec) => { emi.nextAt = Math.max(emi.nextAt, t + sec); },
  });
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
        // the stage owns its glass: its own material AND its own copy of the atlas, so the frame
        // it sits on never moves under the run's rig (which shares the cached pack's texture)
        const c = keep(m.clone());
        for (const k of ['map', 'emissiveMap']) if (c[k]) {
          const tx = keep(c[k].clone()); tx.wrapS = THREE.RepeatWrapping; tx.magFilter = tx.minFilter = THREE.NearestFilter; tx.generateMipmaps = false; tx.needsUpdate = true;
          c[k] = tx;
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
    flashes.attach(model, pack.animations);
    for (const cb of emi.ready) { try { cb(model); } catch (err) { /* a listener never breaks the stage */ } }
    emi.ready.length = 0;
    if (log) log(`[menu] ${character.id} on stage: ${Object.keys(emi.actions).join(' ')}`);
  }).catch((e) => { if (log) log('[menu] emi.glb failed: ' + ((e && e.message) || e)); });

  // one hop through gltf.js setFace, so the frame count comes off the atlas that actually loaded
  // (the glb's own strip is narrower than the live one) instead of being spelled out twice
  function face(i) {
    i = FACE_PIN >= 0 ? FACE_PIN : clamp(i | 0, 0, FACES.length - 1);
    if (i === emi.face || !emi.model) return;
    emi.face = i; setFace(emi.model, i);
  }
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
    flashes.release();          // a one-shot or a peek takes her: the glance goes home the same way it would have

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
    if (flashes.frozen) return;   // the `?freeze` screenshot aid holds the whole stage on one frame
    t += dt;
    if (emi.mixer) emi.mixer.update(dt);
    if (mode === 'menu' && !reduced && !emi.busy && !flashes.pending && t >= emi.nextAt) play(ONE_SHOTS[Math.floor(Math.random() * ONE_SHOTS.length)]);
    flashCtx.t = t; flashCtx.mode = mode; flashCtx.reduced = reduced; flashCtx.busy = emi.busy;
    flashes.update(dt, flashCtx);   // after the mixer: the antenna offset rides on top of the clip
    sky.offset.x += dt * 0.004;    // scenery drifts; EMI is the one breath (Law III)
    driftBubbles(dt);
    if (ripple.material.opacity > 0) { ripple.scale.addScalar(dt * 4); ripple.material.opacity = Math.max(0, ripple.material.opacity - dt * 1.6); }
  }
  function render() { pixel.render(scene, camera); }
  function resize(w, h) {
    view.w = Math.max(1, w | 0); view.h = Math.max(1, h | 0);
    camera.aspect = view.w / view.h;
    camera.fov = vFovForAspect(STAGE_FOV, camera.aspect, STAGE_MAX_VFOV);
    applyView();
  }
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
    flashes.dispose();
    if (emi.mixer) emi.mixer.stopAllAction();
    for (const x of own) { try { x.dispose(); } catch (e) { /* shared or gone */ } }
    scene.clear();
  }
  return {
    scene, camera, podium, cup: { group: cup, body: cupBody, ripple: rippleTea },
    emi: { root: emi.root, play, face, peek, model: () => emi.model, busy: () => emi.busy, ready(cb) { if (emi.model) cb(emi.model); else emi.ready.push(cb); } },
    update, render, resize, setViewFraction, setMode(m) { mode = m; }, setReduced(v) { reduced = !!v; bubDirty = true; }, dispose,
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
  const ui = (n, v) => { try { if (audio && audio.ui) audio.ui(n, v); } catch (e) { /* audio gone */ } };   // the menu blips (race/audio.js)
  const theme = (on) => { try { if (audio && audio.menu) audio.menu(on); } catch (e) { /* audio gone */ } };

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
  // A phone has no keys and no pad, so on glass the THUMB rows come first and say what
  // the layer race/touch.js builds actually does. Same test that layer uses, `?touch=1`
  // and `?touch=0` included, so the headless shot can ask for either card.
  el('h3', 'rm-h', howPanel, 'how to drive');
  if (wantsTouch()) {
    el('div', 'rm-sub', howPanel, 'with a thumb');
    for (const [k, v] of [
      ['left side', 'drag it. wherever your thumb lands is the wheel.'],
      ['double tap', 'jump. two quick taps, anywhere on the glass.'],
      ['right side', 'hold to drift, let go for turbo. one tap there jumps too.'],
      ['use', 'the round button by the gauge spends your item.'],
      ['ii / sound', 'pause and mute, top right.'],
    ]) {
      const row = el('div', 'rm-key', howPanel); el('kbd', '', row, k); el('span', '', row, v);
    }
    el('div', 'rm-row-hint', howPanel, 'you are always going. there is no pedal on glass.');
    el('div', 'rm-sub', howPanel, 'or keys and a pad');
  }
  for (const [k, v] of [['arrows / wasd', 'steer, throttle, brake'], ['shift', 'drift (hold, let go for turbo)'], ['space', 'jump (time it at a ramp for big air)'], ['e', 'use the item'], ['p', 'pixel look'], ['m', 'mute'], ['esc', 'the brake'], ['pad', 'stick steers, rt goes, a drifts, b jumps, x item, start brakes']]) {
    const row = el('div', 'rm-key', howPanel); el('kbd', '', row, k); el('span', '', row, v);
  }
  el('div', 'rm-hint', howPanel, 'nothing is lost. you cannot fail.');
  // the way out for a finger: the keyboard has esc and enter, a phone has this row and the backdrop
  const howBack = el('button', 'rm-btn rm-how-back', howPanel, 'back'); howBack.type = 'button'; howBack.setAttribute('role', 'menuitem');
  howBack.addEventListener('click', (e) => { e.stopPropagation(); ui('back'); hit(howBack, 'is-hit'); open('main'); });

  // ---- options: each row is a value with left / right and a press ----
  const pct = (v) => `${Math.round(v * 100)}%`;
  const step = (k, d) => { options[k] = clamp(Math.round((options[k] + d) * 10) / 10, 0, 1); applyLevels(); };
  const cyc = (arr, v, d) => arr[(arr.indexOf(v) + d + arr.length) % arr.length];
  function applyLevels() { if (canLevels) { try { audio.setLevels({ music: options.music, sfx: options.sfx }); } catch (e) { /* audio gone */ } } }
  function setPixel(n) { pixel.setBlock(n); pixel.retexture(stage.scene); options.pixel = pixel.block; }
  const ROWS = [
    { id: 'pixel', label: 'pixel block', get: () => (pixel.block ? `${pixel.block} px` : 'off'), move: (d) => setPixel(cyc(PIXEL_STEPS, pixel.block, d)) },
    { id: 'music', label: 'music', get: () => pct(options.music), move: (d) => step('music', d * 0.1), dim: !canLevels },
    { id: 'sfx', label: 'sfx', get: () => pct(options.sfx), move: (d) => step('sfx', d * 0.1), dim: !canLevels },
    { id: 'motion', label: 'reduced motion', get: () => options.motion, move: (d) => { options.motion = cyc(MOTIONS, options.motion, d); stage.setReduced(reduced()); layer.dataset.motion = options.motion; }, hint: 'stage and intro now, the run on the next launch' },
    { id: 'seed', label: 'seed', get: () => (options.seed === 'custom' ? `custom ${options.seedValue}` : options.seed), move: (d) => { options.seed = cyc(SEEDS, options.seed, d); seedIn.hidden = options.seed !== 'custom'; }, press: () => { if (options.seed === 'custom') seedIn.focus(); } },
    { id: 'back', label: 'back', get: () => '', press: () => open('main') },
  ];
  el('h3', 'rm-h', optPanel, 'options');
  const rowEls = ROWS.map((r) => {
    const row = el('div', `rm-row${r.dim ? ' is-dim' : ''}`, optPanel); row.dataset.id = r.id; row.setAttribute('role', 'menuitem');
    el('span', 'rm-row-label', row, r.label);
    // the value wears its own left / right buttons: a tap on the number alone can only ever step ONE
    // way (the pad's press), so music and sfx would ratchet up and never come down on a phone.
    const ctl = el('div', 'rm-row-ctl', row);
    const arrow = (dir, cls, glyph, label) => {
      const b = el('button', `rm-row-arrow ${cls}`, ctl, glyph); b.type = 'button'; b.tabIndex = -1;
      b.setAttribute('aria-label', `${label} ${r.label}`);
      b.addEventListener('click', (e) => { e.stopPropagation(); focusRow(ROWS.indexOf(r)); act(dir); });
      return b;
    };
    if (r.move) arrow('left', 'is-dec', '‹', 'lower');
    r.valEl = el('span', 'rm-row-val rh-num', ctl, r.get());
    if (r.move) arrow('right', 'is-inc', '›', 'raise');
    if (r.hint) el('span', 'rm-row-hint', row, r.hint);
    row.addEventListener('click', (e) => { e.stopPropagation(); focusRow(ROWS.indexOf(r)); act('press'); });
    row.addEventListener('pointerenter', () => focusRow(ROWS.indexOf(r)));
    return row;
  });
  const seedIn = el('input', 'rm-seed', optPanel); seedIn.type = 'number'; seedIn.min = '1'; seedIn.step = '1'; seedIn.value = String(options.seedValue); seedIn.hidden = options.seed !== 'custom';
  seedIn.addEventListener('input', () => { const v = Number(seedIn.value); if (isFinite(v) && v > 0) { options.seedValue = v >>> 0; refresh(); } });
  seedIn.addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === 'Escape') { e.preventDefault(); seedIn.blur(); } e.stopPropagation(); });

  // ---- the verbs. `track` only under a host (the file dialog is its), `clear` only once a file is in ----
  const canTrack = !!settings.trackPick;
  const VERBS = [['race', 'race'], ['track', 'load a track'], ['clear', 'just the road'], ['options', 'options'], ['how', 'how to drive'], ['story', 'the story'], ['surface', 'surface']];
  const verbEls = VERBS.map(([id, label], i) => {
    const b = el('button', 'rm-btn', list, label); b.type = 'button'; b.dataset.id = id; b.setAttribute('role', 'menuitem');
    b.addEventListener('click', (e) => { e.stopPropagation(); idx.main = i; act('press'); });
    b.addEventListener('pointerenter', () => { idx.main = i; ui('tick'); refresh(); });
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
  let panel = 'main', shown = false, disposed = false, viewHeld = false;
  const idx = { main: 0, options: 0 };
  function open(p) { panel = p; list.hidden = p !== 'main'; optPanel.hidden = p !== 'options'; howPanel.hidden = p !== 'how'; if (p === 'options') idx.options = 0; refresh(); if (p !== 'options') seedIn.blur(); }
  function focusRow(i) { idx.options = clamp(i, 0, ROWS.length - 1); ui('tick'); refresh(); }
  function refresh() {
    verbEls.forEach((b, i) => b.classList.toggle('is-focus', panel === 'main' && i === idx.main));
    ROWS.forEach((r, i) => { const v = r.get(); if (r.valEl.textContent !== v) r.valEl.textContent = v; rowEls[i].classList.toggle('is-focus', panel === 'options' && i === idx.options); });
  }
  function pick(id) { for (const cb of picks) { try { cb(id); } catch (e) { /* a listener never breaks the menu */ } } }
  function act(what) {
    if (!shown || disposed) return;
    if (panel === 'main') {
      if (what === 'up' || what === 'down') { idx.main = stepVerb(idx.main, what === 'up' ? -1 : 1); ui('tick'); refresh(); return; }
      if (what !== 'press') return;
      const id = VERBS[idx.main][0];
      if (verbEls[idx.main].hidden) return;
      hit(verbEls[idx.main], 'is-hit'); ui('pick');
      if (id === 'options' || id === 'how') open(id); else pick(id);
      return;
    }
    if (what === 'back') { ui('back'); open('main'); return; }
    if (panel === 'how') { if (what === 'press') open('main'); return; }
    const r = ROWS[idx.options];
    if (what === 'up' || what === 'down') { focusRow(idx.options + (what === 'up' ? -1 : 1)); return; }
    if ((what === 'left' || what === 'right') && r.move) { r.move(what === 'left' ? -1 : 1); ui('step', r.id === 'music' || r.id === 'sfx' ? options[r.id] : 0.5); hit(rowEls[idx.options], 'is-hit'); }
    else if (what === 'press') { if (r.press) r.press(); else if (r.move) r.move(1); ui(r.id === 'back' ? 'back' : 'pick'); hit(rowEls[idx.options], 'is-hit'); }
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
  /** menu.css collapses to one column at ONE_COL_Q and hides `.rm-stage`; the canvas keeps rendering
   *  behind it, so there is no right-hand column to push her into and the offset would only shove her
   *  off the side of a phone. One column = dead centre, and she reads as the backdrop she now is. */
  const oneColumn = () => !!(typeof matchMedia === 'function' && matchMedia(ONE_COL_Q).matches);
  function onResize() {
    const w = root.clientWidth || window.innerWidth, h = root.clientHeight || window.innerHeight;
    stage.resize(w, h);
    // a hair right of the visible centre: room for the cup. `viewHeld` keeps that framing while the
    // menu is hidden but something else owns the same column (race/cards.js), so a late resize does
    // not walk her back to the middle of the frame.
    const parked = (shown || viewHeld) && !oneColumn();
    stage.setViewFraction(parked ? ((col.offsetWidth || w * 0.38) / w + 1) / 2 + 0.03 : 0.5);
  }
  const onPointer = (e) => { if (shown && e.clientX > window.innerWidth * 0.5) stage.emi.peek(); };
  // the backdrop closes the key card. Every verb and every row stops its own click, so the only
  // clicks that reach the layer are the ones that landed on nothing.
  const onBackdrop = (e) => { if (shown && panel === 'how' && !howPanel.contains(e.target)) { ui('back'); open('main'); } };
  layer.addEventListener('click', onBackdrop);
  window.addEventListener('keydown', onKey);
  const unbindResize = bindViewportResize(onResize);
  stageEl.addEventListener('pointerenter', onPointer);
  layer.dataset.motion = options.motion;

  const menu = {
    options, stage: { update(dt) { if (shown) pollPad(dt); stage.update(dt); }, render: stage.render, dispose: stage.dispose, live: stage },
    onPick(cb) { if (typeof cb === 'function') picks.push(cb); },
    setTrack, get track() { return trackState; },
    /** hideVerb(id): take a verb off the list for good. raceBoot hides `surface` when the page is not
     *  hosted and has nowhere to surface to, so nobody taps their way to a blank screen. */
    hideVerb(id) {
      const b = verbEl(id); if (!b) return;
      b.hidden = true;
      if (verbEls[idx.main].hidden) idx.main = stepVerb(idx.main, 1);
      refresh();
    },
    show() { shown = true; viewHeld = false; layer.hidden = false; stage.setMode('menu'); open(PANEL_PIN || 'main'); onResize(); hit(layer, 'is-in'); theme(true); },
    hide() { shown = false; layer.hidden = true; stage.setViewFraction(0.5); },
    /** Park the stage the way the column parks it, with the menu hidden: race/cards.js uses the same
     *  layout, so hide() sending her back to the middle of the frame would only be a jump and a jump
     *  back. The hold survives a resize; show() takes it off again. */
    refreshView() { viewHeld = true; onResize(); },
    refresh,
    dispose() {
      if (disposed) return;
      disposed = true; shown = false;
      window.removeEventListener('keydown', onKey); unbindResize();
      stage.dispose(); layer.remove();
    },
  };
  onResize();
  return menu;
}

// self-check: node --check is the bar; loadOptions / seedFromOptions are pure given a localStorage stub.
