/* ============================================================================
 * smoke/whip-preview.mjs - build the phone preview of the bishop's whip.
 *
 *   node smoke/whip-preview.mjs --out <html path>
 *
 * Writes ONE self-contained HTML page: three from the allowed CDN, the real
 * bishop and pawn glbs inlined as base64, and board/whip.js and board/jiggle.js
 * inlined VERBATIM (exports stripped, nothing else touched), so the curve the
 * page plays is the curve the game plays. A Replay button, a speed slider
 * (0.25x to 1x), an orbitable camera, and the beat clock in the corner.
 *
 * What it does NOT do: touch the game. It is a viewer for whip.js, and the
 * only thing it knows about anim.js is the tumble it re-does in forty lines.
 * ==========================================================================*/

import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, '..');
function arg(name, fallback) {
  const i = process.argv.indexOf('--' + name);
  return i > -1 && process.argv[i + 1] ? process.argv[i + 1] : fallback;
}
const out = arg('out', join(here, 'whip-preview.html'));

const b64 = (p) => readFileSync(p).toString('base64');
const bishop = b64(join(root, 'assets/pieces/bishop.glb'));
const pawn = b64(join(root, 'assets/pieces/pawn_purple.glb'));

/** A module, its imports and exports stripped so it can sit in one script. */
function inline(rel) {
  return readFileSync(join(root, rel), 'utf8')
    .replace(/^import .*?;\s*$/gm, '')
    .replace(/^export (const|function|class) /gm, '$1 ')
    .replace(/^export \{[^}]*\};?\s*$/gm, '');
}
const whip = inline('board/whip.js');
const jiggle = inline('board/jiggle.js');

const html = `<title>Bishop Whip</title>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=DM+Mono:wght@400;500&display=swap">
<style>
  :root { color-scheme: dark; --bg: #14101c; --ink: #f6e9f4; --pink: #ff69b4; --dim: #9a8aa8; }
  html, body { height: 100%; }
  body { margin: 0; background: var(--bg); color: var(--ink); font: 14px/1.4 system-ui, -apple-system, Segoe UI, sans-serif; overflow: hidden; }
  #stage { position: fixed; inset: 0; }
  canvas { display: block; width: 100%; height: 100%; touch-action: none; }
  #bar { position: fixed; left: 0; right: 0; bottom: 0; padding: 12px 16px calc(12px + env(safe-area-inset-bottom));
         display: flex; align-items: center; gap: 14px; background: linear-gradient(transparent, rgba(20,16,28,.92) 40%); }
  button { appearance: none; border: 0; border-radius: 999px; padding: 12px 22px; font: inherit; font-weight: 600;
           background: var(--pink); color: #2a0f1f; cursor: pointer; }
  button:active { transform: scale(.97); }
  label { display: flex; align-items: center; gap: 8px; color: var(--dim); flex: 1; }
  input[type=range] { flex: 1; accent-color: var(--pink); }
  #clock { position: fixed; top: 12px; left: 16px; font: 500 15px/1 'DM Mono', ui-monospace, Menlo, Consolas, monospace; font-variant-numeric: tabular-nums; color: var(--dim); }
  #clock b { color: var(--ink); font-weight: 500; }
  #hint { position: fixed; top: 12px; right: 16px; color: var(--dim); }
  #stage-name { position: fixed; top: 36px; left: 16px; color: var(--pink); font: 500 11px/1 'DM Mono', ui-monospace, monospace; letter-spacing: .08em; text-transform: uppercase; }
  /* the beat rail: the whip's real marks, ms from the move */
  #rail { position: fixed; left: 16px; right: 16px; top: 62px; height: 30px; }
  #rail .line { position: absolute; left: 0; right: 0; top: 8px; height: 2px; background: rgba(246,233,244,.18); }
  #rail .fill { position: absolute; left: 0; top: 8px; height: 2px; width: 0; background: var(--pink); }
  #rail .tick { position: absolute; top: 0; transform: translateX(-50%); display: flex; flex-direction: column; align-items: center; gap: 4px;
                font: 400 10px/1 'DM Mono', ui-monospace, monospace; color: var(--dim); letter-spacing: .04em; white-space: nowrap; }
  #rail .tick i { display: block; width: 2px; height: 10px; background: rgba(246,233,244,.45); margin-top: 4px; }
  #rail .tick.on { color: var(--ink); } #rail .tick.on i { background: var(--pink); }
  #rail .head { position: absolute; top: 4px; width: 10px; height: 10px; border-radius: 50%; background: var(--pink); transform: translateX(-50%); box-shadow: 0 0 0 3px rgba(255,105,180,.25); }
  @media (prefers-reduced-motion: reduce) { button:active { transform: none; } }
</style>
<div id="stage"><canvas id="c"></canvas></div>
<div id="clock"><b>0</b> ms</div>
<div id="stage-name">ready</div>
<div id="hint">drag to orbit</div>
<div id="rail"><div class="line"></div><div class="fill"></div><div class="head"></div></div>
<div id="bar">
  <button id="replay">Replay</button>
  <label>speed <input id="speed" type="range" min="0.25" max="1" step="0.05" value="1"><span id="speedv">1.0x</span></label>
</div>
<script>window.__err = null; window.addEventListener("error", (e) => { window.__err = String(e.message || e); }); window.addEventListener("unhandledrejection", (e) => { window.__err = String(e.reason && e.reason.message || e.reason); });</script>
<script type="importmap">
{ "imports": {
  "three": "https://cdnjs.cloudflare.com/ajax/libs/three.js/0.169.0/three.module.min.js",
  "three/addons/": "https://cdn.jsdelivr.net/npm/three@0.169.0/examples/jsm/"
} }
</script>
<script type="module">
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

// ---- board/whip.js, verbatim ----
${whip}
// ---- board/jiggle.js, verbatim ----
${jiggle}

const BISHOP = '${bishop}';
const PAWN = '${pawn}';
const toBuffer = (s) => Uint8Array.from(atob(s), (c) => c.charCodeAt(0)).buffer;

const canvas = document.getElementById('c');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
renderer.setPixelRatio(Math.min(2, window.devicePixelRatio || 1));
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x14101c);
const camera = new THREE.PerspectiveCamera(38, 1, 0.1, 50);
camera.position.set(2.6, 2.4, 3.4);
const controls = new OrbitControls(camera, canvas);
controls.target.set(0.2, 0.45, 0);
controls.enableDamping = true;
controls.minDistance = 1.6; controls.maxDistance = 9; controls.maxPolarAngle = Math.PI * 0.49;

// The board: a 4x4 patch of squares around the fight, the game's pink and cream.
const patch = new THREE.Group();
for (let x = -2; x < 2; x++) for (let z = -2; z < 2; z++) {
  const light = (x + z) % 2 === 0;
  const m = new THREE.Mesh(new THREE.BoxGeometry(1, 0.08, 1), new THREE.MeshStandardMaterial({ color: light ? 0xEFE3DC : 0xFF77BF, roughness: 0.85 }));
  m.position.set(x + 0.5, -0.04, z + 0.5);
  m.receiveShadow = true;
  patch.add(m);
}
scene.add(patch);
scene.add(new THREE.HemisphereLight(0xfff0f6, 0x2a1830, 0.9));
const key = new THREE.DirectionalLight(0xffffff, 2.2);
key.position.set(2.5, 5, 2); key.castShadow = true;
key.shadow.mapSize.set(1024, 1024); key.shadow.camera.left = -4; key.shadow.camera.right = 4; key.shadow.camera.top = 4; key.shadow.camera.bottom = -4;
scene.add(key);
const fill = new THREE.DirectionalLight(0xffc9e6, 0.5); fill.position.set(-3, 2, -2); scene.add(fill);

// The men. Same recipe as pieces.js: white base, the glb's own vertex colours, silicone gloss.
function silicone(sheen) {
  return new THREE.MeshPhysicalMaterial({ color: 0xffffff, vertexColors: true, roughness: 0.33, clearcoat: 0.42, clearcoatRoughness: 0.28,
    sheen: 0.72, sheenColor: new THREE.Color(sheen), sheenRoughness: 0.52 });
}
const loader = new GLTFLoader();
function man(b64, sheen) {
  return new Promise((res, rej) => loader.parse(toBuffer(b64), '', (g) => {
    const root = new THREE.Group();
    const mats = [];
    g.scene.traverse((o) => {
      if (!o.isMesh) return;
      const painted = !!o.geometry.attributes.color;
      const mesh = new THREE.Mesh(o.geometry, painted ? silicone(sheen) : o.material);
      mesh.castShadow = true;
      root.add(mesh); mats.push(mesh.material);
    });
    root.userData = { materials: mats };
    res(root);
  }, rej));
}

// Squares: the bishop starts two diagonals away and takes the pawn on the far square.
const FROM = new THREE.Vector3(-1.5, 0, 1.5);
const DEST = new THREE.Vector3(0.5, 0, -0.5);
const DIR = DEST.clone().sub(FROM).setY(0).normalize();
const NEAR = DEST.clone().addScaledVector(DIR, -WHIP_TUNING.standOff);

const jig = createJiggle();
let bishop, pawn;
const clockEl = document.querySelector('#clock b');
const stageEl = document.getElementById('stage-name');
const speedEl = document.getElementById('speed');
const speedV = document.getElementById('speedv');
let speed = 1;
// The rail: marks in ms from the move, read off whip.js so they cannot drift from the code.
const RAIL_MS = 1500;
const rail = document.getElementById('rail');
const K0 = whipTimes(WHIP_TUNING);
const MARKS = [
  ['move', 0], ['drop', WHIP_TUNING.approachSec * 1000], ['crack', (WHIP_TUNING.approachSec + K0.crack) * 1000],
  ['square', whipStandSec() * 1000], ['still', whipTotalSec() * 1000],
  ['sunk', (WHIP_TUNING.approachSec + K0.crack + WHIP_TUNING.tipSec + WHIP_TUNING.rollSec + 0.7) * 1000],
];
for (const [name, ms] of MARKS) {
  const el = document.createElement('div');
  el.className = 'tick'; el.style.left = (ms / RAIL_MS * 100) + '%';
  el.innerHTML = '<i></i><span>' + name + ' ' + Math.round(ms) + '</span>';
  el.dataset.ms = ms;
  rail.appendChild(el);
}
const head = rail.querySelector('.head'), fillEl = rail.querySelector('.fill');
function paintRail(ms) {
  const pct = Math.min(100, ms / RAIL_MS * 100);
  head.style.left = pct + '%'; fillEl.style.width = pct + '%';
  for (const el of rail.querySelectorAll('.tick')) el.classList.toggle('on', ms >= Number(el.dataset.ms));
}
speedEl.addEventListener('input', () => { speed = Number(speedEl.value); speedV.textContent = speed.toFixed(2).replace(/0$/, '') + 'x'; });

// ---- the play: anim.js's slide and tumble, re-done small, driven by whip.js ----
const ease = (t) => (t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2);
const easeOut3 = (t) => 1 - Math.pow(1 - t, 3);   // whip.js has its own easeOut, inlined above
const WT = WHIP_TUNING;   // jiggle.js owns T in this scope
const K = whipTimes(WT);
const SINK_SEC = 0.7;   // anim.js TUNING.sinkSec
let play = null;

function reset() {
  bishop.position.copy(FROM); bishop.rotation.set(0, 0, 0); bishop.visible = true;
  pawn.position.copy(DEST); pawn.rotation.set(0, Math.PI, 0);
  for (const m of pawn.userData.materials) { m.transparent = false; m.opacity = 1; }
  play = { t: 0, cracked: false, shiver: -1, stage: 'approach', axis: new THREE.Vector3().crossVectors(new THREE.Vector3(0, 1, 0), DIR).normalize(), victimT: -1 };
  stageEl.textContent = 'approach';
  clockEl.textContent = '0';
  paintRail(0);
}

const inv = new THREE.Quaternion(); const tmp = new THREE.Vector3();
function local(piece, x, z) { inv.copy(piece.quaternion).invert(); return tmp.set(x, 0, z).applyQuaternion(inv); }

function step(dt) {
  if (!play) return;
  play.t += dt;
  const t = play.t;
  clockEl.textContent = String(Math.round(t * 1000));
  paintRail(t * 1000);
  // 1. the approach to the stand-off
  if (t < WT.approachSec) {
    const p = t / WT.approachSec;
    bishop.position.lerpVectors(FROM, NEAR, ease(p));
    bishop.position.y = Math.sin(Math.PI * p) * WT.approachHop;
    return;
  }
  if (play.stage === 'approach') {
    play.stage = 'whip';
    bishop.position.copy(NEAR);
    jig.land(bishop, [NEAR.x - FROM.x, NEAR.z - FROM.z]);      // the stand-off THUD
  }
  // 2. the whip, seconds since the stand-off landing
  const u = t - WT.approachSec;
  const { bend, stage } = whipBend(u, WT);
  if (stage !== 'done') { const d = local(bishop, DIR.x, DIR.z); jig.drive(bishop, d.x * bend, d.z * bend); }
  stageEl.textContent = u < K.crack ? stage : (u < K.stand ? 'crack + stride' : (stage === 'done' ? 'done' : 'ring'));
  if (!play.cracked && u >= K.crack) {
    play.cracked = true;
    play.victimT = 0;
    jig.impulse(pawn, { bend: [DIR.x * WT.hitBend, DIR.z * WT.hitBend], squash: WT.hitSquash });
    jig.impulse(bishop, { squash: WT.lungeSquash });
    play.shiver = 0;
  }
  if (play.shiver >= 0) {
    play.shiver += dt;
    const a = shiverAt(play.shiver, WT);
    if (a) { const d = local(pawn, -DIR.z, DIR.x); jig.drive(pawn, d.x * a, d.z * a); } else play.shiver = -1;
  }
  // 3. the stride, from the crack
  if (u >= K.step && u < K.stand) {
    const p = (u - K.step) / WT.stepSec;
    bishop.position.lerpVectors(NEAR, DEST, ease(p));
    bishop.position.y = Math.sin(Math.PI * p) * WT.stepHop;
  } else if (u >= K.stand && play.stage === 'whip') {
    play.stage = 'stood';
    bishop.position.copy(DEST);
    jig.land(bishop, [DEST.x - NEAR.x, DEST.z - NEAR.z]);        // the capture THUD
  }
  // 4. the victim: tip, roll, sink (anim.js's tumble with the whip's tip and roll)
  if (play.victimT >= 0) {
    play.victimT += dt;
    const at = play.victimT;
    const tip = Math.min(1, at / WT.tipSec);
    const roll = at <= WT.tipSec ? 0 : Math.min(1, (at - WT.tipSec) / WT.rollSec);
    const angle = (Math.PI / 2) * tip * tip + Math.PI * 2 * easeOut3(roll);
    pawn.setRotationFromAxisAngle(play.axis, angle);
    pawn.position.x = DEST.x + DIR.x * WT.rollPush * easeOut3(roll);
    pawn.position.z = DEST.z + DIR.z * WT.rollPush * easeOut3(roll);
    if (roll >= 1) {
      const sink = Math.min(1, (at - WT.tipSec - WT.rollSec) / SINK_SEC);
      pawn.position.y = -sink * 1.1;
      for (const m of pawn.userData.materials) { m.transparent = true; m.opacity = 1 - sink; }
    }
  }
  if (play.stage === 'stood' && stage === 'done' && play.victimT > WT.tipSec + WT.rollSec + SINK_SEC) {
    stageEl.textContent = 'done - ' + Math.round(whipStandSec() * 1000) + ' ms to the square';
    play = null;
  }
}

function resize() {
  const w = canvas.clientWidth, h = canvas.clientHeight;
  if (canvas.width !== Math.floor(w * renderer.getPixelRatio()) || canvas.height !== Math.floor(h * renderer.getPixelRatio())) {
    renderer.setSize(w, h, false); camera.aspect = w / h; camera.updateProjectionMatrix();
  }
}
let last = performance.now();
function frame(now) {
  const dt = Math.min(0.05, (now - last) / 1000) * speed;
  last = now;
  resize();
  step(dt);
  jig.update(dt);
  controls.update();
  renderer.render(scene, camera);
  requestAnimationFrame(frame);
}

Promise.all([man(BISHOP, 0xFF9EC4), man(PAWN, 0x7B6CFF)]).then(([b, p]) => {
  bishop = b; pawn = p;
  scene.add(bishop, pawn);
  jig.attach(bishop); jig.attach(pawn);
  reset();
  document.getElementById('replay').addEventListener('click', reset);
  requestAnimationFrame(frame);
}).catch((e) => { stageEl.textContent = 'could not load the men: ' + (e && e.message || e); });
</script>
`;

writeFileSync(out, html);
console.log('wrote ' + out + ' (' + Math.round(html.length / 1024) + ' KB)');
