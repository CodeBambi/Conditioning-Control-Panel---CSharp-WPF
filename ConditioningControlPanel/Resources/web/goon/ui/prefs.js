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
