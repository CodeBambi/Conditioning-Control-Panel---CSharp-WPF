/* ============================================================================
 * stations/breakout/payloads.js - the niche layer: GIF sources decoded into
 * canvases (room/gif-decode.js, room/clip-source.js for clips, an <img> still
 * as the last resort), the subliminal word scheduler, and the procedural
 * spiral. COLOUR only: the callers gate on state, this file just supplies.
 * ==========================================================================*/

import { decodedSource } from '../../room/gif-decode.js';
import { isClip, clipSource } from '../../room/clip-source.js';

const MAX_RESIDENT = 8, EDGE = 192, FPS = 12;

/** A still <img> wrapped in the same shape as a decoded source. */
function stillSource(url) {
  return new Promise((resolve) => {
    const img = new Image();
    img.crossOrigin = 'anonymous';
    img.onload = () => resolve({ canvas: img, animated: false, tick() { return false; }, dispose() {} });
    img.onerror = () => resolve(null);
    img.src = url;
  });
}

/**
 * createMedia({ ctx, still }) -> { load(), redeal(), deals(), frame(i), count(), tick(now), words, keys(), trailWords(n),
 *                                  mark(i, x, y), setFocus(x, y, r), dispose() }
 * `frame(i)` picks a resident source by index modulo the resident count (bricks and colliders store a small
 * integer, so a short list still covers them). Returns a canvas or image, or null before anything loaded.
 * Bricks whisper: `mark(i, x, y)` records where source i was last drawn (field coords) and `setFocus(x, y, r)`
 * (the first live ball) lets only sources within r of the focus advance on tick(); the rest hold their frame.
 * A source never marked always ticks, so an older renderer keeps its faces moving.
 */
export function createMedia({ ctx, still = false, count = 8 } = {}) {
  const sources = [];
  let words = [], disposed = false, focus = null, busy = false, deals = 0;
  const controller = new AbortController();
  const idx = i => (!sources.length ? -1 : ((i | 0) % sources.length + sources.length) % sources.length);
  const near = s => !focus || !s.at || Math.hypot(s.at.x - focus.x, s.at.y - focus.y) <= focus.r;

  async function one(url) {
    try {
      const opts = { maxEdge: EDGE, maxFps: FPS, signal: controller.signal };
      const src = isClip(url) ? await clipSource(url, opts) : await decodedSource(url, opts);
      if (src) return src;
    } catch (e) { /* fall through to a still */ }
    try { return await stillSource(url); } catch (e) { return null; }
  }
  /** Ask the host for a deal and decode it into a list; the host keeps the newest deal for this sit-down. */
  async function deal() {
    let dealt = null;
    try { dealt = await ctx.media({ count }); } catch (e) { dealt = null; }
    if (!dealt || disposed) return null;
    const list = [];
    const gifs = Array.isArray(dealt.gifs) ? dealt.gifs.slice(0, MAX_RESIDENT) : [];
    await Promise.all(gifs.map(async (gif) => {
      if (!gif || !gif.url) return;
      const src = await one(gif.url);
      if (!src) return;
      if (disposed || list.length >= MAX_RESIDENT) { try { src.dispose(); } catch (e) { /* noop */ } return; }
      list.push({ key: gif.key, src });
    }));
    if (disposed) { for (const s of list) { try { s.src.dispose(); } catch (e) { /* noop */ } } return null; }
    return { list, words: Array.isArray(dealt.words) ? dealt.words.filter(w => w && w.text).map(w => ({ key: w.key, text: String(w.text) })) : [] };
  }
  async function load() {
    const d = await deal();
    if (!d) return false;
    words = d.words;
    sources.push(...d.list);
    deals++;
    return true;
  }
  /**
   * A fresh deal mid-run (every third wall, or after the room's source changed). The old pictures stay on
   * screen until the new ones are decoded, then the list swaps in place: bricks and colliders keep their small
   * indices and simply wear the new faces. One re-deal in flight at a time; a deal with nothing in it is kept out.
   */
  async function redeal() {
    if (busy || disposed) return false;
    busy = true;
    try {
      const d = await deal();
      if (!d || disposed) return false;
      if (!d.list.length && !d.words.length) return false;
      if (d.list.length) {
        const old = sources.splice(0, sources.length, ...d.list);
        for (const s of old) { try { s.src.dispose(); } catch (e) { /* noop */ } }
      }
      if (d.words.length) words = d.words;
      deals++;
      return true;
    } finally { busy = false; }
  }
  return {
    load, redeal,
    get words() { return words; },
    /** How many deals have landed (1 after load, +1 per re-deal). */
    deals: () => deals,
    count: () => sources.length,
    /** How many resident pictures can play (a still counts as not animated). */
    animated: () => sources.filter(s => s.src && s.src.animated).length,
    keys: () => sources.map(s => s.key),
    frame(i) { const k = idx(i); return k < 0 ? null : sources[k].src.canvas; },
    /** Where source i sits this frame (field coords); the renderer or the station calls it before tick(). */
    mark(i, x, y) { const k = idx(i); if (k < 0) return; const s = sources[k]; if (!s.at) s.at = { x, y }; else { s.at.x = x; s.at.y = y; } },
    setFocus(x, y, r = 140) { focus = (Number.isFinite(x) && Number.isFinite(y)) ? { x, y, r: Math.max(0, r) } : null; },
    clearMarks() { for (const s of sources) s.at = null; },
    /** Source i advances on the next tick() wherever the focus is: the bubbles and the pops always move. */
    pin(i) { const k = idx(i); if (k >= 0) sources[k].pin = true; },
    tick(now) { for (const s of sources) { const go = s.pin || near(s); s.pin = false; if (!go) continue; try { s.src.tick(now, still); } catch (e) { /* a closed decoder */ } } },
    /** Up to n dealt word texts, for the word trail and the mantra wall. */
    trailWords(n = 12) { return words.slice(0, Math.max(0, n | 0)).map(w => w.text); },
    dispose() { disposed = true; controller.abort(); for (const s of sources) { try { s.src.dispose(); } catch (e) { /* noop */ } } sources.length = 0; },
  };
}

/**
 * createSubliminals({ words(), rng, enabled, fx, w, h }) -> { tick(now, sat, on, ball), onBrick(now, sat, ball), current(now), reset() }
 * A word flashes for ~50 ms NEAR the ball (offset, never under it). Cadence: variable ratio, mean 4 s at rung 7
 * falling to 2 s at saturation 1; plus a 25% chance on every brick above 0.6. `fx('fx.sub_single', [key])` at most
 * once per 10 s so the desktop host can flash its own. The fullscreen moments live in host-fx.js.
 */
export function createSubliminals({ words, rng = Math.random, enabled = true, fx = null, w = 480, h = 720 } = {}) {
  let next = 0, flash = null, lastFx = -Infinity;
  const list = () => (typeof words === 'function' ? words() : words) || [];
  function place(now, ball) {
    const pool = list();
    if (!enabled || !pool.length) return null;
    const word = pool[Math.floor(rng() * pool.length)];
    const a = rng() * Math.PI * 2, d = 44 + rng() * 30;
    const x = Math.max(40, Math.min(w - 40, (ball ? ball.x : w / 2) + Math.cos(a) * d));
    const y = Math.max(30, Math.min(h - 60, (ball ? ball.y : h / 2) + Math.sin(a) * d));
    flash = { text: word.text, key: word.key, x, y, at: now, until: now + 0.05 };
    if (fx && now - lastFx >= 10) { lastFx = now; try { fx('fx.sub_single', word.key ? [word.key] : []); } catch (e) { /* host optional */ } }
    return flash;
  }
  return {
    tick(now, sat, on, ball) {
      if (!on || !enabled) { next = now + 1; return null; }
      if (now < next) return null;
      const mean = 8 - 4 * Math.max(0, Math.min(1, (sat - 0.6) / 0.4));   // halved cadence (owner, 2026-09-19)
      next = now + mean * (0.5 + rng());
      return place(now, ball);
    },
    onBrick(now, sat, ball) { return (enabled && sat > 0.6 && rng() < 0.125) ? place(now, ball) : null; },
    current(now) { return flash && now < flash.until ? flash : null; },
    reset() { flash = null; },
  };
}

/** Three arms of a logarithmic spiral, rotated by `rot`, in `stroke`, with alpha. Procedural, no asset. */
export function drawSpiral(g, x, y, r, rot, stroke, alpha = 1, arms = 3) {
  const b = 0.22, turns = 2.6, steps = 60, maxT = turns * Math.PI * 2;
  const scale = r / Math.exp(b * maxT);
  g.save();
  g.translate(x, y); g.rotate(rot);
  g.globalAlpha = alpha; g.strokeStyle = stroke; g.lineCap = 'round';
  for (let k = 0; k < arms; k++) {
    g.rotate((Math.PI * 2) / arms);
    g.beginPath();
    for (let i = 0; i <= steps; i++) {
      const t = (i / steps) * maxT, rr = scale * Math.exp(b * t);
      const px = Math.cos(t) * rr, py = Math.sin(t) * rr;
      if (i === 0) g.moveTo(px, py); else g.lineTo(px, py);
    }
    g.lineWidth = 2.5;
    g.stroke();
  }
  g.restore();
}
