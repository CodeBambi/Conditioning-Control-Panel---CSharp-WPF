/* ============================================================================
 * scene.js - the Daily Daze close-up in three.js, ported from blender-scripting
 * wheel/preview/app.js (drag with direction kept, exact landing, live slice
 * highlight, neon hub spiral, bulb halos). One WebGL context per createScene(),
 * freed by dispose(). Nodes by NAME only (nodes.js).
 *
 * Slices are rebuilt from GET state (wheel.js layoutOf): the glb's placeholder
 * layout_sectors and peg_* hide, the runtime ones carry each slice's printed pay
 * and label, so the picture always matches the table the server sent.
 *
 * The feel is decided in feel.js and played here: the pointer's peg kicks and the
 * slice highlight move at most 6 Hz (feel.tick), THE THUD on the landed slice,
 * THE SHIVER for Snooze, THE REVEAL for the pot, THE BREATH on the jackpot star
 * alone, and the 32 bulbs' heat per tier. Reduced motion (or Calm) takes the
 * settled state: no travel, the landing is simply there (Law VI).
 *
 * Hypno v3 (CONTRACT 10.13.F), the curves in hypno.js: the Loom hub (a disc on
 * the rotor fed by the kit through o.paintHub, a brass star with spiral off),
 * the long last turn (the landing plan's clock warped under 1.6 rad/s, the dim
 * reported through o.onFrame), the quiet room, and at Full the moire rim and
 * the taffy slices (a vertex twist plus four smear ghosts). The fullscreen set
 * is the host's: this file never fires an fx.
 * ==========================================================================*/

import * as THREE from 'three';
import { createRimGlitter } from './rim-glitter.js';
import { createRenderBudget } from '../../room/render-budget.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { REQUIRED, OPTIONAL } from './nodes.js';
import { TAU, sliceAt, planLanding, rotationAt } from './wheel.js';
import { startRecoil } from './juice.js';
import { FEEL, tick, breath, shiverPx, bezier } from './feel.js';
import { warpStep, warpedRotation, stepDim, quietMix, quietDone, outlineAlpha, distance01, mixRgb, QUIET, taffyShear, stepShear,
         trailOffset, sliceU, ghostRotations, TAFFY, stepHub, moireRotations, moireSegments, MOIRE, dressOf } from './hypno.js';
import { createEmi } from './emi.js';
import { createRoomEmi } from './room-emi.js';
import { createRoomReward } from './room-reward.js';
import { createPrizeSector, prizeColor } from './prize-art.js';
import { drawFace, faceKeys, FACE_MS, R0, R1 } from './slice-art.js';
import { HIGHLIGHT_MS } from '../../shared/hypno/callout.js';

const LENS_DEG = 30, FIT = 1.16, RISE_MS = 620, SINK_MS = 300, DEFAULT_OMEGA = -0.011, HUB_AT_REST_MS = 33;
const COLORS = ['#F7BDD2', '#FFEBDD', '#F2AFC9', '#FFE7D2'], GOLD = '#F4D896', SNOOZE = '#C48CA7';
const CHASE = [0xff328f, 0x963cff, 0x29cfff, 0x36ffc2, 0xffc329, 0xff6742].map(c => new THREE.Color(c));
const asset = p => new URL(p, import.meta.url).href;
const clamp = x => Math.min(1, Math.max(0, x));
const thudEase = x => bezier(FEEL.THUD_EASE, x);

function canvasTexture(c, flipY = true) {
  const t = new THREE.CanvasTexture(c);
  Object.assign(t, { colorSpace: THREE.SRGBColorSpace, flipY, generateMipmaps: false, minFilter: THREE.LinearFilter, magFilter: THREE.LinearFilter });
  return t;
}
function haloTexture() {
  const c = document.createElement('canvas'); c.width = c.height = 96;
  const g = c.getContext('2d'), grad = g.createRadialGradient(48, 48, 0, 48, 48, 48);
  [[0, '#ffffff70'], [0.17, '#ffffff60'], [0.5, '#ffffff30'], [1, '#ffffff00']].forEach(([o, col]) => grad.addColorStop(o, col));
  g.fillStyle = grad; g.fillRect(0, 0, 96, 96);
  return canvasTexture(c);
}
/** Law 3 on the GPU: every slice vertex turns about the hub by trailOffset (a = -brShear x u^1.25), normals too. */
const TWIST_R0 = 0.185, TWIST_SPAN = 0.711 - 0.185;
function twist(material, uniform) {
  material.onBeforeCompile = sh => {
    sh.uniforms.brShear = uniform;
    const head = `uniform float brShear;
      vec2 brTwist(vec2 p, vec2 v) { float u = clamp((length(p) - ${TWIST_R0.toFixed(3)}) / ${TWIST_SPAN.toFixed(3)}, 0.0, 1.0);
        float a = -brShear * pow(u, ${TAFFY.curve.toFixed(2)}); float c = cos(a), s = sin(a); return mat2(c, s, -s, c) * v; }`;
    sh.vertexShader = sh.vertexShader.replace('#include <common>', '#include <common>\n' + head)
      .replace('#include <begin_vertex>', '#include <begin_vertex>\n transformed.xy = brTwist(position.xy, transformed.xy);');
    if (sh.vertexShader.includes('#include <beginnormal_vertex>')) {
      sh.vertexShader = sh.vertexShader.replace('#include <beginnormal_vertex>', '#include <beginnormal_vertex>\n objectNormal.xy = brTwist(position.xy, objectNormal.xy);');
    }
  };
  material.customProgramCacheKey = () => 'br-wheel-twist';
  return material;
}
/** A sector of the runtime slice ring as 2D points (clockwise-from-top angles, as the slices are built). */
function sectorPoints(a0, a1, r0, r1) {
  const n = Math.max(3, Math.ceil((a1 - a0) * 32)), pts = [];
  for (let j = 0; j <= n; j++) { const a = a0 + ((a1 - a0) * j) / n; pts.push(new THREE.Vector2(Math.sin(a) * r1, Math.cos(a) * r1)); }
  for (let j = n; j >= 0; j--) { const a = a0 + ((a1 - a0) * j) / n; pts.push(new THREE.Vector2(Math.sin(a) * r0, Math.cos(a) * r0)); }
  return pts;
}
/** The hub's brass star (spiral off, 10.13.A), painted once into the hub canvas. */
function paintStar(c) {
  const g = c.getContext('2d'), w = c.width, h = c.height;
  g.fillStyle = '#2a1a3c'; g.fillRect(0, 0, w, h);
  g.fillStyle = '#e8c27a'; g.beginPath();
  for (let i = 0; i < 10; i++) { const r = (i % 2 ? 0.2 : 0.44) * w, a = -Math.PI / 2 + (i * Math.PI) / 5; g.lineTo(w / 2 + Math.cos(a) * r, h / 2 + Math.sin(a) * r); }
  g.closePath(); g.fill();
}

function paintScreen(canvas, text, gold) {
  const g = canvas.getContext('2d'), { width: w, height: h } = canvas;
  const grad = g.createLinearGradient(0, 0, w, h);
  grad.addColorStop(0, '#180d27'); grad.addColorStop(0.5, gold ? '#4a3418' : '#432040'); grad.addColorStop(1, '#180d27');
  g.fillStyle = grad; g.fillRect(0, 0, w, h);
  g.textAlign = 'center'; g.textBaseline = 'middle'; g.font = `700 ${Math.round(h * 0.52)}px Segoe UI, Arial, sans-serif`;
  g.shadowColor = gold ? '#ffcf6b' : '#ff75c8'; g.shadowBlur = 10; g.fillStyle = gold ? '#fff1c8' : '#ffe3f2';
  g.fillText(text, w / 2, h / 2, w * 0.9);
}

/**
 * @param {{canvas, hud, reduced (the travel flag at open; setReduced keeps it live), labels:(slice)=>{big,small}, canSpin:()=>boolean, onRelease:(omega)=>void,
 *          onGrab?:()=>void, onTick?:(semis)=>void, dress?:object (hypno.dressOf),
 *          paintHub?:(canvas, angle, now)=>boolean, onFrame?:({dim, slowing, speed, turning})=>void}} o
 */
export async function createScene(o) {
  const { canvas, stage } = o;
  const originals = new Map(); let model = null, unregister = null, rewardView = null;
  let reduced = !!o.reduced;
  const budget = createRenderBudget(navigator, devicePixelRatio);
  let lastDraw = -Infinity, rimGlitter = null;
  const renderer = stage?.renderer || new THREE.WebGLRenderer({ canvas, alpha: true, antialias: true, powerPreference: 'default' });
  if (!stage) renderer.setPixelRatio(budget.dpr(canvas.clientWidth, canvas.clientHeight));
  // Seated, the wheel borrows the room's renderer and inherits its tone mapping. This is the standalone path,
  // and it matches on purpose: otherwise dev.html and the in-room wheel disagree and the difference gets chased.
  if (!stage) { renderer.outputColorSpace = THREE.SRGBColorSpace; renderer.toneMapping = THREE.NeutralToneMapping; renderer.toneMappingExposure = 1.6; }
  const scene = stage?.scene || new THREE.Scene(), camera = stage?.camera || new THREE.PerspectiveCamera(LENS_DEG, 16 / 9, 0.05, 30);
  if (!stage) scene.add(new THREE.HemisphereLight(0xfbd7f4, 0x36243e, 1.2));
  if (!stage) for (const [p, c, i] of [[[-3, 4, 5], 0xffd5eb, 2.1], [[3, 2, 3], 0xa5b6ff, 1.4], [[1, 4, -3], 0xff75c1, 2.2]]) {
    const l = new THREE.DirectionalLight(c, i); l.position.set(...p); scene.add(l);
  }
  const owned = [];
  let raf = 0, disposed = false, drag = null, onDown, onMove, onUp;
  function cancelDrag() {
    const held=drag;drag=null;canvas.style.cursor='';
    if(held && canvas.hasPointerCapture(held.id)) canvas.releasePointerCapture(held.id);
  }
  function dispose() {
    if (disposed) return;
    disposed = true;
    cancelDrag();
    cancelAnimationFrame(raf);
    rewardView?.dispose();
    rimGlitter?.dispose();
    if (settle) settle();
    if (onDown) { canvas.removeEventListener('pointerdown', onDown); canvas.removeEventListener('pointermove', onMove); canvas.removeEventListener('pointerup', onUp); canvas.removeEventListener('pointercancel', onUp); }
    const disposable = [], resources = new Set(), borrowed = new Set();
    originals.forEach((s,n) => { if(n.geometry) borrowed.add(n.geometry); for(const m of [].concat(s.material||[])) { borrowed.add(m); for(const v of Object.values(m)) if(v?.isTexture) borrowed.add(v); } });
    (stage ? model : scene)?.traverse(n => { if (!stage || !originals.has(n)) disposable.push(n); });
    disposable.forEach(n => {
      if (n.geometry) resources.add(n.geometry);
      for (const m of [].concat(n.material || [])) { for (const v of Object.values(m)) if (v?.isTexture) resources.add(v); resources.add(m); }
    });
    if (stage) {
      disposable.forEach(n => n.removeFromParent());
      originals.forEach((s, n) => { n.visible = s.visible; n.material = s.material; n.scale.copy(s.scale); });
      const rotor = model?.getObjectByName('wheel_rotor'), pointer = model?.getObjectByName('pointer');
      if (rotor) rotor.rotation.copy(originals.get(rotor).rotation);
      if (pointer) pointer.rotation.copy(originals.get(pointer).rotation);
      if (originals.has(model)) model.rotation.copy(originals.get(model).rotation);
      unregister?.();
    }
    owned.forEach(x => resources.add(x));
    resources.forEach(x => { if(!borrowed.has(x)) x.dispose(); });
    if (emi) emi.dispose();
    if (!stage) { renderer.dispose(); renderer.forceContextLoss(); }
  }
  let settle = null, emi = null, gltf, atlas;
  try {
    [gltf, atlas] = stage ? [{ scene: stage.fixture }, null] : await Promise.all([new GLTFLoader().loadAsync(asset('./assets/wheel.glb')),
      new THREE.TextureLoader().loadAsync(asset('./assets/emi-faces-slot.png')).catch(() => null)]);
  } catch (e) { dispose(); throw e; }
  model = gltf.scene; const get = n => model.getObjectByName(n) || null;
  if (!stage) scene.add(model);
  else model.traverse(n => originals.set(n, { visible: n.visible, material: n.material, rotation: n.rotation.clone(), scale: n.scale.clone() }));
  const missing = REQUIRED.filter(n => !get(n));
  if (missing.length) return { missing, dispose };
  const absent = OPTIONAL.filter(n => !get(n));
  if (absent.length) console.warn(`[wheel] glb lacks optional nodes, degrading: ${absent.join(', ')}`);

  const rotor = get('wheel_rotor'), pointer = get('pointer'), pointerRest = pointer.rotation.z, modelRest = model.position.clone();
  if (get('room_wheel_face')) get('room_wheel_face').visible = false;
  if (get('wheel_rim_diamonds')) get('wheel_rim_diamonds').visible = false;
  if (get('layout_sectors')) get('layout_sectors').visible = false;
  model.traverse(n => { if (/^(peg_|glyph_)/.test(n.name)) n.visible = false; });
  for (const n of ['title_letters', 'status_letters', 'hub_spiral']) if (get(n)) get(n).visible = false;

  if (atlas) Object.assign(atlas, { flipY: false, colorSpace: THREE.SRGBColorSpace, minFilter: THREE.NearestFilter, magFilter: THREE.NearestFilter, generateMipmaps: false });
  owned.push(...(atlas ? [atlas] : []));
  emi = stage ? createRoomEmi(stage.emi, o.hud) : createEmi({ root: model, faceMesh: get('EMI_glass'), atlas, hud: o.hud, reduced });

  const screens = {};
  for (const [name, w] of [['title_screen', 1024], ['status_screen', 1024]]) {
    const mesh = get(name);
    if (!mesh || !mesh.isMesh) continue;
    // A face plane just proud of the screen's front, sized from its bounds: the screen's own UVs wrap the slab.
    mesh.geometry.computeBoundingBox();
    const bb = mesh.geometry.boundingBox, pw = (bb.max.x - bb.min.x) * 0.94, ph = (bb.max.y - bb.min.y) * 0.86;
    const c = document.createElement('canvas'); c.width = w; c.height = Math.round((w * ph) / pw);
    const t = canvasTexture(c);
    const face = new THREE.Mesh(new THREE.PlaneGeometry(pw, ph), new THREE.MeshBasicMaterial({ map: t, toneMapped: false }));
    face.position.set((bb.max.x + bb.min.x) / 2, (bb.max.y + bb.min.y) / 2, bb.max.z + 0.0015);
    mesh.add(face); owned.push(t);
    screens[name] = { c, t, mesh: face, text: null, gold: false };
  }
  function screen(name, text, gold = false) {
    const s = screens[name];
    if (!s || (s.text === text && s.gold === gold)) return;
    s.text = text; s.gold = gold; paintScreen(s.c, String(text), gold); s.t.needsUpdate = true;
  }

  // Bulbs with runtime halos; THE MARQUEE heat and gold.
  const halo = haloTexture(); owned.push(halo);
  const bulbs = [];
  model.traverse(n => { if (n.isMesh && /^bulb_\d+$/.test(n.name)) bulbs.push(n); });
  bulbs.sort((a, b) => a.name.localeCompare(b.name));
  const halos = bulbs.map(b => {
    b.material = b.material.clone(); owned.push(b.material);
    b.scale.multiplyScalar(1.45);
    const s = new THREE.Sprite(new THREE.SpriteMaterial({ map: halo, color: 0xffffff, transparent: true, opacity: 0.6, blending: THREE.AdditiveBlending, depthWrite: false }));
    s.position.copy(b.position); s.position.z += 0.038; s.scale.setScalar(0.24); b.parent.add(s);
    return s;
  });
  rimGlitter = createRimGlitter(rotor.parent, rotor.position, budget.mobile ? 80 : 144);
  // The Loom hub: a runtime disc on the rotor sized from hub_lip (else hub_spiral), a 256 CanvasTexture the kit paints.
  // It replaces the neon tube, which stays only when neither hub node exists.
  let dress = { ...dressOf(), ...(o.dress || {}) };
  const hubNode = get('hub_lip') || get('hub_spiral');
  let hub = null;
  const neon = { clock: { value: 0 }, energy: { value: 0 }, tint: { value: new THREE.Color(0xff72d1) } };
  if (hubNode) {
    rotor.updateMatrixWorld(true);
    const box = new THREE.Box3().setFromObject(hubNode).applyMatrix4(rotor.matrixWorld.clone().invert());
    const tube = hubNode.name === 'hub_lip' ? (box.max.z - box.min.z) / 2 : 0;
    const radius = Math.max(0.02, Math.min(box.max.x - box.min.x, box.max.y - box.min.y) / 2 - tube);
    const c = document.createElement('canvas'); c.width = c.height = 256;
    const tex = canvasTexture(c); owned.push(tex);
    // Mirror the arms locally; negate the painted angle below to preserve the turn.
    tex.repeat.x = -1; tex.offset.x = 1;
    const disc = new THREE.Mesh(new THREE.CircleGeometry(radius, 64), new THREE.MeshBasicMaterial({ map: tex, toneMapped: false }));
    disc.name = 'hub_loom'; disc.position.z = box.max.z + 0.0015; rotor.add(disc);
    hub = { c, tex, disc, radius, mode: null, rot: 0, direction: 1, at: -Infinity };
  } else {
    // The neon hub spiral (a flowing tube, scenery, not a breather).
    const pts = [];
    for (let i = 0; i <= 150; i++) { const u = i / 150, a = u * TAU * 2.2, r = 0.008 + 0.132 * u; pts.push(new THREE.Vector3(r * Math.cos(a), r * Math.sin(a), 0.181)); }
    rotor.add(new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 150, 0.011, 8, false), new THREE.ShaderMaterial({ uniforms: neon,
      vertexShader: 'varying vec2 vUv; void main(){vUv=uv;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}',
      fragmentShader: `varying vec2 vUv; uniform float clock; uniform float energy; uniform vec3 tint;
        void main(){float head=pow(.5+.5*cos(vUv.x*18.-clock),5.);vec3 c=mix(vec3(.12,.8,.92),tint,.5+.5*sin(vUv.x*9.-clock*.45));
        c=mix(c,vec3(1.,.88,1.),head*.35);gl_FragColor=vec4(c*(1.15+energy*.6+head*.45),1.);
        #include <tonemapping_fragment>
        #include <colorspace_fragment>
        }` })));
  }
  /** The hub disc on screen, client px (test seam for the on-screen handedness probe). */
  function hubScreen() {
    if (!hub) return null;
    const c = hub.disc.getWorldPosition(new THREE.Vector3()), e = hub.disc.localToWorld(new THREE.Vector3(hub.radius, 0, 0));
    const r = canvas.getBoundingClientRect(), px = v => { const q = v.clone().project(camera); return { x: r.left + ((q.x + 1) * r.width) / 2, y: r.top + ((1 - q.y) * r.height) / 2 }; };
    const pc = px(c), pe = px(e);
    return { x: pc.x, y: pc.y, r: Math.hypot(pe.x - pc.x, pe.y - pc.y) };
  }
  /** The hub's clockwise angle accumulates every frame; at rest it only drifts (0.35 rad/s), so it repaints every
   *  HUB_AT_REST_MS (30 Hz) and skips half the Loom renders and readbacks. */
  function paintHub(t, dtS) {
    if (!hub) return;
    const mode = dress.hub;
    if (rotating() && frameSpeed > 0.05) hub.direction = -Math.sign(omega);
    else if (!rotating()) hub.direction = 1;
    const advance = stepHub(0, frameSpeed, dtS, plan && plan.w ? plan.w.scale : 1, dress.k);
    hub.rot += hub.direction * advance;
    // Loom: the disc holds still against the rotor, so the field's own angle is its whole turn on screen.
    hub.disc.rotation.z = mode === 'loom' ? -rotor.rotation.z : 0;
    if (mode === 'star') { if (hub.mode !== 'star') { paintStar(hub.c); hub.tex.needsUpdate = true; } hub.mode = mode; return; }
    const fresh = hub.mode !== mode;
    hub.mode = mode;
    if (!fresh && !rotating() && t - hub.at < HUB_AT_REST_MS) return;
    hub.at = t;
    if (o.paintHub && o.paintHub(hub.c, -hub.rot, t)) hub.tex.needsUpdate = true;
  }

  // Moire rim: two rings of 60 fine lines just outside the slices, Full only.
  const moire = [[MOIRE.gold, MOIRE.goldAlpha], [MOIRE.mint, MOIRE.mintAlpha]].map(([color, opacity]) => {
    const g = new THREE.BufferGeometry(), seg = moireSegments(0.745, 0.79), pos = [];
    for (let i = 0; i < seg.length; i += 2) pos.push(seg[i], seg[i + 1], 0);
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    const l = new THREE.LineSegments(g, new THREE.LineBasicMaterial({ color, transparent: true, opacity, depthWrite: false, toneMapped: false }));
    l.position.copy(rotor.position); l.position.z += 0.1; l.visible = false; rotor.parent.add(l);
    return l;
  });

  // THE BREATH: the jackpot star's glow, the only breather on this screen (Law III).
  const star = get('star_mount'), starMat = star && star.isMesh ? (star.material = star.material.clone()) : null;
  if (starMat) { starMat.emissive = new THREE.Color(0xffc23a); owned.push(starMat); }
  const sparks = Array.from({ length: 7 }, () => {
    const s = new THREE.Sprite(new THREE.SpriteMaterial({ map: halo, color: 0xffd7a6, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false }));
    s.visible = false; s.scale.setScalar(0.07); model.add(s); return s;
  });

  // Runtime slices from the server's table.
  let layout = null, sliceGroup = null, ghosts = null, outline = null;
  const shearU = { value: 0 };
  /** THE PICTURE ON THE WEDGES (slice-art.js): one canvas over the whole ring, one texture, one draw call, and
   *  the same taffy twist as the enamel under it so the art smears with the slices instead of sliding over them.
   *  It carries no userData, so every per-frame loop below (the peg ring, the colour wash, the emissive pulse)
   *  skips it by the guards they already have. */
  let face = null;
  function buildFace(group) {
    const S = budget.mobile ? 512 : 768;
    const c = document.createElement('canvas'); c.width = c.height = S;
    const tex = canvasTexture(c);
    const mat = twist(new THREE.MeshBasicMaterial({ map: tex, transparent: true, depthWrite: false, toneMapped: false }), shearU);
    // A DECAL, and it is treated like one. The enamel's front face is at .076 + depth .018 + bevel .003 = .097,
    // so the first guess of .0955 put the art UNDER the wedge it was painted for and nothing showed. .0975 clears
    // it and still passes under the brass trim (.098) and the labels (.102), and the polygon offset keeps that
    // half-millimetre from z-fighting at a grazing angle.
    Object.assign(mat, { polygonOffset: true, polygonOffsetFactor: -1, polygonOffsetUnits: -1 });
    const mesh = new THREE.Mesh(new THREE.RingGeometry(R0, R1, 128, 1), mat);
    mesh.position.z = 0.0975;
    group.add(mesh);
    face = { c, tex, at: -Infinity, dirty: true, painted: false, key: null };
  }
  function setLayout(next) {
    if (sliceGroup) { rotor.remove(sliceGroup); sliceGroup.traverse(n => { if (n.geometry) n.geometry.dispose(); if (n.material) { if (n.material.map) n.material.map.dispose(); n.material.dispose(); } }); }
    if (ghosts) { ghosts.forEach(g => { rotor.parent.remove(g); g.material.dispose(); }); ghosts[0].geometry.dispose(); ghosts = null; }
    outline = null;
    layout = next; sliceGroup = new THREE.Group(); sliceGroup.name = 'runtime_sectors';
    const flat = [];
    for (const s of layout) {
      const { mesh, label, peg, border, pts, color } = createPrizeSector(s, o.labels(s), m => twist(m, shearU));
      flat.push({ pts, color });
      sliceGroup.add(mesh, label, peg, border);
    }
    face = null;   // the old canvas and texture went with sliceGroup's teardown above
    buildFace(sliceGroup);
    rotor.add(sliceGroup);
    under = sliceAt(layout, rotor.rotation.z).index; lit = under;
    // Taffy smear: the whole ring flattened into one vertex-coloured geometry, four ghosts at alpha 0.16 over the slices.
    const pos = [], col = [];
    for (const f of flat) {
      const tri = THREE.ShapeUtils.triangulateShape(f.pts, []), c = new THREE.Color(f.color);
      for (const face of tri) for (const i of face) { pos.push(f.pts[i].x, f.pts[i].y, 0); col.push(c.r, c.g, c.b); }
    }
    const gg = new THREE.BufferGeometry();
    gg.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3)); gg.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
    ghosts = Array.from({ length: TAFFY.ghosts }, () => {
      const m = new THREE.Mesh(gg, twist(new THREE.MeshBasicMaterial({ vertexColors: true, transparent: true, opacity: TAFFY.ghostAlpha,
        depthWrite: false, toneMapped: false, side: THREE.DoubleSide }), shearU));
      m.position.copy(rotor.position); m.position.z += 0.098; m.visible = false; m.renderOrder = 1; rotor.parent.add(m);
      return m;
    });
  }
  /** The landed slice's mint outline: a band just inside its edge (built on the landing, freed with the layout). */
  function outlineFor(index) {
    if (outline && outline.userData.index === index) return outline;
    if (outline) { sliceGroup.remove(outline); outline.geometry.dispose(); outline.material.dispose(); }
    const s = layout[index], gap = Math.min(0.003, s.span * 0.08), w = 0.012, inset = Math.min((s.span - 2 * gap) * 0.25, w / 0.5);
    const shape = new THREE.Shape(sectorPoints(s.start + gap, s.end - gap, 0.185, 0.711));
    shape.holes.push(new THREE.Path(sectorPoints(s.start + gap + inset, s.end - gap - inset, 0.185 + w, 0.711 - w)));
    outline = new THREE.Mesh(new THREE.ShapeGeometry(shape, 8), new THREE.MeshBasicMaterial({ color: 0x5fffd0, transparent: true, opacity: 0.6, depthWrite: false, toneMapped: false }));
    outline.position.z = 0.0985; outline.userData = { index, outline: true }; outline.visible = false; sliceGroup.add(outline);
    return outline;
  }

  // Framing: the bezel, both screens and EMI, straight on (cam_seat is on +Z).
  let play = null;
  function frame() {
    const box = new THREE.Box3();
    for (const n of ['bezel_lacquer', 'title_frame', 'status_frame', 'emi_topper', 'pointer']) if (get(n)) box.expandByObject(get(n));
    if (box.isEmpty()) box.setFromObject(model);
    const c = box.getCenter(new THREE.Vector3()), size = box.getSize(new THREE.Vector3()), tan = Math.tan(THREE.MathUtils.degToRad(LENS_DEG) / 2);
    const dist = Math.max(size.y / 2 / tan, size.x / 2 / (tan * camera.aspect)) * FIT + size.z / 2;
    return { pos: new THREE.Vector3(c.x, c.y, c.z + dist), look: c };
  }
  function resize() {
    if (stage) return;
    const w = canvas.clientWidth || 1, h = canvas.clientHeight || 1;
    renderer.setPixelRatio(budget.dpr(w, h));
    renderer.setSize(w, h, false); camera.aspect = w / h; camera.updateProjectionMatrix();
    play = frame();
    if (phase === 'play') { camera.position.copy(play.pos); camera.lookAt(play.look); }
  }

  // Motion state.
  let phase = 'hidden', tl = null, coast = null, plan = null, under = 0, lit = 0, crossAt = -Infinity, ladder = null;
  let recoilAt = -Infinity, recoilDirection = 1;
  const modelTilt = model.rotation.z;
  let landed = -1, thudAt = -Infinity, kickAt = -Infinity, kickSign = 1, kickAmp = 0, shiverAt = -Infinity;
  let hitAt = -Infinity, hitIndex = -1;   // THE GLYPH HIT: the landed slice's emissive pulse over HIGHLIGHT_MS (callout.js)
  let party = null, heat = 0, gold = false, energy = 0, lightMotion = 0, prev = performance.now();
  let dim = 0, slowing = false, shear = 0, omega = 0, lastRot = null, quietAt = -Infinity, quietK = 1, frameSpeed = 0;
  settle = () => { const a = tl, b = plan; tl = null; plan = null; if (a && a.done) a.done(); if (b && b.done) b.done(); };
  const rotating = () => !!(drag || coast || plan);

  /** The v3 page effects for one frame (CONTRACT 10.13.F): hub, taffy, moire, quiet room, and the dim for the station. */
  /** The wedge art, on the decoder's own clock (12 Hz). Still (Calm or reduced motion) paints the deck's first
   *  frame ONCE and then stops entirely: a held wheel is a wheel with a picture printed on it, not a slow one. */
  function paintFace(t) {
    if (!face || !layout) return;
    const media = typeof o.sliceMedia === 'function' ? o.sliceMedia() : null;   // read every paint: suspend frees it
    const still = reduced || !!dress.calm;
    if (!face.key) face.key = faceKeys(media, layout);
    if (media) media.tick(t, face.key || []);
    const state = media?.debug();
    const ready = state?.ready || 0, frames = state?.frames || 0;
    if (!face.dirty && still && face.painted && face.ready === ready && face.frames === frames && ready + (state?.failed || 0) === media?.size) return;
    if (!face.dirty && t - face.at < FACE_MS) return;
    face.at = t; face.dirty = false;
    face.ready = ready; face.frames = frames;
    if (drawFace(face.c, layout, media, face.key, prizeColor, still ? 0 : t)) { face.tex.needsUpdate = true; face.painted = true; }
  }
  function hypnoFrame(t, dtMs) {
    paintHub(t, dtMs / 1000);
    paintFace(t);
    const turning = rotating() && frameSpeed > 0.05;
    shear = stepShear(shear, dress.taffy && turning ? taffyShear(frameSpeed, dress.k) : 0, dtMs);
    const sign = omega < 0 ? -1 : 1;
    shearU.value = sign * shear;
    if (sliceGroup) {
      for (const n of sliceGroup.children) {
        const u = n.userData;
        if (!u || u.r === undefined) continue;
        const d = trailOffset(shear, sliceU(u.r), sign);   // anticlockwise radians; slice angles run clockwise
        n.position.x = Math.sin(u.a - d) * u.r; n.position.y = Math.cos(u.a - d) * u.r;
        if (u.spin !== undefined) n.rotation.z = u.spin + d;
      }
    }
    if (ghosts) {
      const on = !!dress.taffy && turning && shear > 0.02, rots = ghostRotations(rotor.rotation.z, omega);
      ghosts.forEach((g, i) => { g.visible = on; g.rotation.z = rots[i]; g.material.opacity = TAFFY.ghostAlpha * dress.k; });
    }
    const [m0, m1] = moireRotations(rotor.rotation.z);
    moire[0].visible = moire[1].visible = !!dress.moire; moire[0].rotation.z = m0; moire[1].rotation.z = m1;
    // Quiet room: every other slice greys, colour flows back by angular distance; the landed one keeps a mint outline.
    const since = (performance.now() - quietAt) / 1000;
    const quiet = !!layout && landed >= 0 && since >= 0 && !quietDone(since) && !rotating();
    if (sliceGroup) {
      for (const n of sliceGroup.children) {
        const u = n.userData;
        if (!u || u.outline) continue;
        if (u.base === undefined) { if (u.spin !== undefined) n.material.opacity = quiet && u.index !== landed ? 0.75 : 1; continue; }
        const q = quiet && u.index !== landed ? quietMix(since, distance01(u.mid, layout[landed].mid), quietK) : 0;
        const [r, g, b] = mixRgb(u.base, QUIET.grey, q);
        n.material.color.setRGB(r, g, b, THREE.SRGBColorSpace);
      }
      if (outline) {
        outline.visible = landed === outline.userData.index && !rotating() && Number.isFinite(quietAt);
        outline.material.opacity = outlineAlpha(Math.max(0, since), reduced);
      }
    }
    if (o.onFrame) o.onFrame({ dim, slowing, speed: frameSpeed, turning });
  }

  function update(t) {
    rewardView?.update(t);
    const dt = Math.min(0.05, Math.max(0, (t - prev) / 1000)); prev = t;
    const dtMs = dt * 1000;
    if (tl) {
      const q = clamp((t - tl.start) / tl.ms), k = tl.kind === 'rise' ? 1 - (1 - q) ** 3 : q * q;
      const back = play.pos.clone().add(new THREE.Vector3(0, 0.25, 1.4));
      camera.position.lerpVectors(tl.kind === 'rise' ? back : play.pos, tl.kind === 'rise' ? play.pos : back, k); camera.lookAt(play.look);
      if (q >= 1) { const done = tl.done; phase = tl.kind === 'rise' ? 'play' : 'hidden'; tl = null; done(); }
    }
    if (coast) rotor.rotation.z += coast.omega * dt * 1000;
    slowing = false;
    if (plan && plan.warp) {
      // The long last turn: the plan's own clock, read slower under 1.6 rad/s. Same path, same landing, same slice.
      plan.w = warpStep(plan.w, dtMs, plan, { calm: !!dress.calm }); slowing = plan.w.slowing;
      rotor.rotation.z = warpedRotation(plan, plan.w.elapsed);
      if (plan.w.elapsed >= plan.ms) { const p = plan; plan = null; rotor.rotation.z = p.to; if (p.done) p.done(); }
    } else if (plan) {
      rotor.rotation.z = rotationAt(plan, t - plan.start);
      if (t - plan.start >= plan.ms) { const p = plan; plan = null; rotor.rotation.z = p.to; if (p.done) p.done(); }
    }
    omega = lastRot === null || dt <= 0 ? 0 : (rotor.rotation.z - lastRot) / dt; lastRot = rotor.rotation.z;
    frameSpeed = Math.abs(omega);
    dim = reduced ? 0 : stepDim(dim, slowing, dtMs);
    if (layout) {
      const now = sliceAt(layout, rotor.rotation.z).index;
      if (now !== under) {
        const pn = performance.now(), gap = pn - crossAt; crossAt = pn; under = now;   // the cue clock, not the frame stamp
        const r = tick(ladder, pn, gap); ladder = r.ladder;
        if (r.play && rotating()) { lit = now; kickAt = t; kickAmp = 0.12; kickSign = Math.sign((coast && coast.omega) || (plan && plan.to - plan.from) || 1); if (o.onTick) o.onTick(r.semis); }
        else if (r.play) lit = now;
      }
      for (const m of sliceGroup.children) {
        if (m.userData.index === undefined || !m.material.emissive) continue;
        const i = m.userData.index, isLanded = i === landed && !rotating(), goal = isLanded ? 1 : i === lit && rotating() ? 0.68 : 0;
        m.userData.h = reduced ? goal : THREE.MathUtils.lerp(m.userData.h, goal, 1 - Math.exp(-dt * (goal > m.userData.h ? 45 : 10)));
        const tq = (t - thudAt) / FEEL.THUD_MS, flash = isLanded && tq >= 0 && tq < 1 ? (reduced ? 1.3 : 1 + 1.2 * (1 - thudEase(tq))) : 1;
        const hq = (t - hitAt) / HIGHLIGHT_MS, hit = i === hitIndex && hq >= 0 && hq < 1 ? 1 + 1.6 * Math.sin(hq * Math.PI) : 1;   // the rim glow, 0..400 ms
        m.material.emissiveIntensity = m.userData.h * flash * hit;
      }
    }
    model.rotation.z = modelTilt + startRecoil(t - recoilAt, recoilDirection, reduced || dress.calm);
    // Pointer: a damped kick per played tick, a knock on the landing (THE THUD). Reduced: at rest.
    pointer.rotation.z = pointerRest;
    if (!reduced) { const q = (t - kickAt) / 1000; if (q >= 0 && q < 0.6) pointer.rotation.z += kickSign * kickAmp * Math.exp(-18 * q) * Math.sin(q * 35); }
    // Lights: energy while turning, heat and gold while a party runs, smooth colour flow (never a strobe).
    const partying = !!party && t < party.end;
    const target = rotating() ? 1 : partying ? Math.min(1, 0.35 + heat * 0.18) : 0;
    energy = reduced ? target : THREE.MathUtils.lerp(energy, target, 1 - Math.exp(-dt * 5));
    if (!reduced) lightMotion += dt * (0.65 + energy * 3.2);
    const prize = gold && partying ? CHASE[4] : null;
    bulbs.forEach((b, i) => {
      const p = i * CHASE.length / bulbs.length + lightMotion * 0.35, j = Math.floor(p), c = CHASE[j % CHASE.length].clone().lerp(CHASE[(j + 1) % CHASE.length], p - j);
      if (prize) c.lerp(prize, 0.75);
      const wave = reduced ? 0.55 : Math.pow(0.5 + 0.5 * Math.cos((i / bulbs.length) * TAU * 3 - lightMotion * 2), 3);
      b.material.color.copy(c); b.material.emissive.copy(c); b.material.emissiveIntensity = 0.55 + wave * (0.55 + energy * 0.25);
      halos[i].material.color.copy(c); halos[i].material.opacity = 0.38 + wave * 0.36 + energy * 0.1; halos[i].scale.setScalar(0.30 + wave * 0.075);
    });
    rimGlitter.update(dt, reduced || !!dress.calm, energy, true, canvas.clientHeight || 720);
    neon.clock.value = lightMotion * 2; neon.energy.value = energy;
    if (starMat) starMat.emissiveIntensity = reduced ? 0.45 : partying || rotating() ? (gold && partying ? 1.4 : 0.2) : 0.2 + 0.55 * breath(t);
    sparks.forEach((s, i) => {
      const age = partying && party.sparks ? t - party.start - i * 55 : -1;
      s.visible = !reduced && !dress.calm && age >= 0 && age < 570 && !!star;
      if (!s.visible) return;
      const q = age / 570; star.getWorldPosition(s.position); model.worldToLocal(s.position); s.position.y += 0.58 + Math.sin(q * Math.PI) * 0.3; s.position.x += (i - 3) * 0.09 * q; s.position.z += 0.1 + q * 0.2;
    });
    if (party && t >= party.end) party = null;
    // THE SHIVER: the whole wheel, +-4 px across the screen. Reduced motion plays nothing.
    const px = reduced ? 0 : shiverPx(t - shiverAt);
    if (play) model.position.x = modelRest.x + px * ((2 * camera.position.distanceTo(play.look) * Math.tan(THREE.MathUtils.degToRad(LENS_DEG) / 2)) / (canvas.clientHeight || 1));
    hypnoFrame(t, dtMs);
    emi.update(t);
    const gap = 1000 / (budget.mobile ? 30 : 60);
    if (!stage && !document.hidden && t - lastDraw >= gap - 1) {
      renderer.render(scene, camera); lastDraw = t;
    }
  }
  let failed = false;
  const loop = t => { if (disposed) return; try { update(t); } catch (e) { if (!failed) console.error('[wheel] frame failed', e); failed = true; } raf = requestAnimationFrame(loop); };

  // Drag the rim (preview gesture): the wheel follows at once, a release past 0.1 rad flings it.
  const ray = new THREE.Raycaster(), ndc = new THREE.Vector2();
  function hit(e) {
    const r = canvas.getBoundingClientRect();
    ndc.set(((e.clientX - r.left) / r.width) * 2 - 1, 1 - ((e.clientY - r.top) / r.height) * 2);
    ray.setFromCamera(ndc, camera);
    return sliceGroup ? (stage ? stage.pick(e, sliceGroup.children.filter(n => n.userData.base !== undefined)) : ray.intersectObjects(sliceGroup.children.filter(n => n.userData.base !== undefined), false)).length > 0 : false;
  }
  function angleOf(e) {
    const v = rotor.getWorldPosition(new THREE.Vector3()).project(camera), r = canvas.getBoundingClientRect();
    return Math.atan2(-(e.clientY - (r.top + ((1 - v.y) * r.height) / 2)), e.clientX - (r.left + ((v.x + 1) * r.width) / 2));
  }
  onDown = e => {
    if (phase !== 'play' || rotating() || !e.isPrimary || e.button !== 0 || !hit(e)) return;
    if (!o.canSpin()) { if (o.onGrab) o.onGrab(false); return; }
    e.preventDefault(); canvas.setPointerCapture(e.pointerId);
    drag = { id: e.pointerId, last: angleOf(e), at: performance.now(), travel: 0, omega: 0, intent: 0, sign: -1 };
    canvas.style.cursor = 'grabbing';
    if (o.onGrab) o.onGrab(true);
  };
  onMove = e => {
    if (!drag || drag.id !== e.pointerId) return;
    const a = angleOf(e), now = performance.now(), d = Math.atan2(Math.sin(a - drag.last), Math.cos(a - drag.last)), dtm = Math.max(1, now - drag.at);
    rotor.rotation.z += d; drag.travel += Math.abs(d); drag.last = a; drag.at = now;
    drag.omega = THREE.MathUtils.lerp(drag.omega, d / dtm, 0.4);
    drag.intent = THREE.MathUtils.clamp(drag.intent + d, -0.12, 0.12);
    if (Math.abs(drag.intent) > 0.025) drag.sign = Math.sign(drag.intent);
  };
  onUp = e => {
    if (!drag || drag.id !== e.pointerId) return;
    if(e.type==='pointercancel'){cancelDrag();return;}
    const d = drag; drag = null; canvas.style.cursor = '';
    if (canvas.hasPointerCapture(d.id)) canvas.releasePointerCapture(d.id);
    // Drag strength changes the picture only: the speed is clamped, the landing is the server's.
    if (e.type === 'pointerup' && d.travel > 0.1) o.onRelease(d.sign * THREE.MathUtils.clamp(Math.abs(d.omega), 0.006, 0.03));
  };
  canvas.addEventListener('pointerdown', onDown); canvas.addEventListener('pointermove', onMove);
  canvas.addEventListener('pointerup', onUp); canvas.addEventListener('pointercancel', onUp);

  if(stage) rewardView=createRoomReward(stage,()=>hub?.mode==='loom'?hub.c:null);
  resize();
  if (stage) unregister = stage.register({ update: () => update(performance.now()), dispose });
  else raf = requestAnimationFrame(loop);

  return {
    missing: [], faceImage: atlas && atlas.image,
    get phase() { return phase; },
    get rotation() { return rotor.rotation.z; },
    get spinning() { return rotating(); },
    resize, screen, setLayout, dispose,
    revealReward(result, still = reduced) { return rewardView?.reveal(result, still) || false; },
    /** Live dress (hypno.dressOf): the hub mode, Full-only moire and taffy, k. */
    setDress(d) { dress = { ...dress, ...(d || {}) }; if (face) face.dirty = true; },
    /** Live reduced motion / Calm (a settings frame): the next gesture, landing, rise or sink takes the settled
     *  state. A landing already in flight finishes its path (no jump mid-turn); the stage dim and EMI settle now. */
    setReduced(on) { reduced = !!on; emi.setReduced(reduced); rewardView?.setStill(reduced); if (reduced) dim = 0; if (face) face.dirty = true; },
    /** The slice's own colour, for the landing wash. */
    sliceColor(index) {
      const m = sliceGroup && sliceGroup.children.find(n => n.userData && n.userData.base !== undefined && n.userData.index === index);
      return m ? m.userData.base : '#9b6bff';
    },
    /** Quiet room from this frame (a page effect of the landing moment), at strength k. */
    quiet(k = 1) { if (landed < 0 || !layout) return; quietK = k; quietAt = performance.now(); outlineFor(landed); },
    setFace: name => emi.setFace(name),
    setMood: m => emi.setMode(m),
    /** Put the rotor at `r` with no travel (a replay, a reduced landing). */
    setRotation(r, landedIndex = -1) { coast = null; plan = null; rotor.rotation.z = r; lastRot = r; landed = landedIndex; quietAt = -Infinity; if (layout) { under = sliceAt(layout, r).index; lit = under; } },
    /** Law VIII: the wheel starts turning this frame, before the server answers. */
    coast(omega = DEFAULT_OMEGA) { if (reduced) return; landed = -1; quietAt = -Infinity; ladder = null; plan = null; coast = { omega }; recoilAt = performance.now(); recoilDirection = Math.sign(omega); },
    /** Retarget the coast (or a rest) onto `landing`; resolves when the pointer settles. Reduced: at once. */
    land(landing, index) {
      const omega = coast ? coast.omega : DEFAULT_OMEGA;
      coast = null;
      if (reduced) { this.setRotation(landing, index); thudAt = performance.now(); return Promise.resolve(); }
      return new Promise(done => { plan = { ...planLanding({ from: rotor.rotation.z, omega, landing }), start: performance.now(), warp: true, w: null, done: () => { landed = index; thudAt = performance.now(); kickAt = thudAt; kickAmp = 0.2; done(); } }; });
    },
    /** No result came: the coast winds down where it is, nothing is lit. */
    windDown() {
      if (!coast) return Promise.resolve();
      const omega = coast.omega, from = rotor.rotation.z; coast = null;
      return new Promise(done => { plan = { from, to: from + (omega * 700) / 4, ms: 700, sign: Math.sign(omega), start: performance.now(), done }; });
    },
    /** THE GLYPH HIT (callout.js): the landed slice's emissive pulses over HIGHLIGHT_MS from this frame. */
    hit(index) { hitIndex = index; hitAt = performance.now(); },
    /** One celebration per landing (feel.recipe). */
    celebrate(r) {
      const t = performance.now();
      heat = r.heat; gold = !!r.gold;
      if (r.shiver) shiverAt = t;
      party = { start: t, end: t + r.partyMs, sparks: !!r.sparks };
      emi.setMode(r.tier === 4 ? 'jackpot' : r.tier > 0 ? 'win' : 'sleepy');
    },
    /** Law VI: Back and suspend skip every ceremony to its settled state. */
    skip() {
      cancelDrag();
      rewardView?.skip();
      if (plan) { const p = plan; plan = null; rotor.rotation.z = p.to; if (p.done) p.done(); }
      recoilAt = -Infinity; model.rotation.z = modelTilt;
      coast = null; party = null; shiverAt = thudAt = kickAt = hitAt = -Infinity; energy = 0; dim = 0; shear = 0; emi.skip();
    },
    /** A point in client px: 'landed' is the landed slice's printed face, else a node's centre. */
    project(name) {
      let p;
      if (name === 'landed') p = model.localToWorld(new THREE.Vector3(rotor.position.x, rotor.position.y + 0.5, 0.1));   // the face under the pointer
      else { const n = get(name); if (!n) return null; p = new THREE.Box3().setFromObject(n).getCenter(new THREE.Vector3()); }
      const v = p.project(camera), r = canvas.getBoundingClientRect();
      return { x: r.left + ((v.x + 1) * r.width) / 2, y: r.top + ((1 - v.y) * r.height) / 2 };
    },
    rise() {
      if (stage) { phase = 'play'; return Promise.resolve(); }
      if (reduced) { phase = 'play'; camera.position.copy(play.pos); camera.lookAt(play.look); return Promise.resolve(); }
      phase = 'rise';
      return new Promise(done => { tl = { kind: 'rise', start: performance.now(), ms: RISE_MS, done }; });
    },
    sink() {
      cancelDrag();
      if (stage) { phase = 'hidden'; return Promise.resolve(); }
      if (reduced || phase === 'hidden') { phase = 'hidden'; return Promise.resolve(); }
      const prevTl = tl; phase = 'sink';
      return new Promise(done => { tl = { kind: 'sink', start: performance.now(), ms: SINK_MS, done: () => { done(); if (prevTl && prevTl.done) prevTl.done(); } }; });
    },
    debug() {
      return { rotation: rotor.rotation.z, under: layout ? layout[under].id : null, lit: layout ? layout[lit].id : null, landed: landed >= 0 && layout ? layout[landed].id : null,
               coasting: !!coast, planning: !!plan, dragging: !!drag, energy, heat, gold, party: !!party, face: emi.face, mood: emi.mode,
               face: face ? { painted: face.painted, at: face.at, size: face.c.width, key: face.key } : null,
               pointer: pointer.rotation.z - pointerRest, shiverPx: reduced ? 0 : shiverPx(performance.now() - shiverAt),
               hit: hitIndex >= 0 && layout ? { slice: layout[hitIndex].id, on: performance.now() - hitAt < HIGHLIGHT_MS } : null,
               star: starMat ? starMat.emissiveIntensity : null, calls: renderer.info.render.calls,
               screens: Object.fromEntries(Object.entries(screens).map(([k, v]) => [k, v.text])),
               hypno: { hub: hub ? hub.mode : 'neon', hubRadius: hub ? hub.radius : null, hubRot: hub ? hub.rot : null, dim, slowing, shear, speed: frameSpeed,
                        timeScale: plan && plan.w ? plan.w.scale : 1, ghosts: !!(ghosts && ghosts[0].visible), moire: moire[0].visible,
                        quiet: Number.isFinite(quietAt) ? (performance.now() - quietAt) / 1000 : null,
                        outline: !!(outline && outline.visible), dress, hubScreen: hubScreen() } };
    },
  };
}
