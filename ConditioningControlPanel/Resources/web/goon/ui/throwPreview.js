/* ============================================================================
 * ui/throwPreview.js — what a thrown payload LOOKS like while it is in the air.
 *
 * A payload in flight (ui/opponent.js's inbound throw, ui/arsenal.js's drag
 * ghost) used to be the item STICKER: a cutout of a VHS tape for a video, a
 * spiral disc for a spiral. This module upgrades that to a LIVE PREVIEW of the
 * content that is actually about to play — a muted, looping thumbnail of the
 * clip, or the image the burst will throw — for the kinds that are backed by
 * media at all. Everything else keeps its sticker, which is why the sticker map
 * lives here too: it is the FLOOR, never a failure state.
 *
 * ---------------------------------------------------------------------------
 * WHICH CLIP, AND HOW SURE WE ARE (read this before "fixing" a wrong preview)
 *
 * exec/media.js `drawFor(kind, payload)` is the receiver-side resolution order:
 *   1. `payload.tags` entries of the form `xfer:<sha>` — an artifact the SENDER
 *      transferred over the P2P media lane (net/mediaQueue.js attaches these on
 *      the way out, for GoonPayloadKind.Video and FlashBurst only). A tag hit is
 *      DETERMINISTIC: that sha is that file, on both machines.
 *   2. otherwise a random draw from the RECEIVER's own deck, at render time.
 *
 * So a preview is EXACT exactly when a tag resolves, and that is the only case
 * this module claims (`exact: true`). The fallback peeks the receiver's own deck
 * — representative, honest, and deliberately NON-CONSUMING: `media.peekKind()`
 * reads the deck without popping it, because a preview that spent a draw would
 * change which clip the effect then plays. That is a bug the preview would have
 * caused, and the whole reason peekKind exists.
 *
 * OUTBOUND (the arsenal drag ghost) is the weaker half and says so: tags are
 * attached by net/mediaQueue.js's tryFirePayload wrapper at FIRE time, from the
 * artifacts it has already landed on the peer — so at GRAB time nothing is
 * decided yet. We preview our own pool, which is the same pool that queue draws
 * from, and treat it as stylized.
 * ---------------------------------------------------------------------------
 *
 * NEVER BLOCKS THE THROW. The node is returned immediately with the sticker
 * painted behind it (a CSS background on the caller's art node) and the live
 * element at opacity 0; `is-ready` lands on the first decoded frame. A clip that
 * is slow, broken or missing therefore degrades to the sticker with no timer,
 * no branch and no delay to the animation.
 *
 * REFCOUNTS ARE REAL. A peer artifact resolves through the received store's
 * refcounted view (exec/media.js acquirePeer): the standalone backend revokes
 * its object URL at refcount zero. Every handle this module takes MUST be given
 * back, so `createPreview()` hands out one `destroy()` and `dressGhost()` wires
 * it into the node's own remove(). The video teardown is the one exec/videos.js
 * uses verbatim — pause, removeAttribute('src'), load() — because a <video> left
 * holding a src keeps the decoder alive after the node is gone.
 *
 * Node-import-safe: no DOM and no pool at import.
 * ==========================================================================*/

import { GoonPayloadKind } from '../core/contracts.js';

/** The wire prefix exec/media.js keys peer artifacts by. Frozen with the spec. */
export const XFER_TAG_PREFIX = 'xfer:';

/**
 * Payload kind -> the media kind exec/ will ask the pool for while rendering it.
 * ABSENCE IS A DECISION, not an omission:
 *   Spiral        — drawn procedurally on a canvas (exec/spiral.js draws no media)
 *   LockCard      — a typed card, no media
 *   Subliminals   — words (exec/subliminals.js)
 *   ToyPattern    — no pixels at all
 * Those four fly as their sticker, which is the truthful picture of them.
 */
export const PREVIEW_MEDIA_KIND = Object.freeze({
  [GoonPayloadKind.Video]: 'video',          // exec/videos.js    drawFor('video', payload)
  [GoonPayloadKind.FlashBurst]: 'image',     // exec/flashes.js   drawFor('image', payload)
  [GoonPayloadKind.BrainDrain]: 'image',     // exec/brainDrain.js drawKind('image')
  [GoonPayloadKind.BubbleSwarm]: 'image',    // exec/bubbles.js   drawKind('image')
});

/**
 * Payload kind -> its sticker cutout in assets/items/. Deliberately a COPY of
 * the `img` column of ui/arsenal.js ARSENAL_ITEMS rather than an import: this
 * module is reached from the arsenal itself, and a cycle between the two would
 * be a boot-order bug waiting for its first circular-import day. The self-test
 * pins that every entry here is a file that exists.
 */
export const STICKER_FOR_PAYLOAD = Object.freeze({
  [GoonPayloadKind.FlashBurst]: 'item_flash',
  [GoonPayloadKind.SubliminalStorm]: 'item_subliminal',
  [GoonPayloadKind.BubbleSwarm]: 'item_bubbles',
  [GoonPayloadKind.Video]: 'item_video',
  [GoonPayloadKind.LockCard]: 'item_lockcard',
  [GoonPayloadKind.ToyPattern]: 'item_toy',
  [GoonPayloadKind.BrainDrain]: 'item_braindrain',
  [GoonPayloadKind.Spiral]: 'item_spiral',
});

/** Where the cutouts live, relative to index.html (same base as ui/arsenal.js). */
export const ITEM_ART_BASE = './assets/items/';

/** A ghost nobody ever removed must still give its refcount back. */
const GHOST_MAX_MS = 120000;

// ------------------------------------------------------------------ the pool

/**
 * The page's exec/media.js pool, handed over by ui/hud.js at mount. Module-level
 * on purpose: the arsenal's drag ghost is minted deep inside a pointer closure
 * that takes no media argument, and threading one through would be a real edit
 * to a file this feature has no other business in.
 */
let pool = null;

/** @param {object|null} p the exec/media.js pool (or null to forget it) */
export function setPreviewMedia(p) {
  pool = (p && (typeof p.acquireByTag === 'function' || typeof p.peekKind === 'function')) ? p : null;
  return !!pool;
}

/** Read-only, for a driver or a test. */
export function previewMedia() { return pool; }

// ---------------------------------------------------------------- resolution

const doc = () => (typeof document !== 'undefined' ? document : null);

function releaseHandle(h) {
  try { if (h && typeof h.release === 'function') h.release(); } catch (_e) { /* already gone */ }
}

/** An empty mime is trusted (the store may not carry one); a wrong one is not. */
function mimeAgrees(mime, mediaKind) {
  const m = String(mime || '');
  if (!m) return true;
  return m.indexOf(mediaKind === 'video' ? 'video/' : 'image/') === 0;
}

/** The EXACT half: an `xfer:<sha>` the sender transferred and we already hold. */
function fromTags(mediaKind, payload) {
  const tags = (payload && Array.isArray(payload.tags)) ? payload.tags : null;
  if (!tags || !pool || typeof pool.acquireByTag !== 'function') return null;
  for (const t of tags) {
    if (typeof t !== 'string' || t.indexOf(XFER_TAG_PREFIX) !== 0) continue;
    let h = null;
    try { h = pool.acquireByTag(t); } catch (_e) { h = null; }
    if (!h || !h.url) continue;
    if (!mimeAgrees(h.mime, mediaKind)) { releaseHandle(h); continue; }
    return { url: String(h.url), release: () => releaseHandle(h), provenance: 'peer', exact: true };
  }
  return null;
}

/** The representative half: our own deck, PEEKED — see the header. */
function fromOwnPool(mediaKind) {
  if (!pool || typeof pool.peekKind !== 'function') return null;
  let entry = null;
  try { entry = pool.peekKind(mediaKind); } catch (_e) { entry = null; }
  if (!entry) return null;
  let h = null;
  if (typeof entry.acquire === 'function') { try { h = entry.acquire(); } catch (_e) { h = null; } }
  const url = (h && h.url) || entry.url;
  if (!url) { releaseHandle(h); return null; }
  return {
    url: String(url),
    release: () => releaseHandle(h),
    provenance: (h && h.provenance) || entry.provenance || 'local',
    exact: false,
  };
}

/**
 * The clip/image a payload of this kind is about to put on screen.
 * @param {number} kind      GoonPayloadKind
 * @param {object} [payload] the wire payload, when we have one (inbound only)
 * @returns {{mediaKind:string, url:string, release:Function, provenance:string, exact:boolean}|null}
 */
export function resolvePreview(kind, payload) {
  const mediaKind = PREVIEW_MEDIA_KIND[kind];
  if (!mediaKind) return null;
  const hit = fromTags(mediaKind, payload) || fromOwnPool(mediaKind);
  return hit ? Object.assign({ mediaKind }, hit) : null;
}

/** The sticker for a kind, or null. The floor under every preview. */
export function stickerUrl(kind) {
  const art = STICKER_FOR_PAYLOAD[kind];
  return art ? ITEM_ART_BASE + art + '.png' : null;
}

// ------------------------------------------------------------- the element

function markReady(node) {
  try { if (node && node.classList) node.classList.add('is-ready'); } catch (_e) { /* stub DOM */ }
}

/**
 * A live preview ELEMENT for a payload, or null when there is nothing to show
 * (unbacked kind, no pool, empty library). The caller owns placement, sizing and
 * the sticker underneath it; all this hands back is a node and the one call that
 * gives its refcount and its decoder back.
 *
 * @param {object}  o
 * @param {number}  o.kind        GoonPayloadKind
 * @param {object}  [o.payload]   the wire payload (inbound); omit for outbound
 * @param {string}  [o.cls]       class for the element
 * @returns {{node:Element, destroy:Function, exact:boolean, provenance:string, mediaKind:string}|null}
 */
export function createPreview({ kind, payload = null, cls = 'gg-throw-live' } = {}) {
  const d = doc();
  if (!d || typeof d.createElement !== 'function') return null;
  const res = resolvePreview(kind, payload);
  if (!res) return null;

  const isVideo = res.mediaKind === 'video';
  let node = null;
  try { node = d.createElement(isVideo ? 'video' : 'img'); } catch (_e) { node = null; }
  if (!node) { res.release(); return null; }
  node.className = cls;

  if (isVideo) {
    // Muted + inline + loop: a thumbnail, never a second soundtrack. The
    // attributes go on as well as the properties because autoplay policy reads
    // the ATTRIBUTE on a node that has not been inserted yet.
    try {
      node.muted = true;
      node.autoplay = true;
      node.loop = true;
      node.playsInline = true;
      node.preload = 'auto';
      node.setAttribute('muted', '');
      node.setAttribute('playsinline', '');
      node.setAttribute('loop', '');
    } catch (_e) { /* stub DOM */ }
  } else {
    try { node.alt = ''; node.decoding = 'async'; } catch (_e) { /* stub DOM */ }
  }

  const on = (type, fn) => {
    try { if (typeof node.addEventListener === 'function') node.addEventListener(type, fn); } catch (_e) { /* stub */ }
  };
  // FIRST DECODED FRAME, not "src assigned" — a <video> with a src and no frame
  // is a black rectangle, which is worse than the sticker it would be covering.
  if (isVideo) { on('loadeddata', () => markReady(node)); on('playing', () => markReady(node)); }
  else { on('load', () => markReady(node)); }

  try { node.src = res.url; } catch (_e) { /* stub DOM */ }
  if (isVideo && typeof node.play === 'function') {
    try { const p = node.play(); if (p && typeof p.catch === 'function') p.catch(() => {}); } catch (_e) { /* blocked: sticker stays */ }
  }

  let dead = false;
  function destroy() {
    if (dead) return;
    dead = true;
    if (isVideo) {
      // exec/videos.js's teardown, verbatim: a <video> that keeps its src keeps
      // its decoder, and the node being gone does not release either.
      try { if (typeof node.pause === 'function') node.pause(); } catch (_e) { /* ignore */ }
      try { if (typeof node.removeAttribute === 'function') node.removeAttribute('src'); } catch (_e) { /* ignore */ }
      try { if (typeof node.load === 'function') node.load(); } catch (_e) { /* ignore */ }
    }
    res.release();
  }

  return { node, destroy, exact: res.exact, provenance: res.provenance, mediaKind: res.mediaKind };
}

// -------------------------------------------------------- the arsenal hook

/**
 * THE OUTBOUND SEAM — ui/arsenal.js hands its freshly minted drag ghost here and
 * uses whatever comes back:
 *
 *     ghost = dressGhost(ghost, rec.item.kind) || ghost;
 *
 * …one line, before the ghost is added to the document. A null answer means
 * "keep the sticker you made", so the arsenal needs no branch and no knowledge
 * of media at all. The replacement inherits the ghost's class and inline size,
 * so moveGhost()/killGhost() keep working on it unchanged — and its remove() is
 * wrapped so killGhost() also gives the refcount and the decoder back, which is
 * the one thing the arsenal cannot be expected to know about.
 *
 * @param {Element} node the arsenal's <img class="gg-item-ghost">
 * @param {number}  kind GoonPayloadKind of the item being dragged
 * @returns {Element|null} the node to drag instead, or null to keep the sticker
 */
export function dressGhost(node, kind) {
  if (!node || !node.style) return null;
  // Outbound: no payload exists yet (the engine has not been asked), so this is
  // the representative half by construction — see the header.
  const made = createPreview({ kind, payload: null, cls: 'gg-item-ghost gg-item-ghost--live' });
  if (!made) return null;

  const live = made.node;
  try {
    if (live.style) {
      live.style.width = node.style.width || '';
      live.style.height = node.style.height || '';
      live.style.left = node.style.left || '';
      live.style.top = node.style.top || '';
    }
    // The sticker stays painted behind the clip until its first frame lands.
    const sticker = stickerUrl(kind);
    if (sticker && live.style && typeof live.style.setProperty === 'function') {
      live.style.setProperty('--gg-ghost-sticker', 'url(' + sticker + ')');
    }
  } catch (_e) { /* stub DOM */ }

  let timer = 0;
  const finish = () => {
    if (timer) { try { clearTimeout(timer); } catch (_e) { /* gone */ } timer = 0; }
    made.destroy();
  };
  try {
    const origRemove = typeof live.remove === 'function' ? live.remove.bind(live) : null;
    live.remove = function removeAndRelease() {
      finish();
      if (origRemove) origRemove();
    };
  } catch (_e) { /* frozen node: the timer below is the net */ }
  if (typeof setTimeout === 'function') {
    timer = setTimeout(finish, GHOST_MAX_MS);
    if (timer && typeof timer.unref === 'function') timer.unref();
  }
  return live;
}

export default { createPreview, resolvePreview, dressGhost, setPreviewMedia, stickerUrl };
