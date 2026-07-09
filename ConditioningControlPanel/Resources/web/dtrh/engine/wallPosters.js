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
 * The textures are a SMALL SHARED POOL of the user's images (gif first-frames
 * included), decoded once and handed round-robin to the slots. On a wall that
 * scrolls past at fall speed the repetition is invisible, and it turns "plaster
 * dozens of quads" into "sample from ~24 textures" - near-zero ongoing cost. No
 * user media -> the layer simply stays empty (like the card spawner).
 * ==========================================================================*/

import * as THREE from 'three';
import { Q } from '../shared/quality.js';
import { RADIUS } from './tunnel.js';

// How many DISTINCT textures the shared pool holds. Slots sample from these, so
// this bounds all decode + GPU memory the wall ever costs. Mobile stays lean.
const POOL_SIZE = Q.tier === 'mobile' ? 12 : 24;
const POOL_INFLIGHT = 3;                 // concurrent decodes while filling the pool
const TEX_MAX_DIM = Q.tier === 'mobile' ? 256 : 384; // posters are small on screen

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
  // A hidden host keeps animated <img> elements in the document so the browser
  // actually advances their frames (a detached / display:none img would freeze
  // on the first frame). Off-screen + opacity 0, so it never paints.
  let _animHost = null;
  function animHost() {
    if (!_animHost) {
      _animHost = document.createElement('div');
      _animHost.setAttribute('aria-hidden', 'true');
      _animHost.style.cssText = 'position:fixed;left:0;top:0;width:1px;height:1px;overflow:hidden;opacity:0;pointer-events:none;z-index:-1;';
      document.body.appendChild(_animHost);
    }
    return _animHost;
  }

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

  // Decode one user image into a downscaled texture. An animated GIF keeps a live
  // <img> the browser advances; the per-frame pool tick (tickPool) re-samples it.
  // A still is a single first-frame snapshot (near-zero ongoing cost, as before).
  async function decodeOne() {
    const entry = media.drawKind ? (media.drawKind('image') || media.draw()) : media.draw();
    if (!entry || entry.kind !== 'image') return null; // stills only (videos stay on cards)
    const acquired = await entry.acquire();
    if (!acquired) return null;
    const name = entry.name || null;
    let objUrl = null;
    try {
      const blob = await (await fetch(acquired.url)).blob();
      const animated = blob.type === 'image/gif'
        || /\.gif(\?|$)/i.test(name || '') || /\.gif(\?|$)/i.test(acquired.url || '');
      if (animated) {
        // keep a live <img> in the doc so the browser animates it; we sample it
        // into `c` each pool tick. Own the blob via an object URL (independent of
        // the acquired handle, which we release below like the still path).
        objUrl = URL.createObjectURL(blob);
        const img = document.createElement('img');
        img.decoding = 'async';
        img.src = objUrl;
        try { await img.decode(); } catch (e) { /* first frame may still paint */ }
        const w = img.naturalWidth || 2, h = img.naturalHeight || 2;
        const { c, x } = mkCanvas(w, h);
        x.drawImage(img, 0, 0, c.width, c.height);
        animHost().appendChild(img);
        return { tex: mkTex(c), aspect: w / h, assetName: name, animated: true, img, canvas: c, ctx: x, objUrl };
      }
      let bmp = null;
      try { bmp = await createImageBitmap(blob); }
      catch (e) { return null; } // hosted WebView2 has createImageBitmap; bail otherwise
      const w = bmp.width, h = bmp.height;
      const { c, x } = mkCanvas(w, h);
      x.drawImage(bmp, 0, 0, c.width, c.height);
      bmp.close();
      return { tex: mkTex(c), aspect: w / h, assetName: name };
    } catch (e) { if (objUrl) { try { URL.revokeObjectURL(objUrl); } catch (e2) { /* ignore */ } } return null; }
    finally { if (acquired.release) acquired.release(); }
  }

  // Advance animated posters: re-sample each live gif <img> into its pool canvas
  // and flag the shared texture. Pool-level (not per-slot), so many slots sharing
  // a texture animate in sync for one redraw. Throttled to ~20fps - plenty for
  // gifs and ~3x cheaper than every frame.
  let poolAnimT = 0;
  function tickPool(dt) {
    poolAnimT += dt;
    if (poolAnimT < 0.05) return;
    poolAnimT = 0;
    for (const item of pool) {
      if (!item.animated || !item.img) continue;
      try { item.ctx.drawImage(item.img, 0, 0, item.canvas.width, item.canvas.height); item.tex.needsUpdate = true; }
      catch (e) { /* ignore */ }
    }
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
    slot.mat.needsUpdate = true;
    slot.mesh.visible = true;
    return true;
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

  // Rebase (junction dive): the loop was rebuilt onto the chosen branch, so every
  // placed slot sits on the OLD spine. Retire them all; update() re-places fresh
  // ones ahead on the new spine via layout.frameAtDepth as the fall continues.
  function reset() {
    for (const s of slots) { s.active = false; s.mesh.visible = false; }
    liveCount = 0;
    addCooldown = 0;
    _heldSlot = null;
  }

  // ---- poster hold (click a wall gif to face the camera at it) ----------------
  // The live poster meshes the scene raycast can hit.
  function getPickables() {
    const out = [];
    for (const s of slots) if (s.active && s.mesh.visible) out.push(s.mesh);
    return out;
  }
  // Seize the poster behind `mesh`; returns { assetName } (or null if it's gone).
  function grabPoster(mesh) {
    const slot = mesh && mesh.userData && mesh.userData.slot;
    if (!slot || !slot.active) return null;
    _heldSlot = slot;
    _heldGap = null; // captured on the first advanceHeld() from the live cam depth
    return { assetName: slot.assetName || null };
  }
  function releasePoster() { _heldSlot = null; _heldGap = null; }
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
    // ease the live count toward the region ceiling: add posters gradually (in
    // the fog ahead) and retire extras as they pass behind, so a chamber change
    // breathes in/out instead of snapping.
    addCooldown -= dt;
    if (liveCount < targetCount && pool.length && addCooldown <= 0) {
      const slot = slots.find((s) => !s.active) || makeSlot();
      slot.active = true;
      if (place(slot, camDepth + rand(AHEAD_MIN, AHEAD_MAX))) {
        liveCount += 1;
        addCooldown = 0.06; // ~16 posters/sec fill-in - brisk but not a pop-in wall
      } else { slot.active = false; }
    }

    // recycle / retire
    for (const slot of slots) {
      if (!slot.active) continue;
      if (slot === _heldSlot) continue; // frozen while the player holds it
      if (slot.depth < camDepth - BEHIND) {
        if (liveCount > targetCount) {          // over ceiling: retire this one
          slot.active = false;
          slot.mesh.visible = false;
          slot.mat.map = null;
          liveCount -= 1;
        } else {                                 // re-throw ahead into the fog
          place(slot, camDepth + rand(AHEAD_MAX * 0.6, AHEAD_MAX));
        }
      }
    }
  }

  function dispose() {
    for (const slot of slots) { slot.mat.dispose(); }
    for (const item of pool) {
      try { item.tex.dispose(); } catch (e) { /* ignore */ }
      if (item.animated) {
        try { if (item.img && item.img.parentNode) item.img.parentNode.removeChild(item.img); } catch (e) { /* ignore */ }
        try { if (item.objUrl) URL.revokeObjectURL(item.objUrl); } catch (e) { /* ignore */ }
      }
    }
    pool.length = 0;
    if (_animHost && _animHost.parentNode) { try { _animHost.parentNode.removeChild(_animHost); } catch (e) { /* ignore */ } _animHost = null; }
    scene.remove(group);
    unit.dispose();
  }

  return { setRegion, setPaused, update, dispose, reset, getPickables, grabPoster, releasePoster, advanceHeld, getHeldWorldPos };
}
