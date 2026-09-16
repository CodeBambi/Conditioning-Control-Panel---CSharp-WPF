/* ============================================================================
 * shared/hypno/media.js - the player's dealt pictures, drawn in the page
 * (CONTRACT 10.13.C and D). Soft Hand deals 13 and dresses each card value in
 * one; Daily Daze deals eight for its wedges, Velvet Vortex four for
 * fx.gif_from.
 *
 * Nothing here chooses a file: the host deals, the page gets keys and urls, and
 * only keys ever go back (in fx symbols). Urls are drawn only when they point at
 * ccp.assets, ccp.game or this page's own origin.
 *
 * Decoding is the room's (room/gif-decode.js, WebCodecs ImageDecoder). Caps: at
 * most 13 sources, 192 px on the long edge, 12 fps each; three source loads may overlap; at most ONE animation decode starts per tick, and only
 * sources drawn since the last tick advance. No decoder, or a file it refuses,
 * draws a still taken from an <img>.
 * ==========================================================================*/

import { decodedSource } from '../../room/gif-decode.js';

/** Card values in the pure module's rank order, T for ten. Value i wears gifs[i % gifs.length]. */
export const DECK_VALUES = Object.freeze(['A', '2', '3', '4', '5', '6', '7', '8', '9', 'T', 'J', 'Q', 'K']);
export const DECK_CAPS = Object.freeze({ sources: 13, maxEdge: 192, maxFps: 12, loads: 3 });

const KEY_RE = /^g\d{1,2}$/;

/** FNV-1a, 32 bit, over the UTF-16 code units of a string. */
export function fnv1a(str) {
  let h = 0x811c9dc5;
  const s = String(str);
  for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193); }
  return h >>> 0;
}

/** 'A'..'K', a card code ('Qh', 'Td') or '10' -> 0..12, else -1. */
export function valueIndex(value) {
  const s = String(value == null ? '' : value).toUpperCase();
  if (s.startsWith('10')) return 9;
  return DECK_VALUES.indexOf(s.charAt(0));
}

/** CONTRACT 5: dealt media point at ccp.assets or ccp.game, or this page's own origin (the harnesses). */
export function drawableUrl(url) {
  if (typeof url !== 'string' || !url) return false;
  try {
    const base = typeof location !== 'undefined' ? location.href : 'https://ccp.game/';
    const u = new URL(url, base);
    if (u.protocol === 'https:' && (u.host === 'ccp.assets' || u.host === 'ccp.game')) return true;
    return typeof location !== 'undefined' && u.origin === location.origin;
  } catch (e) { return false; }
}

/** A still from an <img>, drawn once into a canvas no larger than maxEdge (so it never animates on its own). */
function stillOf(url, maxEdge) {
  return new Promise((resolve) => {
    if (typeof Image === 'undefined') { resolve(null); return; }
    const img = new Image();
    img.decoding = 'async';
    // ccp.assets is another origin: without CORS mode the still canvas is tainted, and image() is for a
    // texture, where the WebGL upload throws (same rule as stations/slot/media.js). Set before src.
    img.crossOrigin = 'anonymous';
    img.onload = () => {
      const iw = img.naturalWidth, ih = img.naturalHeight;
      if (!(iw > 0 && ih > 0)) { resolve(null); return; }
      const k = Math.min(1, maxEdge / Math.max(iw, ih));
      const c = document.createElement('canvas');
      c.width = Math.max(1, Math.round(iw * k)); c.height = Math.max(1, Math.round(ih * k));
      try { c.getContext('2d').drawImage(img, 0, 0, c.width, c.height); resolve(c); } catch (e) { resolve(null); }
    };
    img.onerror = () => resolve(null);
    img.src = url;
  });
}

/**
 * Ask the host for a deal and make the deck. Resolves once the deal is back (sources load behind it).
 * @param {Object} ctx  a station ctx: needs ctx.media({count}); without it the deck is one empty key
 */
export async function createDeck(ctx, { count = 13, maxEdge = DECK_CAPS.maxEdge, still = false } = {}) {
  const n = Number.isInteger(count) && count >= 1 && count <= DECK_CAPS.sources ? count : 13;
  const edge = Math.max(16, Math.min(DECK_CAPS.maxEdge, Number(maxEdge) || DECK_CAPS.maxEdge));
  let reply = null;
  if (ctx && typeof ctx.media === 'function') { try { reply = await ctx.media({ count: n }); } catch (e) { reply = null; } }

  const entries = [];
  const byKey = new Map();
  for (const g of (reply && Array.isArray(reply.gifs) ? reply.gifs : [])) {
    if (!g || !KEY_RE.test(String(g.key)) || byKey.has(g.key) || entries.length >= DECK_CAPS.sources) continue;
    const e = { key: g.key, url: drawableUrl(g.url) ? g.url : '', src: null, still: null, state: 'idle', drawn: false, wanted: false };
    entries.push(e); byKey.set(e.key, e);
  }
  // An empty deal (no host, a lost reply) still has one key, g0. The host resolves a key against its own
  // deal for the station (ResolveSymbols: a random dealt item) and acks the picture skipped `unknown` when it dealt none.
  const keys = Object.freeze(entries.length ? entries.map((e) => e.key) : ['g0']);
  const size = keys.length;
  const seed = reply && Number.isFinite(reply.seed) ? reply.seed : 0;

  let isStill = !!still, disposed = false, loading = 0, rr = 0;
  const stats = { loads: 0, animated: 0, stills: 0, failed: 0, decodes: 0, ticks: 0 };
  const canDecode = typeof document !== 'undefined';

  function load(e) {
    e.state = 'loading'; stats.loads++; loading++;
    const done = (async () => {
      let src = null;
      try { src = await decodedSource(e.url, { maxEdge: edge, maxFps: DECK_CAPS.maxFps }); } catch (err) { src = null; }
      if (disposed) { if (src) src.dispose(); return; }
      if (src) { e.src = src; e.state = 'ready'; if (src.animated) stats.animated++; else stats.stills++; return; }
      e.still = await stillOf(e.url, edge);
      if (disposed) return;
      e.state = e.still ? 'ready' : 'failed';
      if (e.still) stats.stills++; else stats.failed++;
    })();
    void done.finally(() => { loading--; pump(); });
  }
  const nextLoad = () => entries.find(e => e.state === 'idle' && e.url && e.wanted) || entries.find(e => e.state === 'idle' && e.url);
  function pump() {
    if (!canDecode || disposed) return;
    while (loading < DECK_CAPS.loads) { const e = nextLoad(); if (!e) break; load(e); }
  }

  pump();

  return {
    seed, keys, size,
    /** The key a card value wears ('A'..'K', or a card code such as 'Qh'), or null for no value. */
    keyFor(value) { const i = valueIndex(value); return i < 0 ? null : keys[i % size]; },
    /** keys[i % size], negative i included. */
    keyAt(i) { const k = Math.trunc(Number(i) || 0); return keys[((k % size) + size) % size]; },
    /** A key that is always the same for the same result string (FNV-1a). */
    pickKey(seedString) { return keys[fnv1a(seedString) % size]; },
    /** Cover-fit the key's picture into x, y, w, h, clipped to the rect. False while it has nothing to show. */
    draw(ctx2d, key, x, y, w, h, { alpha = 1 } = {}) {
      const e = byKey.get(key);
      if (disposed || !e || !ctx2d || !(w > 0 && h > 0)) return false;
      e.drawn = true; e.wanted = true;
      const img = e.src ? e.src.canvas : e.still;
      if (!img || !(img.width > 0 && img.height > 0)) return false;
      const s = Math.max(w / img.width, h / img.height), dw = img.width * s, dh = img.height * s;
      ctx2d.save();
      ctx2d.beginPath(); ctx2d.rect(x, y, w, h); ctx2d.clip();
      ctx2d.globalAlpha *= Math.max(0, Math.min(1, Number.isFinite(alpha) ? alpha : 1));
      ctx2d.drawImage(img, x + (w - dw) / 2, y + (h - dh) / 2, dw, dh);
      ctx2d.restore();
      return true;
    },
    /** The picture's canvas (decoded frames, or the still), for a texture. Null until it has loaded. */
    image(key) { const e = byKey.get(key); if (e) e.wanted = true; return e ? (e.src ? e.src.canvas : e.still) : null; },
    /** Once per rendered frame: at most one animation decode, independent of network loading. */
    tick(now, visibleKeys = []) {
      if (disposed) return false;
      for (const key of visibleKeys) { const e = byKey.get(key); if (e) { e.drawn = true; e.wanted = true; } }
      stats.ticks++;
      const t = Number.isFinite(now) ? now : performance.now();
      let started = false;
      pump();
      if (!started && entries.length) {
        for (let k = 0; k < entries.length && !started; k++) {
          const e = entries[(rr + k) % entries.length];
          if (!e.src || !(e.drawn || isStill)) continue;
          if (e.src.tick(t, isStill)) { started = true; stats.decodes++; rr = (rr + k + 1) % entries.length; }
        }
      }
      for (const e of entries) e.drawn = false;
      return started;
    },
    /** Still (reduced motion, Calm): every picture goes back to its first frame and holds. */
    setStill(on) { isStill = !!on; },
    dispose() {
      if (disposed) return;
      disposed = true;
      for (const e of entries) { if (e.src) { try { e.src.dispose(); } catch (err) { /* noop */ } } e.src = null; e.still = null; }
    },
    /** Test seam. */
    debug() {
      return { ...stats, size, loading: !!loading, loadingCount: loading, ready: entries.filter((e) => e.state === 'ready').length,
        frames: entries.reduce((s, e) => s + (e.src ? e.src.frames : 0), 0), still: isStill, disposed };
    },
  };
}
