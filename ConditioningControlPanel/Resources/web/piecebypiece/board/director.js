/* ============================================================================
 * board/director.js - the follow camera and the capture replay.
 *
 * FOLLOW (owner, 2026-09-28): when a man moves, the camera eases in behind the
 * move and swings a little to the side, then settles back to wherever the rig
 * was. It only ever blends on top of board/camera.js: the rig keeps its own
 * state, so letting go is exact. The player's hand on the camera cancels the
 * follow until the next move.
 *
 * REPLAY: a capture is recorded while it plays (the attacker, the victim and
 * any man busy in it: transforms, the soft-body uniforms, the skins), then
 * shown again from two or three cameras in tilted comic panels, or one corner
 * inset. The layout rotates at random (trio, duo, corner, never the same twice
 * running). The panels are staggered so the hit lands in panel 1, then 2, then
 * 3, with the panel taking its hit lit and the others dimmed; all timing is
 * board/replay-plan.js. A click, a tap or a key skips it, and the board keeps
 * taking input throughout. Online and a low clock get the corner only; under
 * 10 s, reduced motion, or the option off, nothing.
 *
 * Nothing here touches rules, clocks or the game: it is presentation only.
 * ==========================================================================*/

import * as THREE from 'three';
import { LAYOUTS, SHOTS, clipTime, panelState, panelCount, replayLength, replayAllowed,
  createLayoutDeck, scalePoly, orient, REPLAY } from './replay-plan.js';
import { presentation } from '../game/preferences.js';

export const FOLLOW = Object.freeze({
  in: .4,           // seconds to blend in
  out: .9,          // and back out
  hold: .35,        // after the move settles, before letting go
  minSec: 1.2,      // a quick move is still followed this long
  radius: .5,       // of the rig's radius
  radiusMin: 5.0,
  lower: .16,       // radians lower than the rig's elevation
  phiMax: 1.25,
  swing: .34,       // radians round to the side of the move
  opponent: .6,     // how far the camera commits to the other side's move
  lead: .35,        // aim this far along from the man to where he is going
  settle: 6,        // e-fold rate of the aim point chasing the man
});
const REC_MAX = 7;  // seconds; a capture that runs longer is not recorded

const clamp01 = v => Math.max(0, Math.min(1, v));
const smooth = t => t * t * (3 - 2 * t);
const skinsOf = p => p.userData.materials || (p.userData.material ? [p.userData.material] : []);

function reducedNow() {
  const s = globalThis.window?.PBP?.settings;
  return !!(s?.reducedMotion || globalThis.window?.matchMedia?.('(prefers-reduced-motion: reduce)').matches);
}

// ---- snapshots ---------------------------------------------------------------
function snap(obj, group) {
  const u = obj.userData.jiggleUniforms, uni = [];
  if (u) for (const k in u) { const v = u[k].value; uni.push(typeof v === 'number' ? v : v?.toArray ? v.toArray() : null); }
  return {
    parent: obj.parent, inGroup: obj.parent === group, visible: obj.visible,
    tr: [obj.position.x, obj.position.y, obj.position.z, obj.quaternion.x, obj.quaternion.y, obj.quaternion.z, obj.quaternion.w,
      obj.scale.x, obj.scale.y, obj.scale.z],
    uni, skins: skinsOf(obj).map(m => [m.opacity, m.transparent, m.depthWrite]),
  };
}
const q1 = new THREE.Quaternion(), q2 = new THREE.Quaternion();
function applySnap(obj, a, b, f, group, live) {
  if (live) {
    if (obj.parent !== a.parent) { if (a.parent) a.parent.add(obj); else obj.removeFromParent(); }
    obj.visible = a.visible;
  } else if (a.inGroup) {
    if (obj.parent !== group) group.add(obj);
    obj.visible = a.visible;
  } else { obj.visible = false; return; }
  const s = a.tr, e = b ? b.tr : s, k = b ? f : 0;
  obj.position.set(s[0] + (e[0] - s[0]) * k, s[1] + (e[1] - s[1]) * k, s[2] + (e[2] - s[2]) * k);
  q1.set(s[3], s[4], s[5], s[6]); q2.set(e[3], e[4], e[5], e[6]);
  obj.quaternion.copy(q1.slerp(q2, k));
  obj.scale.set(s[7] + (e[7] - s[7]) * k, s[8] + (e[8] - s[8]) * k, s[9] + (e[9] - s[9]) * k);
  const u = obj.userData.jiggleUniforms;
  if (u) {
    let i = 0;
    for (const key in u) {
      const va = a.uni[i], vb = b ? b.uni[i] : va; i++;
      if (va == null) continue;
      if (typeof va === 'number') u[key].value = va + ((typeof vb === 'number' ? vb : va) - va) * k;
      else if (u[key].value?.fromArray) {
        const out = va.map((x, j) => x + (((vb && vb[j]) ?? x) - x) * k);
        u[key].value.fromArray(out);
      }
    }
  }
  const skins = skinsOf(obj);
  a.skins.forEach((m, i) => { if (skins[i]) { skins[i].opacity = m[0]; skins[i].transparent = m[1]; skins[i].depthWrite = m[2]; } });
}

// ---- the compositor ------------------------------------------------------------
const VERT = 'void main(){ gl_Position = vec4(position.xy, 0.0, 1.0); }';
const FRAG = `uniform sampler2D map; uniform vec2 res; uniform vec3 planes[6]; uniform int count;
  uniform float flash; uniform float light;
  void main(){
    vec2 p = gl_FragCoord.xy;
    for (int i = 0; i < 6; i++) { if (i >= count) break; if (dot(planes[i].xy, p) + planes[i].z < 0.0) discard; }
    vec3 c = texture2D(map, p / res).rgb * light;
    c = mix(c, vec3(1.0), flash);
    gl_FragColor = vec4(c, 1.0);
    #include <tonemapping_fragment>
    #include <colorspace_fragment>
  }`;

export function createDirector({ view, anim, bus, game, root = null, random = Math.random }) {
  const { renderer, camera, pieceGroup: group } = view;
  const rig = view.cameraRig;
  const deck = createLayoutDeck(random);
  // ?replay=trio|duo|corner pins one layout, for a dev page or a screenshot harness.
  const pinned = (() => { try { return new URLSearchParams(globalThis.location?.search || '').get('replay'); } catch { return null; } })();
  const nextLayout = allowed => (LAYOUTS[pinned] && (allowed === 'full' || pinned === 'corner') ? pinned : deck(allowed));
  const off = [];

  // ---- follow ----------------------------------------------------------------
  let follow = null;      // { piece, from, to, capture, mine, quiet, cancelled }
  let weight = 0;
  const aim = new THREE.Vector3(), want = new THREE.Vector3(), base = new THREE.Vector3(), look = new THREE.Vector3(),
    goal = new THREE.Vector3(), dir = new THREE.Vector3();

  const prefs = () => { try { return presentation(); } catch { return {}; } };
  function leastMs() {
    const s = game?.clock?.snapshot?.();
    if (!s || s.untimed || s.total === 0) return Infinity;
    return Math.min(s.w ?? Infinity, s.b ?? Infinity);
  }
  const mineSide = side => !game?.seats || game.seats.includes(side);
  const paused = () => !!globalThis.window?.PBP?.isPaused?.();
  const menuUp = () => !!globalThis.window?.PBP?.door?.isUp?.();

  function canFollow() { return prefs().followCam !== false && !reducedNow() && leastMs() >= 10000 && !menuUp(); }

  function update(dt) {
    if (!rig) return;
    let target = 0;
    if (follow) {
      if (rig.interacting()) follow.cancelled = true;
      const moving = anim?.busy?.() || (rec && !rec.done) || !!replay;
      follow.age += dt;
      follow.quiet = moving || follow.age < FOLLOW.minSec ? 0 : follow.quiet + dt;
      if (follow.quiet > FOLLOW.hold || follow.cancelled || !canFollow()) target = 0;
      else target = follow.mine ? 1 : FOLLOW.opponent;
      if (target === 0 && weight <= 0) follow = null;
    }
    const rate = target > weight ? 1 / FOLLOW.in : 1 / FOLLOW.out;
    weight = target > weight ? Math.min(target, weight + dt * rate) : Math.max(target, weight - dt * rate * (follow?.cancelled ? 3 : 1));
    if (weight <= 0 || !follow) return;
    // Aim: the man, a little along toward his square; a capture keeps the victim in frame.
    const p = follow.piece.position;
    want.copy(p).lerp(follow.to, FOLLOW.lead);
    if (follow.capture) want.lerp(follow.to, .35);
    want.y = .45 + Math.max(0, p.y) * .35;
    if (follow.fresh) { aim.copy(want); follow.fresh = false; }
    else aim.lerp(want, 1 - Math.exp(-FOLLOW.settle * dt));
    const s = rig.state();
    const theta = s.theta + follow.swing, phi = Math.min(FOLLOW.phiMax, s.phi + FOLLOW.lower);
    const r = Math.max(FOLLOW.radiusMin, s.radius * FOLLOW.radius) * (s.fit || 1);
    goal.set(aim.x + Math.sin(theta) * Math.sin(phi) * r, aim.y + Math.cos(phi) * r, aim.z + Math.cos(theta) * Math.sin(phi) * r);
    const e = smooth(clamp01(weight));
    base.copy(camera.position);
    rig.lookTarget(look);
    camera.position.lerpVectors(base, goal, e);
    camera.lookAt(look.lerp(aim, e));
  }

  function startFollow(piece, from, to, capture) {
    if (!canFollow()) { follow = null; return; }
    const s = rig?.state?.() || { theta: 0 };
    dir.copy(to).sub(from).setY(0);
    const len = dir.length() || 1; dir.divideScalar(len);
    const right = Math.cos(s.theta) * dir.x - Math.sin(s.theta) * dir.z;   // across the view
    const along = 1 - Math.abs(right) * .6;
    follow = { piece, from: from.clone(), to: to.clone(), capture, mine: mineSide(piece.userData.side),
      quiet: 0, age: 0, cancelled: false, fresh: weight <= 0, swing: FOLLOW.swing * (right >= 0 ? 1 : -1) * along };
  }

  // ---- recording -----------------------------------------------------------------
  let rec = null;          // { t, frames:[{t, states: Map}], objs:Set, hit, from, to, attacker, victim, done }
  let pendingVictim = null;

  function startRecording(attacker, victim, from, to) {
    rec = { t: 0, frames: [], objs: new Set([attacker, victim].filter(Boolean)), hit: null, from: from.clone(), to: to.clone(),
      attacker, victim, done: false, started: false };
  }
  function record(dt) {
    if (!rec || rec.done) return;
    if (rec.started) rec.t += dt; else rec.started = true;
    for (const p of group.children) if (p.userData.type && p.userData.busy) rec.objs.add(p);
    const states = new Map();
    for (const o of rec.objs) states.set(o, snap(o, group));
    rec.frames.push({ t: rec.t, states });
    const settled = rec.t > .3 && !anim?.busy?.();
    if (settled || rec.t > REC_MAX) {
      rec.done = true;
      const clip = rec; rec = null;
      if (clip.hit != null && settled) begin(clip);
    }
  }
  function frameAt(clip, t) {
    const f = clip.frames;
    let lo = 0, hi = f.length - 1;
    if (t <= f[0].t) return [f[0], null, 0];
    if (t >= f[hi].t) return [f[hi], null, 0];
    while (hi - lo > 1) { const m = (lo + hi) >> 1; if (f[m].t <= t) lo = m; else hi = m; }
    const span = f[hi].t - f[lo].t;
    return [f[lo], f[hi], span > 0 ? (t - f[lo].t) / span : 0];
  }
  function showFrame(clip, t) {
    const [a, b, k] = frameAt(clip, t);
    for (const o of clip.objs) {
      const sa = a.states.get(o) || b?.states.get(o);
      if (!sa) continue;
      applySnap(o, sa, b?.states.get(o) || null, k, group, false);
    }
  }

  // ---- replay ----------------------------------------------------------------------
  let replay = null;       // { clip, layout, t, end, perp, dN, mid }
  function allowedNow() {
    return replayAllowed({ enabled: prefs().replays !== false, reduced: reducedNow(), online: !!game?.isOnline, leastMs: leastMs() });
  }
  function begin(clip) {
    const allowed = allowedNow();
    if (allowed === 'off' || menuUp()) return;
    const layout = nextLayout(allowed);
    const dN = clip.to.clone().sub(clip.from).setY(0);
    if (dN.lengthSq() < 1e-6) dN.set(0, 0, -1);
    dN.normalize();
    const mid = clip.from.clone().add(clip.to).multiplyScalar(.5); mid.y = 0;
    const perp = new THREE.Vector3(dN.z, 0, -dN.x);
    if (perp.dot(camera.position.clone().sub(mid)) < 0) perp.negate();   // the side the player is looking from
    replay = { clip: { ...clip, duration: clip.t }, layout, t: 0, end: replayLength(layout) - REPLAY.exit, dN, mid, perp };
    bus?.emit?.('replay-show', { layout });
  }
  function skip() { if (replay && replay.t < replay.end) replay.end = replay.t; }
  function cancel() { replay = null; rec = null; follow = null; weight = 0; pendingVictim = null; paintDom(null); }

  // cameras for the three shots
  const cams = { low: new THREE.PerspectiveCamera(34, 1, .05, 120), chase: new THREE.PerspectiveCamera(48, 1, .05, 120),
    top: new THREE.PerspectiveCamera(32, 1, .05, 120) };
  const at = new THREE.Vector3();
  function aimShot(shot, cam, clipT) {
    const { clip, dN, mid, perp } = replay;
    const prog = clamp01((clipT - (clip.hit - 1)) / 2.4);
    const to = clip.to;
    if (shot === 'low') {
      cam.position.copy(mid).addScaledVector(perp, 3.3 - .5 * prog).addScaledVector(dN, -.3 + .7 * prog); cam.position.y = .55;
      at.copy(mid).addScaledVector(dN, .45 * prog); at.y = .6;
    } else if (shot === 'chase') {
      const a = clip.attacker.position;
      cam.position.copy(a).addScaledVector(dN, -2.1).addScaledVector(perp, .55); cam.position.y = 1.35 + Math.max(0, a.y) * .4;
      at.copy(to); at.y = .6;
    } else {
      cam.position.copy(to).addScaledVector(dN, -1.25).addScaledVector(perp, .7 + .6 * prog); cam.position.y = 6.9 - 1.1 * prog;
      at.copy(to).addScaledVector(dN, -.4); at.y = 0;
    }
    clearOfMen(cam.position, shot === 'low' ? perp : shot === 'chase' ? dN.clone().negate() : null, clip);
    cam.lookAt(at);
  }
  // A replay camera must never stand inside a man: his outline shell would wrap the
  // lens and wash the whole panel out. Step back along `away`, then up, until clear.
  function clearOfMen(pos, away, clip) {
    const blocked = () => group.children.some(p => p.userData.type && p !== clip.attacker && p !== clip.victim
      && Math.hypot(p.position.x - pos.x, p.position.z - pos.z) < .8 && pos.y < 2.1);
    for (let i = 0; i < 4 && away && blocked(); i++) pos.addScaledVector(away, .4);
    for (let i = 0; i < 6 && blocked(); i++) pos.y += .35;
  }

  // render targets + masked quads
  const comp = new THREE.Scene(), compCam = new THREE.Camera(), quadGeo = new THREE.PlaneGeometry(2, 2);
  const targets = [], quads = [];
  const buf = new THREE.Vector2();
  function ensureTargets(bw, bh) {
    while (targets.length < 3) {
      const rt = new THREE.WebGLRenderTarget(bw, bh, { type: THREE.HalfFloatType, samples: 4 });
      const mat = new THREE.ShaderMaterial({
        uniforms: { map: { value: rt.texture }, res: { value: new THREE.Vector2(bw, bh) },
          planes: { value: Array.from({ length: 6 }, () => new THREE.Vector3()) }, count: { value: 0 },
          flash: { value: 0 }, light: { value: 1 } },
        vertexShader: VERT, fragmentShader: FRAG, depthTest: false, depthWrite: false,
      });
      const q = new THREE.Mesh(quadGeo, mat); q.frustumCulled = false; q.renderOrder = targets.length; q.visible = false;
      comp.add(q); targets.push(rt); quads.push(q);
    }
    for (let i = 0; i < 3; i++) {
      if (targets[i].width !== bw || targets[i].height !== bh) targets[i].setSize(bw, bh);
      quads[i].material.uniforms.res.value.set(bw, bh);
    }
  }
  function planesFor(pts, bw, bh) {
    const P = pts.map(([u, v]) => [u * bw, (1 - v) * bh]);
    let area = 0;
    for (let i = 0; i < P.length; i++) { const a = P[i], b = P[(i + 1) % P.length]; area += a[0] * b[1] - b[0] * a[1]; }
    const sg = area >= 0 ? 1 : -1;
    return P.map((a, i) => { const b = P[(i + 1) % P.length]; const nx = -(b[1] - a[1]) * sg, ny = (b[0] - a[0]) * sg; return [nx, ny, -(nx * a[0] + ny * a[1])]; });
  }

  // ---- DOM chrome ------------------------------------------------------------------
  const host = root || document.getElementById('stage');
  const dom = document.createElement('div');
  dom.className = 'pbp-replay'; dom.hidden = true; dom.setAttribute('aria-hidden', 'true');
  dom.innerHTML = '<svg class="pbp-replay-seams" preserveAspectRatio="none"></svg>'
    + '<div class="pbp-replay-chip"><i></i>REPLAY</div><div class="pbp-replay-skip">Click to skip</div>';
  host?.appendChild(dom);
  const seams = dom.querySelector('svg'), chip = dom.querySelector('.pbp-replay-chip'), hint = dom.querySelector('.pbp-replay-skip');
  function paintDom(frame) {
    if (!frame) { dom.hidden = true; seams.innerHTML = ''; return; }
    dom.hidden = false;
    const { w, h, polys, alpha, layout, portrait } = frame;
    seams.setAttribute('viewBox', `0 0 ${w} ${h}`);
    seams.innerHTML = polys.map(({ pts, lit }) => {
      const d = pts.map(([x, y]) => `${(x * w).toFixed(1)},${(y * h).toFixed(1)}`).join(' ');
      return `<polygon points="${d}" class="seam${layout === 'corner' ? ' corner' : ''}"/>`
        + (lit || layout === 'corner' ? `<polygon points="${d}" class="lit"/>` : '');
    }).join('');
    chip.style.opacity = hint.style.opacity = alpha;
    if (layout === 'corner') {
      const box = orient(LAYOUTS.corner[0], portrait);
      const xs = box.map(p => p[0]), ys = box.map(p => p[1]);
      chip.style.left = `${Math.min(...xs) * w + 10}px`; chip.style.top = `${Math.min(...ys) * h - 34}px`;
      hint.style.left = `${Math.min(...xs) * w + 10}px`; hint.style.top = `${Math.max(...ys) * h + 8}px`;
      hint.style.right = 'auto'; hint.style.bottom = 'auto';
    } else {
      chip.style.left = '16px'; chip.style.top = '16px';
      hint.style.left = 'auto'; hint.style.top = 'auto'; hint.style.right = '16px'; hint.style.bottom = '16px';
    }
  }

  // ---- the frame, after the board is drawn ---------------------------------------
  function afterRender(dt) {
    record(dt);
    if (!replay) return;
    if (menuUp()) { cancel(); return; }
    if (!paused()) replay.t += dt;
    const { layout, clip } = replay;
    const n = panelCount(layout);
    if (replay.t > replay.end + REPLAY.exit + .05) { replay = null; paintDom(null); bus?.emit?.('replay-done', {}); return; }
    renderer.getDrawingBufferSize(buf);
    const bw = buf.x, bh = buf.y, portrait = bw < bh;
    ensureTargets(bw, bh);
    const live = new Map();
    for (const o of clip.objs) live.set(o, snap(o, group));
    // Floating points (motes, the spiral's dust) are sized for the main camera; a
    // low replay camera sits among them and they wash the panel out. Off for the panels.
    const points = [];
    view.scene.traverseVisible(o => { if (o.isPoints) points.push(o); });
    for (const o of points) o.visible = false;
    const polys = [];
    const prevTarget = renderer.getRenderTarget();
    for (let i = 0; i < 3; i++) quads[i].visible = false;
    try {
      for (let i = 0; i < n; i++) {
        const st = panelState(layout, i, replay.t, replay.end);
        if (st.scale < .002) continue;
        const full = orient(LAYOUTS[layout][i], portrait);
        const { pts } = scalePoly(full, st.scale);
        const cx = full.reduce((a, p) => a + p[0], 0) / full.length, cy = full.reduce((a, p) => a + p[1], 0) / full.length;
        const clipT = clipTime(layout, i, replay.t, clip);
        showFrame(clip, clipT);
        const cam = cams[SHOTS[layout][i]];
        cam.aspect = bw / bh;
        aimShot(SHOTS[layout][i], cam, clipT);
        cam.setViewOffset(bw, bh, bw / 2 - cx * bw, bh / 2 - cy * bh, bw, bh);
        cam.updateProjectionMatrix();
        renderer.setRenderTarget(targets[i]);
        renderer.clear();
        renderer.render(view.scene, cam);
        cam.clearViewOffset();
        const q = quads[i], u = q.material.uniforms, pl = planesFor(pts, bw, bh);
        pl.forEach((v, j) => u.planes.value[j].set(v[0], v[1], v[2]));
        u.count.value = pl.length; u.flash.value = st.flash; u.light.value = st.light;
        q.visible = true;
        polys.push({ pts, lit: st.lit });
      }
    } finally {
      for (const [o, s] of live) applySnap(o, s, null, 0, group, true);
      for (const o of points) o.visible = true;
      renderer.setRenderTarget(prevTarget);
    }
    const auto = renderer.autoClear;
    renderer.autoClear = false;
    renderer.render(comp, compCam);
    renderer.autoClear = auto;
    const a = clamp01(replay.t / .2) * (1 - clamp01((replay.t - replay.end) / .25));
    const rect = view.renderer.domElement.getBoundingClientRect();
    paintDom({ w: rect.width, h: rect.height, polys, alpha: a, layout, portrait });
  }

  // ---- wiring ------------------------------------------------------------------------
  const hooks = anim?.hooks;
  const baseMoved = hooks?.onMoved, baseCaptured = hooks?.onCaptured;
  if (hooks) {
    hooks.onCaptured = (piece) => {
      pendingVictim = piece;
      if (rec && !rec.victim && !rec.started) { rec.victim = piece; rec.objs.add(piece); }
      return baseCaptured?.(piece);
    };
    hooks.onMoved = (piece, was, origin) => {
      const to = piece.position.clone(), from = (origin || was).clone();
      const r = baseMoved?.(piece, was, origin);
      noteMove(piece, from, to);
      return r;
    };
  }
  function noteMove(piece, from, to) {
    if (replay) { replay = null; paintDom(null); }      // the game moved on; so do we
    const victim = pendingVictim && pendingVictim.userData.side !== piece.userData.side ? pendingVictim : null;
    pendingVictim = null;
    if (rec && !rec.done) rec = null;
    if (victim) startRecording(piece, victim, from, to);
    startFollow(piece, from, to, !!victim);
  }
  off.push(bus?.on?.('hit', () => { if (rec && !rec.done && rec.hit == null) rec.hit = rec.t; }));
  for (const name of ['local', 'newgame', 'menu', 'takeback', 'gameover']) off.push(bus?.on?.(name, cancel));
  const onDown = () => skip();
  const onKey = e => { if (!e.ctrlKey && !e.altKey && !e.metaKey) skip(); };
  globalThis.window?.addEventListener('pointerdown', onDown, true);
  globalThis.window?.addEventListener('keydown', onKey, true);

  return {
    update, afterRender, skip, cancel,
    /** True while a full-screen replay owns the view: the turn card and the computer wait for it. */
    holding() {
      if (replay) return replay.layout !== 'corner';
      return !!(rec && !rec.done && rec.hit != null && allowedNow() === 'full');
    },
    active: () => !!replay,
    stats: () => ({ follow: follow ? { weight, capture: follow.capture, mine: follow.mine } : null,
      recording: rec ? { t: rec.t, frames: rec.frames.length, hit: rec.hit } : null,
      replay: replay ? { layout: replay.layout, t: replay.t, end: replay.end, clip: replay.clip.duration, hit: replay.clip.hit } : null }),
    dispose() {
      for (const f of off) f?.();
      globalThis.window?.removeEventListener('pointerdown', onDown, true);
      globalThis.window?.removeEventListener('keydown', onKey, true);
      if (hooks) { hooks.onMoved = baseMoved; hooks.onCaptured = baseCaptured; }
      for (const rt of targets) rt.dispose();
      for (const q of quads) q.material.dispose();
      quadGeo.dispose();
      dom.remove();
    },
  };
}
