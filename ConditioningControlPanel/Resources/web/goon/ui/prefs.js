/* ============================================================================
 * ui/prefs.js — the page's own small preference store.
 *
 * TWO sinks, one API:
 *   - bridge.savePrefs(partial)  — the standalone path (localStorage 'goon.prefs',
 *     merged into the synthesized `init` on the next reload, so a dev's picks
 *     survive an F5);
 *   - localStorage['goon.ui.prefs'] — written directly so the HOSTED page keeps
 *     its volume/motion picks too (bridge.savePrefs is a deliberate no-op under
 *     WebView2, because the C# side owns the real settings file).
 *
 * Nothing here is authoritative for the MATCH: consent terms live on the wire
 * and are re-agreed every duel. These are volume knobs and remembered typing.
 *
 * Import-safe under node: every storage touch is lazy and guarded.
 * ==========================================================================*/

import * as bridge from '../bridge.js';

const KEY = 'goon.ui.prefs';

export const PREF_DEFAULTS = Object.freeze({
  masterVolume: 0.8,
  musicVolume: 0.55,
  sfxVolume: 0.85,
  reduceMotion: false,
  /**
   * Floating video windows grow a ✕ and can be closed early. OFF by default:
   * being thrown a window is something you sit through, and a dismiss button on
   * by default would quietly turn the payload into a notification. A click still
   * MUTES one either way — see exec/videos.js.
   */
  skippableVideos: false,
  /** Last consent terms this player proposed — pre-filled next lobby. */
  matchLengthSec: 720,
  payloadGapSec: 30,
  /** Remembered so a dropped join can be retried without re-typing. */
  lastCode: '',
  /** The "how it works" modal auto-opens exactly once. */
  seenHowItWorks: false,
  /** Local-only counter; the recap's "GG" title reads it. Never sent anywhere. */
  matchesPlayed: 0,
});

function clamp01(v) { const n = Number(v); return !isFinite(n) ? 0 : n < 0 ? 0 : n > 1 ? 1 : n; }

function readStore() {
  try {
    if (typeof localStorage === 'undefined') return {};
    const raw = localStorage.getItem(KEY);
    const v = raw ? JSON.parse(raw) : null;
    return (v && typeof v === 'object') ? v : {};
  } catch (_e) { return {}; }
}

function writeStore(all) {
  try {
    if (typeof localStorage !== 'undefined') localStorage.setItem(KEY, JSON.stringify(all));
  } catch (_e) { /* private mode / storage disabled — prefs simply do not persist */ }
}

/** Coerce a value into the shape its default implies, so a corrupt store cannot poison the UI. */
function coerce(key, value) {
  const def = PREF_DEFAULTS[key];
  if (typeof def === 'boolean') return !!value;
  if (typeof def === 'number') {
    const n = Number(value);
    if (!isFinite(n)) return def;
    return (key.endsWith('Volume')) ? clamp01(n) : n;
  }
  return value === undefined || value === null ? def : String(value);
}

/* ---------------------------------------------------------------------------
 * PREFS THAT LEAVE THE UI TIER.
 *
 * exec/ never imports ui/ — the renderers are handed layers, media and a logger
 * and nothing else, and they are built once at startup, long before any drawer
 * exists. So a preference one of them has to obey travels the way heat
 * (data-gg-fx) and motion (data-gg-motion) already do: as an attribute on <html>.
 *
 * This is the ONE writer. Reflecting here rather than in ui/options.js is what
 * makes the value true at STARTUP as well as on a toggle — the drawer may never
 * be opened, and a stored `true` that only took effect after you opened Options
 * would read as the setting having been forgotten.
 * ------------------------------------------------------------------------ */
const REFLECT = Object.freeze({
  /** exec/videos.js reads this to decide whether a window's ✕ is real. */
  skippableVideos: { attr: 'data-gg-vskip', on: 'on', off: 'off' },
});

function reflect(key, value) {
  const spec = REFLECT[key];
  if (!spec) return;
  try {
    if (typeof document === 'undefined' || !document || !document.documentElement) return;
    document.documentElement.setAttribute(spec.attr, value ? spec.on : spec.off);
  } catch (_e) { /* a host without a DOM simply never mirrors them */ }
}

/**
 * @param {object} [seed] the `prefs` blob the host sent on `init` (lowest priority
 *                        after the local store, which is the more recent edit).
 */
export function createPrefs(seed) {
  const values = Object.assign({}, PREF_DEFAULTS);
  for (const src of [seed || {}, readStore()]) {
    for (const k of Object.keys(PREF_DEFAULTS)) {
      if (src[k] !== undefined) values[k] = coerce(k, src[k]);
    }
  }
  for (const k of Object.keys(REFLECT)) reflect(k, values[k]);

  const listeners = new Set();
  let flushTimer = 0;

  function flush() {
    flushTimer = 0;
    writeStore(values);
    try { bridge.savePrefs(values); } catch (_e) { /* standalone-only sink */ }
  }

  function schedule() {
    // Coalesce a slider drag into one write instead of sixty.
    if (flushTimer) return;
    try { flushTimer = setTimeout(flush, 250); } catch (_e) { flush(); }
  }

  function emit(key) {
    // Every change goes through here — set, merge and reset alike — so this is
    // the one place the mirrored attributes can be kept honest.
    reflect(key, values[key]);
    for (const fn of Array.from(listeners)) {
      try { fn(key, values[key], values); } catch (_e) { /* a listener must not break a setter */ }
    }
  }

  return {
    get(key) { return values[key]; },
    all() { return Object.assign({}, values); },

    set(key, value) {
      if (!(key in PREF_DEFAULTS)) return false;
      const next = coerce(key, value);
      if (values[key] === next) return false;
      values[key] = next;
      schedule();
      emit(key);
      return true;
    },

    /** Bulk apply (options "Reset", or the host pushing new defaults). */
    merge(partial) {
      let changed = false;
      for (const k of Object.keys(partial || {})) {
        if (!(k in PREF_DEFAULTS)) continue;
        const next = coerce(k, partial[k]);
        if (values[k] === next) continue;
        values[k] = next;
        changed = true;
        emit(k);
      }
      if (changed) schedule();
      return changed;
    },

    reset() {
      const before = JSON.stringify(values);
      Object.assign(values, PREF_DEFAULTS);
      if (JSON.stringify(values) === before) return false;
      schedule();
      for (const k of Object.keys(PREF_DEFAULTS)) emit(k);
      return true;
    },

    /** fn(key, value, all) -> unsubscribe. */
    subscribe(fn) {
      if (typeof fn !== 'function') return () => {};
      listeners.add(fn);
      return () => listeners.delete(fn);
    },

    /** Write now (page hide / exit). */
    flush() {
      try { if (flushTimer) clearTimeout(flushTimer); } catch (_e) { /* ignore */ }
      flush();
    },
  };
}

export default createPrefs;
