/* ============================================================================
 * ui/audio.js — the sound bus. REAL as of the sfx pass.
 *
 *   ┌──────────────────────────────────────────────────────────────────────┐
 *   │ ONE WebAudio graph, five gain nodes:                                  │
 *   │     cue -> perCueGain(trim) -> uiBus   -\                             │
 *   │                                gameBus --\                            │
 *   │                                musicBus ---> masterBus -> destination │
 *   │                                droneBus -/                            │
 *   │ `sfx(id)` looks the id up in SFX_REGISTRY, lazily fetch+decodes its    │
 *   │ mp3 variants, and plays ONE of them through the pool. An UNKNOWN id is │
 *   │ a dev-time warning through the logger, never a silent fallback tone.   │
 *   │ Nothing here can throw and nothing here blocks, so no screen has to    │
 *   │ branch on "is audio real" — every call site stayed exactly as it was.  │
 *   └──────────────────────────────────────────────────────────────────────┘
 *
 * ONE SLIDER PER NODE, AND EACH APPLIED EXACTLY ONCE. The per-cue gain carries
 * the cue's own trim and the house trim and NOTHING ELSE; the player's sliders
 * are the buses it feeds. That is what makes a drag retune a sound that is
 * already in the air (the drone bed, a long pop tail) instead of only the next
 * thing to start, and it is why cueGain() lost its master/sfx arguments in the
 * split: the old shape multiplied `master` into the cue AND into masterBus, so
 * a master of 0.5 was really 0.25 for cues and 0.5 for the bed. Same rule as
 * ui/droneBed.js's staging note: multiplied, never conflated.
 *
 * TWO CUE BUSES, because "sfx" was one slider over two different jobs. `ui` is
 * the chrome you drive — menu ticks, the code cells, the lamps — and `game` is
 * everything that happens to you inside a match: the draft, the countdown,
 * charges, drops, payloads, bubbles, the recap. A player who wants the match
 * loud and the menus quiet (or the reverse, on a phone at 2am) could not say so
 * with one number. Every SFX_REGISTRY entry declares which one it is on.
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
 * THE DRONE BED (ui/droneBed.js) is the one CONTINUOUS voice on this graph: a
 * low binaural pair ported from the Intake's synthesized bed, running from the
 * countdown to the recap. It gets its own bus rather than riding musicBus so the
 * player has a slider for it alone, and it sits UNDER masterBus so the master
 * slider still scales everything. `dronePhase(phase)` is the whole public
 * lifecycle — boot.js's phase router calls it and nothing else needs to know.
 *
 * A BED IS NOT A CUE, and the autoplay rule is the opposite one. A one-shot
 * fired before the unlock is DROPPED (a stale pop three seconds late is worse
 * than a missing one); the bed is started anyway. Its fade is scheduled from
 * ctx.currentTime, which does not advance while the context is suspended, so the
 * ramp begins exactly when the gesture lands — late, continuous, and never a
 * burst, because every ramp starts from the gain's current value.
 *
 * VOLUME comes from ui/prefs.js (masterVolume / musicVolume / uiVolume /
 * gameVolume / droneVolume / mediaVolume — the sliders ui/options.js already
 * draws) and is tracked live, so a drag retunes the cue bus, and the bed that is
 * ALREADY playing, with no settings UI of our own. An older player's single
 * `sfxVolume` seeds both cue sliders once, in ui/prefs.js — see MIGRATIONS there.
 *
 * MEDIA IS NOT ON THIS GRAPH. The opponent's payload videos are <video> elements
 * exec/videos.js owns, and their sound is an element `.volume`, not a node: a
 * MediaElementSource would need CORS on every file and would take the picture
 * down with the graph. So `mediaVolume` is a number this tier PUBLISHES rather
 * than a bus it mixes — ui/prefs.js reflects the EFFECTIVE value (media x master)
 * onto <html data-gg-mediavol>, exactly as it already does for skippable videos
 * and shader spirals, because exec/ never imports ui/. `mediaVolume()` below is
 * the same number for anyone on this side of the fence.
 *
 * Import-safe under node: no AudioContext, no fetch, no DOM at import time.
 * ==========================================================================*/

import {
  createDroneBed, droneWantsPlay, DRONE_FADE_IN_SEC, DRONE_FADE_OUT_SEC, DRONE_GLIDE_SEC,
} from './droneBed.js';
// The media number is a PREF that leaves the UI tier, so ui/prefs.js owns both
// the arithmetic and the <html> attribute it is published on (it is the one
// writer for every such pref). This tier only needs to be able to answer with
// the same number — see mediaVolume() on the api.
import { mediaGain } from './prefs.js';

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
 * THE CUE BUSES — two, and every registry entry declares one.
 *
 * The split is by WHO CAUSED THE SOUND, not by what it sounds like: `ui` is the
 * chrome the player is driving with their own hands (a menu, a sheet, the code
 * cells, the lamps) and `game` is everything the match does at them. That is the
 * line a player can actually hold in their head while dragging a slider, and it
 * is the reason 'ui-error' is UI even though it is a buzz and 'lock-slip' is
 * GAME even though it is the same kind of buzz — one is a form refusing you, the
 * other is the duel.
 * -------------------------------------------------------------------------- */
export const BUS_UI = 'ui';
export const BUS_GAME = 'game';
/** The player-facing cue buses, in slider order. Frozen: two ifs would drift. */
export const SFX_BUSES = Object.freeze([BUS_UI, BUS_GAME]);
/** What an entry with no (or a nonsense) bus is treated as. Almost everything in
 *  this registry is match audio, so an unlabelled cue belongs with the match
 *  rather than under the menus slider — and the self-test fails on one anyway. */
export const DEFAULT_SFX_BUS = BUS_GAME;

/* ----------------------------------------------------------------------------
 * THE REGISTRY — id -> { bus, files, gain, minGapMs? }.
 *
 * `files` are ALREADY-RESOLVED urls; more than one is a no-repeat-last rotation.
 * `gain` is the cue's own trim (house loudness), multiplied by SFX_TRIM — and by
 * NOTHING ELSE, because the player's sliders are the buses this feeds (see the
 * staging note in the banner). Everything here is deliberately quiet: this is an
 * ambience-heavy game and the motion budget has a volume twin.
 *
 * `bus` is stamped by the two wrappers below rather than typed forty times, so
 * the section headers are executable instead of decorative and a cue cannot be
 * pasted into the wrong section and still sound like the section it landed in.
 * -------------------------------------------------------------------------- */
const D = dtrhSfxUrl;
const L = localSfxUrl;
/** Chrome the player drives. */
const uiCue = (e) => Object.assign({ bus: BUS_UI }, e);
/** Anything the match does at them. */
const gameCue = (e) => Object.assign({ bus: BUS_GAME }, e);

export const SFX_REGISTRY = Object.freeze({
  /* ---- chrome: menus, sheets, code entry (screens/*, router.button) --- ui - */
  'ui-move':        uiCue({ files: [L('custody-log-tick-1.mp3'), L('custody-log-tick-2.mp3')], gain: 0.18, minGapMs: 45 }),
  'ui-select':      uiCue({ files: [L('checkbox-tick-1.mp3')],   gain: 0.35 }),
  'ui-back':        uiCue({ files: [L('verify-rewind-1.mp3'), L('verify-rewind-2.mp3')], gain: 0.30 }),
  'ui-error':       uiCue({ files: [L('error-blip-1.mp3')],      gain: 0.40 }),
  'code-cell':      uiCue({ files: [L('checkbox-tick-1.mp3')],   gain: 0.30 }),
  'code-copy':      uiCue({ files: [L('sticker-drag-1.mp3')],    gain: 0.30 }),
  'lamp-confirm':   uiCue({ files: [L('verify-lock-1.mp3')],     gain: 0.38 }),
  'lamp-clear':     uiCue({ files: [L('grid-settle-1.mp3')],     gain: 0.28 }),

  /* ---- draft -------------------------------------------------------- game - */
  'draft-pick':     gameCue({ files: [L('captcha-logged-1.mp3')],  gain: 0.34 }),
  'draft-drop':     gameCue({ files: [L('captcha-rewarp-1.mp3')],  gain: 0.28 }),
  'draft-lock':     gameCue({ files: [L('verify-lock-1.mp3')],     gain: 0.42 }),

  /* ---- match start / clock ------------------------------------------ game - */
  'countdown-tick': gameCue({ files: [L('verify-tick-1.mp3'), L('verify-tick-2.mp3')], gain: 0.40 }),
  'countdown-go':   gameCue({ files: [L('verify-resolve-1.mp3')],  gain: 0.55 }),
  'gg-tick':        gameCue({ files: [L('verify-tick-1.mp3'), L('verify-tick-2.mp3')], gain: 0.26 }),
  'gg-go':          gameCue({ files: [L('verify-resolve-1.mp3')],  gain: 0.50 }),

  /* ---- the economy -------------------------------------------------- game - */
  'gg-charge':      gameCue({ files: [L('chime-1.mp3')],           gain: 0.30 }),
  'charge-earned':  gameCue({ files: [L('chime-1.mp3')],           gain: 0.30 }),
  /** A bubble dropped an item and the slot lit up. The payoff cue of the loop. */
  'gg-drop':        gameCue({ files: [L('slip_dling.mp3')],        gain: 0.42 }),
  /** ...and the same roll refused because the arsenal is already full. A dud. */
  'gg-drop-dud':    gameCue({ files: [L('captcha-reject-1.mp3'), L('captcha-reject-2.mp3')], gain: 0.20, minGapMs: 220 }),

  /* ---- payloads ----------------------------------------------------- game - */
  'gg-fire':        gameCue({ files: [L('custody-stamp-1.mp3')],   gain: 0.42 }),
  'payload-out':    gameCue({ files: [L('custody-stamp-1.mp3')],   gain: 0.42 }),
  /** ONE landing cue for every family. Per-kind stings would be noise. */
  'payload-in':     gameCue({ files: [L('stamp_thud.mp3')],        gain: 0.38 }),
  'gg-endured':     gameCue({ files: [D('chime2.mp3')],            gain: 0.34 }),

  /* ---- the two of you ----------------------------------------------- game - */
  'gg-emote':       gameCue({ files: [L('chime-1.mp3')],           gain: 0.24, minGapMs: 160 }),
  'gg-check':       gameCue({ files: [L('gaze-lock-1.mp3')],       gain: 0.34 }),
  'gg-check-ok':    gameCue({ files: [L('captcha-verify-ok-1.mp3')], gain: 0.38 }),
  'gg-taunt-up':    gameCue({ files: [L('gaze-lock-2.mp3')],       gain: 0.22, minGapMs: 400 }),
  /** The safety valve. A soft bloom, NOT a sting — nothing about pressing it
   *  should read as a punishment. It is a MATCH sound, not a menu one: it is the
   *  duel ending, and it must never be the thing a muted-menus player misses. */
  'gg-mercy':       gameCue({ files: [L('surface-bloom-1.mp3')],   gain: 0.40 }),
  'gg-victory':     gameCue({ files: [L('giggle-7.mp3')],          gain: 0.34 }),
  /** The ribbon sliding in. Deliberately under every pop — it is a caption. */
  'announce-in':    gameCue({ files: [L('briefing-open-1.mp3')],   gain: 0.20, minGapMs: 500 }),

  /* ---- the field ---------------------------------------------------- game - */
  'bubble-pop':     gameCue({ files: [D('Pop2.mp3'), D('Pop3.mp3')], gain: 0.34, minGapMs: 24 }),
  /** An EFFECT bubble is juicier than a plain one: bigger sample, more gain. */
  'bubble-pop-fx':  gameCue({ files: [D('Pop.mp3')],               gain: 0.46, minGapMs: 24 }),
  /** ...and the prism/video bubble is hollower still, because it earns a window. */
  'bubble-pop-video': gameCue({ files: [L('void_pop.mp3')],        gain: 0.44, minGapMs: 60 }),
  'flash':          gameCue({ files: [L('grid-tile-flicker-1.mp3'), L('grid-tile-flicker-2.mp3')], gain: 0.16, minGapMs: 260 }),
  'flash-pop':      gameCue({ files: [D('Pop3.mp3')],              gain: 0.30, minGapMs: 24 }),
  /** Barely there on purpose — a word you half-hear is the whole point. */
  'subliminal':     gameCue({ files: [L('custody-log-tick-1.mp3'), L('custody-log-tick-2.mp3')], gain: 0.10, minGapMs: 300 }),
  /** The lock card is a match payload, so its chime and its buzz are GAME even
   *  though the player is typing — the keyboard is not chrome here. */
  'lock-solved':    gameCue({ files: [D('chime1.mp3')],            gain: 0.40 }),
  'lock-slip':      gameCue({ files: [L('error-blip-1.mp3')],      gain: 0.24, minGapMs: 90 }),

  /* ---- the recap ---------------------------------------------------- game - */
  'recap-reveal':   gameCue({ files: [L('ticket_reveal.mp3')],     gain: 0.45 }),
  'recap-won':      gameCue({ files: [D('GG.mp3')],                gain: 0.45 }),
  'recap-lost':     gameCue({ files: [L('freeze-sting-1.mp3')],    gain: 0.38 }),
  'recap-draw':     gameCue({ files: [L('rank_settle.mp3')],       gain: 0.36 }),
  'title-unlock':   gameCue({ files: [L('gold_toggle.mp3')],       gain: 0.45 }),

  /* ---- registered for LOCKED renderers (exec/videos.js, exec/spiral.js) ----
   * Those files belong to another pass; the ids are live here so the one-line
   * play call can land there without touching this file again. GAME, both of
   * them: they are the sound of a payload arriving, not of a menu. */
  'video-window-in':  gameCue({ files: [L('briefing-open-1.mp3')], gain: 0.30, minGapMs: 120 }),
  'video-window-out': gameCue({ files: [L('card-melt-1.mp3'), L('card-melt-2.mp3')], gain: 0.26, minGapMs: 120 }),
  'spiral-in':        gameCue({ files: [L('loom-spiral-up-1.mp3')],   gain: 0.16 }),
  'spiral-out':       gameCue({ files: [L('loom-spiral-down-1.mp3')], gain: 0.16 }),
});

/** Every sfx id the game may ask for. Derived, so the two can never drift. */
export const SFX_IDS = Object.freeze(Object.keys(SFX_REGISTRY));

/** Which cue bus an id plays on. Pure, total: an unknown id answers the default
 *  rather than throwing, because a missing cue is already a logged warning and
 *  must not become a second failure mode on the mixer. */
export function busOf(id) {
  const entry = SFX_REGISTRY[id];
  const bus = entry && entry.bus;
  return (bus === BUS_UI || bus === BUS_GAME) ? bus : DEFAULT_SFX_BUS;
}

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
/** Glide for a CUE bus under a slider drag. Short — these buses carry one-shots,
 *  so this is only there to stop a click mid-pop, not to make a musical fade. */
export const BUS_GLIDE_SEC = 0.08;

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

/**
 * cue trim x house trim -> the per-cue gain node's value. Pure.
 *
 * THE PLAYER'S SLIDERS ARE NOT IN HERE. They are the buses this node feeds
 * (uiBus/gameBus, then masterBus), so each is applied exactly once and a drag
 * lands on sound that is already playing. The pre-split version took `master`
 * and `sfx` as arguments AND left masterBus at the master volume, which squared
 * it: 0.5 on the dial was 0.25 for every cue and 0.5 for the drone bed.
 */
export function cueGain(entryGain) {
  const g = (typeof entryGain === 'number' && entryGain >= 0) ? entryGain : 0.4;
  return g * SFX_TRIM;
}

/**
 * What a cue on `bus` actually comes out at, end to end. Pure, and exported for
 * the self-test rather than for the graph — the graph gets the same number by
 * multiplying three nodes, and this is the arithmetic that says they agree.
 */
export function busGain(entryGain, busVol, master) {
  const b = (typeof busVol === 'number' && busVol >= 0) ? Math.min(1, busVol) : 0;
  const m = (typeof master === 'number' && master >= 0) ? Math.min(1, master) : 0;
  return cueGain(entryGain) * b * m;
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

  // One entry per slider, and the key IS the bus name setVolume() takes (which
  // is what lets ui/options.js hand us `key.replace('Volume','')` unexamined).
  // `media` is in here even though it is not a node: the mixer is the honest
  // place to read "what is the media volume right now" from, and this tier is
  // the one that gets told when it changes.
  const vol = {
    master: prefs ? prefs.get('masterVolume') : 0.8,
    music: prefs ? prefs.get('musicVolume') : 0.55,
    ui: prefs ? prefs.get('uiVolume') : 0.85,
    game: prefs ? prefs.get('gameVolume') : 0.85,
    drone: prefs ? prefs.get('droneVolume') : 0.5,
    media: prefs ? prefs.get('mediaVolume') : 0.8,
  };

  // --- the graph (all lazy; nothing here exists until the first cue) --------
  let ctx = null;
  let dead = false;              // no AudioContext in this host: stop trying
  let masterBus = null, uiBus = null, gameBus = null, musicBus = null, droneBus = null;
  let unlockHook = null;
  let stateHook = null;
  // THE BED. `droneWanted` is the latch the phase router writes; `drone` is the
  // live graph, which may not exist yet (no context in this host, or the player
  // has not made a gesture). The two are separate so a bed asked for before the
  // context came up is picked up the moment it does, rather than lost.
  let drone = null;
  let droneWanted = false;
  const bufs = new Map();        // url -> AudioBuffer
  const loading = new Map();     // url -> Promise (dedupe)
  const failed = new Set();      // url -> never retry, never spam
  const lastAt = new Map();      // id -> last play timestamp (ms)
  const lastVariant = new Map(); // id -> last variant index
  const livePlays = [];          // {src, g, at} oldest-first — the pool
  const stats = { played: 0, dropped: 0, throttled: 0, stolen: 0, unknown: 0, decoded: 0, failed: 0 };

  /** pref key -> the bus it drives. One table, so a new slider is one line. */
  const VOL_KEYS = {
    masterVolume: 'master',
    musicVolume: 'music',
    uiVolume: 'ui',
    gameVolume: 'game',
    droneVolume: 'drone',
    mediaVolume: 'media',
  };

  const mediaListeners = new Set();

  const unsubPrefs = prefs ? prefs.subscribe((key, value) => {
    const bus = VOL_KEYS[key];
    if (!bus) return;
    vol[bus] = value;
    applyBusGains();
    // Media is not on the graph, so applyBusGains has nothing to do for it —
    // the people who care are told instead.
    if (bus === 'media' || bus === 'master') emitMedia();
  }) : () => {};

  function emitMedia() {
    const v = api.mediaVolume();
    for (const fn of Array.from(mediaListeners)) {
      try { fn(v); } catch (_e) { /* a listener must not break the mixer */ }
    }
  }

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
        uiBus = ctx.createGain();
        gameBus = ctx.createGain();
        musicBus = ctx.createGain();
        droneBus = ctx.createGain();
        uiBus.connect(masterBus);
        gameBus.connect(masterBus);
        musicBus.connect(masterBus);
        droneBus.connect(masterBus);
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
      // ...and the bed is RE-ARMED when the context actually comes up, not just
      // asked for once. Its fade is scheduled from ctx.currentTime, which is
      // frozen while suspended, so this is belt to that braces — and re-ramping
      // from the current gain can never make a burst.
      stateHook = () => { try { if (ctx && ctx.state === 'running' && droneWanted) syncDrone(); } catch (_e) { /* ignore */ } };
      try { ctx.addEventListener ? ctx.addEventListener('statechange', stateHook) : (ctx.onstatechange = stateHook); }
      catch (_e) { /* a host without the event simply starts the bed on its next phase */ }
    }
    try { if (ctx.state === 'suspended') ctx.resume().catch(() => {}); } catch (_e) { /* ignore */ }
    return ctx;
  }

  const clamp01 = (v) => Math.max(0, Math.min(1, (typeof v === 'number' && isFinite(v)) ? v : 0));

  function applyBusGains() {
    try {
      if (masterBus) masterBus.gain.value = clamp01(vol.master);
      // The music bus exists for the API's sake; no MUSIC bed ships (the drone
      // is a separate bus with a separate slider, and it is not ducked).
      if (musicBus) musicBus.gain.value = clamp01(vol.music) * (ducked ? 0.25 : 1);
      // THE TWO CUE BUSES carry the player's sliders and the per-cue gain carries
      // only the trim, so each number is applied exactly once (see cueGain).
      // They GLIDE for the same reason the bed does — a drag mid-pop should not
      // click — just over a much shorter constant, because nothing here sustains.
      if (uiBus) glide(uiBus.gain, clamp01(vol.ui), BUS_GLIDE_SEC);
      if (gameBus) glide(gameBus.gain, clamp01(vol.game), BUS_GLIDE_SEC);
      // THE BED IS THE ONE THAT REALLY NEEDS IT. It is playing while the slider
      // moves, so a stepped write would zipper on every pixel of the drag (the
      // Intake's pref buses use setTargetAtTime for exactly this reason).
      if (droneBus) glide(droneBus.gain, clamp01(vol.drone), DRONE_GLIDE_SEC);
    } catch (_e) { /* ignore */ }
  }

  /** The bus node a cue id plays into, or the destination if there is no graph. */
  function busFor(id, c) {
    const b = busOf(id) === BUS_UI ? uiBus : gameBus;
    return b || (c && c.destination) || null;
  }

  /** Smooth a param toward `value`; a straight write if the context cannot ramp. */
  function glide(param, value, sec) {
    try {
      if (!ctx) { param.value = value; return; }
      const t = ctx.currentTime;
      param.cancelScheduledValues(t);
      param.setValueAtTime(param.value, t);
      param.setTargetAtTime(value, t, Math.max(0.02, sec / 3));
    } catch (_e) {
      try { param.value = value; } catch (_e2) { /* ignore */ }
    }
  }

  /** Bring the live bed in line with the `droneWanted` latch. Idempotent: a bed
   *  that is already up is re-ramped, never rebuilt (the relay-rebuild case). */
  function syncDrone() {
    if (disposed) return false;
    if (!droneWanted) {
      if (drone) drone.stop(DRONE_FADE_OUT_SEC);
      return false;
    }
    const c = ensureCtx();
    if (!c) return false;               // no context in this host — the latch waits
    if (!drone) drone = createDroneBed({ ctx: c, out: droneBus || c.destination, logger: log });
    return drone.start(DRONE_FADE_IN_SEC);
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
        g.gain.value = cueGain(entry.gain);
        src.connect(g);
        g.connect(busFor(id, c));
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

    /* ---------------------------------------------------------- the drone bed
     * ONE entry point, and it takes the engine's phase rather than a verb: the
     * phase is already the single source of truth for what is on screen, so the
     * bed cannot drift out of step with the match by having its own state
     * machine. boot.js's onPhase() calls this and nothing else has to.
     *
     * Every road matters here. A relay rebuild re-attaches the match and re-fires
     * the phase, so this arrives again mid-Live: droneWanted is already true and
     * syncDrone() re-ramps the SAME oscillator pair. Practice is a real match over
     * a loopback pair, so it runs the same phases and gets the same bed. */
    dronePhase(phase) {
      if (disposed) return false;
      const want = droneWantsPlay(phase);
      if (want === droneWanted) { if (want) syncDrone(); return want; }
      droneWanted = want;
      note('drone:' + (want ? 'on' : 'off') + ' (phase ' + phase + ')');
      syncDrone();
      return want;
    },

    /** Force it, for a host with no phases to watch (probes, the self-test). */
    startDrone() { droneWanted = true; return syncDrone(); },
    stopDrone() { droneWanted = false; syncDrone(); return false; },
    /** True when the phase says it should play (whether or not a graph exists). */
    get droneWanted() { return droneWanted; },
    /** True only when real oscillators are running. False under node, always. */
    get droneIsPlaying() { return !!(drone && drone.isPlaying); },

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
      if (!Object.prototype.hasOwnProperty.call(vol, bus)) return;
      vol[bus] = v;
      applyBusGains();
      if (prefs) prefs.set(bus + 'Volume', v);
      if (bus === 'media' || bus === 'master') emitMedia();
    },

    /* ------------------------------------------------------------ media sound
     * The EFFECTIVE volume of the opponent's payload videos, 0..1, right now.
     * A function rather than a getter because it is read from the other side of
     * a fence: exec/ never imports ui/, so exec/videos.js takes the same number
     * off <html data-gg-mediavol> (ui/prefs.js writes it), and anything on THIS
     * side calls here instead of re-deriving `media x master` for itself.
     *
     * It is a plain multiply for the same reason the drone's is: two sliders,
     * multiplied, never conflated. The videos are not on the WebAudio graph at
     * all (see the banner), so master cannot reach them through masterBus and
     * has to be folded in here or a muted master would not mute them. */
    mediaVolume() { return mediaGain(vol.media, vol.master); },

    /** fn(effectiveVolume) whenever the media number moves -> unsubscribe. */
    onMediaVolume(fn) {
      if (typeof fn !== 'function') return () => {};
      mediaListeners.add(fn);
      return () => mediaListeners.delete(fn);
    },

    dispose() {
      if (disposed) return;
      disposed = true;
      api.stopMusic();
      droneWanted = false;
      try { drone?.dispose?.(); } catch (_e) { /* ignore */ }
      drone = null;
      mediaListeners.clear();
      try { unsubPrefs(); } catch (_e) { /* ignore */ }
      if (stateHook && ctx) {
        try { ctx.removeEventListener ? ctx.removeEventListener('statechange', stateHook) : (ctx.onstatechange = null); }
        catch (_e) { /* ignore */ }
      }
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
