/* ============================================================================
 * layers/flash.js - flash images popping at the edges and over the board.
 *
 * The DtRH scattered-image burst (payloadFx.flash), re-aimed at a chess board:
 * most spawns hug the EDGES so the board stays readable early, and the hotter
 * it gets the more of them land dead over the pieces. One image per pop, a hard
 * live cap, and every node self-removes on animationend with a timer as a net.
 * ==========================================================================*/

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

export function createFlash(ctx) {
  const t = (ctx.tuning && ctx.tuning.flash) || { maxLive: 6 };
  let live = 0;
  let disposed = false;
  const nodes = new Set();

  function fire(opts = {}) {
    if (disposed || !ctx.hasRoot() || live >= t.maxLive) return;
    const heat = Math.min(1, Math.max(0, opts.heat == null ? 0.5 : opts.heat));
    const url = ctx.image() || ctx.tile();
    if (!url) return;                     // no pictures at all: nothing to pop
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
    img.style.setProperty('--pbp-peak', (0.55 + heat * 0.4).toFixed(2));
    img.decoding = 'async';
    img.src = url;

    live += 1;
    nodes.add(img);
    const kill = () => {
      if (!nodes.delete(img)) return;
      live = Math.max(0, live - 1);
      try { img.remove(); } catch { /* already gone */ }
    };
    img.addEventListener('animationend', kill, { once: true });
    img.addEventListener('error', kill, { once: true });   // a dead url must not hold a slot
    setTimeout(kill, dur + 700);                           // net for a missed animationend
    ctx.mount(img);
  }

  function clear() {
    for (const n of [...nodes]) { nodes.delete(n); try { n.remove(); } catch { /* gone */ } }
    live = 0;
  }

  return { fire, clear, dispose() { disposed = true; clear(); }, set() {}, show() {}, grab() {}, move() {}, drop() {} };
}

export default createFlash;
