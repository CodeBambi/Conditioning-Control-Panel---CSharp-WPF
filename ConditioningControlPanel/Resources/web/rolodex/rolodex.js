/* ============================================================================
 * rolodex.js - the 3D feature picker for the customizable Home dashboard.
 * Phase E of plan `home-dashboard-slots-rolodex.md`. No bundler, no network,
 * no dependency beyond the vendored three.js r169 under ../vendor/three.
 *
 * MESSAGE CONTRACT (postMessage JSON, {type:...} envelope, see bridge.js)
 *
 *   Host -> page
 *     init  { rings: [ { ring:1..4,
 *                        faces:[ { key, title, blurb,
 *                                  tier:0|1|2, locked:bool,
 *                                  art:dataUri|null } ] } ],
 *             slot:int|null, mode:'edit'|'tour', picks:int,
 *             reducedMotion:bool, lang:string }
 *     focus { key }   spin to that face and make its ring the focused one
 *     close           the host is tearing the view down; stop everything
 *
 *   Page -> host
 *     ready                     once, on load (bridge.announceReady)
 *     pick     { key }          edit mode, one pick, then the host closes us
 *     tourDone { keys:[...] }   tour mode, sent when Done is pressed
 *     close                     Esc or the close button
 *     log      { level, msg }   bridge.log passthrough to Serilog
 *
 * The page never knows the layout rules: replace / split / move prompting all
 * stays WPF-side after `pick`. `slot` and `lang` ride along for display only;
 * nothing here localizes, the host sends strings already localized.
 *
 * MOCK MODE: index.html?mock=1 (edit) or ?mock=tour (tour, picks 3). Also
 * engaged automatically when window.chrome.webview is absent, so the page is
 * iterable in a plain browser. The fixture feeds the 27 real keys in their 4
 * rings with placeholder art, and pick / tourDone / close land in the console
 * and in the on-page status line instead of on the host.
 * ==========================================================================*/

import * as THREE from 'three';
import * as bridge from './bridge.js';

/* ---------------------------------------------------------------- geometry */

const CARD_W = 1.6;            // 16:9 card, world units
const CARD_H = 0.9;
const FACE_GAP = 1.22;         // chord multiplier; keeps neighbours from touching
const RING_GAP = 1.34;         // vertical distance between ring planes
const CAM_BACKOFF = 2.85;      // camera sits this far in front of the front card
const MIN_RADIUS = 1.55;
const TEX_W = 512, TEX_H = 288;
const VELVET = 0x0b0710;
const DIM = 0.34;              // unfocused ring opacity
const DRAG_PX_PER_FACE = 150;  // horizontal pixels that advance one card
const STEP_PX = 78;            // vertical pixels that step one ring
const CLICK_SLOP = 6;          // pointer travel below which a drag is a click

/* ------------------------------------------------------------------- state */

const S = {
  rings: [],        // [{ n, step, radius, group, faces[], backMat, dim, dimTarget }]
  byKey: new Map(), // key -> face
  focus: 0,         // index into S.rings
  mode: 'edit',
  picks: 1,
  picked: [],       // keys, tour mode
  slot: null,
  lang: 'en',
  reduced: false,
  started: false,
  dead: false
};

const el = {
  stage: document.getElementById('stage'),
  title: document.getElementById('title'),
  count: document.getElementById('count'),
  hint: document.getElementById('hint'),
  done: document.getElementById('btn-done'),
  close: document.getElementById('btn-close'),
  status: document.getElementById('status')
};

const QS = new URLSearchParams(location.search);
const MOCK = QS.has('mock') || !bridge.isHosted;

/* ------------------------------------------------------------------- three */

const renderer = new THREE.WebGLRenderer({ canvas: el.stage, antialias: true, alpha: false });
renderer.setClearColor(VELVET, 1);
const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(45, 1, 0.1, 60);
const cam = { y: 0, z: 4 };            // tweened; camera looks at (0, cam.y, 0)
const raycaster = new THREE.Raycaster();
const pointerNdc = new THREE.Vector2();
const cardGeo = new THREE.PlaneGeometry(CARD_W, CARD_H);

function sizeToViewport() {
  const w = Math.max(1, window.innerWidth);
  const h = Math.max(1, window.innerHeight);
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
  renderer.setSize(w, h, false);
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
}
window.addEventListener('resize', sizeToViewport);
// The host resizes the HWND without always firing `resize`, so watch the box too.
if (window.ResizeObserver) new ResizeObserver(sizeToViewport).observe(document.documentElement);
sizeToViewport();

/* ------------------------------------------------------------------ tweens */

const tweens = [];
const easeOut = (t) => 1 - Math.pow(1 - t, 3);

/** Tween obj[prop] to `to`. reducedMotion collapses every tween to a cut. */
function tween(obj, prop, to, dur) {
  for (let i = tweens.length - 1; i >= 0; i--) {
    if (tweens[i].obj === obj && tweens[i].prop === prop) tweens.splice(i, 1);
  }
  if (S.reduced || dur <= 0) { obj[prop] = to; return; }
  tweens.push({ obj, prop, from: obj[prop], to, t: 0, dur });
}

function stepTweens(dt) {
  for (let i = tweens.length - 1; i >= 0; i--) {
    const tw = tweens[i];
    tw.t += dt;
    const k = Math.min(1, tw.t / tw.dur);
    tw.obj[tw.prop] = tw.from + (tw.to - tw.from) * easeOut(k);
    if (k >= 1) tweens.splice(i, 1);
  }
}

/** Frame-rate independent approach, also a cut under reduced motion. */
function approach(cur, target, rate, dt) {
  if (S.reduced) return target;
  return cur + (target - cur) * (1 - Math.exp(-rate * dt));
}

/* ------------------------------------------------------------ card texture */

const TIER = {
  0: { rim: 'rgba(148,120,190,0.50)', ink: '#cbb6e8', badge: null, fill: null },
  1: { rim: 'rgba(226,183,85,0.95)', ink: '#1c1206', badge: 'TIER 1', fill: '#e2b755' },
  2: { rim: 'rgba(190,240,255,0.95)', ink: '#06212a', badge: 'LAB', fill: '#bef0ff' }
};

function hashHue(s) {
  let h = 0;
  for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0;
  return h % 360;
}

function roundRect(ctx, x, y, w, h, r) {
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.arcTo(x + w, y, x + w, y + h, r);
  ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r);
  ctx.arcTo(x, y, x + w, y, r);
  ctx.closePath();
}

function fitText(ctx, text, maxW) {
  if (ctx.measureText(text).width <= maxW) return text;
  let t = text;
  while (t.length > 1 && ctx.measureText(t + '…').width > maxW) t = t.slice(0, -1);
  return t + '…';
}

/** Draw the whole card face. Called at build, on art load and on pick toggle. */
function drawFace(face) {
  const ctx = face.ctx;
  const hue = hashHue(face.key);
  const style = TIER[face.tier] || TIER[0];
  ctx.clearRect(0, 0, TEX_W, TEX_H);

  // Body, clipped to the rounded card so the art never squares off the corners.
  ctx.save();
  roundRect(ctx, 0, 0, TEX_W, TEX_H, 18);
  ctx.clip();

  if (face.img) {
    const sc = Math.max(TEX_W / face.img.width, TEX_H / face.img.height);   // cover fit
    const dw = face.img.width * sc, dh = face.img.height * sc;
    ctx.drawImage(face.img, (TEX_W - dw) / 2, (TEX_H - dh) / 2, dw, dh);
  } else {
    // Placeholder: a hue-tinted wash keyed off the feature key, plus its initial.
    const g = ctx.createLinearGradient(0, 0, TEX_W, TEX_H);
    g.addColorStop(0, 'hsl(' + hue + ' 55% 26%)');
    g.addColorStop(0.55, 'hsl(' + ((hue + 28) % 360) + ' 48% 16%)');
    g.addColorStop(1, 'hsl(' + ((hue + 300) % 360) + ' 40% 10%)');
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, TEX_W, TEX_H);
    ctx.globalAlpha = 0.16;
    ctx.fillStyle = '#ffffff';
    ctx.font = '700 190px "Segoe UI", system-ui, sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(face.title.slice(0, 1).toUpperCase(), TEX_W * 0.5, TEX_H * 0.42);
    ctx.globalAlpha = 1;
    ctx.textAlign = 'left';
    ctx.textBaseline = 'alphabetic';
  }

  // Scrim so the copy reads over any art.
  const scrim = ctx.createLinearGradient(0, TEX_H * 0.36, 0, TEX_H);
  scrim.addColorStop(0, 'rgba(8,4,14,0)');
  scrim.addColorStop(0.55, 'rgba(8,4,14,0.72)');
  scrim.addColorStop(1, 'rgba(8,4,14,0.94)');
  ctx.fillStyle = scrim;
  ctx.fillRect(0, TEX_H * 0.36, TEX_W, TEX_H * 0.64);

  const lift = face.locked ? 46 : 0;   // the lockband owns the bottom strip

  ctx.fillStyle = '#f3ecff';
  ctx.font = '700 34px "Segoe UI", system-ui, sans-serif';
  ctx.fillText(fitText(ctx, face.title, TEX_W - 56), 28, TEX_H - 52 - lift);

  ctx.fillStyle = 'rgba(201,186,222,0.92)';
  ctx.font = '400 19px "Segoe UI", system-ui, sans-serif';
  ctx.fillText(fitText(ctx, face.blurb || '', TEX_W - 56), 28, TEX_H - 24 - lift);

  // Tier badge, top right. Tier 0 has none.
  if (style.badge) {
    ctx.font = '700 17px "Segoe UI", system-ui, sans-serif';
    const bw = ctx.measureText(style.badge).width + 26;
    ctx.fillStyle = style.fill;
    roundRect(ctx, TEX_W - 18 - bw, 16, bw, 30, 15);
    ctx.fill();
    ctx.fillStyle = style.ink;
    ctx.fillText(style.badge, TEX_W - 18 - bw + 13, 37);
  }

  // Lockband.
  if (face.locked) {
    ctx.fillStyle = 'rgba(10,5,16,0.80)';
    ctx.fillRect(0, TEX_H - 44, TEX_W, 44);
    ctx.fillStyle = 'rgba(255,255,255,0.06)';
    ctx.fillRect(0, TEX_H - 44, TEX_W, 1);
    ctx.fillStyle = style.fill || '#d8c8f2';
    ctx.font = '700 18px "Segoe UI", system-ui, sans-serif';
    ctx.textAlign = 'center';
    ctx.fillText('L O C K E D', TEX_W / 2, TEX_H - 16);
    ctx.textAlign = 'left';
  }

  // Picked check, tour mode.
  if (face.picked) {
    ctx.fillStyle = 'rgba(154,102,238,0.96)';
    ctx.beginPath();
    ctx.arc(46, 46, 26, 0, Math.PI * 2);
    ctx.fill();
    ctx.strokeStyle = '#ffffff';
    ctx.lineWidth = 6;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.beginPath();
    ctx.moveTo(33, 47);
    ctx.lineTo(43, 57);
    ctx.lineTo(60, 35);
    ctx.stroke();
  }
  ctx.restore();

  // Rim last, outside the clip, so it is never eaten by the art.
  ctx.lineWidth = 4;
  ctx.strokeStyle = style.rim;
  roundRect(ctx, 3, 3, TEX_W - 6, TEX_H - 6, 16);
  ctx.stroke();
  if (face.tier === 2) {          // the diamond rim gets a second, inner hairline
    ctx.lineWidth = 1.5;
    ctx.strokeStyle = 'rgba(255,255,255,0.55)';
    roundRect(ctx, 9, 9, TEX_W - 18, TEX_H - 18, 12);
    ctx.stroke();
  }

  face.tex.needsUpdate = true;
}

/* ---------------------------------------------------------------- building */

function buildFace(spec, ringIdx, i) {
  const canvas = document.createElement('canvas');
  canvas.width = TEX_W;
  canvas.height = TEX_H;
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.anisotropy = renderer.capabilities.getMaxAnisotropy();

  const face = {
    key: String(spec.key || ''),
    title: String(spec.title || spec.key || ''),
    blurb: String(spec.blurb || ''),
    tier: Number(spec.tier) || 0,
    locked: !!spec.locked,
    picked: false,
    img: null,
    canvas: canvas,
    ctx: canvas.getContext('2d'),
    tex: tex,
    ringIdx: ringIdx,
    index: i,
    mesh: null,
    scale: 1
  };
  drawFace(face);

  if (spec.art) {
    const img = new Image();
    img.onload = () => { face.img = img; drawFace(face); };
    img.onerror = () => bridge.log('warn', 'rolodex: art failed for ' + face.key);
    img.src = spec.art;      // data URI from the host; never a network fetch
  }
  return face;
}

function buildRings(ringSpecs) {
  for (const r of S.rings) scene.remove(r.group);
  S.rings.length = 0;
  S.byKey.clear();

  const list = ringSpecs.filter((r) => r && Array.isArray(r.faces) && r.faces.length);
  list.forEach((spec, ri) => {
    const faces = spec.faces;
    const n = faces.length;
    const step = (Math.PI * 2) / n;
    // A radius that keeps the chord between neighbours wider than a card.
    const radius = Math.max(MIN_RADIUS, (CARD_W * FACE_GAP) / (2 * Math.sin(Math.PI / n)));
    const group = new THREE.Group();
    group.position.y = ((list.length - 1) / 2 - ri) * RING_GAP;   // ring 1 on top

    // One back material per ring so a ring dims as a unit.
    const backMat = new THREE.MeshBasicMaterial({ color: 0x1a1020, transparent: true, opacity: 1 });

    const built = [];
    for (let i = 0; i < n; i++) {
      const face = buildFace(faces[i], ri, i);
      const a = step * i;
      const mat = new THREE.MeshBasicMaterial({ map: face.tex, transparent: true, opacity: 1 });
      const mesh = new THREE.Mesh(cardGeo, mat);
      mesh.position.set(radius * Math.sin(a), 0, radius * Math.cos(a));
      mesh.rotation.y = a;                     // normal points out of the cylinder
      mesh.userData.face = face;
      face.mesh = mesh;
      group.add(mesh);

      // A dark back so the far side of the ring reads as card backs, not holes.
      const back = new THREE.Mesh(cardGeo, backMat);
      back.position.copy(mesh.position).multiplyScalar(0.998);
      back.rotation.y = a + Math.PI;
      group.add(back);

      built.push(face);
      S.byKey.set(face.key, face);
    }

    scene.add(group);
    S.rings.push({
      n: n, step: step, radius: radius, group: group, faces: built, backMat: backMat,
      dim: 1, dimTarget: 1, ring: Number(spec.ring) || (ri + 1)
    });
  });
}

/* -------------------------------------------------------------- navigation */

const ring = () => S.rings[S.focus];

/** Index of the card currently nearest the front of a ring. */
function frontIndex(r) {
  const k = Math.round(-r.group.rotation.y / r.step);
  return ((k % r.n) + r.n) % r.n;
}
function frontFace(r) { return r.faces[frontIndex(r)]; }

/** The continuous turn count that lands `index` at the front, shortest way. */
function nearestTurnFor(r, index) {
  const cur = -r.group.rotation.y / r.step;
  const base = Math.round((cur - index) / r.n);
  return index + base * r.n;
}

/**
 * Snap a ring to the nearest card, or to a named turn count. Rotation is kept
 * as an unbounded continuous value, so there is never a wrap discontinuity to
 * tween across.
 */
function snapToTurn(r, turn, dur) {
  const k = (turn === undefined || turn === null)
    ? Math.round(-r.group.rotation.y / r.step)
    : turn;
  tween(r.group.rotation, 'y', -k * r.step, dur === undefined ? 0.34 : dur);
}
function snapToFace(r, index, dur) { snapToTurn(r, nearestTurnFor(r, index), dur); }

function setFocus(next, dur) {
  const clamped = Math.max(0, Math.min(S.rings.length - 1, next));
  S.focus = clamped;
  for (let i = 0; i < S.rings.length; i++) S.rings[i].dimTarget = (i === clamped ? 1 : DIM);
  const r = ring();
  tween(cam, 'y', r.group.position.y, dur === undefined ? 0.4 : dur);
  tween(cam, 'z', r.radius + CAM_BACKOFF, dur === undefined ? 0.4 : dur);
}

function focusKey(key) {
  const face = S.byKey.get(key);
  if (!face) return;
  setFocus(face.ringIdx);
  snapToFace(S.rings[face.ringIdx], face.index);
}

/* ----------------------------------------------------------------- picking */

function pickFace(face) {
  if (!face || S.dead || !S.started) return;
  if (S.mode === 'tour') {
    const at = S.picked.indexOf(face.key);
    if (at >= 0) {
      S.picked.splice(at, 1);                 // an already picked face toggles off
      face.picked = false;
    } else {
      if (S.picked.length >= S.picks) {
        // Over the ask: drop the oldest so the newest choice always lands.
        const of = S.byKey.get(S.picked.shift());
        if (of) { of.picked = false; drawFace(of); }
      }
      S.picked.push(face.key);
      face.picked = true;
    }
    drawFace(face);
    refreshHud();
  } else {
    say({ type: 'pick', key: face.key });
    // One ask per open: the host closes us after the pick (plan 9.2).
    S.dead = true;
    el.hint.textContent = 'Picked ' + face.title;
  }
}

function requestClose() {
  say({ type: 'close' });
  S.dead = true;
}

/* ------------------------------------------------------------------- input */

let drag = null;   // { x, y, ox, oy, axis, theta0, travel }

el.stage.addEventListener('pointerdown', (e) => {
  if (S.dead || !S.started) return;
  try { el.stage.setPointerCapture(e.pointerId); } catch (err) { /* no capture, fine */ }
  el.stage.classList.add('dragging');
  drag = {
    x: e.clientX, y: e.clientY, ox: e.clientX, oy: e.clientY,
    axis: null, theta0: ring().group.rotation.y, travel: 0
  };
});

el.stage.addEventListener('pointermove', (e) => {
  if (!drag) return;
  const dx = e.clientX - drag.ox;
  const dy = e.clientY - drag.oy;
  drag.travel = Math.max(drag.travel,
    Math.abs(e.clientX - drag.x) + Math.abs(e.clientY - drag.y));
  if (!drag.axis && (Math.abs(dx) > CLICK_SLOP || Math.abs(dy) > CLICK_SLOP)) {
    drag.axis = Math.abs(dx) >= Math.abs(dy) ? 'x' : 'y';
  }
  if (drag.axis === 'x') {
    const r = ring();
    // Dragging right carries the cards right, the way a physical wheel would.
    r.group.rotation.y = drag.theta0 + (dx / DRAG_PX_PER_FACE) * r.step;
  } else if (drag.axis === 'y' && Math.abs(dy) >= STEP_PX) {
    setFocus(S.focus + (dy > 0 ? -1 : 1));    // drag down reveals the shelf above
    drag.oy = e.clientY;
    drag.ox = e.clientX;
    drag.theta0 = ring().group.rotation.y;
  }
});

function endDrag(e) {
  if (!drag) return;
  const axis = drag.axis;
  const travel = drag.travel;
  drag = null;
  el.stage.classList.remove('dragging');
  if (axis === 'x') { snapToTurn(ring()); return; }
  if (axis === null && travel <= CLICK_SLOP) clickAt(e);
}
el.stage.addEventListener('pointerup', endDrag);
el.stage.addEventListener('pointercancel', () => {
  if (!drag) return;
  drag = null;
  el.stage.classList.remove('dragging');
  snapToTurn(ring());
});

/** A click on the front card picks; a click on any other card spins to it. */
function clickAt(e) {
  if (S.dead || !S.started) return;
  pointerNdc.x = (e.clientX / window.innerWidth) * 2 - 1;
  pointerNdc.y = -(e.clientY / window.innerHeight) * 2 + 1;
  raycaster.setFromCamera(pointerNdc, camera);
  const hits = raycaster.intersectObjects(scene.children, true);
  for (const hit of hits) {
    const face = hit.object.userData && hit.object.userData.face;
    if (!face) continue;                       // a card back: ignore, keep looking
    const r = S.rings[face.ringIdx];
    if (face.ringIdx === S.focus && face === frontFace(r)) pickFace(face);
    else { setFocus(face.ringIdx); snapToFace(r, face.index); }
    return;
  }
}

let wheelLock = 0;
el.stage.addEventListener('wheel', (e) => {
  if (S.dead || !S.started) return;
  e.preventDefault();
  const now = performance.now();
  if (now < wheelLock) return;
  wheelLock = now + 190;
  setFocus(S.focus + (e.deltaY > 0 ? 1 : -1));
}, { passive: false });

window.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') { requestClose(); e.preventDefault(); return; }
  if (S.dead || !S.started) return;
  const r = ring();
  switch (e.key) {
    case 'ArrowLeft':
      snapToTurn(r, nearestTurnFor(r, frontIndex(r)) - 1); e.preventDefault(); break;
    case 'ArrowRight':
      snapToTurn(r, nearestTurnFor(r, frontIndex(r)) + 1); e.preventDefault(); break;
    case 'ArrowUp':
      setFocus(S.focus - 1); e.preventDefault(); break;
    case 'ArrowDown':
      setFocus(S.focus + 1); e.preventDefault(); break;
    case 'Enter':
    case ' ':
      pickFace(frontFace(r)); e.preventDefault(); break;
    default:
      break;
  }
});

el.close.addEventListener('click', requestClose);
el.done.addEventListener('click', () => {
  if (S.picked.length < S.picks) return;
  say({ type: 'tourDone', keys: S.picked.slice() });
  S.dead = true;
});

/* --------------------------------------------------------------------- HUD */

function refreshHud() {
  if (S.mode === 'tour') {
    const left = Math.max(0, S.picks - S.picked.length);
    el.count.hidden = false;
    el.count.textContent = left > 0 ? 'pick ' + left : S.picks + ' picked';
    el.title.textContent = 'Pick your favourites';
    el.done.hidden = S.picked.length < S.picks;
  } else {
    el.count.hidden = true;
    el.done.hidden = true;
    el.title.textContent = (S.slot === null || S.slot === undefined)
      ? 'Pick a feature'
      : 'Pick a feature for slot ' + (S.slot + 1);
  }
}

/** Post to the host; in mock, narrate to the console and the status line. */
function say(msg) {
  bridge.send(msg);
  if (MOCK) {
    const line = JSON.stringify(msg);
    console.log('[rolodex -> host]', line);
    el.status.hidden = false;
    el.status.textContent = (el.status.textContent ? el.status.textContent + '\n' : '') + line;
  }
}

/* -------------------------------------------------------------- frame loop */

let last = performance.now();
function frame(now) {
  requestAnimationFrame(frame);
  const dt = Math.min(0.05, (now - last) / 1000);
  last = now;
  stepTweens(dt);

  for (let i = 0; i < S.rings.length; i++) {
    const r = S.rings[i];
    r.dim = approach(r.dim, r.dimTarget, 9, dt);
    r.backMat.opacity = r.dim;
    const fi = (i === S.focus) ? frontIndex(r) : -1;
    for (let j = 0; j < r.faces.length; j++) {
      const f = r.faces[j];
      f.mesh.material.opacity = r.dim;
      f.scale = approach(f.scale, (j === fi) ? 1.07 : 1, 12, dt);
      f.mesh.scale.setScalar(f.scale);
    }
  }

  camera.position.set(0, cam.y + 0.06, cam.z);
  camera.lookAt(0, cam.y, 0);
  renderer.render(scene, camera);
}
requestAnimationFrame(frame);

/* ---------------------------------------------------------------- handlers */

const HANDLERS = {
  init(m) {
    S.mode = (m.mode === 'tour') ? 'tour' : 'edit';
    S.picks = Math.max(1, Number(m.picks) || 1);
    S.slot = (m.slot === null || m.slot === undefined) ? null : Number(m.slot);
    S.lang = String(m.lang || 'en');
    S.reduced = !!m.reducedMotion ||
      !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
    S.picked.length = 0;

    buildRings(Array.isArray(m.rings) ? m.rings : []);
    if (!S.rings.length) { bridge.log('error', 'rolodex: init carried no rings'); return; }

    S.started = true;
    S.dead = false;
    setFocus(0, 0);                 // cut to the first ring, never tween the boot
    refreshHud();
    bridge.log('info', 'rolodex: init mode=' + S.mode + ' rings=' + S.rings.length +
                       ' faces=' + S.byKey.size + (S.reduced ? ' reduced' : ''));
  },

  focus(m) { if (m && m.key) focusKey(String(m.key)); },

  close() { S.dead = true; S.started = false; }
};

for (const type of Object.keys(HANDLERS)) bridge.on(type, HANDLERS[type]);
bridge.announceReady();

/* --------------------------------------------------------------- mock mode */

const MOCK_RINGS = [
  { ring: 1, tier: 0, keys: ['flash', 'video', 'subliminal', 'bouncingtext', 'bubblecount', 'bubbles'] },
  { ring: 2, tier: 0, keys: ['spiral', 'pinkfilter', 'mindwipe', 'braindrain', 'lockcard', 'goon', 'gradedintake'] },
  { ring: 3, tier: 1, keys: ['fyp', 'blinktrainer', 'remotecontrol', 'bambitakeover', 'shelistening', 'haptics', 'awareness', 'lockdown'] },
  { ring: 4, tier: 2, keys: ['dtrh', 'justdrop', 'arcademy', 'piecebypiece', 'gaze', 'focusgaze'] }
];

const MOCK_TITLES = {
  flash: 'Flash Images', video: 'Mandatory Videos', subliminal: 'Subliminals',
  bouncingtext: 'Bouncing Text', bubblecount: 'Bubble Count', bubbles: 'Bubble Pop',
  spiral: 'Spiral Overlay', pinkfilter: 'Pink Filter', mindwipe: 'Mind Wipers',
  braindrain: 'Brain Drain', lockcard: 'Phrase Lock', goon: 'Goon Mode',
  gradedintake: 'Graded Intake', fyp: 'For You Page', blinktrainer: 'Blink Trainer',
  remotecontrol: 'Remote Control', bambitakeover: 'Takeover', shelistening: 'She Is Listening',
  haptics: 'Haptics', awareness: 'Awareness Engine', lockdown: 'Lockdown',
  dtrh: 'Down the Rabbit Hole', justdrop: 'Just Drop', arcademy: 'The Arcademy',
  piecebypiece: 'Piece by Piece', gaze: 'Gaze Minigame', focusgaze: 'Focus Gaze'
};

const MOCK_BLURBS = {
  flash: 'Images that land faster than you can read them.',
  video: 'Clips that interrupt whatever you were doing.',
  subliminal: 'Quiet lines under everything else.',
  bouncingtext: 'One phrase that will not sit still.',
  bubblecount: 'Count them. Out loud, if you like.',
  bubbles: 'Pop them before they drift away.',
  spiral: 'A spiral over the whole screen.',
  pinkfilter: 'Everything, a little softer.',
  mindwipe: 'Wipers across the glass.',
  braindrain: 'A slow pull downward.',
  lockcard: 'Type the phrase to get your desktop back.',
  goon: 'A long session with no exits.',
  gradedintake: 'Answer the questions. Take the grade.',
  fyp: 'An endless feed picked for you.',
  blinktrainer: 'Blink when it says blink.',
  remotecontrol: 'Someone else holds the dial.',
  bambitakeover: 'The screen stops being yours.',
  shelistening: 'She hears the room.',
  haptics: 'Toys that follow along.',
  awareness: 'The panel watches what you open.',
  lockdown: 'A timer you cannot argue with.',
  dtrh: 'Fall through the tunnel.',
  justdrop: 'One door. No preview.',
  arcademy: 'Play your way through the lessons.',
  piecebypiece: 'A game of chess for stakes.',
  gaze: 'Hold your eyes where it says.',
  focusgaze: 'Focus tracked, corrected, scored.'
};

/** Fixture `init`: the 27 real keys, placeholder art, a stable locked mix. */
function mockInit() {
  let seed = 7;
  const rand = () => (seed = (seed * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff;
  const tour = QS.get('mock') === 'tour';
  return {
    type: 'init',
    rings: MOCK_RINGS.map((r) => ({
      ring: r.ring,
      faces: r.keys.map((k) => ({
        key: k,
        title: MOCK_TITLES[k] || k,
        blurb: MOCK_BLURBS[k] || 'A feature of the panel.',
        tier: r.tier,
        locked: r.tier > 0 ? rand() < 0.55 : rand() < 0.12,
        art: null                       // no real images: drawFace paints a placeholder
      }))
    })),
    slot: 4,
    mode: tour ? 'tour' : 'edit',
    picks: tour ? 3 : 1,
    reducedMotion: QS.get('reduced') === '1',
    lang: 'en'
  };
}

if (MOCK) {
  const msg = mockInit();
  HANDLERS.init(msg);
  el.status.hidden = false;
  el.status.textContent = '[mock] mode=' + msg.mode + ' picks=' + msg.picks +
    ' slot=' + msg.slot + ' faces=' + S.byKey.size;
  console.log('[rolodex] mock init', msg);
}
