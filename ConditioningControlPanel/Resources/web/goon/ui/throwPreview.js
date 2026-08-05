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
 * …and UNDER that floor, for every kind alike, is THE MARK (2026-08-04): a
 * glyph, a tint, and — for the two payloads that ARE text — their own words.
 * See THE MARK below for why a sticker on its own left six of the eight kinds
 * looking like nothing had been thrown at all.
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

/* ---------------------------------------------------------------------------
 * THE MARK — every kind reads as ITSELF in the air (2026-08-04).
 *
 * "when an effect arrives from the opponent, flashes and videos show the little
 *  incoming throw — the other kinds do not." (owner)
 *
 * The throw was never kind-gated: ui/hud.js hands EVERY accepted payload to
 * ui/opponent.js markInbound and one projectile is launched per kind. What was
 * missing is that only the four media-backed kinds in PREVIEW_MEDIA_KIND had
 * anything to SHOW. The other four flew as `--gg-throw-sticker` alone — a
 * ~650 KB item cutout whose fetch STARTS when the projectile is built, which
 * routinely loses the race against a 380-760 ms flight (the whole flight is
 * shorter than a cold PNG on a phone). A projectile whose only pixel source has
 * not decoded yet is an empty box, and an empty box is indistinguishable from
 * "the throw never fired" — which is exactly how it was reported.
 *
 * So every kind now carries three layers, in this order of precedence:
 *   1. its GLYPH   — text, painted on frame one, no network, cannot fail;
 *   2. its STICKER — revealed only once the PNG has actually decoded, and
 *      warmed at ACCEPT time (warmSticker) so the lead the engine already
 *      reserved pays for the fetch instead of the flight paying for it;
 *   3. its LIVE PREVIEW — media kinds only, exactly as before.
 * …plus a TINT, so the four kinds that share the sticker-only path still read
 * apart at a glance: it themes the sender's flare, the projectile's glow and
 * the landing splash.
 *
 * THE GLYPHS ARE A COPY, deliberately, of the vocabulary ui/hud.js's rail chips
 * and ui/announcer.js's ribbon already use — the same reason STICKER_FOR_PAYLOAD
 * is a copy of the arsenal's `img` column: this module is reached FROM the
 * arsenal and must not drag the HUD tier in behind it. The self-test pins the
 * copy against ui/announcer.js's ANNOUNCE_GLYPH so the two cannot drift.
 *
 * THE TINTS are goon.css's own hues (pink, violet, gold, green) plus two
 * near-palette neighbours for the kinds that would otherwise collide. They are
 * glow colours only: nothing here is ever the sole carrier of a meaning.
 * ------------------------------------------------------------------------- */
export const THROW_MARK = Object.freeze({
  [GoonPayloadKind.FlashBurst]: Object.freeze({ glyph: '✦', tint: '255, 105, 180' }),   // --gg-pink-rgb
  [GoonPayloadKind.SubliminalStorm]: Object.freeze({ glyph: '≋', tint: '236, 220, 255' }),
  [GoonPayloadKind.BubbleSwarm]: Object.freeze({ glyph: '○', tint: '150, 226, 255' }),
  [GoonPayloadKind.Video]: Object.freeze({ glyph: '▶', tint: '179, 136, 255' }),        // --gg-violet-rgb
  [GoonPayloadKind.LockCard]: Object.freeze({ glyph: '▢', tint: '255, 212, 94' }),      // --gg-gold
  [GoonPayloadKind.ToyPattern]: Object.freeze({ glyph: '∿', tint: '94, 242, 160' }),    // --gg-green-rgb
  [GoonPayloadKind.BrainDrain]: Object.freeze({ glyph: '◍', tint: '138, 92, 246' }),
  [GoonPayloadKind.Spiral]: Object.freeze({ glyph: '◎', tint: '255, 168, 214' }),
});

/**
 * A kind we have never heard of (a newer peer's ninth code) still throws, and
 * still throws something you can SEE. Unknown is never invisible.
 */
export const DEFAULT_THROW_MARK = Object.freeze({ glyph: '◆', tint: '255, 105, 180' });

/** @param {number} kind GoonPayloadKind @returns {{glyph:string, tint:string}} */
export function markFor(kind) {
  return THROW_MARK[kind] || DEFAULT_THROW_MARK;
}

/**
 * THE WORD — the two kinds whose payload IS text fly with it written under them.
 *
 * `payload.text` is the phrase a LockCard will make you type and the line a
 * SubliminalStorm will chant (exec/lockCards.js, exec/subliminals.js both read
 * it). Showing it in flight is the same promise the clip half makes — "this is
 * what is about to play" — kept for two kinds no clip can cover.
 *
 * REMOTE TEXT. The engine sanitized it once and the renderer sanitizes it again
 * on its way to the DOM; this is a third, narrower pass (control characters out,
 * whitespace collapsed, hard length cap) and the caller writes the result with
 * textContent, never markup — ui/opponent.js's TRUST rule, unchanged.
 */
export const WORD_KINDS = Object.freeze([GoonPayloadKind.SubliminalStorm, GoonPayloadKind.LockCard]);

/** Longer than this and it stops being a glance. */
export const THROW_WORD_MAX = 28;

/**
 * @param {number} kind      GoonPayloadKind
 * @param {object} [payload] the wire payload
 * @returns {string} the words to show, or '' for every kind that has none
 */
export function throwWord(kind, payload) {
  if (WORD_KINDS.indexOf(kind) < 0) return '';
  const raw = (payload && typeof payload.text === 'string') ? payload.text : '';
  if (!raw) return '';
  let clean = '';
  for (const ch of raw) {
    const c = ch.charCodeAt(0);
    clean += (c < 0x20 || (c >= 0x7f && c <= 0x9f)) ? ' ' : ch;
  }
  clean = clean.replace(/\s+/g, ' ').trim();
  if (!clean) return '';
  return clean.length > THROW_WORD_MAX ? (clean.slice(0, THROW_WORD_MAX - 1) + '…') : clean;
}

// ------------------------------------------------------- warming the sticker

/**
 * url -> {done, waiters[]}. Module-level for the same reason `pool` is: the
 * page has one HTTP cache and one set of nine cutouts, and a per-mount map
 * would re-fetch them every match.
 */
const warmed = new Map();

/** A stuck decode must not grow an unbounded callback list. */
const WARM_WAITERS_MAX = 32;

/**
 * Start the item cutout's fetch and tell me when it can actually be shown.
 *
 * Called TWICE per throw, on purpose: once by markInbound the instant the
 * engine admits the payload (which is ~1-1.5 s before the projectile exists —
 * the schedule lead pays for the fetch), and once by the launch itself to learn
 * whether the art is ready yet. The second call is free; the fetch is one.
 *
 * @param   {number}   kind      GoonPayloadKind
 * @param   {Function} [onReady] run once the art has decoded (immediately if it
 *                               already has). Never run if it errors — the
 *                               glyph simply stays, which is the point of it.
 * @returns {boolean} whether the art is decoded RIGHT NOW
 */
export function warmSticker(kind, onReady = null) {
  const url = stickerUrl(kind);
  if (!url) return false;
  let rec = warmed.get(url);
  if (!rec) {
    const Img = (typeof Image === 'function') ? Image : null;
    if (!Img) return false;                       // node / no DOM: nothing to warm
    let img = null;
    try { img = new Img(); } catch (_e) { img = null; }
    if (!img) return false;
    rec = { done: false, waiters: [] };
    warmed.set(url, rec);
    const settle = () => {
      if (rec.done) return;
      rec.done = true;
      const list = rec.waiters.splice(0, rec.waiters.length);
      for (const fn of list) { try { fn(); } catch (_e) { /* never break a throw */ } }
    };
    try { img.decoding = 'async'; } catch (_e) { /* stub */ }
    try { if (typeof img.addEventListener === 'function') img.addEventListener('load', settle); } catch (_e) { /* stub */ }
    try { img.src = url; } catch (_e) { /* stub */ }
    try { if (img.complete) settle(); } catch (_e) { /* stub */ }
  }
  if (rec.done) {
    if (typeof onReady === 'function') { try { onReady(); } catch (_e) { /* ignore */ } }
    return true;
  }
  if (typeof onReady === 'function' && rec.waiters.length < WARM_WAITERS_MAX) rec.waiters.push(onReady);
  return false;
}

/** Read-only, for a driver or a test. */
export function stickerWarm(kind) {
  const rec = warmed.get(stickerUrl(kind));
  return !!(rec && rec.done);
}

/** Test seam: forget every warmed cutout. The page never calls this. */
export function resetStickerWarm() { warmed.clear(); }

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

export default {
  createPreview, resolvePreview, dressGhost, setPreviewMedia, stickerUrl,
  markFor, throwWord, warmSticker, stickerWarm,
};
