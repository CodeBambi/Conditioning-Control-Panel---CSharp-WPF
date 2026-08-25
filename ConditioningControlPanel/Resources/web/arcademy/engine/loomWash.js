/* ============================================================================
 * engine/loomWash.js - the LIVE Loom canvas inside the spiral wash element.
 *
 * The owner's law (2026-08-25): "on ALL the games the spirals should be
 * generated with the Loom." sustained.js startWash routes here when the class
 * spiral is a PARAMS OBJECT ({loom:true, params, id, href}) instead of a url
 * string: ONE <canvas> is mounted INSIDE the existing wash element (the
 * one-element-per-wash-kind law is untouched - this is a child, not a second
 * element), and the vendored loomField shader draws the spiral live.
 *
 * WHY A CANVAS BEATS THE 150vmax GIF (the phone lesson): today's spiral wash
 * is a 150vmax composited square spinning by CSS. The shader needs no CSS
 * rotation (u_rot is a uniform), so the canvas is viewport-sized with a SMALL
 * backing store - long side 512 desktop / 320 under coarse-touch, CSS-upscaled;
 * the field is analytically antialiased (u_px) so it stays crisp anyway.
 *
 * LAYERING CONTRACT: the canvas gets NO mix-blend-mode and no opacity of its
 * own - it inherits the wash ELEMENT's styling, so the parent's opacity stays
 * the engine's one intensity channel and the existing `.ae-touch .ae-wash-spiral
 * {mix-blend-mode:normal}` law lands on this layer for free. The element's
 * 150vmax spiral sizing + spin are neutralised with INLINE styles while the
 * canvas is mounted (engine/style.js is not touched) and restored to '' on
 * unmount so the CSS conic fallback works again.
 *
 * PERF + LIFECYCLE KNOBS (all in here):
 *   - rAF loop phased by loopMs2 (seamless by construction);
 *   - 30fps frame cap under coarse-touch (html.ae-touch OR pointer:coarse);
 *   - layer2.enabled=false + wobble.amp=0 under touch (uniform cost trim);
 *   - still:true renders ONE frame and no loop (reduced motion's static veil);
 *   - pause on document.hidden (visibilitychange), and setActive(false) when
 *     the wash element's opacity is driven to 0 (sustained.js calls it);
 *   - webglcontextlost -> tear down, latch unsupported, fire onLost (the
 *     caller paints the bundled-gif fallback so the screen never goes bare);
 *   - dispose(): cancel rAF, drop listeners, lose the context via the
 *     WEBGL_lose_context extension when available, remove the canvas, restore
 *     the element's inline styles.
 *
 * FALLBACK FLOOR: mount() returns false when WebGL is unavailable (or was lost
 * before) - the caller then paints the wrapper's `href` gif via the untouched
 * url path and the LEDGER ANSWER is that gif url, because the ledger records
 * what is actually on screen (Law I). drawFallbackFrame (2D) was considered
 * and rejected for the wash: a static 2D frame under-sells against a spinning
 * gif we already ship, and the gif path needs zero new plumbing.
 * ==========================================================================*/

import { createFieldRenderer, normalizeParams2, loopMs2 } from './loom/loomField.js';

const TOUCH_FRAME_MS = 33;     // ~30fps cap under coarse-touch
const BACK_LONG = 512;         // desktop backing-store long side
const BACK_LONG_TOUCH = 320;   // coarse-touch backing-store long side

/** html.ae-touch is the engine's own stamp; matchMedia is the belt to it. */
function isTouch() {
  try {
    if (typeof document !== 'undefined' && document.documentElement
      && document.documentElement.classList
      && document.documentElement.classList.contains('ae-touch')) return true;
  } catch (e) { /* fall through */ }
  try {
    if (typeof matchMedia === 'function') return !!matchMedia('(pointer: coarse)').matches;
  } catch (e) { /* fall through */ }
  return false;
}

/** The inline styles that neutralise .ae-wash-spiral's 150vmax spin for the
 *  canvas path. Keys are style properties; every one is restored to '' on
 *  unmount so the class's own CSS takes back over. */
const EL_OVERRIDES = Object.freeze({
  left: '0', top: '0', right: '0', bottom: '0',
  width: 'auto', height: 'auto', margin: '0',
  animation: 'none', transform: 'none', backgroundImage: 'none',
});

/**
 * One live-loom manager. sustained.js keeps one per wash hold; only the
 * 'spiral' kind ever mounts one.
 * @param {Object} [o] { log?: (msg)=>void }
 */
export function createLoomWash(o = {}) {
  const say = typeof o.log === 'function' ? o.log : () => {};
  let el = null;              // the wash element the canvas lives in
  let canvas = null;
  let field = null;           // createFieldRenderer handle
  let q = null;               // normalized params (touch-trimmed copy)
  let loop = 3600;            // loopMs2(q)
  let still = false;          // reduced motion: one frame, no loop
  let touch = false;
  let rafId = 0;
  let active = false;         // the caller's on/off (wash opacity > 0)
  let disposed = false;
  let lost = false;           // context lost / WebGL refused - latched
  let lastAt = 0;
  let onLost = null;
  let visWired = false;
  let resizeWired = false;

  function raf(fn) {
    try { if (typeof requestAnimationFrame === 'function') return requestAnimationFrame(fn); } catch (e) { /* none */ }
    return 0;
  }
  function caf(id) {
    try { if (typeof cancelAnimationFrame === 'function') cancelAnimationFrame(id); } catch (e) { /* ignore */ }
  }

  function hidden() {
    try { return typeof document !== 'undefined' && document.hidden === true; } catch (e) { return false; }
  }
  function onVisibility() {
    if (disposed) return;
    if (hidden()) halt();
    else arm();
  }

  /** Trim the params for the platform: under touch the second layer and the
   *  wobble are uniform cost the phone does not owe. Copy, never mutate the
   *  caller's object - the id was hashed off the ORIGINAL params and must keep
   *  meaning "this spiral as designed". */
  function tuneFor(params) {
    const base = normalizeParams2(params);
    if (!touch) return base;
    const t = normalizeParams2(base);   // fresh deep copy via the normalizer
    t.layer2.enabled = false;
    t.wobble.amp = 0;
    return t;
  }

  function backingFor() {
    const long = touch ? BACK_LONG_TOUCH : BACK_LONG;
    let w = 0; let h = 0;
    try { w = Number(window.innerWidth) || 0; h = Number(window.innerHeight) || 0; } catch (e) { /* none */ }
    if (!w || !h) return { w: long, h: long };
    if (w >= h) return { w: long, h: Math.max(64, Math.round((long * h) / w)) };
    return { w: Math.max(64, Math.round((long * w) / h)), h: long };
  }
  function sizeBacking() {
    if (!canvas) return;
    const b = backingFor();
    if (canvas.width !== b.w) canvas.width = b.w;
    if (canvas.height !== b.h) canvas.height = b.h;
  }
  function onResize() { if (!disposed && canvas) sizeBacking(); }

  function halt() { if (rafId) { caf(rafId); rafId = 0; } }
  function arm() {
    if (disposed || lost || !active || still || hidden() || rafId || !field) return;
    rafId = raf(frame);
  }
  function frame(now) {
    rafId = 0;
    if (disposed || lost || !active || hidden() || !field) return;
    const t = Number.isFinite(now) ? now : Date.now();
    if (touch && t - lastAt < TOUCH_FRAME_MS - 1) { rafId = raf(frame); return; }
    lastAt = t;
    try {
      field.render(q, (t % loop) / loop);
    } catch (e) {
      say('loom wash render threw (' + ((e && e.message) || e) + ') - falling back');
      fail();
      return;
    }
    rafId = raf(frame);
  }
  function renderOnce() {
    if (!field || disposed || lost) return;
    try { field.render(q, 0); } catch (e) { fail(); }
  }

  /** Context lost (or a render threw): tear the canvas down, latch, tell. */
  function fail() {
    lost = true;
    const cb = onLost;
    teardownCanvas();
    restoreEl();
    if (typeof cb === 'function') { try { cb(); } catch (e) { /* ignore */ } }
  }
  function onContextLost(ev) {
    try { if (ev && typeof ev.preventDefault === 'function') ev.preventDefault(); } catch (e) { /* ignore */ }
    say('loom wash: webgl context lost - gif fallback takes the element');
    fail();
  }

  function teardownCanvas() {
    halt();
    if (field && field.gl) {
      try {
        const ext = field.gl.getExtension('WEBGL_lose_context');
        if (ext && typeof ext.loseContext === 'function' && !lost) ext.loseContext();
      } catch (e) { /* ignore */ }
    }
    field = null;
    if (canvas) {
      try { canvas.removeEventListener('webglcontextlost', onContextLost); } catch (e) { /* ignore */ }
      try { canvas.remove(); } catch (e) { /* ignore */ }
    }
    canvas = null;
  }
  function applyElOverrides() {
    if (!el || !el.style) return;
    for (const k of Object.keys(EL_OVERRIDES)) {
      try { el.style[k] = EL_OVERRIDES[k]; } catch (e) { /* ignore */ }
    }
  }
  function restoreEl() {
    if (!el || !el.style) return;
    for (const k of Object.keys(EL_OVERRIDES)) {
      try { el.style[k] = ''; } catch (e) { /* ignore */ }
    }
  }

  const api = {
    /** Latched false after a context loss / refused WebGL. */
    supported() { return !disposed && !lost; },

    /**
     * Mount (or retune) the canvas in `washEl` and start drawing `params`.
     * @returns {boolean} true = live canvas is showing; false = caller must
     *   paint the gif fallback (WebGL unavailable or previously lost).
     */
    mount(washEl, params, opt = {}) {
      if (disposed || lost) return false;
      if (!washEl || typeof document === 'undefined' || !document.createElement) return false;
      onLost = typeof opt.onLost === 'function' ? opt.onLost : onLost;
      still = opt.still === true;
      touch = isTouch();
      if (el && el !== washEl) { teardownCanvas(); restoreEl(); }
      el = washEl;
      q = tuneFor(params);
      loop = Math.max(400, loopMs2(q));
      if (!canvas) {
        try {
          canvas = document.createElement('canvas');
          canvas.className = 'ae-wash-loom';
          const s = canvas.style;
          s.position = 'absolute'; s.left = '0'; s.top = '0';
          s.width = '100%'; s.height = '100%';
          s.display = 'block'; s.pointerEvents = 'none';
          sizeBacking();
          field = createFieldRenderer(canvas);
          if (!field) { canvas = null; lost = true; return false; }
          canvas.addEventListener('webglcontextlost', onContextLost, false);
          el.appendChild(canvas);
          if (!resizeWired) {
            try { window.addEventListener('resize', onResize); resizeWired = true; }
            catch (e) { /* ignore */ }
          }
          if (!visWired) {
            try { document.addEventListener('visibilitychange', onVisibility); visWired = true; }
            catch (e) { /* ignore */ }
          }
        } catch (e) {
          say('loom wash mount threw (' + ((e && e.message) || e) + ')');
          teardownCanvas();
          lost = true;
          return false;
        }
      } else if (canvas.parentNode !== el) {
        try { el.appendChild(canvas); } catch (e) { /* ignore */ }
      }
      applyElOverrides();
      active = true;
      if (still) { halt(); renderOnce(); } else { renderOnce(); arm(); }
      return true;
    },

    /** New params on the SAME element (a mid-class re-pick). */
    retune(params) {
      if (disposed || lost || !field) return false;
      q = tuneFor(params);
      loop = Math.max(400, loopMs2(q));
      if (still) renderOnce();
      return true;
    },

    /** The wash's intensity channel went to 0 (or back up): stop/resume the
     *  loop so an invisible spiral costs no GPU. The canvas stays mounted. */
    setActive(on) {
      const want = !!on;
      if (want === active) return;
      active = want;
      if (!active) halt();
      else if (!still) { lastAt = 0; arm(); }
      else renderOnce();
    },

    dispose() {
      if (disposed) return;
      disposed = true;
      active = false;
      teardownCanvas();
      restoreEl();
      if (resizeWired) {
        try { window.removeEventListener('resize', onResize); } catch (e) { /* ignore */ }
        resizeWired = false;
      }
      if (visWired) {
        try { document.removeEventListener('visibilitychange', onVisibility); } catch (e) { /* ignore */ }
        visWired = false;
      }
      el = null; q = null; onLost = null;
    },

    diagnostics() {
      return {
        live: !!field, active, still, touch, lost, disposed,
        loopMs: loop, backing: canvas ? canvas.width + 'x' + canvas.height : null,
      };
    },
  };
  return api;
}

export default createLoomWash;
