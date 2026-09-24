/* ============================================================================
 * ui/juiceDom.js - the DOM half of ui/juice.js.
 *
 * Every helper here is fire-and-forget, never throws, and cleans up after
 * itself on a timer whether or not a frame ever ran (a hidden tab or a stub DOM
 * still ends with no stray nodes). Motion is compositor-only: transform and
 * opacity through the Web Animations API, nothing that reflows per frame.
 *
 * Reduced motion (the page pref stamped as html[data-gg-motion="reduced"], or
 * the OS query) turns every entrance into a plain fade and drops particles and
 * shakes entirely. The lite perf tier (exec/perfTier.js) halves particles.
 *
 * Node-import-safe: nothing touches document until a helper is called.
 * ==========================================================================*/

import {
  THUD_MS, EXIT_MS, ENTER_MS, SHIVER_MS, THUD_EASE, OUT_EASE, SETTLE_EASE,
  popInFrames, popOutFrames, squashFrames, shakeFrames, staggerDelays,
  burstCount, burstParticles, arcPoints, countUpValue, countUpMs,
} from './juice.js';
import { perfLite } from '../exec/perfTier.js';

const doc = () => (typeof document !== 'undefined' ? document : null);

/** True when the player (or the OS) asked for less motion. */
export function isCalm() {
  const d = doc();
  try {
    if (d && d.documentElement && d.documentElement.getAttribute('data-gg-motion') === 'reduced') return true;
  } catch (_e) { /* ignore */ }
  try { return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches; } catch (_e) { return false; }
}

/** True on the lite perf tier. */
export function isLite() { try { return perfLite(); } catch (_e) { return false; } }

/** element.animate() that can never throw; null when there is no WAAPI. */
export function play(node, frames, opts) {
  try {
    if (!node || typeof node.animate !== 'function') return null;
    return node.animate(frames, opts);
  } catch (_e) { return null; }
}

/** Resolve when an animation ends, or after `ms` if it never reports. */
function settled(anim, ms) {
  return new Promise((resolve) => {
    let done = false;
    const end = () => { if (!done) { done = true; resolve(); } };
    try { if (anim && anim.finished) anim.finished.then(end, end); } catch (_e) { /* ignore */ }
    setTimeout(end, Math.max(0, ms) + 60);
  });
}

/** Scale-and-fade in with a THUD overshoot. */
export function popIn(node, { ms = THUD_MS, delay = 0, from = 0.6, over = 1.08 } = {}) {
  const calm = isCalm();
  return play(node, popInFrames({ from, over, calm }), {
    duration: calm ? Math.min(ms, 200) : ms, delay, easing: calm ? 'linear' : SETTLE_EASE, fill: 'backwards',
  });
}

/** Exit toward (dx, dy), shrinking. Resolves when it has gone. */
export function popOut(node, { ms = EXIT_MS, dx = 0, dy = -10, to = 0.7 } = {}) {
  const calm = isCalm();
  const anim = play(node, popOutFrames({ dx, dy, to, calm }), { duration: ms, easing: OUT_EASE, fill: 'forwards' });
  return settled(anim, ms);
}

/** A rubber press. */
export function squash(node, { amount = 0.12, ms = 220 } = {}) {
  if (isCalm()) return null;
  return play(node, squashFrames(amount), { duration: ms, easing: 'ease-out' });
}

/** A damped shake in real pixels. Skipped under reduced motion. */
export function shake(node, px = 4, { ms = SHIVER_MS } = {}) {
  if (isCalm() || !(px > 0)) return null;
  return play(node, shakeFrames(px), { duration: ms, easing: 'linear', composite: 'add' });
}

/**
 * Children cascade in: rise a few px and fade, one after another. Uses
 * fill:'backwards' so a child waiting for its turn is already invisible, and
 * nothing is left pinned once the cascade is done.
 */
export function staggerIn(children, { rise = 14, ms = ENTER_MS, start = 0, step, max } = {}) {
  const list = Array.from(children || []).filter((c) => c && c.nodeType === 1);
  if (!list.length) return;
  const calm = isCalm();
  const delays = staggerDelays(list.length, { start, step, max });
  list.forEach((c, i) => {
    const frames = calm
      ? [{ opacity: 0 }, { opacity: 1 }]
      : [
        { opacity: 0, transform: 'translateY(' + rise + 'px) scale(.97)' },
        { opacity: 1, transform: 'translateY(-2px) scale(1.01)', offset: 0.7 },
        { opacity: 1, transform: 'none' },
      ];
    play(c, frames, { duration: ms, delay: delays[i], easing: SETTLE_EASE, fill: 'backwards' });
  });
}

/* ------------------------------------------------------------------ bursts */

let layer = null;

/** One fixed, click-through layer for every particle, under toasts (z50). */
export function juiceLayer() {
  const d = doc();
  if (!d || !d.body) return null;
  if (layer && layer.isConnected) return layer;
  layer = d.createElement('div');
  layer.className = 'gg-juice-layer';
  layer.setAttribute('aria-hidden', 'true');
  d.body.appendChild(layer);
  return layer;
}

/**
 * A radial burst of soft dots at viewport (x, y). Theme tint, never white.
 * @returns {number} how many particles it actually spawned
 */
export function burst(x, y, { count = 12, color = '255, 105, 180', dist = 70, spread = 50, life = 520,
  arc, bias, sizeMin = 4, sizeMax = 9, shape = 'dot' } = {}) {
  const host = juiceLayer();
  if (!host) return 0;
  const n = burstCount(count, { lite: isLite(), calm: isCalm() });
  if (!n) return 0;
  const parts = burstParticles(n, { dist, spread, life, arc, bias, sizeMin, sizeMax });
  const d = doc();
  let longest = 0;
  for (const p of parts) {
    const node = d.createElement('i');
    node.className = 'gg-juice-p gg-juice-p--' + shape;
    node.style.left = Math.round(x) + 'px';
    node.style.top = Math.round(y) + 'px';
    node.style.width = p.size + 'px';
    node.style.height = p.size + 'px';
    node.style.setProperty('--gg-juice-c', color);
    host.appendChild(node);
    const anim = play(node, [
      { opacity: 0, transform: 'translate(-50%, -50%) scale(.4)' },
      { opacity: 1, transform: 'translate(calc(-50% + ' + (p.dx * 0.35) + 'px), calc(-50% + ' + (p.dy * 0.35) + 'px)) scale(1.1)', offset: 0.18 },
      { opacity: 0, transform: 'translate(calc(-50% + ' + p.dx + 'px), calc(-50% + ' + (p.dy + 12) + 'px)) scale(.3)' },
    ], { duration: p.ms, delay: p.delay, easing: 'cubic-bezier(.15, .8, .3, 1)', fill: 'both' });
    const kill = () => { try { node.remove(); } catch (_e) { /* gone */ } };
    if (anim) anim.onfinish = kill;
    setTimeout(kill, p.ms + p.delay + 80);
    longest = Math.max(longest, p.ms + p.delay);
  }
  return n;
}

/** Centre of an element's box, in viewport px. */
export function centreOf(node) {
  try {
    const r = node.getBoundingClientRect();
    return { x: r.left + r.width / 2, y: r.top + r.height / 2, w: r.width, h: r.height };
  } catch (_e) { return null; }
}

/* ------------------------------------------------------------------- flight */

/**
 * Fly `node` (already appended, position:fixed, left/top 0) along an arc from
 * `from` to `to` (viewport centres). Drops a fading trail dot on the way.
 * Resolves on landing. Reduced motion: a straight fade, no trail.
 */
export function flyArc(node, from, to, { ms = 520, lift = 0.3, spin = 18, scaleFrom = 1, scaleTo = 0.5,
  trail = true, trailColor = '255, 105, 180' } = {}) {
  if (!node || !from || !to) return Promise.resolve();
  const calm = isCalm();
  if (calm) {
    const a = play(node, [
      { opacity: 1, transform: 'translate(' + from.x + 'px,' + from.y + 'px) translate(-50%,-50%)' },
      { opacity: 0, transform: 'translate(' + to.x + 'px,' + to.y + 'px) translate(-50%,-50%)' },
    ], { duration: Math.min(ms, 260), easing: 'linear', fill: 'forwards' });
    return settled(a, Math.min(ms, 260));
  }
  const pts = arcPoints(from, to, { steps: 12, lift, spin, scaleFrom, scaleTo });
  const frames = pts.map((p) => ({
    transform: 'translate(' + p.x + 'px,' + p.y + 'px) translate(-50%,-50%) rotate(' + p.rot + 'deg) scale(' + p.scale + ')',
    opacity: p.t < 0.08 ? p.t / 0.08 : 1,
  }));
  const anim = play(node, frames, { duration: ms, easing: 'cubic-bezier(.35, .1, .5, 1)', fill: 'forwards' });
  if (trail && !isLite()) {
    const host = juiceLayer();
    const d = doc();
    const every = Math.max(28, Math.round(ms / 9));
    for (let i = 1; i < 9; i++) {
      const p = pts[Math.min(pts.length - 1, Math.round((i / 9) * (pts.length - 1)))];
      setTimeout(() => {
        if (!host || !d) return;
        const dot = d.createElement('i');
        dot.className = 'gg-juice-p gg-juice-p--trail';
        dot.style.left = p.x + 'px';
        dot.style.top = p.y + 'px';
        const s = Math.max(3, 10 - i * 0.6);
        dot.style.width = s + 'px';
        dot.style.height = s + 'px';
        dot.style.setProperty('--gg-juice-c', trailColor);
        host.appendChild(dot);
        const a = play(dot, [
          { opacity: 0.8, transform: 'translate(-50%,-50%) scale(1)' },
          { opacity: 0, transform: 'translate(-50%,-50%) scale(.2)' },
        ], { duration: 320, easing: 'ease-out', fill: 'forwards' });
        const kill = () => { try { dot.remove(); } catch (_e) { /* gone */ } };
        if (a) a.onfinish = kill;
        setTimeout(kill, 400);
      }, i * every * 0.9);
    }
  }
  return settled(anim, ms);
}

/* ---------------------------------------------------------------- count up */

/**
 * Count `node`'s text from `from` to `to`. Returns a cancel function. Calls
 * `onStep(value)` each time the shown integer changes (for a tick cue), and
 * `onDone()` once. Reduced motion: jumps straight to the final number.
 */
export function countUp(node, from, to, { ms, format = String, onStep = null, onDone = null, delay = 0 } = {}) {
  const dur = ms == null ? countUpMs(from, to) : ms;
  let cancelled = false;
  let last = null;
  const write = (v) => {
    if (v === last) return;
    last = v;
    try { node.textContent = format(v); } catch (_e) { /* gone */ }
    if (onStep) { try { onStep(v); } catch (_e) { /* ignore */ } }
  };
  const finish = () => { write(Math.round(Number(to) || 0)); if (onDone) { try { onDone(); } catch (_e) { /* ignore */ } } };
  if (isCalm() || typeof requestAnimationFrame !== 'function' || !(dur > 0)) {
    finish();
    return () => { cancelled = true; };
  }
  write(Math.round(Number(from) || 0));
  let t0 = 0;
  const begin = () => {
    if (cancelled) return;
    const step = (now) => {
      if (cancelled) return;
      if (!t0) t0 = now;
      const el = now - t0;
      if (el >= dur) { finish(); return; }
      write(countUpValue(from, to, el, dur));
      requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  };
  if (delay > 0) setTimeout(begin, delay); else begin();
  // A backgrounded tab never runs rAF: land the number anyway.
  setTimeout(() => { if (!cancelled && last !== Math.round(Number(to) || 0)) finish(); }, delay + dur + 400);
  return () => { cancelled = true; };
}

export { THUD_MS, EXIT_MS, ENTER_MS, SHIVER_MS, THUD_EASE, OUT_EASE, SETTLE_EASE };

export default { isCalm, isLite, play, popIn, popOut, squash, shake, staggerIn, burst, juiceLayer, centreOf, flyArc, countUp };

// A brief impact signature shared by sending and receiving. No input or game timing.
const impactMarks = new Set();
export function impactMark(x, y, { glyph = '◆', tint = '255, 105, 180', incoming = false } = {}) {
  const host = juiceLayer();
  if (!host || !Number.isFinite(x) || !Number.isFinite(y)) return;
  burst(x, y, { count: incoming ? 24 : 18, color: tint, dist: incoming ? 100 : 65, life: 540, sizeMin: 3, sizeMax: 7 });
  const node = doc().createElement('div');
  node.className = 'gg-impact-mark' + (incoming ? ' is-incoming' : '');
  node.textContent = glyph;
  node.style.left = x + 'px'; node.style.top = y + 'px';
  node.style.setProperty('--gg-impact-tint', tint);
  host.appendChild(node);
  const remove = () => { try { node.remove(); } catch (_e) { /* gone */ } impactMarks.delete(remove); };
  impactMarks.add(remove);
  while (impactMarks.size > 6) impactMarks.values().next().value();
  const frames = isCalm() ? [{ opacity: .85 }, { opacity: 0 }] : [
    { opacity: 0, transform: 'translate(-50%,-50%) scale(.35) rotate(-15deg)' },
    { opacity: 1, transform: 'translate(-50%,-50%) scale(1.1) rotate(4deg)', offset: .22 },
    { opacity: 0, transform: 'translate(-50%,-65%) scale(1.25) rotate(0deg)' },
  ];
  const anim = play(node, frames, { duration: 520, easing: 'ease-out', fill: 'forwards' });
  if (anim) anim.onfinish = remove;
  setTimeout(remove, 600);
}
