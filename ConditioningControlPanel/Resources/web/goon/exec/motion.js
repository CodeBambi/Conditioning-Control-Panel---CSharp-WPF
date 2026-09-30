/* ============================================================================
 * exec/motion.js - the DOM half of the juice pass: every effect's IN and OUT.
 *
 * exec/tween.js holds the curves (pure, node-tested); this file hands them to
 * the compositor through the Web Animations API. Why WAAPI and not keyframes in
 * fx.css: an entrance must be a FIXED 150-350 ms whatever the effect lasts, and a
 * CSS keyframe's stops are percentages of the whole lifetime. A WAAPI animation
 * sits on top of the CSS one while it runs (script animations sort after CSS
 * animations) and hands the node straight back when it ends, so the effect's own
 * timeline, its animationend teardown and its receipts are untouched.
 *
 * RULES (BRIEF.md "Juice", owner 2026-09-23):
 *   - Nothing appears or vanishes in one frame; things come FROM somewhere.
 *   - Transform and opacity only (the slice shimmer's clip-path is STATIC; only
 *     its transform moves). No white flash: tints come from the theme.
 *   - Photosafe: every animation here plays ONCE; nothing loops.
 *   - Reduced motion (OS or html[data-gg-motion="reduced"]) = opacity fades only,
 *     no travel, no scale, no shake, no particles.
 *   - Lite tier (exec/perfTier.js) = fewer particles, no slices; never broken.
 *   - Motion never owns an effect's life. Exits are scheduled INSIDE the hold
 *     (tween.exitWindow) and a node that died early is simply skipped.
 *
 * Every call is guarded: a node without .animate (the node self-test stub DOM,
 * an ancient engine) gets no motion and no throw.
 * ==========================================================================*/

import { toKeyframes, backOut, cubicOut, cubicIn, quadOut, shakePath, burstVectors, exitWindow, TIMING } from './tween.js';
import { perfLite } from './perfTier.js';

/* ------------------------------------------------------------------- timings */

/** Chosen timings, in one place so the playbook and a tuning pass can find them. */
export const MOTION = Object.freeze({
  FLASH_IN_MS: 300,        // lands from its spawn point, ~12% overshoot
  FLASH_OUT_MS: 200,       // shrinks toward the ground, fades
  PANE_IN_MS: 340,         // a fullscreen wash grows out of the pop point (THUD)
  PANE_OUT_MS: 240,        // ...and is sucked back into it
  PANE_PULSE_MS: 250,      // a second pop of a pane already up (SHIVER)
  SLICE_MS: 300,           // glitch-slice shimmer, one pass, never a loop
  BURST_MS: 560,           // particle burst
  RING_MS: 420,            // shockwave ring
  SHAKE_MS: 220,           // real-pixel shake
  SHAKE_MAX_PX: 7,         // the biggest hit; a normal pop does not shake
  FADE_IN_MS: 220,         // the reduced-motion entrance
  FADE_OUT_MS: 160,        // the reduced-motion exit
  CARD_IN_MS: TIMING.THUD_MS,
  CARD_OUT_MS: 220,
  VIDEO_IN_MS: 320,
  GLOW_IN_MS: 240,
});

/** Theme tints, as CSS colours (never white: the owner found white hit flashes
 *  too bright in Breakout). Kind names follow exec/bubbles.js KINDS. */
export const TINTS = Object.freeze({
  normal: 'rgb(255, 170, 220)',
  flash: 'rgb(255, 196, 150)',
  spiral: 'rgb(110, 225, 255)',
  glitch: 'rgb(140, 255, 170)',
  braindrain: 'rgb(180, 150, 255)',
  pinkfilter: 'rgb(255, 105, 180)',
  video: 'rgb(255, 90, 130)',
});
export const tintFor = (kind) => TINTS[kind] || TINTS.normal;

/* --------------------------------------------------------------------- gates */

/** Reduced motion: the OS setting or the page's own pref (boot.js stamps it). */
export function calm() {
  try {
    if (typeof document !== 'undefined' && document && document.documentElement
        && document.documentElement.getAttribute('data-gg-motion') === 'reduced') return true;
  } catch (_e) { /* fall through */ }
  try { return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch (_e) { return false; }
}

export const canAnimate = (el) => !!el && typeof el.animate === 'function';

/* ------------------------------------------------------------------ plumbing */

const TRACK = '__ggMotion';

/** Run one animation on a node, tracked so cancelMotion() can take it off. */
export function play(el, keyframes, opts) {
  if (!canAnimate(el)) return null;
  let a = null;
  try { a = el.animate(keyframes, Object.assign({ easing: 'linear', fill: 'none' }, opts || {})); }
  catch (_e) { return null; }
  try {
    const list = el[TRACK] || (el[TRACK] = []);
    list.push(a);
    const drop = () => { const i = list.indexOf(a); if (i >= 0) list.splice(i, 1); };
    a.addEventListener('finish', drop, { once: true });
    a.addEventListener('cancel', drop, { once: true });
  } catch (_e) { /* tracking is a nicety */ }
  return a;
}

/** Take every motion animation off a node (a grab, a pop, a teardown). */
export function cancelMotion(el) {
  if (!el) return;
  const list = el[TRACK];
  if (!list || !list.length) return;
  for (const a of list.slice()) { try { a.cancel(); } catch (_e) { /* ignore */ } }
  list.length = 0;
}

const later = (fn, ms) => {
  const t = setTimeout(fn, Math.max(0, ms | 0));
  if (t && typeof t.unref === 'function') t.unref();
  return t;
};

const round = (v, d = 2) => Math.round(v * 10 ** d) / 10 ** d;

/* ------------------------------------------------------------ flash in / out */

/**
 * A flash LANDS: from (dx, dy) px away (its spawn point: a popped bubble, or a
 * short drop from above), tilted a little further than its rest tilt, small,
 * then overshoots to ~1.12 and settles at 1. `base` is the node's own CSS
 * transform prefix (fx.css .gg-flash: translate(-50%, -50%)), `rot` its tilt.
 */
export function landFlash(el, { dx = 0, dy = -40, rot = 0, opacity = 0.95, ms = MOTION.FLASH_IN_MS } = {}) {
  if (!canAnimate(el)) return null;
  const op = Math.max(0, Math.min(1, Number(opacity) || 0.95));
  if (calm()) return play(el, [{ opacity: 0 }, { opacity: op }], { duration: MOTION.FADE_IN_MS, easing: 'ease-out' });
  const spin = rot >= 0 ? 9 : -9;
  const kf = toKeyframes(14, (u) => {
    const travel = cubicOut(u);
    const s = 0.35 + 0.65 * backOut(u, 2.4);
    const off = 1 - travel;
    return {
      opacity: round(op * Math.min(1, u * 3.2)),
      transform: `translate(-50%, -50%) translate(${round(dx * off)}px, ${round(dy * off)}px) rotate(${round(Number(rot) + spin * off)}deg) scale(${round(s, 3)})`,
    };
  });
  return play(el, kf, { duration: ms });
}

/** The flash leaves: shrinks and sinks a little, tilting on, and fades. */
export function leaveFlash(el, { rot = 0, opacity = 0.95, ms = MOTION.FLASH_OUT_MS } = {}) {
  if (!canAnimate(el) || !el.isConnected) return null;
  const op = Math.max(0, Math.min(1, Number(opacity) || 0.95));
  if (calm()) return play(el, [{ opacity: op }, { opacity: 0 }], { duration: Math.min(ms, MOTION.FADE_OUT_MS), fill: 'forwards' });
  const kf = toKeyframes(8, (u) => {
    const e = cubicIn(u);
    return {
      opacity: round(op * (1 - e)),
      transform: `translate(-50%, -50%) translate(0px, ${round(14 * e)}px) rotate(${round(Number(rot) + 6 * e)}deg) scale(${round(1 - 0.28 * e, 3)})`,
    };
  });
  return play(el, kf, { duration: ms, fill: 'forwards' });
}

/**
 * Schedule a flash's exit so it ENDS on its authored hold (tween.exitWindow).
 * `stillOwned()` is asked at fire time: a grabbed, popped or dead flash is left
 * to its own exit. Returns the timer (clear it if you tear down first).
 */
export function scheduleFlashExit(el, holdMs, { rot = 0, opacity = 0.95, stillOwned } = {}) {
  const { atMs, outMs } = exitWindow(holdMs, MOTION.FLASH_OUT_MS);
  if (!(outMs > 0)) return 0;
  return later(() => {
    if (!el || !el.isConnected) return;
    if (typeof stillOwned === 'function' && !stillOwned()) return;
    leaveFlash(el, { rot, opacity, ms: outMs });
  }, atMs);
}

/* ------------------------------------------------------ fullscreen panes */

/**
 * A fullscreen wash GROWS OUT OF (x, y): scale from a pinprick at the pop point
 * to 1.04 and back to 1, opacity riding up with it. transform-origin is set on
 * the node for the ride (static, not animated). `spinning` panes (the spiral
 * bed rotates on its own transform) grow on the individual `scale` property
 * instead, so their spin keeps going underneath.
 */
export function growPane(el, { x, y, opacity = 0.5, ms = MOTION.PANE_IN_MS, spinning = false, restScale = 1 } = {}) {
  if (!canAnimate(el)) return null;
  const op = Math.max(0, Math.min(1, Number(opacity) || 0));
  if (calm()) return play(el, [{ opacity: 0 }, { opacity: op }], { duration: MOTION.FADE_IN_MS, easing: 'ease-out' });
  if (!spinning && typeof x === 'number' && typeof y === 'number') {
    try { el.style.setProperty('transform-origin', `${Math.round(x)}px ${Math.round(y)}px`); } catch (_e) { /* ignore */ }
  }
  const kf = toKeyframes(14, (u) => {
    const s = 0.04 + 0.96 * backOut(u, 1.6);
    const k = { opacity: round(op * quadOut(Math.min(1, u * 1.6))) };
    if (spinning) k.scale = String(round(s * restScale, 3));
    else k.transform = `scale(${round(s, 3)})`;
    return k;
  });
  return play(el, kf, { duration: ms });
}

/** The same pane sucked back into its origin. fill forwards: the caller also
 *  writes opacity 0, and this keeps the node shut until it is removed. */
export function shrinkPane(el, { opacity = 0.5, ms = MOTION.PANE_OUT_MS, spinning = false, restScale = 1 } = {}) {
  if (!canAnimate(el) || !el.isConnected) return null;
  const op = Math.max(0, Math.min(1, Number(opacity) || 0));
  if (calm()) return play(el, [{ opacity: op }, { opacity: 0 }], { duration: MOTION.FADE_OUT_MS, fill: 'forwards' });
  const kf = toKeyframes(8, (u) => {
    const e = cubicIn(u);
    const s = 1 - 0.92 * e;
    const k = { opacity: round(op * (1 - e)) };
    if (spinning) k.scale = String(round(s * restScale, 3));
    else k.transform = `scale(${round(s, 3)})`;
    return k;
  });
  return play(el, kf, { duration: ms, fill: 'forwards' });
}

/** A pane already up takes a fresh hit: a small swell, no re-grow. */
export function pulsePane(el, { spinning = false, restScale = 1, ms = MOTION.PANE_PULSE_MS } = {}) {
  if (!canAnimate(el) || calm()) return null;
  const kf = toKeyframes(8, (u) => {
    const s = 1 + 0.035 * Math.sin(Math.PI * u) * (1 - u * 0.4);
    return spinning ? { scale: String(round(s * restScale, 4)) } : { transform: `scale(${round(s, 4)})` };
  });
  return play(el, kf, { duration: ms });
}

/**
 * The glitch-slice shimmer: 3-5 horizontal bands of the pane's own picture
 * (background: inherit) knocked sideways and pulled home ONCE, then removed.
 * The bands' clip-path is static; only their transform moves. Lite and calm
 * skip it (the pane still grows / fades).
 */
export function sliceShimmer(pane, { ms = MOTION.SLICE_MS, tint } = {}) {
  if (!pane || typeof document === 'undefined' || !document || calm() || perfLite()) return 0;
  if (!canAnimate(pane)) return 0;
  const n = 3 + Math.floor(Math.random() * 3);
  let made = 0;
  for (let i = 0; i < n; i++) {
    let s;
    try { s = document.createElement('div'); } catch (_e) { break; }
    s.className = 'gg-slice';
    const top = Math.random() * 88;
    const h = 2 + Math.random() * 9;
    s.style.setProperty('clip-path', `inset(${top.toFixed(1)}% 0 ${Math.max(0, 100 - top - h).toFixed(1)}% 0)`);
    if (tint) s.style.setProperty('--gg-slice-tint', tint);
    const dir = Math.random() < 0.5 ? -1 : 1;
    const px = dir * (18 + Math.random() * 46);
    const kill = () => { try { s.remove(); } catch (_e) { /* ignore */ } };
    try { pane.appendChild(s); } catch (_e) { break; }
    const a = play(s, toKeyframes(8, (u) => ({
      transform: `translateX(${round(px * (1 - cubicOut(u)))}px)`,
      opacity: round(0.9 * (1 - u)),
    })), { duration: ms, delay: i * 24 });
    if (a) { a.addEventListener('finish', kill, { once: true }); a.addEventListener('cancel', kill, { once: true }); }
    later(kill, ms + i * 24 + 200);
    made++;
  }
  return made;
}

/* ------------------------------------------------------------- impact kit */

/**
 * A particle burst at (x, y) inside `host` (a fullscreen fx layer). Particles
 * fly out on burstVectors, shrink and fade; gravity pulls them a little. The
 * lite tier throws about half; calm throws none.
 */
export function burst(host, x, y, { n = 10, color = TINTS.normal, minR = 36, maxR = 120, size = 7, ms = MOTION.BURST_MS } = {}) {
  if (!host || typeof document === 'undefined' || !document || calm()) return 0;
  const count = perfLite() ? Math.max(2, Math.round(n / 2)) : n;
  const vecs = burstVectors(count, { minR, maxR });
  let made = 0;
  for (const v of vecs) {
    let p;
    try { p = document.createElement('div'); } catch (_e) { break; }
    p.className = 'gg-particle';
    const sz = Math.max(3, Math.round(size * (0.6 + Math.random() * 0.8)));
    p.style.setProperty('left', `${Math.round(x)}px`);
    p.style.setProperty('top', `${Math.round(y)}px`);
    p.style.setProperty('width', `${sz}px`);
    p.style.setProperty('height', `${sz}px`);
    p.style.setProperty('color', color);
    const kill = () => { try { p.remove(); } catch (_e) { /* ignore */ } };
    try { host.appendChild(p); } catch (_e) { break; }
    const fall = 20 + Math.random() * 40;
    const a = play(p, toKeyframes(8, (u) => {
      const e = cubicOut(u);
      return {
        transform: `translate(${round(v.dx * e)}px, ${round(v.dy * e + fall * u * u)}px) scale(${round(1 - 0.8 * u, 3)})`,
        opacity: round(u < 0.6 ? 1 : 1 - (u - 0.6) / 0.4),
      };
    }), { duration: ms * (0.8 + Math.random() * 0.4) });
    if (a) { a.addEventListener('finish', kill, { once: true }); a.addEventListener('cancel', kill, { once: true }); }
    later(kill, ms * 1.3 + 100);
    made++;
  }
  return made;
}

/** A shockwave ring out of (x, y). One node; calm skips it. */
export function ring(host, x, y, { color = TINTS.normal, size = 90, ms = MOTION.RING_MS } = {}) {
  if (!host || typeof document === 'undefined' || !document || calm()) return null;
  let r;
  try { r = document.createElement('div'); } catch (_e) { return null; }
  r.className = 'gg-ring';
  r.style.setProperty('left', `${Math.round(x)}px`);
  r.style.setProperty('top', `${Math.round(y)}px`);
  r.style.setProperty('width', `${Math.round(size)}px`);
  r.style.setProperty('height', `${Math.round(size)}px`);
  r.style.setProperty('color', color);
  const kill = () => { try { r.remove(); } catch (_e) { /* ignore */ } };
  try { host.appendChild(r); } catch (_e) { return null; }
  const a = play(r, toKeyframes(8, (u) => ({
    transform: `translate(-50%, -50%) scale(${round(0.25 + 1.55 * cubicOut(u), 3)})`,
    opacity: round(0.85 * (1 - u)),
  })), { duration: ms });
  if (a) { a.addEventListener('finish', kill, { once: true }); a.addEventListener('cancel', kill, { once: true }); }
  later(kill, ms + 200);
  return r;
}

/**
 * Shake a node by `px` REAL pixels (the first sample carries the full
 * amplitude; it decays to 0). Uses the individual `translate` property so it
 * never fights a node's own transform animation. Calm: no shake. Capped at
 * MOTION.SHAKE_MAX_PX. Returns the animation (or null).
 */
export function shake(el, px, { ms = MOTION.SHAKE_MS } = {}) {
  if (!canAnimate(el) || calm()) return null;
  const amp = Math.max(0, Math.min(MOTION.SHAKE_MAX_PX, Number(px) || 0));
  if (amp < 0.5) return null;
  const path = shakePath(amp, 8);
  // First sample at FULL amplitude on x, so a measurement sees the real number.
  path[0] = { x: amp * (Math.random() < 0.5 ? -1 : 1), y: 0 };
  const kf = path.map((p, i) => ({ translate: `${p.x}px ${p.y}px`, offset: round(i / (path.length - 1), 4) }));
  return play(el, kf, { duration: ms });
}

/** Hit size -> shake px: strength 0..100 maps onto 2..SHAKE_MAX_PX. */
export const shakeForStrength = (strength) => {
  const s = Math.max(0, Math.min(100, Number(strength) || 0));
  return round(2 + (MOTION.SHAKE_MAX_PX - 2) * (s / 100), 2);
};

/* ---------------------------------------------------------------- generic */

/** Fade in (with an optional small scale settle). For overlays and words. */
export function fadeIn(el, { opacity = 1, ms = MOTION.GLOW_IN_MS, from = 0.92, base = '' } = {}) {
  if (!canAnimate(el)) return null;
  if (calm()) return play(el, [{ opacity: 0 }, { opacity }], { duration: MOTION.FADE_IN_MS, easing: 'ease-out' });
  const kf = toKeyframes(10, (u) => ({
    opacity: round(opacity * cubicOut(Math.min(1, u * 1.4)), 3),
    transform: `${base} scale(${round(from + (1 - from) * backOut(u, 1.4), 4)})`.trim(),
  }));
  return play(el, kf, { duration: ms });
}

/** Drop-in THUD for a card: from above and a touch big, lands with the house
 *  overshoot (cubic-bezier(.2,1.5,.4,1), 340 ms). */
export function thudIn(el, { ms = MOTION.CARD_IN_MS, dy = -46 } = {}) {
  if (!canAnimate(el)) return null;
  if (calm()) return play(el, [{ opacity: 0 }, { opacity: 1 }], { duration: MOTION.FADE_IN_MS, easing: 'ease-out' });
  return play(el, [
    { opacity: 0, transform: `translateY(${dy}px) scale(1.08)` },
    { opacity: 1, offset: 0.35 },
    { opacity: 1, transform: 'translateY(0px) scale(1)' },
  ], { duration: ms, easing: TIMING.THUD_EASE });
}

/** Zoom/iris in for a media surface: from 0.86 scale and dark to full. */
export function zoomIn(el, { ms = MOTION.VIDEO_IN_MS } = {}) {
  if (!canAnimate(el)) return null;
  if (calm()) return play(el, [{ opacity: 0 }, { opacity: 1 }], { duration: MOTION.FADE_IN_MS, easing: 'ease-out' });
  const kf = toKeyframes(10, (u) => ({
    opacity: round(Math.min(1, u * 2.2), 3),
    transform: `scale(${round(0.86 + 0.14 * backOut(u, 1.2), 4)})`,
  }));
  return play(el, kf, { duration: ms });
}

/** A generic quick exit: shrink a little and fade, fill forwards. */
export function fadeOut(el, { ms = MOTION.CARD_OUT_MS, to = 0.9, base = '' } = {}) {
  if (!canAnimate(el) || !el.isConnected) return null;
  if (calm()) return play(el, [{ opacity: 1 }, { opacity: 0 }], { duration: MOTION.FADE_OUT_MS, fill: 'forwards' });
  const kf = toKeyframes(8, (u) => ({
    opacity: round(1 - cubicIn(u), 3),
    transform: `${base} scale(${round(1 - (1 - to) * cubicIn(u), 4)})`.trim(),
  }));
  return play(el, kf, { duration: ms, fill: 'forwards' });
}

export default { MOTION, TINTS, landFlash, leaveFlash, growPane, shrinkPane, burst, ring, shake };
