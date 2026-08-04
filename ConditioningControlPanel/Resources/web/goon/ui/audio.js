/* ============================================================================
 * ui/audio.js — the sound bus. REAL as of the sfx pass.
 *
 *   ┌──────────────────────────────────────────────────────────────────────┐
 *   │ ONE WebAudio graph, three gain nodes:                                 │
 *   │     source -> perCueGain -> sfxBus ---\                               │
 *   │                             musicBus --> masterBus -> destination     │
 *   │ `sfx(id)` looks the id up in SFX_REGISTRY, lazily fetch+decodes its    │
 *   │ mp3 variants, and plays ONE of them through the pool. An UNKNOWN id is │
 *   │ a dev-time warning through the logger, never a silent fallback tone.   │
 *   │ Nothing here can throw and nothing here blocks, so no screen has to    │
 *   │ branch on "is audio real" — every call site stayed exactly as it was.  │
 *   └──────────────────────────────────────────────────────────────────────┘
 *
 * WHERE THE SOUNDS COME FROM (nothing was commissioned for this pass):
 *   - /dtrh/assets/bubbles/sfx/*.mp3 — HOTLINKED, not copied. Resources/web is
 *     the page origin's root under WebView2 and every harness mounts /dtrh/ at
 *     that exact prefix, which is the same precedent exec/fx.css already relies
 *     on for the bubble sprites (`url('/dtrh/assets/bubbles/bubble.png')`).
 *   - assets/sfx/*.mp3 — COPIES of cues from Resources/web/intake/assets/sfx/
 *     and from the Bureau (cclabs-site/bureau/sfx/). The Bureau is a different
 *     deployment entirely and /intake/ is NOT mounted by the headless harnesses,
 *     so both were copied in rather than linked. Page-relative, resolved off
 *     import.meta.url, so they work under file://, the harness AND WebView2.
 *
 * AUTOPLAY. WebView2 launches the game with --autoplay-policy=no-user-gesture-
 * required, so the context comes up `running` in the app. A plain browser needs
 * a gesture: ensureCtx() arms a one-shot resume on pointerdown/keydown/touch/
 * wheel (capture, passive) and unlock() forces it. Cues fired before the unlock
 * are DROPPED, not queued — a stale pop landing three seconds late is worse
 * than a missing one.
 *
 * THE POOL. 26 bubbles can burst inside one animation frame. Two limits keep
 * that from becoming 26 stacked sources:
 *   - MIN_GAP_MS per id (registry may raise it) swallows same-frame repeats;
 *   - POOL_MAX live sources total; at the cap the OLDEST is stopped so the
 *     newest pop is always the one you hear.
 *
 * VOLUME comes from ui/prefs.js (masterVolume / musicVolume / sfxVolume — the
 * sliders ui/options.js already draws) and is tracked live, so a drag retunes
 * the next cue with no migration and no settings UI of our own.
 *
 * Import-safe under node: no AudioContext, no fetch, no DOM at import time.
 * ==========================================================================*/

/* ----------------------------------------------------------------------------
 * URL RESOLUTION — pure, exported, and testable without a network.
 * -------------------------------------------------------------------------- */

/** DtRH's bundle, reached by ABSOLUTE path (the exec/fx.css sprite precedent). */
export const DTRH_SFX_DIR = '/dtrh/assets/bubbles/sfx/';
/** Our own copies, page-relative to THIS module (ui/ -> ../assets/sfx/). */
export const LOCAL_SFX_DIR = '../assets/sfx/';

/** A DtRH file name -> the absolute URL the page fetches. Pure. */
export function dtrhSfxUrl(file) { return DTRH_SFX_DIR + file; }

/** A local file name -> a URL resolved against this module (string fallback). */
export function localSfxUrl(file) {
  try { return new URL(LOCAL_SFX_DIR + file, import.meta.url).href; }
  catch (_e) { return './assets/sfx/' + file; }
}

/* ----------------------------------------------------------------------------
 * THE REGISTRY — id -> { files, gain, minGapMs? }.
 *
 * `files` are ALREADY-RESOLVED urls; more than one is a no-repeat-last rotation.
 * `gain` is the cue's own trim (house loudness), multiplied by SFX_TRIM and by
 * the player's master x sfx sliders. Everything here is deliberately quiet: this
 * is an ambience-heavy game and the motion budget has a volume twin.
 * -------------------------------------------------------------------------- */
const D = dtrhSfxUrl;
const L = localSfxUrl;

export const SFX_REGISTRY = Object.freeze({
  /* ---- chrome: menus, sheets, code entry (screens/*, router.button) -------- */
  'ui-move':        { files: [L('custody-log-tick-1.mp3'), L('custody-log-tick-2.mp3')], gain: 0.18, minGapMs: 45 },
  'ui-select':      { files: [L('checkbox-tick-1.mp3')],   gain: 0.35 },
  'ui-back':        { files: [L('verify-rewind-1.mp3'), L('verify-rewind-2.mp3')], gain: 0.30 },
  'ui-error':       { files: [L('error-blip-1.mp3')],      gain: 0.40 },
  'code-cell':      { files: [L('checkbox-tick-1.mp3')],   gain: 0.30 },
  'code-copy':      { files: [L('sticker-drag-1.mp3')],    gain: 0.30 },
  'lamp-confirm':   { files: [L('verify-lock-1.mp3')],     gain: 0.38 },
  'lamp-clear':     { files: [L('grid-settle-1.mp3')],     gain: 0.28 },

  /* ---- draft ------------------------------------------------------------- */
  'draft-pick':     { files: [L('captcha-logged-1.mp3')],  gain: 0.34 },
  'draft-drop':     { files: [L('captcha-rewarp-1.mp3')],  gain: 0.28 },
  'draft-lock':     { files: [L('verify-lock-1.mp3')],     gain: 0.42 },

  /* ---- match start / clock ----------------------------------------------- */
  'countdown-tick': { files: [L('verify-tick-1.mp3'), L('verify-tick-2.mp3')], gain: 0.40 },
  'countdown-go':   { files: [L('verify-resolve-1.mp3')],  gain: 0.55 },
  'gg-tick':        { files: [L('verify-tick-1.mp3'), L('verify-tick-2.mp3')], gain: 0.26 },
  'gg-go':          { files: [L('verify-resolve-1.mp3')],  gain: 0.50 },

  /* ---- the economy ------------------------------------------------------- */
  'gg-charge':      { files: [L('chime-1.mp3')],           gain: 0.30 },
  'charge-earned':  { files: [L('chime-1.mp3')],           gain: 0.30 },
  /** A bubble dropped an item and the slot lit up. The payoff cue of the loop. */
  'gg-drop':        { files: [L('slip_dling.mp3')],        gain: 0.42 },
  /** ...and the same roll refused because the arsenal is already full. A dud. */
  'gg-drop-dud':    { files: [L('captcha-reject-1.mp3'), L('captcha-reject-2.mp3')], gain: 0.20, minGapMs: 220 },

  /* ---- payloads ---------------------------------------------------------- */
  'gg-fire':        { files: [L('custody-stamp-1.mp3')],   gain: 0.42 },
  'payload-out':    { files: [L('custody-stamp-1.mp3')],   gain: 0.42 },
  /** ONE landing cue for every family. Per-kind stings would be noise. */
  'payload-in':     { files: [L('stamp_thud.mp3')],        gain: 0.38 },
  'gg-endured':     { files: [D('chime2.mp3')],            gain: 0.34 },

  /* ---- the two of you ---------------------------------------------------- */
  'gg-emote':       { files: [L('chime-1.mp3')],           gain: 0.24, minGapMs: 160 },
  'gg-check':       { files: [L('gaze-lock-1.mp3')],       gain: 0.34 },
  'gg-check-ok':    { files: [L('captcha-verify-ok-1.mp3')], gain: 0.38 },
  'gg-taunt-up':    { files: [L('gaze-lock-2.mp3')],       gain: 0.22, minGapMs: 400 },
  /** The safety valve. A soft bloom, NOT a sting — nothing about pressing it
   *  should read as a punishment. */
  'gg-mercy':       { files: [L('surface-bloom-1.mp3')],   gain: 0.40 },
  'gg-victory':     { files: [L('giggle-7.mp3')],          gain: 0.34 },
  /** The ribbon sliding in. Deliberately under every pop — it is a caption. */
  'announce-in':    { files: [L('briefing-open-1.mp3')],   gain: 0.20, minGapMs: 500 },

  /* ---- the field --------------------------------------------------------- */
  'bubble-pop':     { files: [D('Pop2.mp3'), D('Pop3.mp3')], gain: 0.34, minGapMs: 24 },
  /** An EFFECT bubble is juicier than a plain one: bigger sample, more gain. */
  'bubble-pop-fx':  { files: [D('Pop.mp3')],               gain: 0.46, minGapMs: 24 },
  /** ...and the prism/video bubble is hollower still, because it earns a window. */
  'bubble-pop-video': { files: [L('void_pop.mp3')],        gain: 0.44, minGapMs: 60 },
  'flash':          { files: [L('grid-tile-flicker-1.mp3'), L('grid-tile-flicker-2.mp3')], gain: 0.16, minGapMs: 260 },
  'flash-pop':      { files: [D('Pop3.mp3')],              gain: 0.30, minGapMs: 24 },
  /** Barely there on purpose — a word you half-hear is the whole point. */
  'subliminal':     { files: [L('custody-log-tick-1.mp3'), L('custody-log-tick-2.mp3')], gain: 0.10, minGapMs: 300 },
  'lock-solved':    { files: [D('chime1.mp3')],            gain: 0.40 },
  'lock-slip':      { files: [L('error-blip-1.mp3')],      gain: 0.24, minGapMs: 90 },

  /* ---- the recap --------------------------------------------------------- */
  'recap-reveal':   { files: [L('ticket_reveal.mp3')],     gain: 0.45 },
  'recap-won':      { files: [D('GG.mp3')],                gain: 0.45 },
  'recap-lost':     { files: [L('freeze-sting-1.mp3')],    gain: 0.38 },
  'recap-draw':     { files: [L('rank_settle.mp3')],       gain: 0.36 },
  'title-unlock':   { files: [L('gold_toggle.mp3')],       gain: 0.45 },

  /* ---- registered for LOCKED renderers (exec/videos.js, exec/spiral.js) ----
   * Those files belong to another pass; the ids are live here so the one-line
   * play call can land there without touching this file again. */
  'video-window-in':  { files: [L('briefing-open-1.mp3')], gain: 0.30, minGapMs: 120 },
  'video-window-out': { files: [L('card-melt-1.mp3'), L('card-melt-2.mp3')], gain: 0.26, minGapMs: 120 },
  'spiral-in':        { files: [L('loom-spiral-up-1.mp3')],   gain: 0.16 },
  'spiral-out':       { files: [L('loom-spiral-down-1.mp3')], gain: 0.16 },
});

/** Every sfx id the game may ask for. Derived, so the two can never drift. */
export const SFX_IDS = Object.freeze(Object.keys(SFX_REGISTRY));

/* ----------------------------------------------------------------------------
 * TUNING — pinned, exported, asserted by test/selftest-hud.js.
 * -------------------------------------------------------------------------- */
/** House trim over EVERY cue, under the player's sliders. One number to turn
 *  the whole game down without touching 40 registry gains. */
export const SFX_TRIM = 0.9;
/** Hard ceiling on simultaneously-playing one-shots. A 26-bubble burst may not
 *  become 26 sources; at the cap the oldest is stopped, newest always wins. */
export const POOL_MAX = 8;
/** Default floor between two plays of the SAME id (registry may raise it). */
export const MIN_GAP_MS = 24;
/** Gestures that unlock a suspended context in a plain browser. */
export const UNLOCK_EVENTS = Object.freeze(['pointerdown', 'keydown', 'touchstart', 'wheel']);

const LOG_BUDGET = 40;   // enough to see the shape of a session, not enough to spam

/** No-repeat-last pick over an array. Pure (rnd injectable for the tests). */
export function pickVariant(count, last, rnd) {
  const n = Math.max(1, count | 0);
  if (n === 1) return 0;
  const r = (typeof rnd === 'function') ? rnd : Math.random;
  let v = (r() * n) | 0;
  if (v >= n) v = n - 1;
  if (v === last) v = (v + 1) % n;   // one deterministic step off the repeat
  return v;
}

/** cue trim x master x sfx x house trim -> the gain node's value. Pure. */
export function cueGain(entryGain, master, sfxVol) {
  const g = (typeof entryGain === 'number' && entryGain >= 0) ? entryGain : 0.4;
  const m = (typeof master === 'number' && master >= 0) ? master : 0;
  const s = (typeof sfxVol === 'number' && sfxVol >= 0) ? sfxVol : 0;
  return g * m * s * SFX_TRIM;
}

/**
 * @param {object} [o]
 * @param {object} [o.prefs] ui/prefs.js handle — read for volumes, subscribed for changes
 * @param {object} [o.logger] console-shaped; omit for total silence
 * @param {boolean} [o.trace] log every call (default: only the first of each id)
 */
export function createAudio({ prefs = null, logger = null, trace = false } = {}) {
  const log = logger;
  const seen = new Set();
  let logged = 0;
  let ducked = false;
  let currentMusic = null;
  let disposed = false;

  const vol = {
    master: prefs ? prefs.get('masterVolume') : 0.8,
    music: prefs ? prefs.get('musicVolume') : 0.55,
    sfx: prefs ? prefs.get('sfxVolume') : 0.85,
  };

  // --- the graph (all lazy; nothing here exists until the first cue) --------
  let ctx = null;
  let dead = false;              // no AudioContext in this host: stop trying
  let masterBus = null, sfxBus = null, musicBus = null;
  let unlockHook = null;
  const bufs = new Map();        // url -> AudioBuffer
  const loading = new Map();     // url -> Promise (dedupe)
  const failed = new Set();      // url -> never retry, never spam
  const lastAt = new Map();      // id -> last play timestamp (ms)
  const lastVariant = new Map(); // id -> last variant index
  const livePlays = [];          // {src, g, at} oldest-first — the pool
  const stats = { played: 0, dropped: 0, throttled: 0, stolen: 0, unknown: 0, decoded: 0, failed: 0 };

  const unsubPrefs = prefs ? prefs.subscribe((key, value) => {
    if (key === 'masterVolume') vol.master = value;
    else if (key === 'musicVolume') vol.music = value;
    else if (key === 'sfxVolume') vol.sfx = value;
    applyBusGains();
  }) : () => {};

  function note(what) {
    if (!log || !log.debug) return;
    if (!trace) {
      if (seen.has(what)) return;
      seen.add(what);
      if (++logged > LOG_BUDGET) return;
    }
    log.debug('[GG audio] ' + what);
  }
  function warn(what) {
    if (!log || !log.warn) return;
    if (seen.has('!' + what)) return;
    seen.add('!' + what);
    log.warn('[GG audio] ' + what);
  }

  const now = () => {
    try { return (typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now(); }
    catch (_e) { return Date.now(); }
  };

  /* ------------------------------------------------------------ the context */

  function ensureCtx() {
    if (dead || disposed) return null;
    if (!ctx) {
      const AC = (typeof window !== 'undefined') && (window.AudioContext || window.webkitAudioContext);
      if (!AC) { dead = true; note('no AudioContext in this host — silent'); return null; }
      try { ctx = new AC(); } catch (_e) { dead = true; return null; }
      try {
        masterBus = ctx.createGain();
        sfxBus = ctx.createGain();
        musicBus = ctx.createGain();
        sfxBus.connect(masterBus);
        musicBus.connect(masterBus);
        masterBus.connect(ctx.destination);
      } catch (_e) { dead = true; ctx = null; return null; }
      applyBusGains();
      // A plain browser starts suspended. WebView2 does not (the host passes
      // --autoplay-policy=no-user-gesture-required), so this is usually a no-op.
      unlockHook = () => {
        try { if (ctx && ctx.state === 'suspended') ctx.resume().catch(() => {}); } catch (_e) { /* ignore */ }
      };
      if (typeof window !== 'undefined' && window.addEventListener) {
        for (const ev of UNLOCK_EVENTS) {
          try { window.addEventListener(ev, unlockHook, { capture: true, passive: true }); } catch (_e) { /* ignore */ }
        }
      }
    }
    try { if (ctx.state === 'suspended') ctx.resume().catch(() => {}); } catch (_e) { /* ignore */ }
    return ctx;
  }

  function applyBusGains() {
    try {
      if (masterBus) masterBus.gain.value = Math.max(0, Math.min(1, vol.master));
      // The music bus exists for the API's sake; no bed ships in this pass.
      if (musicBus) musicBus.gain.value = Math.max(0, Math.min(1, vol.music)) * (ducked ? 0.25 : 1);
      // Per-cue gain already carries vol.sfx, so this bus stays unity — one
      // slider must not be applied twice.
      if (sfxBus) sfxBus.gain.value = 1;
    } catch (_e) { /* ignore */ }
  }

  /* -------------------------------------------------------------- decoding */

  function load(url) {
    if (bufs.has(url) || failed.has(url)) return;
    if (loading.has(url)) return;
    const c = ctx;
    if (!c || typeof fetch !== 'function') return;
    const p = (async () => {
      try {
        const r = await fetch(url);
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const ab = await r.arrayBuffer();
        const buf = await c.decodeAudioData(ab);
        bufs.set(url, buf);
        stats.decoded++;
      } catch (e) {
        failed.add(url);
        stats.failed++;
        warn('asset failed: ' + url + ' (' + ((e && e.message) || e) + ')');
      } finally {
        loading.delete(url);
      }
    })();
    loading.set(url, p);
  }

  /** Fetch+decode a set of ids ahead of time (called on unlock for the hot ones). */
  function warm(ids) {
    const c = ensureCtx();
    if (!c) return 0;
    const list = Array.isArray(ids) && ids.length ? ids : SFX_IDS;
    let n = 0;
    for (const id of list) {
      const entry = SFX_REGISTRY[id];
      if (!entry) continue;
      for (const url of entry.files) { load(url); n++; }
    }
    return n;
  }

  /* ------------------------------------------------------------- the pool */

  function reap() {
    const t = now();
    for (let i = livePlays.length - 1; i >= 0; i--) {
      if (livePlays[i].done || t - livePlays[i].at > 12000) livePlays.splice(i, 1);
    }
  }

  function steal() {
    const oldest = livePlays.shift();
    if (!oldest) return;
    stats.stolen++;
    try {
      const t = ctx.currentTime;
      oldest.g.gain.cancelScheduledValues(t);
      oldest.g.gain.setValueAtTime(oldest.g.gain.value, t);
      oldest.g.gain.linearRampToValueAtTime(0.0001, t + 0.03);
      oldest.src.stop(t + 0.04);
    } catch (_e) { try { oldest.src.stop(); } catch (_e2) { /* gone */ } }
  }

  /* -------------------------------------------------------------- the API */

  const api = {
    /** True once the real graph can exist in this host (false under node). */
    get isReal() {
      if (dead) return false;
      if (ctx) return true;
      return typeof window !== 'undefined' && !!(window.AudioContext || window.webkitAudioContext);
    },
    get volumes() { return Object.assign({}, vol); },
    get isDucked() { return ducked; },
    /** Name of the bed that would be playing. NOT `music` — that is the verb. */
    get currentMusic() { return currentMusic; },
    /** Diagnostics for the headless probes and the self-test. Never load-bearing. */
    get stats() { return Object.assign({}, stats, { live: livePlays.length, buffers: bufs.size }); },
    get contextState() { try { return ctx ? ctx.state : 'none'; } catch (_e) { return 'none'; } },

    /**
     * One-shot cue. An unknown id is a logged warning and nothing else; a known
     * id whose asset has not decoded yet is silently skipped (the NEXT one will
     * have it). Never throws, never queues.
     */
    sfx(id) {
      if (disposed || !id) return false;
      const entry = SFX_REGISTRY[id];
      if (!entry) { stats.unknown++; warn('unknown sfx id "' + id + '" — nothing registered'); return false; }
      const c = ensureCtx();
      if (!c) { stats.dropped++; return false; }

      const t = now();
      const gap = (typeof entry.minGapMs === 'number') ? entry.minGapMs : MIN_GAP_MS;
      const prev = lastAt.get(id);
      if (prev != null && t - prev < gap) { stats.throttled++; return false; }

      // Warm on demand: the first ask for an id kicks its fetch and stays quiet.
      let ready = null;
      let readyIdx = -1;
      const idx = pickVariant(entry.files.length, lastVariant.has(id) ? lastVariant.get(id) : -1);
      const order = [idx];
      for (let k = 0; k < entry.files.length; k++) if (k !== idx) order.push(k);
      for (const k of order) {
        const url = entry.files[k];
        if (bufs.has(url)) { ready = bufs.get(url); readyIdx = k; break; }
        load(url);
      }
      if (!ready) { stats.dropped++; return false; }
      // A suspended context would accept start() and play it whenever the user
      // finally clicks. Drop it instead — a cue is only true when it is timely.
      if (c.state !== 'running') { stats.dropped++; return false; }

      lastAt.set(id, t);
      lastVariant.set(id, readyIdx);

      reap();
      while (livePlays.length >= POOL_MAX) steal();

      try {
        const src = c.createBufferSource();
        src.buffer = ready;
        const g = c.createGain();
        g.gain.value = cueGain(entry.gain, vol.master, vol.sfx);
        src.connect(g);
        g.connect(sfxBus || c.destination);
        const rec = { src, g, at: t, done: false };
        try { src.onended = () => { rec.done = true; }; } catch (_e) { /* ignore */ }
        src.start();
        livePlays.push(rec);
        stats.played++;
        note('sfx:' + id);
        return true;
      } catch (_e) { stats.dropped++; return false; }
    },

    /** Force the context up (call from a real gesture). Returns the state. */
    unlock() {
      const c = ensureCtx();
      if (!c) return 'none';
      // The pops are the cue that must never be late; everything else can decode
      // on its first ask.
      warm(['bubble-pop', 'bubble-pop-fx', 'bubble-pop-video', 'gg-drop', 'ui-select']);
      try { return c.state; } catch (_e) { return 'none'; }
    },

    /** Fetch+decode ahead of time. No args = the whole registry. */
    warm(ids) { return warm(ids); },

    /** Start (or cross-fade to) a music bed. `null` is the same as stopMusic().
     *  BOOKKEEPING ONLY: no bed ships in this pass (see the report's note on
     *  /dtrh/assets/audio/drone1.mp3), and a screen calling this is not a bug. */
    music(name) {
      if (disposed) return;
      if (!name) { api.stopMusic(); return; }
      if (currentMusic === name) return;
      currentMusic = String(name);
      note('music:' + currentMusic + ' (no bed wired)');
    },

    stopMusic() {
      if (disposed || currentMusic === null) return;
      note('music:stop');
      currentMusic = null;
    },

    /** Ride the music bus down under speech/lock cards. Idempotent. */
    duck(on) {
      if (disposed) return;
      const next = !!on;
      if (next === ducked) return;
      ducked = next;
      applyBusGains();
      note('duck:' + (ducked ? 'on' : 'off'));
    },

    /** Direct volume write (options drawer drives prefs, which drives us). */
    setVolume(bus, value) {
      const v = Math.max(0, Math.min(1, Number(value) || 0));
      if (bus === 'master' || bus === 'music' || bus === 'sfx') vol[bus] = v;
      applyBusGains();
      if (prefs) prefs.set(bus + 'Volume', v);
    },

    dispose() {
      if (disposed) return;
      disposed = true;
      api.stopMusic();
      try { unsubPrefs(); } catch (_e) { /* ignore */ }
      if (unlockHook && typeof window !== 'undefined' && window.removeEventListener) {
        for (const ev of UNLOCK_EVENTS) {
          try { window.removeEventListener(ev, unlockHook, { capture: true }); } catch (_e) { /* ignore */ }
        }
      }
      for (const rec of livePlays.splice(0)) { try { rec.src.stop(); } catch (_e) { /* gone */ } }
      try { if (ctx && typeof ctx.close === 'function') ctx.close(); } catch (_e) { /* ignore */ }
      ctx = null;
    },
  };

  return api;
}

export default createAudio;
