/* ============================================================================
 * boonPick.js - the between-loop boon draft, staged INSIDE the tube.
 *
 * The old draft was a flat DOM overlay (game/overlays.js showDraft) that froze
 * the world behind a web form. This presenter keeps you in the fall: the tube
 * parks, 3 (or 4) boon cards hang ahead with borders/FX themed to each boon's
 * rarity + category, and you CLICK one to shatter it and drop through it. If you
 * don't pick in time the cards fade and you bank +1 resistance (the shield flies
 * up into the HUD, driven by the game layer).
 *
 * It is a sibling of engine/junctions.js and reuses the same proven pieces:
 *   - nav.setForwardHold(true, atDepth) parks the fall at the pick (junctions.js)
 *   - a 4x4 shard shatter (ported from junctions.spawnShatter)
 *   - fx.pulseFlash + nav.fovKick = "the Pow" on the dive-through
 *   - additive Points sparkles / jagged LineSegments bolts (fx.js primitives)
 *
 * The game keeps its existing callback contract: open({... onPick, onSkip,
 * onReroll ...}) mirrors overlays.showDraft, so chaosRun.resolveDraft/applyBoon
 * are untouched. onPick(boon,false) on a click; onSkip(true) on timeout.
 * ==========================================================================*/

import * as THREE from 'three';
import { boonTheme } from '../game/boons.js';

const ART = 'https://ccp.art/';       // boon art lives on the CDN (matches overlays.js)

const CARD_AHEAD = 8;                   // camera-to-card gap (was 13; closer = the Pow lands right on them)
const STOP_AHEAD = 4;                   // the fall parks this far ahead of where it was (settles motion)
const CARD_W = 2.4, CARD_H = 3.05;     // card content size (portrait, like the DOM boon cards)
const ROW_SPREAD = 3.35;               // centre-to-centre for a 3-across row
const GRID_X = 1.9, GRID_Y = 2.0;      // 2x2 half-offsets
const GRID_SCALE = 0.82;               // 2x2 cards a touch smaller so they clear the bore

// theme -> palette + which extra FX the card wears
const THEMES = {
  sin:      { frame: 0xff2b4a, glow: 0xff506a, look: 'crack' },
  electric: { frame: 0x8fdcff, glow: 0xbfefff, look: 'bolt'  },
  rabbit:   { frame: 0xc9a2ff, glow: 0xe6c6ff, look: 'pulse' },
  duo:      { frame: 0xffd76a, glow: 0xffe9a8, look: 'glow'  },
  rare:     { frame: 0xc178ff, glow: 0xe0b3ff, look: 'glow'  },
  uncommon: { frame: 0x66e0d0, glow: 0xa9f0e6, look: 'glow'  },
  common:   { frame: 0xccd6e6, glow: 0xeef2f8, look: 'glow'  },
};
const themeOf = (b) => THEMES[boonTheme(b)] || THEMES.common;

const clamp = (v, a, b) => Math.min(b, Math.max(a, v));

// ---- shared canvas textures (built once) -----------------------------------
function makeGlowTex() {
  const s = 128, c = document.createElement('canvas'); c.width = c.height = s;
  const x = c.getContext('2d');
  const g = x.createRadialGradient(s / 2, s / 2, 0, s / 2, s / 2, s / 2);
  g.addColorStop(0, 'rgba(255,255,255,0.9)');
  g.addColorStop(0.4, 'rgba(255,255,255,0.35)');
  g.addColorStop(1, 'rgba(255,255,255,0)');
  x.fillStyle = g; x.fillRect(0, 0, s, s);
  return new THREE.CanvasTexture(c);
}
function makeDotTex() {
  const s = 64, c = document.createElement('canvas'); c.width = c.height = s;
  const x = c.getContext('2d');
  const g = x.createRadialGradient(s / 2, s / 2, 0, s / 2, s / 2, s / 2);
  g.addColorStop(0, 'rgba(255,255,255,1)');
  g.addColorStop(0.35, 'rgba(255,255,255,0.5)');
  g.addColorStop(1, 'rgba(255,255,255,0)');
  x.fillStyle = g; x.beginPath(); x.arc(s / 2, s / 2, s / 2, 0, Math.PI * 2); x.fill();
  return new THREE.CanvasTexture(c);
}
// a rounded-rect border, transparent inside, white so it can be tinted per theme
function makeFrameTex() {
  const w = 256, h = 320, c = document.createElement('canvas'); c.width = w; c.height = h;
  const x = c.getContext('2d');
  const r = 26, pad = 8, lw = 12;
  x.strokeStyle = '#ffffff'; x.lineWidth = lw; x.lineJoin = 'round';
  x.beginPath();
  x.moveTo(pad + r, pad);
  x.arcTo(w - pad, pad, w - pad, h - pad, r);
  x.arcTo(w - pad, h - pad, pad, h - pad, r);
  x.arcTo(pad, h - pad, pad, pad, r);
  x.arcTo(pad, pad, w - pad, pad, r);
  x.closePath(); x.stroke();
  return new THREE.CanvasTexture(c);
}
// red jagged cracks over the sin cards (additive, tinted at material level)
function makeCrackTex() {
  const w = 256, h = 320, c = document.createElement('canvas'); c.width = w; c.height = h;
  const x = c.getContext('2d');
  x.strokeStyle = '#ffffff'; x.lineWidth = 2.4; x.lineCap = 'round';
  const seeds = 5;
  for (let s = 0; s < seeds; s++) {
    let px = 40 + Math.random() * (w - 80);
    let py = 30 + Math.random() * (h - 60);
    const branches = 2 + (Math.random() * 3 | 0);
    for (let br = 0; br < branches; br++) {
      x.globalAlpha = 0.5 + Math.random() * 0.4;
      x.beginPath(); x.moveTo(px, py);
      let cx = px, cy = py;
      const steps = 4 + (Math.random() * 5 | 0);
      const ang0 = Math.random() * Math.PI * 2;
      for (let i = 0; i < steps; i++) {
        const a = ang0 + (Math.random() - 0.5) * 1.6;
        const len = 12 + Math.random() * 26;
        cx += Math.cos(a) * len; cy += Math.sin(a) * len;
        x.lineTo(cx, cy);
      }
      x.stroke();
    }
  }
  x.globalAlpha = 1;
  return new THREE.CanvasTexture(c);
}
// fallback content when a boon PNG is missing/blocked: a theme-tinted name panel
// (canvas -> local texture, so no CORS taint). Mirrors the DOM img.onerror drop.
function makeNamePanelTex(boon, hexFrame) {
  const w = 256, h = 320, c = document.createElement('canvas'); c.width = w; c.height = h;
  const x = c.getContext('2d');
  const col = '#' + ('000000' + (hexFrame >>> 0).toString(16)).slice(-6);
  const g = x.createLinearGradient(0, 0, 0, h);
  g.addColorStop(0, 'rgba(24,16,34,0.96)');
  g.addColorStop(1, 'rgba(12,8,20,0.96)');
  x.fillStyle = g; x.fillRect(0, 0, w, h);
  x.fillStyle = col; x.globalAlpha = 0.16; x.fillRect(0, 0, w, h); x.globalAlpha = 1;
  x.fillStyle = boon.curse ? '#ff9db8' : col;
  x.font = 'bold 26px "Segoe UI", sans-serif';
  x.textAlign = 'center'; x.textBaseline = 'middle';
  const words = String(boon.name || '').split(' ');
  const lines = []; let line = '';
  for (const wd of words) {
    if ((line + ' ' + wd).length > 12 && line) { lines.push(line); line = wd; }
    else line = line ? line + ' ' + wd : wd;
  }
  if (line) lines.push(line);
  const y0 = h / 2 - (lines.length - 1) * 18;
  lines.forEach((l, i) => x.fillText(l, w / 2, y0 + i * 36));
  x.font = '16px "Segoe UI", sans-serif'; x.fillStyle = 'rgba(255,255,255,0.55)';
  x.fillText(boon.curse ? 'a sin' : String(boon.rarity || '').toLowerCase(), w / 2, h - 34);
  return new THREE.CanvasTexture(c);
}

export function createBoonPick({ scene, camera, layout, nav, fx, hud }) {
  const unitPlane = new THREE.PlaneGeometry(1, 1);
  const glowTex = makeGlowTex();
  const dotTex = makeDotTex();
  const frameTex = makeFrameTex();
  const crackTex = makeCrackTex();

  let state = 'idle';         // 'idle' | 'active' | 'resolved'
  let cards = [];             // live BoonCard handles
  let opts = null;            // the open() options (callback contract)
  let autoT = 0, autoLimit = 0;
  let aimIndex = -1;
  const shatters = [];        // shard bursts, ticked even after resolve
  const _q = new THREE.Quaternion();
  const _euler = new THREE.Euler();
  const _wp = new THREE.Vector3();

  // ---- DOM chrome (caption + labels + buttons), created lazily --------------
  let dom = null;
  function ensureDom() {
    if (dom) return dom;
    const root = document.createElement('div');
    root.className = 'cf-boonpick';
    root.hidden = true;
    const labels = document.createElement('div');
    labels.className = 'cf-boonpick-labels';
    const cap = document.createElement('div');
    cap.className = 'cf-boonpick-cap';
    const btns = document.createElement('div');
    btns.className = 'cf-boonpick-btns';
    const reroll = document.createElement('button');
    reroll.type = 'button'; reroll.className = 'sf-btn cf-boonpick-reroll';
    const resist = document.createElement('button');
    resist.type = 'button'; resist.className = 'sf-btn cf-boonpick-resist';
    resist.textContent = '♥ resist (+1)';
    btns.append(reroll, resist);
    root.append(labels, cap, btns);
    (hud || document.body).appendChild(root);
    reroll.addEventListener('click', doReroll);
    resist.addEventListener('click', () => { if (state === 'active' && opts) resolve(() => opts.onSkip(false)); });
    dom = { root, labels, cap, btns, reroll, resist, labelEls: [] };
    return dom;
  }

  function buildLabels(options) {
    const d = ensureDom();
    d.labels.innerHTML = '';
    d.labelEls = options.map((b) => {
      const el = document.createElement('div');
      el.className = 'cf-boonpick-label' + (b.curse ? ' is-sin' : '');
      const nm = document.createElement('span'); nm.className = 'cf-bp-name';
      nm.textContent = `${b.curse ? '☠ ' : ''}${b.name}`;
      const rt = document.createElement('span'); rt.className = 'cf-bp-rar';
      rt.textContent = b.curse ? 'sin' : String(b.rarity || '').toLowerCase();
      el.append(nm, rt);
      d.labels.appendChild(el);
      return el;
    });
  }

  function setCaption(text) { if (dom) dom.cap.textContent = text || ''; }
  function refreshCaption() {
    if (!opts) return;
    const b = aimIndex >= 0 && cards[aimIndex] ? cards[aimIndex].boon : null;
    if (b) setCaption(`${b.name} · ${b.curse ? 'a sin' : (b.rarity || '').toLowerCase()} — ${b.desc}`);
    else setCaption('aim a card to read it · click to take it');
  }

  function doReroll() {
    if (state !== 'active' || !opts || !opts.onReroll) return;
    const res = opts.onReroll();
    if (!res || !res.options) { if (dom) dom.reroll.disabled = true; return; }
    opts.rerollsLeft = res.rerollsLeft;
    rebuildCards(res.options);
    syncButtons();
  }

  function syncButtons() {
    if (!dom || !opts) return;
    const left = opts.rerollsLeft || 0;
    dom.reroll.textContent = `🎲 reroll (${left})`;
    dom.reroll.hidden = !(left > 0 && opts.onReroll);
    dom.reroll.disabled = false;
    dom.resist.hidden = !opts.allowSkip;
  }

  // ---- one themed boon card --------------------------------------------------
  function buildCard(boon, pos, scale) {
    let disposed = false;   // the CDN art can land after teardown; guard the async map swap so the texture isn't leaked
    const th = themeOf(boon);
    const group = new THREE.Group();
    group.position.copy(pos);
    group.userData = { type: 'booncard' };   // index stamped by caller
    const w = CARD_W * scale, h = CARD_H * scale;

    // glow (additive, behind)
    const glowMat = new THREE.MeshBasicMaterial({
      map: glowTex, color: new THREE.Color(th.glow),
      transparent: true, opacity: 0, depthWrite: false,
      blending: THREE.AdditiveBlending, side: THREE.DoubleSide,
    });
    const glow = new THREE.Mesh(unitPlane, glowMat);
    glow.scale.set(w * 1.7, h * 1.6, 1); glow.position.z = -0.08;
    group.add(glow);

    // frame (tinted border)
    const frameMat = new THREE.MeshBasicMaterial({
      map: frameTex, color: new THREE.Color(th.frame),
      transparent: true, opacity: 0, depthWrite: false, side: THREE.DoubleSide,
    });
    const frame = new THREE.Mesh(unitPlane, frameMat);
    frame.scale.set(w * 1.08, h * 1.08, 1); frame.position.z = 0.01;
    group.add(frame);

    // content plane: boon art (CDN) with a local name-panel fallback
    const contentMat = new THREE.MeshBasicMaterial({
      map: makeNamePanelTex(boon, th.frame),
      transparent: true, opacity: 0, depthWrite: false, side: THREE.DoubleSide,
    });
    const content = new THREE.Mesh(unitPlane, contentMat);
    content.scale.set(w, h, 1); content.position.z = 0.02;
    group.add(content);
    // try the real art; on success swap the map, on error keep the panel
    new THREE.TextureLoader().load(
      `${ART}boons/${boon.id}.png`,
      (tex) => {
        if (disposed) { tex.dispose(); return; }   // card already torn down: don't leak the just-loaded GPU texture
        tex.colorSpace = THREE.SRGBColorSpace;
        const old = contentMat.map; contentMat.map = tex; contentMat.needsUpdate = true;
        if (old) old.dispose();
      },
      undefined,
      () => { /* keep the fallback name panel */ },
    );

    // per-theme extra FX
    let crackMat = null, bolts = null, sparks = null;
    if (th.look === 'crack') {
      crackMat = new THREE.MeshBasicMaterial({
        map: crackTex, color: new THREE.Color(0xff3b52),
        transparent: true, opacity: 0, depthWrite: false,
        blending: THREE.AdditiveBlending, side: THREE.DoubleSide,
      });
      const cm = new THREE.Mesh(unitPlane, crackMat);
      cm.scale.set(w, h, 1); cm.position.z = 0.03; group.add(cm);
    } else if (th.look === 'bolt') {
      const geo = new THREE.BufferGeometry();
      geo.setAttribute('position', new THREE.BufferAttribute(new Float32Array(3 * 2 * 8 * 3), 3));
      const mat = new THREE.LineBasicMaterial({
        color: new THREE.Color(th.glow), transparent: true, opacity: 0,
        blending: THREE.AdditiveBlending, depthWrite: false,
      });
      const seg = new THREE.LineSegments(geo, mat);
      seg.position.z = 0.05; group.add(seg);
      bolts = { seg, geo, mat, t: 0, w, h };
    } else if (th.look === 'pulse') {
      const N = 44, arr = new Float32Array(N * 3);
      for (let i = 0; i < N; i++) {
        const a = (i / N) * Math.PI * 2;
        const rr = Math.max(w, h) * (0.62 + Math.random() * 0.14);
        arr[i * 3] = Math.cos(a) * rr; arr[i * 3 + 1] = Math.sin(a) * rr * 0.95; arr[i * 3 + 2] = 0.04;
      }
      const geo = new THREE.BufferGeometry();
      geo.setAttribute('position', new THREE.BufferAttribute(arr, 3));
      const mat = new THREE.PointsMaterial({
        map: dotTex, color: new THREE.Color(th.glow), size: 0.5, sizeAttenuation: true,
        transparent: true, opacity: 0, blending: THREE.AdditiveBlending, depthWrite: false,
      });
      const pts = new THREE.Points(geo, mat);
      group.add(pts);
      sparks = { pts, geo, mat };
    }

    scene.add(group);

    const mats = [glowMat, frameMat, contentMat];
    if (crackMat) mats.push(crackMat);
    const baseOp = { glow: 0.9, frame: 1, content: 1, crack: 0.5 };
    let appear = 0, fade = 0, sway = Math.random() * 6, aimed = 0;

    function rebuildBolts(dt) {
      bolts.t -= dt;
      if (bolts.t > 0) return;
      bolts.t = 0.06 + Math.random() * 0.05;
      const p = bolts.geo.attributes.position; const a = p.array;
      const hw = bolts.w * 0.62, hh = bolts.h * 0.62;
      let k = 0;
      for (let s = 0; s < 3; s++) {
        // a jagged bolt hugging the frame perimeter
        let x = (Math.random() * 2 - 1) * hw, y = hh * (Math.random() < 0.5 ? 1 : -1);
        const steps = 8;
        for (let i = 0; i < steps; i++) {
          const nx = x + (Math.random() - 0.5) * hw * 0.5;
          const ny = y + (Math.random() - 0.5) * hh * 0.5;
          a[k++] = x; a[k++] = y; a[k++] = 0;
          a[k++] = nx; a[k++] = ny; a[k++] = 0;
          x = nx; y = ny;
        }
      }
      while (k < a.length) a[k++] = 0;
      p.needsUpdate = true;
    }

    return {
      boon, group, content,
      getContent: () => content,
      setAimed(on) { aimed = on ? 1 : 0; },
      startFade() { if (fade <= 0) fade = 0.001; },
      isFading: () => fade > 0,
      update(dt, camQuat) {
        appear = Math.min(1, appear + dt * 3.2);
        // billboard to the screen plane (art upright, never mirrored) + sway
        sway += dt;
        _euler.set(0, 0, Math.sin(sway * 1.1) * 0.045);
        _q.setFromEuler(_euler);
        group.quaternion.copy(camQuat).multiply(_q);
        // aim lift: nudge toward the camera + brighter
        const lift = aimed ? 1 : 0;
        const fk = fade > 0 ? clamp(1 - fade / 0.35, 0, 1) : 0; // 0 -> 1 as it fades out
        if (fade > 0) fade += dt;
        const vis = appear * (1 - fk);
        glowMat.opacity = baseOp.glow * vis * (0.7 + 0.5 * lift + 0.18 * Math.sin(sway * 2.4));
        frameMat.opacity = baseOp.frame * vis;
        contentMat.opacity = baseOp.content * vis;
        const s = (1 + 0.05 * lift) * (1 - 0.12 * fk);
        group.scale.setScalar(s);
        if (crackMat) crackMat.opacity = baseOp.crack * vis * (0.55 + 0.45 * Math.sin(sway * 3.1));
        if (bolts) { rebuildBolts(dt); bolts.mat.opacity = vis * (0.5 + 0.5 * Math.random()); }
        if (sparks) {
          sparks.pts.rotation.z += dt * 0.6;
          sparks.mat.opacity = vis * (0.5 + 0.4 * Math.sin(sway * 2.0));
        }
        return fk >= 1; // done fading -> disposable
      },
      dispose() {
        disposed = true;
        scene.remove(group);
        // unitPlane/glowTex/etc are shared, disposed by the module's dispose()
        glowMat.dispose(); frameMat.dispose();
        if (contentMat.map) contentMat.map.dispose();
        contentMat.dispose();
        if (crackMat) crackMat.dispose();
        if (bolts) { bolts.geo.dispose(); bolts.mat.dispose(); }
        if (sparks) { sparks.geo.dispose(); sparks.mat.dispose(); }
      },
    };
  }

  // ---- shatter (ported from junctions.spawnShatter) -------------------------
  function spawnShatter(card) {
    const content = card.getContent();
    if (!content || !content.material || !content.material.map) { card.dispose(); return; }
    const tex = content.material.map;
    content.updateWorldMatrix(true, false);
    const wm = content.matrixWorld.clone();
    const sc = new THREE.Vector3(); wm.decompose(new THREE.Vector3(), new THREE.Quaternion(), sc);
    const w = sc.x, h = sc.y;
    const grp = new THREE.Group();
    grp.applyMatrix4(wm); grp.scale.set(1, 1, 1);
    const N = 4, sw = w / N, sh = h / N;
    const shards = [];
    for (let iy = 0; iy < N; iy++) {
      for (let ix = 0; ix < N; ix++) {
        const g = new THREE.PlaneGeometry(sw * 0.92, sh * 0.92);
        const uv = g.attributes.uv;
        for (let k = 0; k < uv.count; k++) uv.setXY(k, (ix + uv.getX(k)) / N, (iy + uv.getY(k)) / N);
        uv.needsUpdate = true;
        const m = new THREE.MeshBasicMaterial({ map: tex, transparent: true, side: THREE.DoubleSide, depthWrite: false });
        const mesh = new THREE.Mesh(g, m);
        mesh.position.set((ix - (N - 1) / 2) * sw, (iy - (N - 1) / 2) * sh, 0);
        grp.add(mesh);
        const vel = new THREE.Vector3((ix - (N - 1) / 2) + (Math.random() - 0.5), (iy - (N - 1) / 2) + (Math.random() - 0.5), (Math.random() - 0.3) * 2).multiplyScalar(0.9);
        shards.push({ mesh, mat: m, geo: g, vel, spin: (Math.random() - 0.5) * 8 });
      }
    }
    scene.add(grp);
    shatters.push({ grp, shards, life: 0, dur: 0.6, card });
    card.group.visible = false; // the shards carry the look now; card disposed when shatter ends
  }

  function tickShatters(dt) {
    for (let i = shatters.length - 1; i >= 0; i--) {
      const s = shatters[i]; s.life += dt;
      const k = clamp(s.life / s.dur, 0, 1);
      for (const sh of s.shards) {
        sh.mesh.position.addScaledVector(sh.vel, dt * 6);
        sh.vel.y -= dt * 4;
        sh.mesh.rotation.z += sh.spin * dt;
        sh.mat.opacity = 1 - k;
      }
      if (k >= 1) {
        for (const sh of s.shards) { sh.geo.dispose(); sh.mat.dispose(); }
        scene.remove(s.grp);
        if (s.card && s.card.dispose) { try { s.card.dispose(); } catch (e) { /* ignore */ } }
        shatters.splice(i, 1);
      }
    }
  }

  // ---- open / resolve --------------------------------------------------------
  function layoutPositions(n, fr) {
    // positions in the tube cross-section plane at parkDepth+ahead (binormal = x, normal = y)
    const out = [];
    const put = (ox, oy, scale) => {
      const p = fr.pos.clone().addScaledVector(fr.binormal, ox).addScaledVector(fr.normal, oy);
      out.push({ pos: p, scale });
    };
    if (n >= 4) {
      put(-GRID_X, GRID_Y, GRID_SCALE); put(GRID_X, GRID_Y, GRID_SCALE);
      put(-GRID_X, -GRID_Y, GRID_SCALE); put(GRID_X, -GRID_Y, GRID_SCALE);
    } else {
      const x0 = -ROW_SPREAD * (n - 1) / 2;
      for (let i = 0; i < n; i++) put(x0 + i * ROW_SPREAD, 0, 1);
    }
    return out.slice(0, n);
  }

  function clearCards(disposeAll) {
    for (const c of cards) { if (disposeAll) c.dispose(); }
    cards = [];
  }

  function rebuildCards(options) {
    clearCards(true);
    const depth = nav.getDepth();
    const fr = layout.frameAtDepth(depth + STOP_AHEAD + CARD_AHEAD);
    const slots = layoutPositions(options.length, fr);
    cards = options.map((b, i) => {
      const card = buildCard(b, slots[i].pos, slots[i].scale);
      card.group.userData.index = i;
      return card;
    });
    buildLabels(options);
    aimIndex = -1; refreshCaption();
  }

  function open(o) {
    if (state !== 'idle') teardown();
    opts = o;
    state = 'active';
    autoT = 0; autoLimit = o.autoResumeSec > 0 ? o.autoResumeSec : 0;
    // park the fall a touch ahead so the camera glides to a stop facing the cards
    try { nav.setForwardHold(true, nav.getDepth() + STOP_AHEAD); } catch (e) { /* ignore */ }
    rebuildCards(o.options);
    const d = ensureDom();
    d.root.hidden = false;
    syncButtons();
  }

  // run `after` (the chosen callback) once, tear the pick down, release the fall
  function resolve(after) {
    if (state !== 'active') return;
    state = 'resolved';
    if (dom) dom.root.hidden = true;
    try { nav.setForwardHold(false); } catch (e) { /* ignore */ }
    try { after && after(); } catch (e) { /* ignore */ }
  }

  function pick(index) {
    if (state !== 'active' || !cards[index]) return;
    const chosen = cards[index];
    const boon = chosen.boon;
    // the Pow: flash + fov lunge + a dive cue, then shatter the chosen card
    try { fx && fx.pulseFlash && fx.pulseFlash(0.7); } catch (e) { /* ignore */ }
    try { nav && nav.fovKick && nav.fovKick(6); } catch (e) { /* ignore */ }
    try { opts && opts.sfx && opts.sfx('dive', 0.6); } catch (e) { /* ignore */ }
    cards.splice(index, 1);           // ownership moves to the shatter
    spawnShatter(chosen);
    for (const c of cards) c.startFade();   // the losers fade
    resolve(() => opts.onPick(boon, false));
  }

  function fireTimeout() {
    if (state !== 'active') return;
    for (const c of cards) c.startFade();
    // onSkip owns the outcome: normal drafts bank +1 resistance (and the game
    // layer flies a shield into the HUD from resolveDraft); the scripted table
    // hands you its first card instead.
    resolve(() => opts.onSkip(true));
  }

  function teardown() {
    clearCards(true);
    if (dom) { dom.root.hidden = true; dom.labels.innerHTML = ''; dom.labelEls = []; }
    try { nav.setForwardHold(false); } catch (e) { /* ignore */ }
    opts = null; aimIndex = -1; state = 'idle';
  }

  // ---- per-frame -------------------------------------------------------------
  function update(dt, depth) {
    tickShatters(dt);
    if (state === 'idle') return;

    // billboard + FX; drop cards that have finished fading out
    for (let i = cards.length - 1; i >= 0; i--) {
      const done = cards[i].update(dt, camera.quaternion);
      if (done && cards[i].isFading()) { cards[i].dispose(); cards.splice(i, 1); }
    }

    if (state === 'resolved') {
      // wait for fades + shards to clear, then go fully idle
      if (cards.length === 0 && shatters.length === 0) teardown();
      return;
    }

    // project DOM labels onto the cards
    if (dom && dom.labelEls.length) {
      const rect = dom.root.getBoundingClientRect();
      const W = rect.width || window.innerWidth, H = rect.height || window.innerHeight;
      for (let i = 0; i < cards.length; i++) {
        const el = dom.labelEls[i]; if (!el) continue;
        cards[i].group.getWorldPosition(_wp);
        _wp.y += CARD_H * 0.62;            // sit the label just above the card
        _wp.project(camera);
        if (_wp.z > 1) { el.style.opacity = '0'; continue; }
        el.style.opacity = '1';
        el.style.left = `${(_wp.x * 0.5 + 0.5) * W}px`;
        el.style.top = `${(-_wp.y * 0.5 + 0.5) * H}px`;
        el.classList.toggle('is-aimed', i === aimIndex);
      }
    }

    // auto-resume countdown (accumulates in update, so freeze/pause hold it)
    if (autoLimit > 0) {
      autoT += dt;
      const left = Math.max(0, Math.ceil(autoLimit - autoT));
      if (dom) dom.resist.dataset.left = String(left);
      if (autoT >= autoLimit) fireTimeout();
    }
  }

  return {
    open,
    update,
    isBusy: () => state === 'active',
    getPickables: () => (state === 'active' ? cards.map((c) => c.group) : []),
    pickIndex: (i) => pick(i),
    setAimed(i) {
      if (state !== 'active') return;
      if (i === aimIndex) return;
      aimIndex = i;
      for (let k = 0; k < cards.length; k++) cards[k].setAimed(k === i);
      refreshCaption();
    },
    dispose() {
      teardown();
      for (let i = shatters.length - 1; i >= 0; i--) {
        for (const sh of shatters[i].shards) { sh.geo.dispose(); sh.mat.dispose(); }
        scene.remove(shatters[i].grp);
        if (shatters[i].card && shatters[i].card.dispose) { try { shatters[i].card.dispose(); } catch (e) { /* ignore */ } }
      }
      shatters.length = 0;
      if (dom && dom.root.parentNode) dom.root.parentNode.removeChild(dom.root);
      dom = null;
      unitPlane.dispose(); glowTex.dispose(); dotTex.dispose(); frameTex.dispose(); crackTex.dispose();
    },
  };
}
