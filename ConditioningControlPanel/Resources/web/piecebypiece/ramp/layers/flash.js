/* ============================================================================
 * layers/flash.js - flash images popping at the edges and over the board.
 *
 * The DtRH scattered-image burst (payloadFx.flash), re-aimed at a chess board:
 * most spawns hug the EDGES so the board stays readable early, and the hotter
 * it gets the more of them land dead over the pieces. One image per pop, a hard
 * live cap, and every node self-removes on animationend with a timer as a net.
 * ==========================================================================*/

import { isClip } from './clip.js';

/** Where a flash lands. Edge bands early, over the board once it is hot. */
function place(ctx, heat) {
  const overBoard = ctx.rng() < 0.18 + 0.42 * heat;
  if (overBoard) return { left: ctx.rand(30, 70), top: ctx.rand(28, 70) };
  // four edge bands, picked evenly, so pops ring the board instead of stacking
  switch (ctx.randInt(0, 3)) {
    case 0: return { left: ctx.rand(4, 96), top: ctx.rand(2, 16) };
    case 1: return { left: ctx.rand(4, 96), top: ctx.rand(82, 96) };
    case 2: return { left: ctx.rand(2, 16), top: ctx.rand(6, 94) };
    default: return { left: ctx.rand(84, 97), top: ctx.rand(6, 94) };
  }
}

/**
 * How strong a flash may be at this spot: 1 at the edges, down to centreAlpha
 * over the middle of the screen (Mort, 2026-09-28: a flash dead over the board
 * hid what the computer did with its move). `at` is the landing spot in vw/vh.
 */
export function centreFade(at, t = {}) {
  const floor = t.centreAlpha ?? 0.22;
  const inner = t.centreInner ?? 0.16;
  const outer = t.centreOuter ?? 0.6;
  const d = Math.hypot((at.left - 50) / 50, (at.top - 50) / 50);
  const k = d <= inner ? 0 : d >= outer ? 1 : (d - inner) / Math.max(0.01, outer - inner);
  return floor + (1 - floor) * (k * k * (3 - 2 * k));
}

export function createFlash(ctx) {
  const t = (ctx.tuning && ctx.tuning.flash) || { maxLive: 6 };
  let live = 0;
  let disposed = false;
  const nodes = new Map();   // element -> the picture it shows

  function fire(opts = {}) {
    if (disposed || !ctx.hasRoot() || live >= t.maxLive) return;
    const heat = Math.min(1, Math.max(0, opts.heat == null ? 0.5 : opts.heat));
    // every still up: a tile may still answer, but never a clip (an <img> cannot play one)
    const tile = ctx.image() || ctx.tile();
    const url = isClip(tile) ? null : tile;
    if (!url) return;                     // no pictures free: nothing to pop
    const img = ctx.el('img', 'pbp-flash');
    if (!img) return;
    const at = place(ctx, heat);
    // hotter = quicker and punchier, so a busy board reads as a strobe
    const dur = Math.round(ctx.rand(1500, 2100) - heat * 700);
    img.style.left = at.left + 'vw';
    img.style.top = at.top + 'vh';
    img.style.setProperty('--pbp-dur', dur + 'ms');
    img.style.setProperty('--pbp-rot', ctx.rand(-9, 9).toFixed(2) + 'deg');
    img.style.setProperty('--pbp-size', (16 + heat * 16).toFixed(1) + 'vmin');
    img.style.setProperty('--pbp-peak', ((0.55 + heat * 0.4) * centreFade(at, t)).toFixed(2));
    img.decoding = 'async';
    img.src = url;

    live += 1;
    nodes.set(img, url);
    ctx.hold(url);
    const kill = () => {
      if (!nodes.delete(img)) return;
      live = Math.max(0, live - 1);
      ctx.release(url);
      try { img.remove(); } catch { /* already gone */ }
    };
    img.addEventListener('animationend', kill, { once: true });
    img.addEventListener('error', kill, { once: true });   // a dead url must not hold a slot
    setTimeout(kill, dur + 700);                           // net for a missed animationend
    ctx.mount(img);
  }

  function clear() {
    for (const [n, u] of [...nodes]) { nodes.delete(n); ctx.release(u); try { n.remove(); } catch { /* gone */ } }
    live = 0;
  }

  /** The breath after a move: what is still up lets go quickly instead of finishing its show. */
  function fade(ms = 280) {
    for (const n of [...nodes.keys()]) {
      try {
        const from = getComputedStyle(n).opacity;
        n.style.animationPlayState = 'paused';
        n.style.opacity = from;
        n.style.transition = 'opacity ' + ms + 'ms ease';
        void n.offsetWidth;
        n.style.animation = 'none';
        n.style.opacity = '0';
      } catch { /* gone */ }
    }
    setTimeout(clear, ms + 40);
  }

  return { fire, clear, fade, dispose() { disposed = true; clear(); }, set() {}, show() {}, grab() {}, move() {}, drop() {} };
}

export default createFlash;
