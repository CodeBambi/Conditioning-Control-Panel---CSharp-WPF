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
 * board/replay-plan.js. Panels slide in from their own edge with a swing past
 * the seat, take their hit (punch, shake, chromatic split, speed lines or a
 * halftone ring, an impact word, a flash) and slide back out, faster on a skip;
 * the look is one shader pass per panel, board/replay-fx.js. The HUD fades down
 * behind a full-screen replay. A click, a tap or a key skips it, and the board keeps
 * taking input throughout. Online and a low clock get the corner only; under
 * 10 s, reduced motion, or the option off, nothing.
 *
 * Nothing here touches rules, clocks or the game: it is presentation only.
 * ==========================================================================*/

import * as THREE from 'three';
import { LAYOUTS, clipTime, panelState, panelCount, replayLength, replayAllowed,
  createLayoutDeck, placePoly, offStage, exitLength, orient, REPLAY, hitAt, lensZoom } from './replay-plan.js';
import { REPLAY_VERT, REPLAY_FRAG, burstStyle, impactWords, boil, panelRank, inkLayers } from './replay-fx.js';
import { createReplayShots } from './replay-shots.js';
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
let inkSerial = 0;  // one set of seam masks per director

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
    if (allowed === 'off' || menuUp() || paused()) return;   // never starts under the pause card (CHESS-3)
    const layout = nextLayout(allowed);
    const dN = clip.to.clone().sub(clip.from).setY(0);
    if (dN.lengthSq() < 1e-6) dN.set(0, 0, -1);
    dN.normalize();
    const mid = clip.from.clone().add(clip.to).multiplyScalar(.5); mid.y = 0;
    const perp = new THREE.Vector3(dN.z, 0, -dN.x);
    if (perp.dot(camera.position.clone().sub(mid)) < 0) perp.negate();   // the side the player is looking from
    const full = { ...clip, duration: clip.t };
    const impact = clip.hitInfo?.impact;
    replay = { clip: full, layout, t: 0, prevT: -1, end: replayLength(layout) - REPLAY.exit, dN, mid, perp, exiting: false,
      shots: shots.pick(layout, full), words: impactWords(impact, random), bstyle: burstStyle(impact), seed: Math.floor(random() * 997),
      tilt: [0, 1, 2].map(() => (random() - .5) * 16), pinned: null };
    hudDown(true, layout === 'corner');
    chip.classList.remove('stamp'); void chip.offsetWidth; chip.classList.add('stamp');
    bus?.emit?.('replay-show', { layout, n: panelCount(layout), hit: clip.hitInfo || null });
  }
  function skip() { if (replay && replay.t < replay.end) { replay.end = replay.t; replay.skipped = true; } }
  // Replay beats for sound and effects, fired once each as presentation time crosses them:
  //   replay-show {layout,n,hit}  replay-panel-in {i,n,layout}  replay-panel-hit {i,n,layout,hit}
  //   replay-exit {layout,skipped}  replay-done {} ({cancelled:true} when cut short)
  // `hit` is the original capture's own 'hit' payload (piece, sound, impact, victim...).
  function beats(prev, t) {
    const { layout, clip } = replay, n = panelCount(layout);
    const crossed = x => prev < x && t >= x;
    for (let i = 0; i < n; i++) {
      if (crossed(i * REPLAY.enterGap)) bus?.emit?.('replay-panel-in', { i, n, layout });
      if (!replay.skipped && crossed(hitAt(layout, i)) && t < replay.end) bus?.emit?.('replay-panel-hit', { i, n, layout, hit: clip.hitInfo || null });
    }
    if (!replay.exiting && t >= replay.end) { replay.exiting = true; hudDown(false); bus?.emit?.('replay-exit', { layout, skipped: !!replay.skipped }); }
  }
  // A replay cut short (the game moved on, a new game, the menu) still says it is
  // done, so the sound lane's duck and tails let go.
  function dropReplay() {
    if (!replay) return;
    replay = null; paintDom(null);
    bus?.emit?.('replay-done', { cancelled: true });
  }
  function cancel() { dropReplay(); rec = null; follow = null; weight = 0; pendingVictim = null; paintDom(null); }
  // The HUD (clocks, camera buttons, hints) steps back behind a full-screen replay; behind the
  // corner inset only the Options pill does, as it sits over the inset (bug hunt 2026-09-29, CHESS-8).
  function hudDown(on, corner = false) {
    globalThis.document?.body?.classList.toggle('pbp-replay-full', !!on && !corner);
    globalThis.document?.body?.classList.toggle('pbp-replay-corner', !!on && corner);
  }

  // cameras, one per panel; where they stand is board/replay-shots.js
  const shots = createReplayShots({ group, random });
  const cams = [0, 1, 2].map(() => new THREE.PerspectiveCamera(40, 1, .05, 120));

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
          flash: { value: 0 }, light: { value: 1 }, desat: { value: 0 }, zoom: { value: 1 }, chroma: { value: 0 },
          burst: { value: -1 }, bstyle: { value: 0 }, wipe: { value: -1 }, time: { value: 0 }, seed: { value: 0 },
          radius: { value: 1 }, grain: { value: .024 }, center: { value: new THREE.Vector2() }, wipeDir: { value: new THREE.Vector2(1, 0) } },
        vertexShader: REPLAY_VERT, fragmentShader: REPLAY_FRAG, depthTest: false, depthWrite: false,
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
    + '<b class="pbp-replay-word"></b><b class="pbp-replay-word"></b><b class="pbp-replay-word"></b>'
    + '<div class="pbp-replay-chip"><i></i>REPLAY</div><div class="pbp-replay-skip">Click to skip</div>';
  host?.appendChild(dom);
  const seams = dom.querySelector('svg'), chip = dom.querySelector('.pbp-replay-chip'), hint = dom.querySelector('.pbp-replay-skip');
  const inkId = `pbp-ink-${++inkSerial}`;   // mask ids, unique in the page
  const words = [...dom.querySelectorAll('.pbp-replay-word')];
  function paintDom(frame) {
    if (!frame) {
      dom.hidden = true; seams.innerHTML = ''; hudDown(false); chip.classList.remove('stamp');
      for (const w of words) w.style.opacity = 0;
      return;
    }
    dom.hidden = false;
    const { w, h, polys, alpha, layout, t } = frame;
    seams.setAttribute('viewBox', `0 0 ${w} ${h}`);
    const corner = layout === 'corner';
    // ink seams: drawn on as the panel lands, boiling a little, retracting on the way out
    const outline = new Map(polys.map(({ i, pts, seat }) => [i, pts.map(([x, y], v) => {
      const [bx, by] = boil(seat[v][0], seat[v][1], t, corner ? 1 : 1.6);
      return `${(x * w + bx).toFixed(1)},${(y * h + by).toFixed(1)}`;
    }).join(' ')]));
    // Painted in the pictures' own order, and each seam masked by the panels above
    // it, so a panel's edge slides under a neighbour instead of across its picture.
    const box = `x="-64" y="-64" width="${(w + 128).toFixed(0)}" height="${(h + 128).toFixed(0)}"`;
    let defs = '', ink = '';
    for (const { poly: { i, lit, seam }, over } of inkLayers(polys)) {
      const d = outline.get(i);
      let mask = '';
      if (over.length) {
        const id = `${inkId}-${i}`;
        defs += `<mask id="${id}" maskUnits="userSpaceOnUse" ${box}><rect ${box} fill="#fff"/>`
          + over.map(o => `<polygon points="${outline.get(o.i)}" fill="#000"/>`).join('') + '</mask>';
        mask = ` mask="url(#${id})"`;
      }
      const dash = `stroke-dasharray="1 1" stroke-dashoffset="${(1 - seam).toFixed(3)}"`;
      ink += `<g${mask}><polygon points="${d}" pathLength="1" ${dash} class="seam${corner ? ' corner' : ''}"/>`
        + (lit || corner ? `<polygon points="${d}" pathLength="1" ${dash} class="lit"/>` : '') + '</g>';
    }
    seams.innerHTML = (defs ? `<defs>${defs}</defs>` : '') + ink;
    chip.style.opacity = hint.style.opacity = alpha;
    // the impact word rides its own panel, up and to one side of the action
    words.forEach((el, i) => {
      const p = polys.find(q => q.i === i);
      if (!p || !(p.word.alpha > 0)) { el.style.opacity = 0; return; }
      const xs = p.pts.map(q => q[0]), ys = p.pts.map(q => q[1]);
      const pw = (Math.max(...xs) - Math.min(...xs)) * w, ph = (Math.max(...ys) - Math.min(...ys)) * h;
      const size = Math.max(26, Math.min(120, Math.min(pw, ph) * (corner ? .24 : .2)));
      const side = i % 2 ? 1 : -1, text = frame.words[i] || 'POP';
      // up and to one side of the action, but always wholly inside its panel
      const hw = size * .36 * text.length + 10, hh = size * .6 + 8;
      const x0 = Math.min(...xs) * w + hw, x1 = Math.max(...xs) * w - hw, y0 = Math.min(...ys) * h + hh, y1 = Math.max(...ys) * h - hh;
      const fit = (v, a, b) => (a > b ? (a + b) / 2 : Math.max(a, Math.min(b, v)));
      el.textContent = text;
      el.style.fontSize = `${size.toFixed(0)}px`;
      el.style.left = `${fit(p.cx * w + side * pw * .18, x0, x1).toFixed(1)}px`;
      el.style.top = `${fit(p.cy * h - ph * .26, y0, y1).toFixed(1)}px`;
      el.style.opacity = p.word.alpha.toFixed(3);
      el.style.transform = `translate(-50%, -50%) rotate(${(frame.tilt[i] || 0).toFixed(1)}deg) scale(${p.word.scale.toFixed(3)})`;
    });
    if (corner && polys[0]) {
      const xs = polys[0].pts.map(p => p[0]), ys = polys[0].pts.map(p => p[1]);
      chip.style.left = `${Math.min(...xs) * w + 10}px`; chip.style.top = `${Math.min(...ys) * h - 34}px`;
      hint.style.left = `${Math.min(...xs) * w + 10}px`; hint.style.top = `${Math.max(...ys) * h + 8}px`;
      hint.style.right = 'auto'; hint.style.bottom = 'auto';
    } else if (!corner) {
      chip.style.left = '16px'; chip.style.top = '16px';
      hint.style.left = 'auto'; hint.style.top = 'auto'; hint.style.right = '16px'; hint.style.bottom = '16px';
    }
  }

  // ---- the spotlight ---------------------------------------------------------------
  // In a replay only the board and the two men in the fight keep their light
  // (owner, 2026-09-29): the room, the walls and every other man sink to REST.
  // Colours are scaled for the panel renders and put back straight after.
  const REST_DIM = .68;
  const saved = new Map();
  function dimRest(clip, k) {
    saved.clear();
    if (k >= .999) return () => {};
    const keep = new Set([view.boardGroup, clip.attacker, clip.victim].filter(Boolean));
    const visit = o => {
      if (keep.has(o) || o.isPoints) return;
      if (o.isMesh || o.isLine || o.isSprite) {
        for (const m of Array.isArray(o.material) ? o.material : [o.material]) {
          if (!m || saved.has(m)) continue;
          saved.set(m, [m.color?.clone(), m.emissive?.clone(), m.envMapIntensity]);
          m.color?.multiplyScalar(k); m.emissive?.multiplyScalar(k);
          if (typeof m.envMapIntensity === 'number') m.envMapIntensity *= k;
        }
      }
      for (const c of o.children) visit(c);
    };
    visit(view.scene);
    const bg = view.scene.background?.isColor ? view.scene.background.clone() : null;
    if (bg) view.scene.background.multiplyScalar(k);
    return () => {
      for (const [m, [c, e, env]] of saved) { if (c) m.color.copy(c); if (e) m.emissive.copy(e); if (typeof env === 'number') m.envMapIntensity = env; }
      saved.clear();
      if (bg) view.scene.background.copy(bg);
    };
  }

  // ---- the frame, after the board is drawn ---------------------------------------
  function afterRender(dt) {
    record(dt);
    if (!replay) return;
    if (menuUp()) { cancel(); return; }
    if (replay.pinned != null) replay.t = replay.pinned;
    else if (!paused()) replay.t += dt;
    beats(replay.prevT, replay.t); replay.prevT = replay.t;
    const { layout, clip } = replay;
    const n = panelCount(layout);
    if (replay.t > replay.end + exitLength(replay.skipped) + .05) { replay = null; paintDom(null); bus?.emit?.('replay-done', {}); return; }
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
    // The men in the fight are bent by their shader (board/silicone.js), so their
    // meshes' bounds are the upright rest pose: a close lens on the victim culled
    // the king's whole body while his head was in its frame. Never culled in a panel.
    const uncull = [];
    for (const o of clip.objs) o.traverse(m => { if (m.isMesh && m.frustumCulled) { m.frustumCulled = false; uncull.push(m); } });
    const polys = [];
    const undim = dimRest(clip, 1 - REST_DIM * clamp01(replay.t / .2) * (1 - clamp01((replay.t - replay.end) / .25)));
    const prevTarget = renderer.getRenderTarget();
    for (let i = 0; i < 3; i++) quads[i].visible = false;
    try {
      for (let i = 0; i < n; i++) {
        const st = panelState(layout, i, replay.t, replay.end, { portrait, skipped: !!replay.skipped });
        const full = orient(LAYOUTS[layout][i], portrait);
        const placed = placePoly(full, st.scale, st.ox, st.oy);
        if (st.scale < .002 || offStage(placed.pts)) continue;
        const pts = placed.pts;
        const cx = full.reduce((a, p) => a + p[0], 0) / full.length, cy = full.reduce((a, p) => a + p[1], 0) / full.length;
        const clipT = clipTime(layout, i, replay.t, clip);
        showFrame(clip, clipT);
        const cam = cams[i], shot = replay.shots[i];
        const xs = full.map(p => p[0]), ys = full.map(p => p[1]);
        const pw = Math.max(...xs) - Math.min(...xs), ph = Math.max(...ys) - Math.min(...ys);
        const ctx = { clip, clipT, t: replay.t, i, n, layout, dN: replay.dN, mid: replay.mid, perp: replay.perp, bw, bh,
          panel: { cx, cy, w: pw, h: ph } };
        cam.aspect = bw / bh;
        shots.aim(shot, cam, ctx);
        cam.fov *= lensZoom(clipT - clip.hit);
        // the picture travels with its panel: the lens centres on where the panel IS
        cam.setViewOffset(bw, bh, bw / 2 - placed.cx * bw, bh / 2 - placed.cy * bh, bw, bh);
        cam.updateProjectionMatrix();
        const hide = shots.occluders(shot, cam, ctx) || [];
        const wasVisible = hide.map(o => o.visible);
        for (const o of hide) o.visible = false;
        renderer.setRenderTarget(targets[i]);
        renderer.clear();
        try { renderer.render(view.scene, cam); }
        finally { hide.forEach((o, k) => { o.visible = wasVisible[k]; }); }
        cam.clearViewOffset();
        const q = quads[i], u = q.material.uniforms, pl = planesFor(pts, bw, bh);
        pl.forEach((v, j) => u.planes.value[j].set(v[0], v[1], v[2]));
        u.count.value = pl.length; u.flash.value = st.flash; u.light.value = st.light; u.desat.value = st.desat;
        u.zoom.value = st.zoom; u.chroma.value = st.chroma; u.burst.value = st.burst; u.bstyle.value = replay.bstyle;
        u.wipe.value = st.wipe; u.time.value = replay.t; u.seed.value = replay.seed + i * 13;
        u.radius.value = .5 * Math.max(pw * st.scale * bw, ph * st.scale * bh);
        u.center.value.set(placed.cx * bw, (1 - placed.cy) * bh);
        u.wipeDir.value.set(-st.dir[0], st.dir[1]);
        q.renderOrder = panelRank(i, st.lit);  // the panel taking its hit punches over its neighbours
        q.visible = true;
        polys.push({ i, pts, seat: full, cx: placed.cx, cy: placed.cy, lit: st.lit, seam: st.seam,
          word: st.lit || n === 1 ? st.word : { alpha: 0, scale: 0 } });
      }
    } finally {
      for (const [o, s] of live) applySnap(o, s, null, 0, group, true);
      for (const m of uncull) m.frustumCulled = true;
      for (const o of points) o.visible = true;
      undim();
      renderer.setRenderTarget(prevTarget);
    }
    const auto = renderer.autoClear;
    renderer.autoClear = false;
    renderer.render(comp, compCam);
    renderer.autoClear = auto;
    const a = clamp01(replay.t / .12) * (1 - clamp01((replay.t - replay.end) / (exitLength(replay.skipped) * .8)));
    const rect = view.renderer.domElement.getBoundingClientRect();
    paintDom({ w: rect.width, h: rect.height, polys, alpha: a, layout, portrait, t: replay.t, words: replay.words, tilt: replay.tilt });
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
    hooks.onMoved = (piece, was, origin, motion) => {
      const to = piece.position.clone(), from = (origin || was).clone();
      const r = baseMoved?.(piece, was, origin, motion);
      noteMove(piece, from, to);
      return r;
    };
  }
  function noteMove(piece, from, to) {
    dropReplay();                                        // the game moved on; so do we
    const victim = pendingVictim && pendingVictim.userData.side !== piece.userData.side ? pendingVictim : null;
    pendingVictim = null;
    if (rec && !rec.done) rec = null;
    if (victim) startRecording(piece, victim, from, to);
    startFollow(piece, from, to, !!victim);
  }
  off.push(bus?.on?.('hit', p => {
    if (rec && !rec.done && rec.hit == null) { rec.hit = rec.t; rec.hitInfo = p ? { ...p } : null; }
  }));
  for (const name of ['local', 'newgame', 'menu', 'takeback', 'gameover']) off.push(bus?.on?.(name, cancel));
  const onDown = () => skip();
  const onKey = e => { if (!e.ctrlKey && !e.altKey && !e.metaKey) skip(); };
  globalThis.window?.addEventListener('pointerdown', onDown, true);
  globalThis.window?.addEventListener('keydown', onKey, true);

  return {
    update, afterRender, skip, cancel,
    /** The pause: cut a replay at once (a skip's exit waits for the clock, so it froze on screen) and
     *  forget a capture still being recorded, so no replay starts under the card (bug hunt 2026-09-29, CHESS-3). */
    drop() { dropReplay(); rec = null; },
    /** Hold the replay at presentation time t (null lets it run again): a screenshot harness's seek. */
    pin(t) { if (replay) replay.pinned = t == null ? null : Math.max(0, +t); return !!replay; },
    /** True while a full-screen replay owns the view: the turn card and the computer wait for it. */
    holding() {
      if (replay) return replay.layout !== 'corner';
      return !!(rec && !rec.done && rec.hit != null && allowedNow() === 'full');
    },
    active: () => !!replay,
    stats: () => ({ follow: follow ? { weight, capture: follow.capture, mine: follow.mine } : null,
      recording: rec ? { t: rec.t, frames: rec.frames.length, hit: rec.hit } : null,
      replay: replay ? { layout: replay.layout, t: replay.t, end: replay.end, clip: replay.clip.duration, hit: replay.clip.hit, shots: replay.shots } : null }),
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
