/* ============================================================================
 * wallPosters.js - the Four Chambers wall dressing: the deeper you fall, the
 * more of the tube wall is plastered with the user's own media.
 *
 * Inspiration: the Explore page's decorative wall screens (sectorBuilder.js) -
 * flat art pasted onto the tunnel wall. Here it's a REGION-IDENTITY layer:
 *   Region I  (The Long Fall)     - bare (open sky, the fall itself is the beat)
 *   Region II (The Hall of Doors) - a few posters here and there
 *   Region III(The Mad Garden)    - the wall gets busy
 *   Region IV (The Court of Hearts) - almost wall-to-wall, overwhelming
 *
 * Design for cost: posters are flat, UNLIT quads flush on the tube wall, mounted
 * in the spine's local frame (same math the ribbons/sparkles/cards use, so they
 * ride the treadmill loop). They form a RECYCLING ring buffer - a slot that
 * falls behind the camera is re-thrown ahead into the fog with a fresh angle and
 * texture, so a fixed pool of meshes covers an endless fall.
 *
 * The textures are a SMALL SHARED POOL of the user's images, decoded once and
 * handed round-robin to the slots. On a wall that scrolls past at fall speed
 * the repetition is invisible, and it turns "plaster dozens of quads" into
 * "sample from ~24 textures" - near-zero ongoing cost. A capped few of those
 * entries are ANIMATED gifs: real frames decoded off-thread by the spawner's
 * gifWorker into small ImageBitmaps, cycled at pool level so every slot sharing
 * the texture animates in sync for one redraw. Gifs past the cap join as
 * first-frame stills. No user media -> the layer simply stays empty (like the
 * card spawner).
 * ==========================================================================*/

import * as THREE from 'three';
import { Q } from '../shared/quality.js';
import { loadTexture } from '../shared/assets.js';
import { RADIUS } from './tunnel.js';

// How many DISTINCT textures the shared pool holds. Slots sample from these, so
// this bounds all decode + GPU memory the wall ever costs. Mobile stays lean.
const POOL_SIZE = Q.tier === 'mobile' ? 12 : 24;
const POOL_INFLIGHT = 3;                 // concurrent decodes while filling the pool
const TEX_MAX_DIM = Q.tier === 'mobile' ? 256 : 384; // posters are small on screen

// Animated posters cache REAL decoded frames (sampling a live <img> can't work:
// canvas drawImage always takes a gif's first frame per the HTML spec). The
// frames are small downscaled ImageBitmaps but still the wall's biggest memory
// item, so both the animated-entry count and the frames kept per gif are
// capped; long gifs loop their opening and extra gifs join as stills.
const ANIM_MAX = Q.tier === 'mobile' ? 3 : 8;          // animated entries in the pool
const ANIM_MAX_FRAMES = Q.tier === 'mobile' ? 10 : 20; // frames kept per gif
const ANIM_UPLOADS_PER_TICK = 2;         // texture redraws per anim tick (~20fps)

// Slot budget indexed by the 1-based waveIndex setRegion() is called with:
//   [0] non-region / bare   [1] Region I   [2] Region II   [3] Region III   [4] Region IV
// Region I is BARE (0) on purpose - the fall starts on an empty wall so the very
// first poster in Region II is a surprise. Density then ramps to IV's near-
// wall-to-wall. Mobile roughly halves it; the near field carries the look and fog
// swallows the rest, so IV reads "plastered" without wrecking fill-rate.
const REGION_MAX = (Q.tier === 'mobile')
  ? [0, 0, 6, 16, 30]  // -, I, II, III, IV
  : [0, 0, 12, 34, 60];

const AHEAD_MIN = 8;    // a recycled slot re-throws this..MAX units ahead of the POV
const AHEAD_MAX = 78;   // within the fog sightline so it fades in, never pops
const BEHIND = 12;      // slots this far behind the camera are recycled
const WALL_INSET = 0.14;// how far inside RADIUS the poster sits (just off the wall)

const rand = (a, b) => a + Math.random() * (b - a);

export function createWallPosters({ scene, layout, media, renderer, camera }) {
  const group = new THREE.Group();
  scene.add(group);

  const unit = new THREE.PlaneGeometry(1, 1);
  const pool = [];           // shared decoded textures {tex}
  let poolInflight = 0;
  let poolFilling = false;
  let disposed = false;      // decodes resolving after teardown must not resurrect the pool

  const slots = [];          // { mesh, mat, active, depth }
  let targetCount = 0;       // region ceiling
  let liveCount = 0;         // slots currently shown (eased toward targetCount)
  let addCooldown = 0;       // seconds between activating slots (graceful ramp)

  // The poster the player is holding to face the camera at it. It's frozen from
  // the recycle/retire pass and RIDES ALONG at a fixed depth-gap ahead of the
  // camera (advanceHeld), so it travels with us on the wall instead of flying by.
  let _heldSlot = null;
  let _heldGap = null;       // depth ahead of the camera, captured at grab
  const _wp = new THREE.Vector3();

  // Reused scratch so per-frame placement allocates nothing.
  const _inward = new THREE.Vector3();
  const _right = new THREE.Vector3();
  const _up = new THREE.Vector3();
  const _m = new THREE.Matrix4();

  // ---- shared texture pool ---------------------------------------------------
  const mkTex = (c) => {
    const tex = new THREE.CanvasTexture(c);
    tex.colorSpace = THREE.SRGBColorSpace;
    tex.generateMipmaps = false;
    tex.minFilter = THREE.LinearFilter; tex.magFilter = THREE.LinearFilter;
    if (renderer) { try { renderer.initTexture(tex); } catch (e) { /* ignore */ } }
    return tex;
  };
  const mkCanvas = (w, h) => {
    const shrink = Math.min(1, TEX_MAX_DIM / Math.max(w, h));
    const c = document.createElement('canvas');
    c.width = Math.max(2, Math.round(w * shrink));
    c.height = Math.max(2, Math.round(h * shrink));
    const x = c.getContext('2d');
    x.imageSmoothingQuality = 'high';
    return { c, x };
  };

  // ---- gif decode worker -------------------------------------------------
  // The same off-thread decoder the card spawner uses (gifWorker.js + omggif):
  // frames stream back as ready-to-draw ImageBitmaps, the main thread never
  // blocks on a decode. Feature-gated like the spawner; anything without a
  // worker path just gets first-frame stills.
  let gifWorker = null, gifWorkerDead = false, gifJobId = 0;
  const gifJobs = new Map(); // id -> { frame(msg), done(), fail() }
  function failGifWorker() {
    gifWorkerDead = true;
    for (const job of gifJobs.values()) job.fail('worker dead');
    gifJobs.clear();
    if (gifWorker) { try { gifWorker.terminate(); } catch (e) { /* ignore */ } gifWorker = null; }
  }
  function ensureGifWorker() {
    if (gifWorkerDead) return null;
    if (gifWorker) return gifWorker;
    if (typeof Worker !== 'function' || typeof OffscreenCanvas !== 'function'
      || typeof createImageBitmap !== 'function') { gifWorkerDead = true; return null; }
    try { gifWorker = new Worker('/dtrh/engine/gifWorker.js', { type: 'module' }); }
    catch (e) { gifWorkerDead = true; return null; }
    gifWorker.onmessage = (e) => {
      const m = e.data;
      const job = gifJobs.get(m.id);
      if (!job) { // job cancelled: frames may still be in flight - free them
        if (m.frame) { try { m.frame.bitmap.close(); } catch (err) { /* ignore */ } }
        return;
      }
      if (m.error) { gifJobs.delete(m.id); job.fail(m.error); }
      else if (m.frame) job.frame(m);
      else if (m.done) { gifJobs.delete(m.id); job.done(); }
    };
    gifWorker.onerror = failGifWorker; // script load / module failure
    return gifWorker;
  }

  // Decode a gif into an ANIMATED pool item (resolves on the first frame so the
  // wall fills fast; later frames keep streaming in and the loop just grows).
  // Resolves null on any failure so decodeOne falls back to a still.
  function decodeGifFrames(buf, name) {
    const w = ensureGifWorker();
    if (!w) return Promise.resolve(null);
    return new Promise((resolve) => {
      const id = ++gifJobId;
      let item = null;
      const watchdog = setTimeout(() => { // a silently-hung job must not wedge the pool pump
        if (!gifJobs.delete(id)) return;
        try { if (gifWorker) gifWorker.postMessage({ cancel: id }); } catch (e) { /* ignore */ }
        resolve(null);
      }, 8000);
      const job = {
        frame(m) {
          if (item) {
            if (item.anim.dead) { try { m.frame.bitmap.close(); } catch (e) { /* ignore */ } }
            else item.anim.frames.push(m.frame);
            return;
          }
          clearTimeout(watchdog);
          const c = document.createElement('canvas');
          c.width = m.w; c.height = m.h;
          const x = c.getContext('2d');
          x.drawImage(m.frame.bitmap, 0, 0);
          item = {
            tex: mkTex(c), aspect: m.aspect, assetName: name, ctx: x, jobId: id,
            anim: { frames: [m.frame], i: 0, nextAt: performance.now() + m.frame.durMs, dead: false },
          };
          resolve(item);
        },
        done() { clearTimeout(watchdog); if (!item) resolve(null); },
        // failed mid-stream: the poster just loops the frames it already has
        fail() { clearTimeout(watchdog); if (!item) resolve(null); },
      };
      gifJobs.set(id, job);
      try { w.postMessage({ id, buf, maxDim: TEX_MAX_DIM, maxFrames: ANIM_MAX_FRAMES }); }
      catch (e) { clearTimeout(watchdog); gifJobs.delete(id); resolve(null); }
    });
  }

  function animCount() {
    let n = 0;
    for (const item of pool) if (item.anim) n += 1;
    return n;
  }

  // Decode one user image into a downscaled texture. An animated GIF (within the
  // ANIM_MAX budget) becomes a frame-cycled texture via the worker; everything
  // else is a single first-frame snapshot (near-zero ongoing cost, as before).
  async function decodeOne() {
    const entry = media.drawKind ? (media.drawKind('image') || media.draw()) : media.draw();
    if (!entry || entry.kind !== 'image') return null; // stills only (videos stay on cards)
    const acquired = await entry.acquire();
    if (!acquired) return null;
    const name = entry.name || null;
    try {
      const blob = await (await fetch(acquired.url)).blob();
      const animated = blob.type === 'image/gif'
        || /\.gif(\?|$)/i.test(name || '') || /\.gif(\?|$)/i.test(acquired.url || '');
      if (animated && animCount() < ANIM_MAX) {
        const item = await decodeGifFrames(await blob.arrayBuffer(), name);
        if (item) return item; // worker failed: fall through to a still
      }
      let bmp = null;
      try { bmp = await createImageBitmap(blob); } // gif blob -> first frame
      catch (e) { return null; } // hosted WebView2 has createImageBitmap; bail otherwise
      const w = bmp.width, h = bmp.height;
      const { c, x } = mkCanvas(w, h);
      x.drawImage(bmp, 0, 0, c.width, c.height);
      bmp.close();
      return { tex: mkTex(c), aspect: w / h, assetName: name };
    } catch (e) { return null; }
    finally { if (acquired.release) acquired.release(); }
  }

  // Advance animated posters to the cached frame that should be showing NOW
  // (same catch-up walk as the spawner's advanceGif - a hitch skips frames
  // instead of playing slow-mo). Pool-level, so many slots sharing a texture
  // animate in sync for one redraw. Throttled to ~20fps with a small per-tick
  // upload budget; the round-robin cursor keeps every gif moving under load.
  let poolAnimT = 0;
  let animRotate = 0;
  const _due = [];
  function tickPool(dt) {
    poolAnimT += dt;
    if (poolAnimT < 0.05) return;
    poolAnimT = 0;
    const t = performance.now();
    _due.length = 0;
    for (const item of pool) {
      const a = item.anim;
      if (a && !a.dead && a.frames.length > 1 && t >= a.nextAt) _due.push(item);
    }
    if (!_due.length) return;
    const n = Math.min(ANIM_UPLOADS_PER_TICK, _due.length);
    for (let k = 0; k < n; k++) {
      const item = _due[(animRotate + k) % _due.length];
      const a = item.anim;
      let guard = a.frames.length * 2; // long stalls resync below instead of walking forever
      while (t >= a.nextAt && guard-- > 0) {
        a.i = (a.i + 1) % a.frames.length;
        a.nextAt += a.frames[a.i].durMs;
      }
      if (t >= a.nextAt) a.nextAt = t + a.frames[a.i].durMs;
      try { item.ctx.drawImage(a.frames[a.i].bitmap, 0, 0); item.tex.needsUpdate = true; }
      catch (e) { /* ignore */ }
    }
    animRotate += n;
    _due.length = 0;
  }

  // Trickle the pool up to POOL_SIZE (called while a region wants posters). A few
  // in flight at a time so a fresh chamber never fires two dozen decodes at once.
  function fillPool() {
    if (poolFilling || !media.hasUserMedia()) return;
    poolFilling = true;
    const pump = () => {
      while (pool.length + poolInflight < POOL_SIZE && poolInflight < POOL_INFLIGHT) {
        poolInflight += 1;
        decodeOne().then((item) => {
          poolInflight -= 1;
          if (disposed) {
            if (item) {
              try { item.tex.dispose(); } catch (e) { /* ignore */ }
              if (item.anim) { item.anim.dead = true; for (const f of item.anim.frames) { try { f.bitmap.close(); } catch (e) { /* ignore */ } } }
            }
            return;
          }
          if (item) { pool.push(item); pump(); }
          else if (pool.length + poolInflight < 1 && poolInflight === 0) poolFilling = false;
          else pump();
        }).catch(() => { poolInflight -= 1; pump(); });
      }
      if (pool.length >= POOL_SIZE) poolFilling = false;
    };
    pump();
  }

  function anyTex() {
    return pool.length ? pool[(Math.random() * pool.length) | 0] : null;
  }

  // ---- slots -----------------------------------------------------------------
  function makeSlot() {
    const mat = new THREE.MeshBasicMaterial({
      map: null, transparent: true, opacity: 0.92,
      side: THREE.DoubleSide, depthWrite: true, toneMapped: false,
    });
    const mesh = new THREE.Mesh(unit, mat);
    mesh.visible = false;
    mesh.renderOrder = -1; // wall dressing draws under the cards/pickups
    group.add(mesh);
    const slot = { mesh, mat, active: false, depth: 0, assetName: null, angle: 0, wallR: RADIUS, roll: 0 };
    mesh.userData = { type: 'poster', slot }; // grabbable: click-hold to face the camera at it
    slots.push(slot);
    return slot;
  }

  // Position + orient a poster flush on the wall at `depth`, from the slot's
  // stored angle/radius/roll. Split out of place() so a HELD poster can be
  // re-oriented every frame as it rides the tube along WITH us (stays framed on
  // the wall instead of flying by). The plane's +Z (face) points INWARD toward
  // the spine; +X runs down-tube (tangent); a small roll gives the collage tilt.
  function orient(slot, depth) {
    const fr = layout.frameAtDepth(depth);
    const ca = Math.cos(slot.angle), sa = Math.sin(slot.angle);
    slot.mesh.position.copy(fr.pos)
      .addScaledVector(fr.normal, ca * slot.wallR)
      .addScaledVector(fr.binormal, sa * slot.wallR);
    _inward.copy(fr.normal).multiplyScalar(-ca).addScaledVector(fr.binormal, -sa).normalize();
    _right.copy(fr.tangent).normalize();
    _up.crossVectors(_inward, _right).normalize();
    _right.crossVectors(_up, _inward).normalize();
    _m.makeBasis(_right, _up, _inward);
    slot.mesh.quaternion.setFromRotationMatrix(_m);
    slot.mesh.rotateZ(slot.roll);
  }

  // Throw a slot onto the wall at a fresh depth/angle/roll with a pooled texture.
  function place(slot, depth) {
    const item = anyTex();
    if (!item) { slot.mesh.visible = false; return false; }
    slot.depth = depth;
    slot.angle = rand(0, Math.PI * 2);              // angle around the tube
    slot.wallR = RADIUS - WALL_INSET - rand(0, 0.18);
    slot.roll = rand(-0.5, 0.5);
    orient(slot, depth);
    // size: keep the poster's aspect, scaled to a chunky wall tile (bumped a
    // further ~20% - the user wanted these to read even larger)
    const base = rand(3.6, 5.76);
    const asp = item.aspect || 1;
    const w = asp >= 1 ? base : base * asp;
    const h = asp >= 1 ? base / asp : base;
    slot.mesh.scale.set(w, h, 1);
    slot.mat.map = item.tex;
    slot.assetName = item.assetName || null; // which user image this poster shows (for the "like")
    // born mid-Mirror-Moment: wear the favorite (the real map waits underneath)
    if (mirror) { slot.mirrorMap = slot.mat.map; slot.mat.map = mirror.tex; }
    slot.mat.needsUpdate = true;
    slot.mat.opacity = 0.92;
    slot.melt = null;
    slot.mesh.visible = true;
    return true;
  }

  // ---- biome seams: grab arbitration, the melt, the Mirror Moment -------------
  // THE BIOMES (game/biomeMech.js): the game may veto a poster hold ('melt' -
  // the Gallery's look-don't-touch) and wants to hear about holds that stick.
  let grabHooks = null;   // { policy(rec)->'allow'|'melt', onGrab(rec), onMelt(rec) }
  function setGrabHooks(h) { grabHooks = h || null; }

  // A melting poster droops off the wall and fades, then its slot retires.
  function meltSlot(slot) {
    if (!slot || !slot.active || slot.melt) return;
    slot.melt = { t: 0, dur: 0.6, h: slot.mesh.scale.y };
    if (_heldSlot === slot) { _heldSlot = null; _heldGap = null; }
  }
  /** THE BIOMES (Incognito): the wipe - every live poster droops off the wall
   * at once. Returns how many started melting. */
  function meltAll() {
    let n = 0;
    for (const slot of slots) {
      if (!slot.active || slot.melt || !slot.mesh.visible) continue;
      meltSlot(slot);
      if (slot.melt) n++;
    }
    return n;
  }

  function tickMelts(dt) {
    for (const slot of slots) {
      if (!slot.melt || !slot.active) continue;
      slot.melt.t += dt;
      const k = Math.min(1, slot.melt.t / slot.melt.dur);
      slot.mesh.scale.y = Math.max(0.03, slot.melt.h * (1 - k));
      slot.mat.opacity = 0.92 * (1 - k);
      if (k >= 1) {
        slot.active = false;
        slot.mesh.visible = false;
        slot.mat.map = null;
        slot.mirrorMap = undefined;
        slot.mat.opacity = 0.92;
        slot.melt = null;
        liveCount -= 1;
      }
    }
  }

  // The Mirror Moment (Hall of Mirrors): every live poster temporarily wears
  // ONE texture - the player's most-engaged asset. The pool keeps animating
  // underneath; restore just points the slots back at their real maps.
  let mirror = null;   // { url, tex }
  function setMirror(url) {
    if (!url) {
      if (!mirror) return;
      for (const s of slots) {
        if (s.mirrorMap === undefined) continue;
        s.mat.map = s.mirrorMap;
        s.mirrorMap = undefined;
        s.mat.needsUpdate = true;
      }
      mirror = null;   // texture is owned by the shared assets cache - no dispose
      return;
    }
    if (mirror && mirror.url === url) return;
    if (mirror) setMirror(null);   // swap: restore first
    mirror = { url, tex: loadTexture(url) };   // cached: repeat moments reuse the decode
    for (const s of slots) {
      if (!s.active || !s.mesh.visible || s.melt) continue;
      s.mirrorMap = s.mat.map;
      s.mat.map = mirror.tex;
      s.mat.needsUpdate = true;
    }
  }

  // ---- public API ------------------------------------------------------------
  // Region 0 = bare wall (Region I / non-region runs). 1..4 set the ceiling.
  function setRegion(regionIndex) {
    const i = Math.max(0, Math.min(REGION_MAX.length - 1, regionIndex | 0));
    targetCount = REGION_MAX[i];
    if (targetCount > 0) fillPool();
  }

  let paused = false;
  function setPaused(v) { paused = !!v; }

  // Junction chamber ahead (scene.setBlockedSpan): a poster placed in its span
  // floats in the hidden-trunk cut or hides behind the opaque room - which
  // starved the whole fork approach of wall gifs. Re-aim throws just short of
  // the fork; report null when the camera is already at the gate (no room).
  let blocked = null;
  function setBlockedSpan(s) { blocked = s || null; }
  function safeDepth(d, camDepth) {
    if (blocked && d > blocked.lo && d < blocked.hi) d = blocked.lo - rand(0.5, 6);
    return d > camDepth + 3 ? d : null;
  }

  // Rebase (junction dive): the loop was rebuilt onto the chosen branch, so every
  // placed slot sits on the OLD spine. Retire them all; update() re-places fresh
  // ones ahead on the new spine via layout.frameAtDepth as the fall continues.
  function reset() {
    for (const s of slots) {
      s.active = false; s.mesh.visible = false;
      s.melt = null; s.mirrorMap = undefined; s.mat.opacity = 0.92;
    }
    liveCount = 0;
    addCooldown = 0;
    _heldSlot = null;
  }

  // ---- poster hold (click a wall gif to face the camera at it) ----------------
  // The live poster meshes the scene raycast can hit.
  function getPickables() {
    const out = [];
    for (const s of slots) if (s.active && s.mesh.visible && !s.melt) out.push(s.mesh);
    return out;
  }
  // Seize the poster behind `mesh`; returns { assetName } (or null if it's gone).
  function grabPoster(mesh) {
    const slot = mesh && mesh.userData && mesh.userData.slot;
    if (!slot || !slot.active || slot.melt) return null;
    const rec = { assetName: slot.assetName || null };
    // THE BIOMES: the Gallery melts a denied hold right off the wall
    if (grabHooks && grabHooks.policy && grabHooks.policy(rec) === 'melt') {
      meltSlot(slot);
      if (grabHooks.onMelt) { try { grabHooks.onMelt(rec); } catch (e) { /* ignore */ } }
      return null;
    }
    _heldSlot = slot;
    _heldGap = null; // captured on the first advanceHeld() from the live cam depth
    if (grabHooks && grabHooks.onGrab) { try { grabHooks.onGrab(rec); } catch (e) { /* ignore */ } }
    return rec;
  }
  function releasePoster() { _heldSlot = null; _heldGap = null; }
  // Sticky Fingers: RIP the poster off the wall to wield it - the slot retires
  // instantly and the caller (spawner.grabExternalCard) builds the paddle card
  // around the SAME texture. The map stays pool-owned: the pool keeps its gif
  // animating and future posters may wear it again, so the card must never
  // dispose or Ken-Burns it. Same Gallery melt-veto as a plain hold.
  function ripPoster(mesh) {
    const slot = mesh && mesh.userData && mesh.userData.slot;
    if (!slot || !slot.active || slot.melt) return null;
    const rec = { assetName: slot.assetName || null };
    if (grabHooks && grabHooks.policy && grabHooks.policy(rec) === 'melt') {
      meltSlot(slot);
      if (grabHooks.onMelt) { try { grabHooks.onMelt(rec); } catch (e) { /* ignore */ } }
      return null;
    }
    // rip the REAL picture, not the Mirror Moment's borrowed favorite
    const tex = slot.mirrorMap !== undefined ? slot.mirrorMap : slot.mat.map;
    if (!tex) return null;
    const aspect = slot.mesh.scale.y > 0 ? slot.mesh.scale.x / slot.mesh.scale.y : 1;
    const pos = slot.mesh.getWorldPosition(new THREE.Vector3());   // the paddle eases in from here
    if (_heldSlot === slot) { _heldSlot = null; _heldGap = null; }
    slot.active = false;
    slot.mesh.visible = false;
    slot.mat.map = null;
    slot.mirrorMap = undefined;
    liveCount = Math.max(0, liveCount - 1);
    if (grabHooks && grabHooks.onGrab) { try { grabHooks.onGrab(rec); } catch (e) { /* ignore */ } }
    return { tex, aspect, assetName: rec.assetName, pos };
  }
  // Ride the held poster along with the camera AND ease it to be level with us
  // (gap -> 0), so the camera swings all the way onto it and we look straight
  // into its face (perpendicular) instead of at it obliquely from ahead. Call
  // BEFORE reading its world pos for the camera aim so the two never lag a frame.
  const HELD_GAP = 0;        // ride level with the camera = head-on, face-on view
  const HELD_GAP_LERP = 3;   // ease the grabbed depth-gap down to level
  function advanceHeld(camDepth, dt) {
    const slot = _heldSlot;
    if (!slot || !slot.active) return;
    if (_heldGap == null) _heldGap = Math.max(0, slot.depth - camDepth);
    _heldGap += (HELD_GAP - _heldGap) * Math.min(1, (dt || 0.016) * HELD_GAP_LERP);
    slot.depth = camDepth + _heldGap;
    orient(slot, slot.depth);
  }
  // World position of the held poster (parent group is at the origin, so this is
  // just its world transform) - fed to nav.setFaceTarget as the camera aim point.
  function getHeldWorldPos(out) {
    const t = out || _wp;
    if (_heldSlot) _heldSlot.mesh.getWorldPosition(t);
    return t;
  }

  function update(cam, camDepth, dt) {
    if (paused) return;
    tickPool(dt); // advance any animated (gif) poster textures
    tickMelts(dt); // THE BIOMES: denied holds drooping off the wall
    // ease the live count toward the region ceiling: add posters gradually (in
    // the fog ahead) and retire extras as they pass behind, so a chamber change
    // breathes in/out instead of snapping.
    addCooldown -= dt;
    if (liveCount < targetCount && pool.length && addCooldown <= 0) {
      const slot = slots.find((s) => !s.active) || makeSlot();
      slot.active = true;
      const d = safeDepth(camDepth + rand(AHEAD_MIN, AHEAD_MAX), camDepth);
      if (d != null && place(slot, d)) {
        liveCount += 1;
        addCooldown = 0.06; // ~16 posters/sec fill-in - brisk but not a pop-in wall
      } else {
        slot.active = false;
        if (d == null) addCooldown = 0.25; // parked at a fork gate: don't spin every frame
      }
    }

    // recycle / retire
    for (const slot of slots) {
      if (!slot.active) continue;
      if (slot === _heldSlot) continue; // frozen while the player holds it
      if (slot.melt) continue;          // mid-melt: tickMelts owns its exit
      if (slot.depth < camDepth - BEHIND) {
        if (liveCount > targetCount) {          // over ceiling: retire this one
          slot.active = false;
          slot.mesh.visible = false;
          slot.mat.map = null;
          slot.mirrorMap = undefined;
          liveCount -= 1;
        } else {                                 // re-throw ahead into the fog
          const d = safeDepth(camDepth + rand(AHEAD_MAX * 0.6, AHEAD_MAX), camDepth);
          if (d != null) place(slot, d);
          else {                                 // fork gate: retire; fill-in re-adds after
            slot.active = false;
            slot.mesh.visible = false;
            slot.mat.map = null;
            slot.mirrorMap = undefined;
            liveCount -= 1;
          }
        }
      }
    }
  }

  function dispose() {
    disposed = true;
    for (const slot of slots) { slot.mat.dispose(); }
    for (const item of pool) {
      try { item.tex.dispose(); } catch (e) { /* ignore */ }
      const a = item.anim;
      if (a) {
        a.dead = true;
        if (gifJobs.delete(item.jobId) && gifWorker) { // still decoding: stop the stream
          try { gifWorker.postMessage({ cancel: item.jobId }); } catch (e) { /* ignore */ }
        }
        for (const f of a.frames) { try { f.bitmap.close(); } catch (e) { /* ignore */ } }
        a.frames.length = 0;
      }
    }
    pool.length = 0;
    if (gifWorker) { try { gifWorker.terminate(); } catch (e) { /* ignore */ } gifWorker = null; gifJobs.clear(); }
    scene.remove(group);
    unit.dispose();
  }

  return { setRegion, setPaused, setBlockedSpan, update, dispose, reset, getPickables, grabPoster, releasePoster, ripPoster, advanceHeld, getHeldWorldPos, setGrabHooks, setMirror, meltAll };
}
