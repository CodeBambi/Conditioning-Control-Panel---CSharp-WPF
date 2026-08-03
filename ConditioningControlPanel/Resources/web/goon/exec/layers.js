/* ============================================================================
 * exec/layers.js — the z-layer manager for the effect executors.
 *
 * index.html owns the stack; this module is the ONLY way executors reach into
 * it, so nobody appends a flash to the HUD or leaks a video into #gg-fx:
 *
 *   z0  #gg-bg      canvas backdrop
 *   z20 #gg-stage   centre stage (videos, the one layer that takes pointers)
 *   z30 #gg-fx      -> flash · sub · bubbles · bounce · drain (pointer-events:none)
 *
 * DOM lookups are LAZY (never at import — the module must import clean under
 * node and before <body> exists) and cached once resolved.
 *
 * setFxHeat() is the armor switch: html[data-gg-fx] flips idle -> warm -> hot,
 * and goon.css drops backdrop-filter + suspends decorative animation at "hot" so
 * a heavy effect stack never starves the mercy button's frame budget.
 * ==========================================================================*/

/** layer name -> element id. 'stage' is deliberately in here: stopAll clears it. */
const IDS = {
  flash: 'gg-fx-flash',
  sub: 'gg-fx-sub',
  bubbles: 'gg-fx-bubbles',
  bounce: 'gg-fx-bounce',
  drain: 'gg-fx-drain',
  stage: 'gg-stage',
  fx: 'gg-fx',
};

/** The fx children only — stopAll clears these plus the stage. */
export const FX_LAYERS = ['flash', 'sub', 'bubbles', 'bounce', 'drain'];

const cache = new Map();

function doc() { return (typeof document !== 'undefined') ? document : null; }

/** The layer element, or null if the DOM/layer isn't there (never throws). */
export function get(name) {
  const id = IDS[name];
  if (!id) return null;
  const hit = cache.get(name);
  if (hit && hit.isConnected) return hit;
  const d = doc();
  if (!d) return null;
  const el = d.getElementById(id);
  if (el) cache.set(name, el);
  return el || null;
}

/** Empty one layer. Returns true if there was a layer to empty. */
export function clear(name) {
  const el = get(name);
  if (!el) return false;
  try { el.replaceChildren(); } catch (_e) { el.innerHTML = ''; }
  return true;
}

/** Empty every fx layer + the stage, and drop the heat back to idle. */
export function stopAll() {
  let n = 0;
  for (const name of FX_LAYERS) if (clear(name)) n++;
  if (clear('stage')) n++;
  setFxHeat(0);
  return n;
}

/**
 * Effect pressure -> html[data-gg-fx]: 0 idle · 1-2 warm · 3+ hot.
 * @param {number} activeCount how many effects are running right now
 * @returns {'idle'|'warm'|'hot'}
 */
export function setFxHeat(activeCount) {
  const n = Math.max(0, activeCount | 0);
  const heat = n === 0 ? 'idle' : (n < 3 ? 'warm' : 'hot');
  const d = doc();
  if (d && d.documentElement) {
    try { d.documentElement.setAttribute('data-gg-fx', heat); } catch (_e) { /* ignore */ }
  }
  return heat;
}

/** Current heat, read back off the DOM (defaults to idle). */
export function fxHeat() {
  const d = doc();
  const v = d && d.documentElement ? d.documentElement.getAttribute('data-gg-fx') : null;
  return v || 'idle';
}

/** Drop cached element refs (call if the shell is ever re-rendered wholesale). */
export function resetCache() { cache.clear(); }
