/* ============================================================================
 * shared/hypno/loom.js - the Back Room's Loom fields (CONTRACT 10.13.D).
 *
 * Every spiral a station draws comes from the Loom (owner law): the real
 * arcademy/engine/loom/loomField.js, imported, never forked. This module adds
 * only what a game table needs on top of it:
 *
 *   LOOM_PRESETS  the v3 mockup's five fields in Loom schema v2, normalised by
 *                 loomField's own normalizeParams2 and frozen.
 *   phaseForAngle an object's clockwise screen angle -> the loop phase whose
 *                 layer-1 rotation is that angle (loomField's span rule), so a
 *                 hub or a whirlpool turns WITH the wheel under it.
 *   createLoomKit one shared WebGL context per page, whichever station asks.
 *
 * HANDEDNESS (law 3). loomField's field turns clockwise on screen as the
 * phase grows with direction 1, and its arms lead at the rim (the band angle
 * grows with the radius), so it reads as pulling inward. Every preset's layer 1
 * is direction 1; the thin gold layer 2 of `screen` counter-turns by design, as
 * in the mockup. kit-check.mjs measures both on every preset, WebGL and 2D.
 *
 * COST (law 7). One GL canvas for the page, made on the first draw or paint and
 * lost on the last kit's dispose. Its backing store is at most 512 px on the
 * long side (256 for card backs), resized to the aspect asked for (quantised to
 * 0.05). The same preset at the same phase, aspect and backing renders once:
 * thirteen card backs in a frame cost one render. Pass the frame's one `now`
 * to every draw; a draw without it reads a clock held for the current task, so
 * backs drawn in one rAF callback still share a phase. No WebGL, or a lost
 * context: loomField's drawFallbackFrame (loomSpiral.js) into a small 2D store
 * until the browser restores the context, then the field again.
 * ==========================================================================*/

import { createFieldRenderer, normalizeParams2, loopMs2, drawFallbackFrame } from '../../../arcademy/engine/loom/loomField.js';

const TWO_PI = Math.PI * 2;
const FALLBACK_LONG = 256;   // the 2D wedge renderer is CPU work: never more than this

let taskNow = NaN;
/** performance.now(), read once per task (a rAF callback) and held until its microtasks run. */
function taskClock() {
  if (Number.isNaN(taskNow)) {
    taskNow = typeof performance !== 'undefined' ? performance.now() : Date.now();
    queueMicrotask(() => { taskNow = NaN; });
  }
  return taskNow;
}

function deepFreeze(o) {
  if (o && typeof o === 'object' && !Object.isFrozen(o)) {
    Object.freeze(o);
    for (const v of Object.values(o)) deepFreeze(v);
  }
  return o;
}

const layer = (arms, turns, duty, style, direction, colors, bandMode = 'hard') => ({ arms, turns, duty, style, direction, colors, bandMode });

/** The mockup's presets (hypno-spins-v3.html, Loom.P), in the app's schema v2. */
export const LOOM_PRESETS = deepFreeze({
  backs: normalizeParams2({ layer: layer(4, 2, 0.5, 'log', 1, ['#ff69b4', '#8a5cff']), bg: { kind: 'solid', color: '#14060f' }, glow: 0.25, speed: 2 }),
  hub: normalizeParams2({ layer: layer(3, 1.5, 0.55, 'golden', 1, ['#e8c27a', '#ff5fa2', '#9b6bff']), bg: { kind: 'solid', color: '#1a0f2b' }, glow: 0.35, speed: 3 }),
  whirl: normalizeParams2({ layer: layer(2, 2.5, 0.45, 'ribbon', 1, ['#5fffd0', '#9b6bff']), bg: { kind: 'solid', color: '#1c1230' }, glow: 0.4,
    wobble: { amp: 0.12, freq: 3, cycles: 1 } }),
  wake: normalizeParams2({ layer: layer(4, 3, 0.5, 'log', 1, ['#5fffd0', '#3a1f5c']), bg: { kind: 'solid', color: '#0a0614' }, glow: 0.45,
    wobble: { amp: 0.15, freq: 2, cycles: 1 }, speed: 2 }),
  screen: normalizeParams2({ layer: layer(6, 2.5, 0.5, 'log', 1, ['#ff5fa2', '#9b6bff', '#5fffd0'], 'gradient'),
    layer2: { enabled: true, ...layer(3, 1.5, 0.3, 'log', -1, ['#e8c27a']) },
    bg: { kind: 'radial', color: '#14060f', outer: '#08040e' }, glow: 0.5, pulse: { amp: 0.08, cycles: 1 }, speed: 1 }),
});

/** Backing store long sides: `long` for fields, `small` for card backs. */
export const LOOM_BACKING = Object.freeze({ long: 512, small: 256 });

/** loomField's symmetrySpanRad times speedMul: the rotation one whole loop turns a layer by. */
export function spanRad(l) {
  const n = Math.max(1, Math.min(l.colors.length, 6));
  return (TWO_PI / l.arms) * (l.arms % n === 0 ? n : l.arms) * (l.speedMul || 1);
}

/** Clockwise screen radians -> phase 0..1 whose layer-1 rotation is `rad` (modulo the layer's symmetry). */
export function phaseForAngle(presetName, rad) {
  const q = LOOM_PRESETS[presetName];
  if (!q || !Number.isFinite(rad)) return 0;
  const p = rad / (q.layer.direction * spanRad(q.layer));
  const f = p - Math.floor(p);
  return f < 1 ? f : 0;
}

/** Loop phase at clock `now` (ms): (now % loopMs2) / loopMs2. */
export function phaseAt(presetName, now) {
  const q = LOOM_PRESETS[presetName];
  if (!q || !Number.isFinite(now)) return 0;
  const loop = loopMs2(q);
  return (((now % loop) + loop) % loop) / loop;
}

/** The backing store for a w x h draw at `long`: the aspect quantised to 0.05, the long side `long` (<= 512). */
export function backingFor(w, h, long) {
  const L = Math.max(16, Math.min(LOOM_BACKING.long, Math.round(Number(long) || LOOM_BACKING.long)));
  const a = Math.max(0.05, Math.round((w > 0 && h > 0 ? w / h : 1) / 0.05) * 0.05);
  return a >= 1 ? { w: L, h: Math.max(8, Math.round(L / a)) } : { w: Math.max(8, Math.round(L * a)), h: L };
}

const longOf = (backing) => (backing === 'small' ? LOOM_BACKING.small
  : Number.isFinite(backing) ? Math.min(LOOM_BACKING.long, backing) : LOOM_BACKING.long);

/* ---- the one field per page -------------------------------------------- */

let shared = null;   // { canvas, field, lost, users:Set, key, renders, onLost, onRestored }

function acquire(kit, say) {
  if (shared) { shared.users.add(kit); return shared; }
  if (typeof document === 'undefined' || !document.createElement) return null;
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = LOOM_BACKING.small;
  let field = null;
  try { field = createFieldRenderer(canvas); } catch (e) { say('loom shader failed (' + ((e && e.message) || e) + '), 2D fallback'); field = null; }
  const s = { canvas, field, lost: !field, users: new Set([kit]), key: '', renders: 0, onLost: null, onRestored: null };
  shared = s;
  if (!field) { say('loom: no WebGL, 2D fallback'); return s; }
  s.onLost = (ev) => {
    try { ev.preventDefault(); } catch (e) { /* noop */ }   // without it the browser never restores
    s.lost = true; s.key = '';
    say('loom: webgl context lost, 2D fallback');
  };
  s.onRestored = () => {
    if (shared !== s) return;
    let f = null;
    try { f = createFieldRenderer(canvas); } catch (e) { f = null; }   // programs and buffers died with the old context
    if (!f) { say('loom: webgl context restored but the field would not rebuild, 2D fallback'); return; }
    s.field = f; s.lost = false; s.key = '';
    say('loom: webgl context restored');
  };
  canvas.addEventListener('webglcontextlost', s.onLost, false);
  canvas.addEventListener('webglcontextrestored', s.onRestored, false);
  return s;
}

function release(kit) {
  const s = shared;
  if (!s || !s.users.delete(kit) || s.users.size) return;
  shared = null;
  try {
    if (s.onLost) s.canvas.removeEventListener('webglcontextlost', s.onLost);
    if (s.onRestored) s.canvas.removeEventListener('webglcontextrestored', s.onRestored);
  } catch (e) { /* noop */ }
  try {
    const gl = s.field && s.field.gl;
    const ext = gl && gl.getExtension ? gl.getExtension('WEBGL_lose_context') : null;
    if (ext) ext.loseContext();
  } catch (e) { /* noop */ }
}

/**
 * @param {{still?: boolean, log?: (msg: string) => void}} [o]
 * @returns {{webgl: boolean, draw: Function, paint: Function, setStill: Function, dispose: Function, debug: Function}}
 */
export function createLoomKit({ still = false, log = null } = {}) {
  const say = (m) => { if (typeof log === 'function') { try { log(m); } catch (e) { /* noop */ } } };
  let isStill = !!still, disposed = false, fb = null, fbKey = '';
  const held = new Map();   // preset -> the last angle it was given, held while still
  const stats = { draws: 0, fallbacks: 0 };
  const kit = {};

  const ensure = () => (disposed ? null : acquire(kit, say));

  function phaseOf(name, o) {
    if (isStill) return held.has(name) ? phaseForAngle(name, held.get(name)) : 0;
    if (Number.isFinite(o.angle)) { held.set(name, o.angle); return phaseForAngle(name, o.angle); }
    return phaseAt(name, Number.isFinite(o.now) ? o.now : taskClock());
  }

  /** The surface holding `name` at `phase` for a w x h draw, rendered only when it is not already there. */
  function surface(name, phase, w, h, long) {
    const s = ensure();
    if (!s) return null;
    const q = LOOM_PRESETS[name];
    if (s.field && !s.lost) {
      const b = backingFor(w, h, long);
      const key = name + '|' + phase.toFixed(5) + '|' + b.w + 'x' + b.h;
      if (s.key !== key) {
        if (s.canvas.width !== b.w || s.canvas.height !== b.h) { s.canvas.width = b.w; s.canvas.height = b.h; }
        try { s.field.render(q, phase); s.renders++; s.key = key; }
        catch (e) { say('loom render threw (' + ((e && e.message) || e) + '), 2D fallback'); s.lost = true; s.key = ''; }
      }
      if (!s.lost) return s.canvas;
    }
    const b = backingFor(w, h, Math.min(long, FALLBACK_LONG));
    const key = name + '|' + phase.toFixed(5) + '|' + b.w + 'x' + b.h;
    if (!fb) fb = document.createElement('canvas');
    if (fbKey !== key) {
      if (fb.width !== b.w || fb.height !== b.h) { fb.width = b.w; fb.height = b.h; }
      const g = fb.getContext('2d');
      if (!g) return null;
      drawFallbackFrame(g, q, phase, b.w, b.h);
      fbKey = key; stats.fallbacks++;
    }
    return fb;
  }

  // A getter, not an Object.assign member: assign would read it once at creation and copy a fixed boolean.
  Object.defineProperty(kit, 'webgl', {
    /** True while the page's one context draws this kit's fields. False until the first draw or paint makes it: reading never does. */
    get() { const s = shared; return !!(!disposed && s && s.users.has(kit) && s.field && !s.lost); },
    enumerable: true,
  });

  Object.assign(kit, {
    /**
     * Draw preset `name` covering x, y, w, h of a 2D context. `angle` (clockwise rad) drives the spin, else `now`
     * (ms). Give every draw of a frame the same `now` so they share one render; without it the task's clock is used.
     */
    draw(ctx2d, name, x, y, w, h, { now, angle, alpha = 1, backing = 'long' } = {}) {
      if (disposed || !ctx2d || !LOOM_PRESETS[name] || !(w > 0 && h > 0)) return false;
      const src = surface(name, phaseOf(name, { now, angle }), w, h, longOf(backing));
      if (!src) return false;
      const a = Math.max(0, Math.min(1, Number.isFinite(alpha) ? alpha : 1));
      if (a <= 0) return true;
      const prev = ctx2d.globalAlpha;
      ctx2d.globalAlpha = prev * a;
      ctx2d.drawImage(src, x, y, w, h);
      ctx2d.globalAlpha = prev;
      stats.draws++;
      return true;
    },

    /** Fill a caller-owned canvas (a CanvasTexture's image) with preset `name`, rendered at no more than 512 px. */
    paint(canvas, name, { now, angle } = {}) {
      if (disposed || !canvas || !LOOM_PRESETS[name] || !(canvas.width > 0 && canvas.height > 0)) return false;
      const long = Math.min(LOOM_BACKING.long, Math.max(canvas.width, canvas.height));
      const src = surface(name, phaseOf(name, { now, angle }), canvas.width, canvas.height, long);
      const g = src && canvas.getContext('2d');
      if (!g) return false;
      g.drawImage(src, 0, 0, canvas.width, canvas.height);
      stats.draws++;
      return true;
    },

    /** Still (reduced motion, Calm): phase 0, or the last angle each preset was given. */
    setStill(on) { isStill = !!on; },

    /** Free this kit; the last one to go loses the page's context. Call it from the station's close. */
    dispose() {
      if (disposed) return;
      disposed = true;
      release(kit);
      if (fb) { fb.width = fb.height = 1; fb = null; }
      held.clear();
    },

    /** Test seam. `renders` is the page's shared count. */
    debug() {
      const s = shared;
      return { renders: s ? s.renders : 0, users: s ? s.users.size : 0, lost: s ? s.lost : null,
        backing: s ? s.canvas.width + 'x' + s.canvas.height : null, still: isStill, disposed, ...stats };
    },
    /** Test seam: the page's GL context, or null. */
    debugGl() { const s = shared; return s && s.field ? s.field.gl || null : null; },
  });
  return kit;
}
