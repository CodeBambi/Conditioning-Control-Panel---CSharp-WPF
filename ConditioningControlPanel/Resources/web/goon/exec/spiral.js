/* ============================================================================
 * exec/spiral.js — GoonElement.Spiral (8) + GoonPayloadKind.Spiral (7).
 *
 * The DtRH spiral, straight across: ONE persistent full-window pane on
 * #gg-fx-spiral, screen-blended and spinning slowly (.gg-spiral in fx.css is the
 * port of DtRH's .sf-pfx-spiral). Intensity moves exactly one dial — opacity,
 * 0.25..0.70, the same band payloadFx.showSpiral uses — because "more spiral"
 * is the whole idea.
 *
 * ONE ELEMENT, ALWAYS. The element and the payload share the pane through a dial
 * stack (whoever wants it louder wins); a payload landing on a running bed does
 * not add a second spinning cover pass.
 *
 * TWO WAYS TO FILL THAT PANE, AND THE GOOD ONE IS PREFERRED.
 *   1. WOVEN (default). exec/spiralField.js renders the Loom's own spiral field
 *      live in WebGL at the window's real pixel ratio — no magnification, no
 *      palette, no dither. The pane gets a <canvas> child and its CSS
 *      magnification/softening (`scale: 1.6`, `filter: blur(1.1px)`) and its
 *      spin keyframe are all cancelled INLINE, because a live renderer needs
 *      none of them: the shader oversizes to the corners itself and does its own
 *      rotation. See that module's banner for why the rasters were grainy.
 *   2. RASTER (fallback). No WebGL, no canvas, or a lost context -> the pane
 *      keeps the bundled sp*.gif pool exactly as it always worked, blur and
 *      overscan included. The fallback is never removed and never degraded; it
 *      is what a machine without a GPU still gets.
 * The pane always carries a pool background-image either way, so path 2 is one
 * property removal away at any moment.
 *
 * ASSETS: the pool is the DtRH bundle under /dtrh/assets/bubbles/effects/spirals/
 * (same ccp.game origin — Resources/web is the host root, so /dtrh/... resolves
 * from /goon/). Deliberately NOT imported from dtrh/engine/loomSpirals.js: that
 * module pulls DtRH settings/Loom storage in with it. The pick is three lines.
 * exec/spiralField.js obeys the same rule — it is a COPY of the Loom's shader,
 * not an import, and it never touches the player's saved-spiral library.
 *
 * Uniform renderer shape — see the banner in exec/flashes.js.
 * ==========================================================================*/

import { createSpiralField, pickSpiralParams, loopMsFor } from './spiralField.js';

/**
 * The bundled spiral pool — the FALLBACK bed (DtRH ships these; goon reads them
 * off the same host) and the source for the bubble-pop flick, which is a brief
 * transient where magnification never gets the chance to read as grain.
 *
 * sp7.gif IS DELIBERATELY NOT IN HERE. Measured, these are 360x360..720x720 with
 * palettes as thin as 8 and 16 colours, and a fullscreen cover plus
 * `.gg-spiral { scale: 1.6 }` magnifies them 4.3x (sp2/sp4) to 8.5x (sp7) — so
 * sp7 is the worst case by a wide margin. The rest are softened enough by
 * `.gg-spiral { filter: blur(1.1px) }` in fx.css, which is why that blur must
 * STAY for this path even though the woven path cancels it. The FILE stays on
 * disk — DtRH owns it and still uses it.
 */
export const SPIRAL_DIR = '/dtrh/assets/bubbles/effects/spirals/';
export const SPIRAL_POOL = Object.freeze([
  'sp1.gif', 'sp2.webp', 'sp3.gif', 'sp4.webp', 'sp5.gif', 'sp6.gif',
]);
/** The one still that ships outside the pool — the floor if the pool is ever empty. */
export const SPIRAL_FALLBACK = '/dtrh/assets/bubbles/effects/spiral.png';

/** A random spiral URL. Shared with exec/bubbles.js (a popped spiral bubble flicks one). */
export function pickSpiralUrl() {
  if (!SPIRAL_POOL.length) return SPIRAL_FALLBACK;
  return SPIRAL_DIR + SPIRAL_POOL[(Math.random() * SPIRAL_POOL.length) | 0];
}

const clamp01 = (n) => (typeof n === 'number' && n === n ? (n < 0 ? 0 : n > 1 ? 1 : n) : 0);
const lerp = (a, b, t) => a + (b - a) * clamp01(t);
const soon = (fn, ms) => {
  const t = setTimeout(fn, Math.max(0, ms | 0));
  if (t && typeof t.unref === 'function') t.unref();
  return t;
};
const reducedMotion = () => {
  try { return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch (_e) { return false; }
};

/** Cue intensity -> the one dial. The band is DtRH's scaleD(0.25, 0.70, strength). */
export function spiralTuning(intensity, calm) {
  const i = clamp01(intensity);
  return {
    opacity: +lerp(0.25, 0.70, i).toFixed(3),
    fadeMs: calm ? 900 : 450,
  };
}

export function createSpiral({ layers, media, audio, logger } = {}) {
  const log = logger || null;
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:spiral] ${m}`); };
  const sfx = (id) => { try { if (audio && typeof audio.sfx === 'function') audio.sfx(id); } catch (_e) { /* stub */ } };
  const calm = reducedMotion();

  let paneEl = null;
  let elementIntensity = null;    // null = element not running
  let payloadIntensity = null;    // null = no payload running

  // --- the woven path's state (all null on the raster fallback) -------------
  let canvasEl = null;
  let field = null;
  let params = null;
  let rafId = 0;
  let phase = 0;
  let lastTs = 0;
  let onLost = null;
  let onResize = null;
  // Bumped on every weave. A frame callback queued by a weave that has since
  // been torn down carries a stale token and returns without re-arming itself —
  // without this, the pane's own fade-out could hand its rAF chain to the NEXT
  // pane and leave two render loops driving one canvas.
  let weaveGen = 0;

  const layer = () => (layers && typeof layers.get === 'function' ? layers.get('spiral') : null);
  const doc = () => (typeof document !== 'undefined' ? document : null);
  const hasRaf = () => typeof requestAnimationFrame === 'function' && typeof cancelAnimationFrame === 'function';
  /** The heat armor parks decoration through CSS; a canvas has to check itself. */
  const parked = () => {
    const d = doc();
    try { return !!(d && d.documentElement && d.documentElement.getAttribute('data-gg-fx') === 'hot'); }
    catch (_e) { return false; }
  };

  function ensurePane() {
    if (paneEl && paneEl.isConnected) return true;
    const host = layer();
    if (!host || typeof document === 'undefined') return false;
    paneEl = document.createElement('div');
    // .gg-deco so the heat armor can park the spin when the stack goes hot.
    // (The woven path cancels the keyframe and parks itself — see step().)
    paneEl.className = calm ? 'gg-spiral' : 'gg-spiral gg-deco';
    repick();
    host.appendChild(paneEl);
    weave();          // AFTER the append: the canvas needs a laid-out parent to size against.
    return true;
  }

  /** Backing-store size for the pane, at the display's real pixel ratio. */
  function backingSize() {
    const w = (paneEl && paneEl.clientWidth) || (typeof innerWidth === 'number' ? innerWidth : 0);
    const h = (paneEl && paneEl.clientHeight) || (typeof innerHeight === 'number' ? innerHeight : 0);
    // Capped at 2: past that the fill cost doubles again for detail nobody can
    // see through a screen blend at 0.7.
    const dpr = Math.min(2, (typeof devicePixelRatio === 'number' && devicePixelRatio > 0) ? devicePixelRatio : 1);
    return { w: Math.max(1, Math.round(w * dpr)), h: Math.max(1, Math.round(h * dpr)) };
  }

  function sizeCanvas() {
    if (!canvasEl) return false;
    const { w, h } = backingSize();
    if (canvasEl.width === w && canvasEl.height === h) return false;
    canvasEl.width = w;
    canvasEl.height = h;
    return true;
  }

  /**
   * Try to upgrade the pane to the live Loom field. Every failure here is silent
   * and lands on the raster bed, which is already mounted and already correct.
   */
  function weave() {
    if (field || !paneEl) return;
    const d = doc();
    if (!d || typeof d.createElement !== 'function') return;

    let cv = null;
    try { cv = d.createElement('canvas'); } catch (_e) { return; }
    if (!cv || typeof cv.getContext !== 'function' || !cv.style) return;

    canvasEl = cv;
    sizeCanvas();
    const f = createSpiralField(cv);
    if (!f) { canvasEl = null; return; }
    field = f;
    params = pickSpiralParams();
    const gen = ++weaveGen;

    // fx.css is not ours to edit and must keep serving the raster fallback, so
    // the woven pane opts out of the raster-only treatments INLINE:
    //   scale 1.6  -> the shader oversizes to the corners itself, and scaling a
    //                 canvas up 1.6x would throw away the resolution we came for
    //   blur 1.1px -> that blur exists to hide dither; there is none here
    //   animation  -> the shader owns the rotation (and its loop is seamless)
    try {
      cv.style.setProperty('position', 'absolute');
      cv.style.setProperty('inset', '0');
      cv.style.setProperty('width', '100%');
      cv.style.setProperty('height', '100%');
      cv.style.setProperty('display', 'block');
      paneEl.style.setProperty('scale', '1');
      paneEl.style.setProperty('filter', 'none');
      paneEl.style.setProperty('animation', 'none');
      paneEl.appendChild(cv);
    } catch (_e) { unweave(); return; }

    // A GPU reset kills the context without killing the page; when that happens
    // we put every borrowed property back and the raster bed simply resumes.
    if (typeof cv.addEventListener === 'function') {
      onLost = (e) => { try { e.preventDefault(); } catch (_e2) { /* ignore */ } warn('webgl context lost — falling back to the raster pool'); unweave(); };
      try { cv.addEventListener('webglcontextlost', onLost); } catch (_e) { /* ignore */ }
    }
    if (typeof addEventListener === 'function') {
      onResize = () => { if (sizeCanvas() && field && params) { try { field.render(params, phase); } catch (_e) { /* ignore */ } } };
      try { addEventListener('resize', onResize); } catch (_e) { onResize = null; }
    }

    phase = Math.random();      // never start every match on the same frame
    lastTs = 0;
    draw();
    // Reduced motion gets ONE still frame — and unlike a GIF, which ignores the
    // preference entirely, this actually honours it.
    if (!calm && hasRaf()) rafId = requestAnimationFrame((ts) => step(ts, gen));
  }

  function draw() {
    if (!field || !params) return;
    try { field.render(params, phase); }
    catch (e) { warn(`field render threw: ${e && e.message}`); unweave(); }
  }

  function step(ts, gen) {
    if (gen !== weaveGen) return;      // a torn-down weave's last frame
    rafId = 0;
    if (!field || !paneEl) return;
    const now = typeof ts === 'number' ? ts : 0;
    // Parked at data-gg-fx="hot": hold the frame instead of advancing it, the
    // same thing --gg-deco-play does to every keyframe in fx.css.
    if (!parked()) {
      const loop = Math.max(1, loopMsFor(params));
      if (lastTs) phase = (phase + (now - lastTs) / loop) % 1;
      draw();
    }
    lastTs = now;
    if (hasRaf()) rafId = requestAnimationFrame((t) => step(t, gen));
  }

  /**
   * Lift the live weave OUT of module state and hand back its disposer. The
   * caller decides when the GPU resources actually go — teardown holds them
   * until the pane has finished fading, because releasing them early would
   * swap a crisp spiral for a magnified raster one mid-fade, in full view.
   */
  function detachWoven() {
    const w = { cv: canvasEl, f: field, raf: rafId, lost: onLost, resize: onResize };
    weaveGen++;                        // every queued frame for this weave is now stale
    canvasEl = null; field = null; params = null;
    rafId = 0; lastTs = 0; onLost = null; onResize = null;
    return () => {
      if (w.raf && typeof cancelAnimationFrame === 'function') {
        try { cancelAnimationFrame(w.raf); } catch (_e) { /* ignore */ }
      }
      if (w.resize && typeof removeEventListener === 'function') {
        try { removeEventListener('resize', w.resize); } catch (_e) { /* ignore */ }
      }
      if (w.cv && w.lost && typeof w.cv.removeEventListener === 'function') {
        try { w.cv.removeEventListener('webglcontextlost', w.lost); } catch (_e) { /* ignore */ }
      }
      // dispose() force-loses the context. Panes come and go all match and a
      // page only gets a handful of live WebGL contexts, so this is not optional.
      if (w.f) { try { w.f.dispose(); } catch (_e) { /* ignore */ } }
      if (w.cv) { try { w.cv.remove(); } catch (_e) { /* ignore */ } }
    };
  }

  /** Drop the weave NOW and give the surviving pane back to the raster bed. */
  function unweave() {
    detachWoven()();
    // Hand the raster treatments back exactly as fx.css declares them.
    if (paneEl && paneEl.style) {
      try {
        paneEl.style.setProperty('scale', '');
        paneEl.style.setProperty('filter', '');
        paneEl.style.setProperty('animation', '');
      } catch (_e) { /* ignore */ }
    }
  }

  /**
   * Draw a different spiral. BOTH beds are repicked every time: the woven one
   * takes a new preset, and the raster background-image is refreshed too so the
   * fallback underneath is never stale if the context dies mid-match.
   */
  function repick() {
    if (!paneEl || !paneEl.style) return;
    try { paneEl.style.setProperty('background-image', `url('${pickSpiralUrl()}')`); }
    catch (_e) { /* a host without inline style support just keeps the CSS default */ }
    if (field) {
      params = pickSpiralParams();
      draw();
    }
  }

  function teardown() {
    if (!paneEl) return;
    const el = paneEl;
    // The canvas KEEPS ITS LAST FRAME on screen for the whole fade — see
    // detachWoven(). Releasing it here instead would uncover the raster
    // background underneath and the bed would visibly jump from crisp to
    // magnified for 900ms on the way out. Detaching from module state right away
    // also means a pane that comes back inside those 900ms weaves a fresh one.
    const dispose = detachWoven();
    el.classList.remove('is-on');
    soon(() => {
      dispose();
      try { el.remove(); } catch (_e) { /* ignore */ }
    }, 900);
    paneEl = null;
  }

  /** The payload outranks the element; whichever is louder wins the dial. */
  function apply() {
    const want = (payloadIntensity === null && elementIntensity === null)
      ? null
      : Math.max(payloadIntensity === null ? 0 : payloadIntensity, elementIntensity === null ? 0 : elementIntensity);

    if (want === null) { teardown(); return; }
    if (!ensurePane()) return;

    const tune = spiralTuning(want, calm);
    paneEl.style.setProperty('--gg-spiral-op', String(tune.opacity));
    paneEl.style.setProperty('--gg-spiral-fade', `${tune.fadeMs}ms`);
    soon(() => { if (paneEl) paneEl.classList.add('is-on'); }, 16);
  }

  return {
    name: 'spiral',

    start(cue) {
      const fresh = elementIntensity === null;
      elementIntensity = clamp01(cue && cue.intensity);
      apply();
      if (fresh) { repick(); sfx('spiral-in'); }   // a new bed gets a new spiral; a re-tune keeps its own
    },

    setIntensity(v) {
      if (elementIntensity === null) return;
      elementIntensity = clamp01(v);
      apply();
    },

    stop() {
      elementIntensity = null;
      apply();
      sfx('spiral-out');
    },

    /** Spiral payload: ride the pane up for duration_ms, then hand it back. */
    renderPayload(payload, done) {
      const p = payload || {};
      const runMs = Math.max(1000, (p.duration_ms | 0) || 12000);
      payloadIntensity = Math.max(0.5, clamp01(p.intensity !== undefined ? p.intensity : 0.75));
      apply();
      repick();

      let finished = false;
      let endTimer = 0;
      const settle = (endured) => {
        if (finished) return;
        finished = true;
        try { clearTimeout(endTimer); } catch (_e) { /* ignore */ }
        payloadIntensity = null;
        apply();
        if (typeof done === 'function') { try { done(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
      };
      endTimer = soon(() => settle(true), runMs);
      return () => settle(false);
    },
  };
}

export default createSpiral;
