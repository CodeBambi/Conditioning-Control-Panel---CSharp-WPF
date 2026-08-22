/* ============================================================================
 * engine/util.js — DOM guards + a timer registry.
 *
 * Every DOM touch in the engine goes through `hasDom()` / lazy element creation
 * so that importing any engine module under node (for the pure-logic tests)
 * never explodes. Timers/rAF loops are registered so suspend(on) and dispose()
 * can drop EVERYTHING immediately — that is a hard requirement (panic key,
 * mandatory video, audio-only session).
 * ==========================================================================*/

export const hasDom = () => (typeof document !== 'undefined' && !!document.createElement);
export const hasWindow = () => (typeof window !== 'undefined');
export const nowMs = () => ((typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now());

/** Create an element with class + optional style/text. Returns null with no DOM. */
export function el(tag, cls, styles) {
  if (!hasDom()) return null;
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (styles) for (const k in styles) { try { n.style[k] = styles[k]; } catch { /* ignore */ } }
  return n;
}

/** Live probes (soft degrade, never withholding). Safe with no DOM. */
export function probeReducedMotion() {
  if (!hasWindow() || !window.matchMedia) return false;
  try { return !!window.matchMedia('(prefers-reduced-motion: reduce)').matches; } catch { return false; }
}
export function probeCoarsePointer() {
  if (!hasWindow() || !window.matchMedia) return false;
  try { return !!window.matchMedia('(pointer: coarse)').matches; } catch { return false; }
}

/**
 * A registry of timeouts / intervals / rAF loops / DOM nodes with one kill switch.
 * Nothing in the engine calls setTimeout directly — everything lands here so a
 * panic suspend really is instant.
 */
export function createTimers() {
  let timeouts = new Set();
  let intervals = new Set();
  let rafs = new Set();
  let nodes = new Set();
  let killed = false;

  function after(ms, fn) {
    if (killed) return 0;
    const id = setTimeout(() => { timeouts.delete(id); if (!killed) { try { fn(); } catch { /* swallow */ } } }, Math.max(0, ms | 0));
    timeouts.add(id);
    return id;
  }
  function every(ms, fn) {
    if (killed) return 0;
    const id = setInterval(() => { if (!killed) { try { fn(); } catch { /* swallow */ } } }, Math.max(16, ms | 0));
    intervals.add(id);
    return id;
  }
  function cancel(id) {
    if (timeouts.has(id)) { clearTimeout(id); timeouts.delete(id); }
    if (intervals.has(id)) { clearInterval(id); intervals.delete(id); }
  }
  /** A rAF loop: fn(dtSec, nowMs) -> false to stop. Falls back to setInterval headless. */
  function loop(fn) {
    if (killed) return { cancel() {} };
    let stop = false;
    let last = nowMs();
    const handle = { cancel() { stop = true; rafs.delete(handle); } };
    rafs.add(handle);
    const step = () => {
      if (stop || killed) { rafs.delete(handle); return; }
      const t = nowMs();
      const dt = Math.min(0.05, (t - last) / 1000);
      last = t;
      let keep = true;
      try { keep = fn(dt, t) !== false; } catch { keep = false; }
      if (!keep) { rafs.delete(handle); return; }
      if (hasWindow() && window.requestAnimationFrame) window.requestAnimationFrame(step);
      else after(33, step);
    };
    if (hasWindow() && window.requestAnimationFrame) window.requestAnimationFrame(step);
    else after(33, step);
    return handle;
  }
  /** Track a node so kill() removes it. */
  function own(node) { if (node) nodes.add(node); return node; }
  function release(node) {
    if (!node) return;
    nodes.delete(node);
    try { if (node.parentNode) node.parentNode.removeChild(node); } catch { /* ignore */ }
  }
  function kill() {
    for (const id of timeouts) clearTimeout(id);
    for (const id of intervals) clearInterval(id);
    for (const h of rafs) { try { h.cancel(); } catch { /* ignore */ } }
    for (const n of nodes) { try { if (n.parentNode) n.parentNode.removeChild(n); } catch { /* ignore */ } }
    timeouts = new Set(); intervals = new Set(); rafs = new Set(); nodes = new Set();
  }
  return {
    after, every, cancel, loop, own, release, kill,
    get size() { return timeouts.size + intervals.size + rafs.size + nodes.size; },
    dispose() { killed = true; kill(); },
  };
}

/** Seeded scatter helpers (never Math.random on a replayable path). */
export const rand = (rng, lo, hi) => lo + (hi - lo) * rng();
export const randInt = (rng, lo, hi) => Math.floor(lo + (hi - lo + 1) * rng());
export const pickFrom = (rng, list) => (list && list.length ? list[Math.min(list.length - 1, Math.floor(rng() * list.length))] : null);

export default { hasDom, el, createTimers };
