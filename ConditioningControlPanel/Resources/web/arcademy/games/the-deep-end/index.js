/* ============================================================================
 * games/the-deep-end/index.js - THE DEEP END (2048; family: comfort; the one
 * meaty 300s slot, DECISIONS #3).
 *
 * Tiles are trance-depth tiers and merging them sinks you: the deepest tier on
 * the board IS the heat input to the Distraction Engine, so every success
 * makes the room dreamier and harder to read. Your own progress is the
 * difficulty dial. Low APM, swipe and drift, meant to be soaked in.
 *
 * THE DIVE LOOP (pinned by the contract):
 *   move  = slide -> merges (score, chain, casino light, descending chime)
 *        -> new deepest (casino, reward roll -> jackpot / near-miss, current)
 *        -> ONE seeded spawn -> trickster.afterMove -> heat -> lock check
 *   RESURFACE  a locked board ends the DIVE, never the class: deepest tier
 *              banked, board drains, fresh board, the clock keeps running.
 *              Composite x 0.92 per resurface. A class always fills its bell.
 *   EXHALE     once per dive at 14/16 (22/25) cells: heat x0.6 for 10s and the
 *              next spawn is guaranteed to fit (board.js picks the tier).
 *   CEILING    tier_11 ends the class immediately, warm (DECISIONS #12).
 *   BELL       the real budget, counting down; gold for the last 20s.
 *   FREE SWIM  spec.endless: no budget at all. The clock counts UP, no bell and
 *              no bell warning ever fire, the HUD carries a real Surface BUTTON
 *              that runs the same dim-out and end card, and the CEILING stops
 *              ending anything - the royal fires once per dive and the swim goes
 *              on (tier-11 tiles never merge, so the board fills and resurfaces
 *              like any other dive). Nothing is graded: endClass carries
 *              {endless:true} with an empty ledger and the shell records no row.
 *
 * PASS 2 (owner's playtest): the SLIDE has weight - every moving tile is
 * marked .is-sliding(-<dir>) with its distance so style.js streaks and
 * squashes it, vacated cells blink (.is-wake), the casino leans the bench
 * (casino.slide) and a `slide` whoosh fires scaled by tiles moved and pitched
 * by depth. The WALL - a move that slides nothing - shakes the board into the
 * blocked edge (.g-de-bump-<dir>), the casino flashes that edge (casino.bump)
 * and a `bump` thud plays. STUCK - a 6s stall on an unlocked board pulses the
 * wall cells of every direction that would move (a pure local simulation,
 * board.js untouched). FACES - every tile wears the player's own media
 * (.g-de-face > img.g-de-media), ONE url per tier dealt lazily on the tier's
 * first appearance and frozen for the class; the setting `de_tile_faces`
 * (media | still | plain) and reduced motion / motionLevel <= 1 pick stills.
 * PASS 5 re-dialled the budget: THE SHALLOWS ARE STILL, DEPTH IS ALIVE -
 * tiers 1..SHALLOW_STILL_MAX_TIER (the numerous ones) always wear a still, a
 * loop is dealt only from the next tier down, and only until FACE_CAP
 * DISTINCT ANIMATED TIERS are live (tiers, not tiles: the url is frozen per
 * tier, so every tile of a tier is a decode of the same file). A numeral badge
 * (.g-de-num) carries the tier number - numbers are not words.
 *
 * PASS 3 (owner's second playtest): THE HAND - a press on the board is now a
 * held gesture, not a stopwatch. pointerdown captures the pointer and marks
 * the board .g-de-held (the tiles lift); pointermove writes the clamped drag
 * vector into --de-grab-x/--de-grab-y and, past GRAB_DEAD, marks the dominant
 * direction .g-de-grab-<dir> (plus .g-de-grab-blocked when wouldMove says that
 * wall is solid, so the board resists); pointerup clears all of it and the old
 * SWIPE_PX rule decides the move - the grab offset vanishing and the new --r/--c
 * landing ride the SAME transition, so a release flows straight into the slide.
 * THE QUEUE - a direction pressed while the MOVE_MS lock holds is no longer
 * eaten: one slot, last press wins, drained on the next timer tick after the
 * lock releases (never inside the releasing callback), and dropped whenever the
 * board is not the player's any more (pause, suspend, resurface, new dive,
 * bell, end, destroy). Fast play now flows instead of hitching.
 *
 * PASS 5 (the Chromium trace: GPU main thread at 79% of a core on a 3060 Ti
 * with 16 live video tiles): THE PERF LADDER. `de_perf` = auto | full | lite.
 * `lite` puts .g-de-lite on the stage and .ae-lite on <html> (style.js and
 * engine/style.js read both), halves the animated-tier cap, stills the
 * shallows five deep, stops the face ken-burns, freezes the backdrop pattern
 * drift and drops the engine's shared video budget to 2. `auto` runs the class
 * FULL, samples rAF deltas for ~3s once the board is dealt and demotes ONCE if
 * the median frame is over 20ms (or a quarter of frames over 25ms) - it never
 * promotes back mid-class, because a room that changes its own look twice is
 * worse than a room that is simply lighter. Reduced motion / motionLevel <= 1
 * is lite from the first frame. With no requestAnimationFrame (the headless
 * double) the probe never runs and the class stays FULL.
 *
 * PASS 4 (the owner's third note): THE PRESSURE - the CCP effects ladder and
 * the Balatro tremor live in pressure.js, DECK III. This file owns none of
 * that look; it owns the WIRING. The deck is built after the casino and the
 * trickster, handed the stage/bench/board, the four HUD chips, a read-only
 * view of the engine (fire/sustain/stop plus a READ of the clamped channels),
 * a live reader over the player's asset pool and the game's own pause-aware
 * timer registry; it is then told everything that happens, at the same call
 * sites the casino is told (slide, merge, newDeepest, exhale, resurface,
 * ceiling, bell, dimOut, stop) plus ONE that is its own: setDepth, from heat(),
 * because the RUNG rides the deepest tile while the MAGNITUDE rides heat.
 * THE DEV SEAM: `ctx.dev === true` (only the scratch rig sets it) lets a spec
 * carry `devDeepest` 2..11, and every dive then OPENS on board.js's pure
 * devBoard ladder instead of the two-tile deal - so a rung can be looked at
 * without being played to. Nothing reads it when ctx.dev is absent.
 *
 * LAWS THIS FILE KEEPS:
 *   I   the ledger is honest - score, chain, depth, resurfaces and the clock
 *       are computed here from board.js and never routed through a deck. The
 *       trickster may lie on a chip FACE or a tile NAME; truth is repainted.
 *   II  tiles are never clickable; the board is a gesture surface; every
 *       engine one-shot over it is decoration (fireSafe() welds clickSafe on).
 *   V   spawn stream, plan, casino and trickster are all scoped off the class
 *       seed; a retake replays the identical dive.
 *   VI  pause/resume/suspend/destroy: the timer registry defers, the decks
 *       ride it, no timer survives destroy, the window listener is removed.
 *   VII every string is ctx.lexicon(key, fallback) over lex.js DE_LEX.
 *
 * WHAT THIS FILE DOES NOT OWN: grades (core/grades.js via ctx.endClass), XP
 * (C#), the tier (registry + meta), effect strengths (the engine's ceiling
 * rule), tile transforms and transitions (style.js - we only ever set the
 * --r/--c vars), the lighting (casino.js) and the lies (trickster.js). The
 * pure model is board.js, the seeded plan schedule.js, the composite grade.js,
 * and the ladder + the tremor pressure.js.
 *
 * ENGINE TARGETING NOTE: glitch_swap adds `.ae-glitch{position:relative;
 * animation}` and row_drift writes an inline transform on its targets, so
 * neither may ever touch a .g-de-tile (B owns its transform). The shimmer
 * targets the tile's FACE nodes (.g-de-glyph / .g-de-name) and the drift
 * targets the static .g-de-cell floor, never a tile.
 * ==========================================================================*/

import { injectDeepEndStyle } from './style.js';
import { createDeCasino } from './casino.js';
import { createDeTrickster } from './trickster.js';
import { createDePressure } from './pressure.js';
import {
  createBoard, move as boardMove, spawn as boardSpawn, openingSpawn, devBoard, drain,
  isLocked, deepest as boardDeepest, occupancy, strainPair, serialize, TIER_MAX,
} from './board.js';
import {
  buildPlan, heatCurve, depthLineFor, cadenceMs, sizeFromSetting, PLAYTEST,
} from './schedule.js';
import { compositeFor, hardGates, flavorXp } from './grade.js';
import { DE_LEX } from './lex.js';
import { makeTaggedRoll } from '../../core/rng.js';

/** A url the <img> element cannot show (a webm/mp4 loop). Mirrors engine/util.js
 *  VIDEO_URL_RE; games never import the engine, so the two-line rule is repeated. */
const VIDEO_URL_RE = /\.(mp4|webm|m4v)(\?|#|$)/i;

const GAME_KEY = 'the_deep_end';

const KEY_DIRS = Object.freeze({
  ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right',
  KeyW: 'up', KeyS: 'down', KeyA: 'left', KeyD: 'right',
  w: 'up', s: 'down', a: 'left', d: 'right', W: 'up', S: 'down', A: 'left', D: 'right',
});
const SWIPE_PX = 24;

/** Move direction -> unit vector (x right, y down) + axis class. */
const DIRV = Object.freeze({
  up: { x: 0, y: -1, axis: 'y' }, down: { x: 0, y: 1, axis: 'y' },
  left: { x: -1, y: 0, axis: 'x' }, right: { x: 1, y: 0, axis: 'x' },
});
const SLIDE_CLASSES = Object.freeze(['is-sliding', 'is-sliding-x', 'is-sliding-y',
  'is-sliding-up', 'is-sliding-down', 'is-sliding-left', 'is-sliding-right']);
const BUMP_CLASSES = Object.freeze(['g-de-bump-up', 'g-de-bump-down', 'g-de-bump-left', 'g-de-bump-right']);
/** Pass 3 - THE HAND: at most one of these rides the board at a time. */
const GRAB_CLASSES = Object.freeze(['g-de-grab-up', 'g-de-grab-down', 'g-de-grab-left', 'g-de-grab-right']);
const FACE_MODES = Object.freeze(['media', 'still', 'plain']);
/** PASS 5 - the perf ladder. `auto` is the default and the only value that
 *  may change its mind mid-class (once, downward). */
const PERF_MODES = Object.freeze(['auto', 'full', 'lite']);

/**
 * PURE, LOCAL: would a slide in `dir` move anything? Reads board.tiles and
 * never mutates (board.js is not this file's to call on a clone). A tile
 * moves when there is a gap before it along the line of travel, or when it
 * meets an equal, non-silt, non-ceiling neighbour it would merge with.
 */
export function wouldMove(board, dir) {
  const v = DIRV[dir];
  if (!board || !v || !Array.isArray(board.tiles)) return false;
  const n = Math.max(1, Number(board.n) || 4);
  const g = [];
  for (let r = 0; r < n; r++) g.push(new Array(n).fill(null));
  for (const t of board.tiles) if (t && g[t.r] && t.c >= 0 && t.c < n) g[t.r][t.c] = t;
  for (let i = 0; i < n; i++) {
    let last = null;
    let slot = 0;
    for (let j = 0; j < n; j++) {
      const r = v.y !== 0 ? (v.y > 0 ? n - 1 - j : j) : i;
      const c = v.x !== 0 ? (v.x > 0 ? n - 1 - j : j) : i;
      const t = g[r][c];
      if (!t) continue;
      if (j !== slot) return true;                                   // a gap before it: it slides
      if (last && !last.silt && !t.silt && last.tier === t.tier && t.tier < TIER_MAX) return true;   // a merge
      last = t;
      slot += 1;
    }
  }
  return false;
}

/** Diagnostics seam (DV precedent): the live class, the last report, the
 *  final snapshot. The shell never reads these; the harness does. */
let liveClass = null;
let lastReport = null;
let lastSnapshot = null;

/** Test seam: the scratch harness compresses the clock. Production = 1. */
let timeScale = 1;
export function setTimeScale(f) { const v = Number(f); timeScale = Number.isFinite(v) && v > 0 ? v : 1; }
export function getTimeScale() { return timeScale; }
function scaled(ms) { return Math.max(0, Math.round(ms * timeScale)); }

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = String(text);
  return n;
}

/** Reduced motion from the shell's projection first, then the two probes. */
function probeReduced(ctx) {
  try { if (ctx && ctx.motion && ctx.motion.reducedMotion) return true; } catch (e) { /* ignore */ }
  try {
    if (typeof document !== 'undefined' && document.documentElement
      && document.documentElement.classList
      && document.documentElement.classList.contains('arc-reduced')) return true;
  } catch (e) { /* ignore */ }
  try {
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      const m = window.matchMedia('(prefers-reduced-motion: reduce)');
      if (m && m.matches) return true;
    }
  } catch (e) { /* ignore */ }
  return false;
}

/** A key press that belongs to a form control is never a move. */
function isFormTarget(target) {
  try {
    if (!target) return false;
    const tag = String(target.tagName || '').toUpperCase();
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || tag === 'BUTTON') return true;
    if (target.isContentEditable) return true;
  } catch (e) { /* ignore */ }
  return false;
}

function mmss(secLeft) {
  const s = Math.max(0, secLeft | 0);
  return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');
}

export default {
  key: GAME_KEY,
  family: 'comfort',
  meaty: true,
  flagship: false,
  timeBudgetSec: 300,
  title: 'The Deep End',

  manifest: {
    /* flash_burst is declared ONLY as clickSafe decoration over the board
     * (DECISIONS #9) - fireSafe() below welds that on at every call site. */
    effectsConsumed: [
      'wash', 'ambient_field', 'sub_flash', 'row_drift', 'glitch_swap',
      'audio_trigger', 'gif_burst', 'flash_burst', 'bubble_field',
      /* pass 4 - THE PRESSURE (pressure.js): the CRT ladder and the gif rain
       * a new deepest tier brings down. Everything else it asks for is
       * already in the nine above. */
      'crt', 'gif_rain',
    ],
    /* 24 loops + 12 stills: the tile faces (one per tier, pass 2) and the
     * reward currents; tiles are DOM, nothing is drawn into canvas, so the
     * provider may serve remote media here. */
    assetNeeds: { loops: 24, targets: 0, stills: 12, canvasSafe: false },
    /* The shell's boardSizes row has below-par semantics that are INVERTED
     * here (5x5 is the easier board) - so the size is a plain enum setting
     * and the S-gate is declared by the game (grade.js hardGates). */
    boardSizes: null,
    keybinds: null,
    settings: [
      {
        key: 'de_board_size', kind: 'enum', values: ['4x4', '5x5'], default: '4x4',
        label_key: 'de_board_size', hint_key: 'de_board_size_hint',
      },
      {
        key: 'de_tile_faces', kind: 'enum', values: FACE_MODES.slice(), default: 'media',
        label_key: 'de_tile_faces', hint_key: 'de_tile_faces_hint',
      },
      /* PASS 5 - THE PERF LADDER. `values`, never `options` (CLAUDE.md trap 21:
       * an `options` array renders NO row at all and the setting is stuck on
       * its default forever). `auto` first, so an untouched install probes. */
      {
        key: 'de_perf', kind: 'enum', values: PERF_MODES.slice(), default: 'auto',
        label_key: 'de_perf', hint_key: 'de_perf_hint',
      },
    ],
    peek: false,
    /* FREE SWIM: the campus door card gains a second, secondary button and the
     * shell starts this class with timeBudgetSec 0 + endless:true - no bell, no
     * grade, no row. A game that declares nothing simply shows no button. */
    endless: { label_key: 'de_free_swim', hint_key: 'de_free_swim_hint' },
  },

  create(ctx) {
    const t = (key, fallback) => {
      const fb = fallback == null ? (DE_LEX[key] == null ? key : DE_LEX[key]) : fallback;
      try { const v = ctx.lexicon(key, fb); return v == null ? fb : v; } catch (e) { return fb; }
    };
    const say = (m) => { try { ctx.log('[de] ' + m); } catch (e) { /* noop */ } };

    /* ---- lifecycle flags ------------------------------------------------ */
    let dead = false;
    let paused = false;
    let ended = false;
    let reported = false;
    let busy = true;                       // input closed until the briefing ends

    /* ---- class state ---------------------------------------------------- */
    let spec = null;
    let seed = '';
    let tier = 1;
    let n = 4;
    let plan = null;
    let board = null;
    let reduced = false;
    let retake = false;
    let endless = false;                   // FREE SWIM: no bell, no grade, no row
    let surfaced = false;                  // the Surface button fires once
    let ceilingCelebrated = false;         // the royal fires once per dive
    let ceilingHold = false;               // the royal owns the phase while it runs
    let budgetMs = 300000;
    let rollLocal = null;                  // reward fallback when the engine is absent
    let pool = null;

    let casino = null;
    let trickster = null;
    let pressure = null;                   // pass 4 - THE PRESSURE (the ladder + the tremor)
    /* THE DEV SEAM (pass 4): `?deep=N` in the scratch rig. 0 = off, and it is
     * only ever non-zero when ctx.dev === true. The shell never sets ctx.dev,
     * so production is byte-identical to a tree without this line. */
    let devDeepest = 0;

    /* ---- the ledger (Law I: computed here, never by a deck) ------------- */
    let score = 0;
    let swipes = 0;
    let merges = 0;
    let chainLinks = 0;
    let chain = 0;
    let maxChain = 0;
    let dives = 0;
    let resurfaces = 0;
    let diveDeepest = 0;
    let bestDeepest = 0;
    let lifetimeBefore = 0;
    let exhaleUsed = false;
    let exhalePending = false;
    let exhaleOn = false;
    let bellOn = false;
    let ceilingReached = false;
    let survived = false;
    let siltSeen = false;
    let jackpots = 0;
    let currents = 0;
    let subFlashes = 0;
    let shimmers = 0;
    let strains = 0;
    let stallMs = 0;
    let lastStrainKey = '';
    let lastStrainAt = -Infinity;
    let currentHeat = 0;
    let driftOn = false;
    let subIdx = 0;
    let shimmerIdx = 0;
    let currentIdx = 0;
    /* pass 2 */
    let facesMode = 'media';               // de_tile_faces: media | still | plain
    let faceKind = 'loop';                 // what a NEW tier's face is dealt as
    const faceUrls = new Map();            // tier -> {url, kind} | {broken:true}, frozen per class
    let faceLogged = false;
    let capLogged = false;
    let stuckShown = false;
    /* pass 5 - THE PERF LADDER */
    let perfSetting = 'auto';              // de_perf: auto | full | lite
    let perf = 'full';                     // the RESOLVED level this class runs at
    let perfReason = '';                   // why (diagnostics + the log line)
    let perfProbeDone = false;             // the probe fires once per class
    let perfLogged = false;
    let stuckHints = 0;
    let slides = 0;
    let bumps = 0;
    let slideTimer = 0;
    let wakeTimer = 0;
    let bumpTimer = 0;
    let hintTimer = 0;
    /* pass 3 */
    let opened = false;                    // the briefing is over: the water is the player's
    let queued = null;                     // THE QUEUE: one slot, last press wins
    let queueTimer = 0;
    let drained = 0;                       // moves the queue fired

    /* ---- clock ---------------------------------------------------------- */
    let clockId = 0;
    let lastTick = 0;
    let elapsedMs = 0;

    /* ---- dom ------------------------------------------------------------ */
    let stage = null; let backdrop = null; let hud = null; let bench = null;
    let boardEl = null; let well = null; let msgEl = null; let endEl = null;
    let depthChip = null; let clockChip = null; let scoreChip = null; let chainChip = null;
    let surfaceBtn = null;                 // the free swim's own way out (endless only)
    const cellEls = [];
    const tileEls = new Map();             // tile id -> element
    let subTimer = 0;
    let shimmerTimer = 0;
    let stallTimer = 0;
    /* THE HAND's live gesture: {id, x, y, gx, gy, dir, blocked, captured} or null */
    let grab = null;

    /* ==================================================================== *
     * TIMERS - every step goes through run() so a suspend freezes the class
     * mid-move and a resume finishes it. `every` simply skips while paused.
     * ==================================================================== */
    const timers = new Map();
    let nextTimerId = 1;
    const deferred = [];
    function run(fn) {
      if (dead) return;
      if (paused) { deferred.push(fn); return; }
      try { fn(); } catch (e) { say('step failed: ' + ((e && e.message) || e)); }
    }
    function after(ms, fn) {
      const id = nextTimerId++;
      const h = setTimeout(() => { timers.delete(id); run(fn); }, scaled(ms));
      timers.set(id, { kind: 'after', h });
      return id;
    }
    function every(ms, fn) {
      const id = nextTimerId++;
      const h = setInterval(() => {
        if (dead || paused) return;
        try { fn(); } catch (e) { say('tick failed: ' + ((e && e.message) || e)); }
      }, Math.max(4, scaled(ms)));
      timers.set(id, { kind: 'every', h });
      return id;
    }
    function clearTimer(id) {
      const rec = timers.get(id);
      if (!rec) return;
      if (rec.kind === 'after') clearTimeout(rec.h); else clearInterval(rec.h);
      timers.delete(id);
    }
    function clearTimers() {
      for (const id of Array.from(timers.keys())) clearTimer(id);
      timers.clear();
      deferred.length = 0;
    }
    /** The decks' registry: this class's own pause-aware timers. */
    const deckTimers = { after, every, clear: clearTimer };

    /* ==================================================================== *
     * ENGINE - one wrapper, the input-trust law welded on.
     * ==================================================================== */
    function fireSafe(kind, opts) {
      if (dead || paused || !ctx.engine) return null;
      const o = Object.assign({}, opts || {});
      if (kind === 'flash_burst' || kind === 'gif_burst') {
        o.clickSafe = true;            // decoration only over a gesture board
        o.clickable = false;
        delete o.onPop;
      }
      try { return ctx.engine.fire(kind, o) || null; } catch (e) { say('fire(' + kind + ') failed'); return null; }
    }
    function sustainSafe(kind, opts) {
      if (dead || paused || !ctx.engine) return null;
      try { return ctx.engine.sustain(kind, opts || {}) || null; } catch (e) { return null; }
    }
    function stopSafe(kind) { try { if (ctx.engine) ctx.engine.stop(kind); } catch (e) { /* noop */ } }
    /** The engine, as a deck sees it: the three welded primitives plus the
     *  READ of the clamped channel vector (THE CEILING RULE - a deck asks, it
     *  never raises). Every member is null-safe and may answer null. */
    const deckEngine = {
      fire: fireSafe,
      sustain: sustainSafe,
      stop: stopSafe,
      channels: () => {
        try { return (ctx.engine && typeof ctx.engine.channels === 'function') ? ctx.engine.channels() : null; }
        catch (e) { return null; }
      },
    };
    /** The player's own media, as a deck sees it. The pool lands ASYNC (a
     *  claim resolves well after start()), so a deck must be handed a LIVE
     *  reader rather than the pool object, or it captures null forever. */
    const deckAssets = {
      next(kind) {
        try { return (pool && typeof pool.next === 'function') ? (pool.next(kind) || null) : null; }
        catch (e) { return null; }
      },
    };
    /** bgIntensity 0 is the player's exit: read it LIVE, never a launch snapshot. */
    function capsArmed() { return !(ctx.caps && Number(ctx.caps.bgIntensity) === 0); }
    /** A cue through the engine; level never above the tier's audio ceiling. */
    function tick(name, level, extra) {
      const ceil = plan ? plan.audioCeil : 0.45;
      const lv = Math.min(ceil, level == null ? 0.4 : level);
      fireSafe('audio_trigger', Object.assign({ name, level: lv }, extra || {}));
    }

    /* ---- the decks, null-safe ------------------------------------------- */
    function deck(which, method, ...args) {
      const d = which === 'casino' ? casino : which === 'pressure' ? pressure : trickster;
      if (!d || typeof d[method] !== 'function') return undefined;
      try { return d[method](...args); } catch (e) { say(which + '.' + method + ' threw: ' + ((e && e.message) || e)); return undefined; }
    }

    /* ==================================================================== *
     * HEAT - depthLine -> curve (gradeTier caps it) -> engine + casino
     * ==================================================================== */
    function heat() {
      if (!board || !plan) return;
      const deepTier = boardDeepest(board);
      const depthLine = depthLineFor(deepTier);
      let h = heatCurve(depthLine, tier);
      if (exhaleOn) h *= plan.exhaleHeatMult;
      currentHeat = h;
      try { if (ctx.engine) ctx.engine.setHeat(h); } catch (e) { /* engine is optional */ }
      deck('casino', 'setHeat', h);
      deck('pressure', 'setHeat', h);
      /* THE RUNG rides the deepest tile, the MAGNITUDE rides heat - and heat()
       * is the one place both are known, so the pressure deck learns the depth
       * on every recompute (every move, every dive, every exhale edge). */
      deck('pressure', 'setDepth', deepTier, depthLine);
      rowDriftCheck(depthLine);
    }

    /* ==================================================================== *
     * DOM (the contract's exact shape)
     * ==================================================================== */
    function tierName(k) {
      if (k <= 0) return t('de_tier_silt', DE_LEX.de_tier_silt);
      const key = 'de_tier_' + k;
      return t(key, DE_LEX[key] || String(k));
    }
    function setPhase(p) { if (stage) stage.setAttribute('data-phase', p); }
    function basePhase() { return ceilingHold ? 'ceiling' : bellOn ? 'bell' : exhaleOn ? 'exhale' : 'dive'; }
    function msg(key, fallback) {
      if (!msgEl) return;
      msgEl.textContent = t(key, fallback);
    }

    function buildDom() {
      const root = ctx.root;
      root.textContent = '';
      stage = el('div', 'g-de-stage');
      /* pass 5: the level is resolved before any node exists, so the stage is
         born lit-down rather than flipping a frame after the deal. */
      if (perf === 'lite') stage.classList.add('g-de-lite');
      stage.setAttribute('data-phase', 'briefing');
      stage.setAttribute('data-size', String(n));
      if (reduced) stage.setAttribute('data-reduced', '1');

      backdrop = el('div', 'g-de-backdrop');
      backdrop.setAttribute('aria-hidden', 'true');
      backdrop.style.pointerEvents = 'none';
      stage.appendChild(backdrop);

      hud = el('div', 'g-de-hud');
      depthChip = el('span', 'g-de-chip g-de-depth', tierName(1));
      depthChip.setAttribute('data-tier', '1');
      depthChip.setAttribute('aria-label', t('de_chip_depth', DE_LEX.de_chip_depth));
      clockChip = el('span', 'g-de-chip g-de-clock', clockText());
      // a free swim's chip is a stopwatch, so "Time left" would be a lie to a
      // screen reader; the count-up wears the end card's own 'Time' row
      clockChip.setAttribute('aria-label', endless
        ? t('de_end_time', DE_LEX.de_end_time)
        : t('de_chip_clock', DE_LEX.de_chip_clock));
      scoreChip = el('span', 'g-de-chip g-de-score', '0');
      scoreChip.setAttribute('aria-label', t('de_chip_score', DE_LEX.de_chip_score));
      chainChip = el('span', 'g-de-chip g-de-chain', 'x 0');
      chainChip.setAttribute('aria-label', t('de_chip_chain', DE_LEX.de_chip_chain));
      chainChip.hidden = true;
      hud.appendChild(depthChip);
      hud.appendChild(clockChip);
      hud.appendChild(scoreChip);
      hud.appendChild(chainChip);
      if (retake) hud.appendChild(el('span', 'g-de-chip g-de-retake', t('de_retake', DE_LEX.de_retake)));
      /* FREE SWIM: the only way a free swim ends on its own terms. A REAL
       * button, so it is tab-reachable and answers Enter/Space; the window
       * keydown handler ignores a BUTTON target (isFormTarget), so pressing it
       * is never eaten as a move. */
      if (endless) {
        surfaceBtn = el('button', 'g-de-chip g-de-surface', t('de_surface', DE_LEX.de_surface));
        surfaceBtn.setAttribute('type', 'button');
        try { surfaceBtn.type = 'button'; } catch (e) { /* the DOM double has no button semantics */ }
        surfaceBtn.addEventListener('click', onSurface);
        hud.appendChild(surfaceBtn);
      }
      stage.appendChild(hud);

      bench = el('div', 'g-de-bench');
      boardEl = el('div', 'g-de-board');
      boardEl.style.setProperty('--de-n', String(n));
      boardEl.style.touchAction = 'none';          // a swipe is a move, never a scroll
      boardEl.setAttribute('role', 'application');
      boardEl.setAttribute('aria-label', t('game_the_deep_end', 'The Deep End'));
      cellEls.length = 0;
      for (let r = 0; r < n; r++) {
        for (let c = 0; c < n; c++) {
          const cell = el('div', 'g-de-cell');
          cell.setAttribute('data-r', String(r));
          cell.setAttribute('data-c', String(c));
          boardEl.appendChild(cell);
          cellEls.push(cell);
        }
      }
      bench.appendChild(boardEl);
      stage.appendChild(bench);

      well = el('div', 'g-de-flashwell');
      well.setAttribute('aria-hidden', 'true');
      well.style.pointerEvents = 'none';
      stage.appendChild(well);

      msgEl = el('p', 'g-de-msg');
      msgEl.setAttribute('aria-live', 'polite');
      stage.appendChild(msgEl);

      endEl = el('div', 'g-de-end');
      endEl.hidden = true;
      stage.appendChild(endEl);

      root.appendChild(stage);
    }

    /** One element per live tile, positioned ONLY through --r / --c.
     *  Children (pass 2): [.g-de-face > img.g-de-media] .g-de-trail .g-de-glyph
     *  .g-de-name .g-de-num - the name node stays `.g-de-name` (the trickster's
     *  one lie target; paintName restores it). Silt never wears a face. */
    function tileEl(tile, fresh) {
      let node = tileEls.get(tile.id);
      if (!node) {
        node = el('div', 'g-de-tile');
        node.setAttribute('data-id', String(tile.id));
        node.setAttribute('aria-hidden', 'true');
        if (facesMode !== 'plain' && !tile.silt) {
          const face = el('span', 'g-de-face');
          const img = el('img', 'g-de-media');
          armMedia(img, node, false);
          face.appendChild(img);
          node.appendChild(face);
          // seeded-enough desync of the ken-burns phase: the id is the spawn order
          node.style.setProperty('--de-kbp', ((tile.id * 0.37) % 1).toFixed(2));
        }
        node.appendChild(el('span', 'g-de-trail'));
        node.appendChild(el('span', 'g-de-glyph'));
        node.appendChild(el('span', 'g-de-name', tierName(tile.silt ? 0 : tile.tier)));
        node.appendChild(el('span', 'g-de-num', tile.silt ? '' : String(tile.tier)));
        if (tile.silt) node.classList.add('is-silt');
        if (fresh) {
          node.classList.add('is-new');
          after(reduced ? 200 : 320, () => node.classList.remove('is-new'));
        }
        tileEls.set(tile.id, node);
        boardEl.appendChild(node);
      }
      node.setAttribute('data-tier', String(tile.silt ? 0 : tile.tier));
      node.style.setProperty('--r', String(tile.r));
      node.style.setProperty('--c', String(tile.c));
      const num = childOf(node, 'g-de-num');
      if (num) { const want = tile.silt ? '' : String(tile.tier); if (num.textContent !== want) num.textContent = want; }
      dressFace(node, tile);
      return node;
    }
    function childOf(node, cls) {
      try { for (const k of node.children || []) if (k.classList && k.classList.contains(cls)) return k; } catch (e) { /* ignore */ }
      return null;
    }
    function mediaOf(node) {
      const face = childOf(node, 'g-de-face');
      return face ? childOf(face, 'g-de-media') : null;
    }
    /** Wire a face's media node: is-loaded once it has a frame, faceBroken on
     *  error. A <video> (a webm/mp4 loop - the only animated shape a remote
     *  provider serves) is muted / looping / inline and nudged to play. */
    function armMedia(img, node, video) {
      if (!img) return;
      try {
        img.setAttribute('alt', '');
        img.setAttribute('draggable', 'false');
        img.draggable = false;
        if (video) {
          img.muted = true; img.loop = true; img.autoplay = true; img.playsInline = true;
          img.setAttribute('muted', ''); img.setAttribute('loop', ''); img.setAttribute('autoplay', '');
          img.setAttribute('playsinline', ''); img.setAttribute('preload', 'auto');
          img.addEventListener('loadeddata', () => {
            if (dead) return;
            node.classList.add('is-loaded');
            try { const p = img.play(); if (p && typeof p.catch === 'function') p.catch(() => {}); } catch (e) { /* ignore */ }
          });
        } else {
          img.decoding = 'async';
          img.addEventListener('load', () => { if (!dead) node.classList.add('is-loaded'); });
        }
        img.addEventListener('error', () => { if (!dead) faceBroken(node); });
      } catch (e) { /* the double has no img semantics; fine */ }
    }
    /** The media node that can SHOW this url: the face's <img>, swapped for a
     *  <video> when the tier's url is a webm/mp4 loop (and back for a still).
     *  Same class, same place in the face; the listeners are re-armed. */
    function mediaNodeFor(node, url) {
      const face = childOf(node, 'g-de-face');
      if (!face) return null;
      const cur = childOf(face, 'g-de-media');
      const wantVideo = VIDEO_URL_RE.test(String(url || ''));
      const isVideo = !!(cur && String(cur.tagName || '').toUpperCase() === 'VIDEO');
      if (cur && wantVideo === isVideo) return cur;
      const next = el(wantVideo ? 'video' : 'img', 'g-de-media');
      armMedia(next, node, wantVideo);
      try {
        if (cur && typeof face.replaceChild === 'function') face.replaceChild(next, cur);
        else face.appendChild(next);
      } catch (e) { try { face.appendChild(next); } catch (e2) { /* ignore */ } }
      return next;
    }

    /* ==================================================================== *
     * PASS 5 - THE PERF LADDER (de_perf: auto | full | lite)
     *
     * ONE resolved level per class, and `auto` may lower it exactly once. The
     * level is expressed in TWO places at once: `.g-de-lite` on the stage (this
     * game's own stylesheet reads it) and `.ae-lite` on <html> (the ENGINE's
     * stylesheet and its shared video budget read that one - a game may not
     * reach into the engine, but the document root is common ground). Both come
     * off on destroy, or the lobby inherits a lit-down room.
     * ==================================================================== */
    /** The animated-tier cap and the still-shallows line, by resolved level. */
    function faceCap() { return perf === 'lite' ? PLAYTEST.FACE_CAP_LITE : PLAYTEST.FACE_CAP; }
    function shallowStillMaxTier() {
      return perf === 'lite' ? PLAYTEST.SHALLOW_STILL_MAX_TIER_LITE : PLAYTEST.SHALLOW_STILL_MAX_TIER;
    }
    /** Read the setting. Anything that is not one of the three is `auto` - the
     *  host clamps its OWN knobs, but a per-game bag is stored verbatim, so a
     *  hand-edited blob really can arrive as 42, '' or null. */
    function perfFromSetting(v) {
      if (typeof v !== 'string') return 'auto';    // an array stringifies to its one item; a setting is a string
      const want = v.trim().toLowerCase();
      return PERF_MODES.includes(want) ? want : 'auto';
    }
    function setLiteClass(on) {
      try { if (stage) stage.classList[on ? 'add' : 'remove']('g-de-lite'); } catch (e) { /* ignore */ }
      try {
        const html = typeof document !== 'undefined' ? document.documentElement : null;
        if (html && html.classList) html.classList[on ? 'add' : 'remove']('ae-lite');
      } catch (e) { /* ignore */ }
    }
    /** Resolve to a level. DOWNWARD ONLY: once lite, lite for the class. */
    function applyPerf(level, reason) {
      const want = level === 'lite' ? 'lite' : 'full';
      if (perf === 'lite' && want === 'full') return;      // never re-promote mid-class
      perf = want;
      perfReason = String(reason || '');
      setLiteClass(perf === 'lite');
      if (!perfLogged || perf === 'lite') {
        perfLogged = true;
        say('perf: ' + perf + (perfReason ? ' (' + perfReason + ')' : ''));
      }
    }
    /**
     * THE AUTO PROBE. Sample rAF deltas once the board is dealt, skip the
     * warm-up (style injection and the first decodes land there and would
     * demote a machine that is actually fine), then judge the MEDIAN and the
     * slow-frame share. Robustness rules, in the order they cost time:
     *   - no requestAnimationFrame at all (node, the DOM double) -> stay FULL,
     *     silently. A missing clock is not evidence of a slow machine.
     *   - a hidden tab does not paint, so every delta is garbage: abandon the
     *     probe rather than demote, and never restart it (once per class).
     *   - a paused/suspended class is the same story (a mandatory video owns
     *     the screen); abandon.
     */
    function startPerfProbe() {
      if (perfProbeDone || perfSetting !== 'auto' || perf === 'lite') return;
      const raf = (typeof window !== 'undefined' && typeof window.requestAnimationFrame === 'function')
        ? (fn) => window.requestAnimationFrame(fn) : null;
      if (!raf) { perfProbeDone = true; perfReason = 'auto: no rAF, stayed full'; return; }
      const deltas = [];
      let t0 = 0;
      let last = 0;
      const hidden = () => { try { return typeof document !== 'undefined' && document.hidden === true; } catch (e) { return false; } };
      const step = (now) => {
        if (dead || ended || perfProbeDone) return;
        if (paused || hidden()) { perfProbeDone = true; perfReason = 'auto: probe abandoned (not painting)'; return; }
        const t = Number.isFinite(Number(now)) ? Number(now) : Date.now();
        if (!t0) { t0 = t; last = t; raf(step); return; }
        const dt = t - last;
        last = t;
        if (t - t0 >= PLAYTEST.PERF_WARMUP_MS) deltas.push(dt);
        if (t - t0 < PLAYTEST.PERF_WARMUP_MS + PLAYTEST.PERF_SAMPLE_MS) { raf(step); return; }
        perfProbeDone = true;
        judgePerf(deltas);
      };
      raf(step);
    }
    /** The verdict on a delta sample (its own function so the harness can drive
     *  it without a real frame clock). */
    function judgePerf(deltas) {
      const n = deltas.length;
      if (n < PLAYTEST.PERF_MIN_FRAMES) { perfReason = 'auto: ' + n + ' frames sampled, stayed full'; return; }
      const sorted = deltas.slice().sort((a, b) => a - b);
      const median = sorted[Math.floor(n / 2)];
      let slow = 0;
      for (const d of deltas) if (d > PLAYTEST.PERF_SLOW_MS) slow += 1;
      const share = slow / n;
      if (median > PLAYTEST.PERF_MEDIAN_MS || share > PLAYTEST.PERF_SLOW_SHARE) {
        applyPerf('lite', 'median ' + Math.round(median) + 'ms, ' + Math.round(share * 100) + '% slow');
        return;
      }
      perfReason = 'auto: full (median ' + Math.round(median) + 'ms, ' + Math.round(share * 100) + '% slow)';
    }

    /* ---- faces (pass 2, re-dialled by pass 5): one url per tier, frozen ---- */
    /** How many DISTINCT TIERS have been dealt an animated face. Tiles are the
     *  wrong unit: a tier's url is frozen for the class, so ten tier-4 tiles are
     *  ten decoders of ONE file and only a NEW tier buys a new decode. */
    function animatedFaces() {
      let k = 0;
      for (const f of faceUrls.values()) if (f && !f.broken && f.kind === 'loop') k += 1;
      return k;
    }
    /** The tier's face, dealing one on the tier's first appearance. null = no
     *  face (plain mode, silt, no pool yet, or a url that broke). */
    function faceFor(tier) {
      if (facesMode === 'plain' || !(tier > 0)) return null;
      const have = faceUrls.get(tier);
      if (have) return have.broken ? null : have;
      if (!pool || typeof pool.next !== 'function') return null;
      let kind = faceKind;
      if (kind === 'loop' && tier <= shallowStillMaxTier()) {
        /* THE SHALLOWS ARE STILL, DEPTH IS ALIVE. Tiers 1-3 are most of every
         * board; giving them the loops spent the whole decoder budget on the
         * tiles nobody is looking at. */
        kind = 'still';
      } else if (kind === 'loop' && animatedFaces() >= faceCap()) {
        kind = 'still';
        if (!capLogged) { capLogged = true; say('faces: ' + faceCap() + ' animated tiers live - new tiers wear stills'); }
      }
      let url = null;
      try { const got = pool.next(kind); url = got && got.url ? String(got.url) : null; } catch (e) { url = null; }
      if (!url) return null;
      const face = { url, kind };
      faceUrls.set(tier, face);
      if (!faceLogged) {
        faceLogged = true;
        /* the CLASS's policy, not this one deal: faceKind is what a tier past
           the shallows gets, and the two numbers are the pass-5 budget. */
        say('faces: ' + faceKind + ' (' + facesMode + ', motion ' + motionLevelOf() + (reduced ? ', reduced' : '')
          + ', still to tier ' + shallowStillMaxTier() + ', cap ' + faceCap() + ')');
      }
      return face;
    }
    /** Dress (or re-dress after a merge) a tile with its tier's face. Never
     *  blocks a draw: the plain body shows until the image has a frame. */
    function dressFace(node, tile) {
      if (tile.silt) return;
      const img = mediaOf(node);
      if (!img) return;
      const tier = tile.tier;
      if (node.getAttribute('data-face') === String(tier)) return;
      const face = faceFor(tier);
      if (!face) {
        if (node.getAttribute('data-face') != null) {        // it wore an older tier's face; strip it
          node.classList.remove('is-loaded');
          try { if (typeof img.removeAttribute === 'function') img.removeAttribute('src'); } catch (e) { /* ignore */ }
          node.setAttribute('data-face', '');
        }
        return;
      }
      node.setAttribute('data-face', String(tier));
      node.classList.remove('is-loaded');
      const media = mediaNodeFor(node, face.url) || img;
      try { media.src = face.url; } catch (e) { /* ignore */ }
    }
    /** A url that failed: this tier goes plain for the rest of the class (no retry storm). */
    function faceBroken(node) {
      const tier = Number(node.getAttribute('data-face'));
      node.classList.remove('is-loaded');
      if (tier > 0) {
        faceUrls.set(tier, { broken: true });
        say('faces: tier ' + tier + ' media failed - plain face for the class');
        for (const tile of board ? board.tiles : []) {
          if (tile.tier !== tier || tile.silt) continue;
          const other = tileEls.get(tile.id);
          if (!other) continue;
          other.classList.remove('is-loaded');
          other.setAttribute('data-face', '');
          const img = mediaOf(other);
          try { if (img && typeof img.removeAttribute === 'function') img.removeAttribute('src'); } catch (e) { /* ignore */ }
        }
      }
    }
    function dressAllFaces() {
      if (!board) return;
      for (const tile of board.tiles) { const node = tileEls.get(tile.id); if (node) dressFace(node, tile); }
    }
    function motionLevelOf() {
      try { const v = ctx.motion && ctx.motion.motionLevel; return Number.isFinite(Number(v)) ? Number(v) : 2; } catch (e) { return 2; }
    }
    function faceOf(node) {
      const out = [];
      try { for (const k of node.children || []) if (k.classList && (k.classList.contains('g-de-glyph') || k.classList.contains('g-de-name'))) out.push(k); } catch (e) { /* ignore */ }
      return out;
    }
    function nameNodeOf(node) {
      try { for (const k of node.children || []) if (k.classList && k.classList.contains('g-de-name')) return k; } catch (e) { /* ignore */ }
      return null;
    }
    /** Repaint the TRUE tier name on a tile (the trickster may have lied on it). */
    function paintName(node, tile) {
      const nm = nameNodeOf(node);
      if (nm) nm.textContent = tierName(tile.silt ? 0 : tile.tier);
    }
    function removeTileEl(id, delayMs) {
      const node = tileEls.get(id);
      if (!node) return;
      tileEls.delete(id);
      // a tile leaving the board carries no live state with it
      node.classList.remove('is-deepest', 'is-strain', 'is-new', 'is-merged', ...SLIDE_CLASSES);
      node.classList.add('is-gone');
      const drop = () => { try { node.remove(); } catch (e) { /* noop */ } };
      if (delayMs > 0) after(delayMs, drop); else drop();
    }
    function liveTiles() {
      const out = [];
      for (const tile of board.tiles) { const node = tileEls.get(tile.id); if (node) out.push(node); }
      return out;
    }

    /* ---- HUD paint (truth) ---------------------------------------------- */
    /** Seconds left on the real budget. A FREE SWIM has no budget, so it has no
     *  "left": the trickster's stats read 0 rather than Infinity. */
    function secLeft() { return endless ? 0 : Math.max(0, Math.ceil((budgetMs - elapsedMs) / 1000)); }
    /** Seconds swum so far - the free swim's clock counts UP. */
    function secElapsed() { return Math.max(0, Math.floor(elapsedMs / 1000)); }
    /** THE TRUTH on the clock chip; the trickster restores exactly this string. */
    function clockText() { return endless ? mmss(secElapsed()) : mmss(secLeft()); }
    function scoreText() { return String(score); }
    function depthText() { return tierName(Math.max(1, boardDeepest(board))); }
    function chipText(which) {
      if (which === 'clock') return clockText();
      if (which === 'score') return scoreText();
      return depthText();
    }
    function paintHud() {
      if (clockChip) clockChip.textContent = clockText();
      if (scoreChip) scoreChip.textContent = scoreText();
      if (depthChip) {
        const d = Math.max(1, boardDeepest(board));
        depthChip.textContent = tierName(d);
        depthChip.setAttribute('data-tier', String(d));
      }
    }
    function paintChain() {
      if (!chainChip) return;
      if (chain < PLAYTEST.STREAK_VISIBLE) { chainChip.hidden = true; chainChip.textContent = 'x ' + chain; return; }
      chainChip.hidden = false;
      chainChip.textContent = 'x ' + chain;
      // the meter is the SHELL's primitive (10 segments, always); it rides
      // inside the chip so the contract's DOM gains no extra node
      try {
        const meter = ctx.ceremonies.streakMeter({ filled: Math.min(PLAYTEST.CHAIN_CAP, chain), gold: chain >= PLAYTEST.CHAIN_CAP });
        if (meter) chainChip.appendChild(meter);
      } catch (e) { /* a ceremony must never be the thing that fails */ }
    }
    function markDeepest() {
      const d = boardDeepest(board);
      for (const tile of board.tiles) {
        const node = tileEls.get(tile.id);
        if (!node) continue;
        if (!tile.silt && d >= 2 && tile.tier === d) node.classList.add('is-deepest');
        else node.classList.remove('is-deepest');
      }
    }

    /* ==================================================================== *
     * INPUT - window keydown (arrows / WASD) + pointer swipe on the board.
     * Tiles are never clickable (Law II); a move is the only verb.
     * ==================================================================== */
    function onKeyDown(e) {
      if (!e || e.repeat || e.ctrlKey || e.altKey || e.metaKey) return;
      if (isFormTarget(e.target)) return;
      const dir = KEY_DIRS[String(e.key || '')] || KEY_DIRS[String(e.code || '')];
      if (!dir) return;
      try { e.preventDefault(); } catch (err) { /* noop */ }
      input(dir);
    }

    /* -------------------------------------------------------------------- *
     * THE HAND (pass 3) - press, lean, release. The board is the ONLY
     * listener (Law II): the grab is a board gesture and never a tile click.
     * index.js sets the state; style.js owns every pixel of the preview, and
     * ignores all of it under reduced motion (--de-grab-k: 0) except the lift.
     * -------------------------------------------------------------------- */
    function unit(v) { return v < -1 ? -1 : v > 1 ? 1 : v; }
    /** Write the clamped drag vector, 2 decimals, skipping unchanged writes. */
    function setGrabVars(x, y) {
      if (!grab || !boardEl) return;
      const gx = Number(unit(x).toFixed(2));
      const gy = Number(unit(y).toFixed(2));
      if (gx !== grab.gx) { grab.gx = gx; try { boardEl.style.setProperty('--de-grab-x', String(gx)); } catch (e) { /* noop */ } }
      if (gy !== grab.gy) { grab.gy = gy; try { boardEl.style.setProperty('--de-grab-y', String(gy)); } catch (e) { /* noop */ } }
    }
    /** The dominant direction of the drag, and whether that wall is solid. */
    function setGrabDir(dir) {
      if (!grab || !boardEl) return;
      const blocked = !!dir && !wouldMove(board, dir);
      if (dir === grab.dir && blocked === grab.blocked) return;
      grab.dir = dir;
      grab.blocked = blocked;
      boardEl.classList.remove(...GRAB_CLASSES);
      if (dir) boardEl.classList.add('g-de-grab-' + dir);
      if (blocked) boardEl.classList.add('g-de-grab-blocked');
      else boardEl.classList.remove('g-de-grab-blocked');
    }
    /** Drop the whole gesture: capture, classes, vars. Never fires a move. */
    function clearGrab() {
      const g = grab;
      grab = null;
      if (!boardEl) return;
      if (g && g.captured && g.id != null) {
        try { if (typeof boardEl.releasePointerCapture === 'function') boardEl.releasePointerCapture(g.id); } catch (e) { /* the pointer is already gone */ }
      }
      try { boardEl.classList.remove('g-de-held', 'g-de-grab-blocked', ...GRAB_CLASSES); } catch (e) { /* noop */ }
      try { boardEl.style.removeProperty('--de-grab-x'); boardEl.style.removeProperty('--de-grab-y'); } catch (e) { /* noop */ }
    }
    /** Is this event the pointer we are holding? */
    function samePointer(e) {
      if (!grab) return false;
      if (e && e.pointerId != null && grab.id != null) return e.pointerId === grab.id;
      return true;
    }
    function onPointerDown(e) {
      if (!e || !boardEl || dead || paused || ended) return;
      grab = {
        id: e.pointerId == null ? null : e.pointerId,
        x: Number(e.clientX) || 0, y: Number(e.clientY) || 0,
        gx: 0, gy: 0, dir: '', blocked: false, captured: false,
      };
      // with capture the drag survives leaving the board; without it,
      // pointerleave goes back to being the cancel (see onPointerLeave)
      try {
        if (typeof boardEl.setPointerCapture === 'function' && grab.id != null) {
          boardEl.setPointerCapture(grab.id);
          grab.captured = true;
        }
      } catch (err) { grab.captured = false; }
      boardEl.classList.add('g-de-held');
      try {
        boardEl.style.setProperty('--de-grab-x', '0');
        boardEl.style.setProperty('--de-grab-y', '0');
      } catch (err) { /* noop */ }
    }
    function onPointerMove(e) {
      if (!e || !grab || !samePointer(e)) return;
      const dx = (Number(e.clientX) || 0) - grab.x;
      const dy = (Number(e.clientY) || 0) - grab.y;
      setGrabVars(dx / PLAYTEST.GRAB_PX, dy / PLAYTEST.GRAB_PX);
      const ax = Math.abs(dx); const ay = Math.abs(dy);
      setGrabDir(Math.max(ax, ay) < PLAYTEST.GRAB_DEAD ? '' : (ax >= ay ? (dx > 0 ? 'right' : 'left') : (dy > 0 ? 'down' : 'up')));
    }
    function onPointerUp(e) {
      if (!e || !grab || !samePointer(e)) return;
      const dx = (Number(e.clientX) || 0) - grab.x;
      const dy = (Number(e.clientY) || 0) - grab.y;
      clearGrab();
      const ax = Math.abs(dx); const ay = Math.abs(dy);
      if (Math.max(ax, ay) < SWIPE_PX) return;
      input(ax >= ay ? (dx > 0 ? 'right' : 'left') : (dy > 0 ? 'down' : 'up'));
    }
    function onPointerCancel(e) { if (!grab || samePointer(e)) clearGrab(); }
    /** With capture, leaving the board mid-drag is normal; without it, it ends. */
    function onPointerLeave(e) { if (grab && !grab.captured) onPointerCancel(e); }
    function bindInput() {
      try { if (typeof window !== 'undefined') window.addEventListener('keydown', onKeyDown); }
      catch (e) { say('keydown bind failed: ' + ((e && e.message) || e)); }
      try {
        boardEl.addEventListener('pointerdown', onPointerDown);
        boardEl.addEventListener('pointermove', onPointerMove);
        boardEl.addEventListener('pointerup', onPointerUp);
        boardEl.addEventListener('pointercancel', onPointerCancel);
        boardEl.addEventListener('lostpointercapture', onPointerCancel);
        boardEl.addEventListener('pointerleave', onPointerLeave);
      } catch (e) { say('pointer bind failed: ' + ((e && e.message) || e)); }
    }
    function unbindInput() {
      try { if (typeof window !== 'undefined') window.removeEventListener('keydown', onKeyDown); } catch (e) { /* noop */ }
      try {
        if (boardEl) {
          boardEl.removeEventListener('pointerdown', onPointerDown);
          boardEl.removeEventListener('pointermove', onPointerMove);
          boardEl.removeEventListener('pointerup', onPointerUp);
          boardEl.removeEventListener('pointercancel', onPointerCancel);
          boardEl.removeEventListener('lostpointercapture', onPointerCancel);
          boardEl.removeEventListener('pointerleave', onPointerLeave);
        }
      } catch (e) { /* noop */ }
    }

    /* ==================================================================== *
     * THE QUEUE (pass 3) - the MOVE_MS lock no longer eats a fast second
     * press. ONE slot, last press wins; it drains on the next timer tick
     * after the lock releases, so a queued move never runs inside the
     * callback that released it. It is dropped the moment the board stops
     * being the player's: pause, suspend, resurface, a new dive, the bell,
     * the ceiling, the end, destroy.
     * ==================================================================== */
    function clearQueue() {
      queued = null;
      if (queueTimer) { clearTimer(queueTimer); queueTimer = 0; }
    }
    /** The lock is off: hand the slot back to the pipeline, one tick later. */
    function release() {
      busy = false;
      if (!queued || dead || paused || ended || !board) { clearQueue(); return; }
      if (queueTimer) clearTimer(queueTimer);
      queueTimer = after(PLAYTEST.QUEUE_DRAIN_MS, () => {
        queueTimer = 0;
        const dir = queued;                       // read late: a pause in between drops it
        queued = null;
        if (!dir || dead || paused || ended || busy || !board) return;
        drained += 1;
        input(dir);
      });
    }

    /* ==================================================================== *
     * THE MOVE PIPELINE
     * ==================================================================== */
    function input(dir) {
      if (dead || paused || ended || !board) return false;
      /* THE QUEUE: the lock is up, so the press is REMEMBERED instead of
       * eaten (a wall-bump direction too - the bump plays when it drains).
       * The briefing is not a lock the player can play against, so a press
       * before the water opens is still simply refused. */
      if (busy) {
        if (opened && DIRV[dir] && PLAYTEST.QUEUE_SLOTS > 0) queued = dir;
        return false;
      }
      const result = boardMove(board, dir);
      if (!result.moved) {
        // THE WALL: a swipe that slides nothing. No spawn, no count - but the
        // board shakes into the blocked edge, the casino flashes it, and a
        // muted thud plays. Never silence.
        bumps += 1;
        shakeBoard(dir);
        deck('casino', 'bump', dir);
        tick('bump', PLAYTEST.BUMP_LEVEL);
        deck('trickster', 'afterMove', { moved: false, merges: [], spawn: null, locked: false });
        return false;
      }
      swipes += 1;
      slides += 1;
      stallMs = 0;
      stuckShown = false;
      clearHint();
      deck('trickster', 'stalled', 0);
      busy = true;
      const moveMs = reduced ? PLAYTEST.MOVE_MS_REDUCED : PLAYTEST.MOVE_MS;

      /* 1. slide: vars only; victims ride to the merge cell and dissolve.
         THE FEEL (pass 2): every moving tile is marked for style.js's trail
         and squash with its distance, vacated cells blink, the casino leans
         the bench, and the whoosh scales with tiles moved, pitched by depth. */
      const v = DIRV[dir];
      const sliding = [];
      const from = new Set();
      const to = new Set();
      let maxDist = 0;
      let moved = 0;
      for (const mv of result.moves) {
        const node = tileEls.get(mv.id);
        if (!node) continue;
        node.style.setProperty('--r', String(mv.to.r));
        node.style.setProperty('--c', String(mv.to.c));
        const dist = Math.abs(mv.to.r - mv.from.r) + Math.abs(mv.to.c - mv.from.c);
        if (dist > 0) {
          moved += 1;
          maxDist = Math.max(maxDist, dist);
          from.add(mv.from.r * n + mv.from.c);
          to.add(mv.to.r * n + mv.to.c);
          if (!reduced) {
            node.classList.remove(...SLIDE_CLASSES);
            node.style.setProperty('--de-td', String(dist));
            node.classList.add('is-sliding', 'is-sliding-' + v.axis, 'is-sliding-' + dir);
            sliding.push(node);
          }
        }
        if (mv.victim) removeTileEl(mv.id, moveMs + 60);
      }
      if (sliding.length) {
        if (slideTimer) clearTimer(slideTimer);
        slideTimer = after(moveMs + PLAYTEST.SLIDE_TRAIL_MS, () => {
          slideTimer = 0;
          for (const node of sliding) node.classList.remove(...SLIDE_CLASSES);
        });
      }
      const wake = [];
      for (const ix of from) { if (!to.has(ix) && cellEls[ix]) { cellEls[ix].classList.add('is-wake'); wake.push(cellEls[ix]); } }
      if (wake.length) {
        if (wakeTimer) clearTimer(wakeTimer);
        wakeTimer = after(PLAYTEST.LAND_MS, () => { wakeTimer = 0; for (const c of wake) c.classList.remove('is-wake'); });
      }
      deck('casino', 'slide', dir, moved, maxDist);
      deck('pressure', 'slide', dir, moved, maxDist);
      tick('slide', PLAYTEST.SLIDE_LV_BASE + PLAYTEST.SLIDE_LV_STEP * Math.min(4, moved), {
        pitch: 1 - PLAYTEST.SLIDE_PITCH_DROP * depthLineFor(boardDeepest(board)),
      });

      /* 2. merges: score, chain, light, the descending chime */
      chain = result.merges.length;
      let deepestMergeEl = null;
      let deepestMergeTier = 0;
      /* Law I is untouched: `score` still moves in ONE place (below, by
       * result.score). This walks the same arithmetic board.js already did -
       * a merge pays 2^newTier - purely so a deck can be told what a single
       * merge was worth and what the running total will read. The two agree
       * by construction: result.score IS the sum of these. */
      let runScore = score;
      for (const m of result.merges) {
        merges += 1;
        const deltaScore = Math.pow(2, m.tier);
        runScore += deltaScore;
        const tile = board.tiles.find((x) => x.id === m.id);
        const node = tile ? tileEl(tile, false) : null;
        if (node) {
          paintName(node, tile);
          node.classList.remove('is-new');
          node.classList.add('is-merged');
          after(moveMs + 320, () => node.classList.remove('is-merged'));
        }
        deck('casino', 'merge', { tier: m.tier, link: m.link, tileEl: node });
        deck('pressure', 'merge', { tier: m.tier, link: m.link, tileEl: node, deltaScore, score: runScore });
        // one chime family, a semitone DOWN per link: you hear yourself sinking
        tick('streak', 0.3 + 0.04 * m.tier, { pitch: Math.pow(2, -m.link / 12) });
        if (m.tier > deepestMergeTier) { deepestMergeTier = m.tier; deepestMergeEl = node; }
      }
      score += result.score;
      if (chain >= 2) chainLinks += chain - 1;
      maxChain = Math.max(maxChain, chain);
      paintChain();

      /* 3. a new deepest tier */
      const d = boardDeepest(board);
      if (d > diveDeepest) {
        diveDeepest = d;
        bestDeepest = Math.max(bestDeepest, d);
        onNewDeepest(d, deepestMergeEl);
      }
      markDeepest();
      paintHud();

      /* THE CEILING: tier_11 ends a TIMED class, warm - no spawn, the board is
       * done. A FREE SWIM keeps going: the royal fires once per dive and the
       * move finishes normally (spawn, trickster, heat, lock check). */
      if (d >= TIER_MAX) {
        if (!endless) { ceiling(); return true; }
        if (!ceilingCelebrated) { ceilingCelebrated = true; ceiling(); }
      }

      /* 4. one seeded spawn (the exhale guarantee rides the flag) */
      const sp = boardSpawn(board, {
        table: plan.spawnTable, siltChance: plan.siltChance, siltMax: plan.siltMax,
        airlockChance: plan.airlockChance, exhale: exhalePending,
      });
      if (sp.exhaled) exhalePending = false;
      if (sp.airlock) removeTileEl(sp.airlock.id, moveMs + 200);
      if (sp.tile) tileEl(sp.tile, true);
      if (sp.silt && !siltSeen) { siltSeen = true; msg('de_silt_line', DE_LEX.de_silt_line); }

      /* 5. the trickster sees the truth AFTER the ledger moved */
      const locked = isLocked(board);
      deck('trickster', 'afterMove', {
        moved: true,
        merges: result.merges.map((m) => ({ tier: m.tier, link: m.link })),
        spawn: sp.tile ? { tier: sp.tile.silt ? 0 : sp.tile.tier, r: sp.tile.r, c: sp.tile.c, silt: !!sp.tile.silt } : null,
        locked,
      });

      /* 6. heat, then the board's verdicts */
      heat();
      if (locked) {
        after(moveMs + 80, resurface);
        return true;
      }
      if (!exhaleUsed && occupancy(board) >= plan.exhaleAt) startExhale();
      strainCheck();
      /* The lock comes off at MOVE_MS and nothing extends it: every step
       * above is synchronous, `after` is only scaled by the harness's own
       * timeScale (1 in production), and a merge victim left tileEls the
       * moment it was removed, so a queued move landing here can never
       * re-slide or resurrect one. */
      after(moveMs, release);
      return true;
    }

    /** THE WALL's shake: a class on the board (style.js keyframes; reduced
     *  motion kills the animation, the cue and the casino's flash remain). */
    function shakeBoard(dir) {
      if (!boardEl || !boardEl.classList) return;
      boardEl.classList.remove(...BUMP_CLASSES);
      try { if (typeof boardEl.offsetWidth === 'number') void boardEl.offsetWidth; } catch (e) { /* ignore */ }
      boardEl.classList.add('g-de-bump-' + dir);
      if (bumpTimer) clearTimer(bumpTimer);
      bumpTimer = after(PLAYTEST.BUMP_MS, () => { bumpTimer = 0; if (boardEl) boardEl.classList.remove(...BUMP_CLASSES); });
    }

    /** STUCK: the wall cells of every direction that would move pulse once. */
    function stuckHint() {
      if (!board || !cellEls.length) return;
      const dirs = Object.keys(DIRV).filter((d) => wouldMove(board, d));
      if (!dirs.length) return;
      stuckHints += 1;
      for (const d of dirs) {
        for (let i = 0; i < n; i++) {
          const ix = d === 'up' ? i : d === 'down' ? (n - 1) * n + i : d === 'left' ? i * n : i * n + n - 1;
          if (cellEls[ix]) cellEls[ix].classList.add('is-hint');
        }
      }
      if (hintTimer) clearTimer(hintTimer);
      hintTimer = after(PLAYTEST.HINT_MS, () => { hintTimer = 0; clearHint(); });
      msg('de_stuck_hint', DE_LEX.de_stuck_hint);
    }
    function clearHint() {
      if (hintTimer) { clearTimer(hintTimer); hintTimer = 0; }
      for (const c of cellEls) c.classList.remove('is-hint');
    }

    function onNewDeepest(d, node) {
      deck('casino', 'newDeepest', d, node);
      deck('pressure', 'newDeepest', d, node);
      if (d >= TIER_MAX) return;             // the ceiling has its own ceremony
      if (d >= 3) {
        try { ctx.ceremonies.stamp({ text: tierName(d), target: bench }); } catch (e) { /* noop */ }
        msg(d > lifetimeBefore ? 'de_lifetime_new' : 'de_new_depth',
          d > lifetimeBefore ? DE_LEX.de_lifetime_new : DE_LEX.de_new_depth);
      }
      rewardBeat();
    }

    /** The variable-ratio canon: the engine's roll, else a seeded local one. */
    function rewardBeat() {
      let r = null;
      try {
        if (ctx.engine && typeof ctx.engine.rewardRoll === 'function') r = ctx.engine.rewardRoll({ streak: chain, success: true }) || null;
      } catch (e) { r = null; }
      if (!r) r = rollLocal();
      if (r.jackpot) {
        jackpots += 1;
        try { ctx.ceremonies.reward('jackpot', { target: boardEl, text: t('de_jackpot', DE_LEX.de_jackpot) }); } catch (e) { /* noop */ }
        fireSafe('flash_burst', { count: 2, alpha: 0.45 });     // decoration (fireSafe welds it)
        tick('jackpot', 0.7);
        current();
        return;
      }
      if (r.fire) { current(); return; }
      if (r.nearMiss) {
        try { ctx.ceremonies.reward('near_miss', { target: boardEl, text: t('de_near_miss', DE_LEX.de_near_miss) }); } catch (e) { /* noop */ }
        tick('near_miss', 0.35);
      }
    }

    /** A passing current: the player's own loops sweep across the board. */
    function current() {
      currents += 1;
      let x; let y;
      try {
        const rect = boardEl.getBoundingClientRect();
        const vw = (typeof window !== 'undefined' && window.innerWidth) || 0;
        const vh = (typeof window !== 'undefined' && window.innerHeight) || 0;
        if (rect && vw > 0 && vh > 0) {
          const dir = plan.currentDir[currentIdx % plan.currentDir.length];
          currentIdx += 1;
          x = Math.round(((rect.left + rect.width * (dir < 0 ? 0.25 : 0.75)) / vw) * 100);
          y = Math.round(((rect.top + rect.height / 2) / vh) * 100);
        }
      } catch (e) { /* the engine picks its own spot */ }
      let url;
      try { if (pool && typeof pool.next === 'function') { const got = pool.next('loop'); url = got && got.url ? got.url : undefined; } } catch (e) { url = undefined; }
      fireSafe('gif_burst', { count: 3, variant: 'scatter', x, y, url, assetKind: 'loop', holdMs: 900 });
    }

    /** The strain: two deepest tiles almost meeting. Casino glow + a low hum. */
    function strainCheck() {
      const pair = strainPair(board);
      for (const node of liveTiles()) node.classList.remove('is-strain');
      if (!pair) { lastStrainKey = ''; return; }
      const a = tileEls.get(pair[0].id); const b = tileEls.get(pair[1].id);
      if (a) a.classList.add('is-strain');
      if (b) b.classList.add('is-strain');
      const key = pair[0].id + ':' + pair[1].id + ':' + pair[0].tier;
      const now = elapsedMs;
      if (key === lastStrainKey || (now - lastStrainAt) < PLAYTEST.STRAIN_COOLDOWN_MS) return;
      lastStrainKey = key;
      lastStrainAt = now;
      strains += 1;
      deck('casino', 'almost', a, b);
      tick('near_miss', 0.3);
      msg('de_strain', DE_LEX.de_strain);
    }

    /* ==================================================================== *
     * EXHALE (mercy, once per dive) / RESURFACE / CEILING / BELL
     * ==================================================================== */
    function startExhale() {
      exhaleUsed = true;
      exhalePending = true;
      exhaleOn = true;
      setPhase(basePhase());
      deck('casino', 'exhale', true);
      deck('pressure', 'exhale', true);
      msg('de_exhale_line', DE_LEX.de_exhale_line);
      tick('whisper', 0.3);
      heat();
      after(plan.exhaleMs, () => {
        exhaleOn = false;
        deck('casino', 'exhale', false);
        deck('pressure', 'exhale', false);
        if (!ended) setPhase(basePhase());
        heat();
      });
    }

    function resurface() {
      if (dead || ended) return;
      busy = true;
      clearQueue();                       // the dive that press was aimed at is over
      setPhase('resurface');
      resurfaces += 1;
      bestDeepest = Math.max(bestDeepest, diveDeepest);
      deck('casino', 'resurface');
      deck('pressure', 'resurface');
      try { ctx.ceremonies.stamp({ text: t('de_stamp_resurface', DE_LEX.de_stamp_resurface), tone: 'pink', target: bench }); } catch (e) { /* noop */ }
      msg('de_resurface_line', DE_LEX.de_resurface_line);
      tick('stamp_bad', 0.3);                      // the loss: a muted thud, never silence
      tick('wash', 0.4);
      stopDrift();
      for (const node of liveTiles()) node.classList.add('is-gone');
      sustainSafe('bubble_field', { clickSafe: true, variant: 'rise', max: 10 });
      const drainMs = reduced ? PLAYTEST.DRAIN_MS_REDUCED : PLAYTEST.DRAIN_MS;
      after(drainMs, () => {
        stopSafe('bubble_field');
        // the bell can land mid-drain: the class is over, the water stays drained
        if (ended) return;
        for (const id of Array.from(tileEls.keys())) removeTileEl(id, 0);
        drain(board);
        newDive();
        setPhase(basePhase());
        release();
        say('resurface #' + resurfaces + ' (banked tier ' + bestDeepest + ')');
      });
    }

    function newDive() {
      dives += 1;
      clearQueue();                       // a queued press belonged to the old board
      clearGrab();                        // and so did any hand still on it
      diveDeepest = 0;
      ceilingCelebrated = false;           // a fresh dive may earn the royal again
      ceilingHold = false;
      exhaleUsed = false;
      exhalePending = false;
      exhaleOn = false;
      lastStrainKey = '';
      /* THE DEV SEAM: with ?deep=N the dive OPENS at tier N instead of being
       * played there. Off (devDeepest 0) in every build the shell starts. */
      const opening = devDeepest > 0
        ? devBoard(board, devDeepest, board.rng)
        : openingSpawn(board, plan.spawnTable);
      for (const tile of opening) tileEl(tile, true);
      diveDeepest = boardDeepest(board);
      bestDeepest = Math.max(bestDeepest, diveDeepest);
      chain = 0;
      paintChain();
      markDeepest();
      paintHud();
      heat();
    }

    /** THE CEILING. A timed class ends here, warm. A FREE SWIM does not: the
     *  royal fires (once per dive - `ceilingCelebrated`) and the water stays
     *  open. Tier-11 tiles never merge, so the board fills on its own and the
     *  dive ends in a resurface like any other, which re-arms the ceremony. */
    function ceiling() {
      if (dead || ended) return;
      clearQueue();                       // the royal owns the board now
      ceilingReached = true;
      survived = true;
      setPhase('ceiling');
      deck('casino', 'ceiling');
      deck('pressure', 'ceiling');
      try { ctx.ceremonies.reward('jackpot', { intensity: 1, target: boardEl, text: t('de_stamp_ceiling', DE_LEX.de_stamp_ceiling) }); } catch (e) { /* noop */ }
      try { ctx.ceremonies.stamp({ text: t('de_stamp_ceiling', DE_LEX.de_stamp_ceiling), target: bench }); } catch (e) { /* noop */ }
      msg('de_ceiling_line', DE_LEX.de_ceiling_line);
      tick('jackpot', 0.9);
      fireSafe('flash_burst', { count: 3, alpha: 0.5 });
      current();
      const ceremonyMs = reduced ? PLAYTEST.CEREMONY_MS_REDUCED : PLAYTEST.CEREMONY_MS;
      if (endless) {
        // the royal owns the phase while it runs (an exhale that lands mid
        // ceremony must not steal the stage), then the water re-opens
        ceilingHold = true;
        after(ceremonyMs, () => {
          ceilingHold = false;
          if (dead || ended) return;
          if (stage && stage.getAttribute('data-phase') === 'resurface') return;
          setPhase(basePhase());
        });
        return;
      }
      busy = true;
      stopClock();
      after(ceremonyMs, () => finish(true));
    }

    /** THE SURFACE BUTTON (free swim only): the bell path, on the player's own
     *  cue. Exactly once - the button disables itself and `surfaced` latches. */
    function onSurface() {
      if (dead || ended || surfaced || !endless) return;
      surfaced = true;
      try { if (surfaceBtn) surfaceBtn.disabled = true; } catch (e) { /* noop */ }
      stopClock();
      say('free swim: surfacing at ' + clockText());
      bell();
    }

    function bell() {
      if (dead || ended) return;
      busy = true;
      clearQueue();                       // the water is closed; nothing drains after the bell
      clearGrab();
      bellOn = true;
      setPhase('bell');
      survived = !isLocked(board);
      bestDeepest = Math.max(bestDeepest, diveDeepest);
      deck('casino', 'dimOut');
      deck('pressure', 'dimOut');
      try { ctx.ceremonies.stamp({ text: t('de_stamp_bell', DE_LEX.de_stamp_bell), tone: 'pink', target: bench }); } catch (e) { /* noop */ }
      msg('de_bell_line', DE_LEX.de_bell_line);
      tick('stamp', 0.6);
      after(reduced ? PLAYTEST.CEREMONY_MS_REDUCED : PLAYTEST.CEREMONY_MS, () => finish(false));
    }

    /* ==================================================================== *
     * THE END - exactly one endClass, after the end card has been seen
     * ==================================================================== */
    function finish(viaCeiling) {
      if (ended) return;
      ended = true;
      busy = true;
      stopClock();
      stopAmbience();
      if (subTimer) { clearTimer(subTimer); subTimer = 0; }
      if (shimmerTimer) { clearTimer(shimmerTimer); shimmerTimer = 0; }
      if (stallTimer) { clearTimer(stallTimer); stallTimer = 0; }
      clearQueue();
      clearGrab();
      clearHint();
      deck('trickster', 'stop');
      deck('casino', 'stop');
      deck('pressure', 'stop');
      paintHud();                                  // truth on every chip, whatever the trickster left
      for (const tile of board.tiles) { const node = tileEls.get(tile.id); if (node) paintName(node, tile); }

      /* A FREE SWIM is never graded (the shell records no row either), so the
       * composite is not even computed: the report carries the flag and an
       * empty ledger. Everything else - the meta merge, the end card, the one
       * endClass after the hold - is the same class ending. */
      const graded = endless ? null : compositeFor({
        gradeTier: tier, bestDeepest, chainLinks, merges, swipes, survived, resurfaces,
      });
      const gates = endless ? {} : hardGates(n);
      const fx = endless ? 0 : flavorXp(lifetimeBefore, bestDeepest);
      const lifetimeAfter = Math.max(lifetimeBefore, bestDeepest);

      let priorDives = 0;
      try { priorDives = Math.max(0, Number((ctx.store.gameMeta(GAME_KEY) || {}).dives) || 0); } catch (e) { priorDives = 0; }
      try {
        ctx.store.mergeGameMeta(GAME_KEY, {
          lifetimeDeepest: lifetimeAfter, dives: priorDives + dives, lastSeed: seed, lastPlayedAt: Date.now(),
        });
      } catch (e) { say('meta write failed (class unaffected): ' + ((e && e.message) || e)); }

      renderEnd(graded, lifetimeAfter, viaCeiling);
      setPhase('ended');

      const report = endless
        ? { endless: true, metrics: {}, hardGates: {}, flavorXp: 0 }
        : { metrics: { composite: graded.composite }, hardGates: gates, flavorXp: fx };
      lastReport = Object.assign({}, report, {
        inputs: {
          tier, n, seed, retake, endless, bestDeepest, chainLinks, merges, swipes, survived, resurfaces,
          score, maxChain, dives, ceiling: !!viaCeiling, lifetimeBefore, lifetimeAfter,
          elapsedMs, terms: graded ? graded.terms : null, tax: graded ? graded.tax : 1,
        },
      });
      try { lastSnapshot = instance.snapshot(); } catch (e) { /* diagnostics only */ }
      say((endless ? 'free swim over: ' : 'class over: ') + 'best tier ' + bestDeepest + ', score ' + score + ', '
        + merges + ' merges / ' + swipes + ' swipes, chains ' + chainLinks + ', resurfaces ' + resurfaces
        + (endless
          ? ', ' + clockText() + ' swum' + (ceilingReached ? ', CEILING' : '') + ' -> ungraded'
          : (viaCeiling ? ', CEILING' : survived ? ', survived' : ', locked at the bell')
            + ' -> composite ' + graded.composite.toFixed(3) + ' (x' + graded.tax.toFixed(3) + ')'));

      after(reduced ? PLAYTEST.END_HOLD_MS_REDUCED : PLAYTEST.END_HOLD_MS, () => {
        if (reported) return;
        reported = true;
        try { ctx.endClass(report); } catch (e) { say('endClass threw: ' + ((e && e.message) || e)); }
      });
    }

    function renderEnd(graded, lifetimeAfter, viaCeiling) {
      if (!endEl) return;
      endEl.textContent = '';
      endEl.hidden = false;
      endEl.appendChild(el('h3', 'g-de-end-title', endless
        ? t('de_end_title_free', DE_LEX.de_end_title_free)
        : t('de_end_title', DE_LEX.de_end_title)));
      const row = (cls, k, v) => {
        const r = el('div', 'g-de-end-row' + (cls ? ' ' + cls : ''));
        r.appendChild(el('span', 'g-de-end-k', k));
        r.appendChild(el('span', 'g-de-end-v', v));
        endEl.appendChild(r);
        return r;
      };
      const best = row('g-de-end-best', t('de_end_best', DE_LEX.de_end_best), tierName(Math.max(1, bestDeepest)));
      best.setAttribute('data-tier', String(Math.max(1, bestDeepest)));
      row('', t('de_end_score', DE_LEX.de_end_score), String(score));
      row('', t('de_end_chains', DE_LEX.de_end_chains), 'x ' + maxChain + ' / ' + chainLinks);
      /* A FREE SWIM has nothing to be measured against, so it reports the swim
       * itself: how many dives, how long. No survival, efficiency, resurface or
       * ceiling row - none of them mean anything without a bell. */
      if (endless) {
        row('', t('de_end_dives', DE_LEX.de_end_dives), String(dives));
        row('', t('de_end_time', DE_LEX.de_end_time), mmss(secElapsed()));
      } else {
        row('', t('de_end_efficiency', DE_LEX.de_end_efficiency), (swipes > 0 ? (merges / swipes) : 0).toFixed(2));
        row('', t('de_end_survival', DE_LEX.de_end_survival), survived ? t('de_end_yes', DE_LEX.de_end_yes) : t('de_end_no', DE_LEX.de_end_no));
        row('', t('de_end_resurfaces', DE_LEX.de_end_resurfaces), String(resurfaces));
        if (viaCeiling) row('g-de-end-ceiling', t('de_end_ceiling', DE_LEX.de_end_ceiling), t('de_end_yes', DE_LEX.de_end_yes));
      }
      const dare = el('div', 'g-de-end-dare');
      dare.setAttribute('data-tier', String(Math.max(1, lifetimeAfter)));
      dare.appendChild(el('span', 'g-de-end-k', t('de_end_dare', DE_LEX.de_end_dare)));
      dare.appendChild(el('span', 'g-de-end-v', tierName(Math.max(1, lifetimeAfter))));
      dare.appendChild(el('p', 'g-de-end-line', lifetimeBefore > 0
        ? t('de_end_dare_line', DE_LEX.de_end_dare_line)
        : t('de_end_dare_first', DE_LEX.de_end_dare_first)));
      endEl.appendChild(dare);
    }

    /* ==================================================================== *
     * AMBIENCE (the tier dials)
     * ==================================================================== */
    function openAmbience() {
      heat();
      if (plan.wash) sustainSafe('wash', { variant: 'drain', sustainForever: true });
      if (plan.ambient) sustainSafe('ambient_field', { kind: 'bubbles' });
      armSubFlash();
      armShimmer();
    }
    function stopAmbience() {
      stopDrift();
      for (const k of ['wash', 'ambient_field', 'bubble_field']) stopSafe(k);
    }
    /** sub_flash on the plan's cadence (tier 2+), heat-shortened, seed-jittered. */
    function armSubFlash() {
      if (!plan || plan.subFlashMs <= 0 || ended) return;
      const ms = cadenceMs(plan.subFlashMs, currentHeat, plan.subJitter[subIdx % plan.subJitter.length]);
      subTimer = after(ms, () => {
        subTimer = 0;
        const r = fireSafe('sub_flash', { anchor: well, variant: plan.subVariants[subIdx % plan.subVariants.length] });
        if (r) subFlashes += 1;
        subIdx += 1;
        armSubFlash();
      });
    }
    /** glitch_swap face-shimmer on the two deepest tiles (tier 3+). onSwap
     *  swaps NOTHING - the value never lies, only the face wobbles. Targets
     *  are the tiles' FACE nodes, never the tile (see the header note). */
    function armShimmer() {
      if (!plan || plan.shimmerMs <= 0 || ended) return;
      const ms = cadenceMs(plan.shimmerMs, currentHeat, plan.shimmerJitter[shimmerIdx % plan.shimmerJitter.length]);
      shimmerTimer = after(ms, () => {
        shimmerTimer = 0;
        shimmerIdx += 1;
        const ranked = board.tiles.filter((x) => !x.silt).sort((a, b) => b.tier - a.tier).slice(0, 2);
        const faces = [];
        for (const tile of ranked) { const node = tileEls.get(tile.id); if (node) faces.push(...faceOf(node)); }
        if (faces.length) {
          const r = fireSafe('glitch_swap', { targets: faces, seconds: 0.5, onSwap: () => {}, sfx: false });
          if (r) shimmers += 1;
        }
        armShimmer();
      });
    }
    /** row_drift past the depth line (tier 3+), on the static cell floor. Off under reduced motion. */
    function rowDriftCheck(depthLine) {
      if (!plan || !plan.rowDrift || reduced || ended) return;
      const want = depthLine > plan.rowDriftDepth;
      if (want && !driftOn) {
        const targets = cellEls.filter((c) => (Number(c.getAttribute('data-r')) % 2) === 0);
        const h = sustainSafe('row_drift', { targets, axis: 'x', variant: 'sway', amplitudeMult: 0.5, stagger: false });
        driftOn = !!h;
      } else if (!want && driftOn) {
        stopDrift();
      }
    }
    function stopDrift() { if (driftOn) { stopSafe('row_drift'); driftOn = false; } }

    /* ---- clock ---------------------------------------------------------- */
    function startClock() {
      lastTick = Date.now();
      clockId = every(250, () => {
        if (ended) return;
        const now = Date.now();
        const dt = now - lastTick;
        lastTick = now;
        elapsedMs += dt / Math.max(0.0001, timeScale);
        if (clockChip) clockChip.textContent = clockText();
        /* FREE SWIM: the clock is a stopwatch, not a fuse. No bell warning, no
         * bell - only the Surface button (or the panic ladder) ends it. */
        if (endless) return;
        const left = secLeft();
        if (!bellOn && left <= plan.bellWarnSec && elapsedMs < budgetMs) {
          bellOn = true;
          setPhase(basePhase());
          deck('casino', 'bell', true);
          deck('pressure', 'bell', true);
          msg('de_bell_warn', DE_LEX.de_bell_warn);
          tick('sting', 0.4);
        }
        if (elapsedMs >= budgetMs) { stopClock(); run(bell); }
      });
    }
    function stopClock() { if (clockId) { clearTimer(clockId); clockId = 0; } }

    /* ---- assets (never block a draw) ------------------------------------ */
    function claimAssets() {
      Promise.resolve()
        .then(() => ctx.assets.claim({ loops: 24, targets: 0, stills: 12, canvasSafe: false }))
        .then((p) => {
          if (dead || !p || typeof p.next !== 'function') return;
          pool = p;
          run(dressAllFaces);                     // the tiles already on the board get their faces now
        })
        .catch((e) => say('asset claim failed - plain faces, currents fall back to the engine pool: ' + ((e && e.message) || e)));
    }

    /* ---- the class rules sheet (Deck VI, Law IV: drawn, not told) --------- */
    /**
     * Four vignettes in this pool's own language: the swipe that moves the
     * whole board at once, two matching tiles meeting and sinking one depth,
     * the locked board banking its depth and refilling, and the eleventh rung
     * where the ladder simply stops. Every figure is CSS on the same tile
     * chrome the board uses (the drawn ring glyph included), so the sheet costs
     * no media and reads at any size.
     */
    let howtoEl = null;

    /** Tiers this player has already had the rules sheet for (persisted). */
    function howtoSeenTiers() {
      try {
        const m = (ctx.store && typeof ctx.store.gameMeta === 'function')
          ? (ctx.store.gameMeta(GAME_KEY) || {}) : {};
        return Array.isArray(m.howtoTiers) ? m.howtoTiers.slice() : [];
      } catch (e) { return []; }
    }

    function hideHowto() {
      if (howtoEl) { try { howtoEl.remove(); } catch (e) { /* noop */ } }
      howtoEl = null;
    }

    function buildHowto(onGo) {
      const sheet = el('div', 'g-de-howto');
      sheet.appendChild(el('h2', 'g-de-hw-title', t('de_howto_title', DE_LEX.de_howto_title)));

      const row = (build, caption) => {
        const r = el('div', 'g-de-hw-row');
        const fig = el('span', 'g-de-hw-fig');
        fig.setAttribute('aria-hidden', 'true');
        try { build(fig); } catch (e) { /* a caption alone still teaches */ }
        r.appendChild(fig);
        r.appendChild(el('p', 'g-de-hw-cap', caption));
        sheet.appendChild(r);
        return r;
      };

      /** A mini tile wearing a depth tier - the same data-tier the board uses,
       *  so the palette and the drawn rings come from the one place. */
      const tile = (cls, tierN) => {
        const n2 = el('span', 'g-de-hw-tile' + (cls ? ' ' + cls : ''));
        n2.setAttribute('data-tier', String(tierN == null ? 1 : tierN));
        n2.appendChild(el('span', 'g-de-hw-num', String(tierN == null ? 1 : tierN)));
        return n2;
      };

      /* 1 - THE SWIPE. Four arrows around a board whose whole row slides. */
      row((fig) => {
        const pad = el('span', 'g-de-hw-pad');
        for (const d of ['up', 'right', 'down', 'left']) pad.appendChild(el('i', 'g-de-hw-arrow ' + d));
        fig.appendChild(pad);
        const line = el('span', 'g-de-hw-line slide');
        for (let i = 0; i < 3; i++) {
          const n2 = tile(null, i + 1);
          n2.style.setProperty('--de-hw-i', String(i));
          line.appendChild(n2);
        }
        fig.appendChild(line);
      }, t('de_howto_swipe', DE_LEX.de_howto_swipe));

      /* 2 - THE MERGE. Two equal tiles close on each other and come back as
         one, a depth further down; the water darkens behind them. */
      row((fig) => {
        const scene = el('span', 'g-de-hw-scene');
        scene.appendChild(tile('mrg a', 4));
        scene.appendChild(tile('mrg b', 4));
        scene.appendChild(tile('mrg out', 5));
        fig.appendChild(scene);
      }, t('de_howto_merge', DE_LEX.de_howto_merge));

      /* 3 - THE RESURFACE. A jammed board drains and refills; the mark it
         reached is banked on the gauge beside it. */
      row((fig) => {
        const grid4 = el('span', 'g-de-hw-grid');
        for (let i = 0; i < 4; i++) {
          const n2 = tile('drain', 2 + (i % 3));
          n2.style.setProperty('--de-hw-i', String(i));
          grid4.appendChild(n2);
        }
        fig.appendChild(grid4);
        const gauge = el('span', 'g-de-hw-gauge');
        gauge.appendChild(el('i'));
        fig.appendChild(gauge);
      }, t('de_howto_resurface', DE_LEX.de_howto_resurface));

      /* 4 - THE CEILING. The ladder, and the rung it stops on. */
      row((fig) => {
        const ladder = el('span', 'g-de-hw-ladder');
        for (let i = 1; i <= 11; i++) {
          const rung = el('i', i === 11 ? 'top' : null);
          rung.setAttribute('data-tier', String(i));
          rung.style.setProperty('--de-hw-i', String(i - 1));
          ladder.appendChild(rung);
        }
        fig.appendChild(ladder);
        fig.appendChild(tile('ceil', 11));
      }, t('de_howto_ceiling', DE_LEX.de_howto_ceiling));

      const go = el('button', 'g-de-hw-go', t('de_howto_go', DE_LEX.de_howto_go));
      go.setAttribute('type', 'button');
      try { go.type = 'button'; } catch (e) { /* the DOM double has no button semantics */ }
      go.addEventListener('click', () => { try { onGo(); } catch (e) { say('howto go: ' + ((e && e.message) || e)); } });
      sheet.appendChild(go);
      try { if (typeof go.focus === 'function') go.focus(); } catch (e) { /* noop */ }
      return sheet;
    }

    /**
     * Policy is the shell's "Skip class tutorials" contract: by default the
     * sheet shows EVERY class; with the skip on, the pool still explains itself
     * ONCE per grade tier. Dismissal is the sheet's own button and nothing
     * else - every arrow key and the whole board are verbs here, so a key
     * shortcut would be a move played against a board that has not dealt.
     */
    function howto(onDone) {
      if (ctx.hideTutorial === true && howtoSeenTiers().indexOf(tier) >= 0) { onDone(); return; }
      if (ctx.dev === true && spec && spec.devSkipHowto === true) { onDone(); return; }
      if (!stage) { onDone(); return; }
      const tierNow = tier;
      let done = false;
      let sheet = null;
      try {
        sheet = buildHowto(() => {
          if (done || dead || ended) return;
          done = true;
          try {
            const seen = howtoSeenTiers();
            if (seen.indexOf(tierNow) < 0) {
              seen.push(tierNow);
              if (ctx.store && typeof ctx.store.mergeGameMeta === 'function') {
                ctx.store.mergeGameMeta(GAME_KEY, { howtoTiers: seen });
              }
            }
          } catch (e) { /* best effort - the sheet just shows again next time */ }
          hideHowto();
          onDone();
        });
      } catch (e) { say('rules sheet refused: ' + ((e && e.message) || e)); sheet = null; }
      if (!sheet) { onDone(); return; }
      howtoEl = sheet;
      stage.appendChild(sheet);
    }

    /* ==================================================================== *
     * THE MODULE INSTANCE
     * ==================================================================== */
    const instance = {
      start(classSpec) {
        spec = classSpec || { gradeTier: 1, seed: GAME_KEY + '|none', timeBudgetSec: 300 };
        tier = Math.max(1, Math.min(4, Math.round(Number(spec.gradeTier) || 1)));
        seed = String(spec.seed == null ? GAME_KEY : spec.seed);
        /* FREE SWIM: `timeBudgetSec: 0` is a real answer here, not a missing
         * one, so it must NOT be clamped up to the 20s floor - the budget is
         * Infinity and the clock becomes a stopwatch. */
        endless = spec.endless === true;
        opened = false;                     // the briefing owns the board first
        queued = null;
        drained = 0;
        grab = null;
        surfaced = false;
        ceilingCelebrated = false;
        ceilingHold = false;
        budgetMs = endless ? Infinity : Math.max(20000, (Number(spec.timeBudgetSec) || 300) * 1000);
        retake = endless ? false : !!spec.retake;
        /* `ctx.dev` is the ONE gate, and only the scratch rig sets it. Without
         * it a devDeepest on the spec is read by nothing at all. */
        devDeepest = 0;
        if (ctx.dev === true) {
          const want = Math.round(Number(spec.devDeepest));
          if (Number.isFinite(want) && want >= 2 && want <= TIER_MAX) devDeepest = want;
          if (devDeepest) say('DEV: opening every dive at tier ' + devDeepest);
        }
        reduced = probeReduced(ctx);
        /* PASS 5 - THE PERF LADDER, resolved BEFORE the first node: the stage,
         * the face budget and the engine's video budget all read it. `full` is
         * an explicit opt-out and is honoured even under reduced motion (the
         * player asked for the room); `auto` is the only value that probes. */
        perfSetting = perfFromSetting(ctx.settings ? ctx.settings.de_perf : 'auto');
        perf = 'full';
        perfReason = '';
        perfProbeDone = false;
        perfLogged = false;
        setLiteClass(false);
        if (perfSetting === 'lite') applyPerf('lite', 'setting');
        else if (perfSetting === 'auto' && (reduced || motionLevelOf() <= 1)) applyPerf('lite', 'reduced motion');
        else applyPerf('full', perfSetting === 'full' ? 'setting' : 'auto: probing');
        n = sizeFromSetting(ctx.settings ? ctx.settings.de_board_size : '4x4');
        {
          const want = String(ctx.settings && ctx.settings.de_tile_faces != null ? ctx.settings.de_tile_faces : 'media').trim().toLowerCase();
          facesMode = FACE_MODES.includes(want) ? want : 'media';
          faceKind = (reduced || motionLevelOf() <= 1 || facesMode === 'still') ? 'still' : 'loop';
          faceUrls.clear();
        }
        // the plan's own draws do not depend on the budget; a free swim deals the
        // same seeded show a 300s class would (never Infinity into the clamp)
        plan = buildPlan({ seed, gradeTier: tier, size: n, timeBudgetSec: endless ? 300 : budgetMs / 1000 });
        board = createBoard(n, seed);
        rollLocal = (() => {
          const roll = makeTaggedRoll(seed + '|de-vr');
          return () => {
            const base = 0.30 + 0.30 * currentHeat;
            const chance = Math.min(1, base + Math.min(PLAYTEST.CHAIN_CAP, chain) * 0.03);
            const r = roll('fire');
            const fire = r < chance;
            return { fire, jackpot: fire && roll('jack') >= 0.85, nearMiss: !fire && r < chance + 0.08 };
          };
        })();

        try { lifetimeBefore = Math.max(0, Number((ctx.store.gameMeta(GAME_KEY) || {}).lifetimeDeepest) || 0); }
        catch (e) { lifetimeBefore = 0; }

        try { injectDeepEndStyle(); } catch (e) { say('style inject failed (class unaffected): ' + ((e && e.message) || e)); }
        buildDom();

        const capsOk = !(ctx.caps && Number(ctx.caps.bgIntensity) === 0);
        try {
          casino = createDeCasino({
            seed, tier, stage, bench, board: boardEl, backdrop,
            timers: deckTimers, reduced, capsOk, log: say,
          });
        } catch (e) { casino = null; say('casino refused: ' + ((e && e.message) || e)); }
        try {
          trickster = createDeTrickster({
            seed, tier, timers: deckTimers, reduced, capsOk,
            isHalted: () => dead || paused || ended || busy,
            stats: () => ({ moves: swipes, depth: boardDeepest(board), secLeft: secLeft(), score, chain, resurfaces }),
            chipEl: (which) => (which === 'clock' ? clockChip : which === 'score' ? scoreChip : depthChip),
            chipText,
            tiles: () => liveTiles(),
            tierName,
            /* Law VII: the trickster's two taunts come through the lexicon, and
             * land on the ONE proctor line (one message at a time - a lie that
             * lands while a real line is up simply waits for its next deal). */
            t,
            announce: (text, ms) => {
              if (!msgEl || !text) return;
              msgEl.textContent = String(text);
              const mine = msgEl.textContent;
              deckTimers.after(Math.max(400, Number(ms) || 1600), () => {
                if (msgEl && msgEl.textContent === mine) msgEl.textContent = '';
              });
            },
            log: say,
          });
        } catch (e) { trickster = null; say('trickster refused: ' + ((e && e.message) || e)); }
        /* DECK III - THE PRESSURE. Built last (it layers OVER the casino's
         * light) and handed the game's own DOM, the game's pause-aware timer
         * registry and a READ-ONLY view of the engine: it may spend the
         * channels, never raise them. */
        try {
          pressure = createDePressure({
            seed,
            gradeTier: tier,
            reduced,
            motionLevel: motionLevelOf(),
            stage,
            bench,
            board: boardEl,
            hud: { score: scoreChip, depth: depthChip, chain: chainChip, clock: clockChip },
            engine: deckEngine,
            assets: deckAssets,
            timers: deckTimers,
            capsOk: capsArmed,
            log: say,
          });
        } catch (e) { pressure = null; say('pressure refused: ' + ((e && e.message) || e)); }

        claimAssets();

        /* THE SHEET FIRST (Deck VI), then the briefing, then the water
           opens. Nothing that measures the player runs until GO: no key
           is bound, no dive is dealt and the clock has not started, so a
           class read at leisure grades exactly like one that skipped the
           sheet. */
        const beginClass = () => {
          if (dead || ended) return;
          bindInput();
          newDive();
          startPerfProbe();                    // the board is dealt: start counting frames
          startClock();
          stallTimer = every(PLAYTEST.STALL_TICK_MS, () => {
            if (ended || busy) return;
            stallMs += PLAYTEST.STALL_TICK_MS;
            deck('trickster', 'stalled', stallMs);
            // STUCK: one pulse per stall, then silence until the next move
            if (!stuckShown && stallMs >= PLAYTEST.STUCK_MS && board && !isLocked(board)) {
              stuckShown = true;
              stuckHint();
            }
          });

          /* the briefing: one line, then the water opens */
          if (endless) msg('de_brief_free', DE_LEX.de_brief_free);
          else msg('de_brief', DE_LEX.de_brief);
          after(reduced ? PLAYTEST.BRIEF_MS_REDUCED : PLAYTEST.BRIEF_MS, () => {
            setPhase(basePhase());
            msg('de_play_hint', DE_LEX.de_play_hint);
            deck('casino', 'start');
            deck('pressure', 'start');
            deck('trickster', 'start');
            openAmbience();
            // the water opens: from here a press against the lock is REMEMBERED
            // (the briefing itself is not a lock the player was playing against,
            // so nothing it swallowed drains into the first dive)
            opened = true;
            busy = false;
          });
        };
        howto(beginClass);

        liveClass = instance;
        lastReport = null;
        lastSnapshot = null;
        say('tier ' + tier + ' board ' + n + 'x' + n + ', budget ' + (endless ? 'FREE SWIM' : Math.round(budgetMs / 1000) + 's') + ', heat cap '
          + plan.heatCap + (plan.siltChance ? ', silt ' + plan.siltChance : '') + (reduced ? ', reduced' : '')
          + (retake ? ', RETAKE' : '') + ', lifetime ' + lifetimeBefore);
      },

      pause() {
        if (paused) return;
        paused = true;
        // a frozen class holds no hand and remembers no press
        clearGrab();
        clearQueue();
        deck('pressure', 'pause');
        if (stage) stage.classList.add('suspended');
      },

      resume() {
        if (!paused) return;
        paused = false;
        if (stage) stage.classList.remove('suspended');
        deck('pressure', 'resume');
        lastTick = Date.now();
        const q = deferred.splice(0);
        for (const fn of q) run(fn);
      },

      /** The shell owns the overlay and the engine's suspend; we just freeze. */
      suspend(on) { if (on) instance.pause(); else instance.resume(); },

      destroy() {
        dead = true;
        hideHowto();
        perfProbeDone = true;                // any in-flight rAF step is a no-op now
        setLiteClass(false);                 // .ae-lite is on <html>: it outlives us unless we take it off
        clearQueue();
        clearGrab();
        opened = false;
        try { if (surfaceBtn) surfaceBtn.removeEventListener('click', onSurface); } catch (e) { /* noop */ }
        surfaceBtn = null;
        stopClock();
        clearTimers();
        stopAmbience();
        unbindInput();
        try { if (trickster) trickster.destroy(); } catch (e) { /* noop */ }
        trickster = null;
        try { if (casino) casino.destroy(); } catch (e) { /* noop */ }
        casino = null;
        try { if (pressure) pressure.destroy(); } catch (e) { /* noop */ }
        pressure = null;
        if (pool && typeof pool.release === 'function') { try { pool.release(); } catch (e) { /* noop */ } }
        pool = null;
        tileEls.clear();
        cellEls.length = 0;
        try { ctx.root.textContent = ''; } catch (e) { /* noop */ }
        if (liveClass === instance) liveClass = null;
      },

      /* -------- test / diagnostics seams (never read by the shell) -------- */
      /** Drive one move as the keyboard would. */
      input(dir) { return input(dir); },
      /** The live board model (the harness stages lock / ceiling boards on it). */
      board() { return board; },
      /** Press Surface as the player would (free swim only). */
      surface() { onSurface(); },
      /** The TRUE chip text - what the trickster restores after a lie. */
      chipText(which) { return chipText(which); },
      /** Pass 5: hand the auto probe a frame sample directly (the harness has no
       *  frame clock, and a real 3s sample is not a unit test). */
      perfJudge(deltas) { perfProbeDone = true; judgePerf(Array.isArray(deltas) ? deltas : []); return perf; },
      /** Re-sync every tile element to the model after the harness staged a board. */
      resync() {
        if (!board) return;
        for (const id of Array.from(tileEls.keys())) if (!board.tiles.some((x) => x.id === id)) removeTileEl(id, 0);
        for (const tile of board.tiles) { const node = tileEl(tile, false); paintName(node, tile); }
        diveDeepest = boardDeepest(board);
        bestDeepest = Math.max(bestDeepest, diveDeepest);
        markDeepest();
        paintHud();
        heat();
      },

      snapshot() {
        return {
          tier, n, seed, retake, reduced,
          plan: plan ? {
            heatCap: plan.heatCap, exhaleAt: plan.exhaleAt, subFlashMs: plan.subFlashMs, shimmerMs: plan.shimmerMs,
            rowDrift: plan.rowDrift, siltChance: plan.siltChance, spawnTable: plan.spawnTable,
          } : null,
          board: board ? serialize(board) : '',
          tiles: board ? board.tiles.map((x) => ({ id: x.id, tier: x.tier, r: x.r, c: x.c, silt: !!x.silt })) : [],
          deepest: board ? boardDeepest(board) : 0,
          occupancy: board ? occupancy(board) : 0,
          locked: board ? isLocked(board) : false,
          score, swipes, merges, chainLinks, chain, maxChain, dives, resurfaces,
          diveDeepest, bestDeepest, lifetimeBefore,
          exhaleUsed, exhalePending, exhaleOn, bellOn, ceilingReached, survived,
          jackpots, currents, subFlashes, shimmers, strains, stallMs, currentHeat, driftOn,
          endless, surfaced, ceilingCelebrated, surfaceBtn, secLeft: secLeft(), clockTruth: clockText(),
          facesMode, faceKind, faces: faceUrls.size, animatedFaces: animatedFaces(),
          /* pass 5 - THE PERF LADDER */
          perf, perfReason, perfSetting, perfProbeDone,
          faceCap: faceCap(), shallowStillMaxTier: shallowStillMaxTier(),
          lite: !!(stage && stage.classList && stage.classList.contains('g-de-lite')),
          slides, bumps, stuckHints, stuckShown,
          /* pass 3 - THE HAND + THE QUEUE */
          held: !!grab,
          grabDir: grab ? grab.dir : '',
          grabBlocked: !!(grab && grab.blocked),
          grabX: grab ? grab.gx : 0,
          grabY: grab ? grab.gy : 0,
          queued, drained, opened,
          elapsedMs, budgetMs, ended, reported, busy, paused, dead,
          phase: stage ? stage.getAttribute('data-phase') : null,
          liveTileEls: tileEls.size,
          casino: casino && typeof casino.diagnostics === 'function' ? (() => { try { return casino.diagnostics(); } catch (e) { return null; } })() : null,
          trickster: trickster && typeof trickster.diagnostics === 'function' ? (() => { try { return trickster.diagnostics(); } catch (e) { return null; } })() : null,
          pressure: pressure && typeof pressure.diagnostics === 'function' ? (() => { try { return pressure.diagnostics(); } catch (e) { return null; } })() : null,
          devDeepest,
          stage, boardEl, well, msgEl, endEl, depthChip, clockChip, scoreChip, chainChip,
        };
      },
    };
    return instance;
  },

  /** The live class's state, or null. Never read by the shell. */
  diagnostics() { return liveClass ? liveClass.snapshot() : null; },

  /** The last report handed to endClass (survives teardown). Diagnostics only. */
  get lastReport() { return lastReport; },

  /** The final board state of the last class (survives teardown). Diagnostics only. */
  get lastSnapshot() { return lastSnapshot; },

  setTimeScale,
};
