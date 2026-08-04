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
  /**
   * THE TWO CUE SLIDERS, split out of the old single `sfxVolume`. `ui` is the
   * chrome you drive (menus, sheets, the code cells); `game` is everything the
   * match does at you (the draft, the countdown, charges, drops, payloads,
   * bubbles, the recap). Every entry in ui/audio.js's SFX_REGISTRY declares
   * which bus it is on, and each is a real gain node under masterBus.
   *
   * Both default to the old sfxVolume default, so a fresh install sounds exactly
   * like it did before the split, and an EXISTING player's saved sfxVolume seeds
   * both of them once (see MIGRATIONS) rather than being silently dropped.
   */
  uiVolume: 0.85,
  gameVolume: 0.85,
  /**
   * The opponent's payload VIDEOS (exec/videos.js). Not a bus and not on the
   * WebAudio graph at all — a <video> element's own `.volume` — so this is the
   * one volume that has to leave the UI tier as a number rather than as a node;
   * see REFLECT below. 0.8 rather than 1: the windows already duck each other
   * and the HUD has to stay audible under them, and a player who wants them
   * louder has a slider now.
   */
  mediaVolume: 0.8,
  /**
   * The low binaural bed that runs under a match (ui/droneBed.js). Its own
   * slider rather than a share of "music", because it is the only continuous
   * voice on the graph and it is the one a player is most likely to want at a
   * different level from everything else. 0.5 by default — audible, well under
   * the cues, and 0 is effectively off.
   */
  droneVolume: 0.5,
  reduceMotion: false,
  /**
   * Floating video windows grow a ✕ and can be closed early. OFF by default:
   * being thrown a window is something you sit through, and a dismiss button on
   * by default would quietly turn the payload into a notification. A click still
   * MUTES one either way — see exec/videos.js.
   */
  skippableVideos: false,
  /**
   * The spiral bed is drawn live on the GPU (exec/spiralField.js). ON by default
   * — it is the good path, and the raster pool it falls back to is a magnified,
   * dithered GIF. This exists as an ESCAPE HATCH: on 2026-08-04 a session froze
   * visually while its script kept running (a compositor/GPU stall, no crash),
   * and the shader pane is the only GPU context the duel owns. Off, exec/spiral.js
   * takes the raster path from the start and drops any live weave within a second.
   */
  shaderSpirals: true,
  /**
   * Whether an opponent's shared Discord picture is shown on this machine (the
   * VS splash, the HUD mini, the recap plate). ON by default — the sharer has
   * already opted in and hiding their choice by default would make the feature
   * invisible to everyone.
   *
   * OFF SUPPRESSES THE FETCH, NOT JUST THE RENDER (contract §1). ui/discord.js
   * checks this BEFORE posting `peer-card-req` and again when a card lands, so
   * a viewer who has switched it off never causes the host to pull an image at
   * all. That is the whole point: "do not show me" and "do not download it" are
   * the same request, and a pref that only hid the pixels would be a lie.
   */
  showOpponentAvatars: true,
  /**
   * The HUD's zen toggle (ui/hud.js): score, risk, the closeness dial and the
   * mercy button hidden, leaving the opponent monitor and the arsenal. OFF by
   * default — a duel you cannot read your own score in is a choice, never a
   * starting position — and remembered only so a player who wants the quiet
   * desk does not have to ask for it every match. It is NOT mirrored onto
   * <html> from here: ui/hud.js owns that bit for as long as a HUD is mounted,
   * and a stale attribute would hide the mercy button with no button to bring
   * it back.
   */
  hudZen: false,
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

/* ---------------------------------------------------------------------------
 * MIGRATIONS — a retired key seeding the keys that replaced it.
 *
 * `sfxVolume` was ONE slider over two jobs (menu chrome and match audio) until
 * the granularity pass split it into uiVolume + gameVolume. A player who had
 * already turned the game down to 0.3 must not be handed 0.85 twice on the next
 * launch — "we redesigned the mixer" is not a reason to undo somebody's setting.
 *
 * The rule, deliberately narrow:
 *   · a legacy key only seeds a successor that is ABSENT from the same blob, so
 *     a store that already has the new keys is never overwritten by a stale
 *     sfxVolume the C# host is still sending in `init`;
 *   · it is applied per source blob, before the merge, so the normal precedence
 *     (defaults < host seed < local store) still decides;
 *   · the retired key is NOT in PREF_DEFAULTS, so it is migrated, then dropped —
 *     it is never written back and there is no second slider to keep in step.
 * ------------------------------------------------------------------------- */
export const PREF_MIGRATIONS = Object.freeze({
  sfxVolume: Object.freeze(['uiVolume', 'gameVolume']),
});

/** A stored/seeded blob -> the same blob with retired keys expanded. Pure. */
export function migratePrefs(src) {
  const raw = (src && typeof src === 'object') ? src : {};
  let out = raw;
  for (const legacy of Object.keys(PREF_MIGRATIONS)) {
    if (raw[legacy] === undefined) continue;
    for (const key of PREF_MIGRATIONS[legacy]) {
      if (raw[key] !== undefined) continue;             // the new key wins, always
      if (out === raw) out = Object.assign({}, raw);    // copy only if we change it
      out[key] = raw[legacy];
    }
  }
  return out;
}

/* ---------------------------------------------------------------------------
 * THE MEDIA NUMBER. exec/videos.js sets a <video> element's `.volume`, which is
 * not a node on ui/audio.js's graph, so masterBus cannot reach it: master has to
 * be folded in HERE or a muted master would leave the opponent's clips playing.
 * Two sliders multiplied, never conflated — the same rule as droneGain/busGain.
 * ------------------------------------------------------------------------- */
export const MEDIA_VOL_ATTR = 'data-gg-mediavol';
/** Fired on `window` whenever the published number changes (exec/ may listen). */
export const MEDIA_VOL_EVENT = 'gg-media-volume';

/** mediaVolume x masterVolume -> the effective 0..1. Pure, total. */
export function mediaGain(mediaVol, master) { return clamp01(mediaVol) * clamp01(master); }

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
const MEDIA_SPEC = {
  attr: MEDIA_VOL_ATTR,
  event: MEDIA_VOL_EVENT,
  /* DERIVED FROM TWO KEYS, which is why it is a function and not an on/off pair:
   * exec/videos.js cannot multiply two preferences it is not allowed to import,
   * so what gets published is the finished number. Both contributing keys carry
   * this same spec, so either slider rewrites it. Fixed to 3 places: an attribute
   * that churns through 17 digits of float on every drag is a diff nobody wants
   * in the inspector, and 0.001 of media volume is not a thing. */
  derive: (values) => mediaGain(values.mediaVolume, values.masterVolume).toFixed(3),
};

const REFLECT = Object.freeze({
  /** exec/videos.js reads this to decide whether a window's ✕ is real. */
  skippableVideos: { attr: 'data-gg-vskip', on: 'on', off: 'off' },
  /** exec/spiral.js reads this to decide whether the bed is a shader or a GIF.
   *  Note the polarity: absent means ON there (a page with no prefs store still
   *  gets the good path), so it is the OFF value that carries the meaning. */
  shaderSpirals: { attr: 'data-gg-shader', on: 'on', off: 'off' },
  /** ...and the media volume, from BOTH of the keys that make it. Absent means
   *  FULL there, on the same principle as the shader flag: a page with no prefs
   *  store plays its videos at the volume exec/videos.js chose on its own. */
  mediaVolume: MEDIA_SPEC,
  masterVolume: MEDIA_SPEC,
});

/**
 * Mirror one pref onto <html>. Takes the whole `values` bag rather than a single
 * value because a derived attribute (the media volume) is made of two keys.
 */
function reflect(key, values) {
  const spec = REFLECT[key];
  if (!spec) return;
  try {
    if (typeof document === 'undefined' || !document || !document.documentElement) return;
    const next = spec.derive ? spec.derive(values) : (values[key] ? spec.on : spec.off);
    const prev = document.documentElement.getAttribute(spec.attr);
    document.documentElement.setAttribute(spec.attr, next);
    // The attribute is the contract; the event is a courtesy for anything that
    // wants to retune sound it has ALREADY started (an open video window) rather
    // than wait for the next one. Edge-triggered, so master and media writing the
    // same number twice is one notification, and a host with no CustomEvent (the
    // node test stub) simply never hears it.
    if (spec.event && next !== prev) {
      if (typeof window !== 'undefined' && window && typeof window.dispatchEvent === 'function'
          && typeof CustomEvent === 'function') {
        window.dispatchEvent(new CustomEvent(spec.event, { detail: { volume: Number(next) } }));
      }
    }
  } catch (_e) { /* a host without a DOM simply never mirrors them */ }
}

/**
 * @param {object} [seed] the `prefs` blob the host sent on `init` (lowest priority
 *                        after the local store, which is the more recent edit).
 */
export function createPrefs(seed) {
  const values = Object.assign({}, PREF_DEFAULTS);
  for (const src of [seed || {}, readStore()]) {
    const blob = migratePrefs(src);
    for (const k of Object.keys(PREF_DEFAULTS)) {
      if (blob[k] !== undefined) values[k] = coerce(k, blob[k]);
    }
  }
  for (const k of Object.keys(REFLECT)) reflect(k, values);

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
    reflect(key, values);
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
