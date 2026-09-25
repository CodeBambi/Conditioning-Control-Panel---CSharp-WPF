/* media.js - the sit-down deal (CONTRACT.md section 5) as the reel painter sees it.
 * gif0..gif3 = gifs[i] (missing or duplicate sources use distinct built-in art), sub0..sub3 = words[0..3], kept until the player stands up. GIFs load as
 * explicit decoded canvases because drawImage(<img>) captures the default image.
 * Hidden <img> elements supply a still only when decoding is unavailable. Only ccp.assets / ccp.game (or this page's own origin, for dev.html) URLs
 * are loaded; anything else, or a load failure, falls back to the built-in art in symbols.js.
 * A dealt CLIP (webm/mp4: since 2026-09-17 every remote picture is one) is routed by its extension to room/clip-source.js and PLAYED into its
 * canvas on the reel's own tick, the same shape a decoded GIF has, so the painter never learns a new kind of picture. A clip that cannot play
 * takes the built-in art at once: an <img> cannot show a video either, so the second attempt below is only ever for a picture.
 * Keys, never URLs, leave this file, and that includes the `log` seam: it is room/main.js's bridge.log, and
 * console.warn does NOT reach the app log (only the bridge's 'log' envelope does), so anything worth reading
 * back off a session log has to go through it. */

import { kindOf } from './symbols.js';
import { decodedSource } from '../../room/gif-decode.js';
import { clipSource, isClip } from '../../room/clip-source.js';
import { overBudget } from '../../room/media-limits.js';

const LOAD_MS = 2500;

function allowed(url) {
  try {
    const u = new URL(url, location.href);
    return u.origin === 'https://ccp.assets' || u.origin === 'https://ccp.game' || u.origin === location.origin;
  } catch { return false; }
}

/** The dealt key, for a log line. Keys, never urls, leave this file (see the header). */
function keyOf(item, i) { return item && typeof item.key === 'string' && item.key ? item.key : 'gif' + i; }

/** False when drawing the image would taint a canvas (served without CORS), checked once per img, so the
 *  warning is written once per dealt picture rather than once per painted frame. */
function readable(img, log) {
  if (img.dataset.readable) return img.dataset.readable === '1';
  let ok = true;
  try {
    const c = document.createElement('canvas'); c.width = c.height = 1;
    const g = c.getContext('2d'); g.drawImage(img, 0, 0, 1, 1); g.getImageData(0, 0, 1, 1);
  } catch { ok = false; log('warn', 'slot media is not CORS-readable, using built-in art'); }
  img.dataset.readable = ok ? '1' : '0';
  return ok;
}

/** The same question for a clip's canvas, asked ONCE when the clip opens rather than per painted frame. A <video>
 *  served without CORS taints the canvas clip-source.js paints it into, and the taint surfaces later as a thrown
 *  WebGL upload on the reel texture, so it is caught here where the answer is "built-in art" and not a broken reel. */
function readableCanvas(canvas, log) {
  try {
    const c = document.createElement('canvas'); c.width = c.height = 1;
    const g = c.getContext('2d'); g.drawImage(canvas, 0, 0, 1, 1); g.getImageData(0, 0, 1, 1);
    return true;
  } catch { log('warn', 'slot clip is not CORS-readable, using built-in art'); return false; }
}

export function createMedia(holder, lex = (k, f) => f, log = () => {}) {
  let gifs = [], words = [], imgs = [], sources = [], epoch = 0;

  const pendingImages = new Set();
  let controller = new AbortController();
  const releaseImage = img => { img.onload = img.onerror = null; img.removeAttribute('src'); img.remove(); };

  function clear() {
    controller.abort(); controller = new AbortController();
    epoch++; sources.forEach(src => src?.dispose()); sources = [];
    for (const cancel of [...pendingImages]) cancel();
    imgs.forEach(img => img && releaseImage(img));
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
      const decoded = gifs.map(async (item, i) => {
        if (!item?.url || !allowed(item.url)) return;
        if (isClip(item.url)) {
          // A clip is played, not decoded. clipSource never throws: null is "this page cannot play it"
          // (no video element, a codec WebView2 lacks, a load that timed out), and there is no second
          // pipeline for a video, so null is the built-in art. Silent when it is only this deal being
          // replaced; a word in the log otherwise, because a reel on fallback art is worth reading back.
          const clip = await clipSource(item.url, { maxEdge: 256, signal: controller.signal });
          if (dealEpoch !== epoch) { clip?.dispose(); return; }
          if (!clip) { if (!controller.signal.aborted) log('warn', 'slot media ' + keyOf(item, i) + ' clip could not play, using built-in art'); return; }
          if (!readableCanvas(clip.canvas, log)) { clip.dispose(); return; }
          sources[i] = clip;
          return;
        }
        let source;
        try { source = await decodedSource(item.url, { maxEdge: 256, maxFps: 12, signal: controller.signal }); }
        catch(error) {
          // decodedSource funnels EVERY failure into MediaLimitError or AbortError, so "it refused" on its
          // own says nothing about why, and this used to return on all three, which left the reel on
          // built-in art AND skipped the <img> fallback below - dead code, and an all-faces-fallback reel
          // for the rest of the page's life, because station.js caches one deal per page.
          // An abort is this deal being replaced or the seat being left, and is not worth a word.
          // A budget breach is a real refusal: we declined to spend the pixels, so we must not turn round
          // and hand the same file to the browser's decoder instead.
          // Anything else is a transfer that did not arrive - most plausibly a warm remote file that
          // RemoteMediaCache swept out from under its url (oldest-first, once 50 temp files are tracked)
          // while the pool refilled - and that deserves the second, independent attempt below.
          if (error?.name === 'AbortError') return;
          if (overBudget(error)) { log('warn', 'slot media ' + keyOf(item, i) + ' refused: ' + error.message); return; }
          log('warn', 'slot media ' + keyOf(item, i) + ' did not decode (' + error.message + '), trying the browser image pipeline');
          source = null;
        }
        if (dealEpoch !== epoch) { source?.dispose(); return; }
        if (source) { sources[i] = source; return; }
        // Start the browser image pipeline only if explicit decoding failed.
        await new Promise(done => {
          const img = new Image();
          let timer, finished = false;
          const finish = keep => {
            if (finished) return;
            finished = true;
            clearTimeout(timer); pendingImages.delete(cancel);
            img.onload = img.onerror = null;
            if (keep && dealEpoch === epoch) imgs[i] = img;
            else releaseImage(img);
            done();
          };
          const cancel = () => finish(false);
          pendingImages.add(cancel);
          img.decoding = 'async'; img.alt = ''; img.crossOrigin = 'anonymous';
          img.onload = () => finish(img.naturalWidth > 0);
          img.onerror = cancel;
          timer = setTimeout(cancel, LOAD_MS);
          holder.append(img);
          img.src = new URL(item.url, location.href).href;
        });
      });
      let timer;
      return Promise.race([Promise.all(decoded), new Promise(done => { timer = setTimeout(done, LOAD_MS); })])
        .finally(() => clearTimeout(timer));
    },
    /** A drawable for gif{i} (stable symbol identity), or null when missing or broken (the painter draws fallback art).
     *  `still` (reduced motion, Calm, Motion off: the station's stillFx) holds the picture on one frame - a decoded GIF
     *  returns to its first, a clip PAUSES - so discarding stills as a fetched class never took away the reel's ability
     *  to paint one when the player asked for stillness. */
    gif(i, still = false) {
      const source = sources.length ? sources[i] : null;
      if (source) { source.tick(performance.now(), !!still); return source.clip && !source.frames ? null : source.canvas; }
      const img = imgs.length ? imgs[i] : null;
      return img && img.complete && img.naturalWidth > 0 && readable(img, log) ? img : null;
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
    get animated() { return sources.some(Boolean) || imgs.some(Boolean); },
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
