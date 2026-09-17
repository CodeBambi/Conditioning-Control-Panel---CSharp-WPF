/* media.js - the sit-down deal (CONTRACT.md section 5) as the reel painter sees it.
 * gif0..gif3 = gifs[i] (missing or duplicate sources use distinct built-in art), sub0..sub3 = words[0..3], kept until the player stands up. GIFs load as
 * explicit decoded canvases because drawImage(<img>) captures the default image.
 * Hidden <img> elements supply a still only when decoding is unavailable. Only ccp.assets / ccp.game (or this page's own origin, for dev.html) URLs
 * are loaded; anything else, or a load failure, falls back to the built-in art in symbols.js.
 * Keys, never URLs, leave this file. */

import { kindOf } from './symbols.js';
import { decodedSource } from '../../room/gif-decode.js';

const LOAD_MS = 2500;

function allowed(url) {
  try {
    const u = new URL(url, location.href);
    return u.origin === 'https://ccp.assets' || u.origin === 'https://ccp.game' || u.origin === location.origin;
  } catch { return false; }
}

/** False when drawing the image would taint a canvas (served without CORS), checked once per img. */
function readable(img) {
  if (img.dataset.readable) return img.dataset.readable === '1';
  let ok = true;
  try {
    const c = document.createElement('canvas'); c.width = c.height = 1;
    const g = c.getContext('2d'); g.drawImage(img, 0, 0, 1, 1); g.getImageData(0, 0, 1, 1);
  } catch { ok = false; console.warn('[slot] dealt GIF is not CORS-readable, using fallback art'); }
  img.dataset.readable = ok ? '1' : '0';
  return ok;
}

export function createMedia(holder, lex = (k, f) => f) {
  let gifs = [], words = [], imgs = [], sources = [], epoch = 0;

  function clear() {
    epoch++; sources.forEach(src => src?.dispose()); sources = [];
    imgs.forEach(img => img && img.remove());
    imgs = []; gifs = []; words = [];
  }

  return {
    /** Adopt a `media` reply. Resolves once every GIF loaded or failed (capped), never rejects. */
    deal(media) {
      clear();
      const dealEpoch = epoch;
      gifs = Array.isArray(media && media.gifs) ? media.gifs.slice(0, 4) : [];
      // A chase names a specific symbol. Repeated/missing media uses that symbol's distinct fallback.
      const seenUrls = new Set();
      gifs = gifs.map(item => { if (!item?.url || seenUrls.has(item.url)) return null; seenUrls.add(item.url); return item; });
      words = Array.isArray(media && media.words) ? media.words.slice(0, 4) : [];
      imgs = gifs.map(g => {
        if (!g || typeof g.url !== 'string' || !allowed(g.url)) return null;
        const img = new Image();
        img.decoding = 'async'; img.alt = '';
        // ccp.assets is another origin: without CORS mode the reel canvas is tainted and the WebGL
        // upload throws (same rule as dtrh/engine/spawner.js). Set before src.
        img.crossOrigin = 'anonymous';
        img.src = new URL(g.url, location.href).href;
        holder.append(img);
        return img;
      });
      const decoded = gifs.map(async (item, i) => {
        if (!item?.url || !allowed(item.url)) return;
        const source = await decodedSource(item.url, { maxEdge: 256, maxFps: 12 });
        if (dealEpoch !== epoch) { source?.dispose(); return; }
        sources[i] = source;
      });
      const waits = imgs.filter(Boolean).map(img => new Promise(done => {
        if (img.complete) return done();
        img.onload = img.onerror = () => done();
      }));
      return Promise.race([Promise.all([...waits, ...decoded]), new Promise(done => setTimeout(done, LOAD_MS))]);
    },
    /** A drawable for gif{i} (stable symbol identity), or null when missing or broken (the painter draws fallback art). */
    gif(i) {
      const source = sources.length ? sources[i] : null;
      if (source) { source.tick(performance.now(), false); return source.canvas; }
      const img = imgs.length ? imgs[i] : null;
      return img && img.complete && img.naturalWidth > 0 && readable(img) ? img : null;
    },
    word(i) {
      const w = words[i];
      if (!w || typeof w.text !== 'string' || !w.text) return null;
      return w.src === 'preset' ? lex(w.text, w.text) : w.text;
    },
    /** Symbol id -> the dealt key (g0, s1...), or null for symbols with no media behind them. */
    keyFor(id) {
      const { kind, n } = kindOf(id);
      const item = kind === 'gif' ? (gifs[n] || null) : kind === 'sub' ? words[n] : null;
      return item && typeof item.key === 'string' ? item.key : null;
    },
    get animated() { return imgs.some(Boolean); },
    dispose: clear,
  };
}

/** The dealt keys an effect should carry for one outcome (section 4: host resolves them). */
export function fxSymbols(fxId, outcome, media) {
  const row = Array.isArray(outcome.symbols) ? outcome.symbols : [];
  const pick = fxId.startsWith('fx.sub_') ? (Array.isArray(outcome.subs) ? outcome.subs : row.filter(s => kindOf(s).kind === 'sub'))
    : fxId.startsWith('fx.gif_') ? row.filter(s => kindOf(s).kind === 'gif')
    : row;
  return [...new Set(pick.map(s => media.keyFor(s)).filter(Boolean))];
}
