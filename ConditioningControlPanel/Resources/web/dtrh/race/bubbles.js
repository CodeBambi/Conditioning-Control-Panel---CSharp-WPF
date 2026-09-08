/* ============================================================================
 * race/bubbles.js - Racing Thoughts pickup layer: the bubbles on the road.
 * Implements CONTRACT.md section `race/bubbles.js` (createBubbleField + the
 * BUBBLE_KINDS table, which lives in bubbleKinds.js and is re-exported here).
 *
 * Every bubble is a pooled THREE.Sprite in track space (d, x, h), placed
 * through layout.toWorld only. A bubble that carries a WORD wears it on its own face:
 * race/wordFace.js paints the kind's sprite plus the word into one CanvasTexture and this
 * layer hangs that on the sprite instead of the plain kind texture (the owner's call, after
 * the plate over the bubble: "I want the word inside the bubble"). Four placements: lane (rests on the road and
 * bobs), air (threads a ramp's air line), spawn (materialises ahead and
 * wobbles), rain (falls from the ceiling, rests, fizzles). A ROW (spawnRow) is a
 * line of bubbles across the whole road at one depth: it goes down whole or not at
 * all, and it spends as ONE thing (the first of them to pop or to slip past settles
 * the row, so a row is one pop credit and at worst one broken combo, never five). Pops are pass-
 * through against the kart box; a pop is SILENT here (race/audio.js sounds it)
 * (engine/audioBus) and throws a few sparkle shards, the WPF BubbleService way.
 * Sprite textures come from the two locally mapped hosts (never remote media). Every sprite
 * (bubbles and shards) sits on pixel.js's CRISP_LAYER: full resolution over the blocky world.
 *
 * EVERY HEIGHT RIDES THE ROAD (2026-09-08). A bubble's `h` is metres above THE REACHABLE LINE
 * (spine.js rideH: the ramp wedge under the wheels, then the flight arc off its lip), not above the
 * flat road plane. Hung at a flat LANE_H a lane bubble on a ramp sat inside the wedge and one over
 * an air line sat metres under the kart - the owner: "some bubbles get placed under the slopes and
 * we can't get to them". The line is read ONCE, at placement, and kept on the slot as `ride`, so a
 * bob or a rain landing costs no extra lookup; moveRow re-reads it because the row moved. Only rain
 * starts absolute: it falls from the tube's own ceiling and lands on the line.
 * ==========================================================================*/

import * as THREE from 'three';
import { CEILING_H, POP_HIT_D, POP_HIT_X, POP_HIT_H, LANE_H, LANE_X_MAX, TREATS_ONLY_SEC } from './consts.js';
import { BUBBLE_KINDS, KIND_BY_ID, rollKind } from './bubbleKinds.js';
import { CRISP_LAYER } from './pixel.js';
import { createWordFaces } from './wordFace.js';
import { Q } from '../shared/quality.js';

export { BUBBLE_KINDS };


// CAP (live bubbles, recycled farthest-first when full), SHARD_CAP and VIEW_AHEAD (sprites beyond this are
// hidden, not freed: every visible sprite is a draw call, and past ~80 m the world has folded into fog anyway)
// are shared/quality.js knobs read when the field is built: desktop 160 / 64 / 110, the mobile tier 100 / 32 / 76.
const LANE_STEP = 3.2;        // metres between bubbles in a lane line
/** Metres between the bubbles of a ramp's air line, and how many of them. The line covers the FIRST
 *  ten metres of the flight on purpose: every pace from the gentle opening to a boosted lap crosses
 *  those metres within the pop box of the same arc (race/smoke/slope-check.mjs measures it), where
 *  the far end of a flight is only ever reachable at exactly one speed. */
const AIR_STEP = 2.0, AIR_N = 5;
const MISS_BEHIND = 6;        // a treat this far behind the kart unpopped = miss
const DROP_BEHIND = 12;       // freed once this far behind
const PASS_FADE_M = 1.6;      // metres behind the pop box over which a passed bubble fades away (before it balloons into the seat)
const RAIN_FALL = 4, RAIN_REST = 2, RAIN_FIZZLE = 0.5;
const POP_ANIM = 0.14;
const PRISM_REACH = 8;        // prism chain-pops neighbours within this many metres

const rand = (a, b) => a + Math.random() * (b - a);
const pick = (arr) => arr[(Math.random() * arr.length) | 0];
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const sizeOf = (id) => (id === 'video' || id === 'gifrain') ? 1.5 : id === 'golden' ? 0.95 : id === 'lucky' ? 0.8 : 1.15;
/** A trigger row's centre bubble, and an accent word, are drawn this much bigger than the rest. */
const BIG_SCALE = 1.15;

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

export function createBubbleField({ scene, layout, media, getIntensity, getRoom, getElapsed, onTexture }) {
  void media;   // reserved: flash media is drawn by payloadFx at pop time, never here
  void onTexture;   // see the loader below: crisp-layer sprites keep their own filters
  const T = layout.totalDepth;
  /** Signed depth from `from` to `d`, folded into (-T/2, T/2] so the start line is nothing special. */
  const relD = (d, from) => { let r = (d - from) % T; if (r > T / 2) r -= T; else if (r <= -T / 2) r += T; return r; };
  /** THE REACHABLE LINE at this depth (spine.js rideH). A layout without one is all road, at 0. */
  const rideAt = (d) => (typeof layout.rideH === 'function' ? layout.rideH(d) : 0);
  const intensity = () => clamp(getIntensity ? getIntensity() : 0, 0, 1);
  const roomBias = () => { const r = getRoom && getRoom(); return (r && r.bubbleBias) || null; };
  /** 0 while the opening is treats only, then a ramp to 1 over the next minute. */
  const effectGate = () => { const el = getElapsed ? getElapsed() : Infinity; return el < TREATS_ONLY_SEC ? 0 : clamp((el - TREATS_ONLY_SEC) / 60, 0.15, 1); };


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
      // onTexture (the pixel look's nearest-filter hook) is accepted for the contract but no longer
      // applied: the sprites draw on the crisp layer at full resolution and keep their own filters
      texOf[k.id] = tex;
      // a faced bubble is repainted onto the real sprite; a plain one just swaps its map
      for (const s of pool) if (s.alive && s.kindId === k.id) { if (s.w) wear(s, s.w, s.ink, s.big, s.rowN); else { s.mat.map = tex; s.mat.needsUpdate = true; } }
    }, undefined, () => { /* keep the dot */ });
  }
  /** The word faces: one CanvasTexture per (kind, ink, word), held in an LRU (race/wordFace.js). */
  const faces = createWordFaces();
  /** Names the base a face is painted on, so the face is rebuilt once when the kind's PNG lands. */
  const baseKey = (kindId) => kindId + (texOf[kindId] && texOf[kindId] !== dotTex ? ':png' : ':dot');

  // ---- pools ---------------------------------------------------------------
  const CAP = Q.bubbleCap || 160, SHARD_CAP = Q.bubbleShards || 64, VIEW_AHEAD = Q.bubbleViewAhead || 110;
  /** riptide (race/pickups.js): bubbles this far ahead slide into the kart's lane over this long. */
  const PULL_M = 40, PULL_SEC = 0.5;
  const group = new THREE.Group();
  group.name = 'race-bubbles';
  scene.add(group);
  const pool = [];
  for (let i = 0; i < CAP; i++) {
    const mat = new THREE.SpriteMaterial({ transparent: true, depthWrite: false, opacity: 1 });
    const sprite = new THREE.Sprite(mat);
    sprite.visible = false; sprite.layers.set(CRISP_LAYER);
    group.add(sprite);
    pool.push({ sprite, mat, slot: i, eventId: null, rowId: 0, alive: false, kindId: 'treat', placement: 'lane', d: 0, x: 0, h: LANE_H,
      x0: 0, baseH: LANE_H, ride: 0, phase: 0, age: 0, size: 1, scale: 1, popT: -1, missed: false,
      w: '', ink: null, big: false, rowN: 0 });   // the word painted on this bubble's face (race/wordFace.js)
  }
  const shards = [];
  for (let i = 0; i < SHARD_CAP; i++) {
    const mat = new THREE.SpriteMaterial({ map: dotTex, transparent: true, depthWrite: false, opacity: 0 });
    const sprite = new THREE.Sprite(mat);
    sprite.visible = false; sprite.layers.set(CRISP_LAYER);
    group.add(sprite);
    shards.push({ sprite, mat, alive: false, age: 0, life: 0.5, p0: new THREE.Vector3(), r: null, u: null, vr: 0, vu: 0, size: 0.2 });
  }
  let liveCount = 0;
  let lastKartD = 0;
  let density = 1;
  // With a track loaded the density knob changes what it means: the seeded lanes must keep dressing
  // the road exactly as they do without one, so density gates the CUE spawns instead (CHART.md).
  let tracked = false;
  // A track whose road came out of a transcript: seedChunk stops dressing the road at all, because
  // the words are what the player is meant to read on it. The owner's law: too many bubbles is no
  // bubbles, the fun is realising the bubbles are the lyric.
  let sparse = false;
  let rowSeq = 0;
  let reachX = POP_HIT_X, reachH = POP_HIT_H;   // the pop box (setReach: poppers, the wand)
  let sweep = false;                            // the pump: the whole road pops (setSweep)
  let reachAll = true;                          // false: the wider box is for treats alone (the wand)
  let pull = false;                             // riptide: the road ahead slides into the lane (setPull)
  const popCbs = [], missCbs = [];
  const emit = (cbs, ev) => { for (const cb of cbs) { try { cb(ev); } catch (e) { /* listener bug, not ours */ } } };

  function freeSlot(s) { if (!s.alive) return; s.alive = false; s.sprite.visible = false; liveCount--; }
  function takeSlot() {
    let s = null;
    for (let i = 0; i < pool.length; i++) if (!pool[i].alive) { s = pool[i]; break; }
    if (!s) {   // full: recycle the bubble farthest from the kart
      let far = -1;
      for (const p of pool) { const a = Math.abs(relD(p.d, lastKartD)); if (a > far) { far = a; s = p; } }
      freeSlot(s);
    }
    s.alive = true; liveCount++;
    return s;
  }

  const liveOf = (id) => { let n = 0; for (const p of pool) if (p.alive && p.kindId === id) n++; return n; };
  /** Weighted kind roll: room bias, intensity gate, the treats-only opening, and a per-placement nudge. */
  function roll(placement) {
    const gate = effectGate();
    const extra = (k) => {
      if (k.kind === 'effect' && gate < 1) { if (gate <= 0) return 0; return gate * (placement === 'rain' ? 0.5 : 1); }
      if (k.id === 'video' && liveOf('video') > 0) return 0;      // one video on the road at a time
      if (k.id === 'freeze' && liveOf('freeze') > 1) return 0;
      if (placement === 'air' && k.id === 'golden') return 3;     // gold favours the air line
      if (placement === 'rain' && k.kind === 'effect') return 0.5;
      return 1;
    };
    return rollKind(intensity(), roomBias(), extra).id;
  }

  /** Every kind this field has PUT ON THE ROAD, counted. Never read by the game: it is the one
   *  honest answer to "did a darkened kind ever spawn" (race/smoke/word-flash-check.mjs), because
   *  place() is the single door every roll, lane line, rain, cue and row goes through. */
  const placed = new Map();

  function place(kindId, placement, d, x, h) {
    const k = KIND_BY_ID[kindId] || KIND_BY_ID.treat;
    placed.set(k.id, (placed.get(k.id) || 0) + 1);
    const s = takeSlot();
    s.kindId = k.id; s.placement = placement;
    s.d = layout.wrap(d); s.x = clamp(x, -LANE_X_MAX, LANE_X_MAX); s.x0 = s.x;
    // `h` is metres above THE REACHABLE LINE, except rain, which starts at the tube's own ceiling
    // and falls onto it (update below): a ceiling that rode a 4 m flight arc would be outside the tube.
    s.ride = rideAt(s.d);
    s.h = placement === 'rain' ? h : h + s.ride; s.baseH = s.h; s.phase = Math.random() * Math.PI * 2; s.age = 0;
    s.size = sizeOf(k.id); s.scale = placement === 'spawn' ? 0 : 1; s.popT = -1; s.missed = false; s.eventId = null; s.rowId = 0;
    s.w = ''; s.ink = null; s.big = false; s.rowN = 0;
    s.mat.map = texOf[k.id]; s.mat.color.set(k.tint); s.mat.opacity = 1; s.mat.needsUpdate = true;
    s.sprite.scale.setScalar(s.size * s.scale);
    layout.toWorld(s.d, s.x, s.h, s.sprite.position);
    s.sprite.visible = true;
    return s;
  }

  /** The bubble puts a word ON its face. The material goes white because the kind's tint is
   *  already multiplied into the canvas: tinting a second time would drag the ink toward it.
   *  `big` is the trigger row's centre bubble and the accent word, drawn BIG_SCALE larger. */
  function wear(s, w, ink, big, rowN) {
    const text = String(w || '');
    if (!text) return;
    const k = KIND_BY_ID[s.kindId] || KIND_BY_ID.treat;
    const tex = faces.faceFor({ key: baseKey(k.id), word: text, ink: ink || undefined, image: texOf[k.id] && texOf[k.id].image, tint: k.tint });
    s.w = text; s.ink = ink || null; s.big = !!big; s.rowN = rowN || 1;
    s.size = sizeOf(k.id) * (s.big ? BIG_SCALE : 1);
    if (!tex) return;
    s.mat.map = tex; s.mat.color.set('#ffffff'); s.mat.needsUpdate = true;
    s.sprite.scale.setScalar(s.size * s.scale);
  }

  // ---- placements ----------------------------------------------------------
  const seeded = new Set();
  const chunkRecs = [];
  const LANES = [-2.2, -1.1, 0, 1.1, 2.2];

  /** The Tea Garden start straight: a run-up of three, a slow zigzag of treats the whole length,
   *  a chevron (tip forward) every 4th beat, and one golden at the end. Readable at the first lap. */
  function seedStartStraight(chunk) {
    for (let i = 0; i < 3; i++) place('treat', 'lane', chunk.d0 - 8 + i * LANE_STEP, 0, LANE_H);
    const n = Math.floor((chunk.d1 - chunk.d0 - 6) / LANE_STEP);
    for (let i = 0; i < n; i++) {
      const d = chunk.d0 + 2 + i * LANE_STEP, x = 1.6 * Math.sin(i * Math.PI / 8);
      place('treat', 'lane', d, x, LANE_H);
      if (i % 4 === 3) { place('treat', 'lane', d - 1.3, x - 1.3, LANE_H); place('treat', 'lane', d - 1.3, x + 1.3, LANE_H); }
    }
    place('golden', 'lane', chunk.d1 - 2.5, 0, LANE_H);
  }

  /** Lane lines the kart can thread and ramp air lines. */
  function seedChunk(chunk) {
    if (!chunk || seeded.has(chunk.id)) return;
    seeded.add(chunk.id);
    chunkRecs.push({ id: chunk.id, d0: chunk.d0, d1: chunk.d1 });
    const len = Math.max(0, chunk.d1 - chunk.d0);
    if (sparse) {   // the file dresses this road, not us: one golden to say where the chunk ended, nothing else
      if (chunk.kind !== 'gate' && len > 8) place('golden', 'lane', chunk.d1 - 2.5, 0, LANE_H);
      return;
    }
    if (chunk.id === 1 && chunk.room === 'teagarden' && chunk.kind === 'straight') seedStartStraight(chunk);
    else if (chunk.kind !== 'gate' && len > 8) {
      const lines = Math.max(1, Math.round((len / 26) * (tracked ? 1 : density)));
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
        // the air line is the kart's own flight now: `h` is above the arc (rideAt), so the line
        // threads the jump instead of hanging a metre and a half over the top of it
        const x = rand(-0.6, 0.6);
        for (let i = 0; i < AIR_N; i++) place(roll('air'), 'air', f.d + AIR_STEP * (i + 1), x, LANE_H);
      }
    }
  }

  function spawnAhead(kartD, n = 1) {
    for (let i = 0; i < n; i++) place(roll('spawn'), 'spawn', kartD + rand(35, 60), rand(-LANE_X_MAX, LANE_X_MAX), LANE_H);
  }
  function rain(kartD, n = 1) {
    for (let i = 0; i < n; i++) place(roll('rain'), 'rain', kartD + rand(18, 44), rand(-LANE_X_MAX, LANE_X_MAX), CEILING_H);
  }
  /** An explicit placement from a track cue (CHART.md): the run has already worked the depth out at
   *  the kart's speed, so nothing is rolled or gated by intensity here. Returns the slot id, -1 when
   *  the pool is full (a decorative cue never steals a live bubble), when the kind is dark
   *  (bubbleKinds.js spawn:false), or when density has gated this one out.
   *  Over 1, density is the chance of a second bubble beside the first.
   *  `script` is THE ROAD'S OWN SCRIPT (race/cues.js marks the word bubbles and the trigger rows):
   *  see spawnRow below for why it goes down whatever the density knob is saying. */
  function spawnAt({ kindId, placement = 'lane', d, x = 0, h, eventId = null, w = '', ink = null, big = false, script = false } = {}) {
    if (liveCount >= CAP && !script) return -1;
    if (KIND_BY_ID[kindId] && KIND_BY_ID[kindId].spawn === false) return -1;   // a dark kind: no chart may place one
    if (tracked && !script) {
      if (density <= 0) return -1;
      if (density < 1 && Math.random() >= density) return -1;
    }
    const top = h == null ? (placement === 'rain' ? CEILING_H : placement === 'air' ? 2.6 : LANE_H) : h;
    const s = place(kindId, placement, d, x, top);
    s.eventId = eventId;
    if (w) wear(s, w, ink, big, 1);
    if (tracked && density > 1 && liveCount < CAP && Math.random() < density - 1) {
      place(kindId, placement, d + 2.4, x + (x > 0 ? -1.1 : 1.1), top).eventId = eventId;
    }
    return s.slot;
  }
  /** A ROW from a track cue: one bubble at every x the cue named, all at one depth, all carrying the
   *  same eventId and one row id. All or nothing, three ways: a row that would not fit the pool is
   *  not laid at all (a row with a hole in it is a row the kart drives through), the density gate is
   *  rolled ONCE for the whole line rather than per bubble, and density over 1 never doubles it,
   *  because a doubled row is just a thicker wall and the wall was already unavoidable.
   *
   *  THE SCRIPT IS NOT DECORATION (2026-09-08). `script` marks a row the FILE asked for - a word
   *  bubble off the transcript, a trigger row - and neither of the two refusals above may touch one.
   *  `density` is the road-dressing knob: race/cues.js turns it down to 0.6 on a release and to 0
   *  through a silence, and until now every word bubble was rolled against it one at a time. On a
   *  worded road that is the road losing three words of a four word line at random and leaving one
   *  bubble sitting on its own, which is exactly what the owner saw ("I see maybe 1 word out of a
   *  phrase"): 22 percent of every word on the shelf, 44 percent of the lines thinned, 353 lines
   *  gone altogether. A line the voice is saying is not the road being busy, so a script row skips
   *  the density roll and, when the pool is full, RECYCLES the bubble farthest from the kart
   *  (takeSlot already does, and 60 m of road behind the kart is a cheaper thing to lose than the
   *  word she is saying) instead of refusing to lay the line at all.
   *  Returns how many went down, 0 for a row that was gated out. */
  function spawnRow({ kindId, kindIds = null, placement = 'lane', d, h, xs, eventId = null, w = '', ink = null, big = false, script = false } = {}) {
    const list = Array.isArray(xs) ? xs.filter((x) => Number.isFinite(Number(x))) : [];
    if (!list.length) return 0;
    if (KIND_BY_ID[kindId] && KIND_BY_ID[kindId].spawn === false) return 0;   // a dark kind: no chart may place one
    // a script row may evict, but never more than half the pool: a line longer than that is not a line
    if (liveCount + list.length > CAP && !(script && list.length * 2 <= CAP)) return 0;
    if (tracked && !script) {
      if (density <= 0) return 0;
      if (density < 1 && Math.random() >= density) return 0;
    }
    const top = h == null ? (placement === 'rain' ? CEILING_H : placement === 'air' ? 2.6 : LANE_H) : h;
    const id = ++rowSeq;
    // kindIds: one kind per x for a row that is not all one thing (the rabbit foot's golden centre)
    // EVERY bubble of a row wears the set's word now that the word is the bubble's own face: five
    // plates of text was a wall of text, five bubbles that each say the word is the wall the row
    // already is. Only the centre one is drawn big. A row of one (a word bubble, cues.js
    // `case 'word'`) is its own centre.
    const mid = list.length >> 1;
    list.forEach((x, i) => {
      const s = place((kindIds && kindIds[i]) || kindId, placement, d, Number(x), top);
      s.eventId = eventId; s.rowId = id;
      if (w) wear(s, w, ink, big && i === mid, list.length);
    });
    return id;   // the row's id: run.js hands it to race/sync.js, which moveRow()s it onto its word
  }
  /** A row goes to depth `d`: the kart's speed changed inside the lookahead, so the word will be said
   *  somewhere else on the road (race/sync.js re-places it every frame until the kart is on it). A
   *  bubble already popped or slipped stays where it is; the sprite follows `d` on the next update. */
  function moveRow(id, d) {
    if (!id || !Number.isFinite(Number(d))) return 0;
    const dd = layout.wrap(Number(d));
    const ride = rideAt(dd);
    let n = 0;
    // the row moved, so the line under it moved: keep its height above the line, not above 0
    for (const s of pool) if (s.alive && s.rowId === id && s.popT < 0 && !s.missed) { s.d = dd; s.baseH += ride - s.ride; s.ride = ride; n++; }
    return n;
  }
  /** The row has been settled by one of its own: nobody else in it may report a miss. `missed` is
   *  only ever read by the miss gate, so marking the siblings is all it takes. */
  function spendRow(id) {
    if (!id) return;
    for (const o of pool) if (o.alive && o.rowId === id) o.missed = true;
  }

  // ---- pop -----------------------------------------------------------------
  const _r = new THREE.Vector3(), _u = new THREE.Vector3();
  // burst frames come from a fixed ring (no per-pop allocation): one right/up pair per burst,
  // shared by its shards; 16 pairs outlive any shard (life <= 0.6 s, 64 shards, 7..12 per burst)
  const DIR_RING = 16, dirRing = [];
  for (let i = 0; i < DIR_RING; i++) dirRing.push({ r: new THREE.Vector3(), u: new THREE.Vector3() });
  let dirNext = 0, shardNext = 0;
  function burst(s, golden) {
    const f = layout.frameAtDepth(s.d);
    const dirs = dirRing[dirNext]; dirNext = (dirNext + 1) % DIR_RING;
    dirs.r.copy(f.right); dirs.u.copy(f.up);
    const n = golden ? 12 : 7;
    for (let i = 0; i < n; i++) {
      // next free shard scanning from where the last burst stopped; a full ring steals the oldest slot
      let sh = null;
      for (let j = 0; j < SHARD_CAP; j++) { const c = shards[(shardNext + j) % SHARD_CAP]; if (!c.alive) { sh = c; shardNext = (shardNext + j + 1) % SHARD_CAP; break; } }
      if (!sh) { sh = shards[shardNext]; shardNext = (shardNext + 1) % SHARD_CAP; }
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
    spendRow(s.rowId);              // one of the row is the row: the rest may not be missed behind it
    const golden = k.id === 'golden';
    burst(s, golden);
    const strength = k.strength > 0 ? clamp(k.strength + 0.35 * intensity() + Math.random() * 0.1, 0, 1) : 0;
    emit(popCbs, { id: k.id, kind: k.kind, payload: k.payload, overlayKind: k.overlayKind, strength,
      points: k.points, placement: s.placement, x: s.x, d: s.d, eventId: s.eventId, worldPos: s.sprite.position.clone() });
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
        // riptide: x slides to the kart (x0 too, so a spawn's wobble rides along with it)
        if (pull && rel > 0 && rel <= PULL_M) { const k = Math.min(1, dt / PULL_SEC); s.x += (kart.x - s.x) * k; s.x0 += (kart.x - s.x0) * k; }
        if (s.placement === 'lane') { s.h = s.baseH + bob; }
        else if (s.placement === 'air') { s.h = s.baseH + 0.08 * Math.sin(t * 2.6 + s.phase); }
        else if (s.placement === 'spawn') {
          const a = Math.min(1, s.age / 0.45);
          s.scale = 1 - (1 - a) * (1 - a);
          s.x = clamp(s.x0 + 0.55 * Math.sin(t * 1.7 + s.phase), -LANE_X_MAX, LANE_X_MAX);
          s.h = s.ride + LANE_H + bob;
        } else if (s.placement === 'rain') {
          const rest = s.ride + LANE_H;   // it lands ON the line, wherever the line is at its depth
          if (s.age < RAIN_FALL) { const k = s.age / RAIN_FALL; s.h = CEILING_H - (CEILING_H - rest) * k * k; }
          else if (s.age < RAIN_FALL + RAIN_REST) { s.h = rest + bob; }
          else {
            const f = (s.age - RAIN_FALL - RAIN_REST) / RAIN_FIZZLE;
            s.h = rest + bob; s.scale = 1 - 0.4 * f; s.mat.opacity = 1 - f;
            if (f >= 1) { freeSlot(s); continue; }
          }
        }
        // behind the kart: a treat that slipped by is a miss, further back it is gone
        if (rel < -MISS_BEHIND && rel > -DROP_BEHIND - 40 && !s.missed) {
          s.missed = true;
          spendRow(s.rowId);        // a whole row that slipped by is one miss, not one per bubble
          const k = KIND_BY_ID[s.kindId];
          if (k.kind === 'treat') emit(missCbs, { id: k.id, points: k.points, d: s.d, x: s.x, h: s.h, eventId: s.eventId });
        }
        if (rel < -DROP_BEHIND && rel > -DROP_BEHIND - 40) { freeSlot(s); continue; }
        const wide = reachAll || KIND_BY_ID[s.kindId].kind === 'treat';   // the wand reaches for treats alone
        if (Math.abs(rel) < POP_HIT_D && (sweep || (Math.abs(s.x - kart.x) < (wide ? reachX : POP_HIT_X) && Math.abs(s.h - kart.h) < (wide ? reachH : POP_HIT_H)))) pop(s);
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
    faces.dispose();
    popCbs.length = 0; missCbs.length = 0; seeded.clear(); chunkRecs.length = 0;
  }

  return {
    seedChunk, spawnAhead, rain, spawnAt, spawnRow, moveRow, update, dispose,
    onPop(cb) { if (typeof cb === 'function') popCbs.push(cb); },
    onMiss(cb) { if (typeof cb === 'function') missCbs.push(cb); },
    setDensity(mult) { const v = Number(mult); density = clamp(isFinite(v) ? v : 1, tracked ? 0 : 0.25, 3); },
    /** A track is loaded: setDensity now gates the cue spawns, not the seeded lanes. */
    setTracked(on) { tracked = !!on; },
    /** The loaded track has a transcript under it: seedChunk stands down to the chunk's golden. */
    setSparse(on) { sparse = !!on; },
    /** Poppers / the wand: widen the pop box (X and H) by mult; 1 restores it. treatsOnly keeps an
     *  effect bubble at the plain box, so the wand is a magnet for treats and never a shortcut into a payload. */
    setReach(mult, treatsOnly = false) { const m = clamp(Number(mult) || 1, 0.5, 3); reachX = POP_HIT_X * m; reachH = POP_HIT_H * m; reachAll = !treatsOnly; },
    /** The pump: while on, every bubble the kart's depth crosses pops, the whole road wide and high. */
    setSweep(on) { sweep = !!on; },
    /** riptide: while on, everything inside PULL_M ahead slides into the kart's lane. */
    setPull(on) { pull = !!on; },
    /** Every live bubble in track space, as it was placed. Never read by the game: it is what
     *  race/smoke/slope-check.mjs holds against THE REACHABLE LINE, so the one honest answer to
     *  "is anything hung inside a slope or under a flight" comes out of the real placement code. */
    slots() {
      const out = [];
      for (const s of pool) if (s.alive) out.push({ kindId: s.kindId, placement: s.placement, d: s.d, x: s.x, h: s.h, baseH: s.baseH, ride: s.ride, rowId: s.rowId });
      return out;
    },
    /** What race/smoke/face-check.mjs reads: the face cache, and what every live word bubble
     *  is wearing this frame, nearest the kart first, plus `placed`, every kind this run has put
     *  on the road counted by id. Never read by the game itself. */
    faceReport() {
      const worn = [];
      for (const s of pool) {
        if (!s.alive || !s.w) continue;
        worn.push({ w: s.w, ink: s.ink, big: s.big, rowN: s.rowN, rowId: s.rowId, kindId: s.kindId,
          ahead: relD(s.d, lastKartD), size: s.size, popped: s.popT >= 0, faced: !!(s.mat.map && s.mat.map !== dotTex && s.mat.map.isCanvasTexture) });
      }
      worn.sort((a, b) => a.ahead - b.ahead);
      return { ...faces.report(), pool: pool.length, live: liveCount, worn, placed: Object.fromEntries(placed) };
    },
    get liveCount() { return liveCount; },
  };
}
