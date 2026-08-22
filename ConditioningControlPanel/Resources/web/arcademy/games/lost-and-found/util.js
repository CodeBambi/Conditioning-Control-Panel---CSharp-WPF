/* ============================================================================
 * games/lost-and-found/util.js - local helpers. No shared-layer imports beyond
 * what the module contract hands us, so nothing here can drift out of sync with
 * the engine or the shell.
 *
 * WHY OUR OWN TIMER REGISTRY: engine/util.js has one, but it is engine-internal
 * and a game must never reach into it. The game owns timers of its own (churn
 * cadence, the clock, the pity pulse, ceremonies) and pause()/suspend()/destroy()
 * must be able to drop every one of them INSTANTLY - the panic key and a
 * mandatory video both depend on it.
 * ==========================================================================*/

export const clamp01 = (n) => {
  const v = Number(n);
  if (!Number.isFinite(v)) return 0;
  return v < 0 ? 0 : v > 1 ? 1 : v;
};
export const clamp = (n, lo, hi) => {
  const v = Number(n);
  if (!Number.isFinite(v)) return lo;
  return v < lo ? lo : v > hi ? hi : v;
};
export const nowMs = () => Date.now();

/** Element factory that survives a DOM-less import (tests, node). */
export function el(tag, cls, text) {
  if (typeof document === 'undefined' || !document.createElement) return null;
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

/** Seeded shuffle (Fisher-Yates on the class rng - never Math.random). */
export function shuffle(list, rng) {
  const a = (Array.isArray(list) ? list : []).slice();
  for (let i = a.length - 1; i > 0; i--) {
    const j = Math.floor(rng() * (i + 1));
    const t = a[i]; a[i] = a[j]; a[j] = t;
  }
  return a;
}

export function median(nums) {
  const a = (Array.isArray(nums) ? nums : []).filter((n) => Number.isFinite(n)).sort((x, y) => x - y);
  if (!a.length) return 0;
  const mid = a.length >> 1;
  return a.length % 2 ? a[mid] : (a[mid - 1] + a[mid]) / 2;
}

/* ----------------------------------------------------------------------------
 * MOTION / POINTER PROBES
 *
 * ctx does not carry reducedMotion / motionLevel / platform (flagged in the build
 * report), so we read the two facts the shell has already published:
 *   - html.arc-reduced  <- shell.js sets it for init.reducedMotion OR motionLevel 0,
 *                          i.e. the host's accessibility ceiling, already resolved;
 *   - the live media queries, same probes engine/util.js uses.
 * Both are soft: a missing DOM answers "no", never "withhold the class".
 * -------------------------------------------------------------------------- */
export function probeReduced() {
  try {
    if (typeof document !== 'undefined' && document.documentElement
      && document.documentElement.classList
      && document.documentElement.classList.contains('arc-reduced')) return true;
  } catch (e) { /* ignore */ }
  try {
    if (typeof window !== 'undefined' && window.matchMedia) {
      return !!window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }
  } catch (e) { /* ignore */ }
  return false;
}

export function probeCoarse() {
  try {
    if (typeof window !== 'undefined' && window.matchMedia) {
      return !!window.matchMedia('(pointer: coarse)').matches;
    }
  } catch (e) { /* ignore */ }
  return false;
}

/* ----------------------------------------------------------------------------
 * THE TIMER REGISTRY
 * -------------------------------------------------------------------------- */
export function createTimers() {
  let timeouts = new Set();
  let intervals = new Set();
  let dead = false;

  function after(ms, fn) {
    if (dead) return 0;
    const id = setTimeout(() => {
      timeouts.delete(id);
      if (dead) return;
      try { fn(); } catch (e) { /* a timer must never take the class down */ }
    }, Math.max(0, ms | 0));
    timeouts.add(id);
    return id;
  }
  function every(ms, fn) {
    if (dead) return 0;
    const id = setInterval(() => {
      if (dead) return;
      try { fn(); } catch (e) { /* as above */ }
    }, Math.max(16, ms | 0));
    intervals.add(id);
    return id;
  }
  function cancel(id) {
    if (!id) return;
    if (timeouts.has(id)) { clearTimeout(id); timeouts.delete(id); }
    if (intervals.has(id)) { clearInterval(id); intervals.delete(id); }
  }
  function killAll() {
    for (const id of timeouts) clearTimeout(id);
    for (const id of intervals) clearInterval(id);
    timeouts = new Set();
    intervals = new Set();
  }
  return {
    after, every, cancel, killAll,
    get size() { return timeouts.size + intervals.size; },
    dispose() { dead = true; killAll(); },
  };
}

export default { el, shuffle, median, createTimers };
