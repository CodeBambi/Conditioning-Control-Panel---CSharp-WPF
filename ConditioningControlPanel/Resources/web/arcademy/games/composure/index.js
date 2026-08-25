/* ============================================================================
 * games/composure/index.js - COMPOSURE (the sliding picture; family: puzzle,
 * Semester III). One 300s class of as many boards as you can bank, or an
 * untimed Zen table that banks them until you leave.
 *
 * THE PITCH. A 15-puzzle whose picture never sits still: every tile is a
 * clipped viewport into ONE looping clip (N <img> of the SAME url at different
 * object offsets - one decoder, and the loop stays in lockstep across the whole
 * board, which is the point). The Distraction Engine then lies to you: washes
 * bury the board while input stays live, and the trickster deck flashes false
 * previews of tiles in positions they are not in. The grade measures COMPOSURE,
 * not speed - backtracks, panic moves under a wash and assists cost you; the
 * clock does not.
 *
 * THE LOOP:
 *   press  = a tap on a tile beside the gap / an arrow or WASD key / a swipe
 *          -> slide (truth: board.js) -> backtrack + thrash check -> lock check
 *          -> casino light, pitch-ratcheted lock chime -> heat -> solved?
 *   LOCK     a tile sitting on its own home cell wears .is-locked and snaps.
 *            It is a MARKER, never a freeze: freezing tiles can make a solvable
 *            board unsolvable, and Law I does not allow the board to lie.
 *   WASH     the class's own burial. Input stays live the whole time; a
 *            backtrack made under a wash is THRASH and costs double.
 *   RESCUE   the critic's top fix, and it is LAW: 20s with no new piece home
 *            lights the baseline solver's next move (.is-hint) and fails the
 *            declared sGate, so a C-player finishes and the class stays honest.
 *   SOLVE    seams dissolve, the clip plays clean, the jackpot ladder rolls -
 *            and then the picture BANKS and a fresh scramble deals.
 *   BELL     THE LAW, and it replaced the old one: a timed class always fills
 *            its bell. A solve BANKS and RE-DEALS; it never ends the class.
 *            The bell is the single ending, and the board that was still in
 *            progress when it rang is graded as partial progress ON TOP of
 *            every banked solve (grade.js compositeFor).
 *   ZEN      untimed, gentle, its own board size. It banks and re-deals the
 *            same way - there is simply no bell, so the player's own Finish
 *            button is the ending, and it ends with endClass({zen:true}) -
 *            'pass', no letter (core/grades.js).
 *
 * PEEK IS THE SHELL'S. `manifest.peek` opts into shell/peek.js, and this game
 * only says what a reveal SHOWS (the solved reference). The A-cap is the
 * shell's, read off peek.used at endClass - a game that implements its own
 * peek has broken the rule (CLAUDE.md trap 9).
 *
 * LAWS THIS FILE KEEPS:
 *   I    the ledger is honest - moves, locks, backtracks, thrash, the clock and
 *        the solved test are computed here from board.js and never routed
 *        through a deck. The trickster may lie on a chip FACE or on the
 *        .g-cp-preview layer; tile truth (--r/--c) never moves for a lie.
 *   II   the only live things on the board are the tiles themselves; every
 *        engine one-shot over it is decoration (fireSafe welds clickSafe on).
 *   III  something always breathes - the subject is a loop, and the backdrop
 *        and the casino keep moving even on a still board.
 *   IV   the class rules are DRAWN (.g-cp-howto, four figures + one GO), the
 *        dismissal is GO only, the sheet is FREE OF THE CLOCK (startClock()
 *        runs in openClass, past GO), and gameMeta.howtoTiers means every
 *        player gets it ONCE per grade tier - ctx.hideTutorial skips that
 *        first showing too.
 *   V    scramble, wash windows, sub_flash cadence and every deck are scoped
 *        off the class seed; a retake replays the identical boards and show.
 *        THE BOARD SEEDS ARE INDEXED: board 1 is `<seed>|cp-scramble|<n>`,
 *        exactly what this class always dealt, and boards 2..k append `|b2`,
 *        `|b3` and so on. So today's opening board is byte-for-byte the one it
 *        was before the class grew, and every re-deal after it is a function
 *        of the seed and the board index - never of the wall clock.
 *   VI   pause/resume/suspend/destroy: the timer registry defers, the decks
 *        ride it, no timer survives destroy, the window listener is removed,
 *        and a suspend force-hides the peek.
 *   VII  every string is ctx.lexicon(key, fallback) over lex.js CP_LEX.
 *
 * WHAT THIS FILE DOES NOT OWN: grades (core/grades.js via ctx.endClass), XP
 * (C#), the tier (registry + meta), effect strengths (the engine's ceiling
 * rule), the LOOK (style.js), the lighting (casino.js), the lies
 * (trickster.js) and the CCP-effects ladder (pressure.js). The pure model is
 * board.js, the baseline solver.js, the rubric + dials + plan grade.js.
 *
 * THE FOUR CREATIVE FILES ARE IMPORTED DYNAMICALLY. They are written by a
 * parallel agent; a file that does not exist yet, or throws on import, costs
 * this class nothing (the shell's own loadOptional posture). A CORE-only
 * geometry stylesheet is injected ONLY when style.js did not load, so the
 * board is playable either way.
 *
 * ENGINE TARGETING NOTE: glitch_swap adds `.ae-glitch{position:relative;
 * animation}` and row_drift writes an inline transform on its targets, so
 * neither may ever touch a .g-cp-tile (style.js owns the tile's transform).
 * Drift targets the static .g-cp-cell floor; shimmer targets tile FACES.
 * ==========================================================================*/

import { CP_LEX } from './lex.js';
import {
  BLANK, cloneState, dealBoard, canSlide, slide, slotForDir, isSolved,
  homeMask, lockedCount, tileCount, manhattan, isSolvable, sizeFromSetting,
  rowOf, colOf, serialize,
} from './board.js';
import { nextMove, baselineLength } from './solver.js';
import { makeTaggedRoll } from '../../core/rng.js';
import {
  PLAYTEST, buildPlan, compositeFor, hardGates, flavorXp, parFor, heatFor, cadenceMs,
  gridForTier, scrambleWalkFor, expectedBoardsFor,
} from './grade.js';

const GAME_KEY = 'composure';

/** A url an <img> cannot show. Mirrors engine/util.js VIDEO_URL_RE - games
 *  never import the engine, so the two-line rule is repeated here. */
const VIDEO_URL_RE = /\.(mp4|webm|m4v)(\?|#|$)/i;

/** Keyboard -> the direction the TILE travels (a swipe reads the same way). */
const KEY_DIRS = Object.freeze({
  ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right',
  KeyW: 'up', KeyS: 'down', KeyA: 'left', KeyD: 'right',
  w: 'up', s: 'down', a: 'left', d: 'right', W: 'up', S: 'down', A: 'left', D: 'right',
});

/** Diagnostics seam (the Deep End / Deja Vu precedent). The shell never reads these. */
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

function mmss(sec) {
  const s = Math.max(0, sec | 0);
  return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');
}

/* --------------------------------------------------------------------------
 * THE CORE-ONLY FALLBACK SHEET. Injected ONLY when style.js could not be
 * loaded (the creative file is written in parallel, and a missing look must
 * never mean an unplayable board). Geometry and hit targets, nothing else -
 * no colour story, no motion, no chrome. Deliberately carries no rule for the
 * `hidden` attribute and no bare display on any node this file toggles with
 * it: styles.css already owns that cascade (CLAUDE.md trap 27).
 * ----------------------------------------------------------------------- */
const FALLBACK_CSS = [
  '.g-cp-stage{position:relative;display:flex;flex-direction:column;align-items:center;gap:10px;padding:10px;color:#f4eef7}',
  '.g-cp-hud{display:flex;gap:8px;flex-wrap:wrap;justify-content:center}',
  '.g-cp-chip{padding:2px 10px;border:1px solid rgba(244,238,247,.35);border-radius:999px;font:600 12px/1.7 system-ui,sans-serif}',
  '.g-cp-frame{position:relative;width:min(70vh,90vw);height:min(70vh,90vw)}',
  '.g-cp-board{position:absolute;inset:0}',
  '.g-cp-cell{position:absolute;left:0;top:0;box-sizing:border-box;width:calc(100% / var(--cp-n));height:calc(100% / var(--cp-n));',
  'transform:translate(calc(var(--c) * 100%),calc(var(--r) * 100%));border:1px solid rgba(244,238,247,.06)}',
  /* the ride is a TRANSFORM, never left/top: an animated left/top re-lays-out
     the whole board every frame (CLAUDE.md trap 36) */
  '.g-cp-tile{position:absolute;left:0;top:0;box-sizing:border-box;width:calc(100% / var(--cp-n));height:calc(100% / var(--cp-n));',
  'transform:translate(calc(var(--c) * 100%),calc(var(--r) * 100%));padding:1px;cursor:pointer;',
  'transition:transform .15s ease}',
  '.g-cp-face{position:relative;width:100%;height:100%;overflow:hidden;border-radius:5px;background:rgba(244,238,247,.06)}',
  '.g-cp-media{position:absolute;object-fit:cover}',
  '.g-cp-num{position:absolute;right:4px;bottom:2px;font:700 11px/1 system-ui,sans-serif;opacity:.55}',
  '.g-cp-tile.is-locked .g-cp-face{outline:1px solid rgba(240,194,75,.55)}',
  '.g-cp-tile.is-hint .g-cp-face{outline:2px solid rgba(240,194,75,.95)}',
  '.g-cp-preview{position:absolute;inset:0}',
  '.g-cp-peeklayer{position:absolute;inset:0;overflow:hidden;border-radius:6px;background:rgba(10,8,16,.92)}',
  '.g-cp-peeklayer .g-cp-media{position:absolute;inset:0;width:100%;height:100%}',
  '.g-cp-msg{min-height:1.3em;text-align:center;margin:0}',
  '.g-cp-howto{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);z-index:9;max-width:min(520px,88vw);',
  'padding:16px 18px;border-radius:12px;background:rgba(16,12,26,.96);border:1px solid rgba(244,238,247,.18)}',
  '.g-cp-hw-row{display:flex;gap:10px;align-items:center;margin:8px 0}',
  '.g-cp-hw-fig{position:relative;flex:0 0 64px;height:44px;border-radius:6px;background:rgba(244,238,247,.08)}',
  '.g-cp-hw-cap{margin:0;font:400 13px/1.4 system-ui,sans-serif}',
  '.g-cp-end{margin:0 auto;max-width:min(520px,90vw);text-align:left}',
  '.g-cp-end-row{display:flex;justify-content:space-between;gap:12px;padding:2px 0}',
].join('');

function injectFallbackStyle() {
  if (typeof document === 'undefined' || !document.createElement) return;
  try {
    if (document.getElementById && document.getElementById('g-cp-core-style')) return;
    const s = document.createElement('style');
    s.id = 'g-cp-core-style';
    s.textContent = FALLBACK_CSS;
    const head = document.head || document.documentElement;
    if (head && head.appendChild) head.appendChild(s);
  } catch (e) { /* a class without a stylesheet still runs */ }
}

export default {
  key: GAME_KEY,
  family: 'puzzle',
  /* MEATY, and it is a TIMETABLE fact first: core/timetable.js ranks
   * no-repeat-3 ABOVE the meaty preference (CLAUDE.md trap 6), so a ten-game
   * pool with two meaty classes filled the meaty slot on only ~46% of dealt
   * nights, and three on 75% (a cycle of four). With four (Lost & Found, The
   * Deep End, Instant Recall and this one) a dealt fortnight carries one on
   * 28/28 nights. NOTHING in this module branches on the flag - it is the
   * registry's and the timetable's to read. Keep it in step with
   * games/registry.js GAME_META, which is the parachute a suspended class is
   * dealt from.
   *
   * As of the class-length wave (2026-08-24) the flag is ALSO honest about the
   * length: 300s, and every tier really fills it. It used to be a 120s class
   * that a year-1 player finished in ~35-40s on the solve, so "meaty" was a
   * label the class did not earn; a solve now BANKS the picture and deals a
   * fresh scramble, and only the bell ends a timed class. */
  meaty: true,
  flagship: false,
  timeBudgetSec: 300,
  orientation: 'portrait',   // phone only; see games/registry.js ORIENTATIONS
  title: 'Composure',

  manifest: {
    /* flash_burst and gif_burst are declared ONLY as clickSafe decoration over
     * a board whose tiles are the click targets (DECISIONS #9) - fireSafe()
     * welds that on at every call site. glitch_swap is the trickster's False
     * Preview and never moves a tile; row_drift rides the static cell floor. */
    effectsConsumed: [
      'glitch_swap', 'wash', 'sub_flash', 'audio_trigger',
      'flash_burst', 'bubble_field', 'gif_burst', 'row_drift',
    ],
    /* ONE loop is the board's subject, cut into n*n viewports; the decoys and
     * the reward bursts want a few more. DOM only, nothing is drawn into a
     * canvas, so the provider may serve remote media here. */
    assetNeeds: { loops: 4, targets: 1, stills: 2, canvasSafe: false },
    /* The timed grid is the YEAR's (1-2 -> 3x3, 3-4 -> 4x4), so there is no
     * below-par board to detect; zen's size is a plain enum setting. */
    boardSizes: null,
    /* No verb of its own: arrows/WASD slide, a tap slides, a swipe slides -
     * and the PEEK key is the shell's shared verb, never a game keybind. */
    keybinds: null,
    settings: [
      {
        key: 'cp_mode', kind: 'enum', values: ['timed', 'zen'], default: 'timed',
        label_key: 'cp_mode', hint_key: 'cp_mode_hint',
      },
      {
        key: 'cp_zen_grid', kind: 'enum', values: ['3x3', '4x4', '5x5'], default: '3x3',
        label_key: 'cp_zen_grid', hint_key: 'cp_zen_grid_hint',
      },
    ],
    /* SYNTHESIS #6: the shared hold-to-reveal verb. The shell owns the A-cap. */
    peek: true,
  },

  create(ctx) {
    const t = (key, fallback) => {
      const fb = fallback == null ? (CP_LEX[key] == null ? key : CP_LEX[key]) : fallback;
      try { const v = ctx.lexicon(key, fb); return v == null ? fb : v; } catch (e) { return fb; }
    };
    const say = (m) => { try { ctx.log('[cp] ' + m); } catch (e) { /* noop */ } };

    /* EMI COMMENTARY SEAMS (the heartbeat wave). note() names a moment the
     * mascot may react to - the shell prefixes 'game:' and its own voice engine
     * decides whether the moment is worth a face, a line or nothing at all.
     * Composure has no timing-critical input window anywhere (no per-move timer
     * and no clock term in the grade), so there is no hold() fence in this
     * file. The seam is additive, one-way and fully guarded: an older shell has
     * no note() at all, and a mascot may never break a class. */
    const note = (id, extra) => {
      try { if (ctx.mood && typeof ctx.mood.note === 'function') ctx.mood.note(id, extra); }
      catch (e) { /* a mascot may never break a class */ }
    };
    /* Seam bookkeeping. `emiStallNotedAt` remembers WHICH stall has already
     * been spoken for by storing the `lastLockAtMs` it belonged to - every lock
     * and every fresh deal moves that value, so the seam re-arms itself and
     * needs no reset of its own. `emiPeekNoted` is once ever per class. */
    let emiStallNotedAt = -1;
    let emiPeekNoted = false;

    /* ---- lifecycle flags ------------------------------------------------ */
    let dead = false;
    let paused = false;
    let ended = false;
    let reported = false;
    let busy = true;                 // input closed until the rules sheet is done
    let opened = false;              // the board is the player's
    /* THE BANK BEAT: a solve is celebrating and the next scramble is dealing.
     * Input is closed, and it is closed SILENTLY (see press()) - the beat is a
     * scene the player earned, not a wall they walked into. */
    let banking = false;
    /* AN ENDING HAS BEGUN (the bell rang, or zen's Finish was pressed). This is
     * the guard that keeps the file's pinned law true across the re-deal loop:
     * a bank timer that lands after the bell must NOT deal a board under the
     * end card, and there must still be exactly one endClass. */
    let closing = false;

    /* ---- class state ---------------------------------------------------- */
    let spec = null;
    let seed = '';
    let tier = 1;
    let n = 3;
    let zen = false;
    let plan = null;
    let state = null;                // the live puzzle (board.js)
    let reduced = false;
    let retake = false;
    let budgetMs = 120000;
    let pool = null;
    let subjectUrl = null;
    let rollLocal = null;

    let casino = null;
    let trickster = null;
    let pressure = null;
    let styleOk = false;

    /* ---- the ledger (Law I: computed here, never by a deck) -------------
     * TWO SCOPES, and mixing them up is the whole risk of the multi-board
     * rework. CLASS-cumulative: moves, bumps, backtracks, thrash, washes,
     * subFlashes, jackpots, rescueEpisodes, rescueUsed, banked, parBanked,
     * bestLockStreak, elapsedMs. PER-BOARD, reset on every deal: state, par,
     * baseline, mhStart, locked, lastMove, lockStreak, boardMoves, solved,
     * rescueActive, hintId, lastLockAtMs. */
    let moves = 0;
    let bumps = 0;
    let backtracks = 0;
    let thrash = 0;
    let lastMove = null;             // {id, from, to} of the previous real slide
    let locked = 0;
    let bestLocked = 0;
    let lockStreak = 0;
    let bestLockStreak = 0;
    /** The LIVE board is whole. Transient now: true from the solve until the
     *  next board deals, which is also the window the bell can catch. */
    let solved = false;
    /* ---- THE BANK (multi-board) ---------------------------------------- */
    let boardIndex = 0;              // 0-based; board 1 is the seed's own deal
    let banked = 0;                  // pictures finished and banked this class
    let boardMoves = 0;              // slides on the LIVE board only
    let parBanked = 0;               // the sum of every banked board's own par
    let bestSolveMoves = 0;          // fewest moves any ONE board took tonight
    let underParSolve = false;       // any one board came in at or under its par
    let expectedBoards = 1;          // the S/A normaliser, off tier + budget
    let washOn = false;
    let washes = 0;
    let subFlashes = 0;
    let jackpots = 0;
    let rescueUsed = false;
    let rescueActive = false;
    let rescueEpisodes = 0;
    let hintId = -1;
    let lastLockAtMs = 0;
    let mhStart = 0;
    let baseline = -1;
    let par = 1;
    let currentHeat = 0;
    let bellOn = false;
    let finished = false;            // zen's own way out was pressed
    let solvesBefore = 0;
    let bestMovesBefore = 0;
    let subIdx = 0;
    let driftOn = false;
    let bubblesOn = false;

    /* ---- clock ---------------------------------------------------------- */
    let clockId = 0;
    let lastTick = 0;
    let elapsedMs = 0;

    /* ---- dom ------------------------------------------------------------ */
    let stage = null; let backdrop = null; let hud = null; let frame = null;
    let boardEl = null; let previewEl = null; let peekEl = null; let peekMedia = null;
    let wellEl = null; let msgEl = null; let endEl = null; let howtoEl = null;
    let movesChip = null; let clockChip = null; let lockedChip = null; let calmChip = null;
    let bankedChip = null;
    let peekBtn = null; let finishBtn = null;
    /** The class's earned par at the ending; renderEnd draws it beside moves. */
    let endParEarned = 0;
    const cellEls = [];
    const tileEls = new Map();       // tile id -> element
    let queued = null;               // THE QUEUE: one slot, last press wins
    let queueTimer = 0;
    let subTimer = 0;
    let bumpTimer = 0;
    let msgTimer = 0;
    let grab = null;                 // the live swipe

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
        o.clickSafe = true;              // the tiles are the click targets
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
    /* THE SEEP'S CLASS-SIDE DOOR (tell 13, the Overseen Frame). We name a DEAD
     * MOMENT and the engine asks the director; it answers null the overwhelming
     * majority of the time and that is the feature. The engine owns the pixels,
     * on its own pointer-events:none fx layer, and the claim releases itself on
     * the tell's last frame - there is nothing here to hold and nothing to undo.
     * A `dead`/`paused` class never asks: a dead moment in a class nobody is
     * playing is not a dead moment, it is an absence. */
    function deadBeatSafe(name) {
      if (dead || paused || !ctx.engine || typeof ctx.engine.deadBeat !== 'function') return null;
      try { return ctx.engine.deadBeat(name) || null; } catch (e) { return null; }
    }
    /** The engine, as a deck sees it: the three welded primitives plus a READ
     *  of the clamped channel vector (THE CEILING RULE - a deck asks, it never
     *  raises). Every member is null-safe and may answer null. */
    const deckEngine = {
      fire: fireSafe,
      sustain: sustainSafe,
      stop: stopSafe,
      channels: () => {
        try { return (ctx.engine && typeof ctx.engine.channels === 'function') ? ctx.engine.channels() : null; }
        catch (e) { return null; }
      },
    };
    /** The player's own media, as a deck sees it: the pool lands ASYNC, so a
     *  deck gets a LIVE reader rather than the pool object. */
    const deckAssets = {
      next(kind) {
        try { return (pool && typeof pool.next === 'function') ? (pool.next(kind) || null) : null; }
        catch (e) { return null; }
      },
      subject() { return subjectUrl; },
    };
    /** bgIntensity 0 is the player's exit: read it LIVE, never a launch snapshot. */
    function capsArmed() { return !(ctx.caps && Number(ctx.caps.bgIntensity) === 0); }
    function motionLevelOf() {
      try { const v = ctx.motion && ctx.motion.motionLevel; return Number.isFinite(Number(v)) ? Number(v) : 2; }
      catch (e) { return 2; }
    }
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
      try { return d[method](...args); }
      catch (e) { say(which + '.' + method + ' threw: ' + ((e && e.message) || e)); return undefined; }
    }

    /* ==================================================================== *
     * HEAT - the locked fraction is the ladder (the class's own progress is
     * its difficulty dial), capped by the year and halved in zen.
     * ==================================================================== */
    function lockedFrac() {
      const tiles = tileCount(n);
      return tiles > 0 ? locked / tiles : 0;
    }
    function heat() {
      const f = lockedFrac();
      const h = heatFor(f, tier, zen);
      currentHeat = h;
      try { if (ctx.engine) ctx.engine.setHeat(h); } catch (e) { /* the engine is optional */ }
      deck('casino', 'setHeat', h);
      deck('pressure', 'setHeat', h);
      deck('pressure', 'setProgress', f);
    }

    /* ==================================================================== *
     * DOM (the contract's exact shape)
     * ==================================================================== */
    function setPhase(p) { if (stage) stage.setAttribute('data-phase', p); }
    function msg(key, fallback, holdMs) {
      if (!msgEl) return;
      msgEl.textContent = t(key, fallback);
      if (msgTimer) { clearTimer(msgTimer); msgTimer = 0; }
      if (holdMs > 0) {
        const mine = msgEl.textContent;
        msgTimer = after(holdMs, () => {
          msgTimer = 0;
          if (msgEl && msgEl.textContent === mine) msgEl.textContent = '';
        });
      }
    }

    function buildDom() {
      const root = ctx.root;
      root.textContent = '';
      stage = el('div', 'g-cp-stage');
      stage.setAttribute('data-phase', 'briefing');
      stage.setAttribute('data-mode', zen ? 'zen' : 'timed');
      stage.setAttribute('data-n', String(n));
      if (reduced) stage.setAttribute('data-reduced', '1');

      backdrop = el('div', 'g-cp-backdrop');
      backdrop.setAttribute('aria-hidden', 'true');
      backdrop.style.pointerEvents = 'none';
      stage.appendChild(backdrop);

      hud = el('div', 'g-cp-hud');
      movesChip = el('span', 'g-cp-chip g-cp-moves', '0');
      movesChip.setAttribute('aria-label', t('cp_chip_moves', CP_LEX.cp_chip_moves));
      clockChip = el('span', 'g-cp-chip g-cp-clock', clockText());
      clockChip.setAttribute('aria-label', zen
        ? t('cp_end_time', CP_LEX.cp_end_time)
        : t('cp_chip_clock', CP_LEX.cp_chip_clock));
      lockedChip = el('span', 'g-cp-chip g-cp-locked', lockedText());
      lockedChip.setAttribute('aria-label', t('cp_chip_locked', CP_LEX.cp_chip_locked));
      calmChip = el('span', 'g-cp-chip g-cp-calm', '');
      calmChip.setAttribute('aria-label', t('cp_chip_calm', CP_LEX.cp_chip_calm));
      calmChip.hidden = true;
      /* THE BANK COUNTER. Hidden until the first picture lands (like the calm
       * chip) so a class that never banks one never grows a zero. It is NOT in
       * the trickster's chipEl/chipText seam and must not be: the bank count is
       * the class's own ledger, and a lie about how many pictures you finished
       * is a lie about the grade, not about the board (Law I). */
      bankedChip = el('span', 'g-cp-chip g-cp-banked', '0');
      bankedChip.setAttribute('aria-label', t('cp_chip_banked', CP_LEX.cp_chip_banked));
      bankedChip.hidden = true;
      hud.appendChild(movesChip);
      hud.appendChild(clockChip);
      hud.appendChild(lockedChip);
      hud.appendChild(bankedChip);
      hud.appendChild(calmChip);

      /* THE PEEK CONTROL. The node is built here and OWNED by the shell: it is
       * handed to ctx.peek.attach() and this file never tracks the A-cap.
       * `arc-peekbtn` is the shell's own chrome class (Lost & Found's foot
       * button wears the same one) so the control reads right before style.js
       * has an opinion about it. */
      peekBtn = el('button', 'g-cp-peek arc-peekbtn', t('peek', 'Peek'));
      peekBtn.setAttribute('type', 'button');
      try { peekBtn.type = 'button'; } catch (e) { /* the DOM double has no button semantics */ }
      hud.appendChild(peekBtn);

      /* ZEN's own way out. An untimed class has no bell, so it needs a real,
       * tab-reachable button (the window keydown handler ignores a BUTTON
       * target, so pressing it is never eaten as a slide). */
      if (zen) {
        finishBtn = el('button', 'g-cp-chip g-cp-finish', t('cp_finish', CP_LEX.cp_finish));
        finishBtn.setAttribute('type', 'button');
        try { finishBtn.type = 'button'; } catch (e) { /* noop */ }
        finishBtn.addEventListener('click', onFinishPressed);
        hud.appendChild(finishBtn);
      }
      if (retake) hud.appendChild(el('span', 'g-cp-chip g-cp-retake', t('cp_retake', CP_LEX.cp_retake)));
      stage.appendChild(hud);

      frame = el('div', 'g-cp-frame');
      boardEl = el('div', 'g-cp-board');
      boardEl.style.setProperty('--cp-n', String(n));
      boardEl.style.touchAction = 'none';       // a swipe is a slide, never a scroll
      boardEl.setAttribute('role', 'application');
      boardEl.setAttribute('aria-label', t('game_composure', 'Composure'));
      cellEls.length = 0;
      for (let i = 0; i < n * n; i++) {
        const cell = el('div', 'g-cp-cell');
        cell.setAttribute('data-i', String(i));
        cell.style.setProperty('--r', String(rowOf(n, i)));
        cell.style.setProperty('--c', String(colOf(n, i)));
        boardEl.appendChild(cell);
        cellEls.push(cell);
      }
      frame.appendChild(boardEl);

      /* The trickster's lie layer. CORE builds it, never draws on it. */
      previewEl = el('div', 'g-cp-preview');
      previewEl.setAttribute('aria-hidden', 'true');
      previewEl.style.pointerEvents = 'none';
      frame.appendChild(previewEl);

      /* THE PEEK REVEAL: the solved reference, one unclipped copy of the
       * subject. Hidden until the shell's verb says otherwise. */
      peekEl = el('div', 'g-cp-peeklayer');
      peekEl.setAttribute('aria-hidden', 'true');
      peekEl.style.pointerEvents = 'none';
      peekEl.hidden = true;
      peekMedia = null;
      peekEl.appendChild(el('span', 'g-cp-peek-cap', t('cp_peek_ref', CP_LEX.cp_peek_ref)));
      frame.appendChild(peekEl);

      stage.appendChild(frame);

      msgEl = el('p', 'g-cp-msg');
      msgEl.setAttribute('aria-live', 'polite');
      stage.appendChild(msgEl);

      wellEl = el('div', 'g-cp-flashwell');
      wellEl.setAttribute('aria-hidden', 'true');
      wellEl.style.pointerEvents = 'none';
      stage.appendChild(wellEl);

      endEl = el('div', 'g-cp-end');
      endEl.hidden = true;
      stage.appendChild(endEl);

      root.appendChild(stage);
      paintTiles(true);
    }

    /** One element per tile, positioned ONLY through --r / --c (style.js owns
     *  the transform). --hr / --hc carry the tile's HOME, which is also which
     *  fragment of the picture it wears. */
    function tileElFor(id) {
      let node = tileEls.get(id);
      if (node) return node;
      node = el('div', 'g-cp-tile');
      node.setAttribute('data-id', String(id));
      node.setAttribute('data-home', String(id));
      node.style.setProperty('--hr', String(rowOf(n, id)));
      node.style.setProperty('--hc', String(colOf(n, id)));
      const face = el('span', 'g-cp-face');
      face.appendChild(el('span', 'g-cp-num', String(id + 1)));
      node.appendChild(face);
      node.addEventListener('click', () => onTileClick(id));
      tileEls.set(id, node);
      boardEl.appendChild(node);
      return node;
    }

    /** Write every tile's --r/--c from the model. `fresh` also builds them. */
    function paintTiles(fresh) {
      if (!state || !boardEl) return;
      for (let pos = 0; pos < state.cells.length; pos++) {
        const id = state.cells[pos];
        if (id === BLANK) continue;
        const node = fresh ? tileElFor(id) : (tileEls.get(id) || tileElFor(id));
        node.style.setProperty('--r', String(rowOf(n, pos)));
        node.style.setProperty('--c', String(colOf(n, pos)));
      }
    }

    /** The clipped viewport: ONE url, n*n offsets, one decoder. */
    function dressTiles() {
      if (!subjectUrl || !state) return;
      for (const [id, node] of tileEls) {
        const face = firstChildWith(node, 'g-cp-face');
        if (!face) continue;
        let media = firstChildWith(face, 'g-cp-media');
        if (!media) {
          media = el('img', 'g-cp-media');
          try {
            media.setAttribute('alt', '');
            media.setAttribute('draggable', 'false');
            media.draggable = false;
            media.decoding = 'async';
            media.addEventListener('load', () => { if (!dead) node.classList.add('is-loaded'); });
            media.addEventListener('error', () => { if (!dead) mediaBroken(); });
          } catch (e) { /* the double has no img semantics; fine */ }
          face.insertBefore ? face.insertBefore(media, face.children[0] || null) : face.appendChild(media);
        }
        /* THE CLIP. The media is n times the tile in both axes and offset by
         * the tile's HOME, so every tile is a window onto the same running
         * loop at a different place - which is what makes the board move. */
        try {
          media.style.width = (n * 100) + '%';
          media.style.height = (n * 100) + '%';
          media.style.left = (-100 * colOf(n, id)) + '%';
          media.style.top = (-100 * rowOf(n, id)) + '%';
          media.src = subjectUrl;
        } catch (e) { /* ignore */ }
      }
      if (peekEl && !peekMedia) {
        peekMedia = el('img', 'g-cp-media');
        try {
          peekMedia.setAttribute('alt', '');
          peekMedia.decoding = 'async';
          peekMedia.src = subjectUrl;
        } catch (e) { /* ignore */ }
        peekEl.appendChild(peekMedia);
      }
    }

    /** The subject url failed: the board goes to numbered faces for the class. */
    function mediaBroken() {
      if (!subjectUrl) return;
      say('subject media failed - the board wears numbered faces for this class');
      subjectUrl = null;
      for (const [, node] of tileEls) {
        node.classList.remove('is-loaded');
        const face = firstChildWith(node, 'g-cp-face');
        const media = face && firstChildWith(face, 'g-cp-media');
        if (media && typeof media.remove === 'function') { try { media.remove(); } catch (e) { /* noop */ } }
      }
    }

    function firstChildWith(node, cls) {
      try { for (const k of node.children || []) if (k.classList && k.classList.contains(cls)) return k; }
      catch (e) { /* ignore */ }
      return null;
    }

    /* ---- HUD paint (truth) ---------------------------------------------- */
    function secLeft() { return zen ? 0 : Math.max(0, Math.ceil((budgetMs - elapsedMs) / 1000)); }
    function secElapsed() { return Math.max(0, Math.floor(elapsedMs / 1000)); }
    /** THE TRUTH on the clock chip; the trickster restores exactly this string. */
    function clockText() { return zen ? mmss(secElapsed()) : mmss(secLeft()); }
    function movesText() { return String(moves); }
    function lockedText() { return locked + '/' + tileCount(n); }
    function chipText(which) {
      if (which === 'clock') return clockText();
      if (which === 'moves') return movesText();
      return lockedText();
    }
    function paintHud() {
      if (clockChip) clockChip.textContent = clockText();
      if (movesChip) movesChip.textContent = movesText();
      if (lockedChip) lockedChip.textContent = lockedText();
      if (bankedChip) {
        bankedChip.hidden = banked <= 0;
        bankedChip.textContent = String(banked);
      }
      paintCalm();
    }
    function paintCalm() {
      if (!calmChip) return;
      if (lockStreak < 2) { calmChip.hidden = true; calmChip.textContent = 'x ' + lockStreak; return; }
      calmChip.hidden = false;
      calmChip.textContent = 'x ' + lockStreak;
      /* The meter is the SHELL's primitive (10 segments); it rides inside the
       * chip so the contract's DOM gains no extra node. */
      try {
        const meter = ctx.ceremonies.streakMeter({
          filled: Math.min(PLAYTEST.LOCK_STREAK_CAP, lockStreak),
          gold: lockStreak >= PLAYTEST.LOCK_STREAK_CAP,
        });
        if (meter) calmChip.appendChild(meter);
      } catch (e) { /* a ceremony must never be the thing that fails */ }
    }

    /* ==================================================================== *
     * INPUT - a tap on a tile beside the gap, window keydown (arrows / WASD,
     * form targets ignored), and a swipe on the board. THE QUEUE: one slot,
     * last press wins, drained on the next tick after the move lock releases.
     * ==================================================================== */
    function onTileClick(id) {
      if (!state) return;
      const pos = state.cells.indexOf(id);
      press('pos', pos);
    }
    function onKeyDown(e) {
      if (!e || e.repeat || e.ctrlKey || e.altKey || e.metaKey) return;
      if (isFormTarget(e.target)) return;
      const dir = KEY_DIRS[String(e.key || '')] || KEY_DIRS[String(e.code || '')];
      if (!dir) return;
      try { e.preventDefault(); } catch (err) { /* noop */ }
      press('dir', dir);
    }
    function onPointerDown(e) {
      if (!e || !boardEl || dead || paused || ended) return;
      grab = { id: e.pointerId == null ? null : e.pointerId, x: Number(e.clientX) || 0, y: Number(e.clientY) || 0, captured: false };
      try {
        if (typeof boardEl.setPointerCapture === 'function' && grab.id != null) {
          boardEl.setPointerCapture(grab.id);
          grab.captured = true;
        }
      } catch (err) { grab.captured = false; }
      /* THE HAND: a press marks the board so style.js can lift the tiles. It is
       * a look, not a verb - the slide still happens on release (or on the
       * tile's own click), so nothing about input moved. */
      try { boardEl.classList.add('g-cp-held'); } catch (err) { /* noop */ }
    }
    function samePointer(e) {
      if (!grab) return false;
      if (e && e.pointerId != null && grab.id != null) return e.pointerId === grab.id;
      return true;
    }
    function onPointerUp(e) {
      if (!e || !grab || !samePointer(e)) return;
      const dx = (Number(e.clientX) || 0) - grab.x;
      const dy = (Number(e.clientY) || 0) - grab.y;
      clearGrab();
      const ax = Math.abs(dx); const ay = Math.abs(dy);
      if (Math.max(ax, ay) < PLAYTEST.SWIPE_PX) return;      // a tap: the tile's own click handles it
      press('dir', ax >= ay ? (dx > 0 ? 'right' : 'left') : (dy > 0 ? 'down' : 'up'));
    }
    function onPointerCancel(e) { if (!grab || samePointer(e)) clearGrab(); }
    function onPointerLeave(e) { if (grab && !grab.captured) onPointerCancel(e); }
    function clearGrab() {
      const g = grab;
      grab = null;
      try { if (boardEl) boardEl.classList.remove('g-cp-held'); } catch (e) { /* noop */ }
      if (g && g.captured && g.id != null && boardEl) {
        try { if (typeof boardEl.releasePointerCapture === 'function') boardEl.releasePointerCapture(g.id); }
        catch (e) { /* the pointer is already gone */ }
      }
    }
    function bindInput() {
      try { if (typeof window !== 'undefined') window.addEventListener('keydown', onKeyDown); }
      catch (e) { say('keydown bind failed: ' + ((e && e.message) || e)); }
      try {
        boardEl.addEventListener('pointerdown', onPointerDown);
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
          boardEl.removeEventListener('pointerup', onPointerUp);
          boardEl.removeEventListener('pointercancel', onPointerCancel);
          boardEl.removeEventListener('lostpointercapture', onPointerCancel);
          boardEl.removeEventListener('pointerleave', onPointerLeave);
        }
      } catch (e) { /* noop */ }
    }

    function clearQueue() {
      queued = null;
      if (queueTimer) { clearTimer(queueTimer); queueTimer = 0; }
    }
    /** The lock is off: hand the slot back to the pipeline, one tick later. */
    function release() {
      busy = false;
      if (!queued || dead || paused || ended || !state) { clearQueue(); return; }
      if (queueTimer) clearTimer(queueTimer);
      queueTimer = after(PLAYTEST.QUEUE_DRAIN_MS, () => {
        queueTimer = 0;
        const q = queued;                 // read late: a pause in between drops it
        queued = null;
        if (!q || dead || paused || ended || busy || !state) return;
        const pos = q.kind === 'dir' ? slotForDir(state, q.v) : q.v;
        /* A queued POSITION can be stale (the board moved under it). A stale
         * one is dropped in silence - it is not a wall the player hit. */
        if (pos < 0 || !canSlide(state, pos)) return;
        applyMove(pos);
      });
    }

    /** Every press funnels here. Returns true when a real slide happened. */
    function press(kind, v) {
      if (dead) return false;
      /* A press in a phase that cannot take one - the rules sheet, a pause, a
       * suspend, the end card - is REFUSED, and a refusal is never silent (W2). */
      if (paused || ended || closing || !state || !opened) { bumpCue(); return false; }
      /* THE BANK BEAT IS A SCENE, NOT A WALL. The picture the player just
       * finished is playing itself whole and the next scramble is dealing; a
       * press in that window is not a refusal, so it neither thuds nor takes
       * the queue slot (a queued press would land on the FRESH board, which is
       * the one thing the deal must never hand them). */
      if (banking) return false;
      if (busy) {
        if (PLAYTEST.QUEUE_SLOTS > 0) {
          queued = { kind, v };
          /* W3 P0-10: a press taken into the lock slot is ACCEPTED, not
           * refused, and it gets its own answer - held, it is coming. The
           * lock itself is the rate limit. */
          tick('queue', 0.12);
          return false;
        }
        bumpCue();                       // no queue slot to take it: simply refused
        return false;
      }
      const pos = kind === 'dir' ? slotForDir(state, v) : v;
      if (pos < 0 || !canSlide(state, pos)) { bump(); return false; }
      return applyMove(pos);
    }

    /** THE REFUSED PRESS (the school's chrome vocabulary, W2). The board's
     *  shove is the VISUAL and it plays every time; the CUE is throttled, so a
     *  held arrow key or a mashed tile cannot machine-gun one bump a frame. */
    const REFUSE_GAP_MS = 250;
    let lastBumpAt = 0;
    function bumpCue() {
      const now = Date.now();
      if (now - lastBumpAt < REFUSE_GAP_MS) return;
      lastBumpAt = now;
      tick('bump', 0.15);   /* owner 2026-08-24: error cues -50% */
    }

    /** A press that slides nothing: the board bumps, a muted thud, never silence. */
    function bump() {
      bumps += 1;
      if (boardEl && boardEl.classList) {
        boardEl.classList.remove('is-bump');
        try { if (typeof boardEl.offsetWidth === 'number') void boardEl.offsetWidth; } catch (e) { /* ignore */ }
        boardEl.classList.add('is-bump');
        if (bumpTimer) clearTimer(bumpTimer);
        bumpTimer = after(240, () => { bumpTimer = 0; if (boardEl) boardEl.classList.remove('is-bump'); });
      }
      bumpCue();
      /* Milestone-gated off the class's own bump counter: a mashing player
       * cannot machine-gun this the way they can the thud. */
      if (bumps === 3 || (bumps > 3 && bumps % 10 === 0)) note('cp.bump', { kind: 'tease', n: bumps });
    }

    /* ==================================================================== *
     * THE MOVE PIPELINE
     * ==================================================================== */
    function applyMove(pos) {
      const res = slide(state, pos);
      if (!res.moved) { bump(); return false; }
      busy = true;
      moves += 1;                    // the CLASS's slide count (pace, calm)
      boardMoves += 1;               // ...and this board's, for the standing best

      /* 1. the tile rides to its new cell (vars only - style.js owns the ride) */
      const node = tileEls.get(res.id);
      if (node) {
        node.style.setProperty('--r', String(rowOf(n, res.to)));
        node.style.setProperty('--c', String(colOf(n, res.to)));
        node.classList.add('is-moving');
        after(moveMs() + 40, () => { if (node) node.classList.remove('is-moving'); });
      }

      /* 2. BACKTRACK / THRASH - the composure ledger. A slide that puts the
       *    same tile back where the slide before it took it from is a
       *    backtrack; one made while a wash is burying the board is THRASH,
       *    and thrash is what the whole class is about. */
      const isBack = !!lastMove && lastMove.id === res.id && lastMove.from === res.to && lastMove.to === res.from;
      if (isBack) {
        backtracks += 1;
        lockStreak = 0;
        /* W3 P2-1: an undo is a move with a direction of its own, so it is
         * the ordinary slide dropped a whole step. */
        if (!washOn) tick('slide', 0.16, { pitch: 0.88 });
        if (washOn) {
          thrash += 1;
          deck('casino', 'thrash');
          deck('pressure', 'beat', 'thrash');
          msg('cp_backtrack_line', CP_LEX.cp_backtrack_line, 1600);
          note('cp.thrash', { kind: 'commiserate', n: thrash, streak: backtracks });
        }
      }
      lastMove = { id: res.id, from: res.from, to: res.to };

      /* 3. locks (cosmetic truth: a tile home wears .is-locked, and is still
       *    perfectly free to slide again - Law I) */
      applyLockClasses(homeMask(state));
      const prevLocked = locked;
      locked = lockedCount(state);
      bestLocked = Math.max(bestLocked, locked);
      if (locked > prevLocked) {
        lockStreak += 1;
        bestLockStreak = Math.max(bestLockStreak, lockStreak);
        lastLockAtMs = elapsedMs;
        /* W3 P1-4: the assist letting go is the player taking the board back.
         * The false edge only - a lock with no rescue up says nothing. */
        if (rescueActive) tick('lift', 0.22, { pitch: 1.1 });
        clearHint();
        rescueActive = false;
        deck('casino', 'lock', locked, tileCount(n));
        deck('pressure', 'beat', 'lock');
        // one chime family, a semitone UP per link: you hear the picture settling
        tick('streak', 0.28 + 0.03 * Math.min(8, lockStreak),
          { pitch: Math.pow(2, Math.min(7, lockStreak - 1) / 12) });
        if (locked >= tileCount(n) - 1) msg('cp_lock_line', CP_LEX.cp_lock_line, 1400);
        /* Milestone-gated: the streak climbs one link at a time, so testing for
         * the exact link fires this once per crossing and never per move. */
        if (lockStreak === 3 || lockStreak === 5 || lockStreak === PLAYTEST.LOCK_STREAK_CAP) {
          note('cp.lockStreak', {
            kind: 'celebrate', streak: lockStreak, n: locked, left: tileCount(n) - locked,
          });
        }
      }

      /* 4. the decks see the truth AFTER the ledger moved */
      deck('casino', 'slide', { id: res.id, locked: locked > prevLocked, moves });
      deck('trickster', 'afterSlide');
      /* W3 P1-1: the slide cue is casino.js's alone (it carries heat and the
       * progress pitch). Two of them on one move was the most frequent sound
       * in the class, doubled into mush. */

      paintHud();
      heat();
      rewardBeatMaybe(locked > prevLocked);

      /* 5. the board's verdict */
      if (isSolved(state)) { onSolved(); return true; }
      if (rescueActive) showHint();
      after(moveMs(), release);
      return true;
    }

    function moveMs() { return reduced ? PLAYTEST.MOVE_MS_REDUCED : PLAYTEST.MOVE_MS; }

    function applyLockClasses(mask) {
      for (let i = 0; i < mask.length; i++) {
        const id = state.cells[i];
        if (id === BLANK) continue;
        const node = tileEls.get(id);
        if (!node) continue;
        if (mask[i]) node.classList.add('is-locked'); else node.classList.remove('is-locked');
      }
    }

    /** The variable-ratio canon: the engine's roll, else a seeded local one. */
    function rewardBeatMaybe(gotLock) {
      if (!gotLock || solved || ended) return;
      let r = null;
      try {
        if (ctx.engine && typeof ctx.engine.rewardRoll === 'function') {
          r = ctx.engine.rewardRoll({ streak: lockStreak, success: true }) || null;
        }
      } catch (e) { r = null; }
      if (!r) r = rollLocal();
      if (r.jackpot) {
        jackpots += 1;
        try { ctx.ceremonies.reward('jackpot', { target: boardEl, text: t('cp_jackpot', CP_LEX.cp_jackpot) }); }
        catch (e) { /* noop */ }
        fireSafe('flash_burst', { count: 2, alpha: 0.4 });
        tick('jackpot', 0.65);
        return;
      }
      if (r.nearMiss) {
        try { ctx.ceremonies.reward('near_miss', { target: boardEl, text: t('cp_near_miss', CP_LEX.cp_near_miss) }); }
        catch (e) { /* noop */ }
        tick('near_miss', 0.3);
      }
    }

    /* ==================================================================== *
     * THE SKILL-FLOOR RESCUE (the critic's top fix; LAW)
     * 20s with no new piece home lights the baseline solver's next move and
     * fails the declared sGate. The class never ends because of it, and the
     * board is never played for the player: one cell, lit.
     *
     * THE STATE IS PER BOARD (multi-board, 2026-08-24). `lastLockAtMs`,
     * `rescueActive` and the lit hint all reset on every deal - twenty seconds
     * into a brand new scramble is not being stuck, and a hint left over from
     * the board before points at a tile that has moved. What does NOT reset is
     * `rescueUsed` and the episode count: the sGate was a promise about the
     * CLASS, and the tax is about the class too.
     * ==================================================================== */
    function rescueEnabled() { return zen ? PLAYTEST.RESCUE_IN_ZEN : true; }
    function checkRescue() {
      if (!rescueEnabled() || solved || banking || closing || ended || !opened || rescueActive || !state) return;
      if ((elapsedMs - lastLockAtMs) < PLAYTEST.RESCUE_MS) return;
      rescueActive = true;
      rescueEpisodes += 1;
      if (!rescueUsed) {
        rescueUsed = true;
        say('skill-floor rescue armed - the class is capped at A from here');
        try { ctx.ceremonies.stamp({ text: t('cp_stamp_assist', CP_LEX.cp_stamp_assist), tone: 'pink', target: frame }); }
        catch (e) { /* noop */ }
      }
      deck('casino', 'assist');
      msg('cp_rescue_line', CP_LEX.cp_rescue_line, 3200);
      tick('whisper', 0.28);
      /* The stamp already said "capped at A" - she is here for the tile, not
       * the rubric. Once per episode, off the clock tick. */
      note('cp.rescueArmed', {
        kind: 'commiserate', n: rescueEpisodes, left: tileCount(n) - locked,
      });
      showHint();
    }
    function showHint() {
      clearHint();
      if (!state || solved || ended) return;
      let mv = null;
      try { mv = nextMove(state); } catch (e) { mv = null; }
      if (!mv) { say('rescue: the baseline solver had no answer - no hint this time'); return; }
      const node = tileEls.get(mv.id);
      if (!node) return;
      hintId = mv.id;
      node.classList.add('is-hint');
    }
    function clearHint() {
      if (hintId >= 0) {
        const node = tileEls.get(hintId);
        if (node) node.classList.remove('is-hint');
      }
      hintId = -1;
    }

    /* ==================================================================== *
     * THE WASHES (the class's own effect) + the other dials
     * ==================================================================== */
    function armWashes() {
      if (!plan || !plan.washes.length) return;
      for (const w of plan.washes) after(w.atMs, () => runWash(w));
    }
    function runWash(w) {
      if (dead || ended || closing) return;
      /* A wash that comes due DURING a bank beat waits for it rather than
       * being dropped. The room does not bury the reward it just handed you,
       * and the schedule is the CLASS's (one spread across the whole bell),
       * so losing a window would quietly thin the burial every time somebody
       * solved. The poll is a pause-aware timer like everything else here. */
      if (banking || solved) { after(600, () => runWash(w)); return; }
      washes += 1;
      washOn = true;
      if (stage) stage.setAttribute('data-wash', '1');
      deck('trickster', 'washOn', true);
      deck('pressure', 'beat', 'wash');
      const alpha = w.alpha * (reduced ? 0.6 : 1);
      sustainSafe('wash', { variant: reduced ? 'pink' : w.variant, alpha, holdMs: w.ms });
      tick('wash', 0.3);
      msg('cp_wash_line', CP_LEX.cp_wash_line, 2200);
      /* Once per wash window, never per frame of it. */
      note('cp.washOn', { kind: 'tension', n: washes, left: tileCount(n) - locked });
      /* THE WHISPER-OUT. NEVER stop('wash') mid-class (CLAUDE.md trap 33):
       * a LOWER-alpha re-trigger is the step-down and ends the hold cleanly. */
      const stepAt = Math.max(200, Math.round(w.ms * (1 - plan.washStepdownShare)));
      after(stepAt, () => {
        if (dead || ended) return;
        sustainSafe('wash', {
          variant: reduced ? 'pink' : w.variant,
          alpha: alpha * plan.washStepdown,
          holdMs: Math.max(300, w.ms - stepAt),
        });
      });
      after(w.ms + 250, () => {
        washOn = false;
        if (stage) stage.removeAttribute('data-wash');
        deck('trickster', 'washOn', false);
        /* W3 P0-11: the room letting go of the board. The same wash pitched
         * down is the honest inverse of its arrival, and the beat pressure.js
         * has always answered to but nobody dispatched clears the haze flare
         * it would otherwise leave lingering. */
        tick('wash', 0.18, { pitch: 0.8 });
        deck('pressure', 'beat', 'unwash');
      });
    }

    /** sub_flash on the plan's cadence (tier 3+), heat-shortened, seed-jittered. */
    function armSubFlash() {
      if (!plan || plan.subFlashMs <= 0 || ended) return;
      const ms = cadenceMs(plan.subFlashMs, currentHeat, plan.subJitter[subIdx % plan.subJitter.length]);
      subTimer = after(ms, () => {
        subTimer = 0;
        const r = fireSafe('sub_flash', {
          anchor: wellEl,
          variant: plan.subVariants[subIdx % plan.subVariants.length],
          /* VOICE: cadence floor is 5200 * CADENCE_MIN_MULT * (1 - CADENCE_JITTER)
             = ~1690ms, clear of the 1400ms voiced-gap floor. */
          voice: true,
          voiceKey: 'composure-whisper',
        });
        if (r) subFlashes += 1;
        subIdx += 1;
        armSubFlash();
      });
    }

    function openAmbience() {
      heat();
      if (plan.bubbles && !reduced) {
        bubblesOn = !!sustainSafe('bubble_field', { clickSafe: true, variant: 'drift', max: 8, alpha: 0.3 });
      }
      if (plan.rowDrift && !reduced && cellEls.length) {
        const targets = cellEls.filter((c) => (Number(c.getAttribute('data-i')) % n) === 0);
        driftOn = !!sustainSafe('row_drift', { targets, axis: 'x', variant: 'sway', amplitudeMult: 0.4, stagger: false });
      }
      armSubFlash();
      armWashes();
    }
    function stopAmbience() {
      if (subTimer) { clearTimer(subTimer); subTimer = 0; }
      if (driftOn) { stopSafe('row_drift'); driftOn = false; }
      if (bubblesOn) { stopSafe('bubble_field'); bubblesOn = false; }
    }

    /* ==================================================================== *
     * THE BANK LOOP + THE ENDINGS.
     *
     * THE PINNED LAW IS UNCHANGED AND IT IS THE POINT: exactly one endClass,
     * ever. What changed is that a SOLVE is no longer one of the ways out. A
     * solve banks the picture and deals a fresh scramble; only the bell (timed)
     * or zen's own Finish button closes the class, and both of those set
     * `closing` FIRST, which is what a pending bank timer tests before it deals
     * anything. So the bell landing in the middle of a celebration is safe in
     * both orders: the deal refuses, and finish() still guards on `ended`.
     * ==================================================================== */

    /** Board 1 is `<seed>|cp-scramble|<n>[|zen]` - byte-for-byte the seed this
     *  class dealt before it grew. Every board after it appends `|b2`, `|b3`. */
    function scrambleSeedFor(index) {
      const base = seed + '|cp-scramble|' + n + (zen ? '|zen' : '');
      return index <= 0 ? base : base + '|b' + (index + 1);
    }

    /**
     * Deal the board at `boardIndex` onto the live model and reset the
     * PER-BOARD half of the ledger. Same tier dials every board; the only
     * thing that moves is the scramble depth at tiers 1-2 (grade.js
     * SCRAMBLE_WALK_STEP), which is a pure function of the index.
     *
     * The tile ELEMENTS are never rebuilt: a re-deal is the same n*n-1 ids in
     * new cells, so paintTiles() re-points them and the <img> viewports (which
     * are keyed to a tile's HOME, not its cell) never even reload.
     */
    function dealCurrentBoard() {
      const scrambleSeed = scrambleSeedFor(boardIndex);
      const walk = scrambleWalkFor(tier, zen, boardIndex);
      state = dealBoard(n, scrambleSeed, { walk });
      if (!isSolvable(state.cells, n)) {
        say('FATAL-ish: the dealt board failed the parity test - re-dealing solved-adjacent');
        state = dealBoard(n, scrambleSeed + '|repair', { walk: Math.max(20, n * n * 2) });
      }
      mhStart = manhattan(state);
      locked = lockedCount(state);
      bestLocked = Math.max(bestLocked, locked);

      /* THE BASELINE, per board. par is derived from a real solve of THIS
       * scramble, not a table someone guessed (dossier open question 3). A
       * solver that cannot answer leaves par on the per-tile fallback and the
       * rescue without a hint - never a throw inside a live class. */
      baseline = -1;
      try { baseline = baselineLength(cloneState(state)); } catch (e) { baseline = -1; }
      par = parFor(baseline, tier, n);

      /* the per-board half of the ledger */
      boardMoves = 0;
      lastMove = null;
      lockStreak = 0;
      solved = false;
      rescueActive = false;
      lastLockAtMs = elapsedMs;
    }

    /** A picture came back whole: BANK it, celebrate, then deal the next one. */
    function onSolved() {
      if (solved || ended || closing) return;
      solved = true;
      banking = true;
      busy = true;
      /* THE BANK ITSELF, and it happens HERE rather than at the deal: the
       * counter must tick the instant the picture lands, and if the bell rings
       * during the celebration this solve is already paid for. Which is also
       * why grade.js scores a `boardSolved` live board at ZERO - its whole is
       * in `banked` and counting both would pay for it twice. */
      banked += 1;
      parBanked += par;
      if (!(bestSolveMoves > 0) || boardMoves < bestSolveMoves) bestSolveMoves = boardMoves;
      if (boardMoves <= par) underParSolve = true;
      clearQueue();
      clearGrab();
      clearHint();
      rescueActive = false;
      setPhase('solved');
      if (boardEl) boardEl.classList.add('is-solved');
      if (stage) stage.setAttribute('data-solved', '1');
      paintHud();
      deck('casino', 'solved');
      deck('pressure', 'beat', 'solved');
      try { ctx.ceremonies.reward('jackpot', { intensity: 1, target: boardEl, text: t('cp_stamp_solved', CP_LEX.cp_stamp_solved) }); }
      catch (e) { /* noop */ }
      try { ctx.ceremonies.stamp({ text: t('cp_stamp_solved', CP_LEX.cp_stamp_solved), target: frame }); }
      catch (e) { /* noop */ }
      msg(zen ? 'cp_zen_done' : 'cp_solved_line', zen ? CP_LEX.cp_zen_done : CP_LEX.cp_solved_line);
      tick('jackpot', 0.8);
      fireSafe('flash_burst', { count: 3, alpha: 0.45 });
      say('BANKED board ' + (boardIndex + 1) + ' in ' + boardMoves + ' moves (par ' + par
        + ') - ' + banked + ' this class');
      /* THE SAFEST WORDS IN THE CLASS. The picture is whole, presses are inert
       * rather than refused, and the file already calls this a dead beat. */
      note('cp.banked', {
        kind: 'celebrate', n: banked, left: Math.max(0, expectedBoards - banked),
      });
      if (boardMoves <= par) {
        note('cp.underPar', {
          kind: 'celebrate', n: boardMoves, left: Math.max(0, par - boardMoves),
        });
      }
      /* The clock is NOT stopped: the bell owns the class now. */
      const playMs = reduced ? PLAYTEST.SOLVE_PLAY_MS_REDUCED : PLAYTEST.SOLVE_PLAY_MS;
      after(playMs, dealNextBoard);
    }

    /** The celebration is over: scatter the picture into a fresh scramble. */
    function dealNextBoard() {
      /* The bell (or zen's Finish) won the race. Nothing deals under an end
       * card - this single test is what keeps "exactly one endClass" true. */
      if (dead || ended || closing) return;
      boardIndex += 1;
      clearQueue();
      clearGrab();
      clearHint();
      if (boardEl) boardEl.classList.remove('is-solved');
      if (stage) stage.removeAttribute('data-solved');
      setPhase('dealing');
      if (boardEl) boardEl.classList.add('is-dealing');

      dealCurrentBoard();
      paintTiles(false);
      applyLockClasses(homeMask(state));
      paintHud();
      heat();
      /* The decks' own royal state has to come down with the board. Both
       * `deal` hooks are null-safe through deck(), and pressure's is not
       * optional polish: its setProgress() early-returns while royalOn, so
       * without it the CCP-effects ladder would freeze after the first bank. */
      deck('casino', 'deal', { bell: bellOn });
      deck('pressure', 'deal');
      deck('trickster', 'deal');
      msg('cp_bank_line', CP_LEX.cp_bank_line, 2400);
      tick('lift', 0.42);
      /* THE BREATH BETWEEN BOARDS, and the widest one in the collection. The
       * bank beat is a SCENE, not a wall: while `banking` is set a press is not
       * refused, it is INERT - no cue, no thud, no queue slot - and the deal
       * holds it for another 700ms (300 reduced) on top of the 1800 the
       * celebration already spent. Nothing can be costed here. */
      deadBeatSafe('round_gap');
      /* Once per deal, never per tile of it - the widest breath in the class. */
      note('cp.dealNext', {
        kind: 'curiosity', n: boardIndex + 1, left: Math.max(0, expectedBoards - banked),
      });
      say('dealing board ' + (boardIndex + 1) + ' (walk ' + scrambleWalkFor(tier, zen, boardIndex)
        + ', mh ' + mhStart + ', baseline ' + baseline + ', par ' + par + ')');

      after(reduced ? PLAYTEST.DEAL_MS_REDUCED : PLAYTEST.DEAL_MS, () => {
        if (dead || ended || closing) return;
        if (boardEl) boardEl.classList.remove('is-dealing');
        setPhase('play');
        banking = false;
        busy = false;
        clearQueue();               // nothing pressed at the celebration rides in
        lastLockAtMs = elapsedMs;   // the stall clock starts with the new board
      });
    }

    /**
     * THE ONE ENDING for a timed class. It fires whatever the board is doing -
     * mid-slide, mid-celebration, mid-deal - and `closing` is set BEFORE
     * anything else so the pending bank timer finds it.
     */
    function bell() {
      if (dead || ended || closing) return;
      closing = true;
      banking = false;
      busy = true;
      clearQueue();
      clearGrab();
      clearHint();
      bellOn = true;
      if (boardEl) boardEl.classList.remove('is-dealing');
      setPhase('ended');
      deck('casino', 'bell', true);
      deck('casino', 'dimOut');
      deck('pressure', 'dimOut');
      deck('pressure', 'beat', 'bell');
      try { ctx.ceremonies.stamp({ text: t('cp_stamp_bell', CP_LEX.cp_stamp_bell), tone: 'pink', target: frame }); }
      catch (e) { /* noop */ }
      msg('cp_bell_line', CP_LEX.cp_bell_line);
      /* W3 P0-3: the bell IS the end of the class, and the stamp lands after
       * it - the school speaks first and the paperwork follows. */
      tick('bell', 0.5);
      after(420, () => tick('stamp', 0.55));
      /* The bell caught a picture halfway home. Input is dead and the ceremony
       * owns the next 2400ms, so this is a safe place for actual words. */
      if (!solved && state && locked < tileCount(n)) {
        note('cp.bellMidBoard', {
          kind: 'commiserate', n: locked, left: tileCount(n) - locked, streak: banked,
        });
      }
      after(reduced ? PLAYTEST.CEREMONY_MS_REDUCED : PLAYTEST.CEREMONY_MS, () => finish('bell'));
    }

    /** ZEN's own way out, and zen's ONLY way out - a zen solve banks and
     *  re-deals exactly like a timed one, so the table keeps dealing until the
     *  player says stop. Exactly once: the button disables itself, and
     *  `closing` stops any bank beat in flight from dealing board k+1. */
    function onFinishPressed() {
      if (dead || ended || finished || closing || !zen) return;
      finished = true;
      closing = true;
      banking = false;
      try { if (finishBtn) finishBtn.disabled = true; } catch (e) { /* noop */ }
      /* W3 P1-4: an ending the player CHOSE is a committed press, not a
       * silent exit. */
      tick('commit', 0.4);
      if (boardEl) boardEl.classList.remove('is-dealing');
      stopClock();
      /* Zen's only ending, and the only one with no letter attached: zen
       * reports 'pass', so there is no band to hand her here. */
      note('cp.zenFinish', {
        kind: banked > 0 ? 'celebrate' : 'commiserate', n: banked, streak: boardIndex + 1,
      });
      finish('left');
    }

    function finish(reason) {
      if (ended) return;
      ended = true;
      closing = true;
      banking = false;
      busy = true;
      stopClock();
      stopAmbience();
      clearQueue();
      clearGrab();
      clearHint();
      deck('trickster', 'stop');
      deck('casino', 'stop');
      deck('pressure', 'stop');
      paintHud();                                  // truth on every chip, whatever the trickster left

      const tiles = tileCount(n);
      const mhNow = manhattan(state);
      /* THE WHOLE CLASS, not the last board: banked solves + whatever the live
       * board was worth when the bell rang, normalised by what this year is
       * expected to fill the bell with. `boardSolved` is the bell-caught-the-
       * celebration case and tells grade.js to score the live board at zero
       * (its whole is already inside `banked`). */
      const graded = compositeFor({
        gradeTier: tier, banked, expectedBoards, boardSolved: solved,
        n, moves, par, parBanked,
        manhattanStart: mhStart, manhattanNow: mhNow,
        locked, tiles, backtracks, thrash, assists: rescueEpisodes,
      });
      const gates = hardGates(rescueUsed);
      const fx = flavorXp({ banked, bestSolveMoves, bestMovesBefore, underParSolve });
      endParEarned = Math.round(graded.parEarned);

      /* meta: the standing dare per board size, and how many pictures came back
       * across the player's whole history. The dare is a PER-BOARD number, so
       * it is the best single board of the class that goes up against it -
       * never the class's cumulative move count. */
      const bestKey = 'bestMoves' + n;
      try {
        const patch = { solves: solvesBefore + banked, lastSeed: seed, lastPlayedAt: Date.now() };
        if (bestSolveMoves > 0 && (!(bestMovesBefore > 0) || bestSolveMoves < bestMovesBefore)) {
          patch[bestKey] = bestSolveMoves;
        }
        ctx.store.mergeGameMeta(GAME_KEY, patch);
      } catch (e) { say('meta write failed (class unaffected): ' + ((e && e.message) || e)); }

      renderEnd();
      setPhase('ended');

      /* ZEN is a PASS, not a letter (DECISIONS #1): no gates, no S, and the
       * composite rides along only so the report card has something honest to
       * draw. TIMED carries the composite, the declared gate and the flavour. */
      const report = zen
        ? { zen: true, metrics: { composite: graded.composite }, flavorXp: fx }
        : { metrics: { composite: graded.composite }, hardGates: gates, flavorXp: fx };

      lastReport = Object.assign({}, report, {
        inputs: {
          tier, n, zen, seed, retake, reason, moves, bumps, baseline,
          banked, boardIndex, boardsDone: graded.boardsDone, expectedBoards,
          boardSolved: solved, boardMoves, par, parBanked, parEarned: graded.parEarned,
          bestSolveMoves, underParSolve,
          locked, tiles, backtracks, thrash, assists: rescueEpisodes, rescueUsed,
          manhattanStart: mhStart, manhattanNow: mhNow, elapsedMs, washes, subFlashes,
          jackpots, bestLockStreak, terms: graded.terms, tax: graded.tax,
        },
      });
      try { lastSnapshot = instance.snapshot(); } catch (e) { /* diagnostics only */ }
      say('class over (' + reason + '): ' + banked + ' banked of ' + expectedBoards + ' expected, '
        + 'live board ' + (solved ? 'WHOLE' : locked + '/' + tiles + ' home') + ', '
        + moves + ' moves (par earned ' + Math.round(graded.parEarned) + '), '
        + backtracks + ' backtracks / ' + thrash + ' panic'
        + (rescueUsed ? ', RESCUE' : '') + (zen ? ' -> zen pass' : ' -> composite ' + graded.composite.toFixed(3)));

      after(reduced ? PLAYTEST.END_HOLD_MS_REDUCED : PLAYTEST.END_HOLD_MS, () => {
        if (reported) return;
        reported = true;
        try { ctx.endClass(report); } catch (e) { say('endClass threw: ' + ((e && e.message) || e)); }
      });
    }

    function renderEnd() {
      if (!endEl) return;
      endEl.textContent = '';
      endEl.hidden = false;
      endEl.appendChild(el('h3', 'g-cp-end-title', zen
        ? t('cp_end_title_zen', CP_LEX.cp_end_title_zen)
        : t('cp_end_title', CP_LEX.cp_end_title)));
      const row = (cls, k, v) => {
        const r = el('div', 'g-cp-end-row' + (cls ? ' ' + cls : ''));
        r.appendChild(el('span', 'g-cp-end-k', k));
        r.appendChild(el('span', 'g-cp-end-v', v));
        endEl.appendChild(r);
        return r;
      };
      /* THE HEADLINE IS THE BANK. `cp_end_solved` used to be a Yes/No about
       * the one board; a class that deals boards until the bell has a COUNT
       * instead, so the row was re-worded rather than a new key minted (and
       * cp_end_yes / cp_end_no went with the question they answered). The
       * locked row underneath is the board that was still in progress. */
      row('g-cp-end-solved', t('cp_end_solved', CP_LEX.cp_end_solved), String(banked));
      row('', t('cp_end_locked', CP_LEX.cp_end_locked), locked + '/' + tileCount(n));
      row('', t('cp_end_moves', CP_LEX.cp_end_moves), String(moves));
      /* ...and the baseline it is measured against is the class's EARNED par
       * (every banked board's own, plus the live board's pro-rated), so the
       * two numbers still read as a pair. */
      row('', t('cp_end_par', CP_LEX.cp_end_par), String(Math.max(0, Math.round(endParEarned))));
      row('', t('cp_end_backtracks', CP_LEX.cp_end_backtracks), String(backtracks));
      row('', t('cp_end_thrash', CP_LEX.cp_end_thrash), String(thrash));
      if (rescueEpisodes > 0) row('g-cp-end-assist', t('cp_end_assists', CP_LEX.cp_end_assists), String(rescueEpisodes));
      row('', t('cp_end_time', CP_LEX.cp_end_time), mmss(secElapsed()));

      /* The standing dare is a PER-BOARD number: the best single board of this
       * class against the best single board of every class before it. */
      const bestNow = (bestSolveMoves > 0 && (!(bestMovesBefore > 0) || bestSolveMoves < bestMovesBefore))
        ? bestSolveMoves
        : bestMovesBefore;
      const dare = el('div', 'g-cp-end-dare');
      dare.appendChild(el('span', 'g-cp-end-k', t('cp_end_best', CP_LEX.cp_end_best)));
      dare.appendChild(el('span', 'g-cp-end-v', bestNow > 0 ? String(bestNow) : '--'));
      dare.appendChild(el('p', 'g-cp-end-line', bestMovesBefore > 0
        ? t('cp_end_best_line', CP_LEX.cp_end_best_line)
        : t('cp_end_best_first', CP_LEX.cp_end_best_first)));
      endEl.appendChild(dare);
      /* THE DEBRIEF (W2): .g-cp-end animates in as ONE card (g-cp-endin) and
       * its rows carry no stagger of their own, so the blip ladder collapses
       * to a single sheet cue rather than eight blips inside one frame. */
      tick('slide', 0.35);
    }

    /* ---- clock ----------------------------------------------------------- */
    function startClock() {
      lastTick = Date.now();
      clockId = every(250, () => {
        if (ended) return;
        const now = Date.now();
        const dt = now - lastTick;
        lastTick = now;
        elapsedMs += dt / Math.max(0.0001, timeScale);
        if (clockChip) clockChip.textContent = clockText();
        checkRescue();
        const stallMs = Math.max(0, elapsedMs - lastLockAtMs);
        deck('trickster', 'stalled', stallMs);
        /* HALF the rescue clock: nothing has come home for a while and the
         * room still has minutes in it. Edge-detected off `lastLockAtMs`
         * itself, so one stall gets one note and a lock or a deal re-arms it.
         * No new timer - this rides the clock tick the class already runs. */
        if (opened && !banking && !closing && stallMs >= PLAYTEST.RESCUE_MS / 2
          && emiStallNotedAt !== lastLockAtMs) {
          emiStallNotedAt = lastLockAtMs;
          note('cp.stallNoLock', {
            kind: 'ambient', n: Math.round(stallMs / 1000), left: tileCount(n) - locked,
          });
        }
        if (zen) return;                       // an untimed class has no bell
        const left = secLeft();
        if (!bellOn && left <= plan.bellWarnSec && elapsedMs < budgetMs) {
          bellOn = true;
          deck('casino', 'bell', true);
          msg('cp_bell_warn', CP_LEX.cp_bell_warn, 2200);
          /* W3 P0-3: the warn is the end bell struck softer, one vocabulary
           * across the school. `bellOn` above is the latch, so it lands once. */
          tick('bell', 0.3);
        }
        if (elapsedMs >= budgetMs) { stopClock(); run(bell); }
      });
    }
    function stopClock() { if (clockId) { clearTimer(clockId); clockId = 0; } }

    /* ==================================================================== *
     * THE PEEK (the shell's verb; this file only says what a reveal SHOWS)
     * ==================================================================== */
    function wirePeek() {
      if (!ctx.peek || typeof ctx.peek.setHandlers !== 'function') return;
      try {
        ctx.peek.setHandlers({
          onReveal: () => {
            if (!peekEl) return;
            peekEl.hidden = false;
            if (stage) stage.setAttribute('data-peek', '1');
          },
          onHide: () => {
            if (!peekEl) return;
            peekEl.hidden = true;
            if (stage) stage.removeAttribute('data-peek');
          },
          onFirstUse: () => {
            msg('peek_hint', 'Hold to peek. Using it caps this class at A.', 2400);
            /* Once ever per class. The shell owns the A-cap and the hint line
             * already said it - she is only here for the looking. */
            if (!emiPeekNoted) {
              emiPeekNoted = true;
              note('cp.peekFirstUse', {
                kind: 'tease', n: locked, left: tileCount(n) - locked, streak: banked,
              });
            }
          },
        });
      } catch (e) { say('peek handlers refused: ' + ((e && e.message) || e)); }
      try { if (peekBtn && typeof ctx.peek.attach === 'function') ctx.peek.attach(peekBtn); }
      catch (e) { /* noop */ }
      /* The manifest declares no keybinds, so this binds nothing today; it
       * costs nothing and it is the ONE line that would have to change if the
       * shell ever gives peek a verb slot of its own. */
      try { if (typeof ctx.peek.bindKeys === 'function') ctx.peek.bindKeys(ctx.keys, 'peek'); }
      catch (e) { /* noop */ }
    }

    /* ==================================================================== *
     * THE DRAWN CLASS-RULES SHEET (Deck VI, Law IV)
     * FOUR figures and ONE way out - the fourth is the bank rule, which is a
     * class rule and therefore belongs on the drawn sheet rather than in a
     * proctor line the player can miss. THE LAW, uniform across every open
     * class (owner ruling 2026-08-24): the sheet SHOWS the first time this
     * player meets this class at this grade tier and AUTO-SKIPS every later
     * class at that tier, whatever the setting says; the shell's "Skip class
     * tutorials" switch (ctx.hideTutorial) means "skip even the first
     * showing". It is also FREE OF THE CLOCK - openClass() (and startClock()
     * inside it) is on the far side of GO. CORE builds the structure and owns
     * the policy; style.js draws it.
     * ==================================================================== */
    function howtoSeenTiers() {
      try {
        const m = (ctx.store && typeof ctx.store.gameMeta === 'function') ? (ctx.store.gameMeta(GAME_KEY) || {}) : {};
        return Array.isArray(m.howtoTiers) ? m.howtoTiers.slice() : [];
      } catch (e) { return []; }
    }
    function howto(onDone) {
      const seen = howtoSeenTiers();
      /* AUTO-SKIP once this tier is on the record; hideTutorial skips the
       * first showing too. No meta = an empty list = the sheet shows. */
      if (ctx.hideTutorial === true || seen.indexOf(tier) >= 0) { onDone(); return; }
      if (!stage) { onDone(); return; }
      let done = false;
      howtoEl = el('div', 'g-cp-howto');
      howtoEl.appendChild(el('h2', 'g-cp-hw-title', t('cp_howto_title', CP_LEX.cp_howto_title)));
      const figRow = (figCls, caption) => {
        const r = el('div', 'g-cp-hw-row');
        const fig = el('span', 'g-cp-hw-fig ' + figCls);
        fig.setAttribute('aria-hidden', 'true');
        fig.style.pointerEvents = 'none';
        // the drawn parts style.js hangs its gradients and borders off
        fig.appendChild(el('i', 'g-cp-hw-a'));
        fig.appendChild(el('i', 'g-cp-hw-b'));
        fig.appendChild(el('i', 'g-cp-hw-c'));
        r.appendChild(fig);
        r.appendChild(el('p', 'g-cp-hw-cap', caption));
        howtoEl.appendChild(r);
      };
      figRow('g-cp-hw-slide', t('cp_howto_slide', CP_LEX.cp_howto_slide));
      figRow('g-cp-hw-lock', t('cp_howto_lock', CP_LEX.cp_howto_lock));
      figRow('g-cp-hw-wash', t('cp_howto_wash', CP_LEX.cp_howto_wash));
      /* THE BANK. In zen there is no bell to promise, but the re-deal is the
       * same, so the row is drawn either way - a zen player who solves and
       * sees a fresh scramble has been told it was coming. */
      figRow('g-cp-hw-bank', t('cp_howto_bank', CP_LEX.cp_howto_bank));
      const go = el('button', 'g-cp-hw-go', t('cp_howto_go', CP_LEX.cp_howto_go));
      go.setAttribute('type', 'button');
      try { go.type = 'button'; } catch (e) { /* noop */ }
      go.setAttribute('autofocus', '');
      go.addEventListener('click', () => {
        if (done || dead) return;
        done = true;
        /* THE PRESS THAT STARTS PLAY (W2). The sheet is one page and GO is its
         * only dismissal, so this is the school's start beat, not a page turn. */
        tick('lift', 0.5);
        try {
          const list = howtoSeenTiers();
          if (list.indexOf(tier) < 0) {
            list.push(tier);
            if (ctx.store && typeof ctx.store.mergeGameMeta === 'function') {
              ctx.store.mergeGameMeta(GAME_KEY, { howtoTiers: list });
            }
          }
        } catch (e) { /* best effort - the sheet just shows again next time */ }
        hideHowto();
        onDone();
      });
      howtoEl.appendChild(go);
      stage.appendChild(howtoEl);
      try { if (typeof go.focus === 'function') go.focus(); } catch (e) { /* noop */ }
    }
    function hideHowto() {
      if (!howtoEl) return;
      try { howtoEl.remove(); } catch (e) { /* noop */ }
      howtoEl = null;
    }

    /* ==================================================================== *
     * ASSETS - never block a draw. ONE subject, and never a <video>: N tile
     * viewports would mean N decoders, and 2+ playing videos lock the page to
     * 30Hz (the Lost & Found trap). A video-only pool simply means the board
     * wears its numbered faces, which is still a complete puzzle.
     * ==================================================================== */
    function pickSubject() {
      if (!pool || typeof pool.next !== 'function') return null;
      for (const kind of ['target', 'loop', 'loop', 'loop', 'still', 'still']) {
        let got = null;
        try { got = pool.next(kind); } catch (e) { got = null; }
        const url = got && got.url ? String(got.url) : '';
        if (url && !VIDEO_URL_RE.test(url)) return url;
      }
      say('asset pool served only video loops - numbered faces this class (N viewports would be N decoders)');
      return null;
    }
    function claimAssets() {
      Promise.resolve()
        .then(() => ctx.assets.claim({ loops: 4, targets: 1, stills: 2, canvasSafe: false }))
        .then((p) => {
          if (dead || !p || typeof p.next !== 'function') return;
          pool = p;
          subjectUrl = pickSubject();
          run(dressTiles);
        })
        .catch((e) => say('asset claim failed - numbered faces: ' + ((e && e.message) || e)));
    }

    /* ==================================================================== *
     * THE DECKS - written by a parallel agent, so every import is DYNAMIC and
     * every failure is survivable. The class opens whether they land or not.
     * ==================================================================== */
    function loadDecks() {
      const opt = (path) => import(path).then((m) => m, (e) => {
        say(path + ' not loaded (' + ((e && e.message) || e) + ')');
        return null;
      });
      return Promise.all([
        opt('./style.js'), opt('./casino.js'), opt('./trickster.js'), opt('./pressure.js'),
      ]).then(([styleMod, casinoMod, tricksterMod, pressureMod]) => {
        if (dead) return;
        try {
          if (styleMod && typeof styleMod.injectComposureStyle === 'function') {
            styleMod.injectComposureStyle();
            styleOk = true;
          }
        } catch (e) { say('style inject failed (class unaffected): ' + ((e && e.message) || e)); }
        if (!styleOk) injectFallbackStyle();

        const capsOk = capsArmed();
        try {
          if (casinoMod && typeof casinoMod.createCpCasino === 'function') {
            casino = casinoMod.createCpCasino({
              seed, tier, stage, frame, board: boardEl, hud, backdrop,
              timers: deckTimers, reduced, capsOk, t, engine: deckEngine, assets: deckAssets,
              mode: zen ? 'zen' : 'timed', log: say,
            }) || null;
          }
        } catch (e) { casino = null; say('casino refused: ' + ((e && e.message) || e)); }
        try {
          if (tricksterMod && typeof tricksterMod.createCpTrickster === 'function') {
            trickster = tricksterMod.createCpTrickster({
              seed, tier, timers: deckTimers, reduced, capsOk, cue: tick,
              mode: zen ? 'zen' : 'timed',
              budgetSec: zen ? 0 : Math.round(budgetMs / 1000),
              isHalted: () => dead || paused || ended || busy,
              t,
              stats: () => ({
                moves, locked, tiles: tileCount(n), n, lockedFrac: lockedFrac(),
                secLeft: secLeft(), budgetSec: zen ? 0 : Math.round(budgetMs / 1000),
                mode: zen ? 'zen' : 'timed', backtracks, thrash, washOn, solved,
              }),
              chipEl: (which) => (which === 'clock' ? clockChip : which === 'moves' ? movesChip : lockedChip),
              chipText,
              /* the ONE surface a lie may be drawn on; tile truth never moves */
              preview: previewEl,
              tiles: () => Array.from(tileEls.values()),
              engine: deckEngine,
              assets: deckAssets,
              announce: (text, ms) => {
                if (!msgEl || !text) return;
                msgEl.textContent = String(text);
                const mine = msgEl.textContent;
                deckTimers.after(Math.max(400, Number(ms) || 1600), () => {
                  if (msgEl && msgEl.textContent === mine) msgEl.textContent = '';
                });
              },
              log: say,
            }) || null;
          }
        } catch (e) { trickster = null; say('trickster refused: ' + ((e && e.message) || e)); }
        try {
          if (pressureMod && typeof pressureMod.createCpPressure === 'function') {
            pressure = pressureMod.createCpPressure({
              seed, gradeTier: tier, reduced, motionLevel: motionLevelOf(),
              stage, frame, board: boardEl,
              hud: { moves: movesChip, clock: clockChip, locked: lockedChip, calm: calmChip },
              engine: deckEngine, assets: deckAssets, timers: deckTimers, capsOk: capsArmed,
              mode: zen ? 'zen' : 'timed', log: say,
            }) || null;
          }
        } catch (e) { pressure = null; say('pressure refused: ' + ((e && e.message) || e)); }

        /* The decks may land after the board opened (two dynamic imports); if
         * they did, start them where they are. */
        if (opened && !ended) {
          deck('casino', 'start');
          deck('pressure', 'start');
          deck('trickster', 'start');
          heat();
        }
      });
    }

    /* ==================================================================== *
     * OPEN - the rules sheet is done, the board is the player's.
     * ==================================================================== */
    function openClass() {
      if (dead || ended || opened) return;
      setPhase('play');
      msg('cp_play_hint', CP_LEX.cp_play_hint, 3000);
      opened = true;
      busy = false;
      banking = false;
      lastLockAtMs = 0;
      elapsedMs = 0;
      deck('casino', 'start');
      deck('pressure', 'start');
      deck('trickster', 'start');
      openAmbience();
      startClock();
    }

    /* ==================================================================== *
     * THE MODULE INSTANCE
     * ==================================================================== */
    const instance = {
      start(classSpec) {
        spec = classSpec || { gradeTier: 1, seed: GAME_KEY + '|none', timeBudgetSec: 300 };
        tier = Math.max(1, Math.min(4, Math.round(Number(spec.gradeTier) || 1)));
        seed = String(spec.seed == null ? GAME_KEY : spec.seed);
        retake = !!spec.retake;
        reduced = probeReduced(ctx);
        /* The shell's budget is the truth; the module's own timeBudgetSec (and
         * registry.js's mirror of it) is the 300s parachute. Min-clamped only,
         * so a shell that hands us a longer class simply gets more boards. */
        budgetMs = Math.max(20000, (Number(spec.timeBudgetSec) || 300) * 1000);

        const mode = String((ctx.settings && ctx.settings.cp_mode) != null ? ctx.settings.cp_mode : 'timed')
          .trim().toLowerCase();
        zen = mode === 'zen';
        n = zen
          ? sizeFromSetting(ctx.settings && ctx.settings.cp_zen_grid, 3)
          : gridForTier(tier);

        plan = buildPlan({ seed, gradeTier: tier, n, zen, timeBudgetSec: budgetMs / 1000 });

        /* How many whole boards this year is expected to hold in this budget -
         * the number the PROGRESS term is divided by, and therefore the whole
         * S/A bar. 7 / 5 / 3 / 2 at 300s; grade.js's header carries the
         * arithmetic behind those four numbers. */
        expectedBoards = expectedBoardsFor(tier, budgetMs / 1000);

        /* BOARD 1. Seeded, and SOLVABLE by construction (board.js repairs a
         * bad parity); the deal, the parity belt-and-braces and the per-board
         * baseline all live in dealCurrentBoard() now, because every bank runs
         * them again. boardIndex 0 is the UN-SUFFIXED seed, so tonight's
         * opening board is exactly the one this class dealt before it grew. */
        boardIndex = 0;
        bestLocked = 0;
        dealCurrentBoard();

        /* Law V: even the FALLBACK reward roll (used only when the engine is
         * absent) runs off the class seed, in its own append-only namespace. */
        rollLocal = (() => {
          const roll = makeTaggedRoll(seed + '|cp-vr');
          return () => {
            const chance = Math.min(1, 0.28 + 0.3 * currentHeat + Math.min(8, lockStreak) * 0.03);
            const r = roll('fire');
            const fire = r < chance;
            return { fire, jackpot: fire && roll('jack') >= 0.85, nearMiss: !fire && r < chance + 0.08 };
          };
        })();

        try {
          const m = (ctx.store && typeof ctx.store.gameMeta === 'function') ? (ctx.store.gameMeta(GAME_KEY) || {}) : {};
          solvesBefore = Math.max(0, Number(m.solves) || 0);
          bestMovesBefore = Math.max(0, Number(m['bestMoves' + n]) || 0);
        } catch (e) { solvesBefore = 0; bestMovesBefore = 0; }

        buildDom();
        wirePeek();
        bindInput();
        claimAssets();
        applyLockClasses(homeMask(state));
        paintHud();

        /* The decks (and the look) land asynchronously; the class does not wait
         * on them. The rules sheet runs first either way. */
        try { loadDecks(); } catch (e) { injectFallbackStyle(); }

        msg(zen ? 'cp_brief_zen' : 'cp_brief', zen ? CP_LEX.cp_brief_zen : CP_LEX.cp_brief);
        howto(() => {
          after(reduced ? PLAYTEST.BRIEF_MS_REDUCED : PLAYTEST.BRIEF_MS, openClass);
        });

        liveClass = instance;
        lastReport = null;
        lastSnapshot = null;
        say('tier ' + tier + ' ' + n + 'x' + n + ' ' + (zen ? 'ZEN' : Math.round(budgetMs / 1000) + 's')
          + ', expect ' + expectedBoards + ' board(s), scramble mh ' + mhStart
          + ', baseline ' + baseline + ', par ' + par + ', ' + plan.washes.length + ' washes'
          + (reduced ? ', reduced' : '') + (retake ? ', RETAKE' : ''));
      },

      pause() {
        if (paused) return;
        paused = true;
        clearGrab();
        clearQueue();
        try { if (ctx.peek && typeof ctx.peek.forceHide === 'function') ctx.peek.forceHide(); } catch (e) { /* noop */ }
        deck('pressure', 'pause');
        deck('casino', 'pause');
        if (stage) stage.classList.add('suspended');
      },

      resume() {
        if (!paused) return;
        paused = false;
        if (stage) stage.classList.remove('suspended');
        deck('pressure', 'resume');
        deck('casino', 'resume');
        lastTick = Date.now();
        const q = deferred.splice(0);
        for (const fn of q) run(fn);
      },

      /** The shell owns the overlay and the engine's suspend; we just freeze. */
      suspend(on) { if (on) instance.pause(); else instance.resume(); },

      destroy() {
        dead = true;
        clearQueue();
        clearGrab();
        clearHint();
        opened = false;
        banking = false;
        closing = true;
        stopClock();
        clearTimers();
        stopAmbience();
        unbindInput();
        hideHowto();
        try { if (ctx.peek && typeof ctx.peek.forceHide === 'function') ctx.peek.forceHide(); } catch (e) { /* noop */ }
        try { if (finishBtn) finishBtn.removeEventListener('click', onFinishPressed); } catch (e) { /* noop */ }
        finishBtn = null;
        peekBtn = null;
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
      /** Press as the player would: ('dir','left') or ('pos', 7). */
      press(kind, v) { return press(kind, v); },
      /** Click a tile by its id, as the pointer would. */
      tap(id) { onTileClick(id); },
      /** The live puzzle model (the harness stages boards on it). */
      state() { return state; },
      /** The TRUE chip text - what the trickster restores after a lie. */
      chipText(which) { return chipText(which); },
      /** Re-sync every tile element to the model after the harness staged a board. */
      resync() {
        if (!state) return;
        paintTiles(true);
        locked = lockedCount(state);
        applyLockClasses(homeMask(state));
        paintHud();
        heat();
      },
      /** Open the board without waiting on the rules sheet (harness only). */
      open() { hideHowto(); openClass(); },
      /** Arm the rescue as the stall timer would (harness only). */
      forceRescue() { lastLockAtMs = elapsedMs - PLAYTEST.RESCUE_MS - 1; checkRescue(); },
      /** Run a wash window now (harness only). */
      forceWash(ms) { runWash({ atMs: 0, ms: Math.max(200, Number(ms) || 1200), alpha: 0.3, variant: 'pink' }); },
      /** Bank the live board and deal the next one NOW, skipping the beat
       *  (harness only - the real path is a solve). */
      forceDeal() { dealNextBoard(); },
      /** End as the bell would (harness only). */
      forceBell() { stopClock(); bell(); },

      snapshot() {
        return {
          tier, n, zen, seed, retake, reduced, styleOk,
          plan: plan ? {
            washes: plan.washes.length, subFlashMs: plan.subFlashMs, heatCap: plan.heatCap,
            audioCeil: plan.audioCeil, bubbles: plan.bubbles, rowDrift: plan.rowDrift,
            budgetMs: plan.budgetMs,
          } : null,
          boardIndex, banked, banking, closing, boardMoves, parBanked,
          bestSolveMoves, underParSolve, expectedBoards,
          board: state ? serialize(state) : '',
          cells: state ? state.cells.slice() : [],
          blank: state ? state.blank : -1,
          solvable: state ? isSolvable(state.cells, n) : false,
          manhattan: state ? manhattan(state) : 0,
          mhStart, baseline, par,
          moves, bumps, backtracks, thrash, locked, bestLocked, tiles: tileCount(n),
          lockStreak, bestLockStreak, solved, washOn, washes, subFlashes, jackpots,
          rescueUsed, rescueActive, rescueEpisodes, hintId,
          currentHeat, bellOn, finished, elapsedMs, budgetMs,
          ended, reported, busy, paused, dead, opened, queued,
          phase: stage ? stage.getAttribute('data-phase') : null,
          peekOpen: !!(peekEl && peekEl.hidden === false),
          howtoUp: !!howtoEl,
          liveTileEls: tileEls.size,
          subjectUrl,
          stage, frame, boardEl, previewEl, peekEl, wellEl, msgEl, endEl, howtoEl,
          movesChip, clockChip, lockedChip, calmChip, bankedChip, peekBtn, finishBtn,
          casino: casino && typeof casino.diagnostics === 'function' ? (() => { try { return casino.diagnostics(); } catch (e) { return null; } })() : null,
          trickster: trickster && typeof trickster.diagnostics === 'function' ? (() => { try { return trickster.diagnostics(); } catch (e) { return null; } })() : null,
          pressure: pressure && typeof pressure.diagnostics === 'function' ? (() => { try { return pressure.diagnostics(); } catch (e) { return null; } })() : null,
        };
      },
    };
    return instance;
  },

  /** The live class's state, or null. Never read by the shell. */
  diagnostics() { return liveClass ? liveClass.snapshot() : null; },

  /** The last report handed to endClass (survives teardown). Diagnostics only. */
  get lastReport() { return lastReport; },

  /** The final snapshot of the last class (survives teardown). Diagnostics only. */
  get lastSnapshot() { return lastSnapshot; },

  setTimeScale,
};
