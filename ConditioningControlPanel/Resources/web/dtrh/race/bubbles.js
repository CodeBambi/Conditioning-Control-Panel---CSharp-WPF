/* ============================================================================
 * race/bubbles.js - The Caucus Race pickup layer: the bubbles on the road.
 * Implements CONTRACT.md section `race/bubbles.js` (createBubbleField + the
 * BUBBLE_KINDS table, which lives in bubbleKinds.js and is re-exported here).
 *
 * Every bubble is a pooled THREE.Sprite in track space (d, x, h), placed
 * through layout.toWorld only. Four placements: lane (rests on the road and
 * bobs), air (threads a ramp's air line), spawn (materialises ahead and
 * wobbles), rain (falls from the ceiling, rests, fizzles). Pops are pass-
 * through against the kart box; a pop plays the DtRH pop/chime in-page
 * (engine/audioBus) and throws a few sparkle shards, the WPF BubbleService way.
 * Sprite textures come from the two locally mapped hosts (never remote media).
 * ==========================================================================*/

import * as THREE from 'three';
import { ROAD_HALF_W, CEILING_H, POP_HIT_D, POP_HIT_X, POP_HIT_H, LANE_H } from './consts.js';
import { BUBBLE_KINDS, KIND_BY_ID, rollKind } from './bubbleKinds.js';
import { makeSfxPlayer } from '../engine/audioBus.js';
import { getLevel } from '../engine/audioLevels.js';

export { BUBBLE_KINDS };

const SFX_BASE = '/dtrh/assets/bubbles/sfx/';
const POP_SFX = ['Pop.mp3', 'Pop2.mp3', 'Pop3.mp3'];
const CHIME_SFX = ['chime1.mp3', 'chime2.mp3', 'chime3.mp3'];
const BURST_SFX = 'Burst.mp3';

const CAP = 160;              // live bubbles, recycled farthest-first when full
const SHARD_CAP = 64;
const LANE_STEP = 3.2;        // metres between bubbles in a lane line
const VIEW_AHEAD = 150;       // sprites beyond this are hidden, not freed
const MISS_BEHIND = 6;        // a treat this far behind the kart unpopped = miss
const DROP_BEHIND = 12;       // freed once this far behind
const PASS_FADE_M = 2.4;      // metres behind the pop box over which a passed bubble fades away
const RAIN_FALL = 4, RAIN_REST = 2, RAIN_FIZZLE = 0.5;
const POP_ANIM = 0.14;
const PRISM_REACH = 8;        // prism chain-pops neighbours within this many metres

const rand = (a, b) => a + Math.random() * (b - a);
const pick = (arr) => arr[(Math.random() * arr.length) | 0];
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const sizeOf = (id) => (id === 'video' || id === 'gifrain') ? 1.5 : id === 'golden' ? 0.95 : id === 'lucky' ? 0.8 : 1.15;

/** Soft radial dot: the fallback face while a sprite loads, and the shard face. */
function makeDotTex() {
  try {
    const c = document.createElement('canvas'); c.width = c.height = 64;
    const g = c.getContext('2d');
    const grad = g.createRadialGradient(32, 32, 4, 32, 32, 30);
    grad.addColorStop(0, 'rgba(255,255,255,1)'); grad.addColorStop(0.6, 'rgba(255,255,255,0.55)'); grad.addColorStop(1, 'rgba(255,255,255,0)');
    g.fillStyle = grad; g.fillRect(0, 0, 64, 64);
    const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; return t;
  } catch (e) { return null; }
}

export function createBubbleField({ scene, layout, media, getIntensity, getRoom, onTexture }) {
  void media;   // reserved: flash media is drawn by payloadFx at pop time, never here
  const T = layout.totalDepth;
  /** Signed depth from `from` to `d`, folded into (-T/2, T/2] so the start line is nothing special. */
  const relD = (d, from) => { let r = (d - from) % T; if (r > T / 2) r -= T; else if (r <= -T / 2) r += T; return r; };
  const intensity = () => clamp(getIntensity ? getIntensity() : 0, 0, 1);
  const roomBias = () => { const r = getRoom && getRoom(); return (r && r.bubbleBias) || null; };

  const audio = makeSfxPlayer();
  audio.preload([...POP_SFX, ...CHIME_SFX, BURST_SFX].map((f) => SFX_BASE + f));
  const sfx = (file, vol) => audio.play(SFX_BASE + file, vol * getLevel('fx'));

  // ---- textures: one per kind, fallback dot until the PNG lands --------------
  const dotTex = makeDotTex();
  const texOf = {};
  const loader = new THREE.TextureLoader();
  let disposed = false;
  for (const k of BUBBLE_KINDS) {
    texOf[k.id] = dotTex;
    loader.load(k.sprite, (tex) => {
      if (disposed) { tex.dispose(); return; }
      tex.colorSpace = THREE.SRGBColorSpace;
      if (onTexture) { try { onTexture(tex); } catch (e) { /* the look never breaks the field */ } }
      texOf[k.id] = tex;
      for (const s of pool) if (s.alive && s.kindId === k.id) { s.mat.map = tex; s.mat.needsUpdate = true; }
    }, undefined, () => { /* keep the dot */ });
  }

  // ---- pools ---------------------------------------------------------------
  const group = new THREE.Group();
  group.name = 'race-bubbles';
  scene.add(group);
  const pool = [];
  for (let i = 0; i < CAP; i++) {
    const mat = new THREE.SpriteMaterial({ transparent: true, depthWrite: false, opacity: 1 });
    const sprite = new THREE.Sprite(mat);
    sprite.visible = false;
    group.add(sprite);
    pool.push({ sprite, mat, alive: false, kindId: 'treat', placement: 'lane', d: 0, x: 0, h: LANE_H,
      x0: 0, baseH: LANE_H, phase: 0, age: 0, size: 1, scale: 1, popT: -1, missed: false });
  }
  const shards = [];
  for (let i = 0; i < SHARD_CAP; i++) {
    const mat = new THREE.SpriteMaterial({ map: dotTex, transparent: true, depthWrite: false, opacity: 0 });
    const sprite = new THREE.Sprite(mat);
    sprite.visible = false;
    group.add(sprite);
    shards.push({ sprite, mat, alive: false, age: 0, life: 0.5, p0: new THREE.Vector3(), r: null, u: null, vr: 0, vu: 0, size: 0.2 });
  }
  let liveCount = 0;
  let lastKartD = 0;
  let density = 1;
  const popCbs = [], missCbs = [];
  const emit = (cbs, ev) => { for (const cb of cbs) { try { cb(ev); } catch (e) { /* listener bug, not ours */ } } };

  function freeSlot(s) { if (!s.alive) return; s.alive = false; s.sprite.visible = false; liveCount--; }
  function takeSlot() {
    let s = pool.find((p) => !p.alive);
    if (!s) {   // full: recycle the bubble farthest from the kart
      let far = -1;
      for (const p of pool) { const a = Math.abs(relD(p.d, lastKartD)); if (a > far) { far = a; s = p; } }
      freeSlot(s);
    }
    s.alive = true; liveCount++;
    return s;
  }

  const liveOf = (id) => { let n = 0; for (const p of pool) if (p.alive && p.kindId === id) n++; return n; };
  /** Weighted kind roll: room bias, intensity gate, and a per-placement nudge. */
  function roll(placement) {
    const extra = (k) => {
      if (k.id === 'video' && liveOf('video') > 0) return 0;      // one video on the road at a time
      if (k.id === 'freeze' && liveOf('freeze') > 1) return 0;
      if (placement === 'air' && k.id === 'golden') return 3;     // gold favours the air line
      if (placement === 'rain' && k.kind === 'effect') return 0.5;
      return 1;
    };
    return rollKind(intensity(), roomBias(), extra).id;
  }

  function place(kindId, placement, d, x, h) {
    const k = KIND_BY_ID[kindId] || KIND_BY_ID.treat;
    const s = takeSlot();
    s.kindId = k.id; s.placement = placement;
    s.d = layout.wrap(d); s.x = clamp(x, -ROAD_HALF_W + 0.4, ROAD_HALF_W - 0.4); s.x0 = s.x;
    s.h = h; s.baseH = h; s.phase = Math.random() * Math.PI * 2; s.age = 0;
    s.size = sizeOf(k.id); s.scale = placement === 'spawn' ? 0 : 1; s.popT = -1; s.missed = false;
    s.mat.map = texOf[k.id]; s.mat.color.set(k.tint); s.mat.opacity = 1; s.mat.needsUpdate = true;
    s.sprite.scale.setScalar(s.size * s.scale);
    layout.toWorld(s.d, s.x, s.h, s.sprite.position);
    s.sprite.visible = true;
    return s;
  }

  // ---- placements ----------------------------------------------------------
  const seeded = new Set();
  const chunkRecs = [];
  const LANES = [-2.2, -1.1, 0, 1.1, 2.2];

  /** Lane lines the kart can thread, ramp air lines, and a pair flanking each sugar cube. */
  function seedChunk(chunk) {
    if (!chunk || seeded.has(chunk.id)) return;
    seeded.add(chunk.id);
    chunkRecs.push({ id: chunk.id, d0: chunk.d0, d1: chunk.d1 });
    const len = Math.max(0, chunk.d1 - chunk.d0);
    if (chunk.kind !== 'gate' && len > 8) {
      const lines = Math.max(1, Math.round((len / 26) * density));
      for (let l = 0; l < lines; l++) {
        const count = 3 + ((Math.random() * 4) | 0);
        const x = pick(LANES) + rand(-0.3, 0.3);
        const drift = rand(-0.22, 0.22);
        const d0 = chunk.d0 + rand(2, Math.max(2, len - count * LANE_STEP - 2));
        const lineKind = roll('lane');
        for (let i = 0; i < count; i++) {
          place(Math.random() < 0.7 ? lineKind : roll('lane'), 'lane', d0 + i * LANE_STEP, x + drift * i, LANE_H);
        }
      }
    }
    for (const f of chunk.features || []) {
      if (f.type === 'ramp') {
        const n = 5, x = rand(-0.6, 0.6);
        for (let i = 0; i < n; i++) {
          const u = (i + 0.5) / n;
          place(roll('air'), 'air', f.d + f.airLen * u, x, 2 + 3 * (4 * u * (1 - u)));
        }
      } else if (f.type === 'itembox') {
        place(roll('lane'), 'lane', f.d, f.x - 1.5, LANE_H);
        place(roll('lane'), 'lane', f.d, f.x + 1.5, LANE_H);
      }
    }
  }

  function spawnAhead(kartD, n = 1) {
    for (let i = 0; i < n; i++) place(roll('spawn'), 'spawn', kartD + rand(35, 60), rand(-ROAD_HALF_W + 0.6, ROAD_HALF_W - 0.6), LANE_H);
  }
  function rain(kartD, n = 1) {
    for (let i = 0; i < n; i++) place(roll('rain'), 'rain', kartD + rand(18, 44), rand(-ROAD_HALF_W + 0.6, ROAD_HALF_W - 0.6), CEILING_H);
  }

  // ---- pop -----------------------------------------------------------------
  const _r = new THREE.Vector3(), _u = new THREE.Vector3();
  function burst(s, golden) {
    const f = layout.frameAtDepth(s.d);
    const dirs = { r: f.right.clone(), u: f.up.clone() };   // one pair per burst, shared by its shards
    const n = golden ? 12 : 7;
    for (let i = 0; i < n; i++) {
      const sh = shards.find((p) => !p.alive) || shards[(Math.random() * SHARD_CAP) | 0];
      const ang = (Math.PI * 2 * i) / n + rand(-0.35, 0.35);
      const v = rand(2.2, 4.6) * (golden ? 1.3 : 1);
      sh.alive = true; sh.age = 0; sh.life = rand(0.35, 0.6);
      sh.p0.copy(s.sprite.position); sh.r = dirs.r; sh.u = dirs.u;
      sh.vr = Math.cos(ang) * v; sh.vu = Math.sin(ang) * v + 1.2;
      sh.size = rand(0.14, 0.26) * (golden ? 1.3 : 1);
      sh.mat.color.set(golden ? '#ffe27a' : '#ffd9ef'); sh.mat.opacity = 1;
      sh.sprite.position.copy(sh.p0); sh.sprite.scale.setScalar(sh.size); sh.sprite.visible = true;
    }
  }

  function pop(s, chained = false) {
    if (s.popT >= 0) return;
    const k = KIND_BY_ID[s.kindId];
    s.popT = 0;
    const golden = k.id === 'golden';
    if (golden) sfx(pick(CHIME_SFX), 0.4);
    else if (k.id === 'prism') sfx(BURST_SFX, 0.35);
    else sfx(pick(POP_SFX), 0.28);
    burst(s, golden);
    const strength = k.strength > 0 ? clamp(k.strength + 0.35 * intensity() + Math.random() * 0.1, 0, 1) : 0;
    emit(popCbs, { id: k.id, kind: k.kind, payload: k.payload, overlayKind: k.overlayKind, strength,
      points: k.points, placement: s.placement, x: s.x, d: s.d, worldPos: s.sprite.position.clone() });
    if (k.id === 'prism' && !chained) {
      for (const o of pool) {
        if (!o.alive || o === s || o.popT >= 0 || o.kindId === 'video') continue;
        const dd = relD(o.d, s.d);
        if (dd > -2 && dd < PRISM_REACH) pop(o, true);
      }
    }
  }

  // ---- per frame -----------------------------------------------------------
  function update(dt, t, kart) {
    lastKartD = kart.d;
    for (const s of pool) {
      if (!s.alive) continue;
      s.age += dt;
      const rel = relD(s.d, kart.d);
      if (s.popT >= 0) {   // pop flash: swell + fade, then free
        s.popT += dt;
        const f = Math.min(1, s.popT / POP_ANIM);
        s.scale = 1 + 0.55 * f; s.mat.opacity = 1 - f;
        if (f >= 1) { freeSlot(s); continue; }
      } else {
        const bob = 0.12 * Math.sin(t * 2.2 + s.phase);
        if (s.placement === 'lane') { s.h = s.baseH + bob; }
        else if (s.placement === 'air') { s.h = s.baseH + 0.08 * Math.sin(t * 2.6 + s.phase); }
        else if (s.placement === 'spawn') {
          const a = Math.min(1, s.age / 0.45);
          s.scale = 1 - (1 - a) * (1 - a);
          s.x = clamp(s.x0 + 0.55 * Math.sin(t * 1.7 + s.phase), -ROAD_HALF_W + 0.4, ROAD_HALF_W - 0.4);
          s.h = LANE_H + bob;
        } else if (s.placement === 'rain') {
          if (s.age < RAIN_FALL) { const k = s.age / RAIN_FALL; s.h = CEILING_H - (CEILING_H - LANE_H) * k * k; }
          else if (s.age < RAIN_FALL + RAIN_REST) { s.h = LANE_H + bob; }
          else {
            const f = (s.age - RAIN_FALL - RAIN_REST) / RAIN_FIZZLE;
            s.h = LANE_H + bob; s.scale = 1 - 0.4 * f; s.mat.opacity = 1 - f;
            if (f >= 1) { freeSlot(s); continue; }
          }
        }
        // behind the kart: a treat that slipped by is a miss, further back it is gone
        if (rel < -MISS_BEHIND && rel > -DROP_BEHIND - 40 && !s.missed) {
          s.missed = true;
          const k = KIND_BY_ID[s.kindId];
          if (k.kind === 'treat') emit(missCbs, { id: k.id, points: k.points, d: s.d, x: s.x, h: s.h });
        }
        if (rel < -DROP_BEHIND && rel > -DROP_BEHIND - 40) { freeSlot(s); continue; }
        if (Math.abs(rel) < POP_HIT_D && Math.abs(s.x - kart.x) < POP_HIT_X && Math.abs(s.h - kart.h) < POP_HIT_H) pop(s);
      }
      s.sprite.visible = rel > -DROP_BEHIND && rel < VIEW_AHEAD;
      if (s.sprite.visible) {
        layout.toWorld(s.d, s.x, s.h, s.sprite.position);
        // a bubble that slipped past the pop box fades and shrinks before it can balloon into the
        // low chase camera (the seat is only ~6 m back)
        const gone = s.popT < 0 && rel < -POP_HIT_D ? clamp(1 + (rel + POP_HIT_D) / PASS_FADE_M, 0, 1) : 1;
        if (gone < 1) s.mat.opacity = Math.min(s.mat.opacity, gone);
        s.sprite.scale.setScalar(s.size * s.scale * (0.6 + 0.4 * gone));
      }
    }
    for (const sh of shards) {
      if (!sh.alive) continue;
      sh.age += dt;
      const f = sh.age / sh.life;
      if (f >= 1) { sh.alive = false; sh.sprite.visible = false; continue; }
      _r.copy(sh.r).multiplyScalar(sh.vr * sh.age);
      _u.copy(sh.u).multiplyScalar(sh.vu * sh.age - 4.5 * sh.age * sh.age);
      sh.sprite.position.copy(sh.p0).add(_r).add(_u);
      sh.sprite.scale.setScalar(sh.size * (1 - 0.7 * f));
      sh.mat.opacity = 1 - f * f;
    }
    // a chunk the kart has just cleared may seed again next lap
    for (let i = chunkRecs.length - 1; i >= 0; i--) {
      const r = relD(chunkRecs[i].d1, kart.d);
      if (r < -30 && r > -80) { seeded.delete(chunkRecs[i].id); chunkRecs.splice(i, 1); }
    }
  }

  function dispose() {
    disposed = true;
    scene.remove(group);
    for (const s of pool) s.mat.dispose();
    for (const sh of shards) sh.mat.dispose();
    for (const id of Object.keys(texOf)) if (texOf[id] && texOf[id] !== dotTex) texOf[id].dispose();
    if (dotTex) dotTex.dispose();
    popCbs.length = 0; missCbs.length = 0; seeded.clear(); chunkRecs.length = 0;
  }

  return {
    seedChunk, spawnAhead, rain, update, dispose,
    onPop(cb) { if (typeof cb === 'function') popCbs.push(cb); },
    onMiss(cb) { if (typeof cb === 'function') missCbs.push(cb); },
    setDensity(mult) { density = clamp(Number(mult) || 1, 0.25, 3); },
    get liveCount() { return liveCount; },
  };
}
