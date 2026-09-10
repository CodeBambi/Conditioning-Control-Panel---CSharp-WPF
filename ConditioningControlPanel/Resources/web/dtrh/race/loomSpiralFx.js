/* ============================================================================
 * race/loomSpiralFx.js - THE LIVE LOOM ON THE ROAD (race page only).
 *
 *   createLoomSpiralFx({ reducedMotion, log }) -> { mount, drop, cancel, dispose, ... }
 *
 * WHAT THIS REPLACES. A spiral pop in Racing Thoughts painted one of seven stock
 * gifs out of assets/bubbles/effects/spirals/ as the background of payloadFx's
 * `.sf-pfx-spiral` hold - 2-5 MB each, the same seven pictures every run, and
 * nothing to do with the room the kart was in. The owner's law (2026-08-25):
 * "on ALL the games the spirals should be generated with the Loom." So the hold
 * gets a CANVAS instead, and shared/loomField.js draws the spiral live off the
 * params race/loomBook.js wove for this seed and this room.
 *
 * THE PATTERN IS THE ARCADEMY'S, not a new one: arcademy/engine/loomWash.js has
 * done exactly this inside the spiral wash element since 2026-08-25 (one canvas
 * child, small backing store, rAF phased by loopMs2, still frame under reduced
 * motion, gif floor on context loss). This is that manager with the race's own
 * holds under it - two of them at most (`spiral` and `gifwash`), each with its
 * own field renderer.
 *
 * ONE DIFFERENCE FROM THE WASH, AND IT IS THE POINT. The arcademy mounts the GL
 * canvas itself, so its spirals can never wear a centrepiece. The race draws
 * through loomField's own `composeFrame` instead - GL field -> 2D blit ->
 * centrepiece - because the book puts THE ROAD'S OWN WORD in the middle of the
 * spiral when the voice has just said one. The cost is one drawImage of a
 * <=512px surface per frame, which is the same pipeline (and a smaller surface)
 * than the Loom studio's own live preview.
 *
 * THE SEAM. game/payloadFx.js takes an optional `spiralFx` exactly the way it
 * takes `subliminalFx` (#991), defaulting to null - so the Descent, which
 * passes nothing, still paints the same gif background it always has and
 * dtrh.html is byte-for-byte the game it was. race/run.js is the only caller.
 *
 * LAYERING CONTRACT (the arcademy law, kept). The canvas gets NO opacity and NO
 * blend mode of its own: it inherits the HOLD's, so payloadFx's fade stays the
 * one intensity channel and race.css's `#race-root .sf-pfx-spiral { filter:
 * opacity(0.78) }`, THE MIX's `data-ov` crossfade and styles.css's
 * `mix-blend-mode: screen` all land on this layer for free. The spiral hold's
 * own 14 s CSS spin and its 1.6 overscan are neutralised with INLINE styles
 * while a canvas is mounted (the shader owns rotation; a CSS spin on top of it
 * is a second, wrong one) and restored to '' on unmount, so the gif path draws
 * exactly as before the moment it takes the element back.
 *
 * PERF (measured knobs, all in here):
 *   - ONE WebGL context and ONE compiled shader for the manager's whole life
 *     (2026-09-09, the owner: "the spirals make the phone version laggy"). The
 *     first cut built a fresh context per hold - a shader compile + program link
 *     on every pop, which on a phone is the frame-long stall the player felt as
 *     lag. Now every hold is a 2D view over the same field surface; a hold costs
 *     a canvas element and nothing else. The context is torn down on dispose
 *     (the run's end) and on loss, never between pops.
 *   - backing store 512 long side on desktop, 256 on the mobile tier / a coarse
 *     pointer, CSS-upscaled to the viewport. The field is analytically
 *     antialiased (u_px in loomField's shader), so it stays crisp anyway - this
 *     is the whole reason a canvas beats a 5 MB gif on a phone.
 *   - 24 fps cap under touch, paced through a timeout so the compositor idles
 *     between paints (a bare rAF re-arm keeps a display-rate heartbeat alive).
 *   - layer2 off and wobble flattened under touch: two uniforms the phone does
 *     not owe. The params are COPIED first - the book's id means "this spiral
 *     as woven", and must not shift because a phone drew it.
 *   - the loop stops on document.hidden, and each hold unmounts itself when its
 *     fade is over (payloadFx hands the hold's duration in), so a spiral that
 *     is not on the glass costs no GPU at all.
 *   - REDUCED MOTION draws exactly one frame and never arms the loop.
 *
 * THE FLOOR. mount() returns false when WebGL is missing or was lost, and a
 * context lost mid-hold tears the canvas down and calls back - payloadFx then
 * paints the wrapper's `href` (a bundled gif) so the screen is never bare.
 * ==========================================================================*/

import { createFieldRenderer, normalizeParams2, loopMs2, composeFrame } from '../shared/loomField.js';
import { Q } from '../shared/quality.js';

/** Backing-store long side, by tier. A phone draws a quarter of the desktop's pixels. */
export const BACK_LONG = 512;
export const BACK_LONG_TOUCH = 256;
/** 24 fps under touch; the desktop runs at display rate. */
export const TOUCH_FRAME_MS = 42;
/** payloadFx's `.sf-pfx-layer` fade is 0.45 s; the canvas outlives the hold by that plus a breath. */
export const FADE_MS = 700;

/**
 * The hold styles a live canvas has to neutralise, per payloadFx class. Every
 * key is restored to '' on unmount, so the element's own CSS takes back over
 * and the gif path is untouched.
 *
 * SPIRAL: styles.css spins the element 14 s per turn and overscans it 1.6x so
 * the rotation never bares a corner. The shader rotates the field itself and
 * the canvas is inset:0 - both would be wrong, and the overscan would throw
 * away a third of the pixels the backing store just paid for.
 * GIFWASH: only the background. Its `is-washing` shudder is a transform on the
 * element and reads just as well over a canvas, so it stays.
 */
const OVERRIDES = {
  spiral: { animation: 'none', scale: '1', transform: 'none', backgroundImage: 'none' },
  gifwash: { backgroundImage: 'none' },
};
const DEFAULT_OVERRIDE = { backgroundImage: 'none' };

/** The mobile tier's own stamp, with a coarse pointer as the belt to it. */
export function isTouchTier() {
  try { if (Q && Q.tier === 'mobile') return true; } catch (e) { /* fall through */ }
  try { if (typeof matchMedia === 'function') return !!matchMedia('(pointer: coarse)').matches; } catch (e) { /* fall through */ }
  return false;
}

/** The backing store a viewport of w x h wants at this tier: `long` on the long side. */
export function backingFor(w, h, touch) {
  const long = touch ? BACK_LONG_TOUCH : BACK_LONG;
  if (!w || !h) return { w: long, h: long };
  return w >= h
    ? { w: long, h: Math.max(64, Math.round((long * h) / w)) }
    : { w: Math.max(64, Math.round((long * w) / h)), h: long };
}

/**
 * One manager for the whole run. `mount` is called by game/payloadFx.js with
 * the hold element and the wrapper race/loomBook.js (or the player's own saved
 * sidecar) produced.
 *
 * @param {Object} o { reducedMotion?: bool, log?: (msg)=>void }
 */
export function createLoomSpiralFx({ reducedMotion = false, log = null } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  /** el -> { el, view, ctx, q, loop, kind, timer, id, onLost } - the 2D view over the shared field */
  const live = new Map();
  /** THE ONE FIELD. A GL canvas (never in the DOM) and loomField's renderer over it, built on the
   *  first mount and kept until dispose or a context loss: every hold draws off this surface. */
  let glc = null, field = null, onContextLost = null;
  let touch = isTouchTier();
  let lost = false;          // WebGL refused or a context was lost - latched for the run
  let disposed = false;
  let rafId = 0, toId = 0, lastAt = 0;
  let visWired = false, resizeWired = false;
  const stats = { mounts: 0, drops: 0, floors: 0, frames: 0 };

  const raf = (fn) => { try { return typeof requestAnimationFrame === 'function' ? requestAnimationFrame(fn) : 0; } catch (e) { return 0; } };
  const caf = (id) => { try { if (id && typeof cancelAnimationFrame === 'function') cancelAnimationFrame(id); } catch (e) { /* ignore */ } };
  const hidden = () => { try { return typeof document !== 'undefined' && document.hidden === true; } catch (e) { return false; } };

  function viewport() {
    let w = 0, h = 0;
    try { w = Number(window.innerWidth) || 0; h = Number(window.innerHeight) || 0; } catch (e) { /* none */ }
    return backingFor(w, h, touch);
  }
  function sizeRec(rec) {
    const b = viewport();
    if (glc && (glc.width !== b.w || glc.height !== b.h)) { glc.width = b.w; glc.height = b.h; }
    if (rec.view.width !== b.w || rec.view.height !== b.h) {
      rec.view.width = b.w; rec.view.height = b.h;
      return true;
    }
    return false;
  }
  function onResize() {
    if (disposed) return;
    for (const rec of live.values()) { if (sizeRec(rec) && reducedMotion) drawOne(rec, 0); }
  }
  function onVisibility() { if (disposed) return; if (hidden()) halt(); else arm(); }

  /** Under touch, the second layer and the wobble come off. The caller's params
   *  are never mutated: the id was hashed off the spiral AS WOVEN. */
  function tuneFor(params) {
    const base = normalizeParams2(params);
    if (!touch) return base;
    const t = normalizeParams2(base);     // a fresh deep copy through the normalizer
    t.layer2.enabled = false;
    t.wobble.amp = 0;
    return t;
  }

  function halt() { caf(rafId); rafId = 0; if (toId) { clearTimeout(toId); toId = 0; } }
  function arm() {
    // reduced motion draws its one frame at mount and never loops
    if (disposed || lost || reducedMotion || rafId || toId || !live.size || hidden()) return;
    rafId = raf(frame);
  }
  function drawOne(rec, phase) {
    try { composeFrame(rec.ctx, field, rec.q, phase, rec.view.width, rec.view.height); stats.frames++; return true; }
    catch (e) { say('race loom render threw (' + ((e && e.message) || e) + ') - the gif takes the layer'); fail(rec); return false; }
  }
  function frame(now) {
    rafId = 0;
    if (disposed || lost || !live.size || hidden()) return;
    const t = Number.isFinite(now) ? now : Date.now();
    if (touch && t - lastAt < TOUCH_FRAME_MS - 1) {
      toId = setTimeout(() => { toId = 0; rafId = raf(frame); }, TOUCH_FRAME_MS);
      return;
    }
    lastAt = t;
    for (const rec of [...live.values()]) { if (!drawOne(rec, (t % rec.loop) / rec.loop)) return; }
    rafId = raf(frame);
  }

  /** Context lost, or a render threw: latch, tear EVERY hold down (they all drew off the one
   *  surface), tell each caller. `rec` is the hold that saw it first, or null from the context. */
  function fail(rec) {
    lost = true;
    const recs = rec ? [rec, ...[...live.values()].filter((r) => r !== rec)] : [...live.values()];
    for (const r of recs) {
      const back = r.onLost;
      dropRec(r);
      stats.floors++;
      if (typeof back === 'function') { try { back(); } catch (e) { /* ignore */ } }
    }
    dropField();
  }

  function applyOverrides(el, kind) {
    const map = OVERRIDES[kind] || DEFAULT_OVERRIDE;
    for (const k of Object.keys(map)) { try { el.style[k] = map[k]; } catch (e) { /* ignore */ } }
  }
  function restoreOverrides(el, kind) {
    const map = OVERRIDES[kind] || DEFAULT_OVERRIDE;
    for (const k of Object.keys(map)) { try { el.style[k] = ''; } catch (e) { /* ignore */ } }
  }

  /** Take one hold's view off: remove the node, give the element back. The field stays for the next pop. */
  function dropRec(rec) {
    if (!rec || !live.has(rec.el)) return;
    if (rec.timer) { clearTimeout(rec.timer); rec.timer = 0; }
    live.delete(rec.el);
    try { rec.view.remove(); } catch (e) { /* ignore */ }
    restoreOverrides(rec.el, rec.kind);
    stats.drops++;
    if (!live.size) halt();
  }

  /** The shared field: built once, or null (and `lost` latched) if WebGL will not have us. */
  function ensureField() {
    if (field) return field;
    if (lost) return null;
    const gl = document.createElement('canvas');     // the field's own surface, never in the DOM
    const b = viewport();
    gl.width = b.w; gl.height = b.h;
    let f = null;
    try { f = createFieldRenderer(gl); } catch (e) { say('race loom shader failed (' + ((e && e.message) || e) + ') - the gif takes the layer'); f = null; }
    if (!f) { lost = true; return null; }
    onContextLost = (ev) => {
      try { if (ev && typeof ev.preventDefault === 'function') ev.preventDefault(); } catch (e) { /* ignore */ }
      say('race loom: webgl context lost - the gif takes the layer');
      fail(null);
    };
    gl.addEventListener('webglcontextlost', onContextLost, false);
    glc = gl; field = f;
    return field;
  }
  /** Free the one context: on dispose, and on loss (where it is already gone). */
  function dropField() {
    if (!glc) return;
    try {
      const g = field && field.gl;
      const ext = g && g.getExtension ? g.getExtension('WEBGL_lose_context') : null;
      if (ext && typeof ext.loseContext === 'function') ext.loseContext();
    } catch (e) { /* ignore */ }
    try { glc.removeEventListener('webglcontextlost', onContextLost); } catch (e) { /* ignore */ }
    glc = null; field = null; onContextLost = null;
  }

  /** Build the 2D view for one hold, or null if the page has no 2D canvas. */
  function makeRec(el, kind) {
    const view = document.createElement('canvas');
    view.className = 'rh-loom-spiral';
    const s = view.style;
    s.position = 'absolute'; s.left = '0'; s.top = '0';
    s.width = '100%'; s.height = '100%';
    s.display = 'block'; s.pointerEvents = 'none';
    const rec = { el, view, ctx: null, kind, q: null, loop: 3600, timer: 0, id: '', onLost: null };
    sizeRec(rec);
    rec.ctx = view.getContext('2d');
    if (!rec.ctx) return null;
    return rec;
  }

  const api = {
    /** False once WebGL has refused or been lost: payloadFx stops asking. */
    supported() { return !disposed && !lost; },

    /**
     * Put a live Loom spiral on this hold.
     * @param {Element} el   the payloadFx hold (`.sf-pfx-spiral` / `.sf-pfx-gifwash`)
     * @param {Object} wrap  { loom:true, params, id?, href? }
     * @param {Object} [opt] { kind?: 'spiral'|'gifwash', durMs?: how long the hold is up,
     *                         onLost?: () => void (paint the gif floor) }
     * @returns {boolean} true = the canvas has the element; false = paint the gif.
     */
    mount(el, wrap, opt = {}) {
      if (disposed || lost) return false;
      if (!el || !wrap || !wrap.params) return false;
      if (typeof document === 'undefined' || !document.createElement) return false;
      const kind = String(opt.kind || 'spiral');
      const durMs = Number(opt.durMs) > 0 ? Number(opt.durMs) : 3000;
      touch = isTouchTier();
      let rec = live.get(el);
      if (rec && rec.kind !== kind) { dropRec(rec); rec = null; }
      if (!rec) {
        try {
          if (!ensureField()) { stats.floors++; return false; }
          rec = makeRec(el, kind);
          if (!rec) { lost = true; stats.floors++; return false; }
          el.appendChild(rec.view);
          live.set(el, rec);
          if (!resizeWired) { try { window.addEventListener('resize', onResize); resizeWired = true; } catch (e) { /* ignore */ } }
          if (!visWired) { try { document.addEventListener('visibilitychange', onVisibility); visWired = true; } catch (e) { /* ignore */ } }
        } catch (e) {
          say('race loom mount threw (' + ((e && e.message) || e) + ') - the gif takes the layer');
          lost = true; stats.floors++;
          return false;
        }
      } else {
        sizeRec(rec);          // a viewport flip between pops
      }
      stats.mounts++;
      rec.onLost = typeof opt.onLost === 'function' ? opt.onLost : null;
      rec.q = tuneFor(wrap.params);
      rec.loop = Math.max(400, loopMs2(rec.q));
      rec.id = String(wrap.id || '');
      applyOverrides(el, kind);
      if (!drawOne(rec, 0)) return false;     // the first frame threw and took the hold down
      // THE HOLD OWNS THE CLOCK. payloadFx fades the layer out after durMs; the
      // canvas follows it off the element one fade later, so nothing spins
      // behind an invisible layer and the next pop builds a fresh one.
      if (rec.timer) clearTimeout(rec.timer);
      rec.timer = setTimeout(() => { rec.timer = 0; if (!disposed) dropRec(rec); }, durMs + FADE_MS);
      arm();
      return true;
    },

    /** Give this hold back to the gif path (a url was drawn for it). */
    drop(el) { dropRec(live.get(el)); },

    /** Run end / room arrival: take every canvas off at once (payloadFx cancelHeavy). */
    cancel() { for (const rec of [...live.values()]) dropRec(rec); },

    dispose() {
      if (disposed) return;
      disposed = true;
      for (const rec of [...live.values()]) dropRec(rec);
      dropField();
      halt();
      if (resizeWired) { try { window.removeEventListener('resize', onResize); } catch (e) { /* ignore */ } resizeWired = false; }
      if (visWired) { try { document.removeEventListener('visibilitychange', onVisibility); } catch (e) { /* ignore */ } visWired = false; }
    },

    /** SMOKE / RIG ONLY. Pretend the one context went: the same path a real `webglcontextlost`
     *  takes, so race/smoke/loom-spiral-check.mjs can prove the gif floor without a driver
     *  reset. Returns how many holds were dropped. */
    loseContext() {
      const n = live.size;
      if (!glc) return 0;
      try { glc.dispatchEvent(new Event('webglcontextlost')); }
      catch (e) { fail(null); }
      return n;
    },

    /** race/smoke/loom-spiral-check.mjs + the shot harness: what is on the glass. */
    diagnostics() {
      const holds = [...live.values()].map((r) => ({
        kind: r.kind, id: r.id, loopMs: r.loop,
        backing: r.view.width + 'x' + r.view.height,
        mounted: r.view.parentNode === r.el,
      }));
      return { holds, touch, lost, disposed, still: !!reducedMotion, field: !!field, ...stats };
    },
  };
  return api;
}

export default createLoomSpiralFx;

// self-check: node --check is the bar; every line of this touches the DOM or WebGL.
