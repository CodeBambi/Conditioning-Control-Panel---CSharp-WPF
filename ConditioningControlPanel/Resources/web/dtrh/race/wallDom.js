/* ============================================================================
 * race/wallDom.js - the ONLINE FEED on the tube wall, as DOM.
 *
 * createWallDomPosters({ root, layout, camera, media, rng })
 *   -> { update(depth), setRoom(id), setHidden(bool), ready(), dispose() }
 *
 * No dt: the layer has no animation of its own, every poster is a pure function of the
 * camera and the road, so one depth per frame is the whole input.
 *
 * WHY THIS IS NOT A TEXTURE, AND NEVER CAN BE
 *
 * race/walls.js plasters the upper wall out of engine/wallPosters.js, which
 * decodes each picture into a three.js texture. That road is closed to the feed:
 * the feed's media all comes off images.scrolller.com, which sends NO
 * access-control-allow-origin header at all (its preflight 403s), so
 *
 *   - `fetch(url)` is refused outright. wallPosters.js's loader is a fetch, so a
 *     remote entry can never enter its pool in the first place.
 *   - an `<img crossOrigin="anonymous">` does not merely taint, it FAILS TO LOAD.
 *   - a plain `<img>` loads and taints the canvas, and uploading a tainted image
 *     to WebGL throws SecurityError.
 *
 * Proxying the bytes through a CC Labs server is not on the table either: the
 * browser talks to the source or it does not talk. So a player with no library of
 * their own used to get painted word placards on the wall while the feed showed
 * only on the flash cards. The pixels have to reach the wall some other way, and
 * the only other way is to draw them as ELEMENTS and transform those elements
 * into the same 3D space the wall quads live in. That is this file. If you are
 * here to "simplify" it into a texture: read the three bullets again.
 *
 * THE TRANSFORM. No CSS3DRenderer is vendored (adding one means a new file under
 * vendor/three/addons), so the matrix is written out here, and it is short: the
 * container carries a CSS `perspective` solved from the camera's live fov and the
 * viewport height; the inner "camera" element carries translateZ(perspective) plus
 * camera.matrixWorldInverse with its y ROW negated; each poster carries
 * translate(-50%,-50%) plus its own world matrix with its y COLUMN negated. Both
 * negations are the same fact twice: CSS's y axis points down.
 *
 * PLACED, NOT FETCHED, AND WARM BEFORE THE FLAG. A slot is only ever pointed at a
 * picture that has already finished loading, so the loading cannot start when the
 * world does or the run opens on a bare wall while the CDN answers. race/wallWarm.js
 * starts it as soon as a manifest carrying feed rows lands - before the menu, let
 * alone the countdown - and this layer opens on that set. It tops itself up from the
 * pool afterwards, once a second while it is short, which is also how a feed switched
 * on late (or a manifest that landed after the world was built) still reaches the wall
 * instead of leaving the layer dead for the whole run. A slot that falls behind the
 * camera is re-pointed at another member of the set it already holds; nothing is
 * fetched per slot mid-run, so the wall never stalls waiting on the CDN.
 *
 * OCCLUSION. A DOM layer sits over the WebGL canvas and cannot be hidden by
 * geometry, so the discipline is all here: a forward-depth window, a facing test
 * against the wall normal so nothing shows through a bend, a distance fade so a
 * poster arrives instead of popping, the same upper-wall band walls.js uses so
 * nothing lands over the road, and a hard hide under the menu, the Brake and
 * setHidden(true).
 * ==========================================================================*/

import * as THREE from 'three';
import { BAND_LO, BAND_HI, makeWallFrame } from './walls.js';
import { roomById } from './rooms.js';
import { warmShots, warmWallPosters } from './wallWarm.js';

/* The drawn set. Twelve, because the forward window below is ~56 m and a slot lands
 * every ~6.5 m, so twelve covers the whole visible band with a little slack: one
 * picture per slot at the start, and enough spare that a recycled slot can re-point
 * at something it was not already showing. Twelve decodes of feed-sized jpegs is a
 * one-time cost the HTTP cache absorbs, and it stays wide enough that hostMedia's
 * four-deep echo guard keeps neighbours different. */
const PRE_DRAW = 12;
const REFILL_MS = 900;       // how often a layer that is short of pictures asks the pool again
const SLOT_GAP = 6.5;        // metres of road between slots
const FAR = 58, NEAR = 2.2;  // the forward window: nothing outside it is on the wall
const FADE_FAR = 16, FADE_NEAR = 3.5;   // metres of fade at each end of the window
const BEHIND = 9;            // metres behind the lens before a slot recycles ahead
const FACE_MIN = 0.12;       // cos of the grazing angle at which a wall has turned away
const PX_W = 300;            // the element's own pixel box; the world size is the scale
const POSTER_W = 3.4;        // metres wide, before the per-slot jitter
const ALPHA = 0.94;          // never quite solid: the wall paint reads through
const NDC_PAD = 1.5;         // off-screen by more than half a screen, stop transforming it

/** loud rooms plaster, soft rooms scatter, and the TEA GARDEN carries pictures too.
 *  rooms.js rollRoomOrder puts the Tea Garden first in every run and it runs 222..560 m
 *  (10..25 s at KART_BASE_SPEED), so a bare opening room is a bare opening minute - which
 *  is exactly what a player saw. It opens generously and the rest of the run reads off it.
 *  race/walls.js's own per-room density carries the same fix for the player's own media. */
const density = (id) => { const r = roomById(id); return !r ? 0 : id === 'teagarden' ? 0.8 : r.loud ? 1 : 0.6; };
const clamp01 = (v) => (v < 0 ? 0 : v > 1 ? 1 : v);
const e6 = (v) => (Math.abs(v) < 1e-6 ? 0 : Math.round(v * 1e5) / 1e5);
/** camera.matrixWorldInverse as CSS: the y ROW flips, because CSS's y points down. */
const camCss = (e) => 'matrix3d(' + [e[0], -e[1], e[2], e[3], e[4], -e[5], e[6], e[7], e[8], -e[9], e[10], e[11], e[12], -e[13], e[14], e[15]].map(e6) + ')';
/** a world matrix as CSS: the y COLUMN flips, the same fact from the object's side. */
const objCss = (e) => 'matrix3d(' + [e[0], e[1], e[2], e[3], -e[4], -e[5], -e[6], -e[7], e[8], e[9], e[10], e[11], e[12], e[13], e[14], e[15]].map(e6) + ')';

const INERT = { update() {}, setRoom() {}, setHidden() {}, ready: () => false, dispose() {} };

export function createWallDomPosters({ root, layout, camera, media, rng }) {
  if (!root || !layout || !camera || !media || typeof media.drawRemoteDom !== 'function') return INERT;
  if (typeof document === 'undefined' || typeof Image === 'undefined') return INERT;

  // ---- the picture set -------------------------------------------------------------
  // Whatever race/wallWarm.js already decoded IS the opening set: those pictures are in hand
  // before the world is built, so the first slots can be aimed on the run's very first frame.
  let disposed = false;
  const shots = warmShots().slice();   // { url, aspect } - ONLY pictures that have finished loading
  const loading = new Set();           // the loader <img>s, so dispose can let go of them
  let pending = 0;                     // draws in flight, so a refill does not re-ask for them
  let lastRefill = 0;

  function pull(p) {
    pending++;
    Promise.resolve().then(() => p.acquire()).then((h) => {
      if (disposed || !h || !h.url) { pending--; return; }
      // no crossOrigin: the CDN sends no ACAO, and an anonymous request would not load at all
      const img = new Image();
      img.decoding = 'async';
      loading.add(img);
      img.onload = () => {
        pending--;
        loading.delete(img);
        // the element is kept on the shot: its decode stays resident, so a slot pointed at this
        // url paints in the same frame instead of asking the cache for the picture again
        if (!disposed && img.naturalWidth) shots.push({ url: h.url, aspect: img.naturalHeight / img.naturalWidth, img });
      };
      img.onerror = () => { pending--; loading.delete(img); };
      img.src = h.url;
    }).catch(() => { pending--; /* the pool handed back nothing: the slot simply stays empty */ });
  }

  /** Top the set up from the pool. Called at creation and then, at most once a REFILL_MS,
   *  while the layer is short: a manifest that lands after the world was built (the feed's
   *  own build takes seconds on a phone) is the ordinary case, not the exception, and a
   *  layer that only ever asked once would stay dead for the whole of that run. */
  function refill() {
    if (disposed || shots.length + pending >= PRE_DRAW) return;
    for (let i = shots.length + pending; i < PRE_DRAW; i++) {
      const p = media.drawRemoteDom('image');
      if (!p) return;   // nothing in the pool yet: ask again on the next tick
      pull(p);
    }
  }
  warmWallPosters(media, PRE_DRAW);   // whatever the warm set is short of, start it now
  refill();

  // ---- the layer, built only when there is a picture for it -------------------------
  // NOT eager. A full-screen 3D container with a dozen posters in it standing over the
  // canvas is not free even with nothing in it and display:none on top: measured on
  // race/smoke/pace-check.mjs, building it in a run that has no feed at all pushed the
  // worst "row under the kart" from 0.10 s to 0.70 s and failed that check. A player with
  // no feed (no consent, the desktop build, a manifest that carries no remote rows) must
  // pay nothing for a layer that can never show anything, so the elements are made on the
  // first frame that actually has a picture to hang, and a feed that arrives late still
  // builds them then.
  let box = null, camEl = null;
  const slots = [];
  function build() {
    box = document.createElement('div');
    box.className = 'rh-wall3d';
    camEl = document.createElement('div');
    camEl.className = 'rh-wall3d-cam';
    box.appendChild(camEl);
    root.appendChild(box);
    for (let i = 0; i < PRE_DRAW; i++) {
      const el = document.createElement('img');
      el.className = 'rh-wall3d-shot';
      el.alt = '';
      el.decoding = 'async';
      camEl.appendChild(el);
      slots.push({ el, shot: null, depth: 0, angle: 0, roll: 0, w: POSTER_W, h: POSTER_W, on: false, shown: false });
    }
  }

  const wallFrame = makeWallFrame(layout);
  const total = layout.totalDepth;
  const _m = new THREE.Matrix4(), _inv = new THREE.Matrix4();
  const _pos = new THREE.Vector3(), _v = new THREE.Vector3(), _fwd = new THREE.Vector3(), _ndc = new THREE.Vector3();
  const _bx = new THREE.Vector3(), _by = new THREE.Vector3(), _bz = new THREE.Vector3();
  let odo = 0, lastW = null, head = 0, seeded = false;
  let room = null, live = 0, hidden = false, wasHidden = null, side = rng() < 0.5 ? 1 : -1;

  /** A picture out of the pre-drawn set that this slot is not already showing. */
  function pickShot(cur) {
    if (!shots.length) return null;
    if (shots.length === 1) return shots[0];
    for (let t = 0; t < 6; t++) { const s = shots[(rng() * shots.length) | 0]; if (s && s !== cur) return s; }
    return shots[0];
  }

  /** Aim a slot at a depth. False while nothing has finished loading, and a slot that
   *  cannot be aimed stays off: a poster is never placed before its picture is in. */
  function aim(slot, depth) {
    const s = pickShot(slot.shot);
    if (!s) return false;
    slot.shot = s; slot.depth = depth; slot.on = true;
    side = -side;
    slot.angle = side * (BAND_LO + rng() * (BAND_HI - BAND_LO));
    slot.roll = (rng() - 0.5) * 0.3;
    slot.w = POSTER_W * (0.85 + rng() * 0.4);
    slot.h = slot.w * Math.max(0.6, Math.min(1.5, s.aspect));
    if (slot.el.getAttribute('src') !== s.url) slot.el.src = s.url;
    slot.el.style.width = PX_W + 'px';
    slot.el.style.height = Math.round(PX_W * slot.h / slot.w) + 'px';
    return true;
  }

  const off = (slot) => { if (slot.shown) { slot.shown = false; slot.el.style.opacity = '0'; slot.el.style.visibility = 'hidden'; } };

  function setRoom(id) {
    if (id === room) return;
    room = id;
    live = Math.round(PRE_DRAW * density(id));
  }
  /** Hides AT ONCE, not on the next update: the run's pause and the menu both stop calling
   *  update(), so a lever that only took effect there would leave the layer standing. */
  function setHidden(v) {
    hidden = !!v;
    if (hidden && wasHidden !== true) { wasHidden = true; if (box) box.style.display = 'none'; }
  }
  const ready = () => shots.length > 0;

  function update(depth) {
    if (disposed) return;
    // unwrapped odometer, exactly walls.js's: the race laps a closed loop but a slot's
    // depth has to keep counting up, and frameAtDepth wraps internally anyway
    const w = layout.wrap(depth);
    // the odometer STARTS at the first depth it is given, not at zero: the run's own first
    // frame is at depth 0, but nothing may assume that, and a layer seeded at 0 while the
    // camera stands at 288 hangs its whole band a lap away where no one can see it
    if (lastW == null) { lastW = w; odo = w; }
    let delta = w - lastW;
    if (delta > total / 2) delta -= total; else if (delta < -total / 2) delta += total;
    odo += delta; lastW = w;
    setRoom(layout.roomAtDepth(w));

    // ahead of the hidden check: a layer standing in a room that wants nothing still gathers
    // the pictures the room after it will want
    if (shots.length < PRE_DRAW) {
      const now = Date.now();
      if (now - lastRefill > REFILL_MS) { lastRefill = now; refill(); }
    }

    // Nothing decoded yet: no elements are made and none of the camera work below runs.
    // This is the whole of the no-feed cost, and it is a room lookup and a compare.
    if (hidden || !live || !shots.length) {
      if (box && wasHidden !== true) { wasHidden = true; box.style.display = 'none'; }
      return;
    }
    if (!box) build();
    if (wasHidden !== false) { wasHidden = false; box.style.display = ''; }

    if (!seeded) { seeded = true; head = odo + 6; }
    const vw = window.innerWidth || root.clientWidth || 1, vh = window.innerHeight || root.clientHeight || 1;
    const persp = 0.5 * vh / Math.tan(camera.fov * Math.PI / 360);

    camera.updateMatrixWorld();
    _inv.copy(camera.matrixWorld).invert();
    camera.matrixWorldInverse.copy(_inv);          // Vector3.project reads this
    box.style.perspective = e6(persp) + 'px';
    camEl.style.transform = 'translateZ(' + e6(persp) + 'px)' + camCss(_inv.elements) + 'translate(' + vw / 2 + 'px,' + vh / 2 + 'px)';
    camera.matrixWorld.extractBasis(_bx, _by, _bz);
    _fwd.copy(_bz).negate();                        // the lens looks down its own -Z

    for (let i = 0; i < slots.length; i++) {
      const slot = slots[i];
      if (i >= live) { off(slot); continue; }
      // recycle first, so a slot that fell behind is re-aimed ahead in the same frame
      if (slot.on && slot.depth < odo - BEHIND) slot.on = false;
      if (!slot.on) {
        // the head only advances on a SUCCESSFUL aim: a slot that could not be filled
        // (nothing loaded yet) must not push the next one out past the far window
        const at = Math.max(head, odo + 6) + SLOT_GAP * (0.8 + rng() * 0.4);
        if (!aim(slot, at)) { off(slot); continue; }
        head = at;
      }
      const f = wallFrame(slot.depth, slot.angle, slot.roll);
      _pos.copy(f.c);
      _v.subVectors(_pos, camera.position);
      const fwd = _v.dot(_fwd);
      if (fwd <= NEAR || fwd >= FAR) { off(slot); continue; }
      const dist = _v.length() || 1;
      const face = -_v.dot(f.in) / dist;            // f.in is the poster's own front normal
      if (face < FACE_MIN) { off(slot); continue; }
      _ndc.copy(_pos).project(camera);
      if (Math.abs(_ndc.x) > NDC_PAD || Math.abs(_ndc.y) > NDC_PAD) { off(slot); continue; }
      const o = ALPHA * clamp01((FAR - fwd) / FADE_FAR) * clamp01((fwd - NEAR) / FADE_NEAR) * clamp01((face - FACE_MIN) / 0.25);
      if (o <= 0.01) { off(slot); continue; }
      const s = slot.w / PX_W;
      _m.makeBasis(_bx.copy(f.rt).multiplyScalar(s), _by.copy(f.up).multiplyScalar(s), _bz.copy(f.in).multiplyScalar(s));
      _m.setPosition(_pos);
      slot.el.style.transform = 'translate(-50%,-50%)' + objCss(_m.elements);
      slot.el.style.opacity = e6(o);
      if (!slot.shown) { slot.shown = true; slot.el.style.visibility = 'visible'; }
    }
  }

  function dispose() {
    disposed = true;
    for (const img of loading) { img.onload = null; img.onerror = null; img.src = ''; }
    loading.clear();
    for (const slot of slots) { slot.el.src = ''; slot.shot = null; }
    slots.length = 0;
    live = 0;
    // the warm set's own elements are NOT released here: race/wallWarm.js owns those and the
    // next run opens on them. Only this layer's own loaders are let go.
    shots.length = 0;
    if (box && box.parentNode) box.parentNode.removeChild(box);
    box = null; camEl = null;
  }

  return { update, setRoom, setHidden, ready, dispose };
}
