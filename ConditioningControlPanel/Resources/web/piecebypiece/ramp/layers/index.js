/* ============================================================================
 * ramp/layers/index.js - the layer stack the ramp drives.
 *
 * This file is the SEAM. `attachRamp` never touches DOM: it hands this stack a
 * heat number and a list of kinds to fire, and the stack owns the pixels. That
 * keeps the meter/schedule math (node-testable) apart from the layers (not).
 *
 * Two planes, both click-through: `root` (#fx) carries everything, `front`
 * carries the video card so a tape can ride in front of the POV. One shared
 * FILTER COMPOSER owns `#stage`'s CSS filter, because melt and blur both write
 * it and the last one to touch style.filter would otherwise erase the other.
 * ==========================================================================*/

import { createFlash } from './flash.js';
import { createGifRain } from './gifrain.js';
import { createMelt } from './melt.js';
import { createBlur } from './blur.js';
import { createSpiral } from './spiral.js';
import { createOverlay } from './overlay.js';
import { createVideoCard } from './videocard.js';
import { createGlitchGrab } from './glitchgrab.js';

/** A layer that has not been built yet: every call is a safe no-op. */
const NOOP = Object.freeze({ set() {}, fire() {}, show() {}, grab() {}, move() {}, drop() {}, clear() {}, dispose() {} });

/**
 * ONE owner for `#stage`'s filter. Layers contribute a named fragment and the
 * composer writes the concatenation, so melt and blur stack instead of fight.
 */
export function createStageFilter(stage) {
  const parts = new Map();
  let armed = false;
  function write() {
    if (!stage) return;
    const css = [...parts.values()].filter(Boolean).join(' ');
    try {
      if (!armed) { stage.classList.add('pbp-stage-fx'); armed = true; }
      stage.style.filter = css;
    } catch { /* the stage went away under us */ }
  }
  return {
    set(name, css) { if (css) parts.set(name, css); else parts.delete(name); write(); },
    clear() { parts.clear(); write(); },
    dispose() {
      parts.clear();
      if (!stage) return;
      try { stage.style.filter = ''; stage.classList.remove('pbp-stage-fx'); } catch { /* gone */ }
    },
  };
}

/** The helpers every layer gets, so no layer re-implements random or mounting. */
function makeCtx(ctx) {
  const rng = typeof ctx.rng === 'function' ? ctx.rng : Math.random;
  const media = ctx.media || null;
  return {
    ...ctx,
    rng,
    rand: (lo, hi) => lo + rng() * (hi - lo),
    randInt: (lo, hi) => Math.floor(lo + rng() * (hi - lo + 1)),
    pick: (arr) => arr[Math.floor(rng() * arr.length)],
    /** A detached element, class already set. Null when there is no document. */
    el(tag, cls) {
      try {
        const e = document.createElement(tag);
        if (cls) e.className = cls;
        return e;
      } catch { return null; }
    },
    mount(e) { try { if (ctx.root && e) ctx.root.appendChild(e); } catch { /* no root */ } return e; },
    mountFront(e) {
      const host = ctx.front || ctx.root;
      try { if (host && e) host.appendChild(e); } catch { /* no host */ }
      return e;
    },
    /** A gif url, or a generated pink noise tile when the pool has no gifs. */
    tile: () => (media && media.drawTile ? media.drawTile() : null),
    image: () => (media && media.draw ? media.draw('image') : null),
    video: () => (media && media.draw ? media.draw('video') : null),
    hasRoot: () => !!ctx.root,
  };
}

/**
 * createLayerStack(ctx) - ctx is { root, front, stage, media, tuning, rng }.
 * Every method must survive a missing root, a missing stage and empty media.
 */
export function createLayerStack(ctx = {}) {
  const base = makeCtx(ctx);
  base.stageFilter = createStageFilter(ctx.stage || null);

  const oneshot = {
    flash: safe(() => createFlash(base)),
    gifRain: safe(() => createGifRain(base)),
  };
  const sustained = {
    melt: safe(() => createMelt(base)),
    blur: safe(() => createBlur(base)),
    spiral: safe(() => createSpiral(base)),
    overlay: safe(() => createOverlay(base)),
  };
  const card = safe(() => createVideoCard(base));
  const drag = safe(() => createGlitchGrab(base));

  const counts = { flash: 0, gifRain: 0, burst: 0, videoCard: 0, grab: 0, drop: 0 };
  let disposed = false;

  /** A layer that throws while being built must not take the ramp with it. */
  function safe(build) {
    try { return build() || NOOP; } catch (e) {
      try { console.warn('[pbp/ramp] layer failed to build: ' + (e && e.message)); } catch { /* no console */ }
      return NOOP;
    }
  }

  const api = {
    oneshot(kind, opts) {
      if (disposed) return;
      const layer = oneshot[kind];
      if (!layer) return;
      counts[kind] = (counts[kind] || 0) + 1;
      try { layer.fire(opts || {}); } catch { /* one bad spawn is not a crash */ }
    },
    burst(spec) {
      if (disposed || !spec) return;
      counts.burst += 1;
      for (let i = 0; i < (spec.flashes || 0); i++) api.oneshot('flash', spec);
      for (let i = 0; i < (spec.gifs || 0); i++) api.oneshot('gifRain', spec);
    },
    setSustained(name, spec) {
      if (disposed) return;
      const layer = sustained[name];
      if (!layer) return;
      try { layer.set(spec || { on: false }); } catch { /* a retune is never fatal */ }
    },
    videoCard(opts) {
      if (disposed) return;
      counts.videoCard += 1;
      try { card.show(opts || {}); } catch { /* no card is fine */ }
    },
    grab(p) { if (!disposed) { counts.grab += 1; try { drag.grab(p); } catch { /* ignore */ } } },
    dragmove(p) { if (!disposed) { try { drag.move(p); } catch { /* per-frame, stay quiet */ } } },
    drop(p) { if (!disposed) { counts.drop += 1; try { drag.drop(p); } catch { /* ignore */ } } },
    clear() {
      for (const l of [...Object.values(oneshot), ...Object.values(sustained), card, drag]) {
        try { l.clear(); } catch { /* best effort */ }
      }
      base.stageFilter.clear();
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      for (const l of [...Object.values(oneshot), ...Object.values(sustained), card, drag]) {
        try { l.dispose(); } catch { /* best effort */ }
      }
      base.stageFilter.dispose();
    },
    debug() { return { counts: { ...counts }, hasMedia: !!(ctx.media && ctx.media.size) }; },
  };
  return api;
}

export default createLayerStack;
